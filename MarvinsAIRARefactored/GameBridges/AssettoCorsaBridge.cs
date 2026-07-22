
using System.Runtime.InteropServices;

using IRSDKSharper;

using MarvinsAIRARefactored.GameBridges.Ac;

namespace MarvinsAIRARefactored.GameBridges;

public class AssettoCorsaBridge : GameBridgeAdapter
{
	public override string GameName => "Assetto Corsa";
	public override string LocalizationKey => "AssettoCorsa";
	public override string[] ProcessNames => [ "acs" ];
	public override GameBridgeCapabilities Capabilities => GameBridgeCapabilities.SteeringWheelTorque360Hz | GameBridgeCapabilities.ShockVelocities;

	public override bool IsImplemented => true;

	// === sign conventions relative to iRacing - validated against the 2026-07-21 ks_monza66 capture ===
	// Ground truth was a continuous right/clockwise oval; anchored on kinematic accG (corr(accG[2], d(fwd
	// vel)/dt)=+0.99 proves accG is real acceleration, so +accG[0]=left) and on the +365 deg heading change
	// over the clockwise lap (AC heading is already a compass CW-positive heading, matching iRacing YawNorth).
	// AC steerAngle is right-positive (iRacing is left-positive -> flip); finalFF is CCW-positive like iRacing
	// shaft torque (no flip - it makes the angle/torque correlation negative, as a self-aligning torque should).
	private const float SteeringAngleSign = -1f;
	private const float SteeringTorqueSign = 1f;
	private const float LongitudinalSign = 1f;
	private const float LateralSign = 1f;
	private const float LongAccelSign = 1f;
	private const float LatAccelSign = 1f;
	private const float YawRateSign = 1f;
	private const float YawSign = 1f;

	// AC local-frame vector component indices (validated) - accG / localVelocity / localAngularVel are [3]:
	// [0]=lateral, [1]=vertical, [2]=forward/longitudinal
	private const int ForwardIndex = 2;
	private const int LateralIndex = 0;
	private const int VerticalIndex = 1;

	private const double Gravity = 9.80665;

	// AC's physics shared memory genuinely updates at ~330 Hz (measured via its packetId counter), so the pump
	// samples finalFF at 360 Hz and fills the six 360 Hz sub-samples per 60 Hz frame with REAL readings instead
	// of interpolating - MAIRA gets true high-resolution torque, better than the interpolated LMU path
	private const int SamplesPerFrame = GameBridgeVarTable.SamplesPerFrame360Hz;
	private const int SubSampleFrequency = 360;

	// AC exposes a normalized final FFB signal (finalFF, -1..1) instead of a shaft torque in Nm, so it is
	// scaled to a nominal Nm range that MAIRA's max-force stage then works against (tune with the live wheel)
	private const float FinalFfToNm = 20f;

	// AC's shared memory does not expose the car's steering lock, so the soft-lock range falls back to this
	// default until a per-car source is found; the physical wheel angle is reconstructed from DirectInput
	private const float DefaultSteeringRangeDegrees = 900f;

	private GameBridgeVarTable? _varTable = null;
	private AcDataProvider? _provider = null;

	// the bridge is pumped from the multimedia timer worker thread (see Pump); this lock keeps a Stop from
	// a background task from closing the provider while a pump is mid-read
	private readonly object _pumpLock = new();
	private bool _providerOpen = false;
	private double _lastOpenAttemptSeconds = double.MinValue;
	private double _nextSubSampleSeconds = 0.0;

	protected byte[] _physicsBuffer = [];
	protected byte[] _graphicsBuffer = [];
	protected byte[] _staticBuffer = [];

	protected AcPhysics _physics;
	private AcGraphics _graphics;
	private AcStatic _static;

	private bool _hasStatic = false;

	// game-agnostic snapshot of the slow (graphics/static) page values the frame writer needs. The base class
	// fills it from the parsed AC1 structs; the EVO bridge overrides the parse/fill because EVO renamed the
	// maps AND reorganized the graphics/static layouts (the physics page is unchanged from AC1)
	protected struct SlowFrameData
	{
		public AcStatus Status;
		public bool IsInPit;
		public float LapDistPct;
		public double SessionTimeSeconds;
		public double SessionTimeRemainSeconds; // negative = unlimited
		public int NumberOfLaps;                // <= 0 = unlimited
		public int CompletedLaps;
		public int BestLapTimeMs;               // <= 0 = none yet
		public int LastLapTimeMs;
		public int Position;
		public int SessionTypeRaw;
		public float MaxFuel;
		public double TrackLengthMeters;
		public float MaxRpm;
	}

