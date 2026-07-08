
namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Per-tick auxiliary input for the FFB stack engine. Built once per 360 Hz tick (no allocation) and
/// passed by readonly reference into every module's PrePass/Process. Everything a module might need from
/// the outside world (torque samples, telemetry, wheel state, protection pulses) lives here so the modules
/// themselves stay free of App/Simulator references and remain testable in isolation.
/// </summary>
/// <remarks>
/// Torque samples are in Newton-metres (the main signal bus is Nm until the Output module). The vibration
/// bus is normalized. In the preview / parity path the context is "neutral": real MaxForce and torque
/// samples, but all telemetry-derived aux values are zero and the protection pulses are false, so every
/// effect and generator module is inert (exactly as the old preview passed curbProtectionLerpFactor = 0
/// and only showed the algorithm).
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
	public readonly float ParkedFactor;
	public readonly float SteeringWheelAngle;
	public readonly float SteeringWheelAngleMax;

	// one-tick protection pulses (rising edge drives the protection modules' timers)

	public readonly bool CrashProtectionTriggered;
	public readonly bool CurbProtectionTriggered;

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
		float parkedFactor,
		float steeringWheelAngle,
		float steeringWheelAngleMax,
		bool crashProtectionTriggered,
		bool curbProtectionTriggered )
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
		ParkedFactor = parkedFactor;
		SteeringWheelAngle = steeringWheelAngle;
		SteeringWheelAngleMax = steeringWheelAngleMax;
		CrashProtectionTriggered = crashProtectionTriggered;
		CurbProtectionTriggered = curbProtectionTriggered;
	}

	/// <summary>
	/// A "neutral" context used by the preview / parity path: real MaxForce and torque samples, but all
	/// aux telemetry zero and no protection pulses, so effect and generator modules produce nothing and
	/// only the algorithm (DSP) chain contributes — matching the old preview exactly.
	/// </summary>
	public static FFBTickContext Neutral( float deltaMilliseconds, float torque60Hz, float torque360Hz, float maxForce )
	{
		return new FFBTickContext(
			deltaMilliseconds: deltaMilliseconds,
			torque60Hz: torque60Hz,
			torque360Hz: torque360Hz,
			maxForce: maxForce,
			lfeMagnitude: 0f,
			wheelPosition: 0f,
			wheelVelocity: 0f,
			understeerEffect: 0f,
			oversteerEffect: 0f,
			seatOfPantsEffect: 0f,
			skidSlip: 0f,
			rpm: 0f,
			shiftRPM: 0f,
			gear: 0,
			numForwardGears: 0,
			absActive: false,
			isOnTrack: false,
			usingTorqueData: false,
			velocityMS: 0f,
			velocityY: 0f,
			parkedFactor: 0f,
			steeringWheelAngle: 0f,
			steeringWheelAngleMax: 0f,
			crashProtectionTriggered: false,
			curbProtectionTriggered: false );
	}
}
