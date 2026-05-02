
using System.Collections.ObjectModel;
using System.IO;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Components;

public sealed class AudioManager : IDisposable
{
	private readonly Lock _lock = new();

	private readonly string _soundsDirectory = Path.Combine( App.DocumentsFolder, "Sounds" );

	// Key = lowercase filename-without-extension (e.g. "click", "click_custom")
	private readonly Dictionary<string, FmodSound> _soundCache = [];
	private readonly Dictionary<string, FmodChannel> _channelCache = [];

	private FileSystemWatcher? _fileSystemWatcher = null;

	private FMOD.System? _fmodSystem;

	private readonly Dictionary<string, DateTime> _debounceMap = [];

	/// <summary>
	/// List of available output device names. First entry is always the default-device placeholder.
	/// Populated in <see cref="OpenDevice"/> and exposed for binding to the UI.
	/// </summary>
	public ObservableCollection<string> OutputDevices { get; } = [];

	public const string DefaultDeviceName = "[Default Windows Sound Device]";

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
			if ( _fmodSystem != null )
			{
				return; // Already open
			}

			var app = App.Instance!;

			app.Logger.WriteLine( "[AudioManager] OpenDevice >>>" );

			try
			{
				FMOD.Factory.System_Create( out var system );

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

				// Reload all cached sounds on the new device
				var paths = _soundCache.Values.Select( s => s.FilePath ).ToList();

				foreach ( var fmodSound in _soundCache.Values )
				{
					fmodSound.Dispose();
				}

				_soundCache.Clear();
				_channelCache.Clear();

				foreach ( var path in paths )
				{
					LoadSoundFromPath( path );
				}

				app.Logger.WriteLine( "[AudioManager] Audio device opened successfully" );
			}
			catch ( Exception exception )
			{
				_fmodSystem = null;

				app.Logger.WriteLine( $"[AudioManager] Failed to open audio device: {exception.Message}" );
			}

			app.Logger.WriteLine( "[AudioManager] <<< OpenDevice" );
		}
	}

	public void CloseDevice()
	{
		using ( _lock.EnterScope() )
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
	/// </summary>
	public void Update()
	{
		using ( _lock.EnterScope() )
		{
			if ( _fmodSystem.HasValue ) _fmodSystem.Value.update();
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

		// Snapshot which sound file paths were loaded so we can reload them after reopen
		List<string> loadedPaths;

		using ( _lock.EnterScope() )
		{
			loadedPaths = _soundCache.Values.Select( s => s.FilePath ).ToList();
		}

		CloseDevice();
		OpenDevice();

		// Re-load the sounds that were in the cache before the device change
		foreach ( var path in loadedPaths )
		{
			LoadSoundFromPath( path );
		}
	}

	// -------------------------------------------------------------------------
	// Device enumeration helpers
	// -------------------------------------------------------------------------

	private void RefreshOutputDevices()
	{
		App.Instance!.Dispatcher.Invoke( () =>
		{
			OutputDevices.Clear();

			if ( _fmodSystem == null )
			{
				return;
			}

			_fmodSystem.Value.getNumDrivers( out int count );

			for ( int i = 0; i < count; i++ )
			{
				_fmodSystem.Value.getDriverInfo( i, out string name, 256, out _, out _, out _, out _ );
				OutputDevices.Add( name );
			}
		} );
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

		var desiredName = DataContext.DataContext.Instance.Settings.SoundsOutputDevice;

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
		if ( !File.Exists( path ) )
		{
			return;
		}

		var key = Path.GetFileNameWithoutExtension( path )?.ToLowerInvariant();

		if ( key == null )
		{
			return;
		}

		using ( _lock.EnterScope() )
		{
			if ( _fmodSystem == null )
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
	}

	// -------------------------------------------------------------------------
	// Playback
	// -------------------------------------------------------------------------

	public void Play( string key, float volume, float frequencyRatio = 1f, bool loop = false, uint loopStartMs = 0, uint loopEndMs = 0 )
	{
		using ( _lock.EnterScope() )
		{
			if ( !TryGetChannel( key, out var channel ) )
			{
				App.Instance!.Logger.WriteLine( $"[AudioManager] Trying to play sound '{key}' but it was not loaded" );
				return;
			}

			channel!.Play( volume, frequencyRatio, loop, loopStartMs, loopEndMs );
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
		_fileSystemWatcher?.Dispose();

		CloseDevice();

		using ( _lock.EnterScope() )
		{
			_soundCache.Clear();
		}
	}
}
