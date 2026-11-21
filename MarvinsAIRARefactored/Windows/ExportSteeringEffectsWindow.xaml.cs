
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

using MarvinsAIRARefactored.DataContext;

namespace MarvinsAIRARefactored.Windows;

/// <summary>
/// Exports the steering effects settings of the cars the user picks, one file per car (see
/// <see cref="Settings.ExportSteeringEffects"/>). The car being driven is picked to begin with. Export asks for the
/// folder, confirms before replacing files that are already there, writes the files, and opens the folder so they
/// are ready to share.
/// </summary>
public partial class ExportSteeringEffectsWindow : Window
{
	/// <summary>One row of the car list.</summary>
	public sealed class CarChoice : INotifyPropertyChanged
	{
		public required Context Car { get; init; }
		public required string Label { get; init; }

		private bool _isSelected = false;

		public bool IsSelected
		{
			get => _isSelected;

			set
			{
				if ( value != _isSelected )
				{
					_isSelected = value;

					OnPropertyChanged();
				}
			}
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		private void OnPropertyChanged( [CallerMemberName] string? propertyName = null )
		{
			PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
		}
	}

	private readonly List<CarChoice> _carChoices;

	public ExportSteeringEffectsWindow()
	{
		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var liveCar = settings.GetLiveSteeringEffectsShareCar();

		_carChoices = [ .. settings.GetSteeringEffectsShareCars().Select( car =>
		{
			var isLiveCar = ( liveCar != null ) && ( liveCar.CompareTo( car ) == 0 );

			var label = Settings.DescribeSteeringEffectsShareCar( car );

			return new CarChoice
			{
				Car = car,
				Label = isLiveCar ? string.Format( localization[ "ExportSteeringEffectsCurrentCar" ], label ) : label,
				IsSelected = isLiveCar
			};
		} ) ];

		foreach ( var carChoice in _carChoices )
		{
			carChoice.PropertyChanged += ( sender, e ) => UpdateFileNameExample();
		}

		Cars_ListBox.ItemsSource = _carChoices;

		var hasCars = _carChoices.Count > 0;

		NoCars_TextBlock.Visibility = hasCars ? Visibility.Collapsed : Visibility.Visible;

		SelectAll_MairaButton.IsEnabled = hasCars;
		SelectNone_MairaButton.IsEnabled = hasCars;
		Export_MairaButton.IsEnabled = hasCars;

		UpdateFileNameExample();
	}

	public static void ShowModal()
	{
		var dialog = new ExportSteeringEffectsWindow
		{
			Owner = System.Windows.Application.Current?.MainWindow
		};

		dialog.ShowDialog();
	}

	// "File names look like this: Ferrari 296 GT3 - Marvin.mairasteering", using the first picked car (or the first car)
	private void UpdateFileNameExample()
	{
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var exampleCar = _carChoices.FirstOrDefault( carChoice => carChoice.IsSelected )?.Car ?? _carChoices.FirstOrDefault()?.Car ?? new Context { CarName = "Ferrari 296 GT3" };

		FileNameExample_TextBlock.Text = string.Format( localization[ "ExportSteeringEffectsFileNameExample" ], Settings.BuildSteeringEffectsShareFileName( exampleCar, AuthorName_MairaTextBox.Value ?? string.Empty ) );
	}

	private void AuthorName_MairaTextBox_ValueChanged( object sender, RoutedEventArgs e )
	{
		if ( IsLoaded )
		{
			UpdateFileNameExample();
		}
	}

	private void SelectAll_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		foreach ( var carChoice in _carChoices )
		{
			carChoice.IsSelected = true;
		}

		UpdateFileNameExample();
	}

	private void SelectNone_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		foreach ( var carChoice in _carChoices )
		{
			carChoice.IsSelected = false;
		}

		UpdateFileNameExample();
	}

	private void Export_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var cars = _carChoices.Where( carChoice => carChoice.IsSelected ).Select( carChoice => carChoice.Car ).ToList();

		if ( cars.Count == 0 )
		{
			ErrorWindow.ShowModal( localization[ "ExportSteeringEffectsNoCarsPicked" ] );

			return;
		}

		// the author name box commits on lost focus - take what is typed right now
		var author = AuthorName_MairaTextBox.Value ?? string.Empty;

		settings.SteeringEffectsShareAuthorName = author;

		var folderDialog = new Microsoft.Win32.OpenFolderDialog
		{
			Title = localization[ "ExportSteeringEffectsPickFolder" ]
		};

		if ( folderDialog.ShowDialog( this ) != true )
		{
			return;
		}

		var folderPath = folderDialog.FolderName;

		var existingFileCount = cars.Count( car => File.Exists( Path.Combine( folderPath, Settings.BuildSteeringEffectsShareFileName( car, author ) ) ) );

		if ( existingFileCount > 0 )
		{
			var confirmActionWindow = new ConfirmActionWindow( localization[ "ExportSteeringEffectsReplaceTitle" ], string.Format( localization[ "ExportSteeringEffectsReplaceMessage" ], existingFileCount ) )
			{
				Owner = this
			};

			confirmActionWindow.ShowDialog();

			if ( !confirmActionWindow.Confirmed )
			{
				return;
			}
		}

		app.Logger.WriteLine( $"[ExportSteeringEffectsWindow] Exporting {cars.Count} cars >>>" );

		try
		{
			settings.ExportSteeringEffects( cars, folderPath, author );
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[ExportSteeringEffectsWindow] Export failed: {exception.Message}" );

			ErrorWindow.ShowModal( localization[ "ExportSteeringEffectsFailed" ], exception );

			return;
		}

		app.Logger.WriteLine( "[ExportSteeringEffectsWindow] <<< Exporting" );

		// show the files, ready to share
		try
		{
			Process.Start( new ProcessStartInfo( "explorer.exe", $"\"{folderPath}\"" ) { UseShellExecute = true } );
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[ExportSteeringEffectsWindow] Could not open the export folder: {exception.Message}" );
		}

		Close();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Close();
	}

	private void Window_KeyDown( object sender, KeyEventArgs e )
	{
		if ( e.Key == Key.Escape )
		{
			Close();
		}
	}
}
