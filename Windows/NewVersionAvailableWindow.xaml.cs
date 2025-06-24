
using System.Windows;

namespace MarvinsAIRARefactored.Windows;

public partial class NewVersionAvailableWindow : Window
{
	public bool DownloadUpdate { get; private set; } = false;

	public NewVersionAvailableWindow( string currentVersion, string changeLog )
	{
		var app = App.Instance!;

		app.MainWindow.MakeWindowVisible();

		InitializeComponent();

		var lines = changeLog.Split( [ "\r\n", "\n", "\r" ], StringSplitOptions.None );

		CurrentVersion_Label.Content = currentVersion;
		ChangeLog_TextBlock.Text = string.Join( Environment.NewLine, lines.Where( line => !string.IsNullOrWhiteSpace( line ) ).Select( line => $"• {line}" ) );
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
