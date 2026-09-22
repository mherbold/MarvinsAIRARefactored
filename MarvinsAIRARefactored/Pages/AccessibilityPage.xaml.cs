
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using AccessibilityComponent = MarvinsAIRARefactored.Components.Accessibility;
using AppDataContext = MarvinsAIRARefactored.DataContext.DataContext;
using AppSettings = MarvinsAIRARefactored.DataContext.Settings;

using Point = System.Windows.Point;
using Size = System.Windows.Size;
using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class AccessibilityPage : UserControl
{
	// room around the plot for the axis labels
	private const double PlotMarginX = 16.0;
	private const double PlotMarginTop = 28.0;
	private const double PlotMarginBottom = 28.0;

	// the plot shows a little past the remapped limits (and never less than this many degrees each way)
	private const float PlotSpanPadding = 1.15f;
	private const float MinimumPlotSpanDegrees = 20f;

	// the dashed "at this speed" curve is redrawn when the speed factor moves by more than this
	private const float SpeedCurveRedrawThreshold = 0.02f;

	private float _plotSpanDegrees = 180f;
	private double _plotCenterX = 0.0;
	private double _plotCenterY = 0.0;
	private double _plotScaleX = 1.0;
	private double _plotScaleY = 1.0;

	private float _drawnSpeedFactor = 1f;

	public AccessibilityPage()
	{
		InitializeComponent();

		AppDataContext.Instance.Settings.PropertyChanged += Settings_PropertyChanged;

		// re-subscribe to the new Settings instance whenever it is replaced (e.g. after settings file load)
		AppDataContext.Instance.PropertyChanged += ( _, e ) =>
		{
			if ( e.PropertyName == nameof( AppDataContext.Settings ) )
			{
				AppDataContext.Instance.Settings.PropertyChanged += Settings_PropertyChanged;

				RedrawCurve();
			}
		};
	}

	public void Update()
	{
		var localization = AppDataContext.Instance.Localization;

		LeftLabel_TextBlock.Text = localization[ "WheelTurnedLeft" ];
		RightLabel_TextBlock.Text = localization[ "WheelTurnedRight" ];
		FullLeftLabel_TextBlock.Text = localization[ "FullLeftLock" ];
		FullRightLabel_TextBlock.Text = localization[ "FullRightLock" ];

		RedrawCurve();

		var app = App.Instance;

		// the page can be refreshed during startup, before the accessibility component exists
		if ( app?.Accessibility != null )
		{
			UpdateLiveState( app );
		}
	}

	private void Settings_PropertyChanged( object? sender, PropertyChangedEventArgs e )
	{
		if ( ( e.PropertyName == null ) || !e.PropertyName.StartsWith( "Accessibility", StringComparison.Ordinal ) || e.PropertyName.EndsWith( "String", StringComparison.Ordinal ) )
		{
			return;
		}

		// mapped knob buttons can change these from the input polling thread
		if ( !Dispatcher.CheckAccess() )
		{
			_ = Dispatcher.InvokeAsync( RedrawCurve );

			return;
		}

		RedrawCurve();
	}

	private void Curve_Canvas_SizeChanged( object sender, SizeChangedEventArgs e )
	{
		RedrawCurve();
	}

	#region Curve

	// screen x for a wheel angle (left positive, drawn on the LEFT side of the plot)
	private double AngleToX( float angleDegrees ) => _plotCenterX - angleDegrees * _plotScaleX;

	// screen y for a fraction of the car's lock (left positive, so full left lock is at the BOTTOM and full right lock
	// at the top)
	private double LockFractionToY( float lockFraction ) => _plotCenterY + lockFraction * _plotScaleY;

	private void RedrawCurve()
	{
		var width = Curve_Canvas.ActualWidth;
		var height = Curve_Canvas.ActualHeight;

		if ( ( width <= PlotMarginX * 4 ) || ( height <= PlotMarginTop + PlotMarginBottom ) )
		{
			return;
		}

		var settings = AppDataContext.Instance.Settings;

		var leftLimitDegrees = settings.AccessibilityCenterOffset + settings.AccessibilityLeftRange;
		var rightLimitDegrees = settings.AccessibilityCenterOffset - settings.AccessibilityRightRange;

		_plotSpanDegrees = MathF.Max( MinimumPlotSpanDegrees, MathF.Max( leftLimitDegrees, -rightLimitDegrees ) * PlotSpanPadding );

		_plotCenterX = width / 2.0;
		_plotCenterY = PlotMarginTop + ( height - PlotMarginTop - PlotMarginBottom ) / 2.0;
		_plotScaleX = ( width / 2.0 - PlotMarginX ) / _plotSpanDegrees;
		_plotScaleY = ( height - PlotMarginTop - PlotMarginBottom ) / 2.0;

		// axes through the true wheel center and zero steering

		var axesGeometry = new StreamGeometry();

		using ( var context = axesGeometry.Open() )
		{
			context.BeginFigure( new Point( AngleToX( 0f ), PlotMarginTop ), false, false );
			context.LineTo( new Point( AngleToX( 0f ), height - PlotMarginBottom ), true, false );

			context.BeginFigure( new Point( PlotMarginX, _plotCenterY ), false, false );
			context.LineTo( new Point( width - PlotMarginX, _plotCenterY ), true, false );
		}

		axesGeometry.Freeze();

		Axes_Path.Data = axesGeometry;

		// dashed lines at full lock and at the remapped physical limits of each side

		var limitsGeometry = new StreamGeometry();

		using ( var context = limitsGeometry.Open() )
		{
			context.BeginFigure( new Point( PlotMarginX, LockFractionToY( 1f ) ), false, false );
			context.LineTo( new Point( width - PlotMarginX, LockFractionToY( 1f ) ), true, false );

			context.BeginFigure( new Point( PlotMarginX, LockFractionToY( -1f ) ), false, false );
			context.LineTo( new Point( width - PlotMarginX, LockFractionToY( -1f ) ), true, false );

			context.BeginFigure( new Point( AngleToX( leftLimitDegrees ), PlotMarginTop ), false, false );
			context.LineTo( new Point( AngleToX( leftLimitDegrees ), height - PlotMarginBottom ), true, false );

			context.BeginFigure( new Point( AngleToX( rightLimitDegrees ), PlotMarginTop ), false, false );
			context.LineTo( new Point( AngleToX( rightLimitDegrees ), height - PlotMarginBottom ), true, false );
		}

		limitsGeometry.Freeze();

		Limits_Path.Data = limitsGeometry;

		// the remap curve (at standing speed), dimmed while the remap is switched off

		Curve_Path.Data = BuildCurveGeometry( settings, 1f );
		Curve_Path.Opacity = settings.AccessibilityRemapEnabled ? 1.0 : 0.35;

		RedrawSpeedCurve( settings, App.Instance?.Accessibility?.LastSpeedFactor ?? 1f );

		// labels - the curve runs from full left lock at the bottom left to full right lock at the top right, so the
		// wheel direction labels sit by the ends of the zero-steering axis (above it on the left, below it on the
		// right) and the full lock labels outside their dashed lines, where the curve never goes

		PlaceLabel( LeftLabel_TextBlock, PlotMarginX, _plotCenterY - 20 );
		PlaceLabel( RightLabel_TextBlock, width - PlotMarginX - MeasureWidth( RightLabel_TextBlock ), _plotCenterY + 4 );
		PlaceLabel( FullLeftLabel_TextBlock, PlotMarginX, LockFractionToY( 1f ) + 4 );
		PlaceLabel( FullRightLabel_TextBlock, width - PlotMarginX - MeasureWidth( FullRightLabel_TextBlock ), LockFractionToY( -1f ) - 20 );
	}

	private void RedrawSpeedCurve( AppSettings settings, float speedFactor )
	{
		_drawnSpeedFactor = speedFactor;

		if ( settings.AccessibilityRemapEnabled && ( speedFactor < 1f - SpeedCurveRedrawThreshold ) )
		{
			SpeedCurve_Path.Data = BuildCurveGeometry( settings, speedFactor );
			SpeedCurve_Path.Visibility = Visibility.Visible;
		}
		else
		{
			SpeedCurve_Path.Visibility = Visibility.Collapsed;
		}
	}

	private StreamGeometry BuildCurveGeometry( AppSettings settings, float speedFactor )
	{
		var remapParameters = new AccessibilityComponent.RemapParameters( settings, 0f, speedFactor );

		var geometry = new StreamGeometry();

		var firstX = PlotMarginX;
		var lastX = Curve_Canvas.ActualWidth - PlotMarginX;

		using ( var context = geometry.Open() )
		{
			for ( var x = firstX; x <= lastX; x += 1.0 )
			{
				var angleDegrees = (float) ( ( _plotCenterX - x ) / _plotScaleX );

				var lockFraction = AccessibilityComponent.RemapToLockFraction( angleDegrees, in remapParameters );

				var point = new Point( x, LockFractionToY( lockFraction ) );

				if ( x == firstX )
				{
					context.BeginFigure( point, false, false );
				}
				else
				{
					context.LineTo( point, true, true );
				}
			}
		}

		geometry.Freeze();

		return geometry;
	}

	private static void PlaceLabel( TextBlock textBlock, double x, double y )
	{
		Canvas.SetLeft( textBlock, x );
		Canvas.SetTop( textBlock, y );
	}

	private static double MeasureWidth( TextBlock textBlock )
	{
		textBlock.Measure( new Size( double.PositiveInfinity, double.PositiveInfinity ) );

		return textBlock.DesiredSize.Width;
	}

	#endregion

	#region Live state

	// refreshed ~20 times a second from Accessibility.Tick while this page is showing
	public void UpdateLiveState( App app )
	{
		var settings = AppDataContext.Instance.Settings;
		var localization = AppDataContext.Instance.Localization;

		var accessibility = app.Accessibility;

		// vJoy driver warning

		if ( settings.GameBridgeSendSteeringToVJoy && app.VirtualJoystick.Faulted )
		{
			VJoyStatus_TextBlock.Text = localization[ "VJoyNotAvailable" ];
		}
		else
		{
			VJoyStatus_TextBlock.Text = string.Empty;
		}

		UpdateSweepButton( app );

		// the dashed curve follows the speed-sensitive steering

		if ( MathF.Abs( accessibility.LastSpeedFactor - _drawnSpeedFactor ) > SpeedCurveRedrawThreshold )
		{
			RedrawSpeedCurve( settings, accessibility.LastSpeedFactor );
		}

		// the dot and readout follow the wheel

		if ( !accessibility.PassthroughActive )
		{
			Dot_Ellipse.Visibility = Visibility.Collapsed;
			LiveReadout_TextBlock.Text = localization[ "AccessibilityPassthroughOff" ];

			return;
		}

		if ( settings.GameBridgeSteeringTestEnabled )
		{
			Dot_Ellipse.Visibility = Visibility.Collapsed;
			LiveReadout_TextBlock.Text = localization[ "AccessibilityCalibrationModeOn" ];

			return;
		}

		var inputAngleDegrees = accessibility.LastInputAngleDegrees;
		var lockFraction = Math.Clamp( accessibility.LastLockFraction, -1f, 1f );

		var dotX = Math.Clamp( AngleToX( inputAngleDegrees ), PlotMarginX, Curve_Canvas.ActualWidth - PlotMarginX );
		var dotY = LockFractionToY( lockFraction );

		Canvas.SetLeft( Dot_Ellipse, dotX - Dot_Ellipse.Width / 2.0 );
		Canvas.SetTop( Dot_Ellipse, dotY - Dot_Ellipse.Height / 2.0 );

		Dot_Ellipse.Visibility = Visibility.Visible;

		var wheelText = FormatSigned( inputAngleDegrees, $"0{localization[ "Degrees" ]}", "AngleLeftFormat", "AngleRightFormat" );
		var steeringText = FormatSigned( lockFraction * 100f, $"0{localization[ "Percent" ]}", "PercentLeftFormat", "PercentRightFormat" );

		LiveReadout_TextBlock.Text = string.Format( localization[ "AccessibilityLiveReadout" ], wheelText, steeringText );
	}

	private static string FormatSigned( float value, string zeroText, string leftFormatKey, string rightFormatKey )
	{
		var localization = AppDataContext.Instance.Localization;

		var roundedValue = MathF.Round( value );

		if ( roundedValue == 0f )
		{
			return zeroText;
		}

		return string.Format( localization[ ( roundedValue > 0f ) ? leftFormatKey : rightFormatKey ], MathF.Abs( roundedValue ) );
	}

	#endregion

	#region Calibration buttons

	// the calibration buttons drive the vJoy axis directly (the real wheel is not passed through while calibration
	// mode is on) - the 90 degree buttons use the wheelbase rotation range, so the game learns the real degrees

	private void SteeringWheelLeft_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.Accessibility.SetCalibrationSteering( -1f );
	}

	private void SteeringWheel90Left_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.Accessibility.SetCalibrationSteering( AccessibilityComponent.GetAxisPositionForAngle( 90f ) );
	}

	private void SteeringWheelCenter_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.Accessibility.SetCalibrationSteering( 0f );
	}

	private void SteeringWheel90Right_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.Accessibility.SetCalibrationSteering( AccessibilityComponent.GetAxisPositionForAngle( -90f ) );
	}

	private void SteeringWheelRight_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.Accessibility.SetCalibrationSteering( 1f );
	}

	private void SteeringWheelSweep_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.Accessibility.ToggleSteeringSweep();
	}

	public void UpdateSweepButton( App app )
	{
		SteeringWheelSweep_MairaButton.Blink = app.Accessibility.SteeringSweepActive;
	}

	#endregion
}
