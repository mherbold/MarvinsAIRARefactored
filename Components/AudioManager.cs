
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
				IncludeSubdirectories = false
			};

			_fileSystemWatcher.Changed += OnSoundFileChanged;
			_fileSystemWatcher.Created += OnSoundFileChanged;
			_fileSystemWatcher.Renamed += OnSoundFileChanged;

			string[] soundKeys = [
				"0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "cancel", "enter",
					"double_file", "single_file",
					"plus_one_lap", "minus_one_lap",
					"throw_caution_flag", "advance_to_next_session",
					"chat_toggle", "chat_enabled", "chat_disabled",
					"black_flag_drive_through", "black_flag_stop_and_go", "clear_black_flag", "clear_all_black_flags",
					"wave_by_driver", "end_of_line_driver", "remove_driver", "disqualify_driver",
					"live", "pause", "play", "slow_motion", "fast_forward", "rewind", "next_incident", "previous_incident",
					"volume"
			];

			foreach ( var soundKey in soundKeys )
			{
				var path = Path.Combine( _soundsDirectory, $"{soundKey}.wav" );

				LoadSound( path );
			}

			app.Logger.WriteLine( "[AudioManager] <<< Initialize" );
		}

		private void OnSoundFileChanged( object sender, FileSystemEventArgs e )
		{
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

		public void LoadSound( string path )
		{
			if ( File.Exists( path ) )
			{
				string? key = Path.GetFileNameWithoutExtension( path )?.ToLower();

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

		public void Play( string key, float volume, bool loop = false )
		{
			using ( _lock.EnterScope() )
			{
				if ( _soundPlayerCache.TryGetValue( key, out var player ) )
				{
					player.Play( volume, loop );
				}
			}
		}

		public void Stop( string key )
		{
			using ( _lock.EnterScope() )
			{
				if ( _soundPlayerCache.TryGetValue( key, out var player ) )
				{
					player.Stop();
				}
			}
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
