
using System.Windows;

using Image = System.Windows.Controls.Image;

using IRSDKSharper;

namespace MarvinsAIRARefactored.Components;

public class Simulator
{
	public const int IRSDK_360HZ_SAMPLES_PER_FRAME = 6;

	private readonly IRacingSdk _irsdk = new();

	public IRacingSdk IRSDK { get => _irsdk; }

	public bool IsConnected { get => _irsdk.IsConnected; }
	public bool IsOnTrack { get; private set; } = false;
	public bool OnPitRoad { get; private set; } = false;
	public IRacingSdkEnum.TrkLoc PlayerTrackSurface { get; private set; } = IRacingSdkEnum.TrkLoc.NotInWorld;
	public bool SteeringFFBEnabled { get; private set; } = false;
	public float SteeringWheelAngle { get; private set; } = 0f;
	public float SteeringWheelAngleMax { get; private set; } = 0f;
	public float[] SteeringWheelTorque_ST { get; private set; } = new float[ IRSDK_360HZ_SAMPLES_PER_FRAME ];
	public float Velocity { get; private set; } = 0f;
	public float VelocityX { get; private set; } = 0f;
	public float VelocityY { get; private set; } = 0f;

	private bool _telemetryDataInitialized = false;
	private int? _tickCountLastFrame = null;

	private IRacingSdkDatum? _isOnTrackDatum = null;
	private IRacingSdkDatum? _onPitRoadDatum = null;
	private IRacingSdkDatum? _playerTrackSurfaceDatum = null;
	private IRacingSdkDatum? _steeringFFBEnabledDatum = null;
	private IRacingSdkDatum? _steeringWheelAngleDatum = null;
	private IRacingSdkDatum? _steeringWheelAngleMaxDatum = null;
	private IRacingSdkDatum? _steeringWheelTorque_STDatum = null;
	private IRacingSdkDatum? _velocityXDatum = null;
	private IRacingSdkDatum? _velocityYDatum = null;

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
	}

	private void OnTelemetryData()
	{
		var app = App.Instance;

		if ( app != null )
		{
			// initialize telemetry data properties

			if ( !_telemetryDataInitialized )
			{
				_isOnTrackDatum = _irsdk.Data.TelemetryDataProperties[ "IsOnTrack" ];
				_onPitRoadDatum = _irsdk.Data.TelemetryDataProperties[ "OnPitRoad" ];
				_playerTrackSurfaceDatum = _irsdk.Data.TelemetryDataProperties[ "PlayerTrackSurface" ];
				_steeringFFBEnabledDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringFFBEnabled" ];
				_steeringWheelAngleDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringWheelAngle" ];
				_steeringWheelAngleMaxDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringWheelAngleMax" ];
				_steeringWheelTorque_STDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringWheelTorque_ST" ];
				_velocityXDatum = _irsdk.Data.TelemetryDataProperties[ "VelocityX" ];
				_velocityYDatum = _irsdk.Data.TelemetryDataProperties[ "VelocityY" ];

				_telemetryDataInitialized = true;
			}

			// get is on track status

			IsOnTrack = _irsdk.Data.GetBool( _isOnTrackDatum );

			app.RacingWheel.UseSteeringWheelTorqueData = IsOnTrack;

			// get on pit road status

			OnPitRoad = _irsdk.Data.GetBool( _onPitRoadDatum );

			// suspend racing wheel force feedback if iracing ffb is enabled

			SteeringFFBEnabled = _irsdk.Data.GetBool( _steeringFFBEnabledDatum );

			app.RacingWheel.SuspendForceFeedback = SteeringFFBEnabled;

			// get the player track surface

			PlayerTrackSurface = (IRacingSdkEnum.TrkLoc) _irsdk.Data.GetInt( _playerTrackSurfaceDatum );

			// get steering wheel angle and max angle

			SteeringWheelAngle = _irsdk.Data.GetFloat( _steeringWheelAngleDatum );
			SteeringWheelAngleMax = _irsdk.Data.GetFloat( _steeringWheelAngleMaxDatum );

			// get next 360 Hz steering wheel torque samples

			_irsdk.Data.GetFloatArray( _steeringWheelTorque_STDatum, SteeringWheelTorque_ST, 0, SteeringWheelTorque_ST.Length );

			app.RacingWheel.UpdateSteeringWheelTorqueBuffer = true;

			// get car body velocity

			VelocityX = _irsdk.Data.GetFloat( _velocityXDatum );
			VelocityY = _irsdk.Data.GetFloat( _velocityYDatum );

			Velocity = MathF.Sqrt( VelocityX * VelocityX + VelocityY * VelocityY );

			// set last frame tick count if its not been set yet

			_tickCountLastFrame ??= _irsdk.Data.TickCount - 1;

			// poll direct input devices

			app.DirectInput.PollDevices( (float) ( _irsdk.Data.TickCount - (int) _tickCountLastFrame ) / _irsdk.Data.TickRate );

			// update tick count last frame

			_tickCountLastFrame = _irsdk.Data.TickCount;

			// update statistics and graphs

			if ( app.MainWindow.GraphsTabItemIsVisible )
			{
				_native60HzTorqueStatistics.Update( SteeringWheelTorque_ST[ 5 ] );

				var y60Hz = SteeringWheelTorque_ST[ 5 ] / DataContext.Instance.Settings.RacingWheelMaxForce;

				_native60HzTorqueGraph.DrawGradientLine( y60Hz, 255, 0, 0 );
				_native60HzTorqueGraph.Advance();

				for ( var i = 0; i < SteeringWheelTorque_ST.Length; i++ )
				{
					_native360HzTorqueStatistics.Update( SteeringWheelTorque_ST[ i ] );

					var y360Hz = SteeringWheelTorque_ST[ i ] / DataContext.Instance.Settings.RacingWheelMaxForce;

					_native360HzTorqueGraph.DrawGradientLine( y360Hz, 255, 0, 0 );
					_native360HzTorqueGraph.Advance();
				}
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
		app.MainWindow.RacingWheel_CurrentForce_Label.Content = $"{MathF.Abs( SteeringWheelTorque_ST[ 5 ] ):F2}{DataContext.Instance.Localization[ "TorqueUnits" ]}";

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
