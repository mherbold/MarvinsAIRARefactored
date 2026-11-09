using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using MarvinsAIRARefactored.GameBridges;
using MarvinsAIRARefactored.GameBridges.Ams2;

// AMS2 shared-memory probe / fake publisher used to test MAIRA's Automobilista 2 bridge without the game.
//   layout  - print the C# struct size and key field offsets (compare with the ctypes model of the header)
//   publish - create the $pcars2$ mapping and publish a synthetic driving session (process name AMS2AVX
//             makes MAIRA believe the game is running)
//   dump    - read the real game's mapping and print the fields the bridge maps, once per second

var mode = ( args.Length > 0 ) ? args[ 0 ] : "layout";

static int Off<T>( ref Ams2SharedMemory block, ref T field ) where T : struct
{
	return (int) Unsafe.ByteOffset( ref Unsafe.As<Ams2SharedMemory, byte>( ref block ), ref Unsafe.As<T, byte>( ref field ) );
}

if ( mode == "layout" )
{
	var d = default( Ams2SharedMemory );

	Console.WriteLine( $"participant size {Unsafe.SizeOf<Ams2Participant>()} total {Unsafe.SizeOf<Ams2SharedMemory>()}" );
	Console.WriteLine( $"mParticipantInfo {Off( ref d, ref d.mParticipantInfo )}" );
	Console.WriteLine( $"mUnfilteredThrottle {Off( ref d, ref d.mUnfilteredThrottle )}" );
	Console.WriteLine( $"mCarName {Off( ref d, ref d.mCarName )}" );
	Console.WriteLine( $"mBestLapTime {Off( ref d, ref d.mBestLapTime )}" );
	Console.WriteLine( $"mSpeed {Off( ref d, ref d.mSpeed )}" );
	Console.WriteLine( $"mGear {Off( ref d, ref d.mGear )}" );
	Console.WriteLine( $"mAntiLockActive {Off( ref d, ref d.mAntiLockActive )}" );
	Console.WriteLine( $"mLastOpponentCollisionIndex {Off( ref d, ref d.mLastOpponentCollisionIndex )}" );
	Console.WriteLine( $"mBoostAmount {Off( ref d, ref d.mBoostAmount )}" );
	Console.WriteLine( $"mOrientation {Off( ref d, ref d.mOrientation )}" );
	Console.WriteLine( $"mLocalVelocity {Off( ref d, ref d.mLocalVelocity )}" );
	Console.WriteLine( $"mTyreFlags {Off( ref d, ref d.mTyreFlags )}" );
	Console.WriteLine( $"mSequenceNumber {Off( ref d, ref d.mSequenceNumber )} (constant {Ams2Constants.SequenceNumberOffset})" );
	Console.WriteLine( $"mSuspensionVelocity {Off( ref d, ref d.mSuspensionVelocity )}" );
	Console.WriteLine( $"mCurrentSector1Times {Off( ref d, ref d.mCurrentSector1Times )}" );
	Console.WriteLine( $"mLapsInvalidated {Off( ref d, ref d.mLapsInvalidated )}" );
	Console.WriteLine( $"mRaceStates {Off( ref d, ref d.mRaceStates )}" );
	Console.WriteLine( $"mCarNames {Off( ref d, ref d.mCarNames )}" );
	Console.WriteLine( $"mEnforcedPitStopLap {Off( ref d, ref d.mEnforcedPitStopLap )}" );
	Console.WriteLine( $"mTyreCompound {Off( ref d, ref d.mTyreCompound )}" );
	Console.WriteLine( $"mSnowDensity {Off( ref d, ref d.mSnowDensity )}" );
	Console.WriteLine( $"mSessionDuration {Off( ref d, ref d.mSessionDuration )}" );
	Console.WriteLine( $"mErsAutoModeEnabled {Off( ref d, ref d.mErsAutoModeEnabled )}" );
	Console.WriteLine( $"mClutchTemp {Off( ref d, ref d.mClutchTemp )}" );
	Console.WriteLine( $"mYellowFlagState {Off( ref d, ref d.mYellowFlagState )}" );
	Console.WriteLine( $"mLaunchStage {Off( ref d, ref d.mLaunchStage )}" );

	return;
}

static void PutString( ref ByteArray64 target, string value )
{
	var bytes = Encoding.UTF8.GetBytes( value );

	for ( var i = 0; i < 64; i++ )
	{
		target[ i ] = ( i < bytes.Length && i < 63 ) ? bytes[ i ] : (byte) 0;
	}
}

