
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

using CsvHelper;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public sealed class RecordingManager : IDisposable
{
	private readonly string _recordingsDirectory = Path.Combine( App.DocumentsFolder, "Recordings" );

	public Dictionary<string, Recording> Recordings { get; private set; } = [];

	public Recording? Recording
	{
		get
		{
			if ( Recordings.TryGetValue( DataContext.DataContext.Instance.Settings.RacingWheelSelectedRecording, out var value ) )
			{
				return value;
			}
			else
			{
				return null;
			}
		}
	}

	private FileSystemWatcher? _fileSystemWatcher = null;

	private readonly RecordingData[] _recordingData = new RecordingData[ 3840 ];

	private int _recordingDataIndex;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RecordingManager] Initialize >>>" );

		var settings = DataContext.DataContext.Instance.Settings;

		if ( !Directory.Exists( _recordingsDirectory ) )
		{
			Directory.CreateDirectory( _recordingsDirectory );
		}

		_fileSystemWatcher = new FileSystemWatcher( _recordingsDirectory, "*.csv" )
		{
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
			EnableRaisingEvents = true,
			IncludeSubdirectories = false
		};

		_fileSystemWatcher.Changed += OnRecordingFilesChanged;
		_fileSystemWatcher.Created += OnRecordingFilesChanged;
		_fileSystemWatcher.Renamed += OnRecordingFilesChanged;

		var files = Directory.GetFiles( _recordingsDirectory, "*.csv" );

		foreach ( var file in files )
		{
			var path = Path.Combine( _recordingsDirectory, file );

			LoadRecording( path );
		}

		if ( ( settings.RacingWheelSelectedRecording == string.Empty ) || !Recordings.ContainsKey( settings.RacingWheelSelectedRecording ) )
		{
			settings.RacingWheelSelectedRecording = Recordings.FirstOrDefault().Key;
		}

		app.Logger.WriteLine( "[RecordingManager] <<< Initialize" );
	}

	private void OnRecordingFilesChanged( object sender, FileSystemEventArgs e )
	{
		Task.Delay( 2000 ).ContinueWith( _ =>
		{
			var app = App.Instance!;

			app.Logger.WriteLine( "[RecordingManager] OnRecordingChanged >>>" );

			try
			{
				LoadRecording( e.FullPath );

				MainWindow._racingWheelPage.UpdatePreviewRecordingsOptions();

				app.Logger.WriteLine( $"[RecordingManager] Hot-reloaded recording: {e.FullPath}" );
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( $"[RecordingManager] Failed to reload {e.FullPath}: {exception.Message}" );
			}

			app.Logger.WriteLine( "[RecordingManager] <<< OnRecordingChanged" );
		} );
	}

	private void LoadRecording( string path )
	{
		if ( File.Exists( path ) )
		{
			var key = Path.GetFileNameWithoutExtension( path )?.ToLower();

			if ( key != null )
			{
				var recording = new Recording( path );

				if ( recording.IsValid )
				{
					Recordings[ recording.Path! ] = recording;
				}
			}
		}
	}

	public void Dispose()
	{
		_fileSystemWatcher?.Dispose();

		Recordings.Clear();
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void AddRecordingData( float inputTorque60Hz, float inputTorque500Hz )
	{
		if ( _recordingDataIndex < _recordingData.Length )
		{
			_recordingData[ _recordingDataIndex++ ] = new RecordingData()
			{
				InputTorque60Hz = inputTorque60Hz,
				InputTorque500Hz = inputTorque500Hz,
			};

			var app = App.Instance!;
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void ResetRecording()
	{
		_recordingDataIndex = 0;
	}

	public void SaveRecording()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RecordingManager] SaveRecording >>>" );

		var filePath = Path.Combine( _recordingsDirectory, "NewRecording.csv" );

		using var writer = new StreamWriter( filePath );

		writer.WriteLine( "New recording" );

		using var csv = new CsvWriter( writer, CultureInfo.InvariantCulture );

		csv.WriteRecords( _recordingData );

		app.Logger.WriteLine( "[RecordingManager] <<< SaveRecording" );
	}
}
