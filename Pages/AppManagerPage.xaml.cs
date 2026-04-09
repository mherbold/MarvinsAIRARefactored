using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using UserControl = System.Windows.Controls.UserControl;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Pages;

public partial class AppManagerPage : UserControl
{
	private readonly ObservableCollection<AppManagerStartEntryViewModel> _startEntries = [];
	private readonly ObservableCollection<AppManagerTerminateEntry> _terminateEntries = [];

	private List<KeyValuePair<ProcessPriorityClass, string>> _cpuPriorityOptions = [];
	private List<KeyValuePair<ProcessWindowStyle, string>> _windowStyleOptions = [];

	public AppManagerPage()
	{
		InitializeComponent();
	}

	public void UpdateComboBoxOptions()
	{
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var cpuPriorityDictionary = new Dictionary<ProcessPriorityClass, string>
		{
			{ ProcessPriorityClass.Idle, localization[ "CpuPriorityIdle" ] },
			{ ProcessPriorityClass.BelowNormal, localization[ "CpuPriorityBelowNormal" ] },
			{ ProcessPriorityClass.Normal, localization[ "CpuPriorityNormal" ] },
			{ ProcessPriorityClass.AboveNormal, localization[ "CpuPriorityAboveNormal" ] },
			{ ProcessPriorityClass.High, localization[ "CpuPriorityHigh" ] },
		};

		_cpuPriorityOptions = cpuPriorityDictionary.ToList();

		var windowStyleDictionary = new Dictionary<ProcessWindowStyle, string>
		{
			{ ProcessWindowStyle.Normal, localization[ "WindowStyleNormal" ] },
			{ ProcessWindowStyle.Minimized, localization[ "WindowStyleMinimized" ] },
			{ ProcessWindowStyle.Maximized, localization[ "WindowStyleMaximized" ] },
			{ ProcessWindowStyle.Hidden, localization[ "WindowStyleHidden" ] },
		};

		_windowStyleOptions = windowStyleDictionary.ToList();

		RefreshLists();
	}

	public void RefreshLists()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		_startEntries.Clear();

		foreach ( var entry in settings.AppManagerStartList )
		{
			_startEntries.Add( new AppManagerStartEntryViewModel( entry ) );
		}

		UpdateStartEntryFlags();

		_terminateEntries.Clear();

		foreach ( var entry in settings.AppManagerTerminateList )
		{
			_terminateEntries.Add( entry );
		}

