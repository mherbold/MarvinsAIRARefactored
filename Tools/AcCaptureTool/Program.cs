
using System.Diagnostics;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace AcCaptureTool;

internal static class Program
{
	private const string FileMagic = "ACCAP01";

	// Assetto Corsa updates its physics shared memory at ~330 Hz. Without raising the system timer resolution,
	// Thread.Sleep(1) actually waits for the next ~15.6 ms Windows tick, so the poll loop runs at only ~64 Hz
	// and undersamples the page. timeBeginPeriod(1) drops the tick to 1 ms so the loop polls fast enough to
	// catch every update.
	[DllImport( "winmm.dll", EntryPoint = "timeBeginPeriod" )]
	private static extern uint TimeBeginPeriod( uint milliseconds );

	[DllImport( "winmm.dll", EntryPoint = "timeEndPeriod" )]
	private static extern uint TimeEndPeriod( uint milliseconds );

	// buffer indices match MarvinsAIRARefactored.GameBridges.Ac.AcBufferType (Physics=0, Graphics=1, Static=2)
	private static readonly string[] BufferNames =
	[
		"acpmf_physics",
		"acpmf_graphics",
		"acpmf_static"
	];

	// each slot tries the classic Kunos names (AC, ACC, AC Rally) first, then the Assetto Corsa EVO names -
	// EVO publishes the same three pages but renamed to acevo_pmf_* (and with different struct layouts, which
	// is fine here because the capture is raw bytes)
	private static readonly string[][] MapNameCandidates =
	[
		[ "Local\\acpmf_physics", "Local\\acevo_pmf_physics" ],
		[ "Local\\acpmf_graphics", "Local\\acevo_pmf_graphics" ],
		[ "Local\\acpmf_static", "Local\\acevo_pmf_static" ]
	];

	private static int Main( string[] args )
	{
		Console.WriteLine( "MAIRA Assetto Corsa capture tool" );
		Console.WriteLine( "Captures the Assetto Corsa shared memory pages to a file while you drive." );
		Console.WriteLine();

		var outputFolder = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ), "MarvinsAIRA Refactored", "Captures" );

		if ( args.Length > 0 )
		{
			outputFolder = args[ 0 ];
		}

		Directory.CreateDirectory( outputFolder );

		var outputFilePath = Path.Combine( outputFolder, $"ac-{DateTime.Now:yyyyMMdd-HHmmss}.accap" );

		var accessors = new MemoryMappedViewAccessor?[ BufferNames.Length ];
		var memoryMappedFiles = new MemoryMappedFile?[ BufferNames.Length ];
		var hasCaptured = new bool[ BufferNames.Length ];
		var frameCounts = new long[ BufferNames.Length ];
		var scratchBuffers = new byte[ BufferNames.Length ][];
		var lastBuffers = new byte[ BufferNames.Length ][];

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

		TimeBeginPeriod( 1 );

		while ( !stopRequested )
		{
			var elapsed = stopwatch.Elapsed.TotalSeconds;

			// try to open any pages that are not open yet, once per second
			if ( elapsed - lastOpenAttempt >= 1.0 )
			{
				lastOpenAttempt = elapsed;

				for ( var i = 0; i < BufferNames.Length; i++ )
				{
					if ( accessors[ i ] == null )
					{
						foreach ( var mapName in MapNameCandidates[ i ] )
						{
							try
							{
								memoryMappedFiles[ i ] = MemoryMappedFile.OpenExisting( mapName, MemoryMappedFileRights.Read );

								accessors[ i ] = memoryMappedFiles[ i ]!.CreateViewAccessor( 0, 0, MemoryMappedFileAccess.Read );

								scratchBuffers[ i ] = new byte[ accessors[ i ]!.Capacity ];
								lastBuffers[ i ] = new byte[ accessors[ i ]!.Capacity ];

								Console.WriteLine( $"Found {mapName} ({accessors[ i ]!.Capacity} bytes)" );

								break;
							}
							catch ( FileNotFoundException )
							{
							}
						}
					}
				}
			}

			// capture any page whose content changed. AC1's pages start with a packetId counter but EVO's
			// layouts are unknown, so a whole-page compare is used - it is layout-agnostic and still cheap
			// (a few KB per page at the 1 kHz poll)
			for ( var i = 0; i < BufferNames.Length; i++ )
			{
				var accessor = accessors[ i ];

				if ( accessor == null )
				{
					continue;
				}

				var scratchBuffer = scratchBuffers[ i ]!;

				accessor.ReadArray( 0, scratchBuffer, 0, scratchBuffer.Length );

				if ( !hasCaptured[ i ] || !scratchBuffer.AsSpan().SequenceEqual( lastBuffers[ i ] ) )
				{
					hasCaptured[ i ] = true;

					scratchBuffer.CopyTo( lastBuffers[ i ]!, 0 );

					writer.Write( i );
					writer.Write( stopwatch.Elapsed.TotalSeconds );
					writer.Write( scratchBuffer.Length );
					writer.Write( scratchBuffer );

					frameCounts[ i ]++;
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

			accessors[ i ]?.Dispose();
			memoryMappedFiles[ i ]?.Dispose();
		}

		if ( frameCounts.All( count => count == 0 ) )
		{
			Console.WriteLine();
			Console.WriteLine( "No data was captured. Make sure Assetto Corsa is running and you are on track." );

			return 1;
		}

		return 0;
	}
}
