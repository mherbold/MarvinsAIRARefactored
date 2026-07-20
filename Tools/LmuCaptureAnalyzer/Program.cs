
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

using MarvinsAIRARefactored.GameBridges.Rf2;

namespace LmuCaptureAnalyzer;

internal static class Program
{
	private const string FileMagic = "LMUCAP01";

	private record TelemetrySample( double CaptureSeconds, double ElapsedTime, double Steering, double ShaftTorque, double VelX, double VelY, double VelZ, double AccelX, double AccelY, double AccelZ, double RotY, double Yaw, int Gear, double Rpm, double Fuel, double Speed, double SuspDeflFL, float PhysicalSteeringRange, float VisualSteeringRange );

	private record FfbSample( double CaptureSeconds, double ForceValue );

	private record LmuProbeSample( double CaptureSeconds, byte PlayerVehicleIndex, float GenericFfb, double ElapsedTime, double Steering, double Torque );

	private const int LmuDataTelemetryOffset = 128464;
	private const int LmuDataVehicleArrayOffset = LmuDataTelemetryOffset + 4;
	private const int LmuDataVehicleSize = 1888;

	private record ScoringSample( double CaptureSeconds, rF2ScoringInfo Info, rF2VehicleScoring[] Vehicles, int PlayerIndex );

