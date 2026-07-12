
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.FFB.Modules;

// Generic DSP primitives operating in the Nm main-bus domain. The old monolithic algorithms
// (RacingWheel.ProcessAlgorithm) can be recreated by COMPOSING these primitives in a graph.
// Decomposition identities (verified algebraically):
//   out = Lerp(out_prev + g·Δin, anchor, b)  ==  LPF_b(anchor) + g·HPF_b(in)
// where LPF_b(x): y = Lerp(y_prev, x, b), and HPF_b(x) = x − LPF_b(x). DetailBooster uses g=1+Boost,
// anchor=in; DeltaLimiter uses g=1, in=rate-limited copy; Hybrid10 uses b=0.1, anchor=60 Hz, g=Detail.
// The g factor is applied by a dedicated Gain module placed after the HighPassFilter (omitted when g=1).

/// <summary>
/// Shared low-pass core for the LowPassFilter and HighPassFilter modules: a one-pole (6 dB/oct) or two-pole
/// Butterworth (12 dB/oct, bilinear transform with prewarping) low-pass running at the 360 Hz tick rate.
/// Coefficients are cached and recomputed only when the knob values change, so the hot path is pure
/// multiply-adds. A cutoff of 0 Hz zeroes the state and outputs 0 (fully blocked) in both slopes — this also
/// keeps the two-pole form away from its degenerate double pole on the unit circle, which would extrapolate
/// stale state forever.
/// </summary>
internal sealed class LowPassCore
{
	// index into FilterSlopeChoices; also referenced by the editor's slope-switch cutoff retune
	internal const int SlopeTwoPole = 1;

	private const float TickRateHz = 360f;

	private float _lastCutoffHz = float.NaN;
	private float _lastSlope = float.NaN;

	private bool _isTwoPole;
	private float _alpha;
	private float _b0, _b1, _b2, _a1, _a2;

	private float _y;
	private float _x1, _x2, _y1, _y2;

	public void Reset()
	{
		_y = 0f;

		_x1 = 0f;
		_x2 = 0f;
		_y1 = 0f;
		_y2 = 0f;
	}

	public float Process( float x, float cutoffHz, float slope )
	{
		if ( cutoffHz <= 0f )
		{
			Reset();

			return 0f;
		}

		if ( ( cutoffHz != _lastCutoffHz ) || ( slope != _lastSlope ) )
		{
			_isTwoPole = (int) slope == SlopeTwoPole;

			if ( _isTwoPole )
			{
				// prewarped bilinear transform; cutoff kept below Nyquist where tan blows up at 90°
				var k = MathF.Tan( MathF.PI * MathF.Min( cutoffHz, 179.9f ) / TickRateHz );
				var k2 = k * k;
				var norm = 1f / ( 1f + MathF.Sqrt( 2f ) * k + k2 );

				_b0 = k2 * norm;
				_b1 = 2f * k2 * norm;
				_b2 = k2 * norm;
				_a1 = 2f * ( k2 - 1f ) * norm;
				_a2 = ( 1f - MathF.Sqrt( 2f ) * k + k2 ) * norm;
			}
			else
			{
				_alpha = 1f - MathF.Exp( -MathF.Tau * cutoffHz / TickRateHz );
			}

			_lastCutoffHz = cutoffHz;
			_lastSlope = slope;
		}

		if ( _isTwoPole )
		{
			var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;

			_x2 = _x1;
			_x1 = x;
			_y2 = _y1;
			_y1 = y;

			return y;
		}

		_y = MathZ.Lerp( _y, x, _alpha );

		return _y;
	}
}

/// <summary>
/// Low-pass filter: anything slower than Cutoff (Hz) passes, anything faster is smoothed away (0 Hz = output
/// blocked, 180 Hz = Nyquist at the 360 Hz tick rate). Slope picks the rolloff: one pole (6 dB/oct, minimum
/// phase — the least lag possible for its response) or two poles (12 dB/oct Butterworth — steeper separation,
/// letting the cutoff sit further above the felt band for less lag at equal smoothing). With equal Cutoff and
/// Slope, an LPF and HPF split a signal into complementary body + detail that sum back to the original.
/// </summary>
public sealed class LowPassFilterModule : FFBModule
{
	private const int Slope = 1;
	private const int Cutoff = 2;

