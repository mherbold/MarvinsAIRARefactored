
namespace MarvinsAIRARefactored.GameBridges;

public class AssettoCorsaCompetizioneBridge : GameBridgeAdapter
{
	public override string GameName => "Assetto Corsa Competizione";
	public override string LocalizationKey => "AssettoCorsaCompetizione";
	public override string[] ProcessNames => [ "AC2-Win64-Shipping" ];
	public override GameBridgeCapabilities Capabilities => GameBridgeCapabilities.None;
}
