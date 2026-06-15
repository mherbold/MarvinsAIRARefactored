
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

using MarvinsAIRARefactored.Classes;

using Color = System.Windows.Media.Color;

namespace MarvinsAIRARefactored.Windows;

public partial class GripOMeterWindow : Window
{
	private const int UpdateInterval = 0;

	private int _updateCounter = UpdateInterval + 3;

	private bool _isDraggable = false;

	private readonly OverlayWindowScaler _scaler;

	private float _smoothedSkidSlip = 0f;
	private float _smoothedSeatOfPants = 0f;

	private const float SmoothingFactor = 0.15f;

	private readonly SolidColorBrush[] _backgroundBrushes = new SolidColorBrush[ 16 ];

	public GripOMeterWindow()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GripOMeterWindow] Constructor >>>" );

		InitializeComponent();

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		_scaler = new OverlayWindowScaler( this, () => settings.OverlaysGripOMeterWindowScale, value => settings.OverlaysGripOMeterWindowScale = value );

		var rectangle = settings.OverlaysGripOMeterWindowPosition;

		Left = rectangle.Location.X;
		Top = rectangle.Location.Y;

		WindowStartupLocation = WindowStartupLocation.Manual;

		// Create 16 brushes with channel values from 0..255 in 16 steps and freeze them to avoid allocations during high-frequency Tick calls

		for ( var i = 0; i < _backgroundBrushes.Length; i++ )
		{
			var gradientValue = (byte) Math.Round( i * 255.0 / ( _backgroundBrushes.Length - 1 ) );

			var brush = new SolidColorBrush( Color.FromRgb( 255, gradientValue, gradientValue ) );

			brush.Freeze();

			_backgroundBrushes[ i ] = brush;
		}

		MakeDraggable();

		Show();

		app.Logger.WriteLine( "[GripOMeterWindow] <<< Constructor" );
	}

	private void Window_LocationChanged( object sender, EventArgs e )
	{
		if ( IsVisible && ( WindowState == WindowState.Normal ) )
		{
			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

			var rectangle = settings.OverlaysGripOMeterWindowPosition;

			rectangle.Location = new System.Drawing.Point( (int) RestoreBounds.Left, (int) RestoreBounds.Top );

			settings.OverlaysGripOMeterWindowPosition = rectangle;
		}
	}

	public void ResetWindow()
	{
		Left = 0;
		Top = 0;
	}

	public void MakeDraggable()
	{
		_isDraggable = App.Instance!.OverlaysDraggable;

		ScaleIcon.Visibility = _isDraggable ? Visibility.Visible : Visibility.Collapsed;

		var hwnd = new WindowInteropHelper( this ).Handle;

		var exStyle = PInvoke.GetWindowLong( (HWND) hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE );

		if ( _isDraggable )
		{
			exStyle &= ~(int) WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
		}
		else
		{
			exStyle |= (int) WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
		}

		_ = PInvoke.SetWindowLong( (HWND) hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle );
	}

	protected override void OnMouseLeftButtonDown( MouseButtonEventArgs e )
	{
		if ( _isDraggable )
		{
			DragMove();
		}
	}

	private void Window_PreviewMouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( ReferenceEquals( e.OriginalSource, ScaleIcon ) )
		{
			_scaler.Start();

			e.Handled = true;
		}
	}

	private void Window_PreviewMouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( _scaler.IsScaling )
		{
			_scaler.Stop();

			e.Handled = true;
		}
	}

	private void Window_MouseMove( object sender, System.Windows.Input.MouseEventArgs e )
	{
		_scaler.Update();
	}

	public void Tick( App app )
	{
		if ( Visibility == Visibility.Visible )
		{
			_updateCounter--;

			if ( _updateCounter <= 0 )
			{
				_updateCounter = UpdateInterval;

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
}
