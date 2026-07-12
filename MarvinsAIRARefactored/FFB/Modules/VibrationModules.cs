
using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.FFB.Modules;

// Vibration generators (IsGenerator, no signal inputs). Their output goes to the engine's normalized
// vibration bus, not the main chain — matching the old code where vibrations were summed into a separate
// vibrationTorque that bypassed the output curve. Under the neutral preview/parity context UsingTorqueData is
// false and every generator produces 0. Waveform math mirrors the old inline RacingWheel vibration code
// verbatim, including the per-effect sawtooth sign conventions.

/// <summary>Understeer wheel vibration (old 732–792).</summary>
public sealed class UndersteerVibrationModule : FFBModule
{
	private const int Pattern = 1;
	private const int Strength = 2;
	private const int MinimumFrequency = 3;
	private const int MaximumFrequency = 4;
	private const int Curve = 5;

	private float _minFrequency;
	private float _maxFrequency;
	private float _minPeriodMS;
	private float _maxPeriodMS;
	private float _curvePower;

	private float _timerMS;

	public override void Reset() => _timerMS = 0f;

	protected override void OnValuesChanged()
	{
		_minFrequency = MathF.Max( 0.01f, _v[ MinimumFrequency ] );
		_maxFrequency = MathF.Max( 0.01f, _v[ MaximumFrequency ] );
		_minPeriodMS = 1000f / _minFrequency;
		_maxPeriodMS = 1000f / _maxFrequency;
		_curvePower = MathZ.CurveToPower( _v[ Curve ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.UsingTorqueData || ( ctx.UndersteerEffect <= 0f ) )
		{
			return 0f;
		}

		var isUndersteering = ( ctx.UndersteerEffect == 1f );

		var frequency = isUndersteering ? _maxFrequency : _minFrequency;

		var timeInSeconds = _timerMS * 0.001f;

		var effectTorque = (RacingWheel.VibrationPattern) (int) _v[ Pattern ] switch
		{
			RacingWheel.VibrationPattern.SineWave => MathF.Sin( timeInSeconds * MathF.Tau * frequency ),
			RacingWheel.VibrationPattern.SquareWave => ( MathF.Sin( timeInSeconds * MathF.Tau * frequency ) >= 0f ) ? 1f : -1f,
			RacingWheel.VibrationPattern.TriangleWave => 4f * MathF.Abs( ( timeInSeconds * frequency ) % 1f - 0.5f ) - 1f,
			RacingWheel.VibrationPattern.SawtoothWaveIn => ( 1f - ( timeInSeconds * frequency ) % 1f ) * MathF.Sign( ctx.SteeringWheelAngle ),
			RacingWheel.VibrationPattern.SawtoothWaveOut => ( 1f - ( timeInSeconds * frequency ) % 1f ) * -MathF.Sign( ctx.SteeringWheelAngle ),
			_ => 0f
		};

		_timerMS += ctx.DeltaMilliseconds;

		var periodMS = isUndersteering ? _maxPeriodMS : _minPeriodMS;

		if ( _timerMS >= periodMS )
		{
			_timerMS -= periodMS * MathF.Floor( _timerMS / periodMS );
		}

		return effectTorque * _v[ Strength ] * MathF.Pow( ctx.UndersteerEffect, _curvePower );
	}
}

/// <summary>Oversteer wheel vibration (old 796–856).</summary>
public sealed class OversteerVibrationModule : FFBModule
{
	private const int Pattern = 1;
	private const int Strength = 2;
	private const int MinimumFrequency = 3;
	private const int MaximumFrequency = 4;
	private const int Curve = 5;

	private float _minFrequency;
	private float _maxFrequency;
	private float _minPeriodMS;
	private float _maxPeriodMS;
	private float _curvePower;

	private float _timerMS;

	public override void Reset() => _timerMS = 0f;

