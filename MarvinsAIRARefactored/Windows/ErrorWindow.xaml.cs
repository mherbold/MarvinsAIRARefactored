
using System.Windows;

using Clipboard = System.Windows.Clipboard;

namespace MarvinsAIRARefactored.Windows;

public partial class ErrorWindow : Window
{
	private readonly string _exceptionString;

	public ErrorWindow( string message, Exception? exception = null )
	{
		InitializeComponent();

#if ADMINBOXX

		Title = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "AdminBoxx" ];

#endif

		Message_TextBlock.Text = message;

		_exceptionString = exception?.ToString() ?? string.Empty;

		_exceptionString = _exceptionString.Replace( App.DevRootPath, string.Empty, StringComparison.OrdinalIgnoreCase );

		Details_TextBlock.Text = _exceptionString;
	}

	public static void ShowModal( string message, Exception? exception = null )
	{
		var dialog = new ErrorWindow( message, exception );

		dialog.ShowDialog();
	}

	private void CopyDetails_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		try
		{
			var textToCopy = $"{Message_TextBlock.Text}\r\n\r\n{_exceptionString}\r\n";

			Clipboard.SetText( textToCopy );
		}
		catch
		{
			// Swallow – clipboard can throw if unavailable
		}
	}

	private void Exit_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		DialogResult = true;

		Close();
	}
}
