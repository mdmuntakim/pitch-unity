using Pitch.Core;
using UnityEngine;

namespace Pitch.Game
{
    /// <summary>Conversions between the engine independent core and Unity types.</summary>
    public static class Conversions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Vec3 ToCore(this Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Quaternion ToUnityYaw(this float radians) => Quaternion.Euler(0f, radians * Mathf.Rad2Deg, 0f);
        public static Color ToColor(this Vec3 v) => new Color(v.X, v.Y, v.Z, 1f);
    }

    public static class Layers
    {
        public const int Default = 0;
        public const int Pitch = 8;
        public const int Players = 9;
    }
}
