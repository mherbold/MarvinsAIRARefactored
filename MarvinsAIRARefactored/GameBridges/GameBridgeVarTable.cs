
using IRSDKSharper;

namespace MarvinsAIRARefactored.GameBridges;

/// <summary>
/// Declares the complete set of iRacing telemetry variables that MAIRA reads, on an in-memory data source.
/// Every variable Simulator.cs looks up by name must exist here, otherwise MAIRA throws on the first
/// telemetry frame from a bridge.
/// </summary>
public class GameBridgeVarTable
{
	public const int MaxNumCars = 64;
	public const int SamplesPerFrame360Hz = 6;

	public IRacingSdkInMemoryDataSource DataSource { get; }

	public IRacingSdkDatum BrakeABSactive { get; }
	public IRacingSdkDatum Brake { get; }
	public IRacingSdkDatum BrakeRaw { get; }
	public IRacingSdkDatum CarIdxBestLapTime { get; }
	public IRacingSdkDatum CarIdxEstTime { get; }
	public IRacingSdkDatum CarIdxF2Time { get; }
	public IRacingSdkDatum CarIdxLap { get; }
	public IRacingSdkDatum CarIdxLapCompleted { get; }
	public IRacingSdkDatum CarIdxLapDistPct { get; }
	public IRacingSdkDatum CarIdxPosition { get; }
	public IRacingSdkDatum CarIdxOnPitRoad { get; }
	public IRacingSdkDatum CarIdxSessionFlags { get; }
	public IRacingSdkDatum CarIdxTireCompound { get; }
	public IRacingSdkDatum CarDistAhead { get; }
	public IRacingSdkDatum CarDistBehind { get; }
	public IRacingSdkDatum CarLeftRight { get; }
	public IRacingSdkDatum Clutch { get; }
	public IRacingSdkDatum DisplayUnits { get; }
	public IRacingSdkDatum EngineWarnings { get; }
	public IRacingSdkDatum FrameRate { get; }
	public IRacingSdkDatum Gear { get; }
	public IRacingSdkDatum GpuUsage { get; }
	public IRacingSdkDatum IsOnTrack { get; }
	public IRacingSdkDatum IsReplayPlaying { get; }
	public IRacingSdkDatum FuelLevel { get; }
	public IRacingSdkDatum FuelLevelPct { get; }
	public IRacingSdkDatum FuelUsePerHour { get; }
	public IRacingSdkDatum LapBestLapTime { get; }
	public IRacingSdkDatum Lap { get; }
	public IRacingSdkDatum LapDist { get; }
	public IRacingSdkDatum LapDistPct { get; }
	public IRacingSdkDatum LapLastLapTime { get; }
	public IRacingSdkDatum LatAccel { get; }
	public IRacingSdkDatum LoadNumTextures { get; }
	public IRacingSdkDatum LongAccel { get; }
	public IRacingSdkDatum OnPitRoad { get; }
	public IRacingSdkDatum PaceMode { get; }
	public IRacingSdkDatum Pitch { get; }
	public IRacingSdkDatum PitsOpen { get; }
	public IRacingSdkDatum PlayerCarClassPosition { get; }
	public IRacingSdkDatum PlayerCarIdx { get; }
	public IRacingSdkDatum PlayerCarMyIncidentCount { get; }
	public IRacingSdkDatum PlayerCarPosition { get; }
	public IRacingSdkDatum PlayerTrackSurface { get; }
	public IRacingSdkDatum PlayerTrackSurfaceMaterial { get; }
	public IRacingSdkDatum RadioTransmitCarIdx { get; }
	public IRacingSdkDatum ReplayFrameNumEnd { get; }
	public IRacingSdkDatum ReplayPlaySlowMotion { get; }
	public IRacingSdkDatum ReplayPlaySpeed { get; }
	public IRacingSdkDatum Roll { get; }
	public IRacingSdkDatum RPM { get; }
	public IRacingSdkDatum SessionFlags { get; }
	public IRacingSdkDatum SessionLapsRemainEx { get; }
	public IRacingSdkDatum SessionNum { get; }
	public IRacingSdkDatum SessionState { get; }
	public IRacingSdkDatum SessionTime { get; }
	public IRacingSdkDatum SessionTimeRemain { get; }
	public IRacingSdkDatum Speed { get; }
	public IRacingSdkDatum SteeringFFBEnabled { get; }
	public IRacingSdkDatum SteeringWheelAngle { get; }
	public IRacingSdkDatum SteeringWheelAngleMax { get; }
	public IRacingSdkDatum SteeringWheelTorque { get; }
	public IRacingSdkDatum SteeringWheelTorque_ST { get; }
	public IRacingSdkDatum Throttle { get; }
	public IRacingSdkDatum ThrottleRaw { get; }
	public IRacingSdkDatum TireLF_RumblePitch { get; }
	public IRacingSdkDatum TireRF_RumblePitch { get; }
	public IRacingSdkDatum TireLR_RumblePitch { get; }
	public IRacingSdkDatum TireRR_RumblePitch { get; }
	public IRacingSdkDatum VelocityX { get; }
	public IRacingSdkDatum VelocityY { get; }
	public IRacingSdkDatum VertAccel { get; }
	public IRacingSdkDatum WeatherDeclaredWet { get; }
	public IRacingSdkDatum Yaw { get; }
	public IRacingSdkDatum YawNorth { get; }
	public IRacingSdkDatum YawRate { get; }
	public IRacingSdkDatum LFshockVel_ST { get; }
	public IRacingSdkDatum RFshockVel_ST { get; }
	public IRacingSdkDatum LRshockVel_ST { get; }
	public IRacingSdkDatum RRshockVel_ST { get; }

