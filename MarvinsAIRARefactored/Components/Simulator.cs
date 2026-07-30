
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using Windows.Win32;

using IRSDKSharper;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Windows;

using static MarvinsAIRARefactored.Windows.MainWindow;

namespace MarvinsAIRARefactored.Components;

public partial class Simulator
{
	public const int SamplesPerFrame360Hz = 6;
	private const int UpdateInterval = 6;
	private const int MaxNumGears = 10;

	private readonly IRacingSdk _irsdk = new();

	public IRacingSdk IRSDK { get => _irsdk; }

	public IntPtr? WindowHandle { get; private set; } = null;

	public List<IRacingSdkSessionInfo.DriverInfoModel.DriverTireModel>? AvailableTires = null;
	public bool BrakeABSactive { get; private set; } = false;
	public float Brake { get; private set; } = 0f;
	public float BrakeRaw { get; private set; } = 0f;
	public int[] CarIdxLap { get; private set; } = [];
	public float[] CarIdxLapDistPct { get; private set; } = [];
	public float[] CarIdxBestLapTime { get; private set; } = [];
	public float[] CarIdxEstTime { get; private set; } = [];
	public float[] CarIdxF2Time { get; private set; } = [];
	public int[] CarIdxLapCompleted { get; private set; } = [];
	public bool[] CarIdxOnPitRoad { get; private set; } = [];
	public int[] CarIdxPosition { get; private set; } = [];
	public uint[] CarIdxSessionFlags { get; private set; } = [];
	public IRacingSdkEnum.CarLeftRight CarLeftRight { get; private set; } = IRacingSdkEnum.CarLeftRight.Off;
	public float CarDistAhead { get; private set; } = 0f;
	public float CarDistBehind { get; private set; } = 0f;
	public string CarScreenName { get; private set; } = string.Empty;
	public string CarSetupName { get; private set; } = string.Empty;
	public float[] CFShockVel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float Clutch { get; private set; } = 0f;
	public float[] CRShockVel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float CurrentRpmSpeedRatio { get; private set; } = 0f;
	public int CurrentTireIndex { get; private set; } = -1;
	public string CurrentTireCompoundType { get; private set; } = string.Empty;
	public int DisplayUnits { get; private set; } = 0;
	public bool DriverIsAdmin { get; private set; } = false;
	public bool EngineRunning { get; private set; } = false;
	public float FrameRate { get; private set; } = 0;
	public int Gear { get; private set; } = 0;
	public float GpuUsage { get; private set; } = 0f;
	public float LongitudinalGForce { get; private set; } = 0f;
	public float LateralGForce { get; private set; } = 0f;
	public float MaxShockVelocity { get; private set; } = 0f;
	public bool IsConnected { get => _irsdk.IsConnected; }
	public bool IsOnTrack { get; private set; } = false;
	public bool IsReplayPlaying { get; private set; } = false;
	public int Lap { get; private set; } = 0;
	public float LapBestLapTime { get; private set; } = 0f;
	public float LapDist { get; private set; } = 0;
	public float LapDistPct { get; private set; } = 0f;
	public float LapLastLapTime { get; private set; } = 0f;
	public int LastRadioTransmitCarIdx { get; private set; } = -1;
	public float LatAccel { get; private set; } = 0f;
	public int LeagueID { get; private set; } = 0;
	public float[] LFShockVel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public bool LoadNumTextures { get; private set; } = false;
	public float LongAccel { get; private set; } = 0f;
	public float[] LRShockVel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public int NumForwardGears { get; private set; } = 0;
	public IRacingSdkEnum.PaceMode PaceMode { get; private set; } = IRacingSdkEnum.PaceMode.NotPacing;
	public float Pitch { get; private set; } = 0f;
	public float FuelLevel { get; private set; } = 0f;
	public float FuelLevelPct { get; private set; } = 0f;
	public float FuelUsePerHour { get; private set; } = 0f;
	public bool OnPitRoad { get; private set; } = false;
	public int PlayerCarClassPosition { get; private set; } = 0;
	public int PlayerCarIdx { get; private set; } = 0;
	public int PlayerCarMyIncidentCount { get; private set; } = 0;
	public int PlayerCarPosition { get; private set; } = 0;
	public bool PitsOpen { get; private set; } = false;
	public IRacingSdkEnum.TrkLoc PlayerTrackSurface { get; private set; } = IRacingSdkEnum.TrkLoc.NotInWorld;
	public IRacingSdkEnum.TrkSurf PlayerTrackSurfaceMaterial { get; private set; } = IRacingSdkEnum.TrkSurf.SurfaceNotInWorld;
	public int RadioTransmitCarIdx { get; private set; } = -1;
	public int ReplayFrameNumEnd { get; private set; } = 1;
	public bool ReplayPlaySlowMotion { get; private set; } = false;
	public int ReplayPlaySpeed { get; private set; } = 1;
	public float[] RFShockVel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float Roll { get; private set; } = 0f;
	public float RPM { get; private set; } = 0f;
	public float[] RPMSpeedRatios { get; private set; } = new float[ MaxNumGears ];
	public float[] RRShockVel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public int SeriesID { get; private set; } = 0;
	public IRacingSdkEnum.Flags SessionFlags { get; private set; } = 0;
	public int SessionID { get; private set; } = 0;
	public int SessionLapsRemainEx { get; private set; } = 0;
	public int SessionNum { get; private set; } = 0;
	public IRacingSdkEnum.SessionState SessionState { get; private set; } = IRacingSdkEnum.SessionState.Invalid;
	public double SessionTime { get; private set; } = 0f;
	public double SessionTimeRemain { get; private set; } = 0;
	public float RedlineRPM { get; private set; } = 0f;
	// iRacing describes a car's shift lights with four rising RPMs: First is where the first light comes
	// on, Shift is where the shift indication begins, Last is where the final light comes on, and Blink is
	// where they start blinking. Anything filling a rev bar wants First to Last; Shift sits in the middle
	// and is a colour change, not the top of the range.
	public float ShiftLightsFirstRPM { get; private set; } = 0f;
	public float ShiftLightsShiftRPM { get; private set; } = 0f;
	public float ShiftLightsLastRPM { get; private set; } = 0f;
	public float ShiftLightsBlinkRPM { get; private set; } = 0f;

	private float _lastLoggedShiftLightsFirstRPM = -1f;
	private float _lastLoggedShiftLightsLastRPM = -1f;
	public string SimMode { get; private set; } = string.Empty;
	public float Speed { get; private set; } = 0f;
	public bool SteeringFFBEnabled { get; private set; } = false;
	public float SteeringOffsetInDegrees { get; private set; } = 0f;
	public float SteeringRatio { get; private set; } = 10f;
	public float SteeringWheelAngle { get; private set; } = 0f;
	public float SteeringWheelAngleMax { get; private set; } = 0f;
	public float SteeringWheelVelocity { get; private set; } = 0f;   // rad/s, derived from SteeringWheelAngle across telemetry frames (60 Hz)
	public float[] SteeringWheelTorque_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float Throttle { get; private set; } = 0f;
	public float ThrottleRaw { get; private set; } = 0f;
	public float TireLF_RumblePitch { get; private set; } = 0f;
	public float TireRF_RumblePitch { get; private set; } = 0f;
	public float TireLR_RumblePitch { get; private set; } = 0f;
	public float TireRR_RumblePitch { get; private set; } = 0f;
	public string TimeOfDay { get; private set; } = string.Empty;
	public string TrackDisplayName { get; private set; } = string.Empty;
	public string TrackConfigName { get; private set; } = string.Empty;
	public float TrackLength { get; private set; } = 0f;
	public string UserName { get; private set; } = string.Empty;
	public float Velocity { get; private set; } = 0f;
	public float VelocityX { get; private set; } = 0f;
	public float VelocityY { get; private set; } = 0f;
	public float VertAccel { get; private set; } = 0f;
	public bool WasOnTrack { get; private set; } = false;
	public bool WeatherDeclaredWet { get; private set; } = false;
	public float Yaw { get; private set; } = 0f;
	public float YawNorth { get; private set; } = 0f;
	public float YawRate { get; private set; } = 0f;
	public float[] YawRate_ST { get; private set; } = new float[ SamplesPerFrame360Hz ]; // true 360 Hz yaw rate; falls back to the 60 Hz YawRate when the sim doesn't expose it

