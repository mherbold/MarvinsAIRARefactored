using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MarvinsAIRARefactored.Classes;

/// <summary>
/// Stores and retrieves the ElevenLabs API key using Windows DPAPI
/// (DataProtectionScope.CurrentUser) so it is never written to Settings.xml in plain text.
/// The encrypted blob lives at: %DOCUMENTS%\MarvinsAIRA Refactored\TTS\elevenlabs-api-key.dat
/// </summary>
public static class ElevenLabsKeyStore
{
	private static readonly string KeyFilePath =
		Path.Combine( App.DocumentsFolder, "TTS", "elevenlabs-api-key.dat" );

	/// <summary>
	/// Encrypts <paramref name="apiKey"/> with DPAPI and writes it to disk.
	/// Passing an empty string deletes the key file.
	/// </summary>
	public static void SaveKey( string apiKey )
	{
		ArgumentNullException.ThrowIfNull( apiKey );

		App.Instance?.Logger.WriteLine( $"[ElevenLabsKeyStore] SaveKey: length={apiKey.Trim().Length}, file={KeyFilePath}" );

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			if ( File.Exists( KeyFilePath ) )
			{
				File.Delete( KeyFilePath );
			}

			return;
		}

		var directory = Path.GetDirectoryName( KeyFilePath )!;

		Directory.CreateDirectory( directory );

		var plainBytes = Encoding.UTF8.GetBytes( apiKey );
		var cipherBytes = ProtectedData.Protect( plainBytes, null, DataProtectionScope.CurrentUser );

		File.WriteAllBytes( KeyFilePath, cipherBytes );

		App.Instance?.Logger.WriteLine( $"[ElevenLabsKeyStore] SaveKey: wrote {cipherBytes.Length} cipher bytes" );
	}

	/// <summary>
	/// Reads and decrypts the API key from disk.
	/// Returns <see cref="string.Empty"/> if the key file does not exist or decryption fails.
	/// </summary>
	public static string LoadKey()
	{
		App.Instance?.Logger.WriteLine( $"[ElevenLabsKeyStore] LoadKey: file exists={File.Exists( KeyFilePath )}, path={KeyFilePath}" );

		if ( !File.Exists( KeyFilePath ) )
		{
			return string.Empty;
		}

		try
		{
			var cipherBytes = File.ReadAllBytes( KeyFilePath );
			var plainBytes = ProtectedData.Unprotect( cipherBytes, null, DataProtectionScope.CurrentUser );
			var key = Encoding.UTF8.GetString( plainBytes );

			App.Instance?.Logger.WriteLine( $"[ElevenLabsKeyStore] LoadKey: decrypted length={key.Length}" );

			return key;
		}
		catch ( CryptographicException ex )
		{
			App.Instance?.Logger.WriteLine( $"[ElevenLabsKeyStore] LoadKey: DPAPI decryption failed — {ex.Message}" );

			// Key was encrypted by a different user account or the file is corrupt.
			return string.Empty;
		}
	}

	/// <summary>Returns true if an encrypted key file exists on disk.</summary>
	public static bool HasKey() => File.Exists( KeyFilePath );
}
