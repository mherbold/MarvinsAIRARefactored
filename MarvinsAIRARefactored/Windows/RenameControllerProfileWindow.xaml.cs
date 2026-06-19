
using System.Windows;

namespace MarvinsAIRARefactored.Windows;

public partial class RenameControllerProfileWindow : Window
{
	public bool Confirmed { get; private set; } = false;
	public string ProfileName { get; private set; } = string.Empty;

	public RenameControllerProfileWindow( string currentProfileName )
	{
		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		ProfileName = currentProfileName;

		Loaded += RenameControllerProfileWindow_Loaded;
	}

	private void RenameControllerProfileWindow_Loaded( object sender, RoutedEventArgs e )
	{
		ProfileName_MairaTextBox.Value = ProfileName;
		ProfileName_MairaTextBox.Focus();
	}

	private void OK_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirmed = true;
		ProfileName = ProfileName_MairaTextBox.Value ?? string.Empty;

		Close();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirmed = false;

		Close();
	}
}
