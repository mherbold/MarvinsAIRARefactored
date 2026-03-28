
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using PInvoke;
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
	public int[] CarIdxLap {  get; private set; } = new int[ IRacingSdkConst.MaxNumCars ];
	public float[] CarIdxLapDistPct { get; private set; } = new float[ IRacingSdkConst.MaxNumCars ];
	public bool[] CarIdxOnPitRoad { get; private set; } = new bool[ IRacingSdkConst.MaxNumCars ];
	public int[] CarIdxPosition { get; private set; } = new int[ IRacingSdkConst.MaxNumCars ];
	public string CarScreenName { get; private set; } = string.Empty;
	public string CarSetupName { get; private set; } = string.Empty;
	public float[] CFShockVel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float Clutch { get; private set; } = 0f;
	public float[] CRShockVel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float CurrentRpmSpeedRatio { get; private set; } = 0f;
	public int CurrentTireIndex { get; private set; } = -1;
	public string CurrentTireCompoundType { get; private set; } = string.Empty;
	public int DisplayUnits { get; private set; } = 0;
	public float FrameRate { get; private set; } = 0;
	public int Gear { get; private set; } = 0;
	public float GpuUsage { get; private set; } = 0f;
	public float LongitudinalGForce { get; private set; } = 0f;
	public float LateralGForce { get; private set; } = 0f;
	public bool IsConnected { get => _irsdk.IsConnected; }
	public bool IsOnTrack { get; private set; } = false;
	public bool IsReplayPlaying { get; private set; } = false;
	public int Lap { get; private set;  } = 0;
	public float LapDist { get; private set; } = 0;
	public float LapDistPct { get; private set; } = 0f;
	public int LastRadioTransmitCarIdx { get; private set; } = -1;
	public float LatAccel { get; private set; } = 0f;
	public int LeagueID { get; private set; } = 0;
	public float[] LFShockVel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public bool LoadNumTextures { get; private set; } = false;
	public float LongAccel { get; private set; } = 0f;
	public float[] LRShockVel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public int NumForwardGears { get; private set; } = 0;
	public IRacingSdkEnum.PaceMode PaceMode { get; private set; } = IRacingSdkEnum.PaceMode.NotPacing;
	public int PlayerCarIdx { get; private set; } = 0;
	public IRacingSdkEnum.TrkLoc PlayerTrackSurface { get; private set; } = IRacingSdkEnum.TrkLoc.NotInWorld;
	public IRacingSdkEnum.TrkSurf PlayerTrackSurfaceMaterial { get; private set; } = IRacingSdkEnum.TrkSurf.SurfaceNotInWorld;
	public int RadioTransmitCarIdx { get; private set; } = -1;
	public int ReplayFrameNumEnd { get; private set; } = 1;
	public bool ReplayPlaySlowMotion { get; private set; } = false;
	public int ReplayPlaySpeed { get; private set; } = 1;
	public float[] RFShockVel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float RPM { get; private set; } = 0f;
	public float[] RPMSpeedRatios { get; private set; } = new float[ MaxNumGears ];
	public float[] RRShockVel_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public int SeriesID { get; private set; } = 0;
	public IRacingSdkEnum.Flags SessionFlags { get; private set; } = 0;
	public int SessionID { get; private set; } = 0;
	public int SessionNum { get; private set; } = 0;
	public double SessionTime { get; private set; } = 0f;
	public float ShiftLightsFirstRPM { get; private set; } = 0f;
	public float ShiftLightsShiftRPM { get; private set; } = 0f;
	public string SimMode { get; private set; } = string.Empty;
	public float Speed { get; private set; } = 0f;
	public bool SteeringFFBEnabled { get; private set; } = false;
	public float SteeringOffsetInDegrees { get; private set; } = 0f;
	public float SteeringRatio { get; private set; } = 10f;
	public float SteeringWheelAngle { get; private set; } = 0f;
	public float SteeringWheelAngleMax { get; private set; } = 0f;
	public float[] SteeringWheelTorque_ST { get; private set; } = new float[ SamplesPerFrame360Hz ];
	public float Throttle { get; private set; } = 0f;
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
	public float YawNorth { get; private set; } = 0f;
	public float YawRate { get; private set; } = 0f;

	private bool _telemetryDataInitialized = false;
	private bool _waitingForFirstSessionInfo = false;

	private int? _tickCountLastFrame = null;
	private bool? _weatherDeclaredWetLastFrame = null;
	private bool? _isReplayPlayingLastFrame = null;
	private IRacingSdkEnum.Flags? _sessionFlagsLastFrame = null;
	private int? _currentTireIndexLastFrame = null;

	private IRacingSdkDatum? _brakeABSactiveDatum = null;
	private IRacingSdkDatum? _brakeDatum = null;
	private IRacingSdkDatum? _carIdxLapDatum = null;
	private IRacingSdkDatum? _carIdxLapDistPctDatum = null;
	private IRacingSdkDatum? _carIdxPositionDatum = null;
	private IRacingSdkDatum? _carIdxOnPitRoadDatum = null;
	private IRacingSdkDatum? _carIdxTireCompoundDatum = null;
	private IRacingSdkDatum? _cfShockVel_STDatum = null;
	private IRacingSdkDatum? _clutchDatum = null;
	private IRacingSdkDatum? _crShockVel_STDatum = null;
	private IRacingSdkDatum? _displayUnitsDatum = null;
	private IRacingSdkDatum? _frameRateDatum = null;
	private IRacingSdkDatum? _gearDatum = null;
	private IRacingSdkDatum? _gpuUsageDatum = null;
	private IRacingSdkDatum? _isOnTrackDatum = null;
	private IRacingSdkDatum? _isReplayPlayingDatum = null;
	private IRacingSdkDatum? _lapDatum = null;
	private IRacingSdkDatum? _lapDistDatum = null;
	private IRacingSdkDatum? _lapDistPctDatum = null;
	private IRacingSdkDatum? _latAccelDatum = null;
	private IRacingSdkDatum? _lfShockVel_STDatum = null;
	private IRacingSdkDatum? _loadNumTexturesDatum = null;
	private IRacingSdkDatum? _longAccelDatum = null;
	private IRacingSdkDatum? _lrShockVel_STDatum = null;
	private IRacingSdkDatum? _paceModeDatum = null;
	private IRacingSdkDatum? _playerCarIdxDatum = null;
	private IRacingSdkDatum? _playerTrackSurfaceDatum = null;
	private IRacingSdkDatum? _playerTrackSurfaceMaterialDatum = null;
	private IRacingSdkDatum? _radioTransmitCarIdxDatum = null;
	private IRacingSdkDatum? _replayFrameNumEndDatum = null;
	private IRacingSdkDatum? _replayPlaySlowMotionDatum = null;
	private IRacingSdkDatum? _replayPlaySpeedDatum = null;
	private IRacingSdkDatum? _rfShockVel_STDatum = null;
	private IRacingSdkDatum? _rpmDatum = null;
	private IRacingSdkDatum? _rrShockVel_STDatum = null;
	private IRacingSdkDatum? _sessionFlagsDatum = null;
	private IRacingSdkDatum? _sessionNumDatum = null;
	private IRacingSdkDatum? _sessionTimeDatum = null;
	private IRacingSdkDatum? _speedDatum = null;
	private IRacingSdkDatum? _steeringFFBEnabledDatum = null;
	private IRacingSdkDatum? _steeringWheelAngleDatum = null;
	private IRacingSdkDatum? _steeringWheelAngleMaxDatum = null;
	private IRacingSdkDatum? _steeringWheelTorque_STDatum = null;
	private IRacingSdkDatum? _throttleDatum = null;
	private IRacingSdkDatum? _velocityXDatum = null;
	private IRacingSdkDatum? _velocityYDatum = null;
	private IRacingSdkDatum? _vertAccelDatum = null;
	private IRacingSdkDatum? _weatherDeclaredWetDatum = null;
	private IRacingSdkDatum? _yawNorthDatum = null;
	private IRacingSdkDatum? _yawRateDatum = null;

