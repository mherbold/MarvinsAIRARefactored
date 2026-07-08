
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.FFB.Modules;

// Generic DSP primitives operating in the Nm main-bus domain. The old monolithic algorithms
// (RacingWheel.ProcessAlgorithm) are recreated by COMPOSING these primitives in the built-in stacks; see
// FFBStackMigration for the exact wiring. Decomposition identities (verified algebraically):
//   out = Lerp(out_prev + g·Δin, anchor, b)  ==  LPF_b(anchor) + g·HPF_b(in)
// where LPF_b(x): y = Lerp(y_prev, x, b), and HPF_b(x) = x − LPF_b(x). DetailBooster uses g=1+Boost,
// anchor=in; DeltaLimiter uses g=1, in=rate-limited copy; Hybrid10 uses b=0.1, anchor=60 Hz, g=Detail.

/// <summary>One-pole low-pass filter. <c>y = Lerp(y_prev, x, Smoothing)</c>.</summary>
public sealed class LowPassFilterModule : FFBModule
{
	private const int Smoothing = 1;

	private float _y;

	public override void Reset() => _y = 0f;

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		_y = MathZ.Lerp( _y, inputA, _v[ Smoothing ] );

		return _y;
	}
}

/// <summary>
/// High-pass "detail" extractor with gain: <c>(x − LPF(x)) · effectiveGain</c>, where
/// <c>effectiveGain = Lerp(Gain, 1, curbFactor)</c> (old 362/388: curb pulls the boost back toward unity).
/// Its internal LPF uses the same Smoothing as its paired LowPassFilter so the pair reconstructs the exact
/// DetailBooster recursion.
/// </summary>
public sealed class DetailExtractorModule : FFBModule
{
	private const int Smoothing = 1;
	private const int Gain = 2;

	private float _y;

	public override void Reset() => _y = 0f;

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		_y = MathZ.Lerp( _y, inputA, _v[ Smoothing ] );

		var effectiveGain = MathZ.Lerp( _v[ Gain ], 1f, Owner.CurbProtectionFactor );

		return ( inputA - _y ) * effectiveGain;
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
/// Two-input mixer. Add mode: <c>A + B·LevelB</c>. Blend mode: <c>Lerp(A, B, Mix)</c>.
/// </summary>
public sealed class MixerModule : FFBModule
{
	public const int ModeAdd = 0;
	public const int ModeBlend = 1;

	private const int Mode = 1;
	private const int Mix = 2;
	private const int LevelB = 3;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( (int) _v[ Mode ] == ModeBlend )
		{
			return MathZ.Lerp( inputA, inputB, _v[ Mix ] );
		}

		return inputA + inputB * _v[ LevelB ];
	}
}

/// <summary>
/// Slew (rate) limiter. Reconstructs the signal from per-tick deltas of the TRUE input clamped to
/// ±(Limit/500) (old line 374). <c>Limit</c> is in the old Nm/s-style units; the ÷500 matches the old code.
/// Consumes the curb factor (curb drives the limit toward 0, old 373).
/// </summary>
public sealed class RateLimiterModule : FFBModule
{
	private const int Limit = 1;

	private float _lastInput;
	private float _output;

	public override void Reset()
	{
		_lastInput = 0f;
		_output = 0f;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var perTickLimit = MathZ.Lerp( _v[ Limit ] / 500f, 0f, Owner.CurbProtectionFactor );

		var delta = Math.Clamp( inputA - _lastInput, -perTickLimit, perTickLimit );

		_output += delta;
		_lastInput = inputA;

		return _output;
	}
}

/// <summary>
/// Slew compressor with two modes.
/// <para>Linear mode reproduces the old SlewAndTotalCompression branch (RacingWheel.cs 414–448) INCLUDING the
/// total-compression step, because the old code feeds the post-compression running torque back into the slew
/// integrator (line 447 stores the compressed value) — the two operations are coupled and cannot be split
/// into independent forward modules without breaking parity.</para>
/// <para>Soft mode reproduces the old Multi SlewRateReduction stage (529–572), including the peak-hold branch;
/// its slew Threshold/Rate/Width are the already-derived values (old 557–558/562), so the module simply guards
/// on <c>Rate &gt; 0</c> (equivalent to the old <c>slewAmount &gt; 0</c>, since Rate = min(slewAmount^0.55, 0.9)
/// is 0 iff slewAmount is 0) and always tracks its two state values.</para>
/// </summary>
public sealed class SlewCompressorModule : FFBModule
{
	public const int ModeLinear = 0;
	public const int ModeSoft = 1;

