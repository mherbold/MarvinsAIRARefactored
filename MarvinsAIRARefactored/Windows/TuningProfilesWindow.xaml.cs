
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using MarvinsAIRARefactored.DataContext;

namespace MarvinsAIRARefactored.Windows;

// One row of the profile list on the left. Rows whose Key starts with an underscore are the non-selectable group
// headers (same convention as the add-module picker), and only those have a null Profile.
//
// A profile row spreads its name over up to three lines - car, track, track configuration - so a deeply scoped
// profile's track and configuration are not ellipsized away behind the car name. The trailing badges sit on the
// first two lines: the weather (when the profile has one) and the live marker on line 1, and the changed count on
// line 1 unless the weather badge is already there, in which case it moves down to line 2. A line with no text on
// either side collapses, so a shallow profile is still the single row it always was.
public sealed class TuningProfileListItem
{
	public required string Key { get; init; }
	public required string Label { get; init; }
	public string Line1Text { get; init; } = string.Empty;
	public string WeatherBadge { get; init; } = string.Empty;
	public string Line1ChangedCount { get; init; } = string.Empty;
	public string LiveMarker { get; init; } = string.Empty;
	public string Line2Text { get; init; } = string.Empty;
	public string Line2ChangedCount { get; init; } = string.Empty;
	public string Line3Text { get; init; } = string.Empty;
	public TuningProfile? Profile { get; init; } = null;
}

// A section header in the detail list on the right - a settings category, or the graph for FFB rows.
public sealed class TuningProfileDetailSection
{
	public required string Label { get; init; }
}

// A sub-group header inside a section - the page section the settings sit in, or the module for FFB rows. Only
// emitted for a run of rows that actually carries one (a setting with no group of its own has none).
public sealed class TuningProfileDetailSubSection
{
	public required string Label { get; init; }
}

// One setting in the detail list on the right. The row itself is handed back to the engine when the user reverts.
public sealed class TuningProfileDetailRow
{
	public required TuningProfileRow Row { get; init; }

	public string Label { get => Row.Label; }

	// only the default profile fills these in - the scope the setting is tuned at, beside the fallback value it
	// holds. The label is the icon cluster's tooltip and the four flags are the icons themselves, so they are all
	// empty / false together.
	public string ScopeLabel { get => Row.ScopeLabel; }

	public bool ScopePerCar { get => Row.ScopePerCar; }
	public bool ScopePerTrack { get => Row.ScopePerTrack; }
	public bool ScopePerTrackConfiguration { get => Row.ScopePerTrackConfiguration; }
	public bool ScopePerWetDry { get => Row.ScopePerWetDry; }

	public string ValueString { get => Row.ValueString; }
	public string DefaultValueString { get => Row.DefaultValueString; }

	// only the default profile fills this in - it lists every setting, so the ones it has actually moved off the
	// factory value are called out in the value column
	public bool IsChanged { get => Row.IsChanged; }
}

// The tuning profile manager, opened by clicking the main window's status bar. Everything it shows comes out of
// Settings.BuildTuningProfiles() and every operation is done by the engine - this window only picks what to act on,
// asks for confirmation, and rebuilds itself afterwards.
public partial class TuningProfilesWindow : Window
{
	private List<TuningProfile> _profiles = [];
	private TuningProfile? _selectedProfile = null;

	// the list is rebuilt wholesale on every refresh, and swapping the items source fires SelectionChanged on the
	// way out - which would otherwise clear the selection we are in the middle of restoring
	private bool _rebuildingProfileList = false;

	// closing an owned dialog re-activates this window, and the activation refresh would then fire in the middle
	// of an operation (the clean up's dry run and its apply straddle a confirmation dialog) - so only genuine
	// external activations are allowed to rebuild the model
	private bool _suppressActivatedRefresh = false;

