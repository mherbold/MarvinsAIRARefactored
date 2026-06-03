
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Controls;

public partial class MairaTextBox : UserControl
{
	public MairaTextBox()
	{
		InitializeComponent();
	}

	#region User Control Events

	private void InnerTextBox_KeyDown( object sender, System.Windows.Input.KeyEventArgs e )
	{
		if ( e.Key == Key.Enter )
		{
			if ( sender is TextBox textBox )
			{
				var binding = textBox.GetBindingExpression( TextBox.TextProperty );

				binding?.UpdateSource();

				textBox.MoveFocus( new TraversalRequest( FocusNavigationDirection.Next ) );

				e.Handled = true;
			}
		}
	}

	private bool _updatingPassword;

	private void InnerPasswordBox_PasswordChanged( object sender, RoutedEventArgs e )
	{
		if ( sender is PasswordBox passwordBox && !_updatingPassword )
		{
			_updatingPassword = true;
			Value = passwordBox.Password;
			_updatingPassword = false;
		}
	}

	#endregion

	#region Dependency Properties

	public static readonly DependencyProperty LabelProperty = DependencyProperty.Register( nameof( Label ), typeof( string ), typeof( MairaTextBox ), new PropertyMetadata( string.Empty ) );

	public string Label
	{
		get => (string) GetValue( LabelProperty );
		set => SetValue( LabelProperty, value );
	}

	public static readonly DependencyProperty ValueProperty = DependencyProperty.Register( nameof( Value ), typeof( string ), typeof( MairaTextBox ), new PropertyMetadata( string.Empty, OnValueChanged ) );

	private static void OnValueChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
	{
		if ( d is MairaTextBox control )
		{
			if ( control.IsPassword && !control._updatingPassword )
			{
				control._updatingPassword = true;

				if ( control.FindName( "InnerPasswordBox" ) is PasswordBox passwordBox )
				{
					passwordBox.Password = (string) ( e.NewValue ?? string.Empty );
				}

				control._updatingPassword = false;
			}

			control.RaiseEvent( new RoutedEventArgs( ValueChangedEvent, control ) );
		}
	}

	public string Value
	{
		get => (string) GetValue( ValueProperty );
		set => SetValue( ValueProperty, value );
	}

	public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent(
		nameof( ValueChanged ), RoutingStrategy.Bubble, typeof( RoutedEventHandler ), typeof( MairaTextBox ) );

	public event RoutedEventHandler ValueChanged
	{
		add => AddHandler( ValueChangedEvent, value );
		remove => RemoveHandler( ValueChangedEvent, value );
	}

	public static readonly DependencyProperty IsNumericOnlyProperty = DependencyProperty.Register( nameof( IsNumericOnly ), typeof( bool ), typeof( MairaTextBox ) );

	public bool IsNumericOnly
	{
		get => (bool) GetValue( ValueProperty );
		set => SetValue( ValueProperty, value );
	}

	public static readonly DependencyProperty IsPasswordProperty = DependencyProperty.Register( nameof( IsPassword ), typeof( bool ), typeof( MairaTextBox ), new PropertyMetadata( false ) );

	public bool IsPassword
	{
		get => (bool) GetValue( IsPasswordProperty );
		set => SetValue( IsPasswordProperty, value );
	}

	public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register( nameof( Placeholder ), typeof( string ), typeof( MairaTextBox ), new PropertyMetadata( string.Empty ) );

	/// <summary>Placeholder text shown when the text box is empty and has no label.</summary>
	public string Placeholder
	{
		get => (string) GetValue( PlaceholderProperty );
		set => SetValue( PlaceholderProperty, value );
	}

	#endregion
}
