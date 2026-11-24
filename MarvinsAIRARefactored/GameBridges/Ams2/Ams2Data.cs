
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MarvinsAIRARefactored.GameBridges.Ams2;

// Byte-exact transcription of the Automobilista 2 shared memory block (SHARED_MEMORY_VERSION 14 - the header
// ships with the game in Support\SharedMemory\AMS2_SharedMemoryExampleApp\SharedMemory.h). AMS2 publishes
// the "Project CARS 2" layout under the "$pcars2$" file mapping when Shared Memory is set to "Project CARS 2"
// in the game's system options. The C++ struct uses default packing: bools are 1 byte, everything else is
// 4-byte aligned, so a field-by-field transcription with Pack=4 (bools as bytes) reproduces the layout - the
// reference size is 20700 bytes with the participant block at 100 bytes each (verified against a ctypes model
// of the header; Ams2Constants.StructSize is checked against that figure when the bridge starts).
//
// As with the other bridges the arrays are [InlineArray] value types so the struct stays blittable and is read
// out of the byte buffer with MemoryMarshal.Read and zero heap allocations on the playout timer worker thread.

public static class Ams2Constants
{
	public const string MapName = "$pcars2$";

	public const int SharedMemoryVersion = 14;

	public const int ExpectedStructSize = 20700;

	public const int StringLengthMax = 64;
	public const int StoredParticipantsMax = 64;
	public const int TyreCompoundNameLengthMax = 40;
	public const int TyreMax = 4;

	public static readonly int StructSize = Unsafe.SizeOf<Ams2SharedMemory>();

	// byte offset of mSequenceNumber inside the block - the live data provider re-reads it after copying the
	// block to detect a torn read (the game increments it before and after every write, so it is odd mid-write)
	public static readonly int SequenceNumberOffset = ComputeSequenceNumberOffset();

	private static int ComputeSequenceNumberOffset()
	{
		var block = default( Ams2SharedMemory );

		return (int) Unsafe.ByteOffset( ref Unsafe.As<Ams2SharedMemory, byte>( ref block ), ref Unsafe.As<uint, byte>( ref block.mSequenceNumber ) );
	}
}

public enum Ams2GameState : uint
{
	Exited = 0,
	FrontEnd = 1,
	InGamePlaying = 2,
	InGamePaused = 3,
	InGameInMenuTimeTicking = 4,
	InGameRestarting = 5,
	InGameReplay = 6,
	FrontEndReplay = 7
}

public enum Ams2SessionState : uint
{
	Invalid = 0,
	Practice = 1,
	Test = 2,
	Qualify = 3,
	FormationLap = 4,
	Race = 5,
	TimeAttack = 6
}

public enum Ams2RaceState : uint
{
	Invalid = 0,
	NotStarted = 1,
	Racing = 2,
	Finished = 3,
	Disqualified = 4,
	Retired = 5,
	Dnf = 6
}

public enum Ams2FlagColour : uint
{
	None = 0,
	Green = 1,
	Blue = 2,
	WhiteSlowCar = 3,
	WhiteFinalLap = 4,
	Red = 5,
	Yellow = 6,
	DoubleYellow = 7,
	BlackAndWhite = 8,
	BlackOrangeCircle = 9,
	Black = 10,
	Chequered = 11
}

public enum Ams2PitMode : uint
{
	None = 0,
	DrivingIntoPits = 1,
	InPit = 2,
	DrivingOutOfPits = 3,
	InGarage = 4,
	DrivingOutOfGarage = 5
}

