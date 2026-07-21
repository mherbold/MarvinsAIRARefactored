
using System.Diagnostics;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;

namespace LmuCaptureTool;

internal static class Program
{
	private const string FileMagic = "LMUCAP01";

	// each buffer is captured whenever the version counter in its first four bytes changes
	private static readonly string[] BufferNames =
	[
		"$rFactor2SMMP_Telemetry$",
		"$rFactor2SMMP_Scoring$",
		"$rFactor2SMMP_ForceFeedback$",
		"$rFactor2SMMP_Extended$",
		"$rFactor2SMMP_Rules$",
		"LMU_Data",
		"LMU_Data_Probe"
	];

	// the game's native LMU_Data map has no version counter, so it gets special handling - a full snapshot
	// every couple of seconds for layout validation, plus a tiny probe record polled at high rate to measure
	// the map's true update cadence (offsets from the LMUFFB project's LMU shared memory header)
	private const int LmuDataBufferIndex = 5;
	private const int LmuDataProbeBufferIndex = 6;

	private const int LmuDataTelemetryOffset = 128464;
	private const int LmuDataPlayerVehicleIndexOffset = LmuDataTelemetryOffset + 1;
	private const int LmuDataVehicleArrayOffset = LmuDataTelemetryOffset + 4;
	private const int LmuDataVehicleSize = 1888;
	private const int LmuDataVehicleElapsedTimeOffset = 12;
	private const int LmuDataVehicleSteeringOffset = 404;
	private const int LmuDataVehicleTorqueOffset = 452;
	private const int LmuDataGenericFfbOffset = 68;

	private const double LmuDataFullSnapshotIntervalSeconds = 2.0;

