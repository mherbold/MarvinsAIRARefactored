
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

using CsvHelper;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public sealed class RecordingManager : IDisposable
{
	// 360 Hz capture; the record button toggles recording on/off and the recorder auto-stops (and saves) when
	// the five-minute buffer fills, so recordings can be anywhere from a moment to five minutes long
	private const int SamplesPerSecond = 360;
	private const int MaxRecordingSeconds = 300;

	// automatic stop on lap completion — once the car has left this zone around the recording's starting track
	// position, coming back within it means a complete lap was captured
	private const float LapCompletionRadius = 50f;

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

	public bool IsRecording { get; private set; } = false;

	private FileSystemWatcher? _fileSystemWatcher = null;

	private readonly RecordingData[] _recordingData = new RecordingData[ SamplesPerSecond * MaxRecordingSeconds ];

	private int _recordingDataIndex = 0;

	// lap-completion auto-stop state (see LapCompletionRadius)
	private float _startLapDist = 0f;
	private bool _lapCompletionArmed = false;

	// true while the background CSV write is running — StartRecording is blocked so the buffer can't be
	// overwritten mid-save
	private volatile bool _saveInProgress = false;

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

		// only the metadata (format line + description) is read here — the sample data is loaded on demand when
		// the preview actually replays a recording (see RequestRecordingData)

		foreach ( var file in files )
		{
			var filePath = Path.Combine( _recordingsDirectory, file );

			LoadRecording( filePath );
		}

		if ( ( settings.RacingWheelSelectedRecording == string.Empty ) || !Recordings.ContainsKey( settings.RacingWheelSelectedRecording ) )
		{
			// all recordings may have been rejected (e.g. old pre-version-line format) — never store a null key
			settings.RacingWheelSelectedRecording = Recordings.FirstOrDefault().Key ?? string.Empty;
		}

		for ( var i = 0; i < _recordingData.Length; i++ )
		{
			_recordingData[ i ] = new RecordingData();
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

				// LoadRecording replaced the Recording instance with a fresh metadata-only one — if it's the
				// selected recording, the preview must reload so its sample data gets lazily loaded again
				// (otherwise the hover data card and track map go dark until the next preview update)
				app.RacingWheel.UpdateAlgorithmPreview = true;

				app.Logger.WriteLine( $"[RecordingManager] Hot-reloaded recording: {e.FullPath}" );
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( $"[RecordingManager] Failed to reload {e.FullPath}: {exception.Message}" );
			}

			app.Logger.WriteLine( "[RecordingManager] <<< OnRecordingChanged" );
		} );
	}

	private void LoadRecording( string filePath )
	{
		if ( File.Exists( filePath ) )
		{
			var key = Path.GetFileNameWithoutExtension( filePath )?.ToLower();

			if ( key != null )
			{
				var recording = new Recording( filePath );

				if ( recording.IsValid )
				{
					Recordings[ recording.Path! ] = recording;
				}
			}
		}
	}

	/// <summary>
	/// Kicks off a background load of the given recording's sample data, unloading every other recording so only
	/// the one the preview is using stays in memory. The preview redraws itself when the load completes.
	/// </summary>
	public void RequestRecordingData( Recording recording )
	{
		if ( recording.IsLoadPending || recording.IsDataLoaded || recording.LoadFailed )
		{
			return;
		}

		recording.IsLoadPending = true;

		foreach ( var otherRecording in Recordings.Values )
		{
			if ( otherRecording != recording )
			{
				otherRecording.UnloadData();
			}
		}

		_ = Task.Run( () =>
		{
			recording.LoadData();

			recording.IsLoadPending = false;

			App.Instance!.RacingWheel.UpdateAlgorithmPreview = true;
		} );
	}

	public void Dispose()
	{
		_fileSystemWatcher?.Dispose();

		Recordings.Clear();
	}

	/// <summary>
	/// Captures one 360 Hz sample. Everything the FFB graph modules consume comes from the tick context; the raw
	/// protection telemetry (G forces, peak shock velocity) comes straight from the simulator since the context
	/// only carries the already-derived trigger pulses.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void AddRecordingData( in FFB.FFBTickContext tickContext, float inputTorque60Hz )
	{
		if ( IsRecording )
		{
			var app = App.Instance!;

			var simulator = app.Simulator;

			if ( simulator.IsOnTrack == false )
			{
				// going off track ends the recording — whatever was captured so far is kept
				StopRecording();
			}
			else
			{
				// Don't begin capturing until a 60 Hz frame boundary. The record toggle flips IsRecording from a
				// non-telemetry thread, so it can land mid-burst; deferring the first sample to sub-tick 0
				// guarantees sample N in the file is always sub-tick N % SamplesPerFrame of a burst — the phase the
				// 60 Hz interpolator relies on when the preview replays a recording. Costs at most a few sub-ticks.
				if ( ( _recordingDataIndex == 0 ) && ( tickContext.SampleIndex != 0 ) )
				{
					return;
				}

				ref var recordingData = ref _recordingData[ _recordingDataIndex++ ];

				recordingData.InputTorque60Hz = inputTorque60Hz;
				recordingData.InputTorque360Hz = tickContext.Torque360Hz;

				recordingData.LFEMagnitude = tickContext.LFEMagnitude;

				recordingData.LongitudinalGForce = simulator.LongitudinalGForce;
				recordingData.LateralGForce = simulator.LateralGForce;
				recordingData.MaxShockVelocity = simulator.MaxShockVelocity;

				recordingData.UndersteerEffect = tickContext.UndersteerEffect;
				recordingData.OversteerEffect = tickContext.OversteerEffect;
				recordingData.SeatOfPantsEffect = tickContext.SeatOfPantsEffect;
				recordingData.SkidSlip = tickContext.SkidSlip;

				recordingData.RPM = tickContext.RPM;
				recordingData.ShiftRPM = tickContext.ShiftRPM;
				recordingData.RedlineRPM = tickContext.RedlineRPM;
				recordingData.Gear = tickContext.Gear;
				recordingData.NumForwardGears = tickContext.NumForwardGears;
				recordingData.ABSActive = tickContext.ABSActive;

				recordingData.VelocityMS = tickContext.VelocityMS;
				recordingData.VelocityY = tickContext.VelocityY;
				recordingData.SteeringWheelAngle = tickContext.SteeringWheelAngle;
				recordingData.SteeringWheelAngleMax = tickContext.SteeringWheelAngleMax;
				recordingData.SteeringWheelVelocity = tickContext.SteeringWheelVelocity;
				recordingData.YawRate = simulator.YawRate_ST[ tickContext.SampleIndex ];

				recordingData.TrackPosition = simulator.LapDist;

				recordingData.YawNorth = simulator.YawNorth;
				recordingData.VelocityX = simulator.VelocityX;
				recordingData.Speed = simulator.Speed;

				// prediction-audit extras — per-tick 360 Hz ST samples plus the 60 Hz driver inputs

				var sampleIndex = tickContext.SampleIndex;

				recordingData.VelocityY360Hz = simulator.VelocityY_ST[ sampleIndex ];
				recordingData.LatAccel = simulator.LatAccel_ST[ sampleIndex ];
				recordingData.RollRate = simulator.RollRate_ST[ sampleIndex ];
				recordingData.PitchRate = simulator.PitchRate_ST[ sampleIndex ];
				recordingData.LFShockVelocity = simulator.LFShockVel_ST[ sampleIndex ];
				recordingData.RFShockVelocity = simulator.RFShockVel_ST[ sampleIndex ];
				recordingData.LFShockDeflection = simulator.LFShockDefl_ST[ sampleIndex ];
				recordingData.RFShockDeflection = simulator.RFShockDefl_ST[ sampleIndex ];
				recordingData.Throttle = simulator.Throttle;
				recordingData.Brake = simulator.Brake;

				// automatic stop on lap completion — arm once the car leaves the start zone, stop when it comes
				// back. The s/f wrap is handled explicitly: the circular distance is the shorter way around the
				// lap, so the stop fires at the true radius even when the start zone straddles the s/f line
				// (TrackLength is km from session info; 0 until parsed, which degrades to the plain delta)
				var lapDistDelta = MathF.Abs( simulator.LapDist - _startLapDist );

				var trackLengthMeters = simulator.TrackLength * 1000f;

				if ( ( trackLengthMeters > 0f ) && ( lapDistDelta > trackLengthMeters / 2f ) )
				{
					lapDistDelta = trackLengthMeters - lapDistDelta;
				}

				if ( _lapCompletionArmed )
				{
					if ( lapDistDelta < LapCompletionRadius )
					{
						StopRecording();
					}
				}
				else if ( lapDistDelta > LapCompletionRadius )
				{
					_lapCompletionArmed = true;
				}

				if ( _recordingDataIndex == _recordingData.Length )
				{
					StopRecording();
				}
			}
		}
	}

	/// <summary>
	/// The record button toggles the recorder — press once to start, press again to stop and save.
	/// </summary>
	public void ToggleRecording()
	{
		if ( IsRecording )
		{
			StopRecording();
		}
		else
		{
			StartRecording();
		}
	}

	public void StartRecording()
	{
		var app = App.Instance!;

		if ( app.Simulator.IsOnTrack && !_saveInProgress && !IsRecording )
		{
			app.Logger.WriteLine( "[RecordingManager] StartRecording >>>" );

			_recordingDataIndex = 0;

			_startLapDist = app.Simulator.LapDist;
			_lapCompletionArmed = false;

			IsRecording = true;

			PlayRecordingBeep( Sounds.SoundEffectType.RecordingStarted );

			app.Logger.WriteLine( "[RecordingManager] <<< StartRecording" );
		}
	}

	/// <summary>
	/// Stops the recorder and saves whatever was captured (manual stop, buffer full, or going off track). Called
	/// from the UI thread (button/mapping) or the telemetry thread (auto-stop) — the save itself is offloaded
	/// either way.
	/// </summary>
	public void StopRecording()
	{
		if ( IsRecording )
		{
			var app = App.Instance!;

			app.Logger.WriteLine( "[RecordingManager] StopRecording >>>" );

			IsRecording = false;

			PlayRecordingBeep( Sounds.SoundEffectType.RecordingStopped );

			if ( _recordingDataIndex > 0 )
			{
				SaveRecording();
			}

			app.Logger.WriteLine( "[RecordingManager] <<< StopRecording" );
		}
	}

	private static void PlayRecordingBeep( Sounds.SoundEffectType soundEffectType )
	{
		var app = App.Instance!;

		var settings = DataContext.DataContext.Instance.Settings;

		var beepEnabled = ( soundEffectType == Sounds.SoundEffectType.RecordingStarted ) ? settings.SoundsRecordingStartedEnabled : settings.SoundsRecordingStoppedEnabled;

		if ( settings.SoundsMasterEnabled && beepEnabled )
		{
			app.Sounds.Play( soundEffectType );
		}
	}

	public void SaveRecording()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RecordingManager] SaveRecording >>>" );

		_saveInProgress = true;

		var recordedSampleCount = _recordingDataIndex;

		var durationSeconds = (int) MathF.Round( (float) recordedSampleCount / SamplesPerSecond );

		var gameName = app.GameBridge.ActiveAdapter?.GameName ?? "iRacing";

		var trackConfigSuffix = string.IsNullOrWhiteSpace( app.Simulator.TrackConfigName ) ? string.Empty : $" - {app.Simulator.TrackConfigName}";

		var fileName = $"{gameName} - {app.Simulator.CarScreenName} @ {app.Simulator.TrackDisplayName}{trackConfigSuffix} ({durationSeconds}s)";

		var filePath = Path.Combine( _recordingsDirectory, $"{fileName}.csv" );

		// the CSV write is offloaded so the telemetry thread (which calls this when the recording buffer fills)
		// doesn't stall the FFB frame

		_ = Task.Run( () =>
		{
			try
			{
				// the CsvWriter must be disposed before the StreamWriter closes (it flushes its buffer into the
				// StreamWriter on dispose), and both must be closed before LoadRecording reads the file back
				using ( var writer = new StreamWriter( filePath ) )
				using ( var csv = new CsvWriter( writer, CultureInfo.InvariantCulture ) )
				{
					writer.WriteLine( Recording.FormatLine );
					writer.WriteLine( fileName );

					csv.WriteRecords( _recordingData.Take( recordedSampleCount ) );
				}

				LoadRecording( filePath );

				MainWindow._racingWheelPage.UpdatePreviewRecordingsOptions();

				var settings = DataContext.DataContext.Instance.Settings;

				settings.RacingWheelSelectedRecording = filePath;
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( $"[RecordingManager] Failed to save recording: {exception.Message}" );
			}
			finally
			{
				_saveInProgress = false;
			}

			app.Logger.WriteLine( "[RecordingManager] <<< SaveRecording" );
		} );
	}
}
