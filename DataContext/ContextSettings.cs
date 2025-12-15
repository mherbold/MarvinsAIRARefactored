
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.DataContext;

public class ContextSettings
{
	#region Racing wheel

	public bool RacingWheelEnableForceFeedback { get; set; }
	public float RacingWheelWheelForce { get; set; }
	public float RacingWheelStrength { get; set; }
	public float RacingWheelAutoMargin { get; set; }
	public RacingWheel.Algorithm RacingWheelAlgorithm { get; set; }
	public bool RacingWheelEnableSoftLimiter { get; set; }
	public float RacingWheelDetailBoost { get; set; }
	public float RacingWheelDetailBoostBias { get; set; }
	public float RacingWheelDeltaLimit { get; set; }
	public float RacingWheelDeltaLimiterBias { get; set; }
	public float RacingWheelSlewCompressionThreshold { get; set; }
	public float RacingWheelSlewCompressionRate { get; set; }
	public float RacingWheelTotalCompressionThreshold { get; set; }
	public float RacingWheelTotalCompressionRate { get; set; }
	public float RacingWheelMulti360HzDetail { get; set; }
	public float RacingWheelMultiTorqueCompression { get; set; }
	public bool RacingWheelMultiEnableSlewPeakMode { get; set; }
	public float RacingWheelMultiSlewRateReduction { get; set; }
	public float RacingWheelMultiFFBSource { get; set; }
	public float RacingWheelMultiDetailGain { get; set; }
	public float RacingWheelMultiOutputSmoothing { get; set; }
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
	public float RacingWheelParkedFriction { get; set; }
	public float RacingWheelSoftLockStrength { get; set; }
	public float RacingWheelFriction { get; set; }
	public float RacingWheelWheelCenteringStrength { get; set; }
	public bool RacingWheelVibrateOnGearChange { get; set; }
	public bool RacingWheelVibrateOnABS { get; set; }
	public bool RacingWheelCenterWheelWhileRacing { get; set; }
	public bool RacingWheelCenterWheelWhileParked { get; set; }
	public bool RacingWheelFadeEnabled { get; set; }

	#endregion

	#region Steering effects - General

	public string SteeringEffectsCalibrationFileName { get; set; } = string.Empty;

	#endregion

	#region Steering effects - Understeer

	public float SteeringEffectsUndersteerMinimumThreshold { get; set; }
	public float SteeringEffectsUndersteerMaximumThreshold { get; set; }
	public RacingWheel.VibrationPattern SteeringEffectsUndersteerWheelVibrationPattern { get; set; }
	public float SteeringEffectsUndersteerWheelVibrationStrength { get; set; }
	public float SteeringEffectsUndersteerWheelVibrationMinimumFrequency { get; set; }
	public float SteeringEffectsUndersteerWheelVibrationMaximumFrequency { get; set; }
	public float SteeringEffectsUndersteerWheelVibrationCurve { get; set; }
	public RacingWheel.ConstantForceDirection SteeringEffectsUndersteerWheelConstantForceDirection { get; set; }
	public float SteeringEffectsUndersteerWheelConstantForceStrength { get; set; }
	public float SteeringEffectsUndersteerWheelConstantForceCurve { get; set; }
	public float SteeringEffectsUndersteerPedalVibrationMinimumFrequency { get; set; }
	public float SteeringEffectsUndersteerPedalVibrationMaximumFrequency { get; set; }
	public float SteeringEffectsUndersteerPedalVibrationCurve { get; set; }

	#endregion

	#region Steering effects - Oversteer

