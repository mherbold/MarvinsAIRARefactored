
using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.FFB.Modules;

// Vibration generators (IsGenerator, no signal inputs). Their output goes to the engine's normalized
// vibration bus, not the main chain — matching the old code where vibrations were summed into a separate
// vibrationTorque that bypassed the output curve. Under the neutral preview/parity context UsingTorqueData is
// false and every generator produces 0. Waveform math mirrors the old inline RacingWheel vibration code
// verbatim, including the per-effect sawtooth sign conventions.

/// <summary>
/// The steering-effect values (understeer/oversteer/seat-of-pants) update at 60 Hz and hold for the frame's six
/// sub-ticks, which stairsteps anything using them at 360 Hz — the vibration amplitude envelopes here and the
/// constant-force effects in EffectModules.cs. This ramps the previous frame's value toward the current frame's
/// across the sub-ticks — the same scheme as <see cref="InterpolatorModule"/>, at the cost of one 60 Hz frame
/// (~16.7 ms) of latency on the effect value.
/// </summary>
internal struct EffectInterpolator
{
	private float _previousFrameValue;
	private float _currentFrameValue;

	public void Reset()
	{
		_previousFrameValue = 0f;
		_currentFrameValue = 0f;
	}

	public float Interpolate( float value, int sampleIndex )
	{
		if ( sampleIndex == 0 )
		{
			_previousFrameValue = _currentFrameValue;
			_currentFrameValue = value;
		}

		var fraction = ( sampleIndex + 1f ) / FFBTickContext.SamplesPerFrame;

		return MathZ.Lerp( _previousFrameValue, _currentFrameValue, fraction );
	}
}

/// <summary>Understeer wheel vibration (old 732–792).</summary>
public sealed class UndersteerVibrationModule : FFBModule
{
	private const int Pattern = 1;
	private const int Strength = 2;
	private const int Frequency = 3;
	private const int Curve = 4;

	private float _frequency;
	private float _periodMS;
	private float _curvePower;

	private float _timerMS;
	private EffectInterpolator _effectInterpolator;

	public override void Reset()
	{
		_timerMS = 0f;
		_effectInterpolator.Reset();
	}

	protected override void OnValuesChanged()
	{
		_frequency = MathF.Max( 0.01f, _v[ Frequency ] );
		_periodMS = 1000f / _frequency;
		_curvePower = MathZ.CurveToPower( _v[ Curve ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.UsingTorqueData )
		{
			_timerMS = 0f;

			_effectInterpolator.Reset();

			return 0f;
		}

		// the 60 Hz-held effect value is interpolated across the frame's sub-ticks so the amplitude envelope
		// doesn't stairstep at 360 Hz; the interpolator runs every tick so ramps in and out of zero stay smooth
		var understeerEffect = _effectInterpolator.Interpolate( ctx.UndersteerEffect, ctx.SampleIndex );

		if ( understeerEffect <= 0f )
		{
			_timerMS = 0f;   // the effect restarts at 0 phase the next time it fires

			return 0f;
		}

		var timeInSeconds = _timerMS * 0.001f;

		var effectTorque = (RacingWheel.VibrationPattern) (int) _v[ Pattern ] switch
		{
			RacingWheel.VibrationPattern.SineWave => MathF.Sin( timeInSeconds * MathF.Tau * _frequency ),
			RacingWheel.VibrationPattern.SquareWave => ( MathF.Sin( timeInSeconds * MathF.Tau * _frequency ) >= 0f ) ? 1f : -1f,
			RacingWheel.VibrationPattern.TriangleWave => 4f * MathF.Abs( ( timeInSeconds * _frequency ) % 1f - 0.5f ) - 1f,
			RacingWheel.VibrationPattern.SawtoothWaveIn => ( 1f - ( timeInSeconds * _frequency ) % 1f ) * MathF.Sign( ctx.SteeringWheelAngle ),
			RacingWheel.VibrationPattern.SawtoothWaveOut => ( 1f - ( timeInSeconds * _frequency ) % 1f ) * -MathF.Sign( ctx.SteeringWheelAngle ),
			_ => 0f
		};

		_timerMS += ctx.DeltaMilliseconds;

		if ( _timerMS >= _periodMS )
		{
			_timerMS -= _periodMS * MathF.Floor( _timerMS / _periodMS );
		}

		return effectTorque * _v[ Strength ] * MathF.Pow( understeerEffect, _curvePower );
	}
}

/// <summary>Oversteer wheel vibration (old 796–856).</summary>
public sealed class OversteerVibrationModule : FFBModule
{
	private const int Pattern = 1;
	private const int Strength = 2;
	private const int Frequency = 3;
	private const int Curve = 4;

