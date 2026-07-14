
using System.Globalization;
using System.IO;

using CsvHelper;
using CsvHelper.Configuration;

namespace MarvinsAIRARefactored.Classes;

/// <summary>
/// One recording file. Only the metadata (format line + description) is read when the file is discovered at app
/// initialize / hot-reload time; the sample data is loaded on demand (see <see cref="LoadData"/>) when the FFB
/// graph preview actually replays this recording, and is unloaded again when another recording takes its place,
/// so at most one recording's samples sit in memory at a time.
/// </summary>
public class Recording
{
	// bump this whenever the file format changes (columns added/renamed, capture rate, etc.) — recordings whose
	// first line doesn't match the current format line are rejected at load, so stale files can never replay with
	// silently-zeroed or misinterpreted columns. v2 = true 360 Hz capture with the InputTorque360Hz column.
	// v3 = dynamic recording length plus the TrackPosition column and the track-map columns
	// (YawNorth, VelocityX, Speed). v4 = the prediction-audit columns (for the PredictionLab telemetry
	// audit): 360 Hz YawRate, VelocityY360Hz, LatAccel, RollRate, PitchRate, front shock velocity and
	// deflection, plus the Throttle and Brake driver inputs; also DROPS the redundant WheelPosition and
	// WheelVelocity columns (pure derivations of SteeringWheelAngle/AngleMax/Velocity — the replay
	// re-derives them).
	public const int FormatVersion = 4;

	private const string FormatLinePrefix = "MAIRA Recording v";

	public static string FormatLine => $"{FormatLinePrefix}{FormatVersion}";

	public bool IsValid { get; private set; } = false;
	public string? Path { get; private set; } = null;
	public string? Description { get; private set; } = null;

	// null until LoadData succeeds — only the UI thread reads this (preview replay + zoom overlay)
	public List<RecordingData>? Data { get; private set; } = null;

	public bool IsDataLoaded => Data != null;
	public bool LoadFailed { get; private set; } = false;

	// set while a background load is in flight so the preview doesn't kick off a second one
	public bool IsLoadPending { get; set; } = false;

	private readonly Lock _loadLock = new();

	public Recording( string path )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[Recording] Reading metadata from {path}" );

		Path = path;

		try
		{
			using var reader = new StreamReader( path );

			// version gate — old recordings have the description on the first line, so they fail this check

			var formatLine = reader.ReadLine();

			if ( formatLine != FormatLine )
			{
				app.Logger.WriteLine( $"[Recording] Skipping unsupported recording format (expected '{FormatLine}')" );

				return;
			}

			Description = reader.ReadLine();

			if ( Description != null )
			{
				app.Logger.WriteLine( $"[Recording] Description is {Description}" );

				IsValid = true;
			}
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[Recording] Error reading file: {exception.Message}" );
		}
	}

	/// <summary>
	/// Reads the sample data from disk (blocking — call from a background task). Returns true when the data is
	/// available. A failed load is latched via <see cref="LoadFailed"/> so the preview doesn't retry every update.
	/// </summary>
	public bool LoadData()
	{
		using ( _loadLock.EnterScope() )
		{
			if ( Data != null )
			{
				return true;
			}

			if ( !IsValid || LoadFailed )
			{
				return false;
			}

			var app = App.Instance!;

			app.Logger.WriteLine( $"[Recording] Loading data from {Path}" );

			try
			{
				using var reader = new StreamReader( Path! );

				reader.ReadLine(); // format line
				reader.ReadLine(); // description

				var configuration = new CsvConfiguration( CultureInfo.InvariantCulture )
				{
					HeaderValidated = null,
					MissingFieldFound = null
				};

				using var csv = new CsvReader( reader, configuration );

				var data = new List<RecordingData>( csv.GetRecords<RecordingData>() );

				app.Logger.WriteLine( $"[Recording] {data.Count} records read" );

				Data = data;

				return true;
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( $"[Recording] Error reading records: {exception.Message}" );

				LoadFailed = true;

				return false;
			}
		}
	}

	public void UnloadData()
	{
		using ( _loadLock.EnterScope() )
		{
			Data = null;
			LoadFailed = false;
		}
	}
}
