
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using MarvinsAIRARefactored.GameBridges;
using MarvinsAIRARefactored.GameBridges.R3e;

namespace R3eCaptureAnalyzer;

internal static class Program
{
	private const string FileMagic = "R3ECAP01";

	private sealed record SharedFrame( double Seconds, R3eShared Shared, byte[] Bytes );
	private sealed record ProbeFrame( double Seconds, int Ticks, double SimulationTime, double SteeringForce );

	private static int Main( string[] args )
	{
		if ( args.Length < 1 )
		{
			Console.WriteLine( "usage: R3eCaptureAnalyzer <capture.r3ecap>" );

			return 1;
		}

		var sharedFrames = new List<SharedFrame>();
		var probeFrames = new List<ProbeFrame>();

		string[] bufferNames;

		using ( var fileStream = File.OpenRead( args[ 0 ] ) )
		using ( var gzipStream = new GZipStream( fileStream, CompressionMode.Decompress ) )
		using ( var reader = new BinaryReader( gzipStream ) )
		{
			var magic = Encoding.ASCII.GetString( reader.ReadBytes( 8 ) );

			if ( magic != FileMagic )
			{
				Console.WriteLine( $"Not an R3E capture (magic '{magic}')" );

				return 1;
			}

			var bufferCount = reader.ReadInt32();

			bufferNames = new string[ bufferCount ];

			for ( var i = 0; i < bufferCount; i++ )
			{
				bufferNames[ i ] = reader.ReadString();
			}

			while ( true )
			{
				int index;

				try
				{
					index = reader.ReadInt32();
				}
				catch ( EndOfStreamException )
				{
					break;
				}

				if ( index == -1 )
				{
					break;
				}

				var seconds = reader.ReadDouble();
				var length = reader.ReadInt32();

				if ( index == 1 )
				{
					var ticks = reader.ReadInt32();
					var simulationTime = reader.ReadDouble();
					var probeSteeringForce = reader.ReadDouble();

					probeFrames.Add( new ProbeFrame( seconds, ticks, simulationTime, probeSteeringForce ) );
				}
				else
				{
					var bytes = reader.ReadBytes( length );

					var shared = MemoryMarshal.Read<R3eShared>( bytes );

					sharedFrames.Add( new SharedFrame( seconds, shared, bytes ) );
				}
			}
		}

		Console.WriteLine( $"Buffers: {string.Join( ", ", bufferNames )}" );
		Console.WriteLine( $"Shared snapshots: {sharedFrames.Count}   Probe records: {probeFrames.Count}" );

		if ( sharedFrames.Count == 0 )
		{
			Console.WriteLine( "No shared snapshots captured." );

			return 1;
		}

		var first = sharedFrames[ 0 ].Shared;

		// session info is -1/empty while in the menus, so report it from a mid-drive snapshot instead
		var drivingFrames = sharedFrames.Where( f => f.Shared.car_speed > 5f ).ToList();
		var session = drivingFrames.Count > 0 ? drivingFrames[ drivingFrames.Count / 2 ].Shared : first;

		// === layout self-check ===

		var structSize = Unsafe.SizeOf<R3eShared>();
		var driverSize = Unsafe.SizeOf<R3eDriverData>();

		Console.WriteLine();
		Console.WriteLine( "=== Layout self-check ===" );
		Console.WriteLine( $"version: {first.version_major}.{first.version_minor}   map bytes: {sharedFrames[ 0 ].Bytes.Length}" );
		Console.WriteLine( $"all_drivers_offset: {first.all_drivers_offset}   expected (SizeOf<R3eShared> - 4): {structSize - 4}   {( first.all_drivers_offset == structSize - 4 ? "OK" : "MISMATCH!" )}" );
		Console.WriteLine( $"driver_data_size: {first.driver_data_size}   expected (SizeOf<R3eDriverData>): {driverSize}   {( first.driver_data_size == driverSize ? "OK" : "MISMATCH!" )}" );

		// === session / identity ===

		Console.WriteLine();
		Console.WriteLine( "=== Session ===" );
		Console.WriteLine( $"track: '{ReadUtf8( session.track_name )}'   layout: '{ReadUtf8( session.layout_name )}'   length: {session.layout_length:F0} m" );
		Console.WriteLine( $"session_type: {session.session_type}   phase: {session.session_phase}   num_cars: {session.num_cars}   control_type: {session.control_type}" );
		Console.WriteLine( $"player_name: '{ReadUtf8( session.player_name )}'" );

		var vehicleInfo = session.vehicle_info;

		Console.WriteLine( $"vehicle_info.name: '{ReadUtf8( vehicleInfo.name )}'   car_number: {vehicleInfo.car_number}   model_id: {vehicleInfo.model_id}   class_id: {vehicleInfo.class_id}   manufacturer_id: {vehicleInfo.manufacturer_id}" );
		Console.WriteLine( $"steer_lock_degrees: {session.steer_lock_degrees}   steer_wheel_range_degrees: {session.steer_wheel_range_degrees}   steer_wheel_max_rotation: {session.steer_wheel_max_rotation}" );
		Console.WriteLine( $"aid abs: {session.aid_settings.abs}   tc: {session.aid_settings.tc}" );

		// first few driver entries by self-described offset
		Console.WriteLine();
		Console.WriteLine( "=== Driver table (by self-described offset) ===" );

		var sessionBytes = drivingFrames.Count > 0 ? drivingFrames[ drivingFrames.Count / 2 ].Bytes : sharedFrames[ 0 ].Bytes;
		var driverArrayOffset = session.all_drivers_offset + 4;

		for ( var i = 0; i < Math.Min( session.num_cars, 5 ); i++ )
		{
			var offset = driverArrayOffset + i * session.driver_data_size;

			if ( offset + driverSize > sessionBytes.Length )
			{
				break;
			}

			var driver = MemoryMarshal.Read<R3eDriverData>( sessionBytes.AsSpan( offset ) );
			var driverInfo = driver.driver_info;

			Console.WriteLine( $"  [{i}] name: '{ReadUtf8( driverInfo.name )}'   slot: {driverInfo.slot_id}   model: {driverInfo.model_id}   place: {driver.place}   speed: {driver.car_speed:F1}" );
		}

		// === probe cadence ===

		Console.WriteLine();
		Console.WriteLine( "=== Probe cadence (any of ticks / sim_time / steering_force changed, 1 kHz poll) ===" );

		if ( probeFrames.Count > 2 )
		{
			var probeSpan = probeFrames[ ^1 ].Seconds - probeFrames[ 0 ].Seconds;

			Console.WriteLine( $"{probeFrames.Count} probe records over {probeSpan:F1}s = {probeFrames.Count / probeSpan:F1} Hz" );

			var tickDeltas = new List<int>();
			var timeDeltas = new List<double>();
			var forceChanges = 0;

			for ( var i = 1; i < probeFrames.Count; i++ )
			{
				if ( probeFrames[ i ].Ticks != probeFrames[ i - 1 ].Ticks )
				{
					tickDeltas.Add( probeFrames[ i ].Ticks - probeFrames[ i - 1 ].Ticks );
				}

				if ( probeFrames[ i ].SimulationTime != probeFrames[ i - 1 ].SimulationTime )
				{
					timeDeltas.Add( probeFrames[ i ].SimulationTime - probeFrames[ i - 1 ].SimulationTime );
				}

				if ( probeFrames[ i ].SteeringForce != probeFrames[ i - 1 ].SteeringForce )
				{
					forceChanges++;
				}
			}

			if ( tickDeltas.Count > 0 )
			{
				tickDeltas.Sort();

				Console.WriteLine( $"game_simulation_ticks deltas: median {tickDeltas[ tickDeltas.Count / 2 ]} ticks (1 tick = 1/400 s -> {400.0 / Math.Max( 1, tickDeltas[ tickDeltas.Count / 2 ] ):F1} Hz map update)   min {tickDeltas[ 0 ]}   max {tickDeltas[ ^1 ]}" );
			}

			if ( timeDeltas.Count > 0 )
			{
				timeDeltas.Sort();

				Console.WriteLine( $"game_simulation_time deltas: median {timeDeltas[ timeDeltas.Count / 2 ] * 1000.0:F2} ms   ({timeDeltas.Count} changes over {probeSpan:F1}s = {timeDeltas.Count / probeSpan:F1} Hz)" );
			}

			Console.WriteLine( $"steering_force changes: {forceChanges} over {probeSpan:F1}s = {forceChanges / probeSpan:F1} Hz" );
		}

		// === player telemetry ranges (driving snapshots only) ===

		var driving = sharedFrames.Where( f => f.Shared.car_speed > 5f ).ToList();

		Console.WriteLine();
		Console.WriteLine( "=== Player telemetry (all snapshots / driving = car_speed > 5) ===" );
		Console.WriteLine( $"snapshots: {sharedFrames.Count} total, {driving.Count} driving" );

		PrintRange( "steer_input_raw", sharedFrames.Select( f => (double) f.Shared.steer_input_raw ) );
		PrintRange( "steering_force", sharedFrames.Select( f => f.Shared.player.steering_force ) );
		PrintRange( "steering_force_pct", sharedFrames.Select( f => f.Shared.player.steering_force_percentage ) );
		PrintRange( "car_speed m/s", sharedFrames.Select( f => (double) f.Shared.car_speed ) );
		PrintRange( "local_velocity.z", sharedFrames.Select( f => f.Shared.player.local_velocity.z ) );
		PrintRange( "local_velocity.x", sharedFrames.Select( f => f.Shared.player.local_velocity.x ) );
		PrintRange( "local_accel.y", sharedFrames.Select( f => f.Shared.player.local_acceleration.y ) );
		PrintRange( "local_g_force.y", sharedFrames.Select( f => f.Shared.player.local_g_force.y ) );
		PrintRange( "local_angvel.y", sharedFrames.Select( f => f.Shared.player.local_angular_velocity.y ) );
		PrintRange( "engine_rps", sharedFrames.Select( f => (double) f.Shared.engine_rps ) );

		var suspensionVelocityMin = double.MaxValue;
		var suspensionVelocityMax = double.MinValue;

		foreach ( var frame in sharedFrames )
		{
			var player = frame.Shared.player;

			for ( var i = 0; i < 4; i++ )
			{
				suspensionVelocityMin = Math.Min( suspensionVelocityMin, player.suspension_velocity[ i ] );
				suspensionVelocityMax = Math.Max( suspensionVelocityMax, player.suspension_velocity[ i ] );
			}
		}

		Console.WriteLine( $"suspension_velocity (all 4): min {suspensionVelocityMin,10:F3}   max {suspensionVelocityMax,10:F3}" );

		// === steering force units: force vs percentage ratio ===

		Console.WriteLine();
		Console.WriteLine( "=== steering_force units (ratio force / percentage where |pct| > 0.05) ===" );

		var ratios = sharedFrames
			.Where( f => Math.Abs( f.Shared.player.steering_force_percentage ) > 0.05 )
			.Select( f => f.Shared.player.steering_force / f.Shared.player.steering_force_percentage )
			.ToList();

		if ( ratios.Count > 0 )
		{
			ratios.Sort();

			Console.WriteLine( $"samples: {ratios.Count}   median ratio: {ratios[ ratios.Count / 2 ]:F3}   min: {ratios[ 0 ]:F3}   max: {ratios[ ^1 ]:F3}" );
			Console.WriteLine( "-> if the ratio is constant, that constant is the max force in steering_force's unit (pct = force / max)" );
		}
		else
		{
			Console.WriteLine( "no samples with |pct| > 0.05" );
		}

		// === at rest: gravity convention ===

		var atRest = sharedFrames.Where( f => f.Shared.car_speed < 0.2f && f.Shared.control_type == 0 ).ToList();

		Console.WriteLine();
		Console.WriteLine( "=== At rest (car_speed < 0.2, player control) ===" );

		if ( atRest.Count > 0 )
		{
			var frame = atRest[ atRest.Count / 2 ];
			var player = frame.Shared.player;

			Console.WriteLine( $"t={frame.Seconds:F1}s  local_accel=({player.local_acceleration.x:F3}, {player.local_acceleration.y:F3}, {player.local_acceleration.z:F3})  local_g_force=({player.local_g_force.x:F3}, {player.local_g_force.y:F3}, {player.local_g_force.z:F3})" );
			Console.WriteLine( "-> local_accel.y ~ 0 at rest = excludes gravity; ~ +/-9.8 = includes it. g_force in g units?" );
		}
		else
		{
			Console.WriteLine( "no at-rest player-control samples" );
		}

		// === forward-motion sign check ===

		var straight = driving.Where( f => Math.Abs( f.Shared.steer_input_raw ) < 0.1f ).ToList();

		Console.WriteLine();
		Console.WriteLine( "=== Forward motion (driving, |steer| < 0.1) ===" );

		if ( straight.Count > 0 )
		{
			Console.WriteLine( $"samples: {straight.Count}   avg local_velocity.z: {straight.Average( f => f.Shared.player.local_velocity.z ):F2}   avg local_velocity.x: {straight.Average( f => f.Shared.player.local_velocity.x ):F2}" );
			Console.WriteLine( "-> negative z while driving forward confirms forward = -z (frame doc: +Z = back)" );
		}

		// === first sustained steering event ===

		Console.WriteLine();
		Console.WriteLine( "=== First sustained steering event (driving, |steer| > 0.25 for >= 0.5s) ===" );

		for ( var i = 0; i < driving.Count; i++ )
		{
			if ( Math.Abs( driving[ i ].Shared.steer_input_raw ) < 0.25f )
			{
				continue;
			}

			var start = driving[ i ].Seconds;
			var window = driving.Skip( i ).TakeWhile( f => Math.Abs( f.Shared.steer_input_raw ) > 0.25f && f.Seconds - start < 2.0 ).ToList();

			if ( window.Count < 5 || window[ ^1 ].Seconds - start < 0.5 )
			{
				continue;
			}

			Console.WriteLine( $"t={start:F1}s  n={window.Count}" );
			Console.WriteLine( $"  avg steer_input_raw: {window.Average( f => (double) f.Shared.steer_input_raw ):F3}" );
			Console.WriteLine( $"  avg local_angvel.y:  {window.Average( f => f.Shared.player.local_angular_velocity.y ):F3}" );
			Console.WriteLine( $"  avg local_accel.x:   {window.Average( f => f.Shared.player.local_acceleration.x ):F3}" );
			Console.WriteLine( $"  avg local_g_force.x: {window.Average( f => f.Shared.player.local_g_force.x ):F3}" );
			Console.WriteLine( $"  avg steering_force:  {window.Average( f => f.Shared.player.steering_force ):F3}" );

			break;
		}

		// === correlations while driving ===

		Console.WriteLine();
		Console.WriteLine( "=== Correlations (driving) ===" );

		var steer = driving.Select( f => (double) f.Shared.steer_input_raw ).ToArray();
		var yawRate = driving.Select( f => f.Shared.player.local_angular_velocity.y ).ToArray();
		var latAccel = driving.Select( f => f.Shared.player.local_acceleration.x ).ToArray();
		var steeringForce = driving.Select( f => f.Shared.player.steering_force ).ToArray();

		Console.WriteLine( $"corr( steer, local_angvel.y ) = {Correlation( steer, yawRate ):F3}" );
		Console.WriteLine( $"corr( steer, local_accel.x )  = {Correlation( steer, latAccel ):F3}" );
		Console.WriteLine( $"corr( steer, steering_force ) = {Correlation( steer, steeringForce ):F3}" );
		Console.WriteLine( $"corr( angvel.y, accel.x )     = {Correlation( yawRate, latAccel ):F3}" );

		// === yaw handedness check ===
		// A compass-style heading (iRacing YawNorth: 0 = north, clockwise/right-positive) must INCREASE
		// while the car turns right. local_angular_velocity.y is right-positive in R3E (validated via the
		// steering correlation above), so corr( d(yaw)/dt, local_angvel.y ) should be ~+1 if car_orientation.yaw
		// is compass-style, ~-1 if it is CCW-positive (which would mirror the track map north/south).

		Console.WriteLine();
		Console.WriteLine( "=== Yaw handedness (driving) ===" );

		var headingRates = new List<double>();
		var angularVelocities = new List<double>();
		var cumulativeHeadingChange = 0.0;

		for ( var i = 1; i < driving.Count; i++ )
		{
			var deltaSeconds = driving[ i ].Seconds - driving[ i - 1 ].Seconds;

			var deltaHeading = (double) ( driving[ i ].Shared.car_orientation.yaw - driving[ i - 1 ].Shared.car_orientation.yaw );

			while ( deltaHeading > Math.PI ) { deltaHeading -= 2.0 * Math.PI; }
			while ( deltaHeading < -Math.PI ) { deltaHeading += 2.0 * Math.PI; }

			cumulativeHeadingChange += deltaHeading;

			if ( ( deltaSeconds <= 0.0 ) || ( deltaSeconds > 0.1 ) )
			{
				continue;
			}

			headingRates.Add( deltaHeading / deltaSeconds );
			angularVelocities.Add( driving[ i ].Shared.player.local_angular_velocity.y );
		}

		Console.WriteLine( $"corr( d(car_orientation.yaw)/dt, local_angvel.y ) = {Correlation( [ .. headingRates ], [ .. angularVelocities ] ):F3}" );
		Console.WriteLine( $"cumulative yaw change while driving = {cumulativeHeadingChange * 180.0 / Math.PI:F0} deg" );

		// === materials seen ===

		var materials = new HashSet<int>();

		foreach ( var frame in sharedFrames )
		{
			var tireOnMtrl = frame.Shared.tire_on_mtrl;

			for ( var i = 0; i < 4; i++ )
			{
				materials.Add( tireOnMtrl[ i ] );
			}
		}

		Console.WriteLine();
		Console.WriteLine( $"tire_on_mtrl values seen: {string.Join( ", ", materials.OrderBy( m => m ) )}" );

		return 0;
	}

