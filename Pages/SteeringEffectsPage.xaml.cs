
using System.IO;
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

	private void ResetGripOMeter_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.GripOMeterWindow.ResetWindow();
	}

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

	public void UpdateCalibrationFileNameOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffectsPage] UpdateCalibrationFileNameOptions >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<string, string>()
		{
			{ string.Empty, localization[ "CalibrationFileNotSelected" ] }
		};

		var autoSelectedValue = string.Empty;

		if ( app.Simulator.CarScreenName != string.Empty )
		{
			foreach ( var filePath in Directory.GetFiles( SteeringEffects.CalibrationDirectory, $"{app.Simulator.CarScreenName} - *.csv" ) )
			{
				var option = Path.GetFileNameWithoutExtension( filePath );

				dictionary.Add( option, option );

				if ( settings.SteeringEffectsCalibrationFileName == string.Empty )
				{
					autoSelectedValue = option;
				}
			}
		}

		app.Dispatcher.Invoke( () =>
		{
			CalibrationFileName_MairaComboBox.ItemsSource = dictionary.ToList();

			if ( autoSelectedValue != string.Empty )
			{
				settings.SteeringEffectsCalibrationFileName = autoSelectedValue;
			}

			CalibrationFileName_MairaComboBox.SelectedValue = settings.SteeringEffectsCalibrationFileName;
			CalibrationFileName_MairaComboBox.OffValue = string.Empty;
		} );

		app.Logger.WriteLine( "[SteeringEffectsPage] <<< UpdateCalibrationFileNameOptions" );
	}

	public void UpdateVibrationPatternOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffectsPage] SetVibrationPatternMairaComboBoxItemsSource >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<RacingWheel.VibrationPattern, string>
		{
			{ RacingWheel.VibrationPattern.None, localization[ "None" ] },
			{ RacingWheel.VibrationPattern.SineWave, localization[ "SineWave" ] },
			{ RacingWheel.VibrationPattern.SquareWave, localization[ "SquareWave" ] },
			{ RacingWheel.VibrationPattern.TriangleWave, localization[ "TriangleWave" ] },
			{ RacingWheel.VibrationPattern.SawtoothWaveIn, localization[ "SawtoothWaveIn" ] },
			{ RacingWheel.VibrationPattern.SawtoothWaveOut, localization[ "SawtoothWaveOut" ] }
		};

		app.Dispatcher.Invoke( () =>
		{
			UndersteerWheelVibrationPattern_MairaComboBox.ItemsSource = dictionary.ToList();
			UndersteerWheelVibrationPattern_MairaComboBox.SelectedValue = settings.SteeringEffectsUndersteerWheelVibrationPattern;

			OversteerWheelVibrationPattern_MairaComboBox.ItemsSource = dictionary.ToList();
			OversteerWheelVibrationPattern_MairaComboBox.SelectedValue = settings.SteeringEffectsOversteerWheelVibrationPattern;

			SeatOfPantsWheelVibrationPattern_MairaComboBox.ItemsSource = dictionary.ToList();
			SeatOfPantsWheelVibrationPattern_MairaComboBox.SelectedValue = settings.SteeringEffectsSeatOfPantsWheelVibrationPattern;
		} );

		app.Logger.WriteLine( "[SteeringEffectsPage] <<< SetVibrationPatternMairaComboBoxItemsSource" );
	}

	public void UpdateConstantForceDirectionOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffectsPage] SetConstantForceDirectionMairaComboBoxItemsSource >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<RacingWheel.ConstantForceDirection, string>
		{
			{ RacingWheel.ConstantForceDirection.None, localization[ "None" ] },
			{ RacingWheel.ConstantForceDirection.DecreaseForce, localization[ "DecreaseForce" ] },
			{ RacingWheel.ConstantForceDirection.IncreaseForce, localization[ "IncreaseForce" ] }
		};

		app.Dispatcher.Invoke( () =>
		{
			UndersteerWheelConstantForceDirection_MairaComboBox.ItemsSource = dictionary.ToList();
			UndersteerWheelConstantForceDirection_MairaComboBox.SelectedValue = settings.SteeringEffectsUndersteerWheelConstantForceDirection;

			OversteerWheelConstantForceDirection_MairaComboBox.ItemsSource = dictionary.ToList();
			OversteerWheelConstantForceDirection_MairaComboBox.SelectedValue = settings.SteeringEffectsOversteerWheelConstantForceDirection;

			SeatOfPantsWheelConstantForceDirection_MairaComboBox.ItemsSource = dictionary.ToList();
			SeatOfPantsWheelConstantForceDirection_MairaComboBox.SelectedValue = settings.SteeringEffectsSeatOfPantsWheelConstantForceDirection;
		} );

		app.Logger.WriteLine( "[SteeringEffectsPage] <<< SetConstantForceDirectionMairaComboBoxItemsSource" );
	}

	public void UpdateSeatOfPantsAlgorithmOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffectsPage] UpdateSeatOfPantsAlgorithmOptions >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<SteeringEffects.SeatOfPantsAlgorithm, string>
		{
			{ SteeringEffects.SeatOfPantsAlgorithm.YAcceleration, localization[ "LateralAcceleration" ] },
			{ SteeringEffects.SeatOfPantsAlgorithm.YVelocity, localization[ "LateralVelocity" ] },
			{ SteeringEffects.SeatOfPantsAlgorithm.YVelocityOverXVelocity, localization[ "RatioOfVelocities" ] }
		};

		app.Dispatcher.Invoke( () =>
		{
			SeatOfPantsAlgorithm_MairaComboBox.ItemsSource = dictionary.ToList();
			SeatOfPantsAlgorithm_MairaComboBox.SelectedValue = settings.SteeringEffectsSeatOfPantsAlgorithm;
		} );

		app.Logger.WriteLine( "[SteeringEffectsPage] <<< UpdateSeatOfPantsAlgorithmOptions" );
	}

	public void CalibrationFileNameChanged( bool isSelected )
	{
		var app = App.Instance!;

		app.Dispatcher.InvokeAsync( () =>
		{
			Understeer_CalibrationFileWarning.Visibility = isSelected ? Visibility.Collapsed : Visibility.Visible;
			Oversteer_CalibrationFileWarning.Visibility = isSelected ? Visibility.Collapsed : Visibility.Visible;
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
