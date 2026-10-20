using System;
using System.Collections.Generic;
using FortressUnited.Core.Maps;
using FortressUnited.Core.Movement;

namespace FortressUnited.Game
{
    /// <summary>
    /// Closes the hairline cracks between a compiled map's polygons. The BSP
    /// compiler splits surfaces into pieces whose corners can sit part-way
    /// along a neighbour's edge (a T-junction); drawn as they are, rounding
    /// leaves sub-pixel gaps along those edges that sparkle with whatever is
    /// behind (often bright sky). Each such corner is added to the edge it
    /// lies on, so neighbouring pieces share exactly the same points.
    /// </summary>
    public static class TJunctions
    {
        const float Cell = 64;            // spatial hash cell (map units)
        const float OnEdge = 0.05f;       // how close a point must be to an edge to join it (units)

        public static int Fix(BspFile bsp)
        {
            // Every face corner, hashed by position
            var grid = new Dictionary<(int, int, int), List<Vec3>>();
            foreach (var f in bsp.Faces)
                foreach (var p in f.Points)
                {
                    var key = Key(p);
                    if (!grid.TryGetValue(key, out var list))
                        grid[key] = list = new List<Vec3>();
                    list.Add(p);
                }

            int added = 0;
            var result = new List<Vec3>();
            var onEdge = new List<(float t, Vec3 p)>();
            foreach (var f in bsp.Faces)
            {
                var pts = f.Points;
                if (pts == null || pts.Length < 3)
                    continue;
                result.Clear();
                for (int i = 0; i < pts.Length; i++)
                {
                    Vec3 a = pts[i], b = pts[(i + 1) % pts.Length];
                    result.Add(a);
                    Vec3 ab = b - a;
                    float len2 = Vec3.Dot(ab, ab);
                    if (len2 < 1e-4f)
                        continue;
                    onEdge.Clear();
                    // Look in the cells the edge passes through
                    var lo = Key(new Vec3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)));
                    var hi = Key(new Vec3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));
                    for (int x = lo.Item1; x <= hi.Item1; x++)
                        for (int y = lo.Item2; y <= hi.Item2; y++)
                            for (int z = lo.Item3; z <= hi.Item3; z++)
                            {
                                if (!grid.TryGetValue((x, y, z), out var list))
                                    continue;
                                foreach (var p in list)
                                {
                                    float t = Vec3.Dot(p - a, ab) / len2;
                                    if (t <= 0.001f || t >= 0.999f)
                                        continue;
                                    Vec3 off = p - (a + ab * t);
                                    if (Vec3.Dot(off, off) > OnEdge * OnEdge)
                                        continue;
                                    onEdge.Add((t, a + ab * t));
                                }
                            }
                    if (onEdge.Count == 0)
                        continue;
                    onEdge.Sort((u, v) => u.t.CompareTo(v.t));
                    float last = -1;
                    foreach (var (t, p) in onEdge)
                    {
                        if (t - last < 1e-4f)
                            continue;      // the same point found twice
                        result.Add(p);
                        last = t;
                        added++;
                    }
                }
                if (result.Count != pts.Length)
                    f.Points = result.ToArray();
            }
            return added;
        }

        static (int, int, int) Key(Vec3 p) =>
            ((int)Math.Floor(p.X / Cell), (int)Math.Floor(p.Y / Cell), (int)Math.Floor(p.Z / Cell));
    }
}
