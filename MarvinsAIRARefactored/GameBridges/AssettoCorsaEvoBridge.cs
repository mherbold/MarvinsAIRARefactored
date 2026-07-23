
using System.Text;

using MarvinsAIRARefactored.GameBridges.Ac;

namespace MarvinsAIRARefactored.GameBridges;

/// <summary>
/// Assetto Corsa EVO bridge. EVO publishes the same three Kunos shared memory pages as AC but renames them to
/// acevo_pmf_* and reorganizes the graphics and static layouts (see AcEvoData.cs). The PHYSICS page is
/// byte-for-byte AC1's SPageFilePhysics - confirmed against the 2026-07-22 Mount Panorama capture (packetId
/// cadence 330 Hz, the known left-turn / hard-braking events matched the AC1 signs and axis order, at-rest accG
/// of zero matched the gravity convention, and finalFF is the same normalized +/-1 FFB signal) - so all of the
/// base bridge's physics mapping, sign conventions, 360 Hz torque sub-sampling, and shock velocity derivation
/// are inherited unchanged. Only the slow-page parsing is overridden, reading the few needed graphics/static
/// fields by capture-validated byte offset.
/// </summary>
public class AssettoCorsaEvoBridge : AssettoCorsaBridge
{
	public override string GameName => "Assetto Corsa EVO";
	public override string LocalizationKey => "AssettoCorsaEvo";
	public override string[] ProcessNames => [ "AssettoCorsaEVO" ];

	protected override int PhysicsBufferSize => AcEvoConstants.PhysicsMapSize;
	protected override int GraphicsBufferSize => AcEvoConstants.GraphicsMapSize;
	protected override int StaticBufferSize => AcEvoConstants.StaticMapSize;

	protected override AcDataProvider CreateProvider()
	{
		return new AcLiveDataProvider( AcEvoConstants.PhysicsMapName, AcEvoConstants.GraphicsMapName, AcEvoConstants.StaticMapName );
	}

	// the EVO graphics/static pages are consumed by offset (AcEvoData.cs), so there is nothing to pre-parse
	protected override void OnStaticPageRead()
	{
	}

	protected override void OnGraphicsPageRead()
	{
	}

	// runs at 60 Hz on the multimedia timer worker thread - BitConverter reads only, no allocation
	protected override void FillSlowFrameData()
	{
		var graphicsBuffer = _graphicsBuffer;
		var staticBuffer = _staticBuffer;

		_slowFrame.Status = (AcStatus) BitConverter.ToInt32( graphicsBuffer, AcEvoConstants.GraphicsStatusOffset );
		_slowFrame.IsInPit = graphicsBuffer[ AcEvoConstants.GraphicsIsInPitLaneOffset ] != 0;
		_slowFrame.LapDistPct = BitConverter.ToSingle( graphicsBuffer, AcEvoConstants.GraphicsNposOffset );
		_slowFrame.SessionTimeSeconds = BitConverter.ToUInt32( graphicsBuffer, AcEvoConstants.GraphicsTotalDrivingTimeOffset );

		var timeLeftMilliseconds = BitConverter.ToInt32( graphicsBuffer, AcEvoConstants.GraphicsTimeLeftMsOffset );

		_slowFrame.SessionTimeRemainSeconds = ( timeLeftMilliseconds > 0 ) ? timeLeftMilliseconds / 1000.0 : -1.0;
		_slowFrame.NumberOfLaps = BitConverter.ToInt32( graphicsBuffer, AcEvoConstants.GraphicsTotalLapOffset );

		// current_lap is 1-based when populated but stays 0 in practice/free sessions
		_slowFrame.CompletedLaps = Math.Max( 0, BitConverter.ToInt32( graphicsBuffer, AcEvoConstants.GraphicsCurrentLapOffset ) - 1 );

		_slowFrame.BestLapTimeMs = BitConverter.ToInt32( graphicsBuffer, AcEvoConstants.GraphicsBestLaptimeMsOffset );
		_slowFrame.LastLapTimeMs = BitConverter.ToInt32( graphicsBuffer, AcEvoConstants.GraphicsLastLaptimeMsOffset );
		_slowFrame.Position = (int) BitConverter.ToUInt32( graphicsBuffer, AcEvoConstants.GraphicsCurrentPosOffset );
		_slowFrame.SessionTypeRaw = BitConverter.ToInt32( staticBuffer, AcEvoConstants.StaticSessionTypeOffset );
		_slowFrame.MaxFuel = BitConverter.ToSingle( graphicsBuffer, AcEvoConstants.GraphicsMaxFuelOffset );
		_slowFrame.TrackLengthMeters = BitConverter.ToSingle( staticBuffer, AcEvoConstants.StaticTrackLengthOffset );

		// EVO's static page has no max RPM; the physics page's rev limiter ceiling (an AC1 field beyond the
		// shared 580-byte AcPhysics prefix) serves instead
		_slowFrame.MaxRpm = BitConverter.ToInt32( _physicsBuffer, AcEvoConstants.PhysicsCurrentMaxRpmOffset );
	}

	// called at 1 Hz from UpdateSessionInfo - string allocation is fine here
	protected override void GetSessionStrings( out string track, out string trackConfiguration, out string carModel, out string playerName )
	{
		track = ReadUtf8String( _staticBuffer, AcEvoConstants.StaticTrackOffset );
		trackConfiguration = ReadUtf8String( _staticBuffer, AcEvoConstants.StaticTrackConfigurationOffset );
		carModel = ReadUtf8String( _graphicsBuffer, AcEvoConstants.GraphicsCarModelOffset );

		var driverName = ReadUtf8String( _graphicsBuffer, AcEvoConstants.GraphicsDriverNameOffset );
		var driverSurname = ReadUtf8String( _graphicsBuffer, AcEvoConstants.GraphicsDriverSurnameOffset );

		playerName = $"{driverName} {driverSurname}".Trim();
	}

	protected override string MapSessionTypeName( int sessionTypeRaw )
	{
		return sessionTypeRaw switch
		{
			AcEvoConstants.SessionTypeRace => "Race",
			AcEvoConstants.SessionTypeTimeAttack or AcEvoConstants.SessionTypeHotStint => "Lone Qualify",
			_ => "Practice"
		};
	}

	// EVO strings are single-byte UTF-8 char arrays (AC1's are UTF-16 - see the base ReadString)
	private static string ReadUtf8String( byte[] buffer, int offset )
	{
		var span = buffer.AsSpan( offset, AcEvoConstants.GraphicsStringLength );

		var terminator = span.IndexOf( (byte) 0 );

		if ( terminator == 0 )
		{
			return string.Empty;
		}

		return Encoding.UTF8.GetString( terminator > 0 ? span[ ..terminator ] : span );
	}
}
