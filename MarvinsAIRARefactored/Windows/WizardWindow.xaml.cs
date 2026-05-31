
using System.Windows;
using System.Windows.Controls;

namespace MarvinsAIRARefactored.Windows;

public partial class WizardWindow : Window
{
	private int _currentStep = 0;

	// Ordered list of intermediate step panels (between welcome and final)
	private readonly StackPanel[] _stepPanels;

	public WizardWindow()
	{
		var app = App.Instance!;

		app.MainWindow.MakeWindowVisible();

		InitializeComponent();

		_stepPanels = [ Step1_Panel, Step2_Panel, Step3_Panel, Step4_Panel, Step5_Panel, Step6_Panel, Step7_Panel ];

		UpdateSteeringDeviceOptions();
	}

	/// <summary>
	/// Attempts to set RacingWheelWheelForce from a known wheelbase lookup table.
	/// The guess is applied only when the setting is still at the default value of 5 Nm.
	/// </summary>
	public void ApplyWheelForceGuess()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		// Only auto-seed when the user has not yet changed the value from its default.
		const float defaultWheelForce = 5f;

		if ( settings.RacingWheelWheelForce != defaultWheelForce )
		{
			return;
		}

		var app = App.Instance!;

		if ( !app.DirectInput.ForceFeedbackDeviceList.TryGetValue( settings.RacingWheelSteeringDeviceGuid, out var deviceName ) )
		{
			return;
		}

		var name = deviceName.ToUpperInvariant();

		float? guess = name switch
		{
			// Fanatec
			var n when n.Contains( "PODIUM DD1" ) => 20f,
			var n when n.Contains( "PODIUM DD2" ) => 25f,
			var n when n.Contains( "DD1" ) => 20f,
			var n when n.Contains( "DD2" ) => 25f,
			var n when n.Contains( "CLUBSPORT DD+" ) => 15f,
			var n when n.Contains( "CLUBSPORT DD" ) || n.Contains( "CLUBSPORT WHEEL BASE DD" ) => 12f,
			var n when n.Contains( "GT DD PRO" ) => 8f,
			var n when n.Contains( "CSL DD" ) => 8f,
			var n when n.Contains( "CSL ELITE" ) => 6f,
			var n when n.Contains( "FANATEC" ) || n.Contains( "CSL" ) => 6f,

			// Moza
			var n when n.Contains( "MOZA R21" ) => 21f,
			var n when n.Contains( "MOZA R16" ) || n.Contains( "MOZA R-16" ) => 16f,
			var n when n.Contains( "MOZA R12" ) || n.Contains( "MOZA R-12" ) => 12f,
			var n when n.Contains( "MOZA R9" ) || n.Contains( "MOZA R-9" ) => 9f,
			var n when n.Contains( "MOZA R5" ) || n.Contains( "MOZA R-5" ) => 5.5f,
			var n when n.Contains( "MOZA R3" ) || n.Contains( "MOZA R-3" ) => 3.9f,
			var n when n.Contains( "MOZA" ) => 9f,

			// Simagic (most specific first)
			var n when n.Contains( "SIMAGIC ALPHA EVO ULTRA" ) => 28f,
			var n when n.Contains( "SIMAGIC ALPHA EVO PRO" ) => 18f,
			var n when n.Contains( "SIMAGIC ALPHA EVO SPORT" ) => 9f,
			var n when n.Contains( "SIMAGIC ALPHA EVO" ) => 12f,
			var n when n.Contains( "SIMAGIC ALPHA ULTIMATE" ) => 23f,
			var n when n.Contains( "SIMAGIC ALPHA MINI" ) => 10f,
			var n when n.Contains( "SIMAGIC ALPHA" ) => 15f,
			var n when n.Contains( "SIMAGIC M10" ) => 10f,
			var n when n.Contains( "SIMAGIC" ) => 10f,

			// Simucube
			var n when n.Contains( "SIMUCUBE 2 PRO" ) || n.Contains( "SC2 PRO" ) => 25f,
			var n when n.Contains( "SIMUCUBE 2 SPORT" ) || n.Contains( "SC2 SPORT" ) => 17f,
			var n when n.Contains( "SIMUCUBE 2 ULTIMATE" ) || n.Contains( "SC2 ULTIMATE" ) => 32f,
			var n when n.Contains( "SIMUCUBE" ) => 25f,

			// Logitech
			var n when n.Contains( "G PRO" ) && n.Contains( "LOGITECH" ) => 11f,
			var n when n.Contains( "G923" ) || n.Contains( "G29" ) || n.Contains( "G920" ) || n.Contains( "G27" ) => 2.2f,
			var n when n.Contains( "LOGITECH" ) => 2.2f,

			// Thrustmaster (most specific first)
			var n when n.Contains( "T818" ) => 10f,
			var n when n.Contains( "T-GT2" ) || n.Contains( "TGT2" ) => 6f,
			var n when n.Contains( "T-GT" ) || n.Contains( "TGT" ) => 6f,
			var n when n.Contains( "TS-XW" ) || n.Contains( "TSXW" ) => 6.4f,
			var n when n.Contains( "TS-PC" ) || n.Contains( "TSPC" ) => 6f,
			var n when n.Contains( "T500" ) => 4.4f,
			var n when n.Contains( "T300" ) || n.Contains( "TX" ) => 3.9f,
			var n when n.Contains( "T248" ) => 3.5f,
			var n when n.Contains( "T150" ) || n.Contains( "T128" ) => 2f,
			var n when n.Contains( "THRUSTMASTER" ) => 3.5f,

			// VRS
			var n when n.Contains( "VRS DIRECTFORCE PRO 20" ) || n.Contains( "VRS DFP20" ) => 20f,
			var n when n.Contains( "VRS DIRECTFORCE PRO 15" ) || n.Contains( "VRS DFP15" ) => 15f,
			var n when n.Contains( "VRS DIRECTFORCE PRO" ) || n.Contains( "VRS DFP" ) => 15f,
			var n when n.Contains( "VRS" ) => 15f,

			_ => null
		};

