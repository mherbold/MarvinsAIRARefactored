
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.FFB.Modules;

/// <summary>
/// Fixed final module and the one place the Nm main bus is converted to the normalized (−1..1) signal the wheel
/// driver expects: <c>inputA / MaxForce</c>. The two shapers that only make sense in that normalized space live
/// here as settings (moving them out as standalone modules coupled their behavior to the global max force
/// setting at whatever point of the graph they sat):
/// <para>• <b>Curve</b> — <c>sign(n)·|n|^power</c> with <c>power = CurveToPower(Curve)</c>; identity at 0.</para>
/// <para>• <b>Soft limiter</b> — a compressor on the normalized output (same <see cref="MathZ.Compression"/>
/// shaper family as the Compressor module, so the controls read the same): output below Threshold (a fraction
/// of full scale) passes bit-exact, excess above it is squeezed at Ratio (N:1) across a sine-eased Knee. It
/// protects the approach to ±100% instead of hard-clipping at the driver.</para>
/// The Nm-domain Maximum / Minimum modules remain standalone and belong ahead of this module.
/// </summary>
public sealed class OutputModule : FFBModule
{
	private const int Curve = 1;
	private const int SoftLimiter = 2;
	private const int Threshold = 3;
	private const int Knee = 4;
	private const int Ratio = 5;

	// settings-derived values cached on knob change — this module runs every tick unconditionally
	private float _curvePower;
	private float _softLimiterRate;

	public override void Reset() { }

	protected override void OnValuesChanged()
	{
		_curvePower = MathZ.CurveToPower( _v[ Curve ] );
		_softLimiterRate = 1f - 1f / MathF.Max( _v[ Ratio ], 1f );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var normalized = inputA / ctx.MaxForce;

		if ( _v[ Curve ] != 0f )
		{
			normalized = MathF.Sign( normalized ) * MathF.Pow( MathF.Abs( normalized ), _curvePower );
		}

		if ( _v[ SoftLimiter ] != 0f )
		{
			normalized = MathZ.Compression( normalized, _softLimiterRate, _v[ Threshold ], _v[ Knee ] );
		}

		return normalized;
	}
}
