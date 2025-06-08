
using MarvinsAIRARefactored.Enums;

namespace MarvinsAIRARefactored.DataContext;

public class ContextSettings
{
	public Guid RacingWheelDeviceGuid { get; set; }
	public bool RacingWheelEnableForceFeedback {  get; set; }
	public float RacingWheelMaxForce {  get; set; }
	public float RacingWheelAutoMargin {  get; set; }
	public float RacingWheelCrashProtectionGForce {  get; set; }
	public float RacingWheelCrashProtectionDuration {  get; set; }
	public float RacingWheelCrashProtectionForceReduction {  get; set; }
	public float RacingWheelCurbProtectionShockVelocity {  get; set; }
	public float RacingWheelCurbProtectionDuration {  get; set; }
	public float RacingWheelCurbProtectionForceReduction { get; set; }
	public float RacingWheelParkedStrength {  get; set; }
	public float RacingWheelSoftLockStrength {  get; set; }
	public RacingWheelAlgorithmEnum RacingWheelAlgorithm {  get; set; }
	public float RacingWheelDetailBoost {  get; set; }
	public float RacingWheelDeltaLimit {  get; set; }
	public float RacingWheelBias {  get; set; }
	public float RacingWheelCompressionRate {  get; set; }
	public float RacingWheelFriction {  get; set; }
 	public bool RacingWheelFadeEnabled {  get; set; }
	public float PedalsMinimumFrequency { get; set; }
	public float PedalsMaximumFrequency {  get; set; }
	public float PedalsFrequencyCurve {  get; set; }
	public float PedalsMinimumAmplitude {  get; set; }
	public float PedalsMaximumAmplitude {  get; set; }
	public float PedalsAmplitudeCurve {  get; set; }
	public PedalEffectEnum PedalsClutchEffect1 {  get; set; }
	public float PedalsClutchEffect1Strength {  get; set; }
	public PedalEffectEnum PedalsClutchEffect2 {  get; set; }
	public float PedalsClutchEffect2Strength {  get; set; }
	public PedalEffectEnum PedalsClutchEffect3 {  get; set; }
	public float PedalsClutchEffect3Strength {  get; set; }
	public PedalEffectEnum PedalsBrakeEffect1 {  get; set; }
	public float PedalsBrakeEffect1Strength { get; set; }
	public PedalEffectEnum PedalsBrakeEffect2 {  get; set; }
	public float PedalsBrakeEffect2Strength { get; set; }
	public PedalEffectEnum PedalsBrakeEffect3 {  get; set; }
	public float PedalsBrakeEffect3Strength { get; set; }
	public PedalEffectEnum PedalsThrottleEffect1 {  get; set; }
	public float PedalsThrottleEffect1Strength { get; set; }
	public PedalEffectEnum PedalsThrottleEffect2 {  get; set; }
	public float PedalsThrottleEffect2Strength { get; set; }
	public PedalEffectEnum PedalsThrottleEffect3 {  get; set; }
	public float PedalsThrottleEffect3Strength { get; set; }
}
