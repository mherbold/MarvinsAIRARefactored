
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Windows;
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Pages;

public partial class AppSettingsPage : UserControl
{
	public AppSettingsPage()
	{
		InitializeComponent();

		Loaded += OnLoaded;
	}

	private void OnLoaded( object? sender, RoutedEventArgs e )
	{
#if ADMINBOXX

		Misc.ApplyToTaggedElements( Root, "HideIfAdminBoxx", element => element.Visibility = Visibility.Collapsed );

#endif
	}

	#region User Control Events

	private async void CheckNow_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		await app.CloudService.CheckForUpdates( true );
	}

	#endregion

	#region Logic

	public void UpdateLanguageOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AppSettingsPage] UpdateLanguageOptions >>>" );

		Language_MairaComboBox.ItemsSource = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization.Languages.ToList();

		app.Logger.WriteLine( "[AppSettingsPage] <<< UpdateLanguageOptions" );
	}

	public void UpdateDefaultPageOptions()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AppSettingsPage] UpdateDefaultPageOptions >>>" );

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var defaultPageOptions = new Dictionary<MainWindow.AppPage, string>
		{
			{ MainWindow.AppPage.RacingWheel, localization[ "RacingWheel" ] },
			{ MainWindow.AppPage.SteeringEffects, localization[ "SteeringEffects" ] },
			{ MainWindow.AppPage.Pedals, localization[ "Pedals" ] },
			{ MainWindow.AppPage.Wind, localization[ "Wind" ] },
			{ MainWindow.AppPage.Sounds, localization[ "Sounds" ] },
			{ MainWindow.AppPage.SpeechToText, localization[ "SpeechToText" ] },
			{ MainWindow.AppPage.TradingPaints, localization[ "TradingPaints" ] },
			{ MainWindow.AppPage.Graph, localization[ "Graph" ] },
			{ MainWindow.AppPage.Simulator, localization[ "Simulator" ] },
			{ MainWindow.AppPage.AppSettings, localization[ "AppSettings" ] },
			{ MainWindow.AppPage.Contribute, localization[ "Contribute" ] },
			{ MainWindow.AppPage.Donate, localization[ "Donate" ] }
		};

		DefaultPage_MairaComboBox.ItemsSource = defaultPageOptions.ToList();
		DefaultPage_MairaComboBox.SelectedValue = settings.AppDefaultPage;

		app.Logger.WriteLine( "[AppSettingsPage] <<< UpdateDefaultPageOptions" );
	}

	#endregion
}
