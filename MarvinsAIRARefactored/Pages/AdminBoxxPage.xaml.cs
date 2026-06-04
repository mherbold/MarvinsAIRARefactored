
using System.Windows;
using System.Windows.Threading;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class AdminBoxxPage : UserControl
{
	private readonly DispatcherTimer _volumePreviewTimer = new() { Interval = TimeSpan.FromMilliseconds( 100 ) };
	private float _pendingVolumePreview = 0f;

	public AdminBoxxPage()
	{
		InitializeComponent();

		_volumePreviewTimer.Tick += VolumePreviewTimer_Tick;

#if ADMINBOXX
		ConnectOnStartup_MairaSwitch.Visibility = Visibility.Collapsed;
#endif
	}

	#region User Control Events

	private void ConnectToAdminBoxx_MairaSwitch_Toggled( object sender, EventArgs e )
	{
		var app = App.Instance!;

		if ( ConnectToAdminBoxx_MairaSwitch.IsOn )
		{
			if ( !app.AdminBoxx.IsConnected )
			{
				app.AdminBoxx.Connect();
			}
		}
		else
		{
			if ( app.AdminBoxx.IsConnected )
			{
				app.AdminBoxx.Disconnect();
			}
		}
	}

	private void Brightness_ValueChanged( float newValue )
	{
		var app = App.Instance!;

		app.AdminBoxx.ResendAllLEDs();
	}

	private void Volume_ValueChanged( float newValue )
	{
		_pendingVolumePreview = newValue;

		_volumePreviewTimer.Stop();
		_volumePreviewTimer.Start();
	}

	private void VolumePreviewTimer_Tick( object? sender, EventArgs e )
	{
		var app = App.Instance!;

		_volumePreviewTimer.Stop();

		app.AudioManager.Play( "adminboxx_tone", _pendingVolumePreview );
	}

	private void Test_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.AdminBoxx.StartTestCycle();
	}

	private void RetryDevice_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.AdminBoxx.RetryDevice();
	}

	#endregion
}
