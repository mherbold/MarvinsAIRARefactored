
using System.Windows;
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

		Localization.LoadDefaultLanguage();

		_settings = new Settings();

		app?.Logger.WriteLine( "[DataContext] <<< Constructor" );
	}
}