	public GameBridgeVarTable( int tickRate = 60 )
	{
		DataSource = new IRacingSdkInMemoryDataSource( tickRate );

		BrakeABSactive = DataSource.AddVar( "BrakeABSactive", IRacingSdkEnum.VarType.Bool, 1, "", "true if abs is currently reducing brake force pressure" );
		Brake = DataSource.AddVar( "Brake", IRacingSdkEnum.VarType.Float, 1, "%", "0=brake released to 1=max pedal force" );
		BrakeRaw = DataSource.AddVar( "BrakeRaw", IRacingSdkEnum.VarType.Float, 1, "%", "Raw brake input 0=brake released to 1=max pedal force" );
		CarIdxBestLapTime = DataSource.AddVar( "CarIdxBestLapTime", IRacingSdkEnum.VarType.Float, MaxNumCars, "s", "Cars best lap time" );
		CarIdxEstTime = DataSource.AddVar( "CarIdxEstTime", IRacingSdkEnum.VarType.Float, MaxNumCars, "s", "Estimated time to reach current location on track" );
		CarIdxF2Time = DataSource.AddVar( "CarIdxF2Time", IRacingSdkEnum.VarType.Float, MaxNumCars, "s", "Race time behind leader or fastest lap time otherwise" );
		CarIdxLap = DataSource.AddVar( "CarIdxLap", IRacingSdkEnum.VarType.Int, MaxNumCars, "", "Laps started by car index" );
		CarIdxLapCompleted = DataSource.AddVar( "CarIdxLapCompleted", IRacingSdkEnum.VarType.Int, MaxNumCars, "", "Laps completed by car index" );
		CarIdxLapDistPct = DataSource.AddVar( "CarIdxLapDistPct", IRacingSdkEnum.VarType.Float, MaxNumCars, "%", "Percentage distance around lap by car index" );
		CarIdxPosition = DataSource.AddVar( "CarIdxPosition", IRacingSdkEnum.VarType.Int, MaxNumCars, "", "Cars position in race by car index" );
		CarIdxOnPitRoad = DataSource.AddVar( "CarIdxOnPitRoad", IRacingSdkEnum.VarType.Bool, MaxNumCars, "", "On pit road between the cones by car index" );
		CarIdxSessionFlags = DataSource.AddVar( "CarIdxSessionFlags", IRacingSdkEnum.VarType.BitField, MaxNumCars, "irsdk_Flags", "Session flags by car index" );
		CarIdxTireCompound = DataSource.AddVar( "CarIdxTireCompound", IRacingSdkEnum.VarType.Int, MaxNumCars, "", "Cars current tire compound" );
		CarDistAhead = DataSource.AddVar( "CarDistAhead", IRacingSdkEnum.VarType.Float, 1, "m", "Distance to first car in front of player in meters" );
		CarDistBehind = DataSource.AddVar( "CarDistBehind", IRacingSdkEnum.VarType.Float, 1, "m", "Distance to first car behind player in meters" );
		CarLeftRight = DataSource.AddVar( "CarLeftRight", IRacingSdkEnum.VarType.Int, 1, "irsdk_CarLeftRight", "Notify if car is to the left or right of driver" );
		Clutch = DataSource.AddVar( "Clutch", IRacingSdkEnum.VarType.Float, 1, "%", "0=disengaged to 1=fully engaged" );
		DisplayUnits = DataSource.AddVar( "DisplayUnits", IRacingSdkEnum.VarType.Int, 1, "", "Default units for the user interface 0 = english 1 = metric" );
		EngineWarnings = DataSource.AddVar( "EngineWarnings", IRacingSdkEnum.VarType.BitField, 1, "irsdk_EngineWarnings", "Bitfield for warning lights" );
		FrameRate = DataSource.AddVar( "FrameRate", IRacingSdkEnum.VarType.Float, 1, "fps", "Average frames per second" );
		Gear = DataSource.AddVar( "Gear", IRacingSdkEnum.VarType.Int, 1, "", "-1=reverse 0=neutral 1..n=current gear" );
		GpuUsage = DataSource.AddVar( "GpuUsage", IRacingSdkEnum.VarType.Float, 1, "%", "Percent of available tim gpu took with a 1 sec avg" );
		IsOnTrack = DataSource.AddVar( "IsOnTrack", IRacingSdkEnum.VarType.Bool, 1, "", "1=Car on track physics running with player in car" );
		IsReplayPlaying = DataSource.AddVar( "IsReplayPlaying", IRacingSdkEnum.VarType.Bool, 1, "", "0=replay not playing 1=replay playing" );
		FuelLevel = DataSource.AddVar( "FuelLevel", IRacingSdkEnum.VarType.Float, 1, "l", "Liters of fuel remaining" );
		FuelLevelPct = DataSource.AddVar( "FuelLevelPct", IRacingSdkEnum.VarType.Float, 1, "%", "Percent fuel remaining" );
		FuelUsePerHour = DataSource.AddVar( "FuelUsePerHour", IRacingSdkEnum.VarType.Float, 1, "kg/h", "Engine fuel used instantaneous" );
		LapBestLapTime = DataSource.AddVar( "LapBestLapTime", IRacingSdkEnum.VarType.Float, 1, "s", "Players best lap time" );
		Lap = DataSource.AddVar( "Lap", IRacingSdkEnum.VarType.Int, 1, "", "Laps started count" );
		LapDist = DataSource.AddVar( "LapDist", IRacingSdkEnum.VarType.Float, 1, "m", "Meters traveled from S/F this lap" );
		LapDistPct = DataSource.AddVar( "LapDistPct", IRacingSdkEnum.VarType.Float, 1, "%", "Percentage distance around lap" );
		LapLastLapTime = DataSource.AddVar( "LapLastLapTime", IRacingSdkEnum.VarType.Float, 1, "s", "Players last lap time" );
		LatAccel = DataSource.AddVar( "LatAccel", IRacingSdkEnum.VarType.Float, 1, "m/s^2", "Lateral acceleration (including gravity)" );
		LoadNumTextures = DataSource.AddVar( "LoadNumTextures", IRacingSdkEnum.VarType.Bool, 1, "", "True if the car_num texture will be loaded" );
		LongAccel = DataSource.AddVar( "LongAccel", IRacingSdkEnum.VarType.Float, 1, "m/s^2", "Longitudinal acceleration (including gravity)" );
		OnPitRoad = DataSource.AddVar( "OnPitRoad", IRacingSdkEnum.VarType.Bool, 1, "", "Is the player car on pit road between the cones" );
		PaceMode = DataSource.AddVar( "PaceMode", IRacingSdkEnum.VarType.Int, 1, "irsdk_PaceMode", "Are we pacing or not" );
		Pitch = DataSource.AddVar( "Pitch", IRacingSdkEnum.VarType.Float, 1, "rad", "Pitch orientation" );
		PitsOpen = DataSource.AddVar( "PitsOpen", IRacingSdkEnum.VarType.Bool, 1, "", "True if pit stop is allowed for the current player" );
		PlayerCarClassPosition = DataSource.AddVar( "PlayerCarClassPosition", IRacingSdkEnum.VarType.Int, 1, "", "Players class position in race" );
		PlayerCarIdx = DataSource.AddVar( "PlayerCarIdx", IRacingSdkEnum.VarType.Int, 1, "", "Players carIdx" );
		PlayerCarMyIncidentCount = DataSource.AddVar( "PlayerCarMyIncidentCount", IRacingSdkEnum.VarType.Int, 1, "", "Players own incident count" );
		PlayerCarPosition = DataSource.AddVar( "PlayerCarPosition", IRacingSdkEnum.VarType.Int, 1, "", "Players position in race" );
		PlayerTrackSurface = DataSource.AddVar( "PlayerTrackSurface", IRacingSdkEnum.VarType.Int, 1, "irsdk_TrkLoc", "Players car track surface type" );
		PlayerTrackSurfaceMaterial = DataSource.AddVar( "PlayerTrackSurfaceMaterial", IRacingSdkEnum.VarType.Int, 1, "irsdk_TrkSurf", "Players car track surface material type" );
		RadioTransmitCarIdx = DataSource.AddVar( "RadioTransmitCarIdx", IRacingSdkEnum.VarType.Int, 1, "", "The car index of the current person speaking on the radio" );
		ReplayFrameNumEnd = DataSource.AddVar( "ReplayFrameNumEnd", IRacingSdkEnum.VarType.Int, 1, "", "Integer replay frame number from end of tape" );
		ReplayPlaySlowMotion = DataSource.AddVar( "ReplayPlaySlowMotion", IRacingSdkEnum.VarType.Bool, 1, "", "0=not slow motion 1=replay is in slow motion" );
		ReplayPlaySpeed = DataSource.AddVar( "ReplayPlaySpeed", IRacingSdkEnum.VarType.Int, 1, "", "Replay playback speed" );
		Roll = DataSource.AddVar( "Roll", IRacingSdkEnum.VarType.Float, 1, "rad", "Roll orientation" );
		RPM = DataSource.AddVar( "RPM", IRacingSdkEnum.VarType.Float, 1, "revs/min", "Engine rpm" );
		SessionFlags = DataSource.AddVar( "SessionFlags", IRacingSdkEnum.VarType.BitField, 1, "irsdk_Flags", "Session flags" );
		SessionLapsRemainEx = DataSource.AddVar( "SessionLapsRemainEx", IRacingSdkEnum.VarType.Int, 1, "", "New improved laps left till session ends" );
		SessionNum = DataSource.AddVar( "SessionNum", IRacingSdkEnum.VarType.Int, 1, "", "Session number" );
		SessionState = DataSource.AddVar( "SessionState", IRacingSdkEnum.VarType.Int, 1, "irsdk_SessionState", "Session state" );
		SessionTime = DataSource.AddVar( "SessionTime", IRacingSdkEnum.VarType.Double, 1, "s", "Seconds since session start" );
		SessionTimeRemain = DataSource.AddVar( "SessionTimeRemain", IRacingSdkEnum.VarType.Double, 1, "s", "Seconds left till session ends" );
		Speed = DataSource.AddVar( "Speed", IRacingSdkEnum.VarType.Float, 1, "m/s", "GPS vehicle speed" );
		SteeringFFBEnabled = DataSource.AddVar( "SteeringFFBEnabled", IRacingSdkEnum.VarType.Bool, 1, "", "Force feedback is enabled" );
		SteeringWheelAngle = DataSource.AddVar( "SteeringWheelAngle", IRacingSdkEnum.VarType.Float, 1, "rad", "Steering wheel angle" );
		SteeringWheelAngleMax = DataSource.AddVar( "SteeringWheelAngleMax", IRacingSdkEnum.VarType.Float, 1, "rad", "Steering wheel max angle" );
		SteeringWheelTorque = DataSource.AddVar( "SteeringWheelTorque", IRacingSdkEnum.VarType.Float, 1, "N*m", "Output torque on steering shaft" );
		SteeringWheelTorque_ST = DataSource.AddVar( "SteeringWheelTorque_ST", IRacingSdkEnum.VarType.Float, SamplesPerFrame360Hz, "N*m", "Output torque on steering shaft at 360 Hz", true );
		Throttle = DataSource.AddVar( "Throttle", IRacingSdkEnum.VarType.Float, 1, "%", "0=off throttle to 1=full throttle" );
		ThrottleRaw = DataSource.AddVar( "ThrottleRaw", IRacingSdkEnum.VarType.Float, 1, "%", "Raw throttle input 0=off throttle to 1=full throttle" );
		TireLF_RumblePitch = DataSource.AddVar( "TireLF_RumblePitch", IRacingSdkEnum.VarType.Float, 1, "Hz", "Players LF Tire Sound rumblestrip pitch" );
		TireRF_RumblePitch = DataSource.AddVar( "TireRF_RumblePitch", IRacingSdkEnum.VarType.Float, 1, "Hz", "Players RF Tire Sound rumblestrip pitch" );
		TireLR_RumblePitch = DataSource.AddVar( "TireLR_RumblePitch", IRacingSdkEnum.VarType.Float, 1, "Hz", "Players LR Tire Sound rumblestrip pitch" );
		TireRR_RumblePitch = DataSource.AddVar( "TireRR_RumblePitch", IRacingSdkEnum.VarType.Float, 1, "Hz", "Players RR Tire Sound rumblestrip pitch" );
		VelocityX = DataSource.AddVar( "VelocityX", IRacingSdkEnum.VarType.Float, 1, "m/s", "X velocity" );
		VelocityY = DataSource.AddVar( "VelocityY", IRacingSdkEnum.VarType.Float, 1, "m/s", "Y velocity" );
		VertAccel = DataSource.AddVar( "VertAccel", IRacingSdkEnum.VarType.Float, 1, "m/s^2", "Vertical acceleration (including gravity)" );
		WeatherDeclaredWet = DataSource.AddVar( "WeatherDeclaredWet", IRacingSdkEnum.VarType.Bool, 1, "", "The steward says rain tires can be used" );
		Yaw = DataSource.AddVar( "Yaw", IRacingSdkEnum.VarType.Float, 1, "rad", "Yaw orientation" );
		YawNorth = DataSource.AddVar( "YawNorth", IRacingSdkEnum.VarType.Float, 1, "rad", "Yaw orientation relative to north" );
		YawRate = DataSource.AddVar( "YawRate", IRacingSdkEnum.VarType.Float, 1, "rad/s", "Yaw rate" );
		LFshockVel_ST = DataSource.AddVar( "LFshockVel_ST", IRacingSdkEnum.VarType.Float, SamplesPerFrame360Hz, "m/s", "LF shock velocity at 360 Hz", true );
		RFshockVel_ST = DataSource.AddVar( "RFshockVel_ST", IRacingSdkEnum.VarType.Float, SamplesPerFrame360Hz, "m/s", "RF shock velocity at 360 Hz", true );
		LRshockVel_ST = DataSource.AddVar( "LRshockVel_ST", IRacingSdkEnum.VarType.Float, SamplesPerFrame360Hz, "m/s", "LR shock velocity at 360 Hz", true );
		RRshockVel_ST = DataSource.AddVar( "RRshockVel_ST", IRacingSdkEnum.VarType.Float, SamplesPerFrame360Hz, "m/s", "RR shock velocity at 360 Hz", true );

		DataSource.Initialize();
	}
}
