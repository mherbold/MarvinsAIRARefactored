
namespace MarvinsAIRARefactored.GameBridges;

public class RFactor2Bridge : GameBridgeAdapter
{
	public override string GameName => "rFactor 2";
	public override string LocalizationKey => "RFactor2";
	public override string[] ProcessNames => [ "rFactor2" ];
	public override GameBridgeCapabilities Capabilities => GameBridgeCapabilities.None;
}