	private static int Main( string[] args )
	{
		Console.WriteLine( "MAIRA LMU capture tool" );
		Console.WriteLine( "Captures the rFactor 2 / Le Mans Ultimate shared memory plugin buffers to a file." );
		Console.WriteLine();

		var outputFolder = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ), "MarvinsAIRA Refactored", "Captures" );

		if ( args.Length > 0 )
		{
			outputFolder = args[ 0 ];
		}

		Directory.CreateDirectory( outputFolder );

		var outputFilePath = Path.Combine( outputFolder, $"lmu-{DateTime.Now:yyyyMMdd-HHmmss}.lmucap" );

		var accessors = new MemoryMappedViewAccessor?[ BufferNames.Length ];
		var memoryMappedFiles = new MemoryMappedFile?[ BufferNames.Length ];
		var lastVersions = new uint[ BufferNames.Length ];
		var hasCaptured = new bool[ BufferNames.Length ];
		var frameCounts = new long[ BufferNames.Length ];
		var scratchBuffers = new byte[ BufferNames.Length ][];

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
		var lastLmuFullSnapshot = double.MinValue;
		var lastProbeElapsedTime = double.NaN;
		var lastProbeSteering = double.NaN;
		var lastProbeTorque = double.NaN;
		var lastProbeGenericFfb = float.NaN;

		while ( !stopRequested )
		{
			var elapsed = stopwatch.Elapsed.TotalSeconds;

			// try to open any buffers that are not open yet, once per second
			if ( elapsed - lastOpenAttempt >= 1.0 )
			{
				lastOpenAttempt = elapsed;

				for ( var i = 0; i < BufferNames.Length; i++ )
				{
					if ( i == LmuDataProbeBufferIndex )
					{
						continue;
					}

					if ( accessors[ i ] == null )
					{
						try
						{
							memoryMappedFiles[ i ] = MemoryMappedFile.OpenExisting( BufferNames[ i ], MemoryMappedFileRights.Read );

							accessors[ i ] = memoryMappedFiles[ i ]!.CreateViewAccessor( 0, 0, MemoryMappedFileAccess.Read );

							scratchBuffers[ i ] = new byte[ accessors[ i ]!.Capacity ];

							Console.WriteLine( $"Found {BufferNames[ i ]} ({accessors[ i ]!.Capacity} bytes)" );
						}
						catch ( FileNotFoundException )
						{
						}
					}
				}
			}

			// capture any buffer whose version counter changed
			for ( var i = 0; i < BufferNames.Length; i++ )
			{
				if ( i >= LmuDataBufferIndex )
				{
					continue;
				}

				var accessor = accessors[ i ];

				if ( accessor == null )
				{
					continue;
				}

				var version = accessor.ReadUInt32( 0 );

				if ( !hasCaptured[ i ] || ( version != lastVersions[ i ] ) )
				{
					lastVersions[ i ] = version;
					hasCaptured[ i ] = true;

					var scratchBuffer = scratchBuffers[ i ]!;

					accessor.ReadArray( 0, scratchBuffer, 0, scratchBuffer.Length );

					writer.Write( i );
					writer.Write( stopwatch.Elapsed.TotalSeconds );
					writer.Write( scratchBuffer.Length );
					writer.Write( scratchBuffer );

					frameCounts[ i ]++;
				}
			}

			// the native LMU_Data map - periodic full snapshot plus a high-rate change-detecting probe
			var lmuAccessor = accessors[ LmuDataBufferIndex ];

			if ( lmuAccessor != null )
			{
				if ( elapsed - lastLmuFullSnapshot >= LmuDataFullSnapshotIntervalSeconds )
				{
					lastLmuFullSnapshot = elapsed;

					var scratchBuffer = scratchBuffers[ LmuDataBufferIndex ]!;

					lmuAccessor.ReadArray( 0, scratchBuffer, 0, scratchBuffer.Length );

					writer.Write( LmuDataBufferIndex );
					writer.Write( elapsed );
					writer.Write( scratchBuffer.Length );
					writer.Write( scratchBuffer );

					frameCounts[ LmuDataBufferIndex ]++;
				}

				if ( lmuAccessor.Capacity > LmuDataVehicleArrayOffset )
				{
					var playerVehicleIndex = lmuAccessor.ReadByte( LmuDataPlayerVehicleIndexOffset );

					var vehicleOffset = LmuDataVehicleArrayOffset + playerVehicleIndex * LmuDataVehicleSize;

					if ( lmuAccessor.Capacity >= vehicleOffset + LmuDataVehicleTorqueOffset + 8 )
					{
						var elapsedTime = lmuAccessor.ReadDouble( vehicleOffset + LmuDataVehicleElapsedTimeOffset );
						var steering = lmuAccessor.ReadDouble( vehicleOffset + LmuDataVehicleSteeringOffset );
						var torque = lmuAccessor.ReadDouble( vehicleOffset + LmuDataVehicleTorqueOffset );
						var genericFfb = lmuAccessor.ReadSingle( LmuDataGenericFfbOffset );

						if ( ( elapsedTime != lastProbeElapsedTime ) || ( steering != lastProbeSteering ) || ( torque != lastProbeTorque ) || ( genericFfb != lastProbeGenericFfb ) )
						{
							lastProbeElapsedTime = elapsedTime;
							lastProbeSteering = steering;
							lastProbeTorque = torque;
							lastProbeGenericFfb = genericFfb;

							writer.Write( LmuDataProbeBufferIndex );
							writer.Write( elapsed );
							writer.Write( 29 );
							writer.Write( playerVehicleIndex );
							writer.Write( genericFfb );
							writer.Write( elapsedTime );
							writer.Write( steering );
							writer.Write( torque );

							frameCounts[ LmuDataProbeBufferIndex ]++;
						}
					}
				}
			}

			if ( elapsed - lastStatus >= 5.0 )
			{
				lastStatus = elapsed;

				var status = string.Join( "   ", BufferNames.Select( ( name, i ) => $"{ShortName( name )}: {frameCounts[ i ]}" ) );

				Console.WriteLine( $"[{TimeSpan.FromSeconds( elapsed ):hh\\:mm\\:ss}] {status}   file: {fileStream.Length / ( 1024.0 * 1024.0 ):F1} MB" );
			}

			Thread.Sleep( 1 );
		}

		writer.Write( -1 );

		writer.Flush();

		Console.WriteLine();
		Console.WriteLine( $"Capture saved to {outputFilePath}" );

		for ( var i = 0; i < BufferNames.Length; i++ )
		{
			Console.WriteLine( $"  {BufferNames[ i ]}: {frameCounts[ i ]} frames" );

			accessors[ i ]?.Dispose();
			memoryMappedFiles[ i ]?.Dispose();
		}

		if ( frameCounts.All( count => count == 0 ) )
		{
			Console.WriteLine();
			Console.WriteLine( "No data was captured. Make sure the rFactor2SharedMemoryMapPlugin is installed and enabled in the game." );

			return 1;
		}

		return 0;
	}

	private static string ShortName( string bufferName )
	{
		return bufferName.Replace( "$rFactor2SMMP_", "" ).Replace( "$", "" );
	}
}
