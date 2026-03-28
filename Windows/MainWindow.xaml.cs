
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;

using Simagic;

using Application = System.Windows.Application;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.Pages;

namespace MarvinsAIRARefactored.Windows;

public partial class MainWindow : Window
{
	public enum AppPage
	{
		Help,
		RacingWheel,
		SteeringEffects,
		Pedals,
		Wind,
		SeatBeltTensioner,
		Overlays,
		Sounds,
		SpeechToText,
		TradingPaints,
		Graph,
		Simulator,
		AdminBoxx,
		AppSettings,
		Contribute,
		Donate,
		Debug
	};

	private const int UpdateInterval = 6;

	public static readonly HelpPage _helpPage = new();
	public static readonly RacingWheelPage _racingWheelPage = new();
	public static readonly SteeringEffectsPage _steeringEffectsPage = new();
	public static readonly PedalsPage _pedalsPage = new();
	public static readonly WindPage _windPage = new();
	public static readonly SeatBeltTensionerPage _seatBeltTensionerPage = new();
	public static readonly OverlaysPage _overlaysPage = new();
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

		_appSettingsPage.UpdateLanguageOptions();

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

		_racingWheelPage.UpdateSteeringDeviceSection();

		AppMenuPopup.Initialize();

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
			_racingWheelPage.UpdateAlgorithmOptions();
			_racingWheelPage.UpdateMultiFFBSourceOptions();
			_racingWheelPage.UpdatePredictionModeOptions();
			_racingWheelPage.UpdatePreviewRecordingsOptions();
			_racingWheelPage.UpdateLFERecordingDeviceOptions();

			_steeringEffectsPage.UpdateCalibrationFileNameOptions();
			_steeringEffectsPage.UpdateVibrationPatternOptions();
			_steeringEffectsPage.UpdateConstantForceDirectionOptions();
			_steeringEffectsPage.UpdateSeatOfPantsAlgorithmOptions();

			_pedalsPage.UpdateEffectOptions();

			_speechToTextPage.UpdateLanguageOptions();

			app.SpeechToText.UpdateStrings();

			_appSettingsPage.UpdateDefaultPageOptions();

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

