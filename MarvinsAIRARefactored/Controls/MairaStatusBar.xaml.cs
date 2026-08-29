
using System.Windows;
using System.Windows.Input;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaStatusBar : UserControl
{
	public enum StatusStyleEnum
	{
		Normal,
		Warning,
		Error
	};

	/// <summary>Raised when the user clicks anywhere on the bar - the main window opens the tuning profile
	/// manager from it. Fires in every status state, including "the simulator is not running".</summary>
	public event RoutedEventHandler? Click;

	// True between a left press that landed on the bar and its release. The app menu's scrim closes on mouse DOWN,
	// so the click that dismisses the menu releases over the bar - and a bar that fired on the release alone would
	// open the tuning profile manager unbidden. Pairing the press with the release (with the mouse captured in
	// between) makes the bar respond only to clicks that actually started on it.
	private bool _pressed = false;

	public MairaStatusBar()
	{
		InitializeComponent();
	}

	#region User Control Events

	private void Status_Border_MouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		_pressed = Status_Border.CaptureMouse();
	}

	private void Status_Border_MouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( !_pressed )
		{
			return;
		}

		_pressed = false;

		Status_Border.ReleaseMouseCapture();

		// a press dragged off the bar before it was released is not a click
		if ( Status_Border.InputHitTest( e.GetPosition( Status_Border ) ) != null )
		{
			Click?.Invoke( this, e );
		}
	}

	private void Status_Border_LostMouseCapture( object sender, System.Windows.Input.MouseEventArgs e )
	{
		_pressed = false;
	}

	#endregion

	#region Dependency Properties

	public static readonly DependencyProperty StatusText1Property = DependencyProperty.Register( nameof( StatusText1 ), typeof( string ), typeof( MairaStatusBar ), new PropertyMetadata( string.Empty ) );

	public string StatusText1
	{
		get => (string) GetValue( StatusText1Property );
		set => SetValue( StatusText1Property, value );
	}

	public static readonly DependencyProperty StatusText2Property = DependencyProperty.Register( nameof( StatusText2 ), typeof( string ), typeof( MairaStatusBar ), new PropertyMetadata( string.Empty ) );

	public string StatusText2
	{
		get => (string) GetValue( StatusText2Property );
		set => SetValue( StatusText2Property, value );
	}

	public static readonly DependencyProperty StatusText3Property = DependencyProperty.Register( nameof( StatusText3 ), typeof( string ), typeof( MairaStatusBar ), new PropertyMetadata( string.Empty ) );

	public string StatusText3
	{
		get => (string) GetValue( StatusText3Property );
		set => SetValue( StatusText3Property, value );
	}

	public static readonly DependencyProperty StatusText4Property = DependencyProperty.Register( nameof( StatusText4 ), typeof( string ), typeof( MairaStatusBar ), new PropertyMetadata( string.Empty ) );

	public string StatusText4
	{
		get => (string) GetValue( StatusText4Property );
		set => SetValue( StatusText4Property, value );
	}

	public static readonly DependencyProperty StatusStyleProperty = DependencyProperty.Register( nameof( StatusStyle ), typeof( StatusStyleEnum ), typeof( MairaStatusBar ), new PropertyMetadata( StatusStyleEnum.Normal ) );

	public StatusStyleEnum StatusStyle
	{
		get => (StatusStyleEnum) GetValue( StatusStyleProperty );
		set => SetValue( StatusStyleProperty, value );
	}

	#endregion
}