public enum Ams2Terrain : uint
{
	Road = 0,
	LowGripRoad = 1,
	BumpyRoad1 = 2,
	BumpyRoad2 = 3,
	BumpyRoad3 = 4,
	Marbles = 5,
	GrassyBerms = 6,
	Grass = 7,
	Gravel = 8,
	BumpyGravel = 9,
	RumbleStrips = 10,
	Drains = 11,
	TyreWalls = 12,
	CementWalls = 13,
	GuardRails = 14,
	Sand = 15,
	BumpySand = 16,
	Dirt = 17,
	BumpyDirt = 18,
	DirtRoad = 19,
	BumpyDirtRoad = 20,
	Pavement = 21,
	DirtBank = 22,
	Wood = 23,
	DryVerge = 24,
	ExitRumbleStrips = 25,
	Grasscrete = 26,
	LongGrass = 27,
	SlopeGrass = 28,
	Cobbles = 29,
	SandRoad = 30,
	BakedClay = 31,
	Astroturf = 32,
	SnowHalf = 33,
	SnowFull = 34,
	DamagedRoad1 = 35,
	TrainTrackRoad = 36,
	BumpyCobbles = 37,
	AriesOnly = 38,
	OrionOnly = 39,
	B1Rumbles = 40,
	B2Rumbles = 41,
	RoughSandMedium = 42,
	RoughSandHeavy = 43,
	SnowWalls = 44,
	IceRoad = 45,
	RunoffRoad = 46,
	IllegalStrip = 47,
	PaintConcrete = 48,
	PaintConcreteIllegal = 49,
	RallyTarmac = 50
}

[Flags]
public enum Ams2CarFlags : uint
{
	Headlight = 1 << 0,
	EngineActive = 1 << 1,
	EngineWarning = 1 << 2,
	SpeedLimiter = 1 << 3,
	Abs = 1 << 4,
	Handbrake = 1 << 5,
	Tcs = 1 << 6,
	Scs = 1 << 7
}

[InlineArray( 40 )] public struct ByteArray40 { private byte _element0; }
[InlineArray( 4 )] public struct UIntArray4 { private uint _element0; }
[InlineArray( 64 )] public struct UIntArray64 { private uint _element0; }
[InlineArray( 64 )] public struct FloatArray64 { private float _element0; }
[InlineArray( 64 )] public struct Ams2ParticipantArray { private Ams2Participant _element0; }
[InlineArray( 64 )] public struct Ams2NameArray { private ByteArray64 _element0; }
[InlineArray( 64 )] public struct Ams2Vec3Array { private FloatArray3 _element0; }
[InlineArray( 4 )] public struct Ams2CompoundArray { private ByteArray40 _element0; }

[StructLayout( LayoutKind.Sequential, Pack = 4 )]
public struct Ams2Participant
{
	public byte mIsActive;                  // 0    bool
	public ByteArray64 mName;               // 1    char[64]
	public FloatArray3 mWorldPosition;      // 68   (3 bytes of padding before)
	public float mCurrentLapDistance;       // 80   metres
	public uint mRacePosition;              // 84   1 = leader, 0 = unset
	public uint mLapsCompleted;             // 88
	public uint mCurrentLap;                // 92
	public int mCurrentSector;              // 96   -1 = unset
}                                           // 100 bytes

