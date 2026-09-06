
using Point = System.Windows.Point;

namespace MarvinsAIRARefactored.Classes;

/// <summary>
/// Builds the world-frame path of a recorded segment by rotating each sample's car-relative velocity vector
/// into the world frame (by YawNorth) and integrating it over the sample interval. Conventions — validated
/// empirically against a Summit Point recording (integrated path length matched the LapDist delta to 0.3%,
/// and the cumulative yaw direction matched the track's clockwise layout): iRacing's YawNorth is a
/// compass-style heading (0 = north, clockwise-positive) and VelocityY is positive toward the car's LEFT.
/// The returned points are in meters with x = east and y = SOUTH (i.e. north is already flipped so the
/// points can be drawn directly in screen space, north up).
/// </summary>
public static class TrackMap
{
	private const float SampleDeltaSeconds = 1f / 360f;

	/// <summary>
	/// One point per recorded sample, meters, x = east / y = south (screen orientation, north up).
	/// The path starts at the origin — the display centers it wherever it likes.
	/// </summary>
	public static Point[] BuildPath( List<RecordingData> recordingDataList )
	{
		var path = new Point[ recordingDataList.Count ];

		var east = 0.0;
		var south = 0.0;

		for ( var i = 0; i < recordingDataList.Count; i++ )
		{
			var recordingData = recordingDataList[ i ];

			var ( sinYaw, cosYaw ) = MathF.SinCos( recordingData.YawNorth );

			// forward = (E: sin yaw, N: cos yaw); car-left = (E: -cos yaw, N: sin yaw)
			var velocityEast = recordingData.VelocityX * sinYaw - recordingData.VelocityY * cosYaw;
			var velocityNorth = recordingData.VelocityX * cosYaw + recordingData.VelocityY * sinYaw;

			east += velocityEast * SampleDeltaSeconds;
			south -= velocityNorth * SampleDeltaSeconds;

			path[ i ] = new Point( east, south );
		}

		return path;
	}
}
