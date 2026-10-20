using FortressUnited.Core.Movement;

namespace FortressUnited.Core.Maps
{
    /// <summary>
    /// Traces against a BSP's collision hulls, the way the GoldSrc engine
    /// does it: walk the hull's node tree, split the segment at each plane,
    /// stop where it first enters solid, and back off by a small epsilon.
    /// Using the map's own precomputed hulls gives players exactly the
    /// collision they had in TFC.
    /// </summary>
    public sealed class BspCollision
    {
        const float DistEpsilon = 0.03125f;

        readonly BspFile bsp;

        public BspCollision(BspFile bsp) { this.bsp = bsp; }

        /// <summary>GoldSrc hull sizes: 0 point, 1 standing, 2 large, 3 crouched.</summary>
        public static readonly Vec3[] HullMins =
        {
            Vec3.Zero, new Vec3(-16, -16, -36), new Vec3(-32, -32, -32), new Vec3(-16, -16, -18),
        };
        public static readonly Vec3[] HullMaxs =
        {
            Vec3.Zero, new Vec3(16, 16, 36), new Vec3(32, 32, 32), new Vec3(16, 16, 18),
        };

        /// <summary>The engine hull for a player's movement hull (standing, ducked, point, large).</summary>
        public static int EngineHull(int useHull)
        {
            switch (useHull)
            {
                case Hulls.Standing: return 1;
                case Hulls.Ducked: return 3;
                case Hulls.Point: return 0;
                default: return 2;
            }
        }

        BspFile.HullNode[] Nodes(int hull) => hull == 0 ? bsp.PointNodes : bsp.ClipNodes;

        /// <summary>Contents at a point in one hull of one model (point is in model space).</summary>
        public int PointContents(int model, int hull, Vec3 p)
        {
            var nodes = Nodes(hull);
            int num = bsp.Models[model].HeadNode[hull];
            while (num >= 0)
            {
                if (num >= nodes.Length)
                    return Contents.Solid; // corrupt tree: treat as solid
                var node = nodes[num];
                var plane = bsp.Planes[node.Plane];
                float d = plane.Type < 3
                    ? p[plane.Type] - plane.Dist
                    : Vec3.Dot(plane.Normal, p) - plane.Dist;
                num = d < 0 ? node.Back : node.Front;
            }
            return num;
        }

        /// <summary>
        /// Sweeps a hull from start to end through one model (positions in
        /// model space). The result's Ent is left for the caller to fill.
        /// </summary>
        public MoveTrace Trace(int model, int hull, Vec3 start, Vec3 end)
        {
            var tr = new MoveTrace { Fraction = 1, AllSolid = true, EndPos = end, Ent = -1 };
            RecursiveCheck(model, hull, bsp.Models[model].HeadNode[hull], 0, 1, start, end, ref tr);

            if (tr.AllSolid)
                tr.StartSolid = true;
            if (tr.StartSolid)
                tr.Fraction = 0;
            if (tr.Fraction == 1)
                tr.EndPos = end;
            return tr;
        }

        /// <summary>Returns false once the segment is blocked.</summary>
        bool RecursiveCheck(int model, int hull, int num, float p1f, float p2f, Vec3 p1, Vec3 p2, ref MoveTrace tr)
        {
            // Reached contents
            if (num < 0)
            {
                if (num != Contents.Solid)
                {
                    tr.AllSolid = false;
                    if (num == Contents.Empty)
                        tr.InOpen = true;
                    else
                        tr.InWater = true;
                }
                else
                {
                    tr.StartSolid = true;
                }
                return true;
            }

            var nodes = Nodes(hull);
            if (num >= nodes.Length)
                return true;
            var node = nodes[num];
            var plane = bsp.Planes[node.Plane];

            float t1, t2;
            if (plane.Type < 3)
            {
                t1 = p1[plane.Type] - plane.Dist;
                t2 = p2[plane.Type] - plane.Dist;
            }
            else
            {
                t1 = Vec3.Dot(plane.Normal, p1) - plane.Dist;
                t2 = Vec3.Dot(plane.Normal, p2) - plane.Dist;
            }

            // Entirely on one side
            if (t1 >= 0 && t2 >= 0)
                return RecursiveCheck(model, hull, node.Front, p1f, p2f, p1, p2, ref tr);
            if (t1 < 0 && t2 < 0)
                return RecursiveCheck(model, hull, node.Back, p1f, p2f, p1, p2, ref tr);

            // Split at the plane, a little on the near side
            float frac = t1 < 0
                ? (t1 + DistEpsilon) / (t1 - t2)
                : (t1 - DistEpsilon) / (t1 - t2);
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;

            float midf = p1f + (p2f - p1f) * frac;
            Vec3 mid = p1 + (p2 - p1) * frac;

            bool backSide = t1 < 0;
            int near = backSide ? node.Back : node.Front;
            int far = backSide ? node.Front : node.Back;

            // Move up to the plane on the near side
            if (!RecursiveCheck(model, hull, near, p1f, midf, p1, mid, ref tr))
                return false;

            // Free on the far side: continue there
            if (ContentsFrom(hull, far, mid) != Contents.Solid)
                return RecursiveCheck(model, hull, far, midf, p2f, mid, p2, ref tr);

            // Never got out of solid
            if (tr.AllSolid)
                return false;

            // The far side is solid: this plane is the impact
            if (!backSide)
            {
                tr.PlaneNormal = plane.Normal;
                tr.PlaneDist = plane.Dist;
            }
            else
            {
                tr.PlaneNormal = -plane.Normal;
                tr.PlaneDist = -plane.Dist;
            }

            // Back off until the point is outside solid
            int root = bsp.Models[model].HeadNode[hull];
            while (ContentsFrom(hull, root, mid) == Contents.Solid)
            {
                frac -= 0.1f;
                if (frac < 0)
                {
                    tr.Fraction = midf;
                    tr.EndPos = mid;
                    return false;
                }
                midf = p1f + (p2f - p1f) * frac;
                mid = p1 + (p2 - p1) * frac;
            }

            tr.Fraction = midf;
            tr.EndPos = mid;
            return false;
        }

        int ContentsFrom(int hull, int num, Vec3 p)
        {
            var nodes = Nodes(hull);
            while (num >= 0)
            {
                if (num >= nodes.Length)
                    return Contents.Solid;
                var node = nodes[num];
                var plane = bsp.Planes[node.Plane];
                float d = plane.Type < 3
                    ? p[plane.Type] - plane.Dist
                    : Vec3.Dot(plane.Normal, p) - plane.Dist;
                num = d < 0 ? node.Back : node.Front;
            }
            return num;
        }
    }
}
