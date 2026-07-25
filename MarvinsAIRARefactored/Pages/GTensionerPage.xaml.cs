
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Controls;

namespace MarvinsAIRARefactored.Pages;

public partial class GTensionerPage : UserControl, INotifyPropertyChanged
{
	private List<KeyValuePair<GTensioner.AxisMode, string>> _axisModeOptions = [];

	public event PropertyChangedEventHandler? PropertyChanged;

	private string _surgeMinGString = "---";
	private string _surgeMaxGString = "---";
	private string _swayMinGString = "---";
	private string _swayMaxGString = "---";
	private string _heaveMinGString = "---";
	private string _heaveMaxGString = "---";

	private string _autoTuneSurgeEffectiveString = "---";
	private string _autoTuneSwayEffectiveString = "---";
	private string _autoTuneHeaveEffectiveString = "---";
	private string _autoTuneSopEffectiveString = "---";

	public string SurgeMinGString { get => _surgeMinGString; set => SetField( ref _surgeMinGString, value ); }
	public string SurgeMaxGString { get => _surgeMaxGString; set => SetField( ref _surgeMaxGString, value ); }
	public string SwayMinGString { get => _swayMinGString; set => SetField( ref _swayMinGString, value ); }
	public string SwayMaxGString { get => _swayMaxGString; set => SetField( ref _swayMaxGString, value ); }
	public string HeaveMinGString { get => _heaveMinGString; set => SetField( ref _heaveMinGString, value ); }
	public string HeaveMaxGString { get => _heaveMaxGString; set => SetField( ref _heaveMaxGString, value ); }

	public string AutoTuneSurgeEffectiveString { get => _autoTuneSurgeEffectiveString; set => SetField( ref _autoTuneSurgeEffectiveString, value ); }
	public string AutoTuneSwayEffectiveString { get => _autoTuneSwayEffectiveString; set => SetField( ref _autoTuneSwayEffectiveString, value ); }
	public string AutoTuneHeaveEffectiveString { get => _autoTuneHeaveEffectiveString; set => SetField( ref _autoTuneHeaveEffectiveString, value ); }
	public string AutoTuneSopEffectiveString { get => _autoTuneSopEffectiveString; set => SetField( ref _autoTuneSopEffectiveString, value ); }

	private void SetField( ref string field, string value, [CallerMemberName] string? propertyName = null )
	{
		if ( field == value )
		{
			return;
		}

		field = value;

		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
	}

	public GTensionerPage()
	{
		InitializeComponent();
	}

	public void UpdateAxisModeOptions()
	{
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<GTensioner.AxisMode, string>
		{
			{ GTensioner.AxisMode.Disabled, localization[ "AxisModeDisabled" ] },
			{ GTensioner.AxisMode.Normal,   localization[ "AxisModeNormal" ] },
			{ GTensioner.AxisMode.Inverted, localization[ "AxisModeInverted" ] }
		};

		_axisModeOptions = dictionary.ToList();

		Dispatcher.Invoke( () =>
		{
			SurgeMode_MairaComboBox.ItemsSource = _axisModeOptions;
			SurgeMode_MairaComboBox.SelectedValue = settings.GTensionerSurgeMode;

			SwayMode_MairaComboBox.ItemsSource = _axisModeOptions;
			SwayMode_MairaComboBox.SelectedValue = settings.GTensionerSwayMode;

			HeaveMode_MairaComboBox.ItemsSource = _axisModeOptions;
			HeaveMode_MairaComboBox.SelectedValue = settings.GTensionerHeaveMode;

			SeatOfPantsMode_MairaComboBox.ItemsSource = _axisModeOptions;
			SeatOfPantsMode_MairaComboBox.SelectedValue = settings.GTensionerSeatOfPantsMode;
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

		var isRunning = app.GTensioner.IsTestRunning;

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

		LeftShoulderABS_TextBlock.Visibility = ( absActive ) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		LeftShoulderWheelSlip_TextBlock.Visibility = ( wheelSlipActive ) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		LeftShoulderRumble_TextBlock.Visibility = ( rumbleLeftActive ) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

		RightShoulderABS_TextBlock.Visibility = ( absActive ) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		RightShoulderWheelSlip_TextBlock.Visibility = ( wheelSlipActive ) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		RightShoulderRumble_TextBlock.Visibility = ( rumbleRightActive ) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
	}

	#region User Control Events

	private void ConnectToGt_MairaSwitch_Toggled( object sender, EventArgs e )
	{
		var app = App.Instance!;

		if ( ConnectToGt_MairaSwitch.IsOn )
		{
			if ( !app.GTensioner.IsConnected )
			{
				app.GTensioner.Connect();
			}
		}
		else
		{
			if ( app.GTensioner.IsConnected )
			{
				app.GTensioner.Disconnect();
			}
		}
	}

	private void RetryDevice_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.GTensioner.ScanForDevice();
	}

	private void SurgeMode_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( sender is MairaComboBox combo && combo.SelectedValue is GTensioner.AxisMode mode )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.GTensionerSurgeMode = mode;
		}
	}

	private void SwayMode_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( sender is MairaComboBox combo && combo.SelectedValue is GTensioner.AxisMode mode )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.GTensionerSwayMode = mode;
		}
	}

	private void HeaveMode_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( sender is MairaComboBox combo && combo.SelectedValue is GTensioner.AxisMode mode )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.GTensionerHeaveMode = mode;
		}
	}

	private void SeatOfPantsMode_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( sender is MairaComboBox combo && combo.SelectedValue is GTensioner.AxisMode mode )
		{
			MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.GTensionerSeatOfPantsMode = mode;
		}
	}

	private void SurgeTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.GTensioner.StartTest( GTensioner.TestAxis.Surge );

		UpdateTestStatus();
	}

	private void SwayTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.GTensioner.StartTest( GTensioner.TestAxis.Sway );

		UpdateTestStatus();
	}

	private void HeaveTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.GTensioner.StartTest( GTensioner.TestAxis.Heave );

		UpdateTestStatus();
	}

	private void StopTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.GTensioner.StopTest();

		UpdateTestStatus();
	}

	private void ABSTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.GTensioner.StartVibrationTest( GTensioner.TestVibrationEffect.ABS );

		UpdateTestStatus();
	}

	private void WheelSlipTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.GTensioner.StartVibrationTest( GTensioner.TestVibrationEffect.WheelSlip );

		UpdateTestStatus();
	}

	private void RumbleTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.GTensioner.StartVibrationTest( GTensioner.TestVibrationEffect.Rumble );

		UpdateTestStatus();
	}

	private void CalibrationSweepTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		App.Instance!.GTensioner.StartCalibrationSweepTest();

		UpdateTestStatus();
	}

	#endregion
}
