
using System.Runtime.InteropServices;

namespace MarvinsAIRARefactored.GameBridges.Ac;

// Byte-exact transcription of Assetto Corsa's shared memory pages (the Kunos "assetto_corsa_shared_memory"
// layout that acs.exe writes to Local\acpmf_physics, Local\acpmf_graphics and Local\acpmf_static). AC's C++
// structs use default packing, which for these float/int/wchar fields matches Pack=4, so a faithful
// field-by-field transcription reproduces the in-memory layout (same approach that worked for rF2/LMU).
//
// Arrays and wchar strings are represented as [InlineArray] value types (see InlineArrays.cs) instead of
// [MarshalAs] fields, keeping the structs blittable so the bridge can read them from the shared memory bytes
// with MemoryMarshal.Read and ZERO heap allocations - the physics page is read at 360 Hz on the multimedia
// timer worker thread. The byte layout is identical to the marshaled representation.

public static class AcConstants
{
	public const string PhysicsMapName = "Local\\acpmf_physics";
	public const string GraphicsMapName = "Local\\acpmf_graphics";
	public const string StaticMapName = "Local\\acpmf_static";

	// generous upper bounds for the view accessors / capture scratch buffers (the real pages are smaller, but
	// later AC / AC EVO revisions append fields, so we allocate room and only read the offsets we understand)
	public const int PhysicsMapSize = 2048;
	public const int GraphicsMapSize = 4096;
	public const int StaticMapSize = 2048;
}

public enum AcBufferType
{
	Physics = 0,
	Graphics = 1,
	Static = 2
}

public enum AcStatus
{
	Off = 0,
	Replay = 1,
	Live = 2,
	Pause = 3
}

public enum AcSessionType
{
	Unknown = -1,
	Practice = 0,
	Qualify = 1,
	Race = 2,
	Hotlap = 3,
	TimeAttack = 4,
	Drift = 5,
	Drag = 6,
	HotStint = 7,
	HotStintSuperPole = 8
}

[StructLayout( LayoutKind.Sequential, Pack = 4 )]
public struct AcPhysics
{
	public int packetId;                 // 0
	public float gas;                    // 4    0..1
	public float brake;                  // 8    0..1
	public float fuel;                   // 12   liters
	public int gear;                     // 16   0=reverse, 1=neutral, 2=first gear ...
	public int rpms;                     // 20
	public float steerAngle;             // 24   steering input
	public float speedKmh;               // 28
	public FloatArray3 velocity;         // 32   world velocity m/s
	public FloatArray3 accG;             // 44   acceleration in G (local frame)
	public FloatArray4 wheelSlip;        // 56
	public FloatArray4 wheelLoad;        // 72   (unused in AC)
	public FloatArray4 wheelsPressure;   // 88
	public FloatArray4 wheelAngularSpeed;// 104
	public FloatArray4 tyreWear;         // 120  (unused in AC)
	public FloatArray4 tyreDirtyLevel;   // 136  (unused in AC)
	public FloatArray4 tyreCoreTemperature; // 152
	public FloatArray4 camberRAD;        // 168  (unused in AC)
	public FloatArray4 suspensionTravel; // 184  meters
	public float drs;                    // 200  (unused in AC)
	public float tc;                     // 204
	public float heading;                // 208  radians
	public float pitch;                  // 212  radians
	public float roll;                   // 216  radians
	public float cgHeight;               // 220  (unused in AC)
	public FloatArray5 carDamage;        // 224
	public int numberOfTyresOut;         // 244
	public int pitLimiterOn;             // 248
	public float abs;                    // 252
	public float kersCharge;             // 256
	public float kersInput;              // 260
	public int autoShifterOn;            // 264
	public FloatArray2 rideHeight;       // 268
	public float turboBoost;             // 276
	public float ballast;                // 280
	public float airDensity;             // 284
	public float airTemp;                // 288
	public float roadTemp;               // 292
	public FloatArray3 localAngularVel;  // 296  rad/s (local frame)
	public float finalFF;                // 308  the game's final force feedback signal (-1..1)
	public float performanceMeter;       // 312
	public int engineBrake;              // 316
	public int ersRecoveryLevel;         // 320
	public int ersPowerLevel;            // 324
	public int ersHeatCharging;          // 328
	public int ersIsCharging;            // 332
	public float kersCurrentKJ;          // 336
	public int drsAvailable;             // 340
	public int drsEnabled;               // 344
	public FloatArray4 brakeTemp;        // 348
	public float clutch;                 // 364  1=fully engaged (pedal up), 0=fully pressed
	public FloatArray4 tyreTempI;        // 368
	public FloatArray4 tyreTempM;        // 384
	public FloatArray4 tyreTempO;        // 400
	public int isAIControlled;           // 416
	public FloatArray12 tyreContactPoint;   // 420 [4][3]
	public FloatArray12 tyreContactNormal;  // 468 [4][3]
	public FloatArray12 tyreContactHeading; // 516 [4][3]
	public float brakeBias;              // 564
	public FloatArray3 localVelocity;    // 568  body-frame velocity m/s
	// end of the Assetto Corsa (vanilla) physics page = 580 bytes; ACC / AC EVO append fields beyond here
}

[StructLayout( LayoutKind.Sequential, Pack = 4 )]
public struct AcGraphics
{
	public int packetId;                 // 0
	public int status;                   // 4  AcStatus
	public int session;                  // 8  AcSessionType
	public CharArray15 currentTime;      // 12  wchar[15]
	public CharArray15 lastTime;         // 42
	public CharArray15 bestTime;         // 72
	public CharArray15 split;            // 102
	public int completedLaps;            // 132
	public int position;                 // 136
	public int iCurrentTime;             // 140
	public int iLastTime;                // 144
	public int iBestTime;                // 148
	public float sessionTimeLeft;        // 152
	public float distanceTraveled;       // 156
	public int isInPit;                  // 160
	public int currentSectorIndex;       // 164
	public int lastSectorTime;           // 168
	public int numberOfLaps;             // 172
	public CharArray33 tyreCompound;     // 176  wchar[33]
	public float replayTimeMultiplier;   // 244 (242 padded to 4)
	public float normalizedCarPosition;  // 248
	// AC vanilla continues with multi-car arrays (activeCars / carCoordinates / carID) that we don't need yet
}

[StructLayout( LayoutKind.Sequential, Pack = 4 )]
public struct AcStatic
{
	public CharArray15 smVersion;        // 0  wchar[15]
	public CharArray15 acVersion;        // 30
	public int numberOfSessions;         // 60
	public int numCars;                  // 64
	public CharArray33 carModel;         // 68  wchar[33]
	public CharArray33 track;            // 134
	public CharArray33 playerName;       // 200
	public CharArray33 playerSurname;    // 266
	public CharArray33 playerNick;       // 332
	public int sectorCount;              // (padded to 400)
	public float maxTorque;
	public float maxPower;
	public int maxRpm;
	public float maxFuel;
	public FloatArray4 suspensionMaxTravel;
	public FloatArray4 tyreRadius;
	public float maxTurboBoost;
	public float deprecated1;
	public float deprecated2;
	public int penaltiesEnabled;
	public float aidFuelRate;
	public float aidTireRate;
	public float aidMechanicalDamage;
	public int aidAllowTyreBlankets;
	public float aidStability;
	public int aidAutoClutch;
	public int aidAutoBlip;
	public int hasDRS;
	public int hasERS;
	public int hasKERS;
	public float kersMaxJ;
	public int engineBrakeSettingsCount;
	public int ersPowerControllerCount;
	public float trackSPlineLength;      // track length in meters
	public CharArray33 trackConfiguration;
}
