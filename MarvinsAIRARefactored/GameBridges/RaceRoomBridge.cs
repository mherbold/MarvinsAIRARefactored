
using System.Runtime.InteropServices;
using System.Text;

using IRSDKSharper;

using MarvinsAIRARefactored.GameBridges.R3e;

namespace MarvinsAIRARefactored.GameBridges;

/// <summary>
/// RaceRoom Racing Experience bridge. RaceRoom exposes a native "$R3E" shared memory map (no plugin needed)
/// carrying a single r3e_shared struct - see R3eData.cs. Unlike the Kunos games it has a real steering torque
/// signal (player.steering_force, "total steering force coming through steering bars") plus direct per-wheel
/// suspension velocities, a proper per-car steering lock (steer_wheel_range_degrees), an ABS-active flag, and
/// per-tire surface materials. The local frame is documented as +X = left, +Y = up, +Z = back - the same
/// frame as rFactor 2 / LMU - and the carried-over sign conventions plus the steering_force scale and the
/// 400 Hz map update rate were confirmed against a RaceRoom capture (Mantorp Park, Alpine A110 Cup,
/// 2026-07-22) - see the constants below. Only the shock velocity sign remains assumed.
/// </summary>
public class RaceRoomBridge : GameBridgeAdapter
{
	public override string GameName => "RaceRoom Racing Experience";
	public override string LocalizationKey => "RaceRoomRacingExperience";
	public override string[] ProcessNames => [ "RRRE64", "RRRE" ];
	public override GameBridgeCapabilities Capabilities => GameBridgeCapabilities.SteeringWheelTorque360Hz | GameBridgeCapabilities.ShockVelocities;

	public override bool IsImplemented => true;

	// sign conventions relative to iRacing (CCW/left-positive for steering, torque, lateral velocity, and
	// yaw) - carried over from the capture-validated rF2/LMU bridges and confirmed against a RaceRoom
	// capture (2026-07-22): steer-vs-yaw corr +0.97, steer-vs-latAccel corr -0.89, steer-vs-steering_force
	// corr -0.91 while driving, so steering input, torque, and yaw rate are right-positive in R3E (flip)
	// and lateral matches iRacing directly (no flip).
	private const float SteeringAngleSign = -1f;
	private const float SteeringTorqueSign = -1f;
	private const float LateralSign = 1f;
	private const float YawRateSign = -1f;
	private const float YawSign = 1f;
	private const float ShockVelocitySign = 1f;

	// the capture showed steering_force == steering_force_percentage * 11500 exactly (constant ratio across
	// 18k+ samples), so 11500 is the game's full-scale reference for the raw force. Mapping that full scale
	// to a nominal 50 Nm puts RaceRoom's torque in the same magnitude family as the LMU/rF2 shaft torque
	// (the capture's Alpine A110 Cup peaked at 44% = ~22 Nm); the auto max force then adapts per car as usual
	private const float SteeringForceToNm = 50f / 11500f;

	private const double Gravity = 9.80665;
	private const float RadiansPerSecondToRpm = (float) ( 60.0 / ( 2.0 * Math.PI ) );

	// RaceRoom's physics runs at 400 Hz internally (game_simulation_ticks is 1/400th of a second) and the
	// capture confirmed the shared memory map updates at that full 400 Hz (median tick delta of 1, with
	// steering_force changing on virtually every tick), so each 360 Hz sub-sample gets a genuinely fresh
	// torque reading - the best source rate of all the bridges
	private const int SamplesPerFrame = GameBridgeVarTable.SamplesPerFrame360Hz;
	private const int SubSampleFrequency = 360;

	private const int DriversParseInterval = 20;

	// the soft-lock fallback when the map exposes no per-car range (steer_wheel_range_degrees is normally
	// populated, so this should rarely be used)
	private const float DefaultSteeringRangeDegrees = 900f;

	// map layout constants - the 10 header ints put r3e_playerdata at offset 40; game_simulation_time sits
	// 8 bytes into it (after user_id + game_simulation_ticks), read directly as the cheap update detector
	private const int PlayerDataOffset = 40;
	private const int SimulationTimeOffset = PlayerDataOffset + 8;
	private const int MapSize = 128 * 1024;

	private GameBridgeVarTable? _varTable = null;
	private R3eDataProvider? _provider = null;

