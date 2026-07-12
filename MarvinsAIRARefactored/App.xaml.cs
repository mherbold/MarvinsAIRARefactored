
using System.IO;
using System.Runtime;
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
	public TextToSpeech TextToSpeech { get; private set; } = null!;
	public Commentary Commentary { get; private set; } = null!;
	public SpeechToText SpeechToText { get; private set; } = null!;
	public TyphoonWind TyphoonWind { get; private set; } = null!;
	public GTensioner GTensioner { get; private set; } = null!;
	public HidHotPlugMonitor HidHotPlugMonitor { get; private set; } = null!;
	public TradingPaints TradingPaints { get; private set; } = null!;
	public AppManager AppManager { get; private set; } = null!;

	public GripOMeterWindow? GripOMeterWindow { get; set; }
	public GapMonitorWindow? GapMonitorWindow { get; set; }
	public DeltaMonitorWindow? DeltaMonitorWindow { get; set; }
	public SpeechToTextWindow? SpeechToTextWindow { get; set; }

	// Transient (not persisted) - when true, all overlay windows are made visible and draggable
	public bool OverlaysDraggable { get; set; } = false;

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

	// Index of mapped click-button coordinates -> the actions they fire. Rebuilt whenever mappings
	// change (controller profile switch, mapping edit) or at startup, so OnInput is an O(1) lookup
	// instead of scanning every mapping on every button event.
	private readonly Dictionary<(Guid deviceInstanceGuid, int buttonNumber), List<(MappableAction action, ButtonMappings.MappedButton mappedButton)>> _buttonMappingIndex = [];

	App()
	{
		Instance = this;

		InitializeComponent();
	}

	private void InitializeRuntimeComponents()
	{
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
		TextToSpeech = new();
		Commentary = new();
		SpeechToText = new();
		TyphoonWind = new();
		GTensioner = new();
		HidHotPlugMonitor = new();
		TradingPaints = new();
		AppManager = new();

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

		Logger = new();

		Logger.Initialize();

		// Mirror every window's layout for right-to-left languages. Registered before any window is
		// shown so the splash screen and all dialogs/overlays pick it up automatically.
		Misc.RegisterGlobalFlowDirectionHandler();

#if !ADMINBOXX

	DataContext.DataContext.Instance.Settings.AppCurrentLanguageCode = DataContext.DataContext.Instance.Localization.ChooseInitialLanguage();

	ShutdownMode = ShutdownMode.OnExplicitShutdown;

	// Check if splash screen should be shown
	var disableSplashScreenFilePath = Path.Combine( DocumentsFolder, "DisableSplashScreen.txt" );
	var showSplashScreen = !File.Exists( disableSplashScreenFilePath );

	StartupWindow? startupWindow = showSplashScreen ? new() : null;

	startupWindow?.Show();

	var startupStepIndex = 0;
	const int startupStepCount = 24;

	void RunStartupStep( string statusKey, Action initializeAction )
	{
		startupStepIndex++;

		startupWindow?.UpdateProgress( startupStepIndex * 100d / startupStepCount, DataContext.DataContext.Instance.Localization[ statusKey ] );

		initializeAction();
	}

#endif

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

#if !ADMINBOXX

				RunStartupStep( "InitializingTopLevelWindow", TopLevelWindow.Initialize );
				RunStartupStep( "InitializingAudioManager", AudioManager.Initialize ); // must be called before AdminBoxx.Initialize()
				RunStartupStep( "InitializingAdminBoxx", AdminBoxx.Initialize );
				RunStartupStep( "InitializingSimulator", Simulator.Initialize );
				RunStartupStep( "InitializingDirectInput", DirectInput.Initialize );
				RunStartupStep( "InitializingStreamDeck", StreamDeck.Initialize );
				RunStartupStep( "InitializingTextToSpeech", TextToSpeech.Initialize );
				RunStartupStep( "InitializingCommentary", () => Commentary.Initialize() );
				RunStartupStep( "InitializingCloudService", CloudService.Initialize );
				RunStartupStep( "InitializingGraph", Graph.Initialize );
				RunStartupStep( "InitializingPedals", Pedals.Initialize );
				RunStartupStep( "InitializingRacingWheel", RacingWheel.Initialize );
				RunStartupStep( "InitializingSounds", Sounds.Initialize );
				RunStartupStep( "InitializingLFE", LFE.Initialize );
				RunStartupStep( "InitializingMultimediaTimer", MultimediaTimer.Initialize );
				RunStartupStep( "InitializingRecordingManager", RecordingManager.Initialize );
				RunStartupStep( "InitializingTimingMarkers", TimingMarkers.Initialize );
				RunStartupStep( "InitializingTelemetry", Telemetry.Initialize );
				RunStartupStep( "InitializingWind", TyphoonWind.Initialize );
				RunStartupStep( "InitializingGTensioner", GTensioner.Initialize );
				RunStartupStep( "InitializingHidHotPlugMonitor", HidHotPlugMonitor.Initialize );
				RunStartupStep( "InitializingTradingPaints", TradingPaints.Initialize );
				RunStartupStep( "InitializingSettingsFile", SettingsFile.Initialize );

#else

				TopLevelWindow.Initialize();
				AudioManager.Initialize(); // must be called before AdminBoxx.Initialize()
				AdminBoxx.Initialize();
				Simulator.Initialize();
				DirectInput.Initialize();
				StreamDeck.Initialize();
				SettingsFile.Initialize();

#endif

				var settings = DataContext.DataContext.Instance.Settings;

				Misc.ForcePropertySetters( settings );

				try
				{
					CpuAffinityHelper.SetCpuAffinity( settings.AppAffinityMaskBits );
				}
				catch ( Exception ex )
				{
					Logger.WriteLine( $"[App] Failed to apply CPU affinity at startup: {ex.Message}" );
				}

				settings.UpdateSettings( false );

				ApplyTheme( settings.AppLightThemeEnabled );

				DirectInput.OnInput += OnInput;

				RebuildButtonMappingIndex();

				var ( missingMappableActions, duplicatedMappableActions ) = MappableActionCatalog.Validate();

				if ( missingMappableActions.Count > 0 )
				{
					Logger.WriteLine( $"[App] WARNING: {missingMappableActions.Count} button mapping(s) are not covered by MappableActionCatalog: {string.Join( ", ", missingMappableActions )}" );
				}

				if ( duplicatedMappableActions.Count > 0 )
				{
					Logger.WriteLine( $"[App] WARNING: MappableActionCatalog has duplicated action(s): {string.Join( ", ", duplicatedMappableActions )}" );
				}

				Ready = true;

				GC.Collect();

				MainWindow.Resources = Current.Resources;

#if !ADMINBOXX

				RunStartupStep( "InitializingMainWindow", MainWindow.Initialize );
				base.MainWindow = MainWindow;
				ShutdownMode = ShutdownMode.OnMainWindowClose;
				startupWindow?.Close();
				startupWindow = null;

#else

				MainWindow.Initialize();

#endif

				var showWindow = true;

				var startMinimized = DataContext.DataContext.Instance.Settings.AppStartMinimized;
				var minimizeToSystemTray = DataContext.DataContext.Instance.Settings.AppMinimizeToSystemTray;

				foreach ( var commandLineArgument in e.Args )
				{
					var normalizedArgument = commandLineArgument.TrimStart( '-', '/' ).ToLowerInvariant();

					if ( normalizedArgument is "minimized" or "m" )
					{
						startMinimized = true;

						Logger.WriteLine( $"[App] Command line argument '{commandLineArgument}' detected - starting minimized." );
					}
					else if ( normalizedArgument is "tray" or "t" )
					{
						startMinimized = true;
						minimizeToSystemTray = true;

						Logger.WriteLine( $"[App] Command line argument '{commandLineArgument}' detected - starting minimized to system tray." );
					}
				}

				if ( startMinimized )
				{
					MainWindow.WindowState = WindowState.Minimized;

					if ( minimizeToSystemTray )
					{
						showWindow = false;
					}
				}

				if ( showWindow )
				{
					MainWindow.Show();
				}

				// Start the worker thread and the input polling timer before showing the modal
				// first-run wizard. The wizard's button-mapping recorder relies on DirectInput.OnInput,
				// which is only fired from PollDevices (driven by OnTimer). ShowDialog() runs a nested
				// dispatcher loop, so the timer and worker keep ticking while the wizard is open.

				_workerThread.Start();

				_timer.Start();

#if !ADMINBOXX

				if ( showWindow && !DataContext.DataContext.Instance.Settings.AppWizardHasRun )
				{
					var wizardWindow = new Windows.WizardWindow
					{
						Owner = MainWindow
					};

					wizardWindow.ShowDialog();
				}

#endif

				if ( DataContext.DataContext.Instance.Settings.TyphoonWindConnectOnStartup )
				{
					TyphoonWind.Connect();
				}

				if ( DataContext.DataContext.Instance.Settings.GTensionerConnectOnStartup )
				{
					GTensioner.Connect();
				}

				if ( DataContext.DataContext.Instance.Settings.AdminBoxxConnectOnStartup )
				{
					AdminBoxx.Connect();
				}

#if !ADMINBOXX

				// "Check For Updates On Startup" - an explicit per-launch check, independent of the interval.
				if ( DataContext.DataContext.Instance.Settings.AppCheckForUpdates )
				{
					await CloudService.CheckForUpdates( false );
				}

#endif

				Simulator.Start();

				GC.Collect();

				// from here on the app is a soft-realtime device — prefer deferring blocking gen2 collections
				// (the startup GC.Collect calls above still ran in the default mode, fully compacting the
				// startup garbage first); the FFB hot paths are allocation-free in steady state, so collections
				// should be rare and UI-driven either way

				GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
			}
		}

		Logger.WriteLine( "[App] <<< App_Startup" );
	}

	/// <summary>
	/// Swaps the active theme resource dictionary between dark and light.
	/// Safe to call from any thread; marshals to the UI dispatcher if needed.
	/// </summary>
	public void ApplyTheme( bool lightTheme )
	{
		if ( !Dispatcher.CheckAccess() )
		{
			Dispatcher.Invoke( () => ApplyTheme( lightTheme ) );
			return;
		}

		var themeUri = lightTheme
			? new Uri( "/MarvinsAIRARefactored;component/Themes/LightTheme.xaml", UriKind.Relative )
			: new Uri( "/MarvinsAIRARefactored;component/Themes/DarkTheme.xaml", UriKind.Relative );

		var mergedDicts = Current.Resources.MergedDictionaries;

		// Replace the first merged dictionary (the theme slot)
		if ( mergedDicts.Count > 0 )
		{
			mergedDicts[ 0 ] = new ResourceDictionary { Source = themeUri };
		}
		else
		{
			mergedDicts.Add( new ResourceDictionary { Source = themeUri } );
		}

		// Update programmatic renderers that cannot use DynamicResource
		Controls.MairaKnob.UpdateThemeColors( lightTheme );

		if ( Graph != null )
		{
			Graph.UpdateThemeColors( lightTheme );
		}
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
		DeltaMonitorWindow?.Close();

		_ = SpeechToText.DisableAsync();
		TextToSpeech.Dispose();

		Simulator.Shutdown();
		AdminBoxx.Shutdown();
		DirectInput.Shutdown();

#if !ADMINBOXX

		LFE.Shutdown();
		MultimediaTimer.Shutdown();
		VirtualJoystick.Shutdown();
		Telemetry.Shutdown();
		TradingPaints.Shutdown();
		AppManager.Shutdown();

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

	// Rebuilds the click-button -> actions index from the active controller profile's live mappings.
	// Call after any mapping change (profile switch, mapping editor close, clear) and at startup.
	public void RebuildButtonMappingIndex()
	{
		_buttonMappingIndex.Clear();

		var settings = DataContext.DataContext.Instance.Settings;

		void AddToIndex( MappableAction action, ButtonMappings buttonMappings )
		{
			foreach ( var mappedButton in buttonMappings.MappedButtons )
			{
				var clickButton = mappedButton.ClickButton;

				if ( clickButton.DeviceInstanceGuid == Guid.Empty )
				{
					continue;
				}

				var key = ( clickButton.DeviceInstanceGuid, clickButton.ButtonNumber );

				if ( !_buttonMappingIndex.TryGetValue( key, out var candidates ) )
				{
					candidates = [];

					_buttonMappingIndex[ key ] = candidates;
				}

				candidates.Add( ( action, mappedButton ) );
			}
		}

		foreach ( var action in MappableActionCatalog.Actions )
		{
			AddToIndex( action, action.GetButtonMappings( settings ) );
		}

		// FFB graph module knob mappings are dynamic (modules come and go with the user's graphs), so they are
		// indexed straight from their settings store rather than the static catalog. The fire body resolves the
		// module view-model at fire time, so switching graphs never needs an index rebuild — only mapping edits
		// and module/graph removal do, and both already land here.
		foreach ( var ( mappingKey, buttonMappings ) in settings.RacingWheelFFBModuleButtonMappings )
		{
			if ( buttonMappings.MappedButtons.Count == 0 )
			{
				continue;
			}

			// mappingKey = "{ModuleId}/{SettingKey}/Plus|Minus" (see Settings.GetFFBModuleButtonMappings)
			var firstSlashIndex = mappingKey.IndexOf( '/' );
			var lastSlashIndex = mappingKey.LastIndexOf( '/' );

			if ( ( firstSlashIndex <= 0 ) || ( lastSlashIndex <= firstSlashIndex ) )
			{
				continue;
			}

			var moduleId = mappingKey[ ..firstSlashIndex ];
			var settingKey = mappingKey[ ( firstSlashIndex + 1 )..lastSlashIndex ];
			var direction = mappingKey[ ( lastSlashIndex + 1 ).. ] == "Plus" ? 1f : -1f;

			var action = new MappableAction
			{
				SettingsPropertyName = mappingKey,   // identifier only — never resolved against Settings
				CategoryKey = "RacingWheel",
				GroupLabelKey = "FFBGraph",
				LabelKey = settingKey,
				Direction = ( direction > 0f ) ? MappableActionDirection.Increase : MappableActionDirection.Decrease,
				OnFire = _ => DataContext.DataContext.Instance.RacingWheelGraphViewModel.AdjustModuleKnob( moduleId, settingKey, direction )
			};

			AddToIndex( action, buttonMappings );
		}
	}

	private void OnInput( string deviceProductName, Guid deviceInstanceGuid, int buttonNumber, bool isPressed )
	{
		if ( UpdateButtonMappingsWindow.WindowIsOpen || !isPressed )
		{
			return;
		}

		if ( !_buttonMappingIndex.TryGetValue( ( deviceInstanceGuid, buttonNumber ), out var candidates ) )
		{
			return;
		}

		var settings = DataContext.DataContext.Instance.Settings;

		HashSet<MappableAction>? firedActions = null;

		foreach ( var ( action, mappedButton ) in candidates )
		{
			var holdButton = mappedButton.HoldButton;

			var fired = ( holdButton.DeviceInstanceGuid == Guid.Empty ) || DirectInput.IsButtonDown( holdButton.DeviceInstanceGuid, holdButton.ButtonNumber );

			if ( !fired )
			{
				continue;
			}

			// Fire each action at most once per button press (matches the original return-on-first-match).
			firedActions ??= [];

			if ( !firedActions.Add( action ) )
			{
				continue;
			}

			if ( settings.SoundsMasterEnabled && settings.SoundsClickEnabled )
			{
				this.Sounds.Play( Sounds.SoundEffectType.Click );
			}

			action.OnFire( this );
		}
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
						app.Commentary.Tick( app );
						app.TyphoonWind.Tick( app );
						app.GTensioner.Tick( app );
						app.CloudService.Tick( app );

						app.GripOMeterWindow?.Tick( app );
						app.GapMonitorWindow?.Tick( app );
						app.DeltaMonitorWindow?.Tick( app );
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

	public void EnsureDeltaMonitorWindowExists()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			if ( DeltaMonitorWindow == null )
			{
				Dispatcher.Invoke( () =>
				{
					Logger.WriteLine( "[App] Creating DeltaMonitorWindow" );

					DeltaMonitorWindow = new();
				} );
			}
		} );
	}

	public void DestroyDeltaMonitorWindow()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			if ( DeltaMonitorWindow != null )
			{
				Dispatcher.Invoke( () =>
				{
					Logger.WriteLine( "[App] Destroying DeltaMonitorWindow" );

					DeltaMonitorWindow.Close();

					DeltaMonitorWindow = null;
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

			if ( settings.OverlaysShowGripOMeterWindow && ( OverlaysDraggable || ( Simulator.IsConnected && ( Simulator.IsOnTrack || settings.OverlaysShowWhenOffTrack ) && ( !Simulator.IsReplayPlaying || settings.OverlaysShowInReplayMode ) ) ) )
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

			if ( settings.OverlaysShowGapMonitorWindow && ( OverlaysDraggable || ( Simulator.IsConnected && ( Simulator.IsOnTrack || settings.OverlaysShowWhenOffTrack ) && ( !Simulator.IsReplayPlaying || settings.OverlaysShowInReplayMode ) ) ) )
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

	public void UpdateDeltaMonitorWindowVisibility()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			var settings = DataContext.DataContext.Instance.Settings;

			if ( settings.OverlaysShowDeltaMonitorWindow && ( OverlaysDraggable || ( Simulator.IsConnected && ( Simulator.IsOnTrack || settings.OverlaysShowWhenOffTrack ) && ( !Simulator.IsReplayPlaying || settings.OverlaysShowInReplayMode ) ) ) )
			{
				EnsureDeltaMonitorWindowExists();

				DeltaMonitorWindow?.MakeDraggable();
			}
			else
			{
				DestroyDeltaMonitorWindow();
			}
		} );
	}

	public void UpdateSpeechToTextWindowVisibility()
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			var settings = DataContext.DataContext.Instance.Settings;

			if ( OverlaysDraggable && settings.SpeechToTextEnabled )
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