[StructLayout( LayoutKind.Sequential, Pack = 4 )]
public struct Ams2SharedMemory
{
	public uint mVersion;                                   // 0
	public uint mBuildVersionNumber;                        // 4
	public uint mGameState;                                 // 8    Ams2GameState
	public uint mSessionState;                              // 12   Ams2SessionState
	public uint mRaceState;                                 // 16   Ams2RaceState
	public int mViewedParticipantIndex;                     // 20
	public int mNumParticipants;                            // 24
	public Ams2ParticipantArray mParticipantInfo;           // 28   64 x 100 bytes
	public float mUnfilteredThrottle;                       // 6428
	public float mUnfilteredBrake;                          // 6432
	public float mUnfilteredSteering;                       // 6436
	public float mUnfilteredClutch;                         // 6440
	public ByteArray64 mCarName;                            // 6444
	public ByteArray64 mCarClassName;                       // 6508
	public uint mLapsInEvent;                               // 6572
	public ByteArray64 mTrackLocation;                      // 6576
	public ByteArray64 mTrackVariation;                     // 6640
	public float mTrackLength;                              // 6704 metres
	public int mNumSectors;                                 // 6708
	public byte mLapInvalidated;                            // 6712 bool
	public float mBestLapTime;                              // 6716 seconds, -1 = unset
	public float mLastLapTime;                              // 6720 seconds
	public float mCurrentTime;                              // 6724 seconds (current lap)
	public float mSplitTimeAhead;                           // 6728
	public float mSplitTimeBehind;                          // 6732
	public float mSplitTime;                                // 6736
	public float mEventTimeRemaining;                       // 6740 milliseconds, -1 = unset
	public float mPersonalFastestLapTime;                   // 6744
	public float mWorldFastestLapTime;                      // 6748
	public float mCurrentSector1Time;                       // 6752
	public float mCurrentSector2Time;                       // 6756
	public float mCurrentSector3Time;                       // 6760
	public float mFastestSector1Time;                       // 6764
	public float mFastestSector2Time;                       // 6768
	public float mFastestSector3Time;                       // 6772
	public float mPersonalFastestSector1Time;               // 6776
	public float mPersonalFastestSector2Time;               // 6780
	public float mPersonalFastestSector3Time;               // 6784
	public float mWorldFastestSector1Time;                  // 6788
	public float mWorldFastestSector2Time;                  // 6792
	public float mWorldFastestSector3Time;                  // 6796
	public uint mHighestFlagColour;                         // 6800 Ams2FlagColour
	public uint mHighestFlagReason;                         // 6804
	public uint mPitMode;                                   // 6808 Ams2PitMode
	public uint mPitSchedule;                               // 6812
	public uint mCarFlags;                                  // 6816 Ams2CarFlags
	public float mOilTempCelsius;                           // 6820
	public float mOilPressureKPa;                           // 6824
	public float mWaterTempCelsius;                         // 6828
	public float mWaterPressureKPa;                         // 6832
	public float mFuelPressureKPa;                          // 6836
	public float mFuelLevel;                                // 6840 0..1 fraction of capacity
	public float mFuelCapacity;                             // 6844 litres
	public float mSpeed;                                    // 6848 m/s
	public float mRpm;                                      // 6852
	public float mMaxRPM;                                   // 6856
	public float mBrake;                                    // 6860 0..1
	public float mThrottle;                                 // 6864 0..1
	public float mClutch;                                   // 6868 0..1 (pedal travel, 1 = fully pressed)
	public float mSteering;                                 // 6872 -1..1
	public int mGear;                                       // 6876 -1 reverse, 0 neutral, 1..n
	public int mNumGears;                                   // 6880
	public float mOdometerKM;                               // 6884
	public byte mAntiLockActive;                            // 6888 bool
	public int mLastOpponentCollisionIndex;                 // 6892
	public float mLastOpponentCollisionMagnitude;           // 6896
	public byte mBoostActive;                               // 6900 bool
	public float mBoostAmount;                              // 6904
	public FloatArray3 mOrientation;                        // 6908 euler angles (pitch, yaw, roll)
	public FloatArray3 mLocalVelocity;                      // 6920 m/s, body frame
	public FloatArray3 mWorldVelocity;                      // 6932
	public FloatArray3 mAngularVelocity;                    // 6944 rad/s
	public FloatArray3 mLocalAcceleration;                  // 6956 m/s^2, body frame
	public FloatArray3 mWorldAcceleration;                  // 6968
	public FloatArray3 mExtentsCentre;                      // 6980
	public UIntArray4 mTyreFlags;                           // 6992
	public UIntArray4 mTerrain;                             // 7008 Ams2Terrain per tyre (FL, FR, RL, RR)
	public FloatArray4 mTyreY;                              // 7024
	public FloatArray4 mTyreRPS;                            // 7040
	public FloatArray4 mTyreSlipSpeed;                      // 7056
	public FloatArray4 mTyreTemp;                           // 7072
	public FloatArray4 mTyreGrip;                           // 7088
	public FloatArray4 mTyreHeightAboveGround;              // 7104
	public FloatArray4 mTyreLateralStiffness;               // 7120
	public FloatArray4 mTyreWear;                           // 7136
	public FloatArray4 mBrakeDamage;                        // 7152
	public FloatArray4 mSuspensionDamage;                   // 7168
	public FloatArray4 mBrakeTempCelsius;                   // 7184
	public FloatArray4 mTyreTreadTemp;                      // 7200
	public FloatArray4 mTyreLayerTemp;                      // 7216
	public FloatArray4 mTyreCarcassTemp;                    // 7232
	public FloatArray4 mTyreRimTemp;                        // 7248
	public FloatArray4 mTyreInternalAirTemp;                // 7264
	public uint mCrashState;                                // 7280
	public float mAeroDamage;                               // 7284
	public float mEngineDamage;                             // 7288
	public float mAmbientTemperature;                       // 7292
	public float mTrackTemperature;                         // 7296
	public float mRainDensity;                              // 7300
	public float mWindSpeed;                                // 7304
	public float mWindDirectionX;                           // 7308
	public float mWindDirectionY;                           // 7312
	public float mCloudBrightness;                          // 7316
	public uint mSequenceNumber;                            // 7320 odd while the game is writing
	public FloatArray4 mWheelLocalPositionY;                // 7324
	public FloatArray4 mSuspensionTravel;                   // 7340 metres
	public FloatArray4 mSuspensionVelocity;                 // 7356 rate of change of pushrod deflection
	public FloatArray4 mAirPressure;                        // 7372
	public float mEngineSpeed;                              // 7388
	public float mEngineTorque;                             // 7392 Nm (engine, not steering)
	public FloatArray2 mWings;                              // 7396
	public float mHandBrake;                                // 7404
	public FloatArray64 mCurrentSector1Times;               // 7408
	public FloatArray64 mCurrentSector2Times;               // 7664
	public FloatArray64 mCurrentSector3Times;               // 7920
	public FloatArray64 mFastestSector1Times;               // 8176
	public FloatArray64 mFastestSector2Times;               // 8432
	public FloatArray64 mFastestSector3Times;               // 8688
	public FloatArray64 mFastestLapTimes;                   // 8944 seconds, -1 = unset
	public FloatArray64 mLastLapTimes;                      // 9200
	public ByteArray64 mLapsInvalidated;                    // 9456 bool[64]
	public UIntArray64 mRaceStates;                         // 9520
	public UIntArray64 mPitModes;                           // 9776
	public Ams2Vec3Array mOrientations;                     // 10032
	public FloatArray64 mSpeeds;                            // 10800
	public Ams2NameArray mCarNames;                         // 11056
	public Ams2NameArray mCarClassNames;                    // 15152
	public int mEnforcedPitStopLap;                         // 19248
	public ByteArray64 mTranslatedTrackLocation;            // 19252
	public ByteArray64 mTranslatedTrackVariation;           // 19316
	public float mBrakeBias;                                // 19380
	public float mTurboBoostPressure;                       // 19384
	public Ams2CompoundArray mTyreCompound;                 // 19388
	public UIntArray64 mPitSchedules;                       // 19548
	public UIntArray64 mHighestFlagColours;                 // 19804
	public UIntArray64 mHighestFlagReasons;                 // 20060
	public UIntArray64 mNationalities;                      // 20316
	public float mSnowDensity;                              // 20572
	public float mSessionDuration;                          // 20576
	public int mSessionAdditionalLaps;                      // 20580
	public FloatArray4 mTyreTempLeft;                       // 20584
	public FloatArray4 mTyreTempCenter;                     // 20600
	public FloatArray4 mTyreTempRight;                      // 20616
	public uint mDrsState;                                  // 20632
	public FloatArray4 mRideHeight;                         // 20636
	public uint mJoyPad0;                                   // 20652
	public uint mDPad;                                      // 20656
	public int mAntiLockSetting;                            // 20660
	public int mTractionControlSetting;                     // 20664
	public int mErsDeploymentMode;                          // 20668
	public byte mErsAutoModeEnabled;                        // 20672 bool
	public float mClutchTemp;                               // 20676
	public float mClutchWear;                               // 20680
	public byte mClutchOverheated;                          // 20684 bool
	public byte mClutchSlipping;                            // 20685 bool
	public int mYellowFlagState;                            // 20688
	public byte mSessionIsPrivate;                          // 20692 bool
	public int mLaunchStage;                                // 20696
}                                                           // 20700 bytes