	private const int Mode = 1;
	private const int Threshold = 2;
	private const int Rate = 3;
	private const int Width = 4;                       // Soft
	private const int PeakMode = 5;                    // Soft
	private const int TotalCompressionThreshold = 6;   // Linear
	private const int TotalCompressionRate = 7;        // Linear

	// Linear state
	private float _runningNm;

	// Soft state
	private float _lastNormalizedCompressed;
	private float _lastNormalizedSlewReduced;

	public override void Reset()
	{
		_runningNm = 0f;
		_lastNormalizedCompressed = 0f;
		_lastNormalizedSlewReduced = 0f;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return (int) _v[ Mode ] == ModeSoft ? ProcessSoft( ctx, inputA ) : ProcessLinear( ctx, inputA );
	}

	private float ProcessLinear( in FFBTickContext ctx, float inputA )
	{
		var maxForce = ctx.MaxForce;
		var curbFactor = Owner.CurbProtectionFactor;

		var normalizedRunningTorque = _runningNm / maxForce;
		var normalizedDelta = ( inputA - _runningNm ) / maxForce;
		var normalizedDeltaAbs = MathF.Abs( normalizedDelta );

		var deltaLimit = MathZ.Lerp( _v[ Threshold ] / 500f, 0f, curbFactor );

		float oneMinusSlewCompressionRate;

		if ( MathF.Sign( normalizedDelta ) == MathF.Sign( inputA ) )
		{
			oneMinusSlewCompressionRate = 1f - _v[ Rate ];
		}
		else
		{
			oneMinusSlewCompressionRate = MathF.Max( 0.75f, 1f - _v[ Rate ] * 0.75f );
		}

		oneMinusSlewCompressionRate = MathZ.Lerp( oneMinusSlewCompressionRate, 0f, curbFactor );

		if ( normalizedDeltaAbs > deltaLimit )
		{
			normalizedRunningTorque += MathF.CopySign( deltaLimit + ( ( normalizedDeltaAbs - deltaLimit ) * oneMinusSlewCompressionRate ), normalizedDelta );
		}
		else
		{
			normalizedRunningTorque += normalizedDelta;
		}

		if ( _v[ TotalCompressionRate ] != 0f )
		{
			normalizedRunningTorque = MathZ.Compression( normalizedRunningTorque, _v[ TotalCompressionRate ], _v[ TotalCompressionThreshold ], _v[ TotalCompressionThreshold ] );
		}

		_runningNm = normalizedRunningTorque * maxForce;

		return _runningNm;
	}