	protected SlowFrameData _slowFrame;

	// per-game shared memory page sizes - EVO overrides these (its pages are larger)
	protected virtual int PhysicsBufferSize => AcConstants.PhysicsMapSize;
	protected virtual int GraphicsBufferSize => AcConstants.GraphicsMapSize;
	protected virtual int StaticBufferSize => AcConstants.StaticMapSize;

	// per-frame accumulators for the six real 360 Hz sub-samples (torque + per-wheel shock velocity)
	private int _subSampleIndex = 0;
	private readonly float[] _torqueSamples = new float[ SamplesPerFrame ];
	private readonly float[,] _shockSamples = new float[ 4, SamplesPerFrame ];

	// the pump samples at 360 Hz but AC's page updates at ~330 Hz, so ~1 in 11 sub-samples reads an unchanged
	// page (same packetId); shock velocity is held across those repeats rather than recomputed as a spurious 0
	private int _previousPacketId = -1;
	private double _previousShockSeconds = double.NaN;
	private readonly float[] _previousSuspensionTravel = new float[ 4 ];
	private readonly float[] _lastShockVelocities = new float[ 4 ];
	private bool _hasSuspensionBaseline = false;

	private double _steeringScale = 0.0;
	private int _steeringScaleSampleCount = 0;

	private string _lastSessionInfoSignature = string.Empty;
	private double _lastSessionInfoUpdateTime = double.MinValue;

	private readonly float[] _carIdxFloatScratch = new float[ GameBridgeVarTable.MaxNumCars ];
	private readonly int[] _carIdxIntScratch = new int[ GameBridgeVarTable.MaxNumCars ];