#if DEBUG

	private float _minMaxLogAccumulator = 0f;
	private float _minFrameRate = float.MaxValue;
	private float _maxFrameRate = float.MinValue;
	private float _minGpuUsage = float.MaxValue;
	private float _maxGpuUsage = float.MinValue;

#endif

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

	private void OnException( Exception exception )
	{
		App.Instance!.ShowFatalError( null, exception );
	}

	private void OnConnected()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Simulator] OnConnected >>>" );

		WindowHandle = User32.FindWindow( null, "iRacing.com Simulator" );

		app.MultimediaTimer.Suspend = false;

		_waitingForFirstSessionInfo = true;

		app.RacingWheel.ResetForceFeedback = true;

		app.AdminBoxx.SimulatorConnected();

#if !ADMINBOXX

		app.SpeechToText.SimulatorConnected();

#endif

		for ( var gear = 0; gear < MaxNumGears; gear++ )
		{
			RPMSpeedRatios[ gear ] = 0f;
		}

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
		CarScreenName = string.Empty;
		CarSetupName = string.Empty;
		Clutch = 0f;
		CurrentRpmSpeedRatio = 0f;
		CurrentTireIndex = -1;
		CurrentTireCompoundType = string.Empty;
		DisplayUnits = 0;
		Gear = 0;
		LongitudinalGForce = 0f;
		LateralGForce = 0f;
		IsOnTrack = false;
		IsReplayPlaying = false;
		Lap = 0;
		LapDist = 0f;
		LapDistPct = 0f;
		LastRadioTransmitCarIdx = -1;
		LatAccel = 0f;
		LoadNumTextures = false;
		LongAccel = 0f;
		NumForwardGears = 0;
		PaceMode = IRacingSdkEnum.PaceMode.NotPacing;
		PlayerCarIdx = 0;
		PlayerTrackSurface = IRacingSdkEnum.TrkLoc.NotInWorld;
		PlayerTrackSurfaceMaterial = IRacingSdkEnum.TrkSurf.SurfaceNotInWorld;
		RadioTransmitCarIdx = -1;
		ReplayFrameNumEnd = 1;
		ReplayPlaySlowMotion = false;
		ReplayPlaySpeed = 1;
		RPM = 0f;
		SessionFlags = 0;
		SessionID = 0;
		SessionNum = 0;
		SessionTime = 0;
		Speed = 0f;
		ShiftLightsFirstRPM = 0f;
		ShiftLightsShiftRPM = 0f;
		SimMode = string.Empty;
		SteeringFFBEnabled = false;
		SteeringOffsetInDegrees = 0f;
		SteeringRatio = 10f;
		SteeringWheelAngle = 0f;
		SteeringWheelAngleMax = 0f;
		Throttle = 0f;
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
		YawNorth = 0f;
		YawRate = 0f;

		Array.Clear( CFShockVel_ST );
		Array.Clear( CRShockVel_ST );
		Array.Clear( LFShockVel_ST );
		Array.Clear( LRShockVel_ST );
		Array.Clear( RFShockVel_ST );
		Array.Clear( RPMSpeedRatios );
		Array.Clear( RRShockVel_ST );
		Array.Clear( SteeringWheelTorque_ST );

		_tickCountLastFrame = null;
		_weatherDeclaredWetLastFrame = null;
		_isReplayPlayingLastFrame = null;
		_sessionFlagsLastFrame = null;
		_currentTireIndexLastFrame = null;

