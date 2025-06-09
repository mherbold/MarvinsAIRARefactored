
using System.Windows;

namespace MarvinsAIRARefactored.Windows;

public partial class NewVersionAvailableWindow : Window
{
	public bool DownloadUpdate { get; private set; } = false;

	public NewVersionAvailableWindow( string currentVersion, string changeLog )
	{
		InitializeComponent();

		CurrentVersion_Label.Content = currentVersion;
		ChangeLog_TextBlock.Text = changeLog.Trim();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		DownloadUpdate = false;

		Close();
	}

	private void ThumbsUp_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		DownloadUpdate = true;

		Close();
	}
}
