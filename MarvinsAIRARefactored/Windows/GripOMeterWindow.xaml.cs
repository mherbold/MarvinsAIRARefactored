
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
	private readonly OverlayWindowMover _mover;

	private float _smoothedSkidSlip = 0f;
	private float _smoothedSeatOfPants = 0f;

	private const float SmoothingFactor = 0.15f;

	private readonly SolidColorBrush[] _backgroundBrushes = new SolidColorBrush[ 16 ];
	private byte _backgroundBrushesAlpha = 255;

	public GripOMeterWindow()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[GripOMeterWindow] Constructor >>>" );

		InitializeComponent();

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		_scaler = new OverlayWindowScaler( this, ScaleIcon, () => settings.OverlaysGripOMeterWindowScale, value => settings.OverlaysGripOMeterWindowScale = value );
		_mover = new OverlayWindowMover( this, DragIcon );

		var rectangle = settings.OverlaysGripOMeterWindowPosition;

		Left = rectangle.Location.X;
		Top = rectangle.Location.Y;

		WindowStartupLocation = WindowStartupLocation.Manual;

		// Build the 16 grip gradient brushes up front (rebuilt only when the opacity changes) to avoid allocations during high-frequency Tick calls.
		// The opacity setting is baked into the alpha channel so it tints the background fill without affecting the black border.

		_backgroundBrushesAlpha = (byte) Math.Round( Math.Clamp( settings.OverlaysGripOMeterWindowOpacity, 0f, 1f ) * 255f );

		RebuildBackgroundBrushes( _backgroundBrushesAlpha );

		MakeDraggable();

		Show();

		app.Logger.WriteLine( "[GripOMeterWindow] <<< Constructor" );
	}

	// Rebuilds the 16 grip gradient brushes with the given alpha so the opacity setting tints only the background fill (the black border stays opaque).
	private void RebuildBackgroundBrushes( byte alpha )
	{
		for ( var i = 0; i < _backgroundBrushes.Length; i++ )
		{
			var gradientValue = (byte) Math.Round( i * 255.0 / ( _backgroundBrushes.Length - 1 ) );

			var brush = new SolidColorBrush( Color.FromArgb( alpha, 255, gradientValue, gradientValue ) );

			brush.Freeze();

			_backgroundBrushes[ i ] = brush;
		}
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

	// Re-applies the window position from settings (used when the active overlay layout changes, e.g. on a car
	// change). Scale re-applies automatically through the XAML ScaleTransform binding, so only position is set here.
	public void ApplyPositionFromSettings()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var rectangle = settings.OverlaysGripOMeterWindowPosition;

		Left = rectangle.Location.X;
		Top = rectangle.Location.Y;
	}

	public void MakeDraggable()
	{
		_isDraggable = App.Instance!.OverlaysDraggable;

		ScaleIcon.Visibility = _isDraggable ? Visibility.Visible : Visibility.Collapsed;
		GearIcon.Visibility = _isDraggable ? Visibility.Visible : Visibility.Collapsed;
		DragIcon.Visibility = _isDraggable ? Visibility.Visible : Visibility.Collapsed;

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

	private void Window_PreviewMouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( ReferenceEquals( e.OriginalSource, ScaleIcon ) )
		{
			_scaler.Start();

			e.Handled = true;
		}
		else if ( ReferenceEquals( e.OriginalSource, DragIcon ) )
		{
			_mover.Start();

			e.Handled = true;
		}
		else if ( ReferenceEquals( e.OriginalSource, GearIcon ) )
		{
			e.Handled = true;

			Dispatcher.BeginInvoke( OpenOverlaySettings );
		}
	}

	private void Window_PreviewMouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( _scaler.IsScaling )
		{
			_scaler.Stop();

			e.Handled = true;
		}
		else if ( _mover.IsMoving )
		{
			_mover.Stop();

			e.Handled = true;
		}
	}

	private void Window_MouseMove( object sender, System.Windows.Input.MouseEventArgs e )
	{
		_scaler.Update();
		_mover.Update();
	}

	private void OpenOverlaySettings()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		// the Grip-O-Meter has no background panel, so only its window opacity can be customized
		var overlaySettingsWindow = new OverlaySettingsWindow(
			colorEnabled: false,
			getColor: null,
			setColor: null,
			defaultColor: "#000000",
			getOpacity: () => settings.OverlaysGripOMeterWindowOpacity,
			setOpacity: value => settings.OverlaysGripOMeterWindowOpacity = value,
			defaultOpacity: 1f )
		{
			Owner = this
		};

		overlaySettingsWindow.ShowDialog();
	}

	public void Tick( App app )
	{
		if ( Visibility == Visibility.Visible )
		{
			_updateCounter--;

			if ( _updateCounter <= 0 )
			{
				_updateCounter = UpdateInterval;

				// rebuild the gradient brushes if the opacity setting changed since the last update
				var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

				var backgroundAlpha = (byte) Math.Round( Math.Clamp( settings.OverlaysGripOMeterWindowOpacity, 0f, 1f ) * 255f );

				if ( backgroundAlpha != _backgroundBrushesAlpha )
				{
					_backgroundBrushesAlpha = backgroundAlpha;

					RebuildBackgroundBrushes( backgroundAlpha );
				}

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
