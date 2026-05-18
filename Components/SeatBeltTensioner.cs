
using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public class SeatBeltTensioner
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

	private readonly SeatBeltTensionerGraph _surgeGraph = new();
	private readonly SeatBeltTensionerGraph _swayGraph = new();
	private readonly SeatBeltTensionerGraph _heaveGraph = new();

	private readonly SeatBeltTensionerGraph _leftShoulderGraph = new();
	private readonly SeatBeltTensionerGraph _rightShoulderGraph = new();

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

	public SeatBeltTensioner()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SeatBeltTensioner] Constructor >>>" );

		_usbSerialPortHelper.PortClosed += OnPortClosed;

		app.Logger.WriteLine( "[SeatBeltTensioner] <<< Constructor" );
	}

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SeatBeltTensioner] Initialize >>>" );

		_usbSerialPortHelper.Initialize();

		var sbtPage = MainWindow._seatBeltTensionerPage;

		_surgeGraph.Initialize( sbtPage.SurgeGraph_Image, 1f, 0.08f, 0.58f );
		_swayGraph.Initialize( sbtPage.SwayGraph_Image, 0.5f, 1f, 0f );
		_heaveGraph.Initialize( sbtPage.HeaveGraph_Image, 0.3f, 0.7f, 1f );

		_leftShoulderGraph.Initialize( sbtPage.LeftShoulderGraph_Image, 0f, 0f, 1f );
		_rightShoulderGraph.Initialize( sbtPage.RightShoulderGraph_Image, 1f, 0f, 0f );

		if ( !_usbSerialPortHelper.DeviceFound )
		{
			app.Logger.WriteLine( "[SeatBeltTensioner] Device not found - disabling SeatBeltTensionerEnabled" );

			var localization = DataContext.DataContext.Instance.Localization;

			app.Dispatcher.Invoke( () =>
			{
				MainWindow._seatBeltTensionerPage.ConnectToSbt_MairaSwitch.IsEnabled = false;
				MainWindow._seatBeltTensionerPage.ConnectToSbt_MairaSwitch.ErrorMessage = localization[ "DeviceNotFound" ];
				MainWindow._seatBeltTensionerPage.RetryDevice_MairaButton.Visibility = System.Windows.Visibility.Visible;
			} );
		}

		app.Logger.WriteLine( "[SeatBeltTensioner] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SeatBeltTensioner] Shutdown >>>" );

		Disconnect();

		app.Logger.WriteLine( "[SeatBeltTensioner] <<< Shutdown" );
	}

	public void RetryDevice()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SeatBeltTensioner] RetryDevice >>>" );

		_usbSerialPortHelper.Initialize();

		app.Dispatcher.Invoke( () =>
		{
			if ( _usbSerialPortHelper.DeviceFound )
			{
				MainWindow._seatBeltTensionerPage.ConnectToSbt_MairaSwitch.IsEnabled = true;
				MainWindow._seatBeltTensionerPage.ConnectToSbt_MairaSwitch.ErrorMessage = string.Empty;
				MainWindow._seatBeltTensionerPage.RetryDevice_MairaButton.Visibility = System.Windows.Visibility.Collapsed;
			}
			else
			{
				MainWindow._seatBeltTensionerPage.ConnectToSbt_MairaSwitch.ErrorMessage = _usbSerialPortHelper.LastErrorMessage;
			}
		} );

		app.Logger.WriteLine( "[SeatBeltTensioner] <<< RetryDevice" );
	}

	public bool Connect()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SeatBeltTensioner] Connect >>>" );

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
			MainWindow._seatBeltTensionerPage.ConnectToSbt_MairaSwitch.IsOn = IsConnected;
			MainWindow._seatBeltTensionerPage.ConnectToSbt_MairaSwitch.ErrorMessage = IsConnected ? string.Empty : _usbSerialPortHelper.LastErrorMessage;
		} );

		app.Logger.WriteLine( "[SeatBeltTensioner] <<< Connect" );

		return IsConnected;
	}

	public void Disconnect()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SeatBeltTensioner] Disconnect >>>" );

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
			MainWindow._seatBeltTensionerPage.ConnectToSbt_MairaSwitch.ErrorMessage = string.Empty;
		} );

		app.Logger.WriteLine( "[SeatBeltTensioner] <<< Disconnect" );
	}

	public void SendCalibration()
	{
		if ( !IsConnected )
		{
			return;
		}

		var settings = DataContext.DataContext.Instance.Settings;

		var neutralTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerNeutral * 10f ), 0, 1800 );
		var minimumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMinimum * 10f ), 0, 900 );
		var maximumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMaximum * 10f ), 900, 1800 );

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
		var maxMovementTenthsPerSec = Math.Clamp( (int) MathF.Round( settings.SeatBeltTensionerMaxMotorSpeed * 10f ), 50, 5000 );

		_usbSerialPortHelper.WriteLine( $"ML{maxMovementTenthsPerSec:D4}R{maxMovementTenthsPerSec:D4}" );
	}

	public void SendInvertedArms()
	{
		if ( !IsConnected )
		{
			return;
		}

		var settings = DataContext.DataContext.Instance.Settings;

		var value = settings.SeatBeltTensionerInvertedArms ? 1 : 0;

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

		var minimumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMinimum * 10f ), 0, 900 );
		var maximumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMaximum * 10f ), 900, 1800 );

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
		var minimumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMinimum * 10f ), 0, 900 );
		var neutralTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerNeutral * 10f ), 0, 1800 );
		var maximumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMaximum * 10f ), 900, 1800 );

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

		var longAccel = settings.SeatBeltTensionerSurgeSubtractGravity ? longAccelAvg - gravLong : longAccelAvg;
		var latAccel = settings.SeatBeltTensionerSwaySubtractGravity ? latAccelAvg - gravLat : latAccelAvg;
		var vertAccel = settings.SeatBeltTensionerHeaveSubtractGravity ? vertAccelAvg - gravVert : vertAccelAvg;

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
		MainWindow._seatBeltTensionerPage.SurgeMinGString = _surgeMinG < float.MaxValue ? $"{_surgeMinG:F2} G" : "---";
		MainWindow._seatBeltTensionerPage.SurgeMaxGString = _surgeMaxG > float.MinValue ? $"{_surgeMaxG:F2} G" : "---";
		MainWindow._seatBeltTensionerPage.SwayMinGString = _swayMinG < float.MaxValue ? $"{_swayMinG:F2} G" : "---";
		MainWindow._seatBeltTensionerPage.SwayMaxGString = _swayMaxG > float.MinValue ? $"{_swayMaxG:F2} G" : "---";
		MainWindow._seatBeltTensionerPage.HeaveMinGString = _heaveMinG < float.MaxValue ? $"{_heaveMinG:F2} G" : "---";
		MainWindow._seatBeltTensionerPage.HeaveMaxGString = _heaveMaxG > float.MinValue ? $"{_heaveMaxG:F2} G" : "---";

		// Surge normalized [-1..1]: braking tightens both belts, acceleration loosens both belts
		var surgeNormalized = Math.Clamp( -longAccel / MathZ.OneG / settings.SeatBeltTensionerSurgeMaxG, -1f, 1f );

		// Sway normalized [-1..1]: positive biases right belt tighter, left belt looser
		var swayNormalized = Math.Clamp( latAccel / MathZ.OneG / settings.SeatBeltTensionerSwayMaxG, -1f, 1f );

		// Heave normalized [-1..1]
		var heaveNormalized = Math.Clamp( vertAccel / MathZ.OneG / settings.SeatBeltTensionerHeaveMaxG, -1f, 1f );

		// Apply axis mode (disable / normal / inverted)
		surgeNormalized = ApplyAxisMode( surgeNormalized, settings.SeatBeltTensionerSurgeMode );
		swayNormalized = ApplyAxisMode( swayNormalized, settings.SeatBeltTensionerSwayMode );
		heaveNormalized = ApplyAxisMode( heaveNormalized, settings.SeatBeltTensionerHeaveMode );

		// Apply dead zone per axis
		surgeNormalized = ApplyDeadZone( surgeNormalized, settings.SeatBeltTensionerSurgeDeadZone );
		swayNormalized = ApplyDeadZone( swayNormalized, settings.SeatBeltTensionerSwayDeadZone );
		heaveNormalized = ApplyDeadZone( heaveNormalized, settings.SeatBeltTensionerHeaveDeadZone );

		// Apply curve per axis
		surgeNormalized = ApplyCurve( surgeNormalized, settings.SeatBeltTensionerSurgeCurve );
		swayNormalized = ApplyCurve( swayNormalized, settings.SeatBeltTensionerSwayCurve );
		heaveNormalized = ApplyCurve( heaveNormalized, settings.SeatBeltTensionerHeaveCurve );

		// Apply EMA smoothing per axis (low-latency: alpha = 1 - smoothing)
		surgeNormalized = ApplySmoothing( surgeNormalized, ref _surgeSmoothed, settings.SeatBeltTensionerSurgeSmoothing );
		swayNormalized = ApplySmoothing( swayNormalized, ref _swaySmoothed, settings.SeatBeltTensionerSwaySmoothing );
		heaveNormalized = ApplySmoothing( heaveNormalized, ref _heaveSmoothed, settings.SeatBeltTensionerHeaveSmoothing );

		// Update graphs if on the SBT page
		if ( MairaAppMenuPopup.CurrentAppPage == MainWindow.AppPage.SeatBeltTensioner )
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
		if ( settings.SeatBeltTensionerSeatOfPantsMode != AxisMode.Disabled )
		{
			var rawSop = App.Instance!.SteeringEffects.SeatOfPantsEffect;

			// Apply mode (Normal or Inverted)
			var sop = ApplyAxisMode( rawSop, settings.SeatBeltTensionerSeatOfPantsMode );

			// Apply curve
			sop = ApplyCurve( sop, settings.SeatBeltTensionerSeatOfPantsCurve );

			var amplitudeTenths = settings.SeatBeltTensionerSeatOfPantsAmplitude * 10f;

			// Positive SoP = right lateral force → tighten right belt, negative SoP → tighten left belt
			// No loosening: only add, never subtract below base position
			if ( sop < 0f )
			{
				// Tighten left
				leftTargetPositionTenths = Math.Clamp( leftTargetPositionTenths + (int) MathF.Round( -sop * amplitudeTenths ), minimumTenths, maximumTenths );
			}
			else if ( sop > 0f )
			{
				// Tighten right
				rightTargetPositionTenths = Math.Clamp( rightTargetPositionTenths + (int) MathF.Round( sop * amplitudeTenths ), minimumTenths, maximumTenths );
			}
		}

		// Update shoulder graphs if on the SBT page
		if ( MairaAppMenuPopup.CurrentAppPage == MainWindow.AppPage.SeatBeltTensioner )
		{
			// Remap tenths to [-1..1]: -1=minimum, 0=neutral, +1=maximum (piecewise linear)
			var leftShoulderNormalized = leftTargetPositionTenths <= neutralTenths ? (float) ( leftTargetPositionTenths - neutralTenths ) / ( neutralTenths - minimumTenths ) : (float) ( leftTargetPositionTenths - neutralTenths ) / ( maximumTenths - neutralTenths );
			var rightShoulderNormalized = rightTargetPositionTenths <= neutralTenths ? (float) ( rightTargetPositionTenths - neutralTenths ) / ( neutralTenths - minimumTenths ) : (float) ( rightTargetPositionTenths - neutralTenths ) / ( maximumTenths - neutralTenths );

			_leftShoulderGraph.Advance( leftShoulderNormalized );
			_rightShoulderGraph.Advance( rightShoulderNormalized );

			_leftShoulderGraph.WritePixels();
			_rightShoulderGraph.WritePixels();

			// Update overlay text for active vibration effects
			var absActive = settings.SeatBeltTensionerABSEnabled && IsABSOrWheelLockActive( app );
			var wheelSlipActive = settings.SeatBeltTensionerWheelSlipEnabled && IsWheelSpinActive( app );
			var rumbleLeftActive = settings.SeatBeltTensionerRumbleEnabled && IsRumbleActiveLeft( app );
			var rumbleRightActive = settings.SeatBeltTensionerRumbleEnabled && IsRumbleActiveRight( app );

			app.Dispatcher.InvokeAsync( () =>
			{
				var page = MainWindow._seatBeltTensionerPage;
				page.UpdateShoulderOverlays( absActive, wheelSlipActive, rumbleLeftActive, rumbleRightActive );
			} );
		}

		// --- Vibration effects 2/3/4: determine which effect is active (priority: ABS > WheelSlip > Rumble) ---
		var leftEffectFreqHz = 0;
		var leftEffectAmplitudeDeg = 0;
		var rightEffectFreqHz = 0;
		var rightEffectAmplitudeDeg = 0;

		if ( settings.SeatBeltTensionerABSEnabled && IsABSOrWheelLockActive( app ) )
		{
			leftEffectFreqHz = rightEffectFreqHz = Math.Clamp( (int) MathF.Round( settings.SeatBeltTensionerABSFrequency ), 0, 15 );
			leftEffectAmplitudeDeg = rightEffectAmplitudeDeg = Math.Clamp( (int) MathF.Round( settings.SeatBeltTensionerABSAmplitude ), 0, 60 );
		}
		else if ( settings.SeatBeltTensionerWheelSlipEnabled && IsWheelSpinActive( app ) )
		{
			leftEffectFreqHz = rightEffectFreqHz = Math.Clamp( (int) MathF.Round( settings.SeatBeltTensionerWheelSlipFrequency ), 0, 15 );
			leftEffectAmplitudeDeg = rightEffectAmplitudeDeg = Math.Clamp( (int) MathF.Round( settings.SeatBeltTensionerWheelSlipAmplitude ), 0, 60 );
		}
		else if ( settings.SeatBeltTensionerRumbleEnabled )
		{
			var rumbleFreqHz = Math.Clamp( (int) MathF.Round( settings.SeatBeltTensionerRumbleFrequency ), 0, 15 );
			var rumbleAmplitudeDeg = Math.Clamp( (int) MathF.Round( settings.SeatBeltTensionerRumbleAmplitude ), 0, 60 );

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

		var minimumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMinimum * 10f ), 0, 900 );
		var neutralTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerNeutral * 10f ), 0, 1800 );
		var maximumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMaximum * 10f ), 900, 1800 );

		// Normalize by each axis' MaxG setting, then apply axis mode
		var surgeNormalized = 0f;
		var swayNormalized = 0f;
		var heaveNormalized = 0f;

		switch ( _testAxis )
		{
			case TestAxis.Surge:
				surgeNormalized = ApplyAxisMode( Math.Clamp( -rawG / settings.SeatBeltTensionerSurgeMaxG, -1f, 1f ), settings.SeatBeltTensionerSurgeMode );
				break;
			case TestAxis.Sway:
				swayNormalized = ApplyAxisMode( Math.Clamp( rawG / settings.SeatBeltTensionerSwayMaxG, -1f, 1f ), settings.SeatBeltTensionerSwayMode );
				break;
			case TestAxis.Heave:
				heaveNormalized = ApplyAxisMode( Math.Clamp( rawG / settings.SeatBeltTensionerHeaveMaxG, -1f, 1f ), settings.SeatBeltTensionerHeaveMode );
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
			MainWindow._seatBeltTensionerPage.UpdateTestStatus();
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

		var minimumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMinimum * 10f ), 0, 900 );

		_calibrationSweepGoingUp = true;
		_calibrationSweepLeftPos = minimumTenths;
		_calibrationSweepRightPos = minimumTenths;
		_testAxis = TestAxis.CalibrationSweep;
		_testStep = 0;

		UpdateTestStatusUI();
	}

	private void UpdateCalibrationSweepTest( App app, DataContext.Settings settings )
	{
		var minimumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMinimum * 10f ), 0, 900 );
		var maximumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMaximum * 10f ), 900, 1800 );

		// Step size per update (20 Hz): deg/sec → tenths/sec → tenths/update
		var stepTenths = Math.Max( 1, (int) MathF.Round( settings.SeatBeltTensionerMaxMotorSpeed * 10f / 20f ) );

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
			TestVibrationEffect.ABS => ( (int) MathF.Round( settings.SeatBeltTensionerABSFrequency ), (int) MathF.Round( settings.SeatBeltTensionerABSAmplitude ) ),
			TestVibrationEffect.WheelSlip => ( (int) MathF.Round( settings.SeatBeltTensionerWheelSlipFrequency ), (int) MathF.Round( settings.SeatBeltTensionerWheelSlipAmplitude ) ),
			TestVibrationEffect.Rumble => ( (int) MathF.Round( settings.SeatBeltTensionerRumbleFrequency ), (int) MathF.Round( settings.SeatBeltTensionerRumbleAmplitude ) ),
			_ => ( 0, 0 )
		};
	}

	private void UpdateVibrationTest( App app, DataContext.Settings settings )
	{
		const int totalSteps = 40; // 2 seconds at 20 Hz

		if ( _vibrationTestStep >= totalSteps )
		{
			if ( IsConnected )
			{
				var minimumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMinimum * 10f ), 0, 900 );

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

		var neutralTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerNeutral * 10f ), 0, 1800 );

		if ( _vibrationTestStep == 1 )
		{
			_lastSentLeftTenths = -1;
			_lastSentRightTenths = -1;

			SendSetPosition( neutralTenths, neutralTenths );

			_lastSentLeftEffectFreqHz = -1;
			_lastSentLeftEffectAmplitudeDeg = -1;
			_lastSentRightEffectFreqHz = -1;
			_lastSentRightEffectAmplitudeDeg = -1;

			var ( freq, amp ) = GetVibrationTestParams( settings );

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

	private void ResetMinMaxG()
	{
		_surgeMinG = float.MaxValue;
		_surgeMaxG = float.MinValue;
		_swayMinG = float.MaxValue;
		_swayMaxG = float.MinValue;
		_heaveMinG = float.MaxValue;
		_heaveMaxG = float.MinValue;

		MainWindow._seatBeltTensionerPage.SurgeMinGString = "---";
		MainWindow._seatBeltTensionerPage.SurgeMaxGString = "---";
		MainWindow._seatBeltTensionerPage.SwayMinGString = "---";
		MainWindow._seatBeltTensionerPage.SwayMaxGString = "---";
		MainWindow._seatBeltTensionerPage.HeaveMinGString = "---";
		MainWindow._seatBeltTensionerPage.HeaveMaxGString = "---";
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
