using System.Collections.Generic;

namespace FortressUnited.Game
{
    /// <summary>
    /// What the base screens show: scores, each flag's state, the clock and a
    /// news ticker. Game rules fill it in as the match runs (for now the demo
    /// does); the screens just read it every frame.
    /// </summary>
    public static class MatchInfo
    {
        public enum FlagState { Home, Taken, Dropped }

        /// <summary>Scores by team (index 0 = blue, 1 = red, 2 = gold, 3 = violet).</summary>
        public static readonly int[] Scores = new int[4];
        public static readonly FlagState[] Flags = new FlagState[4];
        /// <summary>Who holds each team's flag while it's taken.</summary>
        public static readonly string[] Carriers = new string[4];
        /// <summary>Seconds left in the match.</summary>
        public static float TimeLeft = 30 * 60;
        /// <summary>Teams in play (2 to 4).</summary>
        public static int Teams = 2;
        /// <summary>Recent events, newest last, scrolled along the bottom of the screens.</summary>
        public static readonly List<string> Ticker = new List<string>
        {
            "ALL SYSTEMS NOMINAL", "DEFENCE GRID ONLINE", "SENTRY UPLINK ESTABLISHED",
        };

        public static void Post(string message)
        {
            Ticker.Add(message);
            if (Ticker.Count > 8)
                Ticker.RemoveAt(0);
        }
    }
}
