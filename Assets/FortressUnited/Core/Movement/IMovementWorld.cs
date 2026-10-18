namespace FortressUnited.Core.Movement
{
    /// <summary>
    /// What the movement code needs from the world. In the game this is
    /// implemented on top of Unity physics; in tests by a simple brush world
    /// that matches the reference harness.
    /// </summary>
    public interface IMovementWorld
    {
        /// <summary>
        /// Sweep the player's current hull (state.UseHull) from start to end.
        /// ignoreEnt is an entity to skip, or -1.
        /// </summary>
        MoveTrace PlayerTrace(MoveState state, Vec3 start, Vec3 end, int ignoreEnt);

        /// <summary>Contents at a point. trueContents gets the raw value (water currents).</summary>
        int PointContents(Vec3 point, out int trueContents);

        /// <summary>The entity the player's hull overlaps at pos, or -1 if free.</summary>
        int TestPlayerPosition(MoveState state, Vec3 pos);

        /// <summary>True if the entity is a player (for unsticking).</summary>
        bool IsPlayer(int ent);

        /// <summary>True if the entity is a brush model (for unsticking).</summary>
        bool IsBrushModel(int ent);

        /// <summary>
        /// The ladder volume the player's hull is touching, or null.
        /// </summary>
        ILadder FindLadder(MoveState state);

        /// <summary>Seconds since start (Sys_FloatTime), for the unstick throttle.</summary>
        double Time { get; }

        /// <summary>Uniform random integer in [low, high] (only used for sounds).</summary>
        int RandomLong(int low, int high);

        /// <summary>A movement sound (jump, landing, water, ...).</summary>
        void PlaySound(MoveState state, string sample, float volume);

        /// <summary>Called when the player is found stuck in an entity.</summary>
        void StuckTouch(MoveState state, int ent);
    }

    /// <summary>A ladder volume.</summary>
    public interface ILadder
    {
        Vec3 Mins { get; }
        Vec3 Maxs { get; }

        /// <summary>Trace a point from start to end against the ladder volume only.</summary>
        MoveTrace Trace(Vec3 start, Vec3 end);
    }
}
