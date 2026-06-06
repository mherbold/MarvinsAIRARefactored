
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;

using MarvinsAIRARefactored.Classes;

using Newtonsoft.Json.Linq;

namespace MarvinsAIRARefactored.Components;

public sealed class SpeechToText : IDisposable
{
	// ElevenLabs subscription exposes credit/character quota, not direct STT minutes/hours.
	// This conversion factor is an estimate and should be updated if pricing changes.
	public const double ElevenLabsSttCreditsPerMinute = 66.6666667;

	private const string SpeechToTextScope = "stt";
	private const int SampleRate = 16000;
	private const int Channels = 1;
	private const int BitsPerSample = 16;
	private const int SegmentDurationMs = 250;
	private const int RecordingBufferSeconds = 5;

	public const string DefaultRecordingDeviceName = "[Default Recording Device]";

	private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds( 10 ) };

	private readonly Lock _audioBufferLock = new();
	private CancellationTokenSource? _captureCancellationTokenSource;
	private Task? _segmentLoopTask;
	private FMOD.System? _captureSystem;
	private FMOD.Sound _recordSound;
	private bool _recordSoundCreated;
	private int _recordDriverId = -1;
	private uint _recordReadPositionPcm;

	private string _language = "en-US";
	private string _recordingDevice = DefaultRecordingDeviceName;
	private bool _isEnabled;
	private bool _flushSegmentAfterTransmitStops;
	private int _sessionCharactersUsed;
	private readonly SemaphoreSlim _transcriptionSemaphore = new( 1, 1 );
	private int _isRadioTransmitting;
	private int _sawRadioTransmissionSinceLastFlush;

	public ObservableCollection<string> RecordingDevices { get; } = [];

	public string Language
	{
		get => _language;
		set => _language = value ?? "en-US";
	}

	public string RecordingDevice
	{
		get => _recordingDevice;

		set
		{
			var nextDevice = string.IsNullOrWhiteSpace( value ) ? DefaultRecordingDeviceName : value;

			if ( nextDevice == _recordingDevice )
			{
				return;
			}

			_recordingDevice = nextDevice;

			if ( _isEnabled )
			{
				_ = RestartCaptureAsync();
			}
		}
	}

	public event Action<string, int>? TranscriptReceived;

	public int SessionCharactersUsed => _sessionCharactersUsed;

	public void ResetSessionUsage()
	{
		_sessionCharactersUsed = 0;
	}

	public void TrackTranscriptUsage( string transcript, int charactersCharged )
	{
		ArgumentNullException.ThrowIfNull( transcript );

		var charged = Math.Max( 0, charactersCharged );

		if ( charged == 0 )
		{
			charged = transcript.Length;
		}

		_sessionCharactersUsed += charged;

		TranscriptReceived?.Invoke( transcript, charged );
	}

	private void HandleFinalText( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
		{
			return;
		}

		TrackTranscriptUsage( text, text.Length );
	}

	/// <summary>
	/// Probes the ElevenLabs STT endpoint using a proper multipart/form-data request (with a
	/// 1-byte silent WAV) so ElevenLabs evaluates the key's permissions before validating the
	/// audio payload.  A plain JSON body causes the API to reject with 422/415 before it ever
	/// checks permissions, which would produce a false Granted result.
	/// </summary>
	private static async Task<PermissionStatus> ProbeSttPermissionAsync( string apiKey, CancellationToken cancellationToken )
	{
		var app = App.Instance!;

		try
		{
			using var request = new HttpRequestMessage( HttpMethod.Post, "https://api.elevenlabs.io/v1/speech-to-text" );

			request.Headers.Add( "xi-api-key", apiKey );

			// Minimal valid WAV: 44-byte header + 1 byte of silence (1 Hz, 1 ch, 8-bit)
			// so ElevenLabs proceeds past auth before rejecting the payload.
			var minimalWav = BuildWavFromPcm( [ 0 ], 1, 1, 8 );

			using var form = new MultipartFormDataContent();

			form.Add( new StringContent( "scribe_v1" ), "model_id" );

			var fileContent = new ByteArrayContent( minimalWav );
			fileContent.Headers.ContentType = new MediaTypeHeaderValue( "audio/wav" );
			form.Add( fileContent, "file", "probe.wav" );

			request.Content = form;

			using var response = await _httpClient.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancellationToken );

			app.Logger.WriteLine( $"[SpeechToText] ProbeSttPermissionAsync POST speech-to-text: {(int) response.StatusCode}" );

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

			// Any other 4xx (e.g. 422 unprocessable for our tiny probe audio) means auth passed.
			return PermissionStatus.Granted;
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[SpeechToText] ProbeSttPermissionAsync exception: {ex.Message}" );

			return PermissionStatus.InvalidKey;
		}
	}

	public async Task<SttKeyVerificationResult> VerifyApiKeyAsync( CancellationToken cancellationToken = default )
	{
		var apiKey = ElevenLabsKeyStore.LoadKey( SpeechToTextScope ).Trim();

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			return SttKeyVerificationResult.Empty;
		}

		var speechToTextStatus = await ProbeSttPermissionAsync( apiKey, cancellationToken );
		var userReadStatus = await TextToSpeech.ProbePermissionAsync( apiKey, HttpMethod.Get, "https://api.elevenlabs.io/v1/user/subscription", cancellationToken );

		if ( speechToTextStatus == PermissionStatus.InvalidKey && userReadStatus == PermissionStatus.InvalidKey )
		{
			return SttKeyVerificationResult.Invalid;
		}

		return new SttKeyVerificationResult( true, speechToTextStatus, userReadStatus );
	}

	public async Task<SttSubscriptionInfo?> GetSubscriptionUsageAsync( CancellationToken cancellationToken = default )
	{
		var app = App.Instance!;
		var sttApiKey = ElevenLabsKeyStore.LoadKey( SpeechToTextScope );

		if ( string.IsNullOrWhiteSpace( sttApiKey ) )
		{
			return null;
		}

		try
		{
			using var request = new HttpRequestMessage( HttpMethod.Get, "https://api.elevenlabs.io/v1/user/subscription" );

			request.Headers.Add( "xi-api-key", sttApiKey );

			using var response = await _httpClient.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancellationToken );

			if ( !response.IsSuccessStatusCode )
			{
				app.Logger.WriteLine( $"[SpeechToText] GetSubscriptionUsageAsync error {(int) response.StatusCode}" );

				return null;
			}

			var json = await response.Content.ReadAsStringAsync( cancellationToken );
			var root = JObject.Parse( json );

			var characterCount = ReadIntegerField( root, "character_count" );
			var characterLimit = ReadIntegerField( root, "character_limit" );
			var tier = root[ "tier" ]?.Value<string>() ?? string.Empty;
			var status = root[ "status" ]?.Value<string>() ?? string.Empty;
			var nextResetUnix = ReadLongField( root, "next_character_count_reset_unix" );
			var currentOverage = ReadDecimalField( root, "current_overage" );

			if ( characterCount is null || characterLimit is null )
			{
				app.Logger.WriteLine( "[SpeechToText] GetSubscriptionUsageAsync: character_count/character_limit not found in subscription response" );

				return null;
			}

			return new SttSubscriptionInfo( Math.Max( 0, characterCount.Value ), Math.Max( 0, characterLimit.Value ), tier, status, nextResetUnix, currentOverage );
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[SpeechToText] GetSubscriptionUsageAsync exception: {ex.Message}" );

			return null;
		}
	}

	public async Task EnableAsync( int port = 18888 )
	{
		_ = port;

		var app = App.Instance!;

		if ( !app.Simulator.IsConnected || _isEnabled )
		{
			return;
		}

		var apiKey = ElevenLabsKeyStore.LoadKey( SpeechToTextScope );

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			app.SpeechToTextWindow?.SetFinalText( "No ElevenLabs STT API key configured." );

			return;
		}

		app.Logger.WriteLine( "[SpeechToText] >>> EnableAsync (FMOD ElevenLabs path)" );

		try
		{
			InitializeCaptureSystem();
			RefreshRecordingDevices();
			StartRecording();

			_captureCancellationTokenSource = new CancellationTokenSource();
			_segmentLoopTask = RunSegmentLoopAsync( _captureCancellationTokenSource.Token );
			_isEnabled = true;

			app.UpdateSpeechToTextWindowVisibility();
			app.Logger.WriteLine( "[SpeechToText] << EnableAsync (FMOD ElevenLabs path started)" );
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[SpeechToText] EnableAsync failed: {ex.Message}" );

			await DisableAsync();
		}
	}

	public async Task DisableAsync()
	{
		if ( !_isEnabled && !_recordSoundCreated && _captureSystem is null )
		{
			return;
		}

		var app = App.Instance!;
		app.Logger.WriteLine( "[SpeechToText] >>> DisableAsync" );

		_captureCancellationTokenSource?.Cancel();

		if ( _segmentLoopTask is not null )
		{
			try
			{
				await _segmentLoopTask;
			}
			catch ( OperationCanceledException )
			{
			}
			finally
			{
				_segmentLoopTask = null;
			}
		}

		_captureCancellationTokenSource?.Dispose();
		_captureCancellationTokenSource = null;

		using ( _audioBufferLock.EnterScope() )
		{
			if ( _captureSystem is not null && _recordDriverId >= 0 )
			{
				_captureSystem.Value.recordStop( _recordDriverId );
			}

			if ( _recordSoundCreated )
			{
				_recordSound.release();
				_recordSound = default;
				_recordSoundCreated = false;
			}

			if ( _captureSystem is not null )
			{
				_captureSystem.Value.close();
				_captureSystem.Value.release();
				_captureSystem = null;
			}

			_recordDriverId = -1;
			_recordReadPositionPcm = 0;
		}

		_isEnabled = false;
		_flushSegmentAfterTransmitStops = false;

		app.UpdateSpeechToTextWindowVisibility();
		app.Logger.WriteLine( "[SpeechToText] << DisableAsync" );
	}

	public void SimulatorConnected()
	{
		if ( DataContext.DataContext.Instance.Settings.SpeechToTextEnabled )
		{
			_ = EnableAsync();
		}
	}

	public void SimulatorDisconnected()
	{
		_ = DisableAsync();
	}

	public void UpdateRadioTransmitState( bool isRadioTransmitting )
	{
		Interlocked.Exchange( ref _isRadioTransmitting, isRadioTransmitting ? 1 : 0 );

		if ( isRadioTransmitting )
		{
			Interlocked.Exchange( ref _sawRadioTransmissionSinceLastFlush, 1 );
		}
	}

	public void RefreshRecordingDevices()
	{
		var app = App.Instance!;
		var deviceNames = EnumerateRecordingDevices();
		var dispatcher = app.Dispatcher;

		if ( dispatcher != null && !dispatcher.CheckAccess() )
		{
			dispatcher.Invoke( () => UpdateRecordingDeviceCollection( deviceNames ) );
		}
		else
		{
			UpdateRecordingDeviceCollection( deviceNames );
		}

		app.Logger.WriteLine( $"[SpeechToText] RefreshRecordingDevices result count={RecordingDevices.Count}; devices={string.Join( " | ", RecordingDevices )}" );
	}

	public void Dispose()
	{
		_ = DisableAsync();

		_transcriptionSemaphore.Dispose();
	}

	private void InitializeCaptureSystem()
	{
		if ( _captureSystem is not null )
		{
			return;
		}

		var createResult = FMOD.Factory.System_Create( out var system );

		if ( createResult != FMOD.RESULT.OK )
		{
			throw new InvalidOperationException( $"FMOD System_Create failed: {createResult}" );
		}

		_captureSystem = system;

		var initResult = _captureSystem.Value.init( 32, FMOD.INITFLAGS.NORMAL, IntPtr.Zero );

		if ( initResult != FMOD.RESULT.OK )
		{
			throw new InvalidOperationException( $"FMOD init failed: {initResult}" );
		}
	}

	private void StartRecording()
	{
		if ( _captureSystem is null )
		{
			throw new InvalidOperationException( "Capture system is not initialized." );
		}

		_captureSystem.Value.getRecordNumDrivers( out var numDrivers, out _ );

		if ( numDrivers <= 0 )
		{
			throw new InvalidOperationException( "No recording devices found." );
		}

		_recordDriverId = ResolveRecordDriverId( _captureSystem.Value, numDrivers );

		_captureSystem.Value.getRecordDriverInfo( _recordDriverId, out var recordingDeviceName, 256, out _, out _, out _, out _, out _ );

		App.Instance?.Logger.WriteLine( $"[SpeechToText] Using recording device {_recordDriverId}: {recordingDeviceName}" );

		var bufferLengthSamples = (uint) ( SampleRate * RecordingBufferSeconds );
		var exInfo = new FMOD.CREATESOUNDEXINFO
		{
			cbsize = Marshal.SizeOf<FMOD.CREATESOUNDEXINFO>(),
			numchannels = Channels,
			format = FMOD.SOUND_FORMAT.PCM16,
			defaultfrequency = SampleRate,
			length = bufferLengthSamples * (uint) Channels * (uint) ( BitsPerSample / 8 )
		};

		var createSoundResult = _captureSystem.Value.createSound( string.Empty, FMOD.MODE.OPENUSER | FMOD.MODE.LOOP_NORMAL, ref exInfo, out _recordSound );

		if ( createSoundResult != FMOD.RESULT.OK )
		{
			throw new InvalidOperationException( $"FMOD createSound for recording failed: {createSoundResult}" );
		}

		_recordSoundCreated = true;
		_recordReadPositionPcm = 0;

		var recordStartResult = _captureSystem.Value.recordStart( _recordDriverId, _recordSound, true );

		if ( recordStartResult != FMOD.RESULT.OK )
		{
			throw new InvalidOperationException( $"FMOD recordStart failed: {recordStartResult}" );
		}
	}

	private async Task RestartCaptureAsync()
	{
		if ( !DataContext.DataContext.Instance.Settings.SpeechToTextEnabled )
		{
			return;
		}

		await DisableAsync();
		await EnableAsync();
	}

	private List<string> EnumerateRecordingDevices()
	{
		var app = App.Instance!;
		var names = new List<string>();

		if ( _captureSystem is not null )
		{
			_captureSystem.Value.getRecordNumDrivers( out var activeDrivers, out var activeConnectedDrivers );

			app.Logger.WriteLine( $"[SpeechToText] EnumerateRecordingDevices(active system): drivers={activeDrivers}, connected={activeConnectedDrivers}" );

			for ( int i = 0; i < activeDrivers; i++ )
			{
				_captureSystem.Value.getRecordDriverInfo( i, out var activeName, 256, out _, out _, out _, out _, out var state );

				app.Logger.WriteLine( $"[SpeechToText] EnumerateRecordingDevices(active system): index={i}, name='{activeName}', state={state}" );

				if ( !string.IsNullOrWhiteSpace( activeName ) && !IsLoopbackDeviceName( activeName ) )
				{
					names.Add( activeName );
				}
			}

			if ( names.Count > 0 )
			{
				app.Logger.WriteLine( "[SpeechToText] EnumerateRecordingDevices: using active capture system list" );

				return names;
			}

			app.Logger.WriteLine( "[SpeechToText] EnumerateRecordingDevices: active capture system returned no named devices; falling back to temp system" );
		}

		var createResult = FMOD.Factory.System_Create( out var tempSystem );

		if ( createResult != FMOD.RESULT.OK )
		{
			app.Logger.WriteLine( $"[SpeechToText] EnumerateRecordingDevices(temp system): System_Create failed {createResult}" );

			return names;
		}

		try
		{
			var initResult = tempSystem.init( 8, FMOD.INITFLAGS.NORMAL, IntPtr.Zero );

			if ( initResult != FMOD.RESULT.OK )
			{
				app.Logger.WriteLine( $"[SpeechToText] EnumerateRecordingDevices(temp system): init failed {initResult}" );

				return names;
			}

			tempSystem.getRecordNumDrivers( out var numDrivers, out var connectedDrivers );
			app.Logger.WriteLine( $"[SpeechToText] EnumerateRecordingDevices(temp system): drivers={numDrivers}, connected={connectedDrivers}" );

			for ( int i = 0; i < numDrivers; i++ )
			{
				tempSystem.getRecordDriverInfo( i, out var name, 256, out _, out _, out _, out _, out var state );
				app.Logger.WriteLine( $"[SpeechToText] EnumerateRecordingDevices(temp system): index={i}, name='{name}', state={state}" );

				if ( !string.IsNullOrWhiteSpace( name ) && !IsLoopbackDeviceName( name ) )
				{
					names.Add( name );
				}
			}
		}
		finally
		{
			tempSystem.close();
			tempSystem.release();
		}

		app.Logger.WriteLine( $"[SpeechToText] EnumerateRecordingDevices final count={names.Count}" );

		return names;
	}

	private static int ResolveRecordDriverId( FMOD.System system, int numDrivers )
	{
		var selectedName = DataContext.DataContext.Instance.Settings.SpeechToTextRecordingDevice;
		var fallbackDriverId = -1;

		for ( int i = 0; i < numDrivers; i++ )
		{
			system.getRecordDriverInfo( i, out var name, 256, out _, out _, out _, out _, out _ );

			if ( IsLoopbackDeviceName( name ) )
			{
				continue;
			}

			if ( fallbackDriverId < 0 && !string.IsNullOrWhiteSpace( name ) )
			{
				fallbackDriverId = i;
			}

			if ( !string.Equals( selectedName, DefaultRecordingDeviceName, StringComparison.OrdinalIgnoreCase ) && string.Equals( name, selectedName, StringComparison.OrdinalIgnoreCase ) )
			{
				return i;
			}
		}

		return fallbackDriverId >= 0 ? fallbackDriverId : 0;
	}

	private static bool IsLoopbackDeviceName( string? deviceName )
	{
		return !string.IsNullOrWhiteSpace( deviceName ) && deviceName.Contains( "[loopback]", StringComparison.OrdinalIgnoreCase );
	}

	private void UpdateRecordingDeviceCollection( IReadOnlyList<string> deviceNames )
	{
		RecordingDevices.Clear();

		foreach ( var deviceName in deviceNames.Distinct( StringComparer.OrdinalIgnoreCase ).OrderBy( n => n ) )
		{
			RecordingDevices.Add( deviceName );
		}
	}

	private async Task RunSegmentLoopAsync( CancellationToken cancellationToken )
	{
		while ( !cancellationToken.IsCancellationRequested )
		{
			await Task.Delay( SegmentDurationMs, cancellationToken );
			await FlushAndTranscribeAsync( cancellationToken );
		}
	}

	private async Task FlushAndTranscribeAsync( CancellationToken cancellationToken )
	{
		var app = App.Instance!;
		var isRadioTransmitting = Interlocked.CompareExchange( ref _isRadioTransmitting, 0, 0 ) != 0;
		var sawRadioTransmissionSinceLastFlush = Interlocked.Exchange( ref _sawRadioTransmissionSinceLastFlush, 0 ) != 0;

		if ( isRadioTransmitting )
		{
			_flushSegmentAfterTransmitStops = true;
		}
		else if ( !_flushSegmentAfterTransmitStops && !sawRadioTransmissionSinceLastFlush )
		{
			return;
		}

		var consumeTrailingFlush = !isRadioTransmitting && _flushSegmentAfterTransmitStops;

		if ( consumeTrailingFlush )
		{
			_flushSegmentAfterTransmitStops = false;

			app.Logger.WriteLine( "[SpeechToText] Radio transmit ended; flushing trailing buffered segment" );
		}

		byte[] pcmBytes;

		using ( _audioBufferLock.EnterScope() )
		{
			pcmBytes = ReadRecordedPcm();
		}

		if ( pcmBytes.Length < SampleRate / 2 )
		{
			return;
		}

		await _transcriptionSemaphore.WaitAsync( cancellationToken );

		try
		{
			var transcript = await TranscribeSegmentAsync( pcmBytes, cancellationToken );

			if ( string.IsNullOrWhiteSpace( transcript ) )
			{
				return;
			}

			app.EnsureSpeechToTextWindowExists();
			app.SpeechToTextWindow?.SetFinalText( transcript );

			HandleFinalText( transcript );
		}
		finally
		{
			_transcriptionSemaphore.Release();
		}
	}

	private byte[] ReadRecordedPcm()
	{
		if ( _captureSystem is null || !_recordSoundCreated || _recordDriverId < 0 )
		{
			return [];
		}

		_captureSystem.Value.update();
		_captureSystem.Value.getRecordPosition( _recordDriverId, out var recordPositionPcm );

		var bytesPerSampleFrame = (uint) ( Channels * ( BitsPerSample / 8 ) );
		var ringBufferLengthPcm = (uint) ( SampleRate * RecordingBufferSeconds );

		if ( recordPositionPcm >= ringBufferLengthPcm )
		{
			recordPositionPcm %= ringBufferLengthPcm;
		}

		if ( recordPositionPcm == _recordReadPositionPcm )
		{
			return [];
		}

		uint bytesToRead;

		if ( recordPositionPcm > _recordReadPositionPcm )
		{
			bytesToRead = ( recordPositionPcm - _recordReadPositionPcm ) * bytesPerSampleFrame;
		}
		else
		{
			bytesToRead = ( ringBufferLengthPcm - _recordReadPositionPcm + recordPositionPcm ) * bytesPerSampleFrame;
		}

		if ( bytesToRead == 0 )
		{
			return [];
		}

		var lockResult = _recordSound.@lock( _recordReadPositionPcm * bytesPerSampleFrame, bytesToRead, out var ptr1, out var ptr2, out var len1, out var len2 );

		if ( lockResult != FMOD.RESULT.OK )
		{
			App.Instance?.Logger.WriteLine( $"[SpeechToText] Sound.lock failed: {lockResult}" );

			return [];
		}

		try
		{
			var totalLength = checked((int) ( len1 + len2 ));
			var buffer = new byte[ totalLength ];

			if ( len1 > 0 )
			{
				Marshal.Copy( ptr1, buffer, 0, (int) len1 );
			}

			if ( len2 > 0 )
			{
				Marshal.Copy( ptr2, buffer, (int) len1, (int) len2 );
			}

			_recordReadPositionPcm = recordPositionPcm;

			return buffer;
		}
		finally
		{
			_recordSound.unlock( ptr1, ptr2, len1, len2 );
		}
	}

	private async Task<string?> TranscribeSegmentAsync( byte[] pcmBytes, CancellationToken cancellationToken )
	{
		var apiKey = ElevenLabsKeyStore.LoadKey( SpeechToTextScope ).Trim();

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			return null;
		}

		var wavBytes = BuildWavFromPcm( pcmBytes, SampleRate, Channels, BitsPerSample );

		using var request = new HttpRequestMessage( HttpMethod.Post, "https://api.elevenlabs.io/v1/speech-to-text" );

		request.Headers.Add( "xi-api-key", apiKey );
		request.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/json" ) );

		using var form = new MultipartFormDataContent();

		form.Add( new StringContent( "scribe_v1" ), "model_id" );

		if ( !string.IsNullOrWhiteSpace( _language ) )
		{
			form.Add( new StringContent( _language ), "language_code" );
		}

		var fileContent = new ByteArrayContent( wavBytes );

		fileContent.Headers.ContentType = new MediaTypeHeaderValue( "audio/wav" );

		form.Add( fileContent, "file", "segment.wav" );

		request.Content = form;

		using var response = await _httpClient.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancellationToken );

		if ( !response.IsSuccessStatusCode )
		{
			var body = await response.Content.ReadAsStringAsync( cancellationToken );

			App.Instance?.Logger.WriteLine( $"[SpeechToText] TranscribeSegmentAsync error {(int) response.StatusCode}: {body}" );

			return null;
		}

		var json = await response.Content.ReadAsStringAsync( cancellationToken );
		var root = JObject.Parse( json );

		return root[ "text" ]?.Value<string>() ?? root[ "transcript" ]?.Value<string>();
	}

	private static byte[] BuildWavFromPcm( byte[] pcmBytes, int sampleRate, int channels, int bitsPerSample )
	{
		ArgumentNullException.ThrowIfNull( pcmBytes );

		var byteRate = sampleRate * channels * bitsPerSample / 8;
		var blockAlign = channels * bitsPerSample / 8;

		using var stream = new MemoryStream( pcmBytes.Length + 44 );
		using var writer = new BinaryWriter( stream );

		writer.Write( "RIFF"u8.ToArray() );
		writer.Write( 36 + pcmBytes.Length );
		writer.Write( "WAVE"u8.ToArray() );
		writer.Write( "fmt "u8.ToArray() );
		writer.Write( 16 );
		writer.Write( (short) 1 );
		writer.Write( (short) channels );
		writer.Write( sampleRate );
		writer.Write( byteRate );
		writer.Write( (short) blockAlign );
		writer.Write( (short) bitsPerSample );
		writer.Write( "data"u8.ToArray() );
		writer.Write( pcmBytes.Length );
		writer.Write( pcmBytes );

		writer.Flush();

		return stream.ToArray();
	}

	private static int? ReadIntegerField( JToken? source, params string[] keys )
	{
		if ( source is not JObject obj )
		{
			return null;
		}

		foreach ( var key in keys )
		{
			var token = obj[ key ];

			if ( token is null )
			{
				continue;
			}

			if ( token.Type == JTokenType.Integer )
			{
				return token.Value<int>();
			}

			if ( token.Type == JTokenType.Float )
			{
				return (int) Math.Round( token.Value<double>() );
			}

			if ( token.Type == JTokenType.String && int.TryParse( token.Value<string>(), out var parsed ) )
			{
				return parsed;
			}
		}

		return null;
	}

	private static long? ReadLongField( JToken? source, params string[] keys )
	{
		if ( source is not JObject obj )
		{
			return null;
		}

		foreach ( var key in keys )
		{
			var token = obj[ key ];

			if ( token is null )
			{
				continue;
			}

			if ( token.Type == JTokenType.Integer )
			{
				return token.Value<long>();
			}

			if ( token.Type == JTokenType.Float )
			{
				return (long) Math.Round( token.Value<double>() );
			}

			if ( token.Type == JTokenType.String && long.TryParse( token.Value<string>(), out var parsed ) )
			{
				return parsed;
			}
		}

		return null;
	}

	private static decimal? ReadDecimalField( JToken? source, params string[] keys )
	{
		if ( source is not JObject obj )
		{
			return null;
		}

		foreach ( var key in keys )
		{
			var token = obj[ key ];

			if ( token is null )
			{
				continue;
			}

			if ( token.Type == JTokenType.Integer || token.Type == JTokenType.Float )
			{
				return token.Value<decimal>();
			}

			if ( token.Type == JTokenType.String && decimal.TryParse( token.Value<string>(), out var parsed ) )
			{
				return parsed;
			}
		}

		return null;
	}
}

public sealed record SttKeyVerificationResult( bool IsRecognized, PermissionStatus SpeechToText, PermissionStatus UserRead )
{
	public bool IsFullyFunctional => IsRecognized && SpeechToText == PermissionStatus.Granted && UserRead == PermissionStatus.Granted;

	public static readonly SttKeyVerificationResult Empty = new( false, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey );
	public static readonly SttKeyVerificationResult Invalid = new( false, PermissionStatus.InvalidKey, PermissionStatus.InvalidKey );
}

public sealed record SttSubscriptionInfo( int CharacterCount, int CharacterLimit, string Tier, string Status, long? NextCharacterCountResetUnix, decimal? CurrentOverage )
{
	public int RemainingCredits => Math.Max( 0, CharacterLimit - CharacterCount );

	public DateTimeOffset? NextCharacterCountResetUtc => NextCharacterCountResetUnix is > 0 ? DateTimeOffset.FromUnixTimeSeconds( NextCharacterCountResetUnix.Value ) : null;
}
