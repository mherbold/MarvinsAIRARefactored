
using System.Globalization;
using System.Text;

namespace MarvinsAIRARefactored.GameBridges;

/// <summary>
/// Builds the minimal iRacing session info YAML document that MAIRA consumes, from game-neutral data
/// supplied by a bridge adapter. Only the keys MAIRA actually reads are emitted - the YAML deserializer
/// leaves everything else at its default value.
/// </summary>
public class GameBridgeSessionInfoBuilder
{
	public class DriverModel
	{
		public int CarIdx { get; set; } = 0;
		public string UserName { get; set; } = string.Empty;
		public int UserID { get; set; } = 0;
		public string CarNumber { get; set; } = string.Empty;
		public string CarScreenName { get; set; } = string.Empty;
		public string CarPath { get; set; } = string.Empty;
		public int CarClassID { get; set; } = 0;
		public int CarIsPaceCar { get; set; } = 0;
		public int IRating { get; set; } = 0;
		public int IsSpectator { get; set; } = 0;
		public int TeamID { get; set; } = 0;
	}

	public class SessionModel
	{
		public int SessionNum { get; set; } = 0;
		public string SessionType { get; set; } = "Practice";
	}

	public class TireModel
	{
		public int TireIndex { get; set; } = 0;
		public string TireCompoundType { get; set; } = string.Empty;
	}

	public string TrackDisplayName { get; set; } = string.Empty;
	public string TrackConfigName { get; set; } = string.Empty;
	public float TrackLengthInKm { get; set; } = 0f;
	public int SeriesID { get; set; } = 0;
	public int LeagueID { get; set; } = 0;
	public int SessionID { get; set; } = 0;
	public string TimeOfDay { get; set; } = string.Empty;

	public int DriverCarIdx { get; set; } = 0;
	public string DriverSetupName { get; set; } = string.Empty;
	public int DriverCarGearNumForward { get; set; } = 6;
	public float DriverCarSLFirstRPM { get; set; } = 0f;
	public float DriverCarSLShiftRPM { get; set; } = 0f;
	public float DriverCarSLBlinkRPM { get; set; } = 0f;

	public List<DriverModel> Drivers { get; } = [];
	public List<SessionModel> Sessions { get; } = [];
	public List<TireModel> DriverTires { get; } = [];

	public string ToYaml()
	{
		var stringBuilder = new StringBuilder( 4096 );

		stringBuilder.AppendLine( "---" );

		stringBuilder.AppendLine( "WeekendInfo:" );
		stringBuilder.AppendLine( $" TrackDisplayName: {Quote( TrackDisplayName )}" );
		stringBuilder.AppendLine( $" TrackConfigName: {Quote( TrackConfigName )}" );
		stringBuilder.AppendLine( $" TrackLength: {TrackLengthInKm.ToString( "0.00", CultureInfo.InvariantCulture )} km" );
		stringBuilder.AppendLine( $" SeriesID: {SeriesID}" );
		stringBuilder.AppendLine( $" LeagueID: {LeagueID}" );
		stringBuilder.AppendLine( $" SessionID: {SessionID}" );
		stringBuilder.AppendLine( " SimMode: full" );
		stringBuilder.AppendLine( " WeekendOptions:" );
		stringBuilder.AppendLine( $"  TimeOfDay: {Quote( TimeOfDay )}" );

		stringBuilder.AppendLine( "DriverInfo:" );
		stringBuilder.AppendLine( $" DriverCarIdx: {DriverCarIdx}" );
		stringBuilder.AppendLine( $" DriverSetupName: {Quote( DriverSetupName )}" );
		stringBuilder.AppendLine( $" DriverCarGearNumForward: {DriverCarGearNumForward}" );
		stringBuilder.AppendLine( $" DriverCarSLFirstRPM: {FormatFloat( DriverCarSLFirstRPM )}" );
		stringBuilder.AppendLine( $" DriverCarSLShiftRPM: {FormatFloat( DriverCarSLShiftRPM )}" );
		stringBuilder.AppendLine( $" DriverCarSLBlinkRPM: {FormatFloat( DriverCarSLBlinkRPM )}" );

		// an empty list has to be written as an explicit flow sequence - a bare "Key:" with no items under it
		// is a null value in YAML, and the deserialized list would come back null instead of empty

		if ( DriverTires.Count == 0 )
		{
			stringBuilder.AppendLine( " DriverTires: []" );
		}
		else
		{
			stringBuilder.AppendLine( " DriverTires:" );

			foreach ( var tire in DriverTires )
			{
				stringBuilder.AppendLine( $" - TireIndex: {tire.TireIndex}" );
				stringBuilder.AppendLine( $"   TireCompoundType: {Quote( tire.TireCompoundType )}" );
			}
		}

		if ( Drivers.Count == 0 )
		{
			stringBuilder.AppendLine( " Drivers: []" );
		}
		else
		{
			stringBuilder.AppendLine( " Drivers:" );
		}

		foreach ( var driver in Drivers )
		{
			stringBuilder.AppendLine( $" - CarIdx: {driver.CarIdx}" );
			stringBuilder.AppendLine( $"   UserName: {Quote( driver.UserName )}" );
			stringBuilder.AppendLine( $"   UserID: {driver.UserID}" );
			stringBuilder.AppendLine( $"   TeamID: {driver.TeamID}" );
			stringBuilder.AppendLine( $"   CarNumber: {Quote( driver.CarNumber )}" );
			stringBuilder.AppendLine( $"   CarScreenName: {Quote( driver.CarScreenName )}" );
			stringBuilder.AppendLine( $"   CarPath: {Quote( driver.CarPath )}" );
			stringBuilder.AppendLine( $"   CarClassID: {driver.CarClassID}" );
			stringBuilder.AppendLine( $"   CarIsPaceCar: {driver.CarIsPaceCar}" );
			stringBuilder.AppendLine( $"   IRating: {driver.IRating}" );
			stringBuilder.AppendLine( $"   IsSpectator: {driver.IsSpectator}" );
		}

		stringBuilder.AppendLine( "SessionInfo:" );

		if ( Sessions.Count == 0 )
		{
			stringBuilder.AppendLine( " Sessions: []" );
		}
		else
		{
			stringBuilder.AppendLine( " Sessions:" );
		}

		foreach ( var session in Sessions )
		{
			stringBuilder.AppendLine( $" - SessionNum: {session.SessionNum}" );
			stringBuilder.AppendLine( $"   SessionType: {Quote( session.SessionType )}" );
		}

		stringBuilder.AppendLine( "..." );

		return stringBuilder.ToString();
	}

	private static string FormatFloat( float value )
	{
		return value.ToString( "0.###", CultureInfo.InvariantCulture );
	}

	private static string Quote( string value )
	{
		return $"'{value.Replace( "'", "''" )}'";
	}
}
