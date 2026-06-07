
using System.Collections.ObjectModel;
using System.IO;
using System.Net.WebSockets;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;

using MarvinsAIRARefactored.Classes;

using Newtonsoft.Json.Linq;

namespace MarvinsAIRARefactored.Components;

public sealed class SpeechToText : IDisposable
{
	private const string SpeechToTextScope = "stt";
	private const int SampleRate = 16000;
	private const int Channels = 1;
	private const int BitsPerSample = 16;
	private const int SegmentDurationMs = 250;
	private const int RecordingBufferSeconds = 5;
	private const int PostTransmitFlushDurationMs = 500;
	private const string RealtimeSpeechToTextModelId = "scribe_v2_realtime";
	private const string RealtimeSpeechToTextWebSocketUrl = "wss://api.elevenlabs.io/v1/speech-to-text/realtime";
	private const int RealtimeWebSocketConnectTimeoutSeconds = 15;
	private const int SubscriptionQueryIntervalMs = 60 * 1000; // Query at most every 1 minute

	private const bool DumpAudioChunksToFileOnly = false;
	private const string AudioChunkDumpDirectoryName = "STT";

	public const string DefaultRecordingDeviceName = "[Default Recording Device]";

	private readonly Lock _audioBufferLock = new();
	private CancellationTokenSource? _captureCancellationTokenSource;
	private Task? _segmentLoopTask;
	private FMOD.System? _captureSystem;
	private FMOD.Sound _recordSound;
	private bool _recordSoundCreated;
	private int _recordDriverId = -1;
	private uint _recordReadPositionPcm;

	private string _recordingDevice = DefaultRecordingDeviceName;
	private bool _isEnabled;
	private int _remainingPostTransmitFlushSegments;
	private string _latestTranscriptCandidate = string.Empty;
	private readonly SemaphoreSlim _transcriptionSemaphore = new( 1, 1 );
	private readonly SemaphoreSlim _webSocketSendSemaphore = new( 1, 1 );
	private ClientWebSocket? _transcriptionWebSocket;
	private Task? _webSocketReceiveLoopTask;
	private int _isRadioTransmitting;
	private int _sawRadioTransmissionSinceLastFlush;
	private bool _didLogEndOfSpeechSkippedForCurrentIdlePeriod;
	private bool _didLogEndOfSpeechIgnoredForCurrentIdlePeriod;
	private DateTime _lastSubscriptionQueryTime = DateTime.MinValue;
	private int _hadNewSttRequestSinceLastQuery;
	private FileStream? _audioChunkDumpStream;
	private string? _audioChunkDumpPath;
	private int _audioChunkDumpPcmLength;

	public ObservableCollection<string> RecordingDevices { get; } = [];

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

	public event Action<CancellationToken>? UpdateSubscriptionUsage;

	public void ResetSessionUsage()
	{
		_lastSubscriptionQueryTime = DateTime.MinValue;

		Interlocked.Exchange( ref _hadNewSttRequestSinceLastQuery, 0 );
	}

	private static int PostTransmitFlushSegments => Math.Max( 1, (int) Math.Ceiling( (double) PostTransmitFlushDurationMs / SegmentDurationMs ) );

	/// <summary>
	/// Probes the ElevenLabs STT endpoint using a proper multipart/form-data request (with a
	/// 1-byte silent WAV) so ElevenLabs evaluates the key's permissions before validating the
	/// audio payload.  A plain JSON body causes the API to reject with 422/415 before it ever
	/// checks permissions, which would produce a false Granted result.
	/// </summary>
	private static async Task<ElevenLabs.PermissionStatus> ProbeSttPermissionAsync( string apiKey, CancellationToken cancellationToken )
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

			form.Add( new StringContent( RealtimeSpeechToTextModelId ), "model_id" );

			var fileContent = new ByteArrayContent( minimalWav );

			fileContent.Headers.ContentType = new MediaTypeHeaderValue( "audio/wav" );

			form.Add( fileContent, "file", "probe.wav" );

			request.Content = form;

