
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Components;

public sealed class AudioManager : IDisposable
{
	private readonly Lock _lock = new();

	private readonly string _soundsDirectory = Path.Combine( App.DocumentsFolder, "Sounds" );

	// Key = lowercase filename-without-extension (e.g. "click", "click_custom")
	private readonly Dictionary<string, FmodSound> _soundCache = [];
	private readonly Dictionary<string, FmodChannel> _channelCache = [];

	// Absolute paths of every sound file that has been requested for loading. This is the durable source of
	// truth for what the live cache should contain; it is kept separate from _soundCache (which CloseDevice
	// empties on every close) so the cache can be fully rebuilt whenever the FMOD system is reopened — after a
	// device switch or the simulator-connect reset. Guarded by _lock; never cleared by CloseDevice.
	private readonly HashSet<string> _knownSoundPaths = new( StringComparer.OrdinalIgnoreCase );

	private FileSystemWatcher? _fileSystemWatcher = null;

	private FMOD.System? _fmodSystem;

	private readonly Dictionary<string, DateTime> _debounceMap = [];

	/// <summary>
	/// List of available output device names. First entry is always the default-device placeholder.
	/// Populated in <see cref="OpenDevice"/> and exposed for binding to the UI.
	/// </summary>
	public ObservableCollection<string> OutputDevices { get; } = [];

	public const string DefaultDeviceName = "[Default Windows Sound Device]";

	// Delay between closing and reopening the output device in ResetDeviceForSimulatorConnect. iRacing's sim
	// process configures the shared Windows audio endpoint when it initializes audio; if MAIRA opened that
	// endpoint first, the Windows audio engine (audiodg) gets stuck in a heavy mode that starves iRacing's
	// audio (its C and A performance meters peg to 100%). Briefly releasing the endpoint on connect lets
	// iRacing re-establish the light config, after which our reopened stream conforms to it. Must be long
	// enough for iRacing to actually re-sync while the endpoint is free.
	private const int SimulatorConnectResetDelayMilliseconds = 2000;

	// guards ResetDeviceForSimulatorConnect against overlapping resets (0 = idle, 1 = running)
	private int _simulatorConnectResetInProgress = 0;

	// How long the output may sit silent before we release it. An open FMOD output keeps a WASAPI stream
	// running, which Windows reports as an audio power request (powercfg /requests) and which stops the PC
	// from going to sleep on its own. So while nothing is playing (and no simulator is connected) we close
	// the device, and reopen it on demand the moment something wants to play (see EnsureDeviceOpenLocked).
	private const int IdleReleaseDelayMilliseconds = 10000;

	// After a failed open (e.g. the output device is unplugged), wait this long before trying again so a
	// looping sound requested every tick does not retry the open every tick.
	private const int OpenRetryDelayMilliseconds = 5000;

	// Environment.TickCount64 of the last time a sound was started or was found playing. Guarded by _lock.
	private long _lastAudioActivityTickCount = 0;

	// Environment.TickCount64 before which an on-demand open is not allowed. Set after a failed open, and
	// when the simulator connects while the device is closed (so we do not grab the shared endpoint while
	// iRacing is initializing its audio). Guarded by _lock.
	private long _nextOpenAllowedTickCount = 0;

	private bool _disposed = false;

	// Last device name passed to SetOutputDevice. ApplyOutputDeviceSetting prefers this over the global
	// settings object because during settings file deserialization the SoundsOutputDevice setter fires on a
	// new Settings instance before it is assigned to DataContext.Instance — reading the global there would
	// return the stale default value.
	private string? _outputDeviceName = null;