	// the bridge is pumped from the multimedia timer worker thread (see Pump); this lock keeps a Stop from
	// a background task from closing the provider while a pump is mid-read
	private readonly object _pumpLock = new();
	private bool _providerOpen = false;
	private double _lastOpenAttemptSeconds = double.MinValue;
	private double _nextSubSampleSeconds = 0.0;

	private byte[] _sharedBuffer = [];

	private long _frameCounter = 0;

	private bool _hasShared = false;
	private R3eShared _shared;
	private double _previousSimulationTime = 0.0;
	private double _previousFuel = 0.0;
	private float _fuelUsePerHour = 0f;
	private bool _layoutChecked = false;

	private bool _hasDrivers = false;
	private R3eDriverData[] _drivers = [];
	private int _driverDataArrayOffset = 0;
	private int _driverDataSize = 0;

	// per-frame accumulators for the six real 360 Hz sub-samples (torque + per-wheel shock velocity); the
	// current values are held between map updates by the game_simulation_time gate
	private int _subSampleIndex = 0;
	private readonly float[] _torqueSamples = new float[ SamplesPerFrame ];
	private readonly float[,] _shockSamples = new float[ 4, SamplesPerFrame ];
	private readonly float[] _lastShockVelocities = new float[ 4 ];
	private float _lastTorque = 0f;

	private readonly Dictionary<int, int> _carIdxBySlotId = [];

	private double _steeringScale = 0.0;
	private int _steeringScaleSampleCount = 0;

	private string _lastSessionInfoSignature = string.Empty;
	private double _lastSessionInfoUpdateTime = double.MinValue;

	private readonly float[] _carIdxFloatScratch = new float[ GameBridgeVarTable.MaxNumCars ];
	private readonly int[] _carIdxIntScratch = new int[ GameBridgeVarTable.MaxNumCars ];

