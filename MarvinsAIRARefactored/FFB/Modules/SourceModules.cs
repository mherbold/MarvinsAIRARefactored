
namespace MarvinsAIRARefactored.FFB.Modules;

/// <summary>
/// 60 Hz source. Emits the predicted 60 Hz torque sample (Nm). The RLS predictors physically stay in
/// <see cref="Components.RacingWheel"/> (they run per sim frame); this module just surfaces the result and
/// hosts the Prediction settings so the engine can cache mode/blend for RacingWheel to read.
/// </summary>
public sealed class Source60HzModule : FFBModule
{
	// effective-setting indices (0 = Enabled)
	private const int PredictionMode = 1;   // Choice: Disabled / PredictK1 / PredictK2 (stored as index)
	private const int PredictionBlend = 2;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return ctx.Torque60Hz;
	}

	public override void PublishAggregates()
	{
		Owner.PredictionMode = (Components.RacingWheel.PredictionMode) (int) _v[ PredictionMode ];
		Owner.PredictionBlend = _v[ PredictionBlend ];
	}
}

/// <summary>360 Hz source. Emits the Hermite-interpolated "500 Hz" torque sample (Nm).</summary>
public sealed class Source360HzModule : FFBModule
{
	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return ctx.Torque360Hz;
	}
}

/// <summary>
/// LFE source. Emits the low-frequency-effects audio magnitude scaled to full wheel force (Nm), gated on
/// being on track (old 1413–1416). Wire it through a Gain module and blend it into the main signal with a
/// Mixer — a Gain of 0.05 summed in reproduces the old LFE mix at its default 5% strength.
/// </summary>
public sealed class SourceLFEModule : FFBModule
{
	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return ctx.IsOnTrack ? ctx.LFEMagnitude * ctx.MaxForce : 0f;
	}
}
