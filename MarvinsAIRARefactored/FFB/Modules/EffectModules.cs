
using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.FFB.Modules;

// Effect modules operate on the Nm main bus. Multiplicative effects (crash/curb scale, decrease-force) are
// scale-invariant and apply identically in Nm or normalized space; additive effects (increase-force) scale
// their contribution by ctx.MaxForce because the old code added a normalized contribution to the normalized
// bus. (The old purely-additive effects — LFE, friction, soft lock, wheel centering — are source modules in
// SourceModules.cs now, summed in with Add.) The preview replay rebuilds the context from the
// recording's telemetry (including the raw G forces / shock velocity the protection modules compare against
// their own thresholds), so effects render in the preview just as they behaved live; older two-column
// recordings replay with zero telemetry, leaving these modules inert like the old preview.

/// <summary>
/// Crash protection (old 1266–1290 + scale 1398). PrePass detects the trigger ITSELF — comparing the raw
/// G-force telemetry in the context against this module's own thresholds, so multiple crash protection nodes
/// in one graph trigger independently — and advances the timer (re-armed every tick the telemetry stays over
/// a threshold, so a sustained impact holds it at full). Process multiplies by the scale, which ramps back to
/// unity over the user-set recovery time (0 = instant snap-back) after the hold duration expires. ORs itself
/// into the engine's active flag (any-node-active drives the graph gutter, G Tensioner, and announcements).
/// </summary>
public sealed class CrashProtectionModule : FFBModule
{
	private const int LongGForce = 1;
	private const int LatGForce = 2;
	private const int Duration = 3;
	private const int ForceReduction = 4;
	private const int RecoveryTime = 5;

	private float _recoveryTimeMS;

	private float _timerMS;
	private float _scale = 1f;

	public override void Reset()
	{
		_timerMS = 0f;
		_scale = 1f;
	}

	protected override void OnValuesChanged()
	{
		_recoveryTimeMS = _v[ RecoveryTime ] * 1000f;
	}

	public override bool HasPrePass => true;

	public override void PrePass( in FFBTickContext ctx )
	{
		if ( !Enabled )
		{
			_scale = 1f;

			return;
		}

		// Self-triggered from the raw telemetry against this module's OWN thresholds. A threshold parked at the
		// top of its range (20 g) is "off", and a would-do-nothing configuration (zero duration or zero force
		// reduction) never triggers — the same guards the old Simulator-side detection applied to the published
		// aggregate. The context telemetry is zeroed off track / inside the post-reset block window, so those
		// gates ride along for free.
		var triggered = ( _v[ Duration ] > 0f ) && ( _v[ ForceReduction ] > 0f )
			&& ( ( ( _v[ LongGForce ] < 20f ) && ( ctx.LongitudinalGForce >= _v[ LongGForce ] ) )
				|| ( ( _v[ LatGForce ] < 20f ) && ( ctx.LateralGForce >= _v[ LatGForce ] ) ) );

		if ( triggered || TestActive )
		{
			_timerMS = _v[ Duration ] * 1000f + _recoveryTimeMS;
		}

		_scale = 1f;

		if ( _timerMS > 0f )
		{
			// full reduction for the hold duration, then the remaining recovery window ramps linearly back to
			// unity; a zero recovery time never enters the ramp branch (no division by zero)
			_scale = 1f - _v[ ForceReduction ] * ( ( _timerMS <= _recoveryTimeMS ) ? ( _timerMS / _recoveryTimeMS ) : 1f );

			_timerMS -= ctx.DeltaMilliseconds;
		}

		// OR — the engine clears the flag at the top of every tick, and any active crash protection node
		// should light it (another node's PrePass must not un-light it)
		Owner.CrashProtectionActive |= _timerMS > 0f;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return inputA * _scale;
	}
}

/// <summary>
/// Curb protection (old 1294–1318, rebuilt as a direct force reduction). PrePass detects the trigger ITSELF —
/// comparing the frame's peak shock velocity in the context against this module's own threshold, so multiple
/// curb protection nodes in one graph trigger independently — and advances the timer (re-armed every tick the
/// telemetry stays over the threshold, so continuous curb strikes hold it at full). Process multiplies by the
/// scale, which ramps back to unity over the user-set recovery time (0 = instant snap-back) after the hold
/// duration expires — same shape as <see cref="CrashProtectionModule"/>. (The old algorithm instead pulled
/// back each DSP stage's detail parameters; those couplings were dropped in the graph redesign, so this
/// module now owns the reduction itself.)
/// </summary>
public sealed class CurbProtectionModule : FFBModule
{
	private const int ShockVelocity = 1;
	private const int Duration = 2;
	private const int ForceReduction = 3;
	private const int RecoveryTime = 4;

