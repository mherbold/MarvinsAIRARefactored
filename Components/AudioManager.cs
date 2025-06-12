
using System.IO;

using NAudio.Wave;

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

					string? key = Path.GetFileNameWithoutExtension( e.Name )?.ToLower();

					if ( key != null )
					{
						try
						{
							LoadSound( key, e.FullPath );

							app.Logger.WriteLine( $"[AudioManager] Hot-reloaded sound: {key}" );
						}
						catch ( Exception exception )
						{
							app.Logger.WriteLine( $"[AudioManager] Failed to reload {key}: {exception.Message}" );
						}
					}

					app.Logger.WriteLine( "[AudioManager] <<< OnSoundFileChanged" );
				}
			} );
		}

		public void LoadSound( string key, string path )
		{
			if ( !File.Exists( path ) )
			{
				return;
			}

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
		public float[] AudioData { get; private set; }
		public WaveFormat WaveFormat { get; private set; }

		public CachedSound( string audioFileName )
		{
			using var audioFileReader = new AudioFileReader( audioFileName );

			WaveFormat = audioFileReader.WaveFormat;

			var wholeFile = new List<float>( (int) ( audioFileReader.Length / 4 ) );

			var readBuffer = new float[ audioFileReader.WaveFormat.SampleRate * audioFileReader.WaveFormat.Channels ];

			int samplesRead;

			while ( ( samplesRead = audioFileReader.Read( readBuffer, 0, readBuffer.Length ) ) > 0 )
			{
				wholeFile.AddRange( readBuffer.Take( samplesRead ) );
			}

			AudioData = [ .. wholeFile ];
		}
	}

	public class CachedSoundPlayer : IDisposable
	{
		private readonly WaveOutEvent _outputDevice;
		private readonly BufferedWaveProvider _waveProvider;

		public CachedSoundPlayer( CachedSound sound )
		{
			_outputDevice = new WaveOutEvent();
			_waveProvider = new BufferedWaveProvider( sound.WaveFormat );

			_outputDevice.Init( _waveProvider );
		}

		public void Play() => _outputDevice.Play();

		public void Stop() => _outputDevice.Stop();

		public void Dispose() => _outputDevice.Dispose();
	}
}
