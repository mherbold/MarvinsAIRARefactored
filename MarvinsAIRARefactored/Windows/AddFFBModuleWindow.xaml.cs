
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MarvinsAIRARefactored.Windows;

/// <summary>
/// Modal module-type picker for the FFB graph editor's add-module button. Clicking a type adds it right
/// away (the caller reads <see cref="SelectedModuleType"/> after ShowDialog); closing the window any other way
/// (title-bar X, Escape) adds nothing. The item list is the same keyed list the old add-module dropdowns bound
/// to — rows whose Key starts with an underscore are non-selectable category headers.
/// </summary>
public partial class AddFFBModuleWindow : Window
{
	/// <summary>The module type key the user clicked, or null when the window was dismissed.</summary>
	public string? SelectedModuleType { get; private set; } = null;

	public AddFFBModuleWindow( List<KeyValuePair<string, string>> moduleTypes )
	{
		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		ModuleTypes_ListBox.ItemsSource = moduleTypes;
	}

	private void ApplySelection( KeyValuePair<string, string> selected )
	{
		// category headers are disabled in XAML; this guard covers the keyboard path
		if ( selected.Key.StartsWith( '_' ) )
		{
			return;
		}

		SelectedModuleType = selected.Key;

		Close();
	}

	private void ModuleTypeItem_PreviewMouseLeftButtonUp( object sender, MouseButtonEventArgs e )
	{
		if ( ( sender is ListBoxItem listBoxItem ) && ( listBoxItem.DataContext is KeyValuePair<string, string> selected ) )
		{
			ApplySelection( selected );
		}
	}

	private void Window_KeyDown( object sender, KeyEventArgs e )
	{
		if ( ( e.Key == Key.Enter ) && ( ModuleTypes_ListBox.SelectedItem is KeyValuePair<string, string> selected ) )
		{
			ApplySelection( selected );
		}
		else if ( e.Key == Key.Escape )
		{
			Close();
		}
	}
}
