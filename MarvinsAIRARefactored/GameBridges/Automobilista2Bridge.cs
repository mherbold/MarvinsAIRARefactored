
using System.Runtime.InteropServices;
using System.Text;

using IRSDKSharper;

using MarvinsAIRARefactored.GameBridges.Ams2;

namespace MarvinsAIRARefactored.GameBridges;

/// <summary>
/// Automobilista 2 bridge. AMS2 publishes the Project CARS 2 shared memory block natively ("$pcars2$", header
/// version 14), refreshed once per graphics frame.
///
/// This bridge is EFFECTS ONLY. The AMS2 block carries no steering shaft torque and no force feedback output
/// of any kind (only the steering INPUT position and the engine torque), and the game offers no other channel
/// for it - its FFB is shaped in-game by the ffb_custom_settings.txt script. So the bridge reports the game's
/// own force feedback as enabled, which makes RacingWheel suspend MAIRA's wheel output and leave the wheelbase
/// to the game, and feeds everything else MAIRA's consumers need: pedals (RPM, gear, shift point, ABS), Typhoon
/// wind, the G-tensioner, the LFE and the per-wheel shock velocities (AMS2 exposes those directly). Wheel-side
/// steering effects (understeer, oversteer, seat of pants) ride on MAIRA's torque stream and therefore stay
/// silent in AMS2.
///
/// Sign conventions: the Madness engine's body frame is documented only as X = lateral, Y = vertical,
/// Z = longitudinal, without signs. Rather than guess, the bridge calibrates the signs at runtime from the
/// data itself - the longitudinal sign from the velocity while in a forward gear, the lateral and yaw signs
/// from their correlation with the steering input (right-positive, as the header's -1..1 range is used by
/// every other reader of this block), and whether the vertical acceleration includes gravity from its value
/// at rest. Until a sign resolves the right-handed Y-up defaults (forward = -Z, X = right, CCW yaw positive)
/// are used, and each resolution is logged once.
/// </summary>
public class Automobilista2Bridge : GameBridgeAdapter
{
	public override string GameName => "Automobilista 2";
	public override string LocalizationKey => "Automobilista2";
	public override string[] ProcessNames => [ "AMS2AVX", "AMS2" ];
	public override GameBridgeCapabilities Capabilities => GameBridgeCapabilities.ShockVelocities;

	public override bool IsImplemented => true;

	// mSteering is right-positive; iRacing's steering wheel angle is left-positive
	private const float SteeringAngleSign = -1f;

	private const int ForwardIndex = 2;
	private const int LateralIndex = 0;
	private const int VerticalIndex = 1;

	private const int PitchIndex = 0;
	private const int YawIndex = 1;
	private const int RollIndex = 2;

	private const double Gravity = 9.80665;

	// AMS2 refreshes the block once per graphics frame (60-120 Hz in practice), so the pump samples it at
	// 360 Hz like the other bridges and holds the last coherent copy across the sub-samples in between; the
	// per-wheel shock velocities come straight from the block (no differentiation needed)
	private const int SamplesPerFrame = GameBridgeVarTable.SamplesPerFrame360Hz;
	private const int SubSampleFrequency = 360;

	// AMS2 does not expose the car's steering lock, so the soft-lock range falls back to this default; the
	// physical wheel angle is reconstructed from DirectInput the same way the Assetto Corsa bridge does it
	private const float DefaultSteeringRangeDegrees = 900f;

	private GameBridgeVarTable? _varTable = null;
	private Ams2DataProvider? _provider = null;

	private readonly object _pumpLock = new();
	private bool _providerOpen = false;
	private double _lastOpenAttemptSeconds = double.MinValue;
	private double _nextSubSampleSeconds = 0.0;

	private byte[] _blockBuffer = [];
	private Ams2SharedMemory _data;
	private bool _hasData = false;

	private const double StaleMapRecycleSeconds = 2.0;

	private double _lastMapRecycleSeconds = double.MinValue;
	private bool _recyclingStaleMap = false;

	private int _subSampleIndex = 0;
	private readonly float[,] _shockSamples = new float[ 4, SamplesPerFrame ];

	private uint _previousSequenceNumber = uint.MaxValue;
	private bool _versionLogged = false;

	private double _sessionStartSeconds = double.NaN;

	private double _steeringScale = 0.0;
	private int _steeringScaleSampleCount = 0;

	private string _lastSessionInfoSignature = string.Empty;
	private double _lastSessionInfoUpdateTime = double.MinValue;

	private readonly float[] _carIdxFloatScratch = new float[ GameBridgeVarTable.MaxNumCars ];
	private readonly int[] _carIdxIntScratch = new int[ GameBridgeVarTable.MaxNumCars ];

	private readonly StringBuilder _signatureBuilder = new( 4096 );

	#region axis sign calibration

	// accumulates the sign of a product of two signals and resolves once enough qualifying samples were seen
	private sealed class SignEstimator( int minimumSamples )
	{
		private int _sum = 0;
		private int _count = 0;

