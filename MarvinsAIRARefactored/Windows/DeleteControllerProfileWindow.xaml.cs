
using System.Windows;

namespace MarvinsAIRARefactored.Windows;

public partial class DeleteControllerProfileWindow : Window
{
	public bool Confirmed { get; private set; } = false;

	public DeleteControllerProfileWindow( string profileName )
	{
		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		ProfileName_TextBlock.Text = profileName;
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
