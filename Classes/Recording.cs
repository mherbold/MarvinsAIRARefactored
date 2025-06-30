
using System.Globalization;
using System.IO;

using CsvHelper;

namespace MarvinsAIRARefactored.Classes;

public class Recording
{
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
			Description = reader.ReadLine();

			app.Logger.WriteLine( $"[Recording] Description is {Description}" );

			using var csv = new CsvReader( reader, CultureInfo.InvariantCulture );

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
