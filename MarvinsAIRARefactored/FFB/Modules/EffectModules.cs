
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

	// IncreaseForce's push direction ramps in over this much steering angle (~2°) so it can't chatter at center
	private const float DirectionRampRadians = 0.035f;

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
				{
					// push against the steering: the angle is counterclockwise-positive, so the resisting force takes
					// the opposite sign. (This used to follow the sign of VelocityY, which points into the turn in slow
					// corners where the rear tracks inside the front, so the push went light instead of heavy.)
					var steeringDirection = Math.Clamp( ctx.SteeringWheelAngle / DirectionRampRadians, -1f, 1f );

					return inputA - constantForceTorque * steeringDirection * ctx.MaxForce;
				}
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
/// Dynamic damping: adds viscous damping (torque opposing wheel speed, proportional to it) to input A, with a
/// strength that depends on what the wheel is doing. Input A is both the signal the damping is added to and the
/// torque used to classify the situation; the output is the complete signal (input A + damping), so it drops
/// straight into the main chain. Disabled or off track, input A passes through untouched.
/// <para>Velocity comes from an anchor that chases the wheel angle (the friction source's stick point, but
/// dragged exponentially instead of at a fixed distance): the anchor trails the wheel by an amount proportional
/// to wheel speed, so the lag is a smoothed velocity without differentiating the 60 Hz angle. The anchor is
/// stepped once per 60 Hz frame (the angle only changes per frame — stepping it at 360 Hz would ripple the
/// velocity at 60 Hz), with a scale that makes the steady-state estimate exact.</para>
/// <para>Four weights cover the four situations, by whether the torque pushes WITH the wheel's motion (the FFB
/// is driving the wheel) or AGAINST it, and whether the wheel is speeding up or slowing down:
/// accelerating with torque (snap-back / hands off — strongest), decelerating with torque (the driver catching
/// it), accelerating against torque (the driver turning in), decelerating against torque (the FFB already
/// braking the wheel). The with/against split is blended across a small torque band and the
/// accelerating/decelerating split across an acceleration threshold, so no situation change steps the output;
/// the combined weight then follows an attack/release envelope.</para>
/// <para>Strength and MaxDampingTorque are both fractions of the wheel force, so the damping keeps the same
/// proportion to the rest of the FFB on any wheelbase (and a shared graph feels the same on all of them).
/// Strength is the damping torque at one wheel revolution per second — 100% = the full wheel force at 1 rev/s.
/// MaxDampingTorque caps it — 50% = never more than half the wheel force. The telemetry is ~2 frames stale, so
/// very high strengths can add energy above a few Hz instead of removing it — tune on a hands-off oscillation.</para>
/// <para>The situation weights and the smoothing/envelope times are fixed tuning constants (see the "tuning
/// constants" block at the top of the class) — edit them there.</para>
/// </summary>
public sealed class DynamicDampingModule : FFBModule
{
	private const int Strength = 1;                // fraction of the wheel force per rev/s (1 = 100%)
	private const int AccelerationThreshold = 2;   // deg/s² — speeding up by this much counts as fully accelerating
	private const int MaxDampingTorque = 3;        // fraction of the wheel force (1 = 100%)

	// ---- tuning constants ----------------------------------------------------------------------------------

	// situation weights (0 = no damping, 1 = full Strength)
	private const float AcceleratingWithTorqueWeight = 1f;      // FFB flinging the wheel (snap-back, hands off)
	private const float DeceleratingWithTorqueWeight = 0.6f;    // driver catching a wheel the FFB is driving
	private const float AcceleratingAgainstTorqueWeight = 0f;   // driver turning against the aligning torque
	private const float DeceleratingAgainstTorqueWeight = 0f;   // FFB already braking the wheel

	private const float VelocitySmoothingMilliseconds = 15f;       // anchor time constant (must be > 0)
	private const float OutputSmoothingMilliseconds = 5f;          // smooths the 60 Hz steps out of the damping torque
	private const float TorqueSmoothingMilliseconds = 20f;         // smoothing on input A before classification
	private const float TorqueBandNm = 0.75f;                      // with/against blend half-width (iRacing Nm, input A units)
	private const float AccelerationSmoothingMilliseconds = 30f;
	private const float AttackMilliseconds = 10f;                  // weight rising
	private const float ReleaseMilliseconds = 150f;                // weight falling

	// --------------------------------------------------------------------------------------------------------

	private const float TickSeconds = FFBTickContext.TickDeltaMilliseconds / 1000f;
	private const float FrameSeconds = TickSeconds * FFBTickContext.SamplesPerFrame;

