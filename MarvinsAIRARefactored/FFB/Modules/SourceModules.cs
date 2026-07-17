
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
/// Friction source: Karnopp-style dry friction — a torque of constant magnitude opposing the wheel's rotation,
/// −clamp(ω/ω₀, ±1) × Strength × MaxForce (Nm), gated on being on track. Below the stick-region velocity ω₀ it
/// behaves like a stiff damper that holds the wheel planted (this is what keeps a plain sign(ω) from buzzing at
/// rest); beyond it the torque saturates at the constant Strength level. Contrast with
/// <see cref="SourceWheelVelocityModule"/>, whose output stays proportional to velocity (a damper). The 60 Hz
/// staircase in the telemetry velocity is smoothed by a built-in one-pole so it doesn't chatter through the
/// saturation. Sum it into the main signal with an Add; route it through a SpeedGain to fade it with car speed
/// (parked steering weight = full at rest, fading out by ~10 m/s).
/// </summary>
public sealed class SourceFrictionModule : FFBModule
{
	private const int Strength = 1;
	private const int StickRegion = 2;

	// one-pole low-pass on the 60 Hz-carried telemetry velocity: 1 − e^(−2π·20Hz/360Hz)
	private const float SmoothingAlpha = 0.2947f;

	private float _invStickRegion = 1f;
	private float _smoothedVelocity = 0f;

	public override void Reset()
	{
		_smoothedVelocity = 0f;
	}

	protected override void OnValuesChanged()
	{
		// the knob is in °/s of wheel rotation; the telemetry velocity is rad/s
		_invStickRegion = 1f / ( MathF.Max( _v[ StickRegion ], 1f ) * ( MathF.PI / 180f ) );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.IsOnTrack )
		{
			_smoothedVelocity = 0f;

			return 0f;
		}

		_smoothedVelocity += SmoothingAlpha * ( ctx.SteeringWheelVelocity - _smoothedVelocity );

		// negated like SourceWheelVelocityModule: iRacing's angle is positive counterclockwise, and the output
		// convention needs the opposite sign to RESIST wheel motion
		return -Math.Clamp( _smoothedVelocity * _invStickRegion, -1f, 1f ) * _v[ Strength ] * ctx.MaxForce;
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

				if ( MathF.Sign( ctx.WheelVelocity ) != sign )
				{
					contribution += ctx.WheelVelocity * strength;
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
			return Math.Clamp( ( Math.Clamp( ctx.WheelPosition, -0.25f, 0.25f ) + ctx.WheelVelocity * 0.1f ) * _v[ Strength ], -1f, 1f ) * ctx.MaxForce;
		}

		return 0f;
	}
}
