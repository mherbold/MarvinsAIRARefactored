
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

/// <summary>
/// 60 Hz interpolator. Many upstream signals are 60 Hz values carried on the 360 Hz bus, so they hold for six
/// ticks and then jump — a staircase that downstream smoothers (e.g. the adaptive smoother) react badly to. This
/// module removes the steps: it remembers the previous 60 Hz frame's value and linearly ramps toward the current
/// frame's value across the frame's six sub-ticks. It is pure interpolation between two known samples (no
/// prediction), so it adds one 60 Hz frame (~16.7 ms) of latency. Place it on 60 Hz-derived branches; on a genuine
/// 360 Hz signal it would discard the intra-frame detail, since it only samples its input once per frame.
/// </summary>
public sealed class InterpolatorModule : FFBModule
{
	private float _previousFrameValue;
	private float _currentFrameValue;

	public override void Reset()
	{
		_previousFrameValue = 0f;
		_currentFrameValue = 0f;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		// at the start of each 60 Hz frame the input carries that frame's (frame-constant) value: shift it in as
		// the new ramp target, and the old target becomes the ramp's starting point
		if ( ctx.SampleIndex == 0 )
		{
			_previousFrameValue = _currentFrameValue;
			_currentFrameValue = inputA;
		}

		// ramp previous -> current across the frame, reaching current on the frame's final sub-tick
		var fraction = ( ctx.SampleIndex + 1f ) / FFBTickContext.SamplesPerFrame;

		return MathZ.Lerp( _previousFrameValue, _currentFrameValue, fraction );
	}
}

/// <summary>
/// Prediction. Shifts the FFB waveform left in time (reduces latency) by predicting the raw 360 Hz torque
/// Horizon sub-ticks ahead and adding the predicted change onto its input. K6 targets one iRacing 60 Hz frame
/// (~16.7 ms), K12 two.
/// <para>How it works: iRacing delivers each 60 Hz frame's six 360 Hz samples at once, so at sub-tick i the
/// frame's later samples are already known — the module anchors at the frame's NEWEST sample and only has to
/// truly extrapolate the depth left beyond it (1..6 ticks at K6 instead of a constant 6). Targets that land
/// inside the known frame (horizons below K6) are exact recorded data, not prediction. The extrapolation is a
/// bank of per-depth NLMS adaptive FIR filters (one update per depth per frame) that learn each car's torque
/// dynamics within a few seconds and track them continuously. Each filter sees 24 torque lags, 6 frame-spaced
/// taps of four auxiliary telemetry channels — steering angle, steering wheel velocity, lateral velocity, and
/// pitch acceleration (longitudinal weight-transfer rate) — and an adaptive bias that absorbs the
/// slowly-varying torque offset. The aux set is the result of greedy feature selection across six cars in the
/// PredictionLab project: each channel earned its slot by improving accuracy beyond the previous set.
/// Because a least-squares predictor is amplitude-shy (it shrinks toward the mean to minimize error),
/// Strength re-expands the learned correction — 100% applies it as learned, 200% doubles it for a
/// near-full-frame shift at the cost of more high-frequency boost. The correction is clamped to ±Correction
/// limit (Nm) and added onto input A, so place this module directly on the raw 360 Hz source and smooth
/// AFTER it if needed.</para>
/// <para>Measured across six cars/tracks on real 360 Hz recordings (vs. the ideal shifted waveform): the
/// default K6 / 150% / 5 Nm delivers ≈ 11.5 ms of true shift on average (worst car 8.6 ms) with LESS error
/// than doing nothing on every car — a grid sweep confirmed these defaults as the best all-car compromise.
/// 200% buys ≈ 14 ms at slightly higher noise; a higher Correction limit buys more shift on high-torque cars
/// (which saturate 5 Nm) but can overshoot on light ones. Even K2 is a nearly-free 5 ms — most of its
/// targets are known data. The old single-sample RLS model measured under 1 ms at K6.</para>
/// </summary>
public sealed class PredictionModule : FFBModule
{
	private const int Horizon = 1;          // effective-setting indices (0 = Enabled)
	private const int CorrectionLimit = 2;
	private const int Strength = 3;