	private float _recoveryTimeMS;

	private float _timerMS;
	private float _scale = 1f;

	public override void Reset()
	{
		_timerMS = 0f;
		_scale = 1f;
	}

	protected override void OnValuesChanged()
	{
		_recoveryTimeMS = _v[ RecoveryTime ] * 1000f;
	}

	public override bool HasPrePass => true;

	public override void PrePass( in FFBTickContext ctx )
	{
		if ( !Enabled )
		{
			_scale = 1f;

			return;
		}

		// Self-triggered from the raw telemetry against this module's OWN threshold. A zero threshold is "off",
		// and a would-do-nothing configuration (zero duration or zero force reduction) never triggers — the
		// same guards the old Simulator-side detection applied to the published aggregate. The context
		// telemetry is zeroed off track / inside the post-reset block window, so those gates ride along for free.
		var triggered = ( _v[ ShockVelocity ] > 0f ) && ( _v[ Duration ] > 0f ) && ( _v[ ForceReduction ] > 0f )
			&& ( ctx.MaxShockVelocity >= _v[ ShockVelocity ] );

		if ( triggered || TestActive )
		{
			_timerMS = _v[ Duration ] * 1000f + _recoveryTimeMS;
		}

		_scale = 1f;

		if ( _timerMS > 0f )
		{
			// full reduction for the hold duration, then the remaining recovery window ramps linearly back to
			// unity; a zero recovery time never enters the ramp branch (no division by zero)
			_scale = 1f - _v[ ForceReduction ] * ( ( _timerMS <= _recoveryTimeMS ) ? ( _timerMS / _recoveryTimeMS ) : 1f );

			_timerMS -= ctx.DeltaMilliseconds;
		}

		// OR — the engine clears the flag at the top of every tick, and any active curb protection node
		// should light it (another node's PrePass must not un-light it)
		Owner.CurbProtectionActive |= _timerMS > 0f;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return inputA * _scale;
	}
}

// Constant-force steering effects. Direction is a Choice: None / DecreaseForce / IncreaseForce (index).
// Strength is stored in physical Nm at the wheel; dividing by ctx.WheelForce recovers the normalized
// strength fraction. IncreaseForce injects that fraction times MaxForce into the Nm main chain (so the
// physical push stays the stored Nm regardless of the max force setting); DecreaseForce uses it as the
// lerp-toward-zero fraction (saturated — a strength above the wheel force can't over-reduce).

/// <summary>Understeer constant force (old 1330–1348). Module Enabled replaces the old wheel-side enable.</summary>
public sealed class UndersteerForceModule : FFBModule
{
	private const int Direction = 1;
	private const int Strength = 2;
	private const int Curve = 3;

	private float _curvePower;

	private EffectInterpolator _effectInterpolator;

	public override void Reset() => _effectInterpolator.Reset();

