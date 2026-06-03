using System.IO;
using System.Reflection;
using System.Text;

using Newtonsoft.Json;

namespace MarvinsAIRARefactored.Classes;

/// <summary>
/// Loads the per-language commentary phrase templates directly from embedded resources
/// (<c>TTS/{language}.json</c> files compiled into the assembly).
///
/// Template strings may contain <c>{token}</c> placeholders that are substituted at
/// runtime by <see cref="MarvinsAIRARefactored.Components.Commentary"/>.
/// </summary>
public sealed class CommentaryTemplates
{
	// -------------------------------------------------------------------------
	// Loaded data
	// -------------------------------------------------------------------------

	/// <summary>
	/// Maps event-key → array of phrase variants for the active language.
	/// If the requested language file was not found, falls back to <c>en-US</c>.
	/// </summary>
	public IReadOnlyDictionary<string, string[]> Phrases { get; private set; } =
		new Dictionary<string, string[]>();

	/// <summary>The language tag that was actually loaded (may differ from request if fallback occurred).</summary>
	public string LoadedLanguage { get; private set; } = "en-US";

	// -------------------------------------------------------------------------
	// Public API
	// -------------------------------------------------------------------------

	/// <summary>
	/// Loads the template set for <paramref name="language"/> (e.g. <c>"en-US"</c>).
	/// Checks the user's Documents folder first; falls back to the embedded resource.
	/// Falls back further to <c>en-US</c> if the requested language is unavailable.
	/// </summary>
	public void Initialize( string language )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[CommentaryTemplates] Initialize >>>" );

		// Prefer user-customized file over embedded resource
		var loaded = LoadUserLanguage( language, app ) ?? LoadLanguage( language, app );

		if ( loaded == null && language != "en-US" )
		{
			app.Logger.WriteLine( $"[CommentaryTemplates] Language '{language}' not found, falling back to en-US" );
			loaded = LoadUserLanguage( "en-US", app ) ?? LoadLanguage( "en-US", app );
		}

		if ( loaded != null )
		{
			Phrases = loaded.Value.phrases;
			LoadedLanguage = loaded.Value.language;
		}
		else
		{
			app.Logger.WriteLine( "[CommentaryTemplates] WARNING: No language file could be loaded — commentary will be silent." );
			Phrases = new Dictionary<string, string[]>();
			LoadedLanguage = language;
		}

		app.Logger.WriteLine( $"[CommentaryTemplates] Loaded language: {LoadedLanguage} ({Phrases.Count} event keys)" );
		app.Logger.WriteLine( "[CommentaryTemplates] <<< Initialize" );
	}

	/// <summary>
	/// Returns a random phrase variant for <paramref name="eventKey"/>, or <c>null</c>
	/// if the key is not present in the loaded template set.
	/// </summary>
	public string? GetRandomPhrase( string eventKey )
	{
		if ( !Phrases.TryGetValue( eventKey, out var variants ) || variants.Length == 0 )
		{
			return null;
		}

		return variants[ Random.Shared.Next( variants.Length ) ];
	}

	/// <summary>
	/// Returns all language tags for which a <c>TTS/{lang}.json</c> embedded resource
	/// exists. Useful for populating the language combo box.
	/// </summary>
	public static IReadOnlyList<string> GetAvailableLanguages()
	{
		var assembly = Assembly.GetExecutingAssembly();

		return assembly.GetManifestResourceNames()
			.Where( n => n.Contains( ".TTS." ) && n.EndsWith( ".json" ) )
			.Select( n => n.Split( '.' ) )
			.Where( p => p.Length >= 4 )
			.Select( p => p[ ^2 ] )
			.OrderBy( l => l )
			.ToList();
	}

	/// <summary>
	/// Loads and returns the built-in (embedded) phrase dictionary for <paramref name="language"/>
	/// without applying any user overrides. Returns <c>null</c> if the language is not found.
	/// </summary>
	public static IReadOnlyDictionary<string, string[]>? GetBuiltInPhrases( string language )
	{
		var app = App.Instance!;
		var result = LoadLanguage( language, app );

		return result?.phrases;
	}

	// -------------------------------------------------------------------------
	// Load helpers
	// -------------------------------------------------------------------------

	/// <summary>Attempts to load from the user's custom file in Documents; returns null if none exists.</summary>
	private static (IReadOnlyDictionary<string, string[]> phrases, string language)? LoadUserLanguage( string language, App app )
	{
		var dict = UserCommentaryPhrases.Load( language );

		if ( dict is null )
		{
			return null;
		}

		app.Logger.WriteLine( $"[CommentaryTemplates] Loaded user-customized phrases for '{language}' from Documents folder." );

		// Sync user file against the built-in phrases: add missing keys, remove obsolete keys.
		var builtIn = LoadLanguage( language, app )?.phrases;

		if ( builtIn is not null )
		{
			var changed = false;

			// Add keys present in the built-in file that are missing from the user file.
			foreach ( var kvp in builtIn )
			{
				if ( !dict.ContainsKey( kvp.Key ) )
				{
					dict[ kvp.Key ] = kvp.Value;
					app.Logger.WriteLine( $"[CommentaryTemplates] Added missing key '{kvp.Key}' to user phrases for '{language}'." );
					changed = true;
				}
			}

			// Remove keys in the user file that no longer exist in the built-in file.
			var obsoleteKeys = dict.Keys.Except( builtIn.Keys ).ToList();

			foreach ( var key in obsoleteKeys )
			{
				dict.Remove( key );
				app.Logger.WriteLine( $"[CommentaryTemplates] Removed obsolete key '{key}' from user phrases for '{language}'." );
				changed = true;
			}

			if ( changed )
			{
				UserCommentaryPhrases.Save( language, dict );
				app.Logger.WriteLine( $"[CommentaryTemplates] Saved updated user phrases for '{language}' after sync." );
			}
		}

		return (dict, language);
	}

	private static (IReadOnlyDictionary<string, string[]> phrases, string language)? LoadLanguage( string language, App app )
	{
		var assembly = Assembly.GetExecutingAssembly();
		var resourceName = assembly.GetManifestResourceNames()
			.FirstOrDefault( n => n.Contains( ".TTS." ) && n.EndsWith( ".json" ) &&
								  n.Contains( language ) );

		if ( resourceName is null )
		{
			return null;
		}

		try
		{
			using var stream = assembly.GetManifestResourceStream( resourceName )!;
			using var reader = new StreamReader( stream, Encoding.UTF8 );

			var dict = JsonConvert.DeserializeObject<Dictionary<string, string[]>>( reader.ReadToEnd() );

			if ( dict is null )
			{
				app.Logger.WriteLine( $"[CommentaryTemplates] '{resourceName}' deserialized to null — skipping." );
				return null;
			}

			return (dict, language);
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[CommentaryTemplates] Failed to load '{resourceName}': {ex.Message}" );
			return null;
		}
	}
}