if ( mode == "publish" )
{
	var size = Ams2Constants.StructSize;

	using var mmf = MemoryMappedFile.CreateOrOpen( Ams2Constants.MapName, size, MemoryMappedFileAccess.ReadWrite );
	using var accessor = mmf.CreateViewAccessor( 0, size, MemoryMappedFileAccess.ReadWrite );

	var block = default( Ams2SharedMemory );

	block.mVersion = Ams2Constants.SharedMemoryVersion;
	block.mBuildVersionNumber = 9999;
	block.mGameState = (uint) Ams2GameState.InGamePlaying;
	block.mSessionState = (uint) Ams2SessionState.Practice;
	block.mRaceState = (uint) Ams2RaceState.Racing;
	block.mViewedParticipantIndex = 0;
	block.mNumParticipants = 2;

	var p0 = block.mParticipantInfo[ 0 ];
	p0.mIsActive = 1;
	PutString( ref p0.mName, "Michael" );
	p0.mRacePosition = 1;
	p0.mCurrentLap = 1;
	block.mParticipantInfo[ 0 ] = p0;

	var p1 = block.mParticipantInfo[ 1 ];
	p1.mIsActive = 1;
	PutString( ref p1.mName, "AI Driver" );
	p1.mRacePosition = 2;
	p1.mCurrentLap = 1;
	p1.mCurrentLapDistance = 400f;
	block.mParticipantInfo[ 1 ] = p1;

	PutString( ref block.mCarName, "Formula Trainer" );
	PutString( ref block.mCarClassName, "F-Trainer" );
	PutString( ref block.mTrackLocation, "Interlagos" );
	PutString( ref block.mTrackVariation, "GP" );
	PutString( ref block.mTranslatedTrackLocation, "Interlagos" );
	PutString( ref block.mTranslatedTrackVariation, "GP" );
	PutString( ref block.mCarNames[ 0 ], "Formula Trainer" );
	PutString( ref block.mCarNames[ 1 ], "Formula Trainer" );

	block.mTrackLength = 4309f;
	block.mLapsInEvent = 0;
	block.mBestLapTime = -1f;
	block.mEventTimeRemaining = 600000f;
	block.mMaxRPM = 6500f;
	block.mNumGears = 5;
	block.mFuelCapacity = 45f;
	block.mFuelLevel = 0.6f;
	block.mHighestFlagColour = (uint) Ams2FlagColour.Green;
	block.mPitMode = (uint) Ams2PitMode.None;

	var buffer = new byte[ size ];
	var sequence = 0u;
	var t = 0.0;

	Console.WriteLine( $"publishing {Ams2Constants.MapName} ({size} bytes) - Ctrl+C to stop" );

	while ( true )
	{
		t += 1.0 / 120.0;

		// a lap of 60 s: speed builds, gear steps, rpm saws, steering sweeps, occasional ABS
		var lapPhase = ( t % 60.0 ) / 60.0;
		var speed = 20f + 40f * (float) Math.Sin( lapPhase * Math.PI );
		var gear = 1 + (int) ( speed / 14f );
		var rpm = 2500f + 4000f * (float) ( ( speed / 14f ) % 1.0 );
		var steering = (float) Math.Sin( t * 0.7 ) * 0.6f;
		var latAccel = -steering * 12f; // pretend +X is left (steer right -> accel toward -X)
		var yawRate = steering * 0.8f;  // pretend yaw is clockwise-positive

		block.mSpeed = speed;
		block.mRpm = rpm;
		block.mGear = gear;
		block.mThrottle = 0.5f + 0.5f * (float) Math.Sin( t * 0.5 );
		block.mUnfilteredThrottle = block.mThrottle;
		block.mBrake = ( ( t % 10.0 ) < 1.5 ) ? 0.9f : 0f;
		block.mUnfilteredBrake = block.mBrake;
		block.mAntiLockActive = (byte) ( ( block.mBrake > 0.5f && ( t % 0.3 ) < 0.15 ) ? 1 : 0 );
		block.mClutch = 0f;
		block.mSteering = steering;
		block.mUnfilteredSteering = steering;

		block.mLocalVelocity[ 0 ] = 0.2f * steering;
		block.mLocalVelocity[ 1 ] = 0f;
		block.mLocalVelocity[ 2 ] = -speed; // forward = -Z
		block.mLocalAcceleration[ 0 ] = latAccel;
		block.mLocalAcceleration[ 1 ] = 0.3f * (float) Math.Sin( t * 9.0 ); // gravity excluded
		block.mLocalAcceleration[ 2 ] = -2f * (float) Math.Cos( lapPhase * Math.PI );
		block.mAngularVelocity[ 1 ] = yawRate;
		block.mOrientation[ 1 ] = (float) ( t * 0.1 % ( 2 * Math.PI ) );

		for ( var w = 0; w < 4; w++ )
		{
			block.mSuspensionVelocity[ w ] = 0.05f * (float) Math.Sin( t * ( 30.0 + w * 3.0 ) );
			block.mTerrain[ w ] = (uint) Ams2Terrain.Road;
		}

		var lapDist = (float) ( lapPhase * block.mTrackLength );

		var player = block.mParticipantInfo[ 0 ];
		player.mCurrentLapDistance = lapDist;
		player.mCurrentLap = 1 + (uint) ( t / 60.0 );
		player.mLapsCompleted = (uint) ( t / 60.0 );
		block.mParticipantInfo[ 0 ] = player;

		var other = block.mParticipantInfo[ 1 ];
		other.mCurrentLapDistance = ( lapDist + 400f ) % block.mTrackLength;
		block.mParticipantInfo[ 1 ] = other;

		// write like the game: odd sequence while writing, even when done
		sequence++;
		accessor.Write( Ams2Constants.SequenceNumberOffset, sequence );

		block.mSequenceNumber = sequence + 1;

		MemoryMarshal.Write( buffer, in block );
		accessor.WriteArray( 0, buffer, 0, size );

		sequence++;
		accessor.Write( Ams2Constants.SequenceNumberOffset, sequence );

		Thread.Sleep( 8 );
	}
}

