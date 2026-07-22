
using System.Diagnostics;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace R3eCaptureTool;

internal static class Program
{
	private const string FileMagic = "R3ECAP01";

	// Without raising the system timer resolution, Thread.Sleep(1) waits for the next ~15.6 ms Windows tick,
	// so the poll loop runs at only ~64 Hz and cannot measure (or capture) anything updating faster than
	// that. timeBeginPeriod(1) drops the tick to 1 ms so the loop polls at ~1 kHz.
	[DllImport( "winmm.dll", EntryPoint = "timeBeginPeriod" )]
	private static extern uint TimeBeginPeriod( uint milliseconds );

	[DllImport( "winmm.dll", EntryPoint = "timeEndPeriod" )]
	private static extern uint TimeEndPeriod( uint milliseconds );

	private const string MapName = "$R3E";

	// buffer indices match MarvinsAIRARefactored.GameBridges.R3e.R3eBufferType (Shared=0, Probe=1)
	private static readonly string[] BufferNames =
	[
		"$R3E",
		"$R3E_Probe"
	];

	private const int SharedBufferIndex = 0;
	private const int ProbeBufferIndex = 1;

	// offsets into the r3e_shared struct (#pragma pack 1): the 10 header ints put r3e_playerdata at 40, with
	// game_simulation_ticks at +4, game_simulation_time at +8, and steering_force at +280 (after 11 vec3
	// doubles). game_simulation_time is the map's update detector - a full snapshot is written whenever it
	// advances, and the tiny probe record is written on every change of the probed values so the map's true
	// cadence survives even if gzip writes ever stall the full snapshots.
	private const int PlayerDataOffset = 40;
	private const int SimulationTicksOffset = PlayerDataOffset + 4;
	private const int SimulationTimeOffset = PlayerDataOffset + 8;
	private const int SteeringForceOffset = PlayerDataOffset + 280;

	// full snapshots are also written on a slow heartbeat while the simulation time is frozen (menus,
	// garage), so the static session info is always present in the capture
	private const double HeartbeatIntervalSeconds = 2.0;

