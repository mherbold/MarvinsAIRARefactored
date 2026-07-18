
namespace MarvinsAIRARefactored.FFB.Modules;

/// <summary>
/// 60 Hz source. Emits the 60 Hz torque sample (Nm). Prediction is no longer part of this source — it is a
/// separate Prediction module placed in the graph (see <see cref="PredictionModule"/>), so this just surfaces
/// the raw sample.
/// </summary>
public sealed class Source60HzModule : FFBModule
{
	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return ctx.Torque60Hz;
	}
}

/// <summary>360 Hz source. Emits the raw 360 Hz torque sample (Nm).</summary>
public sealed class Source360HzModule : FFBModule
{
	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return ctx.Torque360Hz;
	}
}

/// <summary>
/// LFE source. Emits the low-frequency-effects audio magnitude scaled to full wheel force (Nm) and the module's
/// Strength, gated on being on track (old 1413–1416). Sum it into the main signal with a Mixer; Strength sets its
/// level (a downstream Gain, if any, still composes). Strength defaults to 25%.
/// </summary>
public sealed class SourceLFEModule : FFBModule
{
	private const int Strength = 1;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return ctx.IsOnTrack ? ctx.LFEMagnitude * ctx.MaxForce * _v[ Strength ] : 0f;
	}
}

/// <summary>
/// Wheel velocity source: how fast the steering wheel is being turned, from the sim's 60 Hz SteeringWheelAngle
/// telemetry (radians — so it is independent of the wheel's rotation-range setting), scaled so one full wheel
/// revolution per second equals full wheel force (Nm) times the module's Strength, and gated on being on track.
/// The sign opposes the turn direction, so summing it into the main signal produces friction/damping: Strength
/// sets the damping level (a downstream Gain still composes), and a low-pass ahead of the Add smooths the 60 Hz
/// staircase. Replaces the old fixed-function Friction module. Strength defaults to 100% (pass-through).
/// </summary>
public sealed class SourceWheelVelocityModule : FFBModule
{
	private const int Strength = 1;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		// negated: iRacing's angle is positive counterclockwise, while the output convention needs the
		// opposite sign to RESIST wheel motion (matching the old DirectInput-axis-derived friction)
		return ctx.IsOnTrack ? -ctx.SteeringWheelVelocity / MathF.Tau * ctx.MaxForce * _v[ Strength ] : 0f;
	}
}

/// <summary>
/// Friction source: dry friction via a dragged stick point — the same model wheel firmwares use for their
/// hardware friction effect. The module latches the wheel angle it is "stuck" at and pulls back toward it
/// like a spring, saturating at Strength × MaxForce (Nm); when the wheel is turned more than StickRegion
/// degrees from the stick point, the point is dragged along at that distance. Turning steadily therefore
/// feels like constant sliding friction, and at rest the wheel is held planted at the stick point. Because
/// the model is driven by the wheel's POSITION (no differentiation, no velocity noise) and its torque is
/// bounded by the breakaway level, it does not need a velocity signal the way the old velocity-based
/// friction did. A larger StickRegion feels softer/springier before breakaway; a smaller one feels crisper.
/// Sum it into the main signal with an Add; route it through a SpeedGain to fade it with car speed (parked
/// steering weight = full at rest, fading out by ~10 m/s).
/// <para>The spring acts on a LEAD-COMPENSATED angle: the telemetry angle is ~2 frames stale, and a stiff
/// spring on a stale angle behaves as negative damping — it pumps the wheel into a permanent oscillation
/// whose amplitude tracks the stick region (the drag boundary is the only place energy dissipates, so the
/// limit cycle parks right at it). Extrapolating the angle forward by the loop delay with the telemetry
/// velocity cancels that phase lag, which is what the wheel firmware gets for free by running the same model
/// at the servo rate with no delay. The centering source never needed this because its spring is 10-25×
/// softer (full force over a quarter of the steering range), putting its resonance where the delay phase is
/// negligible.</para>
/// </summary>
public sealed class SourceFrictionModule : FFBModule
{
	private const int Strength = 1;
	private const int StickRegion = 2;

