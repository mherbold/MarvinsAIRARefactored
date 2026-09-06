
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

	// steering-effects values

	public float UndersteerEffect { get; set; }
	public float OversteerEffect { get; set; }
	public float SeatOfPantsEffect { get; set; }
	public float SkidSlip { get; set; }

	// drivetrain / telemetry

	public float RPM { get; set; }
	public float ShiftRPM { get; set; }
	public float RedlineRPM { get; set; }
	public int Gear { get; set; }
	public int NumForwardGears { get; set; }
	public bool ABSActive { get; set; }

	// motion

	public float VelocityMS { get; set; }
	public float VelocityY { get; set; }
	public float SteeringWheelAngle { get; set; }
	public float SteeringWheelAngleMax { get; set; }
	public float SteeringWheelVelocity { get; set; }
	public float YawRate { get; set; }     // rad/s, true 360 Hz (YawRate_ST) — recorded (v4) for the PredictionLab telemetry audit; no FFB module consumes it yet

	// track position (meters past the start/finish line)

	public float TrackPosition { get; set; }

	// track map generation — the car-relative velocity vector (VelocityX forward, VelocityY above) is rotated by
	// YawNorth into the world frame and integrated to build the recorded segment's polyline; LapDist
	// (TrackPosition) anchors the segment along the lap and corrects integration drift

	public float YawNorth { get; set; }    // car heading relative to true north (radians)
	public float VelocityX { get; set; }   // car-relative forward velocity (m/s)
	public float Speed { get; set; }       // GPS vehicle speed (m/s)

	// prediction-audit extras (v4, see MarvinsAIRARefactored.PredictionLab) — true 360 Hz chassis/road
	// channels plus the 60 Hz driver inputs; no FFB module or replay context consumes these yet

	public float VelocityY360Hz { get; set; }     // m/s (VelocityY_ST) — the VelocityY column above stays 60 Hz for replay fidelity
	public float LatAccel { get; set; }           // m/s^2 including gravity (LatAccel_ST)
	public float RollRate { get; set; }           // rad/s (RollRate_ST)
	public float PitchRate { get; set; }          // rad/s (PitchRate_ST)
	public float LFShockVelocity { get; set; }    // m/s (LFshockVel_ST)
	public float RFShockVelocity { get; set; }    // m/s (RFshockVel_ST)
	public float LFShockDeflection { get; set; }  // m (LFshockDefl_ST)
	public float RFShockDeflection { get; set; }  // m (RFshockDefl_ST)
	public float Throttle { get; set; }           // 0..1 (60 Hz driver input)
	public float Brake { get; set; }              // 0..1 (60 Hz driver input)
}
