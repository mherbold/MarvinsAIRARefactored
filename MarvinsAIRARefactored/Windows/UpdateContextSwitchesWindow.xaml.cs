
using System.ComponentModel;
using System.Windows;

using MarvinsAIRARefactored.DataContext;

namespace MarvinsAIRARefactored.Windows;

public partial class UpdateContextSwitchesWindow : Window
{
	private readonly ContextSwitches _contextSwitches;

	private readonly bool _perCar;
	private readonly bool _perTrack;
	private readonly bool _perTrackConfiguration;
	private readonly bool _perWetDry;

	public UpdateContextSwitchesWindow( ContextSwitches contextSwitches )
	{
		var app = App.Instance!;

		app.MainWindow.MakeWindowVisible();

		InitializeComponent();

		Classes.WindowScaler.ApplyAppUIScale( this );

		DataContext = contextSwitches;

		_contextSwitches = contextSwitches;

		_perCar = contextSwitches.PerCar;
		_perTrack = contextSwitches.PerTrack;
		_perTrackConfiguration = contextSwitches.PerTrackConfiguration;
		_perWetDry = contextSwitches.PerWetDry;

		// the switches gate their children through IsEnabled, but a child that is already on has to actually be
		// turned off when its parent is switched off - the setters are equality guarded, so normalizing from here
		// cannot loop back on itself
		_contextSwitches.PropertyChanged += ContextSwitches_PropertyChanged;
	}

	private void ContextSwitches_PropertyChanged( object? sender, PropertyChangedEventArgs e )
	{
		if ( ( ( e.PropertyName == nameof( ContextSwitches.PerCar ) ) && !_contextSwitches.PerCar ) ||
			( ( e.PropertyName == nameof( ContextSwitches.PerTrack ) ) && !_contextSwitches.PerTrack ) )
		{
			_contextSwitches.Normalize();
		}
	}

	private void Save_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		Close();
	}

	private void Cancel_MairaButton_Click( object sender, RoutedEventArgs e )
	{
		if ( DataContext is ContextSwitches contextSwitches )
		{
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

		_contextSwitches.PropertyChanged -= ContextSwitches_PropertyChanged;

		app.Logger.WriteLine( "[UpdateContextSwitchesWindow] Window closed" );

		MarvinsAIRARefactored.DataContext.DataContext.Instance.Settings.UpdateSettings( true );
	}
}
