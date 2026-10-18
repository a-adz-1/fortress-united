using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FortressUnited.Core.Movement;

namespace FortressUnited.Tests
{
    /// <summary>
    /// Differential movement test: runs every scenario in scenarios/ through
    /// PlayerMovement and compares each tick with the trace the original TFC
    /// movement code produced (expected/, recorded by tfc-decomp's
    /// tools/movetest).
    ///
    /// usage: MovementTests [scenario-name] [--dump]
    /// </summary>
    static class Program
    {
        // The original runs on the x87 FPU with extra precision; we round to
        // float at every step, so allow tiny differences in the last bits.
        const double Tolerance = 0.01;

        struct Cmd
        {
            public int Count, Msec, Buttons;
            public Vec3 Angles, DAngles;
            public float Fwd, Side, Up;
        }

        static int Main(string[] args)
        {
            string root = FindTestDir();
            string only = null;
            bool dump = false;
            foreach (var a in args)
            {
                if (a == "--dump") dump = true;
                else only = a;
            }

            int failed = 0, total = 0;
            foreach (var path in Directory.GetFiles(Path.Combine(root, "scenarios"), "*.txt"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (only != null && name != only)
                    continue;
                total++;
                string expectedPath = Path.Combine(root, "expected", name + ".trace");
                List<string> ours = Run(path);
                if (dump)
                {
                    foreach (var l in ours) Console.WriteLine(l);
                    continue;
                }
                if (!File.Exists(expectedPath))
                {
                    Console.WriteLine($"{name,-12} NO EXPECTED TRACE");
                    failed++;
                    continue;
                }
                string[] expected = File.ReadAllLines(expectedPath);
                string result = Compare(expected, ours, out double maxDiff);
                if (result == null)
                    Console.WriteLine($"{name,-12} ok    {ours.Count} ticks, max difference {maxDiff:G3}");
                else
                {
                    Console.WriteLine($"{name,-12} FAIL  {result}");
                    failed++;
                }
            }

            if (!dump)
                Console.WriteLine(failed == 0 ? $"all {total} scenarios match the original" : $"{failed} of {total} scenarios differ");
            return failed == 0 ? 0 : 1;
        }

        static string FindTestDir()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "scenarios")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                throw new DirectoryNotFoundException("scenarios/ not found");
            return dir;
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        static List<string> Run(string path)
        {
            var world = new TestWorld();
            var vars = new MoveVars();
            var state = new MoveState
            {
                PlayerIndex = 0,
                Server = true,
                Multiplayer = true,
                MoveType = MoveType.Walk,
                OnGround = -1,
                Gravity = 1,
                Friction = 1,
                ViewOfs = new Vec3(0, 0, 28),
                MaxSpeed = 400,
                ClientMaxSpeed = 400,
            };
            var cmds = new List<Cmd>();

            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                var w = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0) continue;