			using var response = await ElevenLabs.HttpClient.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancellationToken );

			app.Logger.WriteLine( $"[SpeechToText] ProbeSttPermissionAsync POST speech-to-text: {(int) response.StatusCode}" );

			if ( response.IsSuccessStatusCode )
			{
				return ElevenLabs.PermissionStatus.Granted;
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

				return detail == "missing_permissions" ? ElevenLabs.PermissionStatus.MissingPermission : ElevenLabs.PermissionStatus.InvalidKey;
			}

			// Any other 4xx (e.g. 422 unprocessable for our tiny probe audio) means auth passed.
			return ElevenLabs.PermissionStatus.Granted;
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[SpeechToText] ProbeSttPermissionAsync exception: {ex.Message}" );

			return ElevenLabs.PermissionStatus.InvalidKey;
		}
	}

	public static async Task<ElevenLabs.SttKeyVerificationResult> VerifyApiKeyAsync( CancellationToken cancellationToken = default )
	{
		var apiKey = ElevenLabsKeyStore.LoadKey( SpeechToTextScope ).Trim();

		if ( string.IsNullOrWhiteSpace( apiKey ) )
		{
			return ElevenLabs.SttKeyVerificationResult.Empty;
		}

		var speechToTextStatus = await ProbeSttPermissionAsync( apiKey, cancellationToken );
		var userReadStatus = await ElevenLabs.ProbePermissionAsync( apiKey, HttpMethod.Get, "https://api.elevenlabs.io/v1/user/subscription", cancellationToken );

		if ( speechToTextStatus == ElevenLabs.PermissionStatus.InvalidKey && userReadStatus == ElevenLabs.PermissionStatus.InvalidKey )
		{
			return ElevenLabs.SttKeyVerificationResult.Invalid;
		}

		return new ElevenLabs.SttKeyVerificationResult( true, speechToTextStatus, userReadStatus );
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

		if ( !DumpAudioChunksToFileOnly && string.IsNullOrWhiteSpace( apiKey ) )
		{
			app.SpeechToTextWindow?.SetFinalText( "No ElevenLabs STT API key configured." );

			return;
		}

		app.Logger.WriteLine( DumpAudioChunksToFileOnly ? "[SpeechToText] >>> EnableAsync (FMOD diagnostic dump-to-file path)" : "[SpeechToText] >>> EnableAsync (FMOD ElevenLabs path)" );

		try
		{
			InitializeCaptureSystem();
			RefreshRecordingDevices();
			StartRecording();
			await InitializeAudioChunkDumpAsync( CancellationToken.None );

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

		await CloseRealtimeConnectionAsync( CancellationToken.None );
		await FinalizeAudioChunkDumpAsync( CancellationToken.None );

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
		_remainingPostTransmitFlushSegments = 0;
		_latestTranscriptCandidate = string.Empty;

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
		_webSocketSendSemaphore.Dispose();
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
			await UpdateSubscriptionUsageIfNeededAsync( cancellationToken );
			await FlushAndTranscribeAsync( cancellationToken );
		}
	}

	private async Task FlushAndTranscribeAsync( CancellationToken cancellationToken )
	{
		var isRadioTransmitting = Interlocked.CompareExchange( ref _isRadioTransmitting, 0, 0 ) != 0;
		var sawRadioTransmissionSinceLastFlush = Interlocked.Exchange( ref _sawRadioTransmissionSinceLastFlush, 0 ) != 0;
		var finalizeAfterThisCycle = false;

		if ( isRadioTransmitting )
		{
			_remainingPostTransmitFlushSegments = PostTransmitFlushSegments;
		}
		else
		{
			if ( sawRadioTransmissionSinceLastFlush && _remainingPostTransmitFlushSegments == 0 )
			{
				_remainingPostTransmitFlushSegments = PostTransmitFlushSegments;
			}

			if ( _remainingPostTransmitFlushSegments == 0 )
			{
				await SendEndOfSpeechAsync( cancellationToken );

				FinalizePendingTranscript();

				return;
			}

			_remainingPostTransmitFlushSegments--;

			finalizeAfterThisCycle = _remainingPostTransmitFlushSegments == 0;
		}

		byte[] pcmBytes;

		using ( _audioBufferLock.EnterScope() )
		{
			pcmBytes = ReadRecordedPcm();
		}

		if ( pcmBytes.Length > 0 )
		{
			if ( DumpAudioChunksToFileOnly )
			{
				await AppendAudioChunkDumpAsync( pcmBytes, cancellationToken );
			}
			else
			{
				await EnsureRealtimeConnectionAsync( cancellationToken );
				await SendAudioFrameAsync( pcmBytes, cancellationToken );
			}
		}

		if ( finalizeAfterThisCycle )
		{
			await SendEndOfSpeechAsync( cancellationToken );

			FinalizePendingTranscript();
		}
	}

	private void FinalizePendingTranscript()
	{
		if ( string.IsNullOrWhiteSpace( _latestTranscriptCandidate ) )
		{
			return;
		}

		var app = App.Instance!;

		var finalTranscript = _latestTranscriptCandidate;

		_latestTranscriptCandidate = string.Empty;

		app.EnsureSpeechToTextWindowExists();
		app.SpeechToTextWindow?.SetFinalText( finalTranscript );
	}

	private Task UpdateSubscriptionUsageIfNeededAsync( CancellationToken cancellationToken )
	{
		cancellationToken.ThrowIfCancellationRequested();

		// Only query if we've had a new STT request since the last query.
		if ( Interlocked.CompareExchange( ref _hadNewSttRequestSinceLastQuery, 0, 0 ) == 0 )
		{
			return Task.CompletedTask;
		}

		// Check if enough time has passed since the last query (at most every 1 minute).
		var now = DateTime.UtcNow;
		var timeSinceLastQuery = now - _lastSubscriptionQueryTime;

		if ( timeSinceLastQuery.TotalMilliseconds < SubscriptionQueryIntervalMs )
		{
			return Task.CompletedTask;
		}

		Interlocked.Exchange( ref _hadNewSttRequestSinceLastQuery, 0 );

		_lastSubscriptionQueryTime = now;

		UpdateSubscriptionUsage?.Invoke( cancellationToken );

		return Task.CompletedTask;
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

	private async Task EnsureRealtimeConnectionAsync( CancellationToken cancellationToken )
	{
		if ( _transcriptionWebSocket is { State: WebSocketState.Open } )
		{
			return;
		}

		await _transcriptionSemaphore.WaitAsync( cancellationToken );

		try
		{
			if ( _transcriptionWebSocket is { State: WebSocketState.Open } )
			{
				return;
			}

			await CloseRealtimeConnectionAsync( cancellationToken );

			var apiKey = ElevenLabsKeyStore.LoadKey( SpeechToTextScope ).Trim();

			if ( string.IsNullOrWhiteSpace( apiKey ) )
			{
				return;
			}

			var app = App.Instance!;
			var uri = new Uri( $"{RealtimeSpeechToTextWebSocketUrl}?model_id={Uri.EscapeDataString( RealtimeSpeechToTextModelId )}&audio_format=pcm_16000&commit_strategy=vad" );
			var socket = new ClientWebSocket();

			socket.Options.SetRequestHeader( "xi-api-key", apiKey );
			app.Logger.WriteLine( $"[SpeechToText] WS OUT connect: {uri}" );

			using var connectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
			connectTimeoutCts.CancelAfter( TimeSpan.FromSeconds( RealtimeWebSocketConnectTimeoutSeconds ) );

			try
			{
				await socket.ConnectAsync( uri, connectTimeoutCts.Token );
				app.Logger.WriteLine( $"[SpeechToText] WS IN connected: state={socket.State}" );

				_transcriptionWebSocket = socket;
				_webSocketReceiveLoopTask = RunWebSocketReceiveLoopAsync( socket, cancellationToken );
			}
			catch ( OperationCanceledException ex ) when ( !cancellationToken.IsCancellationRequested )
			{
				app.Logger.WriteLine( $"[SpeechToText] WS connect timeout after {RealtimeWebSocketConnectTimeoutSeconds}s: {uri}" );
				app.Logger.WriteLine( $"[SpeechToText] WS connect timeout details: {ex.Message}" );
			}
			catch ( WebSocketException ex )
			{
				app.Logger.WriteLine( $"[SpeechToText] WS connect websocket error: code={ex.WebSocketErrorCode}, message={ex.Message}" );

				if ( ex.InnerException is not null )
				{
					app.Logger.WriteLine( $"[SpeechToText] WS connect inner error: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" );
				}
			}
			catch ( HttpRequestException ex )
			{
				app.Logger.WriteLine( $"[SpeechToText] WS connect HTTP error: {ex.Message}" );
			}
			catch ( Exception ex )
			{
				app.Logger.WriteLine( $"[SpeechToText] WS connect exception: {ex.GetType().Name}: {ex.Message}" );
			}
			finally
			{
				if ( _transcriptionWebSocket != socket )
				{
					socket.Dispose();
				}
			}
		}
		finally
		{
			_transcriptionSemaphore.Release();
		}
	}

	private async Task SendAudioFrameAsync( byte[] pcmBytes, CancellationToken cancellationToken )
	{
		ArgumentNullException.ThrowIfNull( pcmBytes );

		if ( pcmBytes.Length == 0 )
		{
			return;
		}

		var app = App.Instance!;

		if ( _transcriptionWebSocket is not { State: WebSocketState.Open } socket )
		{
			app.Logger.WriteLine( "[SpeechToText] WS OUT audio skipped: socket not open" );

			return;
		}

		var payload = new JObject
		{
			[ "message_type" ] = "input_audio_chunk",
			[ "audio_base_64" ] = Convert.ToBase64String( pcmBytes ),
			[ "sample_rate" ] = SampleRate,
			[ "commit" ] = false
		};

		var payloadBytes = Encoding.UTF8.GetBytes( payload.ToString() );

		await _webSocketSendSemaphore.WaitAsync( cancellationToken );

		try
		{
			_didLogEndOfSpeechIgnoredForCurrentIdlePeriod = false;

			Interlocked.Exchange( ref _hadNewSttRequestSinceLastQuery, 1 );

			app.Logger.WriteLine( $"[SpeechToText] WS OUT audio frame: pcmBytes={pcmBytes.Length}, payloadBytes={payloadBytes.Length}" );

			await socket.SendAsync( new ArraySegment<byte>( payloadBytes ), WebSocketMessageType.Text, true, cancellationToken );
		}
		catch ( WebSocketException ex )
		{
			App.Instance?.Logger.WriteLine( $"[SpeechToText] SendAudioFrameAsync websocket error: {ex.Message}" );
		}
		finally
		{
			_webSocketSendSemaphore.Release();
		}
	}

	private Task SendEndOfSpeechAsync( CancellationToken cancellationToken )
	{
		_ = cancellationToken;

		var app = App.Instance!;

		if ( _transcriptionWebSocket is not { State: WebSocketState.Open } )
		{
			if ( !_didLogEndOfSpeechSkippedForCurrentIdlePeriod )
			{
				app.Logger.WriteLine( "[SpeechToText] WS OUT end_of_speech skipped: socket not open" );

				_didLogEndOfSpeechSkippedForCurrentIdlePeriod = true;
			}

			return Task.CompletedTask;
		}

		_didLogEndOfSpeechSkippedForCurrentIdlePeriod = false;

		if ( !_didLogEndOfSpeechIgnoredForCurrentIdlePeriod )
		{
			app.Logger.WriteLine( "[SpeechToText] WS OUT end_of_speech ignored: using commit_strategy=vad" );

			_didLogEndOfSpeechIgnoredForCurrentIdlePeriod = true;
		}

		return Task.CompletedTask;
	}

	private async Task RunWebSocketReceiveLoopAsync( ClientWebSocket socket, CancellationToken cancellationToken )
	{
		var app = App.Instance!;
		var buffer = new byte[ 32 * 1024 ];
		using var messageBuffer = new MemoryStream();

		try
		{
			app.Logger.WriteLine( "[SpeechToText] WS IN receive loop started" );

			while ( socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested )
			{
				var receiveResult = await socket.ReceiveAsync( new ArraySegment<byte>( buffer ), cancellationToken );

				app.Logger.WriteLine( $"[SpeechToText] WS IN frame: type={receiveResult.MessageType}, bytes={receiveResult.Count}, endOfMessage={receiveResult.EndOfMessage}" );

				if ( receiveResult.MessageType == WebSocketMessageType.Close )
				{
					app.Logger.WriteLine( $"[SpeechToText] WS IN close frame: status={receiveResult.CloseStatus}, description='{receiveResult.CloseStatusDescription}'" );

					break;
				}

				if ( receiveResult.Count > 0 )
				{
					messageBuffer.Write( buffer, 0, receiveResult.Count );
				}

				if ( !receiveResult.EndOfMessage )
				{
					continue;
				}

				if ( receiveResult.MessageType != WebSocketMessageType.Text )
				{
					messageBuffer.SetLength( 0 );

					continue;
				}

				var json = Encoding.UTF8.GetString( messageBuffer.GetBuffer(), 0, (int) messageBuffer.Length );

				messageBuffer.SetLength( 0 );

				app.Logger.WriteLine( $"[SpeechToText] WS IN text: {TruncateForLog( json )}" );

				ProcessRealtimeMessage( json );
			}

			app.Logger.WriteLine( $"[SpeechToText] WS IN receive loop ended: state={socket.State}, canceled={cancellationToken.IsCancellationRequested}" );
		}
		catch ( OperationCanceledException )
		{
			app.Logger.WriteLine( "[SpeechToText] WS IN receive loop canceled" );
		}
		catch ( WebSocketException ex )
		{
			app.Logger.WriteLine( $"[SpeechToText] RunWebSocketReceiveLoopAsync websocket error: {ex.Message}" );
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[SpeechToText] RunWebSocketReceiveLoopAsync exception: {ex.Message}" );
		}
	}

	private void ProcessRealtimeMessage( string json )
	{
		if ( string.IsNullOrWhiteSpace( json ) )
		{
			return;
		}

		try
		{
			var root = JObject.Parse( json );
			var message = root[ "message" ] as JObject;
			var textEvent = root[ "text_event" ] as JObject;
			var messageType = root[ "message_type" ]?.Value<string>();
			var errorText = root[ "error" ]?.Value<string>();

			if ( !string.IsNullOrWhiteSpace( errorText ) )
			{
				App.Instance?.Logger.WriteLine( $"[SpeechToText] WS IN server error: type={messageType ?? "(unknown)"}, error={errorText}" );
			}

			var text = root[ "text" ]?.Value<string>() ?? root[ "transcript" ]?.Value<string>() ?? root[ "partial" ]?.Value<string>() ?? message?[ "text" ]?.Value<string>() ?? textEvent?[ "text" ]?.Value<string>();

			if ( string.IsNullOrWhiteSpace( text ) )
			{
				App.Instance?.Logger.WriteLine( $"[SpeechToText] WS IN message without transcript text: {TruncateForLog( json )}" );

				return;
			}

			var isFinal = root[ "is_final" ]?.Value<bool>() ?? root[ "final" ]?.Value<bool>() ?? message?[ "is_final" ]?.Value<bool>() ?? textEvent?[ "is_final" ]?.Value<bool>() ?? false;

			if ( !isFinal )
			{
				isFinal = string.Equals( root[ "type" ]?.Value<string>(), "final", StringComparison.OrdinalIgnoreCase ) || string.Equals( message?[ "type" ]?.Value<string>(), "final", StringComparison.OrdinalIgnoreCase ) || string.Equals( textEvent?[ "type" ]?.Value<string>(), "final", StringComparison.OrdinalIgnoreCase );
			}

			_latestTranscriptCandidate = text;

			var app = App.Instance!;

			app.Logger.WriteLine( $"[SpeechToText] WS IN transcript: final={isFinal}, chars={text.Length}, text='{TruncateForLog( text, 200 )}'" );

			app.EnsureSpeechToTextWindowExists();

			if ( isFinal )
			{
				app.SpeechToTextWindow?.SetFinalText( text );

				_latestTranscriptCandidate = string.Empty;
			}
			else
			{
				app.SpeechToTextWindow?.SetPartialText( text );
			}
		}
		catch ( Exception ex )
		{
			App.Instance?.Logger.WriteLine( $"[SpeechToText] ProcessRealtimeMessage parse error: {ex.Message}" );
		}
	}

	private async Task CloseRealtimeConnectionAsync( CancellationToken cancellationToken )
	{
		var socket = _transcriptionWebSocket;

		_transcriptionWebSocket = null;

		if ( socket is null )
		{
			return;
		}

		try
		{
			if ( socket.State is WebSocketState.Open or WebSocketState.CloseReceived )
			{
				await socket.CloseAsync( WebSocketCloseStatus.NormalClosure, "shutdown", cancellationToken );
			}
		}
		catch ( Exception ex )
		{
			App.Instance?.Logger.WriteLine( $"[SpeechToText] CloseRealtimeConnectionAsync error: {ex.Message}" );
		}
		finally
		{
			socket.Dispose();
		}

		if ( _webSocketReceiveLoopTask is not null )
		{
			try
			{
				await _webSocketReceiveLoopTask;
			}
			catch ( OperationCanceledException )
			{
			}
			finally
			{
				_webSocketReceiveLoopTask = null;
			}
		}
	}

	private async Task InitializeAudioChunkDumpAsync( CancellationToken cancellationToken )
	{
		if ( !DumpAudioChunksToFileOnly || _audioChunkDumpStream is not null )
		{
			return;
		}

		var app = App.Instance!;
		var dumpDirectoryPath = Path.Combine( App.DocumentsFolder, AudioChunkDumpDirectoryName );

		Directory.CreateDirectory( dumpDirectoryPath );

		var timestamp = DateTime.UtcNow.ToString( "yyyyMMdd-HHmmss" );

		_audioChunkDumpPath = Path.Combine( dumpDirectoryPath, $"stt-capture-{timestamp}.wav" );
		_audioChunkDumpPcmLength = 0;
		_audioChunkDumpStream = new FileStream( _audioChunkDumpPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 81920, true );

		var headerBytes = BuildWavHeader( 0, SampleRate, Channels, BitsPerSample );

		await _audioChunkDumpStream.WriteAsync( headerBytes, cancellationToken );

		app.Logger.WriteLine( $"[SpeechToText] Audio dump initialized: {_audioChunkDumpPath}" );
	}

	private async Task AppendAudioChunkDumpAsync( byte[] pcmBytes, CancellationToken cancellationToken )
	{
		ArgumentNullException.ThrowIfNull( pcmBytes );

		if ( pcmBytes.Length == 0 || _audioChunkDumpStream is null )
		{
			return;
		}

		await _audioChunkDumpStream.WriteAsync( pcmBytes, cancellationToken );

		_audioChunkDumpPcmLength += pcmBytes.Length;
	}

	private async Task FinalizeAudioChunkDumpAsync( CancellationToken cancellationToken )
	{
		if ( _audioChunkDumpStream is null )
		{
			return;
		}

		var app = App.Instance!;
		var stream = _audioChunkDumpStream;
		var dumpPath = _audioChunkDumpPath;
		var pcmLength = _audioChunkDumpPcmLength;

		_audioChunkDumpStream = null;
		_audioChunkDumpPath = null;
		_audioChunkDumpPcmLength = 0;

		try
		{
			await stream.FlushAsync( cancellationToken );
			stream.Seek( 0, SeekOrigin.Begin );

			var headerBytes = BuildWavHeader( pcmLength, SampleRate, Channels, BitsPerSample );

			await stream.WriteAsync( headerBytes, cancellationToken );
			await stream.FlushAsync( cancellationToken );

			app.Logger.WriteLine( $"[SpeechToText] Audio dump finalized: path={dumpPath}, pcmBytes={pcmLength}" );
		}
		catch ( Exception ex )
		{
			app.Logger.WriteLine( $"[SpeechToText] FinalizeAudioChunkDumpAsync failed: {ex.Message}" );
		}
		finally
		{
			await stream.DisposeAsync();
		}
	}

	private static byte[] BuildWavFromPcm( byte[] pcmBytes, int sampleRate, int channels, int bitsPerSample )
	{
		ArgumentNullException.ThrowIfNull( pcmBytes );

		var headerBytes = BuildWavHeader( pcmBytes.Length, sampleRate, channels, bitsPerSample );
		var wavBytes = new byte[ headerBytes.Length + pcmBytes.Length ];

		Buffer.BlockCopy( headerBytes, 0, wavBytes, 0, headerBytes.Length );
		Buffer.BlockCopy( pcmBytes, 0, wavBytes, headerBytes.Length, pcmBytes.Length );

		return wavBytes;
	}

	private static byte[] BuildWavHeader( int pcmLength, int sampleRate, int channels, int bitsPerSample )
	{
		ArgumentOutOfRangeException.ThrowIfLessThan( pcmLength, 0 );

		var byteRate = sampleRate * channels * bitsPerSample / 8;
		var blockAlign = channels * bitsPerSample / 8;

		using var stream = new MemoryStream( 44 );
		using var writer = new BinaryWriter( stream );

		writer.Write( "RIFF"u8.ToArray() );
		writer.Write( 36 + pcmLength );
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
		writer.Write( pcmLength );

		writer.Flush();

		return stream.ToArray();
	}

	private static string TruncateForLog( string value, int maxLength = 1000 )
	{
		ArgumentNullException.ThrowIfNull( value );
		ArgumentOutOfRangeException.ThrowIfLessThan( maxLength, 1 );

		if ( value.Length <= maxLength )
		{
			return value;
		}

		return $"{value[ ..maxLength ]}... (truncated, totalChars={value.Length})";
	}
}
