
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;

using static MarvinsAIRARefactored.Windows.MainWindow;

using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Controls
{
	public partial class MairaAppMenuPopup : UserControl
	{
		public static AppPage CurrentAppPage { get; private set; } = AppPage.RacingWheel;

		public sealed class AppMenuItem : INotifyPropertyChanged
		{
			public AppPage AppPage { get; init; }
			public UserControl PageUserControl { get; init; } = new UserControl();
			private string _displayName = string.Empty;

			public string DisplayName
			{
				get => _displayName;

				set
				{
					if ( _displayName != value )
					{
						_displayName = value;

						OnPropertyChanged();
					}
				}
			}

			public event PropertyChangedEventHandler? PropertyChanged;
			private void OnPropertyChanged( [CallerMemberName] string? propertyName = null ) => PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
		}

		public ObservableCollection<AppMenuItem> AppMenuItems { get; } = [];

		public MairaAppMenuPopup()
		{
			InitializeComponent();

#if ADMINBOXX

			var uri = new Uri( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Misc/adminboxx-logo.png", UriKind.Absolute );

			Image.Source = new BitmapImage( uri );

#endif
		}

		#region User Control Events

		private void Scrim_MouseDown( object sender, System.Windows.Input.MouseButtonEventArgs e )
		{
			IsMenuOpen = false;
		}

		#endregion

		private void ListBoxItem_PreviewMouseLeftButtonUp( object sender, System.Windows.Input.MouseButtonEventArgs e )
		{
			if ( sender is System.Windows.Controls.ListBoxItem listBoxItem )
			{
				if ( listBoxItem.DataContext is AppMenuItem clickedItem )
				{
					if ( Equals( clickedItem, SelectedAppMenuItem ) )
					{
						IsMenuOpen = false;
						e.Handled = true;
					}
				}
			}
		}

		private static string? GetHelpTopicForAppPage( AppPage appPage )
		{
			switch ( appPage )
			{
				case AppPage.RacingWheel:
					return "advanced/racing-wheel/";

				case AppPage.SteeringEffects:
					return "advanced/steering-effects/";

				case AppPage.Pedals:
					return "advanced/pedals/";

				case AppPage.Wind:
					return "advanced/wind/";

				case AppPage.Sounds:
					return "advanced/sounds/";

				case AppPage.SpeechToText:
					return "advanced/speech-to-text/";

				case AppPage.TradingPaints:
					return "advanced/trading-paints/";

				case AppPage.Graph:
					return "advanced/graph/";

				case AppPage.Simulator:
					return "advanced/simulator/";

				case AppPage.AppSettings:
					return "advanced/app-settings/";

				default:
					return null;
			}
		}

		#region Dependency Properties

		public static readonly DependencyProperty SelectedAppPageProperty = DependencyProperty.Register( nameof( SelectedAppPage ), typeof( AppPage ), typeof( MairaAppMenuPopup ), new FrameworkPropertyMetadata( default( AppPage ), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedAppPageChanged ) );

		public AppPage SelectedAppPage
		{
			get => (AppPage) GetValue( SelectedAppPageProperty );
			set => SetValue( SelectedAppPageProperty, value );
		}

		public static readonly DependencyProperty SelectedAppPageTextProperty = DependencyProperty.Register( nameof( SelectedAppPageText ), typeof( string ), typeof( MairaAppMenuPopup ), new PropertyMetadata( string.Empty ) );

		public string SelectedAppPageText
		{
			get => (string) GetValue( SelectedAppPageTextProperty );
			set => SetValue( SelectedAppPageTextProperty, value );
		}

		public static readonly DependencyProperty SelectedAppPageUserControlProperty = DependencyProperty.Register( nameof( SelectedAppPageUserControl ), typeof( UserControl ), typeof( MairaAppMenuPopup ), new FrameworkPropertyMetadata( null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault ) );

		public object SelectedAppPageUserControl
		{
			get => GetValue( SelectedAppPageUserControlProperty );
			set => SetValue( SelectedAppPageUserControlProperty, value );
		}

		public static readonly DependencyProperty SelectedAppMenuItemProperty = DependencyProperty.Register( nameof( SelectedAppMenuItem ), typeof( AppMenuItem ), typeof( MairaAppMenuPopup ), new FrameworkPropertyMetadata( null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedMenuItemChanged ) );

		public AppMenuItem? SelectedAppMenuItem
		{
			get => (AppMenuItem?) GetValue( SelectedAppMenuItemProperty );
			set => SetValue( SelectedAppMenuItemProperty, value );
		}

		public static readonly DependencyProperty IsMenuOpenProperty = DependencyProperty.Register( nameof( IsMenuOpen ), typeof( bool ), typeof( MairaAppMenuPopup ), new FrameworkPropertyMetadata( false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault ) );

		public bool IsMenuOpen
		{
			get => (bool) GetValue( IsMenuOpenProperty );
			set => SetValue( IsMenuOpenProperty, value );
		}

		#endregion

		#region Dependency Property Changed Events

		private static void OnSelectedMenuItemChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
		{
			if ( d is MairaAppMenuPopup mairaAppMenuPopup )
			{
				if ( e.NewValue is AppMenuItem appMenuItem )
				{
					mairaAppMenuPopup.SelectedAppPage = appMenuItem.AppPage;
					mairaAppMenuPopup.SelectedAppPageUserControl = appMenuItem.PageUserControl;

					mairaAppMenuPopup.IsMenuOpen = false;

					mairaAppMenuPopup.UpdateSelectedAppPageText();
				}
			}
		}

		private static void OnSelectedAppPageChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
		{
			if ( d is MairaAppMenuPopup mairaAppMenuPopup )
			{
				var appPage = (AppPage) e.NewValue;

				var match = mairaAppMenuPopup.AppMenuItems.FirstOrDefault( appMenuItem => appMenuItem.AppPage == appPage );

				if ( ( match != null ) && !Equals( mairaAppMenuPopup.SelectedAppMenuItem, match ) )
				{
					mairaAppMenuPopup.SelectedAppMenuItem = match;
				}

				CurrentAppPage = appPage;
			}
		}

		#endregion

		#region Logic

		public void Initialize()
		{

#if !ADMINBOXX

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.Help,
				PageUserControl = _helpPage
			} );

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.RacingWheel,
				PageUserControl = _racingWheelPage
			} );

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.SteeringEffects,
				PageUserControl = _steeringEffectsPage
			} );

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.Pedals,
				PageUserControl = _pedalsPage
			} );

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.Wind,
				PageUserControl = _windPage
			} );

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.Sounds,
				PageUserControl = _soundsPage
			} );

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.SpeechToText,
				PageUserControl = _speechToTextPage
			} );

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.TradingPaints,
				PageUserControl = _tradingPaintsPage
			} );

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.Graph,
				PageUserControl = _graphPage
			} );

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.Simulator,
				PageUserControl = _simulatorPage
			} );

