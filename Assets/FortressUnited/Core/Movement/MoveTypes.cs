namespace FortressUnited.Core.Movement
{
    /// <summary>Movement modes (TFC's MOVETYPE_* values).</summary>
    public enum MoveType
    {
        None = 0,
        Walk = 3,
        Toss = 6,
        Fly = 5,
        Bounce = 10,
        BounceMissile = 11,
        FlyMissile = 9,
        Noclip = 8,
    }

    /// <summary>Input buttons (IN_*).</summary>
    [System.Flags]
    public enum Buttons
    {
        None = 0,
        Attack = 1 << 0,
        Jump = 1 << 1,
        Duck = 1 << 2,
        Forward = 1 << 3,
        Back = 1 << 4,
        Use = 1 << 5,
        Cancel = 1 << 6,
        Left = 1 << 7,
        Right = 1 << 8,
        MoveLeft = 1 << 9,
        MoveRight = 1 << 10,
        Attack2 = 1 << 11,
        Run = 1 << 12,
        Reload = 1 << 13,
        Alt1 = 1 << 14,
        Score = 1 << 15,
    }

    /// <summary>Player flags the movement code reads or writes (FL_*).</summary>
    [System.Flags]
    public enum MoveFlags
    {
        None = 0,
        OnGround = 1 << 9,
        WaterJump = 1 << 11,
        Frozen = 1 << 12,
        Ducking = 1 << 14,
        OnTrain = 1 << 24,
    }

    /// <summary>Point contents (CONTENTS_*): negative numbers, as in the original.</summary>
    public static class Contents
    {
        public const int Empty = -1;
        public const int Solid = -2;
        public const int Water = -3;
        public const int Slime = -4;
        public const int Lava = -5;
        public const int Sky = -6;
        public const int Ladder = -16;
        public const int Current0 = -9;
        public const int Current90 = -10;
        public const int Current180 = -11;
        public const int Current270 = -12;
        public const int CurrentUp = -13;
        public const int CurrentDown = -14;
        public const int Translucent = -15;
    }

    /// <summary>Hulls: 0 standing, 1 crouched, 2 point, 3 large.</summary>
    public static class Hulls
    {
        public const int Standing = 0;
        public const int Ducked = 1;
        public const int Point = 2;
    }
}
