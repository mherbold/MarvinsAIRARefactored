
using System.Net.Http;
using System.Text;

using Newtonsoft.Json.Linq;

using MarvinsAIRARefactored.Controls;

namespace MarvinsAIRARefactored.Classes;

/// <summary>
/// Shared ElevenLabs API utilities for Text-to-Speech and Speech-to-Text operations.
/// </summary>
public static class ElevenLabs
{
	private const string ApiBaseUrl = "https://api.elevenlabs.io";

	/// <summary>
	/// Shared HTTP client for all ElevenLabs API calls.
	/// Timeout is set to 10 seconds.
	/// </summary>
	public static readonly HttpClient HttpClient = new()
	{
		Timeout = TimeSpan.FromSeconds( 10 )
	};

	/// <summary>
	/// Probes an ElevenLabs endpoint to verify API key permissions.
	/// For POST endpoints, an empty JSON body is sent — ElevenLabs checks auth before validating the payload.
	/// </summary>
	/// <param name="apiKey">The ElevenLabs API key to test.</param>
	/// <param name="method">The HTTP method (Get or Post).</param>
	/// <param name="url">The full URL of the endpoint to probe.</param>
	/// <param name="cancellationToken">Optional cancellation token.</param>
	/// <returns>A PermissionStatus indicating the result of the probe.</returns>
	public static async Task<PermissionStatus> ProbePermissionAsync( string apiKey, HttpMethod method, string url, CancellationToken cancellationToken = default )
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

			using var response = await HttpClient.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancellationToken );

			app.Logger.WriteLine( $"[ElevenLabs] ProbePermissionAsync {method} {url}: {(int) response.StatusCode}" );

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

				return detail == "missing_permissions" ? PermissionStatus.MissingPermission : PermissionStatus.InvalidKey;
			}

			// 4xx other than 401 (e.g. 422 unprocessable for empty POST body) means the key
			// was recognized and passed auth — only the request payload was rejected.
			return PermissionStatus.Granted;
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
			throw;
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[ElevenLabs] ProbePermissionAsync exception: {ex.Message}" );

			return PermissionStatus.InvalidKey;
		}
	}

	/// <summary>
	/// Queries the ElevenLabs subscription endpoint and returns character usage information for the current billing period.
	/// </summary>
	/// <param name="apiKey">The ElevenLabs API key to use.</param>
	/// <param name="cancellationToken">Optional cancellation token.</param>
	/// <returns>Subscription info including characters used and limit, or null if the call fails.</returns>
	public static async Task<SubscriptionInfo?> GetSubscriptionInfoAsync( string apiKey, CancellationToken cancellationToken = default )
	{
		var app = App.Instance!;

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			return null;
		}

		try
		{
			using var request = new HttpRequestMessage( HttpMethod.Get, $"{ApiBaseUrl}/v1/user/subscription" );

			request.Headers.Add( "xi-api-key", apiKey );

			using var response = await HttpClient.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancellationToken );

			if ( !response.IsSuccessStatusCode )
			{
				app.Logger.WriteLine( $"[ElevenLabs] GetSubscriptionAsync error {(int) response.StatusCode}" );

				return null;
			}

			var json = await response.Content.ReadAsStringAsync( cancellationToken );
			var root = JObject.Parse( json );

			var used = root[ "character_count" ]?.Value<int>();
			var limit = root[ "character_limit" ]?.Value<int>();

			if ( used is null || limit is null )
			{
				app.Logger.WriteLine( "[ElevenLabs] GetSubscriptionAsync: unexpected response shape" );

				return null;
			}

			return new SubscriptionInfo( used.Value, limit.Value );
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
			throw;
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[ElevenLabs] GetSubscriptionAsync exception: {ex.Message}" );

			return null;
		}
	}

	/// <summary>Character usage for the current ElevenLabs billing period.</summary>
	public sealed record SubscriptionInfo( int CharactersUsed, int CharacterLimit )
	{
		public int CharactersRemaining => Math.Max( 0, CharacterLimit - CharactersUsed );
		public double PercentUsed => CharacterLimit > 0 ? CharactersUsed * 100.0 / CharacterLimit : 0.0;
	}

	public static async Task UpdateSubscriptionUsageAsync( string apiKey, MairaProgressBar progressBar, CancellationToken cancellationToken = default )
	{
		ArgumentNullException.ThrowIfNull( progressBar );

		cancellationToken.ThrowIfCancellationRequested();

		progressBar.Value = 0;

		var subscriptionInfo = await GetSubscriptionInfoAsync( apiKey, cancellationToken );

		if ( subscriptionInfo is null )
		{
			return;
		}

		cancellationToken.ThrowIfCancellationRequested();

		progressBar.Value = Math.Clamp( subscriptionInfo.PercentUsed, 0.0, 100.0 );
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

	/// <summary>Structured result returned by TextToSpeech key verification.</summary>
	public sealed record TtsKeyVerificationResult( bool IsRecognized, PermissionStatus VoiceRead, PermissionStatus ModelsRead, PermissionStatus TextToSpeech, PermissionStatus UserRead )
	{
		public bool IsFullyFunctional => IsRecognized && VoiceRead == PermissionStatus.Granted && ModelsRead == PermissionStatus.Granted && TextToSpeech == PermissionStatus.Granted && UserRead == PermissionStatus.Granted;

		public static readonly TtsKeyVerificationResult Empty = new( false, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey );
		public static readonly TtsKeyVerificationResult Invalid = new( false, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey );
	}

	public sealed record SttKeyVerificationResult( bool IsRecognized, PermissionStatus SpeechToText, PermissionStatus UserRead )
	{
		public bool IsFullyFunctional => IsRecognized && SpeechToText == PermissionStatus.Granted && UserRead == PermissionStatus.Granted;

		public static readonly SttKeyVerificationResult Empty = new( false, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey );
		public static readonly SttKeyVerificationResult Invalid = new( false, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey );
	}
}