	// prediction-audit 360 Hz channels (recorded for the PredictionLab telemetry audit; nothing else consumes
	// them) — zeros when the sim doesn't expose the ST arrays, except VelocityY_ST which falls back to the
	// 60 Hz VelocityY
	public float[] VelocityY_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float[] LatAccel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float[] RollRate_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float[] PitchRate_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float[] LFShockDefl_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float[] RFShockDefl_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];

	private bool _telemetryDataInitialized = false;
	private bool _waitingForFirstSessionInfo = false;

	private int _activeResetBlockTimerFrames = 0;
	private int? _currentTireIndexLastFrame = null;
	private int? _displayUnitsLastFrame = null;
	private bool? _isReplayPlayingLastFrame = null;
	private float? _lapDistLastFrame = null;
	private int? _radioTransmitCarIdxLastFrame = null;
	private IRacingSdkEnum.Flags? _sessionFlagsLastFrame = null;
	private int? _sessionNumLastFrame = null;
	private IRacingSdkEnum.SessionState? _sessionStateLastFrame = null;
	private int? _tickCountLastFrame = null;
	private bool? _weatherDeclaredWetLastFrame = null;

	// the simulator sometimes turns on the wrong session flag bits - these latches let us filter the erroneous ones out
	private bool _ignoreOneLapToGreenUntilNextSession = false;
	private bool _ignoreBlueUntilCleared = false;

	private IRacingSdkDatum? _brakeABSactiveDatum = null;
	private IRacingSdkDatum? _brakeDatum = null;
	private IRacingSdkDatum? _brakeRawDatum = null;
	private IRacingSdkDatum? _carIdxBestLapTimeDatum = null;
	private IRacingSdkDatum? _carIdxEstTimeDatum = null;
	private IRacingSdkDatum? _carIdxF2TimeDatum = null;
	private IRacingSdkDatum? _carIdxLapDatum = null;
	private IRacingSdkDatum? _carIdxLapCompletedDatum = null;
	private IRacingSdkDatum? _carIdxLapDistPctDatum = null;
	private IRacingSdkDatum? _carIdxPositionDatum = null;
	private IRacingSdkDatum? _carIdxOnPitRoadDatum = null;
	private IRacingSdkDatum? _carIdxSessionFlagsDatum = null;
	private IRacingSdkDatum? _carIdxTireCompoundDatum = null;

	private int[] _carIdxTireCompounds = [];
	private IRacingSdkDatum? _carDistAheadDatum = null;
	private IRacingSdkDatum? _carDistBehindDatum = null;
	private IRacingSdkDatum? _carLeftRightDatum = null;
	private IRacingSdkDatum? _cfShockVel_STDatum = null;
	private IRacingSdkDatum? _clutchDatum = null;
	private IRacingSdkDatum? _crShockVel_STDatum = null;
	private IRacingSdkDatum? _displayUnitsDatum = null;
	private IRacingSdkDatum? _engineWarningsDatum = null;
	private IRacingSdkDatum? _frameRateDatum = null;
	private IRacingSdkDatum? _gearDatum = null;
	private IRacingSdkDatum? _gpuUsageDatum = null;
	private IRacingSdkDatum? _isOnTrackDatum = null;
	private IRacingSdkDatum? _isReplayPlayingDatum = null;
	private IRacingSdkDatum? _fuelLevelDatum = null;
	private IRacingSdkDatum? _fuelLevelPctDatum = null;
	private IRacingSdkDatum? _fuelUsePerHourDatum = null;
	private IRacingSdkDatum? _lapBestLapTimeDatum = null;
	private IRacingSdkDatum? _lapDatum = null;
	private IRacingSdkDatum? _lapDistDatum = null;
	private IRacingSdkDatum? _lapDistPctDatum = null;
	private IRacingSdkDatum? _lapLastLapTimeDatum = null;
	private IRacingSdkDatum? _latAccelDatum = null;
	private IRacingSdkDatum? _lfShockVel_STDatum = null;
	private IRacingSdkDatum? _loadNumTexturesDatum = null;
	private IRacingSdkDatum? _longAccelDatum = null;
	private IRacingSdkDatum? _lrShockVel_STDatum = null;
	private IRacingSdkDatum? _onPitRoadDatum = null;
	private IRacingSdkDatum? _paceModeDatum = null;
	private IRacingSdkDatum? _pitchDatum = null;
	private IRacingSdkDatum? _pitsOpenDatum = null;
	private IRacingSdkDatum? _playerCarClassPositionDatum = null;
	private IRacingSdkDatum? _playerCarIdxDatum = null;
	private IRacingSdkDatum? _playerCarMyIncidentCountDatum = null;
	private IRacingSdkDatum? _playerCarPositionDatum = null;
	private IRacingSdkDatum? _playerTrackSurfaceDatum = null;
	private IRacingSdkDatum? _playerTrackSurfaceMaterialDatum = null;
	private IRacingSdkDatum? _radioTransmitCarIdxDatum = null;
	private IRacingSdkDatum? _replayFrameNumEndDatum = null;
	private IRacingSdkDatum? _replayPlaySlowMotionDatum = null;
	private IRacingSdkDatum? _replayPlaySpeedDatum = null;
	private IRacingSdkDatum? _rollDatum = null;
	private IRacingSdkDatum? _rfShockVel_STDatum = null;
	private IRacingSdkDatum? _rpmDatum = null;
	private IRacingSdkDatum? _rrShockVel_STDatum = null;
	private IRacingSdkDatum? _sessionFlagsDatum = null;
	private IRacingSdkDatum? _sessionLapsRemainExDatum = null;
	private IRacingSdkDatum? _sessionNumDatum = null;
	private IRacingSdkDatum? _sessionStateDatum = null;
	private IRacingSdkDatum? _sessionTimeDatum = null;
	private IRacingSdkDatum? _sessionTimeRemainDatum = null;
	private IRacingSdkDatum? _speedDatum = null;
	private IRacingSdkDatum? _steeringFFBEnabledDatum = null;
	private IRacingSdkDatum? _steeringWheelAngleDatum = null;
	private IRacingSdkDatum? _steeringWheelAngleMaxDatum = null;
	private IRacingSdkDatum? _steeringWheelTorque_STDatum = null;
	private IRacingSdkDatum? _throttleDatum = null;
	private IRacingSdkDatum? _throttleRawDatum = null;
	private IRacingSdkDatum? _tireLF_RumblePitchDatum = null;
	private IRacingSdkDatum? _tireRF_RumblePitchDatum = null;
	private IRacingSdkDatum? _tireLR_RumblePitchDatum = null;
	private IRacingSdkDatum? _tireRR_RumblePitchDatum = null;
	private IRacingSdkDatum? _velocityXDatum = null;
	private IRacingSdkDatum? _velocityYDatum = null;
	private IRacingSdkDatum? _vertAccelDatum = null;
	private IRacingSdkDatum? _weatherDeclaredWetDatum = null;
	private IRacingSdkDatum? _yawDatum = null;
	private IRacingSdkDatum? _yawNorthDatum = null;
	private IRacingSdkDatum? _yawRateDatum = null;
	private IRacingSdkDatum? _yawRate_STDatum = null;
	private IRacingSdkDatum? _velocityY_STDatum = null;
	private IRacingSdkDatum? _latAccel_STDatum = null;
	private IRacingSdkDatum? _rollRate_STDatum = null;
	private IRacingSdkDatum? _pitchRate_STDatum = null;
	private IRacingSdkDatum? _lfShockDefl_STDatum = null;
	private IRacingSdkDatum? _rfShockDefl_STDatum = null;

	private readonly float[] _rpmSpeedRatioAccumulator = new float[ MaxNumGears ];
	private readonly int[] _rpmSpeedRatioSampleCount = new int[ MaxNumGears ];
	private const int RpmSpeedRatioMinSamples = 20;

	private int _updateCounter = UpdateInterval + 5;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Simulator] Initialize >>>" );

		_irsdk.OnException += OnException;
		_irsdk.OnConnected += OnConnected;
		_irsdk.OnDisconnected += OnDisconnected;
		_irsdk.OnSessionInfo += OnSessionInfo;
		_irsdk.OnTelemetryData += OnTelemetryData;
		_irsdk.OnDebugLog += OnDebugLog;

		app.Logger.WriteLine( "[Simulator] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Simulator] Shutdown >>>" );

		app.Logger.WriteLine( "[Simulator] Stopping IRSDKSharper" );

		_irsdk.Stop();

		while ( _irsdk.IsStarted )
		{
			Thread.Sleep( 50 );
		}

		app.Logger.WriteLine( "[Simulator] <<< Shutdown" );
	}

	public void Start()
	{
		_irsdk.Start();
	}

	public void SetDataSource( IRacingSdkDataSource? dataSource )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Simulator] SetDataSource >>>" );

		var wasStarted = _irsdk.IsStarted;
		var wasConnected = _irsdk.IsConnected;

		if ( wasStarted )
		{
			app.Logger.WriteLine( "[Simulator] Stopping IRSDKSharper" );

			_irsdk.Stop();

			while ( _irsdk.IsStarted )
			{
				Thread.Sleep( 50 );
			}
		}

		if ( wasConnected )
		{
			OnDisconnected();
		}

		_irsdk.SetDataSource( dataSource );

		if ( wasStarted )
		{
			app.Logger.WriteLine( "[Simulator] Restarting IRSDKSharper" );

			_irsdk.Start();
		}

		app.Logger.WriteLine( "[Simulator] <<< SetDataSource" );
	}

	public IRacingSdkSessionInfo.DriverInfoModel.DriverModel? GetDriver( int carIdx )
	{
		var sessionInfo = _irsdk.Data.SessionInfo;

		if ( ( sessionInfo != null ) && ( sessionInfo.DriverInfo != null ) && ( sessionInfo.DriverInfo.Drivers != null ) )
		{
			foreach ( var driver in sessionInfo.DriverInfo.Drivers )
			{
				if ( driver.CarIdx == carIdx )
				{
					return driver;
				}
			}
		}

		return null;
	}

	/// <summary>
	/// Returns the SessionType string (e.g. "Race", "Lone Qualify", "Open Practice", "Warmup")
	/// for the currently active session, or null if it cannot be determined. The active session
	/// is the entry in the Sessions array whose SessionNum matches the current telemetry SessionNum.
	/// </summary>
	public string? GetCurrentSessionType()
	{
		var sessionInfo = _irsdk.Data.SessionInfo;

		if ( ( sessionInfo != null ) && ( sessionInfo.SessionInfo != null ) && ( sessionInfo.SessionInfo.Sessions != null ) )
		{
			foreach ( var session in sessionInfo.SessionInfo.Sessions )
			{
				if ( session.SessionNum == SessionNum )
				{
					return session.SessionType;
				}
			}
		}

		return null;
	}

	private void OnException( Exception exception )
	{
		App.Instance!.ShowFatalError( null, exception );
	}

	private void OnConnected()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Simulator] OnConnected >>>" );

		WindowHandle = PInvoke.FindWindow( null, "iRacing.com Simulator" );

		app.PlayoutTimer.Suspend = false;

		_waitingForFirstSessionInfo = true;

		// iRacing has just configured the shared Windows audio endpoint; reset our output device (close, wait,
		// reopen) so our stream re-establishes after iRacing's. Otherwise, when MAIRA was started before the
		// sim, the Windows audio engine stays in a heavy mode that starves iRacing's audio (its C/A meters peg).
		app.AudioManager.ResetDeviceForSimulatorConnect();

		app.RacingWheel.ResetForceFeedback = true;

		app.AdminBoxx.SimulatorConnected();