	public void UpdateRacingWheelAlgorithmControls()
	{
		Dispatcher.Invoke( () =>
		{
			var racingWheelPredictionModeComboBoxVisibility = Visibility.Collapsed;
			var racingWheelPredictionBlendKnobControlVisibility = Visibility.Collapsed;
			var racingWheelPredictionControlsRow = 0;

			var racingWheelDetailBoostKnobControlVisibility = Visibility.Collapsed;
			var racingWheelDetailBoostBiasKnobControlVisibility = Visibility.Collapsed;

			var racingWheelDeltaLimitKnobControlVisibility = Visibility.Collapsed;
			var racingWheelDeltaLimiterBiasKnobControlVisibility = Visibility.Collapsed;

			var racingWheelSlewCompressionThresholdVisibility = Visibility.Collapsed;
			var racingWheelSlewCompressionRateVisibility = Visibility.Collapsed;
			var racingWheelTotalCompressionThresholdVisibility = Visibility.Collapsed;
			var racingWheelTotalCompressionRateVisibility = Visibility.Collapsed;

			var racingWheelMulti360HzDetailVisibility = Visibility.Collapsed;
			var racingWheelMultiTorqueCompressionVisibility = Visibility.Collapsed;
			var racingWheelMultiEnableSlewPeakModeVisibility = Visibility.Collapsed;
			var racingWheelMultiSlewRateReductionVisibility = Visibility.Collapsed;
			var racingWheelMultiFFBSourceVisibility = Visibility.Collapsed;
			var racingWheelMultiDetailGainVisibility = Visibility.Collapsed;
			var racingWheelMultiOutputSmoothingVisibility = Visibility.Collapsed;

			var racingWheelCurbProtectionMairaGroupBoxVisibility = Visibility.Collapsed;

			var useThirdRowSpacer = false;

			switch ( MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.RacingWheelAlgorithm )
			{
				case RacingWheel.Algorithm.Native60Hz:
					racingWheelPredictionModeComboBoxVisibility = Visibility.Visible;
					racingWheelPredictionBlendKnobControlVisibility = Visibility.Visible;
					racingWheelPredictionControlsRow = 0;
					break;

				case RacingWheel.Algorithm.DetailBooster:
					racingWheelDetailBoostKnobControlVisibility = Visibility.Visible;
					racingWheelDetailBoostBiasKnobControlVisibility = Visibility.Visible;
					racingWheelCurbProtectionMairaGroupBoxVisibility = Visibility.Visible;
					break;

				case RacingWheel.Algorithm.DetailBoosterOn60Hz:
					racingWheelDetailBoostKnobControlVisibility = Visibility.Visible;
					racingWheelDetailBoostBiasKnobControlVisibility = Visibility.Visible;
					racingWheelCurbProtectionMairaGroupBoxVisibility = Visibility.Visible;
					racingWheelPredictionModeComboBoxVisibility = Visibility.Visible;
					racingWheelPredictionBlendKnobControlVisibility = Visibility.Visible;
					racingWheelPredictionControlsRow = 2;
					break;

				case RacingWheel.Algorithm.DeltaLimiter:
					racingWheelDeltaLimitKnobControlVisibility = Visibility.Visible;
					racingWheelDeltaLimiterBiasKnobControlVisibility = Visibility.Visible;
					racingWheelCurbProtectionMairaGroupBoxVisibility = Visibility.Visible;
					break;

				case RacingWheel.Algorithm.DeltaLimiterOn60Hz:
					racingWheelDeltaLimitKnobControlVisibility = Visibility.Visible;
					racingWheelDeltaLimiterBiasKnobControlVisibility = Visibility.Visible;
					racingWheelCurbProtectionMairaGroupBoxVisibility = Visibility.Visible;
					racingWheelPredictionModeComboBoxVisibility = Visibility.Visible;
					racingWheelPredictionBlendKnobControlVisibility = Visibility.Visible;
					racingWheelPredictionControlsRow = 2;
					break;

				case RacingWheel.Algorithm.SlewAndTotalCompression:
					racingWheelSlewCompressionThresholdVisibility = Visibility.Visible;
					racingWheelSlewCompressionRateVisibility = Visibility.Visible;
					racingWheelTotalCompressionThresholdVisibility = Visibility.Visible;
					racingWheelTotalCompressionRateVisibility = Visibility.Visible;
					racingWheelCurbProtectionMairaGroupBoxVisibility = Visibility.Visible;
					break;

				case RacingWheel.Algorithm.MultiAdjustmentToolkit:
					racingWheelMulti360HzDetailVisibility = Visibility.Visible;
					racingWheelMultiTorqueCompressionVisibility = Visibility.Visible;
					racingWheelMultiEnableSlewPeakModeVisibility = Visibility.Visible;
					racingWheelMultiSlewRateReductionVisibility = Visibility.Visible;
					racingWheelMultiFFBSourceVisibility = Visibility.Visible;
					racingWheelMultiDetailGainVisibility = Visibility.Visible;
					racingWheelMultiOutputSmoothingVisibility = Visibility.Visible;

					useThirdRowSpacer = true;
					break;
			}

			_racingWheelPage.PredictionMode_MairaComboBox.Visibility = racingWheelPredictionModeComboBoxVisibility;
			_racingWheelPage.PredictionBlend_MairaKnob.Visibility = racingWheelPredictionBlendKnobControlVisibility;

			Grid.SetRow( _racingWheelPage.PredictionMode_MairaComboBox, racingWheelPredictionControlsRow );
			Grid.SetRow( _racingWheelPage.PredictionBlend_MairaKnob, racingWheelPredictionControlsRow );

			_racingWheelPage.AlgorithmThirdRowSpacer_RowDefinition.Height = new GridLength( useThirdRowSpacer ? 20 : 0 );

			_racingWheelPage.DetailBoost_MairaKnob.Visibility = racingWheelDetailBoostKnobControlVisibility;
			_racingWheelPage.DetailBoostBias_MairaKnob.Visibility = racingWheelDetailBoostBiasKnobControlVisibility;

			_racingWheelPage.DeltaLimit_MairaKnob.Visibility = racingWheelDeltaLimitKnobControlVisibility;
			_racingWheelPage.DeltaLimiterBias_MairaKnob.Visibility = racingWheelDeltaLimiterBiasKnobControlVisibility;

			_racingWheelPage.SlewCompressionThreshold_MairaKnob.Visibility = racingWheelSlewCompressionThresholdVisibility;
			_racingWheelPage.SlewCompressionRate_MairaKnob.Visibility = racingWheelSlewCompressionRateVisibility;
			_racingWheelPage.TotalCompressionThreshold_MairaKnob.Visibility = racingWheelTotalCompressionThresholdVisibility;
			_racingWheelPage.TotalCompressionRate_MairaKnob.Visibility = racingWheelTotalCompressionRateVisibility;

			_racingWheelPage.Multi360HzDetail_MairaKnob.Visibility = racingWheelMulti360HzDetailVisibility;
			_racingWheelPage.MultiTorqueCompression_MairaKnob.Visibility = racingWheelMultiTorqueCompressionVisibility;
			_racingWheelPage.MultiEnableSlewPeakMode_MairaSwitch.Visibility = racingWheelMultiEnableSlewPeakModeVisibility;
			_racingWheelPage.MultiSlewRateReduction_MairaKnob.Visibility = racingWheelMultiSlewRateReductionVisibility;
			_racingWheelPage.MultiFFBSource_MairaComboBox.Visibility = racingWheelMultiFFBSourceVisibility;
			_racingWheelPage.MultiDetailGain_MairaKnob.Visibility = racingWheelMultiDetailGainVisibility;
			_racingWheelPage.MultiOutputSmoothing_MairaKnob.Visibility = racingWheelMultiOutputSmoothingVisibility;

			_racingWheelPage.CurbProtection_MairaGroupBox.Visibility = racingWheelCurbProtectionMairaGroupBoxVisibility;
		} );
	}

