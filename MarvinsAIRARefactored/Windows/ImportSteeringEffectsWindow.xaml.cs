
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using Brush = System.Windows.Media.Brush;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

using MarvinsAIRARefactored.DataContext;

namespace MarvinsAIRARefactored.Windows;

/// <summary>
/// The import summary for the per-car steering effects files picked on the steering effects page. Lists every file
/// with its car, author and what importing it does (a new car profile, or replacing the car's current settings) or
/// why it is rejected, lets the user include the FFB graph node settings (off to begin with - those strengths are
/// personal to each wheel), and names the settings that will be switched to per car. Import applies the valid files
/// through <see cref="Settings.ImportSteeringEffects"/>; Cancel changes nothing.
/// </summary>
public partial class ImportSteeringEffectsWindow : Window
{
	/// <summary>One row of the file list.</summary>
	public sealed class FileRow
	{
		public required string CarLabel { get; init; }
		public required string Detail { get; init; }
		public required string Status { get; init; }
		public required Brush StatusBrush { get; init; }
		public required string Warnings { get; init; }

		public Visibility WarningsVisibility { get => ( Warnings.Length > 0 ) ? Visibility.Visible : Visibility.Collapsed; }
	}

	private readonly IReadOnlyList<SteeringEffectsImportCandidate> _candidates;

	private bool _imported = false;

	private ImportSteeringEffectsWindow( IReadOnlyList<SteeringEffectsImportCandidate> candidates )
	{
		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		_candidates = candidates;

		var hasGraphNodes = _candidates.Any( candidate => candidate.IsValid && candidate.HasGraphNodes );

		IncludeGraphNodes_Grid.Visibility = hasGraphNodes ? Visibility.Visible : Visibility.Collapsed;

		Import_MairaButton.IsEnabled = _candidates.Any( candidate => candidate.IsValid );

		RebuildRows();
	}

	/// <summary>Shows the summary and returns true when the files were imported.</summary>
	public static bool ShowModal( IReadOnlyList<SteeringEffectsImportCandidate> candidates )
	{
		var dialog = new ImportSteeringEffectsWindow( candidates )
		{
			Owner = System.Windows.Application.Current?.MainWindow
		};

		dialog.ShowDialog();

		return dialog._imported;
	}

	private bool IncludeGraphNodes { get => ( IncludeGraphNodes_Grid.Visibility == Visibility.Visible ) && IncludeGraphNodes_MairaSwitch.IsOn; }

	private void RebuildRows()
	{
		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		var errorBrush = (Brush) FindResource( "Brush.Status.Error.Text" );
		var statusBrush = (Brush) FindResource( "Brush.Foreground.Secondary" );

		var includeGraphNodes = IncludeGraphNodes;

		var rows = new List<FileRow>();

		foreach ( var candidate in _candidates )
		{
			var detail = candidate.FileName;

			if ( candidate.Author != string.Empty )
			{
				detail = $"{detail} · {string.Format( localization[ "ImportSteeringEffectsAuthor" ], candidate.Author )}";
			}

			string status;

			if ( !candidate.IsValid )
			{
				status = candidate.ErrorMessage;
			}
			else
			{
				status = localization[ candidate.CarHasProfile ? "ImportSteeringEffectsReplacesCar" : "ImportSteeringEffectsNewCar" ];
			}

			var warnings = candidate.IsValid ? candidate.Warnings.ToList() : [];

			if ( candidate.IsValid && includeGraphNodes )
			{
				warnings.AddRange( candidate.GraphWarnings );
			}

			rows.Add( new FileRow
			{
				CarLabel = ( candidate.CarLabel != string.Empty ) ? candidate.CarLabel : candidate.FileName,
				Detail = detail,
				Status = status,
				StatusBrush = candidate.IsValid ? statusBrush : errorBrush,
				Warnings = string.Join( Environment.NewLine, warnings )
			} );
		}

		Files_ListBox.ItemsSource = rows;

		var scopeChanges = settings.DescribeSteeringEffectsImportScopeChanges( _candidates, includeGraphNodes );

		if ( scopeChanges.Count > 0 )
		{
			ScopeChanges_TextBlock.Text = $"{localization[ "ImportSteeringEffectsScopeChanges" ]}{Environment.NewLine}{string.Join( Environment.NewLine, scopeChanges.Select( label => $"• {label}" ) )}";
			ScopeChanges_TextBlock.Visibility = Visibility.Visible;
		}
		else
		{
			ScopeChanges_TextBlock.Visibility = Visibility.Collapsed;
		}
	}

	private void IncludeGraphNodes_MairaSwitch_Toggled( object sender, EventArgs e )
	{
		RebuildRows();
	}

	// the wrapping label beside the switch toggles it, like the switch's own label would
	private void IncludeGraphNodes_TextBlock_MouseDown( object sender, MouseButtonEventArgs e )
	{
		IncludeGraphNodes_MairaSwitch.IsOn = !IncludeGraphNodes_MairaSwitch.IsOn;

		RebuildRows();
	}

	private void Import_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		var app = App.Instance!;

		var settings = MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings;
		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		app.Logger.WriteLine( "[ImportSteeringEffectsWindow] Import >>>" );

		try
		{
			settings.ImportSteeringEffects( _candidates, IncludeGraphNodes );

			_imported = true;
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[ImportSteeringEffectsWindow] Import failed: {exception.Message}" );

			ErrorWindow.ShowModal( localization[ "ImportSteeringEffectsFailed" ], exception );
		}

		app.Logger.WriteLine( "[ImportSteeringEffectsWindow] <<< Import" );

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
