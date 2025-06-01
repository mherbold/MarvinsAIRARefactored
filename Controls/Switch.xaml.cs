
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Controls;

public partial class Switch : UserControl
{
	public Switch()
	{
		InitializeComponent();
		UpdateVisualState();
	}

	public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register( nameof( IsOn ), typeof( bool ), typeof( Switch ), new PropertyMetadata( false, OnIsOnChanged ) );

	public bool IsOn
	{
		get => (bool) GetValue( IsOnProperty );
		set => SetValue( IsOnProperty, value );
	}

	private static void OnIsOnChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		( (Switch) d ).UpdateVisualState();
		( (Switch) d ).Toggled?.Invoke( d, EventArgs.Empty );
	}

	public static readonly DependencyProperty LabelTextProperty = DependencyProperty.Register( nameof( LabelText ), typeof( string ), typeof( Switch ), new PropertyMetadata( string.Empty ) );

	public string LabelText
	{
		get => (string) GetValue( LabelTextProperty );
		set => SetValue( LabelTextProperty, value );
	}

	public event EventHandler? Toggled;

	private void UpdateVisualState()
	{
		OnImage.Visibility = IsOn ? Visibility.Visible : Visibility.Hidden;
		OffImage.Visibility = IsOn ? Visibility.Hidden : Visibility.Visible;
	}

	private void Button_Click( object sender, EventArgs e )
	{
		IsOn = !IsOn;

		Toggled?.Invoke( this, e );
	}
}
