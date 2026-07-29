
using System.Runtime.InteropServices;
using System.Text;

using IRSDKSharper;

using MarvinsAIRARefactored.GameBridges.Lmu;
using MarvinsAIRARefactored.GameBridges.Rf2;

namespace MarvinsAIRARefactored.GameBridges;

public class LeMansUltimateBridge : GameBridgeAdapter
{
	public override string GameName => "Le Mans Ultimate";
	public override string LocalizationKey => "LeMansUltimate";
	public override string[] ProcessNames => [ "Le Mans Ultimate" ];
	public override GameBridgeCapabilities Capabilities => GameBridgeCapabilities.SteeringWheelTorque360Hz | GameBridgeCapabilities.ShockVelocities;

	public override bool IsImplemented => true;

	// sign conventions relative to iRacing (CCW/left-positive for steering, torque, lateral velocity, and
	// yaw) - validated against the 2026-07-20 Circuit de la Sarthe captures with a known hard-right turn:
	// LMU steering, torque, and yaw rate are right-positive (flip), but the local frame is x=left, y=up,
	// z=rearward, so lateral velocity/acceleration already match iRacing (no flip)
	private const float SteeringAngleSign = -1f;
	private const float SteeringTorqueSign = -1f;
	private const float LateralSign = 1f;
	private const float YawRateSign = -1f;

	private const double Gravity = 9.80665;

	// the native LMU_Data map genuinely updates at 100 Hz (measured via mElapsedTime), so the pump samples at
	// 360 Hz and fills the six 360 Hz sub-samples per 60 Hz frame with REAL torque readings (sample-and-hold
	// across the ~3.6x oversampling) instead of interpolating a 60 Hz signal that also aliased the 100 Hz map
	private const int SamplesPerFrame = GameBridgeVarTable.SamplesPerFrame360Hz;
	private const int SubSampleFrequency = 360;

	private const int ScoringParseInterval = 20;

	// offsets into the game's native LMU_Data shared memory map. the telemetry vehicle block reuses the
	// rF2 TelemInfoV01 layout below offset 600 (and the wheel array at 848), but diverges above 600, so
	// those fields are read at explicit offsets validated against captures (via the LMUFFB project's header)
	private const int NativeMapSize = 512 * 1024;
	private const int NativeGenericFfbOffset = 68;
	private const int NativeScoringInfoOffset = 1632;
	private const int NativeScoringVehicleArrayOffset = NativeScoringInfoOffset + 560;
	private const int NativeScoringVehicleSize = 584;
	private const int NativeTelemetryOffset = 128464;
	private const int NativeActiveVehiclesOffset = NativeTelemetryOffset;
	private const int NativePlayerVehicleIndexOffset = NativeTelemetryOffset + 1;
	private const int NativePlayerHasVehicleOffset = NativeTelemetryOffset + 2;
	private const int NativeVehicleArrayOffset = NativeTelemetryOffset + 4;
	private const int NativeVehicleSize = 1888;
	private const int NativeMaxVehicles = 104;

	// mElapsedTime within rF2VehicleTelemetry - read directly (cheap) each sub-sample to detect a new map
	// update before doing the full struct marshal, so marshaling only happens at the real ~100 Hz update rate
	private const int NativeVehicleElapsedTimeOffset = 12;

	private const int NativeVehicleVisualSteeringRangeOffset = 660;
	private const int NativeVehiclePhysicalSteeringRangeOffset = 692;
	private const int NativeVehicleAbsActiveOffset = 746;
	private const int NativeVehicleTcActiveOffset = 747;
	private const int NativeVehicleModelOffset = 796;
	private const int NativeVehicleModelLength = 30;
	private const int NativeWheelArrayOffset = 848;
	private const int NativeWheelSize = 260;
	private const int NativeWheelSurfaceTypeOffset = 176;

	private GameBridgeVarTable? _varTable = null;
	private LmuDataProvider? _provider = null;

	// the bridge is pumped from the playout timer worker thread (see Pump); this lock keeps a Stop from
	// a background task from closing the provider while a pump is mid-read
	private readonly object _pumpLock = new();
	private bool _providerOpen = false;
	private double _lastOpenAttemptSeconds = double.MinValue;
	private double _nextSubSampleSeconds = 0.0;

	private byte[] _nativeBuffer = [];

	private long _frameCounter = 0;

	private bool _hasScoring = false;
	private rF2ScoringInfo _scoringInfo;
	private rF2VehicleScoring[] _scoringVehicles = [];
	private int _playerVehicleId = -1;
	private int _playerScoringIndex = -1;

	private bool _hasTelemetry = false;
	private rF2VehicleTelemetry _playerTelemetry;
	private bool _playerAbsActive = false;
	private byte _playerFrontWheelSurfaceType = 0;
	private float _playerSteeringRangeDegrees = 0f;
	private string _playerVehicleModel = string.Empty;
	private readonly byte[] _playerVehicleModelBytes = new byte[ NativeVehicleModelLength ];
	private double _previousTelemetryElapsedTime = 0.0;
	private double _previousFuel = 0.0;
	private readonly double[] _previousSuspensionDeflection = new double[ 4 ];
	private readonly float[] _shockVelocities = new float[ 4 ];
	private float _fuelUsePerHour = 0f;

