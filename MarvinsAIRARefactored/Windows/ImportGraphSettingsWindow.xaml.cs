
using System.Windows;

namespace MarvinsAIRARefactored.Windows;

// Shown when an imported graph matches one the user already has (same GraphId). Lets the user apply the file's
// per-module settings onto their existing graph - for the current car/track context, the baseline (default), or
// both - or import it as a separate new copy instead. The current-context options are only offered when the sim is
// running with session info (otherwise the live context collapses onto the baseline and the two would be identical).
public partial class ImportGraphSettingsWindow : Window
{
	public enum Choice
	{
		Cancel,
		UpdateCurrentContext,
		UpdateBaseline,
		UpdateBoth,
		NewCopy
	}

	private Choice _choice = Choice.Cancel;

	private ImportGraphSettingsWindow( string graphName, string contextLabel, bool currentContextAvailable )
	{
		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		var localization = MarvinsAIRARefactored.DataContext.DataContext.Instance.Localization;

		Message_TextBlock.Text = string.Format( localization[ "ImportGraphSettingsMessage" ], graphName );

		UpdateCurrentContext_MairaButton.Label = string.Format( localization[ "ImportGraphUpdateCurrentContext" ], contextLabel );
		UpdateBaseline_MairaButton.Label = localization[ "ImportGraphUpdateBaseline" ];
		UpdateBoth_MairaButton.Label = string.Format( localization[ "ImportGraphUpdateBoth" ], contextLabel );
		NewCopy_MairaButton.Label = localization[ "ImportGraphNewCopy" ];
		Cancel_MairaButton.Label = localization[ "Cancel" ];

		// No live context distinct from baseline (sim not running / no session info) - hide the current-context
		// options entirely; updating "the current car" would be identical to updating the baseline.
		if ( !currentContextAvailable )
		{
			UpdateCurrentContext_MairaButton.Visibility = Visibility.Collapsed;
			UpdateBoth_MairaButton.Visibility = Visibility.Collapsed;
		}
	}

	public static Choice ShowModal( string graphName, string contextLabel, bool currentContextAvailable )
	{
		var dialog = new ImportGraphSettingsWindow( graphName, contextLabel, currentContextAvailable )
		{
			Owner = System.Windows.Application.Current?.MainWindow
		};

		dialog.ShowDialog();

		return dialog._choice;
	}

	private void UpdateCurrentContext_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		_choice = Choice.UpdateCurrentContext;

		Close();
	}

	private void UpdateBaseline_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		_choice = Choice.UpdateBaseline;

		Close();
	}

	private void UpdateBoth_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		_choice = Choice.UpdateBoth;

		Close();
	}

	private void NewCopy_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		_choice = Choice.NewCopy;

		Close();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		_choice = Choice.Cancel;

		Close();
	}
}
