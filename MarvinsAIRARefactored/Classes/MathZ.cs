
using System.Runtime.CompilerServices;

namespace MarvinsAIRARefactored.Classes;

public class MathZ
{
	public const float KPHToMPS = 5f / 18f;
	public const float MPSToKPH = 18f / 5f;

	public const float MPSToMPH = 2.23693629f;
	public const float MPHToMPS = 0.44704f;

	public const float RadiansToDegrees = 180f / MathF.PI;
	public const float DegreesToRadians = MathF.PI / 180f;

	public const float OneG = 9.80665f; // in meters per second squared
	public const float OneOverG = 1f / OneG;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float Saturate( float value ) => Math.Clamp( value, 0f, 1f );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float Lerp( float start, float end, float t )
	{
		return start + ( end - start ) * Saturate( t );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float InverseLerp( float start, float end, float t )
	{
		var d = end - start;

		if ( MathF.Abs( d ) < 1e-6f )
		{
			return ( t < start ) ? 0f : 1f;
		}
		else
		{
			return Saturate( ( t - start ) / d );
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float CurveToPower( float curve )
	{
		if ( curve >= 0f )
		{
			return 1f + curve * 4f;
		}
		else
		{
			return 1f + curve * 0.75f;
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float InterpolateHermite( float v0, float v1, float v2, float v3, float t )
	{
		var a = 2.0f * v1;
		var b = v2 - v0;
		var c = 2.0f * v0 - 5.0f * v1 + 4.0f * v2 - v3;
		var d = -v0 + 3.0f * v1 - 3.0f * v2 + v3;

		return 0.5f * ( a + b * t + c * t * t + d * t * t * t );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float Smoothstep( float start, float end, float t )
	{
		t = Saturate( ( t - start ) / ( end - start ) );

		return t * t * ( 3f - 2f * t );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float Compression( float value, float rate, float threshold, float width )
	{
		var absValue = MathF.Abs( value );

		if ( width <= 0f )
		{
			// Region 0: hard-knee fallback

			var mag = absValue <= threshold ? absValue : threshold + ( absValue - threshold ) * ( 1f - rate );

			return MathF.CopySign( mag, value );
		}
		else
		{
			var halfWidth = 0.5f * width;

			if ( absValue <= threshold - halfWidth )
			{
				// Region 1: pass-through (below the knee start)

				return value;
			}
			else if ( absValue < threshold + halfWidth )
			{
				// Region 2: soft knee (sine-eased)

				var t = absValue - threshold + halfWidth;

				var delta = 0.5f * rate * ( t - ( width / MathF.PI ) * MathF.Sin( MathF.PI * t / width ) );

				var magnitude = absValue - delta;

				return MathF.CopySign( magnitude, value );
			}
			else
			{
				// Region 3: above the knee (linear compression)

				var mag3 = threshold + ( absValue - threshold ) * ( 1f - rate );

				return MathF.CopySign( mag3, value );
			}
		}
	}

	private const float SoftLimiterWidth = 1.13333f;

	private static readonly float SoftLimiterHalfWidth = SoftLimiterWidth * 0.5f;
	private static readonly float SoftLimiterThreshold = 1f - 0.5f * ( SoftLimiterHalfWidth - SoftLimiterWidth / MathF.PI );
	private static readonly float SoftLimiterWidthOverPi = SoftLimiterWidth / MathF.PI;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float SoftLimiter( float value )
	{
		var absValue = MathF.Abs( value );

		if ( absValue <= SoftLimiterThreshold )
		{
			return value;
		}

		var magnitude = 1f + 0.5f * ( absValue - SoftLimiterThreshold - SoftLimiterHalfWidth ) + 0.5f * SoftLimiterWidthOverPi * MathF.Sin( MathF.PI * ( absValue - SoftLimiterThreshold + SoftLimiterHalfWidth ) / SoftLimiterWidth );

		return MathF.CopySign( magnitude, value );
	}
}
