
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.Windows;

using Settings = MarvinsAIRARefactored.DataContext.Settings;
using Localization = MarvinsAIRARefactored.Components.Localization;
using UserControl = System.Windows.Controls.UserControl;

namespace MarvinsAIRARefactored.Pages;

public partial class ControllerProfilesPage : UserControl
{
	private bool _refreshingProfileSelector = false;

	private readonly Dictionary<MappableAction, TextBlock> _summaryTextBlocks = [];

	// Step-size editors grouped by knob key, so editing the + row's box keeps the - row's box in sync
	// (both rows of a knob share one universal step size).
	private readonly Dictionary<string, List<MairaTextBox>> _stepSizeEditors = [];
	private bool _syncingStepSizeEditors = false;

	public ControllerProfilesPage()
	{
		InitializeComponent();

		Loaded += ControllerProfilesPage_Loaded;
	}

	private static Settings Settings => MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;
	private static Localization Localization => MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

	#region User Control Events

	private void ControllerProfilesPage_Loaded( object sender, RoutedEventArgs e )
	{
		RefreshAll();
	}

	private void Profile_MairaComboBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( _refreshingProfileSelector )
		{
			return;
		}

		if ( Profile_MairaComboBox.SelectedValue is not string selectedProfileName )
		{
			return;
		}

		var settings = Settings;

		if ( selectedProfileName == settings.CurrentControllerProfileName )
		{
			return;
		}

		var app = App.Instance!;

		app.Logger.WriteLine( $"[ControllerProfilesPage] Switching to controller profile '{selectedProfileName}'" );

		settings.SelectControllerProfile( selectedProfileName );

		app.RebuildButtonMappingIndex();

		MairaMappableButton.RefreshAll();
		MairaMappableSwitch.RefreshAll();

		app.SettingsFile.QueueForSerialization = true;

