
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class AdminBoxxPage : UserControl
{
	public AdminBoxxPage()
	{
		InitializeComponent();
	}

	#region User Control Events

	private void ConnectToAdminBoxx_MairaSwitch_Toggled( object sender, EventArgs e )
	{
		var app = App.Instance!;

		if ( ConnectToAdminBoxx_MairaSwitch.IsOn )
		{
			if ( !app.AdminBoxx.IsConnected )
			{
				app.AdminBoxx.Connect();
			}
		}
		else
		{
			if ( app.AdminBoxx.IsConnected )
			{
				app.AdminBoxx.Disconnect();
			}
		}
	}

	private void Brightness_ValueChanged( float newValue )
	{
		var app = App.Instance!;

		app.AdminBoxx.ResendAllLEDs();
	}

	private void Volume_ValueChanged( float newValue )
	{
		var app = App.Instance!;

		app.AudioManager.Play( "beep", newValue );
	}

	private void Test_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.AdminBoxx.StartTestCycle();
	}

	private void RetryDevice_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.AdminBoxx.RetryDevice();
	}

	#endregion
}
