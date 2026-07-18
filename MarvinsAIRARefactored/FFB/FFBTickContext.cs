
namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// The six raw 360 Hz torque samples (Nm) of the current 60 Hz telemetry frame, as one inline value (no
/// allocation, copied into every tick context). iRacing delivers the whole frame at once, so at any sub-tick
/// the frame's LATER samples are already known — the prediction module exploits this to anchor its
/// extrapolation at the newest sample instead of the current one, cutting the depth it has to guess.
/// </summary>
[System.Runtime.CompilerServices.InlineArray( FFBTickContext.SamplesPerFrame )]
public struct FFBTorqueFrame
{
	private float _element0;
}

/// <summary>
/// Per-tick auxiliary input for the FFB graph engine. Built once per 360 Hz tick (no allocation) by the
/// telemetry-thread frame burst and passed by readonly reference into every module's PrePass/Process.
/// Everything a module might need from the outside world (torque samples, telemetry, wheel state,
/// protection pulses) lives here so the modules themselves stay free of App/Simulator references and
/// remain testable in isolation.
/// </summary>
/// <remarks>
/// Torque samples are in Newton-metres (the main signal bus is Nm until the Output module). The vibration
/// bus is normalized. In the preview path the context is rebuilt per sample from a recording via
/// <see cref="FromRecording"/>, so effect and generator modules see the telemetry they saw live and render
/// working effects in the preview; only the crash/curb trigger pulses are re-derived at replay time from the
/// recorded raw telemetry against the protection modules' current thresholds.
/// </remarks>
public readonly struct FFBTickContext
{
	// timing

	public readonly float DeltaMilliseconds;

	// 0-based index of this tick within its 60 Hz telemetry frame (0..SamplesPerFrame-1). Lets a module that
	// resamples a 60 Hz-carried signal (the 60 Hz interpolator) know where it sits in the frame so it can ramp
	// across the six sub-ticks.
	public readonly int SampleIndex;

	// torque sources (Nm)

	public readonly float Torque60Hz;   // raw 60 Hz sample (the frame's newest ST sample)
	public readonly float Torque360Hz;  // raw 360 Hz ST sample for this tick
	public readonly FFBTorqueFrame TorqueFrame; // all six raw 360 Hz ST samples of this frame (see FFBTorqueFrame)
	public readonly float MaxForce;      // RacingWheelMaxForce (Nm) — normalization divisor

	// audio / LFE

	public readonly float LFEMagnitude;

	// wheel state derived from the sim's steering telemetry, normalized to the car's half-lock (see
	// RacingWheel.FrameContext — 0 when off-car); the preview replay re-derives them from the recorded
	// SteeringWheelAngle/AngleMax/Velocity the same way

	public readonly float WheelPosition;
	public readonly float WheelVelocity;

	// steering-effects values (computed by SteeringEffects, consumed by modules)

	public readonly float UndersteerEffect;
	public readonly float OversteerEffect;
	public readonly float SeatOfPantsEffect;
	public readonly float SkidSlip;

	// drivetrain / telemetry

	public readonly float RPM;
	public readonly float ShiftRPM;
	public readonly float RedlineRPM;
	public readonly bool EngineRunning;   // from the EngineWarnings telemetry (stalled flag clear) — the raw RPM floors at ~300 with the engine off, so it can't be used as an engine-off test
	public readonly int Gear;
	public readonly int NumForwardGears;
	public readonly bool ABSActive;
	public readonly bool IsOnTrack;
	public readonly bool UsingTorqueData;

	// motion

	public readonly float VelocityMS;
	public readonly float VelocityY;
	public readonly float SteeringWheelAngle;
	public readonly float SteeringWheelAngleMax;
	public readonly float SteeringWheelVelocity;   // rad/s from 60 Hz telemetry (rotation-range independent); positive = counterclockwise
	public readonly float PitchRate;               // rad/s — the frame's NEWEST 360 Hz sample (PitchRate_ST); Prediction module aux input

	// one-tick protection pulses (rising edge drives the protection modules' timers)

	public readonly bool CrashProtectionTriggered;
	public readonly bool CurbProtectionTriggered;

	/// <summary>True only for the preview replay's neutral context. Modules whose behavior depends on live
	/// telemetry that is zeroed here (e.g. SpeedGain's velocity) can substitute a representative value so the
	/// preview shows something meaningful instead of the zero-telemetry edge case.</summary>
	public readonly bool IsPreview;

	public FFBTickContext(
		float deltaMilliseconds,
		int sampleIndex,
		float torque60Hz,
		float torque360Hz,
		in FFBTorqueFrame torqueFrame,
		float maxForce,
		float lfeMagnitude,
		float wheelPosition,
		float wheelVelocity,
		float understeerEffect,
		float oversteerEffect,
		float seatOfPantsEffect,
		float skidSlip,
		float rpm,
		float shiftRPM,
		float redlineRPM,
		bool engineRunning,
		int gear,
		int numForwardGears,
		bool absActive,
		bool isOnTrack,
		bool usingTorqueData,
		float velocityMS,
		float velocityY,
		float steeringWheelAngle,
		float steeringWheelAngleMax,
		float steeringWheelVelocity,
		float pitchRate,
		bool crashProtectionTriggered,
		bool curbProtectionTriggered,
		bool isPreview = false )
	{
		DeltaMilliseconds = deltaMilliseconds;
		SampleIndex = sampleIndex;
		Torque60Hz = torque60Hz;
		Torque360Hz = torque360Hz;
		TorqueFrame = torqueFrame;
		MaxForce = maxForce;
		LFEMagnitude = lfeMagnitude;
		WheelPosition = wheelPosition;
		WheelVelocity = wheelVelocity;
		UndersteerEffect = understeerEffect;
		OversteerEffect = oversteerEffect;
		SeatOfPantsEffect = seatOfPantsEffect;
		SkidSlip = skidSlip;
		RPM = rpm;
		ShiftRPM = shiftRPM;
		RedlineRPM = redlineRPM;
		EngineRunning = engineRunning;
		Gear = gear;
		NumForwardGears = numForwardGears;
		ABSActive = absActive;
		IsOnTrack = isOnTrack;
		UsingTorqueData = usingTorqueData;
		VelocityMS = velocityMS;
		VelocityY = velocityY;
		SteeringWheelAngle = steeringWheelAngle;
		SteeringWheelAngleMax = steeringWheelAngleMax;
		SteeringWheelVelocity = steeringWheelVelocity;
		PitchRate = pitchRate;
		CrashProtectionTriggered = crashProtectionTriggered;
		CurbProtectionTriggered = curbProtectionTriggered;
		IsPreview = isPreview;
	}

	/// <summary>The fixed 360 Hz tick length — used by both the live frame burst and the preview replay
	/// (recordings are captured at 360 Hz).</summary>
	public const float TickDeltaMilliseconds = 1000f / 360f;

	/// <summary>360 Hz ticks per 60 Hz telemetry frame (the burst length, and the 60→360 expansion ratio the
	/// interpolator ramps across). Mirrors <see cref="Components.Simulator.SamplesPerFrame360Hz"/>.</summary>
	public const int SamplesPerFrame = 6;

	/// <summary>
	/// The preview replay context: one recorded 360 Hz sample expanded back into a full tick context, so every
	/// module — effects, generators, and protections included — behaves as it did when the recording was made.
	/// Recordings only run while on track with live torque data, so those two flags are hard-wired true. The
	/// crash/curb trigger pulses are passed in because the caller re-derives them from the recorded raw telemetry
	/// (G forces, peak shock velocity) against the protection modules' CURRENT thresholds. The torque frame and
	/// the frame's newest pitch-rate sample are passed in because the caller reassembles them from the
	/// recording's six samples around this one (matching what the live FrameContext carries).
	/// </summary>
	public static FFBTickContext FromRecording( Classes.RecordingData recordingData, in FFBTorqueFrame torqueFrame, float framePitchRate, float maxForce, bool crashProtectionTriggered, bool curbProtectionTriggered, int sampleIndex )
	{
		// half-lock-normalized wheel state, derived exactly like the live FrameContext derives it
		var halfLock = recordingData.SteeringWheelAngleMax * 0.5f;

		var wheelPosition = ( halfLock > 0f ) ? recordingData.SteeringWheelAngle / halfLock : 0f;
		var wheelVelocity = ( halfLock > 0f ) ? recordingData.SteeringWheelVelocity / halfLock : 0f;

		// the steering effects page's per-effect enable switches gate the recorded effect values the same way
		// the live FrameContext gates them, so the preview reflects the toggles too
		var settings = DataContext.DataContext.Instance.Settings;

		return new FFBTickContext(
			deltaMilliseconds: TickDeltaMilliseconds,
			sampleIndex: sampleIndex,
			torque60Hz: recordingData.InputTorque60Hz,
			torque360Hz: recordingData.InputTorque360Hz,
			torqueFrame: in torqueFrame,
			maxForce: maxForce,
			lfeMagnitude: recordingData.LFEMagnitude,
			wheelPosition: wheelPosition,
			wheelVelocity: wheelVelocity,
			understeerEffect: settings.SteeringEffectsUndersteerEnabled ? recordingData.UndersteerEffect : 0f,
			oversteerEffect: settings.SteeringEffectsOversteerEnabled ? recordingData.OversteerEffect : 0f,
			seatOfPantsEffect: settings.SteeringEffectsSeatOfPantsEnabled ? recordingData.SeatOfPantsEffect : 0f,
			skidSlip: recordingData.SkidSlip,
			rpm: recordingData.RPM,
			shiftRPM: recordingData.ShiftRPM,
			redlineRPM: recordingData.RedlineRPM,
			engineRunning: true,   // recordings don't carry the stalled flag; they're captured while driving, so hard-wire running (like isOnTrack)
			gear: recordingData.Gear,
			numForwardGears: recordingData.NumForwardGears,
			absActive: recordingData.ABSActive,
			isOnTrack: true,
			usingTorqueData: true,
			velocityMS: recordingData.VelocityMS,
			velocityY: recordingData.VelocityY,
			steeringWheelAngle: recordingData.SteeringWheelAngle,
			steeringWheelAngleMax: recordingData.SteeringWheelAngleMax,
			steeringWheelVelocity: recordingData.SteeringWheelVelocity,
			pitchRate: framePitchRate,
			crashProtectionTriggered: crashProtectionTriggered,
			curbProtectionTriggered: curbProtectionTriggered,
			isPreview: true );
	}
}
