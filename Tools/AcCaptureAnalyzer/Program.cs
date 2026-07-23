
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

using MarvinsAIRARefactored.GameBridges.Ac;

namespace AcCaptureAnalyzer;

internal static class Program
{
	private const string FileMagic = "ACCAP01";

	private record Sample( double CaptureSeconds, AcPhysics Physics );

	private static int Main( string[] args )
	{
		Console.WriteLine( "=== Struct sizes (sanity check the layout) ===" );
		Console.WriteLine( $"AcPhysics: {Marshal.SizeOf<AcPhysics>()} bytes (AC vanilla physics page = 580)" );
		Console.WriteLine( $"AcGraphics: {Marshal.SizeOf<AcGraphics>()} bytes (expect 248)" );
		Console.WriteLine( $"AcStatic: {Marshal.SizeOf<AcStatic>()} bytes" );
		Console.WriteLine();

		if ( args.Length < 1 )
		{
			Console.WriteLine( "usage: AcCaptureAnalyzer <capture.accap>" );

			return 1;
		}

		var physicsSamples = new List<Sample>();

		var frameCounts = new Dictionary<int, long>();
		var firstSeconds = new Dictionary<int, double>();
		var lastSeconds = new Dictionary<int, double>();

		AcStatic staticInfo = default;
		AcGraphics graphics = default;
		var haveStatic = false;
		var haveGraphics = false;

		string[] bufferNames;

		using ( var fileStream = File.OpenRead( args[ 0 ] ) )
		using ( var gzipStream = new GZipStream( fileStream, CompressionMode.Decompress ) )
		using ( var reader = new BinaryReader( gzipStream ) )
		{
			var magic = Encoding.ASCII.GetString( reader.ReadBytes( FileMagic.Length ) );

			if ( magic != FileMagic )
			{
				Console.WriteLine( $"Not an AC capture file (magic = {magic})" );

				return 1;
			}

			var bufferCount = reader.ReadInt32();

			bufferNames = new string[ bufferCount ];

			for ( var i = 0; i < bufferCount; i++ )
			{
				bufferNames[ i ] = reader.ReadString();
			}

			Console.WriteLine( $"Buffers: {string.Join( ", ", bufferNames )}" );
			Console.WriteLine();

			while ( true )
			{
				int bufferIndex;

				try
				{
					bufferIndex = reader.ReadInt32();
				}
				catch ( EndOfStreamException )
				{
					break;
				}

				if ( bufferIndex < 0 )
				{
					break;
				}

				var captureSeconds = reader.ReadDouble();
				var length = reader.ReadInt32();
				var payload = reader.ReadBytes( length );

				frameCounts[ bufferIndex ] = frameCounts.GetValueOrDefault( bufferIndex ) + 1;

				if ( !firstSeconds.ContainsKey( bufferIndex ) )
				{
					firstSeconds[ bufferIndex ] = captureSeconds;
				}

				lastSeconds[ bufferIndex ] = captureSeconds;

				switch ( (AcBufferType) bufferIndex )
				{
					case AcBufferType.Physics:
						physicsSamples.Add( new Sample( captureSeconds, ReadStruct<AcPhysics>( payload ) ) );
						break;

					case AcBufferType.Graphics:
						graphics = ReadStruct<AcGraphics>( payload );
						haveGraphics = true;
						break;

					case AcBufferType.Static:
						staticInfo = ReadStruct<AcStatic>( payload );
						haveStatic = true;
						break;
				}
			}
		}

		Console.WriteLine( "=== Buffer cadences ===" );

		for ( var i = 0; i < bufferNames.Length; i++ )
		{
			if ( frameCounts.TryGetValue( i, out var count ) )
			{
				var duration = lastSeconds[ i ] - firstSeconds[ i ];

				Console.WriteLine( $"{bufferNames[ i ]}: {count} frames over {duration:F1}s = {( duration > 0 ? count / duration : 0 ):F1} Hz" );
			}
			else
			{
				Console.WriteLine( $"{bufferNames[ i ]}: no frames" );
			}
		}

		Console.WriteLine();

		if ( haveStatic )
		{
			Console.WriteLine( "=== Static ===" );
			Console.WriteLine( $"smVersion: '{ReadString( staticInfo.smVersion )}'  acVersion: '{ReadString( staticInfo.acVersion )}'" );
			Console.WriteLine( $"car: '{ReadString( staticInfo.carModel )}'  track: '{ReadString( staticInfo.track )}'  config: '{ReadString( staticInfo.trackConfiguration )}'" );
			Console.WriteLine( $"numCars: {staticInfo.numCars}  maxRpm: {staticInfo.maxRpm}  maxFuel: {staticInfo.maxFuel:F1}  maxTorque: {staticInfo.maxTorque:F0}  maxPower: {staticInfo.maxPower:F0}" );
			Console.WriteLine( $"trackSPlineLength: {staticInfo.trackSPlineLength:F1} m  player: '{ReadString( staticInfo.playerName )} {ReadString( staticInfo.playerSurname )}'" );
			Console.WriteLine( "-> if car/track/maxRpm look wrong, the static struct layout (padding) is off" );
			Console.WriteLine();
		}

		if ( haveGraphics )
		{
			Console.WriteLine( "=== Graphics (last frame) ===" );
			Console.WriteLine( $"status: {(AcStatus) graphics.status}  session: {(AcSessionType) graphics.session}  completedLaps: {graphics.completedLaps}  position: {graphics.position}" );
			Console.WriteLine( $"isInPit: {graphics.isInPit}  numberOfLaps: {graphics.numberOfLaps}  normalizedCarPosition: {graphics.normalizedCarPosition:F3}  tyreCompound: '{ReadString( graphics.tyreCompound )}'" );
			Console.WriteLine();
		}

		if ( physicsSamples.Count == 0 )
		{
			Console.WriteLine( "(no physics samples)" );

			return 0;
		}

		Console.WriteLine( "=== Physics ranges ===" );
		Console.WriteLine( $"physics samples: {physicsSamples.Count}" );
		PrintRange( "gas", physicsSamples.Select( s => (double) s.Physics.gas ) );
		PrintRange( "brake", physicsSamples.Select( s => (double) s.Physics.brake ) );
		PrintRange( "clutch", physicsSamples.Select( s => (double) s.Physics.clutch ) );
		PrintRange( "steerAngle", physicsSamples.Select( s => (double) s.Physics.steerAngle ) );
		PrintRange( "speedKmh", physicsSamples.Select( s => (double) s.Physics.speedKmh ) );
		PrintRange( "rpms", physicsSamples.Select( s => (double) s.Physics.rpms ) );
		PrintRange( "finalFF", physicsSamples.Select( s => (double) s.Physics.finalFF ) );
		PrintRange( "abs", physicsSamples.Select( s => (double) s.Physics.abs ) );
		PrintRange( "heading", physicsSamples.Select( s => (double) s.Physics.heading ) );
		PrintRange( "localVelocity[0]", physicsSamples.Select( s => (double) s.Physics.localVelocity[ 0 ] ) );
		PrintRange( "localVelocity[1]", physicsSamples.Select( s => (double) s.Physics.localVelocity[ 1 ] ) );
		PrintRange( "localVelocity[2]", physicsSamples.Select( s => (double) s.Physics.localVelocity[ 2 ] ) );
		PrintRange( "accG[0]", physicsSamples.Select( s => (double) s.Physics.accG[ 0 ] ) );
		PrintRange( "accG[1]", physicsSamples.Select( s => (double) s.Physics.accG[ 1 ] ) );
		PrintRange( "accG[2]", physicsSamples.Select( s => (double) s.Physics.accG[ 2 ] ) );
		PrintRange( "localAngularVel[1]", physicsSamples.Select( s => (double) s.Physics.localAngularVel[ 1 ] ) );
		Console.WriteLine( $"gears seen: {string.Join( ",", physicsSamples.Select( s => s.Physics.gear ).Distinct().OrderBy( g => g ) )}" );
		Console.WriteLine();

		Console.WriteLine( "=== At-rest sample (speedKmh < 1) - accG vertical reveals the gravity convention ===" );

		var rest = physicsSamples.FirstOrDefault( s => s.Physics.speedKmh < 1f );

		if ( rest != null )
		{
			Console.WriteLine( $"accG = ({rest.Physics.accG[ 0 ]:F3}, {rest.Physics.accG[ 1 ]:F3}, {rest.Physics.accG[ 2 ]:F3})  -> the ~±1 axis is vertical (gravity)" );
		}

		Console.WriteLine();

		var moving = physicsSamples.Where( s => s.Physics.speedKmh > 20f ).ToList();

		if ( moving.Count > 10 )
		{
			Console.WriteLine( "=== Forward-motion component check (speedKmh > 20) ===" );
			Console.WriteLine( $"avg localVelocity = ({moving.Average( s => s.Physics.localVelocity[ 0 ] ):F2}, {moving.Average( s => s.Physics.localVelocity[ 1 ] ):F2}, {moving.Average( s => s.Physics.localVelocity[ 2 ] ):F2})" );
			Console.WriteLine( "-> the large component is the forward axis; its sign tells us whether forward is + or -" );
			Console.WriteLine();

			Console.WriteLine( "=== World-vs-local heading self-consistency ===" );
			Console.WriteLine( "For each candidate (worldForward, worldLateral, headingSign), we rotate the WORLD velocity by the" );
			Console.WriteLine( "heading and see whether it reproduces localVelocity. The best match pins heading handedness + axes." );
			ProbeHeadingConsistency( moving );
			Console.WriteLine();

			Console.WriteLine( "=== Steering correlations (speedKmh > 20; steerAngle sign convention TBD) ===" );
			Console.WriteLine( $"corr( steerAngle, accG[0] )        = {Correlation( moving.Select( s => (double) s.Physics.steerAngle ), moving.Select( s => (double) s.Physics.accG[ 0 ] ) ):F3}" );
			Console.WriteLine( $"corr( steerAngle, accG[2] )        = {Correlation( moving.Select( s => (double) s.Physics.steerAngle ), moving.Select( s => (double) s.Physics.accG[ 2 ] ) ):F3}" );
			Console.WriteLine( $"corr( steerAngle, localAngularVel[1] ) = {Correlation( moving.Select( s => (double) s.Physics.steerAngle ), moving.Select( s => (double) s.Physics.localAngularVel[ 1 ] ) ):F3}" );
			Console.WriteLine( $"corr( steerAngle, finalFF )        = {Correlation( moving.Select( s => (double) s.Physics.steerAngle ), moving.Select( s => (double) s.Physics.finalFF ) ):F3}" );
			Console.WriteLine( $"corr( localAngularVel[1], accG[0] ) = {Correlation( moving.Select( s => (double) s.Physics.localAngularVel[ 1 ] ), moving.Select( s => (double) s.Physics.accG[ 0 ] ) ):F3}" );
		}

		return 0;
	}