	private float _frequency;
	private float _periodMS;
	private float _curvePower;

	private float _timerMS;
	private EffectInterpolator _effectInterpolator;

	public override void Reset()
	{
		_timerMS = 0f;
		_effectInterpolator.Reset();
	}

	protected override void OnValuesChanged()
	{
		_frequency = MathF.Max( 0.01f, _v[ Frequency ] );
		_periodMS = 1000f / _frequency;
		_curvePower = MathZ.CurveToPower( _v[ Curve ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.UsingTorqueData )
		{
			_timerMS = 0f;

			_effectInterpolator.Reset();

			return 0f;
		}

		// the 60 Hz-held effect value is interpolated across the frame's sub-ticks so the amplitude envelope
		// doesn't stairstep at 360 Hz; the interpolator runs every tick so ramps in and out of zero stay smooth
		var oversteerEffect = _effectInterpolator.Interpolate( ctx.OversteerEffect, ctx.SampleIndex );

		if ( oversteerEffect <= 0f )
		{
			_timerMS = 0f;   // the effect restarts at 0 phase the next time it fires

			return 0f;
		}

		var timeInSeconds = _timerMS * 0.001f;

		var effectTorque = (RacingWheel.VibrationPattern) (int) _v[ Pattern ] switch
		{
			RacingWheel.VibrationPattern.SineWave => MathF.Sin( timeInSeconds * MathF.Tau * _frequency ),
			RacingWheel.VibrationPattern.SquareWave => ( MathF.Sin( timeInSeconds * MathF.Tau * _frequency ) >= 0f ) ? 1f : -1f,
			RacingWheel.VibrationPattern.TriangleWave => 4f * MathF.Abs( ( timeInSeconds * _frequency ) % 1f - 0.5f ) - 1f,
			RacingWheel.VibrationPattern.SawtoothWaveIn => ( ( timeInSeconds * _frequency ) % 1f - 1f ) * -MathF.Sign( ctx.SteeringWheelAngle ),
			RacingWheel.VibrationPattern.SawtoothWaveOut => ( 1f - ( timeInSeconds * _frequency ) % 1f ) * -MathF.Sign( ctx.SteeringWheelAngle ),
			_ => 0f
		};

		_timerMS += ctx.DeltaMilliseconds;

		if ( _timerMS >= _periodMS )
		{
			_timerMS -= _periodMS * MathF.Floor( _timerMS / _periodMS );
		}

		return effectTorque * _v[ Strength ] * MathF.Pow( oversteerEffect, _curvePower );
	}
}

/// <summary>Seat-of-pants wheel vibration (old 860–922). Uses the absolute (signed) effect magnitude.</summary>
public sealed class SeatOfPantsVibrationModule : FFBModule
{
	private const int Pattern = 1;
	private const int Strength = 2;
	private const int Frequency = 3;
	private const int Curve = 4;

	private float _frequency;
	private float _periodMS;
	private float _curvePower;

	private float _timerMS;
	private EffectInterpolator _effectInterpolator;

	public override void Reset()
	{
		_timerMS = 0f;
		_effectInterpolator.Reset();
	}