#if !ADMINBOXX

		app.SpeechToText.SimulatorConnected();
		app.AppManager.SimulatorConnected();

#endif

		Array.Clear( RPMSpeedRatios );
		Array.Clear( _rpmSpeedRatioAccumulator );
		Array.Clear( _rpmSpeedRatioSampleCount );

		app.Logger.WriteLine( "[Simulator] <<< OnConnected" );
	}

	private void OnDisconnected()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Simulator] OnDisconnected >>>" );

		app.RacingWheel.UseSteeringWheelTorqueData = false;

		WindowHandle = null;

		_telemetryDataInitialized = false;
		_waitingForFirstSessionInfo = false;

		AvailableTires = null;
		BrakeABSactive = false;
		Brake = 0f;
		BrakeRaw = 0f;
		CarDistAhead = 0f;
		CarDistBehind = 0f;
		CarLeftRight = IRacingSdkEnum.CarLeftRight.Off;
		CarScreenName = string.Empty;
		CarSetupName = string.Empty;
		Clutch = 0f;
		CurrentRpmSpeedRatio = 0f;
		CurrentTireIndex = -1;
		CurrentTireCompoundType = string.Empty;
		DisplayUnits = 0;
		DriverIsAdmin = false;
		EngineRunning = false;
		Gear = 0;
		LongitudinalGForce = 0f;
		LateralGForce = 0f;
		MaxShockVelocity = 0f;
		IsOnTrack = false;
		IsReplayPlaying = false;
		Lap = 0;
		LapBestLapTime = 0f;
		LapDist = 0f;
		LapDistPct = 0f;
		LapLastLapTime = 0f;
		LastRadioTransmitCarIdx = -1;
		LatAccel = 0f;
		LoadNumTextures = false;
		LongAccel = 0f;
		NumForwardGears = 0;
		OnPitRoad = false;
		FuelLevel = 0f;
		FuelLevelPct = 0f;
		FuelUsePerHour = 0f;
		PaceMode = IRacingSdkEnum.PaceMode.NotPacing;
		Pitch = 0f;
		PitsOpen = false;
		PlayerCarClassPosition = 0;
		PlayerCarIdx = 0;
		PlayerCarMyIncidentCount = 0;
		PlayerCarPosition = 0;
		PlayerTrackSurface = IRacingSdkEnum.TrkLoc.NotInWorld;
		PlayerTrackSurfaceMaterial = IRacingSdkEnum.TrkSurf.SurfaceNotInWorld;
		RadioTransmitCarIdx = -1;
		ReplayFrameNumEnd = 1;
		ReplayPlaySlowMotion = false;
		ReplayPlaySpeed = 1;
		Roll = 0f;
		RPM = 0f;
		SessionFlags = 0;
		SessionID = 0;
		SessionLapsRemainEx = 0;
		SessionNum = 0;
		SessionState = IRacingSdkEnum.SessionState.Invalid;
		SessionTime = 0;
		SessionTimeRemain = 0;
		Speed = 0f;
		RedlineRPM = 0f;
		ShiftLightsFirstRPM = 0f;
		ShiftLightsShiftRPM = 0f;
		ShiftLightsLastRPM = 0f;
		ShiftLightsBlinkRPM = 0f;
		SimMode = string.Empty;
		SteeringFFBEnabled = false;
		SteeringOffsetInDegrees = 0f;
		SteeringRatio = 10f;
		SteeringWheelAngle = 0f;
		SteeringWheelAngleMax = 0f;
		SteeringWheelVelocity = 0f;
		Throttle = 0f;
		ThrottleRaw = 0f;
		TireLF_RumblePitch = 0f;
		TireRF_RumblePitch = 0f;
		TireLR_RumblePitch = 0f;
		TireRR_RumblePitch = 0f;
		TrackDisplayName = string.Empty;
		TrackConfigName = string.Empty;
		TrackLength = 0f;
		UserName = string.Empty;
		Velocity = 0f;
		VelocityX = 0f;
		VelocityY = 0f;
		VertAccel = 0f;
		WasOnTrack = false;
		WeatherDeclaredWet = false;
		Yaw = 0f;
		YawNorth = 0f;
		YawRate = 0f;

		Array.Clear( YawRate_ST );
		Array.Clear( VelocityY_ST );
		Array.Clear( LatAccel_ST );
		Array.Clear( RollRate_ST );
		Array.Clear( PitchRate_ST );
		Array.Clear( LFShockDefl_ST );
		Array.Clear( RFShockDefl_ST );
		Array.Clear( CFShockVel_ST );
		Array.Clear( CRShockVel_ST );
		Array.Clear( LFShockVel_ST );
		Array.Clear( LRShockVel_ST );
		Array.Clear( RFShockVel_ST );
		Array.Clear( RRShockVel_ST );

		Array.Clear( SteeringWheelTorque_ST );

		Array.Clear( RPMSpeedRatios );
		Array.Clear( _rpmSpeedRatioAccumulator );
		Array.Clear( _rpmSpeedRatioSampleCount );

		_activeResetBlockTimerFrames = 0;
		_currentTireIndexLastFrame = null;
		_displayUnitsLastFrame = null;
		_isReplayPlayingLastFrame = null;
		_lapDistLastFrame = null;
		_radioTransmitCarIdxLastFrame = null;
		_sessionFlagsLastFrame = null;
		_sessionNumLastFrame = null;
		_sessionStateLastFrame = null;
		_tickCountLastFrame = null;
		_weatherDeclaredWetLastFrame = null;

		_ignoreOneLapToGreenUntilNextSession = false;
		_ignoreBlueUntilCleared = false;

		DataContext.DataContext.Instance.Settings.UpdateSettings( false );

		app.AdminBoxx.SimulatorDisconnected();

#if !ADMINBOXX

		app.SteeringEffects.SimulatorDisconnected();
		app.SpeechToText.SimulatorDisconnected();
		app.AppManager.SimulatorDisconnected();
		app.TradingPaints.SimulatorDisconnected();

		app.TimingMarkers.Reset();

		app.UpdateGripOMeterWindowVisibility();
		app.UpdateSpeechToTextWindowVisibility();
		app.UpdateGapMonitorWindowVisibility();
		app.UpdateDeltaMonitorWindowVisibility();

