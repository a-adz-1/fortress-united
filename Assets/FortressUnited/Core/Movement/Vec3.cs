using System;

namespace FortressUnited.Core.Movement
{
    /// <summary>
    /// A float 3-vector in TFC's coordinate convention (x forward, y left,
    /// z up; angles in degrees as pitch, yaw, roll). Engine-free so the
    /// movement code can run outside Unity; the Unity layer converts to
    /// UnityEngine.Vector3 at the boundary.
    /// </summary>
    [Serializable]
    public struct Vec3 : IEquatable<Vec3>
    {
        public float X, Y, Z;

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);

        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public float this[int i]
        {
            get => i == 0 ? X : (i == 1 ? Y : Z);
            set { if (i == 0) X = value; else if (i == 1) Y = value; else Z = value; }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        /// <summary>a + b * scale (VectorMA).</summary>
        public static Vec3 MA(Vec3 a, float scale, Vec3 b) => new Vec3(a.X + scale * b.X, a.Y + scale * b.Y, a.Z + scale * b.Z);

        public float Length() => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

        /// <summary>Normalizes in place and returns the old length (VectorNormalize).</summary>
        public float Normalize()
        {
            float length = (float)Math.Sqrt(X * X + Y * Y + Z * Z);
            if (length != 0)
            {
                float ilength = 1 / length;
                X *= ilength; Y *= ilength; Z *= ilength;
            }
            return length;
        }

        public bool IsZero => X == 0 && Y == 0 && Z == 0;

        public bool Equals(Vec3 o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object obj) => obj is Vec3 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:0.###} {Y:0.###} {Z:0.###})";

        /// <summary>Forward, right and up vectors for pitch/yaw/roll angles in degrees (AngleVectors).</summary>
        public static void AngleVectors(Vec3 angles, out Vec3 forward, out Vec3 right, out Vec3 up)
        {
            const double degToRad = Math.PI * 2 / 360;
            float angle = (float)(angles.Y * degToRad);
            float sy = (float)Math.Sin(angle), cy = (float)Math.Cos(angle);
            angle = (float)(angles.X * degToRad);
            float sp = (float)Math.Sin(angle), cp = (float)Math.Cos(angle);
            angle = (float)(angles.Z * degToRad);
            float sr = (float)Math.Sin(angle), cr = (float)Math.Cos(angle);

            forward = new Vec3(cp * cy, cp * sy, -sp);
            right = new Vec3(-1 * sr * sp * cy + -1 * cr * -sy, -1 * sr * sp * sy + -1 * cr * cy, -1 * sr * cp);
            up = new Vec3(cr * sp * cy + -sr * -sy, cr * sp * sy + -sr * cy, cr * cp);
        }
    }
}
