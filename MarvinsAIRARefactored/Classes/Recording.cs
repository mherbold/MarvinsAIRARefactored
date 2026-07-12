
using System.Globalization;
using System.IO;

using CsvHelper;
using CsvHelper.Configuration;

namespace MarvinsAIRARefactored.Classes;

public class Recording
{
	// bump this whenever the file format changes (columns added/renamed, capture rate, etc.) — recordings whose
	// first line doesn't match the current format line are rejected at load, so stale files can never replay with
	// silently-zeroed or misinterpreted columns. v2 = true 360 Hz capture with the InputTorque360Hz column.
	public const int FormatVersion = 2;

	private const string FormatLinePrefix = "MAIRA Recording v";

	public static string FormatLine => $"{FormatLinePrefix}{FormatVersion}";

	public bool IsValid { get; private set; } = false;
	public string? Path { get; private set; } = null;
	public string? Description { get; private set; } = null;
	public List<RecordingData>? Data { get; private set; } = null;

	public Recording( string path )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[Recording] Reading from {path}" );

		Path = path;

		using var reader = new StreamReader( path );

		if ( reader != null )
		{
			// version gate — old recordings have the description on the first line, so they fail this check

			var formatLine = reader.ReadLine();

			if ( formatLine != FormatLine )
			{
				app.Logger.WriteLine( $"[Recording] Skipping unsupported recording format (expected '{FormatLine}')" );

				return;
			}

			Description = reader.ReadLine();

			app.Logger.WriteLine( $"[Recording] Description is {Description}" );

			var configuration = new CsvConfiguration( CultureInfo.InvariantCulture )
			{
				HeaderValidated = null,
				MissingFieldFound = null
			};

			using var csv = new CsvReader( reader, configuration );

			try
			{
				Data = [ .. csv.GetRecords<RecordingData>() ];

				app.Logger.WriteLine( $"[Recording] {Data.Count} records read" );

				IsValid = true;
			}
			catch ( Exception )
			{
				app.Logger.WriteLine( $"[Recording] Error reading records" );
			}
		}
		else
		{
			app.Logger.WriteLine( $"[Recording] Error opening file" );
		}
	}
}
