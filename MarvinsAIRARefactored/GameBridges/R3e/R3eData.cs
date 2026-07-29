/*
RaceRoom Racing Experience shared memory mapping structures.

Transcribed verbatim (subset) for MarvinsAIRA Refactored from the official Sector3 Studios API header:
https://github.com/sector3studios/r3e-api - sample-c/src/r3e.h (R3E_VERSION_MAJOR 3, R3E_VERSION_MINOR 5)

The game publishes a single "$R3E" memory mapped file containing one r3e_shared struct. The header uses
#pragma pack(push, 1), so every struct here is Pack = 1 with no hidden padding. Field names are kept exactly
as in the header (snake_case) so the layout can be cross-checked line by line. Do NOT reorder, rename, or
resize any fields - byte layout fidelity with the game is required.

Arrays are represented as [InlineArray] value types (see InlineArrays.cs) instead of [MarshalAs] fields -
byte layout is identical, but the structs stay blittable so the bridge can read them from the shared memory
bytes with MemoryMarshal.Read with ZERO heap allocations on the playout timer worker thread.

The all_drivers_data_1[128] array at the end of r3e_shared is intentionally NOT part of the R3eShared struct
below - the map is self-describing (all_drivers_offset points at num_cars, driver_data_size is the array
stride), so the bridge reads the driver entries individually by offset instead of marshaling 128 of them
on every parse.
*/

using System.Runtime.InteropServices;

namespace MarvinsAIRARefactored.GameBridges.R3e;

public static class R3eConstants
{
	public const string SharedMemoryMapName = "$R3E";

	public const int VersionMajor = 3;

	public const int NumDriversMax = 128;

	public const int TireIndexMax = 4;
	public const int TireTempIndexMax = 3;
	public const int PitMenuMax = 12;

	// r3e_session
	public enum R3eSession
	{
		Unavailable = -1,
		Practice = 0,
		Qualify = 1,
		Race = 2,
		Warmup = 3
	}

	// r3e_session_phase
	public enum R3eSessionPhase
	{
		Unavailable = -1,
		Garage = 1,
		Gridwalk = 2,
		Formation = 3,
		Countdown = 4,
		Green = 5,
		Checkered = 6
	}

	// r3e_control
	public enum R3eControl
	{
		Unavailable = -1,
		Player = 0,
		AI = 1,
		Remote = 2,
		Replay = 3
	}

	// r3e_mtrl_type - material under the player car's tires
	public enum R3eMaterialType
	{
		Unavailable = -1,
		None = 0,
		Tarmac = 1,
		Grass = 2,
		Dirt = 3,
		Gravel = 4,
		Rumble = 5,
		Concrete = 6
	}

