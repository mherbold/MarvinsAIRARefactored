
using System.IO;
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Pages;

public partial class SteeringEffectsPage : UserControl
{
	public SteeringEffectsPage()
	{
		InitializeComponent();
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

		app.VirtualJoystick.Steering = -( 90f / 450f );
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

		if ( app.Simulator.CarScreenName != string.Empty )
		{
			foreach ( var filePath in Directory.GetFiles( SteeringEffects.CalibrationDirectory, $"{app.Simulator.CarScreenName} - *.csv" ) )
			{
				var option = Path.GetFileNameWithoutExtension( filePath );

				dictionary.Add( option, option );
			}
		}

		app.Dispatcher.Invoke( () =>
		{
			CalibrationFileName_MairaComboBox.ItemsSource = dictionary;
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
			UndersteerWheelVibrationPattern_MairaComboBox.ItemsSource = dictionary;
			UndersteerWheelVibrationPattern_MairaComboBox.SelectedValue = settings.SteeringEffectsUndersteerWheelVibrationPattern;

			OversteerWheelVibrationPattern_MairaComboBox.ItemsSource = dictionary;
			OversteerWheelVibrationPattern_MairaComboBox.SelectedValue = settings.SteeringEffectsOversteerWheelVibrationPattern;
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
			UndersteerWheelConstantForceDirection_MairaComboBox.ItemsSource = dictionary;
			UndersteerWheelConstantForceDirection_MairaComboBox.SelectedValue = settings.SteeringEffectsUndersteerWheelConstantForceDirection;

			OversteerWheelConstantForceDirection_MairaComboBox.ItemsSource = dictionary;
			OversteerWheelConstantForceDirection_MairaComboBox.SelectedValue = settings.SteeringEffectsOversteerWheelConstantForceDirection;
		} );

		app.Logger.WriteLine( "[SteeringEffectsPage] <<< SetConstantForceDirectionMairaComboBoxItemsSource" );
	}

	#endregion
}
