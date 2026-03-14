
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.DataContext;

public class ContextSettings
{
	#region Racing wheel

	public bool RacingWheelEnableForceFeedback { get; set; } = true;
	public float RacingWheelWheelForce { get; set; } = 5f;
	public float RacingWheelStrength { get; set; } = 0.1f;
	public float RacingWheelAutoMargin { get; set; } = 0f;
	public RacingWheel.Algorithm RacingWheelAlgorithm { get; set; } = RacingWheel.Algorithm.DetailBooster;
	public bool RacingWheelEnableSoftLimiter { get; set; } = true;
	public float RacingWheelDetailBoost { get; set; } = 0.5f;
	public float RacingWheelDetailBoostBias { get; set; } = 0.1f;
	public float RacingWheelDeltaLimit { get; set; } = 500f;
	public float RacingWheelDeltaLimiterBias { get; set; } = 0.2f;
	public float RacingWheelSlewCompressionThreshold { get; set; } = 2f;
	public float RacingWheelSlewCompressionRate { get; set; } = 0.65f;
	public float RacingWheelTotalCompressionThreshold { get; set; } = 0.65f;
	public float RacingWheelTotalCompressionRate { get; set; } = 0.75f;
	public float RacingWheelMulti360HzDetail { get; set; } = 1f;
	public float RacingWheelMultiTorqueCompression { get; set; } = 0f;
	public bool RacingWheelMultiEnableSlewPeakMode { get; set; } = true;
	public float RacingWheelMultiSlewRateReduction { get; set; } = 0f;
	public RacingWheel.MultiFFBSourceOptions RacingWheelMultiFFBSourceSelection { get; set; } = RacingWheel.MultiFFBSourceOptions.Native360Hz;
	public float RacingWheelMultiDetailGain { get; set; } = 0f;
	public float RacingWheelMultiOutputSmoothing { get; set; } = 0f;
	public float RacingWheelOutputMinimum { get; set; } = 0f;
	public float RacingWheelOutputMaximum { get; set; } = 1f;
	public float RacingWheelOutputCurve { get; set; } = 0f;
	public float RacingWheelLFEStrength { get; set; } = 0.05f;
	public float RacingWheelCrashProtectionLongitudalGForce { get; set; } = 8f;
	public float RacingWheelCrashProtectionLateralGForce { get; set; } = 6f;
	public float RacingWheelCrashProtectionDuration { get; set; } = 1f;
	public float RacingWheelCrashProtectionForceReduction { get; set; } = 0.95f;
	public float RacingWheelCurbProtectionShockVelocity { get; set; } = 0.5f;
	public float RacingWheelCurbProtectionDuration { get; set; } = 0.1f;
	public float RacingWheelCurbProtectionForceReduction { get; set; } = 0.75f;
	public float RacingWheelParkedStrength { get; set; } = 0.1f;
	public float RacingWheelParkedFriction { get; set; } = 0f;
	public float RacingWheelSoftLockStrength { get; set; } = 0.25f;
	public float RacingWheelFriction { get; set; } = 0f;
	public float RacingWheelWheelCenteringStrength { get; set; } = 0.75f;
	public float RacingWheelGearChangeVibrateStrength { get; set; } = 0.0f;
	public float RacingWheelABSVibrateStrength { get; set; } = 0.0f;
	public bool RacingWheelCenterWheelWhileRacing { get; set; } = false;
	public bool RacingWheelCenterWheelWhileParked { get; set; } = true;
	public bool RacingWheelFadeEnabled { get; set; } = true;

	#endregion

	#region Steering effects - General

	public string SteeringEffectsCalibrationFileName { get; set; } = string.Empty;

	#endregion

	#region Steering effects - Understeer

