
using System.Diagnostics;

using Microsoft.Win32.SafeHandles;

using Windows.Win32;
using Windows.Win32.Foundation;

namespace MarvinsAIRARefactored.Components;

public class PlayoutTimer
{
	// the playout tick rate - 360 Hz, six ticks per 60 Hz telemetry frame, so the playout consumes the processed
	// 360 Hz FFB samples 1:1 (the old ~500 Hz rate also pushed USB updates to the wheel right at the practical
	// limit of many wheel bases; 360 Hz eases that pressure)
	public const float TickRateHz = 360f;

	private const float TimerPeriodMilliseconds = 1000f / TickRateHz;

	// the timer period in raw stopwatch (QPC) ticks - the worker re-arms the one-shot timer against an absolute
	// deadline ladder in these units (SetWaitableTimer's periodic mode only supports whole milliseconds, which
	// cannot express 360 Hz, so periodic mode is not used)
	private static readonly long TimerPeriodStopwatchTicks = (long) ( Stopwatch.Frequency / (double) TickRateHz );

	// TIMER_ALL_ACCESS - CsWin32 doesn't generate this constant
	private const uint TimerAllAccess = 0x1F0003;

	// suspend/resume state is touched from several threads (the Simulator's connect/disconnect events, the
	// game bridge activation task, the main tick loop, and shutdown), so all timer start/stop transitions
	// are serialized by this lock - the worker thread itself never takes it, keeping the hot path lock-free
	private readonly object _timerLock = new();

	private bool _suspend = true;
	private int _suspendCounter = 0;

	public bool Suspend
	{
		get => _suspend;

		set
		{
			lock ( _timerLock )
			{
				if ( value != _suspend )
				{
					var app = App.Instance!;

					if ( value )
					{
						app.Logger.WriteLine( "[PlayoutTimer] Requesting suspend of timer" );

						_suspendCounter = App.TimerTicksPerSecond * 4;
					}
					else
					{
						app.Logger.WriteLine( "[PlayoutTimer] Requesting resumption of timer" );

						ResumeTimerNow();
					}

					_suspend = value;
				}
			}
		}
	}

	private readonly Stopwatch _stopwatch = new();

	private double _lastTotalMilliseconds = 0f;

	// the high-resolution waitable timer handle (null = the timer could not be created, so no force feedback
	// processing) and whether it is currently armed and self-re-arming - the worker re-arms only while
	// _timerActive is true, so at most one stray tick can slip out after a suspend cancels the timer
	private SafeFileHandle? _timerHandle = null;
	private volatile bool _timerActive = false;

	// the absolute deadline ladder in raw stopwatch ticks - advanced by exactly one period per tick so the
	// average rate is exactly 360 Hz with no drift, and snapped forward when the worker falls a whole period
	// behind so a stall doesn't burst catch up
	private long _nextDeadlineStopwatchTicks = 0;