	private static void ProbeHeadingConsistency( List<Sample> moving )
	{
		var best = ( residual: double.MaxValue, desc: "none" );

		// try each pairing of world velocity components as (forward, lateral) and each heading sign
		int[][] axisPairs = [ [ 0, 2 ], [ 2, 0 ], [ 0, 1 ], [ 1, 0 ], [ 2, 1 ], [ 1, 2 ] ];

		foreach ( var pair in axisPairs )
		{
			foreach ( var headingSign in new[] { 1.0, -1.0 } )
			{
				foreach ( var latSign in new[] { 1.0, -1.0 } )
				{
					var residual = 0.0;

					foreach ( var s in moving )
					{
						var p = s.Physics;

						var yaw = headingSign * p.heading;
						var sinY = Math.Sin( yaw );
						var cosY = Math.Cos( yaw );

						var worldFwd = p.velocity[ pair[ 0 ] ];
						var worldLat = p.velocity[ pair[ 1 ] ];

						// rotate world (fwd,lat) by -yaw into the body frame
						var bodyFwd = worldFwd * cosY + worldLat * sinY;
						var bodyLat = latSign * ( -worldFwd * sinY + worldLat * cosY );

						// compare against the largest-magnitude local components (forward + lateral)
						var localFwd = p.localVelocity[ 2 ];
						var localLat = p.localVelocity[ 0 ];

						residual += ( bodyFwd - localFwd ) * ( bodyFwd - localFwd ) + ( bodyLat - localLat ) * ( bodyLat - localLat );
					}

					residual = Math.Sqrt( residual / moving.Count );

					if ( residual < best.residual )
					{
						best = ( residual, $"world(fwd=v[{pair[ 0 ]}], lat=v[{pair[ 1 ]}]), headingSign={headingSign:+0;-0}, latSign={latSign:+0;-0}" );
					}
				}
			}
		}

		Console.WriteLine( $"best fit: {best.desc}   rms residual: {best.residual:F3} m/s" );
		Console.WriteLine( "(a small residual means localVelocity[2]=forward, localVelocity[0]=lateral under that heading convention)" );
	}