	private static int Main( string[] args )
	{
		Console.WriteLine( "MAIRA RaceRoom capture tool" );
		Console.WriteLine( "Captures the RaceRoom Racing Experience shared memory map to a file while you drive." );
		Console.WriteLine();

		var outputFolder = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ), "MarvinsAIRA Refactored", "Captures" );

		if ( args.Length > 0 )
		{
			outputFolder = args[ 0 ];
		}

		Directory.CreateDirectory( outputFolder );

		var outputFilePath = Path.Combine( outputFolder, $"r3e-{DateTime.Now:yyyyMMdd-HHmmss}.r3ecap" );

		MemoryMappedFile? memoryMappedFile = null;
		MemoryMappedViewAccessor? accessor = null;
		byte[]? scratchBuffer = null;

		var frameCounts = new long[ BufferNames.Length ];

		var stopRequested = false;

		Console.CancelKeyPress += ( sender, eventArgs ) =>
		{
			eventArgs.Cancel = true;

			stopRequested = true;
		};

		using var fileStream = new FileStream( outputFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 20 );
		using var gzipStream = new GZipStream( fileStream, CompressionLevel.Fastest );
		using var writer = new BinaryWriter( gzipStream );

		writer.Write( System.Text.Encoding.ASCII.GetBytes( FileMagic ) );
		writer.Write( BufferNames.Length );

		foreach ( var bufferName in BufferNames )
		{
			writer.Write( bufferName );
		}

		Console.WriteLine( $"Writing to {outputFilePath}" );
		Console.WriteLine();
		Console.WriteLine( "Waiting for the game - start driving whenever you like. Press Ctrl+C to stop and save." );
		Console.WriteLine();

		var stopwatch = Stopwatch.StartNew();
		var lastOpenAttempt = -10.0;
		var lastStatus = 0.0;
		var lastHeartbeat = double.MinValue;
		var lastSimulationTime = double.NaN;
		var lastProbeTicks = int.MinValue;
		var lastProbeSimulationTime = double.NaN;
		var lastProbeSteeringForce = double.NaN;

		TimeBeginPeriod( 1 );

		while ( !stopRequested )
		{
			var elapsed = stopwatch.Elapsed.TotalSeconds;

			// try to open the map if it is not open yet, once per second
			if ( ( accessor == null ) && ( elapsed - lastOpenAttempt >= 1.0 ) )
			{
				lastOpenAttempt = elapsed;

				try
				{
					memoryMappedFile = MemoryMappedFile.OpenExisting( MapName, MemoryMappedFileRights.Read );

					accessor = memoryMappedFile.CreateViewAccessor( 0, 0, MemoryMappedFileAccess.Read );

					scratchBuffer = new byte[ accessor.Capacity ];

					Console.WriteLine( $"Found {MapName} ({accessor.Capacity} bytes)" );
				}
				catch ( FileNotFoundException )
				{
				}
			}

			if ( ( accessor != null ) && ( accessor.Capacity > SteeringForceOffset + 8 ) )
			{
				// tiny probe record on every change of the probed values - measures the map's true cadence
				var probeTicks = accessor.ReadInt32( SimulationTicksOffset );
				var probeSimulationTime = accessor.ReadDouble( SimulationTimeOffset );
				var probeSteeringForce = accessor.ReadDouble( SteeringForceOffset );

				if ( ( probeTicks != lastProbeTicks ) || ( probeSimulationTime != lastProbeSimulationTime ) || ( probeSteeringForce != lastProbeSteeringForce ) )
				{
					lastProbeTicks = probeTicks;
					lastProbeSimulationTime = probeSimulationTime;
					lastProbeSteeringForce = probeSteeringForce;

					writer.Write( ProbeBufferIndex );
					writer.Write( elapsed );
					writer.Write( 20 );
					writer.Write( probeTicks );
					writer.Write( probeSimulationTime );
					writer.Write( probeSteeringForce );

					frameCounts[ ProbeBufferIndex ]++;
				}

				// full snapshot whenever the simulation time advances, plus a slow heartbeat while it is
				// frozen (menus, garage) so the static session info is always captured
				var simulationTimeChanged = probeSimulationTime != lastSimulationTime;

				if ( simulationTimeChanged || ( elapsed - lastHeartbeat >= HeartbeatIntervalSeconds ) )
				{
					lastSimulationTime = probeSimulationTime;
					lastHeartbeat = elapsed;

					accessor.ReadArray( 0, scratchBuffer!, 0, scratchBuffer!.Length );

					writer.Write( SharedBufferIndex );
					writer.Write( elapsed );
					writer.Write( scratchBuffer.Length );
					writer.Write( scratchBuffer );

					frameCounts[ SharedBufferIndex ]++;
				}
			}

			if ( elapsed - lastStatus >= 5.0 )
			{
				lastStatus = elapsed;

				var status = string.Join( "   ", BufferNames.Select( ( name, i ) => $"{name}: {frameCounts[ i ]}" ) );

				Console.WriteLine( $"[{TimeSpan.FromSeconds( elapsed ):hh\\:mm\\:ss}] {status}   file: {fileStream.Length / ( 1024.0 * 1024.0 ):F1} MB" );
			}

			Thread.Sleep( 1 );
		}

		TimeEndPeriod( 1 );

		writer.Write( -1 );

		writer.Flush();

		Console.WriteLine();
		Console.WriteLine( $"Capture saved to {outputFilePath}" );

		for ( var i = 0; i < BufferNames.Length; i++ )
		{
			Console.WriteLine( $"  {BufferNames[ i ]}: {frameCounts[ i ]} frames" );
		}

		accessor?.Dispose();
		memoryMappedFile?.Dispose();

		if ( frameCounts.All( count => count == 0 ) )
		{
			Console.WriteLine();
			Console.WriteLine( "No data was captured. Make sure RaceRoom Racing Experience is running and you are on track." );

			return 1;
		}

		return 0;
	}
}
