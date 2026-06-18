
using System.Windows;

namespace MarvinsAIRARefactored.Windows;

public partial class NewControllerProfileWindow : Window
{
	public bool Confirmed { get; private set; } = false;
	public string ProfileName { get; private set; } = string.Empty;
	public bool CopyFromCurrent { get; private set; } = false;

	public NewControllerProfileWindow()
	{
		InitializeComponent();

		Loaded += NewControllerProfileWindow_Loaded;
	}

	private void NewControllerProfileWindow_Loaded( object sender, RoutedEventArgs e )
	{
		ProfileName_MairaTextBox.Focus();
	}

	private void OK_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirmed = true;
		ProfileName = ProfileName_MairaTextBox.Value ?? string.Empty;
		CopyFromCurrent = CopyCurrentMappings_MairaSwitch.IsOn;

		Close();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirmed = false;

		Close();
	}
}
