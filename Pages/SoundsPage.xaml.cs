
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.Pages;

public partial class SoundsPage : UserControl
{
	public SoundsPage()
	{
		InitializeComponent();
	}

	public void UpdateOutputDeviceOptions()
	{
		var app = App.Instance!;

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<string, string>
		{
			{ AudioManager.DefaultDeviceName, localization[ "DefaultWindowsSoundDevice" ] }
		};

		app.AudioManager.OutputDevices.ToList().ForEach( deviceName => dictionary[ deviceName ] = deviceName );

		if ( !dictionary.ContainsKey( settings.SoundsOutputDevice ) )
		{
			dictionary.Add( settings.SoundsOutputDevice, $"{localization[ "DeviceNotFound" ]} [{settings.SoundsOutputDevice}]" );
		}

		OutputDevice_MairaComboBox.ItemsSource = dictionary.ToList();
	}

	#region User Control Events

	private void Click_Test_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Sounds.Test( Sounds.SoundEffectType.Click );
	}

	private void ABSEngaged_Test_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Sounds.Test( Sounds.SoundEffectType.ABSEngaged );
	}

	private void WheelLock_Test_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Sounds.Test( Sounds.SoundEffectType.WheelLock );
	}

	private void WheelSpin_Test_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Sounds.Test( Sounds.SoundEffectType.WheelSpin );
	}

	private void Understeer_Test_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Sounds.Test( Sounds.SoundEffectType.Understeer );
	}

	private void Oversteer_Test_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Sounds.Test( Sounds.SoundEffectType.Oversteer );
	}

	private void SeatOfPants_Test_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Sounds.Test( Sounds.SoundEffectType.SeatOfPants );
	}

	private void BrakeThrottleWarning_Test_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Sounds.Test( Sounds.SoundEffectType.BrakeThrottleWarning );
	}

	private void FfbClipping_Test_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Sounds.Test( Sounds.SoundEffectType.FfbClipping );
	}

	#endregion
}