	// derivations of the tuning constants: the anchor keeps AnchorRetain of its lag each frame, and the velocity
	// scale turns the post-step lag back into exact wheel speed for a steady turn (lag = Δθ·r/(1−r), so
	// velocity = lag·(1−r)/(r·dt))
	private static readonly float AnchorRetain = MathF.Exp( -FrameSeconds / ( VelocitySmoothingMilliseconds / 1000f ) );
	private static readonly float AnchorFollow = 1f - AnchorRetain;
	private static readonly float VelocityScale = ( 1f - AnchorRetain ) / ( AnchorRetain * FrameSeconds );
	private static readonly float AccelerationAlpha = OnePoleAlpha( AccelerationSmoothingMilliseconds, FrameSeconds );
	private static readonly float TorqueAlpha = OnePoleAlpha( TorqueSmoothingMilliseconds, TickSeconds );
	private static readonly float OutputAlpha = OnePoleAlpha( OutputSmoothingMilliseconds, TickSeconds );
	private static readonly float AttackAlpha = OnePoleAlpha( AttackMilliseconds, TickSeconds );
	private static readonly float ReleaseAlpha = OnePoleAlpha( ReleaseMilliseconds, TickSeconds );

	// cached derivation of the knob values (see OnValuesChanged)
	private float _accelerationThresholdRad = 0f;

	private bool _stateValid = false;
	private float _anchor = 0f;
	private float _velocity = 0f;
	private float _acceleration = 0f;
	private float _torque = 0f;
	private float _weight = 0f;
	private float _dampingOutput = 0f;

	public override void Reset()
	{
		_stateValid = false;
	}

	protected override void OnValuesChanged()
	{
		_accelerationThresholdRad = _v[ AccelerationThreshold ] * ( MathF.PI / 180f );
	}

	private static float OnePoleAlpha( float timeConstantMilliseconds, float stepSeconds )
	{
		return ( timeConstantMilliseconds <= 0f ) ? 1f : 1f - MathF.Exp( -stepSeconds / ( timeConstantMilliseconds / 1000f ) );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.IsOnTrack )
		{
			_stateValid = false;

			return inputA;
		}

		if ( !_stateValid )
		{
			_anchor = ctx.SteeringWheelAngle;
			_velocity = 0f;
			_acceleration = 0f;
			_torque = inputA;
			_weight = 0f;
			_dampingOutput = 0f;
			_stateValid = true;
		}
		else if ( ctx.SampleIndex == 0 )
		{
			// new telemetry frame: step the anchor toward the new angle, read the speed off its lag, and take the
			// frame-to-frame change of that (already smoothed) speed as the acceleration
			_anchor += ( ctx.SteeringWheelAngle - _anchor ) * AnchorFollow;

			var previousVelocity = _velocity;

			_velocity = ( ctx.SteeringWheelAngle - _anchor ) * VelocityScale;

			var frameAcceleration = ( _velocity - previousVelocity ) / FrameSeconds;

			_acceleration += ( frameAcceleration - _acceleration ) * AccelerationAlpha;
		}

		_torque += ( inputA - _torque ) * TorqueAlpha;

		var velocitySign = MathF.Sign( _velocity );

		// positive while the wheel is speeding up, negative while it is slowing down
		var speedingUp = _acceleration * velocitySign;

		var acceleratingFactor = ( _accelerationThresholdRad > 0f ) ? Math.Clamp( speedingUp / _accelerationThresholdRad, 0f, 1f ) : ( ( speedingUp > 0f ) ? 1f : 0f );

		// positive while the torque pushes the wheel the way it is already moving (the FFB is driving the wheel);
		// angle, velocity, and torque all share the counterclockwise-positive convention
		var alignment = _torque * velocitySign;

		var withTorqueFactor = ( TorqueBandNm > 0f ) ? Math.Clamp( 0.5f + alignment / ( 2f * TorqueBandNm ), 0f, 1f ) : ( ( alignment > 0f ) ? 1f : 0f );

		var withTorqueWeight = MathZ.Lerp( DeceleratingWithTorqueWeight, AcceleratingWithTorqueWeight, acceleratingFactor );
		var againstTorqueWeight = MathZ.Lerp( DeceleratingAgainstTorqueWeight, AcceleratingAgainstTorqueWeight, acceleratingFactor );

		var targetWeight = MathZ.Lerp( againstTorqueWeight, withTorqueWeight, withTorqueFactor );

		_weight += ( targetWeight - _weight ) * ( ( targetWeight > _weight ) ? AttackAlpha : ReleaseAlpha );

		// strength and cap are both fractions of the wheel force (the knob range keeps the cap within 0..100%)
		var maxDampingFraction = _v[ MaxDampingTorque ];

		var dampingFraction = Math.Clamp( _velocity / MathF.Tau * _v[ Strength ] * _weight, -maxDampingFraction, maxDampingFraction );

		// negated: damping must OPPOSE the counterclockwise-positive velocity. MaxForce lifts the wheel force
		// fraction into the Nm main chain (same as the friction source)
		var targetDampingOutput = -dampingFraction * ctx.MaxForce;

		_dampingOutput += ( targetDampingOutput - _dampingOutput ) * OutputAlpha;

		return inputA + _dampingOutput;
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
