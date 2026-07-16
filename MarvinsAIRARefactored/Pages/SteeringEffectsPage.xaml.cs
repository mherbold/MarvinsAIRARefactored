
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using ComboBox = System.Windows.Controls.ComboBox;
using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Controls;

namespace MarvinsAIRARefactored.Pages;

public partial class SteeringEffectsPage : UserControl
{
	public SteeringEffectsPage()
	{
		InitializeComponent();

#if DEBUG
		Calibration_MairaGroupBox.Visibility = Visibility.Visible;
#else
		Calibration_MairaGroupBox.Visibility = Visibility.Collapsed;
#endif
	}

	#region User Control Events

	private void RunCalibration_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.SteeringEffects.RunCalibration();
	}

	private void StopCalibration_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.SteeringEffects.StopCalibration( false );
	}

	private void SteeringWheelLeft_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.VirtualJoystick.Steering = -1f;
	}

	private void SteeringWheelCenter_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.VirtualJoystick.Steering = 0f;
	}

	private void SteeringWheelRight_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.VirtualJoystick.Steering = 1f;
	}

	private void SteeringWheel90Left_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.VirtualJoystick.Steering = -( 90f / 540f );
	}

	private void MinThrottle_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.VirtualJoystick.Throttle = 0f;
	}

	private void MaxThrottle_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.VirtualJoystick.Throttle = 1f;
	}

	private void ShiftUp_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.VirtualJoystick.ShiftUp = true;
	}

	private void ShiftDown_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.VirtualJoystick.ShiftDown = true;
	}

	private void MinBrake_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.VirtualJoystick.Brake = 0f;
	}

	private void MaxBrake_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.VirtualJoystick.Brake = 1f;
	}

	#endregion

	#region Logic

	public void UpdateVibrationPatternOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffectsPage] SetVibrationPatternMairaComboBoxItemsSource >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var dictionary = new Dictionary<RacingWheel.VibrationPattern, string>
		{
			{ RacingWheel.VibrationPattern.None, localization[ "None" ] },
			{ RacingWheel.VibrationPattern.SineWave, localization[ "SineWave" ] },
			{ RacingWheel.VibrationPattern.SquareWave, localization[ "SquareWave" ] },
			{ RacingWheel.VibrationPattern.TriangleWave, localization[ "TriangleWave" ] },
			{ RacingWheel.VibrationPattern.SawtoothWaveIn, localization[ "SawtoothWaveIn" ] },
			{ RacingWheel.VibrationPattern.SawtoothWaveOut, localization[ "SawtoothWaveOut" ] }
		};

		UndersteerWheelVibrationPattern_MairaComboBox.ItemsSource = dictionary.ToList();

		OversteerWheelVibrationPattern_MairaComboBox.ItemsSource = dictionary.ToList();

		SeatOfPantsWheelVibrationPattern_MairaComboBox.ItemsSource = dictionary.ToList();

		app.Logger.WriteLine( "[SteeringEffectsPage] <<< SetVibrationPatternMairaComboBoxItemsSource" );
	}

	public void UpdateConstantForceDirectionOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffectsPage] SetConstantForceDirectionMairaComboBoxItemsSource >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var dictionary = new Dictionary<RacingWheel.ConstantForceDirection, string>
		{
			{ RacingWheel.ConstantForceDirection.None, localization[ "None" ] },
			{ RacingWheel.ConstantForceDirection.DecreaseForce, localization[ "DecreaseForce" ] },
			{ RacingWheel.ConstantForceDirection.IncreaseForce, localization[ "IncreaseForce" ] }
		};

		UndersteerWheelConstantForceDirection_MairaComboBox.ItemsSource = dictionary.ToList();

		OversteerWheelConstantForceDirection_MairaComboBox.ItemsSource = dictionary.ToList();

		SeatOfPantsWheelConstantForceDirection_MairaComboBox.ItemsSource = dictionary.ToList();

		app.Logger.WriteLine( "[SteeringEffectsPage] <<< SetConstantForceDirectionMairaComboBoxItemsSource" );
	}

	public void UpdateSeatOfPantsAlgorithmOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffectsPage] UpdateSeatOfPantsAlgorithmOptions >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var dictionary = new Dictionary<SteeringEffects.SeatOfPantsAlgorithm, string>
		{
			{ SteeringEffects.SeatOfPantsAlgorithm.YAcceleration, localization[ "LateralAcceleration" ] },
			{ SteeringEffects.SeatOfPantsAlgorithm.YVelocity, localization[ "LateralVelocity" ] },
			{ SteeringEffects.SeatOfPantsAlgorithm.YVelocityOverXVelocity, localization[ "RatioOfVelocities" ] }
		};

		SeatOfPantsAlgorithm_MairaComboBox.ItemsSource = dictionary.ToList();

		app.Logger.WriteLine( "[SteeringEffectsPage] <<< UpdateSeatOfPantsAlgorithmOptions" );
	}

	public void UpdateCalibrationFileWarnings( bool showWarnings )
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			Understeer_CalibrationFileWarning.Visibility = showWarnings ? Visibility.Visible : Visibility.Collapsed;
			Oversteer_CalibrationFileWarning.Visibility = showWarnings ? Visibility.Visible : Visibility.Collapsed;
		} );
	}

	private void UndersteerEnabled_Toggled( object sender, EventArgs e )
	{
		var mairaSwitch = sender as MairaSwitch;

		if ( mairaSwitch is not null )
		{
			Misc.ApplyToTaggedElements( Root, "Understeer", element => element.Visibility = ( ( mairaSwitch.IsOn == true ) ? Visibility.Visible : Visibility.Collapsed ) );
		}
	}

	private void UndersteerWheelVibrationPattern_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		var comboBox = sender as ComboBox;

		if ( comboBox is not null )
		{
			if ( comboBox.SelectedValue is not null )
			{
				var selectedValue = (RacingWheel.VibrationPattern) comboBox.SelectedValue;

				var visibility = selectedValue == RacingWheel.VibrationPattern.None ? Visibility.Collapsed : Visibility.Visible;

				UndersteerWheelVibrationStrength_MairaKnob.Visibility = visibility;
				UndersteerWheelVibrationRow2_Grid.Visibility = visibility;
			}
		}
	}

	private void UndersteerWheelConstantForceEffect_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		var comboBox = sender as ComboBox;

		if ( comboBox is not null )
		{
			if ( comboBox.SelectedValue is not null )
			{
				var selectedValue = (RacingWheel.VibrationPattern) comboBox.SelectedValue;

				var visibility = selectedValue == RacingWheel.VibrationPattern.None ? Visibility.Collapsed : Visibility.Visible;

				UndersteerWheelConstantForceStrength_MairaKnob.Visibility = visibility;
				UndersteerWheelConstantForceCurve_MairaKnob.Visibility = visibility;
			}
		}
	}

	private void OversteerEnabled_Toggled( object sender, EventArgs e )
	{
		var mairaSwitch = sender as MairaSwitch;

		if ( mairaSwitch is not null )
		{
			Misc.ApplyToTaggedElements( Root, "Oversteer", element => element.Visibility = ( ( mairaSwitch.IsOn == true ) ? Visibility.Visible : Visibility.Collapsed ) );
		}
	}

	private void OversteerWheelVibrationPattern_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		var comboBox = sender as ComboBox;

		if ( comboBox is not null )
		{
			if ( comboBox.SelectedValue is not null )
			{
				var selectedValue = (RacingWheel.VibrationPattern) comboBox.SelectedValue;

				var visibility = selectedValue == RacingWheel.VibrationPattern.None ? Visibility.Collapsed : Visibility.Visible;

				OversteerWheelVibrationStrength_MairaKnob.Visibility = visibility;
				OversteerWheelVibrationRow2_Grid.Visibility = visibility;
			}
		}
	}

	private void OversteerWheelConstantForceEffect_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		var comboBox = sender as ComboBox;

		if ( comboBox is not null )
		{
			if ( comboBox.SelectedValue is not null )
			{
				var selectedValue = (RacingWheel.VibrationPattern) comboBox.SelectedValue;

				var visibility = selectedValue == RacingWheel.VibrationPattern.None ? Visibility.Collapsed : Visibility.Visible;

				OversteerWheelConstantForceStrength_MairaKnob.Visibility = visibility;
				OversteerWheelConstantForceCurve_MairaKnob.Visibility = visibility;
			}
		}
	}

	private void SeatOfPantsEnabled_Toggled( object sender, EventArgs e )
	{
		var mairaSwitch = sender as MairaSwitch;

		if ( mairaSwitch is not null )
		{
			Misc.ApplyToTaggedElements( Root, "SeatOfPants", element => element.Visibility = ( ( mairaSwitch.IsOn == true ) ? Visibility.Visible : Visibility.Collapsed ) );
		}
	}

	private void SeatOfPantsWheelVibrationPattern_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		var comboBox = sender as ComboBox;

		if ( comboBox is not null )
		{
			if ( comboBox.SelectedValue is not null )
			{
				var selectedValue = (RacingWheel.VibrationPattern) comboBox.SelectedValue;

				var visibility = selectedValue == RacingWheel.VibrationPattern.None ? Visibility.Collapsed : Visibility.Visible;

				SeatOfPantsWheelVibrationStrength_MairaKnob.Visibility = visibility;
				SeatOfPantsWheelVibrationRow2_Grid.Visibility = visibility;
			}
		}
	}

	private void SeatOfPantsWheelConstantForceEffect_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		var comboBox = sender as ComboBox;

		if ( comboBox is not null )
		{
			if ( comboBox.SelectedValue is not null )
			{
				var selectedValue = (RacingWheel.VibrationPattern) comboBox.SelectedValue;

				var visibility = selectedValue == RacingWheel.VibrationPattern.None ? Visibility.Collapsed : Visibility.Visible;

				SeatOfPantsWheelConstantForceStrength_MairaKnob.Visibility = visibility;
				SeatOfPantsWheelConstantForceCurve_MairaKnob.Visibility = visibility;
			}
		}
	}

	#endregion
}