		public bool Resolved => _count >= minimumSamples;

		public int Sign => ( _sum >= 0 ) ? 1 : -1;

		public void Add( double value )
		{
			if ( value != 0.0 )
			{
				_sum += Math.Sign( value );
				_count++;
			}
		}

		public void Reset()
		{
			_sum = 0;
			_count = 0;
		}
	}

	private const int SignCalibrationSamples = 240; // 4 s of qualifying frames at 60 Hz

	private readonly SignEstimator _forwardEstimator = new( SignCalibrationSamples );
	private readonly SignEstimator _lateralEstimator = new( SignCalibrationSamples );
	private readonly SignEstimator _yawEstimator = new( SignCalibrationSamples );

	private bool _forwardLogged = false;
	private bool _lateralLogged = false;
	private bool _yawLogged = false;

	// vertical acceleration at rest: mean over a window while stationary decides whether gravity is included
	private double _restVerticalSum = 0.0;
	private int _restVerticalCount = 0;
	private bool _verticalResolved = false;
	private bool _verticalIncludesGravity = false;
	private float _upSign = 1f;

	// signs applied to the AMS2 body-frame axes to reach iRacing's frame (forward-positive X, left-positive
	// Y, up-positive Z, CCW-positive yaw)
	private float ForwardSign => _forwardEstimator.Resolved ? _forwardEstimator.Sign : -1f;
	private float LateralSign => _lateralEstimator.Resolved ? -_lateralEstimator.Sign : -1f;
	private float YawRateSign => _yawEstimator.Resolved ? -_yawEstimator.Sign : 1f;

	private void ResetCalibration()
	{
		_forwardEstimator.Reset();
		_lateralEstimator.Reset();
		_yawEstimator.Reset();

		_forwardLogged = false;
		_lateralLogged = false;
		_yawLogged = false;

		_restVerticalSum = 0.0;
		_restVerticalCount = 0;
		_verticalResolved = false;
		_verticalIncludesGravity = false;
		_upSign = 1f;
	}

	// runs at 60 Hz on the playout timer worker thread; the only allocations are the one-off log lines
	private void CalibrateAxes()
	{
		var speed = _data.mSpeed;
		var steering = _data.mSteering;

		var gameState = (Ams2GameState) _data.mGameState;

		if ( gameState != Ams2GameState.InGamePlaying )
		{
			return;
		}

		// longitudinal: the body-frame velocity along Z while rolling forward in a forward gear

		if ( !_forwardEstimator.Resolved && ( _data.mGear >= 1 ) && ( speed > 3f ) )
		{
			_forwardEstimator.Add( _data.mLocalVelocity[ ForwardIndex ] );

			if ( _forwardEstimator.Resolved && !_forwardLogged )
			{
				_forwardLogged = true;

				App.Instance!.Logger.WriteLine( $"[Automobilista2Bridge] Axis calibration: forward is {( ( _forwardEstimator.Sign > 0 ) ? "+Z" : "-Z" )}" );
			}
		}

		// lateral and yaw: correlate the lateral acceleration and the yaw rate with the steering input

		if ( ( Math.Abs( steering ) > 0.15f ) && ( speed > 8f ) )
		{
			var lateralAcceleration = _data.mLocalAcceleration[ LateralIndex ];
			var yawRate = _data.mAngularVelocity[ YawIndex ];

			if ( !_lateralEstimator.Resolved && ( Math.Abs( lateralAcceleration ) > 1.5f ) )
			{
				_lateralEstimator.Add( steering * lateralAcceleration );

				if ( _lateralEstimator.Resolved && !_lateralLogged )
				{
					_lateralLogged = true;

					App.Instance!.Logger.WriteLine( $"[Automobilista2Bridge] Axis calibration: +X is {( ( _lateralEstimator.Sign > 0 ) ? "right" : "left" )}" );
				}
			}

			if ( !_yawEstimator.Resolved && ( Math.Abs( yawRate ) > 0.05f ) )
			{
				_yawEstimator.Add( steering * yawRate );

				if ( _yawEstimator.Resolved && !_yawLogged )
				{
					_yawLogged = true;

					App.Instance!.Logger.WriteLine( $"[Automobilista2Bridge] Axis calibration: yaw rate is {( ( _yawEstimator.Sign > 0 ) ? "clockwise" : "counter-clockwise" )}-positive" );
				}
			}
		}

		// vertical: at rest the body-frame Y acceleration is either ~0 (gravity excluded) or ~±9.81

		if ( !_verticalResolved && ( speed < 0.5f ) )
		{
			_restVerticalSum += _data.mLocalAcceleration[ VerticalIndex ];
			_restVerticalCount++;

			if ( _restVerticalCount >= 120 )
			{
				var mean = _restVerticalSum / _restVerticalCount;

				_verticalResolved = true;
				_verticalIncludesGravity = Math.Abs( mean ) > 5.0;
				_upSign = ( _verticalIncludesGravity && ( mean < 0.0 ) ) ? -1f : 1f;

				App.Instance!.Logger.WriteLine( $"[Automobilista2Bridge] Axis calibration: vertical acceleration at rest is {mean:F2} m/s^2 ({( _verticalIncludesGravity ? "includes gravity" : "excludes gravity" )}, up is {( ( _upSign > 0f ) ? "+Y" : "-Y" )})" );
			}
		}
	}

