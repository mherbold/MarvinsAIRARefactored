
using System.Globalization;
using System.Text.RegularExpressions;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public partial class Wind
{
	private const int UpdateInterval = 12;

	public bool IsConnected { get; private set; } = false;

	private readonly UsbSerialPortHelper _usbSerialPortHelper = new( "MAIRA WIND" );

	private float _leftFanPower = 0f;
	private float _rightFanPower = 0f;

	private int _leftFanRPM = 0;
	private int _rightFanRPM = 0;

	private int _updateCounter = UpdateInterval + 7;

	private static readonly Regex _fanRPMRegex = FanRPMRegex();

	[GeneratedRegex( @"^\s*L(?<left>\d+)\s*R(?<right>\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant )]
	private static partial Regex FanRPMRegex();

	public Wind()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Wind] Constructor >>>" );

		_usbSerialPortHelper.DataReceived += OnDataReceived;
		_usbSerialPortHelper.PortClosed += OnPortClosed;

		app.Logger.WriteLine( "[Wind] <<< Constructor" );
	}

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Wind] Initialize >>>" );

		_usbSerialPortHelper.Initialize();

		app.Logger.WriteLine( "[Wind] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Wind] Shutdown >>>" );

		Disconnect();

		app.Logger.WriteLine( "[Wind] <<< Shutdown" );
	}

	public bool Connect()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Wind] Connect >>>" );

		IsConnected = _usbSerialPortHelper.Open();

		app.Dispatcher.Invoke( () =>
		{
			MainWindow._windPage.ConnectToWind_MairaSwitch.IsOn = IsConnected;
		} );

		app.Logger.WriteLine( "[Wind] <<< Connect" );

		return IsConnected;
	}

	public void Disconnect()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Wind] Disconnect >>>" );

		IsConnected = false;

		_usbSerialPortHelper.Close();

		_leftFanRPM = 0;
		_rightFanRPM = 0;

		app.Logger.WriteLine( "[Wind] <<< Disconnect" );
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

		var speedArray = new float[]
		{
			settings.WindSpeed1,
			settings.WindSpeed2,
			settings.WindSpeed3,
			settings.WindSpeed4,
			settings.WindSpeed5,
			settings.WindSpeed6,
			settings.WindSpeed7,
			settings.WindSpeed8,
			settings.WindSpeed9,
			settings.WindSpeed10,
		};

		var fanPowerArray = new float[]
		{
			settings.WindFanPower1,
			settings.WindFanPower2,
			settings.WindFanPower3,
			settings.WindFanPower4,
			settings.WindFanPower5,
			settings.WindFanPower6,
			settings.WindFanPower7,
			settings.WindFanPower8,
			settings.WindFanPower9,
			settings.WindFanPower10,
		};

		var velocity = MathF.Sqrt( app.Simulator.VelocityX * app.Simulator.VelocityX + app.Simulator.VelocityY * app.Simulator.VelocityY );

		var speed = MathF.Max( velocity, settings.WindMinimumSpeed ) / 100f;

		var fanPower = settings.WindFanPower10;

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

		var curveFactor = Math.Clamp( app.Simulator.VelocityY * 0.08f * settings.WindCurving + app.Simulator.YawRate * 1.91f * settings.WindCurving, -1f, 1f );

		// Negative curveFactor biases wind towards the left fan, positive towards the right fan

		_leftFanPower = fanPower * ( 1f + MathF.Min( 0, curveFactor ) ) * settings.WindMasterWindPower * 320f;
		_rightFanPower = fanPower * ( 1f - MathF.Max( 0, curveFactor ) ) * settings.WindMasterWindPower * 320f;

		var leftFanPower = Math.Max( 1f, _leftFanPower );
		var rightFanPower = Math.Max( 1f, _rightFanPower );

		_usbSerialPortHelper.WriteLine( $"L{leftFanPower:F0}R{rightFanPower:F0}" );
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter == 0 )
		{
			_updateCounter = UpdateInterval;

			Update( app );

			MainWindow._windPage.LeftFanPower_TextBlock.Text = $"{_leftFanPower * 100f / 320f:F0}";
			MainWindow._windPage.RightFanPower_TextBlock.Text = $"{_rightFanPower * 100f / 320f:F0}";

			MainWindow._windPage.LeftFanRPM_TextBlock.Text = $"{_leftFanRPM}";
			MainWindow._windPage.RightFanRPM_TextBlock.Text = $"{_rightFanRPM}";
		}
	}
}
