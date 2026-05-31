
using System.Windows;

using MarvinsAIRARefactored.DataContext;

namespace MarvinsAIRARefactored.Windows;

public partial class UpdateContextSwitchesWindow : Window
{
	private readonly bool _perWheelbase;
	private readonly bool _perCar;
	private readonly bool _perTrack;
	private readonly bool _perTrackConfiguration;
	private readonly bool _perWetDry;

	public UpdateContextSwitchesWindow( ContextSwitches contextSwitches )
	{
		var app = App.Instance!;

		app.MainWindow.MakeWindowVisible();

		InitializeComponent();

		DataContext = contextSwitches;

		_perWheelbase = contextSwitches.PerWheelbase;
		_perCar = contextSwitches.PerCar;
		_perTrack = contextSwitches.PerTrack;
		_perTrackConfiguration = contextSwitches.PerTrackConfiguration;
		_perWetDry = contextSwitches.PerWetDry;
	}

	private void Save_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Close();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( DataContext is ContextSwitches contextSwitches )
		{
			contextSwitches.PerWheelbase = _perWheelbase;
			contextSwitches.PerCar = _perCar;
			contextSwitches.PerTrack = _perTrack;
			contextSwitches.PerTrackConfiguration = _perTrackConfiguration;
			contextSwitches.PerWetDry = _perWetDry;
		}

		Close();
	}

	private void Window_Closed( object sender, EventArgs e )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[UpdateContextSwitchesWindow] Window closed" );

		MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.UpdateSettings( true );
	}
}
