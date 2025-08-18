
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using PInvoke;

using static PInvoke.User32;

using MarvinsAIRARefactored.Classes;

using Brushes = System.Windows.Media.Brushes;

namespace MarvinsAIRARefactored.Windows;

public partial class GripOMeter : Window
{
	private bool _initialized = false;
	private bool _isDraggable = false;

	public GripOMeter()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GripOMeter] Constructor >>>" );

		InitializeComponent();

		app.Logger.WriteLine( "[GripOMeter] <<< Constructor" );
	}

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GripOMeter] Initialize >>>" );

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var rectangle = settings.SteeringEffectsGripOMeterWindowPosition;

		Left = rectangle.Location.X;
		Top = rectangle.Location.Y;

		WindowStartupLocation = WindowStartupLocation.Manual;

		UpdateVisibility();

		_initialized = true;

		app.Logger.WriteLine( "[GripOMeter] <<< Initialize" );
	}

	private void Window_Loaded( object sender, RoutedEventArgs e )
	{
		var hwnd = new WindowInteropHelper( this ).Handle;

		var exStyle = User32.GetWindowLong( hwnd, WindowLongIndexFlags.GWL_EXSTYLE );

		_ = User32.SetWindowLong( hwnd, WindowLongIndexFlags.GWL_EXSTYLE, (SetWindowLongFlags) ( (uint) exStyle | (uint) SetWindowLongFlags.WS_EX_TOOLWINDOW ) ); // Prevent Alt+Tab visibility
	}

	private void Window_LocationChanged( object sender, EventArgs e )
	{
		if ( _initialized )
		{
			if ( IsVisible && ( WindowState == WindowState.Normal ) )
			{
				var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

				var rectangle = settings.SteeringEffectsGripOMeterWindowPosition;

				rectangle.Location = new System.Drawing.Point( (int) RestoreBounds.Left, (int) RestoreBounds.Top );

				settings.SteeringEffectsGripOMeterWindowPosition = rectangle;
			}
		}
	}

	public void ResetWindow()
	{
		Left = 0;
		Top = 0;
	}

	public void UpdateVisibility()
	{
		if ( _initialized )
		{
			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

			if ( settings.SteeringEffectsShowGripOMeterWindow )
			{
				Show();
				MakeDraggable();
			}
			else
			{
				Hide();
			}
		}
	}

	public void MakeDraggable()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		_isDraggable = settings.SteeringEffectsMakeGripOMeterDraggable;

		var hwnd = new WindowInteropHelper( this ).Handle;

		var exStyle = User32.GetWindowLong( hwnd, WindowLongIndexFlags.GWL_EXSTYLE );

		if ( _isDraggable )
		{
			_ = User32.SetWindowLong( hwnd, WindowLongIndexFlags.GWL_EXSTYLE, (SetWindowLongFlags) ( (uint) exStyle & (uint) ~SetWindowLongFlags.WS_EX_TRANSPARENT ) );
		}
		else
		{
			_ = User32.SetWindowLong( hwnd, WindowLongIndexFlags.GWL_EXSTYLE, (SetWindowLongFlags) ( (uint) exStyle | (uint) SetWindowLongFlags.WS_EX_TRANSPARENT ) );
		}
	}

	protected override void OnMouseLeftButtonDown( MouseButtonEventArgs e )
	{
		if ( _isDraggable )
		{
			DragMove();
		}
	}

	public void Tick( App app )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		if ( Visibility == Visibility.Visible )
		{
			float lerpFactor;

			var range = app.SteeringEffects.MaximumGrip - app.SteeringEffects.WarningGrip;

			if ( range > 0f )
			{
				lerpFactor = Math.Clamp( ( app.SteeringEffects.CurrentGrip - app.SteeringEffects.WarningGrip ) / range, 0f, 1f );

				lerpFactor = MathF.Pow( lerpFactor, Misc.CurveToPower( settings.SteeringEffectsUndersteerWheelVibrationCurve ) );
			}
			else
			{
				lerpFactor = ( app.SteeringEffects.CurrentGrip > app.SteeringEffects.MaximumGrip ) ? 1f : 0f;
			}

			var r = Misc.Lerp( 0f / 255f, 255f / 255f, lerpFactor );
			var g = Misc.Lerp( 0f / 255f, 140f / 255f, lerpFactor );
			var b = Misc.Lerp( 128f / 255f, 0f / 255f, lerpFactor );

			GripOMeter_Fill_Rectangle.Height = Math.Clamp( 324f * app.SteeringEffects.CurrentGrip, 0f, 376f );
			GripOMeter_Fill_Rectangle.Fill = new SolidColorBrush( System.Windows.Media.Color.FromScRgb( 1f, r, g, b ) );

			GripOMeter_Bar_Image.Margin = new Thickness( 0, 0, 0, Misc.Lerp( 0f, 324f, app.SteeringEffects.MaximumGrip ) - 16f );
		}
	}
}
