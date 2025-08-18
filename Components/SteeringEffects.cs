
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

using Accord.Statistics.Models.Regression.Linear;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;

using Label = System.Windows.Controls.Label;

namespace MarvinsAIRARefactored.Components;

public class SteeringEffects
{
	public static string CalibrationDirectory { get; private set; } = Path.Combine( App.DocumentsFolder, "Calibration" );

	public bool IsUndersteering { get; private set; } = false;
	public float UndersteerEffectFactor { get; private set; } = 0f;

	public float MaximumGrip { get; private set; } = 0f;
	public float WarningGrip { get; private set; } = 0f;
	public float CurrentGrip { get; private set; } = 0f;

	private enum CalibrationPhase
	{
		NotCalibrating,
		ResetCalibration,
		DriveToWallEdge,
		WarmUpTires,
		DriveToActiveResetPoint,
		TurningPasses,
		Stop
	}

	private enum RobotMode
	{
		DriveToTarget,
		FixedSteeringWheelAngle
	}

	private enum RobotGearMode
	{
		NotShiftingGears,
		Decelerating,
		Accelerating,
		CoolingDown
	}

	private const float KPHToMPS = 5f / 18f;
	private const float MPSToKPH = 18f / 5f;
	private const float RadiansToDegrees = 180f / MathF.PI;
	private const float DegreesToRadians = MathF.PI / 180f;

	private const float MapScale = 1.225f;

	private const float CarHomePositionX = 0f;
	private const float CarHomePositionY = -5.6f;

	private const float WarmUpTiresDrivingRadius = 190f;

	private const float ActiveResetSavePointX = 0f;
	private const float ActiveResetSavePointY = 100f;

	private const int MaxSpeedInKPH = 250;
	private const int MaxSteeringWheelAngleInDegrees = 180;
	private const int MaxNumSteeringWheelAngles = 17;
	private const int MaxCalibrationProgress = 2 + MaxNumSteeringWheelAngles; // not including warm up lap count

	private const float AbsYawRateSpikeThreshold = 0.025f;

	private const int SteeringWheelAngleIncrement = 10;

	private const int PolynomialDegrees = 4;

	private CalibrationPhase _calibrationPhase = CalibrationPhase.NotCalibrating;
	private int _calibrationProgress = 0;

	private int _currentWarmUpLapNumber = 0;

	private float _targetPositionX = 0f;
	private float _targetPositionY = 0f;
	private int _targetSteeringWheelAngleInDegrees = 0;
	private int _targetVelocityInKPH = 0;
	private int _targetAccelerationInKPH = 0;
	private float _targetDistanceToStop = 0f;

	private RobotMode _robotMode = RobotMode.DriveToTarget;
	private RobotGearMode _robotGearMode = RobotGearMode.NotShiftingGears;

	private float _robotSettleTimer = 0f;
	private float _robotSteeringWheelAngleInDegrees = 0f;
	private float _robotBrake = 0f;
	private float _robotThrottle = 0f;
	private float _robotLastFrameVelocityX = 0f;
	private float _robotGearShiftCoolDownTimer = 0f;

	private int _numSteeringWheelAnglesRecorded = 0;

	private float _maxAbsYawRateInDegrees = 0f;

	private readonly int[] _steeringWheelAnglesInDegrees = new int[ MaxNumSteeringWheelAngles ];
	private readonly float[,] _yawRateDataInDegrees = new float[ MaxNumSteeringWheelAngles, MaxSpeedInKPH + 1 ];

	private float _carResetPositionX = CarHomePositionX;
	private float _carResetPositionY = CarHomePositionY;

	private float _carPositionX = 0f;
	private float _carPositionY = 0f;

	private bool _calibrationIsValid = false;

	private MultipleLinearRegression? _multipleLinearRegression = null;

	private float _maximumPredictedLogMaxYawRateFactor = 0f;
	private float _minimumPredictedLogMaxYawRateFactor = 0f;

	private string? _currentlyActiveCarScreenName = null;
	private string _currentlyActiveCalibrationFileName = string.Empty;

