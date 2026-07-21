
namespace MarvinsAIRARefactored.GameBridges;

[Flags]
public enum GameBridgeCapabilities
{
	None = 0,
	SteeringWheelTorque360Hz = 1 << 0,
	ShockVelocities = 1 << 1,
	TireRumble = 1 << 2,
	SpotterCarLeftRight = 1 << 3,
	IncidentCount = 1 << 4,
	TradingPaints = 1 << 5,
	PitCommands = 1 << 6,
	ReplayControl = 1 << 7,
	ChatCommands = 1 << 8,
	CameraControl = 1 << 9,

	All = SteeringWheelTorque360Hz | ShockVelocities | TireRumble | SpotterCarLeftRight | IncidentCount | TradingPaints | PitCommands | ReplayControl | ChatCommands | CameraControl
}
