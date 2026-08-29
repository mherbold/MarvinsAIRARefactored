
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using ComboBox = System.Windows.Controls.ComboBox;
using UserControl = System.Windows.Controls.UserControl;
using Settings = MarvinsAIRARefactored.DataContext.Settings;

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

	public void UpdateSeatOfPantsAlgorithmOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[SteeringEffectsPage] UpdateSeatOfPantsAlgorithmOptions >>>" );

		SteeringEffects.SeatOfPantsAlgorithm[] orderedAlgorithms =
		[
			SteeringEffects.SeatOfPantsAlgorithm.YAcceleration,
			SteeringEffects.SeatOfPantsAlgorithm.YVelocity,
			SteeringEffects.SeatOfPantsAlgorithm.YVelocityOverXVelocity
		];

		// the labels come from the shared settings helper, so this combo box and the tuning profile manager can
		// never say different things about the same setting
		SeatOfPantsAlgorithm_MairaComboBox.ItemsSource = orderedAlgorithms.Select( algorithm => new KeyValuePair<SteeringEffects.SeatOfPantsAlgorithm, string>( algorithm, Settings.FormatSeatOfPantsAlgorithmString( algorithm ) ) ).ToList();

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
