using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

using MarvinsAIRARefactored.Classes;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MarvinsAIRARefactored.Components;

/// <summary>
/// Handles all ElevenLabs TTS requests: cache lookup, HTTP calls, and playback hand-off to AudioManager.
/// Requests are queued internally and processed one at a time so overlapping calls never happen.
/// </summary>
public sealed class TextToSpeech : IDisposable
{
	// -------------------------------------------------------------------------
	// Constants
	// -------------------------------------------------------------------------

	private const string ApiBaseUrl = "https://api.elevenlabs.io";

	private static readonly string CacheDirectory =
		Path.Combine( App.DocumentsFolder, "TTS", "Cache" );

	// -------------------------------------------------------------------------
	// HTTP client (shared; Authorization header swapped on each key change)
	// -------------------------------------------------------------------------

	private static readonly HttpClient _httpClient = new()
	{
		Timeout = TimeSpan.FromSeconds( 10 )
	};

	// -------------------------------------------------------------------------
	// Priority queue processed by a single background consumer
	// -------------------------------------------------------------------------

	/// <summary>Lower numeric value = higher urgency (1 is most urgent).</summary>
	private readonly Channel<SpeechRequest> _queue =
		Channel.CreateBounded<SpeechRequest>( new BoundedChannelOptions( 64 )
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true
		} );

	private CancellationTokenSource _cts = new();
	private Task? _consumerTask;

	// Tracks the slot currently being played so interruption is slot-specific
	private volatile int _playingSlotIndex = -1;
	private CancellationTokenSource? _playbackCts;

	// -------------------------------------------------------------------------
	// Public surface
	// -------------------------------------------------------------------------

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
	/// Enqueues a TTS request for the given voice slot and text.
	/// Returns immediately; playback happens asynchronously.
	/// </summary>
	/// <param name="slotIndex">Index into <see cref="DataContext.Settings.VoiceSlots"/> (0–4).</param>
	/// <param name="text">The text to speak. May include ElevenLabs emotion tags, e.g. [excitedly].</param>
	/// <param name="priority">1 = most urgent (spotter), 4 = low (colour commentary).</param>
	public void Enqueue( int slotIndex, string text, int priority = 3 )
	{
		ArgumentNullException.ThrowIfNull( text );

		var settings = DataContext.DataContext.Instance.Settings;

		if ( !settings.TtsEnabled )
		{
			return;
		}

		var slots = settings.VoiceSlots;

		if ( slotIndex < 0 || slotIndex >= slots.Count )
		{
			return;
		}

		var slot = slots[ slotIndex ];

		if ( !slot.Enabled || string.IsNullOrWhiteSpace( slot.VoiceId ) )
		{
			return;
		}

		var request = new SpeechRequest( slotIndex, slot.VoiceId, settings.TtsLanguage, text, priority,
			slot.Stability, slot.Style, slot.SimilarityBoost, slot.SpeakerBoost );

		// Non-blocking try-write; channel drops oldest on overflow
		_queue.Writer.TryWrite( request );
	}

	/// <summary>
	/// Verifies the stored API key and probes each permission required by the app.
	/// Returns a <see cref="KeyVerificationResult"/> describing the outcome.
	/// </summary>
	public async Task<KeyVerificationResult> VerifyKeyAsync( CancellationToken cancellationToken = default )
	{
		var app = App.Instance!;
		var apiKey = DataContext.DataContext.Instance.Settings.ApiKey.Trim();

		app.Logger.WriteLine( $"[TextToSpeech] VerifyKeyAsync: key length={apiKey.Length}, empty={string.IsNullOrWhiteSpace( apiKey )}" );

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			return KeyVerificationResult.Empty;
		}

		// Probe each endpoint and classify the response.
		// ElevenLabs returns 401 "missing_permissions" for a valid key that lacks a scope,
		// and 401 "invalid_api_key" (or similar) for an unrecognized key.
		// Any non-401 response means the key was at least recognized.

		var voicesRead = await ProbePermissionAsync( apiKey, HttpMethod.Get, $"{ApiBaseUrl}/v1/voices", cancellationToken );
		var modelsRead = await ProbePermissionAsync( apiKey, HttpMethod.Get, $"{ApiBaseUrl}/v1/models", cancellationToken );
		var textToSpeech = await ProbePermissionAsync( apiKey, HttpMethod.Post, $"{ApiBaseUrl}/v1/text-to-speech/21m00Tcm4TlvDq8ikWAM", cancellationToken );
		var userRead = await ProbePermissionAsync( apiKey, HttpMethod.Get, $"{ApiBaseUrl}/v1/user/subscription", cancellationToken );

		app.Logger.WriteLine( $"[TextToSpeech] VerifyKeyAsync: voice_read={voicesRead}, models_read={modelsRead}, text_to_speech={textToSpeech}, user_read={userRead}" );

		// If all four came back as "invalid_api_key" the key itself is not recognized.
		if ( voicesRead == PermissionStatus.InvalidKey && modelsRead == PermissionStatus.InvalidKey && textToSpeech == PermissionStatus.InvalidKey && userRead == PermissionStatus.InvalidKey )
		{
			return KeyVerificationResult.Invalid;
		}

		return new KeyVerificationResult(
			IsRecognized: true,
			VoiceRead: voicesRead,
			ModelsRead: modelsRead,
			TextToSpeech: textToSpeech,
			UserRead: userRead );
	}

	/// <summary>
	/// Sends a lightweight probe request and returns whether the key has permission for that endpoint.
	/// For POST endpoints (text-to-speech) an empty body is sent — ElevenLabs checks auth before validating the payload.
	/// </summary>
	private static async Task<PermissionStatus> ProbePermissionAsync( string apiKey, HttpMethod method, string url, CancellationToken cancellationToken )
	{
		var app = App.Instance!;

		try
		{
			using var request = new HttpRequestMessage( method, url );
			request.Headers.Add( "xi-api-key", apiKey );

			if ( method == HttpMethod.Post )
			{
				request.Content = new StringContent( "{}", Encoding.UTF8, "application/json" );
			}

			using var response = await _httpClient.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancellationToken );

			app.Logger.WriteLine( $"[TextToSpeech] ProbePermissionAsync {method} {url}: {(int) response.StatusCode}" );

			if ( response.IsSuccessStatusCode )
			{
				return PermissionStatus.Granted;
			}

			if ( response.StatusCode == System.Net.HttpStatusCode.Unauthorized )
			{
				var body = await response.Content.ReadAsStringAsync( cancellationToken );
				var detail = string.Empty;

				try
				{
					detail = JObject.Parse( body )[ "detail" ]?[ "status" ]?.Value<string>() ?? string.Empty;
				}
				catch { /* non-JSON body — treat as invalid key */ }

				return detail == "missing_permissions"
					? PermissionStatus.MissingPermission
					: PermissionStatus.InvalidKey;
			}

			// 4xx other than 401 (e.g. 422 unprocessable for empty POST body) means the key
			// was recognized and passed auth — only the request payload was rejected.
			return PermissionStatus.Granted;
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[TextToSpeech] ProbePermissionAsync exception: {ex.Message}" );
			return PermissionStatus.InvalidKey;
		}
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
		var slots = settings.VoiceSlots;

		if ( request.SlotIndex >= slots.Count )
		{
			return;
		}

		var slot = slots[ request.SlotIndex ];
		var cacheFile = GetCacheFilePath( request.SlotIndex, request.VoiceId, request.LanguageId, request.Text,
			request.Stability, request.Style, request.SimilarityBoost, request.SpeakerBoost );

		byte[]? mp3Bytes;

		if ( File.Exists( cacheFile ) )
		{
			mp3Bytes = await File.ReadAllBytesAsync( cacheFile, cancellationToken );
		}
		else
		{
			mp3Bytes = await CallApiAsync( slot, request.Text, settings.ModelId, cancellationToken );

			if ( mp3Bytes is null )
			{
				return;
			}

			// Fire-and-forget cache write — does not block playback
			_ = WriteCacheAsync( cacheFile, mp3Bytes );

			settings.SessionCharactersUsed += request.Text.Length;
		}

		var volume = MathZ.Saturate( slot.Volume * settings.MasterVolume );

		// Create a per-playback CTS linked to the consumer's shutdown token so that
		// InterruptSlot() can stop this specific clip without killing the whole pipeline.
		using var playbackCts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
		_playbackCts = playbackCts;
		_playingSlotIndex = request.SlotIndex;

		try
		{
			await app.AudioManager.PlayFromMemoryAsync( mp3Bytes, volume, playbackCts.Token );
		}
		finally
		{
			_playingSlotIndex = -1;
			_playbackCts = null;
		}
	}

	// -------------------------------------------------------------------------
	// API call
	// -------------------------------------------------------------------------

	private static async Task<byte[]?> CallApiAsync( VoiceSlotSettings slot, string text, string modelId, CancellationToken cancellationToken )
	{
		var app = App.Instance!;
		var apiKey = DataContext.DataContext.Instance.Settings.ApiKey;

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			app.Logger.WriteLine( "[TextToSpeech] No API key configured." );
			return null;
		}

		var url = $"{ApiBaseUrl}/v1/text-to-speech/{slot.VoiceId}?output_format=mp3_44100_128";

		var body = new
		{
			text,
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
			using var request = new HttpRequestMessage( HttpMethod.Post, url );
			request.Headers.Add( "xi-api-key", apiKey );
			request.Content = new StringContent( json, Encoding.UTF8, "application/json" );

			using var response = await _httpClient.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancellationToken );

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

	/// <summary>
	/// Fetches the list of available voices from the ElevenLabs API.
	/// Returns a dictionary of voice ID → voice name sorted alphabetically by name, or null if the call fails.
	/// </summary>
	public async Task<Dictionary<string, string>?> GetVoicesAsync( CancellationToken cancellationToken = default )
	{
		var app = App.Instance!;
		var apiKey = DataContext.DataContext.Instance.Settings.ApiKey;

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			return null;
		}

		try
		{
			using var request = new HttpRequestMessage( HttpMethod.Get, $"{ApiBaseUrl}/v1/voices" );
			request.Headers.Add( "xi-api-key", apiKey );

			using var response = await _httpClient.SendAsync( request, cancellationToken );

			if ( !response.IsSuccessStatusCode )
			{
				app.Logger.WriteLine( $"[TextToSpeech] GetVoicesAsync error {(int) response.StatusCode}" );
				return null;
			}

			var json = await response.Content.ReadAsStringAsync( cancellationToken );
			var root = JObject.Parse( json );
			var voices = root[ "voices" ] as JArray;

			if ( voices is null )
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

			return result.Count > 0
				? result.OrderBy( kv => kv.Value ).ToDictionary( kv => kv.Key, kv => kv.Value )
				: null;
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
	public async Task<Dictionary<string, string>?> GetTtsModelsAsync( CancellationToken cancellationToken = default )
	{
		var app = App.Instance!;
		var apiKey = DataContext.DataContext.Instance.Settings.ApiKey;

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			return null;
		}

		try
		{
			using var request = new HttpRequestMessage( HttpMethod.Get, $"{ApiBaseUrl}/v1/models" );
			request.Headers.Add( "xi-api-key", apiKey );

			using var response = await _httpClient.SendAsync( request, cancellationToken );

			if ( !response.IsSuccessStatusCode )
			{
				app.Logger.WriteLine( $"[TextToSpeech] GetTtsModelsAsync error {(int) response.StatusCode}" );
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
			app.Logger.WriteLine( $"[TextToSpeech] GetTtsModelsAsync exception: {ex.Message}" );
			return null;
		}
	}

	// -------------------------------------------------------------------------
	// Cache helpers
	// -------------------------------------------------------------------------

	/// <summary>
	/// Queries the ElevenLabs subscription endpoint and returns the character usage for the
	/// current billing period, or null if the call fails or the API key is missing.
	/// </summary>
	public async Task<SubscriptionInfo?> GetSubscriptionAsync( CancellationToken cancellationToken = default )
	{
		var app = App.Instance!;
		var apiKey = DataContext.DataContext.Instance.Settings.ApiKey;

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			return null;
		}

		try
		{
			using var request = new HttpRequestMessage( HttpMethod.Get, $"{ApiBaseUrl}/v1/user/subscription" );
			request.Headers.Add( "xi-api-key", apiKey );

			using var response = await _httpClient.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancellationToken );

			if ( !response.IsSuccessStatusCode )
			{
				app.Logger.WriteLine( $"[TextToSpeech] GetSubscriptionAsync error {(int) response.StatusCode}" );
				return null;
			}

			var json = await response.Content.ReadAsStringAsync( cancellationToken );
			var root = JObject.Parse( json );

			var used = root[ "character_count" ]?.Value<int>();
			var limit = root[ "character_limit" ]?.Value<int>();

			if ( used is null || limit is null )
			{
				app.Logger.WriteLine( "[TextToSpeech] GetSubscriptionAsync: unexpected response shape" );
				return null;
			}

			return new SubscriptionInfo( used.Value, limit.Value );
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[TextToSpeech] GetSubscriptionAsync exception: {ex.Message}" );
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
	/// </summary>
	private static string GetCacheFilePath( int slotIndex, string voiceId, string languageId, string text,
		float stability, float style, float similarityBoost, bool speakerBoost )
	{
		var normalized = NormalizeText( text );
		var voiceSettings = $"{stability:F2}_{style:F2}_{similarityBoost:F2}_{( speakerBoost ? 1 : 0 )}";
		var hash = ComputeHash( $"{normalized}|{voiceSettings}" );
		var safeLanguageId = languageId.Replace( '/', '_' ).Replace( '\\', '_' );
		var fileName = $"{slotIndex}_{voiceId}_{safeLanguageId}_{hash}.mp3";

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

	private sealed record SpeechRequest( int SlotIndex, string VoiceId, string LanguageId, string Text, int Priority,
		float Stability, float Style, float SimilarityBoost, bool SpeakerBoost );
}

/// <summary>Character usage for the current ElevenLabs billing period.</summary>
public sealed record SubscriptionInfo( int CharactersUsed, int CharacterLimit )
{
	public int CharactersRemaining => Math.Max( 0, CharacterLimit - CharactersUsed );
	public double PercentUsed => CharacterLimit > 0 ? CharactersUsed * 100.0 / CharacterLimit : 0.0;
}

/// <summary>Outcome of a single ElevenLabs endpoint permission probe.</summary>
public enum PermissionStatus
{
	/// <summary>The endpoint returned success (or a non-auth error, meaning auth passed).</summary>
	Granted,

	/// <summary>The key is recognized but lacks the required scope for this endpoint.</summary>
	MissingPermission,

	/// <summary>The key was not recognized by ElevenLabs.</summary>
	InvalidKey
}

/// <summary>Structured result returned by <see cref="TextToSpeech.VerifyKeyAsync"/>.</summary>
public sealed record KeyVerificationResult(
	bool IsRecognized,
	PermissionStatus VoiceRead,
	PermissionStatus ModelsRead,
	PermissionStatus TextToSpeech,
	PermissionStatus UserRead )
{
	/// <summary>All four required permissions are granted.</summary>
	public bool IsFullyFunctional =>
		IsRecognized &&
		VoiceRead == PermissionStatus.Granted &&
		ModelsRead == PermissionStatus.Granted &&
		TextToSpeech == PermissionStatus.Granted &&
		UserRead == PermissionStatus.Granted;

	/// <summary>The API key field was empty — nothing was sent to ElevenLabs.</summary>
	public static readonly KeyVerificationResult Empty =
		new( false, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey );

	/// <summary>ElevenLabs rejected the key as unrecognized.</summary>
	public static readonly KeyVerificationResult Invalid =
		new( false, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey );
}