	private readonly Thread _workerThread = new( WorkerThread ) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "MAIRA Playout Timer Worker Thread" };

	private volatile bool _running = true;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[PlayoutTimer] Initialize >>>" );

		app.Graph.SetLayerColors( Graph.LayerIndex.TimerJitter, 0f, 1f, 0f );

		app.Logger.WriteLine( "[PlayoutTimer] Calling PInvoke.CreateWaitableTimerEx (high resolution)" );

		var timerHandle = PInvoke.CreateWaitableTimerEx( null, null, PInvoke.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TimerAllAccess );

		if ( ( timerHandle == null ) || timerHandle.IsInvalid )
		{
			// the high-resolution flag needs Windows 10 1803+ and we require 2004+, so this should never happen -
			// but fall back to a standard waitable timer rather than losing force feedback entirely
			app.Logger.WriteLine( "[PlayoutTimer] High-resolution timer not available - falling back to a standard waitable timer" );

			timerHandle = PInvoke.CreateWaitableTimerEx( null, null, 0, TimerAllAccess );
		}

		if ( ( timerHandle == null ) || timerHandle.IsInvalid )
		{
			// the timer could not be created - make some noise, because without this timer there is no force
			// feedback processing at all
			app.Logger.WriteLine( "[PlayoutTimer] CreateWaitableTimerEx failed - the playout timer could not be started!" );

			timerHandle?.Dispose();
		}
		else
		{
			_timerHandle = timerHandle;

			_stopwatch.Start();

			_workerThread.Start();
		}

		app.Logger.WriteLine( "[PlayoutTimer] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[PlayoutTimer] Shutdown >>>" );

		_running = false;

		if ( _workerThread.IsAlive )
		{
			// stop the self-re-arming and fire the timer immediately so the worker wakes up and sees _running
			lock ( _timerLock )
			{
				_timerActive = false;

				ArmTimer( 0 );
			}

			_workerThread.Join( 5000 );
		}

		_timerHandle?.Dispose();

		_timerHandle = null;

		app.Logger.WriteLine( "[PlayoutTimer] <<< Shutdown" );
	}

	// must be called while holding _timerLock
	private void ResumeTimerNow()
	{
		if ( !_timerActive && ( _timerHandle != null ) )
		{
			var app = App.Instance!;

			app.Logger.WriteLine( "[PlayoutTimer] ResumeTimerNow >>>" );

			// the stopwatch keeps running while the timer is suspended, so the baseline is cleared here to
			// make the worker re-baseline on its first tick - otherwise that tick would compute a delta
			// spanning the entire suspension and feed it to the racing wheel update as one giant step
			_lastTotalMilliseconds = 0;

			_nextDeadlineStopwatchTicks = _stopwatch.ElapsedTicks + TimerPeriodStopwatchTicks;

			_timerActive = true;

			ArmTimer( TimerPeriodStopwatchTicks );

			app.Logger.WriteLine( "[PlayoutTimer] <<< ResumeTimerNow" );
		}
	}

	// must be called while holding _timerLock
	private void SuspendTimerNow()
	{
		if ( _timerActive && ( _timerHandle != null ) )
		{
			var app = App.Instance!;

			app.Logger.WriteLine( "[PlayoutTimer] SuspendTimerNow >>>" );

			_timerActive = false;

			_ = PInvoke.CancelWaitableTimer( _timerHandle );

			app.Logger.WriteLine( "[PlayoutTimer] <<< SuspendTimerNow" );
		}
	}

	// arm the one-shot timer to fire dueStopwatchTicks from now (relative due time in 100 ns units, negative =
	// relative; clamped so an overdue deadline still fires as soon as possible)
	private void ArmTimer( long dueStopwatchTicks )
	{
		var dueTime = -Math.Max( 1L, dueStopwatchTicks * 10_000_000L / Stopwatch.Frequency );

		unsafe
		{
			if ( !PInvoke.SetWaitableTimer( _timerHandle!, dueTime, 0, null, null, false ) )
			{
				App.Instance!.Logger.WriteLine( "[PlayoutTimer] SetWaitableTimer failed!" );
			}
		}
	}

	private static void WorkerThread()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[PlayoutTimer] Worker thread started" );

		try
		{
			var playoutTimer = app.PlayoutTimer;

			var timerHandle = playoutTimer._timerHandle!;

			while ( playoutTimer._running )
			{
				if ( PInvoke.WaitForSingleObject( timerHandle, PInvoke.INFINITE ) != WAIT_EVENT.WAIT_OBJECT_0 )
				{
					app.Logger.WriteLine( "[PlayoutTimer] WaitForSingleObject failed!" );

					break;
				}

				if ( !playoutTimer._running )
				{
					break;
				}

				// re-arm for the next deadline before running the consumers, so time spent in them doesn't
				// push the ladder
				if ( playoutTimer._timerActive )
				{
					var nowStopwatchTicks = playoutTimer._stopwatch.ElapsedTicks;

					playoutTimer._nextDeadlineStopwatchTicks += TimerPeriodStopwatchTicks;

					if ( playoutTimer._nextDeadlineStopwatchTicks <= nowStopwatchTicks )
					{
						playoutTimer._nextDeadlineStopwatchTicks = nowStopwatchTicks + TimerPeriodStopwatchTicks;
					}

					playoutTimer.ArmTimer( playoutTimer._nextDeadlineStopwatchTicks - nowStopwatchTicks );
				}

				var totalMilliseconds = playoutTimer._stopwatch.Elapsed.TotalMilliseconds;

				if ( playoutTimer._lastTotalMilliseconds == 0 )
				{
					playoutTimer._lastTotalMilliseconds = totalMilliseconds;
				}
				else
				{
					var deltaMilliseconds = (float) ( totalMilliseconds - playoutTimer._lastTotalMilliseconds );

					if ( deltaMilliseconds > TimerPeriodMilliseconds * 0.5f )
					{
						playoutTimer._lastTotalMilliseconds = totalMilliseconds;

						// pump the active game bridge (if any) right before the racing wheel update, so bridge
						// sub-samples are taken on this precise kernel timer and the freshest torque reading
						// is in place with near zero added latency

						app.GameBridge.Pump( totalMilliseconds / 1000.0 );

						// update racing wheel force feedback

						app.RacingWheel.UpdatePlayout( deltaMilliseconds );

						// update pedals graph

						app.Pedals.UpdateGraph();

						// update jitter statistics and graph

						var jitterMilliseconds = deltaMilliseconds - TimerPeriodMilliseconds;

						var y = Math.Clamp( jitterMilliseconds / TimerPeriodMilliseconds, -1f, 1f );

						app.Graph.UpdateLayer( Graph.LayerIndex.TimerJitter, y );

						// update the graph

						app.Graph.Update();
					}
				}
			}
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[PlayoutTimer] Exception caught: {exception.Message}" );

			app.ShowFatalError( "An exception was thrown inside the playout timer worker thread.", exception );
		}

		app.Logger.WriteLine( "[PlayoutTimer] Worker thread stopped" );
	}

	public void Tick( App app )
	{
		lock ( _timerLock )
		{
			if ( _suspend && _timerActive )
			{
				_suspendCounter--;

				if ( _suspendCounter == 0 )
				{
					SuspendTimerNow();
				}
			}
		}
	}
}