	protected override void OnValuesChanged()
	{
		_frequency = MathF.Max( 0.01f, _v[ Frequency ] );
		_periodMS = 1000f / _frequency;
		_curvePower = MathZ.CurveToPower( _v[ Curve ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.UsingTorqueData )
		{
			_timerMS = 0f;

			_effectInterpolator.Reset();

			return 0f;
		}

		// the 60 Hz-held effect value is interpolated across the frame's sub-ticks so the amplitude envelope
		// doesn't stairstep at 360 Hz; the signed value is interpolated so sign flips sweep through zero
		var seatOfPantsEffect = _effectInterpolator.Interpolate( ctx.SeatOfPantsEffect, ctx.SampleIndex );

		if ( seatOfPantsEffect == 0f )
		{
			_timerMS = 0f;   // the effect restarts at 0 phase the next time it fires

			return 0f;
		}

		var absSeatOfPantsEffect = MathF.Abs( seatOfPantsEffect );

		var timeInSeconds = _timerMS * 0.001f;

		var effectTorque = (RacingWheel.VibrationPattern) (int) _v[ Pattern ] switch
		{
			RacingWheel.VibrationPattern.SineWave => MathF.Sin( timeInSeconds * MathF.Tau * _frequency ),
			RacingWheel.VibrationPattern.SquareWave => ( MathF.Sin( timeInSeconds * MathF.Tau * _frequency ) >= 0f ) ? 1f : -1f,
			RacingWheel.VibrationPattern.TriangleWave => 4f * MathF.Abs( ( timeInSeconds * _frequency ) % 1f - 0.5f ) - 1f,
			RacingWheel.VibrationPattern.SawtoothWaveIn => ( ( timeInSeconds * _frequency ) % 1f - 1f ) * -MathF.Sign( ctx.SteeringWheelAngle ),
			RacingWheel.VibrationPattern.SawtoothWaveOut => ( 1f - ( timeInSeconds * _frequency ) % 1f ) * -MathF.Sign( ctx.SteeringWheelAngle ),
			_ => 0f
		};

		_timerMS += ctx.DeltaMilliseconds;

		if ( _timerMS >= _periodMS )
		{
			_timerMS -= _periodMS * MathF.Floor( _timerMS / _periodMS );
		}

		return effectTorque * _v[ Strength ] * MathF.Pow( absSeatOfPantsEffect, _curvePower );
	}
}

/// <summary>Shift-RPM vibration (old 926–956): a square at <c>Frequency</c>, pulsed on/off every
/// <c>PulseDuration</c> milliseconds while at/above shift RPM.</summary>
public sealed class ShiftRPMVibrationModule : FFBModule
{
	private const int Strength = 1;
	private const int Frequency = 2;
	private const int PulseDuration = 3;

	private float _frequency;
	private float _pulseDurationMS;

	private float _wavePhase;
	private float _gateTimerMS;

	public override void Reset()
	{
		_wavePhase = 0f;
		_gateTimerMS = 0f;
	}

	protected override void OnValuesChanged()
	{
		_frequency = MathF.Max( 1f, _v[ Frequency ] );
		_pulseDurationMS = MathF.Max( 10f, _v[ PulseDuration ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var strength = _v[ Strength ];

		if ( !ctx.UsingTorqueData || ( strength <= 0f ) )
		{
			Reset();   // the effect restarts at 0 phase the next time it fires

			return 0f;
		}

		if ( ( ctx.RPM >= ctx.ShiftRPM ) && ( ctx.NumForwardGears > 0 ) && ( ctx.Gear < ctx.NumForwardGears ) )
		{
			// on for PulseDuration, off for PulseDuration — the square restarts at 0 phase with every pulse
			var result = ( _gateTimerMS < _pulseDurationMS ) ? ( ( _wavePhase < 0.5f ) ? strength : -strength ) : 0f;

			_wavePhase += _frequency * ctx.DeltaMilliseconds * 0.001f;
			_wavePhase -= MathF.Floor( _wavePhase );

			_gateTimerMS += ctx.DeltaMilliseconds;

			if ( _gateTimerMS >= _pulseDurationMS * 2f )
			{
				_gateTimerMS -= _pulseDurationMS * 2f;

				_wavePhase = 0f;   // a new pulse begins — restart the carrier at 0 phase
			}

			return result;
		}

		Reset();

		return 0f;
	}
}

/// <summary>Gear-change vibration (old 960–982): a 100 ms square burst at <c>Frequency</c> on any non-neutral
/// gear change.</summary>
public sealed class GearChangeVibrationModule : FFBModule
{
	private const int Strength = 1;
	private const int Frequency = 2;

	private const float BurstDurationMS = 100f;

	private float _frequency;

	private float _timerMS = BurstDurationMS;
	private int _lastGear;

	public override void Reset()
	{
		_timerMS = BurstDurationMS;
		_lastGear = 0;
	}

	protected override void OnValuesChanged()
	{
		_frequency = MathF.Max( 1f, _v[ Frequency ] );
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
				_timerMS = 0f;   // restart the burst — counting up from zero starts the square at 0 phase
			}

			_lastGear = ctx.Gear;
		}

		if ( _timerMS < BurstDurationMS )
		{
			var result = ( ( ( _timerMS * 0.001f * _frequency ) % 1f ) < 0.5f ) ? strength : -strength;

			_timerMS += ctx.DeltaMilliseconds;

			return result;
		}

		return 0f;
	}
}

/// <summary>ABS vibration (old 986–1005): a triangle at <c>Frequency</c>, pulsed on/off every
/// <c>PulseDuration</c> milliseconds while the brake ABS is active.</summary>
public sealed class ABSVibrationModule : FFBModule
{
	private const int Strength = 1;
	private const int Frequency = 2;
	private const int PulseDuration = 3;

