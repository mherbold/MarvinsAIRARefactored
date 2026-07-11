
using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.FFB.Modules;

// Effect modules operate on the Nm main bus. Multiplicative effects (crash scale, decrease-force) are
// scale-invariant and apply identically in Nm or normalized space; additive effects (increase-force) scale
// their contribution by ctx.MaxForce because the old code added a normalized contribution to the normalized
// bus. (The old purely-additive effects — LFE, friction, soft lock, wheel centering — are source modules in
// SourceModules.cs now, summed in with Add.) The preview replay rebuilds the context from the
// recording's telemetry (with the protection pulses re-derived from the recorded raw G forces / shock
// velocity against the current thresholds), so effects render in the preview just as they behaved live;
// older two-column recordings replay with zero telemetry, leaving these modules inert like the old preview.

/// <summary>
/// Crash protection (old 1266–1290 + scale 1398). PrePass advances the timer (re-armed by the one-tick
/// <c>ctx.CrashProtectionTriggered</c> pulse) and Process multiplies by the recovery-ramped scale. Publishes
/// the active flag and the long/lat g-force thresholds read by Simulator.
/// </summary>
public sealed class CrashProtectionModule : FFBModule
{
	private const float RecoveryTimeMS = 1000f;

	private const int LongGForce = 1;
	private const int LatGForce = 2;
	private const int Duration = 3;
	private const int ForceReduction = 4;

	private float _timerMS;
	private float _scale = 1f;

	public override void Reset()
	{
		_timerMS = 0f;
		_scale = 1f;
	}

	public override void PrePass( in FFBTickContext ctx )
	{
		if ( !Enabled )
		{
			_scale = 1f;

			return;
		}

		if ( ctx.CrashProtectionTriggered || TestActive )
		{
			_timerMS = _v[ Duration ] * 1000f + RecoveryTimeMS;
		}

		_scale = 1f;

		if ( _timerMS > 0f )
		{
			_scale = 1f - _v[ ForceReduction ] * ( ( _timerMS <= RecoveryTimeMS ) ? ( _timerMS / RecoveryTimeMS ) : 1f );

			_timerMS -= ctx.DeltaMilliseconds;
		}

		Owner.CrashProtectionActive = _timerMS > 0f;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return inputA * _scale;
	}

	public override void PublishAggregates()
	{
		// A protection that is disabled or would do nothing (zero duration or zero force reduction) publishes an
		// "off" threshold so Simulator never triggers it — same effect as the old Duration/ForceReduction guards.
		if ( !Enabled || ( _v[ Duration ] <= 0f ) || ( _v[ ForceReduction ] <= 0f ) )
		{
			Owner.CrashLongGForceThreshold = 20f;
			Owner.CrashLatGForceThreshold = 20f;
		}
		else
		{
			Owner.CrashLongGForceThreshold = _v[ LongGForce ];
			Owner.CrashLatGForceThreshold = _v[ LatGForce ];
		}
	}
}

/// <summary>
/// Curb protection (old 1294–1318). PrePass advances the timer (re-armed by <c>ctx.CurbProtectionTriggered</c>)
/// and publishes <see cref="FFBGraphEngine.CurbProtectionFactor"/> BEFORE the signal loop, so downstream
/// curb-consuming DSP modules see it exactly where the old algorithm did. The signal is passed through.
/// </summary>
public sealed class CurbProtectionModule : FFBModule
{
	private const int ShockVelocity = 1;
	private const int Duration = 2;
	private const int ForceReduction = 3;

	private float _timerMS;

	public override void Reset() => _timerMS = 0f;

	public override void PrePass( in FFBTickContext ctx )
	{
		if ( !Enabled )
		{
			return;
		}

		if ( ctx.CurbProtectionTriggered || TestActive )
		{
			_timerMS = _v[ Duration ] * 1000f;
		}

		if ( _timerMS > 0f )
		{
			Owner.CurbProtectionFactor = _v[ ForceReduction ];

			_timerMS -= ctx.DeltaMilliseconds;
		}

		Owner.CurbProtectionActive = _timerMS > 0f;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		return inputA;
	}

	public override void PublishAggregates()
	{
		// Disabled or a would-do-nothing configuration (zero duration or zero force reduction) publishes an "off"
		// threshold (0) so Simulator never triggers it — same effect as the old ShockVelocity/Duration/ForceReduction guards.
		Owner.CurbShockVelocityThreshold = ( !Enabled || ( _v[ Duration ] <= 0f ) || ( _v[ ForceReduction ] <= 0f ) ) ? 0f : _v[ ShockVelocity ];
	}
}

// Constant-force steering effects. Direction is a Choice: None / DecreaseForce / IncreaseForce (index).
// DecreaseForce is scale-invariant (lerp toward 0); IncreaseForce is additive and scaled by MaxForce.

