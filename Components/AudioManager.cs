
using System.IO;

using SharpDX.XAudio2;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Components
{
	public class AudioManager : IDisposable
	{
		private readonly Lock _lock = new();

		private readonly string _soundsDirectory = Path.Combine( App.DocumentsFolder, "Sounds" );

		private readonly Dictionary<string, CachedSound> _soundCache = [];
		private readonly Dictionary<string, CachedSoundPlayer> _soundPlayerCache = [];

		private FileSystemWatcher? _fileSystemWatcher = null;

		private readonly XAudio2 _xaudio2;
		private readonly MasteringVoice _masteringVoice;

		private readonly Dictionary<string, DateTime> _debounceMap = [];

		public AudioManager()
		{
			_xaudio2 = new XAudio2();
			_masteringVoice = new MasteringVoice( _xaudio2 );
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

			app.Logger.WriteLine( "[AudioManager] <<< Initialize" );
		}

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

			LoadSound( path );

			path = Path.Combine( _soundsDirectory, directory, $"{soundKey}_custom.wav" );

			LoadSound( path );
		}

		private void OnSoundFileChanged( object sender, FileSystemEventArgs e )
		{
			using ( _lock.EnterScope() )
			{
				var now = DateTime.Now;

				var expiredKeys = _debounceMap.Where( kvp => ( now - kvp.Value ).TotalSeconds > 10 ).Select( kvp => kvp.Key ).ToList();

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
					LoadSound( e.FullPath );

					app.Logger.WriteLine( $"[AudioManager] Hot-reloaded sound: {e.FullPath}" );
				}
				catch ( Exception exception )
				{
					app.Logger.WriteLine( $"[AudioManager] Failed to reload {e.FullPath}: {exception.Message}" );
				}

				app.Logger.WriteLine( "[AudioManager] <<< OnSoundFileChanged" );
			} );
		}

		private void LoadSound( string path )
		{
			if ( File.Exists( path ) )
			{
				var key = Path.GetFileNameWithoutExtension( path )?.ToLower();

				if ( key != null )
				{
					var sound = new CachedSound( path );
					var player = new CachedSoundPlayer( sound, _xaudio2 );

					using ( _lock.EnterScope() )
					{
						_soundCache[ key ] = sound;

						if ( _soundPlayerCache.TryGetValue( key, out var existing ) )
						{
							existing.Stop();
							existing.Dispose();
						}

						_soundPlayerCache[ key ] = player;
					}
				}
			}
		}

		public void Play( string key, float volume, float frequencyRatio = 1f, bool loop = false )
		{
			using ( _lock.EnterScope() )
			{
				if ( !_soundPlayerCache.TryGetValue( $"{key}_custom", out var player ) )
				{
					if ( !_soundPlayerCache.TryGetValue( key, out player ) )
					{
						player = null;
					}
				}

				player?.Play( volume, frequencyRatio, loop );
			}
		}

		public void Update( string key, float volume, float frequencyRatio = 1f )
		{
			using ( _lock.EnterScope() )
			{
				if ( !_soundPlayerCache.TryGetValue( $"{key}_custom", out var player ) )
				{
					if ( !_soundPlayerCache.TryGetValue( key, out player ) )
					{
						player = null;
					}
				}

				player?.Update( volume, frequencyRatio );
			}
		}

		public void Stop( string key )
		{
			using ( _lock.EnterScope() )
			{
				if ( _soundPlayerCache.TryGetValue( $"{key}_custom", out var player ) )
				{
					player?.Stop();
				}

				if ( _soundPlayerCache.TryGetValue( key, out player ) )
				{
					player?.Stop();
				}
			}
		}

		public bool SomethingIsPlaying()
		{
			foreach ( var keyValuePair in _soundPlayerCache )
			{
				var soundPlayer = keyValuePair.Value;

				if ( soundPlayer.IsPlaying() )
				{
					return true;
				}
			}

			return false;
		}

		public void Dispose()
		{
			_fileSystemWatcher?.Dispose();

			using ( _lock.EnterScope() )
			{
				foreach ( var player in _soundPlayerCache.Values )
				{
					player.Dispose();
				}

				_soundPlayerCache.Clear();
				_soundCache.Clear();
			}

			_masteringVoice.Dispose();
			_xaudio2.Dispose();
		}
	}
}
