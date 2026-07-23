
using MarvinsAIRARefactored.GameBridges.Ac;

namespace MarvinsAIRARefactored.GameBridges;

/// <summary>
/// Assetto Corsa Rally bridge. Confirmed against the 2026-07-22 capture: Rally uses the classic Local\acpmf_*
/// map names and the PHYSICS page is byte-for-byte AC1's SPageFilePhysics at ~330 Hz (same signs, axis order,
/// gravity convention, and normalized finalFF signal as the validated AC bridge), so all physics mapping is
/// inherited unchanged. However, this early-access build leaves the GRAPHICS page almost entirely unpopulated
/// (only packetId, the current-time string, distanceTraveled, and the world car coordinates are written -
/// status, session, laps, and positions all read zero) and the STATIC page completely zeroed. The overrides
/// below patch around that: the live/off status is derived from the physics packetId advancing (paused or in
/// menus the physics page freezes), max RPM comes from the physics page's rev limiter field, and the empty
/// track/car names get stable fallbacks. Revisit when a Rally update starts populating these pages.
/// </summary>
public class AssettoCorsaRallyBridge : AssettoCorsaBridge
{
	public override string GameName => "Assetto Corsa Rally";
	public override string LocalizationKey => "AssettoCorsaRally";
	public override string[] ProcessNames => [ "acr" ];

	// consider the game live if the physics page advanced within the last half second of 60 Hz frame fills
	private const int LiveFillThreshold = 30;

	private int _lastStatusPacketId = -1;
	private int _fillsSincePhysicsAdvance = LiveFillThreshold;

	// runs at 60 Hz on the multimedia timer worker thread - must not allocate
	protected override void FillSlowFrameData()
	{
		base.FillSlowFrameData();

		// Rally never writes graphics.status, so a frozen physics packetId (pause, menus) is the off signal
		if ( _physics.packetId != _lastStatusPacketId )
		{
			_lastStatusPacketId = _physics.packetId;

			_fillsSincePhysicsAdvance = 0;
		}
		else if ( _fillsSincePhysicsAdvance < LiveFillThreshold )
		{
			_fillsSincePhysicsAdvance++;
		}

		_slowFrame.Status = ( _fillsSincePhysicsAdvance < LiveFillThreshold ) ? AcStatus.Live : AcStatus.Off;

		// the static page (and its maxRpm) is all zeros - the physics page's rev limiter ceiling is an AC1
		// field just beyond the shared 580-byte AcPhysics prefix (read 7250 in the capture)
		_slowFrame.MaxRpm = BitConverter.ToInt32( _physicsBuffer, AcEvoConstants.PhysicsCurrentMaxRpmOffset );
	}

	// called at 1 Hz from UpdateSessionInfo - string allocation is fine here
	protected override void GetSessionStrings( out string track, out string trackConfiguration, out string carModel, out string playerName )
	{
		base.GetSessionStrings( out track, out trackConfiguration, out carModel, out playerName );

		// the static page's name strings are all empty in this build - substitute stable fallbacks so the
		// per-context settings still key on something consistent
		if ( track.Length == 0 )
		{
			track = "Unknown stage";
		}

		if ( carModel.Length == 0 )
		{
			carModel = "Assetto Corsa Rally car";
		}
	}
}