	private const int TapCount = 24;        // torque lags per depth filter over the raw 360 Hz history
	private const int AuxCount = 4;         // steering angle, steering wheel velocity, lateral velocity, pitch acceleration
	private const int AuxTapCount = 6;      // frame-spaced taps per aux channel (they are 60 Hz-grade channels)
	private const int FeatureCount = TapCount + ( AuxCount * AuxTapCount ) + 1;  // + 1 adaptive bias feature
	private const float StepSize = 0.25f;   // NLMS mu — one update per depth per frame; misadjustment-safe
	private const float Epsilon = 1e-3f;    // NLMS normalization guard (also idles adaptation on a dead bus)
	private const int HistorySize = 64;     // ring capacity (power of two) — max lookback = horizon + (AuxTapCount-1)*6 = 42
	private const int HistoryMask = HistorySize - 1;

	private readonly float[] _history = new float[ HistorySize ];   // raw torque (Nm), one entry per tick
	private readonly float[][] _auxHistory;                         // aux channels scaled to torque range, frame-constant
	private long _sampleCount;              // samples ingested so far; newest sample sits at _sampleCount - 1

	private float _previousFramePitchRate;  // for the pitch-acceleration frame difference

	// one adaptive filter per extrapolation depth (at most 6 depths are exercised per frame: i + k - 5 for i = 0..5)
	private readonly float[][] _weights;
	private readonly float[] _features = new float[ FeatureCount ];     // scratch — engine thread only
	private readonly float[] _predicted = new float[ FFBTickContext.SamplesPerFrame ];
	private bool _predictionsValid;

	private int _horizonTicks;

	public PredictionModule()
	{
		_auxHistory = new float[ AuxCount ][];

		for ( var i = 0; i < AuxCount; i++ )
		{
			_auxHistory[ i ] = new float[ HistorySize ];
		}

		_weights = new float[ FFBTickContext.SamplesPerFrame ][];

		for ( var i = 0; i < _weights.Length; i++ )
		{
			_weights[ i ] = new float[ FeatureCount ];
		}

		ResetFilters();
	}

	private void ResetFilters()
	{
		// invalidate first — an edit-time reset (UI thread) can race the 360 Hz reader, which checks this
		// flag before touching the predictions (same tolerance class as raw _v reads)
		_predictionsValid = false;

		foreach ( var weights in _weights )
		{
			Array.Clear( weights );

			weights[ 0 ] = 1f; // start at persistence (predict "no change") and learn from there
		}

		Array.Clear( _predicted );
	}

	public override void Reset()
	{
		Array.Clear( _history );

		foreach ( var auxHistory in _auxHistory )
		{
			Array.Clear( auxHistory );
		}

		_sampleCount = 0;
		_previousFramePitchRate = 0f;

		ResetFilters();
	}

	protected override void OnValuesChanged()
	{
		var horizon = (int) MathF.Round( _v[ Horizon ] );

		if ( horizon != _horizonTicks )
		{
			_horizonTicks = horizon;

			ResetFilters(); // each depth's filter learns a fixed depth — a new horizon means new depths
		}
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var horizonTicks = _horizonTicks;

		if ( horizonTicks <= 0 )   // Horizon Off — pass through
		{
			return inputA;
		}

		if ( ctx.SampleIndex == 0 )
		{
			IngestFrameAndPredict( in ctx, horizonTicks );
		}

		var currentTorque = ctx.TorqueFrame[ ctx.SampleIndex ];

		var strength = _v[ Strength ];

		var targetIndex = ctx.SampleIndex + horizonTicks;

		float delta;

		if ( targetIndex < FFBTickContext.SamplesPerFrame )
		{
			// the target sample is inside the already-known frame — exact data, so Strength only fades it in
			// (re-expanding beyond 100% would overshoot truth)
			delta = ( ctx.TorqueFrame[ targetIndex ] - currentTorque ) * MathF.Min( strength, 1f );
		}
		else if ( _predictionsValid )
		{
			var depthIndex = targetIndex - FFBTickContext.SamplesPerFrame - Math.Max( 0, horizonTicks - FFBTickContext.SamplesPerFrame );

			delta = ( _predicted[ depthIndex ] - currentTorque ) * strength;
		}
		else
		{
			delta = 0f; // still filling the history ring after a reset
		}

		var limit = _v[ CorrectionLimit ];

		return inputA + Math.Clamp( delta, -limit, limit );
	}

