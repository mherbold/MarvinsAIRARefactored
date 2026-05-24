
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MarvinsAIRARefactored.Components;

public partial class Localization : INotifyPropertyChanged
{
	private const string ResourcePrefix = "MarvinsAIRARefactored.Resources.Resources";

	private readonly Dictionary<string, string> _languages = new() { { "default", "English (United States)" } };
	public Dictionary<string, string> Languages { get => _languages; }

	private Dictionary<string, string> _defaults = [];
	private Dictionary<string, string> _translations = [];

	public event PropertyChangedEventHandler? PropertyChanged;

	[GeneratedRegex( @"^MarvinsAIRARefactored\.Resources\.Resources\.(?<languageCode>[a-z]{2,3}(?:-[A-Za-z0-9]+)*)\.resources$", RegexOptions.IgnoreCase, "en-US" )]
	private static partial Regex EmbeddedResourceRegex();

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
				return value?.Trim() ?? string.Empty;
			}
			else if ( _defaults.TryGetValue( key, out value ) && ( value != string.Empty ) )
			{
				return value?.Trim() ?? string.Empty;
			}
			else
			{
				return $"!{key}!";
			}
		}
	}

	public void Initialize()
	{
		var assembly = Assembly.GetExecutingAssembly();
		var regex = EmbeddedResourceRegex();

		foreach ( var resourceName in assembly.GetManifestResourceNames() )
		{
			var match = regex.Match( resourceName );

			if ( match.Success )
			{
				using var stream = assembly.GetManifestResourceStream( resourceName );

				if ( stream != null )
				{
					var resxDictionary = LoadResourcesFromStream( stream );

					if ( resxDictionary.TryGetValue( "ThisLanguage", out var value ) )
					{
						_languages.Add( match.Groups[ "languageCode" ].Value, value );
					}
				}
			}
		}
	}

	public void LoadLanguage( string? languageCode = "default" )
	{
		var app = App.Instance;

		app?.Logger.WriteLine( $"[Localization] Loading language: {languageCode}" );

		var assembly = Assembly.GetExecutingAssembly();

		var resourceName = ( languageCode == "default" )
			? $"{ResourcePrefix}.resources"
			: $"{ResourcePrefix}.{languageCode}.resources";

		using var stream = assembly.GetManifestResourceStream( resourceName );

		if ( stream != null )
		{
			app?.Logger.WriteLine( "[Localization] Language found in embedded resources" );

			_translations = LoadResourcesFromStream( stream );
		}
		else
		{
			app?.Logger.WriteLine( "[Localization] Language not found" );

			_translations = [];
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

	private static Dictionary<string, string> LoadResourcesFromStream( Stream stream )
	{
		var dictionary = new Dictionary<string, string>();

		using var reader = new ResourceReader( stream );

		foreach ( System.Collections.DictionaryEntry entry in reader )
		{
			var key = entry.Key?.ToString();
			var value = entry.Value?.ToString();

			if ( key != null && value != null )
			{
				dictionary[ key ] = value;
			}
		}

		return dictionary;
	}
}
