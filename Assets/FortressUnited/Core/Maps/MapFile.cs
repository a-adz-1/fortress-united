using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FortressUnited.Core.Movement;

namespace FortressUnited.Core.Maps
{
    /// <summary>
    /// One brush face from a .map file: a plane through three points, a
    /// texture and its alignment.
    /// </summary>
    public sealed class MapFace
    {
        public Vec3 P0, P1, P2;
        public string Texture;
        /// <summary>Valve 220 texture axes (U/V) with offsets; for the classic Quake format these are derived.</summary>
        public Vec3 UAxis, VAxis;
        public float UOffset, VOffset;
        public float Rotation, ScaleX = 1, ScaleY = 1;
        public bool Valve220;

        /// <summary>
        /// Plane normal from the three points, Quake style: (p0 - p1) x (p2 - p1).
        /// Whether that points out of the brush depends on the editor's winding;
        /// MapBrush orients the planes outward before using them.
        /// </summary>
        public Vec3 Normal
        {
            get
            {
                Vec3 n = Vec3.Cross(P0 - P1, P2 - P1);
                n.Normalize();
                return n;
            }
        }

        public float Dist => Vec3.Dot(Normal, P0);
    }

    /// <summary>A convex brush: the space inside all of its face planes.</summary>
    public sealed class MapBrush
    {
        public readonly List<MapFace> Faces = new List<MapFace>();

        /// <summary>
        /// The polygon of each face, clipped by all the other planes (null if
        /// a face has no area). Same order as Faces.
        /// </summary>
        public Vec3[][] Polygons()
        {
            // Outward planes: the average of the defining points is inside a convex brush
            var normals = new Vec3[Faces.Count];
            var dists = new float[Faces.Count];
            Vec3 centre = Vec3.Zero;
            foreach (var f in Faces)
                centre = centre + f.P0 + f.P1 + f.P2;
            centre = centre * (1.0f / (Faces.Count * 3));
            for (int i = 0; i < Faces.Count; i++)
            {
                normals[i] = Faces[i].Normal;
                dists[i] = Faces[i].Dist;
                if (Vec3.Dot(normals[i], centre) - dists[i] > 0)
                {
                    normals[i] = -normals[i];
                    dists[i] = -dists[i];
                }
            }

            var result = new Vec3[Faces.Count][];
            for (int i = 0; i < Faces.Count; i++)
            {
                List<Vec3> poly = BaseWinding(normals[i], dists[i], 65536);
                for (int j = 0; j < Faces.Count && poly != null; j++)
                {
                    if (j == i) continue;
                    poly = Clip(poly, normals[j], dists[j]);
                }
                result[i] = poly != null && poly.Count >= 3 ? poly.ToArray() : null;
            }
            return result;
        }

        /// <summary>A huge square on the plane, wound clockwise seen from the front.</summary>
        static List<Vec3> BaseWinding(Vec3 normal, float dist, float size)
        {
            // Pick the major axis to build in-plane vectors
            float ax = Math.Abs(normal.X), ay = Math.Abs(normal.Y), az = Math.Abs(normal.Z);
            Vec3 up = az >= ax && az >= ay ? new Vec3(1, 0, 0) : new Vec3(0, 0, 1);

            float v = Vec3.Dot(up, normal);
            up = Vec3.MA(up, -v, normal);
            up.Normalize();
            Vec3 org = normal * dist;
            Vec3 right = Vec3.Cross(up, normal);
            up = up * size;
            right = right * size;

            return new List<Vec3>
            {
                org - right + up,
                org + right + up,
                org + right - up,
                org - right - up,
            };
        }

        /// <summary>Keeps the part of the polygon behind the plane (inside the brush).</summary>
        static List<Vec3> Clip(List<Vec3> poly, Vec3 normal, float dist)
        {
            const float Epsilon = 0.01f;
            var dists = new float[poly.Count];
            int front = 0, back = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                dists[i] = Vec3.Dot(poly[i], normal) - dist;
                if (dists[i] > Epsilon) front++;
                else if (dists[i] < -Epsilon) back++;
            }
            if (front == 0)
                return poly;     // entirely inside
            if (back == 0)
                return null;     // entirely outside

