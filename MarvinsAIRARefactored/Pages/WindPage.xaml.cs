
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class WindPage : UserControl
{
	bool _testingLeft = false;
	bool _testingRight = false;

	public WindPage()
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
			if ( !app.Wind.IsConnected )
			{
				app.Wind.Connect();
			}
		}
		else
		{
			if ( app.Wind.IsConnected )
			{
				app.Wind.Disconnect();
			}
		}
	}

	private void LeftTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		_testingLeft = !_testingLeft;

		app.Wind.TestLeft( _testingLeft );
	}

	private void RightTest_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		_testingRight = !_testingRight;

		app.Wind.TestRight( _testingRight );
	}

	private void RetryDevice_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Wind.RetryDevice();
	}

	private void ResetFanPowerCurveToDefaults_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		settings.WindSpeed1    = 0f;
		settings.WindSpeed2    = 3.313f;
		settings.WindSpeed3    = 9.373f;
		settings.WindSpeed4    = 17.208f;
		settings.WindSpeed5    = 26.494f;
		settings.WindSpeed6    = 37.047f;
		settings.WindSpeed7    = 48.672f;
		settings.WindSpeed8    = 61.374f;
		settings.WindSpeed9    = 74.935f;
		settings.WindSpeed10   = 89.408f;

		settings.WindFanPower1  = 0f;
		settings.WindFanPower2  = 0.125f;
		settings.WindFanPower3  = 0.25f;
		settings.WindFanPower4  = 0.375f;
		settings.WindFanPower5  = 0.5f;
		settings.WindFanPower6  = 0.625f;
		settings.WindFanPower7  = 0.75f;
		settings.WindFanPower8  = 0.8333f;
		settings.WindFanPower9  = 0.9167f;
		settings.WindFanPower10 = 1f;
	}

	#endregion
}
