
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.FFB.Modules;

// Output-stage shapers, split out of the old monolithic Output module so they can be placed at any point in a
// stack (like Gain). Curve and the soft limiter are unit-interval operations, so they normalize by MaxForce,
// apply, then denormalize (keeping the Nm main-bus convention). Maximum and Minimum are expressed directly in Nm,
// so they act on the bus value with no conversion. Placed in the old order (Curve -> SoftLimiter -> Maximum ->
// Minimum) ahead of a bare Output, they reproduce the old OutputModule tail exactly.

/// <summary>
/// Output curve applied in normalized space: <c>sign(n)·|n|^power</c> where <c>n = x / MaxForce</c> and
/// <c>power = CurveToPower(Curve)</c>. Identity at Curve = 0. Denormalizes back to Nm on the way out.
/// </summary>
public sealed class CurveModule : FFBModule
{
	private const int Curve = 1;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var curve = _v[ Curve ];

		if ( curve == 0f )
		{
			return inputA;
		}

		var normalized = inputA / ctx.MaxForce;

		var power = MathZ.CurveToPower( curve );

		normalized = MathF.Sign( normalized ) * MathF.Pow( MathF.Abs( normalized ), power );

		return normalized * ctx.MaxForce;
	}
}

/// <summary>
/// Smooth soft clip toward full scale, applied in normalized space: <c>SoftLimiter(x / MaxForce) · MaxForce</c>.
/// Has no knob settings — only the reserved Enabled switch — so it is a pure on/off stage (disabled ⇒ the engine
/// passes the signal through, matching the old soft-clipping toggle).
/// </summary>
public sealed class SoftLimiterModule : FFBModule
{
	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return MathZ.SoftLimiter( inputA / ctx.MaxForce ) * ctx.MaxForce;
	}
}

/// <summary>
/// Hard ceiling on the signal magnitude, expressed directly in Nm: <c>clamp(x, -Maximum, Maximum)</c>. Acts on
/// the Nm bus value with no normalization (the old percent maximum equals this with Maximum = fraction·MaxForce).
/// </summary>
public sealed class MaximumModule : FFBModule
{
	private const int Maximum = 1;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var maximum = _v[ Maximum ];

		return Math.Clamp( inputA, -maximum, maximum );
	}
}

/// <summary>
/// Floor on the signal magnitude (overcome a wheel's dead zone), expressed directly in Nm: forces <c>|x|</c> up
/// to <c>Minimum</c> while preserving sign (a zero input is pushed to +Minimum, matching the old behavior).
/// Identity at Minimum = 0. Acts on the Nm bus value with no normalization.
/// </summary>
public sealed class MinimumModule : FFBModule
{
	private const int Minimum = 1;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var minimum = _v[ Minimum ];

		if ( minimum <= 0f )
		{
			return inputA;
		}

		if ( inputA >= 0f )
		{
			return inputA < minimum ? minimum : inputA;
		}

		return inputA > -minimum ? -minimum : inputA;
	}
}
