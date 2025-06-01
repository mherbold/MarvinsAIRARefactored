
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.WinApi;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using ScrollEventArgs = System.Windows.Controls.Primitives.ScrollEventArgs;
using TabControl = System.Windows.Controls.TabControl;

namespace MarvinsAIRARefactored.Windows;

public partial class MainWindow : Window
{
	public nint WindowHandle { get; private set; } = 0;
	public bool GraphsTabItemIsVisible { get; private set; } = false;
	public bool DebugTabItemIsVisible { get; private set; } = false;

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
			UpdateRacingWheelTestAndResetButtons();

			Misc.ForcePropertySetters( Components.DataContext.Instance.Settings );

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
				Title = Components.DataContext.Instance.Localization[ "AppTitle" ] + " " + Misc.GetVersion();

				app.DirectInput.SetComboBoxItemsSource( RacingWheel_Device_ComboBox );

				RacingWheel.SetComboBoxItemsSource( RacingWheel_Algorithm_ComboBox );
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
				RacingWheel_Power_Button.Blink = false;

				ImageSource? imageSource;

				if ( !Components.DataContext.Instance.Settings.RacingWheelEnableForceFeedback )
				{
					imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/power_led_red.png" ) as ImageSource;

					RacingWheel_Power_Button.Blink = true;
				}
				else if ( app.RacingWheel.SuspendForceFeedback )
				{
					imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/power_led_yellow.png" ) as ImageSource;

					if ( app.Simulator.IsConnected )
					{
						RacingWheel_Power_Button.Blink = true;
					}
				}
				else
				{
					imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/artwork/power_led_green.png" ) as ImageSource;
				}

				if ( imageSource != null )
				{
					RacingWheel_Power_Button.ButtonIcon = imageSource;
				}
			} );
		}
	}

	public void UpdateRacingWheelTestAndResetButtons()
	{
		var app = App.Instance;

		if ( app != null )
		{
			Dispatcher.BeginInvoke( () =>
			{
				var disableButtons = !app.DirectInput.ForceFeedbackInitialized;

				RacingWheel_Test_Button.Disabled = disableButtons;
				RacingWheel_Reset_Button.Disabled = disableButtons;
			} );
		}
	}

	public void UpdateRacingWheelAlgorithmControls()
	{
		Dispatcher.BeginInvoke( () =>
		{
			var racingWheelAlgorithmRowTwoGridVisibility = Visibility.Collapsed;
			var detailBoostKnobControlVisibility = Visibility.Hidden;
			var deltaLimitKnobControlVisibility = Visibility.Hidden;
			var biasKnobControlVisibility = Visibility.Hidden;

			switch ( Components.DataContext.Instance.Settings.RacingWheelAlgorithm )
			{
				case Settings.RacingWheelAlgorithmEnum.DetailBooster:
				case Settings.RacingWheelAlgorithmEnum.DetailBoosterOn60Hz:
					detailBoostKnobControlVisibility = Visibility.Visible;
					biasKnobControlVisibility = Visibility.Visible;
					break;

				case Settings.RacingWheelAlgorithmEnum.DeltaLimiter:
				case Settings.RacingWheelAlgorithmEnum.DeltaLimiterOn60Hz:
					deltaLimitKnobControlVisibility = Visibility.Visible;
					biasKnobControlVisibility = Visibility.Visible;
					break;

				case Settings.RacingWheelAlgorithmEnum.ZeAlanLeTwist:
					racingWheelAlgorithmRowTwoGridVisibility = Visibility.Visible;
					deltaLimitKnobControlVisibility = Visibility.Visible;
					biasKnobControlVisibility = Visibility.Visible;
					break;
			}

			RacingWheel_AlgorithmRowTwo_Grid.Visibility = racingWheelAlgorithmRowTwoGridVisibility;
			RacingWheel_DetailBoost_KnobControl.Visibility = detailBoostKnobControlVisibility;
			RacingWheel_DeltaLimit_KnobControl.Visibility = deltaLimitKnobControlVisibility;
			RacingWheel_Bias_KnobControl.Visibility = biasKnobControlVisibility;
		} );
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

	private void RacingWheel_Power_Button_Click( object sender, RoutedEventArgs e )
	{
		Components.DataContext.Instance.Settings.RacingWheelEnableForceFeedback = !Components.DataContext.Instance.Settings.RacingWheelEnableForceFeedback;
	}

	private void RacingWheel_Test_Button_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.RacingWheel.PlayTestSignal = true;
		}
	}

	private void RacingWheel_Reset_Button_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.RacingWheel.ResetForceFeedback = true;
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

	private void AdminBoxx_ConnectToAdminBoxx_Switch_Toggled( object sender, EventArgs e )
	{
		var app = App.Instance;

		if ( app != null )
		{
			if ( AdminBoxx_ConnectToAdminBoxx_Switch.IsOn )
			{
				if ( !app.AdminBoxx.Connect() )
				{
					AdminBoxx_ConnectToAdminBoxx_Switch.IsOn = false;
				}
			}
			else
			{
				app.AdminBoxx.Disconnect();
			}
		}
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
