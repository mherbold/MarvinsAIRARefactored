using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace MarvinsAIRARefactored.Classes
{
    public static class MathHelper
    {
        private const float MPHMultiplyer = 2.23694f;

        public const float PIOVER2 = (float)Math.PI / 2f;
        public const float PIOVER4 = (float)Math.PI / 4f;
        public const float TWOPI = (float)Math.PI * 2f;

        private const float _invers180 = 1f / 180f;
        public const float _pi = (float)Math.PI;
        public const float _inversPI = 1 / (float)Math.PI;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(float a, float b)
        {
            return Math.Abs(a - b);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float QuadraticBezier(float start, float end, float mid, float t)
        {
            float u = 1 - t;
            return u * u * start + 2 * u * t * end + t * t * mid;
        }

       
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToRadians(float rad)
        {
            return rad * _pi * _invers180;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToDegress(float deg)
        {
            return deg * 180 * _inversPI;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToMPH(float iracingVelcity)
        {
            return iracingVelcity * MPHMultiplyer;
        }

    }
}
