
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.GameBridges;

using TextBlock = System.Windows.Controls.TextBlock;
using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class GameBridgePage : UserControl
{
	public GameBridgePage()
	{
		InitializeComponent();
	}

	public void Update()
	{
		var app = App.Instance;

		if ( app == null )
		{
			return;
		}

		UpdateAdapterRow( app.GameBridge.LeMansUltimate, LeMansUltimate_MairaSwitch, LeMansUltimateStatus_TextBlock );
		UpdateAdapterRow( app.GameBridge.AssettoCorsa, AssettoCorsa_MairaSwitch, AssettoCorsaStatus_TextBlock );
		UpdateAdapterRow( app.GameBridge.AssettoCorsaCompetizione, AssettoCorsaCompetizione_MairaSwitch, AssettoCorsaCompetizioneStatus_TextBlock );
		UpdateAdapterRow( app.GameBridge.AssettoCorsaEvo, AssettoCorsaEvo_MairaSwitch, AssettoCorsaEvoStatus_TextBlock );
		UpdateAdapterRow( app.GameBridge.RFactor2, RFactor2_MairaSwitch, RFactor2Status_TextBlock );
		UpdateAdapterRow( app.GameBridge.Automobilista2, Automobilista2_MairaSwitch, Automobilista2Status_TextBlock );
	}

	private static void UpdateAdapterRow( GameBridgeAdapter adapter, MairaSwitch mairaSwitch, TextBlock statusTextBlock )
	{
		var app = App.Instance!;

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		mairaSwitch.IsEnabled = adapter.IsImplemented;

		if ( !adapter.IsImplemented )
		{
			statusTextBlock.Text = localization[ "GameBridgeComingSoon" ];
		}
		else if ( app.GameBridge.ActiveAdapter == adapter )
		{
			statusTextBlock.Text = localization[ "Active" ];
		}
		else
		{
			statusTextBlock.Text = string.Empty;
		}
	}
}
