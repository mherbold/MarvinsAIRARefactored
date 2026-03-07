
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class WindPage : UserControl
{
	bool _testingLeft = false;
	bool _testingRight = false;

	public WindPage()
	{
		InitializeComponent();
	}

	#region User Control Events

	private void ConnectToWind_MairaSwitch_Toggled( object sender, EventArgs e )
	{
		var app = App.Instance!;

		if ( ConnectToWind_MairaSwitch.IsOn )
		{
			if ( !app.Wind.IsConnected )
			{
				app.Wind.Connect();
			}
		}
		else
		{
			app.Wind.Disconnect();
		}
	}

	private void LeftTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		_testingLeft = !_testingLeft;

		app.Wind.TestLeft( _testingLeft );
	}

	private void RightTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		_testingRight = !_testingRight;

		app.Wind.TestRight( _testingRight );
	}

	#endregion
}