	private readonly LowPassCore _core = new();

	public override void Reset() => _core.Reset();

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return _core.Process( inputA, _v[ Cutoff ], _v[ Slope ] );
	}
}

/// <summary>
/// High-pass filter: <c>x − LPF(x)</c>, the exact complement of the LowPassFilter — anything slower than
/// Cutoff (Hz) is treated as body and removed, anything faster passes as detail (0 Hz = everything passes,
/// 180 Hz = Nyquist at the 360 Hz tick rate). Slope picks the internal body reference: one pole (6 dB/oct)
/// or two poles (12 dB/oct Butterworth) — steeper means cleaner separation between body and detail. Pair
/// with a Gain module to boost the extracted detail (the old built-in gain and its curb-protection pullback
/// moved out — the CurbProtection module now reduces force directly). To reconstruct the
/// exact DetailBooster recursion, use one pole with Cutoff at the Hz equivalent of the old bias coefficient.
/// </summary>
public sealed class HighPassFilterModule : FFBModule
{
	private const int Slope = 1;
	private const int Cutoff = 2;

	private readonly LowPassCore _core = new();

	public override void Reset() => _core.Reset();

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return inputA - _core.Process( inputA, _v[ Cutoff ], _v[ Slope ] );
	}
}

/// <summary>Simple scale / invert: <c>x · Gain</c>.</summary>
public sealed class GainModule : FFBModule
{
	private const int Gain = 1;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return inputA * _v[ Gain ];
	}
}

/// <summary>
/// True slew limiter: <c>y += clamp(x − y, ±Limit/360)</c> per tick, with Limit in honest Nm/s. Any signal
/// whose slope stays under the limit passes through bit-exact (no lag, no attenuation); over-limit movement
/// ramps at the maximum rate and always converges to the input — excess is delayed, never lost. Replaces the
/// old delta clamp (old line 374), which integrated clamped input-to-input deltas and could carry a permanent
/// DC offset — harmless inside the old DeltaLimiter algorithm where a high-pass always followed, but a footgun
/// as a free-standing module. The curb coupling (limit driven to 0, freezing the output mid-event) was dropped
/// (the CurbProtection module now reduces force directly).
/// </summary>
public sealed class SlewLimiterModule : FFBModule
{
	private const int Limit = 1;

	private const float TickRateHz = 360f;

	private float _perTickLimit;

	private float _y;

	public override void Reset() => _y = 0f;

	protected override void OnValuesChanged()
	{
		_perTickLimit = _v[ Limit ] / TickRateHz;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		_y += Math.Clamp( inputA - _y, -_perTickLimit, _perTickLimit );

		return _y;
	}
}

/// <summary>
/// Slew-domain compressor — the speed analog of the Compressor: the Compressor squeezes amplitude while
/// SlewLimiter / SlewCompressor clamp / squeeze the rate of change. Each
/// tick the delta toward the input is pushed through <see cref="MathZ.Compression"/>: changes slower than
/// Threshold (Nm/s) pass bit-exact, faster changes are squeezed at Ratio (N:1) across a Knee (Nm/s) soft
/// corner. Because it compresses the delta against its own output it always converges — excess is delayed,
/// never lost. Peak mode: while the input magnitude is falling, the output instead tracks the input
/// proportionally (the old Multi peak-hold), letting peaks release cleanly instead of slew-compressing the
/// ride-down. Replaces the old dual-mode module: Linear = old SlewAndTotalCompression 414–448 (its embedded
/// total compression is now a separate downstream Compressor in migration — the old feedback coupling is
/// gone), Soft = old Multi SlewRateReduction 529–572. Dropped: the linear mode's direction asymmetry, the
/// soft mode's 0.8 falling-rate multiplier, and the curb coupling (the CurbProtection module now reduces
/// force directly).
/// </summary>
public sealed class SlewCompressorModule : FFBModule
{
	private const int Threshold = 1;
	private const int Knee = 2;
	private const int Ratio = 3;
	private const int PeakMode = 4;

