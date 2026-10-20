using FortressUnited.Core.Movement;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// Converts between game space (TFC's: inches, x forward, y left, z up,
    /// angles pitch/yaw/roll in degrees with yaw counter-clockwise) and
    /// Unity space (metres, x right, y up, z forward, yaw clockwise).
    /// Gameplay runs in game space so movement matches TFC exactly; only
    /// rendering uses Unity space.
    /// </summary>
    public static class GoldSrcSpace
    {
        /// <summary>One game unit is one inch.</summary>
        public const float Scale = 0.0254f;

        public static Vector3 ToUnity(Vec3 v) => new Vector3(-v.Y, v.Z, v.X) * Scale;

        public static Vec3 FromUnity(Vector3 v)
        {
            v /= Scale;
            return new Vec3(v.z, -v.x, v.y);
        }

        /// <summary>Direction only (no scaling).</summary>
        public static Vector3 DirectionToUnity(Vec3 v) => new Vector3(-v.Y, v.Z, v.X);

        /// <summary>Unity rotation for game pitch/yaw/roll angles.</summary>
        public static Quaternion AnglesToUnity(Vec3 angles) =>
            Quaternion.Euler(angles.X, -angles.Y, -angles.Z);
    }
}
