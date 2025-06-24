
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

using PInvoke;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.DataContext;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaKnob : UserControl
{
	private const int ResetHoldMilliseconds = 1000;

	private readonly RotateTransform _knobRotation = new( 0, 0.5, 0.5 );

	private bool _isDragging = false;
	private POINT _draggingCenter;

	private readonly DispatcherTimer _resetDispatcherTimer = new() { Interval = TimeSpan.FromMilliseconds( 20 ) };
	private DateTime _resetStartTime;
	private bool _isResetting;

	public MairaKnob()
	{
		InitializeComponent();

		KnobImage.RenderTransform = _knobRotation;

		_resetDispatcherTimer.Tick += ResetDispatcherTimer_Tick;

		UpdateLabelVisual();
	}

	#region Dependency Properties

	public static readonly DependencyProperty TitleProperty = DependencyProperty.Register( nameof( Title ), typeof( string ), typeof( MairaKnob ), new PropertyMetadata( string.Empty, OnTitleChanged ) );

	public string Title
	{
		get => (string) GetValue( TitleProperty );
		set => SetValue( TitleProperty, value );
	}

	private static void OnTitleChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		var control = (MairaKnob) d;

		control.UpdateLabelVisual();
	}

	public static readonly DependencyProperty ValueProperty = DependencyProperty.Register( nameof( Value ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( 0f, OnValueChanged ) );

	public float Value
	{
		get => (float) GetValue( ValueProperty );
		set => SetValue( ValueProperty, value );
	}

	private static void OnValueChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		var control = (MairaKnob) d;

		control.UpdateKnobVisual( (float) e.OldValue, (float) e.NewValue );
	}

	public static readonly DependencyProperty ValueStringProperty = DependencyProperty.Register( nameof( ValueString ), typeof( string ), typeof( MairaKnob ), new PropertyMetadata( "0" ) );

	public string ValueString
	{
		get => (string) GetValue( ValueStringProperty );
		set => SetValue( ValueStringProperty, value );
	}

	public static readonly DependencyProperty SmallValueChangeStepProperty = DependencyProperty.Register( nameof( SmallValueChangeStep ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( 0.01f ) );

	public float SmallValueChangeStep
	{
		get => (float) GetValue( SmallValueChangeStepProperty );
		set => SetValue( SmallValueChangeStepProperty, value );
	}

	public static readonly DependencyProperty LargeValueChangeStepProperty = DependencyProperty.Register( nameof( LargeValueChangeStep ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( 0.1f ) );

	public float LargeValueChangeStep
	{
		get => (float) GetValue( LargeValueChangeStepProperty );
		set => SetValue( LargeValueChangeStepProperty, value );
	}

	public static readonly DependencyProperty RotationMultiplierProperty = DependencyProperty.Register( nameof( RotationMultiplier ), typeof( float ), typeof( MairaKnob ), new PropertyMetadata( 1f ) );

	public float RotationMultiplier
	{
		get => (float) GetValue( RotationMultiplierProperty );
		set => SetValue( RotationMultiplierProperty, value );
	}

	public static readonly DependencyProperty ValueChangedCallbackProperty = DependencyProperty.Register( nameof( ValueChangedCallback ), typeof( Action<float> ), typeof( MairaKnob ) );

	public Action<float> ValueChangedCallback
	{
		get => (Action<float>) GetValue( ValueChangedCallbackProperty );
		set => SetValue( ValueChangedCallbackProperty, value );
	}

	public static readonly DependencyProperty ContextSwitchesProperty = DependencyProperty.Register( nameof( ContextSwitches ), typeof( ContextSwitches ), typeof( MairaKnob ), new PropertyMetadata( null ) );

	public ContextSwitches ContextSwitches
	{
		get => (ContextSwitches) GetValue( ContextSwitchesProperty );
		set => SetValue( ContextSwitchesProperty, value );
	}

	public static readonly DependencyProperty PlusButtonMappingsProperty = DependencyProperty.Register( nameof( PlusButtonMappings ), typeof( ButtonMappings ), typeof( MairaKnob ) );

	public ButtonMappings PlusButtonMappings
	{
		get => (ButtonMappings) GetValue( PlusButtonMappingsProperty );
		set => SetValue( PlusButtonMappingsProperty, value );
	}

	public static readonly DependencyProperty MinusButtonMappingsProperty = DependencyProperty.Register( nameof( MinusButtonMappings ), typeof( ButtonMappings ), typeof( MairaKnob ) );

	public ButtonMappings MinusButtonMappings
	{
		get => (ButtonMappings) GetValue( MinusButtonMappingsProperty );
		set => SetValue( MinusButtonMappingsProperty, value );
	}

	public static readonly DependencyProperty ShowCurveProperty = DependencyProperty.Register( nameof( ShowCurve ), typeof( bool ), typeof( MairaKnob ), new PropertyMetadata( false, OnShowCurveChanged ) );

	public bool ShowCurve
	{
		get => (bool) GetValue( ShowCurveProperty );
		set => SetValue( ShowCurveProperty, value );
	}

	private static void OnShowCurveChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		var control = (MairaKnob) d;

		control.UpdateKnobVisual( control.Value, control.Value );
	}

	public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register( nameof( DefaultValue ), typeof( float? ), typeof( MairaKnob ), new PropertyMetadata( null ) );

	public float? DefaultValue
	{
		get => (float?) GetValue( DefaultValueProperty );
		set => SetValue( DefaultValueProperty, value );
	}

	#endregion

	#region Event Handlers

	private void KnobImage_Image_MouseDown( object sender, MouseButtonEventArgs e )
	{
		if ( e.LeftButton == MouseButtonState.Pressed )
		{
			_isDragging = true;

			User32.GetCursorPos( out _draggingCenter );

			_ = User32.ShowCursor( false );

			Mouse.Capture( (Image) sender );
		}
	}

	private void KnobImage_Image_MouseMove( object sender, MouseEventArgs e )
	{
		if ( _isDragging )
		{
			User32.GetCursorPos( out POINT current );

			var delta = ( current.x - _draggingCenter.x ) + ( current.y - _draggingCenter.y );

			if ( delta != 0 )
			{
				AdjustValue( delta * SmallValueChangeStep * 0.25f );

				User32.SetCursorPos( _draggingCenter.x, _draggingCenter.y );
			}
		}
	}

	private void KnobImage_Image_MouseUp( object sender, MouseButtonEventArgs e )
	{
		if ( _isDragging && ( e.ChangedButton == MouseButton.Left ) )
		{
			EndDrag();
		}
	}

	private void KnobImage_Image_LostMouseCapture( object sender, MouseEventArgs e )
	{
		if ( _isDragging )
		{
			EndDrag();
		}
	}

	private void Plus_MairaMappableButton_Click( object sender, RoutedEventArgs e ) => AdjustValue( LargeValueChangeStep );
	private void Minus_MairaMappableButton_Click( object sender, RoutedEventArgs e ) => AdjustValue( -LargeValueChangeStep );

	private void Label_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		var app = App.Instance!;

		e.Handled = true;

		if ( ContextSwitches != null )
		{
			app.Logger.WriteLine( "[MairaKnob] Showing update context switches window" );

			var updateContextSwitchesWindow = new UpdateContextSwitchesWindow( ContextSwitches )
			{
				Owner = app.MainWindow
			};

			updateContextSwitchesWindow.ShowDialog();
		}
	}

	private void Value_Label_PreviewMouseRightButtonDown( object sender, MouseButtonEventArgs e )
	{
		if ( ( Keyboard.Modifiers != ModifierKeys.None ) || ( DefaultValue == null ) )
		{
			return;
		}

		e.Handled = true;

		_resetStartTime = DateTime.Now;
		_isResetting = true;

		_resetDispatcherTimer.Start();

		Mouse.OverrideCursor = Cursors.None;

		CursorCountdownOverlay.Start();
	}

	private void Value_Label_PreviewMouseRightButtonUp( object sender, MouseButtonEventArgs e ) => CancelReset();
	private void Value_Label_MouseLeave( object sender, MouseEventArgs e ) => CancelReset();

	#endregion

	#region Logic

	private void AdjustValue( float amount )
	{
		float oldValue = Value;
		float newValue = oldValue + amount;

		Value = newValue;

		ValueChangedCallback?.Invoke( newValue );
	}
	private void EndDrag()
	{
		_isDragging = false;

		_ = User32.ShowCursor( true );

		Mouse.Capture( null );
	}

	private void UpdateLabelVisual()
	{
		if ( Title == string.Empty )
		{
			Label.Visibility = Visibility.Collapsed;
		}
		else
		{
			Label.Visibility = Visibility.Visible;
		}
	}

	private void UpdateKnobVisual( float oldValue, float newValue )
	{
		float delta = newValue - oldValue;

		_knobRotation.Angle += delta * RotationMultiplier * 50f;

		if ( ShowCurve )
		{
			var imageWidth = (int) CurveImage.Width;
			var imageHeight = (int) CurveImage.Height;

			var power = Misc.CurveToPower( Value );

			var dv = new DrawingVisual();

			using ( var dc = dv.RenderOpen() )
			{
				var darkGray = new SolidColorBrush( System.Windows.Media.Color.FromRgb( 48, 48, 48 ) );

				dc.DrawRectangle( darkGray, null, new Rect( 0, 0, imageWidth, imageHeight ) );

				var penGrid = new Pen( new SolidColorBrush( System.Windows.Media.Color.FromRgb( 0, 0, 0 ) ), 1 );

				for ( var x = imageWidth / 4; x < imageWidth; x += imageWidth / 4 )
				{
					dc.DrawLine( penGrid, new Point( x, 0 ), new Point( x, imageHeight ) );
				}

				for ( var y = imageWidth / 4; y < imageHeight; y += imageHeight / 4 )
				{
					dc.DrawLine( penGrid, new Point( 0, y ), new Point( imageWidth, y ) );
				}

				var geometry = new StreamGeometry();

				using ( var ctx = geometry.Open() )
				{
					for ( var x = 0; x < imageWidth; x++ )
					{
						float xf = x / (float) ( imageWidth - 1 );
						float yf = MathF.Pow( xf, power );

						int y = imageHeight - 1 - (int) ( yf * ( imageHeight - 1 ) );

						if ( x == 0 )
						{
							ctx.BeginFigure( new Point( x, y ), false, false );
						}
						else
						{
							ctx.LineTo( new Point( x, y ), true, false );
						}
					}
				}

				dc.DrawGeometry( null, new Pen( System.Windows.Media.Brushes.White, 1.5f ), geometry );
			}

			var renderTargetBitmap = new RenderTargetBitmap( imageWidth, imageHeight, 96, 96, PixelFormats.Pbgra32 );

			renderTargetBitmap.Render( dv );

			CurveImage.Source = renderTargetBitmap;
			CurveImage.Visibility = Visibility.Visible;
		}
		else
		{
			CurveImage.Visibility = Visibility.Collapsed;
		}
	}

	private void ResetDispatcherTimer_Tick( object? sender, EventArgs e )
	{
		if ( !_isResetting )
		{
			return;
		}

		var elapsed = ( DateTime.Now - _resetStartTime ).TotalMilliseconds;
		var progress = 1 - Math.Min( 1, elapsed / ResetHoldMilliseconds );

		CursorCountdownOverlay.UpdateProgress( progress );

		if ( elapsed >= ResetHoldMilliseconds )
		{
			if ( DefaultValue != null )
			{
				Value = (float) DefaultValue;
			}

			CancelReset();
		}
	}

	private void CancelReset()
	{
		_isResetting = false;

		_resetDispatcherTimer.Stop();

		CursorCountdownOverlay.Stop();

		Mouse.OverrideCursor = null;
	}

	#endregion
}
