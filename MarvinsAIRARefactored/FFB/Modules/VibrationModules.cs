
using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.FFB.Modules;

// Vibration generators (IsGenerator, no signal inputs). Their output goes to the engine's normalized
// vibration bus, not the main chain — matching the old code where vibrations were summed into a separate
// vibrationTorque that bypassed the output curve. Under the neutral preview/parity context UsingTorqueData is
// false and every generator produces 0. Waveform math mirrors the old inline RacingWheel vibration code
// verbatim, including the per-effect sawtooth sign conventions. Strength settings are stored in physical Nm
// at the wheel (portable across wheel force changes and wheelbases); dividing by ctx.WheelForce turns them
// into the normalized bus amplitude the waveform math expects.

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
		// doesn't stairstep at 360 Hz; the interpolator runs every tick so ramps in and out of zero stay smooth.
		// The editor's test toggle plays the effect as if the understeer effect were pegged at 1
		var understeerEffect = _effectInterpolator.Interpolate( TestActive ? 1f : ctx.UndersteerEffect, ctx.SampleIndex );

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

		return effectTorque * ( _v[ Strength ] / ctx.WheelForce ) * MathF.Pow( understeerEffect, _curvePower );
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
		// doesn't stairstep at 360 Hz; the interpolator runs every tick so ramps in and out of zero stay smooth.
		// The editor's test toggle plays the effect as if the oversteer effect were pegged at 1
		var oversteerEffect = _effectInterpolator.Interpolate( TestActive ? 1f : ctx.OversteerEffect, ctx.SampleIndex );

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

		return effectTorque * ( _v[ Strength ] / ctx.WheelForce ) * MathF.Pow( oversteerEffect, _curvePower );
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
		// doesn't stairstep at 360 Hz; the signed value is interpolated so sign flips sweep through zero.
		// The editor's test toggle plays the effect as if the seat-of-pants effect were pegged at 1
		var seatOfPantsEffect = _effectInterpolator.Interpolate( TestActive ? 1f : ctx.SeatOfPantsEffect, ctx.SampleIndex );

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

		return effectTorque * ( _v[ Strength ] / ctx.WheelForce ) * MathF.Pow( absSeatOfPantsEffect, _curvePower );
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
		var strength = _v[ Strength ] / ctx.WheelForce;

		if ( !ctx.UsingTorqueData || ( strength <= 0f ) )
		{
			Reset();   // the effect restarts at 0 phase the next time it fires

			return 0f;
		}

		// the editor's test toggle plays the effect as if the engine were at/above shift RPM in a shiftable gear
		if ( TestActive || ( ( ctx.RPM >= ctx.ShiftRPM ) && ( ctx.NumForwardGears > 0 ) && ( ctx.Gear < ctx.NumForwardGears ) ) )
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
/// gear change. The trigger is edge-detected against the last OBSERVED gear, so every path where the module
/// isn't watching (module disabled — Process never runs — or no torque data / zero strength) either keeps the
/// tracker current or forgets it; re-activating the effect never replays a gear change that happened while it
/// was silenced.</summary>
public sealed class GearChangeVibrationModule : FFBModule
{
	private const int Strength = 1;
	private const int Frequency = 2;

	private const float BurstDurationMS = 100f;

	// "haven't seen a gear yet" — the first gear observed after (re)starting is latched without firing
	private const int UnknownGear = int.MinValue;

	private float _frequency;

	private float _timerMS = BurstDurationMS;
	private int _lastGear = UnknownGear;
	private bool _wasEnabled;
	private bool _wasTestActive;

	public override void Reset()
	{
		_timerMS = BurstDurationMS;
		_lastGear = UnknownGear;
	}

