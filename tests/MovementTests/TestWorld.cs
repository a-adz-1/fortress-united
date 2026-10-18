using System;
using System.Collections.Generic;
using FortressUnited.Core.Movement;

namespace FortressUnited.Tests
{
    /// <summary>
    /// The scenario brush world. Must stay identical, operation for
    /// operation, to the reference harness in tfc-decomp
    /// (tools/movetest/movetest.cpp), which is built with SSE floats so both
    /// sides compute the same bits. Any difference in traces is then down to
    /// the movement code itself.
    /// </summary>
    sealed class TestWorld : IMovementWorld
    {
        public enum Kind { Solid, Water, Ladder }

        public struct Plane { public float NX, NY, NZ, D; }

        public sealed class Brush
        {
            public Kind Contents;
            public List<Plane> Planes = new List<Plane>();
            public Vec3 Mins, Maxs;
        }

        const float DistEpsilon = 0.03125f;

        public readonly List<Brush> Brushes = new List<Brush>();
        public readonly List<Brush> Ladders = new List<Brush>();
        public double CurrentTime;
        uint seed = 12345;

        public static Brush Box(Kind contents, Vec3 mins, Vec3 maxs)
        {
            var b = new Brush { Contents = contents, Mins = mins, Maxs = maxs };
            for (int i = 0; i < 3; i++)
            {
                var p = new Plane();
                Set(ref p, i, 1); p.D = maxs[i];
                b.Planes.Add(p);
                var q = new Plane();
                Set(ref q, i, -1); q.D = -mins[i];
                b.Planes.Add(q);
            }
            return b;
        }

        static void Set(ref Plane p, int axis, float v)
        {
            if (axis == 0) p.NX = v; else if (axis == 1) p.NY = v; else p.NZ = v;
        }

        struct Trace
        {
            public bool AllSolid, StartSolid;
            public float Fraction;
            public Vec3 EndPos, Normal;
            public float Dist;
            public int Ent;
        }

        static void TraceBrush(Brush b, Vec3 start, Vec3 end, Vec3 mins, Vec3 maxs, ref Trace tr)
        {
            float enterFrac = -1, leaveFrac = 1;
            bool startout = false, getout = false;
            bool haveClip = false;
            Plane clip = default;
            float clipDist = 0;

            foreach (var p in b.Planes)
            {
                // push the plane out by the box's support point
                float ofs0 = p.NX < 0 ? maxs.X : mins.X;
                float ofs1 = p.NY < 0 ? maxs.Y : mins.Y;
                float ofs2 = p.NZ < 0 ? maxs.Z : mins.Z;
                float dist = p.D - (ofs0 * p.NX + ofs1 * p.NY + ofs2 * p.NZ);
                float d1 = start.X * p.NX + start.Y * p.NY + start.Z * p.NZ - dist;
                float d2 = end.X * p.NX + end.Y * p.NY + end.Z * p.NZ - dist;

                if (d2 > 0) getout = true;
                if (d1 > 0) startout = true;

                if (d1 > 0 && (d2 >= DistEpsilon || d2 >= d1))
                    return;
                if (d1 <= 0 && d2 <= 0)
                    continue;

                if (d1 > d2)
                {
                    float f = (d1 - DistEpsilon) / (d1 - d2);
                    if (f < 0) f = 0;
                    if (f > enterFrac) { enterFrac = f; clip = p; clipDist = dist; haveClip = true; }
                }
                else
                {
                    float f = (d1 + DistEpsilon) / (d1 - d2);
                    if (f > 1) f = 1;
                    if (f < leaveFrac) leaveFrac = f;
                }
            }

            if (!startout)
            {
                tr.StartSolid = true;
                tr.Ent = 0;
                if (!getout)
                {
                    tr.AllSolid = true;
                    tr.Fraction = 0;
                }
                return;
            }

            if (enterFrac < leaveFrac && enterFrac > -1 && enterFrac < tr.Fraction && haveClip)
            {
                if (enterFrac < 0) enterFrac = 0;
                tr.Fraction = enterFrac;
                tr.Normal = new Vec3(clip.NX, clip.NY, clip.NZ);
                tr.Dist = clipDist;
                tr.Ent = 0;
            }
        }