	private float _frequency;
	private float _pulseDurationMS;

	private float _wavePhase;
	private float _gateTimerMS;

	public override void Reset()
	{
		_wavePhase = 0f;
		_gateTimerMS = 0f;
	}

	protected override void OnValuesChanged()
	{
		_frequency = MathF.Max( 1f, _v[ Frequency ] );
		_pulseDurationMS = MathF.Max( 10f, _v[ PulseDuration ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var strength = _v[ Strength ];

		if ( !ctx.UsingTorqueData || ( strength <= 0f ) )
		{
			Reset();   // the effect restarts at 0 phase the next time it fires

			return 0f;
		}

		if ( ctx.ABSActive )
		{
			// on for PulseDuration, off for PulseDuration — the triangle restarts at 0 phase with every pulse
			var result = ( _gateTimerMS < _pulseDurationMS ) ? strength * ( 4f * MathF.Abs( _wavePhase - 0.5f ) - 1f ) : 0f;

			_wavePhase += _frequency * ctx.DeltaMilliseconds * 0.001f;
			_wavePhase -= MathF.Floor( _wavePhase );

			_gateTimerMS += ctx.DeltaMilliseconds;

			if ( _gateTimerMS >= _pulseDurationMS * 2f )
			{
				_gateTimerMS -= _pulseDurationMS * 2f;

				_wavePhase = 0f;   // a new pulse begins — restart the carrier at 0 phase
			}

			return result;
		}

		Reset();

		return 0f;
	}
}

/// <summary>
/// Speed-scaled pseudo-random rumble (a generator on the normalized vibration bus). Holds a band-limited noise
/// value updated at a speed-scaled rate (bumps arrive faster the faster you drive, reaching the full
/// <c>Frequency</c> setting at 180 MPH) and scales it by strength and a speed factor; silent when parked or
/// off-track.
/// </summary>
public sealed class RoadTextureModule : FFBModule
{
	private const int Strength = 1;
	private const int Frequency = 2;

	// the noise update rate ramps linearly with speed, hitting the full Frequency setting at 180 MPH
	private const float FullFrequencySpeedMS = 180f * 0.44704f;

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
			// the noise clock restarts at 0 phase (and silence) the next time the effect fires
			_phaseMs = 0f;
			_current = 0f;

			return 0f;
		}

		// advancing the noise clock at a speed-scaled rate modulates the effective update frequency —
		// slow chunky bumps at low speed, the full Frequency setting once the ramp tops out
		var frequencyFactor = MathZ.Saturate( ctx.VelocityMS / FullFrequencySpeedMS );

		AdvanceNoise( ctx.DeltaMilliseconds * frequencyFactor );

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
			// the noise clock restarts at 0 phase (and silence) the next time the effect fires
			_phaseMs = 0f;
			_current = 0f;

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