	protected override void OnValuesChanged()
	{
		_minFrequency = MathF.Max( 0.01f, _v[ MinimumFrequency ] );
		_maxFrequency = MathF.Max( 0.01f, _v[ MaximumFrequency ] );
		_minPeriodMS = 1000f / _minFrequency;
		_maxPeriodMS = 1000f / _maxFrequency;
		_curvePower = MathZ.CurveToPower( _v[ Curve ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.UsingTorqueData || ( ctx.OversteerEffect <= 0f ) )
		{
			return 0f;
		}

		var isOversteering = ( ctx.OversteerEffect == 1f );

		var frequency = isOversteering ? _maxFrequency : _minFrequency;

		var timeInSeconds = _timerMS * 0.001f;

		var effectTorque = (RacingWheel.VibrationPattern) (int) _v[ Pattern ] switch
		{
			RacingWheel.VibrationPattern.SineWave => MathF.Sin( timeInSeconds * MathF.Tau * frequency ),
			RacingWheel.VibrationPattern.SquareWave => ( MathF.Sin( timeInSeconds * MathF.Tau * frequency ) >= 0f ) ? 1f : -1f,
			RacingWheel.VibrationPattern.TriangleWave => 4f * MathF.Abs( ( timeInSeconds * frequency ) % 1f - 0.5f ) - 1f,
			RacingWheel.VibrationPattern.SawtoothWaveIn => ( ( timeInSeconds * frequency ) % 1f - 1f ) * -MathF.Sign( ctx.SteeringWheelAngle ),
			RacingWheel.VibrationPattern.SawtoothWaveOut => ( 1f - ( timeInSeconds * frequency ) % 1f ) * -MathF.Sign( ctx.SteeringWheelAngle ),
			_ => 0f
		};

		_timerMS += ctx.DeltaMilliseconds;

		var periodMS = isOversteering ? _maxPeriodMS : _minPeriodMS;

		if ( _timerMS >= periodMS )
		{
			_timerMS -= periodMS * MathF.Floor( _timerMS / periodMS );
		}

		return effectTorque * _v[ Strength ] * MathF.Pow( ctx.OversteerEffect, _curvePower );
	}
}

/// <summary>Seat-of-pants wheel vibration (old 860–922). Uses the absolute (signed) effect magnitude.</summary>
public sealed class SeatOfPantsVibrationModule : FFBModule
{
	private const int Pattern = 1;
	private const int Strength = 2;
	private const int MinimumFrequency = 3;
	private const int MaximumFrequency = 4;
	private const int Curve = 5;

	private float _minFrequency;
	private float _maxFrequency;
	private float _minPeriodMS;
	private float _maxPeriodMS;
	private float _curvePower;

	private float _timerMS;

	public override void Reset() => _timerMS = 0f;

	protected override void OnValuesChanged()
	{
		_minFrequency = MathF.Max( 0.01f, _v[ MinimumFrequency ] );
		_maxFrequency = MathF.Max( 0.01f, _v[ MaximumFrequency ] );
		_minPeriodMS = 1000f / _minFrequency;
		_maxPeriodMS = 1000f / _maxFrequency;
		_curvePower = MathZ.CurveToPower( _v[ Curve ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.UsingTorqueData || ( ctx.SeatOfPantsEffect == 0f ) )
		{
			return 0f;
		}

		var absSeatOfPantsEffect = MathF.Abs( ctx.SeatOfPantsEffect );

		var isAtMax = ( absSeatOfPantsEffect == 1f );

		var frequency = isAtMax ? _maxFrequency : _minFrequency;

		var timeInSeconds = _timerMS * 0.001f;

		var effectTorque = (RacingWheel.VibrationPattern) (int) _v[ Pattern ] switch
		{
			RacingWheel.VibrationPattern.SineWave => MathF.Sin( timeInSeconds * MathF.Tau * frequency ),
			RacingWheel.VibrationPattern.SquareWave => ( MathF.Sin( timeInSeconds * MathF.Tau * frequency ) >= 0f ) ? 1f : -1f,
			RacingWheel.VibrationPattern.TriangleWave => 4f * MathF.Abs( ( timeInSeconds * frequency ) % 1f - 0.5f ) - 1f,
			RacingWheel.VibrationPattern.SawtoothWaveIn => ( ( timeInSeconds * frequency ) % 1f - 1f ) * -MathF.Sign( ctx.SteeringWheelAngle ),
			RacingWheel.VibrationPattern.SawtoothWaveOut => ( 1f - ( timeInSeconds * frequency ) % 1f ) * -MathF.Sign( ctx.SteeringWheelAngle ),
			_ => 0f
		};

		_timerMS += ctx.DeltaMilliseconds;

		var periodMS = isAtMax ? _maxPeriodMS : _minPeriodMS;

		if ( _timerMS >= periodMS )
		{
			_timerMS -= periodMS * MathF.Floor( _timerMS / periodMS );
		}

		return effectTorque * _v[ Strength ] * MathF.Pow( absSeatOfPantsEffect, _curvePower );
	}
}

/// <summary>Shift-RPM vibration (old 926–956): 40 Hz square gated by a 6 Hz pulse when at/above shift RPM.</summary>
public sealed class ShiftRPMVibrationModule : FFBModule
{
	private const int Strength = 1;

	private float _timerMS;

	public override void Reset() => _timerMS = 0f;

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var strength = _v[ Strength ];

		if ( !ctx.UsingTorqueData || ( strength <= 0f ) )
		{
			return 0f;
		}

