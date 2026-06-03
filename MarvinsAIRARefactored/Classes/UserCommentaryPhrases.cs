using System.IO;
using System.Text;

using Newtonsoft.Json;

namespace MarvinsAIRARefactored.Classes;

/// <summary>
/// Manages per-language user-customized commentary phrase JSON files stored in the user's
/// Documents folder (<c>Documents\MarvinsAIRA Refactored\TTS\{language}.json</c>).
///
/// The user file is a full replacement for the built-in embedded phrases for that language.
/// <see cref="CommentaryTemplates"/> prefers the user file when one exists.
/// </summary>
public static class UserCommentaryPhrases
{
	/// <summary>
	/// Returns the full path to the user's custom TTS file for <paramref name="language"/>.
	/// </summary>
	public static string GetUserFilePath( string language )
		=> Path.Combine( App.DocumentsFolder, "TTS", $"{language}.json" );

	/// <summary>
	/// Returns <c>true</c> if a user-customized file exists for <paramref name="language"/>.
	/// </summary>
	public static bool UserFileExists( string language )
		=> File.Exists( GetUserFilePath( language ) );

	/// <summary>
	/// Loads the user-customized phrase dictionary for <paramref name="language"/>,
	/// or <c>null</c> if no user file exists.
	/// </summary>
	public static Dictionary<string, string[]>? Load( string language )
	{
		var path = GetUserFilePath( language );

		if ( !File.Exists( path ) )
		{
			return null;
		}

		try
		{
			var json = File.ReadAllText( path, Encoding.UTF8 );

			return JsonConvert.DeserializeObject<Dictionary<string, string[]>>( json );
		}
		catch ( Exception ex )
		{
			App.Instance?.Logger.WriteLine( $"[UserCommentaryPhrases] Failed to load '{path}': {ex.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Saves <paramref name="phrases"/> as the user-customized file for <paramref name="language"/>.
	/// Creates the TTS folder under Documents if it does not already exist.
	/// </summary>
	public static void Save( string language, Dictionary<string, string[]> phrases )
	{
		var dir = Path.Combine( App.DocumentsFolder, "TTS" );

		Directory.CreateDirectory( dir );

		var path = GetUserFilePath( language );
		var json = JsonConvert.SerializeObject( phrases, Formatting.Indented );

		File.WriteAllText( path, json, Encoding.UTF8 );

		App.Instance?.Logger.WriteLine( $"[UserCommentaryPhrases] Saved custom phrases for '{language}' to '{path}'." );
	}

	/// <summary>
	/// Deletes the user-customized file for <paramref name="language"/>, reverting to the
	/// built-in embedded phrases.
	/// </summary>
	public static void Delete( string language )
	{
		var path = GetUserFilePath( language );

		if ( File.Exists( path ) )
		{
			File.Delete( path );
			App.Instance?.Logger.WriteLine( $"[UserCommentaryPhrases] Deleted custom phrases for '{language}'." );
		}
	}
}
