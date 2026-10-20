using System.Collections.Generic;
using FortressUnited.Core.Maps;
using UnityEngine;
using UnityEngine.Rendering;

namespace FortressUnited.Game
{
    /// <summary>
    /// Structural detail dressed onto any map from its own geometry, so the
    /// blocky brushwork reads as a built sci-fi base:
    /// <list type="bullet">
    /// <item>ribs: arched metal frames every 3 m along corridors (walls both sides,
    /// a ceiling, open both ways), each with a light strip</item>
    /// <item>railings: floor edges with a real drop get a glowing lip; under a roof also a
    /// light rail on posts (visual only, so ledges players jump off still work as in
    /// TFC); open-air ledges like balconies keep just the lip</item>
    /// <item>door frames: chunky frames round every door with glowing inner edges</item>
    /// </list>
    /// Nothing here collides: movement is exactly as the map was built. Glow
    /// strips take the team's colour inside a base.
    /// </summary>
    public static class MapDetail
    {
        const float RibSpacing = 3f;
        static Mesh cube;

        sealed class Batch
        {
            public readonly List<CombineInstance> Parts = new List<CombineInstance>();
            public void Box(Vector3 centre, Quaternion rotation, Vector3 size) =>
                Parts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(centre, rotation, size) });
        }

        // Metal for the structure; glow per team (index 0-3) and for neutral areas (4)
        static Batch metal;
        static Batch[] glow;

        public static void Build(MapLoader map)
        {
            cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            metal = new Batch();
            glow = new Batch[5];
            for (int i = 0; i < 5; i++) glow[i] = new Batch();

            int ribs = Ribs(map), rails = Railings(map), doors = DoorFrames(map);

            var root = new GameObject("Structural Detail").transform;
            root.SetParent(map.transform, false);
            var metalMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Detail Metal" };
            metalMat.SetColor("_BaseColor", new Color(0.16f, 0.17f, 0.19f));
            // Satin, not polished: thin frame and rail faces caught sharp highlights that
            // shimmered as the view moved
            metalMat.SetFloat("_Metallic", 0.35f);
            metalMat.SetFloat("_Smoothness", 0.38f);
            Emit(root, "Metal", metal, metalMat, true);
            for (int i = 0; i < 5; i++)
            {
                Color c = i < 4 ? TeamColours.Glow(TeamColours.For((TeamColours.Team)(i + 1)), 2.2f) : new Color(1.4f, 1.9f, 2.4f);
                var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Detail Glow" };
                m.SetColor("_BaseColor", c);
                Emit(root, "Glow", glow[i], m, false);
            }
            Debug.Log($"Fortress United: detail: {ribs} ribs, {rails} rail runs, {doors} door frames");
        }

        static void Emit(Transform root, string name, Batch b, Material mat, bool shadows)
        {
            if (b.Parts.Count == 0)
                return;
            // Unity combines up to any size with 32-bit indices; split anyway to keep culling useful
            for (int start = 0; start < b.Parts.Count; start += 800)
            {
                var parts = b.Parts.GetRange(start, Mathf.Min(800, b.Parts.Count - start)).ToArray();
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = name };
                mesh.CombineMeshes(parts, true, true);
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        static Batch GlowAt(Vector3 p)
        {
            var (team, w) = MapLights.TeamAt(p);
            return team >= 0 && w > 0.5f ? glow[team] : glow[4];
        }

        // ------------------------------------------------------------ floor samples

        /// <summary>Points on the walkable floors (world faces facing up), on a grid of the given spacing.</summary>
        static IEnumerable<Vector3> FloorSamples(MapLoader map, float spacing)
        {
            var bsp = map.Bsp;
            var model = bsp.Models[0];
            var seen = new HashSet<Vector3Int>();
            for (int i = 0; i < model.NumFaces; i++)
            {
                var f = bsp.Faces[model.FirstFace + i];
                var plane = bsp.Planes[f.Plane];
                Vector3 n = GoldSrcSpace.DirectionToUnity(f.BackSide ? -plane.Normal : plane.Normal);
                if (n.y < 0.95f) continue;
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                foreach (var p in f.Points)
                {
                    var u = GoldSrcSpace.ToUnity(p);
                    min = Vector3.Min(min, u); max = Vector3.Max(max, u);
                }
                for (float x = Mathf.Ceil(min.x / spacing) * spacing; x <= max.x; x += spacing)
                    for (float z = Mathf.Ceil(min.z / spacing) * spacing; z <= max.z; z += spacing)
                    {
                        if (!Physics.Raycast(new Vector3(x, max.y + 0.5f, z), Vector3.down, out var hit, 1.0f) || hit.normal.y < 0.95f)
                            continue;
                        if (seen.Add(Vector3Int.RoundToInt(hit.point * 4)))
                            yield return hit.point;
                    }
            }
        }

        // ------------------------------------------------------------ corridor ribs

        static int Ribs(MapLoader map)
        {
            int count = 0;
            var placed = new HashSet<(int, int, int, int)>();
            foreach (var floor in FloorSamples(map, 1.0f))
            {
                foreach (var axis in new[] { Vector3.right, Vector3.forward })
                {
                    Vector3 side = Vector3.Cross(Vector3.up, axis);
                    Vector3 eye = floor + Vector3.up * 1.2f;
                    if (!Corridor(eye, axis, side, out float left, out float right, out float ceiling))
                        continue;
                    // Snap along the corridor so ribs line up every 3 m, centred between the walls
                    float along = Mathf.Round(Vector3.Dot(eye, axis) / RibSpacing) * RibSpacing;
                    Vector3 at = eye + axis * (along - Vector3.Dot(eye, axis)) + side * ((right - left) * 0.5f);
                    var key = (axis == Vector3.right ? 0 : 1, Mathf.RoundToInt(along), Mathf.RoundToInt(Vector3.Dot(at, side) * 2), Mathf.RoundToInt(floor.y * 2));
                    if (placed.Contains(key))
                        continue;
                    if (!Corridor(at, axis, side, out left, out right, out ceiling))
                        continue;
                    placed.Add(key);
                    Rib(at, axis, side, left, right, ceiling, floor.y);
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// A corridor cross-section at this point: walls both sides 1.8-5.5 m apart
        /// (square on), a ceiling 2.2-5 m up, and open for 1.5 m both ways along it.
        /// </summary>
        static bool Corridor(Vector3 eye, Vector3 axis, Vector3 side, out float left, out float right, out float ceiling)
        {
            left = right = ceiling = 0;
            if (!Physics.Raycast(eye, -side, out var l, 3.5f) || !Physics.Raycast(eye, side, out var r, 3.5f))
                return false;
            if (Vector3.Dot(l.normal, side) < 0.95f || Vector3.Dot(r.normal, -side) < 0.95f)
                return false;
            float width = l.distance + r.distance;
            if (width < 1.8f || width > 5.5f)
                return false;
            if (!Physics.Raycast(eye, Vector3.up, out var up, 4f) || up.normal.y > -0.95f)
                return false;
            float height = up.distance + 1.2f;
            if (height < 2.2f || height > 5f)
                return false;
            if (Physics.Raycast(eye, axis, 1.5f) || Physics.Raycast(eye, -axis, 1.5f))
                return false;
            left = l.distance; right = r.distance; ceiling = up.distance;
            return true;
        }

        static void Rib(Vector3 at, Vector3 axis, Vector3 side, float left, float right, float up, float floorY)
        {
            var rot = Quaternion.LookRotation(axis, Vector3.up);       // box sizes: x across the corridor, y up, z along it
            Vector3 wallL = at - side * left, wallR = at + side * right;
            float top = at.y + up;
            float height = top - floorY;
            const float depth = 0.12f, thick = 0.26f, bevel = 0.42f;
            var g = GlowAt(at);
            foreach (var (wall, inward) in new[] { (wallL, side), (wallR, -side) })
            {
                // Column up the wall
                Vector3 col = new Vector3(wall.x, floorY + (height - bevel) * 0.5f, wall.z) + inward * depth * 0.5f;
                metal.Box(col, rot, new Vector3(depth, height - bevel, thick));
                // Light strip on its face, from knee height to the bevel
                float stripH = height - bevel - 0.6f;
                if (stripH > 0.3f)
                    g.Box(new Vector3(wall.x, floorY + 0.4f + stripH * 0.5f, wall.z) + inward * (depth + 0.006f),
                          rot, new Vector3(0.016f, stripH, 0.05f));
                // Chamfer at the top corner: a block turned 45 degrees across the corner
                Vector3 corner = new Vector3(wall.x, top, wall.z) + inward * (bevel * 0.35f) - Vector3.up * (bevel * 0.35f);
                var tilt = Quaternion.AngleAxis(inward == side ? -45 : 45, axis);
                metal.Box(corner, tilt * rot, new Vector3(depth * 1.4f, bevel * 0.95f, thick));
            }
            // Beam across the ceiling
            Vector3 beam = new Vector3(at.x, top - depth * 0.5f, at.z);
            metal.Box(beam, rot, new Vector3(left + right - bevel * 1.2f, depth, thick));
        }

        // ------------------------------------------------------------ railings

        static int Railings(MapLoader map)
        {
            var bsp = map.Bsp;
            var model = bsp.Models[0];
            int runs = 0;
            for (int i = 0; i < model.NumFaces; i++)
            {
                var f = bsp.Faces[model.FirstFace + i];
                var plane = bsp.Planes[f.Plane];
                Vector3 n = GoldSrcSpace.DirectionToUnity(f.BackSide ? -plane.Normal : plane.Normal);
                if (n.y < 0.95f) continue;
                int tex = bsp.TexInfos[f.TexInfo].Texture;
                string name = tex >= 0 && tex < bsp.Textures.Length ? bsp.Textures[tex].Name : "";
                if (name.StartsWith("!") || name.StartsWith("{") || name.ToLowerInvariant().StartsWith("sky")) continue;
                var pts = new Vector3[f.Points.Length];
                Vector3 centre = Vector3.zero;
                for (int k = 0; k < pts.Length; k++) { pts[k] = GoldSrcSpace.ToUnity(f.Points[k]); centre += pts[k]; }
                centre /= pts.Length;
                for (int k = 0; k < pts.Length; k++)
                {
                    Vector3 a = pts[k], b = pts[(k + 1) % pts.Length];
                    Vector3 edge = b - a;
                    edge.y = 0;
                    float len = edge.magnitude;
                    if (len < 1f) continue;
                    Vector3 dir = edge / len;
                    Vector3 outward = Vector3.Cross(Vector3.up, dir);
                    Vector3 mid = (a + b) * 0.5f;
                    if (Vector3.Dot(outward, mid - centre) < 0) outward = -outward;
                    if (!Ledge(mid, outward)) continue;
                    // Only the stretch that really overhangs a drop (ends may meet walls or steps)
                    float s0 = 0, s1 = len;
                    while (s0 < len && !Ledge(a + dir * (s0 + 0.2f), outward)) s0 += 0.5f;
                    while (s1 > s0 && !Ledge(a + dir * (s1 - 0.2f), outward)) s1 -= 0.5f;
                    if (s1 - s0 < 0.8f) continue;
                    Rail(a + dir * s0, a + dir * s1, outward);
                    runs++;
                }
            }
            return runs;
        }

        /// <summary>
        /// A floor edge here is a ledge players stand on: it drops at least 2.5 m on
        /// its outward side, isn't against a wall, has standing room above (not a
        /// window sill) and floor to stand on behind it.
        /// </summary>
        static bool Ledge(Vector3 edgePoint, Vector3 outward)
        {
            Vector3 inside = edgePoint - outward * 0.5f;
            if (Physics.Raycast(inside + Vector3.up * 0.1f, Vector3.up, 1.9f))
                return false;
            if (!Physics.Raycast(inside + Vector3.up * 0.3f, Vector3.down, out var stand, 0.5f) || stand.normal.y < 0.9f)
                return false;
            Vector3 beyond = edgePoint + outward * 0.25f + Vector3.up * 0.3f;
            if (Physics.Raycast(beyond, Vector3.down, 2.8f))
                return false;
            if (Physics.Raycast(edgePoint + Vector3.up * 0.5f - outward * 0.1f, outward, 0.5f))
                return false;
            return true;
        }

        static void Rail(Vector3 from, Vector3 to, Vector3 outward)
        {
            Vector3 dir = (to - from).normalized;
            float len = Vector3.Distance(from, to);
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 lip = (from + to) * 0.5f - outward * 0.03f;
            var g = GlowAt(lip);
            // Glowing lip along the edge, and a light rail on posts
            g.Box(lip + Vector3.up * 0.025f, rot, new Vector3(0.05f, 0.04f, len));
            metal.Box(lip + Vector3.up * 0.05f - outward * 0.03f, rot, new Vector3(0.1f, 0.1f, len));       // kerb
            // Open-air ledges (balconies, battlements) stay open to jump from: just the lip.
            // Under a roof (a covered bridge, a walkway indoors) they get a rail.
            if (!Physics.Raycast((from + to) * 0.5f - outward * 0.3f + Vector3.up * 0.5f, Vector3.up, 40f))
                return;
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 2f) + 1);
            for (int p = 0; p < posts; p++)
            {
                Vector3 at = Vector3.Lerp(from, to, p / (float)(posts - 1)) - outward * 0.08f;
                metal.Box(at + Vector3.up * 0.5f, rot, new Vector3(0.08f, 1.0f, 0.08f));
            }
            Vector3 mid = (from + to) * 0.5f - outward * 0.08f;
            metal.Box(mid + Vector3.up * 1.0f, rot, new Vector3(0.09f, 0.07f, len));
            g.Box(mid + Vector3.up * 0.955f, rot, new Vector3(0.045f, 0.02f, len));                         // glow under the top rail
            metal.Box(mid + Vector3.up * 0.55f, rot, new Vector3(0.05f, 0.05f, len));                       // mid rail
        }

        // ------------------------------------------------------------ door frames

        static int DoorFrames(MapLoader map)
        {
            int count = 0;
            var entities = map.transform.Find("Entities");
            if (entities == null) return 0;
            // Doorways: door pieces that touch are one doorway (many maps split a door into
            // halves that slide apart; framing each half put two frames on top of each other)
            var doors = new List<Bounds>();
            foreach (Transform e in entities)
            {
                if (!e.name.StartsWith("func_door")) continue;
                var renderers = e.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var db = renderers[0].bounds;
                foreach (var r in renderers) db.Encapsulate(r.bounds);
                doors.Add(db);
            }
            for (bool merged = true; merged;)
            {
                merged = false;
                for (int i = 0; i < doors.Count && !merged; i++)
                    for (int j = i + 1; j < doors.Count && !merged; j++)
                    {
                        var grown = doors[i];
                        grown.Expand(0.1f);
                        if (!grown.Intersects(doors[j])) continue;
                        var both = doors[i];
                        both.Encapsulate(doors[j]);
                        doors[i] = both;
                        doors.RemoveAt(j);
                        merged = true;
                    }
            }
            foreach (var b in doors)
            {
                Vector3 s = b.size;
                if (s.y < 1.6f || s.y > 8f) continue;                         // lifts and platforms aren't doorways
                bool alongX = s.x >= s.z;
                float width = alongX ? s.x : s.z, thick = alongX ? s.z : s.x;
                if (width < 0.8f || width > 8f || thick > 1.2f) continue;
                Vector3 across = alongX ? Vector3.right : Vector3.forward;
                Vector3 through = alongX ? Vector3.forward : Vector3.right;
                var rot = Quaternion.LookRotation(through, Vector3.up);
                const float post = 0.22f, proud = 0.06f;
                // The frame laps over the door's edges, hiding the gap many maps leave between
                // a door and its opening (light from the room behind shows through it)
                const float overlap = 0.12f;
                // How far the wall's faces are from the door's middle on each side: the frame
                // stands proud of them (flush faces flicker as the two fight to be drawn)
                float wallFront = thick * 0.5f, wallBack = thick * 0.5f;
                foreach (float sign in new[] { -1f, 1f })
                {
                    Vector3 probe = b.center + across * sign * (width * 0.5f + post * 0.5f) + Vector3.up * (s.y * 0.2f);
                    for (int dirSign = -1; dirSign <= 1; dirSign += 2)
                    {
                        Vector3 dir = through * dirSign;
                        // Start outside and look back towards the door's middle for the wall face
                        if (Physics.Raycast(probe + dir * 1f, -dir, out var hit, 1f))
                        {
                            float dist = 1f - hit.distance;
                            if (dirSign > 0) wallFront = Mathf.Max(wallFront, dist);
                            else wallBack = Mathf.Max(wallBack, dist);
                        }
                    }
                }
                float front = wallFront + proud, back = wallBack + proud;
                float depth = front + back;
                Vector3 mid = b.center + through * ((front - back) * 0.5f);
                var g = GlowAt(b.center);
                float top = b.max.y + post - overlap;                          // frame top
                foreach (float sign in new[] { -1f, 1f })
                {
                    Vector3 p = mid + across * sign * (width * 0.5f + post * 0.5f - overlap);
                    p.y = b.min.y + (top - b.min.y) * 0.5f;
                    metal.Box(p, rot, new Vector3(post, top - b.min.y, depth));
                    // Glowing inner edge on both faces, running up to meet the top strip
                    float stripTop = b.max.y - overlap + 0.02f;
                    foreach (float face in new[] { -1f, 1f })
                    {
                        float faceAt = face > 0 ? front : -back;
                        Vector3 q = b.center + across * sign * (width * 0.5f - overlap + 0.03f) + through * (faceAt + face * 0.012f);
                        q.y = b.min.y + (stripTop - b.min.y) * 0.5f;
                        g.Box(q, rot, new Vector3(0.05f, stripTop - b.min.y, 0.02f));
                    }
                }
                Vector3 head = mid + Vector3.up * (b.max.y - overlap + post * 0.5f - mid.y);
                metal.Box(head, rot, new Vector3(width + post * 2 - overlap * 2, post, depth));
                foreach (float face in new[] { -1f, 1f })
                {
                    float faceAt = face > 0 ? front : -back;
                    Vector3 q = b.center + through * (faceAt + face * 0.012f);
                    q.y = b.max.y - overlap + 0.03f;
                    g.Box(q, rot, new Vector3(width - overlap * 2 + 0.11f, 0.05f, 0.02f));
                }
                count++;
            }
            return count;
        }
    }
}