	public TuningProfilesWindow()
	{
		App.Instance!.MainWindow.MakeWindowVisible();

		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );
	}

	#region Window Events

	// The simulator can move the app onto another context (and so create or retire whole profiles) while this
	// window sits in the background, so the model is rebuilt every time the window comes back to the front.
	private void Window_Activated( object sender, EventArgs e )
	{
		if ( _suppressActivatedRefresh )
		{
			return;
		}

		Refresh();
	}

	private void Profiles_ListBox_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		if ( _rebuildingProfileList )
		{
			return;
		}

		SelectProfile( ( Profiles_ListBox.SelectedItem as TuningProfileListItem )?.Profile );
	}

	// The widest the profile list can go at the window's current size - the detail panel keeps its 20 pixel gap,
	// its two 200 pixel value columns plus the 44 pixel revert column, and some room for the setting labels.
	private double MaximumListWidth()
	{
		return ListSplit_Grid.ActualWidth - 614.0;
	}

	// Grab handle on the seam between the profile list and the detail panel - dragging resizes the list column
	// (the setting's setter clamps the far ends, so overshooting a limit just parks there; the thumb reports
	// deltas relative to its own grab point, so the handle never jumps when the drag comes back inside the
	// limits).
	private void ListWidth_Thumb_DragDelta( object sender, DragDeltaEventArgs e )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		settings.TuningProfilesListWidth = Math.Min( settings.TuningProfilesListWidth + e.HorizontalChange, MaximumListWidth() );
	}

	private void ListWidth_Thumb_DragCompleted( object sender, DragCompletedEventArgs e )
	{
		App.Instance!.SettingsFile.QueueForSerialization = true;
	}

	// Shrinking the window below a widened list would push the detail panel's fixed value columns out of view,
	// so the list gives the width back (the settings setter's own minimum still wins over this clamp).
	private void ListSplit_Grid_SizeChanged( object sender, SizeChangedEventArgs e )
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		if ( settings.TuningProfilesListWidth > MaximumListWidth() )
		{
			settings.TuningProfilesListWidth = MaximumListWidth();
		}
	}

	#endregion

	#region Model

	private void Refresh()
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		_profiles = settings.BuildTuningProfiles();

		var items = BuildProfileListItems();

		// the model is rebuilt from scratch, so the previous selection is matched by identity (shape + context key)
		// rather than by reference - and falls back to the default profile when it is gone
		var selectedItem = items.Find( item => ( item.Profile != null ) && IsSameProfile( item.Profile, _selectedProfile ) ) ?? items.Find( item => item.Profile?.IsDefaultProfile ?? false );

		_rebuildingProfileList = true;

		Profiles_ListBox.ItemsSource = items;
		Profiles_ListBox.SelectedItem = selectedItem;

		_rebuildingProfileList = false;

		SelectProfile( selectedItem?.Profile );

		DisconnectedHint_TextBlock.Visibility = app.Simulator.IsConnected ? Visibility.Collapsed : Visibility.Visible;

		CleanUpStatus_TextBlock.Visibility = Visibility.Collapsed;
	}

	private void SelectProfile( TuningProfile? profile )
	{
		_selectedProfile = profile;

		UpdateDetail();
		UpdateButtons();
	}

	private static bool IsSameProfile( TuningProfile profile, TuningProfile? otherProfile )
	{
		return ( otherProfile != null ) && ( profile.Shape == otherProfile.Shape ) && ( profile.Key.CompareTo( otherProfile.Key ) == 0 );
	}

	private List<TuningProfileListItem> BuildProfileListItems()
	{
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var items = new List<TuningProfileListItem>();

		// specificity first - the default profile is pinned to the top, then one group per scope shape
		AddProfileListGroup( items, localization[ "Default" ], profile => profile.IsDefaultProfile );
		AddProfileListGroup( items, localization[ "TuningProfilesGroupPerWeather" ], profile => !profile.IsDefaultProfile && ( profile.Shape.Dims == TuningProfileDims.None ) );
		AddProfileListGroup( items, localization[ "TuningProfilesGroupPerCar" ], profile => profile.Shape.Dims == TuningProfileDims.Car );
		AddProfileListGroup( items, localization[ "TuningProfilesGroupPerCarTrack" ], profile => profile.Shape.Dims == TuningProfileDims.CarTrack );
		AddProfileListGroup( items, localization[ "TuningProfilesGroupPerCarTrackConfiguration" ], profile => profile.Shape.Dims == TuningProfileDims.CarTrackConfig );

		return items;
	}

	private void AddProfileListGroup( List<TuningProfileListItem> items, string groupLabel, Func<TuningProfile, bool> belongsToGroup )
	{
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var headerAdded = false;

		foreach ( var profile in _profiles )
		{
			if ( !belongsToGroup( profile ) )
			{
				continue;
			}

			// a group with nothing in it is not shown at all
			if ( !headerAdded )
			{
				headerAdded = true;

				items.Add( new TuningProfileListItem
				{
					Key = $"_{groupLabel}",
					Label = groupLabel
				} );
			}

			var weatherText = ( profile.Weather == TuningProfileWeather.Any ) ? string.Empty : localization[ ( profile.Weather == TuningProfileWeather.Wet ) ? "Wet" : "Dry" ];

			// the first line is the car - except for the default profile ("Default") and the weather-only profiles,
			// which have no dimension names at all, so there the weather IS the name and it moves out of the badge
			var line1Text = profile.IsDefaultProfile ? profile.Label : profile.CarLabel;

			var weatherBadge = weatherText;

			if ( ( line1Text.Length == 0 ) && ( weatherText.Length > 0 ) )
			{
				line1Text = weatherText;
				weatherBadge = string.Empty;
			}

			// the default profile lists every setting rather than a diff, so its number is a total, not a count of changes
			var changedCount = string.Format( localization[ profile.IsDefaultProfile ? "TuningProfilesSettingCount" : "TuningProfilesChangedCount" ], profile.Rows.Count );

			items.Add( new TuningProfileListItem
			{
				Key = "Profile",
				Label = profile.Label,
				Line1Text = line1Text,
				WeatherBadge = weatherBadge,
				Line1ChangedCount = ( weatherBadge.Length == 0 ) ? changedCount : string.Empty,
				LiveMarker = profile.IsLive ? localization[ "TuningProfilesLive" ] : string.Empty,
				Line2Text = profile.TrackLabel,
				Line2ChangedCount = ( weatherBadge.Length > 0 ) ? changedCount : string.Empty,
				Line3Text = profile.TrackConfigurationLabel,
				Profile = profile
			} );
		}
	}

	// The rows come out of the engine in display order, but a group can be split across that order (the catalog
	// categories do not have to line up with the settings property order), so they are bucketed twice - by section
	// and then by sub-group inside it - keeping the order each one first appears in and the order of the rows inside
	// it. The two headers carry the shared part of a row's name, leaving the row itself to show the setting alone.
	private void UpdateDetail()
	{
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		// the default profile's rows diff against factory defaults, every other profile's against the default profile
		DefaultColumn_TextBlock.Text = localization[ ( _selectedProfile?.IsDefaultProfile ?? false ) ? "TuningProfilesFactoryColumn" : "TuningProfilesDefaultColumn" ];

		var items = new List<object>();

		if ( _selectedProfile != null )
		{
			var sectionLabels = new List<string>();

			var subGroupLabelsBySectionLabel = new Dictionary<string, List<string>>( StringComparer.Ordinal );
			var rowsBySubGroup = new Dictionary<(string sectionLabel, string subGroupLabel), List<TuningProfileRow>>();

			foreach ( var row in _selectedProfile.Rows )
			{
				if ( !subGroupLabelsBySectionLabel.TryGetValue( row.GroupLabel, out var subGroupLabels ) )
				{
					subGroupLabels = [];

					subGroupLabelsBySectionLabel.Add( row.GroupLabel, subGroupLabels );
					sectionLabels.Add( row.GroupLabel );
				}

				if ( !rowsBySubGroup.TryGetValue( ( row.GroupLabel, row.SubGroupLabel ), out var subGroupRows ) )
				{
					subGroupRows = [];

					rowsBySubGroup.Add( ( row.GroupLabel, row.SubGroupLabel ), subGroupRows );
					subGroupLabels.Add( row.SubGroupLabel );
				}

				subGroupRows.Add( row );
			}

			foreach ( var sectionLabel in sectionLabels )
			{
				items.Add( new TuningProfileDetailSection { Label = sectionLabel } );

				foreach ( var subGroupLabel in subGroupLabelsBySectionLabel[ sectionLabel ] )
				{
					// a setting with no group of its own sits directly under the section header
					if ( subGroupLabel.Length > 0 )
					{
						items.Add( new TuningProfileDetailSubSection { Label = subGroupLabel } );
					}

					foreach ( var row in rowsBySubGroup[ ( sectionLabel, subGroupLabel ) ] )
					{
						items.Add( new TuningProfileDetailRow { Row = row } );
					}
				}
			}
		}

		Rows_ItemsControl.ItemsSource = items;

		NoChanges_TextBlock.Visibility = ( items.Count == 0 ) ? Visibility.Visible : Visibility.Collapsed;
	}

	private void UpdateButtons()
	{
		// the default profile is the bottom of the stack - it cannot be deleted, copied from, or promoted into itself
		var canModifySelectedProfile = ( _selectedProfile != null ) && !_selectedProfile.IsDefaultProfile;

		Delete_MairaButton.Disabled = !canModifySelectedProfile;
		Promote_MairaButton.Disabled = !canModifySelectedProfile;
		Copy_MairaButton.Disabled = !canModifySelectedProfile || ( FindCopyTargets( _selectedProfile! ).Count == 0 );
	}

	// A profile can only be copied onto another profile of the same shape - two shapes own different sets of
	// settings, so there would be nothing sensible to write.
	private List<TuningProfile> FindCopyTargets( TuningProfile profile )
	{
		return _profiles.FindAll( candidate => !candidate.IsDefaultProfile && ( candidate.Shape == profile.Shape ) && ( candidate.Key.CompareTo( profile.Key ) != 0 ) );
	}

	private bool Confirm( string title, string message )
	{
		var confirmActionWindow = new ConfirmActionWindow( title, message )
		{
			Owner = this
		};

		_suppressActivatedRefresh = true;

		confirmActionWindow.ShowDialog();

		_suppressActivatedRefresh = false;

		return confirmActionWindow.Confirmed;
	}

	#endregion

	#region Operations

	private void Revert_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var profile = _selectedProfile;

		if ( ( profile == null ) || ( sender is not FrameworkElement element ) || ( element.DataContext is not TuningProfileDetailRow detailRow ) )
		{
			return;
		}

		// the row itself only shows the setting - the log line has no headers above it, so it takes the full name
		app.Logger.WriteLine( $"[TuningProfilesWindow] Reverting \"{detailRow.Row.FullLabel}\" in tuning profile \"{profile.LabelWithWeather}\"" );

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		// a refusal means the model this window is showing is stale (the bucket is gone), so refresh either way
		if ( !settings.RevertTuningProfileRow( profile, detailRow.Row ) )
		{
			app.Logger.WriteLine( "[TuningProfilesWindow] Revert was refused" );
		}

		Refresh();
	}

	private void Delete_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var profile = _selectedProfile;

		if ( profile == null )
		{
			return;
		}

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		if ( !Confirm( localization[ "DeleteProfile" ], localization[ "TuningProfilesDeleteConfirmation" ] ) )
		{
			return;
		}

		app.Logger.WriteLine( $"[TuningProfilesWindow] Deleting tuning profile \"{profile.LabelWithWeather}\"" );

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		if ( !settings.DeleteTuningProfile( profile ) )
		{
			app.Logger.WriteLine( "[TuningProfilesWindow] Delete was refused" );
		}

		// it is gone - let the refresh fall back to the default profile
		_selectedProfile = null;

		Refresh();
	}

	private void Copy_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var profile = _selectedProfile;

		if ( profile == null )
		{
			return;
		}

		var candidateProfiles = FindCopyTargets( profile );

		if ( candidateProfiles.Count == 0 )
		{
			return;
		}

		var pickTuningProfileWindow = new PickTuningProfileWindow( candidateProfiles )
		{
			Owner = this
		};

		_suppressActivatedRefresh = true;

		pickTuningProfileWindow.ShowDialog();

		_suppressActivatedRefresh = false;

		var destinationProfile = pickTuningProfileWindow.SelectedProfile;

		if ( destinationProfile == null )
		{
			return;
		}

		app.Logger.WriteLine( $"[TuningProfilesWindow] Copying tuning profile \"{profile.LabelWithWeather}\" to \"{destinationProfile.LabelWithWeather}\"" );

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		if ( !settings.CopyTuningProfile( profile, destinationProfile ) )
		{
			app.Logger.WriteLine( "[TuningProfilesWindow] Copy was refused" );
		}

		Refresh();
	}

	private void Promote_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var profile = _selectedProfile;

		if ( profile == null )
		{
			return;
		}

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		if ( !Confirm( localization[ "TuningProfilesPromote" ], localization[ "TuningProfilesPromoteConfirmation" ] ) )
		{
			return;
		}

		app.Logger.WriteLine( $"[TuningProfilesWindow] Promoting tuning profile \"{profile.LabelWithWeather}\" to the default profile" );

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		if ( !settings.PromoteTuningProfileToDefault( profile ) )
		{
			app.Logger.WriteLine( "[TuningProfilesWindow] Promote was refused" );
		}

		Refresh();
	}

	private void CleanUp_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		// dry run first, so the confirmation can say exactly what would happen
		var dryRunResult = settings.CleanUpTuningProfiles( false );

		app.Logger.WriteLine( $"[TuningProfilesWindow] Clean up would remove {dryRunResult.RemovedModuleValueKeys} module values, repair {dryRunResult.RepairedGraphSelections} graph selections, and remove {dryRunResult.RemovedUnreachableBuckets + dryRunResult.RemovedEmptyBuckets} profiles" );

		if ( dryRunResult.Total == 0 )
		{
			CleanUpStatus_TextBlock.Visibility = Visibility.Visible;

			return;
		}

		var message = string.Format( localization[ "TuningProfilesCleanUpConfirmation" ], dryRunResult.RemovedModuleValueKeys, dryRunResult.RepairedGraphSelections, dryRunResult.RemovedUnreachableBuckets + dryRunResult.RemovedEmptyBuckets );

		if ( !Confirm( localization[ "TuningProfilesCleanUp" ], message ) )
		{
			return;
		}

		app.Logger.WriteLine( "[TuningProfilesWindow] Cleaning up the tuning profiles" );

		settings.CleanUpTuningProfiles( true );

		Refresh();
	}

	private void Close_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Close();
	}

	#endregion
}