        Trace TraceWorld(Vec3 start, Vec3 end, Vec3 mins, Vec3 maxs, Kind contents)
        {
            var tr = new Trace { Fraction = 1, Ent = -1 };
            foreach (var b in Brushes)
                if (b.Contents == contents)
                    TraceBrush(b, start, end, mins, maxs, ref tr);
            tr.EndPos = new Vec3(
                start.X + tr.Fraction * (end.X - start.X),
                start.Y + tr.Fraction * (end.Y - start.Y),
                start.Z + tr.Fraction * (end.Z - start.Z));
            return tr;
        }

        static bool InsideBrush(Brush b, Vec3 p)
        {
            foreach (var pl in b.Planes)
                if (p.X * pl.NX + p.Y * pl.NY + p.Z * pl.NZ - pl.D > 0)
                    return false;
            return true;
        }

        static MoveTrace ToMove(Trace t) => new MoveTrace
        {
            AllSolid = t.AllSolid,
            StartSolid = t.StartSolid,
            InOpen = t.Fraction == 1 && !t.StartSolid,
            Fraction = t.Fraction,
            EndPos = t.EndPos,
            PlaneNormal = t.Normal,
            PlaneDist = t.Dist,
            Ent = t.Ent,
        };

        public MoveTrace PlayerTrace(MoveState s, Vec3 start, Vec3 end, int ignoreEnt) =>
            ToMove(TraceWorld(start, end, s.PlayerMins[s.UseHull], s.PlayerMaxs[s.UseHull], Kind.Solid));

        public int PointContents(Vec3 p, out int trueContents)
        {
            int c = Contents.Empty;
            foreach (var b in Brushes)
                if (b.Contents == Kind.Solid && InsideBrush(b, p)) { c = Contents.Solid; break; }
            if (c == Contents.Empty)
                foreach (var b in Brushes)
                    if (b.Contents == Kind.Water && InsideBrush(b, p)) { c = Contents.Water; break; }
            trueContents = c;
            return c;
        }

        public int TestPlayerPosition(MoveState s, Vec3 pos) =>
            TraceWorld(pos, pos, s.PlayerMins[s.UseHull], s.PlayerMaxs[s.UseHull], Kind.Solid).StartSolid ? 0 : -1;

        public bool IsPlayer(int ent) => false;
        public bool IsBrushModel(int ent) => ent == 0;

        sealed class Ladder : ILadder
        {
            public Brush B;
            public Vec3 Mins => B.Mins;
            public Vec3 Maxs => B.Maxs;
            public MoveTrace Trace(Vec3 start, Vec3 end)
            {
                var t = new Trace { Fraction = 1, Ent = -1 };
                TraceBrush(B, start, end, Vec3.Zero, Vec3.Zero, ref t);
                t.EndPos = new Vec3(
                    start.X + t.Fraction * (end.X - start.X),
                    start.Y + t.Fraction * (end.Y - start.Y),
                    start.Z + t.Fraction * (end.Z - start.Z));
                return ToMove(t);
            }
        }

        public ILadder FindLadder(MoveState s)
        {
            Vec3 hmin = s.PlayerMins[s.UseHull], hmax = s.PlayerMaxs[s.UseHull];
            Vec3 p = s.Origin;
            foreach (var l in Ladders)
            {
                bool inside = true;
                for (int k = 0; k < 3 && inside; k++)
                    inside = p[k] > l.Mins[k] - hmax[k] && p[k] < l.Maxs[k] - hmin[k];
                if (inside)
                    return new Ladder { B = l };
            }
            return null;
        }

        public double Time => CurrentTime;

        public int RandomLong(int low, int high)
        {
            seed = seed * 1103515245u + 12345u;
            uint range = (uint)(high - low + 1);
            return low + (int)((seed >> 16) % range);
        }

        public void PlaySound(MoveState state, string sample, float volume) { }
        public void StuckTouch(MoveState state, int ent) { }
    }
}
