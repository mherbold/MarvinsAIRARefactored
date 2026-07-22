
using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace LmuRateProbe;

// Settles the "is FFBTorque really 400 Hz?" question by BUSY-POLLING LMU_Data as fast as possible (no sleep, so
// no Windows-timer limit) and counting how often each value actually changes. The LMUFFB dev claims
// generic.FFBTorque (offset 68) is a "native 400Hz stream" while the raw mSteeringShaftTorque is 100 Hz - this
// measures both directly. The printed poll rate proves the loop is far faster than 400 Hz, so any measured
// change-rate is real, not poll-limited. Reading is lock-free (a torn double only invents one spurious "change",
// which would inflate - not hide - a high rate, so it cannot mask a true 400 Hz signal).

internal static class Program
{
	private const string MapName = "LMU_Data";

	private const int FfbTorqueOffset = 68;                 // float generic.FFBTorque
	private const int TelemetryOffset = 128464;
	private const int PlayerIndexOffset = TelemetryOffset + 1;   // byte
	private const int HasVehicleOffset = TelemetryOffset + 2;    // byte
	private const int VehicleArrayOffset = TelemetryOffset + 4;
	private const int VehicleSize = 1888;
	private const int VehicleElapsedTimeOffset = 12;        // double mElapsedTime
	private const int VehicleShaftTorqueOffset = 452;       // double mSteeringShaftTorque

	private static int Main()
	{
		Console.WriteLine( "MAIRA LMU rate probe" );
		Console.WriteLine( "Busy-polls LMU_Data to measure the TRUE update rate of FFBTorque vs raw shaft torque." );
		Console.WriteLine( "Get on track and drive (with FFB producing force). Press Ctrl+C to stop. Uses one CPU core." );
		Console.WriteLine();

		MemoryMappedFile memoryMappedFile;

		try
		{
			memoryMappedFile = MemoryMappedFile.OpenExisting( MapName, MemoryMappedFileRights.Read );
		}
		catch ( FileNotFoundException )
		{
			Console.WriteLine( $"Could not open '{MapName}'. Is Le Mans Ultimate running (v1.2+)?" );

			return 1;
		}

		using var accessor = memoryMappedFile.CreateViewAccessor( 0, 0, MemoryMappedFileAccess.Read );

		var capacity = accessor.Capacity;

		var stopRequested = false;

		Console.CancelKeyPress += ( sender, eventArgs ) =>
		{
			eventArgs.Cancel = true;
			stopRequested = true;
		};

		var stopwatch = Stopwatch.StartNew();

		var polls = 0L;
		var ffbChanges = 0L;
		var torqueChanges = 0L;
		var elapsedChanges = 0L;

		var lastFfb = float.NaN;
		var lastTorque = double.NaN;
		var lastElapsed = double.NaN;

		var lastReportSeconds = 0.0;
		var lastPolls = 0L;
		var lastFfbChanges = 0L;
		var lastTorqueChanges = 0L;
		var lastElapsedChanges = 0L;

		while ( !stopRequested )
		{
			polls++;

			var ffb = accessor.ReadSingle( FfbTorqueOffset );

			if ( ffb != lastFfb ) { ffbChanges++; lastFfb = ffb; }

			var playerIndex = accessor.ReadByte( PlayerIndexOffset );

			var vehicleOffset = VehicleArrayOffset + playerIndex * VehicleSize;

			// only read the player telemetry once the car is loaded and the offset is safely inside the map
			if ( ( accessor.ReadByte( HasVehicleOffset ) != 0 ) && ( vehicleOffset + VehicleShaftTorqueOffset + 8 <= capacity ) )
			{
				var elapsed = accessor.ReadDouble( vehicleOffset + VehicleElapsedTimeOffset );
				var torque = accessor.ReadDouble( vehicleOffset + VehicleShaftTorqueOffset );

				if ( torque != lastTorque ) { torqueChanges++; lastTorque = torque; }
				if ( elapsed != lastElapsed ) { elapsedChanges++; lastElapsed = elapsed; }
			}

			var seconds = stopwatch.Elapsed.TotalSeconds;

			if ( seconds - lastReportSeconds >= 1.0 )
			{
				var window = seconds - lastReportSeconds;

				Console.WriteLine(
					$"[{seconds,5:F0}s] poll {( polls - lastPolls ) / window / 1000.0,6:F0}k Hz | " +
					$"FFBTorque {( ffbChanges - lastFfbChanges ) / window,5:F0} Hz | " +
					$"shaft torque {( torqueChanges - lastTorqueChanges ) / window,5:F0} Hz | " +
					$"elapsedTime {( elapsedChanges - lastElapsedChanges ) / window,5:F0} Hz" );

				lastReportSeconds = seconds;
				lastPolls = polls;
				lastFfbChanges = ffbChanges;
				lastTorqueChanges = torqueChanges;
				lastElapsedChanges = elapsedChanges;
			}
		}

		var duration = stopwatch.Elapsed.TotalSeconds;

		Console.WriteLine();
		Console.WriteLine( $"=== Summary over {duration:F1}s ({polls / duration / 1000.0:F0}k polls/s) ===" );
		Console.WriteLine( $"FFBTorque (generic.FFBTorque @68): {ffbChanges} changes = {ffbChanges / duration:F0} Hz" );
		Console.WriteLine( $"raw shaft torque (mSteeringShaftTorque): {torqueChanges} changes = {torqueChanges / duration:F0} Hz" );
		Console.WriteLine( $"mElapsedTime (physics tick): {elapsedChanges} changes = {elapsedChanges / duration:F0} Hz" );
		Console.WriteLine();
		Console.WriteLine( "-> poll rate is far above 400 Hz, so these change-rates are the TRUE update rates." );
		Console.WriteLine( "   If FFBTorque shows ~400 Hz the dev is right; if ~100 Hz it is no faster than raw torque." );

		return 0;
	}
}
