using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FortressUnited.Core.Maps;

namespace FortressUnited.Tools
{
    /// <summary>
    /// Map tools.
    ///
    ///   inventory &lt;maps dir&gt; &lt;out dir&gt; [--stock name,name,...]
    ///     Reads every .bsp and writes texture-inventory.csv and
    ///     texture-inventory.md: each texture's faces, area, maps, size,
    ///     source (embedded / WAD) and a suggested material category.
    ///     Maps named with --stock (Valve's) are counted separately.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "inventory")
            {
                var stock = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 3; i + 1 < args.Length; i++)
                    if (args[i] == "--stock")
                        foreach (var s in args[i + 1].Split(','))
                            stock.Add(s.Trim());
                return Inventory(args[1], args[2], stock);
            }
            if (args.Length >= 2 && args[0] == "walk")
                return Walk(args[1], args.Length >= 3 && args[2] == "-v");
            if (args.Length >= 1 && args[0] == "check")
                return Check(args.Length >= 2 ? args[1] : null);
            Console.Error.WriteLine("usage: MapTools inventory <maps dir> <out dir> [--stock name,name,...]");
            Console.Error.WriteLine("       MapTools check [maps dir]   (reader self-tests, then every .bsp in the dir)");
            Console.Error.WriteLine("       MapTools walk <map.bsp | maps dir> [-v]   (spawn, run, hop and turn on real map collision)");
            return 1;
        }

        sealed class TexStats
        {
            public string Name;
            public int Width, Height;
            public bool Embedded;
            public int Faces;
            public double Area;          // user maps only
            public double StockArea;     // Valve maps
            public readonly SortedSet<string> Maps = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly SortedSet<string> StockMaps = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly SortedSet<string> Wads = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        static int Inventory(string mapsDir, string outDir, HashSet<string> stock)
        {
            var textures = new Dictionary<string, TexStats>(StringComparer.OrdinalIgnoreCase);
            var mapSummaries = new List<string>();
            var failures = new List<string>();
            var wadUse = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int userMaps = 0, stockMaps = 0;

            foreach (var path in Directory.GetFiles(mapsDir, "*.bsp").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string map = Path.GetFileNameWithoutExtension(path);
                bool isStock = stock.Contains(map);
                BspFile bsp;
                try
                {
                    bsp = BspFile.Load(path);
                }
                catch (Exception e)
                {
                    failures.Add($"{map}: {e.Message}");
                    continue;
                }
                if (isStock) stockMaps++; else userMaps++;

                var wads = bsp.WadFiles();
                if (!isStock)
                    foreach (var w in wads)
                        wadUse[w] = wadUse.TryGetValue(w, out int n) ? n + 1 : 1;

                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var face in bsp.Faces)
                {
                    string name = bsp.FaceTexture(face);
                    if (name.Length == 0)
                        continue;
                    if (!textures.TryGetValue(name, out var t))
                    {
                        var tex = bsp.Textures[bsp.TexInfos[face.TexInfo].Texture];
                        t = new TexStats { Name = name, Width = tex.Width, Height = tex.Height, Embedded = tex.Embedded };
                        textures[name] = t;
                    }
                    double area = BspFile.Area(face.Points);
                    t.Faces++;
                    if (isStock)
                    {
                        t.StockArea += area;
                        t.StockMaps.Add(map);
                    }
                    else
                    {
                        t.Area += area;
                        t.Maps.Add(map);
                        if (!t.Embedded)
                            foreach (var w in wads) t.Wads.Add(w);
                    }
                    used.Add(name);
                }
                mapSummaries.Add($"| {map}{(isStock ? " (Valve)" : "")} | {used.Count} | {bsp.Faces.Length} | {bsp.Entities.Count} | {string.Join(", ", wads)} |");
            }

            Directory.CreateDirectory(outDir);
            var all = textures.Values.ToList();
            var userTex = all.Where(t => t.Maps.Count > 0).OrderByDescending(t => t.Area).ToList();
            // Sky, triggers, clips and origin brushes are never drawn: keep them out of area shares
            var drawn = userTex.Where(t => NotDrawn(t.Name) == false).ToList();

            // CSV: everything
            var csv = new StringBuilder();
            csv.AppendLine("texture,category,team,special,width,height,source,faces,area_user_maps,user_maps,area_valve_maps,valve_maps,wads,maps");
            foreach (var t in all.OrderByDescending(t => t.Area + t.StockArea))
            {
                csv.AppendLine(string.Join(",",
                    Csv(t.Name), Category(t.Name), Team(t.Name), Csv(Special(t.Name)), t.Width, t.Height, t.Embedded ? "embedded" : "wad",
                    t.Faces, t.Area.ToString("0", CultureInfo.InvariantCulture), t.Maps.Count,
                    t.StockArea.ToString("0", CultureInfo.InvariantCulture), t.StockMaps.Count,
                    Csv(string.Join(";", t.Wads)), Csv(string.Join(";", t.Maps))));
            }
            File.WriteAllText(Path.Combine(outDir, "texture-inventory.csv"), csv.ToString());

            // Markdown summary
            double totalArea = drawn.Sum(t => t.Area);
            var md = new StringBuilder();
            md.AppendLine("# Texture inventory");
            md.AppendLine();
            md.AppendLine($"Generated by `tests/MapTools` from {userMaps} community/custom maps" +
                          (stockMaps > 0 ? $" (plus {stockMaps} Valve maps, counted separately and not used by Fortress United)" : "") + ".");
            md.AppendLine("Full data: `texture-inventory.csv`. Categories are guesses from the texture name; correct them in the mapping table.");
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine();
            md.AppendLine($"- Distinct textures in the custom maps: **{userTex.Count}** ({userTex.Count(t => t.Embedded)} embedded in the BSPs, {userTex.Count(t => !t.Embedded)} from WADs).");
            md.AppendLine($"- Team-coloured textures (one surface tinted per team): **{drawn.Count(t => Team(t.Name) != "")}**, {drawn.Where(t => Team(t.Name) != "").Sum(t => t.Area) / totalArea:P0} of drawn area.");
            md.AppendLine("- Area shares below are of drawn surfaces (sky, triggers, clip and origin brushes excluded).");
            foreach (double target in new[] { 0.5, 0.8, 0.9, 0.95, 0.99 })
            {
                double acc = 0;
                int n = 0;
                foreach (var t in drawn)
                {
                    if (acc >= target * totalArea) break;
                    acc += t.Area;
                    n++;
                }
                md.AppendLine($"- **{n}** textures cover {target:P0} of all surface area.");
            }
            md.AppendLine();
            md.AppendLine("So a material library of a few hundred materials, mapped by name, would upgrade most of every map.");
            md.AppendLine();

            md.AppendLine("## By suggested category (custom maps)");
            md.AppendLine();
            md.AppendLine("| Category | Textures | Share of area |");
            md.AppendLine("|---|---|---|");
            foreach (var g in drawn.GroupBy(t => Category(t.Name)).OrderByDescending(g => g.Sum(t => t.Area)))
                md.AppendLine($"| {g.Key} | {g.Count()} | {g.Sum(t => t.Area) / totalArea:P1} |");
            md.AppendLine();

            md.AppendLine("## Special textures");
            md.AppendLine();
            md.AppendLine("These aren't drawn as normal surfaces; the importer handles them by name prefix.");
            md.AppendLine();
            md.AppendLine("| Kind | Textures | Examples |");
            md.AppendLine("|---|---|---|");
            foreach (var g in userTex.Where(t => Special(t.Name) != "").GroupBy(t => Special(t.Name)).OrderBy(g => g.Key))
                md.AppendLine($"| {g.Key} | {g.Count()} | {string.Join(", ", g.OrderByDescending(t => t.Area).Take(6).Select(t => "`" + t.Name + "`"))} |");
            md.AppendLine();

            md.AppendLine("## Top 150 textures by area (custom maps)");
            md.AppendLine();
            md.AppendLine("| # | Texture | Category | Team | Size | Maps | Share of area | Source |");
            md.AppendLine("|---|---|---|---|---|---|---|---|");
            int rank = 0;
            foreach (var t in drawn.Take(150))
                md.AppendLine($"| {++rank} | `{t.Name}` | {Category(t.Name)} | {Team(t.Name)} | {t.Width}x{t.Height} | {t.Maps.Count} | {t.Area / totalArea:P2} | {(t.Embedded ? "embedded" : string.Join(", ", t.Wads.Take(2)))} |");
            md.AppendLine();

            md.AppendLine("## WAD files used by the custom maps");
            md.AppendLine();
            md.AppendLine("| WAD | Maps |");
            md.AppendLine("|---|---|");
            foreach (var kv in wadUse.OrderByDescending(kv => kv.Value))
                md.AppendLine($"| {kv.Key} | {kv.Value} |");
            md.AppendLine();

            md.AppendLine("## Maps");
            md.AppendLine();
            md.AppendLine("| Map | Textures | Faces | Entities | WADs |");
            md.AppendLine("|---|---|---|---|---|");
            foreach (var s in mapSummaries)
                md.AppendLine(s);

            if (failures.Count > 0)
            {
                md.AppendLine();
                md.AppendLine("## Maps that couldn't be read");
                md.AppendLine();
                foreach (var f in failures)
                    md.AppendLine("- " + f);
            }

            File.WriteAllText(Path.Combine(outDir, "texture-inventory.md"), md.ToString());
            Console.WriteLine($"{userMaps} custom + {stockMaps} Valve maps, {userTex.Count} textures in custom maps, {failures.Count} unreadable");
            return 0;
        }

        static bool NotDrawn(string name)
        {
            string sp = Special(name);
            return sp != "" && !sp.StartsWith("liquid");
        }

        /// <summary>Self-test of the .map reader, then sanity checks on every BSP.</summary>
        static int Check(string mapsDir)
        {
            int failures = 0;

            // A 64x64x32 box in Valve 220 format, plus an entity with keys
            const string map = @"// test
{
""classname"" ""worldspawn""
""wad"" ""C:\maps\tfc.wad;halflife.wad""
{
( -32 -32 0 ) ( -32 32 0 ) ( 32 32 0 ) floor [ 1 0 0 0 ] [ 0 -1 0 0 ] 0 1 1
( -32 -32 32 ) ( 32 32 32 ) ( -32 32 32 ) ceil [ 1 0 0 0 ] [ 0 -1 0 0 ] 0 1 1
( -32 -32 0 ) ( -32 -32 32 ) ( -32 32 32 ) wall [ 0 1 0 0 ] [ 0 0 -1 0 ] 0 1 1
( 32 32 0 ) ( 32 32 32 ) ( 32 -32 32 ) wall [ 0 1 0 0 ] [ 0 0 -1 0 ] 0 1 1
( 32 -32 0 ) ( 32 -32 32 ) ( -32 -32 32 ) wall [ 1 0 0 0 ] [ 0 0 -1 0 ] 0 1 1
( -32 32 0 ) ( -32 32 32 ) ( 32 32 32 ) wall [ 1 0 0 0 ] [ 0 0 -1 0 ] 0 1 1
}
}
{
""classname"" ""info_player_teamspawn""
""origin"" ""0 0 64""
""team_no"" ""1""
}
";
            var ents = MapFile.Parse(map);
            var polys = ents[0].Brushes[0].Polygons();
            double[] areas = polys.Select(p => p == null ? 0 : (double)BspFile.Area(p)).ToArray();
            bool ok = ents.Count == 2 && ents[1].Get("team_no") == "1" && ents[0].Brushes.Count == 1
                      && polys.All(p => p != null && p.Length == 4)
                      && Math.Abs(areas[0] - 4096) < 0.1 && Math.Abs(areas[1] - 4096) < 0.1
                      && areas.Skip(2).All(a => Math.Abs(a - 2048) < 0.1)
                      && ents[0].Brushes[0].Faces[0].Valve220;
            Console.WriteLine($".map reader self-test: {(ok ? "ok" : "FAIL")} (face areas {string.Join(" ", areas.Select(a => a.ToString("0")))})");
            if (!ok) failures++;

            // Entity text round trip
            string text = MapEntity.Write(ents);
            var again = MapEntity.ParseList(text);
            bool rt = again.Count == 2 && again[1].Get("origin") == "0 0 64" && again[0].Get("wad").Contains("tfc.wad");
            Console.WriteLine($"entity text round trip: {(rt ? "ok" : "FAIL")}");
            if (!rt) failures++;

            if (mapsDir != null)
            {
                int maps = 0, faces = 0, bad = 0;
                foreach (var path in Directory.GetFiles(mapsDir, "*.bsp"))
                {
                    var bsp = BspFile.Load(path);
                    maps++;
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (bsp.Entities.Count == 0 || bsp.Entities[0].ClassName != "worldspawn")
                    {
                        Console.WriteLine($"{name}: first entity is not worldspawn");
                        bad++;
                    }
                    foreach (var f in bsp.Faces)
                    {
                        faces++;
                        var plane = bsp.Planes[f.Plane];
                        float maxOff = 0;
                        foreach (var p in f.Points)
                            maxOff = Math.Max(maxOff, Math.Abs(FortressUnited.Core.Movement.Vec3.Dot(plane.Normal, p) - plane.Dist));
                        if (f.Points.Length < 3 || maxOff > 0.5f || bsp.FaceTexture(f).Length == 0)
                        {
                            if (bad++ < 10)
                                Console.WriteLine($"{name}: bad face ({f.Points.Length} points, {maxOff:0.00} off its plane, texture '{bsp.FaceTexture(f)}')");
                        }
                    }
                }
                Console.WriteLine($"BSP check: {maps} maps, {faces} faces, {bad} problems");
                if (bad > 0) failures++;
            }

            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Plays a scripted run on each map's real collision hulls: spawn,
        /// settle, run forward, bunny hop, turn. Fails if the player ever ends
        /// a tick inside solid, never lands, or can't move.
        /// </summary>
        static int Walk(string path, bool verbose)
        {
            var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.bsp").OrderBy(f => f).ToArray() : new[] { path };
            int failed = 0;
            foreach (var file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                var bsp = BspFile.Load(file);
                var world = new BspWorld(bsp);
                var spawns = world.SpawnPoints(1);
                if (spawns.Count == 0)
                {
                    Console.WriteLine($"{name,-24} no spawn points");
                    continue;
                }
                var (origin, yaw) = spawns[0];
                if (verbose)
                {
                    var probe = new FortressUnited.Core.Movement.MoveState();
                    foreach (var sp in spawns)
                    {
                        var tr = world.PlayerTrace(probe, sp.Origin, sp.Origin, -1);
                        string who = tr.Ent > 0 ? bsp.Entities[tr.Ent].ClassName + " " + bsp.Entities[tr.Ent].Get("model") : tr.Ent == 0 ? "world" : "free";
                        Console.WriteLine($"  spawn {sp.Origin} yaw {sp.Yaw}: {who}");
                    }
                }
                var state = new FortressUnited.Core.Movement.MoveState
                {
                    Origin = origin,
                    ViewOfs = new FortressUnited.Core.Movement.Vec3(0, 0, 28),
                    MaxSpeed = 400, ClientMaxSpeed = 400,
                };
                state.PhysInfo["tfc"] = "1";
                var vars = new FortressUnited.Core.Movement.MoveVars();
                var move = new FortressUnited.Core.Movement.PlayerMovement();

                int ticks = 0, solidTicks = 0, groundTicks = 0;
                float travelled = 0;
                var last = state.Origin;
                string problem = null;

                void Run(int count, float fwd, float side, FortressUnited.Core.Movement.Buttons buttons, float dyaw)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var cmd = new FortressUnited.Core.Movement.MoveCommand
                        {
                            Msec = 10, ViewAngles = new FortressUnited.Core.Movement.Vec3(0, yaw, 0),
                            ForwardMove = fwd, SideMove = side, Buttons = buttons,
                        };
                        move.Move(state, cmd, world, vars);
                        world.CurrentTime += 0.01;
                        yaw += dyaw;
                        ticks++;
                        if (world.TestPlayerPosition(state, state.Origin) != -1)
                            solidTicks++;
                        if (state.OnGround != -1)
                            groundTicks++;
                        travelled += (state.Origin - last).Length();
                        last = state.Origin;
                        if (verbose && ticks % 50 == 0)
                            Console.WriteLine($"  t={ticks * 10}ms pos={state.Origin} vel={state.Velocity} ground={state.OnGround} water={state.WaterLevel}");
                    }
                }

                var B = FortressUnited.Core.Movement.Buttons.None;
                var F = FortressUnited.Core.Movement.Buttons.Forward;
                var J = FortressUnited.Core.Movement.Buttons.Jump;
                var R = FortressUnited.Core.Movement.Buttons.MoveRight;
                Run(100, 0, 0, B, 0);               // settle onto the floor
                bool landed = state.OnGround != -1 || state.WaterLevel >= 2;
                Run(150, 400, 0, F, 0);             // run forward
                Run(200, 0, 400, R | J, 1.5f);      // strafe-hop in a circle
                Run(150, 400, 0, F, -1.2f);         // run, turning
                Run(100, 0, 0, B, 0);               // stop

                if (solidTicks > 0) problem = $"inside solid on {solidTicks} ticks";
                else if (!landed) problem = "never landed after spawning";
                else if (travelled < 200) problem = $"barely moved ({travelled:0} units)";

                Console.WriteLine($"{name,-24} {(problem == null ? "ok  " : "FAIL")} travelled {travelled,6:0} units, on ground {100.0 * groundTicks / ticks,3:0}% of ticks{(problem != null ? " - " + problem : "")}");
                if (problem != null) failed++;
            }
            Console.WriteLine(failed == 0 ? $"all {files.Length} maps ok" : $"{failed} of {files.Length} maps failed");
            return failed == 0 ? 0 : 1;
        }

        static string Csv(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        /// <summary>Textures with engine meaning, by GoldSrc naming convention.</summary>
        static string Special(string name)
        {
            string n = name.ToLowerInvariant();
            if (n == "sky") return "sky";
            if (n.StartsWith("!")) return "liquid (! prefix: water/slime/lava)";
            if (n == "aaatrigger" || n == "trigger") return "trigger volume";
            if (n == "clip" || n == "clipbevel") return "player clip";
            if (n == "origin") return "rotation origin";
            if (n == "null" || n == "skip" || n == "hint") return "compile tool";
            if (n.StartsWith("{")) return "";
            return "";
        }

        static readonly (string Category, string[] Keys)[] Categories =
        {
            ("water/liquid", new[] { "!", "water", "slime", "lava" }),
            ("sky", new[] { "sky" }),
            ("light", new[] { "~", "light", "lite", "lamp", "fluor" }),
            ("glass/window", new[] { "glass", "window", "wind" }),
            ("grate/fence", new[] { "grate", "grid", "fence", "mesh", "vent", "{" }),
            ("door", new[] { "door", "gate" }),
            ("sign/decal", new[] { "sign", "arrow", "num", "logo", "lbl", "label", "poster" }),
            ("brick", new[] { "brick", "brk" }),
            ("stone/rock", new[] { "rock", "stone", "stn", "cliff", "cave", "boulder", "cobble", "marble", "granite", "slate" }),
            ("concrete/plaster", new[] { "concrete", "conc", "cement", "plaster", "cinder", "crete", "c1a", "c2a", "c3a" }),
            ("metal", new[] { "metal", "met", "steel", "iron", "rust", "plate", "pipe", "tank", "panel", "girder", "beam", "crate", "rivet", "alum" }),
            ("wood", new[] { "wood", "wd", "plank", "board", "crate", "log", "bark", "barrel" }),
            ("ground/terrain", new[] { "sand", "dirt", "mud", "grass", "gravel", "ground", "earth", "snow", "ice", "terrain", "path" }),
            ("tile/floor", new[] { "tile", "floor", "flr", "carpet", "fl_" }),
            ("trim/edge", new[] { "trim", "edge", "border", "strip", "lip", "base", "bord" }),
            ("roof", new[] { "roof", "shingle" }),
            ("tech/computer", new[] { "comp", "tech", "console", "screen", "monitor", "lab" }),
        };

        /// <summary>Team colour in the name (TFC maps tint the same surface per team), or "".</summary>
        static string Team(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.EndsWith("blu") || n.EndsWith("blue") || n.Contains("_blu") || n.Contains("blue")) return "blue";
            if (n.EndsWith("red") || n.Contains("_red")) return "red";
            if (n.EndsWith("yel") || n.Contains("yellow") || n.Contains("_yel")) return "yellow";
            if (n.EndsWith("grn") || n.Contains("green") || n.Contains("_grn")) return "green";
            return "";
        }

        /// <summary>A first guess at the material category from the texture name.</summary>
        static string Category(string name)
        {
            string n = name.ToLowerInvariant();
            if (Special(n) != "" && !n.StartsWith("!")) return "special";
            foreach (var (cat, keys) in Categories)
                foreach (var k in keys)
                    if (n.Contains(k))
                        return cat;
            return "unsorted";
        }
    }
}