	private float ProcessSoft( in FFBTickContext ctx, float inputA )
	{
		var maxForce = ctx.MaxForce;

		var normalizedCompressedTorque = inputA / maxForce;
		var normalizedSlewReducedTorque = normalizedCompressedTorque;

		var slewRate = _v[ Rate ];

		if ( slewRate > 0f )
		{
			var absNormalizedCompressedTorque = MathF.Abs( normalizedCompressedTorque );
			var absNormalizedLastCompressedTorque = MathF.Abs( _lastNormalizedCompressed );
			var absNormalizedLastSlewReducedTorque = MathF.Abs( _lastNormalizedSlewReduced );

			if ( ( _v[ PeakMode ] != 0f ) && ( absNormalizedCompressedTorque < absNormalizedLastCompressedTorque ) && ( absNormalizedLastCompressedTorque != 0f ) )
			{
				var targetScaledTorque = _lastNormalizedSlewReduced * normalizedCompressedTorque / _lastNormalizedCompressed;
				var targetBlendedTorque = targetScaledTorque * 0.5f + normalizedCompressedTorque * 0.5f;

				if ( MathF.Abs( targetBlendedTorque ) < absNormalizedLastSlewReducedTorque )
				{
					normalizedSlewReducedTorque = _lastNormalizedSlewReduced + ( targetBlendedTorque - _lastNormalizedSlewReduced );
				}
				else
				{
					normalizedSlewReducedTorque = _lastNormalizedSlewReduced + ( targetScaledTorque - _lastNormalizedSlewReduced );
				}
			}
			else
			{
				var normalizedSlewDelta = normalizedCompressedTorque - _lastNormalizedSlewReduced;
				var slewThreshold = _v[ Threshold ];
				var slewWidth = _v[ Width ];

				if ( MathF.Abs( normalizedSlewDelta ) > slewThreshold )
				{
					var slewRateMultiplier = ( absNormalizedCompressedTorque < absNormalizedLastSlewReducedTorque ) ? 0.8f : 1f;

					normalizedSlewReducedTorque = _lastNormalizedSlewReduced + MathZ.Compression( normalizedSlewDelta, slewRate * slewRateMultiplier, slewThreshold, slewWidth );
				}
				else
				{
					normalizedSlewReducedTorque = normalizedCompressedTorque;
				}
			}
		}

		_lastNormalizedCompressed = normalizedCompressedTorque;
		_lastNormalizedSlewReduced = normalizedSlewReducedTorque;

		return normalizedSlewReducedTorque * maxForce;
	}
}

/// <summary>
/// Amplitude compressor via <see cref="MathZ.Compression"/> in normalized space (old 442–445 / 517–527).
/// Stateless. With Rate = 0 (and Threshold = 1, Width = 0) it is an identity, matching the old
/// "compressionAmount &gt; 0" guard.
/// </summary>
public sealed class CompressorModule : FFBModule
{
	private const int Threshold = 1;
	private const int Rate = 2;
	private const int Width = 3;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var normalized = inputA / ctx.MaxForce;

		var result = MathZ.Compression( normalized, _v[ Rate ], _v[ Threshold ], _v[ Width ] );

		return result * ctx.MaxForce;
	}
}

/// <summary>
/// Deviation-from-LPF dynamic gain with directional gating and momentum carry (old Multi DetailGain 574–609).
/// <c>effectiveGain = Lerp(1 + Gain, 1, curbFactor)</c>; when it equals 1 the module passes through while still
/// tracking its LPF/output state (old 635–636 always store).
/// </summary>
public sealed class DetailEnhancerModule : FFBModule
{
	private const int Smoothing = 1;
	private const int Gain = 2;

	private const float EpsilonGuard = 1e-6f;

	private float _lastInput;   // normalizedLastSlewReducedTorque
	private float _lastLpf;     // normalizedLastDetailGainLpfTorque
	private float _lastOutput;  // normalizedLastDetailGainTorque

	public override void Reset()
	{
		_lastInput = 0f;
		_lastLpf = 0f;
		_lastOutput = 0f;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var maxForce = ctx.MaxForce;

		var normalizedSlewReducedTorque = inputA / maxForce;

		var normalizedDetailGainTorque = normalizedSlewReducedTorque;
		var normalizedDetailGainLpfTorque = 0f;

		var detailGain = MathZ.Lerp( 1f + _v[ Gain ], 1f, Owner.CurbProtectionFactor );

		if ( detailGain != 1f )
		{
			normalizedDetailGainLpfTorque = MathZ.Lerp( _lastLpf, normalizedSlewReducedTorque, _v[ Smoothing ] );

			var currentDeviation = normalizedSlewReducedTorque - normalizedDetailGainLpfTorque;
			var lastDeviation = _lastInput - normalizedDetailGainLpfTorque;
			var priorDeviation = _lastInput - _lastLpf;

			if ( MathF.Abs( currentDeviation ) > MathF.Abs( lastDeviation ) || MathF.Sign( currentDeviation ) != MathF.Sign( priorDeviation ) || MathF.Abs( lastDeviation ) < EpsilonGuard )
			{
				if ( currentDeviation > 0f )
				{
					normalizedDetailGainTorque = MathF.Max( normalizedDetailGainLpfTorque + currentDeviation * detailGain, normalizedDetailGainLpfTorque );
				}
				else
				{
					normalizedDetailGainTorque = MathF.Min( normalizedDetailGainLpfTorque + currentDeviation * detailGain, normalizedDetailGainLpfTorque );
				}
			}
			else
			{
				var ratio = currentDeviation / lastDeviation;
				var carried = ratio * ( _lastOutput - normalizedDetailGainLpfTorque );
				var candidate = normalizedDetailGainLpfTorque + carried;

				normalizedDetailGainTorque = ( currentDeviation > 0f ) ? MathF.Max( candidate, normalizedDetailGainLpfTorque ) : MathF.Min( candidate, normalizedDetailGainLpfTorque );
			}
		}

		_lastInput = normalizedSlewReducedTorque;
		_lastLpf = normalizedDetailGainLpfTorque;
		_lastOutput = normalizedDetailGainTorque;

		return normalizedDetailGainTorque * maxForce;
	}
}