if ( mode == "provider" )
{
	Ams2Probe.ProviderMode.Run();

	return;
}

if ( mode == "dump" )
{
	using var mmf = MemoryMappedFile.OpenExisting( Ams2Constants.MapName, MemoryMappedFileRights.Read );
	using var accessor = mmf.CreateViewAccessor( 0, Ams2Constants.StructSize, MemoryMappedFileAccess.Read );

	var buffer = new byte[ Ams2Constants.StructSize ];

	static string Str( ReadOnlySpan<byte> b )
	{
		var n = b.IndexOf( (byte) 0 );

		return Encoding.UTF8.GetString( ( n < 0 ) ? b : b[ ..n ] );
	}

	while ( true )
	{
		accessor.ReadArray( 0, buffer, 0, buffer.Length );

		var d = MemoryMarshal.Read<Ams2SharedMemory>( buffer );

		var idx = Math.Clamp( d.mViewedParticipantIndex, 0, 63 );
		var p = d.mParticipantInfo[ idx ];

		Console.WriteLine( $"v{d.mVersion} seq {d.mSequenceNumber} state {(Ams2GameState) d.mGameState}/{(Ams2SessionState) d.mSessionState} n={d.mNumParticipants} viewed={idx} '{Str( p.mName )}' car '{Str( d.mCarName )}' track '{Str( d.mTranslatedTrackLocation )}' / '{Str( d.mTranslatedTrackVariation )}' len {d.mTrackLength:F0}" );
		Console.WriteLine( $"  spd {d.mSpeed:F1} rpm {d.mRpm:F0}/{d.mMaxRPM:F0} gear {d.mGear}/{d.mNumGears} thr {d.mThrottle:F2} brk {d.mBrake:F2} clt {d.mClutch:F2} str {d.mSteering:F2} abs {d.mAntiLockActive} pit {(Ams2PitMode) d.mPitMode} flag {(Ams2FlagColour) d.mHighestFlagColour}" );
		Console.WriteLine( $"  lvel [{d.mLocalVelocity[ 0 ]:F2} {d.mLocalVelocity[ 1 ]:F2} {d.mLocalVelocity[ 2 ]:F2}] lacc [{d.mLocalAcceleration[ 0 ]:F2} {d.mLocalAcceleration[ 1 ]:F2} {d.mLocalAcceleration[ 2 ]:F2}] angv [{d.mAngularVelocity[ 0 ]:F2} {d.mAngularVelocity[ 1 ]:F2} {d.mAngularVelocity[ 2 ]:F2}] orient [{d.mOrientation[ 0 ]:F2} {d.mOrientation[ 1 ]:F2} {d.mOrientation[ 2 ]:F2}]" );
		Console.WriteLine( $"  suspVel [{d.mSuspensionVelocity[ 0 ]:F3} {d.mSuspensionVelocity[ 1 ]:F3} {d.mSuspensionVelocity[ 2 ]:F3} {d.mSuspensionVelocity[ 3 ]:F3}] terrain [{(Ams2Terrain) d.mTerrain[ 0 ]} ..] lapDist {p.mCurrentLapDistance:F0} lap {p.mCurrentLap} pos {p.mRacePosition} fuel {d.mFuelLevel:F2}x{d.mFuelCapacity:F0}" );

		Thread.Sleep( 1000 );
	}
}

Console.WriteLine( "usage: AMS2AVX layout | publish | dump" );