#if DEBUG

		_minMaxLogAccumulator = 0f;
		_minFrameRate = float.MaxValue;
		_maxFrameRate = float.MinValue;
		_minGpuUsage = float.MaxValue;
		_maxGpuUsage = float.MinValue;

#endif

		DataContext.DataContext.Instance.Settings.UpdateSettings( false );

		app.AdminBoxx.SimulatorDisconnected();

#if !ADMINBOXX

			app.SteeringEffects.SimulatorDisconnected();
			app.SpeechToText.SimulatorDisconnected();
			app.TimingMarkers.Reset();

			app.UpdateGripOMeterWindowVisibility();
			app.UpdateSpeechToTextWindowVisibility();
			app.UpdateGapMonitorWindowVisibility();

#endif

		app.MultimediaTimer.Suspend = true;

		app.MainWindow.UpdateStatus();

		_racingWheelPage.UpdateSteeringDeviceSection();

		app.Logger.WriteLine( "[Simulator] <<< OnDisconnected" );
	}

	private void OnSessionInfo()
	{
		var app = App.Instance!;

		var sessionInfo = _irsdk.Data.SessionInfo;

		CarSetupName = Path.GetFileNameWithoutExtension( sessionInfo.DriverInfo.DriverSetupName ).ToLower();

		NumForwardGears = sessionInfo.DriverInfo.DriverCarGearNumForward;

		ShiftLightsFirstRPM = sessionInfo.DriverInfo.DriverCarSLFirstRPM;
		ShiftLightsShiftRPM = sessionInfo.DriverInfo.DriverCarSLShiftRPM;

		if ( ShiftLightsShiftRPM <= ShiftLightsFirstRPM )
		{
			ShiftLightsShiftRPM = sessionInfo.DriverInfo.DriverCarSLBlinkRPM;
		}

		SimMode = sessionInfo.WeekendInfo.SimMode;

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

		if ( _waitingForFirstSessionInfo )
		{
			DataContext.DataContext.Instance.Settings.UpdateSettings( false );

			UpdateTireProperties();

#if !ADMINBOXX

			MainWindow._steeringEffectsPage.UpdateCalibrationFileNameOptions();

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
			_carIdxLapDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxLap" ];
			_carIdxLapDistPctDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxLapDistPct" ];
			_carIdxPositionDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxPosition" ];
			_carIdxOnPitRoadDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxOnPitRoad" ];
			_carIdxTireCompoundDatum = _irsdk.Data.TelemetryDataProperties[ "CarIdxTireCompound" ];
			_clutchDatum = _irsdk.Data.TelemetryDataProperties[ "Clutch" ];
			_displayUnitsDatum = _irsdk.Data.TelemetryDataProperties[ "DisplayUnits" ];
			_frameRateDatum = _irsdk.Data.TelemetryDataProperties[ "FrameRate" ];
			_gearDatum = _irsdk.Data.TelemetryDataProperties[ "Gear" ];
			_gpuUsageDatum = _irsdk.Data.TelemetryDataProperties[ "GpuUsage" ];
			_isOnTrackDatum = _irsdk.Data.TelemetryDataProperties[ "IsOnTrack" ];
			_isReplayPlayingDatum = _irsdk.Data.TelemetryDataProperties[ "IsReplayPlaying" ];
			_lapDatum = _irsdk.Data.TelemetryDataProperties[ "Lap" ];
			_lapDistDatum = _irsdk.Data.TelemetryDataProperties[ "LapDist" ];
			_lapDistPctDatum = _irsdk.Data.TelemetryDataProperties[ "LapDistPct" ];
			_latAccelDatum = _irsdk.Data.TelemetryDataProperties[ "LatAccel" ];
			_loadNumTexturesDatum = _irsdk.Data.TelemetryDataProperties[ "LoadNumTextures" ];
			_longAccelDatum = _irsdk.Data.TelemetryDataProperties[ "LongAccel" ];
			_paceModeDatum = _irsdk.Data.TelemetryDataProperties[ "PaceMode" ];
			_playerCarIdxDatum = _irsdk.Data.TelemetryDataProperties[ "PlayerCarIdx" ];
			_playerTrackSurfaceDatum = _irsdk.Data.TelemetryDataProperties[ "PlayerTrackSurface" ];
			_playerTrackSurfaceMaterialDatum = _irsdk.Data.TelemetryDataProperties[ "PlayerTrackSurfaceMaterial" ];
			_radioTransmitCarIdxDatum = _irsdk.Data.TelemetryDataProperties[ "RadioTransmitCarIdx" ];
			_replayFrameNumEndDatum = _irsdk.Data.TelemetryDataProperties[ "ReplayFrameNumEnd" ];
			_replayPlaySlowMotionDatum = _irsdk.Data.TelemetryDataProperties[ "ReplayPlaySlowMotion" ];
			_replayPlaySpeedDatum = _irsdk.Data.TelemetryDataProperties[ "ReplayPlaySpeed" ];
			_rpmDatum = _irsdk.Data.TelemetryDataProperties[ "RPM" ];
			_sessionFlagsDatum = _irsdk.Data.TelemetryDataProperties[ "SessionFlags" ];
			_sessionNumDatum = _irsdk.Data.TelemetryDataProperties[ "SessionNum" ];
			_sessionTimeDatum = _irsdk.Data.TelemetryDataProperties[ "SessionTime" ];
			_speedDatum = _irsdk.Data.TelemetryDataProperties[ "Speed" ];
			_steeringFFBEnabledDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringFFBEnabled" ];
			_steeringWheelAngleDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringWheelAngle" ];
			_steeringWheelAngleMaxDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringWheelAngleMax" ];
			_steeringWheelTorque_STDatum = _irsdk.Data.TelemetryDataProperties[ "SteeringWheelTorque_ST" ];
			_throttleDatum = _irsdk.Data.TelemetryDataProperties[ "Throttle" ];
			_velocityXDatum = _irsdk.Data.TelemetryDataProperties[ "VelocityX" ];
			_velocityYDatum = _irsdk.Data.TelemetryDataProperties[ "VelocityY" ];
			_vertAccelDatum = _irsdk.Data.TelemetryDataProperties[ "VertAccel" ];
			_weatherDeclaredWetDatum = _irsdk.Data.TelemetryDataProperties[ "WeatherDeclaredWet" ];
			_yawNorthDatum = _irsdk.Data.TelemetryDataProperties[ "YawNorth" ];
			_yawRateDatum = _irsdk.Data.TelemetryDataProperties[ "YawRate" ];

			_cfShockVel_STDatum = null;
			_crShockVel_STDatum = null;
			_lfShockVel_STDatum = null;
			_lrShockVel_STDatum = null;
			_rfShockVel_STDatum = null;
			_rrShockVel_STDatum = null;

			_irsdk.Data.TelemetryDataProperties.TryGetValue( "CFshockVel_ST", out _cfShockVel_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "CRshockVel_ST", out _crShockVel_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "LRshockVel_ST", out _lfShockVel_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "LRshockVel_ST", out _lrShockVel_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "RFshockVel_ST", out _rfShockVel_STDatum );
			_irsdk.Data.TelemetryDataProperties.TryGetValue( "RRshockVel_ST", out _rrShockVel_STDatum );

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

		// poll directinput devices right before we process the algorithm (setting app.RacingWheel.UpdateSteeringWheelTorqueBuffer = true updates the prediction on the multimedia timer thread)

		app.DirectInput.PollDevices( deltaSeconds );

		// get next 360 Hz steering wheel torque samples

		_irsdk.Data.GetFloatArray( _steeringWheelTorque_STDatum, SteeringWheelTorque_ST, 0, SteeringWheelTorque_ST.Length );

		app.RacingWheel.UpdateSteeringWheelTorqueBuffer = true;

		// save last frame values

		WasOnTrack = IsOnTrack;

		// update non-array telemetry data properties

		BrakeABSactive = _irsdk.Data.GetBool( _brakeABSactiveDatum );
		Brake = _irsdk.Data.GetFloat( _brakeDatum );
		Clutch = _irsdk.Data.GetFloat( _clutchDatum );
		DisplayUnits = _irsdk.Data.GetInt( _displayUnitsDatum );
		FrameRate = _irsdk.Data.GetFloat( _frameRateDatum );
		Gear = _irsdk.Data.GetInt( _gearDatum );
		GpuUsage = _irsdk.Data.GetFloat( _gpuUsageDatum );
		IsOnTrack = _irsdk.Data.GetBool( _isOnTrackDatum );
		IsReplayPlaying = _irsdk.Data.GetBool( _isReplayPlayingDatum );
		Lap = _irsdk.Data.GetInt( _lapDatum );
		LapDist = _irsdk.Data.GetFloat( _lapDistDatum );
		LapDistPct = _irsdk.Data.GetFloat( _lapDistPctDatum );
		LatAccel = _irsdk.Data.GetFloat( _latAccelDatum );
		LoadNumTextures = _irsdk.Data.GetBool( _loadNumTexturesDatum );
		LongAccel = _irsdk.Data.GetFloat( _longAccelDatum );
		PaceMode = (IRacingSdkEnum.PaceMode) _irsdk.Data.GetInt( _paceModeDatum );
		PlayerCarIdx = _irsdk.Data.GetInt( _playerCarIdxDatum );
		PlayerTrackSurface = (IRacingSdkEnum.TrkLoc) _irsdk.Data.GetInt( _playerTrackSurfaceDatum );
		PlayerTrackSurfaceMaterial = (IRacingSdkEnum.TrkSurf) _irsdk.Data.GetInt( _playerTrackSurfaceMaterialDatum );
		RadioTransmitCarIdx = _irsdk.Data.GetInt( _radioTransmitCarIdxDatum );
		ReplayFrameNumEnd = _irsdk.Data.GetInt( _replayFrameNumEndDatum );
		ReplayPlaySlowMotion = _irsdk.Data.GetBool( _replayPlaySlowMotionDatum );
		ReplayPlaySpeed = _irsdk.Data.GetInt( _replayPlaySpeedDatum );
		RPM = _irsdk.Data.GetFloat( _rpmDatum );
		SessionFlags = (IRacingSdkEnum.Flags) _irsdk.Data.GetBitField( _sessionFlagsDatum );
		SessionNum = _irsdk.Data.GetInt( _sessionNumDatum );
		SessionTime = _irsdk.Data.GetDouble( _sessionTimeDatum );
		Speed = _irsdk.Data.GetFloat( _speedDatum );
		SteeringFFBEnabled = _irsdk.Data.GetBool( _steeringFFBEnabledDatum );
		SteeringWheelAngle = _irsdk.Data.GetFloat( _steeringWheelAngleDatum );
		SteeringWheelAngleMax = _irsdk.Data.GetFloat( _steeringWheelAngleMaxDatum );
		Throttle = _irsdk.Data.GetFloat( _throttleDatum );
		VelocityX = _irsdk.Data.GetFloat( _velocityXDatum );
		VelocityY = _irsdk.Data.GetFloat( _velocityYDatum );
		VertAccel = _irsdk.Data.GetFloat( _vertAccelDatum );
		WeatherDeclaredWet = _irsdk.Data.GetBool( _weatherDeclaredWetDatum );
		YawNorth = _irsdk.Data.GetFloat( _yawNorthDatum );
		YawRate = _irsdk.Data.GetFloat( _yawRateDatum );

		// update min/max FrameRate and GpuUsage, log every second

#if DEBUG

		_minFrameRate = MathF.Min( _minFrameRate, FrameRate );
		_maxFrameRate = MathF.Max( _maxFrameRate, FrameRate );
		_minGpuUsage = MathF.Min( _minGpuUsage, GpuUsage );
		_maxGpuUsage = MathF.Max( _maxGpuUsage, GpuUsage );

		_minMaxLogAccumulator += deltaSeconds;

		if ( _minMaxLogAccumulator >= 1f )
		{
			app.Logger.WriteLine( $"[Simulator] FrameRate min={_minFrameRate:F1} max={_maxFrameRate:F1}, GpuUsage min={_minGpuUsage:F1} max={_maxGpuUsage:F1}" );

			_minMaxLogAccumulator -= 1f;

			_minFrameRate = float.MaxValue;
			_maxFrameRate = float.MinValue;
			_minGpuUsage = float.MaxValue;
			_maxGpuUsage = float.MinValue;
		}

#endif

		// update array telemetry data properties

		_irsdk.Data.GetIntArray( _carIdxLapDatum, CarIdxLap, 0, _carIdxLapDatum!.Count );
		_irsdk.Data.GetFloatArray( _carIdxLapDistPctDatum, CarIdxLapDistPct, 0, _carIdxLapDistPctDatum!.Count );
		_irsdk.Data.GetIntArray( _carIdxPositionDatum, CarIdxPosition, 0, _carIdxPositionDatum!.Count );
		_irsdk.Data.GetBoolArray( _carIdxOnPitRoadDatum, CarIdxOnPitRoad, 0, _carIdxOnPitRoadDatum!.Count );

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

		// update racing wheel

		app.RacingWheel.UseSteeringWheelTorqueData = IsOnTrack;

		// update adminboxx

		if ( IsReplayPlaying != _isReplayPlayingLastFrame )
		{
			app.AdminBoxx.ReplayPlayingChanged();
		}

		_isReplayPlayingLastFrame = IsReplayPlaying;

		if ( SessionFlags != _sessionFlagsLastFrame )
		{
			app.AdminBoxx.SessionFlagsChanged();
		}

		_sessionFlagsLastFrame = SessionFlags;

		// update speech-to-text

		if ( RadioTransmitCarIdx != -1 )
		{
			LastRadioTransmitCarIdx = RadioTransmitCarIdx;
		}

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

		_weatherDeclaredWetLastFrame = WeatherDeclaredWet;

		// get the current tire index and the current tire compound type

		if ( ( PlayerCarIdx >= 0 ) && ( PlayerCarIdx < _carIdxTireCompoundDatum!.Count ) )
		{
			int[] carIdxTireCompounds = new int[ _carIdxTireCompoundDatum!.Count ];

			_irsdk.Data.GetIntArray( _carIdxTireCompoundDatum, carIdxTireCompounds, 0, _carIdxTireCompoundDatum.Count );

			CurrentTireIndex = carIdxTireCompounds[ PlayerCarIdx ]; // iracing's "carIdxTireCompound" data name is wrong - it should probably have been "carIdxTireIdx"

			if ( _currentTireIndexLastFrame != null )
			{
				if ( CurrentTireIndex != _currentTireIndexLastFrame )
				{
					UpdateTireProperties();
				}
			}

			_currentTireIndexLastFrame = CurrentTireIndex;
		}

		// crash protection processing

		if ( IsOnTrack )
		{
			if ( ( settings.RacingWheelCrashProtectionDuration > 0f ) && ( settings.RacingWheelCrashProtectionForceReduction > 0f ) )
			{
				if ( settings.RacingWheelCrashProtectionLongitudalGForce < 20f )
				{
					if ( LongitudinalGForce >= settings.RacingWheelCrashProtectionLongitudalGForce )
					{
						app.RacingWheel.ActivateCrashProtection = true;
					}
				}

				if ( settings.RacingWheelCrashProtectionLateralGForce < 20f )
				{
					if ( LateralGForce >= settings.RacingWheelCrashProtectionLateralGForce )
					{
						app.RacingWheel.ActivateCrashProtection = true;
					}
				}
			}
		}

		// curb protection processing

		if ( IsOnTrack )
		{
			if ( ( settings.RacingWheelCurbProtectionShockVelocity > 0f ) && ( settings.RacingWheelCurbProtectionDuration > 0f ) && ( settings.RacingWheelCurbProtectionForceReduction > 0f ) )
			{
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

				if ( maxShockVelocity >= settings.RacingWheelCurbProtectionShockVelocity )
				{
					app.RacingWheel.ActivateCurbProtection = true;
				}
			}
		}

		// update rpm / speed ratios

		CurrentRpmSpeedRatio = 0f;

		if ( IsOnTrack && ( Gear > 0 ) && ( Clutch == 1f ) && ( RPM > 500f ) && ( VelocityX >= 10f * MathZ.MPHToMPS ) )
		{
			CurrentRpmSpeedRatio = VelocityX / RPM;

			if ( ( Brake == 0f ) && ( VelocityY < 0.1f ) && ( PlayerTrackSurface == IRacingSdkEnum.TrkLoc.OnTrack ) )
			{
				var delta = MathF.Abs( CurrentRpmSpeedRatio - RPMSpeedRatios[ Gear ] );

				if ( delta > 0.001f )
				{
					RPMSpeedRatios[ Gear ] = CurrentRpmSpeedRatio;
				}
				else if ( delta > 0f )
				{
					RPMSpeedRatios[ Gear ] = MathZ.Lerp( RPMSpeedRatios[ Gear ], CurrentRpmSpeedRatio, 0.001f );
				}
				else
				{
					RPMSpeedRatios[ Gear ] = MathZ.Lerp( RPMSpeedRatios[ Gear ], CurrentRpmSpeedRatio, 0.01f );
				}

				/*
				app.Debug.Label_1 = $"{RPMSpeedRatios[ 1 ]:F8}";
				app.Debug.Label_2 = $"{RPMSpeedRatios[ 2 ]:F8}";
				app.Debug.Label_3 = $"{RPMSpeedRatios[ 3 ]:F8}";
				app.Debug.Label_4 = $"{RPMSpeedRatios[ 4 ]:F8}";
				app.Debug.Label_5 = $"{RPMSpeedRatios[ 5 ]:F8}";
				app.Debug.Label_6 = $"{RPMSpeedRatios[ 6 ]:F8}";
				*/
			}
		}

		// update visibility of overlays

		if ( IsOnTrack != WasOnTrack )
		{
			app.UpdateGripOMeterWindowVisibility();
			app.UpdateGapMonitorWindowVisibility();
		}

		// update steering effects

		app.SteeringEffects.Update( app, deltaSeconds );

		// trigger the app worker thread

		app.TriggerWorkerThread();
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

			_racingWheelPage.CurrentForce_TextBlock.Text = $"{MathF.Abs( SteeringWheelTorque_ST[ 5 ] ):F1} {DataContext.DataContext.Instance.Localization[ "TorqueUnits" ]}";
		}
	}

	[GeneratedRegex( @"\s*deg\s*$", RegexOptions.IgnoreCase, "en-US" )]
	private static partial Regex SteeringOffsetRegex();

	[GeneratedRegex( @"\s*:1\s*$", RegexOptions.IgnoreCase, "en-US" )]
	private static partial Regex SteeringRatioRegex();

	[GeneratedRegex( @"([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase, "en-US" )]
	private static partial Regex TrackLengthRegex();
}
