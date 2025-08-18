
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

using Application = System.Windows.Application;
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

	public static App? Instance { get; private set; }

	public Logger Logger { get; private set; }
	public CloudService CloudService { get; private set; }
	public SettingsFile SettingsFile { get; private set; }
	public Graph Graph { get; private set; }
	public Pedals Pedals { get; private set; }
	public AdminBoxx AdminBoxx { get; private set; }
	public Debug Debug { get; private set; }
	public new MainWindow MainWindow { get; private set; }
	public RacingWheel RacingWheel { get; private set; }
	public ChatQueue ChatQueue { get; private set; }
	public AudioManager AudioManager { get; private set; }
	public Sounds Sounds { get; private set; }
	public DirectInput DirectInput { get; private set; }
	public LFE LFE { get; private set; }
	public MultimediaTimer MultimediaTimer { get; private set; }
	public Simulator Simulator { get; private set; }
	public RecordingManager RecordingManager { get; private set; }
	public SteeringEffects SteeringEffects { get; private set; }
	public VirtualJoystick VirtualJoystick { get; private set; }
	public GripOMeter GripOMeter { get; private set; }
	public WindSimulator WindSimulator { get; private set; }

    public const int TimerPeriodInMilliseconds = 17;
	public const int TimerTicksPerSecond = 1000 / TimerPeriodInMilliseconds;

	private const string MutexName = "MarvinsAIRARefactoredMutex";

	private static Mutex? _mutex = null;

	private readonly AutoResetEvent _autoResetEvent = new( false );

	private readonly Thread _workerThread = new( WorkerThread ) { IsBackground = true, Priority = ThreadPriority.Normal };

	private bool _running = true;

	private readonly Timer _timer = new( TimerPeriodInMilliseconds );

	private int _tickMutex = 0;

	App()
	{
		Instance = this;

		Logger = new();
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
		LFE = new();
		MultimediaTimer = new();
		Simulator = new();
		RecordingManager = new();
		SteeringEffects = new();
		VirtualJoystick = new();
		GripOMeter = new();
		WindSimulator = new();	

        _timer.Elapsed += OnTimer;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void TriggerWorkerThread()
	{
		_autoResetEvent.Set();
	}

	private async void App_Startup( object sender, StartupEventArgs e )
	{
		Logger.WriteLine( "[App] App_Startup >>>" );

		_mutex = new Mutex( true, MutexName, out var createdNew );

		if ( !createdNew )
		{
			Logger.WriteLine( "[App] Another instance of this app is already running!" );

			Misc.BringExistingInstanceToFront();

			Shutdown();
		}
		else
		{
			Misc.DisableThrottling();

			if ( !Directory.Exists( DocumentsFolder ) )
			{
				Directory.CreateDirectory( DocumentsFolder );
			}

			Logger.Initialize();
			SettingsFile.Initialize();
			AdminBoxx.Initialize();
			AudioManager.Initialize();
			Simulator.Initialize();
			DirectInput.Initialize();

#if !ADMINBOXX

			CloudService.Initialize();
			Graph.Initialize();
			Pedals.Initialize();
			RacingWheel.Initialize();
			Sounds.Initialize();
			LFE.Initialize();
			MultimediaTimer.Initialize();
			RecordingManager.Initialize();
			VirtualJoystick.Initialize();
			GripOMeter.Initialize();

#endif

			DirectInput.OnInput += OnInput;

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

		Logger.WriteLine( "[App] <<< App_Startup" );
	}

	private void App_Exit( object sender, EventArgs e )
	{
		Logger.WriteLine( "[App] App_Exit >>>" );

		_timer.Stop();

		_running = false;

		_autoResetEvent.Set();

		GripOMeter.Close();

		Simulator.Shutdown();
		MultimediaTimer.Shutdown();
		AdminBoxx.Shutdown();
		LFE.Shutdown();
		DirectInput.Shutdown();
		Logger.Shutdown();

		Logger.WriteLine( "[App] <<< App_Exit" );
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

			// racing wheel auto margin knob

			if ( CheckMappedButtons( settings.RacingWheelAutoMarginPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelAutoMargin += 1f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "AutoMargin", settings.RacingWheelAutoMarginString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelAutoMarginMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelAutoMargin -= 1f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "AutoMargin", settings.RacingWheelAutoMarginString );
				}
			}

			// racing wheel auto button

			if ( CheckMappedButtons( settings.RacingWheelSetButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.AutoSetMaxForce = true;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					var autoTorqueString = $"{RacingWheel.GetCurrentAutoTorque():F1}{localization[ "TorqueUnits" ]}";

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
				settings.RacingWheelDeltaLimit += 0.01f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "DeltaLimit", settings.RacingWheelDeltaLimitString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelDeltaLimitMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDeltaLimit -= 0.01f;

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

			// racing wheel crash protection g force knob

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionGForcePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionGForce += 0.5f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "GForce", settings.RacingWheelCrashProtectionGForceString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionGForceMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionGForce -= 0.5f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "GForce", settings.RacingWheelCrashProtectionGForceString );
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
					RacingWheel.SendChatMessage( "ParkedStrength", settings.RacingWheelParkedStrengthString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelParkedStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelParkedStrength -= 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ParkedStrength", settings.RacingWheelParkedStrengthString );
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

			// racing wheel parked wheel centering strength knob

			if ( CheckMappedButtons( settings.RacingWheelParkedWheelCenteringStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelParkedWheelCenteringStrength += 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ParkedWheelCenteringStrength", settings.RacingWheelParkedWheelCenteringStrengthString );
				}
			}

			if ( CheckMappedButtons( settings.RacingWheelParkedWheelCenteringStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelParkedWheelCenteringStrength -= 0.05f;

				if ( settings.RacingWheelInputMappedSettingUpdateEnabled )
				{
					RacingWheel.SendChatMessage( "ParkedWheelCenteringStrength", settings.RacingWheelParkedWheelCenteringStrengthString );
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

			// steering effects understeer strength

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerStrength += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerStrength -= 0.05f;
			}

			// steering effects understeer warning threshold

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWarningThresholdPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWarningThreshold += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWarningThresholdMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWarningThreshold -= 0.05f;
			}

			// steering effects understeer threshold

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerThresholdPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerThreshold += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerThresholdMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerThreshold -= 0.05f;
			}

			// steering effects understeer curve

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerCurve += 0.05f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerCurve -= 0.05f;
			}

			// steering effects understeer warning frequency

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWarningFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWarningFrequency += 1f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerWarningFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerWarningFrequency -= 1f;
			}

			// steering effects understeer frequency

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerFrequency += 1f;
			}

			if ( CheckMappedButtons( settings.SteeringEffectsUndersteerFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SteeringEffectsUndersteerFrequency -= 1f;
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

			// sounds master volume

			if ( CheckMappedButtons( settings.SoundsMasterVolumePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsMasterVolume += 0.01f;
			}

			if ( CheckMappedButtons( settings.SoundsMasterVolumeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.SoundsMasterVolume -= 0.01f;
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

			// debug reset recording

			if ( CheckMappedButtons( settings.DebugResetRecordingMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RecordingManager.ResetRecording();
			}

			// debug save recording

			if ( CheckMappedButtons( settings.DebugSaveRecordingMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RecordingManager.SaveRecording();
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
					if ( mappedButton.HoldButton.DeviceInstanceGuid == Guid.Empty )
					{
						return true;
					}
					else
					{
						if ( DirectInput.IsButtonDown( deviceInstanceGuid, mappedButton.HoldButton.ButtonNumber ) )
						{
							return true;
						}
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
		var app = Instance;

		if ( app != null )
		{
			while ( app._running )
			{
				app._autoResetEvent.WaitOne();

				if ( Interlocked.Exchange( ref app._tickMutex, 1 ) == 0 )
				{
					app.Dispatcher.BeginInvoke( () =>
					{
						app.RacingWheel.Tick( app );
						app.SettingsFile.Tick( app );
						app.Pedals.Tick( app );
						app.AdminBoxx.Tick( app );
						app.Debug.Tick( app );
						app.ChatQueue.Tick( app );
						app.MainWindow.Tick( app );
						app.MultimediaTimer.Tick( app );
						app.Simulator.Tick( app );
						app.Sounds.Tick( app );
						app.Graph.Tick( app );
						app.SteeringEffects.Tick( app );
						app.VirtualJoystick.Tick( app );
						app.GripOMeter.Tick( app );

						app._tickMutex = 0;
					} );
				}
			}
		}
	}
}
