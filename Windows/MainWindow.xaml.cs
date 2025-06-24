
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using ScrollEventArgs = System.Windows.Controls.Primitives.ScrollEventArgs;
using TabControl = System.Windows.Controls.TabControl;

using Simagic;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.PInvoke;

namespace MarvinsAIRARefactored.Windows;

public partial class MainWindow : Window
{
	public nint WindowHandle { get; private set; } = 0;
	public bool GraphTabItemIsVisible { get; private set; } = false;
	public bool DebugTabItemIsVisible { get; private set; } = false;

	private string? _installerFilePath = null;

	private bool _initialized = false;

	private NotifyIcon? _notifyIcon = null;

	public MainWindow()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[MainWindow] Constructor >>>" );

		InitializeComponent();

		var version = Misc.GetVersion();

		app.Logger.WriteLine( $"[MainWindow] Version is {version}" );

		Components.Localization.SetLanguageComboBoxItemsSource( App_Language_ComboBox );

		AdminBoxx_TabItem.Visibility = Visibility.Collapsed;
		Debug_TabItem.Visibility = Visibility.Collapsed;

		app.Logger.WriteLine( "[MainWindow] <<< Constructor" );
	}

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[MainWindow] Initialize >>>" );

		var value = UXTheme.ShouldSystemUseDarkMode() ? 1 : 0;

		DWMAPI.DwmSetWindowAttribute( WindowHandle, (uint) DWMAPI.cbAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, (uint) System.Runtime.InteropServices.Marshal.SizeOf( value ) );

		UpdateRacingWheelPowerButton();
		UpdateRacingWheelForceFeedbackButtons();

		RefreshWindow();

		Misc.ForcePropertySetters( MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings );

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		if ( settings.AppRememberWindowPositionAndSize )
		{
			var rectangle = settings.AppWindowPositionAndSize;

			if ( Misc.IsWindowBoundsVisible( rectangle ) )
			{
				Left = rectangle.Location.X;
				Top = rectangle.Location.Y;
				Width = rectangle.Size.Width;
				Height = rectangle.Size.Height;

				WindowStartupLocation = WindowStartupLocation.Manual;
			}
		}

		_initialized = true;

		app.Logger.WriteLine( "[MainWindow] <<< Initialize" );
	}

	public void RefreshWindow()
	{
		Dispatcher.BeginInvoke( () =>
		{
			var app = App.Instance!;

			Title = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "AppTitle" ] + " " + Misc.GetVersion();

			app.DirectInput.SetMairaComboBoxItemsSource( RacingWheel_SteeringDevice_ComboBox );

			app.LFE.SetMairaComboBoxItemsSource( RacingWheel_LFERecordingDevice_ComboBox );

			Graph.SetMairaComboBoxItemsSource( Graph_Statistics_ComboBox );

			RacingWheel.SetMairaComboBoxItemsSource( RacingWheel_Algorithm_ComboBox );

			Pedals.SetMairaComboBoxItemsSource( Pedals_Clutch_Effect_ComboBox_1 );
			Pedals.SetMairaComboBoxItemsSource( Pedals_Clutch_Effect_ComboBox_2 );
			Pedals.SetMairaComboBoxItemsSource( Pedals_Clutch_Effect_ComboBox_3 );
			Pedals.SetMairaComboBoxItemsSource( Pedals_Brake_Effect_ComboBox_1 );
			Pedals.SetMairaComboBoxItemsSource( Pedals_Brake_Effect_ComboBox_2 );
			Pedals.SetMairaComboBoxItemsSource( Pedals_Brake_Effect_ComboBox_3 );
			Pedals.SetMairaComboBoxItemsSource( Pedals_Throttle_Effect_ComboBox_1 );
			Pedals.SetMairaComboBoxItemsSource( Pedals_Throttle_Effect_ComboBox_2 );
			Pedals.SetMairaComboBoxItemsSource( Pedals_Throttle_Effect_ComboBox_3 );

			UpdateStatus();
			UpdatePedalsDevice();
			UpdateNotifyIcon();
		} );
	}

	public void UpdateStatus()
	{
		Dispatcher.BeginInvoke( () =>
		{
			var app = App.Instance!;

			var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

			var backgroundColor = Brushes.Black;

			var panel1Message = string.Empty;
			var panel2Message = string.Empty;
			var panel3Message = string.Empty;
			var panel4Message = string.Empty;

			if ( app.CloudService.CheckingForUpdate )
			{
				backgroundColor = Brushes.DarkOrange;

				panel1Message = localization[ "CheckingForUpdate" ];
			}
			else if ( app.CloudService.DownloadingUpdate )
			{
				backgroundColor = Brushes.DarkOrange;

				panel1Message = localization[ "DownloadingUpdate" ];
			}
			else if ( app.Simulator.IsConnected )
			{
				backgroundColor = new SolidColorBrush( System.Windows.Media.Color.FromScRgb( 1f, 0.1f, 0.1f, 0.1f ) );

				panel1Message = app.Simulator.CarScreenName == string.Empty ? localization[ "Default" ] : app.Simulator.CarScreenName;
				panel2Message = app.Simulator.TrackDisplayName == string.Empty ? localization[ "Default" ] : app.Simulator.TrackDisplayName;
				panel3Message = app.Simulator.TrackConfigName == string.Empty ? localization[ "Default" ] : app.Simulator.TrackConfigName;
				panel4Message = localization[ app.Simulator.WeatherDeclaredWet ? "Wet" : "Dry" ];
			}
			else
			{
				backgroundColor = new SolidColorBrush( System.Windows.Media.Color.FromScRgb( 1f, 0.3f, 0f, 0f ) );

				panel1Message = localization[ "SimulatorNotRunning" ];
			}

			Status_Border.Background = backgroundColor;

			if ( panel1Message == string.Empty )
			{
				Status_Car_Label.Visibility = Visibility.Collapsed;
			}
			else
			{
				Status_Car_Label.Content = panel1Message;
				Status_Car_Label.Visibility = Visibility.Visible;
			}

			if ( panel2Message == string.Empty )
			{
				Status_Track_Label.Visibility = Visibility.Collapsed;
			}
			else
			{
				Status_Track_Label.Content = panel2Message;
				Status_Track_Label.Visibility = Visibility.Visible;
			}

			if ( panel3Message == string.Empty )
			{
				Status_TrackConfiguration_Label.Visibility = Visibility.Collapsed;
			}
			else
			{
				Status_TrackConfiguration_Label.Content = panel3Message;
				Status_TrackConfiguration_Label.Visibility = Visibility.Visible;
			}

			if ( panel4Message == string.Empty )
			{
				Status_WetDry_Label.Visibility = Visibility.Collapsed;
			}
			else
			{
				Status_WetDry_Label.Content = panel4Message;
				Status_WetDry_Label.Visibility = Visibility.Visible;
			}
		} );
	}

	public void UpdateRacingWheelPowerButton()
	{
		var app = App.Instance!;

		Dispatcher.BeginInvoke( () =>
		{
			RacingWheel_Power_MairaMappableButton.Blink = false;

			ImageSource? imageSource;

			if ( !MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelEnableForceFeedback )
			{
				imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/power_led_red.png" ) as ImageSource;

				RacingWheel_Power_MairaMappableButton.Blink = true;
			}
			else if ( app.RacingWheel.SuspendForceFeedback || !app.DirectInput.ForceFeedbackInitialized )
			{
				imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/power_led_yellow.png" ) as ImageSource;

				if ( app.Simulator.IsConnected )
				{
					RacingWheel_Power_MairaMappableButton.Blink = true;
				}
			}
			else
			{
				imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/power_led_green.png" ) as ImageSource;
			}

			if ( imageSource != null )
			{
				RacingWheel_Power_MairaMappableButton.ButtonIcon = imageSource;
			}
		} );
	}

	public void UpdateRacingWheelForceFeedbackButtons()
	{
		var app = App.Instance!;

		Dispatcher.BeginInvoke( () =>
		{
			var disableButtons = !app.DirectInput.ForceFeedbackInitialized;

			RacingWheel_Test_MairaMappableButton.Disabled = disableButtons;
			RacingWheel_Reset_MairaMappableButton.Disabled = disableButtons;
			RacingWheel_Auto_MairaMappableButton.Disabled = disableButtons;
			RacingWheel_Clear_MairaMappableButton.Disabled = disableButtons;
		} );
	}

	public void UpdateRacingWheelAlgorithmControls()
	{
		Dispatcher.BeginInvoke( () =>
		{
			var racingWheelDetailBoostKnobControlVisibility = Visibility.Hidden;
			var racingWheelDeltaLimitKnobControlVisibility = Visibility.Hidden;
			var racingWheelDetailBoostBiasKnobControlVisibility = Visibility.Hidden;
			var racingWheelDeltaLimiterBiasKnobControlVisibility = Visibility.Hidden;
			var racingWheelSlewCompressionThresholdVisibility = Visibility.Hidden;
			var racingWheelSlewCompressionRateVisibility = Visibility.Hidden;
			var racingWheelTotalCompressionThresholdVisibility = Visibility.Hidden;
			var racingWheelTotalCompressionRateVisibility = Visibility.Hidden;

			var racingWheelAlgorithmRowTwoGridVisibility = Visibility.Collapsed;
			var racingWheelCurbProtectionGroupBoxVisibility = Visibility.Collapsed;

			switch ( MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelAlgorithm )
			{
				case RacingWheel.Algorithm.DetailBooster:
				case RacingWheel.Algorithm.DetailBoosterOn60Hz:
					racingWheelDetailBoostKnobControlVisibility = Visibility.Visible;
					racingWheelDetailBoostBiasKnobControlVisibility = Visibility.Visible;
					racingWheelCurbProtectionGroupBoxVisibility = Visibility.Visible;
					break;

				case RacingWheel.Algorithm.DeltaLimiter:
				case RacingWheel.Algorithm.DeltaLimiterOn60Hz:
					racingWheelDeltaLimitKnobControlVisibility = Visibility.Visible;
					racingWheelDeltaLimiterBiasKnobControlVisibility = Visibility.Visible;
					racingWheelCurbProtectionGroupBoxVisibility = Visibility.Visible;
					break;

				case RacingWheel.Algorithm.ZeAlanLeTwist:
					racingWheelAlgorithmRowTwoGridVisibility = Visibility.Visible;
					racingWheelSlewCompressionThresholdVisibility = Visibility.Visible;
					racingWheelSlewCompressionRateVisibility = Visibility.Visible;
					racingWheelTotalCompressionThresholdVisibility = Visibility.Visible;
					racingWheelTotalCompressionRateVisibility = Visibility.Visible;
					racingWheelCurbProtectionGroupBoxVisibility = Visibility.Visible;
					break;
			}

			RacingWheel_DetailBoost_KnobControl.Visibility = racingWheelDetailBoostKnobControlVisibility;
			RacingWheel_DeltaLimit_KnobControl.Visibility = racingWheelDeltaLimitKnobControlVisibility;
			RacingWheel_DetailBoostBias_KnobControl.Visibility = racingWheelDetailBoostBiasKnobControlVisibility;
			RacingWheel_DeltaLimiterBias_KnobControl.Visibility = racingWheelDeltaLimiterBiasKnobControlVisibility;
			RacingWheel_SlewCompressionThreshold.Visibility = racingWheelSlewCompressionThresholdVisibility;
			RacingWheel_SlewCompressionRate.Visibility = racingWheelSlewCompressionRateVisibility;
			RacingWheel_TotalCompressionThreshold.Visibility = racingWheelTotalCompressionThresholdVisibility;
			RacingWheel_TotalCompressionRate.Visibility = racingWheelTotalCompressionRateVisibility;

			RacingWheel_AlgorithmRowTwo_Grid.Visibility = racingWheelAlgorithmRowTwoGridVisibility;
			RacingWheel_CurbProtection_GroupBox.Visibility = racingWheelCurbProtectionGroupBoxVisibility;
		} );
	}

	public void UpdatePedalsDevice()
	{
		var app = App.Instance!;

		Dispatcher.BeginInvoke( () =>
		{
			var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

			switch ( app.Pedals.PedalsDevice )
			{
				case HPR.PedalsDevice.None:
					app.MainWindow.Pedals_Device_Label.Content = localization[ "PedalsNone" ];
					break;

				case HPR.PedalsDevice.P1000:
					app.MainWindow.Pedals_Device_Label.Content = localization[ "PedalsP1000" ];
					break;

				case HPR.PedalsDevice.P2000:
					app.MainWindow.Pedals_Device_Label.Content = localization[ "PedalsP2000" ];
					break;
			}
		} );
	}

	public void UpdateNotifyIcon()
	{
		var app = App.Instance!;

		Dispatcher.BeginInvoke( () =>
		{
			var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

			if ( _notifyIcon != null )
			{
				_notifyIcon.Visible = false;

				_notifyIcon.Dispose();
			}

			if ( MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.AppMinimizeToSystemTray )
			{
				var resourceStream = Application.GetResourceStream( new Uri( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/white_icon.ico" ) ).Stream;

				_notifyIcon = new()
				{
					Icon = new Icon( resourceStream ),
					Visible = true,
					Text = localization[ "AppTitle" ],
					ContextMenuStrip = new ContextMenuStrip()
				};

				_notifyIcon.ContextMenuStrip.Items.Add( localization[ "ShowWindow" ], null, ( s, e ) => MakeWindowVisible() );
				_notifyIcon.ContextMenuStrip.Items.Add( localization[ "ExitApp" ], null, ( s, e ) => ExitApp() );

				_notifyIcon.MouseClick += ( s, e ) =>
				{
					if ( e.Button == MouseButtons.Left )
					{
						MakeWindowVisible();
					}
					else if ( e.Button == MouseButtons.Right )
					{
						_notifyIcon.ContextMenuStrip?.Show( System.Windows.Forms.Cursor.Position );
					}
				};
			}
		} );
	}

	public void MakeWindowVisible()
	{
		Show();

		WindowState = WindowState.Normal;

		Activate();

		if ( !MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.AppTopmostWindowEnabled )
		{
			Topmost = true;
			Topmost = false;
		}

		Focus();
	}

	private void ExitApp()
	{
		if ( _notifyIcon != null )
		{
			_notifyIcon.Visible = false;

			_notifyIcon.Dispose();

			Close();
		}
	}

	private void UpdateTabItemIsVisible()
	{
		if ( WindowState == WindowState.Minimized )
		{
			GraphTabItemIsVisible = false;
			DebugTabItemIsVisible = false;
		}
		else if ( TabControl.SelectedItem is TabItem selectedTab )
		{
			GraphTabItemIsVisible = ( selectedTab == Graph_TabItem );
			DebugTabItemIsVisible = ( selectedTab == Debug_TabItem );
		}
	}

	public void CloseAndLaunchInstaller( string installerFilePath )
	{
		_installerFilePath = installerFilePath;

		Close();
	}

	private void Window_ContentRendered( object sender, EventArgs e )
	{
		if ( WindowHandle == 0 )
		{
			WindowHandle = new WindowInteropHelper( this ).Handle;
		}
	}

	private void Window_LocationChanged( object sender, EventArgs e )
	{
		if ( _initialized )
		{
			if ( IsVisible && ( WindowState == WindowState.Normal ) )
			{
				var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

				var rectangle = settings.AppWindowPositionAndSize;

				rectangle.Location = new System.Drawing.Point( (int) RestoreBounds.Left, (int) RestoreBounds.Top );

				settings.AppWindowPositionAndSize = rectangle;
			}
		}
	}

	private void Window_SizeChanged( object sender, SizeChangedEventArgs e )
	{
		if ( _initialized )
		{
			if ( IsVisible && ( WindowState == WindowState.Normal ) )
			{
				var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

				var rectangle = settings.AppWindowPositionAndSize;

				rectangle.Size = new System.Drawing.Size( (int) RestoreBounds.Width, (int) RestoreBounds.Height );

				settings.AppWindowPositionAndSize = rectangle;
			}
		}
	}

	private void Window_StateChanged( object sender, EventArgs e )
	{
		if ( MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.AppMinimizeToSystemTray )
		{
			if ( WindowState == WindowState.Minimized )
			{
				Hide();
			}
		}

		UpdateTabItemIsVisible();
	}

	private void Window_Closing( object sender, CancelEventArgs e )
	{
		if ( _notifyIcon != null )
		{
			_notifyIcon.Visible = false;

			_notifyIcon.Dispose();
		}
	}

	private void Window_Closed( object sender, EventArgs e )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[MainWindow] Window closed" );

		if ( _installerFilePath != null )
		{
			var processStartInfo = new ProcessStartInfo( _installerFilePath )
			{
				UseShellExecute = true
			};

			Process.Start( processStartInfo );
		}
	}

	private void TabControl_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( e.Source is TabControl )
		{
			UpdateTabItemIsVisible();
		}
	}

	private void RacingWheel_Power_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelEnableForceFeedback = !MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelEnableForceFeedback;
	}

	private void RacingWheel_Test_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.RacingWheel.PlayTestSignal = true;
	}

	private void RacingWheel_Reset_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.RacingWheel.ResetForceFeedback = true;
	}

	private void RacingWheel_Auto_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.RacingWheel.AutoSetMaxForce = true;
	}

	private void RacingWheel_Clear_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.RacingWheel.ClearPeakTorque = true;
	}

	private void Simulator_HeaderData_HeaderDataViewer_MouseWheel( object sender, MouseWheelEventArgs e )
	{
		var delta = e.Delta / 30.0f;

		if ( delta > 0 )
		{
			delta = MathF.Max( 1, delta );
		}
		else
		{
			delta = MathF.Min( -1, delta );
		}

		Simulator_HeaderData_ScrollBar.Value -= delta;

		Simulator_HeaderData_HeaderDataViewer.ScrollIndex = (int) Simulator_HeaderData_ScrollBar.Value;
	}

	private void Simulator_HeaderData_ScrollBar_Scroll( object sender, ScrollEventArgs e )
	{
		Simulator_HeaderData_HeaderDataViewer.ScrollIndex = (int) e.NewValue;
	}

	private void Simulator_SessionInfo_SessionInfoViewer_MouseWheel( object sender, MouseWheelEventArgs e )
	{
		var delta = e.Delta / 30.0f;

		if ( delta > 0 )
		{
			delta = MathF.Max( 1, delta );
		}
		else
		{
			delta = MathF.Min( -1, delta );
		}

		Simulator_SessionInfo_ScrollBar.Value -= delta;

		Simulator_SessionInfo_SessionInfoViewer.ScrollIndex = (int) Simulator_SessionInfo_ScrollBar.Value;
	}

	private void Simulator_SessionInfo_ScrollBar_Scroll( object sender, ScrollEventArgs e )
	{
		Simulator_SessionInfo_SessionInfoViewer.ScrollIndex = (int) e.NewValue;
	}

	private void Simulator_TelemetryData_TelemetryDataViewer_MouseWheel( object sender, MouseWheelEventArgs e )
	{
		var delta = e.Delta / 30.0f;

		if ( delta > 0 )
		{
			delta = MathF.Max( 1, delta );
		}
		else
		{
			delta = MathF.Min( -1, delta );
		}

		Simulator_TelemetryData_ScrollBar.Value -= delta;

		Simulator_TelemetryData_TelemetryDataViewer.ScrollIndex = (int) Simulator_TelemetryData_ScrollBar.Value;
	}

	private void Simulator_TelemetryData_ScrollBar_Scroll( object sender, ScrollEventArgs e )
	{
		Simulator_TelemetryData_TelemetryDataViewer.ScrollIndex = (int) e.NewValue;
	}

	private void AdminBoxx_ConnectToAdminBoxx_MairaSwitch_Toggled( object sender, EventArgs e )
	{
		var app = App.Instance!;

		if ( AdminBoxx_ConnectToAdminBoxx_MairaSwitch.IsOn )
		{
			if ( !app.AdminBoxx.IsConnected )
			{
				app.AdminBoxx.Connect();
			}
		}
		else
		{
			app.AdminBoxx.Disconnect();
		}
	}

	private void AdminBoxx_Brightness_ValueChanged( float newValue )
	{
		var app = App.Instance!;

		app.AdminBoxx.ResendAllLEDs();
	}

	private void AdminBoxx_BlackFlag_ValueChanged( float newValue )
	{
		var app = App.Instance!;

		app.AdminBoxx.WaveBlackFlag();
	}

	private void AdminBoxx_Volume_ValueChanged( float newValue )
	{
		var app = App.Instance!;

		app.AudioManager.Play( "volume", newValue );
	}

	private void AdminBoxx_Test_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.AdminBoxx.StartTestCycle();
	}

	private async void App_CheckNow_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		await app.CloudService.CheckForUpdates( true );
	}

	private void Hyperlink_RequestNavigate( object sender, System.Windows.Navigation.RequestNavigateEventArgs e )
	{
		Process.Start( new ProcessStartInfo( e.Uri.AbsoluteUri ) { UseShellExecute = true } );

		e.Handled = true;
	}

	private void Debug_AlanLeReset_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Debug.ResetFFBSamples();
	}

	private void Debug_AlanLeDump_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		app.Debug.DumpFFBSamplesToCSVFile();
	}

	public void Tick( App app )
	{
		// header data

		Simulator_HeaderData_HeaderDataViewer.InvalidateVisual();

		Simulator_HeaderData_ScrollBar.Maximum = Simulator_HeaderData_HeaderDataViewer.NumTotalLines - Simulator_HeaderData_HeaderDataViewer.NumVisibleLines;
		Simulator_HeaderData_ScrollBar.ViewportSize = Simulator_HeaderData_HeaderDataViewer.NumVisibleLines;

		if ( Simulator_HeaderData_HeaderDataViewer.NumVisibleLines >= Simulator_HeaderData_HeaderDataViewer.NumTotalLines )
		{
			Simulator_HeaderData_HeaderDataViewer.ScrollIndex = 0;
			Simulator_HeaderData_ScrollBar.Visibility = Visibility.Collapsed;
		}
		else
		{
			Simulator_HeaderData_ScrollBar.Visibility = Visibility.Visible;
		}

		// session information

		Simulator_SessionInfo_SessionInfoViewer.InvalidateVisual();

		Simulator_SessionInfo_ScrollBar.Maximum = Simulator_SessionInfo_SessionInfoViewer.NumTotalLines - Simulator_SessionInfo_SessionInfoViewer.NumVisibleLines;
		Simulator_SessionInfo_ScrollBar.ViewportSize = Simulator_SessionInfo_SessionInfoViewer.NumVisibleLines;

		if ( Simulator_SessionInfo_SessionInfoViewer.NumVisibleLines >= Simulator_SessionInfo_SessionInfoViewer.NumTotalLines )
		{
			Simulator_SessionInfo_SessionInfoViewer.ScrollIndex = 0;
			Simulator_SessionInfo_ScrollBar.Visibility = Visibility.Collapsed;
		}
		else
		{
			Simulator_SessionInfo_ScrollBar.Visibility = Visibility.Visible;
		}

		// telemetry data

		Simulator_TelemetryData_TelemetryDataViewer.InvalidateVisual();

		Simulator_TelemetryData_ScrollBar.Maximum = Simulator_TelemetryData_TelemetryDataViewer.NumTotalLines - Simulator_TelemetryData_TelemetryDataViewer.NumVisibleLines;
		Simulator_TelemetryData_ScrollBar.ViewportSize = Simulator_TelemetryData_TelemetryDataViewer.NumVisibleLines;

		if ( Simulator_TelemetryData_TelemetryDataViewer.NumVisibleLines >= Simulator_TelemetryData_TelemetryDataViewer.NumTotalLines )
		{
			Simulator_TelemetryData_TelemetryDataViewer.ScrollIndex = 0;
			Simulator_TelemetryData_ScrollBar.Visibility = Visibility.Collapsed;
		}
		else
		{
			Simulator_TelemetryData_ScrollBar.Visibility = Visibility.Visible;
		}
	}
}