		StartList_ItemsControl.ItemsSource = _startEntries;
		TerminateList_ItemsControl.ItemsSource = _terminateEntries;
	}

	private void UpdateStartEntryFlags()
	{
		for ( var i = 0; i < _startEntries.Count; i++ )
		{
			_startEntries[ i ].IsFirst = ( i == 0 );
			_startEntries[ i ].IsLast = ( i == _startEntries.Count - 1 );
		}
	}

	#region ComboBox Loaded handlers (set ItemsSource inside DataTemplate)

	private void CpuPriorityComboBox_Loaded( object sender, RoutedEventArgs e )
	{
		if ( sender is MairaComboBox combo && combo.ItemsSource == null )
		{
			combo.ItemsSource = _cpuPriorityOptions;

			if ( combo.Tag is AppManagerStartEntryViewModel vm )
			{
				combo.SelectedValue = vm.Entry.CpuPriority;
			}
		}
	}

	private void WindowStyleComboBox_Loaded( object sender, RoutedEventArgs e )
	{
		if ( sender is MairaComboBox combo && combo.ItemsSource == null )
		{
			combo.ItemsSource = _windowStyleOptions;

			if ( combo.Tag is AppManagerStartEntryViewModel vm )
			{
				combo.SelectedValue = vm.Entry.WindowStyle;
			}
		}
	}

	#endregion

	#region Start list event handlers

	private void AddStartApp_Click( object sender, RoutedEventArgs e )
	{
		_startEntries.Add( new AppManagerStartEntryViewModel( new AppManagerStartEntry() ) );

		UpdateStartEntryFlags();

		SyncStartListToSettings();
	}

	private void StartRemove_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is MairaButton button && button.Tag is AppManagerStartEntryViewModel vm )
		{
			_startEntries.Remove( vm );

			UpdateStartEntryFlags();

			SyncStartListToSettings();
		}
	}

	private void StartBrowse_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is MairaButton button && button.Tag is AppManagerStartEntryViewModel vm )
		{
			var filePath = BrowseForExecutable();

			if ( filePath != null )
			{
				vm.Entry.ExecutablePath = filePath;

				RefreshLists();

				SyncStartListToSettings();
			}
		}
	}

	private void StartPickProcess_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is MairaButton button && button.Tag is AppManagerStartEntryViewModel vm )
		{
			var window = new PickProcessWindow { Owner = Window.GetWindow( this ) };

			window.ShowDialog();

			if ( window.SelectedPath != null )
			{
				vm.Entry.ExecutablePath = window.SelectedPath;

				RefreshLists();

				SyncStartListToSettings();
			}
		}
	}

	private void StartMoveUp_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is MairaButton button && button.Tag is AppManagerStartEntryViewModel vm )
		{
			var index = _startEntries.IndexOf( vm );

			if ( index > 0 )
			{
				_startEntries.Move( index, index - 1 );

				UpdateStartEntryFlags();

				SyncStartListToSettings();
			}
		}
	}

	private void StartMoveDown_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is MairaButton button && button.Tag is AppManagerStartEntryViewModel vm )
		{
			var index = _startEntries.IndexOf( vm );

			if ( index < _startEntries.Count - 1 )
			{
				_startEntries.Move( index, index + 1 );

				UpdateStartEntryFlags();

				SyncStartListToSettings();
			}
		}
	}

	private void StartEntry_LostFocus( object sender, RoutedEventArgs e )
	{
		SyncStartListToSettings();
	}

	private void StartEntry_SelectionChanged( object sender, SelectionChangedEventArgs e )
	{
		SyncStartListToSettings();
	}

	private void StartEntry_Toggled( object? sender, EventArgs e )
	{
		SyncStartListToSettings();
	}

	#endregion

	#region Terminate list event handlers

	private void AddTerminateApp_Click( object sender, RoutedEventArgs e )
	{
		var entry = new AppManagerTerminateEntry();

		_terminateEntries.Add( entry );

		SyncTerminateListToSettings();
	}

	private void TerminateRemove_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is MairaButton button && button.Tag is AppManagerTerminateEntry entry )
		{
			_terminateEntries.Remove( entry );

			SyncTerminateListToSettings();
		}
	}

	private void TerminateBrowse_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is MairaButton button && button.Tag is AppManagerTerminateEntry entry )
		{
			var filePath = BrowseForExecutable();

			if ( filePath != null )
			{
				entry.ExecutablePath = filePath;

				RefreshLists();

				SyncTerminateListToSettings();
			}
		}
	}

	private void TerminatePickProcess_Click( object sender, RoutedEventArgs e )
	{
		if ( sender is MairaButton button && button.Tag is AppManagerTerminateEntry entry )
		{
			var window = new PickProcessWindow { Owner = Window.GetWindow( this ) };

			window.ShowDialog();

			if ( window.SelectedPath != null )
			{
				entry.ExecutablePath = window.SelectedPath;

				RefreshLists();

				SyncTerminateListToSettings();
			}
		}
	}

	private void TerminateEntry_LostFocus( object sender, RoutedEventArgs e )
	{
		SyncTerminateListToSettings();
	}

	private void TerminateEntry_Toggled( object? sender, EventArgs e )
	{
		SyncTerminateListToSettings();
	}

	#endregion

	#region Helpers

	private static string? BrowseForExecutable()
	{
		var dialog = new OpenFileDialog
		{
			Title = "Select Application",
			Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
			CheckFileExists = true
		};

		return dialog.ShowDialog() == true ? dialog.FileName : null;
	}

	private void SyncStartListToSettings()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		settings.AppManagerStartList.Clear();

		foreach ( var vm in _startEntries )
		{
			settings.AppManagerStartList.Add( vm.Entry );
		}

		App.Instance!.SettingsFile.QueueForSerialization = true;
	}

	private void SyncTerminateListToSettings()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		settings.AppManagerTerminateList.Clear();

		foreach ( var entry in _terminateEntries )
		{
			settings.AppManagerTerminateList.Add( entry );
		}

		App.Instance!.SettingsFile.QueueForSerialization = true;
	}

	#endregion
}