	private static string ReadUtf8( ByteArray64 bytes )
	{
		Span<byte> span = bytes;

		var terminator = span.IndexOf( (byte) 0 );

		if ( terminator == 0 )
		{
			return string.Empty;
		}

		return Encoding.UTF8.GetString( terminator >= 0 ? span[ ..terminator ] : span );
	}

	private static void PrintRange( string label, IEnumerable<double> values )
	{
		var list = values.ToList();

		if ( list.Count == 0 )
		{
			return;
		}

		Console.WriteLine( $"{label,-22} min {list.Min(),10:F3}   max {list.Max(),10:F3}   avg {list.Average(),10:F3}" );
	}

	private static double Correlation( double[] a, double[] b )
	{
		var n = Math.Min( a.Length, b.Length );

		if ( n < 3 )
		{
			return 0.0;
		}

		var meanA = a.Take( n ).Average();
		var meanB = b.Take( n ).Average();

		var covariance = 0.0;
		var varianceA = 0.0;
		var varianceB = 0.0;

		for ( var i = 0; i < n; i++ )
		{
			var da = a[ i ] - meanA;
			var db = b[ i ] - meanB;

			covariance += da * db;
			varianceA += da * da;
			varianceB += db * db;
		}

		var denominator = Math.Sqrt( varianceA * varianceB );

		return denominator > 0.0 ? covariance / denominator : 0.0;
	}
}
