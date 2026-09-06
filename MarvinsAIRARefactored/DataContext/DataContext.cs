
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Localization = MarvinsAIRARefactored.Components.Localization;

namespace MarvinsAIRARefactored.DataContext;

public class DataContext : INotifyPropertyChanged
{
	public static DataContext Instance { get; private set; } = new();

	public event PropertyChangedEventHandler? PropertyChanged;

	public void OnPropertyChanged( [CallerMemberName] string? propertyName = null )
	{
		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
	}

	public Localization Localization { get; }

	// Root VM for the RacingWheelPage FFB graph editor. Rebuilt from the currently selected graph on selection /
	// graph-management / per-context changes (see FFBGraphViewModel.RebuildFromCurrentSelection).
	public FFB.FFBGraphViewModel RacingWheelGraphViewModel { get; } = new();

	private Settings _settings;
	public Settings Settings
	{
		get => _settings;

		set
		{
			_settings = value;

			OnPropertyChanged();

			var app = App.Instance;

			app?.Logger.WriteLine( "[DataContext] Settings object changed" );
		}
	}

	public DataContext()
	{
		var app = App.Instance;

		app?.Logger.WriteLine( "[DataContext] Constructor >>>" );

		Instance = this;

		Localization = new Localization();

		Localization.Initialize();
		Localization.LoadDefaultLanguage();

		_settings = new Settings();

		app?.Logger.WriteLine( "[DataContext] <<< Constructor" );
	}
}
