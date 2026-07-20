
namespace MarvinsAIRARefactored.GameBridges;

public class AssettoCorsaBridge : GameBridgeAdapter
{
	public override string GameName => "Assetto Corsa";
	public override string LocalizationKey => "AssettoCorsa";
	public override string[] ProcessNames => [ "acs" ];
	public override GameBridgeCapabilities Capabilities => GameBridgeCapabilities.None;
}
