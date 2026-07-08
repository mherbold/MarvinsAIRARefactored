
using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.FFB.Modules;

// Effect modules operate on the Nm main bus. Multiplicative effects (crash scale, parked strength, decrease-
// force) are scale-invariant and apply identically in Nm or normalized space; additive effects (LFE, soft
// lock, friction, centering, increase-force) scale their contribution by ctx.MaxForce because the old code
// added a normalized contribution to the normalized bus. Under the neutral preview/parity context every
// effect is inert (all aux telemetry zero, IsOnTrack false, no protection pulses), so they pass input A
// through unchanged — matching the old preview which only showed the algorithm.

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

		if ( ctx.CrashProtectionTriggered )
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
/// and publishes <see cref="FFBStackEngine.CurbProtectionFactor"/> BEFORE the signal loop, so downstream
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

		if ( ctx.CurbProtectionTriggered )
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

/// <summary>Reduce forces when parked (old 1406–1409): <c>× Lerp(1, Strength, ParkedFactor)</c>.</summary>
public sealed class ParkedStrengthModule : FFBModule
{
	private const int Strength = 1;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var strength = _v[ Strength ];

		if ( strength < 1f )
		{
			return inputA * MathZ.Lerp( 1f, strength, ctx.ParkedFactor );
		}

		return inputA;
	}
}

/// <summary>Wheel LFE mix (old 1413–1416), gated on IsOnTrack. Additive, scaled by MaxForce.</summary>
public sealed class LFEMixModule : FFBModule
{
	private const int Strength = 1;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var strength = _v[ Strength ];

		if ( ( strength > 0f ) && ctx.IsOnTrack )
		{
			return inputA + ctx.LFEMagnitude * strength * ctx.MaxForce;
		}

		return inputA;
	}
}

/// <summary>Soft lock (old 1420–1435). Additive, scaled by MaxForce.</summary>
public sealed class SoftLockModule : FFBModule
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

				return inputA + contribution * ctx.MaxForce;
			}
		}

		return inputA;
	}
}

/// <summary>Racing + parked friction (old 1439–1449 merged). Additive, scaled by MaxForce.</summary>
public sealed class FrictionModule : FFBModule
{
	private const int RacingFriction = 1;
	private const int ParkedFriction = 2;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var contribution = 0f;

		var racingFriction = _v[ RacingFriction ];

		if ( racingFriction > 0f )
		{
			contribution += MathZ.Lerp( ctx.WheelVelocity * racingFriction, 0f, ctx.ParkedFactor );
		}

		var parkedFriction = _v[ ParkedFriction ];

		if ( parkedFriction > 0f )
		{
			contribution += MathZ.Lerp( 0f, ctx.WheelVelocity * parkedFriction, ctx.ParkedFactor );
		}

		if ( contribution != 0f )
		{
			return inputA + contribution * ctx.MaxForce;
		}

		return inputA;
	}
}

/// <summary>Wheel centering while racing / parked (old 1453–1461), gated on IsOnTrack. Additive × MaxForce.</summary>
public sealed class WheelCenteringModule : FFBModule
{
	private const int Strength = 1;
	private const int WhileRacing = 2;
	private const int WhileParked = 3;

	public override void Reset() { }

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( ctx.IsOnTrack )
		{
			var centeringForce = Math.Clamp( ( Math.Clamp( ctx.WheelPosition, -0.25f, 0.25f ) + ctx.WheelVelocity * 0.1f ) * _v[ Strength ], -1f, 1f );

			var racingCenteringForce = ( _v[ WhileRacing ] != 0f ) ? centeringForce : 0f;
			var parkedCenteringForce = ( _v[ WhileParked ] != 0f ) ? centeringForce : 0f;

			return inputA + MathZ.Lerp( racingCenteringForce, parkedCenteringForce, ctx.ParkedFactor ) * ctx.MaxForce;
		}

		return inputA;
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
