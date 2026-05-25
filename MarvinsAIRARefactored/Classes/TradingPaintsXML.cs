
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace MarvinsAIRARefactored.Classes;

public static class TradingPaintsXml
{
	public enum Type
	{
		Unknown = 0,
		Car,
		CarNum,
		CarSpec,
		CarDecal,
		Suit,
		Helmet
	}

	public sealed class Asset
	{
		public string FileId { get; init; } = string.Empty;
		public string FileURL { get; init; } = string.Empty;
		public Uri? FileUri => Uri.TryCreate( FileURL, UriKind.Absolute, out var uri ) ? uri : null;
		public long UserID { get; init; }
		public string Directory { get; init; } = string.Empty;
		public long FileSize { get; init; }
		public Type Type { get; init; }
		public int TeamId { get; init; }
		public string? Ext { get; init; }
	}

	public static IReadOnlyList<Asset> ParseAssets( Stream xmlStream )
	{
		ArgumentNullException.ThrowIfNull( xmlStream );

		var doc = XDocument.Load( xmlStream );

		var carsElement = doc.Root?.Element( "Cars" );

		if ( carsElement is null )
		{
			return [];
		}

		var results = new List<Asset>();

		foreach ( var carElement in carsElement.Elements( "Car" ) )
		{
			var directory = (string?) carElement.Element( "directory" ) ?? string.Empty;
			var fileUrl = (string?) carElement.Element( "file" ) ?? string.Empty;
			var userId = ParseInt64( (string?) carElement.Element( "userid" ) );
			var fileSize = ParseInt64( (string?) carElement.Element( "filesize" ) );
			var teamId = ParseInt32( (string?) carElement.Element( "teamid" ) );

			var asset = new Asset
			{
				FileId = (string?) carElement.Element( "carid" ) ?? string.Empty,
				FileURL = fileUrl,
				UserID = userId,
				Directory = ( directory == "suits" || directory == "helmets" ) ? string.Empty : directory,
				FileSize = fileSize,
				Type = ParseType( (string?) carElement.Element( "type" ) ),
				TeamId = teamId,
				Ext = (string?) carElement.Element( "ext" )
			};

			results.Add( asset );
		}

		return results;
	}

	private static Type ParseType( string? value )
	{
		var s = ( value ?? string.Empty ).Trim().ToLowerInvariant();

		return s switch
		{
			"car" => Type.Car,
			"car_num" => Type.CarNum,
			"car_spec" => Type.CarSpec,
			"car_decal" => Type.CarDecal,
			"suit" => Type.Suit,
			"helmet" => Type.Helmet,
			_ => Type.Unknown
		};
	}

	private static long ParseInt64( string? s ) => long.TryParse( s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val ) ? val : 0L;
	private static int ParseInt32( string? s ) => int.TryParse( s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val ) ? val : 0;
}