	public void UpdateRacingWheelSimpleMode()
	{
		Dispatcher.Invoke( () =>
		{
			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

			Misc.ApplyToTaggedElements( Root, "Complex", element => element.Visibility = settings.RacingWheelSimpleModeEnabled ? Visibility.Collapsed : Visibility.Visible );
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

				case HPR.PedalsDevice.P500:
					_pedalsPage.Device_Label.Text = localization[ "PedalsP500" ];
					break;

				case HPR.PedalsDevice.P700:
					_pedalsPage.Device_Label.Text = localization[ "PedalsP700" ];
					break;

				case HPR.PedalsDevice.P1000:
					_pedalsPage.Device_Label.Text = localization[ "PedalsP1000" ];
					break;

				case HPR.PedalsDevice.P2000:
					_pedalsPage.Device_Label.Text = localization[ "PedalsP2000" ];
					break;

				default:
					_pedalsPage.Device_Label.Text = localization[ "PedalsPrototype" ];
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
#if !ADMINBOXX

				var resourceStream = Application.GetResourceStream( new Uri( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/AppIcon/maira-universal.ico" ) ).Stream;

#else

				var resourceStream = Application.GetResourceStream( new Uri( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/AppIcon/adminboxx.ico" ) ).Stream;

#endif

				_notifyIcon = new()
				{
					Icon = new Icon( resourceStream ),
					Visible = true,
					Text = localization[ "AppTitle" ],
					ContextMenuStrip = new ContextMenuStrip()
				};

#if ADMINBOXX

				_notifyIcon.Text = localization[ "AdminBoxx" ];

#endif

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
		UpdateRacingWheelSimpleMode();
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

	private void OpenHelp_Executed( object sender, ExecutedRoutedEventArgs e )
	{
		HelpService.ExecuteOpenHelp( e.Parameter );
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter <= 0 )
		{
			_updateCounter = UpdateInterval;

			_simulatorPage.HeaderData_HeaderDataViewer.InvalidateVisual();
			_simulatorPage.SessionInfo_SessionInfoViewer.InvalidateVisual();
			_simulatorPage.TelemetryData_TelemetryDataViewer.InvalidateVisual();
		}
	}
}