		if ( ( ctx.RPM >= ctx.ShiftRPM ) && ( ctx.NumForwardGears > 0 ) && ( ctx.Gear < ctx.NumForwardGears ) )
		{
			const float frequency = 40f;
			const float pulseFrequency = 6f;

			var timeInSeconds = _timerMS * 0.001f;

			var result = 0f;

			if ( MathF.Sin( timeInSeconds * MathF.Tau * pulseFrequency ) >= 0f )
			{
				result = ( MathF.Sin( timeInSeconds * MathF.Tau * frequency ) >= 0f ) ? strength : -strength;
			}

			_timerMS += ctx.DeltaMilliseconds;

			const float periodMS = 500f;

			if ( _timerMS >= periodMS )
			{
				_timerMS -= periodMS * MathF.Floor( _timerMS / periodMS );
			}

			return result;
		}

		_timerMS = 0f;

		return 0f;
	}
}

/// <summary>Gear-change vibration (old 960–982): a 100 ms 40 Hz square burst on any non-neutral gear change.</summary>
public sealed class GearChangeVibrationModule : FFBModule
{
	private const int Strength = 1;

	private float _timerMS;
	private int _lastGear;

	public override void Reset()
	{
		_timerMS = 0f;
		_lastGear = 0;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var strength = _v[ Strength ];

		if ( !ctx.UsingTorqueData || ( strength <= 0f ) )
		{
			return 0f;
		}

		if ( ctx.Gear != _lastGear )
		{
			if ( ctx.Gear != 0 )
			{
				_timerMS = 100f;
			}

			_lastGear = ctx.Gear;
		}

		if ( _timerMS > 0f )
		{
			var sine = MathF.Sin( ( _timerMS * 0.001f ) * MathF.Tau * 40f );

			var result = ( sine >= 0f ) ? strength : -strength;

			_timerMS -= ctx.DeltaMilliseconds;

			return result;
		}

		return 0f;
	}
}

/// <summary>ABS vibration (old 986–1005): a 50 Hz triangle while the brake ABS is active.</summary>
public sealed class ABSVibrationModule : FFBModule
{
	private const int Strength = 1;

	private float _timerMS;

	public override void Reset() => _timerMS = 0f;

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var strength = _v[ Strength ];

		if ( !ctx.UsingTorqueData || ( strength <= 0f ) )
		{
			return 0f;
		}

		if ( ctx.ABSActive )
		{
			const float frequency = 50f;

			var phase = ( ( _timerMS * 0.001f ) * frequency ) % 1f;

			var result = strength * ( 4f * MathF.Abs( phase - 0.5f ) - 1f );

			const float periodMS = 1000f / frequency;

			if ( _timerMS >= periodMS )
			{
				_timerMS -= periodMS * MathF.Floor( _timerMS / periodMS );
			}

			_timerMS += ctx.DeltaMilliseconds;

			return result;
		}

		return 0f;
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
	private float _periodMs;
	private float _phaseMs;
	private float _current;

	public override void Reset()
	{
		_rng = InitialSeed;
		_phaseMs = 0f;
		_current = 0f;
	}

	protected override void OnValuesChanged()
	{
		_periodMs = 1000f / MathF.Max( 1f, _v[ Frequency ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.IsOnTrack || ( ctx.VelocityMS < 1f ) )
		{
			return 0f;
		}

		AdvanceNoise( ctx.DeltaMilliseconds );

		var speedFactor = MathZ.Saturate( ctx.VelocityMS / 20f );

		return _current * _v[ Strength ] * speedFactor;
	}

	private void AdvanceNoise( float deltaMilliseconds )
	{
		_phaseMs += deltaMilliseconds;

		if ( _phaseMs >= _periodMs )
		{
			_phaseMs -= _periodMs;

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
	private float _periodMs;
	private float _phaseMs;
	private float _current;

	public override void Reset()
	{
		_rng = InitialSeed;
		_phaseMs = 0f;
		_current = 0f;
	}

	protected override void OnValuesChanged()
	{
		_periodMs = 1000f / MathF.Max( 1f, _v[ Frequency ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.IsOnTrack || ( ctx.SkidSlip <= 0f ) )
		{
			return 0f;
		}

		_phaseMs += ctx.DeltaMilliseconds;

		if ( _phaseMs >= _periodMs )
		{
			_phaseMs -= _periodMs;

			_rng ^= _rng << 13;
			_rng ^= _rng >> 17;
			_rng ^= _rng << 5;

			_current = ( _rng / (float) uint.MaxValue ) * 2f - 1f;
		}

		return _current * _v[ Strength ] * MathZ.Saturate( ctx.SkidSlip );
	}
}