                switch (w[0])
                {
                    case "box":
                    case "water":
                    case "ladder":
                    {
                        var mn = new Vec3(F(w[1]), F(w[2]), F(w[3]));
                        var mx = new Vec3(F(w[4]), F(w[5]), F(w[6]));
                        if (w[0] == "ladder")
                            world.Ladders.Add(TestWorld.Box(TestWorld.Kind.Ladder, mn, mx));
                        else
                            world.Brushes.Add(TestWorld.Box(w[0] == "box" ? TestWorld.Kind.Solid : TestWorld.Kind.Water, mn, mx));
                        break;
                    }
                    case "brush":
                    {
                        var b = new TestWorld.Brush { Contents = TestWorld.Kind.Solid };
                        for (int i = 1; i + 3 < w.Length; i += 4)
                            b.Planes.Add(new TestWorld.Plane { NX = F(w[i]), NY = F(w[i + 1]), NZ = F(w[i + 2]), D = F(w[i + 3]) });
                        world.Brushes.Add(b);
                        break;
                    }
                    case "origin": state.Origin = new Vec3(F(w[1]), F(w[2]), F(w[3])); break;
                    case "velocity": state.Velocity = new Vec3(F(w[1]), F(w[2]), F(w[3])); break;
                    case "maxspeed": state.MaxSpeed = state.ClientMaxSpeed = F(w[1]); break;
                    case "physinfo": state.PhysInfo[w[1]] = w[2]; break;
                    case "movevar":
                    {
                        float v = F(w[2]);
                        switch (w[1])
                        {
                            case "gravity": vars.Gravity = v; break;
                            case "airaccelerate": vars.AirAccelerate = v; break;
                            case "accelerate": vars.Accelerate = v; break;
                            case "friction": vars.Friction = v; break;
                            case "maxvelocity": vars.MaxVelocity = v; break;
                            case "stepsize": vars.StepSize = v; break;
                            default: throw new FormatException("unknown movevar " + w[1]);
                        }
                        break;
                    }
                    case "cmd":
                    {
                        var c = new Cmd
                        {
                            Count = int.Parse(w[1]), Msec = int.Parse(w[2]),
                            Angles = new Vec3(F(w[3]), F(w[4]), F(w[5])),
                            Fwd = F(w[6]), Side = F(w[7]), Up = F(w[8]),
                            Buttons = w[9].StartsWith("0x") ? Convert.ToInt32(w[9], 16) : int.Parse(w[9]),
                        };
                        if (w.Length >= 13)
                            c.DAngles = new Vec3(F(w[10]), F(w[11]), F(w[12]));
                        cmds.Add(c);
                        break;
                    }
                    default:
                        throw new FormatException("unknown keyword " + w[0]);
                }
            }

            var movement = new PlayerMovement();
            var output = new List<string>();
            int tick = 0;
            foreach (var c in cmds)
            {
                Vec3 angles = c.Angles;
                for (int k = 0; k < c.Count; k++)
                {
                    var cmd = new MoveCommand
                    {
                        Msec = c.Msec,
                        ViewAngles = angles,
                        ForwardMove = c.Fwd,
                        SideMove = c.Side,
                        UpMove = c.Up,
                        Buttons = (Buttons)c.Buttons,
                    };
                    state.OldAngles = state.Angles;

                    movement.Move(state, cmd, world, vars);

                    world.CurrentTime += c.Msec / 1000.0;
                    angles = angles + c.DAngles;

                    int flags = (int)(state.Flags & (MoveFlags.OnGround | MoveFlags.Ducking | MoveFlags.WaterJump));
                    output.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0} {1:G9} {2:G9} {3:G9} {4:G9} {5:G9} {6:G9} {7} {8} {9} {10} {11:G9} {12}",
                        tick, state.Origin.X, state.Origin.Y, state.Origin.Z,
                        state.Velocity.X, state.Velocity.Y, state.Velocity.Z,
                        flags, state.OnGround, state.WaterLevel, state.UseHull, state.ViewOfs.Z, (int)state.MoveType));
                    tick++;
                }
            }
            return output;
        }

        /// <summary>Null if every tick matches; otherwise the first difference.</summary>
        static string Compare(string[] expected, List<string> ours, out double maxDiff)
        {
            maxDiff = 0;
            if (expected.Length != ours.Count)
                return $"{ours.Count} ticks, expected {expected.Length}";

            string[] fields = { "tick", "x", "y", "z", "vx", "vy", "vz", "flags", "onground", "waterlevel", "hull", "viewz", "movetype" };
            for (int t = 0; t < expected.Length; t++)
            {
                var e = expected[t].Split(' ');
                var o = ours[t].Split(' ');
                for (int i = 0; i < e.Length; i++)
                {
                    double ev = double.Parse(e[i], CultureInfo.InvariantCulture);
                    double ov = double.Parse(o[i], CultureInfo.InvariantCulture);
                    double d = Math.Abs(ev - ov);
                    bool exact = i == 0 || i >= 7 && i != 11;
                    if (exact ? d != 0 : d > Tolerance)
                        return $"tick {t}: {fields[i]} is {o[i]}, original {e[i]}\n  ours:     {ours[t]}\n  original: {expected[t]}";
                    if (!exact && d > maxDiff)
                        maxDiff = d;
                }
            }
            return null;
        }
    }
}
