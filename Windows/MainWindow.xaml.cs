
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Simagic;

using Application = System.Windows.Application;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.Pages;
using MarvinsAIRARefactored.PInvoke;

namespace MarvinsAIRARefactored.Windows;

public partial class MainWindow : Window
{
	public enum AppPage
	{
		RacingWheel,
		SteeringEffects,
		Pedals,
		Wind,
		Sounds,
		SpeechToText,
		TradingPaints,
		Graph,
		Simulator,
		AdminBoxx,
		Application,
		Contribute,
		Donate,
		Debug
	};

	private const int UpdateInterval = 6;

	public static readonly RacingWheelPage _racingWheelPage = new();
	public static readonly SteeringEffectsPage _steeringEffectsPage = new();
	public static readonly PedalsPage _pedalsPage = new();
	public static readonly WindPage _windPage = new();
	public static readonly SoundsPage _soundsPage = new();
	public static readonly SpeechToTextPage _speechToTextPage = new();
	public static readonly TradingPaintsPage _tradingPaintsPage = new();
	public static readonly GraphPage _graphPage = new();
	public static readonly SimulatorPage _simulatorPage = new();
	public static readonly AdminBoxxPage _adminBoxxPage = new();
	public static readonly AppSettingsPage _appSettingsPage = new();
	public static readonly ContributePage _contributePage = new();
	public static readonly DonatePage _donatePage = new();
	public static readonly DebugPage _debugPage = new();

	public nint WindowHandle { get; private set; } = 0;

	private string? _installerFilePath = null;
	private bool _initialized = false;
	private NotifyIcon? _notifyIcon = null;
	private int _updateCounter = UpdateInterval + 6;

	public MainWindow()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[MainWindow] Constructor >>>" );

		InitializeComponent();

		var version = Misc.GetVersion();

		app.Logger.WriteLine( $"[MainWindow] Version is {version}" );

		MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization.SetLanguageComboBoxItemsSource( _appSettingsPage.Language_MairaComboBox );

#if ADMINBOXX

		var iconUri = new Uri( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/AppIcon/adminboxx.ico" );

#else

		var iconUri = new Uri( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/AppIcon/maira-universal.ico" );

#endif

		Icon = BitmapFrame.Create( iconUri );

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
		Dispatcher.Invoke( () =>
		{
			var app = App.Instance!;

#if ADMINBOXX

			Title = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "AdminBoxx" ] + " " + Misc.GetVersion();

#else

			Title = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization[ "AppTitle" ] + " " + Misc.GetVersion();

			_racingWheelPage.UpdateSteeringDeviceOptions();
			_racingWheelPage.UpdateLFERecordingDeviceOptions();
			_racingWheelPage.UpdatePreviewRecordingsOptions();
			_racingWheelPage.UpdateAlgorithmOptions();

			_steeringEffectsPage.UpdateCalibrationFileNameOptions();
			_steeringEffectsPage.UpdateVibrationPatternOptions();
			_steeringEffectsPage.UpdateConstantForceDirectionOptions();

			_pedalsPage.UpdateEffectOptions();

			_speechToTextPage.UpdateLanguageOptions();

			app.SpeechToText.UpdateStrings();

#endif

			AppMenuPopup.RelocalizeAppMenuItems();

			UpdateStatus();
			UpdatePedalsDevice();
			UpdateNotifyIcon();
		} );
	}

	public void UpdateStatus()
	{
		Dispatcher.Invoke( () =>
		{
			var app = App.Instance!;

			var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

			var statusText1 = string.Empty;
			var statusText2 = string.Empty;
			var statusText3 = string.Empty;
			var statusText4 = string.Empty;

			var statusStyle = MairaStatusBar.StatusStyleEnum.Normal;

			if ( app.CloudService.CheckingForUpdate )
			{
				statusText1 = localization[ "CheckingForUpdate" ];

				statusStyle = MairaStatusBar.StatusStyleEnum.Warning;
			}
			else if ( app.CloudService.DownloadingUpdate )
			{
				statusText1 = localization[ "DownloadingUpdate" ];

				statusStyle = MairaStatusBar.StatusStyleEnum.Warning;
			}
			else if ( app.AdminBoxx.IsUpdating )
			{
				statusText1 = localization[ "AdminBoxxIsUpdating" ];

				statusStyle = MairaStatusBar.StatusStyleEnum.Warning;
			}
			else if ( app.Simulator.IsConnected )
			{
				statusText1 = app.Simulator.CarScreenName == string.Empty ? localization[ "Default" ] : app.Simulator.CarScreenName;
				statusText2 = app.Simulator.TrackDisplayName == string.Empty ? localization[ "Default" ] : app.Simulator.TrackDisplayName;
				statusText3 = app.Simulator.TrackConfigName == string.Empty ? localization[ "Default" ] : app.Simulator.TrackConfigName;
				statusText4 = localization[ app.Simulator.WeatherDeclaredWet ? "Wet" : "Dry" ];

				statusStyle = MairaStatusBar.StatusStyleEnum.Normal;
			}
			else
			{
				statusText1 = localization[ "SimulatorNotRunning" ];

				statusStyle = MairaStatusBar.StatusStyleEnum.Error;
			}

			StatusBar.StatusText1 = statusText1;
			StatusBar.StatusText2 = statusText2;
			StatusBar.StatusText3 = statusText3;
			StatusBar.StatusText4 = statusText4;

			StatusBar.StatusStyle = statusStyle;
		} );
	}

