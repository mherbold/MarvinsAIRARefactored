
using System.Windows;
using System.Windows.Input;

using MarvinsAIRARefactored.DataContext;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MarvinsAIRARefactored.Windows;

// Asks which profile the tuning profile manager should copy the selected profile's settings into. The candidate
// list is built by the manager and only ever holds profiles of the same shape as the source. Double-click, Enter
// or OK picks one; the title-bar X, Escape or Cancel leaves SelectedProfile null.
public partial class PickTuningProfileWindow : Window
{
	public TuningProfile? SelectedProfile { get; private set; } = null;

	public PickTuningProfileWindow( List<TuningProfile> candidateProfiles )
	{
		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		Profiles_ListBox.ItemsSource = candidateProfiles;
	}

	private void TryConfirmSelection()
	{
		if ( Profiles_ListBox.SelectedItem is TuningProfile tuningProfile )
		{
			SelectedProfile = tuningProfile;

			Close();
		}
	}

	private void Profiles_ListBox_MouseDoubleClick( object sender, MouseButtonEventArgs e )
	{
		TryConfirmSelection();
	}

	private void Profiles_ListBox_KeyDown( object sender, KeyEventArgs e )
	{
		if ( e.Key == Key.Enter )
		{
			TryConfirmSelection();
		}
	}

	private void Window_KeyDown( object sender, KeyEventArgs e )
	{
		if ( e.Key == Key.Escape )
		{
			Close();
		}
	}

	private void OK_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		TryConfirmSelection();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Close();
	}
}