	public override void Start()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AssettoCorsaBridge] Start >>>" );

		_varTable = new GameBridgeVarTable();

		DataSource = _varTable.DataSource;

		_provider = CreateProvider();

		_physicsBuffer = new byte[ PhysicsBufferSize ];
		_graphicsBuffer = new byte[ GraphicsBufferSize ];
		_staticBuffer = new byte[ StaticBufferSize ];

		ResetState();

		app.Logger.WriteLine( "[AssettoCorsaBridge] <<< Start" );
	}

	public override void Stop()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[AssettoCorsaBridge] Stop >>>" );

		lock ( _pumpLock )
		{
			_provider?.Close();
			_provider = null;

			_providerOpen = false;
		}

		app.Logger.WriteLine( "[AssettoCorsaBridge] <<< Stop" );
	}

	protected virtual AcDataProvider CreateProvider()
	{
		return new AcLiveDataProvider();
	}

	private void ResetState()
	{
		_providerOpen = false;
		_lastOpenAttemptSeconds = double.MinValue;
		_nextSubSampleSeconds = 0.0;

		_hasStatic = false;
		_slowFrame = default;

		_subSampleIndex = 0;
		Array.Clear( _torqueSamples );
		Array.Clear( _shockSamples );

		_previousPacketId = -1;
		_previousShockSeconds = double.NaN;
		Array.Clear( _previousSuspensionTravel );
		Array.Clear( _lastShockVelocities );
		_hasSuspensionBaseline = false;

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

				App.Instance!.Logger.WriteLine( $"[{GetType().Name}] Shared memory opened - pumping" );
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

	// Runs at 360 Hz. Every tick samples the fast-changing FFB / shock channels into the current 360 Hz slot;
	// once six slots are filled (60 Hz), the slower pages are read and a full frame is committed.
	private void ProcessSubSample( double pumpSeconds )
	{
		var provider = _provider!;

		if ( !provider.TryReadBuffer( AcBufferType.Physics, _physicsBuffer ) )
		{
			return;
		}

		_physics = ReadStruct<AcPhysics>( _physicsBuffer );

		// finalFF is held automatically - reading an unchanged page returns the last value (zero-order hold)
		_torqueSamples[ _subSampleIndex ] = SteeringTorqueSign * _physics.finalFF * FinalFfToNm;

		// shock velocity is a derivative, so only recompute it when the page actually advanced; otherwise the
		// last value is held into this slot (dividing an unchanged travel by dt would give a spurious zero)
		if ( _physics.packetId != _previousPacketId )
		{
			_previousPacketId = _physics.packetId;

			UpdateShockVelocities( pumpSeconds );
		}

		for ( var w = 0; w < 4; w++ )
		{
			_shockSamples[ w, _subSampleIndex ] = _lastShockVelocities[ w ];
		}

		if ( _subSampleIndex == SamplesPerFrame - 1 )
		{
			if ( provider.TryReadBuffer( AcBufferType.Static, _staticBuffer ) )
			{
				OnStaticPageRead();

				_hasStatic = true;
			}

			if ( provider.TryReadBuffer( AcBufferType.Graphics, _graphicsBuffer ) )
			{
				OnGraphicsPageRead();
			}

			if ( _hasStatic )
			{
				FillSlowFrameData();

				UpdateSessionInfo( pumpSeconds );

				WriteTelemetryFrame();

				_varTable!.DataSource.CommitFrame();
			}
		}

		_subSampleIndex = ( _subSampleIndex + 1 ) % SamplesPerFrame;
	}

	#endregion

	#region slow-page parsing seam

	// the base class parses the classic AC1 graphics/static structs; the EVO bridge overrides all three of
	// these because EVO reorganized both layouts (it parses by validated offset from the raw buffers instead)

	protected virtual void OnStaticPageRead()
	{
		_static = ReadStruct<AcStatic>( _staticBuffer );
	}

	protected virtual void OnGraphicsPageRead()
	{
		_graphics = ReadStruct<AcGraphics>( _graphicsBuffer );
	}

	// runs at 60 Hz on the multimedia timer worker thread - must not allocate
	protected virtual void FillSlowFrameData()
	{
		_slowFrame.Status = (AcStatus) _graphics.status;
		_slowFrame.IsInPit = _graphics.isInPit != 0;
		_slowFrame.LapDistPct = _graphics.normalizedCarPosition;
		_slowFrame.SessionTimeSeconds = Math.Max( 0.0, _graphics.distanceTraveled );
		_slowFrame.SessionTimeRemainSeconds = ( _graphics.sessionTimeLeft > 0f ) ? _graphics.sessionTimeLeft / 1000.0 : -1.0;
		_slowFrame.NumberOfLaps = _graphics.numberOfLaps;
		_slowFrame.CompletedLaps = _graphics.completedLaps;
		_slowFrame.BestLapTimeMs = _graphics.iBestTime;
		_slowFrame.LastLapTimeMs = _graphics.iLastTime;
		_slowFrame.Position = _graphics.position;
		_slowFrame.SessionTypeRaw = _graphics.session;
		_slowFrame.MaxFuel = _static.maxFuel;
		_slowFrame.TrackLengthMeters = _static.trackSPlineLength;
		_slowFrame.MaxRpm = _static.maxRpm;
	}

	// called at 1 Hz from UpdateSessionInfo, so string allocation is fine here
	protected virtual void GetSessionStrings( out string track, out string trackConfiguration, out string carModel, out string playerName )
	{
		track = ReadString( _static.track );
		trackConfiguration = ReadString( _static.trackConfiguration );
		carModel = ReadString( _static.carModel );

		var playerNick = ReadString( _static.playerNick );

		playerName = ( playerNick.Length > 0 ) ? playerNick : ReadString( _static.playerName );
	}

	protected virtual string MapSessionTypeName( int sessionTypeRaw )
	{
		return MapSessionType( (AcSessionType) sessionTypeRaw );
	}

	#endregion

	#region shock velocities

	// Called only when the physics page advanced, so deltaSeconds is the real interval between game updates.
	private void UpdateShockVelocities( double pumpSeconds )
	{
		var suspensionTravel = _physics.suspensionTravel;

		if ( !_hasSuspensionBaseline )
		{
			for ( var i = 0; i < 4; i++ )
			{
				_previousSuspensionTravel[ i ] = suspensionTravel[ i ];
			}

			_previousShockSeconds = pumpSeconds;
			_hasSuspensionBaseline = true;

			return;
		}

		var deltaSeconds = pumpSeconds - _previousShockSeconds;

		_previousShockSeconds = pumpSeconds;

		if ( deltaSeconds > 0.0 )
		{
			for ( var i = 0; i < 4; i++ )
			{
				_lastShockVelocities[ i ] = (float) ( ( suspensionTravel[ i ] - _previousSuspensionTravel[ i ] ) / deltaSeconds );
			}
		}

		for ( var i = 0; i < 4; i++ )
		{
			_previousSuspensionTravel[ i ] = suspensionTravel[ i ];
		}
	}

	#endregion

	#region telemetry frame mapping

	private void WriteTelemetryFrame()
	{
		var varTable = _varTable!;
		var dataSource = varTable.DataSource;

		var physics = _physics;
		var slowFrame = _slowFrame;

		// pedals and gear

		dataSource.SetFloat( varTable.Throttle, physics.gas );
		dataSource.SetFloat( varTable.ThrottleRaw, physics.gas );
		dataSource.SetFloat( varTable.Brake, physics.brake );
		dataSource.SetFloat( varTable.BrakeRaw, physics.brake );
		dataSource.SetFloat( varTable.Clutch, physics.clutch );
		dataSource.SetBool( varTable.BrakeABSactive, physics.abs > 0.5f );
		dataSource.SetInt( varTable.Gear, physics.gear - 1 );
		dataSource.SetFloat( varTable.RPM, physics.rpms );

		// motion - AC exposes a body-frame localVelocity and per-axis G forces; the component indices and signs
		// are UNVALIDATED and must be confirmed against a capture (world velocity + heading self-consistency)

		var forwardVelocity = LongitudinalSign * physics.localVelocity[ ForwardIndex ];
		var lateralVelocity = LateralSign * physics.localVelocity[ LateralIndex ];

		dataSource.SetFloat( varTable.Speed, physics.speedKmh / 3.6f );
		dataSource.SetFloat( varTable.VelocityX, forwardVelocity );
		dataSource.SetFloat( varTable.VelocityY, lateralVelocity );
		dataSource.SetFloat( varTable.LongAccel, (float) ( LongAccelSign * physics.accG[ ForwardIndex ] * Gravity ) );
		dataSource.SetFloat( varTable.LatAccel, (float) ( LatAccelSign * physics.accG[ LateralIndex ] * Gravity ) );

		// AC's accG excludes gravity (reads 0 on every axis at rest), but iRacing's VertAccel includes it and
		// reads ~+9.81 at rest, so add one G (+accG[1] = up, confirmed by the banking load in the capture)
		dataSource.SetFloat( varTable.VertAccel, (float) ( ( physics.accG[ VerticalIndex ] + 1.0 ) * Gravity ) );
		dataSource.SetFloat( varTable.YawRate, YawRateSign * physics.localAngularVel[ VerticalIndex ] );

		var yaw = YawSign * physics.heading;

		dataSource.SetFloat( varTable.Yaw, yaw );
		dataSource.SetFloat( varTable.YawNorth, yaw );
		dataSource.SetFloat( varTable.Pitch, physics.pitch );
		dataSource.SetFloat( varTable.Roll, physics.roll );

		// steering

		var steeringRangeRadians = DefaultSteeringRangeDegrees * Math.PI / 180.0;

		dataSource.SetFloat( varTable.SteeringWheelAngle, ComputeSteeringWheelAngle( physics.steerAngle, steeringRangeRadians ) );
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

		var maxFuel = ( slowFrame.MaxFuel > 0f ) ? slowFrame.MaxFuel : 0f;

		dataSource.SetFloat( varTable.FuelLevel, physics.fuel );
		dataSource.SetFloat( varTable.FuelLevelPct, ( maxFuel > 0f ) ? physics.fuel / maxFuel : 0f );
		dataSource.SetFloat( varTable.FuelUsePerHour, 0f );

		// session and lap state

		var trackLength = Math.Max( 1.0, slowFrame.TrackLengthMeters );

		var lapDistPct = Math.Clamp( slowFrame.LapDistPct, 0f, 1f );

		var status = slowFrame.Status;
		var isOnTrack = status == AcStatus.Live;
		var isInPit = slowFrame.IsInPit;

		dataSource.SetDouble( varTable.SessionTime, slowFrame.SessionTimeSeconds );
		dataSource.SetDouble( varTable.SessionTimeRemain, ( slowFrame.SessionTimeRemainSeconds >= 0.0 ) ? slowFrame.SessionTimeRemainSeconds : IRacingSdkConst.UnlimitedTime );
		dataSource.SetInt( varTable.SessionNum, 0 );
		dataSource.SetInt( varTable.SessionState, MapSessionState( status ) );
		dataSource.SetBitField( varTable.SessionFlags, MapSessionFlags( status ) );
		dataSource.SetInt( varTable.SessionLapsRemainEx, ( slowFrame.NumberOfLaps > 0 ) ? Math.Max( 0, slowFrame.NumberOfLaps - slowFrame.CompletedLaps ) : IRacingSdkConst.UnlimitedLaps );

		dataSource.SetInt( varTable.Lap, slowFrame.CompletedLaps + 1 );
		dataSource.SetFloat( varTable.LapDist, (float) ( lapDistPct * trackLength ) );
		dataSource.SetFloat( varTable.LapDistPct, lapDistPct );
		dataSource.SetFloat( varTable.LapBestLapTime, ( slowFrame.BestLapTimeMs > 0 ) ? slowFrame.BestLapTimeMs / 1000f : 0f );
		dataSource.SetFloat( varTable.LapLastLapTime, ( slowFrame.LastLapTimeMs > 0 ) ? slowFrame.LastLapTimeMs / 1000f : 0f );

		// player state

		dataSource.SetBool( varTable.IsOnTrack, isOnTrack );
		dataSource.SetBool( varTable.OnPitRoad, isInPit );
		dataSource.SetBool( varTable.PitsOpen, true );
		dataSource.SetInt( varTable.PlayerCarIdx, 0 );
		dataSource.SetInt( varTable.PlayerCarPosition, slowFrame.Position );
		dataSource.SetInt( varTable.PlayerCarClassPosition, slowFrame.Position );
		dataSource.SetInt( varTable.PlayerCarMyIncidentCount, 0 );
		dataSource.SetInt( varTable.PlayerTrackSurface, MapTrackSurface( status, isInPit ) );

		// AC exposes no track surface material; report asphalt so MAIRA's auto max force stays enabled (a real
		// material is required or the peak-torque gate silently disables it - the LMU lesson)
		dataSource.SetInt( varTable.PlayerTrackSurfaceMaterial, (int) IRacingSdkEnum.TrkSurf.Asphalt1Material );

		// weather

		dataSource.SetBool( varTable.WeatherDeclaredWet, false );

		// fixed values with no AC equivalent

		dataSource.SetInt( varTable.DisplayUnits, 1 );
		dataSource.SetFloat( varTable.FrameRate, 60f );
		dataSource.SetFloat( varTable.GpuUsage, 0f );
		dataSource.SetBool( varTable.IsReplayPlaying, status == AcStatus.Replay );
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

		WriteCarIdxArrays( trackLength, lapDistPct );
	}

	private void WriteCarIdxArrays( double trackLength, float lapDistPct )
	{
		var varTable = _varTable!;
		var dataSource = varTable.DataSource;

		var slowFrame = _slowFrame;

		// the Kunos shared memory is player-centric - only the player car (index 0) is populated

		Array.Clear( _carIdxFloatScratch );
		_carIdxFloatScratch[ 0 ] = ( slowFrame.BestLapTimeMs > 0 ) ? slowFrame.BestLapTimeMs / 1000f : 0f;
		dataSource.SetFloatArray( varTable.CarIdxBestLapTime, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		Array.Clear( _carIdxFloatScratch );
		dataSource.SetFloatArray( varTable.CarIdxEstTime, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );
		dataSource.SetFloatArray( varTable.CarIdxF2Time, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		Array.Clear( _carIdxIntScratch );
		_carIdxIntScratch[ 0 ] = slowFrame.CompletedLaps + 1;
		dataSource.SetIntArray( varTable.CarIdxLap, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		Array.Clear( _carIdxIntScratch );
		_carIdxIntScratch[ 0 ] = slowFrame.CompletedLaps;
		dataSource.SetIntArray( varTable.CarIdxLapCompleted, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		Array.Clear( _carIdxFloatScratch );
		_carIdxFloatScratch[ 0 ] = lapDistPct;
		dataSource.SetFloatArray( varTable.CarIdxLapDistPct, _carIdxFloatScratch, 0, GameBridgeVarTable.MaxNumCars );

		dataSource.SetFloat( varTable.CarDistAhead, 999999f );
		dataSource.SetFloat( varTable.CarDistBehind, 999999f );

		Array.Clear( _carIdxIntScratch );
		_carIdxIntScratch[ 0 ] = slowFrame.Position;
		dataSource.SetIntArray( varTable.CarIdxPosition, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		dataSource.SetBool( varTable.CarIdxOnPitRoad, slowFrame.IsInPit, 0 );

		Array.Clear( _carIdxIntScratch );
		dataSource.SetIntArray( varTable.CarIdxTireCompound, _carIdxIntScratch, 0, GameBridgeVarTable.MaxNumCars );

		// CarIdxSessionFlags is a bitfield array - clear it for the player
		dataSource.SetBitField( varTable.CarIdxSessionFlags, 0u, 0 );
	}

	private static int MapSessionState( AcStatus status )
	{
		return status switch
		{
			AcStatus.Off => (int) IRacingSdkEnum.SessionState.Invalid,
			AcStatus.Pause => (int) IRacingSdkEnum.SessionState.GetInCar,
			_ => (int) IRacingSdkEnum.SessionState.Racing
		};
	}

	private static uint MapSessionFlags( AcStatus status )
	{
		return ( status == AcStatus.Live ) ? 0x00000004u : 0u; // green
	}

	private static int MapTrackSurface( AcStatus status, bool isInPit )
	{
		if ( status != AcStatus.Live )
		{
			return (int) IRacingSdkEnum.TrkLoc.NotInWorld;
		}

		return isInPit ? (int) IRacingSdkEnum.TrkLoc.AproachingPits : (int) IRacingSdkEnum.TrkLoc.OnTrack;
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

		// the game clamps its steering value at full lock, so a soft lock spring driven by the game's angle can
		// never see the wheel go past the stop - iRacing reports the PHYSICAL wheel angle instead, which we
		// reconstruct from the DirectInput axis, calibrated against the game's angle in the linear region
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

		// the six sub-samples are real finalFF readings the pump captured at 360 Hz across this frame
		for ( var i = 0; i < SamplesPerFrame; i++ )
		{
			dataSource.SetFloat( varTable.SteeringWheelTorque_ST, _torqueSamples[ i ], i );
		}

		dataSource.SetFloat( varTable.SteeringWheelTorque, _torqueSamples[ SamplesPerFrame - 1 ] );
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

		GetSessionStrings( out var track, out var trackConfiguration, out var carModel, out var playerName );

		var signature = $"{track}|{trackConfiguration}|{carModel}|{_slowFrame.MaxRpm}|{_slowFrame.SessionTypeRaw}";

		if ( signature == _lastSessionInfoSignature )
		{
			return;
		}

		_lastSessionInfoSignature = signature;

		var maxRpm = ( _slowFrame.MaxRpm > 0f ) ? _slowFrame.MaxRpm : 0f;

		var builder = new GameBridgeSessionInfoBuilder
		{
			TrackDisplayName = track,
			TrackConfigName = trackConfiguration,
			TrackLengthInKm = (float) ( Math.Max( 0.0, _slowFrame.TrackLengthMeters ) / 1000.0 ),
			SeriesID = 0,
			LeagueID = 0,
			SessionID = 0,
			TimeOfDay = "12:00 pm",
			DriverCarIdx = 0,
			DriverSetupName = "bridge",
			DriverCarGearNumForward = 6,
			DriverCarSLFirstRPM = maxRpm * 0.88f,
			DriverCarSLShiftRPM = maxRpm * 0.96f,
			DriverCarSLBlinkRPM = maxRpm * 0.98f
		};

		builder.Drivers.Add( new GameBridgeSessionInfoBuilder.DriverModel
		{
			CarIdx = 0,
			UserName = playerName,
			UserID = 0,
			CarNumber = "0",
			CarScreenName = carModel,
			CarPath = carModel,
			CarClassID = 0,
			CarIsPaceCar = 0,
			IRating = 0,
			IsSpectator = 0,
			TeamID = 0
		} );

		builder.Sessions.Add( new GameBridgeSessionInfoBuilder.SessionModel
		{
			SessionNum = 0,
			SessionType = MapSessionTypeName( _slowFrame.SessionTypeRaw )
		} );

		_varTable!.DataSource.SetSessionInfo( builder.ToYaml() );
	}

	private static string MapSessionType( AcSessionType session )
	{
		return session switch
		{
			AcSessionType.Race => "Race",
			AcSessionType.Qualify => "Lone Qualify",
			AcSessionType.Hotlap or AcSessionType.TimeAttack or AcSessionType.HotStint or AcSessionType.HotStintSuperPole => "Lone Qualify",
			_ => "Practice"
		};
	}

	#endregion

	// the structs are blittable (see AcData.cs), so this is a straight allocation-free memory read - the old
	// Marshal.PtrToStructure path boxed the struct and allocated an array per array field on every call
	protected static T ReadStruct<T>( byte[] buffer ) where T : struct
	{
		return MemoryMarshal.Read<T>( buffer );
	}

	protected static string ReadString( ReadOnlySpan<char> chars )
	{
		var length = chars.IndexOf( '\0' );

		if ( length == -1 )
		{
			length = chars.Length;
		}

		return new string( chars[ ..length ] );
	}
}
