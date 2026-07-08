
namespace MarvinsAIRARefactored.FFB.Modules;

/// <summary>
/// Fixed final module and the one place the Nm main bus is converted to the normalized (−1..1) signal the wheel
/// driver expects: <c>inputA / MaxForce</c>. It has no user settings — the output curve, soft limiter, maximum,
/// and minimum that used to live here are now standalone modules (Curve / SoftLimiter / Maximum / Minimum) placed
/// ahead of it, so any of them can be applied at any stage of the stack. Placing them in that order immediately
/// before this module reproduces the old ProcessAlgorithm output tail exactly.
/// </summary>
public sealed class OutputModule : FFBModule
{
	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return inputA / ctx.MaxForce;
	}
}
