using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public class SeatBeltTensioner
{
	// Telemetry → SBT update rate: 60fps source / 3 = 20 updates per second
	private const int UpdateInterval = 3;

	public bool IsConnected { get; private set; } = false;

	private readonly UsbSerialPortHelper _usbSerialPortHelper = new( "MAIRA SBT" );

	private readonly SeatBeltTensionerGraph _surgeGraph = new();
	private readonly SeatBeltTensionerGraph _swayGraph = new();
	private readonly SeatBeltTensionerGraph _heaveGraph = new();

	private readonly SeatBeltTensionerGraph _leftShoulderGraph = new();
	private readonly SeatBeltTensionerGraph _rightShoulderGraph = new();

	private int _updateCounter = UpdateInterval + 2;
	private int _lastSentLeftTenths = -1;
	private int _lastSentRightTenths = -1;

	private float _longAccelSum = 0f;
	private float _latAccelSum = 0f;
	private float _vertAccelSum = 0f;
	private float _pitchSum = 0f;
	private float _rollSum = 0f;
	private int _accelSampleCount = 0;

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

		_surgeGraph.Initialize( sbtPage.SurgeGraph_Image, 0.3f, 0f, 0.2f, 1f, 0.08f, 0.58f );
		_swayGraph.Initialize( sbtPage.SwayGraph_Image, 0.1f, 0.3f, 0f, 0.5f, 1f, 0f );
		_heaveGraph.Initialize( sbtPage.HeaveGraph_Image, 0.1f, 0.2f, 0.3f, 0.3f, 0.7f, 1f );

		_leftShoulderGraph.Initialize( sbtPage.LeftShoulderGraph_Image, 0f, 0f, 0.2f, 0f, 0f, 1f );
		_rightShoulderGraph.Initialize( sbtPage.RightShoulderGraph_Image, 0.2f, 0f, 0f, 1f, 0f, 0f );

		if ( !_usbSerialPortHelper.DeviceFound )
		{
			app.Logger.WriteLine( "[SeatBeltTensioner] Device not found - disabling SeatBeltTensionerEnabled" );

			DataContext.DataContext.Instance.Settings.SeatBeltTensionerEnabled = false;

			app.Dispatcher.Invoke( () =>
			{
				MainWindow._seatBeltTensionerPage.ConnectToSbt_MairaSwitch.IsEnabled = false;
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

	public bool Connect()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SeatBeltTensioner] Connect >>>" );

		IsConnected = _usbSerialPortHelper.Open();

		if ( IsConnected )
		{
			SendCalibration();
			SendMaxMovement();
		}

		app.Dispatcher.Invoke( () =>
		{
			MainWindow._seatBeltTensionerPage.ConnectToSbt_MairaSwitch.IsOn = IsConnected;
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

		_usbSerialPortHelper.Close();

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

		var maxMovement = Math.Clamp( (int) MathF.Round( settings.SeatBeltTensionerMaxMotorSpeed ), 5, 50 );

		_usbSerialPortHelper.WriteLine( $"ML{maxMovement:D4}R{maxMovement:D4}" );
	}

	private void SendSetPosition( int leftTargetPositionTenths, int rightTargetPositionTenths )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		var minimumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMinimum * 10f ), 0, 900 );
		var maximumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMaximum * 10f ), 900, 1800 );

		leftTargetPositionTenths = Math.Clamp( leftTargetPositionTenths, minimumTenths, maximumTenths );
		rightTargetPositionTenths = Math.Clamp( rightTargetPositionTenths, minimumTenths, maximumTenths );

		if ( ( leftTargetPositionTenths == _lastSentLeftTenths ) && ( rightTargetPositionTenths == _lastSentRightTenths ) )
		{
			return;
		}

		_lastSentLeftTenths = leftTargetPositionTenths;
		_lastSentRightTenths = rightTargetPositionTenths;

		_usbSerialPortHelper.WriteLine( $"SL{leftTargetPositionTenths:D4}R{rightTargetPositionTenths:D4}" );
	}

	private void Update( App app )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		if ( _accelSampleCount == 0 )
		{
			return;
		}

		var longAccelAvg = _longAccelSum / _accelSampleCount;
		var latAccelAvg = _latAccelSum / _accelSampleCount;
		var vertAccelAvg = _vertAccelSum / _accelSampleCount;

		var pitch = _pitchSum / _accelSampleCount;
		var roll = _rollSum / _accelSampleCount;

		_longAccelSum = 0f;
		_latAccelSum = 0f;
		_vertAccelSum = 0f;

		_pitchSum = 0f;
		_rollSum = 0f;

		_accelSampleCount = 0;

		if ( !settings.SeatBeltTensionerEnabled || !IsConnected )
		{
			return;
		}

		// Get and sanitize settings
		var minimumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMinimum * 10f ), 0, 900 );
		var neutralTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerNeutral * 10f ), 0, 1800 );
		var maximumTenths = Math.Clamp( (int) Math.Round( settings.SeatBeltTensionerMaximum * 10f ), 900, 1800 );

		// Calculate full range of motion in tenths of degrees
		var rangeTenths = maximumTenths - minimumTenths;

		// Calculate normalized neutral position
		var neutralPositionNormalized = ( neutralTenths - 900 ) / (float) rangeTenths;

		// Compute gravity components in car body space using averaged pitch and roll.
		// iRacing includes gravity in the acceleration telemetry (specific force), so we subtract
		// the gravitational contribution to get the true inertial acceleration.
		// Pitch: positive = nose up. Roll: positive = left side up.
		var cosPitch = MathF.Cos( pitch );
		var sinPitch = MathF.Sin( pitch );
		var cosRoll = MathF.Cos( roll );
		var sinRoll = MathF.Sin( roll );

		// Gravity component along each body axis (what iRacing adds to raw acceleration)
		var gravVert = MathZ.OneG * cosPitch * cosRoll;

		float longAccel;
		float latAccel;
		float vertAccel;

		if ( settings.SeatBeltTensionerSubtractFullGravity )
		{
			var gravLong = MathZ.OneG * -sinPitch;
			var gravLat = MathZ.OneG * cosPitch * sinRoll;

			longAccel = longAccelAvg - gravLong;
			latAccel = latAccelAvg - gravLat;
			vertAccel = vertAccelAvg - gravVert;
		}
		else
		{
			longAccel = longAccelAvg;
			latAccel = latAccelAvg;
			vertAccel = vertAccelAvg - gravVert;
		}

		// Surge normalized [-1..1]:
		var surgeNormalized = Math.Clamp( -longAccel / MathZ.OneG / settings.SeatBeltTensionerSurgeMaxG, -1f, 1f );

		// Sway normalized [-1..1]: positive biases right belt tighter, left belt looser
		var swayNormalized = Math.Clamp( latAccel / MathZ.OneG / settings.SeatBeltTensionerSwayMaxG, -1f, 1f );

		// Heave normalized [-1..1]: bumps and crests both tighten both belts
		var heaveNormalized = Math.Clamp( -vertAccel / MathZ.OneG / settings.SeatBeltTensionerHeaveMaxG, -1f, 1f );

		if ( MairaAppMenuPopup.CurrentAppPage == MainWindow.AppPage.SeatBeltTensioner )
		{
			_surgeGraph.Advance( surgeNormalized );
			_swayGraph.Advance( swayNormalized );
			_heaveGraph.Advance( heaveNormalized );

			_surgeGraph.WritePixels();
			_swayGraph.WritePixels();
			_heaveGraph.WritePixels();
		}

		// Combine into per-arm normalized signal and offset by neutral position
		var leftCombinedNormalized = surgeNormalized + heaveNormalized - swayNormalized + neutralPositionNormalized;
		var rightCombinedNormalized = surgeNormalized + heaveNormalized + swayNormalized + neutralPositionNormalized;

		// Apply soft limiter
		var limitedLeftNormalized = MathZ.SoftLimiter( leftCombinedNormalized );
		var limitedRightNormalized = MathZ.SoftLimiter( rightCombinedNormalized );

		// Map to tenths of degrees and clamp to minimum / maximum
		var leftTargetPositionTenths = Math.Clamp( (int) MathF.Round( limitedLeftNormalized * rangeTenths + 900 ), minimumTenths, maximumTenths );
		var rightTargetPositionTenths = Math.Clamp( (int) MathF.Round( limitedRightNormalized * rangeTenths + 900 ), minimumTenths, maximumTenths );

		if ( MairaAppMenuPopup.CurrentAppPage == MainWindow.AppPage.SeatBeltTensioner )
		{
			// Remap tenths to [-1..1]: -1=minimum, 0=neutral, +1=maximum (piecewise linear)
			var leftShoulderNormalized = leftTargetPositionTenths <= neutralTenths
				? (float) ( leftTargetPositionTenths - neutralTenths ) / ( neutralTenths - minimumTenths )
				: (float) ( leftTargetPositionTenths - neutralTenths ) / ( maximumTenths - neutralTenths );

			var rightShoulderNormalized = rightTargetPositionTenths <= neutralTenths
				? (float) ( rightTargetPositionTenths - neutralTenths ) / ( neutralTenths - minimumTenths )
				: (float) ( rightTargetPositionTenths - neutralTenths ) / ( maximumTenths - neutralTenths );

			_leftShoulderGraph.Advance( leftShoulderNormalized );
			_rightShoulderGraph.Advance( rightShoulderNormalized );

			_leftShoulderGraph.WritePixels();
			_rightShoulderGraph.WritePixels();
		}

		// Send the new positions to the SBT if they have changed since the last update
		SendSetPosition( leftTargetPositionTenths, rightTargetPositionTenths );
	}

	private void OnPortClosed( object? sender, EventArgs e )
	{
		Disconnect();
	}

	public void Tick( App app )
	{
		if ( app.Simulator.IsOnTrack )
		{
			_longAccelSum += app.Simulator.LongAccel;
			_latAccelSum += app.Simulator.LatAccel;
			_vertAccelSum += app.Simulator.VertAccel;

			_pitchSum += app.Simulator.Pitch;
			_rollSum += app.Simulator.Roll;

			_accelSampleCount++;
		}

		_updateCounter--;

		if ( _updateCounter <= 0 )
		{
			_updateCounter = UpdateInterval;

			Update( app );
		}
	}
}