	private static void PrintRange( string name, IEnumerable<double> values )
	{
		var list = values.ToList();

		Console.WriteLine( $"{name,-22} min {list.Min(),10:F3}   max {list.Max(),10:F3}   avg {list.Average(),10:F3}" );
	}

	private static double Correlation( IEnumerable<double> xs, IEnumerable<double> ys )
	{
		var xList = xs.ToList();
		var yList = ys.ToList();

		var meanX = xList.Average();
		var meanY = yList.Average();

		var covariance = 0.0;
		var varianceX = 0.0;
		var varianceY = 0.0;

		for ( var i = 0; i < xList.Count; i++ )
		{
			covariance += ( xList[ i ] - meanX ) * ( yList[ i ] - meanY );
			varianceX += ( xList[ i ] - meanX ) * ( xList[ i ] - meanX );
			varianceY += ( yList[ i ] - meanY ) * ( yList[ i ] - meanY );
		}

		var denominator = Math.Sqrt( varianceX * varianceY );

		return ( denominator > 1e-9 ) ? covariance / denominator : 0.0;
	}

	// the structs are blittable (inline arrays, see AcData.cs), so this is a straight memory read
	private static T ReadStruct<T>( byte[] buffer ) where T : struct
	{
		return MemoryMarshal.Read<T>( buffer );
	}

	private static string ReadString( ReadOnlySpan<char> chars )
	{
		var length = chars.IndexOf( '\0' );

		if ( length == -1 )
		{
			length = chars.Length;
		}

		return new string( chars[ ..length ] );
	}
}
