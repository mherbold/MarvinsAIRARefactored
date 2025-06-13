
using System.IO;

using SharpDX.Multimedia;
using SharpDX.XAudio2;

namespace MarvinsAIRARefactored.Components
{
	public class AudioManager : IDisposable
	{
		private readonly Lock _lock = new();

		private readonly string _soundsDirectory = Path.Combine( App.DocumentsFolder, "Sounds" );

		private readonly Dictionary<string, CachedSound> _soundCache = [];
		private readonly Dictionary<string, CachedSoundPlayer> _soundPlayerCache = [];

		private FileSystemWatcher? _fileSystemWatcher = null;

		public void Initialize()
		{
			var app = App.Instance;

			if ( app != null )
			{
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

				string[] soundKeys = [ "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" ];

				foreach ( var soundKey in soundKeys )
				{
					var path = Path.Combine( _soundsDirectory, $"{soundKey}.wav" );

					LoadSound( path );
				}

				app.Logger.WriteLine( "[AudioManager] <<< Initialize" );
			}
		}

		private void OnSoundFileChanged( object sender, FileSystemEventArgs e )
		{
			Task.Delay( 200 ).ContinueWith( _ =>
			{
				var app = App.Instance;

				if ( app != null )
				{
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
				}
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
					var player = new CachedSoundPlayer( sound );

					using ( _lock.EnterScope() )
					{
						_soundCache[ key ] = sound;

						if ( _soundPlayerCache.TryGetValue( key, out var value ) )
						{
							value.Dispose();
						}

						_soundPlayerCache[ key ] = player;
					}
				}
			}
		}

		public void Play( string key )
		{
			using ( _lock.EnterScope() )
			{
				if ( _soundPlayerCache.TryGetValue( key, out var player ) )
				{
					player.Play();
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
		}
	}

	public class CachedSound
	{
		public SoundStream SoundStream { get; }
		public WaveFormat WaveFormat => SoundStream.Format;
		public AudioBuffer AudioBuffer { get; }
		public uint[]? DecodedPacketsInfo { get; }

		public CachedSound( string path )
		{
			SoundStream = new SoundStream( File.OpenRead( path ) );

			AudioBuffer = new AudioBuffer
			{
				Stream = SoundStream.ToDataStream(),
				AudioBytes = (int) SoundStream.Length,
				Flags = BufferFlags.EndOfStream
			};

			DecodedPacketsInfo = SoundStream.DecodedPacketsInfo != null ? Array.ConvertAll( SoundStream.DecodedPacketsInfo, x => (uint) x ) : null;
		}
	}

	public class CachedSoundPlayer : IDisposable
	{
		private readonly CachedSound _sound;
		private readonly XAudio2 _xaudio;
		private readonly MasteringVoice _masteringVoice;
		private SourceVoice? _sourceVoice;

		public CachedSoundPlayer( CachedSound sound )
		{
			_sound = sound;
			_xaudio = new XAudio2();
			_masteringVoice = new MasteringVoice( _xaudio );
		}

		public void Play()
		{
			_sourceVoice?.DestroyVoice();
			_sourceVoice = new SourceVoice( _xaudio, _sound.WaveFormat );
			_sourceVoice.SubmitSourceBuffer( _sound.AudioBuffer, _sound.DecodedPacketsInfo );
			_sourceVoice.Start();
		}

		public void Stop()
		{
			_sourceVoice?.Stop();
		}

		public void Dispose()
		{
			_sourceVoice?.Dispose();
			_masteringVoice.Dispose();
			_xaudio.Dispose();
			_sound.SoundStream.Dispose();
		}
	}
}
