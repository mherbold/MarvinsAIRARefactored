
using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class WindPage : UserControl
{
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
			if ( !app.AdminBoxx.IsConnected )
			{
				app.Wind.Connect();
			}
		}
		else
		{
			app.Wind.Disconnect();
		}
	}

	#endregion
}
