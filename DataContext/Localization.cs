
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;

namespace MarvinsAIRARefactored.Components;

public partial class Localization : INotifyPropertyChanged
{
	private readonly Dictionary<string, string> _languages = new() { { "default", "English" } };

	private Dictionary<string, string> _defaults = [];
	private Dictionary<string, string> _translations = [];

	public event PropertyChangedEventHandler? PropertyChanged;

	[GeneratedRegex( @"^Resources\.(?<languageCode>[a-z]{2,3}(?:-[A-Za-z0-9]+)*)\.resx$", RegexOptions.IgnoreCase, "en-US" )]
	private static partial Regex ResourceFileRegex();

	public void OnPropertyChanged( [CallerMemberName] string? propertyName = null )
	{
		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
	}

	public string this[ string key ]
	{
		get
		{
			if ( _translations.TryGetValue( key, out var value ) && ( value != string.Empty ) )
			{
				return value?.TrimEnd() ?? string.Empty;
			}
			else if ( _defaults.TryGetValue( key, out value ) && ( value != string.Empty ) )
			{
				return value?.TrimEnd() ?? string.Empty;
			}
			else
			{
				return $"!{key}!";
			}
		}
	}

	public void Initialize()
	{
		var languagesDirectory = Path.Combine( App.DocumentsFolder, "Languages" );

		if ( !Directory.Exists( languagesDirectory ) )
		{
			Directory.CreateDirectory( languagesDirectory );
		}

		var regex = ResourceFileRegex();

		var files = Directory.GetFiles( languagesDirectory, "*.resx" );

		foreach ( var file in files )
		{
			var fileName = Path.GetFileName( file );

			var match = regex.Match( fileName );

			if ( match.Success )
			{
				var resxDictionary = Misc.LoadResx( file );

				if ( resxDictionary.TryGetValue( "ThisLanguage", out var value ) )
				{
					_languages.Add( match.Groups[ "languageCode" ].Value, value );
				}
			}
		}
	}

	public void LoadLanguage( string? languageCode = "default" )
	{
		var app = App.Instance;

		app?.Logger.WriteLine( $"[Localization] Loading language: {languageCode}" );

		var languagesDirectory = Path.Combine( App.DocumentsFolder, "Languages" );

		if ( !Directory.Exists( languagesDirectory ) )
		{
			Directory.CreateDirectory( languagesDirectory );
		}

		var languageFile = ( languageCode == "default" ) ? "Resources.resx" : $"Resources.{languageCode}.resx";

		var filePath = Path.Combine( languagesDirectory, languageFile );

		if ( File.Exists( filePath ) )
		{
			app?.Logger.WriteLine( $"[Localization] Language found in user documents folder" );

			_translations = Misc.LoadResx( filePath );
		}
		else
		{
			filePath = Path.Combine( AppDomain.CurrentDomain.BaseDirectory, "Resources", languageFile );

			if ( File.Exists( filePath ) )
			{
				app?.Logger.WriteLine( $"[Localization] Language found in app folder" );

				_translations = Misc.LoadResx( filePath );
			}
			else
			{
				app?.Logger.WriteLine( $"[Localization] Language not found" );

				_translations = [];
			}
		}

		OnPropertyChanged( null );
	}

	public void LoadDefaultLanguage()
	{
		var app = App.Instance;

		app?.Logger.WriteLine( "[Localization] LoadDefaultLanguage >>>" );

		LoadLanguage();

		_defaults = _translations;

		OnPropertyChanged( null );

		app?.Logger.WriteLine( "[Localization] <<< LoadDefaultLanguage" );
	}

	public void SetLanguageComboBoxItemsSource( MairaComboBox mairaComboBox )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Localization] SetLanguageComboBoxItemsSource >>>" );

		mairaComboBox.ItemsSource = _languages;

		app.Logger.WriteLine( "[Localization] <<< SetLanguageComboBoxItemsSource" );
	}

	public string ChooseInitialLanguage()
	{
		var supportedLanguages = _languages.Keys.ToArray();

		var fullLanguageCode = CultureInfo.CurrentUICulture.Name;
		var twoLetterLanguageCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

		if ( supportedLanguages.Contains( fullLanguageCode, StringComparer.OrdinalIgnoreCase ) )
		{
			return supportedLanguages.First( s => s.Equals( fullLanguageCode, StringComparison.OrdinalIgnoreCase ) );
		}

		var baseMatch = supportedLanguages.FirstOrDefault( s => s.StartsWith( twoLetterLanguageCode + "-", StringComparison.OrdinalIgnoreCase ) );

		if ( !string.IsNullOrEmpty( baseMatch ) )
		{
			return baseMatch!;
		}

		return "default";
	}
}
