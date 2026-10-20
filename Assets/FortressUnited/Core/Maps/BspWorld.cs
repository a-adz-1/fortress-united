using System;
using System.Collections.Generic;
using System.Globalization;
using FortressUnited.Core.Movement;

namespace FortressUnited.Core.Maps
{
    /// <summary>
    /// The movement code's world for a compiled map: player traces against
    /// the world's collision hulls and every solid brush entity (walls,
    /// doors, platforms), water and other contents from the map and from
    /// func_water entities, and func_ladder volumes. Engine-free; brush
    /// entities can be moved at runtime by updating their Origin.
    /// </summary>
    public sealed class BspWorld : IMovementWorld
    {
        public sealed class BrushEntity
        {
            public int Index;          // entity number (world = 0)
            public int Model;          // BSP model number ("*N")
            public string ClassName;
            public Vec3 Origin;
            public bool Solid;         // blocks players
            public int Contents;       // for water/ladder volumes
            public MapEntity Entity;
        }

        public readonly BspFile Bsp;
        public readonly BspCollision Collision;
        public readonly List<BrushEntity> Brushes = new List<BrushEntity>();
        public double CurrentTime;
        readonly Random random = new Random(1);

        // Brush entities players walk through
        static readonly HashSet<string> NonSolid = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "func_illusionary", "func_water", "func_ladder", "func_nobuild", "func_nogrenades", "func_spawnguard",
            "trigger_multiple", "trigger_once", "trigger_push", "trigger_hurt", "trigger_teleport",
            "trigger_changelevel", "trigger_gravity", "trigger_transition", "trigger_autosave",
            "i_t_g", "info_tfgoal", "func_friction", "env_bubbles", "func_buyzone",
        };

        public BspWorld(BspFile bsp)
        {
            Bsp = bsp;
            Collision = new BspCollision(bsp);

            for (int i = 0; i < bsp.Entities.Count; i++)
            {
                var e = bsp.Entities[i];
                string model = e.Get("model");
                if (!model.StartsWith("*") || !int.TryParse(model.Substring(1), out int m) || m <= 0 || m >= bsp.Models.Length)
                    continue;

                string cls = e.ClassName;
                var b = new BrushEntity
                {
                    Index = i,
                    Model = m,
                    ClassName = cls,
                    Origin = ParseVec(e.Get("origin")),
                    Entity = e,
                    Contents = Contents.Empty,
                };
                if (string.Equals(cls, "func_water", StringComparison.OrdinalIgnoreCase))
                    b.Contents = int.TryParse(e.Get("skin", "-3"), out int c) ? c : Contents.Water;
                else if (string.Equals(cls, "func_ladder", StringComparison.OrdinalIgnoreCase))
                    b.Contents = Contents.Ladder;
                // Render mode "texture" with 0 amount and similar fully invisible walls stay solid
                b.Solid = !NonSolid.Contains(cls) && b.Contents == Contents.Empty;
                Brushes.Add(b);
            }
        }

