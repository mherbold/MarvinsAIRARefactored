using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public class SeatBeltTensioner
{
	// Telemetry → SBT update rate: 60fps source / 6 = 10 updates per second
	private const int UpdateInterval = 6;

	public bool IsConnected { get; private set; } = false;

	private readonly UsbSerialPortHelper _usbSerialPortHelper = new( "MAIRA SBT" );

	private int _updateCounter = UpdateInterval + 2;
	private int _lastSentLeftTenths = -1;
	private int _lastSentRightTenths = -1;

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

		if ( !settings.SeatBeltTensionerEnabled || !IsConnected /*|| !app.Simulator.IsOnTrack*/ )
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

		// Surge normalized [-1..1]:
		var surgeNormalized = Math.Clamp( -app.Simulator.LongAccel / MathZ.OneG / settings.SeatBeltTensionerSurgeMaxG, -1f, 1f );

		// Sway normalized [-1..1]: positive biases right belt tighter, left belt looser
		var swayNormalized = Math.Clamp( app.Simulator.LatAccel / MathZ.OneG / settings.SeatBeltTensionerSwayMaxG, -1f, 1f );

		// Heave normalized [-1..1]: bumps and crests both tighten both belts
		var heaveNormalized = Math.Clamp( app.Simulator.VertAccel / MathZ.OneG / settings.SeatBeltTensionerHeaveMaxG, -1f, 1f );

		// Combine into per-arm normalized signal and offset by neutral position
		var leftCombinedNormalized = surgeNormalized + heaveNormalized - swayNormalized + neutralPositionNormalized;
		var rightCombinedNormalized = surgeNormalized + heaveNormalized + swayNormalized + neutralPositionNormalized;

		// Apply soft limiter
		var limitedLeftNormalized = MathZ.SoftLimiter( leftCombinedNormalized );
		var limitedRightNormalized = MathZ.SoftLimiter( rightCombinedNormalized );

		// Map to tenths of degrees and clamp to minimum / maximum
		var leftTargetPositionTenths = Math.Clamp( (int) MathF.Round( limitedLeftNormalized * rangeTenths + 900 ), minimumTenths, maximumTenths );
		var rightTargetPositionTenths = Math.Clamp( (int) MathF.Round( limitedRightNormalized * rangeTenths + 900 ), minimumTenths, maximumTenths );

		// Send the new positions to the SBT if they have changed since the last update
		SendSetPosition( leftTargetPositionTenths, rightTargetPositionTenths );
	}

	private void OnPortClosed( object? sender, EventArgs e )
	{
		Disconnect();
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter <= 0 )
		{
			_updateCounter = UpdateInterval;

			Update( app );
		}
	}
}