	public bool SteeringEffectsUndersteerEnabled { get; set; } = false;
	public float SteeringEffectsUndersteerMinimumThreshold { get; set; } = 0.05f;
	public float SteeringEffectsUndersteerMaximumThreshold { get; set; } = 0.15f;
	public RacingWheel.VibrationPattern SteeringEffectsUndersteerWheelVibrationPattern { get; set; } = RacingWheel.VibrationPattern.SineWave;
	public float SteeringEffectsUndersteerWheelVibrationStrength { get; set; } = 0.1f;
	public float SteeringEffectsUndersteerWheelVibrationMinimumFrequency { get; set; } = 15f;
	public float SteeringEffectsUndersteerWheelVibrationMaximumFrequency { get; set; } = 50f;
	public float SteeringEffectsUndersteerWheelVibrationCurve { get; set; } = 0.25f;
	public RacingWheel.ConstantForceDirection SteeringEffectsUndersteerWheelConstantForceDirection { get; set; } = RacingWheel.ConstantForceDirection.None;
	public float SteeringEffectsUndersteerWheelConstantForceStrength { get; set; } = 0.1f;
	public float SteeringEffectsUndersteerWheelConstantForceCurve { get; set; } = 0f;
	public float SteeringEffectsUndersteerPedalVibrationMinimumFrequency { get; set; } = 0.1f;
	public float SteeringEffectsUndersteerPedalVibrationMaximumFrequency { get; set; } = 1f;
	public float SteeringEffectsUndersteerPedalVibrationCurve { get; set; } = 0f;

	#endregion

	#region Steering effects - Oversteer

	public bool SteeringEffectsOversteerEnabled { get; set; } = false;
	public float SteeringEffectsOversteerMinimumThreshold { get; set; } = 0f;
	public float SteeringEffectsOversteerMaximumThreshold { get; set; } = 0.5f;
	public RacingWheel.VibrationPattern SteeringEffectsOversteerWheelVibrationPattern { get; set; } = RacingWheel.VibrationPattern.None;
	public float SteeringEffectsOversteerWheelVibrationStrength { get; set; } = 0.1f;
	public float SteeringEffectsOversteerWheelVibrationMinimumFrequency { get; set; } = 15f;
	public float SteeringEffectsOversteerWheelVibrationMaximumFrequency { get; set; } = 50f;
	public float SteeringEffectsOversteerWheelVibrationCurve { get; set; } = 0.25f;
	public RacingWheel.ConstantForceDirection SteeringEffectsOversteerWheelConstantForceDirection { get; set; } = RacingWheel.ConstantForceDirection.IncreaseForce;
	public float SteeringEffectsOversteerWheelConstantForceStrength { get; set; } = 0.1f;
	public float SteeringEffectsOversteerWheelConstantForceCurve { get; set; } = 0f;
	public float SteeringEffectsOversteerPedalVibrationMinimumFrequency { get; set; } = 0.1f;
	public float SteeringEffectsOversteerPedalVibrationMaximumFrequency { get; set; } = 1f;
	public float SteeringEffectsOversteerPedalVibrationCurve { get; set; } = 0f;

	#endregion

	#region Steering effects - Seat-of-pants

	public bool SteeringEffectsSeatOfPantsEnabled { get; set; } = false;
	public float SteeringEffectsSeatOfPantsMinimumThreshold { get; set; } = 0f;
	public float SteeringEffectsSeatOfPantsMaximumThreshold { get; set; } = 10f;
	public SteeringEffects.SeatOfPantsAlgorithm SteeringEffectsSeatOfPantsAlgorithm { get; set; } = SteeringEffects.SeatOfPantsAlgorithm.YVelocityOverXVelocity;
	public RacingWheel.VibrationPattern SteeringEffectsSeatOfPantsWheelVibrationPattern { get; set; } = RacingWheel.VibrationPattern.None;
	public float SteeringEffectsSeatOfPantsWheelVibrationStrength { get; set; } = 0.1f;
	public float SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency { get; set; } = 15f;
	public float SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency { get; set; } = 50f;
	public float SteeringEffectsSeatOfPantsWheelVibrationCurve { get; set; } = 0.25f;
	public RacingWheel.ConstantForceDirection SteeringEffectsSeatOfPantsWheelConstantForceDirection { get; set; } = RacingWheel.ConstantForceDirection.IncreaseForce;
	public float SteeringEffectsSeatOfPantsWheelConstantForceStrength { get; set; } = 0.1f;
	public float SteeringEffectsSeatOfPantsWheelConstantForceCurve { get; set; } = 0.25f;
	public float SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequency { get; set; } = 0.1f;
	public float SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequency { get; set; } = 1f;
	public float SteeringEffectsSeatOfPantsPedalVibrationCurve { get; set; } = 0f;

	#endregion

	#region Pedals - General

