
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

using MarvinsAIRARefactored.Components;

using Application = System.Windows.Application;
using Timer = System.Timers.Timer;

namespace MarvinsAIRARefactored;

public partial class App : Application
{
	public const string APP_FOLDER_NAME = "MarvinsAIRA Refactored";

	public static string DocumentsFolder { get; } = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ), APP_FOLDER_NAME );

	public static App? Instance { get; private set; }

	public Logger Logger { get; private set; }
	public RacingWheel RacingWheel { get; private set; }
	public SettingsFile SettingsFile { get; private set; }
	public new MainWindow MainWindow { get; private set; }
	public DirectInput DirectInput { get; private set; }
	public MultimediaTimer MultimediaTimer { get; private set; }
	public Simulator Simulator { get; private set; }
	public Debug Debug { get; private set; }

	public const int TimerPeriodInMilliseconds = 17;
	public const int TimerTicksPerSecond = 1000 / TimerPeriodInMilliseconds;

	private readonly AutoResetEvent _autoResetEvent = new( false );

	private readonly Thread _workerThread = new( WorkerThread ) { IsBackground = true, Priority = ThreadPriority.Normal };

	private bool _running = true;

	private readonly Timer _timer = new( TimerPeriodInMilliseconds );

	App()
	{
		Instance = this;

		Logger = new();
		RacingWheel = new();
		SettingsFile = new();
		MainWindow = new();
		DirectInput = new( MainWindow.OutputTorque_Image );
		MultimediaTimer = new( MainWindow.MultimediaTimerJitter_Image );
		Simulator = new( MainWindow.Native60HzTorque_Image, MainWindow.Native360HzTorque_Image );
		Debug = new( MainWindow.DebugGraph_Image );

		_timer.Elapsed += OnTimer;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void TriggerWorkerThread()
	{
		_autoResetEvent.Set();
	}

	private void App_Startup( object sender, StartupEventArgs e )
	{
		Logger.WriteLine( "[App] App_Startup >>>" );

		Misc.DisableThrottling();

		if ( !Directory.Exists( App.DocumentsFolder ) )
		{
			Directory.CreateDirectory( App.DocumentsFolder );
		}

		Logger.Initialize();
		SettingsFile.Initialize();
		DirectInput.Initialize();
		MultimediaTimer.Initialize();
		Simulator.Initialize();

		GC.Collect();

		MainWindow.Resources = App.Current.Resources;

		MainWindow.Show();
		MainWindow.Initialize();

		_workerThread.Start();

		_timer.Start();

		GC.Collect();

		Logger.WriteLine( "[App] <<< App_Startup" );
	}

	private void App_Exit( object sender, EventArgs e )
	{
		Logger.WriteLine( "[App] App_Exit >>>" );

		_timer.Stop();

		_running = false;

		_autoResetEvent.Set();

		Simulator.Shutdown();
		MultimediaTimer.Shutdown();
		Logger.Shutdown();

		Logger.WriteLine( "[App] <<< App_Exit" );
	}

	private void OnTimer( object? sender, EventArgs e )
	{
		var app = Instance;

		if ( app != null )
		{
			if ( !app.Simulator.IsConnected )
			{
				TriggerWorkerThread();
			}
		}
	}

	private static void WorkerThread()
	{
		var app = Instance;

		if ( app != null )
		{
			while ( app._running )
			{
				app._autoResetEvent.WaitOne();

				app.Dispatcher.BeginInvoke( () =>
				{
					app.SettingsFile.Tick( app );
					app.MainWindow.Tick( app );
					app.DirectInput.Tick( app );
					app.MultimediaTimer.Tick( app );
					app.Simulator.Tick( app );
					app.Debug.Tick( app );
				} );
			}
		}
	}
}
