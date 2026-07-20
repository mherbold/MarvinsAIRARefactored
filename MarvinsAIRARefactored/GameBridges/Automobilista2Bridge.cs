
namespace MarvinsAIRARefactored.GameBridges;

public class Automobilista2Bridge : GameBridgeAdapter
{
	public override string GameName => "Automobilista 2";
	public override string LocalizationKey => "Automobilista2";
	public override string[] ProcessNames => [ "AMS2AVX", "AMS2" ];
	public override GameBridgeCapabilities Capabilities => GameBridgeCapabilities.None;
}
