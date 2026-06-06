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
	private const string DefaultScope = "tts";

	private static string ResolveScope( string? scope )
	{
		if ( string.IsNullOrWhiteSpace( scope ) )
		{
			return DefaultScope;
		}

		return scope.Trim().ToLowerInvariant();
	}

	private static string GetKeyFilePath( string? scope )
	{
		var resolvedScope = ResolveScope( scope );

		return Path.Combine( App.DocumentsFolder, "ElevenLabs", $"{resolvedScope}-api-key.dat" );
	}

	/// <summary>
	/// Encrypts <paramref name="apiKey"/> with DPAPI and writes it to disk.
	/// Passing an empty string deletes the key file.
	/// </summary>
	public static void SaveKey( string apiKey ) => SaveKey( DefaultScope, apiKey );

	/// <summary>
	/// Encrypts <paramref name="apiKey"/> with DPAPI and writes it to disk under the specified key scope.
	/// Passing an empty string deletes the key file.
	/// </summary>
	public static void SaveKey( string scope, string apiKey )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( scope );
		ArgumentNullException.ThrowIfNull( apiKey );

		var keyFilePath = GetKeyFilePath( scope );

		App.Instance?.Logger.WriteLine( $"[ElevenLabsKeyStore] SaveKey: scope={scope}, length={apiKey.Trim().Length}, file={keyFilePath}" );

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			if ( File.Exists( keyFilePath ) )
			{
				File.Delete( keyFilePath );
			}

			return;
		}

		var directory = Path.GetDirectoryName( keyFilePath )!;

		Directory.CreateDirectory( directory );

		var plainBytes = Encoding.UTF8.GetBytes( apiKey );
		var cipherBytes = ProtectedData.Protect( plainBytes, null, DataProtectionScope.CurrentUser );

		File.WriteAllBytes( keyFilePath, cipherBytes );

		App.Instance?.Logger.WriteLine( $"[ElevenLabsKeyStore] SaveKey: wrote {cipherBytes.Length} cipher bytes" );
	}

	/// <summary>
	/// Reads and decrypts the API key from disk.
	/// Returns <see cref="string.Empty"/> if the key file does not exist or decryption fails.
	/// </summary>
	public static string LoadKey() => LoadKey( DefaultScope );

	/// <summary>
	/// Reads and decrypts the API key from disk for the specified key scope.
	/// Returns <see cref="string.Empty"/> if the key file does not exist or decryption fails.
	/// </summary>
	public static string LoadKey( string scope )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( scope );

		var keyFilePath = GetKeyFilePath( scope );

		App.Instance?.Logger.WriteLine( $"[ElevenLabsKeyStore] LoadKey: scope={scope}, file exists={File.Exists( keyFilePath )}, path={keyFilePath}" );

		if ( !File.Exists( keyFilePath ) )
		{
			return string.Empty;
		}

		try
		{
			var cipherBytes = File.ReadAllBytes( keyFilePath );
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
	public static bool HasKey() => HasKey( DefaultScope );

	/// <summary>Returns true if an encrypted key file exists on disk for the specified key scope.</summary>
	public static bool HasKey( string scope )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( scope );

		return File.Exists( GetKeyFilePath( scope ) );
	}
}