	public override void Start()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RaceRoomBridge] Start >>>" );

		_varTable = new GameBridgeVarTable();

		DataSource = _varTable.DataSource;

		_provider = CreateProvider();

		_sharedBuffer = new byte[ MapSize ];

		ResetState();

		app.Logger.WriteLine( "[RaceRoomBridge] <<< Start" );
	}

	public override void Stop()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RaceRoomBridge] Stop >>>" );

		lock ( _pumpLock )
		{
			_provider?.Close();
			_provider = null;

			_providerOpen = false;
		}

		app.Logger.WriteLine( "[RaceRoomBridge] <<< Stop" );
	}

	protected virtual R3eDataProvider CreateProvider()
	{
		return new R3eLiveDataProvider();
	}

	private void ResetState()
	{
		_providerOpen = false;
		_lastOpenAttemptSeconds = double.MinValue;
		_nextSubSampleSeconds = 0.0;

		_frameCounter = 0;

		_hasShared = false;
		_hasDrivers = false;

		_previousSimulationTime = 0.0;
		_previousFuel = 0.0;
		_fuelUsePerHour = 0f;
		_layoutChecked = false;

		_drivers = [];
		_driverDataArrayOffset = 0;
		_driverDataSize = 0;

		_subSampleIndex = 0;
		Array.Clear( _torqueSamples );
		Array.Clear( _shockSamples );
		Array.Clear( _lastShockVelocities );
		_lastTorque = 0f;

		_carIdxBySlotId.Clear();

		_steeringScale = 0.0;
		_steeringScaleSampleCount = 0;

		_lastSessionInfoSignature = string.Empty;
		_lastSessionInfoUpdateTime = double.MinValue;
	}

	#region pump

	// Called from the multimedia timer worker thread (~500 Hz, kernel-scheduled) immediately before the
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

				App.Instance!.Logger.WriteLine( "[RaceRoomBridge] Shared memory opened - pumping" );
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

	// Runs at 360 Hz. Every tick refreshes the shared struct (UpdateShared re-parses only when the map's
	// game_simulation_time advanced, holding its values otherwise) and captures the current torque / shock
	// into the 360 Hz slot; once six slots are filled (60 Hz) the driver array is refreshed and a full frame
	// is committed.
	private void ProcessSubSample( double pumpSeconds )
	{
		if ( _provider?.TryReadBuffer( R3eBufferType.Shared, _sharedBuffer ) ?? false )
		{
			UpdateShared();
		}

		_torqueSamples[ _subSampleIndex ] = _lastTorque;

		for ( var wheelIndex = 0; wheelIndex < 4; wheelIndex++ )
		{
			_shockSamples[ wheelIndex, _subSampleIndex ] = _lastShockVelocities[ wheelIndex ];
		}

		if ( _subSampleIndex == SamplesPerFrame - 1 )
		{
			_frameCounter++;

			if ( _hasShared && ( ( _frameCounter % DriversParseInterval == 1 ) || !_hasDrivers ) )
			{
				UpdateDrivers();
			}

			if ( _hasShared )
			{
				UpdateSessionInfo( pumpSeconds );

				WriteTelemetryFrame();

				_varTable!.DataSource.CommitFrame();
			}
		}

		_subSampleIndex = ( _subSampleIndex + 1 ) % SamplesPerFrame;
	}

	#endregion

	#region shared map parsing

	private void UpdateShared()
	{
		// cheap direct read to skip unchanged maps before the (allocating) full struct marshal - at the
		// 360 Hz pump rate the map is unchanged on most sub-samples, so this holds the marshal to the map's
		// real update rate
		var simulationTime = BitConverter.ToDouble( _sharedBuffer, SimulationTimeOffset );

		if ( simulationTime == _previousSimulationTime )
		{
			return;
		}

		var shared = ReadStruct<R3eShared>( _sharedBuffer, 0 );

		CheckLayout( shared );

		var deltaSeconds = simulationTime - _previousSimulationTime;

		_lastTorque = SteeringTorqueSign * (float) shared.player.steering_force * SteeringForceToNm;

		// RaceRoom hands us the suspension velocities directly (no differentiation needed) - the sign
		// convention relative to iRacing's shock velocity is unvalidated, hence the constant
		for ( var wheelIndex = 0; wheelIndex < 4; wheelIndex++ )
		{
			_lastShockVelocities[ wheelIndex ] = ShockVelocitySign * (float) shared.player.suspension_velocity[ wheelIndex ];
		}

		if ( _hasShared && ( deltaSeconds > 0.0 ) && ( deltaSeconds < 1.0 ) )
		{
			var fuelPerSecond = ( _previousFuel - shared.fuel_left ) / deltaSeconds;

			if ( fuelPerSecond >= 0.0 )
			{
				_fuelUsePerHour += 0.1f * ( (float) ( fuelPerSecond * 3600.0 ) - _fuelUsePerHour );
			}
		}

		_previousSimulationTime = simulationTime;
		_previousFuel = shared.fuel_left;

		_shared = shared;

		_hasShared = true;
	}

	// the map is self-describing (all_drivers_offset points at num_cars, driver_data_size is the array
	// stride) - trust those fields for the driver array reads, and log once if they disagree with the
	// transcribed structs so a game-update layout drift is caught immediately
	private void CheckLayout( in R3eShared shared )
	{
		if ( _layoutChecked )
		{
			return;
		}

		_layoutChecked = true;

		var transcribedNumCarsOffset = System.Runtime.CompilerServices.Unsafe.SizeOf<R3eShared>() - 4;
		var transcribedDriverDataSize = System.Runtime.CompilerServices.Unsafe.SizeOf<R3eDriverData>();

		if ( ( shared.version_major != R3eConstants.VersionMajor ) || ( shared.all_drivers_offset != transcribedNumCarsOffset ) || ( shared.driver_data_size != transcribedDriverDataSize ) )
		{
			App.Instance!.Logger.WriteLine( $"[RaceRoomBridge] Shared memory layout differs from the transcription - version {shared.version_major}.{shared.version_minor}, num_cars offset {shared.all_drivers_offset} (transcribed {transcribedNumCarsOffset}), driver data size {shared.driver_data_size} (transcribed {transcribedDriverDataSize})" );
		}

		_driverDataArrayOffset = shared.all_drivers_offset + 4;
		_driverDataSize = ( shared.driver_data_size > 0 ) ? shared.driver_data_size : transcribedDriverDataSize;
	}

	private void UpdateDrivers()
	{
		var numCars = Math.Clamp( _shared.num_cars, 0, R3eConstants.NumDriversMax );

		if ( ( _driverDataArrayOffset <= 0 ) || ( _driverDataArrayOffset + numCars * _driverDataSize > _sharedBuffer.Length ) )
		{
			return;
		}

		if ( _drivers.Length != numCars )
		{
			_drivers = new R3eDriverData[ numCars ];
		}

		for ( var i = 0; i < numCars; i++ )
		{
			_drivers[ i ] = ReadStruct<R3eDriverData>( _sharedBuffer, _driverDataArrayOffset + i * _driverDataSize );

			var slotId = _drivers[ i ].driver_info.slot_id;

			if ( !_carIdxBySlotId.ContainsKey( slotId ) && ( _carIdxBySlotId.Count < GameBridgeVarTable.MaxNumCars ) )
			{
				_carIdxBySlotId[ slotId ] = _carIdxBySlotId.Count;
			}
		}

		_hasDrivers = numCars > 0;
	}

	// the structs are blittable (see R3eData.cs), so this is a straight allocation-free memory read - the old
	// Marshal.PtrToStructure path boxed the struct and allocated an array per array field on every call
	private static T ReadStruct<T>( byte[] buffer, int offset ) where T : struct
	{
		return MemoryMarshal.Read<T>( buffer.AsSpan( offset ) );
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

	private float GetSteeringRangeDegrees()
	{
		if ( _shared.steer_wheel_range_degrees > 0 )
		{
			return _shared.steer_wheel_range_degrees;
		}

		// steer_lock_degrees is NOT a fallback here - the capture showed it is the front WHEEL lock angle
		// (18 for the Alpine A110 Cup at a 360 degree wheel range), not half the steering wheel rotation

		if ( _shared.steer_wheel_max_rotation > 0 )
		{
			return _shared.steer_wheel_max_rotation;
		}

		return DefaultSteeringRangeDegrees;
	}

	#endregion

	#region torque

	private void WriteSteeringWheelTorque()
	{
		var varTable = _varTable!;
		var dataSource = varTable.DataSource;

		// the six sub-samples are real steering_force readings the pump captured at 360 Hz across this frame
		// (sample-and-hold over the map's update rate), so no interpolation is needed
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

		var shared = _shared;
		var player = shared.player;

		// pedals and gear

		dataSource.SetFloat( varTable.Throttle, Math.Max( 0f, shared.throttle ) );
		dataSource.SetFloat( varTable.ThrottleRaw, Math.Max( 0f, shared.throttle_raw ) );
		dataSource.SetFloat( varTable.Brake, Math.Max( 0f, shared.brake ) );
		dataSource.SetFloat( varTable.BrakeRaw, Math.Max( 0f, shared.brake_raw ) );
		dataSource.SetFloat( varTable.Clutch, Math.Max( 0f, shared.clutch ) );
		dataSource.SetBool( varTable.BrakeABSactive, shared.aid_settings.abs == R3eConstants.AidCurrentlyActive );
		dataSource.SetInt( varTable.Gear, ( shared.gear == -2 ) ? 0 : shared.gear );
		dataSource.SetFloat( varTable.RPM, Math.Max( 0f, shared.engine_rps ) * RadiansPerSecondToRpm );

		// motion - the local frame is x=left, y=up, z=rearward; iRacing is x=forward, y=left, z=up

		var velocityX = -player.local_velocity.z;
		var velocityY = LateralSign * player.local_velocity.x;

		dataSource.SetFloat( varTable.Speed, Math.Max( 0f, shared.car_speed ) );
		dataSource.SetFloat( varTable.VelocityX, (float) velocityX );
		dataSource.SetFloat( varTable.VelocityY, (float) velocityY );
		dataSource.SetFloat( varTable.LongAccel, (float) -player.local_acceleration.z );
		dataSource.SetFloat( varTable.LatAccel, (float) ( LateralSign * player.local_acceleration.x ) );

		// like rF2/LMU, local_acceleration excludes gravity (confirmed ~0 at rest in the capture) while
		// iRacing's VertAccel includes it (~+9.81 at rest), hence the offset
		dataSource.SetFloat( varTable.VertAccel, (float) ( player.local_acceleration.y + Gravity ) );
		dataSource.SetFloat( varTable.YawRate, (float) ( YawRateSign * player.local_angular_velocity.y ) );

		var yaw = YawSign * shared.car_orientation.yaw;

		dataSource.SetFloat( varTable.Yaw, yaw );
		dataSource.SetFloat( varTable.YawNorth, yaw );
		dataSource.SetFloat( varTable.Pitch, shared.car_orientation.pitch );
		dataSource.SetFloat( varTable.Roll, shared.car_orientation.roll );

		// steering

		var steeringRangeRadians = GetSteeringRangeDegrees() * Math.PI / 180.0;

		// iRacing's SteeringWheelAngleMax is the FULL lock-to-lock range - consumers halve it themselves
		dataSource.SetFloat( varTable.SteeringWheelAngle, ComputeSteeringWheelAngle( shared.steer_input_raw, steeringRangeRadians ) );
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

		var fuelLeft = Math.Max( 0f, shared.fuel_left );

		dataSource.SetFloat( varTable.FuelLevel, fuelLeft );
		dataSource.SetFloat( varTable.FuelLevelPct, ( shared.fuel_capacity > 0f ) ? fuelLeft / shared.fuel_capacity : 0f );
		dataSource.SetFloat( varTable.FuelUsePerHour, _fuelUsePerHour );

		// session and lap state

		var trackLength = Math.Max( 1f, shared.layout_length );

		dataSource.SetDouble( varTable.SessionTime, Math.Max( 0.0, player.game_simulation_time ) );
		dataSource.SetDouble( varTable.SessionTimeRemain, ( shared.session_time_remaining >= 0f ) ? shared.session_time_remaining : IRacingSdkConst.UnlimitedTime );
		dataSource.SetInt( varTable.SessionNum, Math.Max( 0, shared.session_type ) );
		dataSource.SetInt( varTable.SessionState, MapSessionState( (R3eConstants.R3eSessionPhase) shared.session_phase ) );
		dataSource.SetBitField( varTable.SessionFlags, MapSessionFlags( shared.flags ) );
		dataSource.SetInt( varTable.SessionLapsRemainEx, ( shared.number_of_laps > 0 ) ? Math.Max( 0, shared.number_of_laps - Math.Max( 0, shared.completed_laps ) ) : IRacingSdkConst.UnlimitedLaps );

		var completedLaps = Math.Max( 0, shared.completed_laps );
		var lapDist = Math.Max( 0f, shared.lap_distance );
		var lapDistPct = ( shared.lap_distance_fraction >= 0f ) ? Math.Clamp( shared.lap_distance_fraction, 0f, 1f ) : (float) ( lapDist / trackLength );

		dataSource.SetInt( varTable.Lap, completedLaps + 1 );
		dataSource.SetFloat( varTable.LapDist, lapDist );
		dataSource.SetFloat( varTable.LapDistPct, lapDistPct );
		dataSource.SetFloat( varTable.LapBestLapTime, Math.Max( 0f, shared.lap_time_best_self ) );
		dataSource.SetFloat( varTable.LapLastLapTime, Math.Max( 0f, shared.lap_time_previous_self ) );

		// player state

		var inMenus = shared.game_in_menus > 0;
		var inGarage = shared.game_player_in_garage > 0;
		var inPitlane = shared.in_pitlane == 1;

		dataSource.SetBool( varTable.IsOnTrack, !inMenus && !inGarage && ( shared.control_type == (int) R3eConstants.R3eControl.Player ) );
		dataSource.SetBool( varTable.OnPitRoad, inPitlane );
		dataSource.SetBool( varTable.PitsOpen, shared.pit_window_status != 1 );
		dataSource.SetInt( varTable.PlayerCarIdx, GetCarIdx( shared.vehicle_info.slot_id ) );
		dataSource.SetInt( varTable.PlayerCarPosition, Math.Max( 0, shared.position ) );
		dataSource.SetInt( varTable.PlayerCarClassPosition, Math.Max( 0, shared.position_class ) );
		dataSource.SetInt( varTable.PlayerCarMyIncidentCount, Math.Max( 0, shared.incident_points ) );
		dataSource.SetInt( varTable.PlayerTrackSurface, MapTrackSurface( inMenus, inGarage, inPitlane ) );
		dataSource.SetInt( varTable.PlayerTrackSurfaceMaterial, MapTrackSurfaceMaterial( (R3eConstants.R3eMaterialType) shared.tire_on_mtrl[ 0 ] ) );

		// weather

		dataSource.SetBool( varTable.WeatherDeclaredWet, false );

		// fixed values with no RaceRoom equivalent

		dataSource.SetInt( varTable.DisplayUnits, 1 );
		dataSource.SetFloat( varTable.FrameRate, 60f );
		dataSource.SetFloat( varTable.GpuUsage, 0f );
		dataSource.SetBool( varTable.IsReplayPlaying, shared.game_in_replay == 1 );
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

	private void WriteCarIdxArrays( float trackLength )
	{
		var varTable = _varTable!;
		var dataSource = varTable.DataSource;

		var playerSlotId = _shared.vehicle_info.slot_id;
		var playerLapDist = Math.Max( 0f, _shared.lap_distance );

		var carDistAhead = float.MaxValue;
		var carDistBehind = float.MaxValue;

		// best lap time - RaceRoom sector times are cumulative, so the third best sector IS the best lap

		Array.Clear( _carIdxFloatScratch );

		foreach ( var driver in _drivers )
		{
			if ( _carIdxBySlotId.TryGetValue( driver.driver_info.slot_id, out var carIdx ) )
			{
				_carIdxFloatScratch[ carIdx ] = Math.Max( 0f, driver.sector_time_best_self[ 2 ] );
			}
		}

		dataSource.SetFloatArray( varTable.CarIdxBestLapTime, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		// est time (time into lap)

		Array.Clear( _carIdxFloatScratch );

		foreach ( var driver in _drivers )
		{
			if ( _carIdxBySlotId.TryGetValue( driver.driver_info.slot_id, out var carIdx ) )
			{
				_carIdxFloatScratch[ carIdx ] = Math.Max( 0f, driver.lap_time_current_self );
			}
		}

		dataSource.SetFloatArray( varTable.CarIdxEstTime, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		// f2 time (time behind leader) - the driver array is in place order, so accumulating each entry's
		// time delta to the car in front walks the field from the leader down

		Array.Clear( _carIdxFloatScratch );

		var timeBehindLeader = 0f;

		for ( var i = 0; i < _drivers.Length; i++ )
		{
			if ( i > 0 )
			{
				timeBehindLeader += Math.Max( 0f, _drivers[ i ].time_delta_front );
			}

			if ( _carIdxBySlotId.TryGetValue( _drivers[ i ].driver_info.slot_id, out var carIdx ) )
			{
				_carIdxFloatScratch[ carIdx ] = timeBehindLeader;
			}
		}

		dataSource.SetFloatArray( varTable.CarIdxF2Time, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		// laps started

		Array.Clear( _carIdxIntScratch );

		foreach ( var driver in _drivers )
		{
			if ( _carIdxBySlotId.TryGetValue( driver.driver_info.slot_id, out var carIdx ) )
			{
				_carIdxIntScratch[ carIdx ] = Math.Max( 0, driver.completed_laps ) + 1;
			}
		}

		dataSource.SetIntArray( varTable.CarIdxLap, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		// laps completed

		Array.Clear( _carIdxIntScratch );

		foreach ( var driver in _drivers )
		{
			if ( _carIdxBySlotId.TryGetValue( driver.driver_info.slot_id, out var carIdx ) )
			{
				_carIdxIntScratch[ carIdx ] = Math.Max( 0, driver.completed_laps );
			}
		}

		dataSource.SetIntArray( varTable.CarIdxLapCompleted, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		// lap dist pct + car dist ahead/behind

		Array.Clear( _carIdxFloatScratch );

		foreach ( var driver in _drivers )
		{
			var driverLapDist = Math.Max( 0f, driver.lap_distance );

			if ( _carIdxBySlotId.TryGetValue( driver.driver_info.slot_id, out var carIdx ) )
			{
				_carIdxFloatScratch[ carIdx ] = ( driver.lap_distance_fraction >= 0f ) ? Math.Clamp( driver.lap_distance_fraction, 0f, 1f ) : driverLapDist / trackLength;
			}

			if ( ( driver.driver_info.slot_id != playerSlotId ) && ( driver.in_pitlane == 0 ) )
			{
				var distance = driverLapDist - playerLapDist;

				if ( distance < -trackLength * 0.5f )
				{
					distance += trackLength;
				}
				else if ( distance > trackLength * 0.5f )
				{
					distance -= trackLength;
				}

				if ( distance >= 0f )
				{
					carDistAhead = Math.Min( carDistAhead, distance );
				}
				else
				{
					carDistBehind = Math.Min( carDistBehind, -distance );
				}
			}
		}

		dataSource.SetFloatArray( varTable.CarIdxLapDistPct, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		dataSource.SetFloat( varTable.CarDistAhead, ( carDistAhead == float.MaxValue ) ? 999999f : carDistAhead );
		dataSource.SetFloat( varTable.CarDistBehind, ( carDistBehind == float.MaxValue ) ? 999999f : carDistBehind );

		// position

		Array.Clear( _carIdxIntScratch );

		foreach ( var driver in _drivers )
		{
			if ( _carIdxBySlotId.TryGetValue( driver.driver_info.slot_id, out var carIdx ) )
			{
				_carIdxIntScratch[ carIdx ] = Math.Max( 0, driver.place );
			}
		}

		dataSource.SetIntArray( varTable.CarIdxPosition, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		// on pit road

		foreach ( var driver in _drivers )
		{
			if ( _carIdxBySlotId.TryGetValue( driver.driver_info.slot_id, out var carIdx ) )
			{
				dataSource.SetBool( varTable.CarIdxOnPitRoad, driver.in_pitlane == 1, carIdx );
			}
		}

		// tire compound

		Array.Clear( _carIdxIntScratch );

		foreach ( var driver in _drivers )
		{
			if ( _carIdxBySlotId.TryGetValue( driver.driver_info.slot_id, out var carIdx ) )
			{
				_carIdxIntScratch[ carIdx ] = Math.Max( 0, driver.tire_subtype_front );
			}
		}

		dataSource.SetIntArray( varTable.CarIdxTireCompound, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );
	}

	private int GetCarIdx( int slotId )
	{
		return _carIdxBySlotId.TryGetValue( slotId, out var carIdx ) ? carIdx : 0;
	}

	private static int MapSessionState( R3eConstants.R3eSessionPhase sessionPhase )
	{
		return sessionPhase switch
		{
			R3eConstants.R3eSessionPhase.Garage => (int) IRacingSdkEnum.SessionState.GetInCar,
			R3eConstants.R3eSessionPhase.Gridwalk => (int) IRacingSdkEnum.SessionState.Warmup,
			R3eConstants.R3eSessionPhase.Formation or R3eConstants.R3eSessionPhase.Countdown => (int) IRacingSdkEnum.SessionState.ParadeLaps,
			R3eConstants.R3eSessionPhase.Checkered => (int) IRacingSdkEnum.SessionState.Checkered,
			_ => (int) IRacingSdkEnum.SessionState.Racing
		};
	}

	private static uint MapSessionFlags( in R3eFlags flags )
	{
		var sessionFlags = 0u;

		if ( flags.checkered == 1 )
		{
			sessionFlags |= 0x00000001; // checkered
		}

		if ( flags.white == 1 )
		{
			sessionFlags |= 0x00000002; // white
		}

		if ( flags.green == 1 )
		{
			sessionFlags |= 0x00000004; // green
		}

		if ( flags.yellow == 1 )
		{
			sessionFlags |= 0x00000008; // yellow
		}

		if ( flags.blue == 1 )
		{
			sessionFlags |= 0x00000020; // blue
		}

		if ( flags.black == 1 )
		{
			sessionFlags |= 0x00010000; // black
		}

		return sessionFlags;
	}

	private static int MapTrackSurface( bool inMenus, bool inGarage, bool inPitlane )
	{
		if ( inMenus )
		{
			return (int) IRacingSdkEnum.TrkLoc.NotInWorld;
		}

		if ( inGarage )
		{
			return (int) IRacingSdkEnum.TrkLoc.InPitStall;
		}

		if ( inPitlane )
		{
			return (int) IRacingSdkEnum.TrkLoc.AproachingPits;
		}

		return (int) IRacingSdkEnum.TrkLoc.OnTrack;
	}

	private static int MapTrackSurfaceMaterial( R3eConstants.R3eMaterialType materialType )
	{
		// unknown materials report asphalt so MAIRA's auto max force stays enabled (a real material is
		// required or the peak-torque gate silently disables it - the LMU lesson)
		return materialType switch
		{
			R3eConstants.R3eMaterialType.Tarmac => (int) IRacingSdkEnum.TrkSurf.Asphalt1Material,
			R3eConstants.R3eMaterialType.Concrete => (int) IRacingSdkEnum.TrkSurf.Concrete1Material,
			R3eConstants.R3eMaterialType.Grass => (int) IRacingSdkEnum.TrkSurf.Grass1Material,
			R3eConstants.R3eMaterialType.Dirt => (int) IRacingSdkEnum.TrkSurf.Dirt1Material,
			R3eConstants.R3eMaterialType.Gravel => (int) IRacingSdkEnum.TrkSurf.Gravel1Material,
			R3eConstants.R3eMaterialType.Rumble => (int) IRacingSdkEnum.TrkSurf.Rumble1Material,
			_ => (int) IRacingSdkEnum.TrkSurf.Asphalt1Material
		};
	}

	#endregion

	#region session info

	private void UpdateSessionInfo( double pumpSeconds )
	{
		// throttle first - the signature strings below allocate, so they are only built at 1 Hz instead of
		// every frame (the multimedia timer worker thread must stay free of steady-state garbage)
		if ( pumpSeconds - _lastSessionInfoUpdateTime < 1.0 )
		{
			return;
		}

		_lastSessionInfoUpdateTime = pumpSeconds;

		var trackName = ReadString( _shared.track_name );
		var layoutName = ReadString( _shared.layout_name );
		var playerCarName = GetPlayerCarName();

		var signature = $"{trackName}|{layoutName}|{playerCarName}|{_shared.session_type}|{_carIdxBySlotId.Count}|{(int) _shared.max_engine_rps}";

		if ( signature == _lastSessionInfoSignature )
		{
			return;
		}

		_lastSessionInfoSignature = signature;

		var maxRpm = Math.Max( 0f, _shared.max_engine_rps ) * RadiansPerSecondToRpm;

		var builder = new GameBridgeSessionInfoBuilder
		{
			TrackDisplayName = trackName,
			TrackConfigName = layoutName,
			TrackLengthInKm = Math.Max( 0f, _shared.layout_length ) / 1000f,
			SeriesID = 0,
			LeagueID = 0,
			SessionID = Math.Max( 0, _shared.session_type ),
			TimeOfDay = "12:00 pm",
			DriverCarIdx = GetCarIdx( _shared.vehicle_info.slot_id ),
			DriverSetupName = "bridge",
			DriverCarGearNumForward = Math.Max( 1, _shared.num_gears ),
			DriverCarRedLine = maxRpm,
			DriverCarSLFirstRPM = maxRpm * 0.88f,
			DriverCarSLShiftRPM = maxRpm * 0.96f,
			DriverCarSLBlinkRPM = maxRpm * 0.98f
		};

		foreach ( var driver in _drivers )
		{
			if ( _carIdxBySlotId.TryGetValue( driver.driver_info.slot_id, out var carIdx ) )
			{
				var isPlayer = driver.driver_info.slot_id == _shared.vehicle_info.slot_id;

				builder.Drivers.Add( new GameBridgeSessionInfoBuilder.DriverModel
				{
					CarIdx = carIdx,
					UserName = ReadString( driver.driver_info.name ),
					UserID = driver.driver_info.user_id,
					CarNumber = ( driver.driver_info.car_number > 0 ) ? driver.driver_info.car_number.ToString() : carIdx.ToString(),
					CarScreenName = isPlayer ? playerCarName : GetCarName( driver.driver_info ),
					CarPath = $"r3e_class_{driver.driver_info.class_id}",
					CarClassID = Math.Max( 0, driver.driver_info.class_id ),
					CarIsPaceCar = 0,
					IRating = 0,
					IsSpectator = 0,
					TeamID = Math.Max( 0, driver.driver_info.team_id )
				} );
			}
		}

		builder.Sessions.Add( new GameBridgeSessionInfoBuilder.SessionModel
		{
			SessionNum = Math.Max( 0, _shared.session_type ),
			SessionType = MapSessionType( (R3eConstants.R3eSession) _shared.session_type )
		} );

		_varTable!.DataSource.SetSessionInfo( builder.ToYaml() );
	}

	// the map identifies cars by database ids rather than names (driver_info.name is the DRIVER's name), so
	// the model id becomes the per-car context key - verify against the first capture whether
	// vehicle_info.name carries the car name or just repeats the player name
	private string GetPlayerCarName()
	{
		var vehicleName = ReadString( _shared.vehicle_info.name );
		var playerName = ReadString( _shared.player_name );

		if ( ( vehicleName.Length > 0 ) && ( vehicleName != playerName ) )
		{
			return vehicleName;
		}

		return GetCarName( _shared.vehicle_info );
	}

	private static string GetCarName( in R3eDriverInfo driverInfo )
	{
		return $"R3E car {driverInfo.model_id}";
	}

	private static string MapSessionType( R3eConstants.R3eSession session )
	{
		return session switch
		{
			R3eConstants.R3eSession.Race => "Race",
			R3eConstants.R3eSession.Qualify => "Lone Qualify",
			R3eConstants.R3eSession.Warmup => "Warmup",
			_ => "Practice"
		};
	}

	#endregion
}
