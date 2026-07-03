
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

#if ADMINBOXX

using System.Windows.Media.Imaging;

#endif

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
			private string _suffix = string.Empty;
			private bool _isSelected = false;

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

			// Optional colored suffix shown after the display name (e.g. the red Donate heart). Rendered in
			// its own Run so it can carry a different brush than the menu label.
			public string Suffix
			{
				get => _suffix;

				set
				{
					if ( _suffix != value )
					{
						_suffix = value;

						OnPropertyChanged();
					}
				}
			}

			public bool IsSelected
			{
				get => _isSelected;

				set
				{
					if ( _isSelected != value )
					{
						_isSelected = value;

						OnPropertyChanged();
					}
				}
			}

			public event PropertyChangedEventHandler? PropertyChanged;
			private void OnPropertyChanged( [CallerMemberName] string? propertyName = null ) => PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
		}

		// The app menu is laid out as three fixed columns. Each column is an independent collection so
		// pages appear in the exact column / order the maintainer specified (auto-flow layouts cannot
		// place specific pages in specific columns when the columns have different lengths).
		public ObservableCollection<AppMenuItem> AppMenuItemsColumn1 { get; } = [];
		public ObservableCollection<AppMenuItem> AppMenuItemsColumn2 { get; } = [];
		public ObservableCollection<AppMenuItem> AppMenuItemsColumn3 { get; } = [];

		private IEnumerable<AppMenuItem> AllMenuItems => AppMenuItemsColumn1.Concat( AppMenuItemsColumn2 ).Concat( AppMenuItemsColumn3 );

		// AdminBoxx is shown only in English. It is added to / removed from column 1 (rather than hidden
		// with a Visibility="Collapsed" trigger) because a Collapsed item does not reclaim
		// its row under right-to-left FlowDirection (Arabic, Hebrew, ...) - it leaves a blank line where
		// the item used to be. Removing the container outright avoids that quirk in every language.
		private AppMenuItem? _adminBoxxMenuItem;
		private int _adminBoxxColumn1Index;

		public MairaAppMenuPopup()
		{
			InitializeComponent();

#if ADMINBOXX

			var uri = new Uri( "pack://application:,,,/MarvinsAIRARefactored;component/Artwork/Misc/adminboxx-logo.png", UriKind.Absolute );

			Image.Visibility = Visibility.Collapsed;

			ColorImage.Source = new BitmapImage( uri );
			ColorImage.Visibility = Visibility.Visible;

			// AdminBoxx has only a few menu items, so restore the classic single-column layout: logo on
			// top, the menu stacked directly beneath it. Give the menu its own row (the multi-column build
			// overlaps the logo and menu in a single row) and hide the two empty columns.
			MenuGrid.RowDefinitions.Add( new System.Windows.Controls.RowDefinition { Height = GridLength.Auto } );

			System.Windows.Controls.Grid.SetRow( MenuScrollViewer, 1 );

			Column2Items.Visibility = Visibility.Collapsed;
			Column3Items.Visibility = Visibility.Collapsed;

#endif
		}

		#region User Control Events

		// A WPF Popup is a separate top-level window and does not follow its placement target when the
		// main window moves or resizes. Nudging the offset forces WPF to re-evaluate the placement so the
		// menu card stays anchored beneath the hamburger button. Called from MainWindow's move/size events.
		public void ReanchorIfOpen()
		{
			if ( MenuPopup.IsOpen )
			{
				var horizontalOffset = MenuPopup.HorizontalOffset;

				MenuPopup.HorizontalOffset = horizontalOffset + 1;
				MenuPopup.HorizontalOffset = horizontalOffset;

				// The window may have been moved to a shorter monitor while the menu was open, so the
				// amount of vertical space below the card can change. Recompute the scroll cap to match.
				UpdateMenuMaxHeight();
			}
		}

		private void MenuPopup_Opened( object sender, EventArgs e )
		{
			// Wait until the popup has been laid out and placed on screen, otherwise PointToScreen below
			// returns a stale position. Loaded priority runs after layout/render for this open.
			Dispatcher.BeginInvoke( DispatcherPriority.Loaded, new Action( UpdateMenuMaxHeight ) );
		}

		// On a small / low-resolution monitor the menu card can be taller than the screen, which used to
		// clip the bottom menu items (and the helmet artwork) off the visible area. The card content lives
		// in a ScrollViewer whose vertical scrollbar is set to Auto, but it never engaged because nothing
		// capped its height. Here we cap MenuScrollViewer.MaxHeight to the space remaining between the
		// ScrollViewer's top and the bottom of the work area of the monitor it is on, so the card always
		// fits and the scrollbar appears whenever the items don't.
		private void UpdateMenuMaxHeight()
		{
			if ( !MenuPopup.IsOpen )
			{
				return;
			}

			// PointToScreen returns physical pixels and already reflects both the inherited AppUIScale
			// (the popup content is scaled by the placement target's ancestor transform) and the monitor
			// DPI, so the on-screen top is measured directly without any manual scaling.
			var scrollViewerTop = MenuScrollViewer.PointToScreen( new System.Windows.Point( 0, 0 ) );

			var screen = System.Windows.Forms.Screen.FromPoint( new System.Drawing.Point( (int) scrollViewerTop.X, (int) scrollViewerTop.Y ) );

			var availablePhysical = screen.WorkingArea.Bottom - scrollViewerTop.Y;

			// Convert the available physical height back into the ScrollViewer's own (unscaled) coordinate
			// space: divide out the monitor DPI and the global AppUIScale, both of which inflate a local
			// device-independent unit on its way to a screen pixel.
			var dpiScaleY = VisualTreeHelper.GetDpi( MenuScrollViewer ).DpiScaleY;
			var appUIScale = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.AppUIScale;
			var effectiveScale = dpiScaleY * appUIScale;

			if ( effectiveScale <= 0 )
			{
				return;
			}

			// Vertical chrome that sits below the ScrollViewer inside the card (in local units): the
			// ScrollViewer bottom margin (10) + card padding bottom (20) + card border (2) + card margin
			// to the screen edge (20). Subtracting it keeps that chrome on screen too.
			const double chromeBelowScrollViewer = 52;

			var maxHeight = ( availablePhysical / effectiveScale ) - chromeBelowScrollViewer;

			// Never collapse the menu to nothing if the math goes sideways on an unusually tiny screen.
			MenuScrollViewer.MaxHeight = Math.Max( 200, maxHeight );
		}

		private void Scrim_MouseDown( object sender, MouseButtonEventArgs e )
		{
			IsMenuOpen = false;
		}

		private void MenuItem_PreviewMouseLeftButtonUp( object sender, MouseButtonEventArgs e )
		{
			if ( ( sender is FrameworkElement frameworkElement ) && ( frameworkElement.DataContext is AppMenuItem appMenuItem ) )
			{
				e.Handled = true;

				// Re-clicking the current page just closes the menu; otherwise navigate (which also
				// closes the menu via the page-change handler below).
				if ( SelectedAppPage != appMenuItem.AppPage )
				{
					SelectedAppPage = appMenuItem.AppPage;
				}

				IsMenuOpen = false;
			}
		}

		#endregion

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

				case AppPage.ControllerProfiles:
					return "advanced/controller-profiles/";

				case AppPage.Wind:
					return "advanced/wind/";

				case AppPage.GTensioner:
					return "advanced/seat-belt-tensioner/";

				case AppPage.Overlays:
					return "advanced/overlays/";

				case AppPage.Sounds:
					return "advanced/sounds/";

				case AppPage.Commentary:
					return "advanced/commentary/";

				case AppPage.SpeechToText:
					return "advanced/captions/";

				case AppPage.TradingPaints:
					return "advanced/trading-paints/";

				case AppPage.Graph:
					return "advanced/graph/";

				case AppPage.Simulator:
					return "advanced/simulator/";

				case AppPage.AppManager:
					return "advanced/app-manager/";

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

		public static readonly DependencyProperty IsMenuOpenProperty = DependencyProperty.Register( nameof( IsMenuOpen ), typeof( bool ), typeof( MairaAppMenuPopup ), new FrameworkPropertyMetadata( false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault ) );

		public bool IsMenuOpen
		{
			get => (bool) GetValue( IsMenuOpenProperty );
			set => SetValue( IsMenuOpenProperty, value );
		}

		// The element the menu card anchors beneath (the hamburger button). MainWindow sets this so the
		// Popup can place its top-left corner under the button instead of centering over the window.
		public static readonly DependencyProperty PlacementTargetProperty = DependencyProperty.Register( nameof( PlacementTarget ), typeof( UIElement ), typeof( MairaAppMenuPopup ), new PropertyMetadata( null ) );

		public UIElement? PlacementTarget
		{
			get => (UIElement?) GetValue( PlacementTargetProperty );
			set => SetValue( PlacementTargetProperty, value );
		}

		#endregion

		#region Dependency Property Changed Events

		private static void OnSelectedAppPageChanged( DependencyObject d, DependencyPropertyChangedEventArgs e )
		{
			if ( d is MairaAppMenuPopup mairaAppMenuPopup )
			{
				var appPage = (AppPage) e.NewValue;

				AppMenuItem? match = null;

				foreach ( var appMenuItem in mairaAppMenuPopup.AllMenuItems )
				{
					var isSelected = appMenuItem.AppPage == appPage;

					appMenuItem.IsSelected = isSelected;

					if ( isSelected )
					{
						match = appMenuItem;
					}
				}

				if ( match != null )
				{
					mairaAppMenuPopup.SelectedAppPageUserControl = match.PageUserControl;
				}

				CurrentAppPage = appPage;

				mairaAppMenuPopup.UpdateSelectedAppPageText();
			}
		}

		#endregion

		#region Logic

		private static void Add( ObservableCollection<AppMenuItem> column, AppPage appPage, UserControl pageUserControl )
		{
			column.Add( new AppMenuItem
			{
				AppPage = appPage,
				PageUserControl = pageUserControl
			} );
		}

		public void Initialize()
		{
			// ----- Column 1 -----

#if !ADMINBOXX

			Add( AppMenuItemsColumn1, AppPage.AppManager, _appManagerPage );
			Add( AppMenuItemsColumn1, AppPage.RacingWheel, _racingWheelPage );
			Add( AppMenuItemsColumn1, AppPage.SteeringEffects, _steeringEffectsPage );
			Add( AppMenuItemsColumn1, AppPage.Pedals, _pedalsPage );
			Add( AppMenuItemsColumn1, AppPage.Wind, _windPage );
			Add( AppMenuItemsColumn1, AppPage.GTensioner, _gTensionerPage );

#endif

			// Remember the slot AdminBoxx belongs in (right after G Tensioner in the full build);
			// RelocalizeAppMenuItems re-inserts it here when the language is English. See field comment.
			_adminBoxxColumn1Index = AppMenuItemsColumn1.Count;
			_adminBoxxMenuItem = new AppMenuItem { AppPage = AppPage.AdminBoxx, PageUserControl = _adminBoxxPage };
			AppMenuItemsColumn1.Add( _adminBoxxMenuItem );

#if !ADMINBOXX

			Add( AppMenuItemsColumn1, AppPage.SpeechToText, _speechToTextPage );
			Add( AppMenuItemsColumn1, AppPage.Commentary, _commentary );
			Add( AppMenuItemsColumn1, AppPage.Sounds, _soundsPage );
			Add( AppMenuItemsColumn1, AppPage.Overlays, _overlaysPage );
			Add( AppMenuItemsColumn1, AppPage.TradingPaints, _tradingPaintsPage );

#endif

			// ----- Column 2 -----

#if ADMINBOXX

			// AdminBoxx uses a single column, so keep App Settings beneath AdminBoxx in column 1.
			Add( AppMenuItemsColumn1, AppPage.AppSettings, _appSettingsPage );

#else

			Add( AppMenuItemsColumn2, AppPage.AppSettings, _appSettingsPage );
			Add( AppMenuItemsColumn2, AppPage.ControllerProfiles, _controllerProfilesPage );
			Add( AppMenuItemsColumn2, AppPage.Simulator, _simulatorPage );
			Add( AppMenuItemsColumn2, AppPage.Graph, _graphPage );

#endif

			// ----- Column 3 -----

#if !ADMINBOXX

			Add( AppMenuItemsColumn3, AppPage.Help, _helpPage );
			Add( AppMenuItemsColumn3, AppPage.Donate, _donatePage );
			Add( AppMenuItemsColumn3, AppPage.Contribute, _contributePage );

#endif

#if DEBUG

#if ADMINBOXX

			// AdminBoxx uses a single column, so keep Debug beneath App Settings in column 1.
			Add( AppMenuItemsColumn1, AppPage.Debug, _debugPage );

#else

			Add( AppMenuItemsColumn3, AppPage.Debug, _debugPage );

#endif

#endif

			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

			var defaultMenuItem = AllMenuItems.FirstOrDefault( appMenuItem => appMenuItem.AppPage == settings.AppDefaultPage ) ?? AllMenuItems.FirstOrDefault();

			if ( defaultMenuItem != null )
			{
				foreach ( var appMenuItem in AllMenuItems )
				{
					appMenuItem.IsSelected = appMenuItem == defaultMenuItem;
				}

				SelectedAppPage = defaultMenuItem.AppPage;
				SelectedAppPageUserControl = defaultMenuItem.PageUserControl;

				CurrentAppPage = defaultMenuItem.AppPage;
			}

			UpdateSelectedAppPageText();
		}

		public void RelocalizeAppMenuItems()
		{
			var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;
			var isEnglish = settings.AppCurrentLanguageCode == "default";

			foreach ( var menuItem in AllMenuItems )
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

					case AppPage.ControllerProfiles:
						menuItem.DisplayName = localization[ "ControllerProfiles" ];
						break;

					case AppPage.Wind:
						menuItem.DisplayName = localization[ "Wind" ];
						break;

					case AppPage.GTensioner:
						menuItem.DisplayName = localization[ "GTensioner" ];
						break;

					case AppPage.Overlays:
						menuItem.DisplayName = localization[ "Overlays" ];
						break;

					case AppPage.Sounds:
						menuItem.DisplayName = localization[ "Sounds" ];
						break;

					case AppPage.Commentary:
						menuItem.DisplayName = localization[ "Commentary" ];
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

					case AppPage.AppManager:
						menuItem.DisplayName = localization[ "AppManager" ];
						break;

					case AppPage.AppSettings:
						menuItem.DisplayName = localization[ "AppSettings" ];
						break;

					case AppPage.Contribute:
						menuItem.DisplayName = localization[ "Contribute" ];
						break;

					case AppPage.Donate:
						menuItem.DisplayName = localization[ "Donate" ];
						menuItem.Suffix = " ❤️";
						break;

					case AppPage.Debug:
						menuItem.DisplayName = "Debug";
						break;
				}
			}

			// AdminBoxx is English-only. Add/remove it from the column instead of collapsing it, so the
			// row is truly gone under right-to-left languages rather than leaving a blank line.
			if ( _adminBoxxMenuItem != null )
			{
				_adminBoxxMenuItem.DisplayName = localization[ "AdminBoxx" ];

				var adminBoxxPresent = AppMenuItemsColumn1.Contains( _adminBoxxMenuItem );

				if ( isEnglish && !adminBoxxPresent )
				{
					AppMenuItemsColumn1.Insert( Math.Min( _adminBoxxColumn1Index, AppMenuItemsColumn1.Count ), _adminBoxxMenuItem );
				}
				else if ( !isEnglish && adminBoxxPresent )
				{
					AppMenuItemsColumn1.Remove( _adminBoxxMenuItem );
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
					SelectedAppPageText = localization.Upper[ "Help" ];
					break;

				case AppPage.RacingWheel:
					SelectedAppPageText = localization.Upper[ "RacingWheel" ];
					break;

				case AppPage.SteeringEffects:
					SelectedAppPageText = localization.Upper[ "SteeringEffects" ];
					break;

				case AppPage.Pedals:
					SelectedAppPageText = localization.Upper[ "Pedals" ];
					break;

				case AppPage.ControllerProfiles:
					SelectedAppPageText = localization.Upper[ "ControllerProfiles" ];
					break;

				case AppPage.Wind:
					SelectedAppPageText = localization.Upper[ "Wind" ];
					break;

				case AppPage.GTensioner:
					SelectedAppPageText = localization.Upper[ "GTensioner" ];
					break;

				case AppPage.AdminBoxx:
					SelectedAppPageText = localization.Upper[ "AdminBoxx" ];
					break;

				case AppPage.Overlays:
					SelectedAppPageText = localization.Upper[ "Overlays" ];
					break;

				case AppPage.Sounds:
					SelectedAppPageText = localization.Upper[ "Sounds" ];
					break;

				case AppPage.Commentary:
					SelectedAppPageText = localization.Upper[ "Commentary" ];
					break;

				case AppPage.SpeechToText:
					SelectedAppPageText = localization.Upper[ "SpeechToText" ];
					break;

				case AppPage.TradingPaints:
					SelectedAppPageText = localization.Upper[ "TradingPaints" ];
					break;

				case AppPage.Graph:
					SelectedAppPageText = localization.Upper[ "Graph" ];
					break;

				case AppPage.Simulator:
					SelectedAppPageText = localization.Upper[ "Simulator" ];
					break;

				case AppPage.AppManager:
					SelectedAppPageText = localization.Upper[ "AppManager" ];
					break;

				case AppPage.AppSettings:
					SelectedAppPageText = localization.Upper[ "AppSettings" ];
					break;

				case AppPage.Contribute:
					SelectedAppPageText = localization.Upper[ "Contribute" ];
					break;

				case AppPage.Donate:
					SelectedAppPageText = localization.Upper[ "Donate" ];
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
