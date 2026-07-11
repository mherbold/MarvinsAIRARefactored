
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

	private string? _currentLanguageCode = null;

	// True when the current language is written right-to-left (Hebrew, Arabic, Farsi, ...).
	// Driven off the culture itself rather than a hardcoded list so it stays correct as languages are added.
	public bool IsRightToLeft
	{
		get
		{
			if ( string.IsNullOrEmpty( _currentLanguageCode ) || ( _currentLanguageCode == "default" ) )
			{
				return false;
			}

			try
			{
				return CultureInfo.GetCultureInfo( _currentLanguageCode ).TextInfo.IsRightToLeft;
			}
			catch ( CultureNotFoundException )
			{
				return false;
			}
		}
	}

	// Culture for the currently-loaded language, used for culture-correct casing
	// (Turkish dotted-I, German eszett, no-op for case-less scripts). Falls back to
	// en-US for the default/unknown language so casing still behaves sensibly.
	private CultureInfo CurrentCulture
	{
		get
		{
			if ( string.IsNullOrEmpty( _currentLanguageCode ) || ( _currentLanguageCode == "default" ) )
			{
				return CultureInfo.GetCultureInfo( "en-US" );
			}

			try
			{
				return CultureInfo.GetCultureInfo( _currentLanguageCode );
			}
			catch ( CultureNotFoundException )
			{
				return CultureInfo.InvariantCulture;
			}
		}
	}

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
			// no Trim() here — leading whitespace is significant (the unit-suffix strings like " Nm" and
			// " Hz" carry a leading space so they can be appended directly to a number); the resx values
			// are kept pre-trimmed everywhere else
			if ( _translations.TryGetValue( key, out var value ) && ( value != string.Empty ) )
			{
				return value;
			}
			else if ( _defaults.TryGetValue( key, out value ) && ( value != string.Empty ) )
			{
				return value;
			}
			else
			{
				return $"!{key}!";
			}
		}
	}

	// Uppercased view of the localized string table, cased with the active
	// language's culture. Bind in XAML as {Binding Localization.Upper[Key]} or read
	// in code as Localization.Upper["Key"]. Lets the resx hold a single normal-case
	// string instead of a second all-caps copy, and refreshes with the table since
	// it hangs off the same object that raises PropertyChanged on language change.
	public UpperCaseAccessor Upper => _upper ??= new UpperCaseAccessor( this );
	private UpperCaseAccessor? _upper;

	public sealed class UpperCaseAccessor( Localization owner )
	{
		public string this[ string key ] => owner[ key ].ToUpper( owner.CurrentCulture );
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

		if ( languageCode != _currentLanguageCode )
		{
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

			_currentLanguageCode = languageCode;
		}
		else
		{
			app?.Logger.WriteLine( "[Localization] This is already the current language - skipping." );
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