	// how far ahead the spring's angle is extrapolated — sized to the telemetry-to-wheel loop delay (~30 ms)
	// plus a little extra, so the slight overcompensation contributes positive damping instead of none
	private const float LeadSeconds = 0.05f;

	private float _stickRegionRad = 1f;
	private float _stickPoint = 0f;
	private bool _stickPointValid = false;

	public override void Reset()
	{
		_stickPointValid = false;
	}

	protected override void OnValuesChanged()
	{
		// the knob is in degrees of wheel rotation; the telemetry angle is radians
		_stickRegionRad = MathF.Max( _v[ StickRegion ], 0.5f ) * ( MathF.PI / 180f );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.IsOnTrack )
		{
			_stickPointValid = false;

			return 0f;
		}

		if ( !_stickPointValid )
		{
			_stickPoint = ctx.SteeringWheelAngle;
			_stickPointValid = true;
		}

		// the stick point is dragged along using the MEASURED angle, so it stays anchored to real positions;
		// past the breakaway distance it trails the wheel, pinning the torque at the sliding-friction level
		var displacement = ctx.SteeringWheelAngle - _stickPoint;

		if ( displacement > _stickRegionRad )
		{
			_stickPoint = ctx.SteeringWheelAngle - _stickRegionRad;
		}
		else if ( displacement < -_stickRegionRad )
		{
			_stickPoint = ctx.SteeringWheelAngle + _stickRegionRad;
		}

		// the spring acts on the lead-compensated angle (see the class comment — this is what keeps the stick
		// phase from pumping a permanent oscillation)
		var leadDisplacement = ctx.SteeringWheelAngle + ctx.SteeringWheelVelocity * LeadSeconds - _stickPoint;

		// negated: the angle is positive counterclockwise (like the output convention), and the spring must
		// pull the wheel BACK toward the stick point
		return -Math.Clamp( leadDisplacement / _stickRegionRad, -1f, 1f ) * _v[ Strength ] * ctx.MaxForce;
	}
}

/// <summary>
/// Soft lock source (old 1420–1435): emits an opposing force once the steering wheel is turned past the car's
/// maximum steering angle, scaled by strength and MaxForce (Nm). Sum it into the main signal with an Add.
/// </summary>
public sealed class SourceSoftLockModule : FFBModule
{
	private const int Strength = 1;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var strength = _v[ Strength ];

		if ( strength > 0f )
		{
			var deltaToMax = ( ctx.SteeringWheelAngleMax * 0.5f ) - MathF.Abs( ctx.SteeringWheelAngle );

			if ( deltaToMax < 0f )
			{
				var sign = MathF.Sign( ctx.SteeringWheelAngle );

				var contribution = sign * deltaToMax * 2f * strength;

				// -WheelVelocity: the old code damped with the DirectInput axis velocity, whose sign convention is
				// the opposite of the sim-telemetry-derived WheelVelocity (iRacing counterclockwise-positive)
				if ( MathF.Sign( -ctx.WheelVelocity ) != sign )
				{
					contribution += -ctx.WheelVelocity * strength;
				}

				return contribution * ctx.MaxForce;
			}
		}

		return 0f;
	}
}

/// <summary>
/// Wheel centering source (old 1453–1461): emits a centering force from the wheel's position and velocity,
/// scaled by strength and MaxForce (Nm), gated on being on track. Sum it into the main signal with an Add;
/// route it through a SpeedGain over the 0–5 mph ramp to gate it by speed (parked-only centering = GainAtMin 1
/// / GainAtMax 0, racing-only = the reverse — this replaced the old while-racing / while-parked toggles).
/// </summary>
public sealed class SourceWheelCenteringModule : FFBModule
{
	private const int Strength = 1;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( ctx.IsOnTrack )
		{
			// negated: WheelPosition/Velocity follow iRacing's counterclockwise-positive convention (the old code
			// used the DirectInput axis, which is the opposite), while the centering torque must oppose the position
			return -Math.Clamp( ( Math.Clamp( ctx.WheelPosition, -0.25f, 0.25f ) + ctx.WheelVelocity * 0.1f ) * _v[ Strength ], -1f, 1f ) * ctx.MaxForce;
		}

		return 0f;
	}
}
