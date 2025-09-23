
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Controls;

namespace MarvinsAIRARefactored.Pages;

public partial class PedalsPage : UserControl
{
	public PedalsPage()
	{
		InitializeComponent();
	}

	#region User Control Events

	private void ClutchTest1_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 0, 0 );
	}

	private void ClutchTest2_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 0, 1 );
	}

	private void ClutchTest3_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 0, 2 );
	}

	private void BrakeTest1_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 1, 0 );
	}

	private void BrakeTest2_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 1, 1 );
	}

	private void BrakeTest3_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 1, 2 );
	}

	private void ThrottleTest1_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 2, 0 );
	}

	private void ThrottleTest2_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 2, 1 );
	}

	private void ThrottleTest3_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Pedals.StartTest( 2, 2 );
	}

	#endregion

	#region Logic

	public void UpdateEffectOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[PedalsPage] UpdateEffectOptions >>>" );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<Pedals.Effect, string>
		{
			{ Pedals.Effect.None, localization[ "None" ] },
			{ Pedals.Effect.GearChange, localization[ "GearChange" ] },
			{ Pedals.Effect.ABSEngaged, localization[ "ABSEngaged" ] },
			{ Pedals.Effect.RPM, localization[ "RPM" ] },
			{ Pedals.Effect.UndersteerEffect, localization[ "UndersteerEffect" ] },
			{ Pedals.Effect.OversteerEffect, localization[ "OversteerEffect" ] },
			{ Pedals.Effect.WheelLock, localization[ "WheelLock" ] },
			{ Pedals.Effect.WheelSpin, localization[ "WheelSpin" ] },
			{ Pedals.Effect.ClutchSlip, localization[ "ClutchSlip" ] },
		};

		app.Dispatcher.Invoke( () =>
		{
			ClutchEffect1_MairaComboBox.ItemsSource = dictionary;
			ClutchEffect2_MairaComboBox.ItemsSource = dictionary;
			ClutchEffect3_MairaComboBox.ItemsSource = dictionary;

			ClutchEffect1_MairaComboBox.SelectedValue = settings.PedalsClutchEffect1;
			ClutchEffect2_MairaComboBox.SelectedValue = settings.PedalsClutchEffect2;
			ClutchEffect3_MairaComboBox.SelectedValue = settings.PedalsClutchEffect3;

			BrakeEffect1_MairaComboBox.ItemsSource = dictionary;
			BrakeEffect2_MairaComboBox.ItemsSource = dictionary;
			BrakeEffect3_MairaComboBox.ItemsSource = dictionary;

			BrakeEffect1_MairaComboBox.SelectedValue = settings.PedalsBrakeEffect1;
			BrakeEffect2_MairaComboBox.SelectedValue = settings.PedalsBrakeEffect2;
			BrakeEffect3_MairaComboBox.SelectedValue = settings.PedalsBrakeEffect3;

			ThrottleEffect1_MairaComboBox.ItemsSource = dictionary;
			ThrottleEffect2_MairaComboBox.ItemsSource = dictionary;
			ThrottleEffect3_MairaComboBox.ItemsSource = dictionary;

			ThrottleEffect1_MairaComboBox.SelectedValue = settings.PedalsThrottleEffect1;
			ThrottleEffect2_MairaComboBox.SelectedValue = settings.PedalsThrottleEffect2;
			ThrottleEffect3_MairaComboBox.SelectedValue = settings.PedalsThrottleEffect3;
		} );

		app.Logger.WriteLine( "[PedalsPage] <<< UpdateEffectOptions" );
	}

	#endregion
}
