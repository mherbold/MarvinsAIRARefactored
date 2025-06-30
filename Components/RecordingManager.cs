
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

using CsvHelper;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;

namespace MarvinsAIRARefactored.Components;

public class RecordingManager : IDisposable
{
	private readonly string _recordingsDirectory = Path.Combine( App.DocumentsFolder, "Recordings" );

	private readonly Dictionary<string, Recording> _recordings = [];

	public Recording? Recording
	{
		get
		{
			if ( _recordings.TryGetValue( DataContext.DataContext.Instance.Settings.RacingWheelSelectedRecording, out var value ) )
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

		_fileSystemWatcher.Changed += OnRecordingChanged;
		_fileSystemWatcher.Created += OnRecordingChanged;
		_fileSystemWatcher.Renamed += OnRecordingChanged;

		var files = Directory.GetFiles( _recordingsDirectory, "*.csv" );

		foreach ( var file in files )
		{
			var path = Path.Combine( _recordingsDirectory, file );

			LoadRecording( path );
		}

		app.Logger.WriteLine( "[RecordingManager] <<< Initialize" );
	}

	public void SetMairaComboBoxItemsSource( MairaComboBox mairaComboBox )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[DirectInput] SetMairaComboBoxItemsSource >>>" );

		var settings = DataContext.DataContext.Instance.Settings;

		var dictionary = new Dictionary<string, string>();

		if ( _recordings.Count == 0 )
		{
			dictionary.Add( string.Empty, DataContext.DataContext.Instance.Localization[ "NoRecordingsFound" ] );
		}

		foreach ( var recording in _recordings )
		{
			dictionary.Add( recording.Key, recording.Value.Description! );
		}

		mairaComboBox.ItemsSource = dictionary.OrderBy( keyValuePair => keyValuePair.Value );

		if ( ( settings.RacingWheelSelectedRecording == string.Empty ) || !dictionary.ContainsKey( settings.RacingWheelSelectedRecording ) )
		{
			settings.RacingWheelSelectedRecording = dictionary.FirstOrDefault().Key;
		}

		mairaComboBox.SelectedValue = settings.RacingWheelSelectedRecording;

		app.Logger.WriteLine( "[DirectInput] <<< SetMairaComboBoxItemsSource" );
	}

	private void OnRecordingChanged( object sender, FileSystemEventArgs e )
	{
		Task.Delay( 1000 ).ContinueWith( _ =>
		{
			var app = App.Instance!;

			app.Logger.WriteLine( "[RecordingManager] OnRecordingChanged >>>" );

			try
			{
				LoadRecording( e.FullPath );

				app.RecordingManager.SetMairaComboBoxItemsSource( app.MainWindow.RacingWheel_PreviewRecordings_ComboBox );

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
					_recordings[ recording.Path! ] = recording;
				}
			}
		}
	}

	public void Dispose()
	{
		_fileSystemWatcher?.Dispose();

		_recordings.Clear();
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