	private float ComputeVertAccel()
	{
		var vertical = _data.mLocalAcceleration[ VerticalIndex ];

		if ( _verticalResolved && _verticalIncludesGravity )
		{
			return _upSign * vertical;
		}

		// gravity excluded (or not yet known - the "excluded" assumption only errs by one G before a car has
		// been seen at rest) - iRacing reports +9.81 at rest
		return (float) ( vertical + Gravity );
	}

	#endregion

	public override void Start()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Automobilista2Bridge] Start >>>" );

		if ( Ams2Constants.StructSize != Ams2Constants.ExpectedStructSize )
		{
			throw new InvalidOperationException( $"Ams2SharedMemory is {Ams2Constants.StructSize} bytes, expected {Ams2Constants.ExpectedStructSize} - the struct transcription no longer matches the header" );
		}

		app.Logger.WriteLine( "[Automobilista2Bridge] AMS2 exposes no steering torque - the wheelbase stays with the game's own force feedback; MAIRA drives the pedals, wind, tensioner and the other effects only" );

		_varTable = new GameBridgeVarTable();

		DataSource = _varTable.DataSource;

		_provider = CreateProvider();

		_blockBuffer = new byte[ Ams2Constants.StructSize ];

		ResetState();

		app.Logger.WriteLine( "[Automobilista2Bridge] <<< Start" );
	}

	public override void Stop()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Automobilista2Bridge] Stop >>>" );

		lock ( _pumpLock )
		{
			_provider?.Close();
			_provider = null;

			_providerOpen = false;
		}

		app.Logger.WriteLine( "[Automobilista2Bridge] <<< Stop" );
	}

	protected virtual Ams2DataProvider CreateProvider()
	{
		return new Ams2LiveDataProvider();
	}

	private void ResetState()
	{
		_providerOpen = false;
		_lastOpenAttemptSeconds = double.MinValue;
		_nextSubSampleSeconds = 0.0;

		_lastMapRecycleSeconds = double.MinValue;
		_recyclingStaleMap = false;

		_data = default;
		_hasData = false;

		_subSampleIndex = 0;
		Array.Clear( _shockSamples );

		_previousSequenceNumber = uint.MaxValue;
		_versionLogged = false;

		LastDataActivitySeconds = double.MinValue;

		_sessionStartSeconds = double.NaN;

		_steeringScale = 0.0;
		_steeringScaleSampleCount = 0;

		_lastSessionInfoSignature = string.Empty;
		_lastSessionInfoUpdateTime = double.MinValue;

		ResetCalibration();
	}

	#region pump

	// Called from the playout timer worker thread (~360 Hz, kernel-scheduled) immediately before the racing
	// wheel update. The 360 Hz sub-sample schedule is kept internally; zero, one, or occasionally two
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

				if ( !_recyclingStaleMap )
				{
					App.Instance!.Logger.WriteLine( "[Automobilista2Bridge] Shared memory opened - pumping" );
				}
			}

			// if the game ever recreates its file mapping, holding the handle to the old section means reading
			// its orphaned frozen copy forever - so while the output has been frozen for a while, periodically
			// drop and reopen the mapping to reattach to the live section
			if ( ( LastDataActivitySeconds != double.MinValue ) && ( totalSeconds - LastDataActivitySeconds >= StaleMapRecycleSeconds ) && ( totalSeconds - _lastMapRecycleSeconds >= StaleMapRecycleSeconds ) )
			{
				if ( !_recyclingStaleMap )
				{
					_recyclingStaleMap = true;

					App.Instance!.Logger.WriteLine( "[Automobilista2Bridge] Telemetry stopped advancing - recycling the shared memory mapping until it resumes" );
				}

				_lastMapRecycleSeconds = totalSeconds;

				_provider.Close();

				_providerOpen = false;

				return;
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

	// Runs at 360 Hz. Every tick takes a coherent copy of the block when one is available (a torn or
	// mid-write read keeps the previous copy) and files the shock velocities into the current 360 Hz slot;
	// once six slots are filled (60 Hz) a full frame is committed.
	private void ProcessSubSample( double pumpSeconds )
	{
		var provider = _provider!;

		if ( provider.TryReadBlock( _blockBuffer ) )
		{
			_data = MemoryMarshal.Read<Ams2SharedMemory>( _blockBuffer );

			_hasData = true;

			if ( _data.mSequenceNumber != _previousSequenceNumber )
			{
				_previousSequenceNumber = _data.mSequenceNumber;

				LastDataActivitySeconds = pumpSeconds;

				if ( _recyclingStaleMap )
				{
					_recyclingStaleMap = false;

					App.Instance!.Logger.WriteLine( "[Automobilista2Bridge] Telemetry resumed - reattached to the live shared memory mapping" );
				}

				if ( !_versionLogged )
				{
					_versionLogged = true;

					App.Instance!.Logger.WriteLine( $"[Automobilista2Bridge] Shared memory version {_data.mVersion} (expected {Ams2Constants.SharedMemoryVersion}), game build {_data.mBuildVersionNumber}" );

					if ( _data.mVersion != Ams2Constants.SharedMemoryVersion )
					{
						App.Instance!.Logger.WriteLine( "[Automobilista2Bridge] WARNING: shared memory version differs from the transcribed header - field offsets may be wrong" );
					}
				}
			}
		}

		if ( _hasData )
		{
			for ( var w = 0; w < 4; w++ )
			{
				_shockSamples[ w, _subSampleIndex ] = _data.mSuspensionVelocity[ w ];
			}
		}

		if ( _subSampleIndex == SamplesPerFrame - 1 )
		{
			if ( _hasData )
			{
				if ( double.IsNaN( _sessionStartSeconds ) )
				{
					_sessionStartSeconds = pumpSeconds;
				}

				CalibrateAxes();

				UpdateSessionInfo( pumpSeconds );

				WriteTelemetryFrame( pumpSeconds );

				_varTable!.DataSource.CommitFrame();
			}
		}

		_subSampleIndex = ( _subSampleIndex + 1 ) % SamplesPerFrame;
	}

	#endregion

	#region telemetry frame mapping

	private void WriteTelemetryFrame( double pumpSeconds )
	{
		var varTable = _varTable!;
		var dataSource = varTable.DataSource;

		var gameState = (Ams2GameState) _data.mGameState;
		var pitMode = (Ams2PitMode) _data.mPitMode;
		var carFlags = (Ams2CarFlags) _data.mCarFlags;

		var numParticipants = Math.Clamp( _data.mNumParticipants, 0, GameBridgeVarTable.MaxNumCars );
		var playerIndex = Math.Clamp( _data.mViewedParticipantIndex, 0, GameBridgeVarTable.MaxNumCars - 1 );
		var player = _data.mParticipantInfo[ playerIndex ];
		var playerActive = ( numParticipants > 0 ) && ( player.mIsActive != 0 );

		var isPlaying = gameState == Ams2GameState.InGamePlaying;
		var isReplay = ( gameState == Ams2GameState.InGameReplay ) || ( gameState == Ams2GameState.FrontEndReplay );
		var isOnTrack = isPlaying && playerActive;
		var isOnPitRoad = pitMode != Ams2PitMode.None;

		// pedals and gear - mClutch is pedal travel (1 = pressed), iRacing's Clutch is engagement (1 = pedal up)

		dataSource.SetFloat( varTable.Throttle, _data.mThrottle );
		dataSource.SetFloat( varTable.ThrottleRaw, _data.mUnfilteredThrottle );
		dataSource.SetFloat( varTable.Brake, _data.mBrake );
		dataSource.SetFloat( varTable.BrakeRaw, _data.mUnfilteredBrake );
		dataSource.SetFloat( varTable.Clutch, 1f - Math.Clamp( _data.mClutch, 0f, 1f ) );
		dataSource.SetBool( varTable.BrakeABSactive, _data.mAntiLockActive != 0 );
		dataSource.SetInt( varTable.Gear, _data.mGear );
		dataSource.SetFloat( varTable.RPM, _data.mRpm );

		// motion - body-frame signs come from the runtime calibration (see the class comment)

		var forwardSign = ForwardSign;
		var lateralSign = LateralSign;

		dataSource.SetFloat( varTable.Speed, _data.mSpeed );
		dataSource.SetFloat( varTable.VelocityX, forwardSign * _data.mLocalVelocity[ ForwardIndex ] );
		dataSource.SetFloat( varTable.VelocityY, lateralSign * _data.mLocalVelocity[ LateralIndex ] );
		dataSource.SetFloat( varTable.LongAccel, forwardSign * _data.mLocalAcceleration[ ForwardIndex ] );
		dataSource.SetFloat( varTable.LatAccel, lateralSign * _data.mLocalAcceleration[ LateralIndex ] );
		dataSource.SetFloat( varTable.VertAccel, ComputeVertAccel() );
		dataSource.SetFloat( varTable.YawRate, YawRateSign * _data.mAngularVelocity[ YawIndex ] );

		var yaw = _data.mOrientation[ YawIndex ];

		dataSource.SetFloat( varTable.Yaw, yaw );
		dataSource.SetFloat( varTable.YawNorth, yaw );
		dataSource.SetFloat( varTable.Pitch, _data.mOrientation[ PitchIndex ] );
		dataSource.SetFloat( varTable.Roll, _data.mOrientation[ RollIndex ] );

		// steering - and the whole point of this bridge's shape: no torque, game FFB reported as ENABLED so
		// RacingWheel suspends MAIRA's own wheel output and the wheelbase stays with the game

		var steeringRangeRadians = DefaultSteeringRangeDegrees * Math.PI / 180.0;

		dataSource.SetFloat( varTable.SteeringWheelAngle, ComputeSteeringWheelAngle( _data.mSteering, steeringRangeRadians ) );
		dataSource.SetFloat( varTable.SteeringWheelAngleMax, (float) steeringRangeRadians );
		dataSource.SetBool( varTable.SteeringFFBEnabled, true );

		for ( var i = 0; i < SamplesPerFrame; i++ )
		{
			dataSource.SetFloat( varTable.SteeringWheelTorque_ST, 0f, i );
		}

		dataSource.SetFloat( varTable.SteeringWheelTorque, 0f );

		// suspension - AMS2 publishes the per-wheel pushrod velocity itself (FL, FR, RL, RR)

		for ( var i = 0; i < SamplesPerFrame; i++ )
		{
			dataSource.SetFloat( varTable.LFshockVel_ST, _shockSamples[ 0, i ], i );
			dataSource.SetFloat( varTable.RFshockVel_ST, _shockSamples[ 1, i ], i );
			dataSource.SetFloat( varTable.LRshockVel_ST, _shockSamples[ 2, i ], i );
			dataSource.SetFloat( varTable.RRshockVel_ST, _shockSamples[ 3, i ], i );
		}

		// fuel - mFuelLevel is a fraction of mFuelCapacity

		var fuelFraction = Math.Clamp( _data.mFuelLevel, 0f, 1f );

		dataSource.SetFloat( varTable.FuelLevel, fuelFraction * Math.Max( 0f, _data.mFuelCapacity ) );
		dataSource.SetFloat( varTable.FuelLevelPct, fuelFraction );
		dataSource.SetFloat( varTable.FuelUsePerHour, 0f );

		// session and lap state

		var trackLength = Math.Max( 1.0, _data.mTrackLength );

		var lapDistance = Math.Max( 0f, player.mCurrentLapDistance );
		var lapDistPct = (float) Math.Clamp( lapDistance / trackLength, 0.0, 1.0 );

		var lapsCompleted = (int) Math.Min( player.mLapsCompleted, int.MaxValue );
		var lapsInEvent = (int) Math.Min( _data.mLapsInEvent, int.MaxValue );

		dataSource.SetDouble( varTable.SessionTime, pumpSeconds - _sessionStartSeconds );
		dataSource.SetDouble( varTable.SessionTimeRemain, ( _data.mEventTimeRemaining >= 0f ) ? _data.mEventTimeRemaining / 1000.0 : IRacingSdkConst.UnlimitedTime );
		dataSource.SetInt( varTable.SessionNum, 0 );
		dataSource.SetInt( varTable.SessionState, MapSessionState( gameState ) );
		dataSource.SetBitField( varTable.SessionFlags, MapSessionFlags( (Ams2FlagColour) _data.mHighestFlagColour, isPlaying ) );
		dataSource.SetInt( varTable.SessionLapsRemainEx, ( lapsInEvent > 0 ) ? Math.Max( 0, lapsInEvent - lapsCompleted ) : IRacingSdkConst.UnlimitedLaps );

		dataSource.SetInt( varTable.Lap, Math.Max( 1, (int) Math.Min( player.mCurrentLap, int.MaxValue ) ) );
		dataSource.SetFloat( varTable.LapDist, lapDistance );
		dataSource.SetFloat( varTable.LapDistPct, lapDistPct );
		dataSource.SetFloat( varTable.LapBestLapTime, Math.Max( 0f, _data.mBestLapTime ) );
		dataSource.SetFloat( varTable.LapLastLapTime, Math.Max( 0f, _data.mLastLapTime ) );

		// player state

		var position = (int) Math.Min( player.mRacePosition, int.MaxValue );

		dataSource.SetBool( varTable.IsOnTrack, isOnTrack );
		dataSource.SetBool( varTable.OnPitRoad, isOnPitRoad );
		dataSource.SetBool( varTable.PitsOpen, true );
		dataSource.SetInt( varTable.PlayerCarIdx, playerIndex );
		dataSource.SetInt( varTable.PlayerCarPosition, position );
		dataSource.SetInt( varTable.PlayerCarClassPosition, position );
		dataSource.SetInt( varTable.PlayerCarMyIncidentCount, 0 );
		dataSource.SetInt( varTable.PlayerTrackSurface, MapTrackSurface( isPlaying, pitMode ) );
		dataSource.SetInt( varTable.PlayerTrackSurfaceMaterial, MapTrackSurfaceMaterial( (Ams2Terrain) _data.mTerrain[ 0 ] ) );

		// weather

		dataSource.SetBool( varTable.WeatherDeclaredWet, _data.mRainDensity > 0.05f );

		// engine warnings - only the pit speed limiter has a direct equivalent

		dataSource.SetBitField( varTable.EngineWarnings, carFlags.HasFlag( Ams2CarFlags.SpeedLimiter ) ? 0x10u : 0u );

		// fixed values with no AMS2 equivalent

		dataSource.SetInt( varTable.DisplayUnits, 1 );
		dataSource.SetFloat( varTable.FrameRate, 60f );
		dataSource.SetFloat( varTable.GpuUsage, 0f );
		dataSource.SetBool( varTable.IsReplayPlaying, isReplay );
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

		WriteCarIdxArrays( numParticipants, playerIndex, trackLength, lapDistance );
	}

	private void WriteCarIdxArrays( int numParticipants, int playerIndex, double trackLength, float playerLapDistance )
	{
		var varTable = _varTable!;
		var dataSource = varTable.DataSource;

		var carDistAhead = float.MaxValue;
		var carDistBehind = float.MaxValue;

		// best lap time

		Array.Clear( _carIdxFloatScratch );

		for ( var i = 0; i < numParticipants; i++ )
		{
			if ( _data.mParticipantInfo[ i ].mIsActive != 0 )
			{
				_carIdxFloatScratch[ i ] = Math.Max( 0f, _data.mFastestLapTimes[ i ] );
			}
		}

		dataSource.SetFloatArray( varTable.CarIdxBestLapTime, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		// est time / f2 time - not available

		Array.Clear( _carIdxFloatScratch );

		dataSource.SetFloatArray( varTable.CarIdxEstTime, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );
		dataSource.SetFloatArray( varTable.CarIdxF2Time, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		// laps started

		Array.Clear( _carIdxIntScratch );

		for ( var i = 0; i < numParticipants; i++ )
		{
			var participant = _data.mParticipantInfo[ i ];

			if ( participant.mIsActive != 0 )
			{
				_carIdxIntScratch[ i ] = Math.Max( 1, (int) Math.Min( participant.mCurrentLap, int.MaxValue ) );
			}
		}

		dataSource.SetIntArray( varTable.CarIdxLap, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		// laps completed

		Array.Clear( _carIdxIntScratch );

		for ( var i = 0; i < numParticipants; i++ )
		{
			var participant = _data.mParticipantInfo[ i ];

			if ( participant.mIsActive != 0 )
			{
				_carIdxIntScratch[ i ] = (int) Math.Min( participant.mLapsCompleted, int.MaxValue );
			}
		}

		dataSource.SetIntArray( varTable.CarIdxLapCompleted, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		// lap dist pct + car dist ahead/behind

		Array.Clear( _carIdxFloatScratch );

		for ( var i = 0; i < numParticipants; i++ )
		{
			var participant = _data.mParticipantInfo[ i ];

			if ( participant.mIsActive == 0 )
			{
				continue;
			}

			var lapDistance = Math.Max( 0f, participant.mCurrentLapDistance );

			_carIdxFloatScratch[ i ] = (float) Math.Clamp( lapDistance / trackLength, 0.0, 1.0 );

			if ( ( i != playerIndex ) && ( (Ams2PitMode) _data.mPitModes[ i ] == Ams2PitMode.None ) )
			{
				var distance = (double) lapDistance - playerLapDistance;

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

		for ( var i = 0; i < numParticipants; i++ )
		{
			var participant = _data.mParticipantInfo[ i ];

			if ( participant.mIsActive != 0 )
			{
				_carIdxIntScratch[ i ] = (int) Math.Min( participant.mRacePosition, int.MaxValue );
			}
		}

		dataSource.SetIntArray( varTable.CarIdxPosition, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		// on pit road, tire compound, session flags

		for ( var i = 0; i < GameBridgeVarTable.MaxNumCars; i++ )
		{
			var onPitRoad = ( i < numParticipants ) && ( (Ams2PitMode) _data.mPitModes[ i ] != Ams2PitMode.None );

			dataSource.SetBool( varTable.CarIdxOnPitRoad, onPitRoad, i );
			dataSource.SetBitField( varTable.CarIdxSessionFlags, 0u, i );
		}

		Array.Clear( _carIdxIntScratch );

		dataSource.SetIntArray( varTable.CarIdxTireCompound, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );
	}

	private static int MapSessionState( Ams2GameState gameState )
	{
		return gameState switch
		{
			Ams2GameState.Exited or Ams2GameState.FrontEnd or Ams2GameState.FrontEndReplay => (int) IRacingSdkEnum.SessionState.Invalid,
			Ams2GameState.InGamePaused or Ams2GameState.InGameInMenuTimeTicking or Ams2GameState.InGameRestarting => (int) IRacingSdkEnum.SessionState.GetInCar,
			_ => (int) IRacingSdkEnum.SessionState.Racing
		};
	}

	// iRacing session flag bits (irsdk_Flags): checkered 0x1, white 0x2, green 0x4, yellow 0x8, red 0x10,
	// blue 0x20, yellow waving 0x100, black 0x10000, furled 0x80000, repair 0x100000
	private static uint MapSessionFlags( Ams2FlagColour flagColour, bool isPlaying )
	{
		if ( !isPlaying )
		{
			return 0u;
		}

		return flagColour switch
		{
			Ams2FlagColour.Green => 0x00000004u,
			Ams2FlagColour.Blue => 0x00000020u,
			Ams2FlagColour.WhiteSlowCar or Ams2FlagColour.WhiteFinalLap => 0x00000002u,
			Ams2FlagColour.Red => 0x00000010u,
			Ams2FlagColour.Yellow => 0x00000008u,
			Ams2FlagColour.DoubleYellow => 0x00000100u,
			Ams2FlagColour.BlackAndWhite => 0x00080000u,
			Ams2FlagColour.BlackOrangeCircle => 0x00100000u,
			Ams2FlagColour.Black => 0x00010000u,
			Ams2FlagColour.Chequered => 0x00000001u,
			_ => 0x00000004u
		};
	}

	private static int MapTrackSurface( bool isPlaying, Ams2PitMode pitMode )
	{
		if ( !isPlaying )
		{
			return (int) IRacingSdkEnum.TrkLoc.NotInWorld;
		}

		return pitMode switch
		{
			Ams2PitMode.InPit or Ams2PitMode.InGarage => (int) IRacingSdkEnum.TrkLoc.InPitStall,
			Ams2PitMode.DrivingIntoPits or Ams2PitMode.DrivingOutOfPits or Ams2PitMode.DrivingOutOfGarage => (int) IRacingSdkEnum.TrkLoc.AproachingPits,
			_ => (int) IRacingSdkEnum.TrkLoc.OnTrack
		};
	}

	// the material feeds MAIRA's peak-torque gate (asphalt through racing dirt count as "on a surface") and the
	// G-tensioner's surface logic; the front-left tyre stands in for the car
	private static int MapTrackSurfaceMaterial( Ams2Terrain terrain )
	{
		return terrain switch
		{
			Ams2Terrain.Road or Ams2Terrain.RallyTarmac or Ams2Terrain.RunoffRoad or Ams2Terrain.DamagedRoad1 => (int) IRacingSdkEnum.TrkSurf.Asphalt1Material,
			Ams2Terrain.LowGripRoad or Ams2Terrain.Marbles or Ams2Terrain.IceRoad => (int) IRacingSdkEnum.TrkSurf.Asphalt2Material,
			Ams2Terrain.BumpyRoad1 or Ams2Terrain.BumpyRoad2 or Ams2Terrain.BumpyRoad3 => (int) IRacingSdkEnum.TrkSurf.Asphalt3Material,
			Ams2Terrain.Pavement or Ams2Terrain.Cobbles or Ams2Terrain.BumpyCobbles or Ams2Terrain.TrainTrackRoad => (int) IRacingSdkEnum.TrkSurf.Concrete1Material,
			Ams2Terrain.PaintConcrete or Ams2Terrain.PaintConcreteIllegal or Ams2Terrain.IllegalStrip => (int) IRacingSdkEnum.TrkSurf.Paint1Material,
			Ams2Terrain.RumbleStrips or Ams2Terrain.ExitRumbleStrips or Ams2Terrain.B1Rumbles or Ams2Terrain.B2Rumbles or Ams2Terrain.Drains => (int) IRacingSdkEnum.TrkSurf.Rumble1Material,
			Ams2Terrain.Grass or Ams2Terrain.GrassyBerms or Ams2Terrain.LongGrass or Ams2Terrain.SlopeGrass or Ams2Terrain.DryVerge => (int) IRacingSdkEnum.TrkSurf.Grass1Material,
			Ams2Terrain.Grasscrete => (int) IRacingSdkEnum.TrkSurf.GrasscreteMaterial,
			Ams2Terrain.Astroturf => (int) IRacingSdkEnum.TrkSurf.AstroturfMaterial,
			Ams2Terrain.Gravel or Ams2Terrain.BumpyGravel => (int) IRacingSdkEnum.TrkSurf.Gravel1Material,
			Ams2Terrain.Sand or Ams2Terrain.BumpySand or Ams2Terrain.RoughSandMedium or Ams2Terrain.RoughSandHeavy or Ams2Terrain.SandRoad => (int) IRacingSdkEnum.TrkSurf.SandMaterial,
			Ams2Terrain.DirtRoad or Ams2Terrain.BumpyDirtRoad or Ams2Terrain.BakedClay => (int) IRacingSdkEnum.TrkSurf.RacingDirt1Material,
			Ams2Terrain.Dirt or Ams2Terrain.BumpyDirt or Ams2Terrain.DirtBank => (int) IRacingSdkEnum.TrkSurf.Dirt3Material,
			_ => (int) IRacingSdkEnum.TrkSurf.UndefinedMaterial
		};
	}

	#endregion

	#region steering wheel angle

	private const double SteeringScaleAlpha = 0.05;
	private const int SteeringScaleMinSamples = 100;
	private const double SteeringScaleMinMagnitude = 1.0;
	private const double SteeringScaleMaxMagnitude = 16.0;

	// same reconstruction as the Assetto Corsa bridge: the game clamps its steering value at full lock, so the
	// PHYSICAL wheel angle is rebuilt from the DirectInput axis, calibrated against the game's angle in the
	// linear region
	private float ComputeSteeringWheelAngle( double gameSteering, double steeringRangeRadians )
	{
		var gameAngle = SteeringAngleSign * gameSteering * steeringRangeRadians * 0.5;

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

	#region session info

	private void UpdateSessionInfo( double pumpSeconds )
	{
		// throttle first - the signature below allocates, so it is only built at 1 Hz instead of every frame
		if ( pumpSeconds - _lastSessionInfoUpdateTime < 1.0 )
		{
			return;
		}

		_lastSessionInfoUpdateTime = pumpSeconds;

		var numParticipants = Math.Clamp( _data.mNumParticipants, 0, GameBridgeVarTable.MaxNumCars );
		var playerIndex = Math.Clamp( _data.mViewedParticipantIndex, 0, GameBridgeVarTable.MaxNumCars - 1 );

		var track = ReadString( _data.mTranslatedTrackLocation );
		var trackVariation = ReadString( _data.mTranslatedTrackVariation );

		if ( track.Length == 0 )
		{
			track = ReadString( _data.mTrackLocation );
		}

		if ( trackVariation.Length == 0 )
		{
			trackVariation = ReadString( _data.mTrackVariation );
		}

		var carName = ReadString( _data.mCarName );

		_signatureBuilder.Clear();
		_signatureBuilder.Append( track ).Append( '|' ).Append( trackVariation ).Append( '|' ).Append( carName ).Append( '|' );
		_signatureBuilder.Append( _data.mMaxRPM ).Append( '|' ).Append( _data.mNumGears ).Append( '|' ).Append( _data.mSessionState ).Append( '|' );
		_signatureBuilder.Append( numParticipants ).Append( '|' ).Append( playerIndex );

		for ( var i = 0; i < numParticipants; i++ )
		{
			var participant = _data.mParticipantInfo[ i ];

			if ( participant.mIsActive != 0 )
			{
				_signatureBuilder.Append( '|' ).Append( i ).Append( ':' ).Append( ReadString( participant.mName ) );
			}
		}

		var signature = _signatureBuilder.ToString();

		if ( signature == _lastSessionInfoSignature )
		{
			return;
		}

		_lastSessionInfoSignature = signature;

		var maxRpm = Math.Max( 0f, _data.mMaxRPM );

		var builder = new GameBridgeSessionInfoBuilder
		{
			TrackDisplayName = track,
			TrackConfigName = trackVariation,
			TrackLengthInKm = (float) ( Math.Max( 0.0, _data.mTrackLength ) / 1000.0 ),
			SeriesID = 0,
			LeagueID = 0,
			SessionID = 0,
			TimeOfDay = "12:00 pm",
			DriverCarIdx = playerIndex,
			DriverSetupName = "bridge",
			DriverCarGearNumForward = ( _data.mNumGears > 0 ) ? _data.mNumGears : 6,
			DriverCarRedLine = maxRpm,
			DriverCarSLFirstRPM = maxRpm * 0.88f,
			DriverCarSLShiftRPM = maxRpm * 0.96f,
			DriverCarSLBlinkRPM = maxRpm * 0.98f
		};

		for ( var i = 0; i < numParticipants; i++ )
		{
			var participant = _data.mParticipantInfo[ i ];

			if ( participant.mIsActive == 0 )
			{
				continue;
			}

			var participantCar = ( i == playerIndex ) ? carName : ReadString( _data.mCarNames[ i ] );

			builder.Drivers.Add( new GameBridgeSessionInfoBuilder.DriverModel
			{
				CarIdx = i,
				UserName = ReadString( participant.mName ),
				UserID = i,
				CarNumber = i.ToString(),
				CarScreenName = participantCar,
				CarPath = participantCar,
				CarClassID = 0,
				CarIsPaceCar = 0,
				IRating = 0,
				IsSpectator = 0,
				TeamID = 0
			} );
		}

		builder.Sessions.Add( new GameBridgeSessionInfoBuilder.SessionModel
		{
			SessionNum = 0,
			SessionType = MapSessionType( (Ams2SessionState) _data.mSessionState )
		} );

		_varTable!.DataSource.SetSessionInfo( builder.ToYaml() );
	}

	private static string MapSessionType( Ams2SessionState sessionState )
	{
		return sessionState switch
		{
			Ams2SessionState.Race or Ams2SessionState.FormationLap => "Race",
			Ams2SessionState.Qualify or Ams2SessionState.TimeAttack => "Lone Qualify",
			_ => "Practice"
		};
	}

	#endregion

	private static string ReadString( ReadOnlySpan<byte> bytes )
	{
		var length = bytes.IndexOf( (byte) 0 );

		if ( length == -1 )
		{
			length = bytes.Length;
		}

		return Encoding.UTF8.GetString( bytes[ ..length ] );
	}
}