        static Vec3 ParseVec(string s)
        {
            var p = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) return Vec3.Zero;
            float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
            float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
            float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
            return new Vec3(x, y, z);
        }

        // ------------------------------------------------------------------
        // IMovementWorld

        public MoveTrace PlayerTrace(MoveState s, Vec3 start, Vec3 end, int ignoreEnt)
        {
            int hull = BspCollision.EngineHull(s.UseHull);

            // Like the engine: trace the world and each solid brush entity;
            // starting in solid counts as fraction 0; the nearest result wins.
            MoveTrace best = Collision.Trace(0, hull, start, end);
            best.Ent = best.Fraction < 1 || best.StartSolid ? 0 : -1;

            foreach (var b in Brushes)
            {
                if (!b.Solid || b.Index == ignoreEnt)
                    continue;
                var t = Collision.Trace(b.Model, hull, start - b.Origin, end - b.Origin);
                if (t.Fraction < best.Fraction)
                {
                    t.EndPos = t.EndPos + b.Origin;
                    t.PlaneDist += Vec3.Dot(t.PlaneNormal, b.Origin);
                    t.Ent = b.Index;
                    best = t;
                }
            }

            return best;
        }

        public int PointContents(Vec3 point, out int trueContents)
        {
            int c = Collision.PointContents(0, 0, point);
            if (c == Contents.Empty || c == Contents.Translucent)
            {
                // Water volumes made of entities
                foreach (var b in Brushes)
                {
                    if (b.Contents == Contents.Empty || b.Contents == Contents.Ladder)
                        continue;
                    if (Collision.PointContents(b.Model, 0, point - b.Origin) != Contents.Empty)
                    {
                        c = b.Contents;
                        break;
                    }
                }
            }
            trueContents = c;
            // Currents count as water for the movement code's water level
            if (c <= Contents.Current0 && c >= Contents.CurrentDown)
                c = Contents.Water;
            return c;
        }

        public int TestPlayerPosition(MoveState s, Vec3 pos)
        {
            var t = PlayerTrace(s, pos, pos, -1);
            return t.StartSolid ? t.Ent : -1;
        }

        public bool IsPlayer(int ent) => false;

        public bool IsBrushModel(int ent) => ent == 0 || Brushes.Exists(b => b.Index == ent);

        sealed class Ladder : ILadder
        {
            public BspWorld World;
            public BrushEntity B;
            public Vec3 Mins => World.Bsp.Models[B.Model].Mins + B.Origin;
            public Vec3 Maxs => World.Bsp.Models[B.Model].Maxs + B.Origin;
            public MoveTrace Trace(Vec3 start, Vec3 end)
            {
                var t = World.Collision.Trace(B.Model, 0, start - B.Origin, end - B.Origin);
                t.EndPos = t.EndPos + B.Origin;
                return t;
            }
        }

        public ILadder FindLadder(MoveState s)
        {
            int hull = BspCollision.EngineHull(s.UseHull);
            foreach (var b in Brushes)
            {
                if (b.Contents != Contents.Ladder)
                    continue;
                if (Collision.PointContents(b.Model, hull, s.Origin - b.Origin) != Contents.Empty)
                    return new Ladder { World = this, B = b };
            }
            return null;
        }

        public double Time => CurrentTime;
        public int RandomLong(int low, int high) => random.Next(low, high + 1);
        public void PlaySound(MoveState state, string sample, float volume) => Sound?.Invoke(state, sample, volume);
        public void StuckTouch(MoveState state, int ent) { }

        /// <summary>Movement sounds (jump, land, wade...), for the game layer to play.</summary>
        public event Action<MoveState, string, float> Sound;

        // ------------------------------------------------------------------
        // Spawn points

        /// <summary>
        /// Where players start (already raised 1 unit, as TFC does): TFC team spawns (info_player_teamspawn / i_p_t)
        /// for the team, else any team spawn, else info_player_start /
        /// deathmatch spawns.
        /// </summary>
        public List<(Vec3 Origin, float Yaw)> SpawnPoints(int team = 0)
        {
            var result = new List<(Vec3, float)>();
            foreach (var pass in new[] { "team", "anyteam", "other" })
            {
                foreach (var e in Bsp.Entities)
                {
                    string cls = e.ClassName;
                    bool teamSpawn = cls == "info_player_teamspawn" || cls == "i_p_t";
                    bool ok = pass == "team" ? teamSpawn && team != 0 && e.Get("team_no") == team.ToString(CultureInfo.InvariantCulture)
                            : pass == "anyteam" ? teamSpawn
                            : cls == "info_player_start" || cls == "info_player_deathmatch";
                    if (!ok)
                        continue;
                    var a = ParseVec(e.Get("angles"));
                    float yaw = a.Y;
                    if (float.TryParse(e.Get("angle"), NumberStyles.Float, CultureInfo.InvariantCulture, out float ang))
                        yaw = ang;
                    // TFC puts the player 1 unit above the spot (spots often sit on the floor)
                    result.Add((ParseVec(e.Get("origin")) + new Vec3(0, 0, 1), yaw));
                }
                if (result.Count > 0)
                    break;
            }
            return result;
        }
    }
}
