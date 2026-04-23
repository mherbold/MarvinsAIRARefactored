
using System.Windows;
using System.Windows.Controls;

using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Controls;

namespace MarvinsAIRARefactored.Pages;

public partial class SeatBeltTensionerPage : UserControl
{
	private List<KeyValuePair<SeatBeltTensioner.AxisMode, string>> _axisModeOptions = [];

	public string SurgeMinGString { get; set; } = "---";
	public string SurgeMaxGString { get; set; } = "---";
	public string SwayMinGString { get; set; } = "---";
	public string SwayMaxGString { get; set; } = "---";
	public string HeaveMinGString { get; set; } = "---";
	public string HeaveMaxGString { get; set; } = "---";

	public SeatBeltTensionerPage()
	{
		InitializeComponent();
	}

	public void UpdateAxisModeOptions()
	{
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<SeatBeltTensioner.AxisMode, string>
		{
			{ SeatBeltTensioner.AxisMode.Disabled, localization[ "AxisModeDisabled" ] },
			{ SeatBeltTensioner.AxisMode.Normal,   localization[ "AxisModeNormal" ] },
			{ SeatBeltTensioner.AxisMode.Inverted, localization[ "AxisModeInverted" ] }
		};

		_axisModeOptions = dictionary.ToList();

		Dispatcher.Invoke( () =>
		{
			SurgeMode_MairaComboBox.ItemsSource = _axisModeOptions;
			SurgeMode_MairaComboBox.SelectedValue = settings.SeatBeltTensionerSurgeMode;

			SwayMode_MairaComboBox.ItemsSource = _axisModeOptions;
			SwayMode_MairaComboBox.SelectedValue = settings.SeatBeltTensionerSwayMode;

			HeaveMode_MairaComboBox.ItemsSource = _axisModeOptions;
			HeaveMode_MairaComboBox.SelectedValue = settings.SeatBeltTensionerHeaveMode;

			SeatOfPantsMode_MairaComboBox.ItemsSource = _axisModeOptions;
			SeatOfPantsMode_MairaComboBox.SelectedValue = settings.SeatBeltTensionerSeatOfPantsMode;
		} );
	}

	public void UpdateSeatOfPantsAlgorithmOptions()
	{
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<Components.SteeringEffects.SeatOfPantsAlgorithm, string>
		{
			{ Components.SteeringEffects.SeatOfPantsAlgorithm.YAcceleration,          localization[ "LateralAcceleration" ] },
			{ Components.SteeringEffects.SeatOfPantsAlgorithm.YVelocity,              localization[ "LateralVelocity" ] },
			{ Components.SteeringEffects.SeatOfPantsAlgorithm.YVelocityOverXVelocity, localization[ "RatioOfVelocities" ] }
		};

		Dispatcher.Invoke( () =>
		{
			SeatOfPantsAlgorithm_MairaComboBox.ItemsSource = dictionary.ToList();
			SeatOfPantsAlgorithm_MairaComboBox.SelectedValue = settings.SteeringEffectsSeatOfPantsAlgorithm;
		} );
	}

	public void UpdateTestStatus()
	{
		var app = App.Instance!;

		var isRunning = app.SeatBeltTensioner.IsTestRunning;

		SurgeTest_MairaButton.IsEnabled = !isRunning;
		SwayTest_MairaButton.IsEnabled = !isRunning;
		HeaveTest_MairaButton.IsEnabled = !isRunning;

		ABSTest_MairaButton.IsEnabled = !isRunning;
		WheelSlipTest_MairaButton.IsEnabled = !isRunning;
		RumbleTest_MairaButton.IsEnabled = !isRunning;

		CalibrationSweepTest_MairaButton.IsEnabled = !isRunning;
	}

	public void UpdateShoulderOverlays( bool absActive, bool wheelSlipActive, bool rumbleLeftActive, bool rumbleRightActive )
	{
		using var _ = Dispatcher.DisableProcessing();

		LeftShoulderABS_TextBlock.Visibility    = ( absActive )       ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		LeftShoulderWheelSlip_TextBlock.Visibility = ( wheelSlipActive ) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		LeftShoulderRumble_TextBlock.Visibility  = ( rumbleLeftActive )  ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

		RightShoulderABS_TextBlock.Visibility    = ( absActive )        ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		RightShoulderWheelSlip_TextBlock.Visibility = ( wheelSlipActive )  ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		RightShoulderRumble_TextBlock.Visibility  = ( rumbleRightActive )  ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
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
			if ( app.SeatBeltTensioner.IsConnected )
			{
				app.SeatBeltTensioner.Disconnect();
			}
		}
	}

	private void RetryDevice_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.SeatBeltTensioner.RetryDevice();
	}

	private void SurgeMode_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( sender is MairaComboBox combo && combo.SelectedValue is SeatBeltTensioner.AxisMode mode )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.SeatBeltTensionerSurgeMode = mode;
		}
	}

	private void SwayMode_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( sender is MairaComboBox combo && combo.SelectedValue is SeatBeltTensioner.AxisMode mode )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.SeatBeltTensionerSwayMode = mode;
		}
	}

	private void HeaveMode_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( sender is MairaComboBox combo && combo.SelectedValue is SeatBeltTensioner.AxisMode mode )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.SeatBeltTensionerHeaveMode = mode;
		}
	}

	private void SeatOfPantsMode_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( sender is MairaComboBox combo && combo.SelectedValue is SeatBeltTensioner.AxisMode mode )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.SeatBeltTensionerSeatOfPantsMode = mode;
		}
	}

	private void SurgeTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.SeatBeltTensioner.StartTest( SeatBeltTensioner.TestAxis.Surge );

		UpdateTestStatus();
	}

	private void SwayTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.SeatBeltTensioner.StartTest( SeatBeltTensioner.TestAxis.Sway );

		UpdateTestStatus();
	}

	private void HeaveTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.SeatBeltTensioner.StartTest( SeatBeltTensioner.TestAxis.Heave );

		UpdateTestStatus();
	}

	private void StopTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.SeatBeltTensioner.StopTest();

		UpdateTestStatus();
	}

	private void ABSTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.SeatBeltTensioner.StartVibrationTest( SeatBeltTensioner.TestVibrationEffect.ABS );

		UpdateTestStatus();
	}

	private void WheelSlipTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.SeatBeltTensioner.StartVibrationTest( SeatBeltTensioner.TestVibrationEffect.WheelSlip );

		UpdateTestStatus();
	}

	private void RumbleTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.SeatBeltTensioner.StartVibrationTest( SeatBeltTensioner.TestVibrationEffect.Rumble );

		UpdateTestStatus();
	}

	private void CalibrationSweepTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.SeatBeltTensioner.StartCalibrationSweepTest();

		UpdateTestStatus();
	}

	#endregion
}
