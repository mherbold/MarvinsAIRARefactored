
using System.Diagnostics;
using System.Runtime.InteropServices;

using Windows.Win32;

namespace MarvinsAIRARefactored.Components;

public partial class MultimediaTimer
{
	private bool _suspend = true;
	private int _suspendCounter = 0;

	public bool Suspend
	{
		get => _suspend;

		set
		{
			if ( value != _suspend )
			{
				var app = App.Instance!;

				if ( value )
				{
					app.Logger.WriteLine( "[MultimediaTimer] Requesting suspend of timer" );

					_suspendCounter = App.TimerTicksPerSecond * 4;
				}
				else
				{
					app.Logger.WriteLine( "[MultimediaTimer] Requesting resumption of timer" );

					ResumeTimerNow();
				}

				_suspend = value;
			}
		}
	}

	private readonly Stopwatch _stopwatch = new();

	private double _lastTotalMilliseconds = 0f;

	private uint _multimediaTimerId = 0;

	private readonly AutoResetEvent _autoResetEvent = new( false );

	private readonly Thread _workerThread = new( WorkerThread ) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "MAIRA Multimedia Timer Worker Thread" };

	private bool _running = true;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[MultimediaTimer] Initialize >>>" );

		app.Graph.SetLayerColors( Graph.LayerIndex.TimerJitter, 0f, 1f, 0f );

		_stopwatch.Start();

		_workerThread.Start();

		app.Logger.WriteLine( "[MultimediaTimer] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[MultimediaTimer] Shutdown >>>" );

		_running = false;

		if ( _workerThread.IsAlive )
		{
			_autoResetEvent.Set();

			_workerThread.Join( 5000 );
		}

		app.Logger.WriteLine( "[MultimediaTimer] <<< Shutdown" );
	}

	private void ResumeTimerNow()
	{
		if ( _multimediaTimerId == 0 )
		{
			var app = App.Instance!;

			app.Logger.WriteLine( "[MultimediaTimer] ResumeTimerNow >>>" );

			app.Logger.WriteLine( "[MultimediaTimer] Calling PInvoke.timeBeginPeriod" );

			_ = PInvoke.timeBeginPeriod( 1 );

			app.Logger.WriteLine( "[MultimediaTimer] Calling TimeSetEvent" );

			var eventHandle = _autoResetEvent.SafeWaitHandle.DangerousGetHandle();

			const uint timePeriodic = 0x0001;
			const uint timeCallbackEventSet = 0x0010;

			_multimediaTimerId = TimeSetEvent( 2, 0, eventHandle, UIntPtr.Zero, timePeriodic | timeCallbackEventSet );

			app.Logger.WriteLine( "[MultimediaTimer] <<< ResumeTimerNow" );
		}
	}

	private void SuspendTimerNow()
	{
		if ( _multimediaTimerId != 0 )
		{
			var app = App.Instance!;

			app.Logger.WriteLine( "[MultimediaTimer] SuspendTimerNow >>>" );

			app.Logger.WriteLine( "[MultimediaTimer] Calling PInvoke.timeEndPeriod" );

			_ = PInvoke.timeEndPeriod( 1 );

			app.Logger.WriteLine( "[MultimediaTimer] Calling PInvoke.timeKillEvent" );

			_ = PInvoke.timeKillEvent( _multimediaTimerId );

			_multimediaTimerId = 0;

			app.Logger.WriteLine( "[MultimediaTimer] <<< SuspendTimerNow" );
		}
	}

	private static void WorkerThread()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[MultimediaTimer] Worker thread started" );

		try
		{
			while ( app.MultimediaTimer._running )
			{
				app.MultimediaTimer._autoResetEvent.WaitOne();

				var multimediaTimer = app.MultimediaTimer;

				var totalMilliseconds = multimediaTimer._stopwatch.Elapsed.TotalMilliseconds;

				if ( multimediaTimer._lastTotalMilliseconds == 0 )
				{
					multimediaTimer._lastTotalMilliseconds = totalMilliseconds;
				}
				else
				{
					var deltaMilliseconds = (float) ( totalMilliseconds - multimediaTimer._lastTotalMilliseconds );

					if ( deltaMilliseconds > 1f )
					{
						multimediaTimer._lastTotalMilliseconds = totalMilliseconds;

						// update racing wheel force feedback

						app.RacingWheel.Update( deltaMilliseconds );

						// update pedals graph

						app.Pedals.UpdateGraph();

						// update jitter statistics and graph

						var jitterMilliseconds = deltaMilliseconds - 2f;

						var y = Math.Clamp( jitterMilliseconds / 2f, -1f, 1f );

						app.Graph.UpdateLayer( Graph.LayerIndex.TimerJitter, y );

						// update the graph

						app.Graph.Update();
					}
				}
			}
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[MultimediaTimer] Exception caught: {exception.Message}" );

			app.ShowFatalError( "An exception was thrown inside the multimedia timer worker thread.", exception );
		}

		app.Logger.WriteLine( "[MultimediaTimer] Worker thread stopped" );
	}

	public void Tick( App app )
	{
		if ( Suspend )
		{
			if ( _multimediaTimerId != 0 )
			{
				_suspendCounter--;

				if ( _suspendCounter == 0 )
				{
					SuspendTimerNow();
				}
			}
		}
	}

	[LibraryImport( "winmm.dll", EntryPoint = "timeSetEvent" )]
	internal static partial uint TimeSetEvent( uint delayMilliseconds, uint resolutionMilliseconds, IntPtr eventHandle, UIntPtr user, uint eventFlags );
}