	/// <summary>
	/// Once per frame (sub-tick 0): ingest the frame's six raw samples (plus the frame's aux channel values),
	/// give each depth filter its one NLMS update (the prediction made from anchor newest−d has just had its
	/// truth arrive — the newest sample), then precompute each depth's prediction from the newest anchor for
	/// the frame's six sub-ticks to read.
	/// </summary>
	private void IngestFrameAndPredict( in FFBTickContext ctx, int horizonTicks )
	{
		var maxForce = ctx.MaxForce;

		// pitch acceleration = frame difference of the pitch rate (rad/s²) — longitudinal weight-transfer rate
		var pitchAcceleration = ( ctx.PitchRate - _previousFramePitchRate ) * 60f;

		_previousFramePitchRate = ctx.PitchRate;

		// the aux channels are order-1 physical quantities scaled into the torque's range (× MaxForce) so the
		// shared NLMS normalization treats their taps comparably to the torque taps; all are 60 Hz
		// frame-constant, so every tick slot of this frame gets the same value
		Span<float> auxValues =
		[
			ctx.WheelPosition * maxForce,     // steering angle, normalized to the car's half-lock
			ctx.WheelVelocity * maxForce,     // steering wheel velocity, in half-locks per second
			ctx.VelocityY * maxForce,         // lateral velocity (m/s)
			pitchAcceleration * maxForce
		];

		for ( var i = 0; i < FFBTickContext.SamplesPerFrame; i++ )
		{
			var slot = (int) ( _sampleCount & HistoryMask );

			_history[ slot ] = ctx.TorqueFrame[ i ];

			for ( var aux = 0; aux < AuxCount; aux++ )
			{
				_auxHistory[ aux ][ slot ] = auxValues[ aux ];
			}

			_sampleCount++;
		}

		// adaptation and prediction need the full feature lookback behind the training anchor (newest - depth)
		if ( _sampleCount < horizonTicks + TapCount + ( AuxTapCount * FFBTickContext.SamplesPerFrame ) )
		{
			return;
		}

		var newest = _sampleCount - 1;

		var newestTorque = _history[ (int) ( newest & HistoryMask ) ];

		var bias = ctx.MaxForce; // constant feature at torque scale — its weight becomes an adaptive offset

		var minDepth = Math.Max( 1, horizonTicks - ( FFBTickContext.SamplesPerFrame - 1 ) );

		for ( var depth = minDepth; depth <= horizonTicks; depth++ )
		{
			var weights = _weights[ depth - minDepth ];

			// NLMS update: score the prediction the filter would have made from anchor (newest - depth)
			// against the truth that just arrived (the newest sample), then step the weights toward it
			BuildFeatures( newest - depth, bias );

			var dot = 0f;
			var norm = Epsilon;

			for ( var j = 0; j < FeatureCount; j++ )
			{
				var feature = _features[ j ];

				dot += weights[ j ] * feature;
				norm += feature * feature;
			}

			var scale = StepSize * ( newestTorque - dot ) / norm;

			for ( var j = 0; j < FeatureCount; j++ )
			{
				weights[ j ] += scale * _features[ j ];
			}

			// this depth's prediction for the frame, anchored at the newest known sample
			BuildFeatures( newest, bias );

			var prediction = 0f;

			for ( var j = 0; j < FeatureCount; j++ )
			{
				prediction += weights[ j ] * _features[ j ];
			}

			_predicted[ depth - minDepth ] = prediction;
		}

		_predictionsValid = true;
	}

	/// <summary>The feature vector anchored at a history position: 24 torque lags, 6 frame-spaced (60 Hz)
	/// taps per aux channel, and the bias — into the <see cref="_features"/> scratch.</summary>
	private void BuildFeatures( long anchor, float bias )
	{
		for ( var j = 0; j < TapCount; j++ )
		{
			_features[ j ] = _history[ (int) ( ( anchor - j ) & HistoryMask ) ];
		}

		var index = TapCount;

		for ( var aux = 0; aux < AuxCount; aux++ )
		{
			var auxHistory = _auxHistory[ aux ];

			for ( var j = 0; j < AuxTapCount; j++ )
			{
				_features[ index++ ] = auxHistory[ (int) ( ( anchor - ( j * FFBTickContext.SamplesPerFrame ) ) & HistoryMask ) ];
			}
		}

		_features[ index ] = bias;
	}
}