	public void SetMairaComboBoxItemsSources()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffects] SetMairaComboBoxItemsSources >>>" );

		if ( ( _currentlyActiveCarScreenName == null ) || ( app.Simulator.CarScreenName != _currentlyActiveCarScreenName ) )
		{
			_currentlyActiveCarScreenName = app.Simulator.CarScreenName;

			SetMairaComboBoxItemsSource( 1 );
			SetMairaComboBoxItemsSource( 2 );
			SetMairaComboBoxItemsSource( 3 );
		}

		app.Logger.WriteLine( "[SteeringEffects] <<< SetMairaComboBoxItemsSources" );
	}

	private static void SetMairaComboBoxItemsSource( int tireIndex )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffects] SetMairaComboBoxItemsSource >>>" );

		var settings = DataContext.DataContext.Instance.Settings;
		var propertyName = $"SteeringEffectsUndersteerCalibrationFileName{tireIndex}";
		var steeringEffectsUndersteerCalibrationFileNamePropertyInfo = settings.GetType().GetProperty( propertyName )!;

		if ( ( app.Simulator.AvailableTires != null ) && ( app.Simulator.AvailableTires.Count >= tireIndex ) )
		{
			var localization = DataContext.DataContext.Instance.Localization;

			var dictionary = new Dictionary<string, string>()
			{
				{ string.Empty, localization["CalibrationFileNotSelected"] }
			};

			foreach ( var filePath in Directory.GetFiles( CalibrationDirectory, $"{app.Simulator.CarScreenName} - *.csv" ) )
			{
				var option = Path.GetFileNameWithoutExtension( filePath );

				dictionary.Add( option, option );
			}

			var calibrationFileName = (string) steeringEffectsUndersteerCalibrationFileNamePropertyInfo.GetValue( settings )!;

			if ( !dictionary.ContainsKey( calibrationFileName ) )
			{
				calibrationFileName = string.Empty;

				steeringEffectsUndersteerCalibrationFileNamePropertyInfo.SetValue( settings, calibrationFileName );
			}

			app.Dispatcher.BeginInvoke( () =>
			{
				// set label

				var label = (Label) app.MainWindow.FindName( $"SteeringEffects_UndersteerCalibrationFileName{tireIndex}_Label" );

				label.Content = app.Simulator.AvailableTires[ tireIndex - 1 ].TireCompoundType.ToUpper();
				label.Visibility = Visibility.Visible;

				// set option

				var mairaComboBox = (MairaComboBox) app.MainWindow.FindName( $"SteeringEffects_UndersteerCalibrationFileName{tireIndex}_ComboBox" );

				mairaComboBox.ItemsSource = dictionary;
				mairaComboBox.SelectedValue = calibrationFileName;
				mairaComboBox.Visibility = Visibility.Visible;
			} );
		}
		else
		{
			steeringEffectsUndersteerCalibrationFileNamePropertyInfo.SetValue( settings, string.Empty );

			app.Dispatcher.BeginInvoke( () =>
			{
				// set label

				var label = (Label) app.MainWindow.FindName( $"SteeringEffects_UndersteerCalibrationFileName{tireIndex}_Label" );

				label.Content = string.Empty;
				label.Visibility = Visibility.Collapsed;

				// set option

				var mairaComboBox = (MairaComboBox) app.MainWindow.FindName( $"SteeringEffects_UndersteerCalibrationFileName{tireIndex}_ComboBox" );

				mairaComboBox.SelectedValue = string.Empty;
				mairaComboBox.Visibility = Visibility.Collapsed;
			} );
		}

		app.Logger.WriteLine( "[SteeringEffects] <<< SetMairaComboBoxItemsSource" );
	}

	public void RunCalibration()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffects] RunCalibration >>>" );

		// start at the very beginning

		_calibrationPhase = CalibrationPhase.ResetCalibration;

		app.Logger.WriteLine( "[SteeringEffects] <<< RunCalibration" );
	}

	public void StopCalibration( bool saveCalibration )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffects] StopCalibration >>>" );

		// whoa, nelly!

		_calibrationPhase = CalibrationPhase.Stop;

		if ( saveCalibration )
		{
			// save the calibration data

			SaveCalibration();

			// clear the calibration data

			ClearCalibration();

			// reload the calibration data

			LoadCalibration();
		}

		app.Logger.WriteLine( "[SteeringEffects] <<< StopCalibration" );
	}

	public void Update( App app, float deltaSeconds )
	{
		if ( _calibrationPhase == CalibrationPhase.NotCalibrating )
		{
			if ( _calibrationIsValid )
			{
				var settings = DataContext.DataContext.Instance.Settings;

				var absSteeringWheelAngleInDegrees = Math.Min( MaxSteeringWheelAngleInDegrees, MathF.Abs( app.Simulator.SteeringWheelAngle * RadiansToDegrees ) );

				// predicted log max yaw rate factor (approx -0.2 to exactly 5.525)

				var predictedLogMaxYawRateFactor = PredictLogMaxYawRateFactor( -absSteeringWheelAngleInDegrees );

				// apply minimum and offset (exactly 1 to approx 6.725)

				predictedLogMaxYawRateFactor = ( 1f + predictedLogMaxYawRateFactor - _minimumPredictedLogMaxYawRateFactor );

				// determine peak and warning log yaw rate factors

				var peakLogYawRateFactor = predictedLogMaxYawRateFactor * settings.SteeringEffectsUndersteerThreshold;
				var warnLogYawRateFactor = predictedLogMaxYawRateFactor * settings.SteeringEffectsUndersteerWarningThreshold;

				// determine our current log yaw rate factor

				var speedInKPH = MathF.Max( app.Simulator.VelocityX * MPSToKPH, 0f );
				var yawRateInDegrees = app.Simulator.YawRate * RadiansToDegrees;
				var absYawRateInDegrees = MathF.Abs( yawRateInDegrees );
				var currentLogYawRateFactor = MathF.Log( ( speedInKPH + 1f ) / ( absYawRateInDegrees + 1f ) );

				// apply minimum and offset

				currentLogYawRateFactor = ( 1f + currentLogYawRateFactor - _minimumPredictedLogMaxYawRateFactor );

				// override current log yaw rate factor if we are parked

				if ( app.Simulator.VelocityX < 2.2352f )
				{
					currentLogYawRateFactor = 0f;
				}

				// update grip-o-meter properties

				MaximumGrip = Misc.Lerp( 0.5f, 1f, settings.SteeringEffectsUndersteerThreshold * ( ( predictedLogMaxYawRateFactor - _minimumPredictedLogMaxYawRateFactor ) / ( _maximumPredictedLogMaxYawRateFactor - _minimumPredictedLogMaxYawRateFactor ) ) );
				WarningGrip = ( warnLogYawRateFactor / peakLogYawRateFactor ) * MaximumGrip;
				CurrentGrip = ( currentLogYawRateFactor / peakLogYawRateFactor ) * MaximumGrip;

				// don't do the understeer effect if we aren't turning in the same direction as the wheel

				if ( MathF.Sign( app.Simulator.SteeringWheelAngle ) == MathF.Sign( app.Simulator.YawRate ) )
				{
					// are we understeering?

					IsUndersteering = ( currentLogYawRateFactor > peakLogYawRateFactor );

					// calculate understeer effect factor

					var logYawRateFactorRange = peakLogYawRateFactor - warnLogYawRateFactor;

					if ( logYawRateFactorRange > 0f )
					{
						UndersteerEffectFactor = Math.Clamp( ( currentLogYawRateFactor - warnLogYawRateFactor ) / logYawRateFactorRange, 0f, 1f );
					}
					else
					{
						UndersteerEffectFactor = IsUndersteering ? 1f : 0f;
					}
				}
				else
				{
					IsUndersteering = false;
					UndersteerEffectFactor = 0f;
				}

				// debug

				app.Debug.Label_1 = $"MaximumGrip: {MaximumGrip * 100f:F0}";
				app.Debug.Label_2 = $"WarningGrip: {WarningGrip * 100f:F0}";
				app.Debug.Label_3 = $"CurrentGrip: {CurrentGrip * 100f:F0}";
				app.Debug.Label_5 = $"UndersteerEffectFactor: {UndersteerEffectFactor * 100f:F0}";
			}
			else
			{
				CurrentGrip = 0f;
				IsUndersteering = false;
				UndersteerEffectFactor = 0f;
			}
		}
		else
		{
			switch ( _calibrationPhase )
			{
				case CalibrationPhase.ResetCalibration:
					DoResetCalibration( app, deltaSeconds );
					break;

				case CalibrationPhase.DriveToWallEdge:
					DoDriveToWallEdge( app, deltaSeconds );
					break;

				case CalibrationPhase.WarmUpTires:
					DoWarmUpTires( app, deltaSeconds );
					break;

				case CalibrationPhase.DriveToActiveResetPoint:
					DoDriveActiveResetSavePoint( app, deltaSeconds );
					break;

				case CalibrationPhase.TurningPasses:
					DoTurningPasses( app, deltaSeconds );
					break;

				case CalibrationPhase.Stop:
					DoStop( app, deltaSeconds );
					break;
			}

			var worldVelocityX = app.Simulator.VelocityX * MathF.Sin( app.Simulator.YawNorth ) - app.Simulator.VelocityY * MathF.Cos( app.Simulator.YawNorth );
			var worldVelocityY = app.Simulator.VelocityX * MathF.Cos( app.Simulator.YawNorth ) + app.Simulator.VelocityY * MathF.Sin( app.Simulator.YawNorth );

			_carPositionX += worldVelocityX * deltaSeconds;
			_carPositionY += worldVelocityY * deltaSeconds;

			CurrentGrip = 0f;
			IsUndersteering = false;
			UndersteerEffectFactor = 0f;
		}
	}

	private float PredictNearestDistanceToTarget( App app, float yawRate, float deltaSeconds )
	{
		var posX = _carPositionX;
		var posY = _carPositionY;

		var nearestDistance = float.MaxValue;

		var yawNorth = app.Simulator.YawNorth;

		var maxLoops = (int) ( 20f / deltaSeconds );

		for ( var i = 0; i < maxLoops; i++ )
		{
			yawNorth -= yawRate * deltaSeconds;

			yawRate *= 0.9985f;

			posX += ( app.Simulator.VelocityX * MathF.Sin( yawNorth ) - app.Simulator.VelocityY * MathF.Cos( yawNorth ) ) * deltaSeconds;
			posY += ( app.Simulator.VelocityX * MathF.Cos( yawNorth ) + app.Simulator.VelocityY * MathF.Sin( yawNorth ) ) * deltaSeconds;

			var deltaX = _targetPositionX - posX;
			var deltaY = _targetPositionY - posY;

			var distance = MathF.Sqrt( deltaX * deltaX + deltaY * deltaY );

			if ( distance > nearestDistance )
			{
				break;
			}
			else
			{
				nearestDistance = distance;
			}
		}

		return nearestDistance;
	}

	private void ResetRobot()
	{
		_robotSettleTimer = 0f;
		_robotSteeringWheelAngleInDegrees = 0f;
		_robotBrake = 0f;
		_robotThrottle = 0f;
		_robotLastFrameVelocityX = 0f;
		_robotGearShiftCoolDownTimer = 0f;
	}

	private void UpdateRobot( App app, float deltaSeconds )
	{
		var currentAccelerationInMPS = ( app.Simulator.VelocityX - _robotLastFrameVelocityX ) / deltaSeconds; // in m*s^2

		if ( _robotSettleTimer < 1f )
		{
			// let the car settle before driving

			_robotSettleTimer = Math.Min( _robotSettleTimer + deltaSeconds, 1f );

			_robotSteeringWheelAngleInDegrees = 0f;
			_robotBrake = 0f;
			_robotThrottle = 0f;

			_carPositionX = _carResetPositionX;
			_carPositionY = _carResetPositionY;
		}
		else
		{
			// shift gears

			switch ( _robotGearMode )
			{
				case RobotGearMode.NotShiftingGears:
				{
					if ( ( app.Simulator.Gear == 0 ) || ( app.Simulator.RPM >= ( app.Simulator.ShiftLightsShiftRPM * 0.9f ) ) )
					{
						if ( _robotMode == RobotMode.DriveToTarget )
						{
							_robotGearMode = RobotGearMode.CoolingDown;
							_robotGearShiftCoolDownTimer = 2f;

							app.VirtualJoystick.ShiftUp = true;
						}
						else
						{
							_robotGearMode = RobotGearMode.Decelerating;
						}
					}
					else if ( ( app.Simulator.Gear > 1 ) && ( app.Simulator.RPM < ( app.Simulator.ShiftLightsShiftRPM * 0.3f ) ) )
					{
						_robotGearMode = RobotGearMode.CoolingDown;
						_robotGearShiftCoolDownTimer = 1f;

						app.VirtualJoystick.ShiftDown = true;
					}

					break;
				}

				case RobotGearMode.Decelerating:
				{
					_targetAccelerationInKPH = -1;

					if ( ( app.Simulator.VelocityX * MPSToKPH ) <= ( _targetVelocityInKPH - 8 ) )
					{
						app.VirtualJoystick.ShiftUp = true;

						_robotGearMode = RobotGearMode.Accelerating;
					}

					break;
				}

				case RobotGearMode.Accelerating:
				{
					_targetAccelerationInKPH = 1;

					if ( ( app.Simulator.VelocityX * MPSToKPH ) >= ( _targetVelocityInKPH - 1 ) )
					{
						_robotGearMode = RobotGearMode.NotShiftingGears;
					}

					break;
				}

				case RobotGearMode.CoolingDown:
				{
					_robotGearShiftCoolDownTimer = MathF.Max( 0f, _robotGearShiftCoolDownTimer - deltaSeconds );

					if ( _robotGearShiftCoolDownTimer == 0f )
					{
						_robotGearMode = RobotGearMode.NotShiftingGears;
					}

					break;
				}
			}

			// adjust steering wheel

			if ( _robotMode == RobotMode.DriveToTarget )
			{
				var nearestDistanceToTargetTurningLeft = PredictNearestDistanceToTarget( app, app.Simulator.YawRate - 0.005f, deltaSeconds );
				var nearestDistanceToTargetTurningRight = PredictNearestDistanceToTarget( app, app.Simulator.YawRate + 0.005f, deltaSeconds );

				var wheelTurnAmount = ( nearestDistanceToTargetTurningRight - nearestDistanceToTargetTurningLeft ) * 0.15f;

				_robotSteeringWheelAngleInDegrees += Math.Clamp( wheelTurnAmount, -0.25f, 0.25f ) * Math.Min( 1f, app.Simulator.VelocityX * 0.25f );
			}
			else
			{
				var deltaSteeringWheelAngleInDegrees = _targetSteeringWheelAngleInDegrees - _robotSteeringWheelAngleInDegrees;

				if ( MathF.Abs( deltaSteeringWheelAngleInDegrees ) < 0.5f )
				{
					_robotSteeringWheelAngleInDegrees = _targetSteeringWheelAngleInDegrees;
				}
				else
				{
					_robotSteeringWheelAngleInDegrees = Misc.Lerp( _robotSteeringWheelAngleInDegrees, _targetSteeringWheelAngleInDegrees, 0.15f );
				}
			}

			// adjust throttle and brake

			if ( ( _targetVelocityInKPH == 0 ) && ( _targetAccelerationInKPH == 0 ) )
			{
				var currentDistanceToStop = -( ( app.Simulator.VelocityX * app.Simulator.VelocityX ) / ( 2f * currentAccelerationInMPS ) ); // how far will the car go before we come to a complete stop at the current acceleration?

				var deltaDistanceToStop = _targetDistanceToStop - currentDistanceToStop;

				if ( ( currentAccelerationInMPS > 0f ) || ( deltaDistanceToStop < 0f ) )
				{
					_robotBrake += MathF.Min( deltaSeconds / 2f, -deltaDistanceToStop * 0.01f ); // increase brake (take 2 seconds to go from 0% to 100% brake)

					_robotThrottle -= deltaSeconds; // go from 100% to 0% throttle in one second
				}
				else if ( _robotBrake > 0f )
				{
					_robotBrake -= deltaSeconds / 2f; // ease off the brake (take 2 seconds to go from 100% to 0% brake)

					_robotThrottle -= deltaSeconds; // go from 100% to 0% throttle in one second
				}
				else
				{
					_robotThrottle += Math.Clamp( deltaDistanceToStop * 0.01f, -deltaSeconds / 30f, deltaSeconds / 30f );
				}
			}
			else if ( _targetAccelerationInKPH != 0 )
			{
				var deltaAccelerationInKPH = _targetAccelerationInKPH - currentAccelerationInMPS * MPSToKPH;

				_robotThrottle += Math.Clamp( deltaAccelerationInKPH * deltaSeconds, -deltaSeconds / 30f, deltaSeconds / 30f );
			}
			else if ( _targetVelocityInKPH != 0 )
			{
				var deltaTargetVelocityInKPH = _targetVelocityInKPH - app.Simulator.VelocityX * MPSToKPH;

				var targetAccelerationInKPH = Math.Clamp( deltaTargetVelocityInKPH, -3f, 2f ); // -3 to +2 KPH/s is the target acceleration in this mode

				var deltaAccelerationInKPH = targetAccelerationInKPH - currentAccelerationInMPS * MPSToKPH;

				_robotThrottle += Math.Clamp( deltaAccelerationInKPH * deltaSeconds, -deltaSeconds / 30f, deltaSeconds / 30f );
			}
		}

		// update virtual joystick

		_robotSteeringWheelAngleInDegrees = Math.Clamp( _robotSteeringWheelAngleInDegrees, -450f, 450f );
		_robotBrake = Math.Clamp( _robotBrake, 0f, 1f );
		_robotThrottle = Math.Clamp( _robotThrottle, 0f, 1f );
		_robotLastFrameVelocityX = app.Simulator.VelocityX;

		app.VirtualJoystick.Steering = _robotSteeringWheelAngleInDegrees / 450f;
		app.VirtualJoystick.Brake = _robotBrake;
		app.VirtualJoystick.Throttle = _robotThrottle;
	}

	private void DoResetCalibration( App app, float deltaSeconds )
	{
		// reset calibration progress

		_calibrationProgress = 0;

		// clear out our old calibration data

		_numSteeringWheelAnglesRecorded = 0;

		Array.Clear( _steeringWheelAnglesInDegrees );
		Array.Clear( _yawRateDataInDegrees );

		// reset the position of the car

		_carResetPositionX = CarHomePositionX;
		_carResetPositionY = CarHomePositionY;

		// reset robot

		ResetRobot();

		// next phase

		_calibrationPhase = CalibrationPhase.DriveToWallEdge;
	}

	private void DoDriveToWallEdge( App app, float deltaSeconds )
	{
		// set target position and velocity

		_targetPositionX = WarmUpTiresDrivingRadius;
		_targetPositionY = CarHomePositionY;
		_targetVelocityInKPH = 50;
		_targetAccelerationInKPH = 0;
		_targetDistanceToStop = 0f;

		// check if we're getting close and if so, go to the next phase

		if ( _carPositionX >= 50f )
		{
			_calibrationProgress++;

			_currentWarmUpLapNumber = 0;

			_targetPositionY = 1f;

			_calibrationPhase = CalibrationPhase.WarmUpTires;
		}

		// update robot

		UpdateRobot( app, deltaSeconds );
	}

	private void DoWarmUpTires( App app, float deltaSeconds )
	{
		// shortcut to settings

		var settings = DataContext.DataContext.Instance.Settings;

		// get warm up speed and lap count

		var warmUpSpeed = (int) MathF.Round( settings.SteeringEffectsWarmUpSpeed );
		var warmUpLapCount = (int) MathF.Round( settings.SteeringEffectsWarmUpLapCount );

		// if we are on the last warm up lap, override warm up speed to 120 kph

		if ( _currentWarmUpLapNumber == ( warmUpLapCount - 1 ) )
		{
			warmUpSpeed = 120;
		}

		// remember our original target position

		var originalTargetPositionY = _targetPositionY;

		// set target position and velocity

		var distance = MathF.Sqrt( _carPositionX * _carPositionX + _carPositionY * _carPositionY );

		_targetPositionX = _carPositionX * WarmUpTiresDrivingRadius / distance;
		_targetPositionY = _carPositionY * WarmUpTiresDrivingRadius / distance;

		float radians = 20f * DegreesToRadians;

		float cosTheta = MathF.Cos( radians );
		float sinTheta = MathF.Sin( radians );

		float rotatedX = _targetPositionX * cosTheta - _targetPositionY * sinTheta;
		float rotatedY = _targetPositionX * sinTheta + _targetPositionY * cosTheta;

		_targetPositionX = rotatedX;
		_targetPositionY = rotatedY;

		_targetVelocityInKPH = warmUpSpeed;
		_targetAccelerationInKPH = 0;
		_targetDistanceToStop = 0f;

		// check if we are done running warm up laps

		if ( _carPositionX > 0 )
		{
			if ( MathF.Sign( _targetPositionY ) != MathF.Sign( originalTargetPositionY ) )
			{
				_calibrationProgress++;
				_currentWarmUpLapNumber++;

				if ( _currentWarmUpLapNumber >= warmUpLapCount )
				{
					_calibrationPhase = CalibrationPhase.DriveToActiveResetPoint;
				}
			}
		}

		// update robot

		UpdateRobot( app, deltaSeconds );
	}

	private void DoDriveActiveResetSavePoint( App app, float deltaSeconds )
	{
		// set target position

		_targetPositionX = ActiveResetSavePointX;
		_targetPositionY = ActiveResetSavePointY;

		// set target velocity and acceleration

		_targetVelocityInKPH = 0;
		_targetAccelerationInKPH = 0;

		// set target distance to stop

		var deltaX = _targetPositionX - _carPositionX;
		var deltaY = _targetPositionY - _carPositionY;

		_targetDistanceToStop = MathF.Sqrt( deltaX * deltaX + deltaY * deltaY );

		// when we have stopped, go to the next phase

		if ( app.Simulator.VelocityX < 0.005f )
		{
			// hit the active reset save button

			app.VirtualJoystick.ActiveResetSave = true;

			// save the reset position

			_carResetPositionX = _carPositionX;
			_carResetPositionY = _carPositionY;

			// prepare for the first pass

			_robotSettleTimer = 0f;
			_targetSteeringWheelAngleInDegrees = -MaxSteeringWheelAngleInDegrees;
			_steeringWheelAnglesInDegrees[ _numSteeringWheelAnglesRecorded ] = _targetSteeringWheelAngleInDegrees;
			_targetVelocityInKPH = 1;
			_targetAccelerationInKPH = 1;
			_targetDistanceToStop = 0f;
			_maxAbsYawRateInDegrees = 0f;

			// tell robot to use fixed steering wheel angle

			_robotMode = RobotMode.FixedSteeringWheelAngle;

			// and we're off

			_calibrationProgress++;

			_calibrationPhase = CalibrationPhase.TurningPasses;
		}

		// update robot

		UpdateRobot( app, deltaSeconds );
	}

	private void DoTurningPasses( App app, float deltaSeconds )
	{
		if ( _robotSettleTimer >= 1f )
		{
			// get our current abs yaw rate in degrees

			var absYawRateInDegrees = MathF.Abs( app.Simulator.YawRate * RadiansToDegrees );

			// figure out if we have crashed (or is likely to crash)

			var crashed = app.Simulator.GForce >= 2f || ( MathF.Sqrt( _carPositionX * _carPositionX + _carPositionY * _carPositionY ) > 195f );

			// check if we've reached our target speed

			if ( !crashed && ( _robotGearMode == RobotGearMode.NotShiftingGears ) && ( app.Simulator.VelocityX >= _targetVelocityInKPH * KPHToMPS ) )
			{
				// yes - save the data

				_yawRateDataInDegrees[ _numSteeringWheelAnglesRecorded, _targetVelocityInKPH ] = app.Simulator.YawRate * RadiansToDegrees;

				// update max abs yaw rate

				_maxAbsYawRateInDegrees = MathF.Max( _maxAbsYawRateInDegrees, absYawRateInDegrees );

				// bump up the target speed

				_targetVelocityInKPH++;
			}

			// check if we are done with this pass

			if ( crashed || ( _targetVelocityInKPH > MaxSpeedInKPH ) || ( MathF.Abs( app.Simulator.VelocityY ) > 1.5f ) || ( ( _robotGearMode == RobotGearMode.NotShiftingGears ) && ( app.Simulator.VelocityX >= ( 40f * KPHToMPS ) ) && ( absYawRateInDegrees < ( _maxAbsYawRateInDegrees * 0.9f ) ) ) )
			{
				// update calibration progress

				_calibrationProgress++;

				// increase num steering wheel angles recorded

				_numSteeringWheelAnglesRecorded++;

				// hit the active reset run button

				app.VirtualJoystick.ActiveResetRun = true;

				// reset the robot

				ResetRobot();

				// prepare for the next pass

				_targetSteeringWheelAngleInDegrees += SteeringWheelAngleIncrement;

				// detect if it's time to stop

				if ( _targetSteeringWheelAngleInDegrees == -10 )
				{
					StopCalibration( true );
				}
				else
				{
					_steeringWheelAnglesInDegrees[ _numSteeringWheelAnglesRecorded ] = _targetSteeringWheelAngleInDegrees;
					_targetVelocityInKPH = 1;
					_targetAccelerationInKPH = 1;
					_targetDistanceToStop = 0f;
					_maxAbsYawRateInDegrees = 0f;
				}
			}
		}

		// update robot

		UpdateRobot( app, deltaSeconds );
	}

	private void DoStop( App app, float deltaSeconds )
	{
		app.VirtualJoystick.Steering = 0f;
		app.VirtualJoystick.Throttle = 0f;
		app.VirtualJoystick.Brake = 1f;

		if ( app.Simulator.VelocityX <= 0.005f )
		{
			app.VirtualJoystick.Brake = 0f;

			_calibrationPhase = CalibrationPhase.NotCalibrating;
		}
	}

	public void ClearCalibration()
	{
		// clear out the data tables

		_numSteeringWheelAnglesRecorded = 0;

		Array.Clear( _steeringWheelAnglesInDegrees );
		Array.Clear( _yawRateDataInDegrees );

		// clear out calibration

		_calibrationIsValid = false;

		_multipleLinearRegression = null;

		_maximumPredictedLogMaxYawRateFactor = 0f;
		_minimumPredictedLogMaxYawRateFactor = 0f;

		// reset the currently active calibration file name

		_currentlyActiveCalibrationFileName = string.Empty;
	}

	private void SaveCalibration()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffects] SaveCalibration >>>" );

		// create directory if it does not exist

		if ( !Directory.Exists( CalibrationDirectory ) )
		{
			Directory.CreateDirectory( CalibrationDirectory );
		}

		// open file

		var filePath = Path.Combine( CalibrationDirectory, $"{app.Simulator.CarScreenName} - {app.Simulator.CarSetupName} - {app.Simulator.CurrentTireCompoundType}.csv" );

		using var writer = new StreamWriter( filePath );

		// write car name and calibration

		writer.WriteLine( $"{app.Simulator.CarScreenName},{app.Simulator.CarSetupName},{app.Simulator.CurrentTireCompoundType}" );

		// write header row

		var headerString = "Speed (KPH)";

		for ( var angleIndex = 0; angleIndex < _numSteeringWheelAnglesRecorded; angleIndex++ )
		{
			headerString += $",{_steeringWheelAnglesInDegrees[ angleIndex ]}";
		}

		writer.WriteLine( headerString );

		// write data rows

		for ( var speed = 0; speed <= MaxSpeedInKPH; speed++ )
		{
			var dataString = $"{speed}";

			for ( var angleIndex = 0; angleIndex < _numSteeringWheelAnglesRecorded; angleIndex++ )
			{
				if ( _yawRateDataInDegrees[ angleIndex, speed ] == 0f )
				{
					dataString += $",";
				}
				else
				{
					dataString += "," + _yawRateDataInDegrees[ angleIndex, speed ].ToString( "F6", CultureInfo.InvariantCulture );
				}
			}

			writer.WriteLine( dataString );
		}

		// update understeer setting to use this calibration file

		DataContext.DataContext.Instance.Settings.SteeringEffectsUndersteerCalibrationFileName1 = Path.GetFileNameWithoutExtension( filePath );

		// update the combo box options

		SetMairaComboBoxItemsSources();

		//

		app.Logger.WriteLine( "[SteeringEffects] <<< SaveCalibration" );
	}

	public void LoadCalibration()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffects] LoadCalibration >>>" );

		// don't load the calibration file if we are currently calibrating

		if ( _calibrationPhase != CalibrationPhase.NotCalibrating )
		{
			app.Logger.WriteLine( "[SteeringEffects] We are currently calibrating" );
		}
		else
		{
			// figure out which calibration file we need to load

			var settings = DataContext.DataContext.Instance.Settings;

			var tireIndex = app.Simulator.CurrentTireIndex + 1;

			if ( ( tireIndex < 1 ) || ( tireIndex > 3 ) )
			{
				app.Logger.WriteLine( $"[SteeringEffects] Current tire index is out of range" );

				ClearCalibration();
			}
			else
			{
				var propertyName = $"SteeringEffectsUndersteerCalibrationFileName{tireIndex}";

				var steeringEffectsUndersteerCalibrationFileNamePropertyInfo = settings.GetType().GetProperty( propertyName );

				if ( steeringEffectsUndersteerCalibrationFileNamePropertyInfo == null )
				{
					app.Logger.WriteLine( $"[SteeringEffects] No such property name '{propertyName}' in settings" );

					ClearCalibration();
				}
				else
				{
					var calibrationFileName = (string?) steeringEffectsUndersteerCalibrationFileNamePropertyInfo.GetValue( settings );

					if ( calibrationFileName == null )
					{
						app.Logger.WriteLine( $"[SteeringEffects] Calibration file name property value is null (shouldn't be possible!)" );

						ClearCalibration();
					}
					else if ( calibrationFileName == string.Empty )
					{
						app.Logger.WriteLine( $"[SteeringEffects] No calibration file selected for this tire compound" );

						ClearCalibration();
					}
					else if ( calibrationFileName == _currentlyActiveCalibrationFileName )
					{
						app.Logger.WriteLine( $"[SteeringEffects] Calibration file is already loaded" );
					}
					else
					{
						// clear the current calibration

						ClearCalibration();

						// keep track of whether the file load was good or not

						var fileLoadWasSuccessful = true;

						// open file

						var filePath = Path.Combine( CalibrationDirectory, $"{calibrationFileName}.csv" );

						if ( !File.Exists( filePath ) )
						{
							app.Logger.WriteLine( $"[SteeringEffects] Calibration file not found: {filePath}" );

							fileLoadWasSuccessful = false;
						}
						else
						{
							using var reader = new StreamReader( filePath );

							// skip the first line

							var carInfoLine = reader.ReadLine();

							// read header line and extract steering wheel angles

							var headerLine = reader.ReadLine();

							if ( string.IsNullOrWhiteSpace( headerLine ) )
							{
								app.Logger.WriteLine( "[SteeringEffects] Missing header line." );

								fileLoadWasSuccessful = false;
							}
							else
							{
								var headerParts = headerLine.Split( ',' );

								if ( headerParts.Length < 2 )
								{
									app.Logger.WriteLine( "[SteeringEffects] Invalid header line." );

									fileLoadWasSuccessful = false;
								}
								else
								{
									var angleLabels = headerParts.Skip( 1 ).ToList();

									_numSteeringWheelAnglesRecorded = angleLabels.Count;

									for ( var angleIndex = 0; angleIndex < _numSteeringWheelAnglesRecorded; angleIndex++ )
									{
										if ( !int.TryParse( angleLabels[ angleIndex ].Trim(), out _steeringWheelAnglesInDegrees[ angleIndex ] ) )
										{
											app.Logger.WriteLine( "[SteeringEffects] Failed to parse initial steering wheel angle." );

											fileLoadWasSuccessful = false;

											break;
										}
									}

									// read yaw rate data

									if ( fileLoadWasSuccessful )
									{
										while ( !reader.EndOfStream )
										{
											var line = reader.ReadLine();

											if ( string.IsNullOrWhiteSpace( line ) ) continue;

											var parts = line.Split( ',' );

											if ( !int.TryParse( parts[ 0 ], out var speedInKPH ) ) continue;

											if ( speedInKPH > MaxSpeedInKPH )
											{
												app.Logger.WriteLine( "[SteeringEffects] Invalid speed." );

												fileLoadWasSuccessful = false;

												break;
											}

											for ( var angleIndex = 0; angleIndex < _numSteeringWheelAnglesRecorded; angleIndex++ )
											{
												var partIndex = angleIndex + 1;

												if ( partIndex >= parts.Length ) break;

												if ( float.TryParse( parts[ partIndex ], NumberStyles.Float, CultureInfo.InvariantCulture, out var yawRate ) )
												{
													_yawRateDataInDegrees[ angleIndex, speedInKPH ] = yawRate;
												}
												else
												{
													_yawRateDataInDegrees[ angleIndex, speedInKPH ] = 0f;
												}
											}
										}
									}
								}
							}
						}

						if ( fileLoadWasSuccessful )
						{
							int steeringWheelAngle;

							// clean up the yaw rate spikes

							CleanUpYawRateSpikes();

							// create predictor functions for predicting the peak yaw rates and corresponding speeds

							var yawRateModel = new YawRateModel( _steeringWheelAnglesInDegrees, _yawRateDataInDegrees, MaxSpeedInKPH );

							var (yawRateInterpolator, speedInterpolator, shallowestSteeringWheelAngle) = yawRateModel.FitWithProgressiveRefinement();

							// write to debug file

							filePath = Path.Combine( SteeringEffects.CalibrationDirectory, $"debug_fitted_max_yaw_rates.csv" );

							using var writer = new StreamWriter( filePath );

							writer.WriteLine( "Steering Wheel Angle,Max Yaw Rate,Corresponding Speed" );

							for ( steeringWheelAngle = -MaxSteeringWheelAngleInDegrees; steeringWheelAngle <= shallowestSteeringWheelAngle; steeringWheelAngle++ )
							{
								var predictedMaxYawRate = yawRateInterpolator( steeringWheelAngle );
								var predictedCorrespondingSpeed = speedInterpolator( steeringWheelAngle );

								writer.WriteLine( $"{steeringWheelAngle:F0},{predictedMaxYawRate:F6},{predictedCorrespondingSpeed:F1}" );
							}

							// allocate data arrays for curve fitting

							steeringWheelAngle = -MaxSteeringWheelAngleInDegrees;

							var numAngles = MaxSteeringWheelAngleInDegrees - Math.Abs( shallowestSteeringWheelAngle ) + 1;

							var angles = new double[ numAngles ];
							var values = new double[ numAngles ];

							// fill out the data arrays

							for ( var angleIndex = 0; angleIndex < numAngles; angleIndex++ )
							{
								angles[ angleIndex ] = steeringWheelAngle;

								var interpolatedMaxYawRate = yawRateInterpolator( steeringWheelAngle );
								var interpolatedCorrespondingSpeed = speedInterpolator( steeringWheelAngle );

								values[ angleIndex ] = MathF.Log( ( interpolatedCorrespondingSpeed + 1f ) / ( interpolatedMaxYawRate + 1f ) );

								steeringWheelAngle++;
							}

							// write to debug file

							filePath = Path.Combine( SteeringEffects.CalibrationDirectory, $"debug_interpolated_log_yaw_rate_factors.csv" );

							using var writer2 = new StreamWriter( filePath );

							writer2.WriteLine( "Steering Wheel Angle,Interpolated Log Yaw Rate Factor" );

							for ( var angleIndex = 0; angleIndex < numAngles; angleIndex++ )
							{
								writer2.WriteLine( $"{angles[ angleIndex ]:F0},{values[ angleIndex ]:F6}" );
							}

							// train the model

							double[][] inputs = ExpandPolynomialFeaturesFast( angles );

							var ols = new OrdinaryLeastSquares();

							_multipleLinearRegression = ols.Learn( inputs, values );

							// figure out the range of the grip-o-meter

							_maximumPredictedLogMaxYawRateFactor = PredictLogMaxYawRateFactor( 0f );
							_minimumPredictedLogMaxYawRateFactor = PredictLogMaxYawRateFactor( -MaxSteeringWheelAngleInDegrees );

							// all good to go!

							_currentlyActiveCalibrationFileName = calibrationFileName;

							_calibrationIsValid = true;

							// write to debug file

							filePath = Path.Combine( SteeringEffects.CalibrationDirectory, $"debug_predicted_log_yaw_rate_factors.csv" );

							using var writer3 = new StreamWriter( filePath );

							writer3.WriteLine( "Steering Wheel Angle,Predicted Log Max Yaw Rate Factor" );

							for ( steeringWheelAngle = -MaxSteeringWheelAngleInDegrees; steeringWheelAngle <= 0; steeringWheelAngle++ )
							{
								var predictedLogMaxYawRateFactor = PredictLogMaxYawRateFactor( steeringWheelAngle );

								writer3.WriteLine( $"{steeringWheelAngle:F1},{predictedLogMaxYawRateFactor:F6}" );
							}
						}
					}
				}
			}
		}

		//

		app.Logger.WriteLine( "[SteeringEffects] <<< LoadCalibration" );
	}

	public static double[] ExpandPolynomialFeaturesFast( double x )
	{
		var features = new double[ PolynomialDegrees ];

		features[ 0 ] = x;

		for ( var d = 1; d < PolynomialDegrees; d++ )
		{
			features[ d ] = features[ d - 1 ] * x;
		}

		return features;
	}

	public static double[][] ExpandPolynomialFeaturesFast( double[] xValues )
	{
		var features = new double[ xValues.Length ][];

		for ( var i = 0; i < xValues.Length; i++ )
		{
			features[ i ] = ExpandPolynomialFeaturesFast( xValues[ i ] );
		}

		return features;
	}

	private void CleanUpYawRateSpikes()
	{
		// go through each steering wheel angle

		for ( var angleIndex = 0; angleIndex < _numSteeringWheelAnglesRecorded; angleIndex++ )
		{
			// remove a max of 10 yaw rate spikes

			for ( var spikeCount = 0; spikeCount < 10; spikeCount++ )
			{
				var biggestSpikeAbsYawRateDelta = 0f;
				var biggestSpikeSpeedInKPH = 0;

				// find the biggest yaw rate spike

				for ( var speedInKPH = 2; speedInKPH <= MaxSpeedInKPH - 2; speedInKPH++ )
				{
					var cubicInterpolatedYawRate = GetCubicInterpolatedYawRate( angleIndex, speedInKPH );

					if ( cubicInterpolatedYawRate != 0f )
					{
						var sampledYawRate = _yawRateDataInDegrees[ angleIndex, speedInKPH ];

						var absYawRateDelta = MathF.Abs( sampledYawRate - cubicInterpolatedYawRate );

						if ( absYawRateDelta > biggestSpikeAbsYawRateDelta )
						{
							biggestSpikeAbsYawRateDelta = absYawRateDelta;
							biggestSpikeSpeedInKPH = speedInKPH;
						}
					}
				}

				if ( biggestSpikeAbsYawRateDelta > AbsYawRateSpikeThreshold )
				{
					_yawRateDataInDegrees[ angleIndex, biggestSpikeSpeedInKPH ] = GetCubicInterpolatedYawRate( angleIndex, biggestSpikeSpeedInKPH );
				}
				else
				{
					break;
				}
			}
		}

		// open debug file

		var filePath = Path.Combine( CalibrationDirectory, $"debug_cleaned_up_yaw_rate_spikes.csv" );

		using var writer = new StreamWriter( filePath );

		// write header row

		var headerString = "Speed (KPH)";

		for ( var angleIndex = 0; angleIndex < _numSteeringWheelAnglesRecorded; angleIndex++ )
		{
			headerString += $",{_steeringWheelAnglesInDegrees[ angleIndex ]}";
		}

		writer.WriteLine( headerString );

		// write data rows

		for ( var speed = 0; speed <= MaxSpeedInKPH; speed++ )
		{
			var dataString = $"{speed}";

			for ( var angleIndex = 0; angleIndex < _numSteeringWheelAnglesRecorded; angleIndex++ )
			{
				if ( _yawRateDataInDegrees[ angleIndex, speed ] == 0f )
				{
					dataString += $",";
				}
				else
				{
					dataString += "," + _yawRateDataInDegrees[ angleIndex, speed ].ToString( "F6", CultureInfo.InvariantCulture );
				}
			}

			writer.WriteLine( dataString );
		}
	}

	private float GetCubicInterpolatedYawRate( int angleIndex, int speedInKPH )
	{
		var prev2 = _yawRateDataInDegrees[ angleIndex, speedInKPH - 2 ];
		var prev1 = _yawRateDataInDegrees[ angleIndex, speedInKPH - 1 ];
		var next1 = _yawRateDataInDegrees[ angleIndex, speedInKPH + 1 ];
		var next2 = _yawRateDataInDegrees[ angleIndex, speedInKPH + 2 ];

		if ( prev2 != 0f && prev1 != 0f && next1 != 0f && next2 != 0f )
		{
			return CubicInterpolate( prev2, prev1, next1, next2, 0.5f );
		}
		else
		{
			return 0f;
		}
	}

	private static float CubicInterpolate( float y0, float y1, float y2, float y3, float mu )
	{
		var a0 = y3 - y2 - y0 + y1;
		var a1 = y0 - y1 - a0;
		var a2 = y2 - y0;
		var a3 = y1;

		return a0 * mu * mu * mu + a1 * mu * mu + a2 * mu + a3;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private float PredictLogMaxYawRateFactor( float steeringWheelAngle )
	{
		steeringWheelAngle = MathF.Max( -180f, steeringWheelAngle );

		double[] features = ExpandPolynomialFeaturesFast( steeringWheelAngle );

		var predictedLogMaxYawRateFactor = (float) _multipleLinearRegression!.Transform( features );

		if ( steeringWheelAngle > -20f )
		{
			var t = 1f + ( steeringWheelAngle / 20f );

			var lerpFactor = MathF.Pow( t, 5f );

			var maxValue = MathF.Log( ( MaxSpeedInKPH + 1f ) );

			predictedLogMaxYawRateFactor = Misc.Lerp( predictedLogMaxYawRateFactor, maxValue, lerpFactor );
		}

		return predictedLogMaxYawRateFactor;
	}

	public void Tick( App app )
	{
		if ( app.MainWindow.SteeringEffectsTabItemIsVisible )
		{
			var settings = DataContext.DataContext.Instance.Settings;
			var localization = DataContext.DataContext.Instance.Localization;

			var maxCalibrationProgress = MaxCalibrationProgress + (int) settings.SteeringEffectsWarmUpLapCount;

			app.MainWindow.SteeringEffects_Calibration_Phase_Label.Content = $"{localization[ "Phase:" ]} {localization[ _calibrationPhase.ToString() ]}";
			app.MainWindow.SteeringEffects_Calibration_Progress_Label.Content = $"{localization[ "Progress:" ]} {_calibrationProgress * 100f / maxCalibrationProgress:F0}{localization[ "Percent" ]}";

			app.MainWindow.SteeringEffects_Calibration_RPM_Label.Content = $"{localization[ "RPM:" ]} {app.Simulator.RPM / app.Simulator.ShiftLightsShiftRPM * 100f:F0}{localization[ "Percent" ]}";
			app.MainWindow.SteeringEffects_Calibration_Brake_Label.Content = $"{localization[ "Brake:" ]} {_robotBrake * 100f:F0}{localization[ "Percent" ]}";
			app.MainWindow.SteeringEffects_Calibration_Throttle_Label.Content = $"{localization[ "Throttle:" ]} {_robotThrottle * 100f:F0}{localization[ "Percent" ]}";
			app.MainWindow.SteeringEffects_Calibration_SteeringWheel_Label.Content = $"{localization[ "SteeringWheel:" ]} {_robotSteeringWheelAngleInDegrees:F0}{localization[ "Degrees" ]}";

			app.MainWindow.SteeringEffects_Calibration_YawRate_Label.Content = $"{localization[ "YawRate:" ]} {app.Simulator.YawRate * RadiansToDegrees,6:F1}";

			app.MainWindow.SteeringEffects_Calibration_CarPositionX_Label.Content = $"{localization[ "CarPositionX:" ]} {_carPositionX,6:F1}";
			app.MainWindow.SteeringEffects_Calibration_CarPositionY_Label.Content = $"{localization[ "CarPositionY:" ]} {_carPositionY,6:F1}";

			app.MainWindow.SteeringEffects_Calibration_Heading_Label.Content = $"{localization[ "Heading:" ]} {app.Simulator.YawNorth * RadiansToDegrees,6:F1}";

			app.MainWindow.SteeringEffects_Calibration_VelocityX_Label.Content = $"{localization[ "VelocityX:" ]} {app.Simulator.VelocityX * MPSToKPH,6:F1}";
			app.MainWindow.SteeringEffects_Calibration_VelocityY_Label.Content = $"{localization[ "VelocityY:" ]} {app.Simulator.VelocityY * MPSToKPH,6:F1}";

			var transformGroup = new TransformGroup
			{
				Children =
				[
					new RotateTransform( app.Simulator.YawNorth * RadiansToDegrees ),
					new TranslateTransform( _carPositionX * MapScale, _carPositionY * -MapScale )
				]
			};

			app.MainWindow.SteeringEffects_RaceCar_Image.RenderTransform = transformGroup;

			if ( _robotMode == RobotMode.DriveToTarget )
			{
				app.MainWindow.SteeringEffects_TargetPosition_Image.RenderTransform = new TranslateTransform( _targetPositionX * MapScale, _targetPositionY * -MapScale );
				app.MainWindow.SteeringEffects_TargetPosition_Image.Visibility = Visibility.Visible;
			}
			else
			{
				app.MainWindow.SteeringEffects_TargetPosition_Image.Visibility = Visibility.Hidden;
			}

			app.MainWindow.SteeringEffects_CarSetupName_TextBlock.Text = $"{localization[ "CurrentCarSetup" ]} {app.Simulator.CarSetupName.ToUpper()}";
			app.MainWindow.SteeringEffects_TireCompoundType_TextBlock.Text = $"{localization[ "CurrentTireCompound" ]} {app.Simulator.CurrentTireCompoundType.ToUpper()}";

			var disableButtons = ( app.Simulator.TrackDisplayName != "Centripetal Circuit" );

			app.MainWindow.SteeringEffects_NotOnCentripetalCircuitTrack_TextBlock.Visibility = disableButtons ? Visibility.Visible : Visibility.Collapsed;

			if ( _calibrationPhase == CalibrationPhase.NotCalibrating )
			{
				app.MainWindow.SteeringEffects_RunCalibration_MairaButton.Disabled = false;
				app.MainWindow.SteeringEffects_StopCalibration_MairaButton.Disabled = !disableButtons;
			}
			else
			{
				app.MainWindow.SteeringEffects_RunCalibration_MairaButton.Disabled = !disableButtons;
				app.MainWindow.SteeringEffects_StopCalibration_MairaButton.Disabled = false;
			}
		}
	}
}
