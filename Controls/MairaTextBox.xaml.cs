
using System.Windows;
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

	#endregion

	#region Dependency Properties

	public static readonly DependencyProperty LabelProperty = DependencyProperty.Register( nameof( Label ), typeof( string ), typeof( MairaTextBox ), new PropertyMetadata( string.Empty ) );

	public string Label
	{
		get => (string) GetValue( LabelProperty );
		set => SetValue( LabelProperty, value );
	}

	public static readonly DependencyProperty ValueProperty = DependencyProperty.Register( nameof( Value ), typeof( string ), typeof( MairaTextBox ), new PropertyMetadata( string.Empty ) );

	public string Value
	{
		get => (string) GetValue( ValueProperty );
		set => SetValue( ValueProperty, value );
	}

	public static readonly DependencyProperty IsNumericOnlyProperty = DependencyProperty.Register( nameof( IsNumericOnly ), typeof( bool ), typeof( MairaTextBox ) );

	public bool IsNumericOnly
	{
		get => (bool) GetValue( ValueProperty );
		set => SetValue( ValueProperty, value );
	}

	#endregion
}
