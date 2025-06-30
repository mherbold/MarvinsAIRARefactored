
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.DataContext;

public class ContextSettings
{
	public bool RacingWheelEnableForceFeedback { get; set; }
	public float RacingWheelWheelForce { get; set; }
	public float RacingWheelStrength { get; set; }
	public float RacingWheelMaxForce { get; set; }
	public float RacingWheelAutoMargin { get; set; }
	public RacingWheel.Algorithm RacingWheelAlgorithm { get; set; }
	public float RacingWheelDetailBoost { get; set; }
	public float RacingWheelDetailBoostBias { get; set; }
	public float RacingWheelDeltaLimit { get; set; }
	public float RacingWheelDeltaLimiterBias { get; set; }
	public float RacingWheelSlewCompressionThreshold { get; set; }
	public float RacingWheelSlewCompressionRate { get; set; }
	public float RacingWheelTotalCompressionThreshold { get; set; }
	public float RacingWheelTotalCompressionRate { get; set; }
	public float RacingWheelOutputMinimum { get; set; }
	public float RacingWheelOutputMaximum { get; set; }
	public float RacingWheelOutputCurve { get; set; }
	public float RacingWheelLFEStrength { get; set; }
	public float RacingWheelCrashProtectionGForce { get; set; }
	public float RacingWheelCrashProtectionDuration { get; set; }
	public float RacingWheelCrashProtectionForceReduction { get; set; }
	public float RacingWheelCurbProtectionShockVelocity { get; set; }
	public float RacingWheelCurbProtectionDuration { get; set; }
	public float RacingWheelCurbProtectionForceReduction { get; set; }
	public float RacingWheelParkedStrength { get; set; }
	public float RacingWheelSoftLockStrength { get; set; }
	public float RacingWheelFriction { get; set; }
	public bool RacingWheelCenterWheelWhenNotInCar { get; set; }
	public bool RacingWheelFadeEnabled { get; set; }
	public Pedals.Effect PedalsClutchEffect1 { get; set; }
	public float PedalsClutchStrength1 { get; set; }
	public Pedals.Effect PedalsClutchEffect2 { get; set; }
	public float PedalsClutchStrength2 { get; set; }
	public Pedals.Effect PedalsClutchEffect3 { get; set; }
	public float PedalsClutchStrength3 { get; set; }
	public Pedals.Effect PedalsBrakeEffect1 { get; set; }
	public float PedalsBrakeStrength1 { get; set; }
	public Pedals.Effect PedalsBrakeEffect2 { get; set; }
	public float PedalsBrakeStrength2 { get; set; }
	public Pedals.Effect PedalsBrakeEffect3 { get; set; }
	public float PedalsBrakeStrength3 { get; set; }
	public Pedals.Effect PedalsThrottleEffect1 { get; set; }
	public float PedalsThrottleStrength1 { get; set; }
	public Pedals.Effect PedalsThrottleEffect2 { get; set; }
	public float PedalsThrottleStrength2 { get; set; }
	public Pedals.Effect PedalsThrottleEffect3 { get; set; }
	public float PedalsThrottleStrength3 { get; set; }
	public float PedalsShiftIntoGearFrequency { get; set; }
	public float PedalsShiftIntoGearAmplitude { get; set; }
	public float PedalsShiftIntoGearDuration { get; set; }
	public float PedalsShiftIntoNeutralFrequency { get; set; }
	public float PedalsShiftIntoNeutralAmplitude { get; set; }
	public float PedalsShiftIntoNeutralDuration { get; set; }
	public float PedalsABSEngagedFrequency { get; set; }
	public float PedalsABSEngagedAmplitude { get; set; }
	public bool PedalsABSEngagedFadeWithBrakeEnabled { get; set; }
	public float PedalsStartingRPM { get; set; }
	public bool PedalsVibrateInTopGearEnabled { get; set; }
	public bool PedalsFadeWithThrottleEnabled { get; set; }
	public float PedalsWheelLockFrequency { get; set; }
	public float PedalsWheelLockSensitivity { get; set; }
	public bool PedalsWheelLockFadeWithBrakeEnabled { get; set; }
	public float PedalsWheelSpinFrequency { get; set; }
	public float PedalsWheelSpinSensitivity { get; set; }
	public bool PedalsWheelSpinFadeWithThrottleEnabled { get; set; }
	public float PedalsClutchSlipStart { get; set; }
	public float PedalsClutchSlipEnd { get; set; }
	public float PedalsClutchSlipFrequency { get; set; }
	public float PedalsMinimumFrequency { get; set; }
	public float PedalsMaximumFrequency { get; set; }
	public float PedalsFrequencyCurve { get; set; }
	public float PedalsMinimumAmplitude { get; set; }
	public float PedalsMaximumAmplitude { get; set; }
	public float PedalsAmplitudeCurve { get; set; }
	public float PedalsNoiseDamper { get; set; }
}
