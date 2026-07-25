
using System.Windows;

using MarvinsAIRARefactored.Controls;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class TyphoonWindPage : UserControl
{
	bool _testingLeft = false;
	bool _testingRight = false;

	public TyphoonWindPage()
	{
		InitializeComponent();

		UpdateSpeedUnitLabel();
	}

	public void UpdateSpeedUnitLabel()
	{
		var app = App.Instance!;
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var unitSuffix = ( app.Simulator?.DisplayUnits ?? 0 ) == 0
			? localization[ "MPHUnits" ]
			: localization[ "KPHUnits" ];

		SpeedLabel_TextBlock.Text = $"{localization[ "Speed" ]} ({unitSuffix.Trim()})";
	}

	#region User Control Events

	private void ConnectToWind_MairaSwitch_Toggled( object sender, EventArgs e )
	{
		var app = App.Instance!;

		if ( ConnectToWind_MairaSwitch.IsOn )
		{
			if ( !app.TyphoonWind.IsConnected )
			{
				app.TyphoonWind.Connect();
			}
		}
		else
		{
			if ( app.TyphoonWind.IsConnected )
			{
				app.TyphoonWind.Disconnect();
			}
		}
	}

	private void LeftTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		_testingLeft = !_testingLeft;

		app.TyphoonWind.TestLeft( _testingLeft );

		( (MairaButton) sender ).Blink = _testingLeft;
	}

	private void RightTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		_testingRight = !_testingRight;

		app.TyphoonWind.TestRight( _testingRight );

		( (MairaButton) sender ).Blink = _testingRight;
	}

	private void RetryDevice_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.TyphoonWind.ScanForDevice();
	}

	private void ResetFanPowerCurveToDefaults_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		settings.TyphoonWindSpeed1    = 0f;
		settings.TyphoonWindSpeed2    = 3.313f;
		settings.TyphoonWindSpeed3    = 9.373f;
		settings.TyphoonWindSpeed4    = 17.208f;
		settings.TyphoonWindSpeed5    = 26.494f;
		settings.TyphoonWindSpeed6    = 37.047f;
		settings.TyphoonWindSpeed7    = 48.672f;
		settings.TyphoonWindSpeed8    = 61.374f;
		settings.TyphoonWindSpeed9    = 74.935f;
		settings.TyphoonWindSpeed10   = 89.408f;

		settings.TyphoonWindFanPower1  = 0f;
		settings.TyphoonWindFanPower2  = 0.125f;
		settings.TyphoonWindFanPower3  = 0.25f;
		settings.TyphoonWindFanPower4  = 0.375f;
		settings.TyphoonWindFanPower5  = 0.5f;
		settings.TyphoonWindFanPower6  = 0.625f;
		settings.TyphoonWindFanPower7  = 0.75f;
		settings.TyphoonWindFanPower8  = 0.8333f;
		settings.TyphoonWindFanPower9  = 0.9167f;
		settings.TyphoonWindFanPower10 = 1f;
	}

	#endregion
}
