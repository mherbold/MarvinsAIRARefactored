
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
	public const string APP_FOLDER_NAME = "MarvinsAIRA Refactored";

	public static string DocumentsFolder { get; } = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ), APP_FOLDER_NAME );

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
	public DirectInput DirectInput { get; private set; }
	public LFE LFE { get; private set; }
	public MultimediaTimer MultimediaTimer { get; private set; }
	public Simulator Simulator { get; private set; }
	public RecordingManager RecordingManager { get; private set; }

	public const int TimerPeriodInMilliseconds = 17;
	public const int TimerTicksPerSecond = 1000 / TimerPeriodInMilliseconds;

	private const string MutexName = "MarvinsAIRARefactoredMutex";

	private static Mutex? _mutex = null;

	private readonly AutoResetEvent _autoResetEvent = new( false );

	private readonly Thread _workerThread = new( WorkerThread ) { IsBackground = true, Priority = ThreadPriority.Normal };

	private bool _running = true;

	private readonly Timer _timer = new( TimerPeriodInMilliseconds );

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
		DirectInput = new();
		LFE = new();
		MultimediaTimer = new();
		Simulator = new();
		RecordingManager = new();

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
			CloudService.Initialize();
			SettingsFile.Initialize();
			Graph.Initialize();
			Pedals.Initialize();
			AdminBoxx.Initialize();
			RacingWheel.Initialize();
			AudioManager.Initialize();
			DirectInput.Initialize();
			LFE.Initialize();
			MultimediaTimer.Initialize();
			Simulator.Initialize();
			RecordingManager.Initialize();

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

			if ( DataContext.DataContext.Instance.Settings.AppCheckForUpdates )
			{
				await CloudService.CheckForUpdates( false );
			}

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

			// racing wheel power button

			if ( CheckMappedButtons( settings.RacingWheelEnableForceFeedbackButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelEnableForceFeedback = !settings.RacingWheelEnableForceFeedback;
			}

			// racing wheel test button

			if ( CheckMappedButtons( settings.RacingWheelTestButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.PlayTestSignal = true;
			}

			// racing wheel reset button

			if ( CheckMappedButtons( settings.RacingWheelResetButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.ResetForceFeedback = true;
			}
			
			// racing wheel strength knob

			if ( CheckMappedButtons( settings.RacingWheelStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelStrength += 0.01f;
			}

			if ( CheckMappedButtons( settings.RacingWheelStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelStrength -= 0.01f;
			}

			// racing wheel max force knob

			if ( CheckMappedButtons( settings.RacingWheelMaxForcePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMaxForce += 1f;
			}

			if ( CheckMappedButtons( settings.RacingWheelMaxForceMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelMaxForce -= 1f;
			}

			// racing wheel auto margin knob

			if ( CheckMappedButtons( settings.RacingWheelAutoMarginPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelAutoMargin += 1f;
			}

			if ( CheckMappedButtons( settings.RacingWheelAutoMarginMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelAutoMargin -= 1f;
			}

			// racing wheel auto button

			if ( CheckMappedButtons( settings.RacingWheelSetButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.AutoSetMaxForce = true;
			}

			// racing wheel clear button

			if ( CheckMappedButtons( settings.RacingWheelClearButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.ClearPeakTorque = true;
			}

			// racing wheel detail boost knob

			if ( CheckMappedButtons( settings.RacingWheelDetailBoostPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDetailBoost += 0.1f;
			}

			if ( CheckMappedButtons( settings.RacingWheelDetailBoostMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDetailBoost -= 0.1f;
			}

			// racing wheel delta limit knob

			if ( CheckMappedButtons( settings.RacingWheelDeltaLimitPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDeltaLimit += 0.01f;
			}

			if ( CheckMappedButtons( settings.RacingWheelDeltaLimitMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDeltaLimit -= 0.01f;
			}

			// racing wheel detail boost bias knob

			if ( CheckMappedButtons( settings.RacingWheelDetailBoostBiasPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDetailBoostBias += 0.01f;
			}

			if ( CheckMappedButtons( settings.RacingWheelDetailBoostBiasMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDetailBoostBias -= 0.01f;
			}

			// racing wheel delta limiter bias knob

			if ( CheckMappedButtons( settings.RacingWheelDeltaLimiterBiasPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDeltaLimiterBias += 0.01f;
			}

			if ( CheckMappedButtons( settings.RacingWheelDeltaLimiterBiasMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelDeltaLimiterBias -= 0.01f;
			}

			// racing wheel slew compression threshold knob

			if ( CheckMappedButtons( settings.RacingWheelSlewCompressionThresholdPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelSlewCompressionThreshold += 1f;
			}

			if ( CheckMappedButtons( settings.RacingWheelSlewCompressionThresholdMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelSlewCompressionThreshold -= 1f;
			}

			// racing wheel slew compression rate knob

			if ( CheckMappedButtons( settings.RacingWheelSlewCompressionRatePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelSlewCompressionRate += 0.01f;
			}

			if ( CheckMappedButtons( settings.RacingWheelSlewCompressionRateMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelSlewCompressionRate -= 0.01f;
			}

			// racing wheel total compression threshold knob

			if ( CheckMappedButtons( settings.RacingWheelTotalCompressionThresholdPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelTotalCompressionThreshold += 0.01f;
			}

			if ( CheckMappedButtons( settings.RacingWheelTotalCompressionThresholdMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelTotalCompressionThreshold -= 0.01f;
			}

			// racing wheel total compression rate knob

			if ( CheckMappedButtons( settings.RacingWheelTotalCompressionRatePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelTotalCompressionRate += 0.01f;
			}

			if ( CheckMappedButtons( settings.RacingWheelTotalCompressionRateMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelTotalCompressionRate -= 0.01f;
			}

			// racing wheel output minimum knob

			if ( CheckMappedButtons( settings.RacingWheelOutputMinimumPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelOutputMinimum += 0.01f;
			}

			if ( CheckMappedButtons( settings.RacingWheelOutputMinimumMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelOutputMinimum -= 0.01f;
			}

			// racing wheel output maximum knob

			if ( CheckMappedButtons( settings.RacingWheelOutputMaximumPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelOutputMaximum += 0.01f;
			}

			if ( CheckMappedButtons( settings.RacingWheelOutputMaximumMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelOutputMaximum -= 0.01f;
			}

			// racing wheel output curve knob

			if ( CheckMappedButtons( settings.RacingWheelOutputCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelOutputCurve += 0.01f;
			}

			if ( CheckMappedButtons( settings.RacingWheelOutputCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelOutputCurve -= 0.01f;
			}

			// racing wheel lfe strength knob

			if ( CheckMappedButtons( settings.RacingWheelLFEStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelLFEStrength += 0.01f;
			}

			if ( CheckMappedButtons( settings.RacingWheelLFEStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelLFEStrength -= 0.01f;
			}

			// racing wheel crash protection g force knob

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionGForcePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionGForce += 0.5f;
			}

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionGForceMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionGForce -= 0.5f;
			}

			// racing wheel crash protection duration knob

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionDurationPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionDuration += 0.5f;
			}

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionDurationMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionDuration -= 0.5f;
			}

			// racing wheel crash protection force reduction knob

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionForceReductionPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionForceReduction += 0.05f;
			}

			if ( CheckMappedButtons( settings.RacingWheelCrashProtectionForceReductionMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCrashProtectionForceReduction -= 0.05f;
			}

			// racing wheel curb protection shock velocity knob

			if ( CheckMappedButtons( settings.RacingWheelCurbProtectionShockVelocityPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCurbProtectionShockVelocity += 0.1f;
			}

			if ( CheckMappedButtons( settings.RacingWheelCurbProtectionShockVelocityMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCurbProtectionShockVelocity -= 0.1f;
			}

			// racing wheel curb protection duration knob

			if ( CheckMappedButtons( settings.RacingWheelCurbProtectionDurationPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCurbProtectionDuration += 0.1f;
			}

			if ( CheckMappedButtons( settings.RacingWheelCurbProtectionDurationMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCurbProtectionDuration -= 0.1f;
			}

			// racing wheel curb protection force reduction knob

			if ( CheckMappedButtons( settings.RacingWheelCurbProtectionForceReductionPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCurbProtectionForceReduction += 0.05f;
			}

			if ( CheckMappedButtons( settings.RacingWheelCurbProtectionForceReductionMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelCurbProtectionForceReduction -= 0.05f;
			}

			// racing wheel parked strength knob

			if ( CheckMappedButtons( settings.RacingWheelParkedStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelParkedStrength += 0.05f;
			}

			if ( CheckMappedButtons( settings.RacingWheelParkedStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelParkedStrength -= 0.05f;
			}

			// racing wheel soft lock knob

			if ( CheckMappedButtons( settings.RacingWheelSoftLockStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelSoftLockStrength += 0.05f;
			}

			if ( CheckMappedButtons( settings.RacingWheelSoftLockStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelSoftLockStrength -= 0.05f;
			}

			// racing wheel friction knob

			if ( CheckMappedButtons( settings.RacingWheelFrictionPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelFriction += 0.05f;
			}

			if ( CheckMappedButtons( settings.RacingWheelFrictionMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.RacingWheelFriction -= 0.05f;
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

			// adminboxx brightness knob

			if ( CheckMappedButtons( settings.AdminBoxxBrightnessPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.AdminBoxxBrightness += 0.01f;
			}

			if ( CheckMappedButtons( settings.AdminBoxxBrightnessMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.AdminBoxxBrightness -= 0.01f;
			}

			// adminboxx black flag r

			if ( CheckMappedButtons( settings.AdminBoxxBlackFlagGPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.AdminBoxxBlackFlagR += 0.01f;
			}

			if ( CheckMappedButtons( settings.AdminBoxxBlackFlagRMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.AdminBoxxBlackFlagR -= 0.01f;
			}

			// adminboxx black flag g

			if ( CheckMappedButtons( settings.AdminBoxxBlackFlagGPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.AdminBoxxBlackFlagG += 0.01f;
			}

			if ( CheckMappedButtons( settings.AdminBoxxBlackFlagGMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.AdminBoxxBlackFlagG -= 0.01f;
			}

			// adminboxx black flag b

			if ( CheckMappedButtons( settings.AdminBoxxBlackFlagGPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.AdminBoxxBlackFlagB += 0.01f;
			}

			if ( CheckMappedButtons( settings.AdminBoxxBlackFlagBMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				settings.AdminBoxxBlackFlagB -= 0.01f;
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
					app.Graph.Tick( app );
				} );
			}
		}
	}
}
