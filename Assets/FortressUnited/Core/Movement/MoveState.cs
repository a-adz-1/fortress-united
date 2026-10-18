namespace FortressUnited.Core.Movement
{
    /// <summary>
    /// Server movement settings (TFC's movevars / sv_* cvars). Defaults are
    /// the usual TFC server values.
    /// </summary>
    public sealed class MoveVars
    {
        public float Gravity = 800;
        public float StopSpeed = 100;
        public float MaxSpeed = 500;
        public float Accelerate = 10;
        public float AirAccelerate = 10;
        public float Friction = 4;
        public float EdgeFriction = 2;
        public float Bounce = 1;
        public float StepSize = 18;
        public float MaxVelocity = 2000;
        public float RollAngle = 0;
        public float RollSpeed = 200;
    }

    /// <summary>One movement command (a usercmd).</summary>
    public struct MoveCommand
    {
        /// <summary>Duration in milliseconds.</summary>
        public int Msec;
        public Vec3 ViewAngles;
        public float ForwardMove, SideMove, UpMove;
        public Buttons Buttons;
    }

    /// <summary>
    /// A trace result (pmtrace_t): how far a hull got from start to end and
    /// what it hit. Ent is -1 for nothing, 0 for the world.
    /// </summary>
    public struct MoveTrace
    {
        public bool AllSolid, StartSolid, InOpen, InWater;
        public float Fraction;
        public Vec3 EndPos;
        public Vec3 PlaneNormal;
        public float PlaneDist;
        public int Ent;
    }

    /// <summary>
    /// Everything the movement code reads and writes for one player
    /// (TFC's playermove_t, minus engine internals).
    /// </summary>
    public sealed class MoveState
    {
        public int PlayerIndex;
        public bool Server = true;
        public bool Multiplayer = true;

        public Vec3 Origin, Velocity, BaseVelocity;
        public Vec3 Angles, OldAngles, PunchAngle, ViewOfs;
        public Vec3 MoveDir;
        public MoveFlags Flags;
        public MoveType MoveType = MoveType.Walk;
        public int OnGround = -1;
        public int WaterLevel, OldWaterLevel, WaterType = Contents.Empty;
        public int UseHull;
        public bool Dead;
        public int DeadFlag;
        public bool Spectator;
        public int IUser1, IUser3;
        public Buttons OldButtons;

        public float MaxSpeed, ClientMaxSpeed;
        public float Gravity = 1, Friction = 1;
        public float WaterJumpTime;
        public float FallVelocity;
        public float DuckTime;
        public bool InDuck;
        public float TimeStepSound, SwimTime;
        public int StepLeft;

        /// <summary>Server settings sent to clients for prediction ("tfc", "slj", ...).</summary>
        public System.Collections.Generic.Dictionary<string, string> PhysInfo =
            new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>Per hull bounding boxes (standing, ducked, point, large).</summary>
        public Vec3[] PlayerMins =
        {
            new Vec3(-16, -16, -36), new Vec3(-16, -16, -18), Vec3.Zero, new Vec3(-32, -32, -32),
        };
        public Vec3[] PlayerMaxs =
        {
            new Vec3(16, 16, 36), new Vec3(16, 16, 18), Vec3.Zero, new Vec3(32, 32, 32),
        };

        // Working values during one move
        public MoveCommand Cmd;
        public float FrameTime;
        public Vec3 Forward, Right, Up;

        public string Info(string key) => PhysInfo.TryGetValue(key, out var v) ? v : "";
    }
}
