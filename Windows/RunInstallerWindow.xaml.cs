
using System.Windows;

namespace MarvinsAIRARefactored.Windows;

public partial class RunInstallerWindow : Window
{
	public bool InstallUpdate { get; private set; } = false;

	public RunInstallerWindow( string localFilePath )
	{
		var app = App.Instance!;

		app.MainWindow.MakeWindowVisible();

		InitializeComponent();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		InstallUpdate = false;

		Close();
	}

	private void ThumbsUp_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		InstallUpdate = true;

		Close();
	}
}
