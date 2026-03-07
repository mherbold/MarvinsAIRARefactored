
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public class SteeringEffects
{
	private const int UpdateInterval = 6;

	public static string CalibrationDirectory { get; private set; } = Path.Combine( App.DocumentsFolder, "Calibration" );

	public enum SeatOfPantsAlgorithm
	{
		YAcceleration,
		YVelocity,
		YVelocityOverXVelocity
	};

	public float UndersteerEffect { get; private set; } = 0f;
	public float OversteerEffect { get; private set; } = 0f;
	public float SeatOfPantsEffect { get; private set; } = 0f;
	public float SkidSlip { get; private set; } = 0f;

	public bool IsCalibrating => _calibrationPhase != CalibrationPhase.NotCalibrating;
	public bool RedrawCalibrationGraph { private get; set; } = false;

	private enum CalibrationPhase
	{
		NotCalibrating,
		ResetCalibration,
		Calibrating,
		Stop
	}

	private const int CalibrationFileVersion = 2;

	private const int MaxSteeringWheelAngleInDegrees = 540;
	private const int MaxNumSteeringWheelAngles = MaxSteeringWheelAngleInDegrees * 2 + 1;

	private const float CalibrationSpeedInKPH = 15f;
	private const float CalibrationSpeedToleranceInKPH = 0.5f;

	private const int CalibrationGraphWidth = MaxSteeringWheelAngleInDegrees * 2;
	private const int CalibrationGraphHeight = 450;
	private const int HalfCalibrationGraphWidth = CalibrationGraphWidth / 2;

	private CalibrationPhase _calibrationPhase = CalibrationPhase.NotCalibrating;
	private float _calibrationProgress = 0f;

	private int _framesToSkip = 0;

	private float _lastFrameSpeed = 0f;

	private int _targetSteeringWheelAngleInDegrees = 0;

	private float _robotSettleTimer = 0f;
	private float _robotThrottle = 0f;
	private float _robotBrake = 0f;

	private int _calibrationSamplesTaken = 0;
	private float _steeringWheelAngleInDegreesRunningAverage = 0f;
	private float _yawRateInDegreesPerSecondRunningAverage = 0f;

	private int _numSteeringWheelAnglesRecorded = 0;

	private readonly float[] _steeringWheelAnglesInDegrees = new float[ MaxNumSteeringWheelAngles ];
	private readonly float[] _yawRateInDegreesPerSecond = new float[ MaxNumSteeringWheelAngles ];

	private readonly float[] _expectedYawRateInDegreesPerSecond = new float[ MaxNumSteeringWheelAngles ];
	private float _steeringWheelAngleAtMinimumExpectedYawRate = 0f;
	private bool _calibrationIsValid = false;

	private readonly RenderTargetBitmap _calibrationGraphRenderTargetBitmap = new( CalibrationGraphWidth, CalibrationGraphHeight, 96.0, 96.0, PixelFormats.Pbgra32 );

	private readonly Pen _calibrationGraphRawDataPen = new( Brushes.Black, 3.0 )
	{
		StartLineCap = PenLineCap.Round,
		EndLineCap = PenLineCap.Round
	};
	private readonly Pen _calibrationGraphSmoothedDataPen = new( Brushes.SkyBlue, 2.0 )
	{
		StartLineCap = PenLineCap.Round,
		EndLineCap = PenLineCap.Round
	};
	private readonly Pen _calibrationGraphExtrapolatedDataPen = new( Brushes.DarkCyan, 2.0 )
	{
		StartLineCap = PenLineCap.Round,
		EndLineCap = PenLineCap.Round
	};
	private readonly Pen _calibrationGraphMinimumThresholdPen = new( Brushes.Yellow, 1.0 )
	{
		StartLineCap = PenLineCap.Round,
		EndLineCap = PenLineCap.Round
	};
	private readonly Pen _calibrationGraphMaximumThresholdPen = new( Brushes.Red, 1.0 )
	{
		StartLineCap = PenLineCap.Round,
		EndLineCap = PenLineCap.Round
	};

	private int _updateCounter = UpdateInterval + 8;

	public SteeringEffects()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffects] Constructor >>>" );

		_calibrationGraphRawDataPen.Freeze();
		_calibrationGraphSmoothedDataPen.Freeze();
		_calibrationGraphExtrapolatedDataPen.Freeze();
		_calibrationGraphMinimumThresholdPen.Freeze();
		_calibrationGraphMaximumThresholdPen.Freeze();

		app.Logger.WriteLine( "[SteeringEffects] <<< Constructor" );
	}

	public void SimulatorDisconnected()
	{
		DataContext.DataContext.Instance.Settings.SteeringEffectsCalibrationFileName = string.Empty;

		ClearCalibration();

		MainWindow._steeringEffectsPage.UpdateCalibrationFileNameOptions();
	}

	public void Update( App app, float deltaSeconds )
	{
		if ( _calibrationPhase == CalibrationPhase.NotCalibrating )
		{
			UpdateEffects( app, deltaSeconds );

			/*
			app.Debug.Label_1 = $"UndersteerEffect: {UndersteerEffect}";
			app.Debug.Label_2 = $"OversteerEffect: {OversteerEffect}";
			app.Debug.Label_3 = $"SeatOfPantsEffect: {SeatOfPantsEffect}";
			app.Debug.Label_4 = $"SkidSlip: {SkidSlip}";
			*/
		}
		else
		{
			UndersteerEffect = 0f;
			OversteerEffect = 0f;
			SeatOfPantsEffect = 0f;
			SkidSlip = 0f;

			switch ( _calibrationPhase )
			{
				case CalibrationPhase.ResetCalibration:
					DoResetCalibration( app, deltaSeconds );
					break;

				case CalibrationPhase.Calibrating:
					DoCalibration( app, deltaSeconds );
					break;

				case CalibrationPhase.Stop:
					DoStop( app, deltaSeconds );
					break;
			}
		}
	}

	private void UpdateEffects( App app, float deltaSeconds )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		// get current steering wheel angle in degrees

		var steeringWheelAngleInDegrees = ( app.Simulator.SteeringWheelAngle ) * MathZ.RadiansToDegrees - app.Simulator.SteeringOffsetInDegrees;

		// denormalize from 10:1 steering ratio

		steeringWheelAngleInDegrees *= app.Simulator.SteeringRatio / 10f;

		// get current speed (minimum 1 kph to avoid divide by 0)

		var speedInKPH = MathF.Max( app.Simulator.Speed * MathZ.MPSToKPH, 1f );

		// get current normalized yaw rate

		var yawRateInDegreesPerSecond = MathF.Abs( app.Simulator.YawRate * MathZ.RadiansToDegrees ) / speedInKPH;

		// compute the float angle index (into the _expectedYawRateInDPS array)

		var angleIndex = Math.Clamp( steeringWheelAngleInDegrees + MaxSteeringWheelAngleInDegrees, 0f, _expectedYawRateInDegreesPerSecond.Length - 1f );

		// get the nearest integer angle indices

		var angleIndexLower = (int) MathF.Floor( angleIndex );
		var angleIndexUpper = (int) MathF.Ceiling( angleIndex );

		// if both indices are the same (at exact int or at boundary), no need to blend

		var expectedYawRateInDegreesPerSecond = 0f;

		if ( _calibrationIsValid )
		{
			if ( angleIndexLower == angleIndexUpper )
			{
				expectedYawRateInDegreesPerSecond = _expectedYawRateInDegreesPerSecond[ angleIndexLower ];
			}
			else
			{
				// linearly interpolate between lower and upper values

				var t = angleIndex - angleIndexLower; // (0..1)

				expectedYawRateInDegreesPerSecond = MathZ.Lerp( _expectedYawRateInDegreesPerSecond[ angleIndexLower ], _expectedYawRateInDegreesPerSecond[ angleIndexUpper ], t );
			}
		}

		// calculate absolute deviation from expected; sign encodes under/over

		var deviation = yawRateInDegreesPerSecond - expectedYawRateInDegreesPerSecond;  // + => oversteer, - => understeer

		var absDeviation = MathF.Abs( deviation );

		// fade effects out at low speed

		var speedFade = MathZ.Smoothstep( 1f, 20f, speedInKPH );

		// deviation < 0 = understeer

		var understeerEffect = 0f;

		if ( _calibrationIsValid )
		{
			if ( ( deviation < 0f ) && ( absDeviation >= settings.SteeringEffectsUndersteerMinimumThreshold ) )
			{
				understeerEffect = MathZ.InverseLerp( settings.SteeringEffectsUndersteerMinimumThreshold, settings.SteeringEffectsUndersteerMaximumThreshold, absDeviation );
			}
		}

		UndersteerEffect = speedFade * understeerEffect;

		// deviation > 0 = oversteer

		var oversteerEffect = 0f;

		if ( _calibrationIsValid )
		{
			if ( ( deviation > 0f ) && ( absDeviation >= settings.SteeringEffectsOversteerMinimumThreshold ) )
			{
				oversteerEffect = MathZ.InverseLerp( settings.SteeringEffectsOversteerMinimumThreshold, settings.SteeringEffectsOversteerMaximumThreshold, absDeviation );
			}
		}

		OversteerEffect = speedFade * oversteerEffect;

		// calculate skid / slip indicator position and align to turning direction

		var skidSlip = 0f;

		if ( deviation < 0f )
		{
			skidSlip = -MathZ.InverseLerp( 0f, settings.SteeringEffectsUndersteerMaximumThreshold, absDeviation );
		}
		else if ( deviation > 0f )
		{
			skidSlip = MathZ.InverseLerp( 0f, settings.SteeringEffectsOversteerMaximumThreshold, absDeviation );
		}

		skidSlip = speedFade * skidSlip;

		if ( steeringWheelAngleInDegrees > _steeringWheelAngleAtMinimumExpectedYawRate )
		{
			skidSlip *= -1;
		}

		SkidSlip = Math.Clamp( skidSlip, -1f, 1f );

		// seat of pants effect is simple

		var seatOfPantsEffect = 0f;

		switch ( settings.SteeringEffectsSeatOfPantsAlgorithm )
		{
			case SeatOfPantsAlgorithm.YAcceleration:

				seatOfPantsEffect = app.Simulator.LatAccel * MathZ.OneOverG;

				break;

			case SeatOfPantsAlgorithm.YVelocity:

				seatOfPantsEffect = -app.Simulator.VelocityY;

				break;

			case SeatOfPantsAlgorithm.YVelocityOverXVelocity:

				seatOfPantsEffect = -app.Simulator.VelocityY / Math.Max( app.Simulator.VelocityX, 20f ) * 20f;

				break;
		}

		var absSeatOfPantsEffect = MathF.Abs( seatOfPantsEffect );

		if ( absSeatOfPantsEffect >= settings.SteeringEffectsSeatOfPantsMinimumThreshold )
		{
			seatOfPantsEffect = MathF.CopySign( MathZ.InverseLerp( settings.SteeringEffectsSeatOfPantsMinimumThreshold, settings.SteeringEffectsSeatOfPantsMaximumThreshold, absSeatOfPantsEffect ), seatOfPantsEffect );
		}
		else
		{
			seatOfPantsEffect = 0f;
		}

		SeatOfPantsEffect = speedFade * seatOfPantsEffect;
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
		_calibrationProgress = 1f;

		// save the calibration

		if ( saveCalibration )
		{
			// save the calibration data

			SaveCalibration();
		}

		app.Logger.WriteLine( "[SteeringEffects] <<< StopCalibration" );
	}

	private void UpdateRobot( App app, float deltaSeconds )
	{
		// let the car settle before driving

		if ( _robotSettleTimer > 0f )
		{
			_robotSettleTimer = Math.Max( _robotSettleTimer - deltaSeconds, 0f );

			_robotThrottle = 0f;
			_robotBrake = 1f;
		}
		else
		{
			// always be in first gear

			if ( app.Simulator.Gear != 1 )
			{
				if ( app.Simulator.Gear > 1 )
				{
					app.VirtualJoystick.ShiftDown = true;
				}
				else
				{
					app.VirtualJoystick.ShiftUp = true;
				}

				_robotSettleTimer = 0.5f;
			}
			else
			{
				// update throttle

				var deltaToTarget = CalibrationSpeedInKPH - app.Simulator.Speed * MathZ.MPSToKPH;

				var targetAcceleration = Math.Clamp( deltaToTarget, -1f, 1f );

				var currentAcceleration = ( app.Simulator.Speed - _lastFrameSpeed ) * MathZ.MPSToKPH / deltaSeconds;

				var deltaAcceleration = targetAcceleration - currentAcceleration;

				if ( MathF.Sign( deltaToTarget ) == MathF.Sign( deltaAcceleration ) )
				{
					_robotThrottle += Math.Clamp( deltaAcceleration, -deltaSeconds / 30f, deltaSeconds / 30f );

					_robotThrottle = MathZ.Saturate( _robotThrottle );
				}

				// update brake

				if ( _robotThrottle == 0f )
				{
					if ( deltaToTarget < 0f )
					{
						_robotBrake -= Math.Clamp( deltaAcceleration, -deltaSeconds / 30f, deltaSeconds / 30f );

						_robotBrake = MathZ.Saturate( _robotBrake );
					}
				}
				else
				{
					_robotBrake = 0f;
				}
			}
		}

		// update virtual joystick

		app.VirtualJoystick.Steering = (float) _targetSteeringWheelAngleInDegrees / MaxSteeringWheelAngleInDegrees;
		app.VirtualJoystick.Brake = _robotBrake;
		app.VirtualJoystick.Throttle = _robotThrottle;

		// remember last frame speed

		_lastFrameSpeed = app.Simulator.Speed;
	}

	private void DoResetCalibration( App app, float deltaSeconds )
	{
		// reset calibration progress

		_calibrationProgress = 0f;

		// reset misc stuff

		_framesToSkip = 0;
		_lastFrameSpeed = 0f;

		// reset targets

		var minimumSteeringWheelAngle = app.Simulator.SteeringWheelAngleMax * MathZ.RadiansToDegrees / -2f;

		_targetSteeringWheelAngleInDegrees = (int) ( MathF.Max( -MaxSteeringWheelAngleInDegrees, minimumSteeringWheelAngle ) );

		// reset robot

		_robotSettleTimer = 1f;
		_robotThrottle = 0f;

		// reset running averages

		_calibrationSamplesTaken = 0;
		_steeringWheelAngleInDegreesRunningAverage = 0f;
		_yawRateInDegreesPerSecondRunningAverage = 0f;

		// clear out our old calibration data

		ClearCalibration();

		// next phase

		_calibrationPhase = CalibrationPhase.Calibrating;
	}

	private void DoCalibration( App app, float deltaSeconds )
	{
		// update robot

		UpdateRobot( app, deltaSeconds );

		// don't do anything if we need to skip some frames (let steering wheel adjustment settle)

		if ( _framesToSkip > 0 )
		{
			_framesToSkip--;

			return;
		}

		// don't do anything if robot is still settling

		if ( _robotSettleTimer != 0f )
		{
			return;
		}

		// stop here if we aren't at our speed target

		var speedInKph = app.Simulator.Speed * MathZ.MPSToKPH;
		var deltaSpeedInKph = Math.Abs( CalibrationSpeedInKPH - speedInKph );

		if ( deltaSpeedInKph > CalibrationSpeedToleranceInKPH )
		{
			return;
		}

		// update running averages

		var steeringWheelAngleInDegrees = app.Simulator.SteeringWheelAngle * MathZ.RadiansToDegrees;
		var yawRateInDegreesPerSecond = MathF.Abs( app.Simulator.YawRate * MathZ.RadiansToDegrees ) / ( app.Simulator.Speed * MathZ.MPSToKPH );

		if ( _calibrationSamplesTaken == 0 )
		{
			_steeringWheelAngleInDegreesRunningAverage = steeringWheelAngleInDegrees;
			_yawRateInDegreesPerSecondRunningAverage = yawRateInDegreesPerSecond;
		}
		else
		{
			_steeringWheelAngleInDegreesRunningAverage = MathZ.Lerp( _steeringWheelAngleInDegreesRunningAverage, steeringWheelAngleInDegrees, 0.2f );
			_yawRateInDegreesPerSecondRunningAverage = MathZ.Lerp( _yawRateInDegreesPerSecondRunningAverage, yawRateInDegreesPerSecond, 0.2f );
		}

		_calibrationSamplesTaken++;

		// stop when we have averaged the yaw rate over 1/6 of a second (10 frames)

		if ( _calibrationSamplesTaken == 10 )
		{
			// save the yaw rate

			_steeringWheelAnglesInDegrees[ _numSteeringWheelAnglesRecorded ] = ( _steeringWheelAngleInDegreesRunningAverage + app.Simulator.SteeringOffsetInDegrees ) * ( 10f / app.Simulator.SteeringRatio );
			_yawRateInDegreesPerSecond[ _numSteeringWheelAnglesRecorded ] = _yawRateInDegreesPerSecondRunningAverage;

			// update calibration graph

			if ( _numSteeringWheelAnglesRecorded >= 1 )
			{
				app.Dispatcher.Invoke( () =>
				{
					var drawingVisual = new DrawingVisual();

					using var drawingContext = drawingVisual.RenderOpen();

					var y1 = _yawRateInDegreesPerSecond[ _numSteeringWheelAnglesRecorded - 1 ] + 0.5;
					var y2 = _yawRateInDegreesPerSecond[ _numSteeringWheelAnglesRecorded - 0 ] + 0.5;

					var p1 = new Point( MaxSteeringWheelAngleInDegrees + _steeringWheelAnglesInDegrees[ _numSteeringWheelAnglesRecorded - 1 ] + 0.5, CalibrationGraphHeight - y1 * 100.0 + 0.5 );
					var p2 = new Point( MaxSteeringWheelAngleInDegrees + _steeringWheelAnglesInDegrees[ _numSteeringWheelAnglesRecorded - 0 ] + 0.5, CalibrationGraphHeight - y2 * 100.0 + 0.5 );

					drawingContext.DrawLine( _calibrationGraphSmoothedDataPen, p1, p2 );
					drawingContext.Close();

					_calibrationGraphRenderTargetBitmap.Render( drawingVisual );
				} );
			}

			// next!

			_numSteeringWheelAnglesRecorded++;

			// reset running averages

			_calibrationSamplesTaken = 0;
			_steeringWheelAngleInDegreesRunningAverage = 0f;
			_yawRateInDegreesPerSecondRunningAverage = 0f;

			// move to the next target steering wheel angle

			_targetSteeringWheelAngleInDegrees++;

			// skip some frames to let the steering wheel angle adjustment settle

			_framesToSkip = 5;

			// check if we are done calibrating

			var maxSteeringWheelAngleInDegrees = Math.Min( MaxSteeringWheelAngleInDegrees, (int) ( app.Simulator.SteeringWheelAngleMax * MathZ.RadiansToDegrees / 2f ) );

			if ( _targetSteeringWheelAngleInDegrees > maxSteeringWheelAngleInDegrees )
			{
				StopCalibration( true );

				return;
			}

			// update the calibration progress

			_calibrationProgress = (float) _numSteeringWheelAnglesRecorded / (float) ( maxSteeringWheelAngleInDegrees * 2 + 1 );
		}
	}

	private void DoStop( App app, float deltaSeconds )
	{
		// apply max brake

		app.VirtualJoystick.Steering = 0f;
		app.VirtualJoystick.Throttle = 0f;
		app.VirtualJoystick.Brake = 1f;

		// wait for car to come to a complete stop

		if ( app.Simulator.Speed <= 0.005f )
		{
			// release the brake

			app.VirtualJoystick.Brake = 0f;

			// we are done calibrating

			_calibrationPhase = CalibrationPhase.NotCalibrating;

			// clear the calibration data

			ClearCalibration();

			// reload the calibration data

			LoadCalibration();
		}
	}

	private void DrawCalibrationGraphGrid()
	{
		var app = App.Instance!;

		app.Dispatcher.Invoke( () =>
		{
			// start drawing

			var drawingVisual = new DrawingVisual();

			using var drawingContext = drawingVisual.RenderOpen();

			// label drawing

			var labelTypeface = new Typeface( "Segoe UI" );

			const double labelFontSize = 14.0;

			var labelBgBrush = new SolidColorBrush( Color.FromArgb( 255, 0, 0, 0 ) );
			var labelBorderBrush = new SolidColorBrush( Color.FromArgb( 255, 255, 255, 255 ) );
			var labelBorderPen = new Pen( labelBorderBrush, 1.0 );

			labelBgBrush.Freeze();
			labelBorderBrush.Freeze();
			labelBorderPen.Freeze();

			const double labelPaddingX = 5.0;
			const double labelPaddingY = 1.0;
			const double labelCornerRadius = 5.0;

			void DrawLabel( string text, int t, bool left )
			{
				var formatted = new FormattedText( text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, labelTypeface, labelFontSize, Brushes.White, 1.0 );

				double labelX, labelY;

				if ( left )
				{
					labelX = labelPaddingX + 10.0;
					labelY = t - ( formatted.Height / 2.0 );
				}
				else
				{
					labelX = t - ( formatted.Width / 2.0 );
					labelY = labelPaddingY + 10.0;
				}

				var rect = new Rect( labelX - labelPaddingX, labelY - labelPaddingY, formatted.Width + ( labelPaddingX * 2.0 ), formatted.Height + ( labelPaddingY * 2.0 ) );

				drawingContext.DrawRoundedRectangle( labelBgBrush, labelBorderPen, rect, labelCornerRadius, labelCornerRadius );
				drawingContext.DrawText( formatted, new Point( rect.X + labelPaddingX, rect.Y + labelPaddingY ) );
			}

			// transparent background

			drawingContext.DrawRectangle( Brushes.Transparent, null, new Rect( 0, 0, CalibrationGraphWidth, CalibrationGraphHeight ) );

			// pen brushes

			var thinPenBrush = new SolidColorBrush( Color.FromArgb( 32, 255, 255, 255 ) );
			var thickPenBrush = new SolidColorBrush( Color.FromArgb( 96, 255, 255, 255 ) );

			thinPenBrush.Freeze();
			thickPenBrush.Freeze();

			// pens

			var thinPen = new Pen( thinPenBrush, 1.0 );
			var thickPen = new Pen( thickPenBrush, 1.0 );

			thinPen.Freeze();
			thickPen.Freeze();

			// draw vertical lines

			for ( var x = -HalfCalibrationGraphWidth; x <= HalfCalibrationGraphWidth; x += 10 )
			{
				if ( ( x == -HalfCalibrationGraphWidth ) || ( x == HalfCalibrationGraphWidth ) )
				{
					continue;
				}

				var pen = ( Math.Abs( x ) % 50 == 0 ) ? thickPen : thinPen;

				drawingContext.DrawLine( pen, new Point( x + 0.5 + HalfCalibrationGraphWidth, 0 ), new Point( x + 0.5 + HalfCalibrationGraphWidth, CalibrationGraphHeight ) );
			}

			// draw horizontal lines

			for ( var y = 0; y <= CalibrationGraphHeight; y += 10 )
			{
				if ( ( y == 0 ) || ( y == CalibrationGraphHeight ) )
				{
					continue;
				}

				var pen = ( y % 50 == 0 ) ? thickPen : thinPen;

				drawingContext.DrawLine( pen, new Point( 0, y + 0.5 ), new Point( CalibrationGraphWidth, y + 0.5 ) );
			}

			// draw vertical text

			for ( var x = -HalfCalibrationGraphWidth; x <= HalfCalibrationGraphWidth; x += 10 )
			{
				if ( ( x == -HalfCalibrationGraphWidth ) || ( x == HalfCalibrationGraphWidth ) )
				{
					continue;
				}

				if ( x % 50 == 0 )
				{
					var labelValue = x;
					var labelText = $"{labelValue}";

					DrawLabel( labelText, x + HalfCalibrationGraphWidth, false );
				}
			}

			// draw horizontal text

			for ( var y = 0; y <= CalibrationGraphHeight; y += 10 )
			{
				if ( ( y == 0 ) || ( y == CalibrationGraphHeight ) )
				{
					continue;
				}

				if ( y % 50 == 0 )
				{
					var labelValue = ( CalibrationGraphHeight - y ) / 100f - 0.5f;
					var labelText = $"{labelValue:F1}";

					DrawLabel( labelText, y, true );
				}
			}

			// finish drawing

			drawingContext.Close();

			_calibrationGraphRenderTargetBitmap.Clear();
			_calibrationGraphRenderTargetBitmap.Render( drawingVisual );

			// set new image source

			MainWindow._steeringEffectsPage.Calibration_Image.Source = _calibrationGraphRenderTargetBitmap;
		} );
	}

	private void DrawCalibrationGraphData()
	{
		if ( _calibrationIsValid )
		{
			App.Instance!.Dispatcher.Invoke( () =>
			{
				var settings = DataContext.DataContext.Instance.Settings;

				var drawingVisual = new DrawingVisual();

				using var drawingContext = drawingVisual.RenderOpen();

				// draw raw calibration data

				var maximumSteeringWheelAngle = float.MinValue;
				var minimumSteeringWheelAngle = float.MaxValue;

				for ( var angleIndex = 1; angleIndex < _numSteeringWheelAnglesRecorded; angleIndex++ )
				{
					var a1 = _steeringWheelAnglesInDegrees[ angleIndex - 1 ] + MaxSteeringWheelAngleInDegrees;
					var a2 = _steeringWheelAnglesInDegrees[ angleIndex - 0 ] + MaxSteeringWheelAngleInDegrees;

					var y1 = _yawRateInDegreesPerSecond[ angleIndex - 1 ] + 0.5;
					var y2 = _yawRateInDegreesPerSecond[ angleIndex - 0 ] + 0.5;

					var p1 = new Point( a1 - 0.5, CalibrationGraphHeight - y1 * 100.0 + 0.5 );
					var p2 = new Point( a2 + 0.5, CalibrationGraphHeight - y2 * 100.0 + 0.5 );

					drawingContext.DrawLine( _calibrationGraphRawDataPen, p1, p2 );

					maximumSteeringWheelAngle = MathF.Max( maximumSteeringWheelAngle, a1 );
					minimumSteeringWheelAngle = MathF.Min( minimumSteeringWheelAngle, a2 );
				}

				if ( settings.SteeringEffectsUndersteerEnabled )
				{
					// draw understeer minimum threshold lines

					for ( var angleIndex = 1; angleIndex < _expectedYawRateInDegreesPerSecond.Length; angleIndex++ )
					{
						var a1 = angleIndex - 0.5;
						var a2 = angleIndex + 0.5;

						var y1 = _expectedYawRateInDegreesPerSecond[ angleIndex - 1 ] - settings.SteeringEffectsUndersteerMinimumThreshold + 0.5;
						var y2 = _expectedYawRateInDegreesPerSecond[ angleIndex - 0 ] - settings.SteeringEffectsUndersteerMinimumThreshold + 0.5;

						var p1 = new Point( a1, CalibrationGraphHeight - y1 * 100.0 + 0.5 );
						var p2 = new Point( a2, CalibrationGraphHeight - y2 * 100.0 + 0.5 );

						drawingContext.DrawLine( _calibrationGraphMinimumThresholdPen, p1, p2 );
					}

					// draw understeer maximum threshold lines

					for ( var angleIndex = 1; angleIndex < _expectedYawRateInDegreesPerSecond.Length; angleIndex++ )
					{
						var a1 = angleIndex - 0.5;
						var a2 = angleIndex + 0.5;

						var y1 = _expectedYawRateInDegreesPerSecond[ angleIndex - 1 ] - settings.SteeringEffectsUndersteerMaximumThreshold + 0.5;
						var y2 = _expectedYawRateInDegreesPerSecond[ angleIndex - 0 ] - settings.SteeringEffectsUndersteerMaximumThreshold + 0.5;

						var p1 = new Point( a1, CalibrationGraphHeight - y1 * 100.0 + 0.5 );
						var p2 = new Point( a2, CalibrationGraphHeight - y2 * 100.0 + 0.5 );

						drawingContext.DrawLine( _calibrationGraphMaximumThresholdPen, p1, p2 );
					}
				}

				if ( settings.SteeringEffectsOversteerEnabled )
				{
					// draw oversteer minimum threshold lines

					for ( var angleIndex = 1; angleIndex < _expectedYawRateInDegreesPerSecond.Length; angleIndex++ )
					{
						var a1 = angleIndex - 0.5;
						var a2 = angleIndex + 0.5;

						var y1 = _expectedYawRateInDegreesPerSecond[ angleIndex - 1 ] + settings.SteeringEffectsOversteerMinimumThreshold + 0.5;
						var y2 = _expectedYawRateInDegreesPerSecond[ angleIndex - 0 ] + settings.SteeringEffectsOversteerMinimumThreshold + 0.5;

						var p1 = new Point( a1, CalibrationGraphHeight - y1 * 100.0 + 0.5 );
						var p2 = new Point( a2, CalibrationGraphHeight - y2 * 100.0 + 0.5 );

						drawingContext.DrawLine( _calibrationGraphMinimumThresholdPen, p1, p2 );
					}

					// draw oversteer maximum threshold lines

					for ( var angleIndex = 1; angleIndex < _expectedYawRateInDegreesPerSecond.Length; angleIndex++ )
					{
						var a1 = angleIndex - 0.5;
						var a2 = angleIndex + 0.5;

						var y1 = _expectedYawRateInDegreesPerSecond[ angleIndex - 1 ] + settings.SteeringEffectsOversteerMaximumThreshold + 0.5;
						var y2 = _expectedYawRateInDegreesPerSecond[ angleIndex - 0 ] + settings.SteeringEffectsOversteerMaximumThreshold + 0.5;

						var p1 = new Point( a1, CalibrationGraphHeight - y1 * 100.0 + 0.5 );
						var p2 = new Point( a2, CalibrationGraphHeight - y2 * 100.0 + 0.5 );

						drawingContext.DrawLine( _calibrationGraphMaximumThresholdPen, p1, p2 );
					}
				}

				// draw expected yaw rate line

				for ( var angleIndex = 1; angleIndex < _expectedYawRateInDegreesPerSecond.Length; angleIndex++ )
				{
					var a1 = angleIndex - 0.5;
					var a2 = angleIndex + 0.5;

					var y1 = _expectedYawRateInDegreesPerSecond[ angleIndex - 1 ] + 0.5;
					var y2 = _expectedYawRateInDegreesPerSecond[ angleIndex - 0 ] + 0.5;

					var p1 = new Point( a1, CalibrationGraphHeight - y1 * 100.0 + 0.5 );
					var p2 = new Point( a2, CalibrationGraphHeight - y2 * 100.0 + 0.5 );

					if ( ( a1 <= maximumSteeringWheelAngle ) && ( a2 >= minimumSteeringWheelAngle ) )
					{
						drawingContext.DrawLine( _calibrationGraphSmoothedDataPen, p1, p2 );
					}
					else
					{
						drawingContext.DrawLine( _calibrationGraphExtrapolatedDataPen, p1, p2 );
					}
				}

				drawingContext.Close();

				_calibrationGraphRenderTargetBitmap.Render( drawingVisual );
			} );
		}
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

		var filePath = Path.Combine( CalibrationDirectory, $"{app.Simulator.CarScreenName} - {app.Simulator.CarSetupName}.csv" );

		using var writer = new StreamWriter( filePath );

		// write version, car name, car setup, and tire compound type

		writer.WriteLine( $"{CalibrationFileVersion},{app.Simulator.CarScreenName},{app.Simulator.CarSetupName}" );

		// write header row

		writer.WriteLine( "Steering Wheel Angle,Yaw Rate" );

		// write data rows

		for ( var angleIndex = 0; angleIndex < _numSteeringWheelAnglesRecorded; angleIndex++ )
		{
			writer.WriteLine( $"{_steeringWheelAnglesInDegrees[ angleIndex ].ToString( "F6", CultureInfo.InvariantCulture )}," + $"{_yawRateInDegreesPerSecond[ angleIndex ].ToString( "F6", CultureInfo.InvariantCulture )}" );
		}

		// close the file

		writer.Close();

		// update the combo box options

		MainWindow._steeringEffectsPage.UpdateCalibrationFileNameOptions();

		// update setting to use this calibration file

		var settings = DataContext.DataContext.Instance.Settings;

		settings.SteeringEffectsCalibrationFileName = Path.GetFileNameWithoutExtension( filePath );

		//

		app.Logger.WriteLine( "[SteeringEffects] <<< SaveCalibration" );
	}

	public void ClearCalibration()
	{
		// clear out calibration

		_calibrationIsValid = false;

		// clear the graph

		RedrawCalibrationGraph = true;

		// clear out the data tables

		_numSteeringWheelAnglesRecorded = 0;

		Array.Clear( _steeringWheelAnglesInDegrees );
		Array.Clear( _yawRateInDegreesPerSecond );
		Array.Clear( _expectedYawRateInDegreesPerSecond );
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

			if ( settings.SteeringEffectsCalibrationFileName == null )
			{
				app.Logger.WriteLine( $"[SteeringEffects] Calibration file name property value is null (shouldn't be possible!)" );

				ClearCalibration();
			}
			else if ( settings.SteeringEffectsCalibrationFileName == string.Empty )
			{
				app.Logger.WriteLine( $"[SteeringEffects] No calibration file selected" );

				ClearCalibration();
			}
			else
			{
				// clear the current calibration

				ClearCalibration();

				// keep track of whether the file load was good or not

				var calibrationDataSeemsGood = false;

				// open file

				var filePath = Path.Combine( CalibrationDirectory, $"{settings.SteeringEffectsCalibrationFileName}.csv" );

				if ( !File.Exists( filePath ) )
				{
					app.Logger.WriteLine( $"[SteeringEffects] Calibration file not found: {filePath}" );
				}
				else
				{
					using var reader = new StreamReader( filePath );

					// skip the first two lines

					var carInfoLine = reader.ReadLine();

					if ( string.IsNullOrWhiteSpace( carInfoLine ) )
					{
						app.Logger.WriteLine( "[SteeringEffects] Car info line is missing" );
					}
					else
					{
						var carInfoParts = carInfoLine.Split( ',' );

						if ( ( carInfoParts.Length < 1 ) || !int.TryParse( carInfoParts[ 0 ], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version ) )
						{
							app.Logger.WriteLine( "[SteeringEffects] Car info line does not contain a valid version number" );
						}
						else
						{
							if ( version != CalibrationFileVersion )
							{
								app.Logger.WriteLine( "[SteeringEffects] Calibration file is not not the right version" );
							}
							else
							{
								var headerLine = reader.ReadLine();

								if ( string.IsNullOrWhiteSpace( headerLine ) )
								{
									app.Logger.WriteLine( "[SteeringEffects] Header line is missing" );
								}
								else
								{
									// read header line and extract steering wheel angles

									while ( !reader.EndOfStream )
									{
										var line = reader.ReadLine();

										if ( string.IsNullOrWhiteSpace( line ) ) continue;

										var parts = line.Split( ',' );

										if ( !float.TryParse( parts[ 0 ], NumberStyles.Float, CultureInfo.InvariantCulture, out var steeringWheelAngleInDegrees ) ) continue;
										if ( !float.TryParse( parts[ 1 ], NumberStyles.Float, CultureInfo.InvariantCulture, out var yawRateInDegreesPerSecond ) ) continue;

										_steeringWheelAnglesInDegrees[ _numSteeringWheelAnglesRecorded ] = steeringWheelAngleInDegrees;
										_yawRateInDegreesPerSecond[ _numSteeringWheelAnglesRecorded ] = yawRateInDegreesPerSecond;

										_numSteeringWheelAnglesRecorded++;
									}

									// done loading

									app.Logger.WriteLine( "[SteeringEffects] Calibration data seems good" );

									calibrationDataSeemsGood = true;
								}
							}
						}
					}
				}

				if ( calibrationDataSeemsGood )
				{
					var descComparer = Comparer<float>.Create( ( a, b ) => b.CompareTo( a ) ); // reverse order

					// Build fast lookup: angle (deg) -> expected yaw rate (DPS)

					for ( var angleIndex = 0; angleIndex < _expectedYawRateInDegreesPerSecond.Length; angleIndex++ )
					{
						var steeringWheelAngleInDegrees = angleIndex - MaxSteeringWheelAngleInDegrees;

						// Find exact or insertion index in the calibration angles

						var foundOrInsertion = Array.BinarySearch( _steeringWheelAnglesInDegrees, 0, _numSteeringWheelAnglesRecorded, steeringWheelAngleInDegrees, descComparer );

						if ( foundOrInsertion >= 0 )
						{
							// Exact hit on a calibration angle

							_expectedYawRateInDegreesPerSecond[ angleIndex ] = _yawRateInDegreesPerSecond[ foundOrInsertion ];
						}
						else
						{
							// No exact hit; ~val is the insertion point of angleDegrees

							var nextIndex = ~foundOrInsertion;

							// Clamp outside the calibration domain

							if ( nextIndex <= 0 )
							{
								var x0 = _steeringWheelAnglesInDegrees[ 0 ];
								var x1 = _steeringWheelAnglesInDegrees[ _numSteeringWheelAnglesRecorded / 4 ];

								var y0 = _yawRateInDegreesPerSecond[ 0 ];
								var y1 = _yawRateInDegreesPerSecond[ _numSteeringWheelAnglesRecorded / 4 ];

								var dx = ( x1 - x0 );

								var slope = ( MathF.Abs( dx ) < 1e-12f ) ? 0f : ( y1 - y0 ) / dx;

								_expectedYawRateInDegreesPerSecond[ angleIndex ] = y0 + slope * ( steeringWheelAngleInDegrees - x0 );
							}
							else if ( nextIndex >= _numSteeringWheelAnglesRecorded )
							{
								var last = _numSteeringWheelAnglesRecorded - 1;

								var x0 = _steeringWheelAnglesInDegrees[ last - _numSteeringWheelAnglesRecorded / 4 ];
								var x1 = _steeringWheelAnglesInDegrees[ last ];

								var y0 = _yawRateInDegreesPerSecond[ last - _numSteeringWheelAnglesRecorded / 4 ];
								var y1 = _yawRateInDegreesPerSecond[ last ];

								var dx = ( x1 - x0 );

								var slope = ( MathF.Abs( dx ) < 1e-12f ) ? 0f : ( y1 - y0 ) / dx;

								_expectedYawRateInDegreesPerSecond[ angleIndex ] = y1 + slope * ( steeringWheelAngleInDegrees - x1 );
							}
							else
							{
								// Interpolate between prevIndex and nextIndex

								var prevIndex = nextIndex - 1;

								var x0 = _steeringWheelAnglesInDegrees[ prevIndex ];
								var x1 = _steeringWheelAnglesInDegrees[ nextIndex ];

								var y0 = _yawRateInDegreesPerSecond[ prevIndex ];
								var y1 = _yawRateInDegreesPerSecond[ nextIndex ];

								var t = ( steeringWheelAngleInDegrees - x0 ) / ( x1 - x0 );

								_expectedYawRateInDegreesPerSecond[ angleIndex ] = y0 + t * ( y1 - y0 );
							}
						}
					}

					// find min and max steering wheel angles

					var maxSteeringWheelAngle = _steeringWheelAnglesInDegrees[ 0 ];
					var minSteeringWheelAngle = _steeringWheelAnglesInDegrees[ _numSteeringWheelAnglesRecorded - 1 ];

					// apply averaging filter to remove suspension and steering noise

					var averagedExpectedYawRateInDegreesPerSecond = new float[ _expectedYawRateInDegreesPerSecond.Length ];

					const int averagingWindowSize = 20;

					for ( var angleIndex = 0; angleIndex < _expectedYawRateInDegreesPerSecond.Length; angleIndex++ )
					{
						var averageValue = 0.0f;
						var averageCount = 0;

						for ( var angleOffset = 0; angleOffset < averagingWindowSize; angleOffset++ )
						{
							var averagingAngleIndex = angleIndex + angleOffset - averagingWindowSize / 2;

							if ( ( averagingAngleIndex >= 0 ) && ( averagingAngleIndex < _expectedYawRateInDegreesPerSecond.Length ) )
							{
								averageValue += _expectedYawRateInDegreesPerSecond[ averagingAngleIndex ];

								averageCount++;
							}
						}

						averagedExpectedYawRateInDegreesPerSecond[ angleIndex ] = averageValue / averageCount;
					}

					const float fadeWindowSize = 20f;

					for ( var angleIndex = 0; angleIndex < _expectedYawRateInDegreesPerSecond.Length; angleIndex++ )
					{
						var angle = angleIndex - MaxSteeringWheelAngleInDegrees;

						var leftFade = MathZ.Saturate( MathF.Min( angleIndex / fadeWindowSize, ( angle - minSteeringWheelAngle ) / fadeWindowSize ) );
						var rightFade = MathZ.Saturate( MathF.Min( ( _expectedYawRateInDegreesPerSecond.Length - angleIndex - 1f ) / 20f, ( maxSteeringWheelAngle - angle ) / fadeWindowSize ) );
						var centerFade = MathZ.Saturate( _expectedYawRateInDegreesPerSecond[ angleIndex ] / 0.25f );

						_expectedYawRateInDegreesPerSecond[ angleIndex ] = MathZ.Lerp( _expectedYawRateInDegreesPerSecond[ angleIndex ], averagedExpectedYawRateInDegreesPerSecond[ angleIndex ], leftFade * centerFade * rightFade );
					}

					// find steering wheel angle at minimum expected yaw rate

					var minimumExpectedYawRate = float.MaxValue;

					for ( var angleIndex = 0; angleIndex < _expectedYawRateInDegreesPerSecond.Length; angleIndex++ )
					{
						if ( _expectedYawRateInDegreesPerSecond[ angleIndex ] < minimumExpectedYawRate )
						{
							minimumExpectedYawRate = _expectedYawRateInDegreesPerSecond[ angleIndex ];

							_steeringWheelAngleAtMinimumExpectedYawRate = angleIndex - MaxSteeringWheelAngleInDegrees;
						}
					}

					// calibration is valid

					_calibrationIsValid = true;

					// update the graph

					RedrawCalibrationGraph = true;
				}
			}

			app.Dispatcher.Invoke( () =>
			{
				MainWindow._steeringEffectsPage.InvalidConfigurationFile_Border.Visibility = ( ( settings.SteeringEffectsCalibrationFileName == string.Empty ) || _calibrationIsValid ) ? Visibility.Hidden : Visibility.Visible;
			} );
		}

		//

		app.Logger.WriteLine( "[SteeringEffects] <<< LoadCalibration" );
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter == 0 )
		{
			_updateCounter = UpdateInterval;

			if ( MairaAppMenuPopup.CurrentAppPage == MainWindow.AppPage.SteeringEffects )
			{
				var localization = DataContext.DataContext.Instance.Localization;

				if ( app.Simulator.CarSetupName == string.Empty )
				{
					MainWindow._steeringEffectsPage.CarSetupName_TextBlock.Visibility = Visibility.Collapsed;
				}
				else
				{
					MainWindow._steeringEffectsPage.CarSetupName_TextBlock.Text = $"{localization[ "CurrentCarSetup" ]} {app.Simulator.CarSetupName.ToUpper()}";
					MainWindow._steeringEffectsPage.CarSetupName_TextBlock.Visibility = Visibility.Visible;
				}

				if ( _calibrationPhase == CalibrationPhase.NotCalibrating )
				{
					MainWindow._steeringEffectsPage.CalibrationProgress_Border.Visibility = Visibility.Collapsed;
					MainWindow._steeringEffectsPage.LiveInfo_Border.Visibility = Visibility.Collapsed;
				}
				else
				{
					MainWindow._steeringEffectsPage.InvalidConfigurationFile_Border.Visibility = Visibility.Hidden;
					MainWindow._steeringEffectsPage.CalibrationProgress_TextBlock.Text = $"{localization[ "Progress:" ]} {_calibrationProgress * 100f:F0}{localization[ "Percent" ]}";
					MainWindow._steeringEffectsPage.CalibrationProgress_Border.Visibility = Visibility.Visible;

					if ( _calibrationSamplesTaken > 0 )
					{
						MainWindow._steeringEffectsPage.LiveInfo_TextBlock.Text = $"{_steeringWheelAngleInDegreesRunningAverage:F0}{localization[ "Degrees" ]}  ➤  {_yawRateInDegreesPerSecondRunningAverage:F2}{localization[ "DegreesPerSecond" ]}";
						MainWindow._steeringEffectsPage.LiveInfo_Border.Visibility = Visibility.Visible;
					}
				}

				if ( app.Simulator.TrackDisplayName != "Centripetal Circuit" )
				{
					if ( app.VirtualJoystick.Initialized )
					{
						app.VirtualJoystick.Shutdown();
					}

					MainWindow._steeringEffectsPage.NotOnCentripetalCircuitTrack_Border.Visibility = Visibility.Visible;
					MainWindow._steeringEffectsPage.CalibrationButtons_UniformGrid.Visibility = Visibility.Collapsed;
				}
				else
				{
					if ( !app.VirtualJoystick.Initialized && !app.VirtualJoystick.Faulted )
					{
						app.VirtualJoystick.Initialize();
					}

					MainWindow._steeringEffectsPage.NotOnCentripetalCircuitTrack_Border.Visibility = Visibility.Collapsed;
					MainWindow._steeringEffectsPage.CalibrationButtons_UniformGrid.Visibility = Visibility.Visible;

					if ( _calibrationPhase == CalibrationPhase.NotCalibrating )
					{
						MainWindow._steeringEffectsPage.RunCalibration_MairaButton.Disabled = false;
						MainWindow._steeringEffectsPage.StopCalibration_MairaButton.Disabled = true;
					}
					else
					{
						MainWindow._steeringEffectsPage.RunCalibration_MairaButton.Disabled = true;
						MainWindow._steeringEffectsPage.StopCalibration_MairaButton.Disabled = false;
					}
				}

				if ( RedrawCalibrationGraph )
				{
					DrawCalibrationGraphGrid();
					DrawCalibrationGraphData();

					RedrawCalibrationGraph = false;
				}

				var speedInKPH = app.Simulator.Speed * MathZ.MPSToKPH;

				if ( speedInKPH >= 1f )
				{
					var steeringWheelAngleInDegrees = ( app.Simulator.SteeringWheelAngle * MathZ.RadiansToDegrees + app.Simulator.SteeringOffsetInDegrees ) * ( 10f / app.Simulator.SteeringRatio );
					var yawRateInDegreesPerSecond = MathF.Abs( app.Simulator.YawRate * MathZ.RadiansToDegrees ) / speedInKPH;

					MainWindow._steeringEffectsPage.CalibrationDot_Border_Transform.X = steeringWheelAngleInDegrees;
					MainWindow._steeringEffectsPage.CalibrationDot_Border_Transform.Y = ( yawRateInDegreesPerSecond + 0.5f ) * -100f + 6f;
					MainWindow._steeringEffectsPage.CalibrationDot_Border.Visibility = Visibility.Visible;

					MainWindow._steeringEffectsPage.LiveInfo_TextBlock.Text = $"{steeringWheelAngleInDegrees:F0}{localization[ "Degrees" ]}  ➤  {yawRateInDegreesPerSecond:F2}{localization[ "DegreesPerSecond" ]}";
					MainWindow._steeringEffectsPage.LiveInfo_Border.Visibility = Visibility.Visible;

				}
				else
				{
					MainWindow._steeringEffectsPage.CalibrationDot_Border.Visibility = Visibility.Hidden;
					MainWindow._steeringEffectsPage.LiveInfo_Border.Visibility = Visibility.Hidden;
				}
			}
		}
	}
}
