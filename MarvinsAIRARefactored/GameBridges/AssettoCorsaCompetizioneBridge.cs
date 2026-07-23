
namespace MarvinsAIRARefactored.GameBridges;

/// <summary>
/// Assetto Corsa Competizione uses the same Kunos shared memory (acpmf_physics / acpmf_graphics / acpmf_static)
/// and the same physics layout and coordinate conventions as Assetto Corsa, so this is simply the AC bridge with
/// ACC's process name. Validated against the 2026-07-21 Nurburgring capture (Huracan GT3 EVO): physics updates at
/// ~420 Hz, every sign matches AC, and - unlike vanilla AC where `abs` is a dead constant - ACC drives `abs` as a
/// real 0/1 ABS-active flag, which the inherited `abs > 0.5` test already turns into BrakeABSactive.
/// </summary>
public class AssettoCorsaCompetizioneBridge : AssettoCorsaBridge
{
	public override string GameName => "Assetto Corsa Competizione";
	public override string LocalizationKey => "AssettoCorsaCompetizione";
	public override string[] ProcessNames => [ "AC2-Win64-Shipping" ];
}
