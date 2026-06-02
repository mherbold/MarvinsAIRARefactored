
namespace MarvinsAIRARefactored.Components;

public class TimingMarkers
{
	private class Car
	{
		public int lastMarkerIndex;

		public readonly double[] markerTiming = new double[ MaxNumMarkers ];
	}

	private const int MaxNumMarkers = 3000;
	private const float MinMarkerSpacingInMeters = 10f;

	private int numMarkers = MaxNumMarkers;
	private float markerSpacingInMeters = MinMarkerSpacingInMeters;
	private float trackLengthInMeters = 0f;

	private int lastSessionNum = 0;

	private Car[] cars = [];

	public void Initialize()
	{
		Reset();
	}

	public void Initialize( int numCars )
	{
		cars = new Car[ numCars ];

		for ( var i = 0; i < cars.Length; i++ )
		{
			cars[ i ] = new Car();
		}

		Reset();
	}

	public void Reset()
	{
		for ( int i = 0; i < cars.Length; i++ )
		{
			cars[ i ].lastMarkerIndex = 0;

			Array.Clear( cars[ i ].markerTiming, 0, cars[ i ].markerTiming.Length );
		}
	}

	public void UpdateTrackLength()
	{
		var app = App.Instance!;

		var newNumMarkers = (int) Math.Clamp( Math.Ceiling( app.Simulator.TrackLength * 1000f / MinMarkerSpacingInMeters ), 2, MaxNumMarkers );
		var newMarkerSpacing = app.Simulator.TrackLength * 1000f / newNumMarkers;

		if ( ( newNumMarkers != numMarkers ) || ( newMarkerSpacing != markerSpacingInMeters ) )
		{
			numMarkers = newNumMarkers;
			markerSpacingInMeters = newMarkerSpacing;
			trackLengthInMeters = app.Simulator.TrackLength * 1000f;
		}
	}

	public bool TryGetMarkerTimeAtLapPct( int carIdx, float lapPct, out int markerIndex, out double time )
	{
		markerIndex = -1;
		time = 0f;

		if ( ( carIdx < 0 ) || ( carIdx >= cars.Length ) )
		{
			return false;
		}

		if ( markerSpacingInMeters <= 0f )
		{
			return false;
		}

		if ( float.IsNaN( lapPct ) || float.IsInfinity( lapPct ) )
		{
			return false;
		}

		var trackPositionInMeters = lapPct * trackLengthInMeters;

		markerIndex = (int) Math.Floor( trackPositionInMeters / markerSpacingInMeters );

		if ( markerIndex < 0 )
		{
			markerIndex = 0;
		}

		if ( markerIndex >= numMarkers )
		{
			markerIndex = numMarkers - 1;
		}

		var t = cars[ carIdx ].markerTiming[ markerIndex ];

		if ( t <= 0f )
		{
			return false;
		}

		time = t;

		return true;
	}

	public void Tick( App app )
	{
		if ( app.Simulator.SessionNum != lastSessionNum )
		{
			lastSessionNum = app.Simulator.SessionNum;

			Reset();
		}

		// guard against invalid marker spacing
		if ( markerSpacingInMeters <= 0f )
		{
			return;
		}

		for ( var carIdx = 0; carIdx < cars.Length; carIdx++ )
		{
			var car = cars[ carIdx ];

			// get lap percent for this car
			var carIdxLapDistPct = app.Simulator.CarIdxLapDistPct[ carIdx ];

			// ignore invalid values
			if ( float.IsNaN( carIdxLapDistPct ) || float.IsInfinity( carIdxLapDistPct ) )
			{
				continue;
			}

			// convert to meters
			var trackPositionInMeters = carIdxLapDistPct * trackLengthInMeters;

			// find the current marker index
			var currentMarkerIndex = (int) Math.Floor( trackPositionInMeters / markerSpacingInMeters );

			if ( currentMarkerIndex < 0 )
			{
				currentMarkerIndex = 0;
			}

			if ( currentMarkerIndex >= numMarkers )
			{
				currentMarkerIndex = numMarkers - 1;
			}

			// nothing to do if still on the same marker
			if ( currentMarkerIndex == car.lastMarkerIndex )
			{
				continue;
			}

			// count number of markers passed
			int markersPassed;

			if ( currentMarkerIndex > car.lastMarkerIndex )
			{
				markersPassed = currentMarkerIndex - car.lastMarkerIndex;
			}
			else
			{
				// wrapped around the lap
				markersPassed = currentMarkerIndex + numMarkers - car.lastMarkerIndex;
			}

			// get the time of the last marker passed
			var lastTime = car.markerTiming[ car.lastMarkerIndex ];

			if ( ( lastTime <= 0 ) || ( app.Simulator.SessionTime <= lastTime ) )
			{
				// unknown previous time or invalid, mark all passed markers with current time
				for ( var step = 1; step <= markersPassed; step++ )
				{
					var markerIndex = ( car.lastMarkerIndex + step ) % numMarkers;

					car.markerTiming[ markerIndex ] = app.Simulator.SessionTime;
				}
			}
			else
			{
				var deltaTime = app.Simulator.SessionTime - lastTime;

				var timeStepSize = deltaTime / markersPassed;

				for ( var step = 1; step <= markersPassed; step++ )
				{
					var markerIndex = ( car.lastMarkerIndex + step ) % numMarkers;

					car.markerTiming[ markerIndex ] = lastTime + timeStepSize * step;
				}
			}

			car.lastMarkerIndex = currentMarkerIndex;
		}
	}
}
