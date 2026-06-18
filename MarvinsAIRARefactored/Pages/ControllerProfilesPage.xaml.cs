
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

		var localization = Localization;

		var result = System.Windows.MessageBox.Show( $"{localization[ "DeleteProfileConfirmation" ]}\r\n\r\n{settings.CurrentControllerProfileName}", localization[ "DeleteProfile" ], MessageBoxButton.YesNo, MessageBoxImage.Warning );

		if ( result != MessageBoxResult.Yes )
		{
			return;
		}

		var app = App.Instance!;

		app.Logger.WriteLine( $"[ControllerProfilesPage] Deleting controller profile '{settings.CurrentControllerProfileName}'" );

		settings.DeleteControllerProfile( settings.CurrentControllerProfileName );

		app.RebuildButtonMappingIndex();

		MairaMappableButton.RefreshAll();

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

		foreach ( var categoryKey in categoryOrder )
		{
			var rowsStackPanel = new StackPanel
			{
				Margin = new Thickness( 0, 5, 0, 5 )
			};

			foreach ( var mappableAction in actionsByCategory[ categoryKey ] )
			{
				rowsStackPanel.Children.Add( BuildRow( mappableAction ) );
			}

			var mairaExpander = new MairaExpander
			{
				Label = localization[ categoryKey ],
				ExpanderContent = rowsStackPanel
			};

			Mappings_StackPanel.Children.Add( mairaExpander );
		}
	}

	private FrameworkElement BuildRow( MappableAction mappableAction )
	{
		var localization = Localization;

		var grid = new Grid
		{
			Margin = new Thickness( 0, 3, 0, 3 )
		};

		// Group label | direction (+/-) | control label | mapping summary | Map | Clear
		grid.ColumnDefinitions.Add( new ColumnDefinition { Width = new GridLength( 3, GridUnitType.Star ) } );
		grid.ColumnDefinitions.Add( new ColumnDefinition { Width = new GridLength( 30 ) } );
		grid.ColumnDefinitions.Add( new ColumnDefinition { Width = new GridLength( 2, GridUnitType.Star ) } );
		grid.ColumnDefinitions.Add( new ColumnDefinition { Width = new GridLength( 3, GridUnitType.Star ) } );
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

		var mapMairaButton = new MairaButton
		{
			Label = localization[ "Map" ],
			IsSmall = true,
			LabelOnRight = true,
			VerticalAlignment = VerticalAlignment.Center,
			Tag = mappableAction
		};

		mapMairaButton.Click += Map_MairaButton_Click;

		Grid.SetColumn( mapMairaButton, 4 );
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

		Grid.SetColumn( clearMairaButton, 5 );
		grid.Children.Add( clearMairaButton );

		return grid;
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
