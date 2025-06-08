
using System.Windows;

using Image = System.Windows.Controls.Image;

using IRSDKSharper;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Components;

public class Simulator
{
	public const int IRSDK_360HZ_SAMPLES_PER_FRAME = 6;
	private const float ONE_G = 9.80665f; // in meters per second squared

	private readonly IRacingSdk _irsdk = new();

	public IRacingSdk IRSDK { get => _irsdk; }

	public bool BrakeABSactive { get; private set; } = false;
	public float Brake { get; private set; } = 0f;
	public string CarScreenName { get; private set; } = string.Empty;
	public float[] CFShockVel_ST { get; private set; } = new float[ IRSDK_360HZ_SAMPLES_PER_FRAME ];
	public float Clutch { get; private set; } = 0f;
	public float[] CRShockVel_ST { get; private set; } = new float[ IRSDK_360HZ_SAMPLES_PER_FRAME ];
	public int Gear { get; private set; } = 0;
	public float GForce { get; private set; } = 0f;
	public bool IsConnected { get => _irsdk.IsConnected; }
	public bool IsOnTrack { get; private set; } = false;
	public float[] LFShockVel_ST { get; private set; } = new float[ IRSDK_360HZ_SAMPLES_PER_FRAME ];
	public float[] LRShockVel_ST { get; private set; } = new float[ IRSDK_360HZ_SAMPLES_PER_FRAME ];
	public int NumForwardGears { get; private set; } = 0;
	public IRacingSdkEnum.TrkLoc PlayerTrackSurface { get; private set; } = IRacingSdkEnum.TrkLoc.NotInWorld;
	public float[] RFShockVel_ST { get; private set; } = new float[ IRSDK_360HZ_SAMPLES_PER_FRAME ];
	public float RPM { get; private set; } = 0f;
	public float[] RRShockVel_ST { get; private set; } = new float[ IRSDK_360HZ_SAMPLES_PER_FRAME ];
	public float ShiftLightsShiftRPM { get; private set; } = 0f;
	public string SimMode { get; private set; } = string.Empty;
	public bool SteeringFFBEnabled { get; private set; } = false;
	public float SteeringWheelAngle { get; private set; } = 0f;
	public float SteeringWheelAngleMax { get; private set; } = 0f;
	public float[] SteeringWheelTorque_ST { get; private set; } = new float[ IRSDK_360HZ_SAMPLES_PER_FRAME ];
	public float Throttle { get; private set; } = 0f;
	public string TrackDisplayName { get; private set; } = string.Empty;
	public string TrackConfigName { get; private set; } = string.Empty;
	public float Velocity { get; private set; } = 0f;
	public float VelocityX { get; private set; } = 0f;
	public float VelocityY { get; private set; } = 0f;
	public bool WasOnTrack { get; private set; } = false;
	public bool WeatherDeclaredWet { get; private set; } = false;

	private bool _telemetryDataInitialized = false;
	private bool _needToUpdateFromContextSettings = false;

	private int? _tickCountLastFrame = null;
	private float? _velocityLastFrame = null;
	private bool? _weatherDeclaredWetLastFrame = null;
	private int? _lastPedalUpdateFrame = null;

	private IRacingSdkDatum? _brakeABSactiveDatum = null;
	private IRacingSdkDatum? _brakeDatum = null;
	private IRacingSdkDatum? _cfShockVel_STDatum = null;
	private IRacingSdkDatum? _clutchDatum = null;
	private IRacingSdkDatum? _crShockVel_STDatum = null;
	private IRacingSdkDatum? _gearDatum = null;
	private IRacingSdkDatum? _isOnTrackDatum = null;
	private IRacingSdkDatum? _lfShockVel_STDatum = null;
	private IRacingSdkDatum? _lrShockVel_STDatum = null;
	private IRacingSdkDatum? _playerTrackSurfaceDatum = null;
	private IRacingSdkDatum? _rfShockVel_STDatum = null;
	private IRacingSdkDatum? _rpmDatum = null;
	private IRacingSdkDatum? _rrShockVel_STDatum = null;
	private IRacingSdkDatum? _steeringFFBEnabledDatum = null;
	private IRacingSdkDatum? _steeringWheelAngleDatum = null;
	private IRacingSdkDatum? _steeringWheelAngleMaxDatum = null;
	private IRacingSdkDatum? _steeringWheelTorque_STDatum = null;
	private IRacingSdkDatum? _throttleDatum = null;
	private IRacingSdkDatum? _velocityXDatum = null;
	private IRacingSdkDatum? _velocityYDatum = null;
	private IRacingSdkDatum? _weatherDeclaredWetDatum = null;


