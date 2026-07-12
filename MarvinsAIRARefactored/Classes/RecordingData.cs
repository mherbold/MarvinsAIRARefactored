
namespace MarvinsAIRARefactored.Classes;

/// <summary>
/// One 360 Hz sample of a recording. Beyond the two torque samples this captures every external signal the
/// FFB graph modules consume, so the editor preview can replay a recording through the whole graph — effects,
/// generators, and protections included — not just the algorithm chain. The crash/curb protection triggers are
/// deliberately NOT recorded as booleans; instead the raw telemetry behind them (G forces, shock velocity) is
/// recorded so the preview re-derives the triggers from the user's CURRENT protection module settings.
/// Recordings from unversioned/older formats are rejected at load (see Recording.FormatVersion).
/// </summary>
public class RecordingData
{
	// torque samples (Nm)

	public float InputTorque60Hz { get; set; }
	public float InputTorque360Hz { get; set; }

	// audio / LFE

	public float LFEMagnitude { get; set; }

	// raw protection telemetry (preview re-derives the crash/curb trigger pulses from these)

	public float LongitudinalGForce { get; set; }
	public float LateralGForce { get; set; }
	public float MaxShockVelocity { get; set; }

	// wheel hardware state (from DirectInput)

	public float WheelPosition { get; set; }
	public float WheelVelocity { get; set; }

	// steering-effects values

	public float UndersteerEffect { get; set; }
	public float OversteerEffect { get; set; }
	public float SeatOfPantsEffect { get; set; }
	public float SkidSlip { get; set; }

	// drivetrain / telemetry

	public float RPM { get; set; }
	public float ShiftRPM { get; set; }
	public int Gear { get; set; }
	public int NumForwardGears { get; set; }
	public bool ABSActive { get; set; }

	// motion

	public float VelocityMS { get; set; }
	public float VelocityY { get; set; }
	public float SteeringWheelAngle { get; set; }
	public float SteeringWheelAngleMax { get; set; }
	public float SteeringWheelVelocity { get; set; }
}