	public float SteeringEffectsOversteerMinimumThreshold { get; set; }
	public float SteeringEffectsOversteerMaximumThreshold { get; set; }
	public RacingWheel.VibrationPattern SteeringEffectsOversteerWheelVibrationPattern { get; set; }
	public float SteeringEffectsOversteerWheelVibrationStrength { get; set; }
	public float SteeringEffectsOversteerWheelVibrationMinimumFrequency { get; set; }
	public float SteeringEffectsOversteerWheelVibrationMaximumFrequency { get; set; }
	public float SteeringEffectsOversteerWheelVibrationCurve { get; set; }
	public RacingWheel.ConstantForceDirection SteeringEffectsOversteerWheelConstantForceDirection { get; set; }
	public float SteeringEffectsOversteerWheelConstantForceStrength { get; set; }
	public float SteeringEffectsOversteerWheelConstantForceCurve { get; set; }
	public float SteeringEffectsOversteerPedalVibrationMinimumFrequency { get; set; }
	public float SteeringEffectsOversteerPedalVibrationMaximumFrequency { get; set; }
	public float SteeringEffectsOversteerPedalVibrationCurve { get; set; }

	#endregion

	#region Steering effects - Seat-of-pants

	public float SteeringEffectsSeatOfPantsMinimumThreshold { get; set; }
	public float SteeringEffectsSeatOfPantsMaximumThreshold { get; set; }
	public SteeringEffects.SeatOfPantsAlgorithm SteeringEffectsSeatOfPantsAlgorithm { get; set; }
	public RacingWheel.VibrationPattern SteeringEffectsSeatOfPantsWheelVibrationPattern { get; set; }
	public float SteeringEffectsSeatOfPantsWheelVibrationStrength { get; set; }
	public float SteeringEffectsSeatOfPantsWheelVibrationMinimumFrequency { get; set; }
	public float SteeringEffectsSeatOfPantsWheelVibrationMaximumFrequency { get; set; }
	public float SteeringEffectsSeatOfPantsWheelVibrationCurve { get; set; }
	public RacingWheel.ConstantForceDirection SteeringEffectsSeatOfPantsWheelConstantForceDirection { get; set; }
	public float SteeringEffectsSeatOfPantsWheelConstantForceStrength { get; set; }
	public float SteeringEffectsSeatOfPantsWheelConstantForceCurve { get; set; }
	public float SteeringEffectsSeatOfPantsPedalVibrationMinimumFrequency { get; set; }
	public float SteeringEffectsSeatOfPantsPedalVibrationMaximumFrequency { get; set; }
	public float SteeringEffectsSeatOfPantsPedalVibrationCurve { get; set; }

	#endregion

	#region Pedals - General

	public float PedalsMinimumFrequency { get; set; }
	public float PedalsMaximumFrequency { get; set; }
	public float PedalsFrequencyCurve { get; set; }
	public float PedalsMinimumAmplitude { get; set; }
	public float PedalsMaximumAmplitude { get; set; }
	public float PedalsAmplitudeCurve { get; set; }

	#endregion

	#region Pedals - Clutch effects

	public Pedals.Effect PedalsClutchEffect1 { get; set; }
	public float PedalsClutchStrength1 { get; set; }
	public Pedals.Effect PedalsClutchEffect2 { get; set; }
	public float PedalsClutchStrength2 { get; set; }
	public Pedals.Effect PedalsClutchEffect3 { get; set; }
	public float PedalsClutchStrength3 { get; set; }

	#endregion

	#region Pedals - Brake effects

	public Pedals.Effect PedalsBrakeEffect1 { get; set; }
	public float PedalsBrakeStrength1 { get; set; }
	public Pedals.Effect PedalsBrakeEffect2 { get; set; }
	public float PedalsBrakeStrength2 { get; set; }
	public Pedals.Effect PedalsBrakeEffect3 { get; set; }
	public float PedalsBrakeStrength3 { get; set; }

	#endregion

	#region Pedals - Throttle effects

	public Pedals.Effect PedalsThrottleEffect1 { get; set; }
	public float PedalsThrottleStrength1 { get; set; }
	public Pedals.Effect PedalsThrottleEffect2 { get; set; }
	public float PedalsThrottleStrength2 { get; set; }
	public Pedals.Effect PedalsThrottleEffect3 { get; set; }
	public float PedalsThrottleStrength3 { get; set; }

	#endregion

	#region Pedals - Effect settings

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
	public float PedalsNoiseDamper { get; set; }

	#endregion

	#region Wind

	public float WindMasterWindPower { get; set; }
	public float WindMinimumSpeed { get; set; }
	public float WindCurving { get; set; }

	#endregion
}
