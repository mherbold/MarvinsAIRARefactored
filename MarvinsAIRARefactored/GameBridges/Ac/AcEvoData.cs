/*
Assetto Corsa EVO shared memory layout constants.

EVO publishes the same three Kunos pages as AC1 but renames the maps to Local\acevo_pmf_* and reorganizes the
graphics and static pages completely (new SPageFileGraphicEvo / SPageFileStaticEvo layouts built from fixed-size
SMEvo* sub-blocks). The PHYSICS page, however, is byte-for-byte identical to AC1's SPageFilePhysics - so the
bridge keeps parsing it with the shared AcPhysics struct and only the graphics/static access changes.

Because the EVO graphics page is ~4.9 KB of mostly HUD data (with 256/128-byte opaque sub-blocks), the bridge
reads the handful of fields it needs by byte offset instead of transcribing the whole struct. Every offset below
was hand-computed from the community-documented C++ header (github.com/dSyncro/acevo-shared-memory
src/bindings/source/wrapper.hpp, #pragma pack(4)) and then VALIDATED against the 2026-07-22 Mount Panorama
capture (EVO 0.8.1): status read 2 (AC_LIVE), graphics steering_percent correlated 0.999 with the physics page's
steerAngle, driver name / car model / track strings decoded correctly, and session_state.time_left_ms agreed
exactly with its neighbouring "8:43" display string. Do not change an offset without re-validating on a capture.

EVO strings are single-byte UTF-8 char arrays (AC1's are UTF-16), so the bridge decodes them with a byte-span
reader instead of the char-span reader used for AC1.
*/

namespace MarvinsAIRARefactored.GameBridges.Ac;

public static class AcEvoConstants
{
	public const string PhysicsMapName = "Local\\acevo_pmf_physics";
	public const string GraphicsMapName = "Local\\acevo_pmf_graphics";
	public const string StaticMapName = "Local\\acevo_pmf_static";

	// actual map sizes observed in the capture (page-aligned by the game)
	public const int PhysicsMapSize = 4096;
	public const int GraphicsMapSize = 8192;
	public const int StaticMapSize = 4096;

	// === physics page (AC1 layout; only fields BEYOND the shared 580-byte AcPhysics prefix are listed) ===

	// int - rev limiter ceiling; an AC1 extended field, so the Rally bridge reads it too (EVO capture read
	// 6200 for the Abarth, Rally capture read 7250)
	public const int PhysicsCurrentMaxRpmOffset = 588;

	// === graphics page (SPageFileGraphicEvo) ===

	public const int GraphicsStatusOffset = 4;               // int - 0 off, 1 replay, 2 live, 3 pause (same values as AC1)
	public const int GraphicsTotalDrivingTimeOffset = 168;   // uint - total driving time in whole seconds (monotonic)
	public const int GraphicsNposOffset = 1244;              // float - normalized lap position 0..1
	public const int GraphicsCurrentPosOffset = 2388;        // uint
	public const int GraphicsLastLaptimeMsOffset = 2396;     // int
	public const int GraphicsBestLaptimeMsOffset = 2400;     // int
	public const int GraphicsTimeLeftMsOffset = 2524;        // int - session_state.time_left_ms (SMEvoSessionState starts at 2476)
	public const int GraphicsTotalLapOffset = 2544;          // int - session_state.total_lap (0 in practice)
	public const int GraphicsCurrentLapOffset = 2548;        // int - session_state.current_lap (0 in practice)
	public const int GraphicsDriverNameOffset = 3020;        // char[33] UTF-8
	public const int GraphicsDriverSurnameOffset = 3053;     // char[33] UTF-8
	public const int GraphicsCarModelOffset = 3086;          // char[33] UTF-8 (read 'Abarth 695 Biposto')
	public const int GraphicsIsInPitLaneOffset = 3120;       // bool (1 byte)
	public const int GraphicsMaxFuelOffset = 3928;           // float - litres (read 35.0)

	public const int GraphicsStringLength = 33;

	// === static page (SPageFileStaticEvo) ===

	public const int StaticSessionTypeOffset = 32;           // int - -1 unknown, 0 time attack, 1 race, 2 hot stint, 3 cruise
	public const int StaticTrackOffset = 136;                // char[33] UTF-8 (read 'Mount Panorama')
	public const int StaticTrackConfigurationOffset = 169;   // char[33] UTF-8 (read 'GP')
	public const int StaticTrackLengthOffset = 204;          // float - metres (read 6213)

	// EVO session type values (ACEVO_SESSION_TYPE - different from AC1's AcSessionType)
	public const int SessionTypeTimeAttack = 0;
	public const int SessionTypeRace = 1;
	public const int SessionTypeHotStint = 2;
	public const int SessionTypeCruise = 3;
}
