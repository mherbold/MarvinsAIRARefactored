using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

using MarvinsAIRARefactored.Classes;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MarvinsAIRARefactored.Components;

/// <summary>
/// Handles all ElevenLabs TTS requests: cache lookup, HTTP calls, and playback hand-off to AudioManager.
/// Requests are queued internally and processed one at a time so overlapping calls never happen.
/// </summary>
public sealed partial class TextToSpeech : IDisposable
{
	// -------------------------------------------------------------------------
	// Constants
	// -------------------------------------------------------------------------

	private static readonly string CacheDirectory = Path.Combine( App.DocumentsFolder, "TTS", "Cache" );

	// -------------------------------------------------------------------------
	// Priority queue processed by a single background consumer
	// -------------------------------------------------------------------------

	/// <summary>Lower numeric value = higher urgency (1 is most urgent).</summary>
	private readonly Channel<SpeechRequest> _queue = Channel.CreateBounded<SpeechRequest>( new BoundedChannelOptions( 64 ) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true } );

	private CancellationTokenSource _cts = new();
	private Task? _consumerTask;

	// Tracks the slot currently being played so interruption is slot-specific
	private volatile int _playingSlotIndex = -1;
	private volatile int _playingPriority = int.MaxValue;
	private CancellationTokenSource? _playbackCts;

	// -------------------------------------------------------------------------
	// Public surface
	// -------------------------------------------------------------------------

	public event Action<CancellationToken>? UpdateSubscriptionUsage;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[TextToSpeech] Initialize >>>" );

		Directory.CreateDirectory( CacheDirectory );

		_cts = new CancellationTokenSource();
		_consumerTask = Task.Run( () => ConsumeQueueAsync( _cts.Token ) );

		app.Logger.WriteLine( "[TextToSpeech] <<< Initialize" );
	}

	/// <summary>
	/// Immediately stops audio playback if the given slot is currently speaking.
	/// Safe to call from any thread.
	/// </summary>
	public void InterruptSlot( int slotIndex )
	{
		if ( _playingSlotIndex == slotIndex )
		{
			_playbackCts?.Cancel();
		}
	}

	/// <summary>
	/// Returns true if the given voice slot is currently playing audio.
	/// Safe to call from any thread.
	/// </summary>
	public bool IsSlotPlaying( int slotIndex ) => _playingSlotIndex == slotIndex;

	/// <summary>
	/// Returns the priority of the audio currently playing on the given slot,
	/// or <see cref="int.MaxValue"/> if nothing is playing.
	/// Lower value = higher urgency (1 is most urgent).
	/// </summary>
	public int GetSlotPlayingPriority( int slotIndex ) => _playingSlotIndex == slotIndex ? _playingPriority : int.MaxValue;

	/// <summary>
	/// Enqueues a TTS request for the given voice slot and text.
	/// Returns immediately; playback happens asynchronously.
	/// </summary>
	/// <param name="slotIndex">Index into <see cref="DataContext.Settings.CommentaryVoiceSlots"/> (0–4).</param>
	/// <param name="text">The text to speak. May include ElevenLabs emotion tags, e.g. [excitedly].</param>
	/// <param name="priority">1 = most urgent (spotter), 4 = low (colour commentary).</param>
	public void Enqueue( int slotIndex, string text, int priority = 3 )
	{
		ArgumentNullException.ThrowIfNull( text );

		var settings = DataContext.DataContext.Instance.Settings;

		if ( !settings.CommentaryEnabled )
		{
			return;
		}

		var slots = settings.CommentaryVoiceSlots;

		if ( slotIndex < 0 || slotIndex >= slots.Count )
		{
			return;
		}

		var slot = slots[ slotIndex ];

		if ( !slot.Enabled || string.IsNullOrWhiteSpace( slot.VoiceId ) )
		{
			return;
		}

		var request = new SpeechRequest( slotIndex, slot.VoiceId, settings.CommentaryElevenLabsLanguage, text, priority, slot.Stability, slot.Style, slot.SimilarityBoost, slot.SpeakerBoost );

		// Non-blocking try-write; channel drops oldest on overflow
		_queue.Writer.TryWrite( request );
	}

	/// <summary>
	/// Verifies the stored API key and probes each permission required by the app.
	/// Returns a <see cref="ElevenLabs.TtsKeyVerificationResult"/> describing the outcome.
	/// </summary>
	public static async Task<ElevenLabs.TtsKeyVerificationResult> VerifyKeyAsync( CancellationToken cancellationToken = default )
	{
		var app = App.Instance!;
		var apiKey = DataContext.DataContext.Instance.Settings.CommentaryElevenLabsApiKey.Trim();

		app.Logger.WriteLine( $"[TextToSpeech] VerifyKeyAsync: key length={apiKey.Length}, empty={string.IsNullOrWhiteSpace( apiKey )}" );

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			return ElevenLabs.TtsKeyVerificationResult.Empty;
		}

		// Probe each endpoint and classify the response.
		// ElevenLabs returns 401 "missing_permissions" for a valid key that lacks a scope,
		// and 401 "invalid_api_key" (or similar) for an unrecognized key.
		// Any non-401 response means the key was at least recognized.

		var voicesRead = await ElevenLabs.ProbePermissionAsync( apiKey, HttpMethod.Get, "https://api.elevenlabs.io/v1/voices", cancellationToken );
		var modelsRead = await ElevenLabs.ProbePermissionAsync( apiKey, HttpMethod.Get, "https://api.elevenlabs.io/v1/models", cancellationToken );
		var textToSpeech = await ElevenLabs.ProbePermissionAsync( apiKey, HttpMethod.Post, "https://api.elevenlabs.io/v1/text-to-speech/21m00Tcm4TlvDq8ikWAM", cancellationToken );
		var userRead = await ElevenLabs.ProbePermissionAsync( apiKey, HttpMethod.Get, "https://api.elevenlabs.io/v1/user/subscription", cancellationToken );

		app.Logger.WriteLine( $"[TextToSpeech] VerifyKeyAsync: voice_read={voicesRead}, models_read={modelsRead}, text_to_speech={textToSpeech}, user_read={userRead}" );

		// If all four came back as "invalid_api_key" the key itself is not recognized.
		if ( voicesRead == ElevenLabs.PermissionStatus.InvalidKey && modelsRead == ElevenLabs.PermissionStatus.InvalidKey && textToSpeech == ElevenLabs.PermissionStatus.InvalidKey && userRead == ElevenLabs.PermissionStatus.InvalidKey )
		{
			return ElevenLabs.TtsKeyVerificationResult.Invalid;
		}

		return new ElevenLabs.TtsKeyVerificationResult( IsRecognized: true, VoiceRead: voicesRead, ModelsRead: modelsRead, TextToSpeech: textToSpeech, UserRead: userRead );
	}

	/// <summary>
	/// Sends a lightweight probe request and returns whether the key has permission for that endpoint.
	/// For POST endpoints (text-to-speech) an empty body is sent — ElevenLabs checks auth before validating the payload.
	/// </summary>
	public static async Task<ElevenLabs.PermissionStatus> ProbePermissionAsync( string apiKey, HttpMethod method, string url, CancellationToken cancellationToken )
	{
		return await ElevenLabs.ProbePermissionAsync( apiKey, method, url, cancellationToken );
	}

	// -------------------------------------------------------------------------
	// Queue consumer
	// -------------------------------------------------------------------------

	private async Task ConsumeQueueAsync( CancellationToken cancellationToken )
	{
		try
		{
			await foreach ( var request in _queue.Reader.ReadAllAsync( cancellationToken ) )
			{
				try
				{
					await ProcessRequestAsync( request, cancellationToken );
				}
				catch ( OperationCanceledException )
				{
					break;
				}
				catch ( Exception ex )
				{
					App.Instance!.Logger.WriteLine( $"[TextToSpeech] Unhandled error processing request: {ex.Message}" );
				}
			}
		}
		catch ( OperationCanceledException )
		{
			// Normal shutdown — cancellation token was signalled.
		}
	}

	private async Task ProcessRequestAsync( SpeechRequest request, CancellationToken cancellationToken )
	{
		var app = App.Instance!;
		var settings = DataContext.DataContext.Instance.Settings;
		var slots = settings.CommentaryVoiceSlots;

		if ( request.SlotIndex >= slots.Count )
		{
			return;
		}

		var slot = slots[ request.SlotIndex ];
		var cacheFile = GetCacheFilePath( request.SlotIndex, request.VoiceId, request.LanguageId, request.Text, request.Stability, request.Style, request.SimilarityBoost, request.SpeakerBoost );

		byte[]? mp3Bytes;

		if ( File.Exists( cacheFile ) )
		{
			mp3Bytes = await File.ReadAllBytesAsync( cacheFile, cancellationToken );
		}
		else
		{
			mp3Bytes = await CallApiAsync( slot, request.Text, settings.CommentaryElevenLabsModelId, cancellationToken );

			if ( mp3Bytes is null )
			{
				return;
			}

			// Fire-and-forget cache write — does not block playback
			_ = WriteCacheAsync( cacheFile, mp3Bytes );

			// Update subscription usage after successful TTS call
			UpdateSubscriptionUsage?.Invoke( cancellationToken );
		}

		var volume = MathZ.Saturate( slot.Volume * settings.CommentaryMasterVolume );

		// Create a per-playback CTS linked to the consumer's shutdown token so that
		// InterruptSlot() can stop this specific clip without killing the whole pipeline.
		using var playbackCts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );

		_playbackCts = playbackCts;
		_playingSlotIndex = request.SlotIndex;
		_playingPriority = request.Priority;

		try
		{
			await app.AudioManager.PlayFromMemoryAsync( mp3Bytes, volume, playbackCts.Token );
		}
		finally
		{
			_playingSlotIndex = -1;
			_playingPriority = int.MaxValue;
			_playbackCts = null;
		}
	}

	// -------------------------------------------------------------------------
	// API call
	// -------------------------------------------------------------------------

	private static async Task<byte[]?> CallApiAsync( VoiceSlotSettings slot, string text, string modelId, CancellationToken cancellationToken )
	{
		var app = App.Instance!;
		var apiKey = DataContext.DataContext.Instance.Settings.CommentaryElevenLabsApiKey;

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			app.Logger.WriteLine( "[TextToSpeech] No API key configured." );

			return null;
		}

		var url = $"https://api.elevenlabs.io/v1/text-to-speech/{slot.VoiceId}?output_format=mp3_44100_128";

		// Emotion/direction tags (e.g. [urgently]) are only supported by eleven_v3; strip them for all other models.
		var spokenText = modelId.StartsWith( "eleven_v3", StringComparison.OrdinalIgnoreCase ) ? text : StripEmotionTags( text );

		var body = new
		{
			text = spokenText,
			model_id = modelId,
			voice_settings = new
			{
				stability = slot.Stability,
				similarity_boost = slot.SimilarityBoost,
				style = slot.Style,
				use_speaker_boost = slot.SpeakerBoost
			}
		};

		var json = JsonConvert.SerializeObject( body );

		try
		{
			app.Logger.WriteLine( $"[TextToSpeech] Sending TTS request to ElevenLabs API: {body.text}" );

			using var request = new HttpRequestMessage( HttpMethod.Post, url );

			request.Headers.Add( "xi-api-key", apiKey );
			request.Content = new StringContent( json, Encoding.UTF8, "application/json" );

			using var response = await ElevenLabs.HttpClient.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancellationToken );

			if ( !response.IsSuccessStatusCode )
			{
				var error = await response.Content.ReadAsStringAsync( cancellationToken );

				app.Logger.WriteLine( $"[TextToSpeech] API error {(int) response.StatusCode}: {error}" );

				return null;
			}

			return await response.Content.ReadAsByteArrayAsync( cancellationToken );
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[TextToSpeech] CallApiAsync exception: {ex.Message}" );

			return null;
		}
	}

	/// <summary>Removes ElevenLabs emotion/direction tags such as [urgently] from text for models that do not support them.</summary>
	private static string StripEmotionTags( string text ) => StripEmotionTagsRegex().Replace( text, string.Empty ).TrimStart();

	/// <summary>
	/// Fetches the list of available voices from the ElevenLabs API.
	/// Returns a dictionary of voice ID → voice name sorted alphabetically by name, or null if the call fails.
	/// </summary>
	public static async Task<Dictionary<string, string>?> GetVoicesAsync( CancellationToken cancellationToken = default )
	{
		var app = App.Instance!;
		var apiKey = DataContext.DataContext.Instance.Settings.CommentaryElevenLabsApiKey;

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			return null;
		}

		try
		{
			using var request = new HttpRequestMessage( HttpMethod.Get, "https://api.elevenlabs.io/v1/voices" );

			request.Headers.Add( "xi-api-key", apiKey );

			using var response = await ElevenLabs.HttpClient.SendAsync( request, cancellationToken );

			if ( !response.IsSuccessStatusCode )
			{
				app.Logger.WriteLine( $"[TextToSpeech] GetVoicesAsync error {(int) response.StatusCode}" );

				return null;
			}

			var json = await response.Content.ReadAsStringAsync( cancellationToken );
			var root = JObject.Parse( json );

			if ( root[ "voices" ] is not JArray voices )
			{
				return null;
			}

			var result = new Dictionary<string, string>();

			foreach ( var voice in voices )
			{
				var id = voice[ "voice_id" ]?.Value<string>();
				var name = voice[ "name" ]?.Value<string>();

				if ( !string.IsNullOrWhiteSpace( id ) && !string.IsNullOrWhiteSpace( name ) )
				{
					result[ id ] = name;
				}
			}

			return result.Count > 0 ? result.OrderBy( kv => kv.Value ).ToDictionary( kv => kv.Key, kv => kv.Value ) : null;
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[TextToSpeech] GetVoicesAsync exception: {ex.Message}" );

			return null;
		}
	}

	/// <summary>
	/// Fetches the list of TTS-capable models from the ElevenLabs API.
	/// Returns a dictionary of model ID → display name, or null if the call fails.
	/// </summary>
	public static async Task<Dictionary<string, string>?> GetModelsAsync( CancellationToken cancellationToken = default )
	{
		var app = App.Instance!;
		var apiKey = DataContext.DataContext.Instance.Settings.CommentaryElevenLabsApiKey;

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			return null;
		}

		try
		{
			using var request = new HttpRequestMessage( HttpMethod.Get, "https://api.elevenlabs.io/v1/models" );

			request.Headers.Add( "xi-api-key", apiKey );

			using var response = await ElevenLabs.HttpClient.SendAsync( request, cancellationToken );

			if ( !response.IsSuccessStatusCode )
			{
				app.Logger.WriteLine( $"[TextToSpeech] GetModelsAsync error {(int) response.StatusCode}" );

				return null;
			}

			var json = await response.Content.ReadAsStringAsync( cancellationToken );
			var array = JArray.Parse( json );

			var models = new Dictionary<string, string>();

			foreach ( var model in array )
			{
				var canDoTts = model[ "can_do_text_to_speech" ]?.Value<bool>() ?? false;

				if ( !canDoTts )
				{
					continue;
				}

				var id = model[ "model_id" ]?.Value<string>();
				var name = model[ "name" ]?.Value<string>();

				if ( !string.IsNullOrWhiteSpace( id ) && !string.IsNullOrWhiteSpace( name ) )
				{
					models[ id ] = name;
				}
			}

			return models.Count > 0 ? models : null;
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[TextToSpeech] GetModelsAsync exception: {ex.Message}" );

			return null;
		}
	}

	// -------------------------------------------------------------------------
	// Cache helpers
	// -------------------------------------------------------------------------

	/// <summary>
	/// Builds the full cache file path for the given slot/voice/text combination.
	/// The text is normalised (trimmed, lowercased, punctuation stripped) and hashed so
	/// casing variants of identical phrases share one cache entry.
	/// Voice settings are encoded as explicit filename segments (not folded into the hash)
	/// so that cache files for a given phrase can be found and deleted across any voice
	/// configuration by matching only the text hash suffix.
	/// Format: {slotIndex}_{voiceId}_{languageId}_{stability}_{style}_{similarityBoost}_{speakerBoost}_{textHash}.mp3
	/// where stability/style/similarityBoost are 0–100 integers and speakerBoost is 0 or 1.
	/// </summary>
	private static string GetCacheFilePath( int slotIndex, string voiceId, string languageId, string text, float stability, float style, float similarityBoost, bool speakerBoost )
	{
		var textHash = ComputeHash( NormalizeText( text ) );
		var safeLanguageId = languageId.Replace( '/', '_' ).Replace( '\\', '_' );
		var stab = (int) Math.Round( stability * 100 );
		var styl = (int) Math.Round( style * 100 );
		var sim = (int) Math.Round( similarityBoost * 100 );
		var sb = speakerBoost ? 1 : 0;
		var fileName = $"{slotIndex}_{voiceId}_{safeLanguageId}_{stab:D3}_{styl:D3}_{sim:D3}_{sb}_{textHash}.mp3";

		return Path.Combine( CacheDirectory, fileName );
	}

	private static string NormalizeText( string text )
	{
		var sb = new StringBuilder( text.Length );

		foreach ( var ch in text.ToLowerInvariant().Trim() )
		{
			if ( char.IsLetterOrDigit( ch ) || ch == ' ' )
			{
				sb.Append( ch );
			}
		}

		return sb.ToString();
	}

	private static string ComputeHash( string text )
	{
		var bytes = SHA256.HashData( Encoding.UTF8.GetBytes( text ) );

		return Convert.ToHexStringLower( bytes )[ ..16 ];
	}

	/// <summary>
	/// Deletes all cached MP3 files associated with <paramref name="text"/> and <paramref name="languageId"/>.
	/// Only files generated for the specified language are removed; entries for other languages are preserved.
	/// Call this whenever a phrase is edited so the next test or live playback triggers a fresh
	/// ElevenLabs API call instead of replaying stale cached audio.
	/// </summary>
	public static void DeleteCachedPhrasesForText( string text, string languageId )
	{
		if ( !Directory.Exists( CacheDirectory ) )
		{
			return;
		}

		var textHash = ComputeHash( NormalizeText( text ) );
		var safeLanguageId = languageId.Replace( '/', '_' ).Replace( '\\', '_' );
		var matchingFiles = Directory.GetFiles( CacheDirectory, $"*_{safeLanguageId}_*_{textHash}.mp3" );

		foreach ( var path in matchingFiles )
		{
			try
			{
				File.Delete( path );

				App.Instance?.Logger.WriteLine( $"[TextToSpeech] Deleted stale cache: {Path.GetFileName( path )}" );
			}
			catch ( Exception ex )
			{
				App.Instance?.Logger.WriteLine( $"[TextToSpeech] Could not delete cache '{path}': {ex.Message}" );
			}
		}
	}

	private static async Task WriteCacheAsync( string path, byte[] bytes )
	{
		try
		{
			await File.WriteAllBytesAsync( path, bytes );
		}
		catch ( Exception ex )
		{
			App.Instance!.Logger.WriteLine( $"[TextToSpeech] Cache write failed ({path}): {ex.Message}" );
		}
	}

	// -------------------------------------------------------------------------
	// IDisposable
	// -------------------------------------------------------------------------

	public void Dispose()
	{
		_cts.Cancel();
		_queue.Writer.TryComplete();
		_consumerTask?.GetAwaiter().GetResult();
		_cts.Dispose();
	}

	// -------------------------------------------------------------------------
	// Private types
	// -------------------------------------------------------------------------

	private sealed record SpeechRequest( int SlotIndex, string VoiceId, string LanguageId, string Text, int Priority, float Stability, float Style, float SimilarityBoost, bool SpeakerBoost );

	[GeneratedRegex( @"\[[a-zA-Z ]+\]\s*" )]
	private static partial Regex StripEmotionTagsRegex();
}
