
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaButton : UserControl
{
	public event RoutedEventHandler? Click;
	private DispatcherTimer? _timer = null;

	public MairaButton()
	{
		InitializeComponent();

		UpdateBlinkTimer();
	}

	#region User Control Events

	private void Button_Click( object sender, RoutedEventArgs e )
	{
		if ( !Disabled )
		{
			Click?.Invoke( this, e );
		}
	}

	private void OnTimer( object? sender, EventArgs e )
	{
		IsBlinking = !IsBlinking;
	}

	#endregion

	#region Dependency Properties

	public static readonly DependencyProperty LabelProperty = DependencyProperty.Register( nameof( Label ), typeof( string ), typeof( MairaButton ), new PropertyMetadata( string.Empty ) );

	public string Label
	{
		get => (string) GetValue( LabelProperty );
		set => SetValue( LabelProperty, value );
	}

	public static readonly DependencyProperty LabelOnRightProperty = DependencyProperty.Register( nameof( LabelOnRight ), typeof( bool ), typeof( MairaButton ), new PropertyMetadata( false ) );

	public bool LabelOnRight
	{
		get => (bool) GetValue( LabelOnRightProperty );
		set => SetValue( LabelOnRightProperty, value );
	}

	public static readonly DependencyProperty IconProperty = DependencyProperty.Register( nameof( Icon ), typeof( ImageSource ), typeof( MairaButton ), new PropertyMetadata( null ) );

	public ImageSource Icon
	{
		get => (ImageSource) GetValue( IconProperty );
		set => SetValue( IconProperty, value );
	}

	public static readonly DependencyProperty BlinkIconProperty = DependencyProperty.Register( nameof( BlinkIcon ), typeof( ImageSource ), typeof( MairaButton ), new PropertyMetadata( null ) );

	public ImageSource BlinkIcon
	{
		get => (ImageSource) GetValue( BlinkIconProperty );
		set => SetValue( BlinkIconProperty, value );
	}

	public static readonly DependencyProperty DefaultFrameProperty = DependencyProperty.Register( nameof( DefaultFrame ), typeof( ImageSource ), typeof( MairaButton ), new PropertyMetadata( new BitmapImage( new Uri( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Buttons/ring-large-default.png", UriKind.Absolute ) ) ) );

	public ImageSource DefaultFrame
	{
		get => (ImageSource) GetValue( DefaultFrameProperty );
		set => SetValue( DefaultFrameProperty, value );
	}

	public static readonly DependencyProperty MappedFrameProperty = DependencyProperty.Register( nameof( MappedFrame ), typeof( ImageSource ), typeof( MairaButton ), new PropertyMetadata( new BitmapImage( new Uri( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Buttons/ring-large-mapped.png", UriKind.Absolute ) ) ) );

	public ImageSource MappedFrame
	{
		get => (ImageSource) GetValue( MappedFrameProperty );
		set => SetValue( MappedFrameProperty, value );
	}

	public static readonly DependencyProperty PressedFrameProperty = DependencyProperty.Register( nameof( PressedFrame ), typeof( ImageSource ), typeof( MairaButton ), new PropertyMetadata( new BitmapImage( new Uri( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Buttons/ring-large-pressed.png", UriKind.Absolute ) ) ) );

	public ImageSource PressedFrame
	{
		get => (ImageSource) GetValue( PressedFrameProperty );
		set => SetValue( PressedFrameProperty, value );
	}

	public static readonly DependencyProperty IconWidthProperty = DependencyProperty.Register( nameof( IconWidth ), typeof( double ), typeof( MairaButton ), new PropertyMetadata( 48.0 ) );

	public double IconWidth
	{
		get => (double) GetValue( IconWidthProperty );
		set => SetValue( IconWidthProperty, value );
	}

	public static readonly DependencyProperty IconHeightProperty = DependencyProperty.Register( nameof( IconHeight ), typeof( double ), typeof( MairaButton ), new PropertyMetadata( 48.0 ) );

	public double IconHeight
	{
		get => (double) GetValue( IconHeightProperty );
		set => SetValue( IconHeightProperty, value );
	}

	public static readonly DependencyProperty IsPressedProperty = DependencyProperty.Register( nameof( IsPressed ), typeof( bool ), typeof( MairaButton ), new PropertyMetadata( false ) );

	public bool IsPressed
	{
		get => (bool) GetValue( IsPressedProperty );
		set => SetValue( IsPressedProperty, value );
	}

	public static readonly DependencyProperty IsMappedProperty = DependencyProperty.Register( nameof( IsMapped ), typeof( bool ), typeof( MairaButton ), new PropertyMetadata( false ) );

	public bool IsMapped
	{
		get => (bool) GetValue( IsMappedProperty );
		set => SetValue( IsMappedProperty, value );
	}

	public static readonly DependencyProperty IsBlinkingProperty = DependencyProperty.Register( nameof( IsBlinking ), typeof( bool ), typeof( MairaButton ), new PropertyMetadata( false ) );

	public bool IsBlinking
	{
		get => (bool) GetValue( IsBlinkingProperty );
		set => SetValue( IsBlinkingProperty, value );
	}

	public static readonly DependencyProperty BlinkProperty = DependencyProperty.Register( nameof( Blink ), typeof( bool ), typeof( MairaButton ), new PropertyMetadata( false, OnBlinkChanged ) );

	public bool Blink
	{
		get => (bool) GetValue( BlinkProperty );
		set => SetValue( BlinkProperty, value );
	}

	public static readonly DependencyProperty IsSmallProperty = DependencyProperty.Register( nameof( IsSmall ), typeof( bool ), typeof( MairaButton ), new PropertyMetadata( false ) );

	public bool IsSmall
	{
		get => (bool) GetValue( IsSmallProperty );
		set => SetValue( IsSmallProperty, value );
	}

	public static readonly DependencyProperty DisabledProperty = DependencyProperty.Register( nameof( Disabled ), typeof( bool ), typeof( MairaButton ), new PropertyMetadata( false ) );

	public bool Disabled
	{
		get => (bool) GetValue( DisabledProperty );
		set => SetValue( DisabledProperty, value );
	}

	#endregion

	#region Dependency Property Changed Events

	private static void OnBlinkChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaButton mairaButton )
		{
			mairaButton.UpdateBlinkTimer();
		}
	}

	#endregion

	#region Logic

	private void UpdateBlinkTimer()
	{
		if ( Blink )
		{
			if ( _timer == null )
			{
				_timer = new()
				{
					Interval = TimeSpan.FromSeconds( 1 )
				};

				_timer.Tick += OnTimer;

				_timer.Start();

				IsBlinking = true;
			}
		}
		else
		{
			if ( _timer != null )
			{
				_timer.Stop();

				_timer = null;

				IsBlinking = false;
			}
		}
	}

	#endregion
}