/// <summary>Understeer constant force (old 1330–1348). Module Enabled replaces the old wheel-side enable.</summary>
public sealed class UndersteerForceModule : FFBModule
{
	private const int Direction = 1;
	private const int Strength = 2;
	private const int Curve = 3;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( ctx.UndersteerEffect > 0f )
		{
			var constantForceTorque = _v[ Strength ] * MathF.Pow( ctx.UndersteerEffect, MathZ.CurveToPower( _v[ Curve ] ) );

			switch ( (RacingWheel.ConstantForceDirection) (int) _v[ Direction ] )
			{
				case RacingWheel.ConstantForceDirection.DecreaseForce:
					return MathZ.Lerp( inputA, 0f, constantForceTorque );

				case RacingWheel.ConstantForceDirection.IncreaseForce:
					return inputA + MathF.CopySign( constantForceTorque, ctx.VelocityY ) * ctx.MaxForce;
			}
		}

		return inputA;
	}
}

/// <summary>Oversteer constant force (old 1352–1370).</summary>
public sealed class OversteerForceModule : FFBModule
{
	private const int Direction = 1;
	private const int Strength = 2;
	private const int Curve = 3;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( ctx.OversteerEffect > 0f )
		{
			var constantForceTorque = _v[ Strength ] * MathF.Pow( ctx.OversteerEffect, MathZ.CurveToPower( _v[ Curve ] ) );

			switch ( (RacingWheel.ConstantForceDirection) (int) _v[ Direction ] )
			{
				case RacingWheel.ConstantForceDirection.DecreaseForce:
					return MathZ.Lerp( inputA, 0f, constantForceTorque );

				case RacingWheel.ConstantForceDirection.IncreaseForce:
					return inputA + MathF.CopySign( constantForceTorque, ctx.VelocityY ) * ctx.MaxForce;
			}
		}

		return inputA;
	}
}

/// <summary>Seat-of-pants constant force (old 1374–1394), signed effect via CopySign.</summary>
public sealed class SeatOfPantsForceModule : FFBModule
{
	private const int Direction = 1;
	private const int Strength = 2;
	private const int Curve = 3;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( ctx.SeatOfPantsEffect != 0f )
		{
			var constantForceTorque = _v[ Strength ] * MathF.CopySign( MathF.Pow( MathF.Abs( ctx.SeatOfPantsEffect ), MathZ.CurveToPower( _v[ Curve ] ) ), ctx.SeatOfPantsEffect );

			switch ( (RacingWheel.ConstantForceDirection) (int) _v[ Direction ] )
			{
				case RacingWheel.ConstantForceDirection.DecreaseForce:
					return MathZ.Lerp( inputA, 0f, MathF.Abs( constantForceTorque ) );

				case RacingWheel.ConstantForceDirection.IncreaseForce:
					return inputA - constantForceTorque * ctx.MaxForce;
			}
		}

		return inputA;
	}
}

/// <summary>
/// Speed-ramped gain: scales the signal from <c>GainAtMin</c> at/below <c>MinSpeed</c> to <c>GainAtMax</c>
/// at/above <c>MaxSpeed</c> (both in m/s). Lightens parking or stiffens at speed. Defaults are unity (no effect).
/// The preview replay uses the recording's real velocity (older two-column recordings replay as zero); the
/// test toggle pins the preview to min speed so the parked end of the ramp can be inspected regardless of
/// what was recorded.
/// </summary>
public sealed class SpeedGainModule : FFBModule
{
	private const int MinSpeed = 1;
	private const int MaxSpeed = 2;
	private const int GainAtMin = 3;
	private const int GainAtMax = 4;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var velocityMS = ( ctx.IsPreview && TestActive ) ? _v[ MinSpeed ] : ctx.VelocityMS;

		var t = MathZ.InverseLerp( _v[ MinSpeed ], _v[ MaxSpeed ], velocityMS );

		return inputA * MathZ.Lerp( _v[ GainAtMin ], _v[ GainAtMax ], t );
	}
}

/// <summary>
/// Tiny high-frequency dither added while the signal magnitude is below <c>Threshold</c> — alternates sign each
/// tick to break static friction on gear-driven wheels near center. Above the threshold it passes through.
/// (This is also the modern replacement for the old output minimum: instead of forcing small signals up to a
/// hard floor, the dither keeps the wheel's mechanism live below the floor level.)
/// </summary>
public sealed class TorqueDitherModule : FFBModule
{
	private const int Strength = 1;
	private const int Threshold = 2;

	private float _sign = 1f;

	public override void Reset() => _sign = 1f;

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( MathF.Abs( inputA / ctx.MaxForce ) < _v[ Threshold ] )
		{
			_sign = -_sign;

			return inputA + _sign * _v[ Strength ] * ctx.MaxForce;
		}

		return inputA;
	}
}
