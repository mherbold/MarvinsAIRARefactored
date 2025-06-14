
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using ScrollEventArgs = System.Windows.Controls.Primitives.ScrollEventArgs;
using TabControl = System.Windows.Controls.TabControl;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Enums;
using MarvinsAIRARefactored.PInvoke;

namespace MarvinsAIRARefactored.Windows;

public partial class MainWindow : Window
{
	public nint WindowHandle { get; private set; } = 0;
	public bool GraphsTabItemIsVisible { get; private set; } = false;
	public bool DebugTabItemIsVisible { get; private set; } = false;

	private string? _installerFilePath = null;

	public MainWindow()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[MainWindow] Constructor >>>" );

			InitializeComponent();

			var version = Misc.GetVersion();

			app.Logger.WriteLine( $"[MainWindow] Version is {version}" );

			RefreshWindow();

			Components.Localization.SetLanguageComboBoxItemsSource( App_Language_ComboBox );

			app.Logger.WriteLine( "[MainWindow] <<< Constructor" );
		}
	}

	public void Initialize()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[MainWindow] Initialize >>>" );

			WindowHandle = new WindowInteropHelper( this ).Handle;

			var value = UXTheme.ShouldSystemUseDarkMode() ? 1 : 0;

			DWMAPI.DwmSetWindowAttribute( WindowHandle, (uint) DWMAPI.cbAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, (uint) System.Runtime.InteropServices.Marshal.SizeOf( value ) );

			UpdateRacingWheelPowerButton();
			UpdateRacingWheelForceFeedbackButtons();
			UpdateStatus();

			Misc.ForcePropertySetters( MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings );

			app.Logger.WriteLine( "[MainWindow] <<< Initialize" );
		}
	}

	public void RefreshWindow()
	{
		Dispatcher.BeginInvoke( () =>
		{
			var app = App.Instance;

			if ( app != null )
			{
				Title = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "AppTitle" ] + " " + Misc.GetVersion();

				app.DirectInput.SetMairaComboBoxItemsSource( RacingWheel_Device_ComboBox );

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
			}
		} );
	}

	public void UpdateRacingWheelPowerButton()
	{
		var app = App.Instance;

		if ( app != null )
		{
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
	}

	public void UpdateRacingWheelForceFeedbackButtons()
	{
		var app = App.Instance;

		if ( app != null )
		{
			Dispatcher.BeginInvoke( () =>
			{
				var disableButtons = !app.DirectInput.ForceFeedbackInitialized;

				RacingWheel_Test_MairaMappableButton.Disabled = disableButtons;
				RacingWheel_Reset_MairaMappableButton.Disabled = disableButtons;
				RacingWheel_Auto_MairaMappableButton.Disabled = disableButtons;
				RacingWheel_Clear_MairaMappableButton.Disabled = disableButtons;
			} );
		}
	}

	public void UpdateRacingWheelAlgorithmControls()
	{
		Dispatcher.BeginInvoke( () =>
		{
			var racingWheelAlgorithmRowTwoGridVisibility = Visibility.Collapsed;
			var racingWheelDetailBoostKnobControlVisibility = Visibility.Hidden;
			var racingWheelDeltaLimitKnobControlVisibility = Visibility.Hidden;
			var racingWheelBiasKnobControlVisibility = Visibility.Hidden;
			var racingWheelCurbProtectionGroupBoxVisibility = Visibility.Collapsed;

			switch ( MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelAlgorithm )
			{
				case RacingWheelAlgorithmEnum.DetailBooster:
				case RacingWheelAlgorithmEnum.DetailBoosterOn60Hz:
					racingWheelDetailBoostKnobControlVisibility = Visibility.Visible;
					racingWheelBiasKnobControlVisibility = Visibility.Visible;
					racingWheelCurbProtectionGroupBoxVisibility = Visibility.Visible;
					break;

				case RacingWheelAlgorithmEnum.DeltaLimiter:
				case RacingWheelAlgorithmEnum.DeltaLimiterOn60Hz:
					racingWheelDeltaLimitKnobControlVisibility = Visibility.Visible;
					racingWheelBiasKnobControlVisibility = Visibility.Visible;
					racingWheelCurbProtectionGroupBoxVisibility = Visibility.Visible;
					break;

				case RacingWheelAlgorithmEnum.ZeAlanLeTwist:
					racingWheelAlgorithmRowTwoGridVisibility = Visibility.Visible;
					racingWheelDeltaLimitKnobControlVisibility = Visibility.Visible;
					racingWheelBiasKnobControlVisibility = Visibility.Visible;
					racingWheelCurbProtectionGroupBoxVisibility = Visibility.Visible;
					break;
			}

			RacingWheel_AlgorithmRowTwo_Grid.Visibility = racingWheelAlgorithmRowTwoGridVisibility;
			RacingWheel_DetailBoost_KnobControl.Visibility = racingWheelDetailBoostKnobControlVisibility;
			RacingWheel_DeltaLimit_KnobControl.Visibility = racingWheelDeltaLimitKnobControlVisibility;
			RacingWheel_Bias_KnobControl.Visibility = racingWheelBiasKnobControlVisibility;
			RacingWheel_CurbProtection_GroupBox.Visibility = racingWheelCurbProtectionGroupBoxVisibility;
		} );
	}

	public void UpdateStatus()
	{
		Dispatcher.BeginInvoke( () =>
		{
			var app = App.Instance;

			if ( app != null )
			{
				if ( app.Simulator.IsConnected )
				{
					Status_Car_Label.Content = app.Simulator.CarScreenName == string.Empty ? MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Default" ] : app.Simulator.CarScreenName;
					Status_Track_Label.Content = app.Simulator.TrackDisplayName == string.Empty ? MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Default" ] : app.Simulator.TrackDisplayName;
					Status_TrackConfiguration_Label.Content = app.Simulator.TrackConfigName == string.Empty ? MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "Default" ] : app.Simulator.TrackConfigName;
					Status_WetDry_Label.Content = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ app.Simulator.WeatherDeclaredWet ? "Wet" : "Dry" ];

					Status_Car_Label.Visibility = Visibility.Visible;
					Status_Track_Label.Visibility = Visibility.Visible;
					Status_TrackConfiguration_Label.Visibility = Visibility.Visible;
					Status_WetDry_Label.Visibility = Visibility.Visible;
				}
				else
				{
					Status_Car_Label.Content = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "SimulatorNotRunning" ];
					Status_Track_Label.Content = string.Empty;
					Status_TrackConfiguration_Label.Content = string.Empty;
					Status_WetDry_Label.Content = string.Empty;

					Status_Car_Label.Visibility = Visibility.Visible;
					Status_Track_Label.Visibility = Visibility.Collapsed;
					Status_TrackConfiguration_Label.Visibility = Visibility.Collapsed;
					Status_WetDry_Label.Visibility = Visibility.Collapsed;
				}
			}
		} );
	}

	public void CloseAndLaunchInstaller( string installerFilePath )
	{
		_installerFilePath = installerFilePath;

		Close();
	}

	private void Window_Closed( object sender, EventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
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
	}

	private void TabControl_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( e.Source is TabControl tabControl )
		{
			if ( tabControl.SelectedItem is TabItem selectedTab )
			{
				GraphsTabItemIsVisible = ( selectedTab == Graphs_TabItem );
				DebugTabItemIsVisible = ( selectedTab == Debug_TabItem );
			}
		}
	}

	private void RacingWheel_Power_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelEnableForceFeedback = !MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelEnableForceFeedback;
	}

	private void RacingWheel_Test_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.RacingWheel.PlayTestSignal = true;
		}
	}

	private void RacingWheel_Reset_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.RacingWheel.ResetForceFeedback = true;
		}
	}

	private void RacingWheel_Auto_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
		}
	}

	private void RacingWheel_Clear_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.RacingWheel.ClearPeakTorque = true;
		}
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
		var app = App.Instance;

		if ( app != null )
		{
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
	}

	private void AdminBoxx_Brightness_ValueChanged( float newValue )
	{
		var app = App.Instance;

		app?.AdminBoxx.ResendAllLEDs();
	}

	private void AdminBoxx_Volume_ValueChanged( float newValue )
	{
		var app = App.Instance;

		app?.AudioManager.Play( "volume", newValue );
	}

	private void AdminBoxx_Test_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		app?.AdminBoxx.WaveFlag( AdminBoxx.Red );
	}

	private void Debug_AlanLeReset_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		app?.Debug.ResetFFBSamples();
	}

	private void Debug_AlanLeDump_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		app?.Debug.DumpFFBSamplesToCSVFile();
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
