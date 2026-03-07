
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using Color = System.Windows.Media.Color;

using PInvoke;

using static PInvoke.User32;

namespace MarvinsAIRARefactored.Windows;

public partial class GripOMeterWindow : Window
{
	private bool _initialized = false;
	private bool _isDraggable = false;

	private float _smoothedSkidSlip = 0f;
	private float _smoothedSeatOfPants = 0f;

	private const float SmoothingFactor = 0.15f;

	private readonly SolidColorBrush[] _backgroundBrushes = new SolidColorBrush[ 16 ];

	public GripOMeterWindow()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GripOMeterWindow] Constructor >>>" );

		InitializeComponent();

		app.Logger.WriteLine( "[GripOMeterWindow] <<< Constructor" );
	}

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GripOMeterWindow] Initialize >>>" );

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var rectangle = settings.SteeringEffectsGripOMeterWindowPosition;

		Left = rectangle.Location.X;
		Top = rectangle.Location.Y;

		WindowStartupLocation = WindowStartupLocation.Manual;

		_initialized = true;

		// Create 16 brushes with channel values from 0..255 in 16 steps and freeze them to avoid allocations during high-frequency Tick calls

		for ( var i = 0; i < _backgroundBrushes.Length; i++ )
		{
			var gradientValue = (byte) Math.Round( i * 255.0 / ( _backgroundBrushes.Length - 1 ) );

			var brush = new SolidColorBrush( Color.FromRgb( 255, gradientValue, gradientValue ) );

			brush.Freeze();

			_backgroundBrushes[ i ] = brush;
		}

		UpdateVisibility();

		app.Logger.WriteLine( "[GripOMeterWindow] <<< Initialize" );
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
			Dispatcher.BeginInvoke( () =>
			{
				var app = App.Instance!;

				var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

				if ( settings.SteeringEffectsShowGripOMeterWindow && app.Simulator.IsOnTrack )
				{
					Show();
					MakeDraggable();
				}
				else
				{
					Hide();
				}
			} );
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
		if ( Visibility == Visibility.Visible )
		{
			_smoothedSkidSlip += ( app.SteeringEffects.SkidSlip - _smoothedSkidSlip ) * SmoothingFactor;
			_smoothedSeatOfPants += ( app.SteeringEffects.SeatOfPantsEffect - _smoothedSeatOfPants ) * SmoothingFactor;

			GripOMeter_Ball_Transform.X = _smoothedSkidSlip * 144f;
			GripOMeter_SeatOfPants_Transform.X = _smoothedSeatOfPants * 144f;

			var intensity = Math.Abs( _smoothedSkidSlip );

			var brushIndex = (int) Math.Round( ( 1.0 - intensity ) * ( _backgroundBrushes.Length - 1 ) );

			brushIndex = Math.Clamp( brushIndex, 0, _backgroundBrushes.Length - 1 );

			GripOMeter_Background.Background = _backgroundBrushes[ brushIndex ];
		}
	}
}
