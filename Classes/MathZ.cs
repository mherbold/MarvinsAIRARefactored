
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
			return 0f;
		}

		return ( t - start ) / d;
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
	public static float Compression( float value, float rate, float threshold, float width, bool limiter )
	{
		var valueAbs = MathF.Abs( value );
		var halfWidth = width / 2f;

		if ( ( valueAbs <= ( threshold - halfWidth ) ) && ( !limiter ) )
		{
			return value;
		}
		else if ( ( valueAbs < ( threshold + halfWidth ) ) || limiter )
		{
			var compressedDelta = 0f;

			if ( !( limiter && ( valueAbs <= threshold ) ) )
			{
				compressedDelta = rate / 2f * ( valueAbs - threshold + halfWidth - ( width / MathF.PI * MathF.Sin( MathF.PI * ( valueAbs - threshold + halfWidth ) / width ) ) );

				if ( limiter )
				{
					compressedDelta = compressedDelta - 1f + threshold;
				}
			}

			return ( valueAbs - compressedDelta ) * MathF.Sign( value );
		}
		else
		{
			return ( threshold + ( valueAbs - threshold ) * ( 1f - rate ) ) * MathF.Sign( value );
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float SoftLimiter( float value, float limit )
	{
		var limiterRate = 1f;
		var limiterWidth = 1.13333f;
		var limiterThreshold = 1f - 0.5f * ( limiterWidth / 2f - limiterWidth / MathF.PI );
		var normalizedValue = value / limit;

		return Compression( normalizedValue, limiterRate, limiterThreshold, limiterWidth, true ) * limit;
	}
}