	private readonly Graph _native60HzTorqueGraph;
	private readonly Statistics _native60HzTorqueStatistics = new( 60 );

	private readonly Graph _native360HzTorqueGraph;
	private readonly Statistics _native360HzTorqueStatistics = new( 360 );

	public Simulator( Image native60HzGraphImage, Image native360HzGraphImage )
	{
		var app = App.Instance;

		app?.Logger.WriteLine( "[Simulator] Constructor >>>" );

		_native60HzTorqueGraph = new Graph( native60HzGraphImage );
		_native360HzTorqueGraph = new Graph( native360HzGraphImage );

		app?.Logger.WriteLine( "[Simulator] <<< Constructor" );
	}

	public void Initialize()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[Simulator] Initialize >>>" );

			_irsdk.OnException += OnException;
			_irsdk.OnConnected += OnConnected;
			_irsdk.OnDisconnected += OnDisconnected;
			_irsdk.OnSessionInfo += OnSessionInfo;
			_irsdk.OnTelemetryData += OnTelemetryData;
			_irsdk.OnDebugLog += OnDebugLog;

			app.Logger.WriteLine( "[Simulator] <<< Initialize" );
		}
	}

	public void Shutdown()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[Simulator] Shutdown >>>" );

			app.Logger.WriteLine( "[Simulator] Stopping IRSDKSharper" );

			_irsdk.Stop();

			app.Logger.WriteLine( "[Simulator] <<< Shutdown" );
		}
	}

	public void Start()
	{
		_irsdk.Start();
	}

	private void OnException( Exception exception )
	{
		var app = App.Instance;

		app?.Logger.WriteLine( $"[Simulator] Exception thrown: {exception.Message.Trim()}" );

		throw new Exception( "IRSDKSharper exception thrown", exception );
	}

	private void OnConnected()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[Simulator] OnConnected >>>" );

			app.MultimediaTimer.Suspend = false;

			_needToUpdateFromContextSettings = true;

			app.Dispatcher.BeginInvoke( () =>
			{
				app.MainWindow.Graphs_Native60HzTorque_SimulatorNotRunning_Label.Visibility = Visibility.Hidden;
				app.MainWindow.Graphs_Native360HzTorque_SimulatorNotRunning_Label.Visibility = Visibility.Hidden;
				app.MainWindow.Simulator_SimulatorNotRunning_Label.Visibility = Visibility.Hidden;
			} );

			app.Logger.WriteLine( "[Simulator] <<< OnConnected" );
		}
	}

	private void OnDisconnected()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[Simulator] OnDisconnected >>>" );

			_telemetryDataInitialized = false;
			_tickCountLastFrame = null;
			_velocityLastFrame = null;
			_lastPedalUpdateFrame = null;

			app.RacingWheel.UseSteeringWheelTorqueData = false;
			app.RacingWheel.SuspendForceFeedback = true;
			app.MultimediaTimer.Suspend = true;

			app.Dispatcher.BeginInvoke( () =>
			{
				app.MainWindow.Graphs_Native60HzTorque_SimulatorNotRunning_Label.Visibility = Visibility.Visible;
				app.MainWindow.Graphs_Native360HzTorque_SimulatorNotRunning_Label.Visibility = Visibility.Visible;
				app.MainWindow.Simulator_SimulatorNotRunning_Label.Visibility = Visibility.Visible;
			} );

			app.Logger.WriteLine( "[Simulator] <<< OnDisconnected" );
		}
	}

	private void OnSessionInfo()
	{
		var sessionInfo = _irsdk.Data.SessionInfo;

		NumForwardGears = sessionInfo.DriverInfo.DriverCarGearNumForward;
		ShiftLightsShiftRPM = sessionInfo.DriverInfo.DriverCarSLShiftRPM;
		SimMode = sessionInfo.WeekendInfo.SimMode;

		foreach ( var driver in _irsdk.Data.SessionInfo.DriverInfo.Drivers )
		{
			if ( driver.CarIdx == _irsdk.Data.SessionInfo.DriverInfo.DriverCarIdx )
			{
				CarScreenName = driver.CarScreenName ?? string.Empty;
				break;
			}
		}

		TrackDisplayName = _irsdk.Data.SessionInfo.WeekendInfo.TrackDisplayName ?? string.Empty;
		TrackConfigName = _irsdk.Data.SessionInfo.WeekendInfo.TrackConfigName ?? string.Empty;

		if ( _needToUpdateFromContextSettings )
		{
			DataContext.DataContext.Instance.Settings.UpdateFromContextSettings();

			_needToUpdateFromContextSettings = false;
		}

		var app = App.Instance;

		if ( app != null )
		{
			app.MainWindow.UpdateStatus();
		}
	}

	private void OnTelemetryData()
	{
		var app = App.Instance;

		if ( app != null )
		{
			// initialize telemetry data properties

			if ( !_telemetryDataInitialized )
			{
				_brakeABSactiveDatum = _irsdk.Data.TelemetryDataProperties[ "BrakeABSactive" ];
				_brakeDatum = _irsdk.Data.TelemetryDataProperties[ "Brake" ];
				_clutchDatum = _irsdk.Data.TelemetryDataProperties[ "Clutch" ];
				_gearDatum = _irsdk.Data.TelemetryDataProperties[ "Gear" ];
				_isOnTrackDatum = _irsdk.Data.TelemetryDataProperties[ "IsOnTrack" ];
				_playerTrackSurfaceDatum = _irsdk.Data.TelemetryDataProperties[ "PlayerTrackSurface" ];
				_rpmDatum = _irsdk.Data.TelemetryDataProperties[ "RPM" ];
				_steeringFFBEnabledDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringFFBEnabled" ];
				_steeringWheelAngleDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringWheelAngle" ];
				_steeringWheelAngleMaxDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringWheelAngleMax" ];
				_steeringWheelTorque_STDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringWheelTorque_ST" ];
				_throttleDatum = _irsdk.Data.TelemetryDataProperties[ "Throttle" ];
				_velocityXDatum = _irsdk.Data.TelemetryDataProperties[ "VelocityX" ];
				_velocityYDatum = _irsdk.Data.TelemetryDataProperties[ "VelocityY" ];
				_weatherDeclaredWetDatum = _irsdk.Data.TelemetryDataProperties[ "WeatherDeclaredWet" ];

				_cfShockVel_STDatum = null;
				_crShockVel_STDatum = null;
				_lfShockVel_STDatum = null;
				_lrShockVel_STDatum = null;
				_rfShockVel_STDatum = null;
				_rrShockVel_STDatum = null;

				_irsdk.Data.TelemetryDataProperties.TryGetValue( "CFshockVel_ST", out _cfShockVel_STDatum );
				_irsdk.Data.TelemetryDataProperties.TryGetValue( "CRshockVel_ST", out _crShockVel_STDatum );
				_irsdk.Data.TelemetryDataProperties.TryGetValue( "LRshockVel_ST", out _lfShockVel_STDatum );
				_irsdk.Data.TelemetryDataProperties.TryGetValue( "LRshockVel_ST", out _lrShockVel_STDatum );
				_irsdk.Data.TelemetryDataProperties.TryGetValue( "RFshockVel_ST", out _rfShockVel_STDatum );
				_irsdk.Data.TelemetryDataProperties.TryGetValue( "RRshockVel_ST", out _rrShockVel_STDatum );

				_telemetryDataInitialized = true;
			}

			// set last frame tick count if its not been set yet

			_tickCountLastFrame ??= _irsdk.Data.TickCount - 1;

			// calculate delta time

			var deltaSeconds = (float) ( _irsdk.Data.TickCount - (int) _tickCountLastFrame ) / _irsdk.Data.TickRate;

			// update tick count last frame

			_tickCountLastFrame = _irsdk.Data.TickCount;

			// protect ourselves from zero or negative time just in case

			if ( deltaSeconds <= 0f )
			{
				return;
			}

			// update brake abs active

			BrakeABSactive = _irsdk.Data.GetBool( _brakeABSactiveDatum );

			// update clutch, brake, throttle

			Clutch = _irsdk.Data.GetFloat( _clutchDatum );
			Brake = _irsdk.Data.GetFloat( _brakeDatum );
			Throttle = _irsdk.Data.GetFloat( _throttleDatum );

			// update rpm

			RPM = _irsdk.Data.GetFloat( _rpmDatum );

			// update was / is on track status

			WasOnTrack = IsOnTrack;

			IsOnTrack = _irsdk.Data.GetBool( _isOnTrackDatum );

			app.RacingWheel.UseSteeringWheelTorqueData = IsOnTrack;

			// suspend racing wheel force feedback if iracing ffb is enabled

			SteeringFFBEnabled = _irsdk.Data.GetBool( _steeringFFBEnabledDatum );

			app.RacingWheel.SuspendForceFeedback = SteeringFFBEnabled;

			// get the player track surface

			PlayerTrackSurface = (IRacingSdkEnum.TrkLoc) _irsdk.Data.GetInt( _playerTrackSurfaceDatum );

			// get steering wheel angle and max angle

			SteeringWheelAngle = _irsdk.Data.GetFloat( _steeringWheelAngleDatum );
			SteeringWheelAngleMax = _irsdk.Data.GetFloat( _steeringWheelAngleMaxDatum );

			// get gear

			Gear = _irsdk.Data.GetInt( _gearDatum );

			// get next 360 Hz steering wheel torque samples

			_irsdk.Data.GetFloatArray( _steeringWheelTorque_STDatum, SteeringWheelTorque_ST, 0, SteeringWheelTorque_ST.Length );

			app.RacingWheel.UpdateSteeringWheelTorqueBuffer = true;

			// get car body velocity

			VelocityX = _irsdk.Data.GetFloat( _velocityXDatum );
			VelocityY = _irsdk.Data.GetFloat( _velocityYDatum );

			Velocity = MathF.Sqrt( VelocityX * VelocityX + VelocityY * VelocityY );

			app.Debug.Label_1 = $"Velocity = {app.Simulator.Velocity:F2} m/s";

			// get weather declared wet and reload settings if it was changed

			WeatherDeclaredWet = _irsdk.Data.GetBool( _weatherDeclaredWetDatum );

			if ( _weatherDeclaredWetLastFrame != null )
			{
				if ( WeatherDeclaredWet != _weatherDeclaredWetLastFrame )
				{
					if ( !_needToUpdateFromContextSettings )
					{
						DataContext.DataContext.Instance.Settings.UpdateFromContextSettings();
					}
				}
			}

			_weatherDeclaredWetLastFrame = WeatherDeclaredWet;

			// calculate g force

			if ( _velocityLastFrame != null )
			{
				GForce = MathF.Abs( Velocity - (float) _velocityLastFrame ) / deltaSeconds / ONE_G;
			}
			else
			{
				GForce = 0f;
			}

			app.Debug.Label_2 = $"GForce = {app.Simulator.GForce:F2} g";

			// crash protection processing

			if ( ( DataContext.DataContext.Instance.Settings.RacingWheelCrashProtectionGForce > 0f ) && ( DataContext.DataContext.Instance.Settings.RacingWheelCrashProtectionDuration > 0f ) && ( DataContext.DataContext.Instance.Settings.RacingWheelCrashProtectionForceReduction > 0f ) )
			{
				if ( MathF.Abs( GForce ) >= DataContext.DataContext.Instance.Settings.RacingWheelCrashProtectionGForce )
				{
					app.RacingWheel.ActivateCrashProtection = true;
				}
			}

			// get next 360 Hz shock velocity samples

			if ( _cfShockVel_STDatum != null )
			{
				_irsdk.Data.GetFloatArray( _cfShockVel_STDatum, CFShockVel_ST, 0, CFShockVel_ST.Length );
			}

			if ( _crShockVel_STDatum != null )
			{
				_irsdk.Data.GetFloatArray( _crShockVel_STDatum, CRShockVel_ST, 0, CRShockVel_ST.Length );
			}

			if ( _lfShockVel_STDatum != null )
			{
				_irsdk.Data.GetFloatArray( _lfShockVel_STDatum, LFShockVel_ST, 0, LFShockVel_ST.Length );
			}

			if ( _lrShockVel_STDatum != null )
			{
				_irsdk.Data.GetFloatArray( _lrShockVel_STDatum, LRShockVel_ST, 0, LRShockVel_ST.Length );
			}

			if ( _rfShockVel_STDatum != null )
			{
				_irsdk.Data.GetFloatArray( _rfShockVel_STDatum, RFShockVel_ST, 0, RFShockVel_ST.Length );
			}

			if ( _rrShockVel_STDatum != null )
			{
				_irsdk.Data.GetFloatArray( _rrShockVel_STDatum, RRShockVel_ST, 0, RRShockVel_ST.Length );
			}

			// curb protection processing

			if ( ( DataContext.DataContext.Instance.Settings.RacingWheelCurbProtectionShockVelocity > 0f ) && ( DataContext.DataContext.Instance.Settings.RacingWheelCurbProtectionDuration > 0f ) && ( DataContext.DataContext.Instance.Settings.RacingWheelCurbProtectionForceReduction > 0f ) )
			{
				var maxShockVelocity = 0f;

				for ( var i = 0; i < IRSDK_360HZ_SAMPLES_PER_FRAME; i++ )
				{
					maxShockVelocity = MathF.Max( maxShockVelocity, MathF.Abs( CFShockVel_ST[ i ] ) );
					maxShockVelocity = MathF.Max( maxShockVelocity, MathF.Abs( CRShockVel_ST[ i ] ) );
					maxShockVelocity = MathF.Max( maxShockVelocity, MathF.Abs( LFShockVel_ST[ i ] ) );
					maxShockVelocity = MathF.Max( maxShockVelocity, MathF.Abs( LRShockVel_ST[ i ] ) );
					maxShockVelocity = MathF.Max( maxShockVelocity, MathF.Abs( RFShockVel_ST[ i ] ) );
					maxShockVelocity = MathF.Max( maxShockVelocity, MathF.Abs( RRShockVel_ST[ i ] ) );
				}

				app.Debug.Label_8 = $"maxShockVelocity = {maxShockVelocity:F2} m/s";

				if ( maxShockVelocity >= DataContext.DataContext.Instance.Settings.RacingWheelCurbProtectionShockVelocity )
				{
					app.RacingWheel.ActivateCurbProtection = true;
				}
			}

			// save values for the next frame

			_velocityLastFrame = Velocity;

			// poll direct input devices

			app.DirectInput.PollDevices( deltaSeconds );

			// update pedals at 20 fps

			if ( ( _lastPedalUpdateFrame == null ) || ( _irsdk.Data.TickCount >= ( _lastPedalUpdateFrame + 3 ) ) )
			{
				_lastPedalUpdateFrame = _irsdk.Data.TickCount;

				app.Pedals.Update( app );
			}

			// update statistics and graphs

			if ( app.MainWindow.GraphsTabItemIsVisible )
			{
				_native60HzTorqueStatistics.Update( SteeringWheelTorque_ST[ 5 ] );

				var y60Hz = SteeringWheelTorque_ST[ 5 ] / DataContext.DataContext.Instance.Settings.RacingWheelMaxForce;

				_native60HzTorqueGraph.DrawGradientLine( y60Hz, 255, 0, 0 );
				_native60HzTorqueGraph.Advance();

				for ( var i = 0; i < SteeringWheelTorque_ST.Length; i++ )
				{
					_native360HzTorqueStatistics.Update( SteeringWheelTorque_ST[ i ] );

					var y360Hz = SteeringWheelTorque_ST[ i ] / DataContext.DataContext.Instance.Settings.RacingWheelMaxForce;

					_native360HzTorqueGraph.DrawGradientLine( y360Hz, 255, 0, 0 );
					_native360HzTorqueGraph.Advance();
				}
			}

			// temporary code for Alan Le

			for ( var i = 0; i < SteeringWheelTorque_ST.Length; i++ )
			{
				app.Debug.AddFFBSample( _irsdk.Data.TickCount * 6 + i, SteeringWheelTorque_ST[ i ] );
			}

			// trigger the app worker thread

			app.TriggerWorkerThread();
		}
	}

	private void OnDebugLog( string message )
	{
		var app = App.Instance;

		app?.Logger.WriteLine( $"[IRSDKSharper] {message}" );
	}

	public void Tick( App app )
	{
		app.MainWindow.RacingWheel_CurrentForce_Label.Content = $"{MathF.Abs( SteeringWheelTorque_ST[ 5 ] ):F2}{DataContext.DataContext.Instance.Localization[ "TorqueUnits" ]}";

		if ( app.MainWindow.GraphsTabItemIsVisible )
		{
			_native60HzTorqueGraph.UpdateImage();

			app.MainWindow.Graphs_Native60HzTorque_MinMaxAvg.Content = $"{_native60HzTorqueStatistics.MinimumValue,5:F2} {_native60HzTorqueStatistics.MaximumValue,5:F2} {_native60HzTorqueStatistics.AverageValue,5:F2}";
			app.MainWindow.Graphs_Native60HzTorque_VarStdDev.Content = $"{_native60HzTorqueStatistics.Variance,5:F2} {_native60HzTorqueStatistics.StandardDeviation,5:F2}";

			_native360HzTorqueGraph.UpdateImage();

			app.MainWindow.Graphs_Native360HzTorque_MinMaxAvg.Content = $"{_native360HzTorqueStatistics.MinimumValue,5:F2} {_native360HzTorqueStatistics.MaximumValue,5:F2} {_native360HzTorqueStatistics.AverageValue,5:F2}";
			app.MainWindow.Graphs_Native360HzTorque_VarStdDev.Content = $"{_native360HzTorqueStatistics.Variance,5:F2} {_native360HzTorqueStatistics.StandardDeviation,5:F2}";
		}
	}
}
