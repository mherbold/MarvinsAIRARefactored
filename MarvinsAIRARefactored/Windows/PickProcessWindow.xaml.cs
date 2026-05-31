
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Input;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MarvinsAIRARefactored.Windows;

public partial class PickProcessWindow : Window
{
	private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

	private record ProcessItem( string Name, string Path );

	private List<ProcessItem> _allProcesses = [];

	public string? SelectedPath { get; private set; }

	public PickProcessWindow()
	{
		InitializeComponent();

		if ( System.ComponentModel.DesignerProperties.GetIsInDesignMode( this ) )
		{
			return;
		}

		Loaded += async ( _, _ ) =>
		{
			SearchBox.Focus();
			await LoadProcessesAsync();
		};
	}

	private async Task LoadProcessesAsync()
	{
		var processes = await Task.Run( () =>
		{
			var list = new List<ProcessItem>();

			foreach ( var process in Process.GetProcesses() )
			{
				string? path = null;

				try
				{
					path = process.MainModule?.FileName;
				}
				catch
				{
					// MainModule access denied for elevated processes — fall through to P/Invoke
				}

				if ( string.IsNullOrEmpty( path ) )
				{
					var hProcess = OpenProcess( PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id );

					if ( hProcess != IntPtr.Zero )
					{
						try
						{
							var sb = new StringBuilder( 1024 );
							var size = (uint) sb.Capacity;

							if ( QueryFullProcessImageName( hProcess, 0, sb, ref size ) )
							{
								path = sb.ToString();
							}
						}
						finally
						{
							CloseHandle( hProcess );
						}
					}
				}

				if ( !string.IsNullOrEmpty( path ) )
				{
					list.Add( new ProcessItem( process.ProcessName, path ) );
				}
			}

			return list
					.Where( p => !string.Equals( p.Path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase ) )
					.DistinctBy( p => p.Path )
					.OrderBy( p => p.Name )
					.ToList();
		} );

		_allProcesses = processes;

		SearchingText.Visibility = Visibility.Collapsed;

		ApplyFilter();
	}

	private void ApplyFilter()
	{
		var text = SearchBox.Text;

		if ( string.IsNullOrEmpty( text ) )
		{
			ProcessListBox.ItemsSource = _allProcesses;
		}
		else
		{
			ProcessListBox.ItemsSource = _allProcesses
				.Where( p => p.Name.Contains( text, StringComparison.OrdinalIgnoreCase ) ||
							 p.Path.Contains( text, StringComparison.OrdinalIgnoreCase ) )
				.ToList();
		}
	}

	private void SearchBox_TextChanged( object sender, System.Windows.Controls.TextChangedEventArgs e )
	{
		ApplyFilter();
	}

	private void SearchBox_KeyDown( object sender, KeyEventArgs e )
	{
		if ( e.Key == Key.Enter )
		{
			TryConfirmSelection();
		}
		else if ( e.Key == Key.Down )
		{
			ProcessListBox.Focus();

			if ( ProcessListBox.Items.Count > 0 && ProcessListBox.SelectedIndex < 0 )
			{
				ProcessListBox.SelectedIndex = 0;
			}
		}
	}

	private void ProcessListBox_KeyDown( object sender, KeyEventArgs e )
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

	private void OK_Click( object sender, RoutedEventArgs e )
	{
		TryConfirmSelection();
	}

	private void Cancel_Click( object sender, RoutedEventArgs e )
	{
		Close();
	}

	private void ProcessListBox_MouseDoubleClick( object sender, MouseButtonEventArgs e )
	{
		TryConfirmSelection();
	}

	private void TryConfirmSelection()
	{
		if ( ProcessListBox.SelectedItem is ProcessItem item )
		{
			SelectedPath = item.Path;
			Close();
		}
	}
}
