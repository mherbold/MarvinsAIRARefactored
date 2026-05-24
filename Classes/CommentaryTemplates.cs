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
	/// Ensures all shipped language files exist in the TTS documents folder, then
	/// loads the template set for <paramref name="language"/> (e.g. <c>"en-US"</c>).
	/// Falls back to <c>en-US</c> if the requested language is unavailable.
	/// </summary>
	public void Initialize( string language )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[CommentaryTemplates] Initialize >>>" );

		var loaded = LoadLanguage( language, app );

		if ( loaded == null && language != "en-US" )
		{
			app.Logger.WriteLine( $"[CommentaryTemplates] Language '{language}' not found, falling back to en-US" );
			loaded = LoadLanguage( "en-US", app );
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

	// -------------------------------------------------------------------------
	// Load helper
	// -------------------------------------------------------------------------

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
