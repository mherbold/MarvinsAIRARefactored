using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarvinsAIRARefactored.Classes
{
    public struct Vector2
    {
        public float X; public float Y;

        public Vector2(float value)
        {
            this.X = value;
            this.Y = value;
        }

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public double Angle()
        {
            return Math.Atan2(Y, X);
        }

        public void Normalize()
        {
            float length = (float)Math.Sqrt(X * X + Y * Y);
            if (length == 0) {
                X = 0;
                Y = 0;
            }
            X /= length;
            Y /= length;

        }

        public readonly static Vector2 Zero = new Vector2(0);

        public static bool operator ==(Vector2 left, Vector2 right)
        {
            return left.X == right.X && left.Y == right.Y;
        }

        public static bool operator !=(Vector2 left, Vector2 right)
        {
            return left.X != right.X || left.Y != right.Y;
        }

        public static Vector2 operator -(Vector2 left, Vector2 right)
        {
            return new Vector2(left.X - right.X, left.Y - right.Y);
        }

        public static Vector2 operator +(Vector2 left, Vector2 right)
        {
            return new Vector2(left.X + right.X, left.Y + right.Y);
        }
    }
}
