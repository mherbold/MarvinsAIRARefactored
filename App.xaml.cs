
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using Application = System.Windows.Application;
using ComboBox = System.Windows.Controls.ComboBox;
using Timer = System.Timers.Timer;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored;

public partial class App : Application
{
#if !ADMINBOXX

	public const string AppName = "MarvinsAIRA Refactored";

#else

	public const string AppName = "AdminBoxx";

#endif

	public static string DocumentsFolder { get; } = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ), AppName );

	public static readonly string DevRootPath = GetDevRootPath();

	private static string GetDevRootPath( [CallerFilePath] string callerFile = "" )
	{
		return Path.GetDirectoryName( callerFile ) ?? string.Empty;
	}

	private static bool IsInDesignMode => System.ComponentModel.DesignerProperties.GetIsInDesignMode( new DependencyObject() );

	public static App? Instance { get; private set; }
	public bool Ready { get; private set; } = false;

	public Logger Logger { get; private set; } = null!;
	public TopLevelWindow TopLevelWindow { get; private set; } = null!;
	public CloudService CloudService { get; private set; } = null!;
	public SettingsFile SettingsFile { get; private set; } = null!;
	public Graph Graph { get; private set; } = null!;
	public Pedals Pedals { get; private set; } = null!;
	public AdminBoxx AdminBoxx { get; private set; } = null!;
	public Debug Debug { get; private set; } = null!;
	public new MainWindow MainWindow { get; private set; } = null!;
	public RacingWheel RacingWheel { get; private set; } = null!;
	public ChatQueue ChatQueue { get; private set; } = null!;
	public AudioManager AudioManager { get; private set; } = null!;
	public Sounds Sounds { get; private set; } = null!;
	public DirectInput DirectInput { get; private set; } = null!;
	public StreamDeck StreamDeck { get; private set; } = null!;
	public LFE LFE { get; private set; } = null!;
	public MultimediaTimer MultimediaTimer { get; private set; } = null!;
	public Simulator Simulator { get; private set; } = null!;
	public RecordingManager RecordingManager { get; private set; } = null!;
	public SteeringEffects SteeringEffects { get; private set; } = null!;
	public VirtualJoystick VirtualJoystick { get; private set; } = null!;
	public Drivers Drivers { get; private set; } = null!;
	public TimingMarkers TimingMarkers { get; private set; } = null!;
	public Telemetry Telemetry { get; private set; } = null!;
	public SpeechToText SpeechToText { get; private set; } = null!;
	public Wind Wind { get; private set; } = null!;
	public SeatBeltTensioner SeatBeltTensioner { get; private set; } = null!;
	public HidHotplugMonitor HidHotplugMonitor { get; private set; } = null!;
	public TradingPaints TradingPaints { get; private set; } = null!;

	public GripOMeterWindow? GripOMeterWindow { get; set; }
	public GapMonitorWindow? GapMonitorWindow { get; set; }
	public SpeechToTextWindow? SpeechToTextWindow { get; set; }

	public const int TimerPeriodInMilliseconds = 17;
	public const int TimerTicksPerSecond = 1000 / TimerPeriodInMilliseconds;

	private const string RefactoredMutexName = "MarvinsAIRARefactoredMutex";
	private const string ClassicMutexName = "MarvinsAIRA Mutex";

	private static Mutex? _refactoredMutex = null;
	private static Mutex? _classicMutex = null;

	private readonly AutoResetEvent _autoResetEvent = new( false );

	private readonly Thread _workerThread = new( WorkerThread ) { IsBackground = true, Priority = ThreadPriority.Normal, Name = "MAIRA App Worker Thread" };

	private bool _running = true;

	private readonly Timer _timer = new( TimerPeriodInMilliseconds );

	private int _tickMutex = 0;

	App()
	{
		Instance = this;

		InitializeComponent();
	}

	private void InitializeRuntimeComponents()
	{
		Logger = new();
		TopLevelWindow = new();
		CloudService = new();
		SettingsFile = new();
		Graph = new();
		Pedals = new();
		AdminBoxx = new();
		Debug = new();
		MainWindow = new();
		RacingWheel = new();
		ChatQueue = new();
		AudioManager = new();
		Sounds = new();
		DirectInput = new();
		StreamDeck = new();
		LFE = new();
		MultimediaTimer = new();
		Simulator = new();
		RecordingManager = new();
		SteeringEffects = new();
		VirtualJoystick = new();
		Drivers = new();
		TimingMarkers = new();
		Telemetry = new();
		SpeechToText = new();
		Wind = new();
		SeatBeltTensioner = new();
		HidHotplugMonitor = new();
		TradingPaints = new();

		_timer.Elapsed += OnTimer;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void TriggerWorkerThread()
	{
		_autoResetEvent.Set();
	}

	public void ShowFatalError( string? message = null, Exception? exception = null )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[App] ShowFatalError >>>" );
		app.Logger.WriteLine( $"\r\n\r\n{exception?.ToString() ?? string.Empty}\r\n" );

		var uiDispatcher = app.Dispatcher;

		message ??= DataContext.DataContext.Instance.Localization[ "ExceptionThrown" ];

		void ShowAndExit()
		{
			try
			{
				ErrorWindow.ShowModal( message, exception );
			}
			catch
			{
				// last-ditch fallback if dialog fails
			}
			finally
			{
				app.Shutdown( -1 );
			}
		}

		if ( uiDispatcher.CheckAccess() )
		{
			ShowAndExit(); // already on UI thread
		}
		else
		{
			uiDispatcher.Invoke( ShowAndExit, DispatcherPriority.Send );
		}

		app.Logger.WriteLine( "[App] <<< ShowFatalError" );
	}

#if !ADMINBOXX
	private async void App_Startup( object sender, StartupEventArgs e )
#else
	private void App_Startup( object sender, StartupEventArgs e )
