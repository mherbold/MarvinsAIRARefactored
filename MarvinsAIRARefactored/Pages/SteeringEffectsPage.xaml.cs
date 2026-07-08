
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

			CalibrationFileName_MairaComboBox.OffValue = string.Empty;
		} );

		app.Logger.WriteLine( "[SteeringEffectsPage] <<< UpdateCalibrationFileNameOptions" );
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

	private void OversteerEnabled_Toggled( object sender, EventArgs e )
	{
		var mairaSwitch = sender as MairaSwitch;

		if ( mairaSwitch is not null )
		{
			Misc.ApplyToTaggedElements( Root, "Oversteer", element => element.Visibility = ( ( mairaSwitch.IsOn == true ) ? Visibility.Visible : Visibility.Collapsed ) );
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

	#endregion
}