	private const float TickRateHz = 360f;

	private float _rate;
	private float _perTickThreshold;
	private float _perTickKnee;

	private float _lastInput;
	private float _y;

	public override void Reset()
	{
		_lastInput = 0f;
		_y = 0f;
	}

	protected override void OnValuesChanged()
	{
		_rate = 1f - 1f / MathF.Max( _v[ Ratio ], 1f );
		_perTickThreshold = _v[ Threshold ] / TickRateHz;
		_perTickKnee = _v[ Knee ] / TickRateHz;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( ( _v[ PeakMode ] != 0f ) && ( MathF.Abs( inputA ) < MathF.Abs( _lastInput ) ) && ( _lastInput != 0f ) )
		{
			// falling peak: ride the input down proportionally; take the blended target only while it stays
			// inside the current output (old Multi peak-hold logic, un-normalized — the math is scale-invariant)
			var targetScaled = _y * inputA / _lastInput;
			var targetBlended = 0.5f * targetScaled + 0.5f * inputA;

			_y = MathF.Abs( targetBlended ) < MathF.Abs( _y ) ? targetBlended : targetScaled;
		}
		else
		{
			_y += MathZ.Compression( inputA - _y, _rate, _perTickThreshold, _perTickKnee );
		}

		_lastInput = inputA;

		return _y;
	}
}

/// <summary>
/// Amplitude compressor via <see cref="MathZ.Compression"/> (old 442–445 / 517–527), working directly in Nm —
/// the transfer curve is scale-invariant, so no normalization is needed. Threshold is where the squeeze starts
/// (Nm), Ratio is the audio-style N:1 slope above it (1:1 = identity, converted internally to
/// <c>rate = 1 − 1/ratio</c>), and Knee is the Nm span of the sine-eased soft corner centered on the
/// threshold. Stateless.
/// </summary>
public sealed class CompressorModule : FFBModule
{
	private const int Threshold = 1;
	private const int Knee = 2;
	private const int Ratio = 3;

	private float _rate;

	public override void Reset() { }

	protected override void OnValuesChanged()
	{
		_rate = 1f - 1f / MathF.Max( _v[ Ratio ], 1f );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return MathZ.Compression( inputA, _rate, _v[ Threshold ], _v[ Knee ] );
	}
}

/// <summary>
/// Transient enhancer: the nonlinear counterpart of the HighPassFilter. Extracts the deviation from an
/// internal one-pole body reference (Cutoff, Hz — same mapping as the filters) and outputs ONLY the enhanced
/// detail: growing or direction-flipping deviations (attacks) are scaled by Gain, while shrinking deviations
/// (decays) carry the previous output down proportionally instead of being re-amplified, and the output never
/// crosses to the other side of the body reference. At Gain = 1 it degenerates to a plain one-pole high-pass.
/// The old DetailEnhancer (Multi DetailGain 574–609) also passed the body through — that part is replicable,
/// so it moved out: recompose it as LowPassFilter + this, summed by a Mixer. The old
/// curb-protection gain pullback was dropped (the CurbProtection module now reduces force directly).
/// </summary>
public sealed class TransientEnhancerModule : FFBModule
{
	private const int Cutoff = 1;
	private const int Gain = 2;

	private const float TickRateHz = 360f;
	private const float EpsilonGuard = 1e-6f;

	private float _alpha;

	private float _lastInput;
	private float _lastLpf;
	private float _lastOutput;  // detail-only (deviation from the body reference)

	public override void Reset()
	{
		_lastInput = 0f;
		_lastLpf = 0f;
		_lastOutput = 0f;
	}

