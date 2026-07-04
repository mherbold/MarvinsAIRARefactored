
using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public class GTensioner
{
	public enum AxisMode
	{
		Disabled,
		Normal,
		Inverted
	}

	public enum TestAxis
	{
		None,
		Surge,
		Sway,
		Heave,
		CalibrationSweep
	}

	public enum TestVibrationEffect
	{
		None,
		ABS,
		WheelSlip,
		Rumble
	}

	// 40-sample suspension-like test signal at 20 Hz (2 seconds).
	// Composed of three sinusoids:
	//   1.2 G @ 0.8 Hz  – slow road undulation
	//   0.6 G @ 3.0 Hz  – suspension resonance (phase +π/3)
	//   0.2 G @ 7.5 Hz  – road texture / wheel hop (phase +π/6)
	private static readonly float[] TestSignalG = GenerateTestSignal();

	private static float[] GenerateTestSignal()
	{
		const int steps = 40;
		const float dt = 1f / 20f;

		var signal = new float[ steps ];

		for ( var i = 0; i < steps; i++ )
		{
			var t = i * dt;

			signal[ i ] =
				1.2f * MathF.Sin( 2f * MathF.PI * 0.8f * t ) +
				0.6f * MathF.Sin( 2f * MathF.PI * 3.0f * t + MathF.PI / 3f ) +
				0.2f * MathF.Sin( 2f * MathF.PI * 7.5f * t + MathF.PI / 6f );
		}

		return signal;
	}

	// Telemetry → SBT update rate: 60fps source / 3 = 20 updates per second
	private const int UpdateInterval = 3;

	// SBT sleeps after 1s with no S packet - force a resend every 0.5s (10 updates × 3 ticks @ 60fps)
	private const int ForceSendInterval = 10;

	public bool IsConnected { get; private set; } = false;

	public bool IsTestRunning => _testAxis != TestAxis.None || _vibrationTestEffect != TestVibrationEffect.None;

	private readonly UsbSerialPortHelper _usbSerialPortHelper = new( "MAIRA SBT" );

	private readonly GTensionerGraph _surgeGraph = new();
	private readonly GTensionerGraph _swayGraph = new();
	private readonly GTensionerGraph _heaveGraph = new();

	private readonly GTensionerGraph _leftShoulderGraph = new();
	private readonly GTensionerGraph _rightShoulderGraph = new();

	private int _updateCounter = UpdateInterval + 2;
	private int _lastSentLeftTenths = -1;
	private int _lastSentRightTenths = -1;
	private int _forceSendCounter = 0;

	// Last-sent vibration effect state (for change detection — only send E when changed)
	private int _lastSentLeftEffectFreqHz = -1;
	private int _lastSentLeftEffectAmplitudeDeg = -1;
	private int _lastSentRightEffectFreqHz = -1;
	private int _lastSentRightEffectAmplitudeDeg = -1;

	private float _longAccelSum = 0f;
	private float _latAccelSum = 0f;
	private float _vertAccelSum = 0f;
	private float _pitchSum = 0f;
	private float _rollSum = 0f;
	private int _sampleCount = 0;

	// Per-axis EMA smoothing state
	private float _surgeSmoothed = 0f;
	private float _swaySmoothed = 0f;
	private float _heaveSmoothed = 0f;

	// Per-axis min/max G tracking (in G units, before normalization)
	private float _surgeMinG = float.MaxValue;
	private float _surgeMaxG = float.MinValue;
	private float _swayMinG = float.MaxValue;
	private float _swayMaxG = float.MinValue;
	private float _heaveMinG = float.MaxValue;
	private float _heaveMaxG = float.MinValue;

	private bool _wasOnTrack = false;

	// --- Auto-tune ---

	private const float AutoTuneFloorG = 1f;                       // learned target never below 1 G (also the SoP floor, in raw signal units)
	private const float AutoTuneDrainPerUpdate = 1f / 300f / 20f;  // seen peaks drain 1 G per 300 seconds at 20 Hz updates
	private const float AutoTuneApproachAlpha = 0.14f;             // ~95% convergence in 1 second at 20 Hz
	private const float AutoTuneMinSpeed = 8.9408f;                // 20 mph in m/s - below this, learning is frozen
	private const int AutoTuneWriteInterval = 200;                 // settings write-back every 10 seconds at 20 Hz
	private const float AutoTuneMinWeight = 0.001f;                // divide-by-zero guard for the balance weights
	private const float AutoTuneWriteEpsilon = 0.01f;              // minimum change worth persisting
	private const float AutoTuneReseedEpsilon = 0.005f;            // external settings change (context switch) detection

	private bool _autoTuneSeeded = false;
	private bool _autoTuneWasEnabled = false;
	private bool _autoTuneGateWasActive = false;

	private float _autoTuneSurgePeakG = AutoTuneFloorG;
	private float _autoTuneSwayPeakG = AutoTuneFloorG;
	private float _autoTuneHeavePeakG = AutoTuneFloorG;
	private float _autoTuneSopPeak = AutoTuneFloorG;

	private float _autoTuneSurgeEffectiveG = AutoTuneFloorG;
	private float _autoTuneSwayEffectiveG = AutoTuneFloorG;
	private float _autoTuneHeaveEffectiveG = AutoTuneFloorG;
	private float _autoTuneSopEffectiveScale = AutoTuneFloorG;

	private float _lastWrittenSurgeMaxG = 0f;
	private float _lastWrittenSwayMaxG = 0f;
	private float _lastWrittenHeaveMaxG = 0f;
	private float _lastWrittenSopScale = 0f;

	private int _autoTuneWriteCounter = 0;

	private SteeringEffects.SeatOfPantsAlgorithm _autoTuneSopAlgorithm = SteeringEffects.SeatOfPantsAlgorithm.YAcceleration;

	// Test sweep state
	private TestAxis _testAxis = TestAxis.None;
	private int _testStep = 0;

	// Vibration effect test state
	private TestVibrationEffect _vibrationTestEffect = TestVibrationEffect.None;
	private int _vibrationTestStep = 0;

	// Calibration sweep state
	private bool _calibrationSweepGoingUp = true;
	private int _calibrationSweepLeftPos = 0;
	private int _calibrationSweepRightPos = 0;

	public GTensioner()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GTensioner] Constructor >>>" );

		_usbSerialPortHelper.PortClosed += OnPortClosed;

		app.Logger.WriteLine( "[GTensioner] <<< Constructor" );
	}

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GTensioner] Initialize >>>" );

		_usbSerialPortHelper.Initialize();

		var sbtPage = MainWindow._gTensionerPage;

		_surgeGraph.Initialize( sbtPage.SurgeGraph_Image, 1f, 0.08f, 0.58f );
		_swayGraph.Initialize( sbtPage.SwayGraph_Image, 0.5f, 1f, 0f );
		_heaveGraph.Initialize( sbtPage.HeaveGraph_Image, 0.3f, 0.7f, 1f );

		_leftShoulderGraph.Initialize( sbtPage.LeftShoulderGraph_Image, 0f, 0f, 1f );
		_rightShoulderGraph.Initialize( sbtPage.RightShoulderGraph_Image, 1f, 0f, 0f );

		if ( !_usbSerialPortHelper.DeviceFound )
		{
			app.Logger.WriteLine( "[GTensioner] Device not found - disabling GTensionerEnabled" );

			var localization = DataContext.DataContext.Instance.Localization;

			app.Dispatcher.Invoke( () =>
			{
				MainWindow._gTensionerPage.ConnectToGt_MairaSwitch.IsEnabled = false;
				MainWindow._gTensionerPage.ConnectToGt_MairaSwitch.ErrorMessage = localization[ "DeviceNotFound" ];
				MainWindow._gTensionerPage.RetryDevice_MairaButton.Visibility = System.Windows.Visibility.Visible;
			} );
		}

		app.Logger.WriteLine( "[GTensioner] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GTensioner] Shutdown >>>" );

		Disconnect();

		app.Logger.WriteLine( "[GTensioner] <<< Shutdown" );
	}

	public void RetryDevice()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GTensioner] RetryDevice >>>" );

		_usbSerialPortHelper.Initialize();

		app.Dispatcher.Invoke( () =>
		{
			if ( _usbSerialPortHelper.DeviceFound )
			{
				MainWindow._gTensionerPage.ConnectToGt_MairaSwitch.IsEnabled = true;
				MainWindow._gTensionerPage.ConnectToGt_MairaSwitch.ErrorMessage = string.Empty;
				MainWindow._gTensionerPage.RetryDevice_MairaButton.Visibility = System.Windows.Visibility.Collapsed;
			}
			else
			{
				MainWindow._gTensionerPage.ConnectToGt_MairaSwitch.ErrorMessage = _usbSerialPortHelper.LastErrorMessage;
			}
		} );

		app.Logger.WriteLine( "[GTensioner] <<< RetryDevice" );
	}

	public bool Connect()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GTensioner] Connect >>>" );

		IsConnected = _usbSerialPortHelper.Open();

		if ( IsConnected )
		{
			SendCalibration();
			SendMaxMovement();
			SendInvertedArms();
			SendVibrationEffect( 0, 0, 0, 0 );
		}

		app.Dispatcher.Invoke( () =>
		{
			MainWindow._gTensionerPage.ConnectToGt_MairaSwitch.IsOn = IsConnected;
			MainWindow._gTensionerPage.ConnectToGt_MairaSwitch.ErrorMessage = IsConnected ? string.Empty : _usbSerialPortHelper.LastErrorMessage;
		} );

		app.Logger.WriteLine( "[GTensioner] <<< Connect" );

		return IsConnected;
	}

	public void Disconnect()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GTensioner] Disconnect >>>" );

		// Persist any pending auto-tuned values before going quiet
		if ( _autoTuneSeeded )
		{
			WriteBackAutoTunedSettings( DataContext.DataContext.Instance.Settings );
		}

		IsConnected = false;

		_lastSentLeftTenths = -1;
		_lastSentRightTenths = -1;
		_forceSendCounter = 0;

		_lastSentLeftEffectFreqHz = -1;
		_lastSentLeftEffectAmplitudeDeg = -1;
		_lastSentRightEffectFreqHz = -1;
		_lastSentRightEffectAmplitudeDeg = -1;

		_usbSerialPortHelper.Close();

		app.Dispatcher.Invoke( () =>
		{
			MainWindow._gTensionerPage.ConnectToGt_MairaSwitch.ErrorMessage = string.Empty;
		} );

		app.Logger.WriteLine( "[GTensioner] <<< Disconnect" );
	}

	public void SendCalibration()
	{
		if ( !IsConnected )
		{
			return;
		}

		var settings = DataContext.DataContext.Instance.Settings;

		var neutralTenths = Math.Clamp( (int) Math.Round( settings.GTensionerNeutral * 10f ), 0, 1800 );
		var minimumTenths = Math.Clamp( (int) Math.Round( settings.GTensionerMinimum * 10f ), 0, 900 );
		var maximumTenths = Math.Clamp( (int) Math.Round( settings.GTensionerMaximum * 10f ), 900, 1800 );

		neutralTenths = Math.Clamp( neutralTenths, minimumTenths, maximumTenths );

		_usbSerialPortHelper.WriteLine( $"NL{neutralTenths:D4}R{neutralTenths:D4}" );
		_usbSerialPortHelper.WriteLine( $"AL{minimumTenths:D4}R{minimumTenths:D4}" );
		_usbSerialPortHelper.WriteLine( $"BL{maximumTenths:D4}R{maximumTenths:D4}" );
	}

	public void SendMaxMovement()
	{
		if ( !IsConnected )
		{
			return;
		}

		var settings = DataContext.DataContext.Instance.Settings;

		// Settings stores deg/sec; Nano expects tenths-of-a-degree/sec
		var maxMovementTenthsPerSec = Math.Clamp( (int) MathF.Round( settings.GTensionerMaxMotorSpeed * 10f ), 50, 5000 );

		_usbSerialPortHelper.WriteLine( $"ML{maxMovementTenthsPerSec:D4}R{maxMovementTenthsPerSec:D4}" );
	}

	public void SendInvertedArms()
	{
		if ( !IsConnected )
		{
			return;
		}

		var settings = DataContext.DataContext.Instance.Settings;

		var value = settings.GTensionerInvertedArms ? 1 : 0;

		_usbSerialPortHelper.WriteLine( $"IL{value:D4}R{value:D4}" );
	}

	public void SendVibrationEffect( int leftFreqHz, int leftAmplitudeDeg, int rightFreqHz, int rightAmplitudeDeg )
	{
		if ( !IsConnected )
		{
			return;
		}

		leftFreqHz = Math.Clamp( leftFreqHz, 0, 50 );
		leftAmplitudeDeg = Math.Clamp( leftAmplitudeDeg, 0, 60 );
		rightFreqHz = Math.Clamp( rightFreqHz, 0, 50 );
		rightAmplitudeDeg = Math.Clamp( rightAmplitudeDeg, 0, 60 );

		if ( leftFreqHz == _lastSentLeftEffectFreqHz
			&& leftAmplitudeDeg == _lastSentLeftEffectAmplitudeDeg
			&& rightFreqHz == _lastSentRightEffectFreqHz
			&& rightAmplitudeDeg == _lastSentRightEffectAmplitudeDeg )
		{
			return;
		}

		_lastSentLeftEffectFreqHz = leftFreqHz;
		_lastSentLeftEffectAmplitudeDeg = leftAmplitudeDeg;
		_lastSentRightEffectFreqHz = rightFreqHz;
		_lastSentRightEffectAmplitudeDeg = rightAmplitudeDeg;

		// Encode: first 2 digits = frequency, last 2 digits = amplitude
		var leftEncoded = leftFreqHz * 100 + leftAmplitudeDeg;
		var rightEncoded = rightFreqHz * 100 + rightAmplitudeDeg;

		_usbSerialPortHelper.WriteLine( $"EL{leftEncoded:D4}R{rightEncoded:D4}" );
	}

	private void SendSetPosition( int leftTargetPositionTenths, int rightTargetPositionTenths )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		var minimumTenths = Math.Clamp( (int) Math.Round( settings.GTensionerMinimum * 10f ), 0, 900 );
		var maximumTenths = Math.Clamp( (int) Math.Round( settings.GTensionerMaximum * 10f ), 900, 1800 );

		leftTargetPositionTenths = Math.Clamp( leftTargetPositionTenths, minimumTenths, maximumTenths );
		rightTargetPositionTenths = Math.Clamp( rightTargetPositionTenths, minimumTenths, maximumTenths );

		_forceSendCounter++;

		if ( _forceSendCounter < ForceSendInterval
			&& leftTargetPositionTenths == _lastSentLeftTenths
			&& rightTargetPositionTenths == _lastSentRightTenths )
		{
			return;
		}

		_forceSendCounter = 0;
		_lastSentLeftTenths = leftTargetPositionTenths;
		_lastSentRightTenths = rightTargetPositionTenths;

		_usbSerialPortHelper.WriteLine( $"SL{leftTargetPositionTenths:D4}R{rightTargetPositionTenths:D4}" );
	}

	private void Update( App app )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		// Handle test sweep (runs even when simulator is not connected)
		if ( _testAxis != TestAxis.None )
		{
			if ( _testAxis == TestAxis.CalibrationSweep )
			{
				UpdateCalibrationSweepTest( app, settings );
			}
			else
			{
				UpdateTest( app, settings );
			}
			return;
		}

		// Handle vibration effect test
		if ( _vibrationTestEffect != TestVibrationEffect.None )
		{
			UpdateVibrationTest( app, settings );
			return;
		}

		if ( !IsConnected || _sampleCount == 0 )
		{
			return;
		}

		var longAccelAvg = _longAccelSum / _sampleCount;
		var latAccelAvg = _latAccelSum / _sampleCount;
		var vertAccelAvg = _vertAccelSum / _sampleCount;

		var pitch = _pitchSum / _sampleCount;
		var roll = _rollSum / _sampleCount;

		_longAccelSum = 0f;
		_latAccelSum = 0f;
		_vertAccelSum = 0f;

		_pitchSum = 0f;
		_rollSum = 0f;

		_sampleCount = 0;

		// Get and sanitize settings
		var minimumTenths = Math.Clamp( (int) Math.Round( settings.GTensionerMinimum * 10f ), 0, 900 );
		var neutralTenths = Math.Clamp( (int) Math.Round( settings.GTensionerNeutral * 10f ), 0, 1800 );
		var maximumTenths = Math.Clamp( (int) Math.Round( settings.GTensionerMaximum * 10f ), 900, 1800 );

		// Compute gravity components in car body space using averaged pitch and roll.
		// iRacing includes gravity in the acceleration telemetry (specific force), so we subtract
		// the gravitational contribution to get the true inertial acceleration.
		// Pitch: positive = nose up. Roll: positive = left side up.
		var cosPitch = MathF.Cos( pitch );
		var sinPitch = MathF.Sin( pitch );
		var cosRoll = MathF.Cos( roll );
		var sinRoll = MathF.Sin( roll );

		// Gravity component along each body axis (what iRacing adds to raw acceleration)
		var gravLong = MathZ.OneG * -sinPitch;
		var gravLat = MathZ.OneG * cosPitch * sinRoll;
		var gravVert = MathZ.OneG * cosPitch * cosRoll;

		var longAccel = settings.GTensionerSurgeSubtractGravity ? longAccelAvg - gravLong : longAccelAvg;
		var latAccel = settings.GTensionerSwaySubtractGravity ? latAccelAvg - gravLat : latAccelAvg;
		var vertAccel = settings.GTensionerHeaveSubtractGravity ? vertAccelAvg - gravVert : vertAccelAvg;

		// Track min/max G values per axis
		var longG = longAccel / MathZ.OneG;
		var latG = latAccel / MathZ.OneG;
		var vertG = vertAccel / MathZ.OneG;

		if ( longG < _surgeMinG ) _surgeMinG = longG;
		if ( longG > _surgeMaxG ) _surgeMaxG = longG;
		if ( latG < _swayMinG ) _swayMinG = latG;
		if ( latG > _swayMaxG ) _swayMaxG = latG;
		if ( vertG < _heaveMinG ) _heaveMinG = vertG;
		if ( vertG > _heaveMaxG ) _heaveMaxG = vertG;

		// Update min/max G display strings
		MainWindow._gTensionerPage.SurgeMinGString = _surgeMinG < float.MaxValue ? $"{_surgeMinG:F2} G" : "---";
		MainWindow._gTensionerPage.SurgeMaxGString = _surgeMaxG > float.MinValue ? $"{_surgeMaxG:F2} G" : "---";
		MainWindow._gTensionerPage.SwayMinGString = _swayMinG < float.MaxValue ? $"{_swayMinG:F2} G" : "---";
		MainWindow._gTensionerPage.SwayMaxGString = _swayMaxG > float.MinValue ? $"{_swayMaxG:F2} G" : "---";
		MainWindow._gTensionerPage.HeaveMinGString = _heaveMinG < float.MaxValue ? $"{_heaveMinG:F2} G" : "---";
		MainWindow._gTensionerPage.HeaveMaxGString = _heaveMaxG > float.MinValue ? $"{_heaveMaxG:F2} G" : "---";

		// --- Auto-tune: learn the car's G envelope and adapt the per-axis max G scalings ---

		var autoTuneOn = settings.GTensionerAutoTuneEnabled;

		// On the enabled -> disabled transition, persist the last auto-tuned values so they become
		// the user's manual starting point
		if ( !autoTuneOn && _autoTuneWasEnabled && _autoTuneSeeded )
		{
			WriteBackAutoTunedSettings( settings );
		}

		_autoTuneWasEnabled = autoTuneOn;

		float surgeMaxG;
		float swayMaxG;
		float heaveMaxG;

		if ( autoTuneOn )
		{
			// Balance weights - heave is the remainder; renormalize defensively
			var swayWeight = settings.GTensionerAutoTuneSwayWeight;
			var surgeWeight = settings.GTensionerAutoTuneSurgeWeight;
			var heaveWeight = MathF.Max( 0f, 1f - swayWeight - surgeWeight );

			var weightSum = swayWeight + surgeWeight + heaveWeight;

			if ( weightSum > 0f )
			{
				swayWeight /= weightSum;
				surgeWeight /= weightSum;
				heaveWeight /= weightSum;
			}
			else
			{
				swayWeight = surgeWeight = heaveWeight = 1f / 3f;
			}

			// Seed on first enable, or reseed when the settings changed externally (car/track context switch)
			if ( !_autoTuneSeeded
				|| ( MathF.Abs( settings.GTensionerSurgeMaxG - _lastWrittenSurgeMaxG ) > AutoTuneReseedEpsilon )
				|| ( MathF.Abs( settings.GTensionerSwayMaxG - _lastWrittenSwayMaxG ) > AutoTuneReseedEpsilon )
				|| ( MathF.Abs( settings.GTensionerHeaveMaxG - _lastWrittenHeaveMaxG ) > AutoTuneReseedEpsilon )
				|| ( MathF.Abs( settings.GTensionerAutoTuneSeatOfPantsScale - _lastWrittenSopScale ) > AutoTuneReseedEpsilon ) )
			{
				SeedAutoTune( settings, swayWeight, surgeWeight, heaveWeight );
			}

			// Learning gate: only adapt while cleanly driving on the track surface
			var gateActive = app.Simulator.IsOnTrack
				&& ( app.Simulator.PlayerTrackSurface == IRSDKSharper.IRacingSdkEnum.TrkLoc.OnTrack )
				&& !app.RacingWheel.CrashProtectionIsActive
				&& !app.RacingWheel.CurbProtectionIsActive
				&& ( app.Simulator.Speed >= AutoTuneMinSpeed );

			if ( gateActive )
			{
				// Seen peaks drain slowly toward zero and raise instantly on new extremes
				_autoTuneSurgePeakG = MathF.Max( _autoTuneSurgePeakG - AutoTuneDrainPerUpdate, MathF.Abs( longG ) );
				_autoTuneSwayPeakG = MathF.Max( _autoTuneSwayPeakG - AutoTuneDrainPerUpdate, MathF.Abs( latG ) );
				_autoTuneHeavePeakG = MathF.Max( _autoTuneHeavePeakG - AutoTuneDrainPerUpdate, MathF.Abs( vertG ) );

				// The seat of pants raw signal changes units with the algorithm - restart its tracker on a change
				if ( settings.SteeringEffectsSeatOfPantsAlgorithm != _autoTuneSopAlgorithm )
				{
					_autoTuneSopAlgorithm = settings.SteeringEffectsSeatOfPantsAlgorithm;
					_autoTuneSopPeak = AutoTuneFloorG;
				}

				_autoTuneSopPeak = MathF.Max( _autoTuneSopPeak - AutoTuneDrainPerUpdate, MathF.Abs( app.SteeringEffects.SeatOfPantsRaw ) );
			}

			// Weight-scaled targets - center of the triangle (w = 1/3) applies the learned peak exactly;
			// dragging toward a tip strengthens that axis (smaller max G) and fades the others out
			var surgeTargetG = Math.Clamp( MathF.Max( AutoTuneFloorG, _autoTuneSurgePeakG ) / ( 3f * MathF.Max( surgeWeight, AutoTuneMinWeight ) ), 0.1f, 50f );
			var swayTargetG = Math.Clamp( MathF.Max( AutoTuneFloorG, _autoTuneSwayPeakG ) / ( 3f * MathF.Max( swayWeight, AutoTuneMinWeight ) ), 0.1f, 50f );
			var heaveTargetG = Math.Clamp( MathF.Max( AutoTuneFloorG, _autoTuneHeavePeakG ) / ( 3f * MathF.Max( heaveWeight, AutoTuneMinWeight ) ), 0.1f, 50f );

			// The seat of pants scale is coupled to the sway weight so both effects rise and fall together
			var sopTargetScale = Math.Clamp( MathF.Max( AutoTuneFloorG, _autoTuneSopPeak ) / ( 3f * MathF.Max( swayWeight, AutoTuneMinWeight ) ), 0.05f, 50f );

			// Smoothly approach the targets (about one second to arrive)
			_autoTuneSurgeEffectiveG += AutoTuneApproachAlpha * ( surgeTargetG - _autoTuneSurgeEffectiveG );
			_autoTuneSwayEffectiveG += AutoTuneApproachAlpha * ( swayTargetG - _autoTuneSwayEffectiveG );
			_autoTuneHeaveEffectiveG += AutoTuneApproachAlpha * ( heaveTargetG - _autoTuneHeaveEffectiveG );
			_autoTuneSopEffectiveScale += AutoTuneApproachAlpha * ( sopTargetScale - _autoTuneSopEffectiveScale );

			surgeMaxG = _autoTuneSurgeEffectiveG;
			swayMaxG = _autoTuneSwayEffectiveG;
			heaveMaxG = _autoTuneHeaveEffectiveG;

			// Persist to settings on a slow throttle (also refreshes the disabled knob displays)
			_autoTuneWriteCounter++;

			var gateFallingEdge = _autoTuneGateWasActive && !gateActive;

			if ( ( _autoTuneWriteCounter >= AutoTuneWriteInterval ) || gateFallingEdge )
			{
				_autoTuneWriteCounter = 0;

				WriteBackAutoTunedSettings( settings );
			}

			_autoTuneGateWasActive = gateActive;

			// Live readouts on the auto-tune section of the page
			MainWindow._gTensionerPage.AutoTuneSurgeEffectiveString = $"{_autoTuneSurgeEffectiveG:F2} G";
			MainWindow._gTensionerPage.AutoTuneSwayEffectiveString = $"{_autoTuneSwayEffectiveG:F2} G";
			MainWindow._gTensionerPage.AutoTuneHeaveEffectiveString = $"{_autoTuneHeaveEffectiveG:F2} G";
			MainWindow._gTensionerPage.AutoTuneSopEffectiveString = $"{_autoTuneSopEffectiveScale:F2}";
		}
		else
		{
			_autoTuneSeeded = false;
			_autoTuneGateWasActive = false;

			surgeMaxG = settings.GTensionerSurgeMaxG;
			swayMaxG = settings.GTensionerSwayMaxG;
			heaveMaxG = settings.GTensionerHeaveMaxG;

			MainWindow._gTensionerPage.AutoTuneSurgeEffectiveString = "---";
			MainWindow._gTensionerPage.AutoTuneSwayEffectiveString = "---";
			MainWindow._gTensionerPage.AutoTuneHeaveEffectiveString = "---";
			MainWindow._gTensionerPage.AutoTuneSopEffectiveString = "---";
		}

		// Surge normalized [-1..1]: acceleration tightens both belts, braking loosens both belts
		var surgeNormalized = Math.Clamp( longAccel / MathZ.OneG / surgeMaxG, -1f, 1f );

		// Sway normalized [-1..1]: positive biases right belt tighter, left belt looser
		var swayNormalized = Math.Clamp( -latAccel / MathZ.OneG / swayMaxG, -1f, 1f );

		// Heave normalized [-1..1]
		var heaveNormalized = Math.Clamp( vertAccel / MathZ.OneG / heaveMaxG, -1f, 1f );

		// Apply axis mode (disable / normal / inverted)
		surgeNormalized = ApplyAxisMode( surgeNormalized, settings.GTensionerSurgeMode );
		swayNormalized = ApplyAxisMode( swayNormalized, settings.GTensionerSwayMode );
		heaveNormalized = ApplyAxisMode( heaveNormalized, settings.GTensionerHeaveMode );

		// Apply dead zone per axis
		surgeNormalized = ApplyDeadZone( surgeNormalized, settings.GTensionerSurgeDeadZone );
		swayNormalized = ApplyDeadZone( swayNormalized, settings.GTensionerSwayDeadZone );
		heaveNormalized = ApplyDeadZone( heaveNormalized, settings.GTensionerHeaveDeadZone );

		// Apply curve per axis
		surgeNormalized = ApplyCurve( surgeNormalized, settings.GTensionerSurgeCurve );
		swayNormalized = ApplyCurve( swayNormalized, settings.GTensionerSwayCurve );
		heaveNormalized = ApplyCurve( heaveNormalized, settings.GTensionerHeaveCurve );

		// Apply EMA smoothing per axis (low-latency: alpha = 1 - smoothing)
		surgeNormalized = ApplySmoothing( surgeNormalized, ref _surgeSmoothed, settings.GTensionerSurgeSmoothing );
		swayNormalized = ApplySmoothing( swayNormalized, ref _swaySmoothed, settings.GTensionerSwaySmoothing );
		heaveNormalized = ApplySmoothing( heaveNormalized, ref _heaveSmoothed, settings.GTensionerHeaveSmoothing );

		// Update graphs if on the SBT page
		if ( MairaAppMenuPopup.CurrentAppPage == MainWindow.AppPage.GTensioner )
		{
			_surgeGraph.Advance( surgeNormalized );
			_swayGraph.Advance( swayNormalized );
			_heaveGraph.Advance( heaveNormalized );

			_surgeGraph.WritePixels();
			_swayGraph.WritePixels();
			_heaveGraph.WritePixels();
		}

		// Combine into per-arm normalized signal
		var leftCombinedNormalized = surgeNormalized + heaveNormalized - swayNormalized;
		var rightCombinedNormalized = surgeNormalized + heaveNormalized + swayNormalized;

		// Apply soft limiter
		var limitedLeftNormalized = MathZ.SoftLimiter( leftCombinedNormalized );
		var limitedRightNormalized = MathZ.SoftLimiter( rightCombinedNormalized );

		// Map to tenths of degrees and clamp to minimum / maximum
		int leftTargetPositionTenths;
		int rightTargetPositionTenths;

		if ( limitedLeftNormalized >= 0f )
		{
			leftTargetPositionTenths = Math.Clamp( (int) MathF.Round( limitedLeftNormalized * ( maximumTenths - neutralTenths ) + neutralTenths ), minimumTenths, maximumTenths );
		}
		else
		{
			leftTargetPositionTenths = Math.Clamp( (int) MathF.Round( limitedLeftNormalized * ( neutralTenths - minimumTenths ) + neutralTenths ), minimumTenths, maximumTenths );
		}

		if ( rightCombinedNormalized >= 0f )
		{
			rightTargetPositionTenths = Math.Clamp( (int) MathF.Round( limitedRightNormalized * ( maximumTenths - neutralTenths ) + neutralTenths ), minimumTenths, maximumTenths );
		}
		else
		{
			rightTargetPositionTenths = Math.Clamp( (int) MathF.Round( limitedRightNormalized * ( neutralTenths - minimumTenths ) + neutralTenths ), minimumTenths, maximumTenths );
		}

		// --- Seat of Pants effect (effect 1): tighten-only SoP offset added directly to positions ---
		if ( settings.GTensionerSeatOfPantsMode != AxisMode.Disabled )
		{
			float sop;
			float amplitudeTenths;

			if ( autoTuneOn )
			{
				// Internal auto-tuned path: normalize the raw (pre-threshold) signal by the learned scale,
				// shape it with the sway curve, and size the amplitude to the full sway belt travel so a
				// full-scale seat of pants offset cancels a full-scale sway contribution exactly
				var rawSop = App.Instance!.SteeringEffects.SeatOfPantsRaw;

				sop = Math.Clamp( rawSop / MathF.Max( _autoTuneSopEffectiveScale, 0.01f ), -1f, 1f );
				sop = ApplyAxisMode( sop, settings.GTensionerSeatOfPantsMode );
				sop = ApplyCurve( sop, settings.GTensionerSwayCurve );

				amplitudeTenths = maximumTenths - neutralTenths;
			}
			else
			{
				var rawSop = App.Instance!.SteeringEffects.SeatOfPantsEffect;

				// Apply mode (Normal or Inverted)
				sop = ApplyAxisMode( rawSop, settings.GTensionerSeatOfPantsMode );

				// Apply curve
				sop = ApplyCurve( sop, settings.GTensionerSeatOfPantsCurve );

				amplitudeTenths = settings.GTensionerSeatOfPantsAmplitude * 10f;
			}

			leftTargetPositionTenths = Math.Clamp( leftTargetPositionTenths + (int) MathF.Round( -sop * amplitudeTenths ), minimumTenths, maximumTenths );
			rightTargetPositionTenths = Math.Clamp( rightTargetPositionTenths + (int) MathF.Round( sop * amplitudeTenths ), minimumTenths, maximumTenths );
		}

		// Update shoulder graphs if on the SBT page
		if ( MairaAppMenuPopup.CurrentAppPage == MainWindow.AppPage.GTensioner )
		{
			// Remap tenths to [-1..1]: -1=minimum, 0=neutral, +1=maximum (piecewise linear)
			var leftShoulderNormalized = leftTargetPositionTenths <= neutralTenths ? (float) ( leftTargetPositionTenths - neutralTenths ) / ( neutralTenths - minimumTenths ) : (float) ( leftTargetPositionTenths - neutralTenths ) / ( maximumTenths - neutralTenths );
			var rightShoulderNormalized = rightTargetPositionTenths <= neutralTenths ? (float) ( rightTargetPositionTenths - neutralTenths ) / ( neutralTenths - minimumTenths ) : (float) ( rightTargetPositionTenths - neutralTenths ) / ( maximumTenths - neutralTenths );

			_leftShoulderGraph.Advance( leftShoulderNormalized );
			_rightShoulderGraph.Advance( rightShoulderNormalized );

			_leftShoulderGraph.WritePixels();
			_rightShoulderGraph.WritePixels();

			// Update overlay text for active vibration effects
			var absActive = settings.GTensionerABSEnabled && IsABSOrWheelLockActive( app );
			var wheelSlipActive = settings.GTensionerWheelSlipEnabled && IsWheelSpinActive( app );
			var rumbleLeftActive = settings.GTensionerRumbleEnabled && IsRumbleActiveLeft( app );
			var rumbleRightActive = settings.GTensionerRumbleEnabled && IsRumbleActiveRight( app );

			app.Dispatcher.InvokeAsync( () =>
			{
				var page = MainWindow._gTensionerPage;
				page.UpdateShoulderOverlays( absActive, wheelSlipActive, rumbleLeftActive, rumbleRightActive );
			} );
		}

		// --- Vibration effects 2/3/4: determine which effect is active (priority: ABS > WheelSlip > Rumble) ---
		var leftEffectFreqHz = 0;
		var leftEffectAmplitudeDeg = 0;
		var rightEffectFreqHz = 0;
		var rightEffectAmplitudeDeg = 0;

		if ( settings.GTensionerABSEnabled && IsABSOrWheelLockActive( app ) )
		{
			leftEffectFreqHz = rightEffectFreqHz = Math.Clamp( (int) MathF.Round( settings.GTensionerABSFrequency ), 0, 15 );
			leftEffectAmplitudeDeg = rightEffectAmplitudeDeg = Math.Clamp( (int) MathF.Round( settings.GTensionerABSAmplitude ), 0, 60 );
		}
		else if ( settings.GTensionerWheelSlipEnabled && IsWheelSpinActive( app ) )
		{
			leftEffectFreqHz = rightEffectFreqHz = Math.Clamp( (int) MathF.Round( settings.GTensionerWheelSlipFrequency ), 0, 15 );
			leftEffectAmplitudeDeg = rightEffectAmplitudeDeg = Math.Clamp( (int) MathF.Round( settings.GTensionerWheelSlipAmplitude ), 0, 60 );
		}
		else if ( settings.GTensionerRumbleEnabled )
		{
			var rumbleFreqHz = Math.Clamp( (int) MathF.Round( settings.GTensionerRumbleFrequency ), 0, 15 );
			var rumbleAmplitudeDeg = Math.Clamp( (int) MathF.Round( settings.GTensionerRumbleAmplitude ), 0, 60 );

			if ( IsRumbleActiveLeft( app ) )
			{
				leftEffectFreqHz = rumbleFreqHz;
				leftEffectAmplitudeDeg = rumbleAmplitudeDeg;
			}

			if ( IsRumbleActiveRight( app ) )
			{
				rightEffectFreqHz = rumbleFreqHz;
				rightEffectAmplitudeDeg = rumbleAmplitudeDeg;
			}
		}

		// Send effect state to Nano only when it changes
		SendVibrationEffect( leftEffectFreqHz, leftEffectAmplitudeDeg, rightEffectFreqHz, rightEffectAmplitudeDeg );

		// Send the new positions to the SBT
		SendSetPosition( leftTargetPositionTenths, rightTargetPositionTenths );
	}

	private static bool IsABSOrWheelLockActive( App app )
	{
		if ( app.Simulator.BrakeABSactive )
		{
			return true;
		}

		var sim = app.Simulator;

		if ( sim.CurrentRpmSpeedRatio > 0f && sim.Gear > 0 && sim.RPMSpeedRatios[ sim.Gear ] > 0f )
		{
			var difference = sim.CurrentRpmSpeedRatio - sim.RPMSpeedRatios[ sim.Gear ];
			var differencePct = ( difference / sim.RPMSpeedRatios[ sim.Gear ] ) - 0.05f;

			if ( differencePct > 0f )
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsWheelSpinActive( App app )
	{
		var sim = app.Simulator;

		if ( sim.CurrentRpmSpeedRatio > 0f && sim.Gear > 0 && sim.RPMSpeedRatios[ sim.Gear ] > 0f )
		{
			var difference = sim.RPMSpeedRatios[ sim.Gear ] - sim.CurrentRpmSpeedRatio;
			var differencePct = ( difference / sim.RPMSpeedRatios[ sim.Gear ] ) - 0.05f;

			if ( differencePct > 0f )
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsRumbleActiveLeft( App app )
	{
		var sim = app.Simulator;

		return sim.TireLF_RumblePitch > 0f || sim.TireLR_RumblePitch > 0f;
	}

	private static bool IsRumbleActiveRight( App app )
	{
		var sim = app.Simulator;

		return sim.TireRF_RumblePitch > 0f || sim.TireRR_RumblePitch > 0f;
	}

	private void UpdateTest( App app, DataContext.Settings settings )
	{
		// Auto-stop after one full pass through the signal
		if ( _testStep >= TestSignalG.Length )
		{
			_testAxis = TestAxis.None;
			_testStep = 0;

			UpdateTestStatusUI();
			return;
		}

		UpdateTestStatusUI();

		// Raw G value from the suspension signal for this step
		var rawG = TestSignalG[ _testStep ];

		_testStep++;

		if ( !IsConnected )
		{
			return;
		}

		var minimumTenths = Math.Clamp( (int) Math.Round( settings.GTensionerMinimum * 10f ), 0, 900 );
		var neutralTenths = Math.Clamp( (int) Math.Round( settings.GTensionerNeutral * 10f ), 0, 1800 );
		var maximumTenths = Math.Clamp( (int) Math.Round( settings.GTensionerMaximum * 10f ), 900, 1800 );

		// Normalize by each axis' MaxG setting, then apply axis mode
		var surgeNormalized = 0f;
		var swayNormalized = 0f;
		var heaveNormalized = 0f;

		switch ( _testAxis )
		{
			case TestAxis.Surge:
				surgeNormalized = ApplyAxisMode( Math.Clamp( -rawG / settings.GTensionerSurgeMaxG, -1f, 1f ), settings.GTensionerSurgeMode );
				break;
			case TestAxis.Sway:
				swayNormalized = ApplyAxisMode( Math.Clamp( rawG / settings.GTensionerSwayMaxG, -1f, 1f ), settings.GTensionerSwayMode );
				break;
			case TestAxis.Heave:
				heaveNormalized = ApplyAxisMode( Math.Clamp( rawG / settings.GTensionerHeaveMaxG, -1f, 1f ), settings.GTensionerHeaveMode );
				break;
		}

		var leftCombinedNormalized = surgeNormalized + heaveNormalized - swayNormalized;
		var rightCombinedNormalized = surgeNormalized + heaveNormalized + swayNormalized;

		var limitedLeftNormalized = MathZ.SoftLimiter( leftCombinedNormalized );
		var limitedRightNormalized = MathZ.SoftLimiter( rightCombinedNormalized );

		int leftTargetPositionTenths;
		int rightTargetPositionTenths;

		if ( limitedLeftNormalized >= 0f )
		{
			leftTargetPositionTenths = Math.Clamp( (int) MathF.Round( limitedLeftNormalized * ( maximumTenths - neutralTenths ) + neutralTenths ), minimumTenths, maximumTenths );
		}
		else
		{
			leftTargetPositionTenths = Math.Clamp( (int) MathF.Round( limitedLeftNormalized * ( neutralTenths - minimumTenths ) + neutralTenths ), minimumTenths, maximumTenths );
		}

		if ( rightCombinedNormalized >= 0f )
		{
			rightTargetPositionTenths = Math.Clamp( (int) MathF.Round( limitedRightNormalized * ( maximumTenths - neutralTenths ) + neutralTenths ), minimumTenths, maximumTenths );
		}
		else
		{
			rightTargetPositionTenths = Math.Clamp( (int) MathF.Round( limitedRightNormalized * ( neutralTenths - minimumTenths ) + neutralTenths ), minimumTenths, maximumTenths );
		}

		SendSetPosition( leftTargetPositionTenths, rightTargetPositionTenths );
	}

	private void UpdateTestStatusUI()
	{
		var app = App.Instance;

		app?.Dispatcher.InvokeAsync( () =>
		{
			MainWindow._gTensionerPage.UpdateTestStatus();
		} );
	}

	public void StartTest( TestAxis axis )
	{
		_testStep = 0;
		_testAxis = axis;

		UpdateTestStatusUI();
	}

	public void StopTest()
	{
		_testAxis = TestAxis.None;
		_testStep = 0;

		UpdateTestStatusUI();
	}

	public void StartCalibrationSweepTest()
	{
		var settings = DataContext.DataContext.Instance.Settings;

		var minimumTenths = Math.Clamp( (int) Math.Round( settings.GTensionerMinimum * 10f ), 0, 900 );

		_calibrationSweepGoingUp = true;
		_calibrationSweepLeftPos = minimumTenths;
		_calibrationSweepRightPos = minimumTenths;
		_testAxis = TestAxis.CalibrationSweep;
		_testStep = 0;

		UpdateTestStatusUI();
	}

	private void UpdateCalibrationSweepTest( App app, DataContext.Settings settings )
	{
		var minimumTenths = Math.Clamp( (int) Math.Round( settings.GTensionerMinimum * 10f ), 0, 900 );
		var maximumTenths = Math.Clamp( (int) Math.Round( settings.GTensionerMaximum * 10f ), 900, 1800 );

		// Step size per update (20 Hz): deg/sec → tenths/sec → tenths/update
		var stepTenths = Math.Max( 1, (int) MathF.Round( settings.GTensionerMaxMotorSpeed * 10f / 20f ) );

		if ( _calibrationSweepGoingUp )
		{
			_calibrationSweepLeftPos = Math.Min( _calibrationSweepLeftPos + stepTenths, maximumTenths );
			_calibrationSweepRightPos = Math.Min( _calibrationSweepRightPos + stepTenths, maximumTenths );

			if ( _calibrationSweepLeftPos >= maximumTenths && _calibrationSweepRightPos >= maximumTenths )
			{
				_calibrationSweepGoingUp = false;
			}
		}
		else
		{
			_calibrationSweepLeftPos = Math.Max( _calibrationSweepLeftPos - stepTenths, minimumTenths );
			_calibrationSweepRightPos = Math.Max( _calibrationSweepRightPos - stepTenths, minimumTenths );

			if ( _calibrationSweepLeftPos <= minimumTenths && _calibrationSweepRightPos <= minimumTenths )
			{
				_testAxis = TestAxis.None;
				_testStep = 0;

				UpdateTestStatusUI();
				return;
			}
		}

		UpdateTestStatusUI();

		if ( !IsConnected )
		{
			return;
		}

		SendSetPosition( _calibrationSweepLeftPos, _calibrationSweepRightPos );
	}

	public void StartVibrationTest( TestVibrationEffect effect )
	{
		_vibrationTestStep = 0;
		_vibrationTestEffect = effect;

		UpdateTestStatusUI();
	}

	public void StopVibrationTest()
	{
		_vibrationTestEffect = TestVibrationEffect.None;
		_vibrationTestStep = 0;

		UpdateTestStatusUI();
	}

	private (int freq, int amp) GetVibrationTestParams( DataContext.Settings settings )
	{
		return _vibrationTestEffect switch
		{
			TestVibrationEffect.ABS => ((int) MathF.Round( settings.GTensionerABSFrequency ), (int) MathF.Round( settings.GTensionerABSAmplitude )),
			TestVibrationEffect.WheelSlip => ((int) MathF.Round( settings.GTensionerWheelSlipFrequency ), (int) MathF.Round( settings.GTensionerWheelSlipAmplitude )),
			TestVibrationEffect.Rumble => ((int) MathF.Round( settings.GTensionerRumbleFrequency ), (int) MathF.Round( settings.GTensionerRumbleAmplitude )),
			_ => (0, 0)
		};
	}

	private void UpdateVibrationTest( App app, DataContext.Settings settings )
	{
		const int totalSteps = 40; // 2 seconds at 20 Hz

		if ( _vibrationTestStep >= totalSteps )
		{
			if ( IsConnected )
			{
				var minimumTenths = Math.Clamp( (int) Math.Round( settings.GTensionerMinimum * 10f ), 0, 900 );

				_lastSentLeftEffectFreqHz = -1;
				_lastSentLeftEffectAmplitudeDeg = -1;
				_lastSentRightEffectFreqHz = -1;
				_lastSentRightEffectAmplitudeDeg = -1;

				SendVibrationEffect( 0, 0, 0, 0 );

				_lastSentLeftTenths = -1;
				_lastSentRightTenths = -1;

				SendSetPosition( minimumTenths, minimumTenths );
			}

			_vibrationTestEffect = TestVibrationEffect.None;
			_vibrationTestStep = 0;

			UpdateTestStatusUI();
			return;
		}

		_vibrationTestStep++;

		if ( !IsConnected )
		{
			return;
		}

		var neutralTenths = Math.Clamp( (int) Math.Round( settings.GTensionerNeutral * 10f ), 0, 1800 );

		if ( _vibrationTestStep == 1 )
		{
			_lastSentLeftTenths = -1;
			_lastSentRightTenths = -1;

			SendSetPosition( neutralTenths, neutralTenths );

			_lastSentLeftEffectFreqHz = -1;
			_lastSentLeftEffectAmplitudeDeg = -1;
			_lastSentRightEffectFreqHz = -1;
			_lastSentRightEffectAmplitudeDeg = -1;

			var (freq, amp) = GetVibrationTestParams( settings );

			SendVibrationEffect( freq, amp, freq, amp );
		}
		else
		{
			SendSetPosition( neutralTenths, neutralTenths );
		}
	}

	private static float ApplyAxisMode( float value, AxisMode mode )
	{
		return mode switch
		{
			AxisMode.Disabled => 0f,
			AxisMode.Inverted => -value,
			_ => value
		};
	}

	private static float ApplyDeadZone( float value, float deadZone )
	{
		if ( deadZone <= 0f ) return value;

		var absValue = MathF.Abs( value );

		if ( absValue <= deadZone ) return 0f;

		return MathF.CopySign( ( absValue - deadZone ) / ( 1f - deadZone ), value );
	}

	private static float ApplyCurve( float value, float curve )
	{
		if ( curve == 0f ) return value;

		var power = MathZ.CurveToPower( curve );

		return MathF.CopySign( MathF.Pow( MathF.Abs( value ), power ), value );
	}

	private static float ApplySmoothing( float value, ref float smoothed, float smoothing )
	{
		if ( smoothing <= 0f )
		{
			smoothed = value;
			return value;
		}

		var alpha = 1f - smoothing;

		smoothed += alpha * ( value - smoothed );

		return smoothed;
	}

	// Initialize the auto-tune state from the persisted settings - on first enable and again whenever
	// the settings change underneath us (car/track context switch). The persisted values are the
	// weight-scaled effective values, so the underlying learned peaks are recovered by inverting
	// effective = peak / (3 x weight).
	private void SeedAutoTune( DataContext.Settings settings, float swayWeight, float surgeWeight, float heaveWeight )
	{
		_autoTuneSeeded = true;

		_autoTuneSurgeEffectiveG = settings.GTensionerSurgeMaxG;
		_autoTuneSwayEffectiveG = settings.GTensionerSwayMaxG;
		_autoTuneHeaveEffectiveG = settings.GTensionerHeaveMaxG;
		_autoTuneSopEffectiveScale = settings.GTensionerAutoTuneSeatOfPantsScale;

		_autoTuneSurgePeakG = MathF.Max( AutoTuneFloorG, _autoTuneSurgeEffectiveG * 3f * surgeWeight );
		_autoTuneSwayPeakG = MathF.Max( AutoTuneFloorG, _autoTuneSwayEffectiveG * 3f * swayWeight );
		_autoTuneHeavePeakG = MathF.Max( AutoTuneFloorG, _autoTuneHeaveEffectiveG * 3f * heaveWeight );
		_autoTuneSopPeak = MathF.Max( AutoTuneFloorG, _autoTuneSopEffectiveScale * 3f * swayWeight );

		_autoTuneSopAlgorithm = settings.SteeringEffectsSeatOfPantsAlgorithm;

		_lastWrittenSurgeMaxG = settings.GTensionerSurgeMaxG;
		_lastWrittenSwayMaxG = settings.GTensionerSwayMaxG;
		_lastWrittenHeaveMaxG = settings.GTensionerHeaveMaxG;
		_lastWrittenSopScale = settings.GTensionerAutoTuneSeatOfPantsScale;

		_autoTuneWriteCounter = 0;
	}

	// Persist the auto-tuned effective values into the settings (throttled by the caller). The setters
	// clamp, refresh the knob display strings, sync the per-context settings, and queue serialization.
	// Read each property back afterwards so our own (possibly clamped) writes are not mistaken for
	// external context switches by the reseed check.
	private void WriteBackAutoTunedSettings( DataContext.Settings settings )
	{
		if ( MathF.Abs( _autoTuneSurgeEffectiveG - _lastWrittenSurgeMaxG ) > AutoTuneWriteEpsilon )
		{
			settings.GTensionerSurgeMaxG = _autoTuneSurgeEffectiveG;

			_lastWrittenSurgeMaxG = settings.GTensionerSurgeMaxG;
		}

		if ( MathF.Abs( _autoTuneSwayEffectiveG - _lastWrittenSwayMaxG ) > AutoTuneWriteEpsilon )
		{
			settings.GTensionerSwayMaxG = _autoTuneSwayEffectiveG;

			_lastWrittenSwayMaxG = settings.GTensionerSwayMaxG;
		}

		if ( MathF.Abs( _autoTuneHeaveEffectiveG - _lastWrittenHeaveMaxG ) > AutoTuneWriteEpsilon )
		{
			settings.GTensionerHeaveMaxG = _autoTuneHeaveEffectiveG;

			_lastWrittenHeaveMaxG = settings.GTensionerHeaveMaxG;
		}

		if ( MathF.Abs( _autoTuneSopEffectiveScale - _lastWrittenSopScale ) > AutoTuneWriteEpsilon )
		{
			settings.GTensionerAutoTuneSeatOfPantsScale = _autoTuneSopEffectiveScale;

			_lastWrittenSopScale = settings.GTensionerAutoTuneSeatOfPantsScale;
		}
	}

	private void ResetMinMaxG()
	{
		_surgeMinG = float.MaxValue;
		_surgeMaxG = float.MinValue;
		_swayMinG = float.MaxValue;
		_swayMaxG = float.MinValue;
		_heaveMinG = float.MaxValue;
		_heaveMaxG = float.MinValue;

		MainWindow._gTensionerPage.SurgeMinGString = "---";
		MainWindow._gTensionerPage.SurgeMaxGString = "---";
		MainWindow._gTensionerPage.SwayMinGString = "---";
		MainWindow._gTensionerPage.SwayMaxGString = "---";
		MainWindow._gTensionerPage.HeaveMinGString = "---";
		MainWindow._gTensionerPage.HeaveMaxGString = "---";
	}

	private void OnPortClosed( object? sender, EventArgs e )
	{
		Disconnect();
	}

	public void Tick( App app )
	{
		// Detect rising edge of IsOnTrack to reset min/max G
		var isOnTrack = app.Simulator.IsOnTrack;

		if ( isOnTrack && !_wasOnTrack )
		{
			ResetMinMaxG();
		}

		_wasOnTrack = isOnTrack;

		if ( isOnTrack )
		{
			_longAccelSum += app.Simulator.LongAccel;
			_latAccelSum += app.Simulator.LatAccel;
			_vertAccelSum += app.Simulator.VertAccel;

			_pitchSum += app.Simulator.Pitch;
			_rollSum += app.Simulator.Roll;

			_sampleCount++;
		}

		_updateCounter--;

		if ( _updateCounter <= 0 )
		{
			_updateCounter = UpdateInterval;

			Update( app );
		}
	}
}
