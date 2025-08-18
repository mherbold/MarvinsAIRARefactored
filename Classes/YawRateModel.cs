
using System.IO;

namespace MarvinsAIRARefactored.Classes;

using MarvinsAIRARefactored.Components;

public class YawRateModel( int[] steeringWheelAnglesInDegrees, float[,] yawRateDataInDegrees, int maxSpeedInKPH )
{
	private const int MinStartingSpeedInKPH = 150;
	private const float MaxSpeedInterpolationErrorInKPH = 6.5f;

	private readonly int[] _steeringWheelAnglesInDegrees = steeringWheelAnglesInDegrees;
	private readonly float[,] _yawRateDataInDegrees = yawRateDataInDegrees;
	private readonly int _maxSpeedInKPH = maxSpeedInKPH;

	public (Func<float, float> yawRateInterpolator, Func<float, float> speedInterpolator, int shallowestSteeringWheelAngle) FitWithProgressiveRefinement()
	{
		var usedAngles = new List<float>();
		var usedMaxYawRates = new List<float>();
		var usedSpeeds = new List<float>();

		var initialAngles = GetSortedAngles().Where( a => Math.Abs( a ) >= MinStartingSpeedInKPH ).ToList();

		foreach ( var angle in initialAngles )
		{
			var (maxYawRate, speed) = GetMaxYawRateAtAngle( angle );

			if ( speed >= 0 )
			{
				usedAngles.Add( angle );
				usedMaxYawRates.Add( maxYawRate );
				usedSpeeds.Add( speed );
			}
		}

		var remainingAngles = GetSortedAngles().Where( a => Math.Abs( a ) < MinStartingSpeedInKPH ).ToList();

		foreach ( var angle in remainingAngles )
		{
			var speedInterpolator = FitSpline( [ .. usedAngles ], [ .. usedSpeeds ] );
			var expectedSpeed = speedInterpolator( angle );
			var candidates = FindPeaksAtAngle( angle );

			if ( candidates.Count > 0 )
			{
				var bestCandidateIndex = 0;
				var bestCandidateError = float.MaxValue;

				for ( var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++ )
				{
					var (candidateMaxYawRate, candidateSpeed) = candidates[ candidateIndex ];

					var candidateError = MathF.Abs( candidateSpeed - expectedSpeed );

					if ( candidateError < bestCandidateError )
					{
						bestCandidateError = candidateError;
						bestCandidateIndex = candidateIndex;
					}
				}

				if ( bestCandidateError <= MaxSpeedInterpolationErrorInKPH )
				{
					var (candidateMaxYawRate, candidateSpeed) = candidates[ bestCandidateIndex ];

					usedAngles.Add( angle );
					usedMaxYawRates.Add( candidateMaxYawRate );
					usedSpeeds.Add( candidateSpeed );
				}
			}
		}

		var finalYawRateInterpolator = FitSpline( [ .. usedAngles ], [ .. usedMaxYawRates ] );
		var finalSpeedInterpolator = FitSpline( [ .. usedAngles ], [ .. usedSpeeds ] );

		// write to debug file

		var filePath = Path.Combine( SteeringEffects.CalibrationDirectory, $"debug_source_max_yaw_rates.csv" );

		using var writer = new StreamWriter( filePath );

		writer.WriteLine( "Steering Wheel Angle,Max Yaw Rate,Speed" );

		for ( var i = 0; i < usedAngles.Count; i++ )
		{
			writer.WriteLine( $"{usedAngles[ i ]:F0},{usedMaxYawRates[ i ]:F6},{usedSpeeds[ i ]:F0}" );
		}

		return (finalYawRateInterpolator, finalSpeedInterpolator, (int) usedAngles.Last());
	}

	private List<int> GetSortedAngles()
	{
		return [ .. _steeringWheelAnglesInDegrees.Where( angle => angle < 0 ).OrderBy( angle => Math.Abs( angle ) ).Reverse() ];
	}

	private (float yaw, int speed) GetMaxYawRateAtAngle( int angle )
	{
		var angleIndex = Array.IndexOf( _steeringWheelAnglesInDegrees, angle );

		if ( angleIndex < 0 )
		{
			return (0f, -1);
		}

		var maxYaw = float.MinValue;
		var speedAtMax = -1;

		for ( var speed = 0; speed <= _maxSpeedInKPH; speed++ )
		{
			var yaw = _yawRateDataInDegrees[ angleIndex, speed ];

			if ( yaw > maxYaw )
			{
				maxYaw = yaw;
				speedAtMax = speed;
			}
		}

		return (maxYaw, speedAtMax);
	}

	private List<(float, float)> FindPeaksAtAngle( int angle )
	{
		var peaks = new List<(float, float)>();
		var angleIndex = Array.IndexOf( _steeringWheelAnglesInDegrees, angle );

		if ( angleIndex < 0 )
		{
			return peaks;
		}

		for ( var speedInKPH = 1; speedInKPH < _maxSpeedInKPH - 1; speedInKPH++ )
		{
			var previousYawRate = _yawRateDataInDegrees[ angleIndex, speedInKPH - 1 ];
			var currentYawRate = _yawRateDataInDegrees[ angleIndex, speedInKPH ];
			var nextYawRate = _yawRateDataInDegrees[ angleIndex, speedInKPH + 1 ];

			if ( ( currentYawRate > previousYawRate ) && ( currentYawRate > nextYawRate ) )
			{
				peaks.Add( (currentYawRate, speedInKPH) );
			}
		}

		return peaks;
	}

	public static Func<float, float> FitSpline( float[] x, float[] y )
	{
		if ( x.Length < 3 )
		{
			throw new ArgumentException( "Need at least three points for cubic spline." );
		}

		var spline = new CubicSpline( x, y );

		return spline.Interpolate;
	}
}