	protected override void OnValuesChanged()
	{
		_frequency = MathF.Max( 1f, _v[ Frequency ] );

		// while the module is disabled Process never runs and gear changes go unobserved — forget the stale
		// gear when the user re-enables it instead of firing a burst for a change we never saw
		if ( Enabled && !_wasEnabled )
		{
			Reset();
		}

		_wasEnabled = Enabled;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var strength = _v[ Strength ] / ctx.WheelForce;

		if ( !ctx.UsingTorqueData || ( strength <= 0f ) )
		{
			// keep tracking the gear (and cancel any running burst) so raising the strength or torque data
			// coming back can't fire the effect for a change that happened while it was silenced
			_lastGear = ctx.Gear;
			_timerMS = BurstDurationMS;
			_wasTestActive = TestActive;

			return 0f;
		}

		// the editor's test toggle plays one burst exactly like a real gear change (rising edge only — the
		// editor releases the toggle itself right after; the burst finishes on its own once started)
		if ( TestActive && !_wasTestActive )
		{
			_timerMS = 0f;
		}

		_wasTestActive = TestActive;

		if ( ctx.Gear != _lastGear )
		{
			if ( ( ctx.Gear != 0 ) && ( _lastGear != UnknownGear ) )
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
		var strength = _v[ Strength ] / ctx.WheelForce;

		if ( !ctx.UsingTorqueData || ( strength <= 0f ) )
		{
			Reset();   // the effect restarts at 0 phase the next time it fires

			return 0f;
		}

		// the editor's test toggle plays the effect as if the brake ABS were engaged
		if ( ctx.ABSActive || TestActive )
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
/// Engine RPM vibration, voiced like a V8 rather than a pure tone. The fundamental frequency tracks the
/// engine — ramping linearly from <c>MinimumFrequency</c> just above 0 RPM up to
/// <c>FrequencyAtRedlineRPM</c> at the car's redline (and proportionally beyond it when over-revving);
/// silent/off while the engine is stalled (the telemetry EngineRunning flag). Each waveform cycle is treated as one firing event, and three things
/// roughen it into an engine: harmonic content on top of the fundamental (a firing is a thump, not a
/// tone — the 2nd/3rd harmonics are dropped near the 360 Hz Nyquist so high settings don't alias),
/// per-firing random amplitude and rate jitter (no two combustion events are identical), and a fixed
/// unevenness pattern repeating every 8 firings (the crossplane-V8-style lope). The Roughness knob scales
/// all three ingredients: 0% is the original pure sine, 100% is the full V8 voice. At full roughness
/// typical peaks land at Strength and the hardest firings overshoot it by up to ~50%. Restarts at 0 phase
/// and a deterministic jitter seed whenever the effect (re)starts, so the preview replays identically.
/// </summary>
public sealed class EngineRPMVibrationModule : FFBModule
{
	private const int Strength = 1;
	private const int FrequencyAtRedlineRPM = 2;
	private const int Roughness = 3;

	// the vibration floor as soon as the engine is turning at all (the knob's 10 Hz minimum keeps the ramp
	// from ever sloping downward)
	private const float MinimumFrequency = 10f;


	// harmonics above this alias against the 360 Hz tick rate, so they fade out of the mix near it
	private const float MaximumHarmonicFrequency = 170f;

	// firing-to-firing unevenness, repeating every 8 firings — the V8 lope
	private static readonly float[] LopePattern = [ 0.30f, -0.10f, 0.12f, -0.28f, 0.22f, -0.18f, 0.05f, -0.13f ];

	private const uint InitialSeed = 0x2468ACEu;

	// the editor's test toggle sweeps the engine 1000 RPM -> redline -> 1000 RPM on this triangle loop
	private const float TestSweepPeriodMS = 10000f;
	private const float TestSweepFloorRPM = 1000f;

	private uint _rng = InitialSeed;
	private float _wavePhase;
	private int _fireIndex;
	private float _fireAmplitude = 1f;
	private float _fireRate = 1f;
	private float _testSweepTimerMS;

	public override void Reset()
	{
		_rng = InitialSeed;
		_wavePhase = 0f;
		_fireIndex = 0;
		_fireAmplitude = 1f;
		_fireRate = 1f;
		_testSweepTimerMS = 0f;
	}

	// xorshift32, mapped to 0..1 (same generator family as the texture modules)
	private float NextRandom()
	{
		_rng ^= _rng << 13;
		_rng ^= _rng >> 17;
		_rng ^= _rng << 5;

		return ( _rng & 0xFFFFFF ) / 16777216f;
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		var strength = _v[ Strength ] / ctx.WheelForce;

		// EngineRunning (the telemetry stalled flag) is the engine-off test — the raw RPM is useless for it
		// because iRacing floors the reported RPM at ~300 even with the engine dead. The editor's test toggle
		// ignores the engine state (it substitutes its own RPM), but still needs a valid redline to sweep to
		if ( !ctx.UsingTorqueData || ( strength <= 0f ) || ( ctx.RedlineRPM <= 0f ) || ( !TestActive && ( !ctx.EngineRunning || ( ctx.RPM <= 0f ) ) ) )
		{
			Reset();   // the effect restarts at 0 phase the next time it fires

			return 0f;
		}

		float rpm;

		if ( TestActive )
		{
			// triangle sweep: floor at phase 0/1, redline at phase 0.5 — one full loop every 10 seconds
			var sweepPhase = _testSweepTimerMS / TestSweepPeriodMS;

			rpm = TestSweepFloorRPM + ( ctx.RedlineRPM - TestSweepFloorRPM ) * ( 1f - MathF.Abs( 2f * sweepPhase - 1f ) );

			_testSweepTimerMS += ctx.DeltaMilliseconds;

			if ( _testSweepTimerMS >= TestSweepPeriodMS )
			{
				_testSweepTimerMS -= TestSweepPeriodMS;
			}
		}
		else
		{
			rpm = ctx.RPM;

			_testSweepTimerMS = 0f;   // the sweep restarts from the floor the next time the test fires
		}

		var roughness = _v[ Roughness ];

		var frequency = ( MinimumFrequency + ( _v[ FrequencyAtRedlineRPM ] - MinimumFrequency ) * rpm / ctx.RedlineRPM ) * _fireRate;

		// one cycle = one firing: fundamental plus 2nd/3rd harmonics turn the sine into a thump; harmonics
		// that would land past the Nyquist guard are left out (their peak-normalization terms drop with them)
		var phaseRadians = _wavePhase * MathF.Tau;

		var waveform = MathF.Sin( phaseRadians );
		var peakNormalizer = 1f;

		if ( frequency * 2f < MaximumHarmonicFrequency )
		{
			waveform += 0.35f * roughness * MathF.Sin( 2f * phaseRadians );
			peakNormalizer += 0.35f * roughness;
		}

		if ( frequency * 3f < MaximumHarmonicFrequency )
		{
			waveform += 0.2f * roughness * MathF.Sin( 3f * phaseRadians );
			peakNormalizer += 0.2f * roughness;
		}

		var result = waveform / peakNormalizer * _fireAmplitude * strength;

		_wavePhase += frequency * ctx.DeltaMilliseconds * 0.001f;

		if ( _wavePhase >= 1f )
		{
			_wavePhase -= MathF.Floor( _wavePhase );

			// a new firing begins: apply the lope pattern plus random combustion variation to its amplitude,
			// and jitter its rate a little so the rumble never locks into a perfect tone — all scaled by
			// Roughness (0 = every firing identical, i.e. a pure tone)
			_fireIndex = ( _fireIndex + 1 ) & 7;
			_fireAmplitude = ( 1f + roughness * LopePattern[ _fireIndex ] ) * ( 1f + roughness * ( 0.3f * NextRandom() - 0.15f ) );
			_fireRate = 1f + roughness * ( 0.1f * NextRandom() - 0.05f );
		}

		return result;
	}
}

/// <summary>
/// Speed-scaled pseudo-random rumble (a generator on the normalized vibration bus). Holds a band-limited noise
/// value updated at a speed-scaled rate (bumps arrive faster the faster you drive, rising steeply off the
/// line and reaching the full <c>Frequency</c> setting at 180 MPH) and scales it by strength and a speed
/// factor; silent when parked or off-track.
/// </summary>
public sealed class RoadTextureModule : FFBModule
{
	private const int Strength = 1;
	private const int Frequency = 2;

	// the speed at which the noise update rate reaches the full Frequency setting; the ramp is a cube-root
	// curve, not linear — most of the frequency arrives quickly (~55% by 30 MPH, ~70% by 60 MPH) and the
	// rest builds gradually to the top
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
		// the editor's test toggle plays the effect as if the car were moving at 100 MPH
		var velocityMS = TestActive ? 100f * MathZ.MPHToMPS : ctx.VelocityMS;

		if ( !ctx.IsOnTrack || ( velocityMS < 1f ) )
		{
			// the noise clock restarts at 0 phase (and silence) the next time the effect fires
			_phaseMs = 0f;
			_current = 0f;

			return 0f;
		}

		// advancing the noise clock at a speed-scaled rate modulates the effective update frequency —
		// slow chunky bumps at low speed, the full Frequency setting once the ramp tops out; the cube root
		// bends the ramp so the frequency rises steeply at low speed instead of linearly
		var frequencyFactor = MathF.Cbrt( MathZ.Saturate( velocityMS / FullFrequencySpeedMS ) );

		AdvanceNoise( ctx.DeltaMilliseconds * frequencyFactor );

		var speedFactor = MathZ.Saturate( velocityMS / 20f );

		return _current * ( _v[ Strength ] / ctx.WheelForce ) * speedFactor;
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
/// <see cref="RoadTextureModule"/> but its amplitude is driven by the sum of the understeer and oversteer
/// effect values (saturated at 1), so the wheel rumbles whenever either end of the car is sliding; silent
/// off-track. The per-effect enable switches on the steering effects page gate the summed values upstream,
/// and the 60 Hz-held values are interpolated across the sub-ticks like the other steering-effect modules.
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
	private EffectInterpolator _effectInterpolator;

	public override void Reset()
	{
		_rng = InitialSeed;
		_phaseMs = 0f;
		_current = 0f;
		_effectInterpolator.Reset();
	}

	protected override void OnValuesChanged()
	{
		_periodMs = 1000f / MathF.Max( 1f, _v[ Frequency ] );
	}

	public override float Process( in FFBTickContext ctx, float inputA, float inputB )
	{
		if ( !ctx.IsOnTrack )
		{
			Reset();

			return 0f;
		}

		// the 60 Hz-held effect values are interpolated across the frame's sub-ticks so the amplitude
		// envelope doesn't stairstep at 360 Hz; the interpolator runs every tick so ramps stay smooth.
		// The editor's test toggle plays the effect as if the summed slip effects were pegged at 1
		var slipEffect = _effectInterpolator.Interpolate( TestActive ? 1f : ( ctx.UndersteerEffect + ctx.OversteerEffect ), ctx.SampleIndex );

		if ( slipEffect <= 0f )
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

		return _current * ( _v[ Strength ] / ctx.WheelForce ) * MathZ.Saturate( slipEffect );
	}
}
