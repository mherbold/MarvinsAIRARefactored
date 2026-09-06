
using System.Windows;

namespace MarvinsAIRARefactored.Windows;

// Generic confirmation dialog, following the DeleteControllerProfileWindow contract: show it modally, then read
// Confirmed. Both the title and the message are supplied by the caller, so one window covers all of the tuning
// profile manager's "are you sure" moments.
public partial class ConfirmActionWindow : Window
{
	public bool Confirmed { get; private set; } = false;

	public ConfirmActionWindow( string title, string message )
	{
		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		Title = title;

		Message_TextBlock.Text = message;
	}

	private void OK_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirmed = true;

		Close();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Confirmed = false;

		Close();
	}
}