	public void UpdateRacingWheelPowerButton()
	{
		var app = App.Instance!;

		Dispatcher.Invoke( () =>
		{
			ImageSource? imageSource;

			if ( !MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelEnableForceFeedback )
			{
				imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Buttons/power-red.png" ) as ImageSource;
			}
			else if ( app.RacingWheel.SuspendForceFeedback || !app.DirectInput.ForceFeedbackInitialized )
			{
				if ( app.Simulator.IsConnected )
				{
					imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Buttons/power-yellow.png" ) as ImageSource;
				}
				else
				{
					imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Buttons/power-orange.png" ) as ImageSource;
				}
			}
			else
			{
				imageSource = new ImageSourceConverter().ConvertFromString( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Buttons/power-green.png" ) as ImageSource;
			}

			if ( imageSource != null )
			{
				_racingWheelPage.Power_MairaMappableButton.Icon = imageSource;
			}
		} );
	}

	public void UpdateRacingWheelForceFeedbackButtons()
	{
		var app = App.Instance!;

		Dispatcher.Invoke( () =>
		{
			var isEnabled = app.DirectInput.ForceFeedbackInitialized;

			_racingWheelPage.Test_MairaMappableButton.IsEnabled = isEnabled;
			_racingWheelPage.Reset_MairaMappableButton.IsEnabled = isEnabled;
			_racingWheelPage.Set_MairaMappableButton.IsEnabled = isEnabled;
			_racingWheelPage.Clear_MairaMappableButton.IsEnabled = isEnabled;
		} );
	}

	public void UpdateRacingWheelAlgorithmControls()
	{
		Dispatcher.Invoke( () =>
		{
			var racingWheelDetailBoostKnobControlVisibility = Visibility.Hidden;
			var racingWheelDetailBoostBiasKnobControlVisibility = Visibility.Hidden;

			var racingWheelDeltaLimitKnobControlVisibility = Visibility.Hidden;
			var racingWheelDeltaLimiterBiasKnobControlVisibility = Visibility.Hidden;

			var racingWheelEnableSoftLimiterVisibility = Visibility.Hidden;
			var racingWheelSlewCompressionThresholdVisibility = Visibility.Hidden;
			var racingWheelSlewCompressionRateVisibility = Visibility.Hidden;
			var racingWheelTotalCompressionThresholdVisibility = Visibility.Hidden;
			var racingWheelTotalCompressionRateVisibility = Visibility.Hidden;

			var racingWheelMultiSoftLimiterVisibility = Visibility.Hidden;
			var racingWheelMultiTorqueCompressionVisibility = Visibility.Hidden;
			var racingWheelMultiSlewRateReductionVisibility = Visibility.Hidden;
			var racingWheelMultiDetailGainVisibility = Visibility.Hidden;
			var racingWheelMultiOutputSmoothingVisibility = Visibility.Hidden;

			var racingWheelAlgorithmRowTwoGridVisibility = Visibility.Collapsed;
			var racingWheelCurbProtectionStackPanelVisibility = Visibility.Collapsed;

			switch ( MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelAlgorithm )
			{
				case RacingWheel.Algorithm.DetailBooster:
				case RacingWheel.Algorithm.DetailBoosterOn60Hz:
					racingWheelDetailBoostKnobControlVisibility = Visibility.Visible;
					racingWheelDetailBoostBiasKnobControlVisibility = Visibility.Visible;
					racingWheelCurbProtectionStackPanelVisibility = Visibility.Visible;
					break;

				case RacingWheel.Algorithm.DeltaLimiter:
				case RacingWheel.Algorithm.DeltaLimiterOn60Hz:
					racingWheelDeltaLimitKnobControlVisibility = Visibility.Visible;
					racingWheelDeltaLimiterBiasKnobControlVisibility = Visibility.Visible;
					racingWheelCurbProtectionStackPanelVisibility = Visibility.Visible;
					break;

				case RacingWheel.Algorithm.SlewAndTotalCompression:
					racingWheelEnableSoftLimiterVisibility = Visibility.Visible;
					racingWheelSlewCompressionThresholdVisibility = Visibility.Visible;
					racingWheelSlewCompressionRateVisibility = Visibility.Visible;
					racingWheelTotalCompressionThresholdVisibility = Visibility.Visible;
					racingWheelTotalCompressionRateVisibility = Visibility.Visible;

					racingWheelAlgorithmRowTwoGridVisibility = Visibility.Visible;
					racingWheelCurbProtectionStackPanelVisibility = Visibility.Visible;
					break;

				case RacingWheel.Algorithm.MultiAdjustmentToolkit:
					racingWheelMultiSoftLimiterVisibility = Visibility.Visible;
					racingWheelMultiTorqueCompressionVisibility = Visibility.Visible;
					racingWheelMultiSlewRateReductionVisibility = Visibility.Visible;
					racingWheelMultiDetailGainVisibility = Visibility.Visible;
					racingWheelMultiOutputSmoothingVisibility = Visibility.Visible;

					racingWheelAlgorithmRowTwoGridVisibility = Visibility.Visible;
					racingWheelCurbProtectionStackPanelVisibility = Visibility.Visible;
					break;
			}

			_racingWheelPage.DetailBoost_MairaKnob.Visibility = racingWheelDetailBoostKnobControlVisibility;
			_racingWheelPage.DetailBoostBias_MairaKnob.Visibility = racingWheelDetailBoostBiasKnobControlVisibility;

			_racingWheelPage.DeltaLimit_MairaKnob.Visibility = racingWheelDeltaLimitKnobControlVisibility;
			_racingWheelPage.DeltaLimiterBias_MairaKnob.Visibility = racingWheelDeltaLimiterBiasKnobControlVisibility;

			_racingWheelPage.EnableSoftLimiter_MairaSwitch.Visibility = racingWheelEnableSoftLimiterVisibility;
			_racingWheelPage.SlewCompressionThreshold_MairaKnob.Visibility = racingWheelSlewCompressionThresholdVisibility;
			_racingWheelPage.SlewCompressionRate_MairaKnob.Visibility = racingWheelSlewCompressionRateVisibility;
			_racingWheelPage.TotalCompressionThreshold_MairaKnob.Visibility = racingWheelTotalCompressionThresholdVisibility;
			_racingWheelPage.TotalCompressionRate_MairaKnob.Visibility = racingWheelTotalCompressionRateVisibility;

			_racingWheelPage.EnableMultiSoftLimiter_MairaSwitch.Visibility = racingWheelMultiSoftLimiterVisibility;
			_racingWheelPage.MultiTorqueCompression_MairaKnob.Visibility = racingWheelMultiTorqueCompressionVisibility;
			_racingWheelPage.MultiSlewRateReduction_MairaKnob.Visibility = racingWheelMultiSlewRateReductionVisibility;
			_racingWheelPage.MultiDetailGain_MairaKnob.Visibility = racingWheelMultiDetailGainVisibility;
			_racingWheelPage.MultiOutputSmoothing_MairaKnob.Visibility = racingWheelMultiOutputSmoothingVisibility;

			_racingWheelPage.AlgorithmRowTwo_Grid.Visibility = racingWheelAlgorithmRowTwoGridVisibility;
			_racingWheelPage.CurbProtection_StackPanel.Visibility = racingWheelCurbProtectionStackPanelVisibility;
		} );
	}

	public void UpdateRacingWheelSimpleMode()
	{
		Dispatcher.Invoke( () =>
		{
			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

			Misc.ApplyToTaggedElements( Root_Grid, "Complex", element => element.Visibility = settings.RacingWheelSimpleModeEnabled ? Visibility.Collapsed : Visibility.Visible );
		} );
	}

	public void UpdatePedalsDevice()
	{
		var app = App.Instance!;

		Dispatcher.Invoke( () =>
		{
			var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

			switch ( app.Pedals.PedalsDevice )
			{
				case HPR.PedalsDevice.None:
					_pedalsPage.Device_Label.Text = localization[ "PedalsNone" ];
					break;

				case HPR.PedalsDevice.P1000:
					_pedalsPage.Device_Label.Text = localization[ "PedalsP1000" ];
					break;

				case HPR.PedalsDevice.P2000:
					_pedalsPage.Device_Label.Text = localization[ "PedalsP2000" ];
					break;
			}
		} );
	}

	public void UpdateNotifyIcon()
	{
		var app = App.Instance!;

		Dispatcher.Invoke( () =>
		{
			var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

			if ( _notifyIcon != null )
			{
				_notifyIcon.Visible = false;

				_notifyIcon.Dispose();
			}

			if ( MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.AppMinimizeToSystemTray )
			{
				var resourceStream = Application.GetResourceStream( new Uri( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/AppIcon/maira-universal.ico" ) ).Stream;

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

			UpdateRacingWheelSimpleMode();
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

	private void PageName_TextBlock_PreviewMouseLeftButtonDown( object sender, MouseButtonEventArgs e )
	{
		AppMenuButton.IsMenuOpen = true;
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter == 0 )
		{
			_updateCounter = UpdateInterval;

			_simulatorPage.HeaderData_HeaderDataViewer.InvalidateVisual();
			_simulatorPage.SessionInfo_SessionInfoViewer.InvalidateVisual();
			_simulatorPage.TelemetryData_TelemetryDataViewer.InvalidateVisual();
		}
	}
}
