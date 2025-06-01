
using System.Diagnostics;

using Image = System.Windows.Controls.Image;

using MarvinsAIRARefactored.WinApi;

namespace MarvinsAIRARefactored.Components;

public class MultimediaTimer
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

				var app = App.Instance;

				if ( value )
				{
					app?.Logger.WriteLine( "[MultimediaTimer] Requesting suspend of timer" );

					_suspendCounter = App.TimerTicksPerSecond * 4;
				}
				else
				{
					app?.Logger.WriteLine( "[MultimediaTimer] Requesting resumption of timer" );

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

	private readonly Thread _workerThread = new( WorkerThread ) { IsBackground = true, Priority = ThreadPriority.Highest };

	private bool _running = true;

	private readonly Graph _jitterGraph;
	private readonly Statistics _jitterStatistics = new( 500 );

	public MultimediaTimer( Image jitterGraphImage )
	{
		var app = App.Instance;

		app?.Logger.WriteLine( "[MultimediaTimer] Constructor >>>" );

		_jitterGraph = new Graph( jitterGraphImage );

		app?.Logger.WriteLine( "[MultimediaTimer] <<< Constructor" );
	}

	public void Initialize()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[MultimediaTimer] Initialize >>>" );

			_stopwatch.Start();

			_workerThread.Start();

			app.Logger.WriteLine( "[MultimediaTimer] <<< Initialize" );
		}
	}

	public void Shutdown()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[MultimediaTimer] Shutdown >>>" );

			_running = false;

			_autoResetEvent.Set();

			app.Logger.WriteLine( "[MultimediaTimer] <<< Shutdown" );
		}
	}

	private void ResumeTimerNow()
	{
		if ( _multimediaTimerId == 0 )
		{
			var app = App.Instance;

			if ( app != null )
			{
				app.Logger.WriteLine( "[MultimediaTimer] ResumeTimerNow >>>" );

				app.Logger.WriteLine( "[MultimediaTimer] Calling WinMM.TimeBeginPeriod" );

				_ = WinMM.TimeBeginPeriod( 1 );

				app.Logger.WriteLine( "[MultimediaTimer] Calling WinMM.TimeSetEvent" );

				_multimediaTimerId = WinMM.TimeSetEvent( 2, 0, _autoResetEvent.SafeWaitHandle.DangerousGetHandle(), ref _multimediaTimerId, (uint) ( WinMM.fuEvent.TIME_PERIODIC | WinMM.fuEvent.TIME_CALLBACK_EVENT_SET ) );

				app.Logger.WriteLine( "[MultimediaTimer] <<< ResumeTimerNow" );
			}
		}
	}

	private void SuspendTimerNow()
	{
		if ( _multimediaTimerId != 0 )
		{
			var app = App.Instance;

			if ( app != null )
			{
				app.Logger.WriteLine( "[MultimediaTimer] SuspendTimerNow >>>" );

				app.Logger.WriteLine( "[MultimediaTimer] Calling WinMM.TimeEndPeriod" );

				_ = WinMM.TimeEndPeriod( 1 );

				app.Logger.WriteLine( "[MultimediaTimer] Calling WinMM.TimeKillEvent" );

				_ = WinMM.TimeKillEvent( _multimediaTimerId );

				_multimediaTimerId = 0;

				app.Logger.WriteLine( "[MultimediaTimer] <<< SuspendTimerNow" );
			}
		}
	}

	private static void WorkerThread()
	{
		var app = App.Instance;

		if ( app != null )
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

						// update jitter statistics and graph

						if ( app.MainWindow.GraphsTabItemIsVisible )
						{
							var jitterMilliseconds = deltaMilliseconds - 2f;

							multimediaTimer._jitterStatistics.Update( jitterMilliseconds );

							var y = Math.Clamp( jitterMilliseconds / 2f, -1f, 1f );

							var absY = MathF.Abs( y );

							var colorRed = (uint) ( absY * 255 );
							var colorGreen = (uint) ( ( 1f - absY ) * 255 );

							multimediaTimer._jitterGraph.DrawGradientLine( y, colorRed, colorGreen, 0 );
							multimediaTimer._jitterGraph.Advance();
						}
					}
				}
			}
		}
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

		if ( app.MainWindow.GraphsTabItemIsVisible )
		{
			_jitterGraph.UpdateImage();

			app.MainWindow.Graphs_MultimediaTimerJitter_MinMaxAvg.Content = $"{_jitterStatistics.MinimumValue,5:F2} {_jitterStatistics.MaximumValue,5:F2} {_jitterStatistics.AverageValue,5:F2}";
			app.MainWindow.Graphs_MultimediaTimerJitter_VarStdDev.Content = $"{_jitterStatistics.Variance,5:F2} {_jitterStatistics.StandardDeviation,5:F2}";
		}
	}
}
