
using System.Windows;

namespace MarvinsAIRARefactored.Windows;

public partial class EditNodeNameWindow : Window
{
	public bool Confirmed { get; private set; } = false;
	public string NodeName { get; private set; } = string.Empty;

	public EditNodeNameWindow( string initialNodeName )
	{
		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		NodeName = initialNodeName;

		Loaded += EditNodeNameWindow_Loaded;
	}

	private void EditNodeNameWindow_Loaded( object sender, RoutedEventArgs e )
	{
		NodeName_MairaTextBox.Value = NodeName;
		NodeName_MairaTextBox.Focus();
	}

	private void OK_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirmed = true;
		NodeName = NodeName_MairaTextBox.Value ?? string.Empty;

		Close();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirmed = false;

		Close();
	}
}
