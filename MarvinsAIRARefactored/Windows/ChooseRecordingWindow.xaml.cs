
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MarvinsAIRARefactored.Windows;

/// <summary>
/// Modal recording picker (matches <see cref="AddFFBModuleWindow"/>): clicking a recording selects it and
/// closes the window immediately; the title-bar X (or Escape) cancels; Enter commits a keyboard selection.
/// </summary>
public partial class ChooseRecordingWindow : Window
{
	/// <summary>The currently open instance, if any — lets the recording manager refresh the list live when a
	/// new recording is saved while this dialog is up.</summary>
	public static ChooseRecordingWindow? Current { get; private set; } = null;

	public ChooseRecordingWindow()
	{
		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		RefreshRecordingsList();

		Current = this;

		Closed += ( sender, e ) => Current = null;
	}

	/// <summary>Rebuild the list from the recording manager, keeping the currently selected recording highlighted.</summary>
	public void RefreshRecordingsList()
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		var items = app.RecordingManager.Recordings.Select( recording => new KeyValuePair<string, string>( recording.Key, recording.Value.Description ?? recording.Key ) ).OrderBy( keyValuePair => keyValuePair.Value ).ToList();

		Recordings_ListBox.ItemsSource = items;

		NoRecordings_TextBlock.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

		var selectedIndex = items.FindIndex( keyValuePair => keyValuePair.Key == settings.RacingWheelSelectedRecording );

		if ( selectedIndex >= 0 )
		{
			Recordings_ListBox.SelectedIndex = selectedIndex;
			Recordings_ListBox.ScrollIntoView( Recordings_ListBox.SelectedItem );
		}
	}

	private void ApplySelection( KeyValuePair<string, string> selected )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;

		// the setter refreshes the algorithm preview on its own
		settings.RacingWheelSelectedRecording = selected.Key;

		app.SettingsFile.QueueForSerialization = true;

		Close();
	}

	private void RecordingItem_PreviewMouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( ( sender is ListBoxItem listBoxItem ) && ( listBoxItem.DataContext is KeyValuePair<string, string> selected ) )
		{
			ApplySelection( selected );
		}
	}

	private void Window_KeyDown( object sender, KeyEventArgs e )
	{
		if ( ( e.Key == Key.Enter ) && ( Recordings_ListBox.SelectedItem is KeyValuePair<string, string> selected ) )
		{
			ApplySelection( selected );
		}
		else if ( e.Key == Key.Escape )
		{
			Close();
		}
	}
}