/// <summary>
/// 60 Hz prediction. The 60 Hz sibling of the <see cref="PredictionModule"/>, for graphs built on the 60 Hz
/// source. It shifts the 60 Hz torque waveform left in time by predicting the 60 Hz sample Horizon FRAMES ahead
/// and adding the predicted change onto its input — computed ONCE per 60 Hz frame and held across the frame's
/// six 360 Hz sub-ticks (so its output is a clean 60 Hz staircase, like everything on a 60 Hz graph). Place it
/// directly on the 60 Hz source and smooth AFTER it if needed. Horizon 6 (K6) on the 360 Hz module and Horizon
/// 1 here both target one 60 Hz frame (~16.7 ms); this module's default is 2 frames (~33.3 ms).
/// <para>Unlike the 360 Hz module there is NO frame-anchoring shortcut — the 60 Hz stream is one value per
/// frame, so the module has to extrapolate the whole horizon rather than reuse already-delivered intra-frame
/// samples. The PredictionLab (--sixty) showed torque history ALONE cannot do this at 60 Hz — least-squares
/// collapses to persistence and Strength only adds noise. The lead instead comes from the telemetry: the module
/// learns a single NLMS filter over 12 frames of 60 Hz torque plus 6 frame-taps each of three auxiliary channels
/// — steering angle, steering wheel velocity, and lateral velocity (the greedy-selected set; a fourth
/// lateral-G-rate channel was not worth new telemetry, and the 360 Hz module's pitch acceleration does not help
/// at 60 Hz) — with an adaptive bias. Strength re-expands the amplitude-shy least-squares correction and the
/// result is clamped to ±Correction limit (Nm).</para>
/// <para>Measured across eleven cars/tracks (vs the ideal shifted 60 Hz waveform): the default 2 frames / 100% /
/// 5 Nm delivers ≈ 8 ms of true shift with LESS error than doing nothing on every car. Two knob lessons flip vs
/// the 360 Hz module: 2 frames beats 1 (the telemetry lead extends further out), and 100% beats higher
/// strengths (the aux channels already carry the lead, so there is little shrinkage to re-expand — 150% pushes
/// the worst car back to worse-than-nothing).</para>
/// </summary>
public sealed class Prediction60HzModule : FFBModule
{
	private const int Horizon = 1;          // effective-setting indices (0 = Enabled)
	private const int CorrectionLimit = 2;
	private const int Strength = 3;

	private const int TapCount = 12;        // 60 Hz torque frame lags
	private const int AuxCount = 3;         // steering angle, steering wheel velocity, lateral velocity
	private const int AuxTapCount = 6;      // frame lags per aux channel
	private const int FeatureCount = TapCount + ( AuxCount * AuxTapCount ) + 1;  // + 1 adaptive bias feature
	private const float StepSize = 0.25f;   // NLMS mu — one update per frame; misadjustment-safe
	private const float Epsilon = 1e-3f;    // NLMS normalization guard (also idles adaptation on a dead bus)
	private const int HistorySize = 32;     // ring capacity (power of two) — max lookback = horizon + TapCount
	private const int HistoryMask = HistorySize - 1;

	private readonly float[] _history = new float[ HistorySize ];   // 60 Hz torque (Nm), one entry per frame
	private readonly float[][] _auxHistory;                         // aux channels scaled to torque range
	private long _frameCount;               // frames ingested so far; newest frame sits at _frameCount - 1

	private readonly float[] _weights = new float[ FeatureCount ];  // single filter (one fixed depth = horizon)
	private readonly float[] _features = new float[ FeatureCount ]; // scratch — engine thread only

	private float _heldCorrection;          // computed at sub-tick 0, held across the frame's six sub-ticks
	private int _horizonFrames;

	public Prediction60HzModule()
	{
		_auxHistory = new float[ AuxCount ][];

		for ( var i = 0; i < AuxCount; i++ )
		{
			_auxHistory[ i ] = new float[ HistorySize ];
		}

		ResetFilter();
	}

	private void ResetFilter()
	{
		Array.Clear( _weights );

		_weights[ 0 ] = 1f; // start at persistence (predict "no change") and learn from there

		_heldCorrection = 0f;
	}

