using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class SeatBeltTensionerPage : UserControl
{
	public SeatBeltTensionerPage()
	{
		InitializeComponent();
	}

	#region User Control Events

	private void ConnectToSbt_MairaSwitch_Toggled( object sender, EventArgs e )
	{
		var app = App.Instance!;

		if ( ConnectToSbt_MairaSwitch.IsOn )
		{
			if ( !app.SeatBeltTensioner.IsConnected )
			{
				app.SeatBeltTensioner.Connect();
			}
		}
		else
		{
			app.SeatBeltTensioner.Disconnect();
		}
	}

	#endregion
}