#endif
	{
		if ( IsInDesignMode )
		{
			return;
		}

		InitializeRuntimeComponents();

		DispatcherUnhandledException += ( sender, args ) =>
			{
				args.Handled = true;

				ShowFatalError( null, args.Exception );
			};

		AppDomain.CurrentDomain.UnhandledException += ( sender, args ) =>
		{
			var exception = args.ExceptionObject as Exception ?? new Exception( "Unknown fatal error." );

			ShowFatalError( null, exception );
		};

		TaskScheduler.UnobservedTaskException += ( sender, args ) =>
		{
			args.SetObserved();

			ShowFatalError( null, args.Exception );
		};

		Logger.WriteLine( "[App] App_Startup >>>" );

		_refactoredMutex = new Mutex( true, RefactoredMutexName, out var createdNew );

		if ( !createdNew )
		{
			Misc.BringExistingInstanceToFront();

			Shutdown();
		}
		else
		{
			_classicMutex = new Mutex( true, ClassicMutexName, out createdNew );

			if ( !createdNew )
			{
				ShowFatalError( DataContext.DataContext.Instance.Localization[ "ClassicMAIRAIsRunning" ] );
			}
			else
			{
				Misc.DisableThrottling();

				if ( !Directory.Exists( DocumentsFolder ) )
				{
					Directory.CreateDirectory( DocumentsFolder );
				}

				Logger.Initialize();
				TopLevelWindow.Initialize();
				SettingsFile.Initialize();
				AdminBoxx.Initialize();
				AudioManager.Initialize();
				Simulator.Initialize();
				DirectInput.Initialize();
				StreamDeck.Initialize();

#if !ADMINBOXX

				CloudService.Initialize();
				Graph.Initialize();
				Pedals.Initialize();
				RacingWheel.Initialize();
				Sounds.Initialize();
				LFE.Initialize();
				MultimediaTimer.Initialize();
				RecordingManager.Initialize();
				TimingMarkers.Initialize();
				Telemetry.Initialize();
				Wind.Initialize();
				SeatBeltTensioner.Initialize();
				HidHotplugMonitor.Initialize();
				TradingPaints.Initialize();

#endif

				DirectInput.OnInput += OnInput;

				DataContext.DataContext.Instance.Settings.UpdateSettings( false );

				Ready = true;

				GC.Collect();

				MainWindow.Resources = Current.Resources;

				MainWindow.Initialize();

				var showWindow = true;

				if ( DataContext.DataContext.Instance.Settings.AppStartMinimized )
				{
					MainWindow.WindowState = WindowState.Minimized;

					if ( DataContext.DataContext.Instance.Settings.AppMinimizeToSystemTray )
					{
						showWindow = false;
					}
				}

				if ( showWindow )
				{
					MainWindow.Show();
				}

				if ( DataContext.DataContext.Instance.Settings.AdminBoxxConnectOnStartup )
				{
					AdminBoxx.Connect();
				}

				if ( DataContext.DataContext.Instance.Settings.WindConnectOnStartup )
				{
					Wind.Connect();
				}

				if ( DataContext.DataContext.Instance.Settings.SeatBeltTensionerEnabled )
				{
					SeatBeltTensioner.Connect();
				}

#if !ADMINBOXX

				if ( DataContext.DataContext.Instance.Settings.AppCheckForUpdates )
				{
					await CloudService.CheckForUpdates( false );
				}

#endif

				_workerThread.Start();

				_timer.Start();

				Simulator.Start();

				GC.Collect();
			}
		}

		Logger.WriteLine( "[App] <<< App_Startup" );
	}

	private void App_Exit( object sender, EventArgs e )
	{
		Logger.WriteLine( "[App] App_Exit >>>" );

		_timer.Stop();

		_running = false;

		if ( _workerThread.IsAlive )
		{
			TriggerWorkerThread();

			_workerThread.Join( 5000 );
		}

		SpeechToTextWindow?.Close();
		GripOMeterWindow?.Close();
		GapMonitorWindow?.Close();

		_ = SpeechToText.DisableAsync();

		Simulator.Shutdown();
		AdminBoxx.Shutdown();
		DirectInput.Shutdown();

#if !ADMINBOXX

		LFE.Shutdown();
		MultimediaTimer.Shutdown();
		VirtualJoystick.Shutdown();
		Telemetry.Shutdown();
		TradingPaints.Shutdown();

#endif

		Logger.WriteLine( "[App] <<< App_Exit" );

		Logger.Shutdown(); // do this last
	}

	private void ComboBox_PreviewMouseWheel( object sender, MouseWheelEventArgs e )
	{
		if ( sender is not ComboBox comboBox ) return;

		if ( comboBox.IsDropDownOpen ) return;

		e.Handled = true;

		var scrollViewer = Misc.FindAncestor<ScrollViewer>( comboBox );

		if ( scrollViewer is null ) return;

		var forwardArgs = new MouseWheelEventArgs( e.MouseDevice, e.Timestamp, e.Delta )
		{
			RoutedEvent = UIElement.MouseWheelEvent,
			Source = sender
		};

		scrollViewer.RaiseEvent( forwardArgs );
	}

	private void OnInput( string deviceProductName, Guid deviceInstanceGuid, int buttonNumber, bool isPressed )
	{
		if ( !UpdateButtonMappingsWindow.WindowIsOpen && isPressed )
		{
			// shortcut to settings

			var settings = DataContext.DataContext.Instance.Settings;

			// shortcut to localization

			var localization = DataContext.DataContext.Instance.Localization;

			// racing wheel power button

			if ( CheckMappedButtons( settings.RacingWheelEnableForceFeedbackButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelEnableForceFeedback = !settings.RacingWheelEnableForceFeedback;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Power", settings.RacingWheelEnableForceFeedback ? localization[ "ON" ] : localization[ "OFF" ] );
				}
			}

			// racing wheel test button

			if ( CheckMappedButtons( settings.RacingWheelTestButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.PlayTestSignal = true;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Test" );
				}
			}

			// racing wheel reset button

			if ( CheckMappedButtons( settings.RacingWheelResetButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.ResetForceFeedback = true;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Reset" );
				}
			}

			// racing wheel strength knob

			if ( CheckMappedButtons( settings.RacingWheelStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelStrength += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Strength", settings.RacingWheelStrengthString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelStrength -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Strength", settings.RacingWheelStrengthString );
				}
			}

			// racing wheel max force knob

			if ( CheckMappedButtons( settings.RacingWheelMaxForcePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMaxForce += 1f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "MaxForce", settings.RacingWheelMaxForceString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelMaxForceMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMaxForce -= 1f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "MaxForce", settings.RacingWheelMaxForceString );
				}
			}

			// racing wheel auto target knob

			if ( CheckMappedButtons( settings.RacingWheelAutoMarginPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelAutoMargin -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "AutoTarget", settings.RacingWheelAutoMarginString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelAutoMarginMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelAutoMargin += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "AutoTarget", settings.RacingWheelAutoMarginString );
				}
			}

			// racing wheel auto button

			if ( CheckMappedButtons( settings.RacingWheelSetButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.AutoSetMaxForce = true;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					var autoTorqueString = $"{RacingWheel.GetCurrentAutoTorque():F1} {localization[ "TorqueUnits" ]}";

					RacingWheel.SendChatMessage( "Set", autoTorqueString );
				}
			}

			// racing wheel clear button

			if ( CheckMappedButtons( settings.RacingWheelClearButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.ClearPeakTorque = true;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Clear" );
				}
			}

			// racing wheel prediction blend knob

			if ( CheckMappedButtons( settings.RacingWheelPredictionBlendPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelPredictionBlend += 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "PredictionBlend", settings.RacingWheelPredictionBlendString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelPredictionBlendMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelPredictionBlend -= 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "PredictionBlend", settings.RacingWheelPredictionBlendString );
				}
			}

			// racing wheel detail boost knob

			if ( CheckMappedButtons( settings.RacingWheelDetailBoostPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDetailBoost += 0.1f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "DetailBoost", settings.RacingWheelDetailBoostString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelDetailBoostMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDetailBoost -= 0.1f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "DetailBoost", settings.RacingWheelDetailBoostString );
				}
			}

			// racing wheel detail boost bias knob

			if ( CheckMappedButtons( settings.RacingWheelDetailBoostBiasPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDetailBoostBias += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "DetailBoostBias", settings.RacingWheelDetailBoostBiasString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelDetailBoostBiasMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDetailBoostBias -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "DetailBoostBias", settings.RacingWheelDetailBoostBiasString );
				}
			}

			// racing wheel delta limit knob

			if ( CheckMappedButtons( settings.RacingWheelDeltaLimitPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDeltaLimit += 10f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "DeltaLimit", settings.RacingWheelDeltaLimitString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelDeltaLimitMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDeltaLimit -= 10f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "DeltaLimit", settings.RacingWheelDeltaLimitString );
				}
			}

			// racing wheel delta limiter bias knob

			if ( CheckMappedButtons( settings.RacingWheelDeltaLimiterBiasPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDeltaLimiterBias += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "DeltaLimiterBias", settings.RacingWheelDeltaLimiterBiasString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelDeltaLimiterBiasMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDeltaLimiterBias -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "DeltaLimiterBias", settings.RacingWheelDeltaLimiterBiasString );
				}
			}

			// racing wheel slew compression threshold knob

			if ( CheckMappedButtons( settings.RacingWheelSlewCompressionThresholdPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelSlewCompressionThreshold += 1f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "SlewCompressionThreshold", settings.RacingWheelSlewCompressionThresholdString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelSlewCompressionThresholdMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelSlewCompressionThreshold -= 1f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "SlewCompressionThreshold", settings.RacingWheelSlewCompressionThresholdString );
				}
			}

			// racing wheel slew compression rate knob

			if ( CheckMappedButtons( settings.RacingWheelSlewCompressionRatePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelSlewCompressionRate += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "SlewCompressionRate", settings.RacingWheelSlewCompressionRateString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelSlewCompressionRateMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelSlewCompressionRate -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "SlewCompressionRate", settings.RacingWheelSlewCompressionRateString );
				}
			}

			// racing wheel total compression threshold knob

			if ( CheckMappedButtons( settings.RacingWheelTotalCompressionThresholdPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelTotalCompressionThreshold += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "TotalCompressionThreshold", settings.RacingWheelTotalCompressionThresholdString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelTotalCompressionThresholdMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelTotalCompressionThreshold -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "TotalCompressionThreshold", settings.RacingWheelTotalCompressionThresholdString );
				}
			}

			// racing wheel total compression rate knob

			if ( CheckMappedButtons( settings.RacingWheelTotalCompressionRatePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelTotalCompressionRate += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "TotalCompressionRate", settings.RacingWheelTotalCompressionRateString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelTotalCompressionRateMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelTotalCompressionRate -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "TotalCompressionRate", settings.RacingWheelTotalCompressionRateString );
				}
			}

			// racing wheel multi 360 Hz detail knob

			if ( CheckMappedButtons( settings.RacingWheelMulti360HzDetailPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMulti360HzDetail += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Multi360HzDetail", settings.RacingWheelMulti360HzDetailString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelMulti360HzDetailMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMulti360HzDetail -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Multi360HzDetail", settings.RacingWheelMulti360HzDetailString );
				}
			}

			// racing wheel multi torque compression knob

			if ( CheckMappedButtons( settings.RacingWheelMultiTorqueCompressionPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMultiTorqueCompression += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "TorqueCompression", settings.RacingWheelMultiTorqueCompressionString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelMultiTorqueCompressionMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMultiTorqueCompression -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "TorqueCompression", settings.RacingWheelMultiTorqueCompressionString );
				}
			}

			// racing wheel multi slew rate reduction knob

			if ( CheckMappedButtons( settings.RacingWheelMultiSlewRateReductionPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMultiSlewRateReduction += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "SlewRateReduction", settings.RacingWheelMultiSlewRateReductionString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelMultiSlewRateReductionMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMultiSlewRateReduction -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "SlewRateReduction", settings.RacingWheelMultiSlewRateReductionString );
				}
			}

			// racing wheel multi detail gain knob

			if ( CheckMappedButtons( settings.RacingWheelMultiDetailGainPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMultiDetailGain += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "DetailGain", settings.RacingWheelMultiDetailGainString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelMultiDetailGainMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMultiDetailGain -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "DetailGain", settings.RacingWheelMultiDetailGainString );
				}
			}

			// racing wheel multi output smoothing knob

			if ( CheckMappedButtons( settings.RacingWheelMultiOutputSmoothingPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMultiOutputSmoothing += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "OutputSmoothing", settings.RacingWheelMultiOutputSmoothingString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelMultiOutputSmoothingMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMultiOutputSmoothing -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "OutputSmoothing", settings.RacingWheelMultiOutputSmoothingString );
				}
			}

			// racing wheel output minimum knob

			if ( CheckMappedButtons( settings.RacingWheelOutputMinimumPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelOutputMinimum += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Minimum", settings.RacingWheelOutputMinimumString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelOutputMinimumMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelOutputMinimum -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Minimum", settings.RacingWheelOutputMinimumString );
				}
			}

			// racing wheel output maximum knob

			if ( CheckMappedButtons( settings.RacingWheelOutputMaximumPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelOutputMaximum += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Maximum", settings.RacingWheelOutputMaximumString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelOutputMaximumMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelOutputMaximum -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Maximum", settings.RacingWheelOutputMaximumString );
				}
			}

			// racing wheel output curve knob

			if ( CheckMappedButtons( settings.RacingWheelOutputCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelOutputCurve += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Curve", settings.RacingWheelOutputCurveString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelOutputCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelOutputCurve -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Curve", settings.RacingWheelOutputCurveString );
				}
			}

			// racing wheel start recording

			if ( CheckMappedButtons( settings.RacingWheelStartRecordingMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RecordingManager.StartRecording();
			}

			// racing wheel lfe strength knob

			if ( CheckMappedButtons( settings.RacingWheelLFEStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelLFEStrength += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Strength", settings.RacingWheelLFEStrengthString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelLFEStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelLFEStrength -= 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Strength", settings.RacingWheelLFEStrengthString );
				}
			}

			// racing wheel crash protection longitudinal g force knob

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionLongitudalGForcePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionLongitudalGForce += 0.5f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "LongitudalGForce", settings.RacingWheelCrashProtectionLongitudalGForceString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionLongitudalGForceMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionLongitudalGForce -= 0.5f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "LongitudalGForce", settings.RacingWheelCrashProtectionLongitudalGForceString );
				}
			}

			// racing wheel crash protection lateral g force knob

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionLateralGForcePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionLateralGForce += 0.5f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "LateralGForce", settings.RacingWheelCrashProtectionLateralGForceString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionLateralGForceMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionLateralGForce -= 0.5f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "LateralGForce", settings.RacingWheelCrashProtectionLateralGForceString );
				}
			}

			// racing wheel crash protection duration knob

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionDurationPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionDuration += 0.5f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Duration", settings.RacingWheelCrashProtectionDurationString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionDurationMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionDuration -= 0.5f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Duration", settings.RacingWheelCrashProtectionDurationString );
				}
			}

			// racing wheel crash protection force reduction knob

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionForceReductionPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionForceReduction += 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ForceReduction", settings.RacingWheelCrashProtectionForceReductionString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionForceReductionMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionForceReduction -= 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ForceReduction", settings.RacingWheelCrashProtectionForceReductionString );
				}
			}

			// racing wheel curb protection shock velocity knob

			if ( CheckMappedButtons( settings.RacingWheelCurbProtectionShockVelocityPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCurbProtectionShockVelocity += 0.1f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ShockVelocity", settings.RacingWheelCurbProtectionShockVelocityString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelCurbProtectionShockVelocityMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCurbProtectionShockVelocity -= 0.1f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ShockVelocity", settings.RacingWheelCurbProtectionShockVelocityString );
				}
			}

			// racing wheel curb protection duration knob

			if ( CheckMappedButtons( settings.RacingWheelCurbProtectionDurationPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCurbProtectionDuration += 0.1f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Duration", settings.RacingWheelCurbProtectionDurationString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelCurbProtectionDurationMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCurbProtectionDuration -= 0.1f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Duration", settings.RacingWheelCurbProtectionDurationString );
				}
			}

			// racing wheel curb protection force reduction knob

			if ( CheckMappedButtons( settings.RacingWheelCurbProtectionForceReductionPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCurbProtectionForceReduction += 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ForceReduction", settings.RacingWheelCurbProtectionForceReductionString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelCurbProtectionForceReductionMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCurbProtectionForceReduction -= 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ForceReduction", settings.RacingWheelCurbProtectionForceReductionString );
				}
			}

			// racing wheel parked strength knob

			if ( CheckMappedButtons( settings.RacingWheelParkedStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelParkedStrength += 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ForceFeedbackStrength", settings.RacingWheelParkedStrengthString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelParkedStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelParkedStrength -= 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ForceFeedbackStrength", settings.RacingWheelParkedStrengthString );
				}
			}

			// racing wheel parked friction knob

			if ( CheckMappedButtons( settings.RacingWheelParkedFrictionPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelParkedFriction += 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ParkedFriction", settings.RacingWheelParkedFrictionString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelParkedFrictionMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelParkedFriction -= 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ParkedFriction", settings.RacingWheelParkedFrictionString );
				}
			}

			// racing wheel soft lock knob

			if ( CheckMappedButtons( settings.RacingWheelSoftLockStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelSoftLockStrength += 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "SoftLockStrength", settings.RacingWheelSoftLockStrengthString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelSoftLockStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelSoftLockStrength -= 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "SoftLockStrength", settings.RacingWheelSoftLockStrengthString );
				}
			}

			// racing wheel friction knob

			if ( CheckMappedButtons( settings.RacingWheelFrictionPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelFriction += 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Friction", settings.RacingWheelFrictionString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelFrictionMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelFriction -= 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "Friction", settings.RacingWheelFrictionString );
				}
			}

			// racing wheel wheel centering strength knob

			if ( CheckMappedButtons( settings.RacingWheelWheelCenteringStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelWheelCenteringStrength += 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "WheelCenteringStrength", settings.RacingWheelWheelCenteringStrengthString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelWheelCenteringStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelWheelCenteringStrength -= 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "WheelCenteringStrength", settings.RacingWheelWheelCenteringStrengthString );
				}
			}

			// racing wheel gear change vibrate strength knob

			if ( CheckMappedButtons( settings.RacingWheelGearChangeVibrateStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelGearChangeVibrateStrength += 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "GearChangeVibrateStrength", settings.RacingWheelGearChangeVibrateStrengthString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelGearChangeVibrateStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelGearChangeVibrateStrength -= 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "GearChangeVibrateStrength", settings.RacingWheelGearChangeVibrateStrengthString );
				}
			}

			// racing wheel abs vibrate strength knob

			if ( CheckMappedButtons( settings.RacingWheelABSVibrateStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelABSVibrateStrength += 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ABSVibrateStrength", settings.RacingWheelABSVibrateStrengthString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelABSVibrateStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelABSVibrateStrength -= 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ABSVibrateStrength", settings.RacingWheelABSVibrateStrengthString );
				}
			}

			// steering effects understeer minimum threshold

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerMinimumThresholdPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerMinimumThreshold += 0.01f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerMinimumThresholdMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerMinimumThreshold -= 0.01f;
			}

			// steering effects understeer maximum threshold

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerMaximumThresholdPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerMaximumThreshold += 0.01f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerMaximumThresholdMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerMaximumThreshold -= 0.01f;
			}

			// steering effects understeer wheel vibration strength

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWheelVibrationStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWheelVibrationStrength += 0.01f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWheelVibrationStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWheelVibrationStrength -= 0.01f;
			}

			// steering effects understeer wheel vibration minimum frequency

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWheelVibrationMinimumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWheelVibrationMinimumFrequency += 1f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWheelVibrationMinimumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWheelVibrationMinimumFrequency -= 1f;
			}

			// steering effects understeer wheel vibration maximum frequency

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWheelVibrationMaximumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWheelVibrationMaximumFrequency += 1f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWheelVibrationMaximumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWheelVibrationMaximumFrequency -= 1f;
			}

			// steering effects understeer wheel vibration curve

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWheelVibrationCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWheelVibrationCurve += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWheelVibrationCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWheelVibrationCurve -= 0.05f;
			}

			// steering effects understeer wheel constant force strength

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWheelConstantForceStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWheelConstantForceStrength += 0.01f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWheelConstantForceStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWheelConstantForceStrength -= 0.01f;
			}

			// steering effects understeer wheel constant force curve

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWheelConstantForceCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWheelConstantForceCurve += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWheelConstantForceCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWheelConstantForceCurve -= 0.05f;
			}

			// steering effects understeer pedal vibration minimum frequency

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerPedalVibrationMinimumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerPedalVibrationMinimumFrequency += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerPedalVibrationMinimumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerPedalVibrationMinimumFrequency -= 0.05f;
			}

			// steering effects understeer pedal vibration maximum frequency

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerPedalVibrationMaximumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerPedalVibrationMaximumFrequency += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerPedalVibrationMaximumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerPedalVibrationMaximumFrequency -= 0.05f;
			}

			// steering effects understeer pedal vibration curve

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerPedalVibrationCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerPedalVibrationCurve += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerPedalVibrationCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerPedalVibrationCurve -= 0.05f;
			}

			// steering effects oversteer minimum threshold

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerMinimumThresholdPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerMinimumThreshold += 0.01f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerMinimumThresholdMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerMinimumThreshold -= 0.01f;
			}

			// steering effects oversteer maximum threshold

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerMaximumThresholdPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerMaximumThreshold += 0.01f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerMaximumThresholdMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerMaximumThreshold -= 0.01f;
			}

			// steering effects oversteer wheel vibration strength

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerWheelVibrationStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerWheelVibrationStrength += 0.01f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerWheelVibrationStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerWheelVibrationStrength -= 0.01f;
			}

			// steering effects oversteer wheel vibration minimum frequency

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerWheelVibrationMinimumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerWheelVibrationMinimumFrequency += 1f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerWheelVibrationMinimumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerWheelVibrationMinimumFrequency -= 1f;
			}

			// steering effects oversteer wheel vibration maximum frequency

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerWheelVibrationMaximumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerWheelVibrationMaximumFrequency += 1f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerWheelVibrationMaximumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerWheelVibrationMaximumFrequency -= 1f;
			}

			// steering effects oversteer wheel vibration curve

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerWheelVibrationCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerWheelVibrationCurve += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerWheelVibrationCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerWheelVibrationCurve -= 0.05f;
			}

			// steering effects oversteer wheel constant force strength

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerWheelConstantForceStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerWheelConstantForceStrength += 0.01f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerWheelConstantForceStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerWheelConstantForceStrength -= 0.01f;
			}

			// steering effects oversteer wheel constant force curve

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerWheelConstantForceCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerWheelConstantForceCurve += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerWheelConstantForceCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerWheelConstantForceCurve -= 0.05f;
			}

			// steering effects oversteer pedal vibration minimum frequency

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerPedalVibrationMinimumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerPedalVibrationMinimumFrequency += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerPedalVibrationMinimumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerPedalVibrationMinimumFrequency -= 0.05f;
			}

			// steering effects oversteer pedal vibration maximum frequency

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerPedalVibrationMaximumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerPedalVibrationMaximumFrequency += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerPedalVibrationMaximumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerPedalVibrationMaximumFrequency -= 0.05f;
			}

			// steering effects oversteer pedal vibration curve

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerPedalVibrationCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerPedalVibrationCurve += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsOversteerPedalVibrationCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsOversteerPedalVibrationCurve -= 0.05f;
			}

			// steering effects seat-of-pants minimum threshold

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsMinimumThresholdPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsMinimumThreshold += 0.5f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsMinimumThresholdMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsMinimumThreshold -= 0.5f;
			}

			// steering effects seat-of-pants maximum threshold

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsMaximumThresholdPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsMaximumThreshold += 0.5f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsMaximumThresholdMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsMaximumThreshold -= 0.5f;
			}

			// steering effects seat-of-pants wheel vibration strength

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsWheelVibrationStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsWheelVibrationStrength += 0.01f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsWheelVibrationStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsWheelVibrationStrength -= 0.01f;
			}

			// steering effects seat-of-pants wheel vibration minimum frequency

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency += 1f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency -= 1f;
			}

			// steering effects seat-of-pants wheel vibration maximum frequency

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency += 1f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency -= 1f;
			}

			// steering effects seat-of-pants wheel vibration curve

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsWheelVibrationCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsWheelVibrationCurve += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsWheelVibrationCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsWheelVibrationCurve -= 0.05f;
			}

			// steering effects seat-of-pants wheel constant force strength

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsWheelConstantForceStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsWheelConstantForceStrength += 0.01f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsWheelConstantForceStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsWheelConstantForceStrength -= 0.01f;
			}

			// steering effects seat-of-pants wheel constant force curve

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsWheelConstantForceCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsWheelConstantForceCurve += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsWheelConstantForceCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsWheelConstantForceCurve -= 0.05f;
			}

			// steering effects seat-of-pants pedal vibration minimum frequency

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequency += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequency -= 0.05f;
			}

			// steering effects seat-of-pants pedal vibration maximum frequency

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequency += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequency -= 0.05f;
			}

			// steering effects seat-of-pants pedal vibration curve

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsPedalVibrationCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsPedalVibrationCurve += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsSeatOfPantsPedalVibrationCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsSeatOfPantsPedalVibrationCurve -= 0.05f;
			}

			// pedals clutch strength 1 knob

			if ( CheckMappedButtons( settings.PedalsClutchStrength1PlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsClutchStrength1 += 0.05f;
			}

			if ( CheckMappedButtons( settings.PedalsClutchStrength1MinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsClutchStrength1 -= 0.05f;
			}

			// pedals clutch test 1 button

			if ( CheckMappedButtons( settings.PedalsClutchTest1ButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				Pedals.StartTest( 0, 0 );
			}

			// pedals clutch strength 2 knob

			if ( CheckMappedButtons( settings.PedalsClutchStrength2PlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsClutchStrength2 += 0.05f;
			}

			if ( CheckMappedButtons( settings.PedalsClutchStrength2MinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsClutchStrength2 -= 0.05f;
			}

			// pedals clutch test 2 button

			if ( CheckMappedButtons( settings.PedalsClutchTest2ButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				Pedals.StartTest( 0, 1 );
			}

			// pedals clutch strength 3 knob

			if ( CheckMappedButtons( settings.PedalsClutchStrength3PlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsClutchStrength3 += 0.05f;
			}

			if ( CheckMappedButtons( settings.PedalsClutchStrength3MinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsClutchStrength3 -= 0.05f;
			}

			// pedals clutch test 3 button

			if ( CheckMappedButtons( settings.PedalsClutchTest3ButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				Pedals.StartTest( 0, 2 );
			}

			// pedals brake strength 1 knob

			if ( CheckMappedButtons( settings.PedalsBrakeStrength1PlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsBrakeStrength1 += 0.05f;
			}

			if ( CheckMappedButtons( settings.PedalsBrakeStrength1MinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsBrakeStrength1 -= 0.05f;
			}

			// pedals brake test 1 button

			if ( CheckMappedButtons( settings.PedalsBrakeTest1ButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				Pedals.StartTest( 1, 0 );
			}

			// pedals brake strength 2 knob

			if ( CheckMappedButtons( settings.PedalsBrakeStrength2PlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsBrakeStrength2 += 0.05f;
			}

			if ( CheckMappedButtons( settings.PedalsBrakeStrength2MinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsBrakeStrength2 -= 0.05f;
			}

			// pedals brake test 2 button

			if ( CheckMappedButtons( settings.PedalsBrakeTest2ButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				Pedals.StartTest( 1, 1 );
			}

			// pedals brake strength 3 knob

			if ( CheckMappedButtons( settings.PedalsBrakeStrength3PlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsBrakeStrength3 += 0.05f;
			}

			if ( CheckMappedButtons( settings.PedalsBrakeStrength3MinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsBrakeStrength3 -= 0.05f;
			}

			// pedals brake test 3 button

			if ( CheckMappedButtons( settings.PedalsBrakeTest3ButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				Pedals.StartTest( 2, 1 );
			}

			// pedals throttle strength 1 knob

			if ( CheckMappedButtons( settings.PedalsThrottleStrength1PlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsThrottleStrength1 += 0.05f;
			}

			if ( CheckMappedButtons( settings.PedalsThrottleStrength1MinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsThrottleStrength1 -= 0.05f;
			}

			// pedals throttle test 1 button

			if ( CheckMappedButtons( settings.PedalsThrottleTest1ButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				Pedals.StartTest( 2, 0 );
			}

			// pedals throttle strength 2 knob

			if ( CheckMappedButtons( settings.PedalsThrottleStrength2PlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsThrottleStrength2 += 0.05f;
			}

			if ( CheckMappedButtons( settings.PedalsThrottleStrength2MinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsThrottleStrength2 -= 0.05f;
			}

			// pedals throttle test 2 button

			if ( CheckMappedButtons( settings.PedalsThrottleTest2ButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				Pedals.StartTest( 2, 1 );
			}

			// pedals throttle strength 3 knob

			if ( CheckMappedButtons( settings.PedalsThrottleStrength3PlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsThrottleStrength3 += 0.05f;
			}

			if ( CheckMappedButtons( settings.PedalsThrottleStrength3MinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsThrottleStrength3 -= 0.05f;
			}

			// pedals throttle test 3 button

			if ( CheckMappedButtons( settings.PedalsThrottleTest3ButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				Pedals.StartTest( 2, 2 );
			}

			// pedals shift into gear frequency knob

			if ( CheckMappedButtons( settings.PedalsShiftIntoGearFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsShiftIntoGearFrequency += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsShiftIntoGearFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsShiftIntoGearFrequency -= 0.01f;
			}

			// pedals shift into gear amplitude knob

			if ( CheckMappedButtons( settings.PedalsShiftIntoGearAmplitudePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsShiftIntoGearAmplitude += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsShiftIntoGearAmplitudeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsShiftIntoGearAmplitude -= 0.01f;
			}

			// pedals shift into gear duration knob

			if ( CheckMappedButtons( settings.PedalsShiftIntoGearDurationPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsShiftIntoGearDuration += 0.05f;
			}

			if ( CheckMappedButtons( settings.PedalsShiftIntoGearDurationMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsShiftIntoGearDuration -= 0.05f;
			}

			// pedals shift into neutral frequency knob

			if ( CheckMappedButtons( settings.PedalsShiftIntoNeutralFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsShiftIntoNeutralFrequency += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsShiftIntoNeutralFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsShiftIntoNeutralFrequency -= 0.01f;
			}

			// pedals shift into neutral amplitude knob

			if ( CheckMappedButtons( settings.PedalsShiftIntoNeutralAmplitudePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsShiftIntoNeutralAmplitude += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsShiftIntoNeutralAmplitudeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsShiftIntoNeutralAmplitude -= 0.01f;
			}

			// pedals shift into neutral duration knob

			if ( CheckMappedButtons( settings.PedalsShiftIntoNeutralDurationPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsShiftIntoNeutralDuration += 0.05f;
			}

			if ( CheckMappedButtons( settings.PedalsShiftIntoNeutralDurationMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsShiftIntoNeutralDuration -= 0.05f;
			}

			// pedals abs engaged frequency knob

			if ( CheckMappedButtons( settings.PedalsABSEngagedFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsABSEngagedFrequency += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsABSEngagedFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsABSEngagedFrequency -= 0.01f;
			}

			// pedals abs engaged amplitude knob

			if ( CheckMappedButtons( settings.PedalsABSEngagedAmplitudePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsABSEngagedAmplitude += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsABSEngagedAmplitudeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsABSEngagedAmplitude -= 0.01f;
			}

			// pedals starting rpm knob

			if ( CheckMappedButtons( settings.PedalsStartingRPMPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsStartingRPM += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsStartingRPMMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsStartingRPM -= 0.01f;
			}

			// pedals wheel lock frequency knob

			if ( CheckMappedButtons( settings.PedalsWheelLockFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsWheelLockFrequency += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsWheelLockFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsWheelLockFrequency -= 0.01f;
			}

			// pedals wheel lock sensitivity knob

			if ( CheckMappedButtons( settings.PedalsWheelLockSensitivityPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsWheelLockSensitivity += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsWheelLockSensitivityMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsWheelLockSensitivity -= 0.01f;
			}

			// pedals wheel spin frequency knob

			if ( CheckMappedButtons( settings.PedalsWheelSpinFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsWheelSpinFrequency += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsWheelSpinFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsWheelSpinFrequency -= 0.01f;
			}

			// pedals wheel spin sensitivity knob

			if ( CheckMappedButtons( settings.PedalsWheelSpinSensitivityPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsWheelSpinSensitivity += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsWheelSpinSensitivityMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsWheelSpinSensitivity -= 0.01f;
			}

			// pedals clutch slip start knob

			if ( CheckMappedButtons( settings.PedalsClutchSlipStartPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsClutchSlipStart += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsClutchSlipStartMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsClutchSlipStart -= 0.01f;
			}

			// pedals clutch slip end knob

			if ( CheckMappedButtons( settings.PedalsClutchSlipEndPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsClutchSlipEnd += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsClutchSlipEndMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsClutchSlipEnd -= 0.01f;
			}

			// pedals clutch slip frequency knob

			if ( CheckMappedButtons( settings.PedalsClutchSlipFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsClutchSlipFrequency += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsClutchSlipFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsClutchSlipFrequency -= 0.01f;
			}

			// pedals minimum frequency knob

			if ( CheckMappedButtons( settings.PedalsMinimumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsMinimumFrequency += 1f;
			}

			if ( CheckMappedButtons( settings.PedalsMinimumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsMinimumFrequency -= 1f;
			}

			// pedals maximum frequency knob

			if ( CheckMappedButtons( settings.PedalsMaximumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsMaximumFrequency += 1f;
			}

			if ( CheckMappedButtons( settings.PedalsMaximumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsMaximumFrequency -= 1f;
			}

			// pedals frequency curve knob

			if ( CheckMappedButtons( settings.PedalsFrequencyCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsFrequencyCurve += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsFrequencyCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsFrequencyCurve -= 0.01f;
			}

			// pedals minimum amplitude knob

			if ( CheckMappedButtons( settings.PedalsMinimumAmplitudePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsMinimumAmplitude += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsMinimumAmplitudeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsMinimumAmplitude -= 0.01f;
			}

			// pedals maximum amplitude knob

			if ( CheckMappedButtons( settings.PedalsMaximumAmplitudePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsMaximumAmplitude += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsMaximumAmplitudeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsMaximumAmplitude -= 0.01f;
			}

			// pedals amplitude curve knob

			if ( CheckMappedButtons( settings.PedalsAmplitudeCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsAmplitudeCurve += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsAmplitudeCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsAmplitudeCurve -= 0.01f;
			}

			// pedals noise damper

			if ( CheckMappedButtons( settings.PedalsNoiseDamperPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsNoiseDamper += 0.01f;
			}

			if ( CheckMappedButtons( settings.PedalsNoiseDamperMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.PedalsNoiseDamper -= 0.01f;
			}

			// wind master wind power

			if ( CheckMappedButtons( settings.WindMasterWindPowerPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.WindMasterWindPower += 0.01f;
			}

			if ( CheckMappedButtons( settings.WindMasterWindPowerMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.WindMasterWindPower -= 0.01f;
			}

			// wind minimum speed

			if ( CheckMappedButtons( settings.WindMinimumSpeedPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.WindMinimumSpeed += 0.01f;
			}

			if ( CheckMappedButtons( settings.WindMinimumSpeedMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.WindMinimumSpeed -= 0.01f;
			}

			// wind curving

			if ( CheckMappedButtons( settings.WindCurvingPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.WindCurving += 0.01f;
			}

			if ( CheckMappedButtons( settings.WindCurvingMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.WindCurving -= 0.01f;
			}

			// sounds master volume

			if ( CheckMappedButtons( settings.SoundsMasterVolumePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsMasterVolume += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsMasterVolumeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsMasterVolume -= 0.01f;
			}

			// sounds click volume

			if ( CheckMappedButtons( settings.SoundsClickVolumePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsClickVolume += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsClickVolumeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsClickVolume -= 0.01f;
			}

			// sounds click frequency ratio

			if ( CheckMappedButtons( settings.SoundsClickFrequencyRatioPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsClickFrequencyRatio += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsClickFrequencyRatioMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsClickFrequencyRatio -= 0.01f;
			}

			// sounds abs engaged volume

			if ( CheckMappedButtons( settings.SoundsABSEngagedVolumePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsABSEngagedVolume += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsABSEngagedVolumeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsABSEngagedVolume -= 0.01f;
			}

			// sounds abs engaged frequency ratio

			if ( CheckMappedButtons( settings.SoundsABSEngagedFrequencyRatioPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsABSEngagedFrequencyRatio += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsABSEngagedFrequencyRatioMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsABSEngagedFrequencyRatio -= 0.01f;
			}

			// sounds wheel lock volume

			if ( CheckMappedButtons( settings.SoundsWheelLockVolumePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsWheelLockVolume += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsWheelLockVolumeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsWheelLockVolume -= 0.01f;
			}

			// sounds wheel lock frequency ratio

			if ( CheckMappedButtons( settings.SoundsWheelLockFrequencyRatioPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsWheelLockFrequencyRatio += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsWheelLockFrequencyRatioMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsWheelLockFrequencyRatio -= 0.01f;
			}

			// sounds wheel lock sensitivity

			if ( CheckMappedButtons( settings.SoundsWheelLockSensitivityPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsWheelLockSensitivity += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsWheelLockSensitivityMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsWheelLockSensitivity -= 0.01f;
			}

			// sounds wheel spin volume

			if ( CheckMappedButtons( settings.SoundsWheelSpinVolumePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsWheelSpinVolume += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsWheelSpinVolumeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsWheelSpinVolume -= 0.01f;
			}

			// sounds wheel spin frequency ratio

			if ( CheckMappedButtons( settings.SoundsWheelSpinFrequencyRatioPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsWheelSpinFrequencyRatio += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsWheelSpinFrequencyRatioMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsWheelSpinFrequencyRatio -= 0.01f;
			}

			// sounds wheel spin sensitivity

			if ( CheckMappedButtons( settings.SoundsWheelSpinSensitivityPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsWheelSpinSensitivity += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsWheelSpinSensitivityMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsWheelSpinSensitivity -= 0.01f;
			}

			// sounds understeer volume

			if ( CheckMappedButtons( settings.SoundsUndersteerVolumePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsUndersteerVolume += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsUndersteerVolumeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsUndersteerVolume -= 0.01f;
			}

			// sounds understeer frequency ratio

			if ( CheckMappedButtons( settings.SoundsUndersteerFrequencyRatioPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsUndersteerFrequencyRatio += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsUndersteerFrequencyRatioMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsUndersteerFrequencyRatio -= 0.01f;
			}

			// sounds oversteer volume

			if ( CheckMappedButtons( settings.SoundsOversteerVolumePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsOversteerVolume += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsOversteerVolumeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsOversteerVolume -= 0.01f;
			}

			// sounds oversteer frequency ratio

			if ( CheckMappedButtons( settings.SoundsOversteerFrequencyRatioPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsOversteerFrequencyRatio += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsOversteerFrequencyRatioMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsOversteerFrequencyRatio -= 0.01f;
			}

			// sounds seat-of-pants volume

			if ( CheckMappedButtons( settings.SoundsSeatOfPantsVolumePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsSeatOfPantsVolume += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsSeatOfPantsVolumeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsSeatOfPantsVolume -= 0.01f;
			}

			// sounds seat-of-pants frequency ratio

			if ( CheckMappedButtons( settings.SoundsSeatOfPantsFrequencyRatioPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsSeatOfPantsFrequencyRatio += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsSeatOfPantsFrequencyRatioMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsSeatOfPantsFrequencyRatio -= 0.01f;
			}

			// adminboxx brightness knob

			if ( CheckMappedButtons( settings.AdminBoxxBrightnessPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.AdminBoxxBrightness += 0.01f;
			}

			if ( CheckMappedButtons( settings.AdminBoxxBrightnessMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.AdminBoxxBrightness -= 0.01f;
			}

			// adminboxx volume knob

			if ( CheckMappedButtons( settings.AdminBoxxVolumePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.AdminBoxxVolume += 0.01f;
			}

			if ( CheckMappedButtons( settings.AdminBoxxVolumeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.AdminBoxxVolume -= 0.01f;
			}
		}
	}

	private bool CheckMappedButtons( ButtonMappings buttonMappings, Guid deviceInstanceGuid, int buttonNumber )
	{
		foreach ( var mappedButton in buttonMappings.MappedButtons )
		{
			if ( mappedButton.ClickButton.DeviceInstanceGuid == deviceInstanceGuid )
			{
				if ( mappedButton.ClickButton.ButtonNumber == buttonNumber )
				{
					var fired = false;

					if ( mappedButton.HoldButton.DeviceInstanceGuid == Guid.Empty )
					{
						fired = true;
					}
					else if ( DirectInput.IsButtonDown( deviceInstanceGuid, mappedButton.HoldButton.ButtonNumber ) )
					{
						fired = true;
					}

					if ( fired )
					{
						var settings = DataContext.DataContext.Instance.Settings;

						if ( settings.SoundsMasterEnabled && settings.SoundsClickEnabled )
						{
							var app = App.Instance!;

							app.Sounds.Play( Sounds.SoundEffectType.Click );
						}

						return true;
					}
				}
			}
		}

		return false;
	}

	private void OnTimer( object? sender, EventArgs e )
	{
		var app = Instance;

		if ( app != null )
		{
			if ( !app.Simulator.IsConnected )
			{
				app.DirectInput.PollDevices( 1f );

				TriggerWorkerThread();
			}
		}
	}

	private static void WorkerThread()
	{
		var app = Instance!;

		app.Logger.WriteLine( "[App] Worker thread started" );

		while ( app._running )
		{
			app._autoResetEvent.WaitOne();

			if ( Interlocked.Exchange( ref app._tickMutex, 1 ) == 0 )
			{
				app.Dispatcher.InvokeAsync( () =>
				{
					try
					{
						app.SettingsFile.Tick( app );
						app.AdminBoxx.Tick( app );
						app.Debug.Tick( app );
						app.ChatQueue.Tick( app );
						app.MainWindow.Tick( app );
						app.Simulator.Tick( app );

#if !ADMINBOXX

						app.RacingWheel.Tick( app );
						app.Pedals.Tick( app );
						app.MultimediaTimer.Tick( app );
						app.Sounds.Tick( app );
						app.Graph.Tick( app );
						app.SteeringEffects.Tick( app );
						app.VirtualJoystick.Tick( app );
						app.TimingMarkers.Tick( app );
						app.Telemetry.Tick( app );
						app.Wind.Tick( app );
						app.SeatBeltTensioner.Tick( app );

						app.GripOMeterWindow?.Tick( app );
						app.GapMonitorWindow?.Tick( app );
						app.SpeechToTextWindow?.Tick( app );

#endif
					}
					catch ( Exception exception )
					{
						app.Logger.WriteLine( $"[App] Exception caught: {exception.Message}" );

						app.ShowFatalError( "An exception was thrown inside the app worker thread.", exception );
					}
					finally
					{
						app._tickMutex = 0;
					}
				} );
			}
		}

		app.Logger.WriteLine( "[App] Worker thread stopped" );
	}

	#region Overlay Window Management

	public void EnsureGripOMeterWindowExists()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			if ( GripOMeterWindow == null )
			{
				Dispatcher.Invoke( () =>
				{
					Logger.WriteLine( "[App] Creating GripOMeterWindow" );

					GripOMeterWindow = new();
				} );
			}
		} );
	}

	public void DestroyGripOMeterWindow()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			if ( GripOMeterWindow != null )
			{
				Dispatcher.Invoke( () =>
				{
					Logger.WriteLine( "[App] Destroying GripOMeterWindow" );

					GripOMeterWindow.Close();

					GripOMeterWindow = null;
				} );
			}
		} );
	}

	public void EnsureGapMonitorWindowExists()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			if ( GapMonitorWindow == null )
			{
				Dispatcher.Invoke( () =>
				{
					Logger.WriteLine( "[App] Creating GapMonitorWindow" );

					GapMonitorWindow = new();
				} );
			}
		} );
	}

	public void DestroyGapMonitorWindow()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			if ( GapMonitorWindow != null )
			{
				Dispatcher.Invoke( () =>
				{
					Logger.WriteLine( "[App] Destroying GapMonitorWindow" );

					GapMonitorWindow.Close();

					GapMonitorWindow = null;
				} );
			}
		} );
	}

	public void EnsureSpeechToTextWindowExists()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			if ( SpeechToTextWindow == null )
			{
				Dispatcher.Invoke( () =>
				{
					Logger.WriteLine( "[App] Creating SpeechToTextWindow" );

					SpeechToTextWindow = new();
				} );
			}
		} );
	}

	public void DestroySpeechToTextWindow()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			if ( SpeechToTextWindow != null )
			{
				Dispatcher.Invoke( () =>
				{
					Logger.WriteLine( "[App] Destroying SpeechToTextWindow" );

					SpeechToTextWindow.Close();

					SpeechToTextWindow = null;
				} );
			}
		} );
	}

	public void UpdateGripOMeterWindowVisibility()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			var settings = DataContext.DataContext.Instance.Settings;

			if ( settings.SteeringEffectsMakeGripOMeterDraggable || ( settings.SteeringEffectsShowGripOMeterWindow && Simulator.IsConnected && Simulator.IsOnTrack ) )
			{
				EnsureGripOMeterWindowExists();

				GripOMeterWindow?.MakeDraggable();
			}
			else
			{
				DestroyGripOMeterWindow();
			}
		} );
	}

	public void UpdateGapMonitorWindowVisibility()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			var settings = DataContext.DataContext.Instance.Settings;

			if ( settings.OverlaysMakeGapMonitorDraggable || ( settings.OverlaysShowGapMonitorWindow && Simulator.IsConnected && Simulator.IsOnTrack ) )
			{
				EnsureGapMonitorWindowExists();

				GapMonitorWindow?.MakeDraggable();
			}
			else
			{
				DestroyGapMonitorWindow();
			}
		} );
	}

	public void UpdateSpeechToTextWindowVisibility()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			var settings = DataContext.DataContext.Instance.Settings;

			if ( settings.SpeechToTextMakeOverlayWindowDraggable )
			{
				EnsureSpeechToTextWindowExists();

				SpeechToTextWindow?.MakeDraggable();
			}
			else
			{
				DestroySpeechToTextWindow();
			}
		} );
	}

	#endregion
}
