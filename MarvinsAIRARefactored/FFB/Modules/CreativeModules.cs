
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.FFB.Modules;

// New telemetry-driven "creative" modules (milestone 6). Unlike the DSP/effect modules these have no old-code
// equivalent — they are optional flavor the user can add to a stack. The DSP primitives themselves (filters,
// mixer, gain, compressors) remain the main creativity enablers; these add speed/slip-driven texture.

/// <summary>
/// Speed-ramped gain: scales the signal from <c>GainAtMin</c> at/below <c>MinSpeed</c> to <c>GainAtMax</c>
/// at/above <c>MaxSpeed</c> (both in m/s). Lightens parking or stiffens at speed. Defaults are unity (no effect).
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
		var t = MathZ.InverseLerp( _v[ MinSpeed ], _v[ MaxSpeed ], ctx.VelocityMS );

		return inputA * MathZ.Lerp( _v[ GainAtMin ], _v[ GainAtMax ], t );
	}
}

/// <summary>
/// Speed-scaled pseudo-random rumble (a generator on the normalized vibration bus). Holds a band-limited noise
/// value updated at <c>Frequency</c> Hz (xorshift state) and scales it by strength and a speed factor; silent
/// when parked or off-track.
/// </summary>
public sealed class RoadTextureModule : FFBModule
{
	private const int Strength = 1;
	private const int Frequency = 2;

	private const uint InitialSeed = 0x1234567u;

	private uint _rng = InitialSeed;
	private float _phaseMs;
	private float _current;

	public override void Reset()
	{
		_rng = InitialSeed;
		_phaseMs = 0f;
		_current = 0f;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.IsOnTrack || ( ctx.VelocityMS < 1f ) )
		{
			return 0f;
		}

		AdvanceNoise( MathF.Max( 1f, _v[ Frequency ] ), ctx.DeltaMilliseconds );

		var speedFactor = MathZ.Saturate( ctx.VelocityMS / 20f );

		return _current * _v[ Strength ] * speedFactor;
	}

	private void AdvanceNoise( float frequency, float deltaMilliseconds )
	{
		var periodMs = 1000f / frequency;

		_phaseMs += deltaMilliseconds;

		if ( _phaseMs >= periodMs )
		{
			_phaseMs -= periodMs;

			// xorshift32 -> pseudo-random value in [-1, 1]
			_rng ^= _rng << 13;
			_rng ^= _rng >> 17;
			_rng ^= _rng << 5;

			_current = ( _rng / (float) uint.MaxValue ) * 2f - 1f;
		}
	}
}

/// <summary>
/// Slip-scaled pseudo-random rumble (a generator on the normalized vibration bus). Same band-limited noise as
/// <see cref="RoadTextureModule"/> but scaled by <c>ctx.SkidSlip</c> instead of speed; silent off-track.
/// </summary>
public sealed class SlipTextureModule : FFBModule
{
	private const int Strength = 1;
	private const int Frequency = 2;

	private const uint InitialSeed = 0x89ABCDEFu;

	private uint _rng = InitialSeed;
	private float _phaseMs;
	private float _current;

	public override void Reset()
	{
		_rng = InitialSeed;
		_phaseMs = 0f;
		_current = 0f;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.IsOnTrack || ( ctx.SkidSlip <= 0f ) )
		{
			return 0f;
		}

		var periodMs = 1000f / MathF.Max( 1f, _v[ Frequency ] );

		_phaseMs += ctx.DeltaMilliseconds;

		if ( _phaseMs >= periodMs )
		{
			_phaseMs -= periodMs;

			_rng ^= _rng << 13;
			_rng ^= _rng >> 17;
			_rng ^= _rng << 5;

			_current = ( _rng / (float) uint.MaxValue ) * 2f - 1f;
		}

		return _current * _v[ Strength ] * MathZ.Saturate( ctx.SkidSlip );
	}
}

/// <summary>
/// Tiny high-frequency dither added while the signal magnitude is below <c>Threshold</c> — alternates sign each
/// tick to break static friction on gear-driven wheels near center. Above the threshold it passes through.
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
