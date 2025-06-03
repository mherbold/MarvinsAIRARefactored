
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

using MarvinsAIRARefactored.Windows;
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
	public Pedals Pedals { get; private set; }
	public SettingsFile SettingsFile { get; private set; }
	public AdminBoxx AdminBoxx { get; private set; }
	public Debug Debug { get; private set; }
	public new MainWindow MainWindow { get; private set; }
	public DirectInput DirectInput { get; private set; }
	public MultimediaTimer MultimediaTimer { get; private set; }
	public Simulator Simulator { get; private set; }

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
		Pedals = new();
		SettingsFile = new();
		AdminBoxx = new();
		Debug = new();
		MainWindow = new();
		DirectInput = new( MainWindow.Graphs_OutputTorque_Image );
		MultimediaTimer = new( MainWindow.Graphs_MultimediaTimerJitter_Image );
		Simulator = new( MainWindow.Graphs_Native60HzTorque_Image, MainWindow.Graphs_Native360HzTorque_Image );

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

		if ( !Directory.Exists( DocumentsFolder ) )
		{
			Directory.CreateDirectory( DocumentsFolder );
		}

		Logger.Initialize();
		Pedals.Initialize();
		SettingsFile.Initialize();
		DirectInput.Initialize();
		MultimediaTimer.Initialize();
		Simulator.Initialize();

		DirectInput.OnInput += OnInput;

		GC.Collect();

		MainWindow.Resources = Current.Resources;

		MainWindow.Show();
		MainWindow.Initialize();

		_workerThread.Start();

		_timer.Start();

		Simulator.Start();

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
		DirectInput.Shutdown();
		Logger.Shutdown();

		Logger.WriteLine( "[App] <<< App_Exit" );
	}

	private void OnInput( string deviceProductName, Guid deviceInstanceGuid, int buttonNumber, bool isPressed )
	{
		if ( !UpdateButtonMappingsWindow.WindowIsOpen && isPressed )
		{
			// racing wheel power button

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelPowerButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelEnableForceFeedback = !DataContext.Instance.Settings.RacingWheelEnableForceFeedback;
			}

			// racing wheel test button

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelTestButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.PlayTestSignal = true;
			}

			// racing wheel reset button
			
			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelResetButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.ResetForceFeedback = true;
			}

			// racing wheel max force knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelMaxForcePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelMaxForce += 1f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelMaxForceMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelMaxForce -= 1f;
			}

			// racing wheel auto margin knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelAutoMarginPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelAutoMargin += 1f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelAutoMarginMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelAutoMargin -= 1f;
			}

			// racing wheel auto button

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelAutoButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.AutoSetMaxForce = true;
			}

			// racing wheel clear button

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelClearButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				RacingWheel.ClearPeakTorque = true;
			}

			// racing wheel detail boost knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelDetailBoostPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelDetailBoost += 0.1f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelDetailBoostMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelDetailBoost -= 0.1f;
			}

			// racing wheel delta limit knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelDeltaLimitPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelDeltaLimit += 0.01f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelDeltaLimitMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelDeltaLimit -= 0.01f;
			}

			// racing wheel bias knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelBiasPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelBias += 0.01f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelBiasMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelBias -= 0.01f;
			}

			// racing wheel compression rate knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCompressionRatePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCompressionRate += 0.01f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCompressionRateMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCompressionRate -= 0.01f;
			}

			// racing wheel crash protection g force knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCrashProtectionGForcePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCrashProtectionGForce += 0.5f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCrashProtectionGForceMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCrashProtectionGForce -= 0.5f;
			}

			// racing wheel crash protection duration knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCrashProtectionDurationPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCrashProtectionDuration += 0.5f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCrashProtectionDurationMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCrashProtectionDuration -= 0.5f;
			}

			// racing wheel crash protection force reduction knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCrashProtectionForceReductionPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCrashProtectionForceReduction += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCrashProtectionForceReductionMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCrashProtectionForceReduction -= 0.05f;
			}

			// racing wheel curb protection shock velocity knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCurbProtectionShockVelocityPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCurbProtectionShockVelocity += 0.1f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCurbProtectionShockVelocityMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCurbProtectionShockVelocity -= 0.1f;
			}

			// racing wheel curb protection duration knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCurbProtectionDurationPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCurbProtectionDuration += 0.1f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCurbProtectionDurationMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCurbProtectionDuration -= 0.1f;
			}

			// racing wheel curb protection force reduction knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCurbProtectionForceReductionPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCurbProtectionForceReduction += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelCurbProtectionForceReductionMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelCurbProtectionForceReduction -= 0.05f;
			}

			// racing wheel parked strength knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelParkedStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelParkedStrength += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelParkedStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelParkedStrength -= 0.05f;
			}

			// racing wheel soft lock knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelSoftLockStrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelSoftLockStrength += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelSoftLockStrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelSoftLockStrength -= 0.05f;
			}

			// racing wheel friction knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelFrictionPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelFriction += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.RacingWheelFrictionMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.RacingWheelFriction -= 0.05f;
			}

			// pedals minimum frequency knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsMinimumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsMinimumFrequency += 1f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsMinimumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsMinimumFrequency -= 1f;
			}

			// pedals maximum frequency knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsMaximumFrequencyPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsMaximumFrequency += 1f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsMaximumFrequencyMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsMaximumFrequency -= 1f;
			}

			// pedals frequency curve knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsFrequencyCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsFrequencyCurve += 0.01f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsFrequencyCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsFrequencyCurve -= 0.01f;
			}

			// pedals minimum amplitude knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsMinimumAmplitudePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsMinimumAmplitude += 0.01f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsMinimumAmplitudeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsMinimumAmplitude -= 0.01f;
			}

			// pedals maximum amplitude knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsMaximumAmplitudePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsMaximumAmplitude += 0.01f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsMaximumAmplitudeMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsMaximumAmplitude -= 0.01f;
			}

			// pedals amplitude curve knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsAmplitudeCurvePlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsAmplitudeCurve += 0.01f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsAmplitudeCurveMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsAmplitudeCurve -= 0.01f;
			}

			// pedals clutch effect 1 strength knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsClutchEffect1StrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsClutchEffect1Strength += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsClutchEffect1StrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsClutchEffect1Strength -= 0.05f;
			}

			// pedals clutch effect 2 strength knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsClutchEffect2StrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsClutchEffect2Strength += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsClutchEffect2StrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsClutchEffect2Strength -= 0.05f;
			}

			// pedals clutch effect 3 strength knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsClutchEffect3StrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsClutchEffect3Strength += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsClutchEffect3StrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsClutchEffect3Strength -= 0.05f;
			}

			// pedals brake effect 1 strength knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsBrakeEffect1StrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsBrakeEffect1Strength += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsBrakeEffect1StrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsBrakeEffect1Strength -= 0.05f;
			}

			// pedals brake effect 2 strength knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsBrakeEffect2StrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsBrakeEffect2Strength += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsBrakeEffect2StrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsBrakeEffect2Strength -= 0.05f;
			}

			// pedals brake effect 3 strength knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsBrakeEffect3StrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsBrakeEffect3Strength += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsBrakeEffect3StrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsBrakeEffect3Strength -= 0.05f;
			}

			// pedals throttle effect 1 strength knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsThrottleEffect1StrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsThrottleEffect1Strength += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsThrottleEffect1StrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsThrottleEffect1Strength -= 0.05f;
			}

			// pedals throttle effect 2 strength knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsThrottleEffect2StrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsThrottleEffect2Strength += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsThrottleEffect2StrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsThrottleEffect2Strength -= 0.05f;
			}

			// pedals throttle effect 3 strength knob

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsThrottleEffect3StrengthPlusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsThrottleEffect3Strength += 0.05f;
			}

			if ( CheckMappedButtons( DataContext.Instance.Settings.PedalsThrottleEffect3StrengthMinusButtonMappings, deviceInstanceGuid, buttonNumber ) )
			{
				DataContext.Instance.Settings.PedalsThrottleEffect3Strength -= 0.05f;
			}
		}
	}

	private bool CheckMappedButtons( Settings.ButtonMappings buttonMappings, Guid deviceInstanceGuid, int buttonNumber )
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
					app.Pedals.Tick( app );
					app.RacingWheel.Tick( app );
					app.SettingsFile.Tick( app );
					app.AdminBoxx.Tick( app );
					app.Debug.Tick( app );
					app.MainWindow.Tick( app );
					app.DirectInput.Tick( app );
					app.MultimediaTimer.Tick( app );
					app.Simulator.Tick( app );
				} );
			}
		}
	}
}
