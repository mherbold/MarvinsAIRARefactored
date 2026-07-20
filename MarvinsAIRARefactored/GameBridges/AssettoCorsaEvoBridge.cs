
namespace MarvinsAIRARefactored.GameBridges;

public class AssettoCorsaEvoBridge : GameBridgeAdapter
{
	public override string GameName => "Assetto Corsa EVO";
	public override string LocalizationKey => "AssettoCorsaEvo";
	public override string[] ProcessNames => [ "AssettoCorsaEVO" ];
	public override GameBridgeCapabilities Capabilities => GameBridgeCapabilities.None;
}