            var result = new List<Vec3>();
            for (int i = 0; i < poly.Count; i++)
            {
                Vec3 p1 = poly[i];
                Vec3 p2 = poly[(i + 1) % poly.Count];
                float d1 = dists[i], d2 = dists[(i + 1) % poly.Count];

                if (d1 <= Epsilon)
                    result.Add(p1);
                if ((d1 > Epsilon && d2 < -Epsilon) || (d1 < -Epsilon && d2 > Epsilon))
                {
                    float t = d1 / (d1 - d2);
                    result.Add(p1 + (p2 - p1) * t);
                }
            }
            return result.Count >= 3 ? result : null;
        }
    }

    /// <summary>
    /// Reads .map source files as saved by TrenchBroom, J.A.C.K. and Hammer:
    /// the classic Quake format and the Valve 220 format (texture axes in
    /// brackets). Gives entities with their keyvalues and brushes.
    /// </summary>
    public static class MapFile
    {
        public static List<MapEntity> Load(string path) => Parse(Latin1.GetString(File.ReadAllBytes(path)));

        public static List<MapEntity> Parse(string text)
        {
            var list = new List<MapEntity>();
            var t = new Tokenizer(text);
            while (t.Next(out string tok))
            {
                Expect(tok, "{", t);
                var e = new MapEntity();
                while (true)
                {
                    if (!t.Next(out tok))
                        throw Error("unexpected end of file in entity", t);
                    if (tok == "}")
                        break;
                    if (tok == "{")
                    {
                        e.Brushes.Add(ParseBrush(t));
                        continue;
                    }
                    if (!t.Next(out string value))
                        throw Error($"missing value for '{tok}'", t);
                    e.Set(tok, value);
                }
                list.Add(e);
            }
            return list;
        }

        static MapBrush ParseBrush(Tokenizer t)
        {
            var b = new MapBrush();
            while (true)
            {
                if (!t.Next(out string tok))
                    throw Error("unexpected end of file in brush", t);
                if (tok == "}")
                    return b;
                if (tok != "(")
                    throw Error($"expected '(' or '}}', got '{tok}' (patches and brush primitives are not supported)", t);

                var f = new MapFace();
                f.P0 = Point(t, firstParenRead: true);
                f.P1 = Point(t, false);
                f.P2 = Point(t, false);
                f.Texture = Word(t);

                if (!t.Next(out tok))
                    throw Error("unexpected end of file in face", t);
                if (tok == "[")
                {
                    // Valve 220: [ ux uy uz uoffset ] [ vx vy vz voffset ] rotation scaleX scaleY
                    f.Valve220 = true;
                    f.UAxis = new Vec3(Num(t), Num(t), Num(t));
                    f.UOffset = Num(t);
                    Expect(Word(t), "]", t);
                    Expect(Word(t), "[", t);
                    f.VAxis = new Vec3(Num(t), Num(t), Num(t));
                    f.VOffset = Num(t);
                    Expect(Word(t), "]", t);
                    f.Rotation = Num(t);
                    f.ScaleX = Num(t);
                    f.ScaleY = Num(t);
                }
                else
                {
                    // Quake: xoffset yoffset rotation scaleX scaleY
                    f.UOffset = ParseFloat(tok, t);
                    f.VOffset = Num(t);
                    f.Rotation = Num(t);
                    f.ScaleX = Num(t);
                    f.ScaleY = Num(t);
                    QuakeAxes(f);
                }
                b.Faces.Add(f);
            }
        }

        /// <summary>The classic format's implied texture axes (from the face's major axis), rotated.</summary>
        static void QuakeAxes(MapFace f)
        {
            Vec3 n = f.Normal;
            Vec3[] baseAxis =
            {
                new Vec3(0, 0, 1), new Vec3(1, 0, 0), new Vec3(0, -1, 0),   // floor
                new Vec3(0, 0, -1), new Vec3(1, 0, 0), new Vec3(0, -1, 0),  // ceiling
                new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, -1),   // west wall
                new Vec3(-1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, -1),  // east wall
                new Vec3(0, 1, 0), new Vec3(1, 0, 0), new Vec3(0, 0, -1),   // south wall
                new Vec3(0, -1, 0), new Vec3(1, 0, 0), new Vec3(0, 0, -1),  // north wall
            };
            int best = 0;
            float bestDot = 0;
            for (int i = 0; i < 6; i++)
            {
                float d = Vec3.Dot(n, baseAxis[i * 3]);
                if (d > bestDot) { bestDot = d; best = i; }
            }
            Vec3 u = baseAxis[best * 3 + 1], v = baseAxis[best * 3 + 2];

            double ang = f.Rotation * Math.PI / 180;
            float sinv = (float)Math.Sin(ang), cosv = (float)Math.Cos(ang);
            int sv = u.X != 0 ? 0 : (u.Y != 0 ? 1 : 2);
            int tv = v.X != 0 ? 0 : (v.Y != 0 ? 1 : 2);
            Vec3 ru = u, rv = v;
            ru[sv] = cosv * u[sv] - sinv * u[tv];
            ru[tv] = sinv * u[sv] + cosv * u[tv];
            rv[sv] = cosv * v[sv] - sinv * v[tv];
            rv[tv] = sinv * v[sv] + cosv * v[tv];
            f.UAxis = ru;
            f.VAxis = rv;
        }

        static Vec3 Point(Tokenizer t, bool firstParenRead)
        {
            if (!firstParenRead)
                Expect(Word(t), "(", t);
            var p = new Vec3(Num(t), Num(t), Num(t));
            Expect(Word(t), ")", t);
            return p;
        }

        static string Word(Tokenizer t)
        {
            if (!t.Next(out string tok))
                throw Error("unexpected end of file", t);
            return tok;
        }

        static float Num(Tokenizer t) => ParseFloat(Word(t), t);

        static float ParseFloat(string s, Tokenizer t)
        {
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                throw Error($"expected a number, got '{s}'", t);
            return v;
        }

        static void Expect(string tok, string want, Tokenizer t)
        {
            if (tok != want)
                throw Error($"expected '{want}', got '{tok}'", t);
        }

        static FormatException Error(string msg, Tokenizer t) => new FormatException($"line {t.Line}: {msg}");
    }
}