	public float PedalsMinimumFrequency { get; set; } = 0f;
	public float PedalsMaximumFrequency { get; set; } = 50f;
	public float PedalsFrequencyCurve { get; set; } = 0.25f;
	public float PedalsMinimumAmplitude { get; set; } = 0f;
	public float PedalsMaximumAmplitude { get; set; } = 1f;
	public float PedalsAmplitudeCurve { get; set; } = 0f;

	#endregion

	#region Pedals - Clutch effects

	public Pedals.Effect PedalsClutchEffect1 { get; set; } = Pedals.Effect.GearChange;
	public float PedalsClutchStrength1 { get; set; } = 1f;
	public Pedals.Effect PedalsClutchEffect2 { get; set; } = Pedals.Effect.ClutchSlip;
	public float PedalsClutchStrength2 { get; set; } = 1f;
	public Pedals.Effect PedalsClutchEffect3 { get; set; } = Pedals.Effect.None;
	public float PedalsClutchStrength3 { get; set; } = 1f;

	#endregion

	#region Pedals - Brake effects

	public Pedals.Effect PedalsBrakeEffect1 { get; set; } = Pedals.Effect.ABSEngaged;
	public float PedalsBrakeStrength1 { get; set; } = 1f;
	public Pedals.Effect PedalsBrakeEffect2 { get; set; } = Pedals.Effect.WheelLock;
	public float PedalsBrakeStrength2 { get; set; } = 1f;
	public Pedals.Effect PedalsBrakeEffect3 { get; set; } = Pedals.Effect.UndersteerEffect;
	public float PedalsBrakeStrength3 { get; set; } = 1f;

	#endregion

	#region Pedals - Throttle effects

	public Pedals.Effect PedalsThrottleEffect1 { get; set; } = Pedals.Effect.WheelSpin;
	public float PedalsThrottleStrength1 { get; set; } = 1f;
	public Pedals.Effect PedalsThrottleEffect2 { get; set; } = Pedals.Effect.RPM;
	public float PedalsThrottleStrength2 { get; set; } = 1f;
	public Pedals.Effect PedalsThrottleEffect3 { get; set; } = Pedals.Effect.OversteerEffect;
	public float PedalsThrottleStrength3 { get; set; } = 1f;

	#endregion

	#region Pedals - Effect settings

	public float PedalsShiftIntoGearFrequency { get; set; } = 0.3f;
	public float PedalsShiftIntoGearAmplitude { get; set; } = 1f;
	public float PedalsShiftIntoGearDuration { get; set; } = 0.1f;
	public float PedalsShiftIntoNeutralFrequency { get; set; } = 0.7f;
	public float PedalsShiftIntoNeutralAmplitude { get; set; } = 0.75f;
	public float PedalsShiftIntoNeutralDuration { get; set; } = 0.05f;
	public float PedalsABSEngagedFrequency { get; set; } = 0.5f;
	public float PedalsABSEngagedAmplitude { get; set; } = 1f;
	public bool PedalsABSEngagedFadeWithBrakeEnabled { get; set; } = true;
	public float PedalsStartingRPM { get; set; } = 1f;
	public bool PedalsVibrateInTopGearEnabled { get; set; } = false;
	public bool PedalsFadeWithThrottleEnabled { get; set; } = true;
	public float PedalsWheelLockFrequency { get; set; } = 0.2f;
	public float PedalsWheelLockSensitivity { get; set; } = 0.95f;
	public bool PedalsWheelLockFadeWithBrakeEnabled { get; set; } = true;
	public float PedalsWheelSpinFrequency { get; set; } = 1f;
	public float PedalsWheelSpinSensitivity { get; set; } = 0.95f;
	public bool PedalsWheelSpinFadeWithThrottleEnabled { get; set; } = true;
	public float PedalsClutchSlipStart { get; set; } = 0.25f;
	public float PedalsClutchSlipEnd { get; set; } = 0.75f;
	public float PedalsClutchSlipFrequency { get; set; } = 1f;
	public float PedalsNoiseDamper { get; set; } = 0.1f;

	#endregion

	#region Wind

	public float WindMasterWindPower { get; set; } = 1f;
	public float WindMinimumSpeed { get; set; } = 0f;
	public float WindCurving { get; set; } = 1f;

	#endregion
}
