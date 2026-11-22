using System;

namespace Pitch.Core
{
    /// <summary>
    /// Engine independent 3D vector. The simulation never references UnityEngine so
    /// that it can be compiled and unit tested with plain dotnet on CI and on a
    /// developer machine without an editor. Axes follow the Unity convention:
    /// X right, Y up, Z forward, metres.
    /// </summary>
    public struct Vec3 : IEquatable<Vec3>
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static readonly Vec3 Up = new Vec3(0f, 1f, 0f);
        public static readonly Vec3 Forward = new Vec3(0f, 0f, 1f);
        public static readonly Vec3 Right = new Vec3(1f, 0f, 0f);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => a * s;
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        public float SqrLength => X * X + Y * Y + Z * Z;
        public float Length => MathF.Sqrt(SqrLength);

        public Vec3 Normalized
        {
            get
            {
                float len = Length;
                return len > 1e-6f ? this / len : Zero;
            }
        }

        /// <summary>Horizontal part of the vector (pitch plane).</summary>
        public Vec3 Flat => new Vec3(X, 0f, Z);
        public float FlatLength => MathF.Sqrt(X * X + Z * Z);

        public Vec3 WithY(float y) => new Vec3(X, y, Z);

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Length;
        public static float FlatDistance(Vec3 a, Vec3 b) => (a - b).FlatLength;

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Vec3(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t);
        }

        /// <summary>Rotate around the Y axis, radians, counter clockwise seen from above.</summary>
        public Vec3 Yaw(float radians)
        {
            float c = MathF.Cos(radians);
            float s = MathF.Sin(radians);
            return new Vec3(X * c + Z * s, Y, -X * s + Z * c);
        }

        public float YawDegrees => MathF.Atan2(X, Z) * (180f / MathF.PI);

        public static Vec3 FromYaw(float radians) => new Vec3(MathF.Sin(radians), 0f, MathF.Cos(radians));

        public static Vec3 ClampFlat(Vec3 v, float minX, float maxX, float minZ, float maxZ)
        {
            return new Vec3(
                Math.Clamp(v.X, minX, maxX),
                v.Y,
                Math.Clamp(v.Z, minZ, maxZ));
        }

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);
        public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);
        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
    }

    public static class PitchMath
    {
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float delta = target - current;
            if (MathF.Abs(delta) <= maxDelta) return target;
            return current + MathF.Sign(delta) * maxDelta;
        }

        public static Vec3 MoveTowards(Vec3 current, Vec3 target, float maxDelta)
        {
            Vec3 delta = target - current;
            float len = delta.Length;
            if (len <= maxDelta || len < 1e-6f) return target;
            return current + delta / len * maxDelta;
        }

        public static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float dt)
        {
            smoothTime = MathF.Max(0.0001f, smoothTime);
            float omega = 2f / smoothTime;
            float x = omega * dt;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = current - target;
            float temp = (velocity + omega * change) * dt;
            velocity = (velocity - omega * temp) * exp;
            return target + (change + temp) * exp;
        }

        /// <summary>Shortest signed angle from a to b in radians.</summary>
        public static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % (MathF.PI * 2f);
            if (d > MathF.PI) d -= MathF.PI * 2f;
            if (d < -MathF.PI) d += MathF.PI * 2f;
            return d;
        }

        public static float AngleLerp(float a, float b, float t) => a + DeltaAngle(a, b) * Clamp01(t);

        /// <summary>
        /// Distance from point p to segment ab in the pitch plane.
        /// </summary>
        public static float FlatSegmentDistance(Vec3 p, Vec3 a, Vec3 b)
        {
            Vec3 ab = (b - a).Flat;
            float len2 = ab.SqrLength;
            if (len2 < 1e-6f) return (p - a).FlatLength;
            float t = Clamp01(Vec3.Dot((p - a).Flat, ab) / len2);
            return (p - (a + ab * t)).FlatLength;
        }

        /// <summary>True when the point is inside an axis aligned rectangle in the pitch plane.</summary>
        public static bool InRect(Vec3 p, float minX, float maxX, float minZ, float maxZ)
            => p.X >= minX && p.X <= maxX && p.Z >= minZ && p.Z <= maxZ;

        public static float InverseLerp(float a, float b, float v)
            => MathF.Abs(b - a) < 1e-6f ? 0f : Clamp01((v - a) / (b - a));

        public static float Remap(float v, float inMin, float inMax, float outMin, float outMax)
            => outMin + (outMax - outMin) * InverseLerp(inMin, inMax, v);

        public static Vec3 Reflect(Vec3 v, Vec3 normal)
            => v - normal * (2f * Vec3.Dot(v, normal));

        /// <summary>Project v onto the plane with the given normal.</summary>
        public static Vec3 ProjectOnPlane(Vec3 v, Vec3 normal)
        {
            Vec3 n = normal.Normalized;
            return v - n * Vec3.Dot(v, n);
        }
    }
}
