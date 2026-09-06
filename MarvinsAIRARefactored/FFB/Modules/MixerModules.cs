
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.FFB.Modules;

// Mixer modules — the two-input combiners of the main chain. Everything that merges two signals lives here:
// the trivial Add/Subtract/Blend and the adaptive detail/anchor blend that earns its keep with a dynamic
// corner. All operate in the Nm main-bus domain.

/// <summary>Two-input sum: <c>A + B</c>. Scale an input with a Gain module before it if levels need adjusting.</summary>
public sealed class AddModule : FFBModule
{
	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return inputA + inputB;
	}
}

/// <summary>Two-input difference: <c>A − B</c>. Removes input B's content from input A — e.g. subtracting a
/// low-pass copy of a signal from the signal itself leaves just the detail.</summary>
public sealed class SubtractModule : FFBModule
{
	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return inputA - inputB;
	}
}

/// <summary>Two-input selector: passes input A or input B through untouched, chosen by the UseInputB switch.
/// Built for instant A/B comparison of two signal paths — pin the switch to flip from the quick controls.</summary>
public sealed class ABSwitchModule : FFBModule
{
	private const int UseInputB = 1;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return _v[ UseInputB ] >= 0.5f ? inputB : inputA;
	}
}

/// <summary>Two-input crossfade: <c>Lerp(A, B, Mix)</c> — 0% = all A, 100% = all B.</summary>
public sealed class BlendModule : FFBModule
{
	private const int Mix = 1;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return MathZ.Lerp( inputA, inputB, _v[ Mix ] );
	}
}

/// <summary>
/// Adaptive blend of two sources (old Multi HybridVariable30 stage 492–505): input A = the detail source
/// (360 Hz), input B = the anchor (60 Hz). Runs the DetailBooster recursion — the output advances by the
/// A-deltas while being pulled toward the anchor by a one-pole whose corner is Cutoff (Hz, same mapping as
/// the filters) — but the corner is DYNAMIC: whenever the output's direction flips (a peak), it drops to
/// PeakCutoff and relaxes back linearly over Hold (ms), letting transients ride through with less anchor pull
/// right when they matter. With a fixed corner this would be replicable as LowPassFilter(B) +
/// HighPassFilter(A) → Add; the direction-triggered corner drop is what earns it a module. Input A enters
/// only via its delta, so scaling the detail is a Gain module on the A input (the old built-in Detail knob —
/// removed — was exactly that; it only had to live inside while curb protection dynamically pulled it to 0,
/// a coupling that was dropped along with the rest).
/// </summary>
public sealed class AdaptiveBlendModule : FFBModule
{
	private const int Cutoff = 1;
	private const int PeakCutoff = 2;
	private const int Hold = 3;

	private const float TickRateHz = 360f;

	private float _alpha;
	private float _peakAlpha;
	private float _holdTicks;

	private float _hybridLast;
	private float _lastVelocitySign;
	private float _lastA;
	private float _peakCountdown;

	public override void Reset()
	{
		_hybridLast = 0f;
		_lastVelocitySign = 0f;
		_lastA = 0f;
		_peakCountdown = 0f;
	}

	protected override void OnValuesChanged()
	{
		_alpha = 1f - MathF.Exp( -MathF.Tau * _v[ Cutoff ] / TickRateHz );
		_peakAlpha = 1f - MathF.Exp( -MathF.Tau * _v[ PeakCutoff ] / TickRateHz );
		_holdTicks = _v[ Hold ] * ( TickRateHz / 1000f );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var holdTicks = _holdTicks;

		var peakCountdown = MathF.Max( 0f, _peakCountdown - 1f );

		var hfScaledDelta = inputA - _lastA;

		// the effective corner eases linearly from the peak corner back to the normal one as the hold expires
		// (guarded — the old code divided by HoldTicks unguarded and produced NaN at 0)
		var alphaNow = holdTicks > 0f ? _alpha - ( _alpha - _peakAlpha ) * peakCountdown / holdTicks : _alpha;

		var preliminaryHybridTorque = MathZ.Lerp( _hybridLast + hfScaledDelta, inputB, alphaNow );

		float hybridTorque;

		if ( MathF.Sign( preliminaryHybridTorque - _hybridLast ) != _lastVelocitySign )
		{
			peakCountdown = holdTicks;

			// with the countdown freshly re-armed the eased corner is exactly the peak corner
			hybridTorque = MathZ.Lerp( _hybridLast + hfScaledDelta, inputB, _peakAlpha );
		}
		else
		{
			hybridTorque = preliminaryHybridTorque;
		}

		_lastVelocitySign = MathF.Sign( hybridTorque - _hybridLast );
		_hybridLast = hybridTorque;
		_lastA = inputA;
		_peakCountdown = peakCountdown;

		return hybridTorque;
	}
}
