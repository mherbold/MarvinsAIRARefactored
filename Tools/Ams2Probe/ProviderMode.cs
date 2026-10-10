using System.Runtime.InteropServices;

using MarvinsAIRARefactored.GameBridges.Ams2;

namespace Ams2Probe;

// exercises the bridge's own live data provider against whatever is publishing $pcars2$ right now
public static class ProviderMode
{
	public static void Run()
	{
		var provider = new Ams2LiveDataProvider();
		var buffer = new byte[ Ams2Constants.StructSize ];
		var opened = false;

		for ( var attempt = 0; attempt < 30 && !opened; attempt++ )
		{
			opened = provider.TryOpen();

			if ( !opened )
			{
				Thread.Sleep( 100 );
			}
		}

		Console.WriteLine( $"provider open: {opened}" );

		if ( !opened )
		{
			return;
		}

		var reads = 0;
		var torn = 0;
		var advanced = 0;
		var oddAccepted = 0;
		uint lastSeq = 0;
		var lastSpeed = 0f;
		var minGap = int.MaxValue;
		var maxGap = 0;
		var start = Environment.TickCount64;

		while ( Environment.TickCount64 - start < 3000 )
		{
			reads++;

			if ( !provider.TryReadBlock( buffer ) )
			{
				torn++;
			}
			else
			{
				var d = MemoryMarshal.Read<Ams2SharedMemory>( buffer );

				if ( ( d.mSequenceNumber & 1u ) != 0u )
				{
					oddAccepted++;
				}

				if ( d.mSequenceNumber != lastSeq )
				{
					if ( lastSeq != 0 )
					{
						var gap = (int) ( d.mSequenceNumber - lastSeq );

						minGap = Math.Min( minGap, gap );
						maxGap = Math.Max( maxGap, gap );
					}

					lastSeq = d.mSequenceNumber;
					advanced++;
					lastSpeed = d.mSpeed;
				}
			}

			Thread.Sleep( 2 );
		}

		provider.Close();

		Console.WriteLine( $"reads {reads} torn {torn} advanced {advanced} oddAccepted {oddAccepted} lastSeq {lastSeq} seqGap {minGap}..{maxGap} lastSpeed {lastSpeed:F1}" );
	}
}