/// <summary>
/// Delta-tracking output smoother (old Multi OutputSmoothing 611–624):
/// <c>Lerp(x, y_prev + Δlpf, 0.9·√Amount)</c>. Passes through when Amount = 0 while still tracking state.
/// </summary>
public sealed class SmootherModule : FFBModule
{
	private const int Amount = 1;
	private const int Smoothing = 2;

	private float _lastSmoothingLpf;
	private float _lastSmoothed;

	public override void Reset()
	{
		_lastSmoothingLpf = 0f;
		_lastSmoothed = 0f;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var maxForce = ctx.MaxForce;

		var normalizedDetailGainTorque = inputA / maxForce;

		var normalizedSmoothedTorque = normalizedDetailGainTorque;
		var normalizedSmoothingLpfTorque = 0f;

		var amount = _v[ Amount ];

		if ( amount > 0f )
		{
			normalizedSmoothingLpfTorque = MathZ.Lerp( _lastSmoothingLpf, normalizedDetailGainTorque, _v[ Smoothing ] );

			var smoothingRate = 0.9f * MathF.Pow( amount, 0.5f );
			var lpfDeltaAdjustedTorque = _lastSmoothed + normalizedSmoothingLpfTorque - _lastSmoothingLpf;

			normalizedSmoothedTorque = MathZ.Lerp( normalizedDetailGainTorque, lpfDeltaAdjustedTorque, smoothingRate );
		}

		_lastSmoothingLpf = normalizedSmoothingLpfTorque;
		_lastSmoothed = normalizedSmoothedTorque;

		return normalizedSmoothedTorque * maxForce;
	}
}

/// <summary>
/// Peak-detected adaptive blend toward an anchor (old Multi HybridVariable30 source stage 492–505).
/// Input A = detail (360 Hz), input B = anchor (60 Hz). Blends the detail-integrated running torque toward
/// the anchor by <c>Mix - (Mix - PeakMix)·peakCountdown/HoldTicks</c>; a velocity-sign flip re-arms the hold
/// (peakCountdown = HoldTicks). Consumes the curb factor (Detail toward 0).
/// </summary>
public sealed class AdaptiveBlendModule : FFBModule
{
	private const int Detail = 1;
	private const int Mix = 2;
	private const int PeakMix = 3;
	private const int HoldTicks = 4;

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

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var detail = MathZ.Lerp( _v[ Detail ], 0f, Owner.CurbProtectionFactor );
		var mix = _v[ Mix ];
		var peakMix = _v[ PeakMix ];
		var holdTicks = _v[ HoldTicks ];

		var peakCountdown = MathF.Max( 0f, _peakCountdown - 1f );

		var hfScaledDelta = ( inputA - _lastA ) * detail;

		var mixNow = mix - ( mix - peakMix ) * peakCountdown / holdTicks;
		var preliminaryHybridTorque = MathZ.Lerp( _hybridLast + hfScaledDelta, inputB, mixNow );

		float hybridTorque;

		if ( MathF.Sign( preliminaryHybridTorque - _hybridLast ) != _lastVelocitySign )
		{
			peakCountdown = holdTicks;

			var mixNowPeak = mix - ( mix - peakMix ) * peakCountdown / holdTicks;

			hybridTorque = MathZ.Lerp( _hybridLast + hfScaledDelta, inputB, mixNowPeak );
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