	private static int Main( string[] args )
	{
		if ( args.Length < 1 )
		{
			Console.WriteLine( "usage: LmuCaptureAnalyzer <capture.lmucap>" );

			return 1;
		}

		var telemetrySamples = new List<TelemetrySample>();
		var ffbSamples = new List<FfbSample>();
		var scoringSamples = new List<ScoringSample>();
		var lmuProbeSamples = new List<LmuProbeSample>();

		byte[]? lmuDataSnapshot = null;

		var telemetryVehicleSize = Marshal.SizeOf<rF2VehicleTelemetry>();
		var scoringInfoSize = Marshal.SizeOf<rF2ScoringInfo>();
		var scoringVehicleSize = Marshal.SizeOf<rF2VehicleScoring>();

		var frameCounts = new Dictionary<int, long>();
		var firstSeconds = new Dictionary<int, double>();
		var lastSeconds = new Dictionary<int, double>();

		string[] bufferNames;
		var playerId = -1;

		using ( var fileStream = File.OpenRead( args[ 0 ] ) )
		using ( var gzipStream = new GZipStream( fileStream, CompressionMode.Decompress ) )
		using ( var reader = new BinaryReader( gzipStream ) )
		{
			var magic = Encoding.ASCII.GetString( reader.ReadBytes( FileMagic.Length ) );

			if ( magic != FileMagic )
			{
				Console.WriteLine( $"Not an LMU capture file (magic = {magic})" );

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

				var bufferName = bufferNames[ bufferIndex ];

				if ( bufferName == "LMU_Data_Probe" )
				{
					lmuProbeSamples.Add( new LmuProbeSample( captureSeconds, payload[ 0 ], BitConverter.ToSingle( payload, 1 ), BitConverter.ToDouble( payload, 5 ), BitConverter.ToDouble( payload, 13 ), BitConverter.ToDouble( payload, 21 ) ) );
				}
				else if ( bufferName == "LMU_Data" )
				{
					lmuDataSnapshot = payload;
				}
				else if ( bufferName.Contains( "ForceFeedback" ) )
				{
					ffbSamples.Add( new FfbSample( captureSeconds, BitConverter.ToDouble( payload, 8 ) ) );
				}
				else if ( bufferName.Contains( "Scoring" ) )
				{
					var info = ReadStruct<rF2ScoringInfo>( payload, 12 );

					var numVehicles = Math.Clamp( info.mNumVehicles, 0, rFactor2Constants.MAX_MAPPED_VEHICLES );

					var vehicles = new rF2VehicleScoring[ numVehicles ];

					var playerIndex = -1;

					for ( var i = 0; i < numVehicles; i++ )
					{
						vehicles[ i ] = ReadStruct<rF2VehicleScoring>( payload, 12 + scoringInfoSize + i * scoringVehicleSize );

						if ( vehicles[ i ].mIsPlayer != 0 )
						{
							playerIndex = i;
							playerId = vehicles[ i ].mID;
						}
					}

					scoringSamples.Add( new ScoringSample( captureSeconds, info, vehicles, playerIndex ) );
				}
				else if ( bufferName.Contains( "Telemetry" ) )
				{
					var numVehicles = Math.Clamp( BitConverter.ToInt32( payload, 12 ), 0, rFactor2Constants.MAX_MAPPED_VEHICLES );

					for ( var i = 0; i < numVehicles; i++ )
					{
						var offset = 16 + i * telemetryVehicleSize;

						if ( ( playerId != -1 ) && ( BitConverter.ToInt32( payload, offset ) == playerId ) )
						{
							var vehicle = ReadStruct<rF2VehicleTelemetry>( payload, offset );

							var speed = Math.Sqrt( vehicle.mLocalVel.x * vehicle.mLocalVel.x + vehicle.mLocalVel.y * vehicle.mLocalVel.y + vehicle.mLocalVel.z * vehicle.mLocalVel.z );

							var yaw = Math.Atan2( vehicle.mOri[ 2 ].x, vehicle.mOri[ 2 ].z );

							telemetrySamples.Add( new TelemetrySample( captureSeconds, vehicle.mElapsedTime, vehicle.mUnfilteredSteering, vehicle.mSteeringShaftTorque, vehicle.mLocalVel.x, vehicle.mLocalVel.y, vehicle.mLocalVel.z, vehicle.mLocalAccel.x, vehicle.mLocalAccel.y, vehicle.mLocalAccel.z, vehicle.mLocalRot.y, yaw, vehicle.mGear, vehicle.mEngineRPM, vehicle.mFuel, speed, vehicle.mWheels[ 0 ].mSuspensionDeflection, vehicle.mPhysicalSteeringWheelRange, vehicle.mVisualSteeringWheelRange ) );

							break;
						}
					}
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

		if ( scoringSamples.Count > 0 )
		{
			var scoring = scoringSamples[ ^1 ];

			Console.WriteLine( "=== Scoring ===" );
			Console.WriteLine( $"Track: '{ReadString( scoring.Info.mTrackName )}'  lapDist: {scoring.Info.mLapDist:F0} m  session: {scoring.Info.mSession}  gamePhase: {scoring.Info.mGamePhase}  maxLaps: {scoring.Info.mMaxLaps}" );
			Console.WriteLine( $"currentET: {scoring.Info.mCurrentET:F1}  endET: {scoring.Info.mEndET:F1}  numVehicles: {scoring.Info.mNumVehicles}  inRealtime: {scoring.Info.mInRealtime}" );
			Console.WriteLine( $"ambient: {scoring.Info.mAmbientTemp:F1}C  track: {scoring.Info.mTrackTemp:F1}C  raining: {scoring.Info.mRaining:F2}" );

			if ( scoring.PlayerIndex != -1 )
			{
				var player = scoring.Vehicles[ scoring.PlayerIndex ];

				Console.WriteLine( $"Player: '{ReadString( player.mDriverName )}'  vehicle: '{ReadString( player.mVehicleName )}'  class: '{ReadString( player.mVehicleClass )}'" );
				Console.WriteLine( $"  id: {player.mID}  place: {player.mPlace}  totalLaps: {player.mTotalLaps}  bestLap: {player.mBestLapTime:F3}  lastLap: {player.mLastLapTime:F3}" );
			}

			Console.WriteLine( "Player state timeline (inGarage / inPits / place / lapDist):" );

			ScoringSample? previous = null;

			foreach ( var sample in scoringSamples )
			{
				if ( sample.PlayerIndex == -1 )
				{
					continue;
				}

				var player = sample.Vehicles[ sample.PlayerIndex ];

				if ( ( previous == null ) || ( previous.PlayerIndex == -1 ) || ( previous.Vehicles[ previous.PlayerIndex ].mInGarageStall != player.mInGarageStall ) || ( previous.Vehicles[ previous.PlayerIndex ].mInPits != player.mInPits ) )
				{
					Console.WriteLine( $"  t={sample.CaptureSeconds,7:F1}s  inGarage={player.mInGarageStall}  inPits={player.mInPits}  place={player.mPlace}  lapDist={player.mLapDist:F0}" );
				}

				previous = sample;
			}

			Console.WriteLine();
		}

		if ( telemetrySamples.Count > 0 )
		{
			Console.WriteLine( "=== Telemetry ranges ===" );

			Console.WriteLine( $"player telemetry samples: {telemetrySamples.Count}" );

			var elapsedDeltas = new List<double>();

			for ( var i = 1; i < telemetrySamples.Count; i++ )
			{
				var delta = telemetrySamples[ i ].ElapsedTime - telemetrySamples[ i - 1 ].ElapsedTime;

				if ( delta > 0.0 )
				{
					elapsedDeltas.Add( delta );
				}
			}

			elapsedDeltas.Sort();

			if ( elapsedDeltas.Count > 0 )
			{
				Console.WriteLine( $"elapsedTime delta: median {elapsedDeltas[ elapsedDeltas.Count / 2 ] * 1000.0:F2} ms -> effective physics feed {1.0 / elapsedDeltas[ elapsedDeltas.Count / 2 ]:F1} Hz" );
			}

			PrintRange( "steering (unfiltered)", telemetrySamples.Select( s => s.Steering ) );
			PrintRange( "shaft torque Nm", telemetrySamples.Select( s => s.ShaftTorque ) );
			PrintRange( "speed m/s", telemetrySamples.Select( s => s.Speed ) );
			PrintRange( "localVel.x", telemetrySamples.Select( s => s.VelX ) );
			PrintRange( "localVel.y", telemetrySamples.Select( s => s.VelY ) );
			PrintRange( "localVel.z", telemetrySamples.Select( s => s.VelZ ) );
			PrintRange( "localAccel.y (vertical?)", telemetrySamples.Select( s => s.AccelY ) );
			PrintRange( "localRot.y (yaw rate?)", telemetrySamples.Select( s => s.RotY ) );
			PrintRange( "rpm", telemetrySamples.Select( s => s.Rpm ) );
			PrintRange( "fuel", telemetrySamples.Select( s => s.Fuel ) );
			PrintRange( "suspDefl FL", telemetrySamples.Select( s => s.SuspDeflFL ) );

			Console.WriteLine( $"physical steering wheel range: {telemetrySamples[ ^1 ].PhysicalSteeringRange:F0} (deg?)   visual steering wheel range: {telemetrySamples[ ^1 ].VisualSteeringRange:F0} (deg?)" );
			Console.WriteLine( $"gears seen: {string.Join( ",", telemetrySamples.Select( s => s.Gear ).Distinct().OrderBy( g => g ) )}" );
			Console.WriteLine();

			Console.WriteLine( "=== At-rest sample (garage, speed < 0.2) ===" );

			var restSample = telemetrySamples.FirstOrDefault( s => s.Speed < 0.2 );

			if ( restSample != null )
			{
				Console.WriteLine( $"t={restSample.CaptureSeconds:F1}s  accel=({restSample.AccelX:F3}, {restSample.AccelY:F3}, {restSample.AccelZ:F3})  vel=({restSample.VelX:F3}, {restSample.VelY:F3}, {restSample.VelZ:F3})" );
				Console.WriteLine( "-> if accel.y is ~0 at rest, mLocalAccel excludes gravity; if ~ -9.8 or +9.8 it includes it" );
			}
			else
			{
				Console.WriteLine( "(no at-rest sample found)" );
			}

			Console.WriteLine();

			Console.WriteLine( "=== Forward-motion sign check (speed > 5, |steering| < 0.1) ===" );

			var straightSamples = telemetrySamples.Where( s => ( s.Speed > 5.0 ) && ( Math.Abs( s.Steering ) < 0.1 ) && ( s.Gear > 0 ) ).ToList();

			if ( straightSamples.Count > 0 )
			{
				Console.WriteLine( $"samples: {straightSamples.Count}  avg localVel.z: {straightSamples.Average( s => s.VelZ ):F2}  avg localVel.x: {straightSamples.Average( s => s.VelX ):F2}" );
				Console.WriteLine( "-> if avg localVel.z is negative while driving forward, forward = -z (as assumed)" );
			}

			Console.WriteLine();

			Console.WriteLine( "=== First sustained steering event (the known HARD RIGHT into pit lane) ===" );

			var eventStart = -1.0;

			for ( var i = 0; i < telemetrySamples.Count; i++ )
			{
				var sample = telemetrySamples[ i ];

				if ( ( sample.Speed > 3.0 ) && ( Math.Abs( sample.Steering ) > 0.4 ) )
				{
					if ( eventStart < 0.0 )
					{
						eventStart = sample.CaptureSeconds;
					}
					else if ( sample.CaptureSeconds - eventStart > 0.5 )
					{
						var window = telemetrySamples.Where( s => ( s.CaptureSeconds >= eventStart ) && ( s.CaptureSeconds <= eventStart + 2.0 ) ).ToList();

						Console.WriteLine( $"t={eventStart:F1}s  avg steering: {window.Average( s => s.Steering ):F3}  avg yawRate(rot.y): {window.Average( s => s.RotY ):F3}  avg latAccel(accel.x): {window.Average( s => s.AccelX ):F3}  avg torque: {window.Average( s => s.ShaftTorque ):F2}" );
						Console.WriteLine( "-> user reports this was a RIGHT turn: steering sign here = rF2's right-turn sign" );

						break;
					}
				}
				else
				{
					eventStart = -1.0;
				}
			}

			Console.WriteLine();

			Console.WriteLine( "=== Correlations (speed > 5 m/s) ===" );

			var movingSamples = telemetrySamples.Where( s => s.Speed > 5.0 ).ToList();

			if ( movingSamples.Count > 10 )
			{
				Console.WriteLine( $"corr( steering, yawRate rot.y )  = {Correlation( movingSamples.Select( s => s.Steering ), movingSamples.Select( s => s.RotY ) ):F3}" );
				Console.WriteLine( $"corr( steering, latAccel x )    = {Correlation( movingSamples.Select( s => s.Steering ), movingSamples.Select( s => s.AccelX ) ):F3}" );
				Console.WriteLine( $"corr( steering, shaftTorque )   = {Correlation( movingSamples.Select( s => s.Steering ), movingSamples.Select( s => s.ShaftTorque ) ):F3}" );
				Console.WriteLine( $"corr( yawRate, latAccel x )     = {Correlation( movingSamples.Select( s => s.RotY ), movingSamples.Select( s => s.AccelX ) ):F3}" );
			}

			Console.WriteLine();
		}

		if ( lmuProbeSamples.Count > 0 )
		{
			Console.WriteLine( "=== Native LMU_Data map (high-rate probe) ===" );

			Console.WriteLine( $"probe frames (any watched value changed): {lmuProbeSamples.Count}" );

			PrintCadence( "elapsedTime", lmuProbeSamples, s => s.ElapsedTime );
			PrintCadence( "shaft torque", lmuProbeSamples, s => s.Torque );
			PrintCadence( "steering", lmuProbeSamples, s => s.Steering );
			PrintCadence( "generic FFB", lmuProbeSamples, s => s.GenericFfb );

			PrintRange( "native shaft torque Nm", lmuProbeSamples.Select( s => s.Torque ) );
			PrintRange( "native generic FFB", lmuProbeSamples.Select( s => (double) s.GenericFfb ) );
			PrintRange( "native steering", lmuProbeSamples.Select( s => s.Steering ) );

			Console.WriteLine();
		}

		if ( lmuDataSnapshot != null )
		{
			Console.WriteLine( "=== Native LMU_Data map (full snapshot validation) ===" );

			Console.WriteLine( $"map size: {lmuDataSnapshot.Length} bytes" );

			if ( lmuDataSnapshot.Length > LmuDataVehicleArrayOffset + LmuDataVehicleSize )
			{
				var playerVehicleIndex = lmuDataSnapshot[ LmuDataTelemetryOffset + 1 ];

				var vehicleOffset = LmuDataVehicleArrayOffset + playerVehicleIndex * LmuDataVehicleSize;

				var nameBytes = new byte[ 64 ];

				Buffer.BlockCopy( lmuDataSnapshot, vehicleOffset + 32, nameBytes, 0, 64 );
				Console.WriteLine( $"vehicle name at native offset: '{ReadString( nameBytes )}'" );

				Buffer.BlockCopy( lmuDataSnapshot, vehicleOffset + 96, nameBytes, 0, 64 );
				Console.WriteLine( $"track name at native offset: '{ReadString( nameBytes )}'" );

				Console.WriteLine( $"physical steering wheel range at native offset: {BitConverter.ToSingle( lmuDataSnapshot, vehicleOffset + 692 ):F0}" );
				Console.WriteLine( $"ABS active byte: {lmuDataSnapshot[ vehicleOffset + 746 ]}   TC active byte: {lmuDataSnapshot[ vehicleOffset + 747 ]}" );
			}

			Console.WriteLine();
		}

		if ( ffbSamples.Count > 0 )
		{
			Console.WriteLine( "=== Force feedback buffer ===" );

			PrintRange( "mForceValue", ffbSamples.Select( s => s.ForceValue ) );

			// pair each telemetry sample with the nearest-in-time ffb sample and regress
			if ( telemetrySamples.Count > 10 )
			{
				var pairedForce = new List<double>();
				var pairedTorque = new List<double>();

				var ffbIndex = 0;

				foreach ( var telemetrySample in telemetrySamples )
				{
					while ( ( ffbIndex < ffbSamples.Count - 1 ) && ( ffbSamples[ ffbIndex + 1 ].CaptureSeconds <= telemetrySample.CaptureSeconds ) )
					{
						ffbIndex++;
					}

					pairedForce.Add( ffbSamples[ ffbIndex ].ForceValue );
					pairedTorque.Add( telemetrySample.ShaftTorque );
				}

				var correlation = Correlation( pairedForce, pairedTorque );

				var meanForce = pairedForce.Average();
				var meanTorque = pairedTorque.Average();

				var covariance = 0.0;
				var variance = 0.0;

				for ( var i = 0; i < pairedForce.Count; i++ )
				{
					covariance += ( pairedForce[ i ] - meanForce ) * ( pairedTorque[ i ] - meanTorque );
					variance += ( pairedForce[ i ] - meanForce ) * ( pairedForce[ i ] - meanForce );
				}

				var slope = ( variance > 1e-9 ) ? covariance / variance : 0.0;

				Console.WriteLine( $"corr( mForceValue, shaftTorque ) = {correlation:F3}   regression slope = {slope:F2} Nm per unit" );
			}
		}

		return 0;
	}

	private static void PrintCadence( string name, List<LmuProbeSample> samples, Func<LmuProbeSample, double> selector )
	{
		var changeTimes = new List<double>();

		var lastValue = double.NaN;

		foreach ( var sample in samples )
		{
			var value = selector( sample );

			if ( value != lastValue )
			{
				changeTimes.Add( sample.CaptureSeconds );

				lastValue = value;
			}
		}

		if ( changeTimes.Count < 3 )
		{
			Console.WriteLine( $"{name,-14} changes: {changeTimes.Count} (not enough to measure cadence)" );

			return;
		}

		var deltas = new List<double>();

		for ( var i = 1; i < changeTimes.Count; i++ )
		{
			deltas.Add( changeTimes[ i ] - changeTimes[ i - 1 ] );
		}

		deltas.Sort();

		var median = deltas[ deltas.Count / 2 ];

		Console.WriteLine( $"{name,-14} changes: {changeTimes.Count,7}   median interval: {median * 1000.0,7:F2} ms   -> ~{( median > 0 ? 1.0 / median : 0 ),6:F0} Hz" );
	}

	private static void PrintRange( string name, IEnumerable<double> values )
	{
		var list = values.ToList();

		Console.WriteLine( $"{name,-26} min {list.Min(),10:F3}   max {list.Max(),10:F3}   avg {list.Average(),10:F3}" );
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

	private static T ReadStruct<T>( byte[] buffer, int offset ) where T : struct
	{
		var handle = GCHandle.Alloc( buffer, GCHandleType.Pinned );

		try
		{
			return Marshal.PtrToStructure<T>( handle.AddrOfPinnedObject() + offset )!;
		}
		finally
		{
			handle.Free();
		}
	}

	private static string ReadString( byte[] bytes )
	{
		var length = Array.IndexOf( bytes, (byte) 0 );

		if ( length == -1 )
		{
			length = bytes.Length;
		}

		return Encoding.UTF8.GetString( bytes, 0, length );
	}
}