	// per-frame accumulators for the six real 360 Hz sub-samples (torque + per-wheel shock velocity); the
	// current values are held between the 100 Hz map updates by UpdateTelemetry's mElapsedTime gate
	private int _subSampleIndex = 0;
	private readonly float[] _torqueSamples = new float[ SamplesPerFrame ];
	private readonly float[,] _shockSamples = new float[ 4, SamplesPerFrame ];

	private readonly Dictionary<int, int> _carIdxByVehicleId = [];

	private double _steeringScale = 0.0;
	private int _steeringScaleSampleCount = 0;

	private string _lastSessionInfoSignature = string.Empty;
	private double _lastSessionInfoUpdateTime = double.MinValue;

	private readonly float[] _carIdxFloatScratch = new float[ GameBridgeVarTable.MaxNumCars ];
	private readonly int[] _carIdxIntScratch = new int[ GameBridgeVarTable.MaxNumCars ];

	public override void Start()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LeMansUltimateBridge] Start >>>" );

		_varTable = new GameBridgeVarTable();

		DataSource = _varTable.DataSource;

		_provider = CreateProvider();

		_nativeBuffer = new byte[ NativeMapSize ];

		ResetState();

		app.Logger.WriteLine( "[LeMansUltimateBridge] <<< Start" );
	}

	public override void Stop()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LeMansUltimateBridge] Stop >>>" );

		lock ( _pumpLock )
		{
			_provider?.Close();
			_provider = null;

			_providerOpen = false;
		}

		app.Logger.WriteLine( "[LeMansUltimateBridge] <<< Stop" );
	}

	protected virtual LmuDataProvider CreateProvider()
	{
		return new LmuLiveDataProvider();
	}

	private void ResetState()
	{
		_providerOpen = false;
		_lastOpenAttemptSeconds = double.MinValue;
		_nextSubSampleSeconds = 0.0;

		LastDataActivitySeconds = double.MinValue;

		_frameCounter = 0;

		_hasScoring = false;
		_hasTelemetry = false;

		_playerVehicleId = -1;
		_playerScoringIndex = -1;

		_playerAbsActive = false;
		_playerFrontWheelSurfaceType = 0;
		_playerSteeringRangeDegrees = 0f;
		_playerVehicleModel = string.Empty;
		Array.Clear( _playerVehicleModelBytes );

		_previousTelemetryElapsedTime = 0.0;
		_previousFuel = 0.0;

		Array.Clear( _previousSuspensionDeflection );
		Array.Clear( _shockVelocities );

		_subSampleIndex = 0;
		Array.Clear( _torqueSamples );
		Array.Clear( _shockSamples );

		_fuelUsePerHour = 0f;

		_carIdxByVehicleId.Clear();

		_steeringScale = 0.0;
		_steeringScaleSampleCount = 0;

		_lastSessionInfoSignature = string.Empty;
		_lastSessionInfoUpdateTime = double.MinValue;
	}

	#region pump

	// Called from the playout timer worker thread (~360 Hz, kernel-scheduled) immediately before the
	// racing wheel update. The 360 Hz sub-sample schedule is kept internally; zero, one, or occasionally two
	// sub-samples are taken per timer tick, each stamped with its scheduled time.
	public override void Pump( double totalSeconds )
	{
		lock ( _pumpLock )
		{
			if ( _provider == null )
			{
				return;
			}

			if ( !_providerOpen )
			{
				if ( totalSeconds - _lastOpenAttemptSeconds < 1.0 )
				{
					return;
				}

				_lastOpenAttemptSeconds = totalSeconds;

				if ( !_provider.TryOpen() )
				{
					return;
				}

				_providerOpen = true;

				_nextSubSampleSeconds = totalSeconds;

				App.Instance!.Logger.WriteLine( "[LeMansUltimateBridge] Native shared memory opened - pumping" );
			}

			// if the timer stalled for a while, resynchronize instead of bursting a backlog of sub-samples
			if ( totalSeconds - _nextSubSampleSeconds > 0.25 )
			{
				_nextSubSampleSeconds = totalSeconds;
			}

			while ( _nextSubSampleSeconds <= totalSeconds )
			{
				ProcessSubSample( _nextSubSampleSeconds );

				_nextSubSampleSeconds += 1.0 / SubSampleFrequency;
			}
		}
	}

	// Runs at 360 Hz. Every tick refreshes the player telemetry (UpdateTelemetry re-parses only when the 100 Hz
	// map advanced, holding its values otherwise) and captures the current torque / shock into the 360 Hz slot;
	// once six slots are filled (60 Hz) the scoring is refreshed and a full frame is committed.
	private void ProcessSubSample( double pumpSeconds )
	{
		if ( !( _provider?.TryReadBuffer( LmuBufferType.NativeData, _nativeBuffer ) ?? false ) )
		{
			return;
		}

		UpdateTelemetry( pumpSeconds );

		_torqueSamples[ _subSampleIndex ] = SteeringTorqueSign * (float) _playerTelemetry.mSteeringShaftTorque;

		for ( var wheelIndex = 0; wheelIndex < 4; wheelIndex++ )
		{
			_shockSamples[ wheelIndex, _subSampleIndex ] = _shockVelocities[ wheelIndex ];
		}

		if ( _subSampleIndex == SamplesPerFrame - 1 )
		{
			_frameCounter++;

			if ( ( _frameCounter % ScoringParseInterval == 1 ) || !_hasScoring )
			{
				UpdateScoring();
			}

			if ( _hasScoring && _hasTelemetry )
			{
				UpdateSessionInfo( pumpSeconds );

				WriteTelemetryFrame();

				_varTable!.DataSource.CommitFrame();
			}
		}

		_subSampleIndex = ( _subSampleIndex + 1 ) % SamplesPerFrame;
	}

	#endregion

	#region native map parsing

	private void UpdateScoring()
	{
		_scoringInfo = ReadStruct<rF2ScoringInfo>( _nativeBuffer, NativeScoringInfoOffset );

		var numVehicles = Math.Clamp( _scoringInfo.mNumVehicles, 0, NativeMaxVehicles );

		if ( _scoringVehicles.Length != numVehicles )
		{
			_scoringVehicles = new rF2VehicleScoring[ numVehicles ];
		}

		_playerScoringIndex = -1;

		for ( var i = 0; i < numVehicles; i++ )
		{
			_scoringVehicles[ i ] = ReadStruct<rF2VehicleScoring>( _nativeBuffer, NativeScoringVehicleArrayOffset + i * NativeScoringVehicleSize );

			if ( !_carIdxByVehicleId.ContainsKey( _scoringVehicles[ i ].mID ) && ( _carIdxByVehicleId.Count < GameBridgeVarTable.MaxNumCars ) )
			{
				_carIdxByVehicleId[ _scoringVehicles[ i ].mID ] = _carIdxByVehicleId.Count;
			}

			if ( _scoringVehicles[ i ].mIsPlayer != 0 )
			{
				_playerScoringIndex = i;
				_playerVehicleId = _scoringVehicles[ i ].mID;
			}
		}

		_hasScoring = _playerScoringIndex != -1;
	}

	private void UpdateTelemetry( double pumpSeconds )
	{
		if ( _nativeBuffer[ NativePlayerHasVehicleOffset ] == 0 )
		{
			return;
		}

		var playerVehicleIndex = _nativeBuffer[ NativePlayerVehicleIndexOffset ];

		if ( playerVehicleIndex >= NativeMaxVehicles )
		{
			return;
		}

		var vehicleOffset = NativeVehicleArrayOffset + playerVehicleIndex * NativeVehicleSize;

		// cheap direct read to skip unchanged frames before the (allocating) full struct marshal - at the 360 Hz
		// pump rate the 100 Hz map is unchanged on most sub-samples, so this holds the marshal to ~100 Hz
		var elapsedTime = BitConverter.ToDouble( _nativeBuffer, vehicleOffset + NativeVehicleElapsedTimeOffset );

		if ( elapsedTime == _previousTelemetryElapsedTime )
		{
			return;
		}

		LastDataActivitySeconds = pumpSeconds;

		var playerTelemetry = ReadStruct<rF2VehicleTelemetry>( _nativeBuffer, vehicleOffset );

		_playerAbsActive = _nativeBuffer[ vehicleOffset + NativeVehicleAbsActiveOffset ] != 0;
		_playerFrontWheelSurfaceType = _nativeBuffer[ vehicleOffset + NativeWheelArrayOffset + NativeWheelSurfaceTypeOffset ];

		// only decode the vehicle model string when its bytes actually changed (it is effectively constant
		// during a session) - decoding it every map update would allocate a string at ~100 Hz
		var vehicleModelSpan = _nativeBuffer.AsSpan( vehicleOffset + NativeVehicleModelOffset, NativeVehicleModelLength );

		if ( !vehicleModelSpan.SequenceEqual( _playerVehicleModelBytes ) )
		{
			vehicleModelSpan.CopyTo( _playerVehicleModelBytes );

			_playerVehicleModel = ReadFixedString( _nativeBuffer, vehicleOffset + NativeVehicleModelOffset, NativeVehicleModelLength );
		}

		var physicalRange = BitConverter.ToSingle( _nativeBuffer, vehicleOffset + NativeVehiclePhysicalSteeringRangeOffset );
		var visualRange = BitConverter.ToSingle( _nativeBuffer, vehicleOffset + NativeVehicleVisualSteeringRangeOffset );

		_playerSteeringRangeDegrees = ( physicalRange > 0f ) ? physicalRange : ( visualRange > 0f ) ? visualRange : 360f;

		var deltaSeconds = playerTelemetry.mElapsedTime - _previousTelemetryElapsedTime;

		if ( _hasTelemetry && ( deltaSeconds > 0.0 ) && ( deltaSeconds < 1.0 ) )
		{
			for ( var wheelIndex = 0; wheelIndex < 4; wheelIndex++ )
			{
				_shockVelocities[ wheelIndex ] = (float) ( ( playerTelemetry.mWheels[ wheelIndex ].mSuspensionDeflection - _previousSuspensionDeflection[ wheelIndex ] ) / deltaSeconds );
			}

			var fuelPerSecond = ( _previousFuel - playerTelemetry.mFuel ) / deltaSeconds;

			if ( fuelPerSecond >= 0.0 )
			{
				_fuelUsePerHour += 0.1f * ( (float) ( fuelPerSecond * 3600.0 ) - _fuelUsePerHour );
			}
		}

		_previousTelemetryElapsedTime = playerTelemetry.mElapsedTime;
		_previousFuel = playerTelemetry.mFuel;

		for ( var wheelIndex = 0; wheelIndex < 4; wheelIndex++ )
		{
			_previousSuspensionDeflection[ wheelIndex ] = playerTelemetry.mWheels[ wheelIndex ].mSuspensionDeflection;
		}

		_playerTelemetry = playerTelemetry;

		_hasTelemetry = true;
	}

	private string GetVehicleModelById( int vehicleId )
	{
		var activeVehicles = Math.Min( (int) _nativeBuffer[ NativeActiveVehiclesOffset ], NativeMaxVehicles );

		for ( var i = 0; i < activeVehicles; i++ )
		{
			var vehicleOffset = NativeVehicleArrayOffset + i * NativeVehicleSize;

			if ( BitConverter.ToInt32( _nativeBuffer, vehicleOffset ) == vehicleId )
			{
				return ReadFixedString( _nativeBuffer, vehicleOffset + NativeVehicleModelOffset, NativeVehicleModelLength );
			}
		}

		return string.Empty;
	}

	// the structs are blittable (see Rf2Data.cs), so this is a straight allocation-free memory read - the old
	// Marshal.PtrToStructure path boxed the struct and allocated an array per array field on every call
	private static T ReadStruct<T>( byte[] buffer, int offset ) where T : struct
	{
		return MemoryMarshal.Read<T>( buffer.AsSpan( offset ) );
	}

	private static string ReadFixedString( byte[] buffer, int offset, int maxLength )
	{
		var length = 0;

		while ( ( length < maxLength ) && ( buffer[ offset + length ] != 0 ) )
		{
			length++;
		}

		return Encoding.UTF8.GetString( buffer, offset, length );
	}

	private static string ReadString( ReadOnlySpan<byte> bytes )
	{
		var length = bytes.IndexOf( (byte) 0 );

		if ( length == -1 )
		{
			length = bytes.Length;
		}

		return Encoding.UTF8.GetString( bytes[ ..length ] );
	}

	#endregion

	#region steering wheel angle

	private const double SteeringScaleAlpha = 0.05;
	private const int SteeringScaleMinSamples = 100;
	private const double SteeringScaleMinMagnitude = 1.0;
	private const double SteeringScaleMaxMagnitude = 16.0;

	private float ComputeSteeringWheelAngle( double gameSteering, double steeringRangeRadians )
	{
		var gameAngle = SteeringAngleSign * gameSteering * steeringRangeRadians * 0.5;

		// the game clamps its steering value at full lock, so a soft lock spring driven by the game's angle
		// can never see the wheel go past the stop - iRacing reports the PHYSICAL wheel angle instead, which
		// we reconstruct here from the DirectInput axis, calibrated against the game's angle while the
		// steering is inside its linear (unclamped) region
		var physicalPosition = App.Instance!.DirectInput.ForceFeedbackWheelPosition;

		if ( ( Math.Abs( gameSteering ) < 0.95 ) && ( Math.Abs( physicalPosition ) > 0.10 ) )
		{
			var instantScale = gameAngle / physicalPosition;

			if ( ( Math.Abs( instantScale ) >= SteeringScaleMinMagnitude ) && ( Math.Abs( instantScale ) <= SteeringScaleMaxMagnitude ) )
			{
				_steeringScale += SteeringScaleAlpha * ( instantScale - _steeringScale );

				_steeringScaleSampleCount++;
			}
		}

		if ( _steeringScaleSampleCount >= SteeringScaleMinSamples )
		{
			return (float) ( _steeringScale * physicalPosition );
		}

		return (float) gameAngle;
	}

	#endregion

	#region torque

	private void WriteSteeringWheelTorque()
	{
		var varTable = _varTable!;
		var dataSource = varTable.DataSource;

		// the six sub-samples are real shaft-torque readings the pump captured at 360 Hz across this frame
		// (sample-and-hold over the ~100 Hz native map), so no interpolation is needed
		for ( var i = 0; i < SamplesPerFrame; i++ )
		{
			dataSource.SetFloat( varTable.SteeringWheelTorque_ST, _torqueSamples[ i ], i );
		}

		dataSource.SetFloat( varTable.SteeringWheelTorque, _torqueSamples[ SamplesPerFrame - 1 ] );
	}

	#endregion

	#region telemetry frame mapping

	private void WriteTelemetryFrame()
	{
		var varTable = _varTable!;
		var dataSource = varTable.DataSource;

		var playerScoring = _scoringVehicles[ _playerScoringIndex ];
		var telemetry = _playerTelemetry;

		// pedals and gear

		dataSource.SetFloat( varTable.Throttle, (float) telemetry.mFilteredThrottle );
		dataSource.SetFloat( varTable.ThrottleRaw, (float) telemetry.mUnfilteredThrottle );
		dataSource.SetFloat( varTable.Brake, (float) telemetry.mFilteredBrake );
		dataSource.SetFloat( varTable.BrakeRaw, (float) telemetry.mUnfilteredBrake );
		dataSource.SetFloat( varTable.Clutch, 1f - (float) telemetry.mFilteredClutch );
		dataSource.SetBool( varTable.BrakeABSactive, _playerAbsActive );
		dataSource.SetInt( varTable.Gear, telemetry.mGear );
		dataSource.SetFloat( varTable.RPM, (float) telemetry.mEngineRPM );

		// LMU has no warning-lights bitfield - synthesize the engine stalled flag from ignition and RPM
		var engineStalled = ( telemetry.mIgnitionStarter == 0 ) || ( telemetry.mEngineRPM < 10.0 );

		dataSource.SetBitField( varTable.EngineWarnings, engineStalled ? (uint) IRacingSdkEnum.EngineWarnings.EngineStalled : 0u );

		// motion - the local frame is x=left, y=up, z=rearward; iRacing is x=forward, y=left, z=up

		var velocityX = -telemetry.mLocalVel.z;
		var velocityY = LateralSign * telemetry.mLocalVel.x;

		dataSource.SetFloat( varTable.Speed, (float) Math.Sqrt( telemetry.mLocalVel.x * telemetry.mLocalVel.x + telemetry.mLocalVel.y * telemetry.mLocalVel.y + telemetry.mLocalVel.z * telemetry.mLocalVel.z ) );
		dataSource.SetFloat( varTable.VelocityX, (float) velocityX );
		dataSource.SetFloat( varTable.VelocityY, (float) velocityY );
		dataSource.SetFloat( varTable.LongAccel, (float) -telemetry.mLocalAccel.z );
		dataSource.SetFloat( varTable.LatAccel, (float) ( LateralSign * telemetry.mLocalAccel.x ) );
		dataSource.SetFloat( varTable.VertAccel, (float) ( telemetry.mLocalAccel.y + Gravity ) );
		dataSource.SetFloat( varTable.YawRate, (float) ( YawRateSign * telemetry.mLocalRot.y ) );

		// mOri[2] is the local rearward axis in world coordinates; the world frame has north = +z and
		// east = -x, so the compass heading (iRacing YawNorth: 0 = north, clockwise-positive) is
		// atan2( rear.x, -rear.z ) - with +z the handedness flips and the track map mirrors north/south
		var yaw = (float) Math.Atan2( telemetry.mOri[ 2 ].x, -telemetry.mOri[ 2 ].z );

		dataSource.SetFloat( varTable.Yaw, yaw );
		dataSource.SetFloat( varTable.YawNorth, yaw );
		dataSource.SetFloat( varTable.Pitch, (float) Math.Asin( Math.Clamp( -telemetry.mOri[ 1 ].z, -1.0, 1.0 ) ) );
		dataSource.SetFloat( varTable.Roll, (float) Math.Asin( Math.Clamp( telemetry.mOri[ 1 ].x, -1.0, 1.0 ) ) );

		// steering

		var steeringRangeRadians = _playerSteeringRangeDegrees * Math.PI / 180.0;

		// iRacing's SteeringWheelAngleMax is the FULL lock-to-lock range - consumers halve it themselves
		dataSource.SetFloat( varTable.SteeringWheelAngle, ComputeSteeringWheelAngle( telemetry.mUnfilteredSteering, steeringRangeRadians ) );
		dataSource.SetFloat( varTable.SteeringWheelAngleMax, (float) steeringRangeRadians );
		dataSource.SetBool( varTable.SteeringFFBEnabled, false );

		WriteSteeringWheelTorque();

		// suspension - the six 360 Hz sub-samples were collected in real time by the pump

		for ( var i = 0; i < SamplesPerFrame; i++ )
		{
			dataSource.SetFloat( varTable.LFshockVel_ST, _shockSamples[ 0, i ], i );
			dataSource.SetFloat( varTable.RFshockVel_ST, _shockSamples[ 1, i ], i );
			dataSource.SetFloat( varTable.LRshockVel_ST, _shockSamples[ 2, i ], i );
			dataSource.SetFloat( varTable.RRshockVel_ST, _shockSamples[ 3, i ], i );
		}

		// fuel

		dataSource.SetFloat( varTable.FuelLevel, (float) telemetry.mFuel );
		dataSource.SetFloat( varTable.FuelLevelPct, ( telemetry.mFuelCapacity > 0.0 ) ? (float) ( telemetry.mFuel / telemetry.mFuelCapacity ) : 0f );
		dataSource.SetFloat( varTable.FuelUsePerHour, _fuelUsePerHour );

		// session and lap state

		var trackLength = Math.Max( 1.0, _scoringInfo.mLapDist );

		dataSource.SetDouble( varTable.SessionTime, _scoringInfo.mCurrentET );
		dataSource.SetDouble( varTable.SessionTimeRemain, ( _scoringInfo.mEndET > 0.0 ) ? Math.Max( 0.0, _scoringInfo.mEndET - _scoringInfo.mCurrentET ) : IRacingSdkConst.UnlimitedTime );
		dataSource.SetInt( varTable.SessionNum, _scoringInfo.mSession );
		dataSource.SetInt( varTable.SessionState, MapSessionState( _scoringInfo.mGamePhase ) );
		dataSource.SetBitField( varTable.SessionFlags, MapSessionFlags( _scoringInfo.mGamePhase, _scoringInfo.mYellowFlagState ) );
		dataSource.SetInt( varTable.SessionLapsRemainEx, ( _scoringInfo.mMaxLaps > 0 ) && ( _scoringInfo.mMaxLaps < 32767 ) ? Math.Max( 0, _scoringInfo.mMaxLaps - playerScoring.mTotalLaps ) : IRacingSdkConst.UnlimitedLaps );

		// mLapDist goes negative while the car is in the garage stall
		var playerLapDist = Math.Max( 0.0, playerScoring.mLapDist );

		dataSource.SetInt( varTable.Lap, playerScoring.mTotalLaps + 1 );
		dataSource.SetFloat( varTable.LapDist, (float) playerLapDist );
		dataSource.SetFloat( varTable.LapDistPct, (float) ( playerLapDist / trackLength ) );
		dataSource.SetFloat( varTable.LapBestLapTime, (float) Math.Max( 0.0, playerScoring.mBestLapTime ) );
		dataSource.SetFloat( varTable.LapLastLapTime, (float) Math.Max( 0.0, playerScoring.mLastLapTime ) );

		// player state

		var inRealtime = _scoringInfo.mInRealtime != 0;

		dataSource.SetBool( varTable.IsOnTrack, inRealtime && ( playerScoring.mInGarageStall == 0 ) );
		dataSource.SetBool( varTable.OnPitRoad, playerScoring.mInPits != 0 );
		dataSource.SetBool( varTable.PitsOpen, true );
		dataSource.SetInt( varTable.PlayerCarIdx, GetCarIdx( _playerVehicleId ) );
		dataSource.SetInt( varTable.PlayerCarPosition, playerScoring.mPlace );
		dataSource.SetInt( varTable.PlayerCarClassPosition, ComputeClassPosition( _playerScoringIndex ) );
		dataSource.SetInt( varTable.PlayerCarMyIncidentCount, 0 );
		dataSource.SetInt( varTable.PlayerTrackSurface, MapTrackSurface( playerScoring, inRealtime ) );
		dataSource.SetInt( varTable.PlayerTrackSurfaceMaterial, MapTrackSurfaceMaterial( (rFactor2Constants.rF2SurfaceType) _playerFrontWheelSurfaceType ) );

		// weather

		dataSource.SetBool( varTable.WeatherDeclaredWet, _scoringInfo.mRaining > 0.1 );

		// fixed values with no LMU equivalent

		dataSource.SetInt( varTable.DisplayUnits, 1 );
		dataSource.SetFloat( varTable.FrameRate, 60f );
		dataSource.SetFloat( varTable.GpuUsage, 0f );
		dataSource.SetBool( varTable.IsReplayPlaying, false );
		dataSource.SetBool( varTable.LoadNumTextures, false );
		dataSource.SetInt( varTable.PaceMode, (int) IRacingSdkEnum.PaceMode.NotPacing );
		dataSource.SetInt( varTable.CarLeftRight, (int) IRacingSdkEnum.CarLeftRight.Off );
		dataSource.SetInt( varTable.RadioTransmitCarIdx, -1 );
		dataSource.SetInt( varTable.ReplayFrameNumEnd, 0 );
		dataSource.SetBool( varTable.ReplayPlaySlowMotion, false );
		dataSource.SetInt( varTable.ReplayPlaySpeed, 1 );
		dataSource.SetFloat( varTable.TireLF_RumblePitch, 0f );
		dataSource.SetFloat( varTable.TireRF_RumblePitch, 0f );
		dataSource.SetFloat( varTable.TireLR_RumblePitch, 0f );
		dataSource.SetFloat( varTable.TireRR_RumblePitch, 0f );

		WriteCarIdxArrays( trackLength );
	}

	private void WriteCarIdxArrays( double trackLength )
	{
		var varTable = _varTable!;
		var dataSource = varTable.DataSource;

		var playerScoring = _scoringVehicles[ _playerScoringIndex ];

		var carDistAhead = float.MaxValue;
		var carDistBehind = float.MaxValue;

		// best lap time

		Array.Clear( _carIdxFloatScratch );

		foreach ( var vehicle in _scoringVehicles )
		{
			if ( _carIdxByVehicleId.TryGetValue( vehicle.mID, out var carIdx ) )
			{
				_carIdxFloatScratch[ carIdx ] = (float) Math.Max( 0.0, vehicle.mBestLapTime );
			}
		}

		dataSource.SetFloatArray( varTable.CarIdxBestLapTime, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		// est time (time into lap)

		Array.Clear( _carIdxFloatScratch );

		foreach ( var vehicle in _scoringVehicles )
		{
			if ( _carIdxByVehicleId.TryGetValue( vehicle.mID, out var carIdx ) )
			{
				_carIdxFloatScratch[ carIdx ] = (float) Math.Max( 0.0, vehicle.mTimeIntoLap );
			}
		}

		dataSource.SetFloatArray( varTable.CarIdxEstTime, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		// f2 time (time behind leader)

		Array.Clear( _carIdxFloatScratch );

		foreach ( var vehicle in _scoringVehicles )
		{
			if ( _carIdxByVehicleId.TryGetValue( vehicle.mID, out var carIdx ) )
			{
				_carIdxFloatScratch[ carIdx ] = (float) Math.Max( 0.0, vehicle.mTimeBehindLeader );
			}
		}

		dataSource.SetFloatArray( varTable.CarIdxF2Time, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		// laps started

		Array.Clear( _carIdxIntScratch );

		foreach ( var vehicle in _scoringVehicles )
		{
			if ( _carIdxByVehicleId.TryGetValue( vehicle.mID, out var carIdx ) )
			{
				_carIdxIntScratch[ carIdx ] = vehicle.mTotalLaps + 1;
			}
		}

		dataSource.SetIntArray( varTable.CarIdxLap, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		// laps completed

		Array.Clear( _carIdxIntScratch );

		foreach ( var vehicle in _scoringVehicles )
		{
			if ( _carIdxByVehicleId.TryGetValue( vehicle.mID, out var carIdx ) )
			{
				_carIdxIntScratch[ carIdx ] = vehicle.mTotalLaps;
			}
		}

		dataSource.SetIntArray( varTable.CarIdxLapCompleted, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		// lap dist pct + car dist ahead/behind

		Array.Clear( _carIdxFloatScratch );

		foreach ( var vehicle in _scoringVehicles )
		{
			if ( _carIdxByVehicleId.TryGetValue( vehicle.mID, out var carIdx ) )
			{
				_carIdxFloatScratch[ carIdx ] = (float) ( Math.Max( 0.0, vehicle.mLapDist ) / trackLength );
			}

			if ( ( vehicle.mID != _playerVehicleId ) && ( vehicle.mInPits == 0 ) )
			{
				var distance = Math.Max( 0.0, vehicle.mLapDist ) - Math.Max( 0.0, playerScoring.mLapDist );

				if ( distance < -trackLength * 0.5 )
				{
					distance += trackLength;
				}
				else if ( distance > trackLength * 0.5 )
				{
					distance -= trackLength;
				}

				if ( distance >= 0.0 )
				{
					carDistAhead = Math.Min( carDistAhead, (float) distance );
				}
				else
				{
					carDistBehind = Math.Min( carDistBehind, (float) -distance );
				}
			}
		}

		dataSource.SetFloatArray( varTable.CarIdxLapDistPct, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		dataSource.SetFloat( varTable.CarDistAhead, ( carDistAhead == float.MaxValue ) ? 999999f : carDistAhead );
		dataSource.SetFloat( varTable.CarDistBehind, ( carDistBehind == float.MaxValue ) ? 999999f : carDistBehind );

		// position

		Array.Clear( _carIdxIntScratch );

		foreach ( var vehicle in _scoringVehicles )
		{
			if ( _carIdxByVehicleId.TryGetValue( vehicle.mID, out var carIdx ) )
			{
				_carIdxIntScratch[ carIdx ] = vehicle.mPlace;
			}
		}

		dataSource.SetIntArray( varTable.CarIdxPosition, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		// on pit road

		foreach ( var vehicle in _scoringVehicles )
		{
			if ( _carIdxByVehicleId.TryGetValue( vehicle.mID, out var carIdx ) )
			{
				dataSource.SetBool( varTable.CarIdxOnPitRoad, vehicle.mInPits != 0, carIdx );
			}
		}

		// tire compound

		Array.Clear( _carIdxIntScratch );

		foreach ( var vehicle in _scoringVehicles )
		{
			if ( _carIdxByVehicleId.TryGetValue( vehicle.mID, out var carIdx ) )
			{
				_carIdxIntScratch[ carIdx ] = ( vehicle.mID == _playerVehicleId ) ? _playerTelemetry.mFrontTireCompoundIndex : 0;
			}
		}

		dataSource.SetIntArray( varTable.CarIdxTireCompound, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );
	}

	private int ComputeClassPosition( int playerScoringIndex )
	{
		var playerVehicle = _scoringVehicles[ playerScoringIndex ];

		var classPosition = 1;

		// the class names are compared as raw byte spans - this runs every frame, so decoding them into
		// strings here would be steady-state garbage on the playout timer worker thread
		foreach ( var vehicle in _scoringVehicles )
		{
			if ( ( vehicle.mID != playerVehicle.mID ) && ( vehicle.mPlace < playerVehicle.mPlace ) && ( (ReadOnlySpan<byte>) vehicle.mVehicleClass ).SequenceEqual( playerVehicle.mVehicleClass ) )
			{
				classPosition++;
			}
		}

		return classPosition;
	}

	private int GetCarIdx( int vehicleId )
	{
		return _carIdxByVehicleId.TryGetValue( vehicleId, out var carIdx ) ? carIdx : 0;
	}

	private static int MapSessionState( byte gamePhase )
	{
		return gamePhase switch
		{
			0 => (int) IRacingSdkEnum.SessionState.GetInCar,
			1 => (int) IRacingSdkEnum.SessionState.Warmup,
			2 or 3 or 4 => (int) IRacingSdkEnum.SessionState.ParadeLaps,
			8 => (int) IRacingSdkEnum.SessionState.Checkered,
			_ => (int) IRacingSdkEnum.SessionState.Racing
		};
	}

	private static uint MapSessionFlags( byte gamePhase, sbyte yellowFlagState )
	{
		var flags = 0u;

		if ( gamePhase == 5 )
		{
			flags |= 0x00000004; // green
		}

		if ( ( gamePhase == 6 ) || ( yellowFlagState > 0 ) )
		{
			flags |= 0x00004000; // caution
		}

		if ( gamePhase == 8 )
		{
			flags |= 0x00000001; // checkered
		}

		return flags;
	}

	private static int MapTrackSurface( rF2VehicleScoring playerScoring, bool inRealtime )
	{
		if ( !inRealtime )
		{
			return (int) IRacingSdkEnum.TrkLoc.NotInWorld;
		}

		if ( playerScoring.mInGarageStall != 0 )
		{
			return (int) IRacingSdkEnum.TrkLoc.InPitStall;
		}

		if ( playerScoring.mInPits != 0 )
		{
			return (int) IRacingSdkEnum.TrkLoc.AproachingPits;
		}

		return (int) IRacingSdkEnum.TrkLoc.OnTrack;
	}

	private static int MapTrackSurfaceMaterial( rFactor2Constants.rF2SurfaceType surfaceType )
	{
		return surfaceType switch
		{
			rFactor2Constants.rF2SurfaceType.Dry => (int) IRacingSdkEnum.TrkSurf.Asphalt1Material,
			rFactor2Constants.rF2SurfaceType.Wet => (int) IRacingSdkEnum.TrkSurf.Asphalt2Material,
			rFactor2Constants.rF2SurfaceType.Grass => (int) IRacingSdkEnum.TrkSurf.Grass1Material,
			rFactor2Constants.rF2SurfaceType.Dirt => (int) IRacingSdkEnum.TrkSurf.Dirt1Material,
			rFactor2Constants.rF2SurfaceType.Gravel => (int) IRacingSdkEnum.TrkSurf.Gravel1Material,
			rFactor2Constants.rF2SurfaceType.Kerb => (int) IRacingSdkEnum.TrkSurf.Rumble1Material,
			_ => (int) IRacingSdkEnum.TrkSurf.Asphalt1Material
		};
	}

	#endregion

	#region session info

	private void UpdateSessionInfo( double pumpSeconds )
	{
		// throttle first - the signature strings below allocate, so they are only built at 1 Hz instead of
		// every frame (the playout timer worker thread must stay free of steady-state garbage)
		if ( pumpSeconds - _lastSessionInfoUpdateTime < 1.0 )
		{
			return;
		}

		_lastSessionInfoUpdateTime = pumpSeconds;

		var trackName = ReadString( _scoringInfo.mTrackName );

		var signature = $"{trackName}|{_scoringInfo.mSession}|{_playerVehicleId}|{_carIdxByVehicleId.Count}|{(int) _playerTelemetry.mEngineMaxRPM}|{_playerVehicleModel}";

		if ( signature == _lastSessionInfoSignature )
		{
			return;
		}

		_lastSessionInfoSignature = signature;

		var builder = new GameBridgeSessionInfoBuilder
		{
			TrackDisplayName = trackName,
			TrackConfigName = string.Empty,
			TrackLengthInKm = (float) ( _scoringInfo.mLapDist / 1000.0 ),
			SeriesID = 0,
			LeagueID = 0,
			SessionID = _scoringInfo.mSession,
			TimeOfDay = "12:00 pm",
			DriverCarIdx = GetCarIdx( _playerVehicleId ),
			DriverSetupName = "bridge",
			DriverCarGearNumForward = Math.Max( 1, (int) _playerTelemetry.mMaxGears ),
			DriverCarRedLine = (float) _playerTelemetry.mEngineMaxRPM,
			DriverCarSLFirstRPM = (float) ( _playerTelemetry.mEngineMaxRPM * 0.88 ),
			DriverCarSLShiftRPM = (float) ( _playerTelemetry.mEngineMaxRPM * 0.96 ),
			DriverCarSLBlinkRPM = (float) ( _playerTelemetry.mEngineMaxRPM * 0.98 )
		};

		foreach ( var vehicle in _scoringVehicles )
		{
			if ( _carIdxByVehicleId.TryGetValue( vehicle.mID, out var carIdx ) )
			{
				// the native map carries the actual car model per vehicle - a much better per-car context
				// key than the team entry name, and stable across liveries and teams
				var vehicleModel = GetVehicleModelById( vehicle.mID );

				var carScreenName = ( vehicleModel.Length > 0 ) ? vehicleModel : ReadString( vehicle.mVehicleName );

				builder.Drivers.Add( new GameBridgeSessionInfoBuilder.DriverModel
				{
					CarIdx = carIdx,
					UserName = ReadString( vehicle.mDriverName ),
					UserID = vehicle.mID,
					CarNumber = carIdx.ToString(),
					CarScreenName = carScreenName,
					CarPath = ReadString( vehicle.mVehicleClass ),
					CarClassID = GetCarClassId( ReadString( vehicle.mVehicleClass ) ),
					CarIsPaceCar = 0,
					IRating = 0,
					IsSpectator = 0,
					TeamID = 0
				} );
			}
		}

		builder.Sessions.Add( new GameBridgeSessionInfoBuilder.SessionModel
		{
			SessionNum = _scoringInfo.mSession,
			SessionType = MapSessionType( _scoringInfo.mSession )
		} );

		_varTable!.DataSource.SetSessionInfo( builder.ToYaml() );
	}

	private readonly Dictionary<string, int> _carClassIdsByName = [];

	private int GetCarClassId( string className )
	{
		if ( !_carClassIdsByName.TryGetValue( className, out var classId ) )
		{
			classId = _carClassIdsByName.Count + 1;

			_carClassIdsByName[ className ] = classId;
		}

		return classId;
	}

	private static string MapSessionType( int session )
	{
		if ( session >= 10 )
		{
			return "Race";
		}
		else if ( session == 9 )
		{
			return "Warmup";
		}
		else if ( session >= 5 )
		{
			return "Lone Qualify";
		}
		else if ( session >= 1 )
		{
			return "Practice";
		}

		return "Offline Testing";
	}

	#endregion
}
