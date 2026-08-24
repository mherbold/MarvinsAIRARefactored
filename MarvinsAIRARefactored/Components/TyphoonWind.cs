
using System.Buffers.Text;
using System.Globalization;
using System.Text.RegularExpressions;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Windows;

using Timer = System.Timers.Timer;

namespace MarvinsAIRARefactored.Components;

public partial class TyphoonWind
{
	private const int UpdateInterval = 12;

	// TOP value of the 25 kHz PWM in the wind box firmware - fan duty is commanded as 0..MaxFanPower
	private const int MaxFanPower = 320;

	// The fan test buttons drive the device from their own timer instead of the app tick, so they work
	// with or without a simulator connected. The interval has to stay well under the two second
	// inactivity watchdog in the firmware, which zeroes both fans when commands stop arriving.
	private const int TestKeepAliveIntervalInMilliseconds = 200;

	public bool IsConnected { get; private set; } = false;

	private readonly UsbSerialPortHelper _usbSerialPortHelper = new( "MAIRA WIND" );

	private readonly Timer _testKeepAliveTimer = new( TestKeepAliveIntervalInMilliseconds );

	private float _leftFanPower = 0f;
	private float _rightFanPower = 0f;

	private int _leftFanRPM = 0;
	private int _rightFanRPM = 0;

	private bool _testingLeft = false;
	private bool _testingRight = false;

	private bool _previewActive = false;
	private float _previewPowerNormalized = 0f;

	private bool _deviceScanAttempted = false;

	private int _updateCounter = UpdateInterval + 7;

	private static readonly Regex _fanRPMRegex = FanRPMRegex();

	[GeneratedRegex( @"^\s*L(?<left>\d+)\s*R(?<right>\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant )]
	private static partial Regex FanRPMRegex();

	public TyphoonWind()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[TyphoonWind] Constructor >>>" );

		_usbSerialPortHelper.DataReceived += OnDataReceived;
		_usbSerialPortHelper.PortClosed += OnPortClosed;

		_testKeepAliveTimer.Elapsed += OnTestKeepAliveTimer;

		app.Logger.WriteLine( "[TyphoonWind] <<< Constructor" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[TyphoonWind] Shutdown >>>" );

		_testingLeft = false;
		_testingRight = false;

		UpdateTestState();

		Disconnect();

		app.Logger.WriteLine( "[TyphoonWind] <<< Shutdown" );
	}

	// The serial port device scan is slow (WMI enumeration plus handshake probes), so it does not run at
	// startup - it runs lazily on the first Connect() and on demand from the retry button on the page.
	public void ScanForDevice()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[TyphoonWind] ScanForDevice >>>" );

		_deviceScanAttempted = true;

		_usbSerialPortHelper.Initialize();

		app.Dispatcher.Invoke( () =>
		{
			if ( _usbSerialPortHelper.DeviceFound )
			{
				MainWindow._typhoonWindPage.ConnectToWind_MairaSwitch.IsEnabled = true;
				MainWindow._typhoonWindPage.ConnectToWind_MairaSwitch.ErrorMessage = string.Empty;
				MainWindow._typhoonWindPage.RetryDevice_MairaButton.Visibility = System.Windows.Visibility.Collapsed;
			}
			else
			{
				MainWindow._typhoonWindPage.ConnectToWind_MairaSwitch.IsEnabled = false;
				MainWindow._typhoonWindPage.ConnectToWind_MairaSwitch.ErrorMessage = _usbSerialPortHelper.LastErrorMessage;
				MainWindow._typhoonWindPage.RetryDevice_MairaButton.Visibility = System.Windows.Visibility.Visible;
			}
		} );

		app.Logger.WriteLine( "[TyphoonWind] <<< ScanForDevice" );
	}

	public bool Connect()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[TyphoonWind] Connect >>>" );

		if ( !_deviceScanAttempted )
		{
			ScanForDevice();
		}

		IsConnected = _usbSerialPortHelper.Open();

		app.Dispatcher.Invoke( () =>
		{
			MainWindow._typhoonWindPage.ConnectToWind_MairaSwitch.IsOn = IsConnected;
			MainWindow._typhoonWindPage.ConnectToWind_MairaSwitch.ErrorMessage = IsConnected ? string.Empty : _usbSerialPortHelper.LastErrorMessage;
		} );

		app.Logger.WriteLine( "[TyphoonWind] <<< Connect" );

		return IsConnected;
	}

	public void Disconnect()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[TyphoonWind] Disconnect >>>" );

		IsConnected = false;

		_usbSerialPortHelper.Close();

		_leftFanRPM = 0;
		_rightFanRPM = 0;