		RebuildMappingList();
	}

	private void NewProfile_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var newControllerProfileWindow = new NewControllerProfileWindow
		{
			Owner = app.MainWindow
		};

		newControllerProfileWindow.ShowDialog();

		if ( !newControllerProfileWindow.Confirmed )
		{
			return;
		}

		var profileName = newControllerProfileWindow.ProfileName.Trim();

		if ( profileName == string.Empty )
		{
			return;
		}

		app.Logger.WriteLine( $"[ControllerProfilesPage] Creating controller profile '{profileName}' (copyFromCurrent = {newControllerProfileWindow.CopyFromCurrent})" );

		Settings.CreateControllerProfile( profileName, newControllerProfileWindow.CopyFromCurrent );

		app.RebuildButtonMappingIndex();

		MairaMappableButton.RefreshAll();
		MairaMappableSwitch.RefreshAll();

		app.SettingsFile.QueueForSerialization = true;

		RefreshAll();
	}

	private void RenameProfile_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var settings = Settings;

		var currentProfileName = settings.CurrentControllerProfileName;

		var app = App.Instance!;

		var renameControllerProfileWindow = new RenameControllerProfileWindow( currentProfileName )
		{
			Owner = app.MainWindow
		};

		renameControllerProfileWindow.ShowDialog();

		if ( !renameControllerProfileWindow.Confirmed )
		{
			return;
		}

		var newProfileName = renameControllerProfileWindow.ProfileName.Trim();

		// Ignore empty names, no-op renames, and collisions with an existing profile.
		if ( ( newProfileName == string.Empty ) || ( newProfileName == currentProfileName ) || settings.ControllerProfiles.ContainsKey( newProfileName ) )
		{
			return;
		}

		app.Logger.WriteLine( $"[ControllerProfilesPage] Renaming controller profile '{currentProfileName}' to '{newProfileName}'" );

		settings.RenameControllerProfile( currentProfileName, newProfileName );

		app.SettingsFile.QueueForSerialization = true;

		RefreshAll();
	}

	private void DeleteProfile_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var settings = Settings;

		// Always keep at least one profile.
		if ( settings.ControllerProfiles.Count <= 1 )
		{
			return;
		}

		var app = App.Instance!;

		var deleteControllerProfileWindow = new DeleteControllerProfileWindow( settings.CurrentControllerProfileName )
		{
			Owner = app.MainWindow
		};

		deleteControllerProfileWindow.ShowDialog();

		if ( !deleteControllerProfileWindow.Confirmed )
		{
			return;
		}

		app.Logger.WriteLine( $"[ControllerProfilesPage] Deleting controller profile '{settings.CurrentControllerProfileName}'" );

		settings.DeleteControllerProfile( settings.CurrentControllerProfileName );

		app.RebuildButtonMappingIndex();

		MairaMappableButton.RefreshAll();
		MairaMappableSwitch.RefreshAll();

		app.SettingsFile.QueueForSerialization = true;

		RefreshAll();
	}

	private void Map_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( ( sender is not MairaButton mairaButton ) || ( mairaButton.Tag is not MappableAction mappableAction ) )
		{
			return;
		}

		var app = App.Instance!;

		var updateButtonMappingsWindow = new UpdateButtonMappingsWindow( mappableAction.GetButtonMappings( Settings ) )
		{
			Owner = app.MainWindow
		};

		updateButtonMappingsWindow.ShowDialog();

		app.RebuildButtonMappingIndex();

		MairaMappableButton.RefreshAll();
		MairaMappableSwitch.RefreshAll();

		RefreshSummary( mappableAction );
	}

	private void Clear_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( ( sender is not MairaButton mairaButton ) || ( mairaButton.Tag is not MappableAction mappableAction ) )
		{
			return;
		}

		var app = App.Instance!;

		mappableAction.GetButtonMappings( Settings ).MappedButtons.Clear();

		app.RebuildButtonMappingIndex();

		MairaMappableButton.RefreshAll();
		MairaMappableSwitch.RefreshAll();

		app.SettingsFile.QueueForSerialization = true;

		RefreshSummary( mappableAction );
	}

	#endregion

	#region Logic

	public void RefreshAll()
	{
		RefreshProfileSelector();

		RebuildMappingList();
	}

	private void RefreshProfileSelector()
	{
		_refreshingProfileSelector = true;

		var settings = Settings;

		var items = new List<KeyValuePair<string, string>>();

		foreach ( var profileName in settings.ControllerProfiles.Keys )
		{
			items.Add( new KeyValuePair<string, string>( profileName, profileName ) );
		}

		Profile_MairaComboBox.ItemsSource = items;
		Profile_MairaComboBox.SelectedValue = settings.CurrentControllerProfileName;

		DeleteProfile_MairaButton.Disabled = settings.ControllerProfiles.Count <= 1;

		_refreshingProfileSelector = false;
	}

	private void RebuildMappingList()
	{
		Mappings_StackPanel.Children.Clear();
		_summaryTextBlocks.Clear();
		_stepSizeEditors.Clear();

		var localization = Localization;

		// Group actions by category, preserving the catalog (UI) order.
		var categoryOrder = new List<string>();
		var actionsByCategory = new Dictionary<string, List<MappableAction>>();

		foreach ( var mappableAction in MappableActionCatalog.Actions )
		{
			if ( !actionsByCategory.TryGetValue( mappableAction.CategoryKey, out var actions ) )
			{
				actions = [];

				actionsByCategory[ mappableAction.CategoryKey ] = actions;

				categoryOrder.Add( mappableAction.CategoryKey );
			}

			actions.Add( mappableAction );
		}

		var filter = MappingSearch_MairaTextBox?.Value ?? string.Empty;
		var filtering = !string.IsNullOrWhiteSpace( filter );

		foreach ( var categoryKey in categoryOrder )
		{
			var matchingActions = filtering
				? actionsByCategory[ categoryKey ].Where( action => MatchesFilter( action, filter ) ).ToList()
				: actionsByCategory[ categoryKey ];

			// When filtering, skip categories with no matching actions entirely.
			if ( matchingActions.Count == 0 )
			{
				continue;
			}

			var rowsStackPanel = new StackPanel
			{
				Margin = new Thickness( 0, 5, 0, 5 )
			};

			foreach ( var mappableAction in matchingActions )
			{
				rowsStackPanel.Children.Add( BuildRow( mappableAction ) );
			}

			var mairaExpander = new MairaExpander
			{
				Label = localization[ categoryKey ],
				ExpanderContent = rowsStackPanel,

				// Auto-expand matching categories while a search is active so results are visible immediately.
				IsExpanded = filtering
			};

			Mappings_StackPanel.Children.Add( mairaExpander );
		}
	}

	private static bool MatchesFilter( MappableAction mappableAction, string filter )
	{
		var localization = Localization;

		var controlLabel = localization[ mappableAction.LabelKey ];

		if ( mappableAction.Index > 0 )
		{
			controlLabel = $"{controlLabel} {mappableAction.Index}";
		}

		return localization[ mappableAction.CategoryKey ].Contains( filter, StringComparison.OrdinalIgnoreCase )
			|| localization[ mappableAction.GroupLabelKey ].Contains( filter, StringComparison.OrdinalIgnoreCase )
			|| controlLabel.Contains( filter, StringComparison.OrdinalIgnoreCase );
	}

	private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;

	private void MappingSearch_MairaTextBox_ValueChanged( object sender, RoutedEventArgs e )
	{
		if ( _searchDebounceTimer is null )
		{
			_searchDebounceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds( 300 ) };

			_searchDebounceTimer.Tick += ( _, _ ) =>
			{
				_searchDebounceTimer.Stop();
				RebuildMappingList();
			};
		}

		_searchDebounceTimer.Stop();
		_searchDebounceTimer.Start();
	}

	private FrameworkElement BuildRow( MappableAction mappableAction )
	{
		var localization = Localization;

		var grid = new Grid
		{
			Margin = new Thickness( 0, 3, 0, 3 ),

			// Uniform row height so rows with a step-size editor (a 34px MairaTextBox) don't stand taller
			// than rows without one - shorter rows grow to match rather than the list looking uneven.
			MinHeight = 34
		};

		// Group label | direction (+/-) | control label | mapping summary | step size | Map | Clear
		grid.ColumnDefinitions.Add( new ColumnDefinition { Width = new GridLength( 3, GridUnitType.Star ) } );
		grid.ColumnDefinitions.Add( new ColumnDefinition { Width = new GridLength( 30 ) } );
		grid.ColumnDefinitions.Add( new ColumnDefinition { Width = new GridLength( 2, GridUnitType.Star ) } );
		grid.ColumnDefinitions.Add( new ColumnDefinition { Width = new GridLength( 3, GridUnitType.Star ) } );
		grid.ColumnDefinitions.Add( new ColumnDefinition { Width = new GridLength( 90 ) } );
		grid.ColumnDefinitions.Add( new ColumnDefinition { Width = GridLength.Auto } );
		grid.ColumnDefinitions.Add( new ColumnDefinition { Width = GridLength.Auto } );

		var groupTextBlock = new TextBlock
		{
			Text = localization[ mappableAction.GroupLabelKey ],
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};

		groupTextBlock.SetResourceReference( StyleProperty, "MairaTextBlockStyle" );

		Grid.SetColumn( groupTextBlock, 0 );
		grid.Children.Add( groupTextBlock );

		var controlLabel = localization[ mappableAction.LabelKey ];

		if ( mappableAction.Index > 0 )
		{
			controlLabel = $"{controlLabel} {mappableAction.Index}";
		}

		var labelTextBlock = new TextBlock
		{
			Text = controlLabel,
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};

		labelTextBlock.SetResourceReference( StyleProperty, "MairaTextBlockStyle" );

		Grid.SetColumn( labelTextBlock, 2 );
		grid.Children.Add( labelTextBlock );

		var directionTextBlock = new TextBlock
		{
			Text = DirectionSymbol( mappableAction.Direction ),
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center
		};

		directionTextBlock.SetResourceReference( StyleProperty, "MairaTextBlockStyle" );

		Grid.SetColumn( directionTextBlock, 1 );
		grid.Children.Add( directionTextBlock );

		var summaryTextBlock = new TextBlock
		{
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};

		summaryTextBlock.SetResourceReference( StyleProperty, "MairaTextBlockStyle" );

		ApplySummary( summaryTextBlock, mappableAction.GetButtonMappings( Settings ) );

		Grid.SetColumn( summaryTextBlock, 3 );
		grid.Children.Add( summaryTextBlock );

		_summaryTextBlocks[ mappableAction ] = summaryTextBlock;

		// Knob actions (the +/- pairs) get an editable step-size field. The value is universal - stored in
		// Settings.KnobStepSizes, shared across all controller profiles - and drives both the on-screen
		// +/- buttons and mapped input. Trigger actions leave this column empty (their cells just reserve space).
		if ( mappableAction.StepSizeKey is string stepSizeKey )
		{
			var currentStepSize = Settings.GetKnobStepSize( stepSizeKey, mappableAction.DefaultStepSize );

			var stepSizeMairaTextBox = new MairaTextBox
			{
				FontSize = 20,
				Width = 80,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Center,
				ToolTip = localization[ "StepSize" ],
				Tag = stepSizeKey,
				Value = FormatStepSize( currentStepSize )
			};

			stepSizeMairaTextBox.ValueChanged += StepSize_MairaTextBox_ValueChanged;

			if ( !_stepSizeEditors.TryGetValue( stepSizeKey, out var editors ) )
			{
				editors = [];

				_stepSizeEditors[ stepSizeKey ] = editors;
			}

			editors.Add( stepSizeMairaTextBox );

			Grid.SetColumn( stepSizeMairaTextBox, 4 );
			grid.Children.Add( stepSizeMairaTextBox );
		}

		var mapMairaButton = new MairaButton
		{
			Label = localization[ "Map" ],
			IsSmall = true,
			LabelOnRight = true,
			VerticalAlignment = VerticalAlignment.Center,
			Tag = mappableAction
		};

		mapMairaButton.Click += Map_MairaButton_Click;

		Grid.SetColumn( mapMairaButton, 5 );
		grid.Children.Add( mapMairaButton );

		var clearMairaButton = new MairaButton
		{
			Label = localization[ "Clear" ],
			IsSmall = true,
			LabelOnRight = true,
			Margin = new Thickness( 15, 0, 0, 0 ),
			VerticalAlignment = VerticalAlignment.Center,
			Tag = mappableAction
		};

		clearMairaButton.Click += Clear_MairaButton_Click;

		Grid.SetColumn( clearMairaButton, 6 );
		grid.Children.Add( clearMairaButton );

		return grid;
	}

	private void StepSize_MairaTextBox_ValueChanged( object sender, RoutedEventArgs e )
	{
		if ( _syncingStepSizeEditors )
		{
			return;
		}

		if ( ( sender is not MairaTextBox mairaTextBox ) || ( mairaTextBox.Tag is not string stepSizeKey ) )
		{
			return;
		}

		// Ignore unparseable or non-positive input (including intermediate states while the user is typing,
		// e.g. "0." on the way to "0.05"). A zero/negative step would freeze the knob.
		if ( !TryParseStepSize( mairaTextBox.Value, out var stepSize ) || ( stepSize <= 0f ) )
		{
			return;
		}

		var app = App.Instance!;

		Settings.SetKnobStepSize( stepSizeKey, stepSize );

		app.SettingsFile.QueueForSerialization = true;

		// Mirror the value into the knob's other (+/-) editor without retriggering this handler.
		if ( _stepSizeEditors.TryGetValue( stepSizeKey, out var editors ) )
		{
			_syncingStepSizeEditors = true;

			var formatted = FormatStepSize( stepSize );

			foreach ( var editor in editors )
			{
				if ( !ReferenceEquals( editor, mairaTextBox ) && ( editor.Value != formatted ) )
				{
					editor.Value = formatted;
				}
			}

			_syncingStepSizeEditors = false;
		}
	}

	private static string FormatStepSize( float stepSize )
	{
		return stepSize.ToString( "0.#####", CultureInfo.CurrentCulture );
	}

	private static bool TryParseStepSize( string? text, out float value )
	{
		text = ( text ?? string.Empty ).Trim();

		if ( float.TryParse( text, NumberStyles.Float, CultureInfo.CurrentCulture, out value ) )
		{
			return true;
		}

		return float.TryParse( text.Replace( ',', '.' ), NumberStyles.Float, CultureInfo.InvariantCulture, out value );
	}

	private static string DirectionSymbol( MappableActionDirection direction )
	{
		return direction switch
		{
			MappableActionDirection.Increase => "+",
			MappableActionDirection.Decrease => "−",
			_ => string.Empty
		};
	}

	private void RefreshSummary( MappableAction mappableAction )
	{
		if ( _summaryTextBlocks.TryGetValue( mappableAction, out var summaryTextBlock ) )
		{
			ApplySummary( summaryTextBlock, mappableAction.GetButtonMappings( Settings ) );
		}
	}

	private static void ApplySummary( TextBlock summaryTextBlock, ButtonMappings buttonMappings )
	{
		summaryTextBlock.Text = BuildSummary( buttonMappings );

		if ( IsMapped( buttonMappings ) )
		{
			// Mapped: fall back to the default (white) style foreground.
			summaryTextBlock.ClearValue( TextBlock.ForegroundProperty );
		}
		else
		{
			// Unmapped: show "(unmapped)" in MAIRA orange.
			summaryTextBlock.SetResourceReference( TextBlock.ForegroundProperty, "Brush.Accent" );
		}
	}

	private static bool IsMapped( ButtonMappings buttonMappings )
	{
		foreach ( var mappedButton in buttonMappings.MappedButtons )
		{
			if ( mappedButton.ClickButton.DeviceInstanceGuid != Guid.Empty )
			{
				return true;
			}
		}

		return false;
	}

	private static string BuildSummary( ButtonMappings buttonMappings )
	{
		var localization = Localization;

		var parts = new List<string>();

		foreach ( var mappedButton in buttonMappings.MappedButtons )
		{
			if ( mappedButton.ClickButton.DeviceInstanceGuid == Guid.Empty )
			{
				continue;
			}

			if ( mappedButton.HoldButton.DeviceInstanceGuid != Guid.Empty )
			{
				parts.Add( $"{localization[ "Hold" ]} {mappedButton.HoldButton.ButtonNumber} + {mappedButton.ClickButton.ButtonNumber}" );
			}
			else
			{
				parts.Add( $"{mappedButton.ClickButton.ButtonNumber}" );
			}
		}

		return parts.Count == 0 ? localization[ "Unmapped" ] : string.Join( ", ", parts );
	}

	#endregion
}