	// aid_settings values - 5 means the aid is currently active
	public const int AidCurrentlyActive = 5;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eVec3F32
{
	public float x, y, z;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eVec3F64
{
	public double x, y, z;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eOriF32
{
	public float pitch;
	public float yaw;
	public float roll;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eSectorStarts
{
	public float sector1;
	public float sector2;
	public float sector3;
}

[System.Runtime.CompilerServices.InlineArray( R3eConstants.TireIndexMax )]
public struct R3eTireTempArray4
{
	private R3eTireTemp _element0;
}

[System.Runtime.CompilerServices.InlineArray( R3eConstants.TireIndexMax )]
public struct R3eBrakeTempArray4
{
	private R3eBrakeTemp _element0;
}

// r3e_playerdata - high precision data for the player's vehicle only. The local frame matches rFactor 2:
// from car center, +X = left, +Y = up, +Z = back (documented at local_acceleration in the header).
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3ePlayerData
{
	public int user_id;

	// virtual physics time, 1 tick = 1/400th of a second
	public int game_simulation_ticks;

	// virtual physics time in seconds
	public double game_simulation_time;

	public R3eVec3F64 position;
	public R3eVec3F64 velocity;
	public R3eVec3F64 local_velocity;
	public R3eVec3F64 acceleration;
	public R3eVec3F64 local_acceleration;
	public R3eVec3F64 orientation;
	public R3eVec3F64 rotation;
	public R3eVec3F64 angular_acceleration;
	public R3eVec3F64 angular_velocity;
	public R3eVec3F64 local_angular_velocity;
	public R3eVec3F64 local_g_force;

	// total steering force coming through steering bars
	public double steering_force;
	public double steering_force_percentage;

	public double engine_torque;
	public double current_downforce;

	public double voltage;
	public double ers_level;
	public double power_mgu_h;
	public double power_mgu_k;
	public double torque_mgu_k;

	public DoubleArray4 suspension_deflection;
	public DoubleArray4 suspension_velocity;
	public DoubleArray4 camber;
	public DoubleArray4 ride_height;

	public double front_wing_height;
	public double front_roll_angle;
	public double rear_roll_angle;
	public double third_spring_suspension_deflection_front;
	public double third_spring_suspension_velocity_front;
	public double third_spring_suspension_deflection_rear;
	public double third_spring_suspension_velocity_rear;

	public double unused1;
	public double unused2;
	public double unused3;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eFlags
{
	public int yellow;
	public int yellowCausedIt;
	public int yellowOvertake;
	public int yellowPositionsGained;
	public IntArray3 sector_yellow;
	public float closest_yellow_distance_into_track;
	public int blue;
	public int black;
	public int green;
	public int checkered;
	public int white;
	public int black_and_white;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eCarDamage
{
	public float engine;
	public float transmission;
	public float aerodynamics;
	public float suspension;
	public float unused1;
	public float unused2;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eCutTrackPenalties
{
	public float drive_through;
	public float stop_and_go;
	public float pit_stop;
	public float time_deduction;
	public float slow_down;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eDrs
{
	public int equipped;
	public int available;
	public int numActivationsLeft;
	public int engaged;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3ePushToPass
{
	public int available;
	public int engaged;
	public int amount_left;
	public float engaged_time_left;
	public float wait_time_left;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eTireTemp
{
	public FloatArray3 current_temp;
	public float optimal_temp;
	public float cold_temp;
	public float hot_temp;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eBrakeTemp
{
	public float current_temp;
	public float optimal_temp;
	public float cold_temp;
	public float hot_temp;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eAidSettings
{
	// each aid: -1 = N/A, 0 = off, 1 = on, 5 = currently active
	public int abs;
	public int tc;
	public int esp;
	public int countersteer;
	public int cornering;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eDriverInfo
{
	public ByteArray64 name; // UTF-8

	public int car_number;
	public int class_id;
	public int model_id;
	public int team_id;
	public int livery_id;
	public int manufacturer_id;
	public int user_id;
	public int slot_id;
	public int class_performance_index;
	public int engine_type;
	public float car_width;
	public float car_length;
	public float rating;
	public float reputation;

	public float unused1;
	public float unused2;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eDriverData
{
	public R3eDriverInfo driver_info;
	public int finish_status;
	public int place;
	public int place_class;
	public float lap_distance;
	public float lap_distance_fraction;
	public R3eVec3F32 position;
	public int track_sector;
	public int completed_laps;
	public int current_lap_valid;
	public float lap_time_current_self;
	public FloatArray3 sector_time_current_self;
	public FloatArray3 sector_time_previous_self;
	public FloatArray3 sector_time_best_self;
	public float time_delta_front;
	public float time_delta_behind;
	public int pitstop_status;
	public int in_pitlane;
	public int num_pitstops;
	public R3eCutTrackPenalties penalties;
	public float car_speed;
	public int tire_type_front;
	public int tire_type_rear;
	public int tire_subtype_front;
	public int tire_subtype_rear;
	public float base_penalty_weight;
	public float aid_penalty_weight;
	public int drs_state;
	public int ptp_state;
	public float virtual_energy;
	public int penaltyType;
	public int penaltyReason;
	public int engineState;
	public R3eVec3F32 orientation;

	public float unused1;
	public float unused2;
	public float unused3;
}

// r3e_shared without the trailing all_drivers_data_1[128] array - ends at num_cars, so the driver array
// starts at Marshal.SizeOf<R3eShared>() (which must equal all_drivers_offset + 4; the bridge verifies the
// self-describing offsets at runtime and logs if the game's layout ever drifts from this transcription)
[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct R3eShared
{
	// version
	public int version_major;
	public int version_minor;
	public int all_drivers_offset; // offset to num_cars
	public int driver_data_size;   // size of one driver data entry

	// game state
	public int game_mode;
	public int game_paused;
	public int game_in_menus;
	public int game_in_replay;
	public int game_using_vr;
	public int game_player_in_garage;

	// high detail player vehicle data
	public R3ePlayerData player;

	// event and session
	public ByteArray64 track_name; // UTF-8
	public ByteArray64 layout_name; // UTF-8

	public int track_id;
	public int layout_id;
	public float layout_length;
	public R3eSectorStarts sector_start_factors;

	public IntArray3 race_session_laps;
	public IntArray3 race_session_minutes;

	public int event_index;
	public int session_type;
	public int session_iteration;
	public int session_length_format;
	public float session_pit_speed_limit;

	public int session_phase;
	public int start_lights;

	public int tire_wear_active;
	public int fuel_use_active;

	public int number_of_laps;

	public float session_time_duration;
	public float session_time_remaining;

	public int max_incident_points;

	public float event_unused1;
	public float event_unused2;

	// pit
	public int pit_window_status;
	public int pit_window_start;
	public int pit_window_end;
	public int in_pitlane;
	public int pit_menu_selection;
	public IntArray12 pit_menu_state;
	public int pit_state;
	public float pit_total_duration;
	public float pit_elapsed_time;
	public int pit_action;
	public int num_pitstops;
	public float pit_min_duration_total;
	public float pit_min_duration_left;

	// scoring and timings
	public R3eFlags flags;

	public int position;
	public int position_class;

	public int finish_status;

	public int cut_track_warnings;
	public R3eCutTrackPenalties penalties;
	public int num_penalties;

	public int completed_laps;
	public int current_lap_valid;
	public int track_sector;
	public float lap_distance;
	public float lap_distance_fraction;

	public float lap_time_best_leader;
	public float lap_time_best_leader_class;
	public FloatArray3 session_best_lap_sector_times;
	public float lap_time_best_self;
	public FloatArray3 sector_time_best_self;
	public float lap_time_previous_self;
	public FloatArray3 sector_time_previous_self;
	public float lap_time_current_self;
	public FloatArray3 sector_time_current_self;
	public float lap_time_delta_leader;
	public float lap_time_delta_leader_class;
	public float time_delta_front;
	public float time_delta_behind;
	public float time_delta_best_self;
	public FloatArray3 best_individual_sector_time_self;
	public FloatArray3 best_individual_sector_time_leader;
	public FloatArray3 best_individual_sector_time_leader_class;
	public int incident_points;

	public int lap_valid_state;
	public int prev_lap_valid;

	public float discharge_rate;
	public float brake_regen;

	public float unused1;

	// vehicle information
	public R3eDriverInfo vehicle_info;
	public ByteArray64 player_name; // UTF-8

	// vehicle state
	public int control_type;

	public float car_speed;

	public float engine_rps;
	public float max_engine_rps;
	public float upshift_rps;

	public int gear;
	public int num_gears;

	public R3eVec3F32 car_cg_location;
	public R3eOriF32 car_orientation;

	// acceleration of the car body in local-space - from car center, +X = left, +Y = up, +Z = back
	public R3eVec3F32 local_acceleration;

	public float total_mass;
	public float fuel_left;
	public float fuel_capacity;
	public float fuel_per_lap;
	public float virtual_energy_left;
	public float virtual_energy_capacity;
	public float virtual_energy_per_lap;
	public float engine_temp;
	public float engine_oil_temp;
	public float fuel_pressure;
	public float engine_oil_pressure;
	public float turbo_pressure;

	public float throttle;
	public float throttle_raw;
	public float brake;
	public float brake_raw;
	public float clutch;
	public float clutch_raw;
	public float steer_input_raw;
	public int steer_lock_degrees;
	public int steer_wheel_range_degrees;

	public R3eAidSettings aid_settings;

	public R3eDrs drs;

	public int pit_limiter;

	public R3ePushToPass push_to_pass;

	public float brake_bias;

	public int drs_numActivationsTotal;
	public int ptp_numActivationsTotal;

	public float battery_soc;

	public float water_left;

	public int abs_setting;

	public int headlights;

	public int steer_wheel_max_rotation;

	// tires
	public int tire_type;
	public FloatArray4 tire_rps;
	public FloatArray4 tire_speed;
	public FloatArray4 tire_grip;
	public FloatArray4 tire_wear;
	public IntArray4 tire_flatspot;
	public FloatArray4 tire_pressure;
	public FloatArray4 tire_dirt;
	public R3eTireTempArray4 tire_temp;
	public int tire_type_front;
	public int tire_type_rear;
	public int tire_subtype_front;
	public int tire_subtype_rear;
	public R3eBrakeTempArray4 brake_temp;
	public FloatArray4 brake_pressure;

	// electronics
	public int traction_control_setting;
	public int engine_map_setting;
	public int engine_brake_setting;

	public float traction_control_percent;

	public IntArray4 tire_on_mtrl;

	public FloatArray4 tire_load;

	// damage
	public R3eCarDamage car_damage;

	// driver info
	public int num_cars;

	// r3e_driver_data all_drivers_data_1[128] follows here - read by offset, see the file header comment
}
