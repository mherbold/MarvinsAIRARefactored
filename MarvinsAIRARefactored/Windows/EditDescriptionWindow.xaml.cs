
using System.Windows;

namespace MarvinsAIRARefactored.Windows;

public partial class EditDescriptionWindow : Window
{
	public bool Confirmed { get; private set; } = false;
	public string DescriptionText { get; private set; } = string.Empty;

	public EditDescriptionWindow( string initialText )
	{
		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		Description_MairaTextBox.Value = initialText;

		Loaded += EditDescriptionWindow_Loaded;
	}

	private void EditDescriptionWindow_Loaded( object sender, RoutedEventArgs e )
	{
		Description_MairaTextBox.Focus();
	}

	private void OK_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirmed = true;
		DescriptionText = Description_MairaTextBox.Value ?? string.Empty;

		Close();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirmed = false;

		Close();
	}
}
