
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaButton : UserControl
{
	private DispatcherTimer? _timer = null;
	private bool _blink = false;

	public MairaButton()
	{
		InitializeComponent();
	}

	private void MairaButton_Loaded( object sender, RoutedEventArgs e )
	{
		UpdateImageSources();
	}

	public static readonly DependencyProperty TitleProperty = DependencyProperty.Register( nameof( Title ), typeof( string ), typeof( MairaButton ), new PropertyMetadata( "" ) );

	public string Title
	{
		get => (string) GetValue( TitleProperty );
		set => SetValue( TitleProperty, value );
	}

	public static readonly DependencyProperty BehindIconProperty = DependencyProperty.Register( nameof( BehindIcon ), typeof( ImageSource ), typeof( MairaButton ), new PropertyMetadata( null ) );

	public ImageSource BehindIcon
	{
		get => (ImageSource) GetValue( BehindIconProperty );
		set => SetValue( BehindIconProperty, value );
	}

	public static readonly DependencyProperty ButtonIconProperty = DependencyProperty.Register( nameof( ButtonIcon ), typeof( ImageSource ), typeof( MairaButton ), new PropertyMetadata( null ) );

	public ImageSource ButtonIcon
	{
		get => (ImageSource) GetValue( ButtonIconProperty );
		set => SetValue( ButtonIconProperty, value );
	}

	public static readonly DependencyProperty BlinkProperty = DependencyProperty.Register( nameof( Blink ), typeof( bool ), typeof( MairaButton ), new PropertyMetadata( false, OnBlinkChanged ) );

	public bool Blink
	{
		get => (bool) GetValue( BlinkProperty );
		set => SetValue( BlinkProperty, value );
	}

	private static void OnBlinkChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaButton mairaButton )
		{
			mairaButton.UpdateBlink();
		}
	}

	public static readonly DependencyProperty SmallProperty = DependencyProperty.Register( nameof( Small ), typeof( bool ), typeof( MairaButton ), new PropertyMetadata( false, OnSmallChanged ) );

	public bool Small
	{
		get => (bool) GetValue( SmallProperty );
		set => SetValue( SmallProperty, value );
	}

	private static void OnSmallChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaButton mairaButton )
		{
			mairaButton.UpdateImageSources();
		}
	}

	public static readonly DependencyProperty DisabledProperty = DependencyProperty.Register( nameof( Disabled ), typeof( bool ), typeof( MairaButton ), new PropertyMetadata( false, OnDisabledChanged ) );

	public bool Disabled
	{
		get => (bool) GetValue( DisabledProperty );
		set => SetValue( DisabledProperty, value );
	}

	private static void OnDisabledChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaButton mairaButton )
		{
			mairaButton.Disabled_Image.Visibility = mairaButton.Disabled ? Visibility.Visible : Visibility.Hidden;
		}
	}

	private void UpdateBlink()
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

				_blink = true;

				ButtonIcon_Image.Visibility = Visibility.Visible;

				_timer.Start();
			}
		}
		else
		{
			if ( _timer != null )
			{
				_timer.Stop();

				_timer = null;

				ButtonIcon_Image.Visibility = Visibility.Visible;
			}
		}
	}

	private void OnTimer( object? sender, EventArgs e )
	{
		ButtonIcon_Image.Visibility = _blink ? Visibility.Hidden : Visibility.Visible;

		_blink = !_blink;
	}

	protected virtual void UpdateImageSources()
	{
		if ( Small )
		{
			Normal_Image.Source = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/round_button_small.png" ) as ImageSource;
			Pressed_Image.Source = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/round_button_pressed_small.png" ) as ImageSource;
			Disabled_Image.Source = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/round_button_disabled_small.png" ) as ImageSource;

			Normal_Image.Height = 24;
			Pressed_Image.Height = 24;
			Disabled_Image.Height = 24;

			ButtonIcon_Image.Height = 24;
		}
		else
		{
			Normal_Image.Source = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/round_button.png" ) as ImageSource;
			Pressed_Image.Source = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/round_button_pressed.png" ) as ImageSource;
			Disabled_Image.Source = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/round_button_disabled.png" ) as ImageSource;

			Normal_Image.Height = 48;
			Pressed_Image.Height = 48;
			Disabled_Image.Height = 48;

			ButtonIcon_Image.Height = 48;
		}
	}

	public event RoutedEventHandler? Click;

	private void Button_Click( object sender, RoutedEventArgs e )
	{
		if ( !Disabled )
		{
			Click?.Invoke( this, e );
		}
	}

	private void Button_PreviewMouseDown( object sender, RoutedEventArgs e )
	{
		Pressed_Image.Visibility = Visibility.Visible;
	}

	private void Button_PreviewMouseUp( object sender, RoutedEventArgs e )
	{
		Pressed_Image.Visibility = Visibility.Hidden;
	}
}
