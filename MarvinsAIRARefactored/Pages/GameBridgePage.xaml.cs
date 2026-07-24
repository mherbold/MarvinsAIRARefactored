
using System.Windows;

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
		UpdateAdapterRow( app.GameBridge.AssettoCorsaRally, AssettoCorsaRally_MairaSwitch, AssettoCorsaRallyStatus_TextBlock );
		UpdateAdapterRow( app.GameBridge.RFactor2, RFactor2_MairaSwitch, RFactor2Status_TextBlock );
		UpdateAdapterRow( app.GameBridge.RaceRoom, RaceRoom_MairaSwitch, RaceRoomStatus_TextBlock );

		UpdateVJoyStatus( app );
	}

	// the steering test buttons drive the vJoy axis directly (the wheelbase passthrough is suspended while
	// the test toggle is on), so the user can move ONLY the vJoy axis while binding steering in the game -
	// the same 540-degree scale as the steering effects calibration robot is used for the 90 degree buttons

	private void SetTestSteering( float steering )
	{
		var app = App.Instance!;

		// a static position button always cancels a running sweep
		app.GameBridge.SteeringSweepActive = false;

		app.VirtualJoystick.Steering = steering;

		UpdateSweepButton( app );
	}

	private void SteeringWheelLeft_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		SetTestSteering( -1f );
	}

	private void SteeringWheel90Left_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		SetTestSteering( -( 90f / 540f ) );
	}

	private void SteeringWheelCenter_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		SetTestSteering( 0f );
	}

	private void SteeringWheel90Right_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		SetTestSteering( 90f / 540f );
	}

	private void SteeringWheelRight_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		SetTestSteering( 1f );
	}

	private void SteeringWheelSweep_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.GameBridge.SteeringSweepActive = !app.GameBridge.SteeringSweepActive;

		if ( !app.GameBridge.SteeringSweepActive )
		{
			app.VirtualJoystick.Steering = 0f;
		}

		UpdateSweepButton( app );
	}

	// the sweep button blinks (white/orange) while the sweep is running - also refreshed at 1 Hz from
	// GameBridge.Tick, which catches the sweep being cancelled by the toggles rather than by a click
	public void UpdateSweepButton( App app )
	{
		SteeringWheelSweep_MairaButton.Blink = app.GameBridge.SteeringSweepActive;
	}

	// warns the user (in MAIRA orange) when the steering passthrough is switched on but the vJoy driver is
	// missing or its device could not be acquired - also refreshed at 1 Hz from GameBridge.Tick while this
	// page is visible, since the fault is only discovered after the passthrough first tries to initialize
	public void UpdateVJoyStatus( App app )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		if ( settings.GameBridgeSendSteeringToVJoy && app.VirtualJoystick.Faulted )
		{
			VJoyStatus_TextBlock.Text = localization[ "VJoyNotAvailable" ];
		}
		else
		{
			VJoyStatus_TextBlock.Text = string.Empty;
		}
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