#endif

#if ADMINBOXX

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.AdminBoxx,
				PageUserControl = _adminBoxxPage
			} );

#endif

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.AppSettings,
				PageUserControl = _appSettingsPage
			} );

#if !ADMINBOXX

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.Contribute,
				PageUserControl = _contributePage
			} );

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.Donate,
				PageUserControl = _donatePage
			} );

#endif

#if DEBUG

			AppMenuItems.Add( new AppMenuItem
			{
				AppPage = AppPage.Debug,
				PageUserControl = _debugPage
			} );

#endif

			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

			object currentPage = settings.AppDefaultPage switch
			{
				AppPage.Help => _helpPage,
				AppPage.RacingWheel => _racingWheelPage,
				AppPage.SteeringEffects => _steeringEffectsPage,
				AppPage.Pedals => _pedalsPage,
				AppPage.Wind => _windPage,
				AppPage.Sounds => _soundsPage,
				AppPage.SpeechToText => _speechToTextPage,
				AppPage.TradingPaints => _tradingPaintsPage,
				AppPage.Graph => _graphPage,
				AppPage.Simulator => _simulatorPage,
				AppPage.AdminBoxx => _adminBoxxPage,
				AppPage.AppSettings => _appSettingsPage,
				AppPage.Contribute => _contributePage,
				AppPage.Donate => _donatePage,
				AppPage.Debug => _debugPage,
				_ => _racingWheelPage
			};

			SelectedAppPage = settings.AppDefaultPage;
			SelectedAppPageUserControl = currentPage;
			SelectedAppMenuItem = AppMenuItems.FirstOrDefault( appMenuItem => appMenuItem.AppPage == SelectedAppPage );

			CurrentAppPage = SelectedAppPage;

			UpdateSelectedAppPageText();
		}

		public void RelocalizeAppMenuItems()
		{
			var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

			foreach ( var menuItem in AppMenuItems )
			{
				switch ( menuItem.AppPage )
				{
					case AppPage.Help:
						menuItem.DisplayName = localization[ "Help" ];
						break;

					case AppPage.RacingWheel:
						menuItem.DisplayName = localization[ "RacingWheel" ];
						break;

					case AppPage.SteeringEffects:
						menuItem.DisplayName = localization[ "SteeringEffects" ];
						break;

					case AppPage.Pedals:
						menuItem.DisplayName = localization[ "Pedals" ];
						break;

					case AppPage.Wind:
						menuItem.DisplayName = localization[ "Wind" ];
						break;

					case AppPage.Sounds:
						menuItem.DisplayName = localization[ "Sounds" ];
						break;

					case AppPage.SpeechToText:
						menuItem.DisplayName = localization[ "SpeechToText" ];
						break;

					case AppPage.TradingPaints:
						menuItem.DisplayName = localization[ "TradingPaints" ];
						break;

					case AppPage.Graph:
						menuItem.DisplayName = localization[ "Graph" ];
						break;

					case AppPage.Simulator:
						menuItem.DisplayName = localization[ "Simulator" ];
						break;

					case AppPage.AdminBoxx:
						menuItem.DisplayName = localization[ "AdminBoxx" ];
						break;

					case AppPage.AppSettings:
						menuItem.DisplayName = localization[ "AppSettings" ];
						break;

					case AppPage.Contribute:
						menuItem.DisplayName = localization[ "Contribute" ];
						break;

					case AppPage.Donate:
						menuItem.DisplayName = localization[ "Donate" ];
						break;

					case AppPage.Debug:
						menuItem.DisplayName = "Debug";
						break;
				}
			}

			UpdateSelectedAppPageText();
		}

		public void UpdateSelectedAppPageText()
		{
			var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

			switch ( SelectedAppPage )
			{
				case AppPage.Help:
					SelectedAppPageText = localization[ "Help_UC" ];
					break;

				case AppPage.RacingWheel:
					SelectedAppPageText = localization[ "RacingWheel_UC" ];
					break;

				case AppPage.SteeringEffects:
					SelectedAppPageText = localization[ "SteeringEffects_UC" ];
					break;

				case AppPage.Pedals:
					SelectedAppPageText = localization[ "Pedals_UC" ];
					break;

				case AppPage.Wind:
					SelectedAppPageText = localization[ "Wind_UC" ];
					break;

				case AppPage.Sounds:
					SelectedAppPageText = localization[ "Sounds_UC" ];
					break;

				case AppPage.SpeechToText:
					SelectedAppPageText = localization[ "SpeechToText_UC" ];
					break;

				case AppPage.TradingPaints:
					SelectedAppPageText = localization[ "TradingPaints_UC" ];
					break;

				case AppPage.Graph:
					SelectedAppPageText = localization[ "Graph_UC" ];
					break;

				case AppPage.Simulator:
					SelectedAppPageText = localization[ "Simulator_UC" ];
					break;

				case AppPage.AdminBoxx:
					SelectedAppPageText = localization[ "AdminBoxx_UC" ];
					break;

				case AppPage.AppSettings:
					SelectedAppPageText = localization[ "AppSettings_UC" ];
					break;

				case AppPage.Contribute:
					SelectedAppPageText = localization[ "Contribute_UC" ];
					break;

				case AppPage.Donate:
					SelectedAppPageText = localization[ "Donate_UC" ];
					break;

				case AppPage.Debug:
					SelectedAppPageText = "DEBUG";
					break;
			}

			var helpTopic = GetHelpTopicForAppPage( SelectedAppPage );

			Classes.HelpService.SetHelpTopic( App.Instance!.MainWindow, helpTopic );
		}

		#endregion
	}
}
