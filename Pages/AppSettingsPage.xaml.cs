
using System.Windows;

using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Windows;
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Pages;

public partial class AppSettingsPage : UserControl
{
	private List<CpuAffinityHelper.PhysicalCoreOption> _physicalCoreOptions = [];
	private List<Controls.MairaSwitch> _cpuAffinitySwitches = [];
	private bool _isUpdatingCpuAffinityUI = false;

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

		InitializeCpuAffinityUI();
		UpdateCpuAffinityUI();
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
			{ MainWindow.AppPage.SeatBeltTensioner, localization[ "SeatBeltTensioner" ] },
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

	private void InitializeCpuAffinityUI()
	{
		var app = App.Instance!;

		try
		{
			app.Logger.WriteLine( "[AppSettingsPage] InitializeCpuAffinityUI >>>" );

			_physicalCoreOptions = CpuAffinityHelper.GetPhysicalCoreOptions().ToList();

			CpuAffinityPanel.Children.Clear();
			_cpuAffinitySwitches.Clear();

			foreach ( var coreOption in _physicalCoreOptions )
			{
				var cpuSwitch = new Controls.MairaSwitch
				{
					Label = coreOption.Label,
					Margin = new Thickness( 0, 0, 0, 10 )
				};

				cpuSwitch.Toggled += CpuAffinitySwitch_Toggled;

				CpuAffinityPanel.Children.Add( cpuSwitch );
				_cpuAffinitySwitches.Add( cpuSwitch );
			}

			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

			settings.PropertyChanged += Settings_PropertyChanged;

			app.Logger.WriteLine( "[AppSettingsPage] <<< InitializeCpuAffinityUI" );
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[AppSettingsPage] Error initializing CPU affinity UI: {ex.Message}" );
		}
	}

	private void Settings_PropertyChanged( object? sender, System.ComponentModel.PropertyChangedEventArgs e )
	{
		if ( e.PropertyName == nameof( MarvinsAIRARefactored.DataContext.Settings.AppAffinityMaskBits ) )
		{
			UpdateCpuAffinityUI();
		}
	}

	private void UpdateCpuAffinityUI()
	{
		if ( _isUpdatingCpuAffinityUI )
		{
			return;
		}

		_isUpdatingCpuAffinityUI = true;

		try
		{
			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;
			var currentAffinityMask = settings.AppAffinityMaskBits;

			for ( var i = 0; i < _cpuAffinitySwitches.Count && i < _physicalCoreOptions.Count; i++ )
			{
				var cpuSwitch = _cpuAffinitySwitches[ i ];
				var coreOption = _physicalCoreOptions[ i ];

				cpuSwitch.IsOn = ( currentAffinityMask & coreOption.AffinityMaskBits ) == coreOption.AffinityMaskBits;
			}
		}
		finally
		{
			_isUpdatingCpuAffinityUI = false;
		}
	}

	private void CpuAffinitySwitch_Toggled( object? sender, EventArgs e )
	{
		if ( _isUpdatingCpuAffinityUI )
		{
			return;
		}

		try
		{
			var selectedIndexes = new List<int>();

			for ( var i = 0; i < _cpuAffinitySwitches.Count && i < _physicalCoreOptions.Count; i++ )
			{
				if ( _cpuAffinitySwitches[ i ].IsOn )
				{
					selectedIndexes.Add( i );
				}
			}

			var newAffinityMask = CpuAffinityHelper.BuildAffinityMask( selectedIndexes, _physicalCoreOptions );

			var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

			settings.AppAffinityMaskBits = newAffinityMask;
		}
		catch ( Exception ex )
		{
			var app = App.Instance!;

			app.Logger.WriteLine( $"[AppSettingsPage] Error updating CPU affinity: {ex.Message}" );
		}
	}

	#endregion
}