	public override void Reset()
	{
		Array.Clear( _history );

		foreach ( var auxHistory in _auxHistory )
		{
			Array.Clear( auxHistory );
		}

		_frameCount = 0;

		ResetFilter();
	}

	protected override void OnValuesChanged()
	{
		var horizon = (int) MathF.Round( _v[ Horizon ] );

		if ( horizon != _horizonFrames )
		{
			_horizonFrames = horizon;

			ResetFilter(); // the filter learns a fixed depth — a new horizon means a new depth
		}
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var horizonFrames = _horizonFrames;

		if ( horizonFrames <= 0 )   // Horizon Off — pass through
		{
			return inputA;
		}

		if ( ctx.SampleIndex == 0 )
		{
			IngestFrameAndPredict( in ctx, horizonFrames );
		}

		return inputA + _heldCorrection;
	}

	/// <summary>
	/// Once per frame (sub-tick 0): ingest the frame's 60 Hz torque and aux values, give the filter its one NLMS
	/// update (the prediction it made from anchor newest−horizon has just had its truth arrive — the newest
	/// sample), then predict horizon frames ahead from the newest anchor and hold that correction for the frame.
	/// </summary>
	private void IngestFrameAndPredict( in FFBTickContext ctx, int horizonFrames )
	{
		var maxForce = ctx.MaxForce;

		// the aux channels are order-1 physical quantities scaled into the torque's range (× MaxForce) so the
		// shared NLMS normalization treats their taps comparably to the torque taps
		Span<float> auxValues =
		[
			ctx.WheelPosition * maxForce,     // steering angle, normalized to the car's half-lock
			ctx.WheelVelocity * maxForce,     // steering wheel velocity, in half-locks per second
			ctx.VelocityY * maxForce          // lateral velocity (m/s)
		];

		var slot = (int) ( _frameCount & HistoryMask );

		_history[ slot ] = ctx.Torque60Hz;

		for ( var aux = 0; aux < AuxCount; aux++ )
		{
			_auxHistory[ aux ][ slot ] = auxValues[ aux ];
		}

		_frameCount++;

		// adaptation and prediction need the full feature lookback behind the training anchor (newest - horizon)
		if ( _frameCount < horizonFrames + TapCount + AuxTapCount )
		{
			_heldCorrection = 0f;

			return;
		}

		var newest = _frameCount - 1;

		var newestTorque = _history[ (int) ( newest & HistoryMask ) ];

		var bias = maxForce; // constant feature at torque scale — its weight becomes an adaptive offset

		// NLMS update: score the prediction the filter would have made from anchor (newest - horizon) against
		// the truth that just arrived (the newest sample), then step the weights toward it
		BuildFeatures( newest - horizonFrames, bias );

		var dot = 0f;
		var norm = Epsilon;

		for ( var j = 0; j < FeatureCount; j++ )
		{
			var feature = _features[ j ];

			dot += _weights[ j ] * feature;
			norm += feature * feature;
		}

		var scale = StepSize * ( newestTorque - dot ) / norm;

		for ( var j = 0; j < FeatureCount; j++ )
		{
			_weights[ j ] += scale * _features[ j ];
		}

		// predict the sample horizon frames ahead, anchored at the newest known sample
		BuildFeatures( newest, bias );

		var prediction = 0f;

		for ( var j = 0; j < FeatureCount; j++ )
		{
			prediction += _weights[ j ] * _features[ j ];
		}

		var limit = _v[ CorrectionLimit ];

		_heldCorrection = Math.Clamp( ( prediction - newestTorque ) * _v[ Strength ], -limit, limit );
	}

	/// <summary>The feature vector anchored at a frame position: 12 60 Hz torque lags, 6 frame-taps per aux
	/// channel, and the bias — into the <see cref="_features"/> scratch.</summary>
	private void BuildFeatures( long anchor, float bias )
	{
		for ( var j = 0; j < TapCount; j++ )
		{
			_features[ j ] = _history[ (int) ( ( anchor - j ) & HistoryMask ) ];
		}

		var index = TapCount;

		for ( var aux = 0; aux < AuxCount; aux++ )
		{
			var auxHistory = _auxHistory[ aux ];

			for ( var j = 0; j < AuxTapCount; j++ )
			{
				_features[ index++ ] = auxHistory[ (int) ( ( anchor - j ) & HistoryMask ) ];
			}
		}

		_features[ index ] = bias;
	}
}