		if ( guess.HasValue )
		{
			settings.RacingWheelWheelForce = guess.Value;
		}
	}

	public void UpdateSteeringDeviceOptions()
	{
		var app = App.Instance!;
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<Guid, string>();

		if ( app.DirectInput.ForceFeedbackDeviceList.Count == 0 )
		{
			dictionary.Add( Guid.Empty, localization[ "NoFFBDevicesFound" ] );
		}
		else
		{
			dictionary.Add( Guid.Empty, localization[ "FFBDeviceNotSelected" ] );
		}

		app.DirectInput.ForceFeedbackDeviceList.ToList().ForEach( kv => dictionary[ kv.Key ] = kv.Value );

		if ( !dictionary.ContainsKey( settings.RacingWheelSteeringDeviceGuid ) )
		{
			dictionary.Add( settings.RacingWheelSteeringDeviceGuid, $"{localization[ "DeviceNotFound" ]} [{settings.RacingWheelSteeringDeviceGuid}]" );
		}

		app.Dispatcher.Invoke( () =>
		{
			WizardSteeringDevice_MairaComboBox.ItemsSource = dictionary.OrderBy( kv => kv.Value ).ToList();
			WizardSteeringDevice_MairaComboBox.OffValue = Guid.Empty;
		} );
	}

	private void ShowStep( int step )
	{
		_currentStep = step;

		Step0_Panel.Visibility = Visibility.Collapsed;
		StepFinal_Panel.Visibility = Visibility.Collapsed;

		foreach ( var panel in _stepPanels )
		{
			panel.Visibility = Visibility.Collapsed;
		}

		var isFinalStep = step > _stepPanels.Length;

		WizardImage_Normal.Visibility = isFinalStep ? Visibility.Collapsed : Visibility.Visible;
		WizardImage_ThumbsUp.Visibility = isFinalStep ? Visibility.Visible : Visibility.Collapsed;

		if ( step == 0 )
		{
			Step0_Panel.Visibility = Visibility.Visible;
		}
		else if ( isFinalStep )
		{
			StepFinal_Panel.Visibility = Visibility.Visible;
		}
		else
		{
			_stepPanels[ step - 1 ].Visibility = Visibility.Visible;
		}
	}

	private void UseWizard_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		ShowStep( 1 );
	}

	private void SkipWizard_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		ShowStep( _stepPanels.Length + 1 );
	}

	private void Next_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var nextStep = _currentStep + 1;

		// When advancing to the wheel-force step, try to seed a guess from the selected device.
		if ( nextStep == Array.IndexOf( _stepPanels, Step3_Panel ) + 1 )
		{
			ApplyWheelForceGuess();
		}

		if ( nextStep > _stepPanels.Length )
		{
			ShowStep( _stepPanels.Length + 1 );
		}
		else
		{
			ShowStep( nextStep );
		}
	}

	private void Prev_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		ShowStep( _currentStep - 1 );
	}

	private void Step5Yes_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		// User wants Auto-Set — advance normally to Step 6.
		ShowStep( Array.IndexOf( _stepPanels, Step6_Panel ) + 1 );
	}

	private void Step5No_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		// User skipped Auto-Set — jump to the FFB style step.
		ShowStep( Array.IndexOf( _stepPanels, Step7_Panel ) + 1 );
	}

	private void WizardSet_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var updateButtonMappingsWindow = new UpdateButtonMappingsWindow( settings.RacingWheelSetButtonMappings )
		{
			Owner = app.MainWindow
		};

		updateButtonMappingsWindow.ShowDialog();

		WizardSet_MairaMappableButton.IsMapped = settings.RacingWheelSetButtonMappings.MappedButtons.Count > 0
			&& settings.RacingWheelSetButtonMappings.MappedButtons.Any( b => b.ClickButton.DeviceInstanceGuid != Guid.Empty );
	}

	private void WizardClear_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var updateButtonMappingsWindow = new UpdateButtonMappingsWindow( settings.RacingWheelClearButtonMappings )
		{
			Owner = app.MainWindow
		};

		updateButtonMappingsWindow.ShowDialog();

		WizardClear_MairaMappableButton.IsMapped = settings.RacingWheelClearButtonMappings.MappedButtons.Count > 0
			&& settings.RacingWheelClearButtonMappings.MappedButtons.Any( b => b.ClickButton.DeviceInstanceGuid != Guid.Empty );
	}

	private void StrengthMinus_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var updateButtonMappingsWindow = new UpdateButtonMappingsWindow( settings.RacingWheelStrengthMinusButtonMappings )
		{
			Owner = app.MainWindow
		};

		updateButtonMappingsWindow.ShowDialog();

		StrengthMinus_MairaMappableButton.IsMapped = settings.RacingWheelStrengthMinusButtonMappings.MappedButtons.Count > 0
			&& settings.RacingWheelStrengthMinusButtonMappings.MappedButtons.Any( b => b.ClickButton.DeviceInstanceGuid != Guid.Empty );
	}

	private void StrengthPlus_MairaMappableButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var updateButtonMappingsWindow = new UpdateButtonMappingsWindow( settings.RacingWheelStrengthPlusButtonMappings )
		{
			Owner = app.MainWindow
		};

		updateButtonMappingsWindow.ShowDialog();

		StrengthPlus_MairaMappableButton.IsMapped = settings.RacingWheelStrengthPlusButtonMappings.MappedButtons.Count > 0
			&& settings.RacingWheelStrengthPlusButtonMappings.MappedButtons.Any( b => b.ClickButton.DeviceInstanceGuid != Guid.Empty );
	}

	private void Step7Apply_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		ApplyFfbStylePreset( (int) FfbStyle_Slider.Value );
		Next_MairaButton_Click( sender, e );
	}

	private void FfbStyle_Slider_ValueChanged( object sender, RoutedPropertyChangedEventArgs<double> e )
	{
		if ( FfbStyleName_TextBlock is null )
		{
			return;
		}

		var loc = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		(FfbStyleName_TextBlock.Text, FfbStyleDescription_TextBlock.Text) = (int) FfbStyle_Slider.Value switch
		{
			-3 => (loc[ "Wizard_Step7_Preset_MaxSmooth_Name" ], loc[ "Wizard_Step7_Preset_MaxSmooth_Desc" ]),
			-2 => (loc[ "Wizard_Step7_Preset_Smooth_Name" ], loc[ "Wizard_Step7_Preset_Smooth_Desc" ]),
			-1 => (loc[ "Wizard_Step7_Preset_SlightlySmooth_Name" ], loc[ "Wizard_Step7_Preset_SlightlySmooth_Desc" ]),
			0 => (loc[ "Wizard_Step7_Preset_Normal_Name" ], loc[ "Wizard_Step7_Preset_Normal_Desc" ]),
			1 => (loc[ "Wizard_Step7_Preset_SlightlyBoosted_Name" ], loc[ "Wizard_Step7_Preset_SlightlyBoosted_Desc" ]),
			2 => (loc[ "Wizard_Step7_Preset_Boosted_Name" ], loc[ "Wizard_Step7_Preset_Boosted_Desc" ]),
			3 => (loc[ "Wizard_Step7_Preset_MaxBoosted_Name" ], loc[ "Wizard_Step7_Preset_MaxBoosted_Desc" ]),
			_ => (string.Empty, string.Empty)
		};
	}

	/// <summary>
	/// Applies algorithm, detail, and prediction settings for the chosen Algorithm position (-3 to +3).
	/// </summary>
	private static void ApplyFfbStylePreset( int position )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		// Common settings for all positions.
		settings.RacingWheelEnableSoftLimiter = true;
		settings.RacingWheelDetailBoostBias = 0.10f;
		settings.RacingWheelDeltaLimiterBias = 0.20f;
		settings.RacingWheelPredictionMode = Components.RacingWheel.PredictionMode.PredictK1;
		settings.RacingWheelPredictionBlend = 0.30f;

		if ( position <= -1 )
		{
			// Detail Limiter (low latency) branch.
			settings.RacingWheelAlgorithm = Components.RacingWheel.Algorithm.DeltaLimiterOn60Hz;

			settings.RacingWheelDeltaLimit = position switch
			{
				-3 => 250f,
				-2 => 500f,
				_ => 1000f   // -1
			};
		}
		else
		{
			// Detail Booster (low latency) branch.
			settings.RacingWheelAlgorithm = Components.RacingWheel.Algorithm.DetailBoosterOn60Hz;

			settings.RacingWheelDetailBoost = position switch
			{
				1 => 0.50f,
				2 => 1.00f,
				3 => 1.50f,
				_ => 0.00f    // 0
			};
		}
	}

	private void DocumentationLink_Click( object sender, RoutedEventArgs e )
	{
		Classes.HelpService.ExecuteOpenHelp( "basic/common-controls/" );
	}

	private void Close_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		settings.AppWizardHasRun = true;

		Close();
	}
}