#endif

		app.PlayoutTimer.Suspend = true;

		app.MainWindow.UpdateStatus();

		_racingWheelPage.UpdateSteeringDeviceSection();

		app.Logger.WriteLine( "[Simulator] <<< OnDisconnected" );
	}

	private void OnSessionInfo()
	{
		var app = App.Instance!;

		var sessionInfo = _irsdk.Data.SessionInfo;

		CarSetupName = Path.GetFileNameWithoutExtension( sessionInfo.DriverInfo.DriverSetupName ).ToLower();

		var newDriverIsAdmin = ( sessionInfo.DriverInfo.DriverIsAdmin != 0 );

		if ( newDriverIsAdmin != DriverIsAdmin )
		{
			app.Logger.WriteLine( $"[Simulator] DriverIsAdmin changed: {DriverIsAdmin} -> {newDriverIsAdmin}" );

			DriverIsAdmin = newDriverIsAdmin;

			app.AdminBoxx.DriverIsAdminChanged();
		}

		NumForwardGears = sessionInfo.DriverInfo.DriverCarGearNumForward;

		RedlineRPM = sessionInfo.DriverInfo.DriverCarRedLine;

		ShiftLightsFirstRPM = sessionInfo.DriverInfo.DriverCarSLFirstRPM;
		ShiftLightsShiftRPM = sessionInfo.DriverInfo.DriverCarSLShiftRPM;
		ShiftLightsLastRPM = sessionInfo.DriverInfo.DriverCarSLLastRPM;
		ShiftLightsBlinkRPM = sessionInfo.DriverInfo.DriverCarSLBlinkRPM;

		// Logged once per car because the four values differ a lot between cars, and anything driving a rev
		// bar has to pick a range out of them. Session info refreshes every couple of seconds, so this only
		// prints when the numbers actually change.
		if ( ( ShiftLightsFirstRPM != _lastLoggedShiftLightsFirstRPM ) || ( ShiftLightsLastRPM != _lastLoggedShiftLightsLastRPM ) )
		{
			_lastLoggedShiftLightsFirstRPM = ShiftLightsFirstRPM;
			_lastLoggedShiftLightsLastRPM = ShiftLightsLastRPM;

			app.Logger.WriteLine( $"[Simulator] Shift lights: first={ShiftLightsFirstRPM:F0} shift={ShiftLightsShiftRPM:F0} last={ShiftLightsLastRPM:F0} blink={ShiftLightsBlinkRPM:F0} redline={RedlineRPM:F0}" );
		}

		if ( ShiftLightsShiftRPM <= ShiftLightsFirstRPM )
		{
			ShiftLightsShiftRPM = sessionInfo.DriverInfo.DriverCarSLBlinkRPM;
		}

		// Not every car fills all four in, and a game bridge only synthesises some of them, so each one
		// falls back to the next value down rather than collapsing a rev bar to zero width.
		if ( ShiftLightsLastRPM <= ShiftLightsFirstRPM )
		{
			ShiftLightsLastRPM = ShiftLightsShiftRPM;
		}

		if ( ShiftLightsBlinkRPM <= ShiftLightsLastRPM )
		{
			ShiftLightsBlinkRPM = ShiftLightsLastRPM;
		}

		// Some cars publish a blink RPM above their own redline, which the engine simply never reaches (the
		// Global MX-5 asks for 7700 against a 7525 redline), so anything waiting on it waits forever. The
		// limiter does hold the engine at the redline, so that is the highest point worth reacting to.
		if ( ( RedlineRPM > 0f ) && ( ShiftLightsBlinkRPM > RedlineRPM ) )
		{
			ShiftLightsBlinkRPM = RedlineRPM;
		}

		// Only repaint the steering device fault message when the sim mode actually changes
		// (OnSessionInfo can fire as often as every ~2 seconds). This covers the case where iRacing
		// FFB is already enabled at connect time: no suspend/device transition fires in the FFB loop,
		// so the empty -> "full" SimMode transition on connect is what refreshes the otherwise-stale
		// "SimulatorNotRunning" message. On disconnect SimMode is reset to empty, so reconnects retrigger.

		var newSimMode = sessionInfo.WeekendInfo.SimMode;

		if ( newSimMode != SimMode )
		{
			SimMode = newSimMode;

			_racingWheelPage.UpdateSteeringDeviceSection();
		}

		var previousCarScreenName = CarScreenName;
		var previousTrackDisplayName = TrackDisplayName;
		var previousTrackConfigName = TrackConfigName;

		foreach ( var driver in sessionInfo.DriverInfo.Drivers )
		{
			if ( driver.CarIdx == sessionInfo.DriverInfo.DriverCarIdx )
			{
				CarScreenName = driver.CarScreenName ?? string.Empty;
				UserName = driver.UserName ?? string.Empty;
				break;
			}
		}

		TrackDisplayName = sessionInfo.WeekendInfo.TrackDisplayName ?? string.Empty;
		TrackConfigName = sessionInfo.WeekendInfo.TrackConfigName ?? string.Empty;

		if ( sessionInfo.CarSetup?.Chassis?.Front?.SteeringOffset != null )
		{
			var numericPart = SteeringOffsetRegex().Replace( sessionInfo.CarSetup.Chassis.Front.SteeringOffset, "" ).Trim();

			if ( float.TryParse( numericPart, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result ) )
			{
				SteeringOffsetInDegrees = result;
			}
			else
			{
				SteeringOffsetInDegrees = 0f;
			}
		}
		else
		{
			SteeringOffsetInDegrees = 0f;
		}

		if ( sessionInfo.CarSetup?.Chassis?.Front?.SteeringRatio != null )
		{
			var numericPart = SteeringRatioRegex().Replace( sessionInfo.CarSetup.Chassis.Front.SteeringRatio, "" ).Trim();

			if ( float.TryParse( numericPart, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result ) )
			{
				SteeringRatio = result;
			}
			else
			{
				SteeringRatio = 10f;
			}
		}
		else
		{
			SteeringRatio = 10f;
		}

		SeriesID = sessionInfo.WeekendInfo.SeriesID;
		LeagueID = sessionInfo.WeekendInfo.LeagueID;
		TimeOfDay = sessionInfo.WeekendInfo.WeekendOptions.TimeOfDay;

		var match = TrackLengthRegex().Match( sessionInfo.WeekendInfo.TrackLength );

		if ( match.Success )
		{
			TrackLength = float.Parse( match.Groups[ 1 ].Value, CultureInfo.InvariantCulture.NumberFormat );
		}
		else
		{
			TrackLength = 0f;
		}

		app.Drivers.Update( sessionInfo );
		app.TimingMarkers.UpdateTrackLength();

		app.MainWindow.UpdateStatus();

		// game bridges can switch the car or track without a disconnect (iRacing always tears the session
		// down first, so there the first-session-info reload is enough) - a mid-connection change must
		// reload the context settings and calibration too
		var contextChanged = !_waitingForFirstSessionInfo && ( ( CarScreenName != previousCarScreenName ) || ( TrackDisplayName != previousTrackDisplayName ) || ( TrackConfigName != previousTrackConfigName ) );

		if ( _waitingForFirstSessionInfo || contextChanged )
		{
			DataContext.DataContext.Instance.Settings.UpdateSettings( false );

			UpdateTireProperties();

#if !ADMINBOXX

			app.SteeringEffects.LoadCalibration();

#endif

			_waitingForFirstSessionInfo = false;
		}

		if ( SessionID != sessionInfo.WeekendInfo.SessionID )
		{
			SessionID = sessionInfo.WeekendInfo.SessionID;

			app.TradingPaints.Reset();
		}

		app.TradingPaints.Update();

#if DEBUG

		// Write out SessionInfo.yaml file

		var sessionInfoYaml = _irsdk.Data.SessionInfoYaml;

		var filePath = Path.Combine( App.DocumentsFolder, "SessionInfo.yaml" );

		File.WriteAllText( filePath, sessionInfoYaml );

		// Write out TelemetryData.yaml file

		filePath = Path.Combine( App.DocumentsFolder, "TelemetryData.yaml" );

		var serializer = new SerializerBuilder().WithNamingConvention( CamelCaseNamingConvention.Instance ).Build();

		var yaml = serializer.Serialize( _irsdk.Data.TelemetryDataProperties );

		File.WriteAllText( filePath, yaml );

#endif
	}

	private void OnTelemetryData()
	{
		var app = App.Instance!;

		// initialize telemetry data properties

		if ( !_telemetryDataInitialized )
		{
			_brakeABSactiveDatum = _irsdk.Data.TelemetryDataProperties[ "BrakeABSactive" ];
			_brakeDatum = _irsdk.Data.TelemetryDataProperties[ "Brake" ];
			_brakeRawDatum = _irsdk.Data.TelemetryDataProperties[ "BrakeRaw" ];
			_carIdxBestLapTimeDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxBestLapTime" ];
			_carIdxEstTimeDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxEstTime" ];
			_carIdxF2TimeDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxF2Time" ];
			_carIdxLapDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxLap" ];
			_carIdxLapCompletedDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxLapCompleted" ];
			_carIdxLapDistPctDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxLapDistPct" ];
			_carIdxPositionDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxPosition" ];
			_carIdxOnPitRoadDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxOnPitRoad" ];
			_carIdxSessionFlagsDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxSessionFlags" ];
			_carIdxTireCompoundDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxTireCompound" ];
			_carDistAheadDatum = _irsdk.Data.TelemetryDataProperties[ "CarDistAhead" ];
			_carDistBehindDatum = _irsdk.Data.TelemetryDataProperties[ "CarDistBehind" ];
			_carLeftRightDatum = _irsdk.Data.TelemetryDataProperties[ "CarLeftRight" ];
			_clutchDatum = _irsdk.Data.TelemetryDataProperties[ "Clutch" ];
			_displayUnitsDatum = _irsdk.Data.TelemetryDataProperties[ "DisplayUnits" ];
			_engineWarningsDatum = _irsdk.Data.TelemetryDataProperties[ "EngineWarnings" ];
			_frameRateDatum = _irsdk.Data.TelemetryDataProperties[ "FrameRate" ];
			_gearDatum = _irsdk.Data.TelemetryDataProperties[ "Gear" ];
			_gpuUsageDatum = _irsdk.Data.TelemetryDataProperties[ "GpuUsage" ];
			_isOnTrackDatum = _irsdk.Data.TelemetryDataProperties[ "IsOnTrack" ];
			_isReplayPlayingDatum = _irsdk.Data.TelemetryDataProperties[ "IsReplayPlaying" ];
			_fuelLevelDatum = _irsdk.Data.TelemetryDataProperties[ "FuelLevel" ];
			_fuelLevelPctDatum = _irsdk.Data.TelemetryDataProperties[ "FuelLevelPct" ];
			_fuelUsePerHourDatum = _irsdk.Data.TelemetryDataProperties[ "FuelUsePerHour" ];
			_lapBestLapTimeDatum = _irsdk.Data.TelemetryDataProperties[ "LapBestLapTime" ];
			_lapDatum = _irsdk.Data.TelemetryDataProperties[ "Lap" ];
			_lapDistDatum = _irsdk.Data.TelemetryDataProperties[ "LapDist" ];
			_lapDistPctDatum = _irsdk.Data.TelemetryDataProperties[ "LapDistPct" ];
			_lapLastLapTimeDatum = _irsdk.Data.TelemetryDataProperties[ "LapLastLapTime" ];
			_latAccelDatum = _irsdk.Data.TelemetryDataProperties[ "LatAccel" ];
			_loadNumTexturesDatum = _irsdk.Data.TelemetryDataProperties[ "LoadNumTextures" ];
			_longAccelDatum = _irsdk.Data.TelemetryDataProperties[ "LongAccel" ];
			_onPitRoadDatum = _irsdk.Data.TelemetryDataProperties[ "OnPitRoad" ];
			_paceModeDatum = _irsdk.Data.TelemetryDataProperties[ "PaceMode" ];
			_pitchDatum = _irsdk.Data.TelemetryDataProperties[ "Pitch" ];
			_pitsOpenDatum = _irsdk.Data.TelemetryDataProperties[ "PitsOpen" ];
			_playerCarClassPositionDatum = _irsdk.Data.TelemetryDataProperties[ "PlayerCarClassPosition" ];
			_playerCarIdxDatum = _irsdk.Data.TelemetryDataProperties[ "PlayerCarIdx" ];
			_playerCarMyIncidentCountDatum = _irsdk.Data.TelemetryDataProperties[ "PlayerCarMyIncidentCount" ];
			_playerCarPositionDatum = _irsdk.Data.TelemetryDataProperties[ "PlayerCarPosition" ];
			_playerTrackSurfaceDatum = _irsdk.Data.TelemetryDataProperties[ "PlayerTrackSurface" ];
			_playerTrackSurfaceMaterialDatum = _irsdk.Data.TelemetryDataProperties[ "PlayerTrackSurfaceMaterial" ];
			_radioTransmitCarIdxDatum = _irsdk.Data.TelemetryDataProperties[ "RadioTransmitCarIdx" ];
			_replayFrameNumEndDatum = _irsdk.Data.TelemetryDataProperties[ "ReplayFrameNumEnd" ];
			_replayPlaySlowMotionDatum = _irsdk.Data.TelemetryDataProperties[ "ReplayPlaySlowMotion" ];
			_replayPlaySpeedDatum = _irsdk.Data.TelemetryDataProperties[ "ReplayPlaySpeed" ];
			_rollDatum = _irsdk.Data.TelemetryDataProperties[ "Roll" ];
			_rpmDatum = _irsdk.Data.TelemetryDataProperties[ "RPM" ];
			_sessionFlagsDatum = _irsdk.Data.TelemetryDataProperties[ "SessionFlags" ];
			_sessionLapsRemainExDatum = _irsdk.Data.TelemetryDataProperties[ "SessionLapsRemainEx" ];
			_sessionNumDatum = _irsdk.Data.TelemetryDataProperties[ "SessionNum" ];
			_sessionStateDatum = _irsdk.Data.TelemetryDataProperties[ "SessionState" ];
			_sessionTimeDatum = _irsdk.Data.TelemetryDataProperties[ "SessionTime" ];
			_sessionTimeRemainDatum = _irsdk.Data.TelemetryDataProperties[ "SessionTimeRemain" ];
			_speedDatum = _irsdk.Data.TelemetryDataProperties[ "Speed" ];
			_steeringFFBEnabledDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringFFBEnabled" ];
			_steeringWheelAngleDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringWheelAngle" ];
			_steeringWheelAngleMaxDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringWheelAngleMax" ];
			_steeringWheelTorque_STDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringWheelTorque_ST" ];
			_throttleDatum = _irsdk.Data.TelemetryDataProperties[ "Throttle" ];
			_throttleRawDatum = _irsdk.Data.TelemetryDataProperties[ "ThrottleRaw" ];
			_tireLF_RumblePitchDatum = _irsdk.Data.TelemetryDataProperties[ "TireLF_RumblePitch" ];
			_tireRF_RumblePitchDatum = _irsdk.Data.TelemetryDataProperties[ "TireRF_RumblePitch" ];
			_tireLR_RumblePitchDatum = _irsdk.Data.TelemetryDataProperties[ "TireLR_RumblePitch" ];
			_tireRR_RumblePitchDatum = _irsdk.Data.TelemetryDataProperties[ "TireRR_RumblePitch" ];
			_velocityXDatum = _irsdk.Data.TelemetryDataProperties[ "VelocityX" ];
			_velocityYDatum = _irsdk.Data.TelemetryDataProperties[ "VelocityY" ];
			_vertAccelDatum = _irsdk.Data.TelemetryDataProperties[ "VertAccel" ];
			_weatherDeclaredWetDatum = _irsdk.Data.TelemetryDataProperties[ "WeatherDeclaredWet" ];
			_yawDatum = _irsdk.Data.TelemetryDataProperties[ "Yaw" ];
			_yawNorthDatum = _irsdk.Data.TelemetryDataProperties[ "YawNorth" ];
			_yawRateDatum = _irsdk.Data.TelemetryDataProperties[ "YawRate" ];

			CarIdxBestLapTime = new float[ _carIdxBestLapTimeDatum.Count ];
			CarIdxEstTime = new float[ _carIdxEstTimeDatum.Count ];
			CarIdxF2Time = new float[ _carIdxF2TimeDatum.Count ];
			CarIdxLap = new int[ _carIdxLapDatum.Count ];
			CarIdxLapCompleted = new int[ _carIdxLapCompletedDatum.Count ];
			CarIdxLapDistPct = new float[ _carIdxLapDistPctDatum.Count ];
			CarIdxPosition = new int[ _carIdxPositionDatum.Count ];
			CarIdxOnPitRoad = new bool[ _carIdxOnPitRoadDatum.Count ];
			CarIdxSessionFlags = new uint[ _carIdxSessionFlagsDatum.Count ];

			_carIdxTireCompounds = new int[ _carIdxTireCompoundDatum.Count ];

			_cfShockVel_STDatum = null;
			_crShockVel_STDatum = null;
			_lfShockVel_STDatum = null;
			_lrShockVel_STDatum = null;
			_rfShockVel_STDatum = null;
			_rrShockVel_STDatum = null;

			_irsdk.Data.TelemetryDataProperties.TryGetValue( "CFshockVel_ST", out _cfShockVel_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "CRshockVel_ST", out _crShockVel_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "LFshockVel_ST", out _lfShockVel_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "LRshockVel_ST", out _lrShockVel_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "RFshockVel_ST", out _rfShockVel_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "RRshockVel_ST", out _rrShockVel_STDatum );

			_irsdk.Data.TelemetryDataProperties.TryGetValue( "YawRate_ST", out _yawRate_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "VelocityY_ST", out _velocityY_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "LatAccel_ST", out _latAccel_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "RollRate_ST", out _rollRate_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "PitchRate_ST", out _pitchRate_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "LFshockDefl_ST", out _lfShockDefl_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "RFshockDefl_ST", out _rfShockDefl_STDatum );

			// log array datum counts so we can detect if any exceed our destination array sizes
			var logger = app.Logger;

			logger.WriteLine( $"[Simulator] IRacingSdkConst.MaxNumCars = {IRacingSdkConst.MaxNumCars}" );
			logger.WriteLine( $"[Simulator] Array datum counts on initialization:" );
			logger.WriteLine( $"[Simulator]   CarIdxBestLapTime.Count   = {_carIdxBestLapTimeDatum!.Count}" );
			logger.WriteLine( $"[Simulator]   CarIdxEstTime.Count       = {_carIdxEstTimeDatum!.Count}" );
			logger.WriteLine( $"[Simulator]   CarIdxF2Time.Count        = {_carIdxF2TimeDatum!.Count}" );
			logger.WriteLine( $"[Simulator]   CarIdxLap.Count           = {_carIdxLapDatum!.Count}" );
			logger.WriteLine( $"[Simulator]   CarIdxLapCompleted.Count  = {_carIdxLapCompletedDatum!.Count}" );
			logger.WriteLine( $"[Simulator]   CarIdxLapDistPct.Count    = {_carIdxLapDistPctDatum!.Count}" );
			logger.WriteLine( $"[Simulator]   CarIdxOnPitRoad.Count     = {_carIdxOnPitRoadDatum!.Count}" );
			logger.WriteLine( $"[Simulator]   CarIdxPosition.Count      = {_carIdxPositionDatum!.Count}" );
			logger.WriteLine( $"[Simulator]   CarIdxSessionFlags.Count  = {_carIdxSessionFlagsDatum!.Count}" );
			logger.WriteLine( $"[Simulator]   CFShockVel_ST.Count       = {_cfShockVel_STDatum?.Count.ToString() ?? "n/a (not present)"}" );
			logger.WriteLine( $"[Simulator]   CRShockVel_ST.Count       = {_crShockVel_STDatum?.Count.ToString() ?? "n/a (not present)"}" );
			logger.WriteLine( $"[Simulator]   LFShockVel_ST.Count       = {_lfShockVel_STDatum?.Count.ToString() ?? "n/a (not present)"}" );
			logger.WriteLine( $"[Simulator]   LRShockVel_ST.Count       = {_lrShockVel_STDatum?.Count.ToString() ?? "n/a (not present)"}" );
			logger.WriteLine( $"[Simulator]   RFShockVel_ST.Count       = {_rfShockVel_STDatum?.Count.ToString() ?? "n/a (not present)"}" );
			logger.WriteLine( $"[Simulator]   RRShockVel_ST.Count       = {_rrShockVel_STDatum?.Count.ToString() ?? "n/a (not present)"}" );
			logger.WriteLine( $"[Simulator]   YawRate_ST.Count          = {_yawRate_STDatum?.Count.ToString() ?? "n/a (not present)"}" );
			logger.WriteLine( $"[Simulator]   VelocityY_ST.Count        = {_velocityY_STDatum?.Count.ToString() ?? "n/a (not present)"}" );
			logger.WriteLine( $"[Simulator]   LatAccel_ST.Count         = {_latAccel_STDatum?.Count.ToString() ?? "n/a (not present)"}" );
			logger.WriteLine( $"[Simulator]   RollRate_ST.Count         = {_rollRate_STDatum?.Count.ToString() ?? "n/a (not present)"}" );
			logger.WriteLine( $"[Simulator]   PitchRate_ST.Count        = {_pitchRate_STDatum?.Count.ToString() ?? "n/a (not present)"}" );
			logger.WriteLine( $"[Simulator]   LFShockDefl_ST.Count      = {_lfShockDefl_STDatum?.Count.ToString() ?? "n/a (not present)"}" );
			logger.WriteLine( $"[Simulator]   RFShockDefl_ST.Count      = {_rfShockDefl_STDatum?.Count.ToString() ?? "n/a (not present)"}" );

			app.TimingMarkers.Initialize( _carIdxLapDistPctDatum.Count );

			_telemetryDataInitialized = true;
		}

		// shortcut to settings

		var settings = DataContext.DataContext.Instance.Settings;

		// set last frame tick count if its not been set yet

		_tickCountLastFrame ??= _irsdk.Data.TickCount - 1;

		// calculate delta time

		var deltaSeconds = (float) ( _irsdk.Data.TickCount - (int) _tickCountLastFrame ) / _irsdk.Data.TickRate;

		// update tick count last frame

		_tickCountLastFrame = _irsdk.Data.TickCount;

		// protect ourselves from zero or negative time just in case

		if ( deltaSeconds <= 0f )
		{
			return;
		}

		// poll directinput devices (button/POV input) right before we process the FFB frame; wheel position and
		// velocity now come from iRacing's steering telemetry (read above), not our own DirectInput axis sampling

		app.DirectInput.PollDevices();

		// get next 360 Hz steering wheel torque samples

		_irsdk.Data.GetFloatArray( _steeringWheelTorque_STDatum, SteeringWheelTorque_ST, 0, SteeringWheelTorque_ST.Length );

		// save last frame values

		_currentTireIndexLastFrame = CurrentTireIndex;
		_displayUnitsLastFrame = DisplayUnits;
		_isReplayPlayingLastFrame = IsReplayPlaying;
		_lapDistLastFrame = LapDist;
		_radioTransmitCarIdxLastFrame = RadioTransmitCarIdx;
		_sessionFlagsLastFrame = SessionFlags;
		_sessionNumLastFrame = SessionNum;
		_sessionStateLastFrame = SessionState;
		_weatherDeclaredWetLastFrame = WeatherDeclaredWet;

		WasOnTrack = IsOnTrack;

		// update non-array telemetry data properties

		BrakeABSactive = _irsdk.Data.GetBool( _brakeABSactiveDatum );
		Brake = _irsdk.Data.GetFloat( _brakeDatum );
		BrakeRaw = _irsdk.Data.GetFloat( _brakeRawDatum );
		Clutch = _irsdk.Data.GetFloat( _clutchDatum );
		CarDistAhead = _irsdk.Data.GetFloat( _carDistAheadDatum );
		CarDistBehind = _irsdk.Data.GetFloat( _carDistBehindDatum );
		CarLeftRight = (IRacingSdkEnum.CarLeftRight) _irsdk.Data.GetInt( _carLeftRightDatum );
		DisplayUnits = _irsdk.Data.GetInt( _displayUnitsDatum );
		EngineRunning = ( (IRacingSdkEnum.EngineWarnings) _irsdk.Data.GetBitField( _engineWarningsDatum ) & IRacingSdkEnum.EngineWarnings.EngineStalled ) == 0;
		FrameRate = _irsdk.Data.GetFloat( _frameRateDatum );
		Gear = _irsdk.Data.GetInt( _gearDatum );
		GpuUsage = _irsdk.Data.GetFloat( _gpuUsageDatum );
		IsOnTrack = _irsdk.Data.GetBool( _isOnTrackDatum );
		IsReplayPlaying = _irsdk.Data.GetBool( _isReplayPlayingDatum );
		Lap = _irsdk.Data.GetInt( _lapDatum );
		LapBestLapTime = _irsdk.Data.GetFloat( _lapBestLapTimeDatum );
		LapDist = _irsdk.Data.GetFloat( _lapDistDatum );
		LapDistPct = _irsdk.Data.GetFloat( _lapDistPctDatum );
		LapLastLapTime = _irsdk.Data.GetFloat( _lapLastLapTimeDatum );
		LatAccel = _irsdk.Data.GetFloat( _latAccelDatum );
		LoadNumTextures = _irsdk.Data.GetBool( _loadNumTexturesDatum );
		LongAccel = _irsdk.Data.GetFloat( _longAccelDatum );
		FuelLevel = _irsdk.Data.GetFloat( _fuelLevelDatum );
		FuelLevelPct = _irsdk.Data.GetFloat( _fuelLevelPctDatum );
		FuelUsePerHour = _irsdk.Data.GetFloat( _fuelUsePerHourDatum );
		OnPitRoad = _irsdk.Data.GetBool( _onPitRoadDatum );
		PitsOpen = _irsdk.Data.GetBool( _pitsOpenDatum );
		PlayerCarClassPosition = _irsdk.Data.GetInt( _playerCarClassPositionDatum );
		PlayerCarMyIncidentCount = _irsdk.Data.GetInt( _playerCarMyIncidentCountDatum );
		PlayerCarPosition = _irsdk.Data.GetInt( _playerCarPositionDatum );
		PaceMode = (IRacingSdkEnum.PaceMode) _irsdk.Data.GetInt( _paceModeDatum );
		Pitch = _irsdk.Data.GetFloat( _pitchDatum );
		PlayerCarIdx = _irsdk.Data.GetInt( _playerCarIdxDatum );
		PlayerTrackSurface = (IRacingSdkEnum.TrkLoc) _irsdk.Data.GetInt( _playerTrackSurfaceDatum );
		PlayerTrackSurfaceMaterial = (IRacingSdkEnum.TrkSurf) _irsdk.Data.GetInt( _playerTrackSurfaceMaterialDatum );
		RadioTransmitCarIdx = _irsdk.Data.GetInt( _radioTransmitCarIdxDatum );
		ReplayFrameNumEnd = _irsdk.Data.GetInt( _replayFrameNumEndDatum );
		ReplayPlaySlowMotion = _irsdk.Data.GetBool( _replayPlaySlowMotionDatum );
		ReplayPlaySpeed = _irsdk.Data.GetInt( _replayPlaySpeedDatum );
		Roll = _irsdk.Data.GetFloat( _rollDatum );
		RPM = _irsdk.Data.GetFloat( _rpmDatum );
		SessionFlags = (IRacingSdkEnum.Flags) _irsdk.Data.GetBitField( _sessionFlagsDatum );
		SessionLapsRemainEx = _irsdk.Data.GetInt( _sessionLapsRemainExDatum );
		SessionNum = _irsdk.Data.GetInt( _sessionNumDatum );
		SessionState = (IRacingSdkEnum.SessionState) _irsdk.Data.GetInt( _sessionStateDatum );

		// work around simulator bugs that turn on the wrong session flag bits (needs SessionNum already read above)

		SessionFlags = FilterBuggySessionFlags( SessionFlags );
		SessionTime = _irsdk.Data.GetDouble( _sessionTimeDatum );
		SessionTimeRemain = _irsdk.Data.GetDouble( _sessionTimeRemainDatum );
		Speed = _irsdk.Data.GetFloat( _speedDatum );
		SteeringFFBEnabled = _irsdk.Data.GetBool( _steeringFFBEnabledDatum );
		var steeringWheelAngleLastFrame = SteeringWheelAngle;

		SteeringWheelAngle = _irsdk.Data.GetFloat( _steeringWheelAngleDatum );
		SteeringWheelAngleMax = _irsdk.Data.GetFloat( _steeringWheelAngleMaxDatum );
		SteeringWheelVelocity = ( SteeringWheelAngle - steeringWheelAngleLastFrame ) / deltaSeconds;
		Throttle = _irsdk.Data.GetFloat( _throttleDatum );
		ThrottleRaw = _irsdk.Data.GetFloat( _throttleRawDatum );
		TireLF_RumblePitch = _irsdk.Data.GetFloat( _tireLF_RumblePitchDatum );
		TireRF_RumblePitch = _irsdk.Data.GetFloat( _tireRF_RumblePitchDatum );
		TireLR_RumblePitch = _irsdk.Data.GetFloat( _tireLR_RumblePitchDatum );
		TireRR_RumblePitch = _irsdk.Data.GetFloat( _tireRR_RumblePitchDatum );
		VelocityX = _irsdk.Data.GetFloat( _velocityXDatum );
		VelocityY = _irsdk.Data.GetFloat( _velocityYDatum );
		VertAccel = _irsdk.Data.GetFloat( _vertAccelDatum );
		WeatherDeclaredWet = _irsdk.Data.GetBool( _weatherDeclaredWetDatum );
		Yaw = _irsdk.Data.GetFloat( _yawDatum );
		YawNorth = _irsdk.Data.GetFloat( _yawNorthDatum );
		YawRate = _irsdk.Data.GetFloat( _yawRateDatum );

		// temporarily log changes to the RadioTransmitCarIdx value

		if ( RadioTransmitCarIdx != _radioTransmitCarIdxLastFrame )
		{
			app.Logger.WriteLine( $"[Simulator] RadioTransmitCarIdx changed: {_radioTransmitCarIdxLastFrame} -> {RadioTransmitCarIdx} (LastRadioTransmitCarIdx={LastRadioTransmitCarIdx})" );
		}

		// update array telemetry data properties

		_irsdk.Data.GetIntArray( _carIdxLapDatum, CarIdxLap, 0, _carIdxLapDatum!.Count );
		_irsdk.Data.GetIntArray( _carIdxLapCompletedDatum, CarIdxLapCompleted, 0, _carIdxLapCompletedDatum!.Count );
		_irsdk.Data.GetFloatArray( _carIdxLapDistPctDatum, CarIdxLapDistPct, 0, _carIdxLapDistPctDatum!.Count );
		_irsdk.Data.GetIntArray( _carIdxPositionDatum, CarIdxPosition, 0, _carIdxPositionDatum!.Count );
		_irsdk.Data.GetBoolArray( _carIdxOnPitRoadDatum, CarIdxOnPitRoad, 0, _carIdxOnPitRoadDatum!.Count );
		_irsdk.Data.GetBitFieldArray( _carIdxSessionFlagsDatum, CarIdxSessionFlags, 0, _carIdxSessionFlagsDatum!.Count );
		_irsdk.Data.GetFloatArray( _carIdxF2TimeDatum, CarIdxF2Time, 0, _carIdxF2TimeDatum!.Count );
		_irsdk.Data.GetFloatArray( _carIdxBestLapTimeDatum, CarIdxBestLapTime, 0, _carIdxBestLapTimeDatum!.Count );
		_irsdk.Data.GetFloatArray( _carIdxEstTimeDatum, CarIdxEstTime, 0, _carIdxEstTimeDatum!.Count );

		// get next 360 Hz shock velocity samples

		if ( _cfShockVel_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _cfShockVel_STDatum, CFShockVel_ST, 0, CFShockVel_ST.Length );
		}

		if ( _crShockVel_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _crShockVel_STDatum, CRShockVel_ST, 0, CRShockVel_ST.Length );
		}

		if ( _lfShockVel_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _lfShockVel_STDatum, LFShockVel_ST, 0, LFShockVel_ST.Length );
		}

		if ( _lrShockVel_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _lrShockVel_STDatum, LRShockVel_ST, 0, LRShockVel_ST.Length );
		}

		if ( _rfShockVel_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _rfShockVel_STDatum, RFShockVel_ST, 0, RFShockVel_ST.Length );
		}

		if ( _rrShockVel_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _rrShockVel_STDatum, RRShockVel_ST, 0, RRShockVel_ST.Length );
		}

		// get next 360 Hz yaw rate samples — fall back to the frame's 60 Hz value when the sim doesn't expose the ST array

		if ( _yawRate_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _yawRate_STDatum, YawRate_ST, 0, YawRate_ST.Length );
		}
		else
		{
			Array.Fill( YawRate_ST, YawRate );
		}

		// get next prediction-audit 360 Hz samples (zeros when not exposed, except VelocityY's 60 Hz fallback)

		if ( _velocityY_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _velocityY_STDatum, VelocityY_ST, 0, VelocityY_ST.Length );
		}
		else
		{
			Array.Fill( VelocityY_ST, VelocityY );
		}

		if ( _latAccel_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _latAccel_STDatum, LatAccel_ST, 0, LatAccel_ST.Length );
		}

		if ( _rollRate_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _rollRate_STDatum, RollRate_ST, 0, RollRate_ST.Length );
		}

		if ( _pitchRate_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _pitchRate_STDatum, PitchRate_ST, 0, PitchRate_ST.Length );
		}

		if ( _lfShockDefl_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _lfShockDefl_STDatum, LFShockDefl_ST, 0, LFShockDefl_ST.Length );
		}

		if ( _rfShockDefl_STDatum != null )
		{
			_irsdk.Data.GetFloatArray( _rfShockDefl_STDatum, RFShockDefl_ST, 0, RFShockDefl_ST.Length );
		}

		// update racing wheel

		app.RacingWheel.UseSteeringWheelTorqueData = IsOnTrack;

		// update adminboxx

		if ( IsReplayPlaying != _isReplayPlayingLastFrame )
		{
			app.AdminBoxx.ReplayPlayingChanged();
		}

		if ( SessionFlags != _sessionFlagsLastFrame )
		{
			app.Logger.WriteLine( $"[Simulator] SessionFlags changed: 0x{(uint) ( _sessionFlagsLastFrame ?? 0 ):X8} -> 0x{(uint) SessionFlags:X8} ({GetSessionFlagsBreakdown( SessionFlags )})" );

			app.AdminBoxx.SessionFlagsChanged();
		}

		if ( SessionNum != _sessionNumLastFrame )
		{
			app.Logger.WriteLine( $"[Simulator] SessionNum changed: {_sessionNumLastFrame?.ToString() ?? "(none)"} -> {SessionNum} (SessionType: {GetCurrentSessionType() ?? "(unknown)"})" );
		}

		if ( SessionState != _sessionStateLastFrame )
		{
			app.Commentary.SessionStateChanged( SessionState );
		}

		if ( DisplayUnits != _displayUnitsLastFrame )
		{
			app.Dispatcher.InvokeAsync( () =>
			{
				DataContext.DataContext.Instance.Settings.UpdateSpeedUnitStrings();

				_typhoonWindPage.UpdateSpeedUnitLabel();
			} );
		}

		// update speech-to-text

		if ( RadioTransmitCarIdx != -1 )
		{
			LastRadioTransmitCarIdx = RadioTransmitCarIdx;
		}

		var isRadioTransmitting = RadioTransmitCarIdx != -1 && RadioTransmitCarIdx != PlayerCarIdx;

		app.SpeechToText.UpdateRadioTransmitState( isRadioTransmitting, RadioTransmitCarIdx );

		// update velocity

		Velocity = MathF.Sqrt( VelocityX * VelocityX + VelocityY * VelocityY );

		// calculate g forces (convert from m/s^2 to g's)

		LongitudinalGForce = MathF.Abs( LongAccel ) * MathZ.OneOverG;
		LateralGForce = MathF.Abs( LatAccel ) * MathZ.OneOverG;

		// reload settings if "weather declared wet" property has changed

		if ( _weatherDeclaredWetLastFrame != null )
		{
			if ( WeatherDeclaredWet != _weatherDeclaredWetLastFrame )
			{
				if ( !_waitingForFirstSessionInfo )
				{
					settings.UpdateSettings( false );
				}
			}
		}

		// get the current tire index and the current tire compound type

		if ( ( PlayerCarIdx >= 0 ) && ( PlayerCarIdx < _carIdxTireCompoundDatum!.Count ) )
		{
			_irsdk.Data.GetIntArray( _carIdxTireCompoundDatum, _carIdxTireCompounds, 0, _carIdxTireCompoundDatum.Count );

			CurrentTireIndex = _carIdxTireCompounds[ PlayerCarIdx ]; // iracing's "carIdxTireCompound" data name is wrong - it should probably have been "carIdxTireIdx"

			if ( _currentTireIndexLastFrame != null )
			{
				if ( CurrentTireIndex != _currentTireIndexLastFrame )
				{
					UpdateTireProperties();
				}
			}
		}

		// detect active reset warp (>10m jump in one frame) and block crash protection for 1 second

		if ( _lapDistLastFrame != null )
		{
			var lapDistDelta = MathF.Abs( LapDist - _lapDistLastFrame.Value );

			if ( lapDistDelta > 10f )
			{
				_activeResetBlockTimerFrames = 60;
			}
		}

		if ( _activeResetBlockTimerFrames > 0 )
		{
			_activeResetBlockTimerFrames--;
		}

		// crash protection processing (thresholds now come from the live FFB graph's crash protection module; an
		// off/disabled/would-do-nothing module publishes >= 20, same "disabled" semantics as the old guards)

		if ( IsOnTrack && ( _activeResetBlockTimerFrames <= 0 ) )
		{
			var crashLongGForceThreshold = app.RacingWheel.CrashProtectionLongGForceThreshold;
			var crashLatGForceThreshold = app.RacingWheel.CrashProtectionLatGForceThreshold;

			if ( ( crashLongGForceThreshold < 20f ) && ( LongitudinalGForce >= crashLongGForceThreshold ) )
			{
				app.RacingWheel.ActivateCrashProtection = true;
			}

			if ( ( crashLatGForceThreshold < 20f ) && ( LateralGForce >= crashLatGForceThreshold ) )
			{
				app.RacingWheel.ActivateCrashProtection = true;
			}
		}

		// track this frame's peak absolute shock velocity across all six dampers — it drives the curb protection
		// trigger below and is recorded so the preview replay can re-derive the trigger from the current settings

		var maxShockVelocity = 0f;

		for ( var i = 0; i < SamplesPerFrame360Hz; i++ )
		{
			maxShockVelocity = MathF.Max( maxShockVelocity, MathF.Abs( CFShockVel_ST[ i ] ) );
			maxShockVelocity = MathF.Max( maxShockVelocity, MathF.Abs( CRShockVel_ST[ i ] ) );
			maxShockVelocity = MathF.Max( maxShockVelocity, MathF.Abs( LFShockVel_ST[ i ] ) );
			maxShockVelocity = MathF.Max( maxShockVelocity, MathF.Abs( LRShockVel_ST[ i ] ) );
			maxShockVelocity = MathF.Max( maxShockVelocity, MathF.Abs( RFShockVel_ST[ i ] ) );
			maxShockVelocity = MathF.Max( maxShockVelocity, MathF.Abs( RRShockVel_ST[ i ] ) );
		}

		MaxShockVelocity = maxShockVelocity;

		// curb protection processing (shock-velocity threshold now comes from the live FFB graph's curb protection
		// module; an off/disabled/would-do-nothing module publishes 0, same "disabled" semantics as the old guards)

		if ( IsOnTrack && ( _activeResetBlockTimerFrames <= 0 ) )
		{
			var curbShockVelocityThreshold = app.RacingWheel.CurbProtectionShockVelocityThreshold;

			if ( ( curbShockVelocityThreshold > 0f ) && ( MaxShockVelocity >= curbShockVelocityThreshold ) )
			{
				app.RacingWheel.ActivateCurbProtection = true;
			}
		}

		// update rpm / speed ratios

		if ( IsOnTrack && ( Gear > 0 ) && ( Clutch == 1f ) && ( RPM > 500f ) && ( VelocityX >= 10f * MathZ.MPHToMPS ) )
		{
			CurrentRpmSpeedRatio = VelocityX / RPM;

			if ( ( Brake == 0f ) && ( VelocityY < 0.1f ) && ( PlayerTrackSurface == IRacingSdkEnum.TrkLoc.OnTrack ) )
			{
				if ( RPMSpeedRatios[ Gear ] == 0f )
				{
					// accumulate samples until we have enough to initialize

					_rpmSpeedRatioAccumulator[ Gear ] += CurrentRpmSpeedRatio;
					_rpmSpeedRatioSampleCount[ Gear ]++;

					if ( _rpmSpeedRatioSampleCount[ Gear ] >= RpmSpeedRatioMinSamples )
					{
						RPMSpeedRatios[ Gear ] = _rpmSpeedRatioAccumulator[ Gear ] / _rpmSpeedRatioSampleCount[ Gear ];

						_rpmSpeedRatioAccumulator[ Gear ] = 0f;
						_rpmSpeedRatioSampleCount[ Gear ] = 0;
					}
				}
				else
				{
					// converge to the current sample over approximately 15 seconds (~95% in 15s with rate=0.2)

					var alpha = 1f - MathF.Exp( -deltaSeconds * 0.2f );

					RPMSpeedRatios[ Gear ] = MathZ.Lerp( RPMSpeedRatios[ Gear ], CurrentRpmSpeedRatio, alpha );
				}
			}
		}
		else
		{
			CurrentRpmSpeedRatio = 0f;
		}

		// for ( var gear = 0; gear < Simulator.MaxNumGears; gear++ )
		// {
		// 	app.Debug.Message[ gear ] = $"RPM Speed Ratio Gear {gear}: {RPMSpeedRatios[ gear ] * 100f:F4}";
		// }

		// update visibility of overlays

		if ( ( IsOnTrack != WasOnTrack ) || ( IsReplayPlaying != _isReplayPlayingLastFrame ) )
		{
			app.UpdateGripOMeterWindowVisibility();
			app.UpdateGapMonitorWindowVisibility();
			app.UpdateDeltaMonitorWindowVisibility();
		}

		// update steering effects

		app.SteeringEffects.Update( app, deltaSeconds );

		// run the FFB graph over this frame's six 360 Hz torque samples — after the crash/curb protection triggers
		// and steering effects above, so the whole frame's context is fresh

		app.RacingWheel.ProcessTelemetryFrame();

		// trigger the app worker thread

		app.TriggerWorkerThread();
	}

	private IRacingSdkEnum.Flags FilterBuggySessionFlags( IRacingSdkEnum.Flags sessionFlags )
	{
		// the simulator sometimes turns on the wrong session flag bits - filter out the ones we know are bogus

		// bug 1: once the checkered flag is raised, a "one lap to green" flag no longer makes sense - ignore it for the
		// rest of the session (the latch is cleared on the next session or on simulator disconnect)

		if ( SessionNum != _sessionNumLastFrame )
		{
			_ignoreOneLapToGreenUntilNextSession = false;
		}

		if ( ( sessionFlags & IRacingSdkEnum.Flags.Checkered ) != 0 )
		{
			_ignoreOneLapToGreenUntilNextSession = true;
		}

		if ( _ignoreOneLapToGreenUntilNextSession )
		{
			sessionFlags &= ~IRacingSdkEnum.Flags.OneLapToGreen;
		}

		// bug 2: the simulator briefly raises the blue flag at the same time as "start go" - ignore the blue flag while
		// this is happening and stop ignoring it once the blue flag is no longer raised

		if ( ( sessionFlags & IRacingSdkEnum.Flags.Blue ) != 0 )
		{
			if ( ( sessionFlags & IRacingSdkEnum.Flags.StartGo ) != 0 )
			{
				_ignoreBlueUntilCleared = true;
			}
		}
		else
		{
			_ignoreBlueUntilCleared = false;
		}

		if ( _ignoreBlueUntilCleared )
		{
			sessionFlags &= ~IRacingSdkEnum.Flags.Blue;
		}

		return sessionFlags;
	}

	private static string GetSessionFlagsBreakdown( IRacingSdkEnum.Flags sessionFlags )
	{
		var bits = (uint) sessionFlags;

		var bitsString = string.Empty;

		foreach ( uint bitMask in Enum.GetValues( typeof( IRacingSdkEnum.Flags ) ) )
		{
			if ( ( bits & bitMask ) != 0 )
			{
				if ( bitsString != string.Empty )
				{
					bitsString += " | ";
				}

				bitsString += Enum.GetName( typeof( IRacingSdkEnum.Flags ), bitMask );
			}
		}

		if ( bitsString == string.Empty )
		{
			bitsString = "(none)";
		}

		return bitsString;
	}

	private void UpdateTireProperties()
	{
		var tireFound = false;

		var sessionInfo = _irsdk.Data.SessionInfo;

		if ( sessionInfo != null )
		{
			if ( sessionInfo.DriverInfo != null )
			{
				if ( sessionInfo.DriverInfo.DriverTires != null )
				{
					AvailableTires = sessionInfo.DriverInfo.DriverTires;

					for ( var tireIndex = 0; tireIndex < sessionInfo.DriverInfo.DriverTires.Count; tireIndex++ )
					{
						if ( AvailableTires[ tireIndex ].TireIndex == CurrentTireIndex )
						{
							CurrentTireCompoundType = AvailableTires[ tireIndex ].TireCompoundType.ToLower();

							tireFound = true;

							break;
						}
					}
				}
			}
		}

		if ( !tireFound )
		{
			CurrentTireCompoundType = "unknown";
		}
	}

	private void OnDebugLog( string message )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[IRSDKSharper] {message}" );
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter <= 0 )
		{
			_updateCounter = UpdateInterval;

			_racingWheelPage.CurrentForce_TextBlock.Text = $"{MathF.Abs( SteeringWheelTorque_ST[ 5 ] ):F1}{DataContext.DataContext.Instance.Localization[ "TorqueUnits" ]}";
		}
	}

	[GeneratedRegex( @"\s*deg\s*$", RegexOptions.IgnoreCase, "en-US" )]
	private static partial Regex SteeringOffsetRegex();

	[GeneratedRegex( @"\s*:1\s*$", RegexOptions.IgnoreCase, "en-US" )]
	private static partial Regex SteeringRatioRegex();

	[GeneratedRegex( @"([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase, "en-US" )]
	private static partial Regex TrackLengthRegex();
}