	public AudioManager()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AudioManager] Constructor >>>" );

		app.Logger.WriteLine( "[AudioManager] <<< Constructor" );
	}

	public void OpenDevice()
	{
		using ( _lock.EnterScope() )
		{
			OpenDeviceLocked();
		}
	}

	// The caller MUST already hold _lock.
	private void OpenDeviceLocked()
	{
		if ( _fmodSystem != null )
		{
			return; // Already open
		}

		var app = App.Instance!;

		app.Logger.WriteLine( "[AudioManager] OpenDevice >>>" );

		FMOD.System? createdSystem = null;

		try
		{
			FMOD.Factory.System_Create( out var system );

			createdSystem = system;
			_fmodSystem = system;

			// Select driver before init so the chosen device is used from the start
			ApplyOutputDeviceSetting( app );

			var result = _fmodSystem.Value.init( 512, FMOD.INITFLAGS.NORMAL, IntPtr.Zero );

			if ( result != FMOD.RESULT.OK )
			{
				throw new InvalidOperationException( $"FMOD init failed: {result}" );
			}

			// Populate the device list after FMOD is up
			RefreshOutputDevices();

			// Rebuild the live cache from the durable set of known sound paths. CloseDevice empties the live
			// caches on every close, but _knownSoundPaths survives, so a device switch, the simulator-connect
			// reset or an idle release reloads every sound that was ever requested. LoadSoundFromPathLocked (not
			// LoadSoundFromPath) is used because we already hold _lock here. A failed reload is logged and skipped
			// so one bad file cannot abort the rest or tear down the freshly opened device.
			foreach ( var fmodSound in _soundCache.Values )
			{
				fmodSound.Dispose();
			}

			_soundCache.Clear();
			_channelCache.Clear();

			foreach ( var path in _knownSoundPaths.ToArray() )
			{
				try
				{
					LoadSoundFromPathLocked( path );
				}
				catch ( Exception exception )
				{
					app.Logger.WriteLine( $"[AudioManager] Failed to reload sound '{path}': {exception.Message}" );
				}
			}

			// a freshly opened device counts as activity, so the idle release does not close it straight away
			_lastAudioActivityTickCount = Environment.TickCount64;

			app.Logger.WriteLine( "[AudioManager] Audio device opened successfully" );
		}
		catch ( Exception exception )
		{
			// release the half-created system so repeated failed attempts do not leak FMOD instances
			createdSystem?.release();

			_fmodSystem = null;

			_nextOpenAllowedTickCount = Environment.TickCount64 + OpenRetryDelayMilliseconds;

			app.Logger.WriteLine( $"[AudioManager] Failed to open audio device: {exception.Message}" );
		}

		app.Logger.WriteLine( "[AudioManager] <<< OpenDevice" );
	}

	public void CloseDevice()
	{
		using ( _lock.EnterScope() )
		{
			CloseDeviceLocked();
		}
	}

	// The caller MUST already hold _lock.
	private void CloseDeviceLocked()
	{
		if ( _fmodSystem == null )
		{
			return; // Already closed
		}

		var app = App.Instance!;

		app.Logger.WriteLine( "[AudioManager] CloseDevice >>>" );

		StopAll();

		foreach ( var fmodSound in _soundCache.Values )
		{
			fmodSound.Dispose();
		}

		_soundCache.Clear();
		_channelCache.Clear();

		_fmodSystem.Value.release();
		_fmodSystem = null;

		app.Logger.WriteLine( "[AudioManager] Audio device closed" );

		app.Logger.WriteLine( "[AudioManager] <<< CloseDevice" );
	}

	// Opens the output device on demand if it was released while idle. Returns true if the device is open.
	// It does NOT reopen when the master switch is off (the user closed it on purpose), while the simulator-
	// connect reset is holding the endpoint free, during the retry back-off after a failed open, or after
	// Dispose. Opening takes a moment (FMOD init plus reloading the sound files), but it happens synchronously
	// right before the sound is started, so the first sound is delayed slightly rather than lost. The caller
	// MUST already hold _lock.
	private bool EnsureDeviceOpenLocked()
	{
		if ( _fmodSystem != null )
		{
			return true;
		}

		if ( _disposed || !DataContext.DataContext.Instance.Settings.SoundsMasterEnabled )
		{
			return false;
		}

		if ( ( _simulatorConnectResetInProgress != 0 ) || ( Environment.TickCount64 < _nextOpenAllowedTickCount ) )
		{
			return false;
		}

		App.Instance!.Logger.WriteLine( "[AudioManager] Reopening the audio device on demand" );

		OpenDeviceLocked();

		return _fmodSystem != null;
	}

	// Called when the simulator connects. If we are holding the shared output device open, close it, wait,
	// then reopen it so our audio stream is re-established AFTER iRacing has configured the shared endpoint.
	// This avoids the case where MAIRA (opened first) left the Windows audio engine in a heavy mode that
	// starves iRacing's audio. See SimulatorConnectResetDelayMilliseconds. Runs off-thread so the connect
	// handler is not blocked for the delay.
	public void ResetDeviceForSimulatorConnect()
	{
		var app = App.Instance!;

		if ( _fmodSystem == null )
		{
			// device is not open (sounds are disabled, or it was released while idle) — nothing to reset, but
			// hold off any on-demand reopen for the same delay so we do not grab the shared endpoint while
			// iRacing is still initializing its audio
			using ( _lock.EnterScope() )
			{
				_nextOpenAllowedTickCount = Math.Max( _nextOpenAllowedTickCount, Environment.TickCount64 + SimulatorConnectResetDelayMilliseconds );
			}

			return;
		}

		if ( Interlocked.Exchange( ref _simulatorConnectResetInProgress, 1 ) == 1 )
		{
			return;
		}

		app.Logger.WriteLine( "[AudioManager] ResetDeviceForSimulatorConnect >>>" );

		_ = Task.Run( async () =>
		{
			try
			{
				// CloseDevice/OpenDevice run on the UI thread as they always have (RefreshOutputDevices used to
				// do a synchronous Dispatcher.Invoke while holding _lock, which deadlocked off the UI thread; it
				// now only posts to the dispatcher, but keeping this on the UI thread is the proven path). Only
				// the delay is off-thread, so the UI is not frozen during the 2 second wait. While this reset is
				// in progress EnsureDeviceOpenLocked refuses on-demand reopens, so the endpoint stays free.
				app.Dispatcher.Invoke( () => CloseDevice() );

				await Task.Delay( SimulatorConnectResetDelayMilliseconds );

				app.Dispatcher.Invoke( () =>
				{
					// re-check the master switch in case the user disabled sounds during the delay
					if ( DataContext.DataContext.Instance.Settings.SoundsMasterEnabled )
					{
						OpenDevice();
					}
				} );
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( $"[AudioManager] ResetDeviceForSimulatorConnect exception: {exception.Message}" );
			}
			finally
			{
				_simulatorConnectResetInProgress = 0;

				app.Logger.WriteLine( "[AudioManager] <<< ResetDeviceForSimulatorConnect" );
			}
		} );
	}

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AudioManager] Initialize >>>" );

		if ( !Directory.Exists( _soundsDirectory ) )
		{
			Directory.CreateDirectory( _soundsDirectory );
		}

		_fileSystemWatcher = new FileSystemWatcher( _soundsDirectory, "*.wav" )
		{
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
			EnableRaisingEvents = true,
			IncludeSubdirectories = true
		};

		_fileSystemWatcher.Changed += OnSoundFileChanged;
		_fileSystemWatcher.Created += OnSoundFileChanged;
		_fileSystemWatcher.Renamed += OnSoundFileChanged;

		if ( DataContext.DataContext.Instance.Settings.SoundsMasterEnabled )
		{
			OpenDevice();
		}

		app.Logger.WriteLine( "[AudioManager] <<< Initialize" );
	}

	/// <summary>
	/// Must be called once per timer tick so FMOD can process streaming, callbacks, etc.
	/// Also releases the output device once it has been silent for <see cref="IdleReleaseDelayMilliseconds"/>.
	/// </summary>
	public void Update()
	{
		using ( _lock.EnterScope() )
		{
			if ( _fmodSystem == null )
			{
				return;
			}

			_fmodSystem.Value.update();

			var nowTickCount = Environment.TickCount64;

			_fmodSystem.Value.getChannelsPlaying( out var channelsPlaying );

			if ( channelsPlaying > 0 )
			{
				_lastAudioActivityTickCount = nowTickCount;

				return;
			}

			// Only release while no simulator is connected. During a session the sim holds its own audio stream
			// (so releasing ours would not let the PC sleep anyway), and keeping ours open avoids the small reopen
			// delay on the first alert sound and repeated close/reopen churn on the endpoint iRacing is using.
			if ( ( nowTickCount - _lastAudioActivityTickCount >= IdleReleaseDelayMilliseconds ) && ( _simulatorConnectResetInProgress == 0 ) && !App.Instance!.Simulator.IsConnected )
			{
				App.Instance!.Logger.WriteLine( "[AudioManager] Nothing has played for a while - releasing the audio device so Windows can sleep" );

				CloseDeviceLocked();
			}
		}
	}

	/// <summary>
	/// Switches output to the device named <paramref name="deviceName"/>.
	/// Closes and reopens the FMOD system so the change takes effect.
	/// </summary>
	public void SetOutputDevice( string deviceName )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[AudioManager] SetOutputDevice: {deviceName}" );

		_outputDeviceName = deviceName;

		// Close and reopen on the new driver. OpenDevice rebuilds the sound cache from _knownSoundPaths, so no
		// manual snapshot/reload is needed here.
		CloseDevice();
		OpenDevice();
	}

	// -------------------------------------------------------------------------
	// Device enumeration helpers
	// -------------------------------------------------------------------------

	// Called from OpenDeviceLocked with _lock held. The driver names are read here, under the lock, and only
	// the finished list is handed to the UI thread. Off the UI thread that hand-off is a non-blocking
	// BeginInvoke: the device can now be reopened on demand from any thread that plays a sound, and a blocking
	// Dispatcher.Invoke while holding _lock would deadlock against the UI thread waiting on _lock.
	private void RefreshOutputDevices()
	{
		if ( _fmodSystem == null )
		{
			return;
		}

		var deviceNames = new List<string>();

		_fmodSystem.Value.getNumDrivers( out int count );

		for ( int i = 0; i < count; i++ )
		{
			_fmodSystem.Value.getDriverInfo( i, out string name, 256, out _, out _, out _, out _ );
			deviceNames.Add( name );
		}

		void ApplyDeviceNames()
		{
			// skip the update when nothing changed, so reopening after an idle release does not churn the list
			if ( OutputDevices.SequenceEqual( deviceNames ) )
			{
				return;
			}

			OutputDevices.Clear();

			foreach ( var deviceName in deviceNames )
			{
				OutputDevices.Add( deviceName );
			}
		}

		var dispatcher = App.Instance!.Dispatcher;

		if ( dispatcher.CheckAccess() )
		{
			ApplyDeviceNames();
		}
		else
		{
			dispatcher.BeginInvoke( ApplyDeviceNames );
		}
	}

	/// <summary>
	/// Maps the current <see cref="DataContext.Settings.SoundsOutputDevice"/> name to an FMOD driver
	/// index and calls <c>setDriver</c>. Must be called before <c>init</c>.
	/// </summary>
	private void ApplyOutputDeviceSetting( App app )
	{
		if ( _fmodSystem == null )
		{
			return;
		}

		var desiredName = _outputDeviceName ?? DataContext.DataContext.Instance.Settings.SoundsOutputDevice;

		if ( string.IsNullOrEmpty( desiredName ) || desiredName == DefaultDeviceName )
		{
			_fmodSystem.Value.setDriver( 0 );
			return;
		}

		_fmodSystem.Value.getNumDrivers( out int count );

		for ( int i = 0; i < count; i++ )
		{
			_fmodSystem.Value.getDriverInfo( i, out string name, 256, out _, out _, out _, out _ );

			if ( string.Equals( name, desiredName, StringComparison.OrdinalIgnoreCase ) )
			{
				_fmodSystem.Value.setDriver( i );
				app.Logger.WriteLine( $"[AudioManager] Using output driver {i}: {name}" );
				return;
			}
		}

		app.Logger.WriteLine( $"[AudioManager] Output device '{desiredName}' not found, falling back to default" );
		_fmodSystem.Value.setDriver( 0 );
	}

	// -------------------------------------------------------------------------
	// Sound loading
	// -------------------------------------------------------------------------

	public void LoadSounds( string directory, string[] soundKeys )
	{
		foreach ( var soundKey in soundKeys )
		{
			LoadSound( directory, soundKey );
		}
	}

	public void LoadSound( string directory, string soundKey )
	{
		var path = Path.Combine( _soundsDirectory, directory, $"{soundKey}.wav" );
		LoadSoundFromPath( path );

		path = Path.Combine( _soundsDirectory, directory, $"{soundKey}_custom.wav" );
		LoadSoundFromPath( path );
	}

	private void OnSoundFileChanged( object sender, FileSystemEventArgs e )
	{
		using ( _lock.EnterScope() )
		{
			var now = DateTime.Now;

			var expiredKeys = _debounceMap
				.Where( kvp => ( now - kvp.Value ).TotalSeconds > 10 )
				.Select( kvp => kvp.Key )
				.ToList();

			foreach ( var key in expiredKeys )
			{
				_debounceMap.Remove( key );
			}

			if ( _debounceMap.TryGetValue( e.FullPath, out var lastTime ) )
			{
				if ( ( now - lastTime ).TotalMilliseconds < 500 )
				{
					return;
				}

				_debounceMap[ e.FullPath ] = now;
			}
			else
			{
				_debounceMap.Add( e.FullPath, now );
			}
		}

		Task.Delay( 1000 ).ContinueWith( _ =>
		{
			var app = App.Instance!;

			app.Logger.WriteLine( "[AudioManager] OnSoundFileChanged >>>" );

			try
			{
				LoadSoundFromPath( e.FullPath );

				app.Logger.WriteLine( $"[AudioManager] Hot-reloaded sound: {e.FullPath}" );
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( $"[AudioManager] Failed to reload {e.FullPath}: {exception.Message}" );
			}

			app.Logger.WriteLine( "[AudioManager] <<< OnSoundFileChanged" );
		} );
	}

	private void LoadSoundFromPath( string path )
	{
		using ( _lock.EnterScope() )
		{
			LoadSoundFromPathLocked( path );
		}
	}

	// Records `path` as a known sound and, if the device is open and the file exists, (re)loads it into the
	// live cache. The caller MUST already hold _lock. The path is remembered in _knownSoundPaths even when the
	// device is closed or the file does not exist yet (e.g. a _custom variant the user may add later), so it
	// will be picked up on the next OpenDevice rebuild or file-change reload.
	private void LoadSoundFromPathLocked( string path )
	{
		var key = Path.GetFileNameWithoutExtension( path )?.ToLowerInvariant();

		if ( key == null )
		{
			return;
		}

		_knownSoundPaths.Add( path );

		if ( _fmodSystem == null || !File.Exists( path ) )
		{
			return;
		}

		// Release the old sound if present
		if ( _soundCache.TryGetValue( key, out var existing ) )
		{
			_channelCache.Remove( key );
			existing.Dispose();
		}

		var fmodSound = new FmodSound( path, _fmodSystem.Value );
		var fmodChannel = new FmodChannel( fmodSound, _fmodSystem.Value );

		_soundCache[ key ] = fmodSound;
		_channelCache[ key ] = fmodChannel;
	}

	// -------------------------------------------------------------------------
	// Playback
	// -------------------------------------------------------------------------

	/// <summary>
	/// Decodes <paramref name="mp3Bytes"/> from memory and plays it through the current FMOD output device.
	/// Returns a Task that completes when playback finishes or <paramref name="cancellationToken"/> is cancelled.
	/// </summary>
	public async Task PlayFromMemoryAsync( byte[] mp3Bytes, float volume, CancellationToken cancellationToken = default )
	{
		ArgumentNullException.ThrowIfNull( mp3Bytes );

		FMOD.Sound sound = default;
		FMOD.Channel channel = default;
		bool started = false;

		using ( _lock.EnterScope() )
		{
			if ( !EnsureDeviceOpenLocked() )
			{
				return;
			}

			_lastAudioActivityTickCount = Environment.TickCount64;

			var exinfo = new FMOD.CREATESOUNDEXINFO
			{
				cbsize = Marshal.SizeOf<FMOD.CREATESOUNDEXINFO>(),
				length = (uint) mp3Bytes.Length
			};

			var result = _fmodSystem!.Value.createSound( mp3Bytes, FMOD.MODE.OPENMEMORY | FMOD.MODE.CREATESAMPLE, ref exinfo, out sound );

			if ( result != FMOD.RESULT.OK )
			{
				App.Instance!.Logger.WriteLine( $"[AudioManager] PlayFromMemoryAsync: createSound failed ({result})" );
				return;
			}

			_fmodSystem.Value.playSound( sound, default, true, out channel );
			channel.setVolume( MathZ.Saturate( volume ) );
			channel.setPaused( false );

			started = true;
		}

		if ( !started )
		{
			return;
		}

		try
		{
			while ( true )
			{
				await Task.Delay( 50, cancellationToken );

				channel.isPlaying( out bool playing );

				if ( !playing )
				{
					break;
				}
			}
		}
		catch ( OperationCanceledException )
		{
			channel.stop();
		}
		finally
		{
			sound.release();
		}
	}

	/// <summary>
	/// Plays the sound <paramref name="key"/>, reopening the output device first if it was released while idle.
	/// Returns false only if the output device is unavailable (so the caller can retry later). A sound that is
	/// not loaded still returns true, as before, so a looping effect with a missing file does not retry and log
	/// on every tick.
	/// </summary>
	public bool Play( string key, float volume, float frequencyRatio = 1f, bool loop = false, uint loopStartMs = 0, uint loopEndMs = 0 )
	{
		using ( _lock.EnterScope() )
		{
			if ( !EnsureDeviceOpenLocked() )
			{
				return false;
			}

			if ( !TryGetChannel( key, out var channel ) )
			{
				App.Instance!.Logger.WriteLine( $"[AudioManager] Trying to play sound '{key}' but it was not loaded" );
				return true;
			}

			_lastAudioActivityTickCount = Environment.TickCount64;

			channel!.Play( volume, frequencyRatio, loop, loopStartMs, loopEndMs );

			return true;
		}
	}

	public void Update( string key, float volume, float frequencyRatio = 1f )
	{
		using ( _lock.EnterScope() )
		{
			if ( TryGetChannel( key, out var channel ) )
			{
				channel!.Update( volume, frequencyRatio );
			}
		}
	}

	public void Stop( string key )
	{
		using ( _lock.EnterScope() )
		{
			if ( _channelCache.TryGetValue( $"{key}_custom", out var custom ) )
			{
				custom.Stop();
			}

			if ( _channelCache.TryGetValue( key, out var channel ) )
			{
				channel.Stop();
			}
		}
	}

	public bool IsPlaying( string key )
	{
		using ( _lock.EnterScope() )
		{
			if ( TryGetChannel( key, out var channel ) )
			{
				return channel!.IsPlaying();
			}

			return false;
		}
	}

	private void StopAll()
	{
		foreach ( var channel in _channelCache.Values )
		{
			channel.Stop();
		}
	}

	/// <summary>
	/// Prefers the _custom variant when present, falls back to the base key.
	/// </summary>
	private bool TryGetChannel( string key, out FmodChannel? channel )
	{
		if ( _channelCache.TryGetValue( $"{key}_custom", out channel ) )
		{
			return true;
		}

		return _channelCache.TryGetValue( key, out channel );
	}

	public void Dispose()
	{
		using ( _lock.EnterScope() )
		{
			_disposed = true; // stop late Play calls from reopening the device during shutdown
		}

		_fileSystemWatcher?.Dispose();

		CloseDevice();

		using ( _lock.EnterScope() )
		{
			_soundCache.Clear();
		}
	}
}
