
using System.Buffers.Text;
using System.Globalization;
using System.Text.RegularExpressions;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public partial class TyphoonWind
{
	private const int UpdateInterval = 12;

	public bool IsConnected { get; private set; } = false;

	private readonly UsbSerialPortHelper _usbSerialPortHelper = new( "MAIRA WIND" );

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

		app.Logger.WriteLine( "[TyphoonWind] <<< Constructor" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[TyphoonWind] Shutdown >>>" );

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
	}

	public void TestRight( bool enable )
	{
		_testingRight = enable;
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
			var previewFanPower = _previewPowerNormalized * settings.TyphoonWindMasterWindPower * 320f;

			_leftFanPower = previewFanPower;
			_rightFanPower = previewFanPower;
		}
		else if ( app.Simulator.IsOnTrack )
		{
			_leftFanPower = fanPower * ( 1f + MathF.Min( 0, curveFactor ) ) * settings.TyphoonWindMasterWindPower * 320f;
			_rightFanPower = fanPower * ( 1f - MathF.Max( 0, curveFactor ) ) * settings.TyphoonWindMasterWindPower * 320f;
		}
		else
		{
			_leftFanPower = 0f;
			_rightFanPower = 0f;
		}

		_leftFanPower = _testingLeft ? 320 : Math.Max( 0f, _leftFanPower );
		_rightFanPower = _testingRight ? 320 : Math.Max( 0f, _rightFanPower );

		// Format command into a stack-allocated UTF-8 buffer to avoid allocating a string

		var leftVal = (int) MathF.Round( _leftFanPower );
		var rightVal = (int) MathF.Round( _rightFanPower );

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

			MainWindow._typhoonWindPage.LeftFanPower_TextBlock.Text = $"{_leftFanPower * 100f / 320f:F0}";
			MainWindow._typhoonWindPage.RightFanPower_TextBlock.Text = $"{_rightFanPower * 100f / 320f:F0}";

			MainWindow._typhoonWindPage.LeftFanRPM_TextBlock.Text = $"{_leftFanRPM}";
			MainWindow._typhoonWindPage.RightFanRPM_TextBlock.Text = $"{_rightFanRPM}";
		}
	}
}