		app.Dispatcher.Invoke( () =>
		{
			MainWindow._typhoonWindPage.ConnectToWind_MairaSwitch.ErrorMessage = string.Empty;
		} );

		app.Logger.WriteLine( "[TyphoonWind] <<< Disconnect" );
	}

	public void TestLeft( bool enable )
	{
		_testingLeft = enable;

		UpdateTestState();
	}

	public void TestRight( bool enable )
	{
		_testingRight = enable;

		UpdateTestState();
	}

	// Pushes the current test button states straight to the device and keeps them refreshed from the
	// keep alive timer. The test deliberately does not go through Update() - that path zeroes the fans
	// whenever the car is not on track, so it would only ever spin the fans during an actual session.
	private void UpdateTestState()
	{
		if ( _testingLeft || _testingRight )
		{
			SendTestFanPowerToDevice();

			_testKeepAliveTimer.Start();
		}
		else
		{
			_testKeepAliveTimer.Stop();

			_leftFanPower = 0f;
			_rightFanPower = 0f;

			SendFanPowerToDevice();
		}
	}

	// The tested fan spins at the master wind power setting, and this is recomputed on every keep alive
	// so turning the master wind power knob during a test is heard right away.
	private void SendTestFanPowerToDevice()
	{
		var settings = DataContext.DataContext.Instance.Settings;

		var testFanPower = settings.TyphoonWindMasterWindPower * MaxFanPower;

		_leftFanPower = _testingLeft ? testFanPower : 0f;
		_rightFanPower = _testingRight ? testFanPower : 0f;

		SendFanPowerToDevice();
	}

	private void OnTestKeepAliveTimer( object? sender, System.Timers.ElapsedEventArgs e )
	{
		SendTestFanPowerToDevice();
	}

	public void StartPreview( float normalizedPower )
	{
		_previewActive = true;
		_previewPowerNormalized = MathF.Max( 0f, MathF.Min( 1f, normalizedPower ) );
	}

	public void StopPreview()
	{
		_previewActive = false;
	}

	private void OnDataReceived( object? sender, string data )
	{
		if ( string.IsNullOrWhiteSpace( data ) )
		{
			return;
		}

		var trimmed = data.Trim();

		var match = _fanRPMRegex.Match( trimmed );

		if ( !match.Success )
		{
			return;
		}

		var leftText = match.Groups[ "left" ].Value;
		var rightText = match.Groups[ "right" ].Value;

		if ( !int.TryParse( leftText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftRpm ) )
		{
			return;
		}

		if ( !int.TryParse( rightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightRpm ) )
		{
			return;
		}

		_leftFanRPM = leftRpm;
		_rightFanRPM = rightRpm;
	}

	private void OnPortClosed( object? sender, EventArgs e )
	{
		Disconnect();
	}

	private void Update( App app )
	{
		// A running fan test owns the device until it is switched off again - see UpdateTestState

		if ( _testingLeft || _testingRight )
		{
			return;
		}

		var settings = DataContext.DataContext.Instance.Settings;

		Span<float> speedArray = stackalloc float[ 10 ];

		speedArray[ 0 ] = settings.TyphoonWindSpeed1;
		speedArray[ 1 ] = settings.TyphoonWindSpeed2;
		speedArray[ 2 ] = settings.TyphoonWindSpeed3;
		speedArray[ 3 ] = settings.TyphoonWindSpeed4;
		speedArray[ 4 ] = settings.TyphoonWindSpeed5;
		speedArray[ 5 ] = settings.TyphoonWindSpeed6;
		speedArray[ 6 ] = settings.TyphoonWindSpeed7;
		speedArray[ 7 ] = settings.TyphoonWindSpeed8;
		speedArray[ 8 ] = settings.TyphoonWindSpeed9;
		speedArray[ 9 ] = settings.TyphoonWindSpeed10;

		Span<float> fanPowerArray = stackalloc float[ 10 ];

		fanPowerArray[ 0 ] = settings.TyphoonWindFanPower1;
		fanPowerArray[ 1 ] = settings.TyphoonWindFanPower2;
		fanPowerArray[ 2 ] = settings.TyphoonWindFanPower3;
		fanPowerArray[ 3 ] = settings.TyphoonWindFanPower4;
		fanPowerArray[ 4 ] = settings.TyphoonWindFanPower5;
		fanPowerArray[ 5 ] = settings.TyphoonWindFanPower6;
		fanPowerArray[ 6 ] = settings.TyphoonWindFanPower7;
		fanPowerArray[ 7 ] = settings.TyphoonWindFanPower8;
		fanPowerArray[ 8 ] = settings.TyphoonWindFanPower9;
		fanPowerArray[ 9 ] = settings.TyphoonWindFanPower10;

		var velocity = MathF.Sqrt( app.Simulator.VelocityX * app.Simulator.VelocityX + app.Simulator.VelocityY * app.Simulator.VelocityY );

		var speed = MathF.Max( velocity, settings.TyphoonWindMinimumSpeed );

		var fanPower = settings.TyphoonWindFanPower10;

		for ( var speedIndex = 0; speedIndex < speedArray.Length; speedIndex++ )
		{
			if ( speed < speedArray[ speedIndex ] )
			{
				var i0 = Math.Max( 0, speedIndex - 2 );
				var i1 = Math.Max( 0, speedIndex - 1 );
				var i2 = speedIndex;
				var i3 = Math.Min( speedArray.Length - 1, speedIndex + 1 );

				if ( speedArray[ i2 ] > speedArray[ i1 ] )
				{
					var t = ( speed - speedArray[ i1 ] ) / ( speedArray[ i2 ] - speedArray[ i1 ] );

					var m0 = fanPowerArray[ i0 ];
					var m1 = fanPowerArray[ i1 ];
					var m2 = fanPowerArray[ i2 ];
					var m3 = fanPowerArray[ i3 ];

					fanPower = MathZ.InterpolateHermite( m0, m1, m2, m3, t );
				}
				else
				{
					fanPower = fanPowerArray[ i1 ];
				}

				break;
			}
		}

		// VelocityY * 0.08f means that at 12.5 m/s (45 km/h) sideways velocity, the wind will be fully curved
		// YawRate * 1.91f means that at 0.523 rad/s (30 deg/s) yaw rate, the wind will be fully curved

		var curveFactor = Math.Clamp( app.Simulator.VelocityY * 0.08f * settings.TyphoonWindCurving + app.Simulator.YawRate * 1.91f * settings.TyphoonWindCurving, -1f, 1f );

		// Negative curveFactor biases wind towards the left fan, positive towards the right fan

		if ( _previewActive )
		{
			var previewFanPower = _previewPowerNormalized * settings.TyphoonWindMasterWindPower * MaxFanPower;

			_leftFanPower = previewFanPower;
			_rightFanPower = previewFanPower;
		}
		else if ( app.Simulator.IsOnTrack )
		{
			_leftFanPower = fanPower * ( 1f + MathF.Min( 0, curveFactor ) ) * settings.TyphoonWindMasterWindPower * MaxFanPower;
			_rightFanPower = fanPower * ( 1f - MathF.Max( 0, curveFactor ) ) * settings.TyphoonWindMasterWindPower * MaxFanPower;
		}
		else
		{
			_leftFanPower = 0f;
			_rightFanPower = 0f;
		}

		_leftFanPower = Math.Max( 0f, _leftFanPower );
		_rightFanPower = Math.Max( 0f, _rightFanPower );

		SendFanPowerToDevice();
	}

	private void SendFanPowerToDevice()
	{
		// Values outside 0..MaxFanPower would make the firmware reject the whole command and leave both
		// fans where they were, so clamp here rather than trusting whatever produced the fan powers

		var leftVal = Math.Clamp( (int) MathF.Round( _leftFanPower ), 0, MaxFanPower );
		var rightVal = Math.Clamp( (int) MathF.Round( _rightFanPower ), 0, MaxFanPower );

		// Format command into a stack-allocated UTF-8 buffer to avoid allocating a string

		Span<byte> buf = stackalloc byte[ 32 ];

		var idx = 0;

		buf[ idx++ ] = (byte) 'L';

		Utf8Formatter.TryFormat( leftVal, buf[ idx.. ], out var leftBytes );

		idx += leftBytes;

		buf[ idx++ ] = (byte) 'R';

		Utf8Formatter.TryFormat( rightVal, buf[ idx.. ], out var rightBytes );

		idx += rightBytes;

		_usbSerialPortHelper.WriteLine( buf[ ..idx ] );
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter <= 0 )
		{
			_updateCounter = UpdateInterval;

			Update( app );

			MainWindow._typhoonWindPage.LeftFanPower_TextBlock.Text = $"{_leftFanPower * 100f / MaxFanPower:F0}";
			MainWindow._typhoonWindPage.RightFanPower_TextBlock.Text = $"{_rightFanPower * 100f / MaxFanPower:F0}";

			MainWindow._typhoonWindPage.LeftFanRPM_TextBlock.Text = $"{_leftFanRPM}";
			MainWindow._typhoonWindPage.RightFanRPM_TextBlock.Text = $"{_rightFanRPM}";
		}
	}
}
