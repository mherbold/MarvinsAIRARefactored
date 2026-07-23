
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace LmuEventProbe;

// Measures the TRUE update rate of LMU's official shared memory interface by riding its event
// (LMU_Data_Event) instead of polling. The header (Support/SharedMemoryInterface/SharedMemoryInterface.hpp)
// signals this auto-reset event on every update and sets a flag in generic.events[SME_*]; SME_FFB fires on
// each ForceFeedback callback. This tells us whether the "in-game FFB" (FFBTorque @ offset 68) is really 400
// Hz or, like our polling showed, 100 Hz. IMPORTANT: close LMUFFB (or any other reader) first - the event is
// auto-reset, so a second waiter would steal signals and split the count.

internal static class Program
{
	private const string MapName = "LMU_Data";
	private const string EventName = "LMU_Data_Event";

	private const int EventsArrayCount = 16;        // SharedMemoryEvent events[SME_MAX], SME_MAX = 16
	private const int FfbTorqueOffset = 68;         // float FFBTorque (after events[16]=64 + long gameVersion=4)

	// SharedMemoryEvent enum indices we care about
	private static readonly (int index, string name)[] TrackedEvents =
	[
		( 10, "SME_UPDATE_SCORING" ),
		( 11, "SME_UPDATE_TELEMETRY" ),
		( 15, "SME_FFB" )
	];

	private static int Main()
	{
		Console.WriteLine( "MAIRA LMU event probe" );
		Console.WriteLine( "Rides LMU_Data_Event to measure the real update rate of the official shared memory (esp. SME_FFB)." );
		Console.WriteLine( "Close LMUFFB or any other shared-memory reader first, then drive. Press Ctrl+C to stop." );
		Console.WriteLine();

		EventWaitHandle waitHandle;

		try
		{
			waitHandle = EventWaitHandle.OpenExisting( EventName );
		}
		catch ( WaitHandleCannotBeOpenedException )
		{
			Console.WriteLine( $"Could not open the event '{EventName}'. Is Le Mans Ultimate running (v1.2+)?" );

			return 1;
		}

		using var memoryMappedFile = TryOpenMap();

		if ( memoryMappedFile == null )
		{
			Console.WriteLine( $"Could not open the shared memory '{MapName}'. Is Le Mans Ultimate running (v1.2+)?" );

			return 1;
		}

		using var accessor = memoryMappedFile.CreateViewAccessor( 0, FfbTorqueOffset + 4, MemoryMappedFileAccess.Read );

		var stopRequested = false;

		Console.CancelKeyPress += ( sender, eventArgs ) =>
		{
			eventArgs.Cancel = true;
			stopRequested = true;
		};

		var stopwatch = Stopwatch.StartNew();

		var totalSignals = 0L;
		var eventCounts = new long[ EventsArrayCount ];
		var ffbChanges = 0L;
		var lastFfb = float.NaN;

		var lastReportSeconds = 0.0;
		var lastReportSignals = 0L;
		var lastReportFfbChanges = 0L;
		var lastReportEventCounts = new long[ EventsArrayCount ];

		while ( !stopRequested )
		{
			if ( !waitHandle.WaitOne( 1000 ) )
			{
				continue;
			}

			totalSignals++;

			for ( var i = 0; i < EventsArrayCount; i++ )
			{
				if ( accessor.ReadUInt32( i * 4 ) != 0 )
				{
					eventCounts[ i ]++;
				}
			}

			var ffb = accessor.ReadSingle( FfbTorqueOffset );

			if ( ffb != lastFfb )
			{
				ffbChanges++;
				lastFfb = ffb;
			}

			var elapsed = stopwatch.Elapsed.TotalSeconds;

			if ( elapsed - lastReportSeconds >= 1.0 )
			{
				var window = elapsed - lastReportSeconds;

				var signalsHz = ( totalSignals - lastReportSignals ) / window;
				var ffbHz = ( ffbChanges - lastReportFfbChanges ) / window;
				var ffbEventHz = ( eventCounts[ 15 ] - lastReportEventCounts[ 15 ] ) / window;
				var telemetryHz = ( eventCounts[ 11 ] - lastReportEventCounts[ 11 ] ) / window;

				Console.WriteLine( $"[{elapsed,6:F0}s] events {signalsHz,5:F0} Hz | SME_FFB {ffbEventHz,5:F0} Hz | SME_UPDATE_TELEMETRY {telemetryHz,5:F0} Hz | FFBTorque changes {ffbHz,5:F0} Hz | FFBTorque={lastFfb,7:F3}" );

				lastReportSeconds = elapsed;
				lastReportSignals = totalSignals;
				lastReportFfbChanges = ffbChanges;
				Array.Copy( eventCounts, lastReportEventCounts, EventsArrayCount );
			}
		}

		var duration = stopwatch.Elapsed.TotalSeconds;

		Console.WriteLine();
		Console.WriteLine( $"=== Summary over {duration:F1}s ===" );
		Console.WriteLine( $"total event signals: {totalSignals} = {totalSignals / duration:F0} Hz" );

		foreach ( var ( index, name ) in TrackedEvents )
		{
			Console.WriteLine( $"  {name,-22} {eventCounts[ index ]} = {eventCounts[ index ] / duration:F0} Hz" );
		}

		Console.WriteLine( $"FFBTorque distinct changes: {ffbChanges} = {ffbChanges / duration:F0} Hz" );
		Console.WriteLine();
		Console.WriteLine( "-> if SME_FFB is ~400 Hz there is a real high-rate (processed) FFB signal; if it matches" );
		Console.WriteLine( "   SME_UPDATE_TELEMETRY (~100 Hz) there is no faster FFB than the raw shaft torque we use." );

		return 0;
	}

	private static MemoryMappedFile? TryOpenMap()
	{
		try
		{
			return MemoryMappedFile.OpenExisting( MapName, MemoryMappedFileRights.Read );
		}
		catch ( FileNotFoundException )
		{
			return null;
		}
	}
}