	protected override void OnValuesChanged()
	{
		_curvePower = MathZ.CurveToPower( _v[ Curve ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		// the 60 Hz-held effect value is interpolated across the frame's sub-ticks so the force doesn't stairstep
		var understeerEffect = _effectInterpolator.Interpolate( ctx.UndersteerEffect, ctx.SampleIndex );

		if ( understeerEffect > 0f )
		{
			var constantForceTorque = ( _v[ Strength ] / ctx.WheelForce ) * MathF.Pow( understeerEffect, _curvePower );

			switch ( (RacingWheel.ConstantForceDirection) (int) _v[ Direction ] )
			{
				case RacingWheel.ConstantForceDirection.DecreaseForce:
					return MathZ.Lerp( inputA, 0f, MathZ.Saturate( constantForceTorque ) );

				case RacingWheel.ConstantForceDirection.IncreaseForce:
					return inputA + MathF.CopySign( constantForceTorque, ctx.VelocityY ) * ctx.MaxForce;
			}
		}

		return inputA;
	}
}

/// <summary>Oversteer constant force (old 1352–1370).</summary>
public sealed class OversteerForceModule : FFBModule
{
	private const int Direction = 1;
	private const int Strength = 2;
	private const int Curve = 3;

	private float _curvePower;

	private EffectInterpolator _effectInterpolator;

	public override void Reset() => _effectInterpolator.Reset();

	protected override void OnValuesChanged()
	{
		_curvePower = MathZ.CurveToPower( _v[ Curve ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		// the 60 Hz-held effect value is interpolated across the frame's sub-ticks so the force doesn't stairstep
		var oversteerEffect = _effectInterpolator.Interpolate( ctx.OversteerEffect, ctx.SampleIndex );

		if ( oversteerEffect > 0f )
		{
			var constantForceTorque = ( _v[ Strength ] / ctx.WheelForce ) * MathF.Pow( oversteerEffect, _curvePower );

			switch ( (RacingWheel.ConstantForceDirection) (int) _v[ Direction ] )
			{
				case RacingWheel.ConstantForceDirection.DecreaseForce:
					return MathZ.Lerp( inputA, 0f, MathZ.Saturate( constantForceTorque ) );

				case RacingWheel.ConstantForceDirection.IncreaseForce:
					return inputA + MathF.CopySign( constantForceTorque, ctx.VelocityY ) * ctx.MaxForce;
			}
		}

		return inputA;
	}
}

/// <summary>Seat-of-pants constant force (old 1374–1394), signed effect via CopySign.</summary>
public sealed class SeatOfPantsForceModule : FFBModule
{
	private const int Direction = 1;
	private const int Strength = 2;
	private const int Curve = 3;

	private float _curvePower;

	private EffectInterpolator _effectInterpolator;

	public override void Reset() => _effectInterpolator.Reset();

	protected override void OnValuesChanged()
	{
		_curvePower = MathZ.CurveToPower( _v[ Curve ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		// the 60 Hz-held signed effect value is interpolated across the frame's sub-ticks so the force doesn't
		// stairstep; interpolating before CopySign lets sign flips sweep through zero
		var seatOfPantsEffect = _effectInterpolator.Interpolate( ctx.SeatOfPantsEffect, ctx.SampleIndex );

		if ( seatOfPantsEffect != 0f )
		{
			var constantForceTorque = ( _v[ Strength ] / ctx.WheelForce ) * MathF.CopySign( MathF.Pow( MathF.Abs( seatOfPantsEffect ), _curvePower ), seatOfPantsEffect );

			switch ( (RacingWheel.ConstantForceDirection) (int) _v[ Direction ] )
			{
				case RacingWheel.ConstantForceDirection.DecreaseForce:
					return MathZ.Lerp( inputA, 0f, MathZ.Saturate( MathF.Abs( constantForceTorque ) ) );

				case RacingWheel.ConstantForceDirection.IncreaseForce:
					return inputA - constantForceTorque * ctx.MaxForce;
			}
		}

		return inputA;
	}
}

/// <summary>
/// Speed-ramped gain: scales the signal from <c>GainAtMin</c> at/below <c>MinSpeed</c> to <c>GainAtMax</c>
/// at/above <c>MaxSpeed</c> (both in m/s). Lightens parking or stiffens at speed. Defaults are unity (no effect).
/// The preview replay uses the recording's real velocity (older two-column recordings replay as zero); the
/// test toggle pins the preview to min speed so the parked end of the ramp can be inspected regardless of
/// what was recorded.
/// </summary>
public sealed class SpeedGainModule : FFBModule
{
	private const int MinSpeed = 1;
	private const int MaxSpeed = 2;
	private const int GainAtMin = 3;
	private const int GainAtMax = 4;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var velocityMS = ( ctx.IsPreview && TestActive ) ? _v[ MinSpeed ] : ctx.VelocityMS;

		var t = MathZ.InverseLerp( _v[ MinSpeed ], _v[ MaxSpeed ], velocityMS );

		return inputA * MathZ.Lerp( _v[ GainAtMin ], _v[ GainAtMax ], t );
	}
}

/// <summary>
/// Tiny high-frequency dither added while the signal magnitude is below <c>Threshold</c> — alternates sign each
/// tick to break static friction on gear-driven wheels near center. Above the threshold it passes through.
/// (This is also the modern replacement for the old output minimum: instead of forcing small signals up to a
/// hard floor, the dither keeps the wheel's mechanism live below the floor level.)
/// </summary>
public sealed class TorqueDitherModule : FFBModule
{
	private const int Strength = 1;
	private const int Threshold = 2;

	private float _sign = 1f;

	public override void Reset() => _sign = 1f;

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( MathF.Abs( inputA / ctx.MaxForce ) < _v[ Threshold ] )
		{
			_sign = -_sign;

			// Strength is physical Nm at the wheel; via WheelForce it becomes the normalized dither amplitude,
			// then MaxForce lifts it into the Nm main chain so it survives the output normalization intact
			return inputA + _sign * ( _v[ Strength ] / ctx.WheelForce ) * ctx.MaxForce;
		}

		return inputA;
	}
}