	protected override void OnValuesChanged()
	{
		_alpha = 1f - MathF.Exp( -MathF.Tau * _v[ Cutoff ] / TickRateHz );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var lpf = MathZ.Lerp( _lastLpf, inputA, _alpha );

		var currentDeviation = inputA - lpf;
		var lastDeviation = _lastInput - lpf;
		var priorDeviation = _lastInput - _lastLpf;

		float outputDeviation;

		if ( MathF.Abs( currentDeviation ) > MathF.Abs( lastDeviation ) || MathF.Sign( currentDeviation ) != MathF.Sign( priorDeviation ) || MathF.Abs( lastDeviation ) < EpsilonGuard )
		{
			outputDeviation = currentDeviation * _v[ Gain ];
		}
		else
		{
			// decay: scale the previous output (re-anchored to the current body reference) by the deviation
			// ratio, so the boosted peak rides down proportionally instead of being re-amplified
			outputDeviation = currentDeviation / lastDeviation * ( _lastOutput + _lastLpf - lpf );
		}

		outputDeviation = ( currentDeviation > 0f ) ? MathF.Max( outputDeviation, 0f ) : MathF.Min( outputDeviation, 0f );

		_lastInput = inputA;
		_lastLpf = lpf;
		_lastOutput = outputDeviation;

		return outputDeviation;
	}
}

/// <summary>
/// Adaptive one-pole smoother (the "One Euro" filter, Casiez et al. 2012) — one knob, low latency. The
/// low-pass cutoff adapts to how fast the signal is moving: a near-static signal sinks the cutoff to a floor
/// set by Amount (strong smoothing of hash and jitter), while a fast transient opens the cutoff in proportion
/// to the smoothed signal speed so big movements pass with minimal lag. Amount maps the cutoff floor
/// logarithmically from 180 Hz (0 = pass-through) down to 1 Hz (1 = maximum smoothing). The speed estimate is
/// normalized by a FIXED Nm reference — deliberately not MaxForce: the bus carries the sim's steering-shaft
/// torque, which depends on the car, not the rig, and normalizing by the user's output-mapping setting would
/// change how much smoothing they get whenever they retune max force. Replaces the old two-knob delta-tracking
/// output smoother (Multi OutputSmoothing 611–624), whose second "Smoothing" knob was a hardcoded constant
/// in the old system anyway.
/// </summary>
public sealed class AdaptiveSmootherModule : FFBModule
{
	private const int Amount = 1;

	private const float TickRateHz = 360f;
	public const float FloorMaxHz = 180f;         // cutoff floor at Amount 0 (≈ pass-through); public for the value formatter
	private const float SpeedToCutoffHz = 10f;    // cutoff opening (Hz) per unit of normalized speed (1/s)
	private const float ReferenceNm = 20f;        // fixed speed-normalization scale (matches the feel of a 20 Nm max force before the decoupling)
	private const float DerivativeCutoffHz = 5f;  // fixed LPF on the speed estimate (rejects hash, tracks transients)

	private static readonly float _derivativeAlpha = CutoffToAlpha( DerivativeCutoffHz );

	private float _floorHz;

	private float _lastInput;
	private float _speedLpf;
	private float _y;

	public override void Reset()
	{
		_lastInput = 0f;
		_speedLpf = 0f;
		_y = 0f;
	}

	// the One Euro alpha form α = r/(r+1) with r = 2π·fc/rate — equivalent to the exp mapping at low
	// frequencies, cheap enough to run per tick (the cutoff changes every tick by design)
	private static float CutoffToAlpha( float cutoffHz )
	{
		var r = MathF.Tau * cutoffHz / TickRateHz;

		return r / ( r + 1f );
	}

	protected override void OnValuesChanged()
	{
		_floorHz = MathF.Pow( FloorMaxHz, 1f - _v[ Amount ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var x = inputA / ReferenceNm;

		var speed = ( x - _lastInput ) * TickRateHz;

		_speedLpf = MathZ.Lerp( _speedLpf, speed, _derivativeAlpha );

		_lastInput = x;

		if ( _v[ Amount ] <= 0f )
		{
			_y = x;

			return inputA;
		}

		var cutoffHz = MathF.Min( _floorHz + SpeedToCutoffHz * MathF.Abs( _speedLpf ), FloorMaxHz );

		_y = MathZ.Lerp( _y, x, CutoffToAlpha( cutoffHz ) );

		return _y * ReferenceNm;
	}
}
