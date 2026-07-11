
namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Per-tick auxiliary input for the FFB graph engine. Built once per 360 Hz tick (no allocation) and
/// passed by readonly reference into every module's PrePass/Process. Everything a module might need from
/// the outside world (torque samples, telemetry, wheel state, protection pulses) lives here so the modules
/// themselves stay free of App/Simulator references and remain testable in isolation.
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

	// torque sources (Nm)

	public readonly float Torque60Hz;   // predicted 60 Hz sample (RacingWheel runs the RLS predictors)
	public readonly float Torque360Hz;  // Hermite-interpolated "500 Hz" sample
	public readonly float MaxForce;      // RacingWheelMaxForce (Nm) — normalization divisor

	// audio / LFE

	public readonly float LFEMagnitude;

	// wheel hardware state (from DirectInput)

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

	// one-tick protection pulses (rising edge drives the protection modules' timers)

	public readonly bool CrashProtectionTriggered;
	public readonly bool CurbProtectionTriggered;

	/// <summary>True only for the preview replay's neutral context. Modules whose behavior depends on live
	/// telemetry that is zeroed here (e.g. SpeedGain's velocity) can substitute a representative value so the
	/// preview shows something meaningful instead of the zero-telemetry edge case.</summary>
	public readonly bool IsPreview;

	public FFBTickContext(
		float deltaMilliseconds,
		float torque60Hz,
		float torque360Hz,
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
		bool crashProtectionTriggered,
		bool curbProtectionTriggered,
		bool isPreview = false )
	{
		DeltaMilliseconds = deltaMilliseconds;
		Torque60Hz = torque60Hz;
		Torque360Hz = torque360Hz;
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
		CrashProtectionTriggered = crashProtectionTriggered;
		CurbProtectionTriggered = curbProtectionTriggered;
		IsPreview = isPreview;
	}

	/// <summary>The preview replay's fixed tick length — recordings are captured at 360 Hz.</summary>
	public const float ReplayDeltaMilliseconds = 1000f / 360f;

	/// <summary>
	/// The preview replay context: one recorded 360 Hz sample expanded back into a full tick context, so every
	/// module — effects, generators, and protections included — behaves as it did when the recording was made.
	/// Recordings only run while on track with live torque data, so those two flags are hard-wired true. The
	/// crash/curb trigger pulses are passed in because the caller re-derives them from the recorded raw telemetry
	/// (G forces, peak shock velocity) against the protection modules' CURRENT thresholds.
	/// </summary>
	public static FFBTickContext FromRecording( Classes.RecordingData recordingData, float maxForce, bool crashProtectionTriggered, bool curbProtectionTriggered )
	{
		return new FFBTickContext(
			deltaMilliseconds: ReplayDeltaMilliseconds,
			torque60Hz: recordingData.InputTorque60Hz,
			torque360Hz: recordingData.InputTorque500Hz,
			maxForce: maxForce,
			lfeMagnitude: recordingData.LFEMagnitude,
			wheelPosition: recordingData.WheelPosition,
			wheelVelocity: recordingData.WheelVelocity,
			understeerEffect: recordingData.UndersteerEffect,
			oversteerEffect: recordingData.OversteerEffect,
			seatOfPantsEffect: recordingData.SeatOfPantsEffect,
			skidSlip: recordingData.SkidSlip,
			rpm: recordingData.RPM,
			shiftRPM: recordingData.ShiftRPM,
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
			crashProtectionTriggered: crashProtectionTriggered,
			curbProtectionTriggered: curbProtectionTriggered,
			isPreview: true );
	}
}
