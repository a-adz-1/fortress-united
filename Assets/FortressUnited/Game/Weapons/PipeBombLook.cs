using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// The demoman's pipe bombs share one model (Art/Grenades/PipeBomb). The
    /// bands glow in the team colour; the window and end caps glow in the
    /// bomb type's colour: green for detonated pipe bombs (right click), blue
    /// for the grenade launcher's grenades. tools/meshy/pipe_glow.py makes
    /// one emission texture per team and type.
    /// </summary>
    public static class PipeBombLook
    {
        public enum Kind { DetPipe, LauncherGrenade }

        /// <summary>The emission texture name for a team and kind (in Art/Grenades).</summary>
        public static string EmissionName(bool red, Kind kind) =>
            $"PipeBomb_Emission_{(red ? "Red" : "Blue")}_{(kind == Kind.DetPipe ? "Green" : "Blue")}";

        /// <summary>Applies the team and kind glow to a spawned pipe bomb, given its emission texture.</summary>
        public static void Apply(GameObject pipe, Texture2D emission, float brightness = 1.6f)
        {
            foreach (var r in pipe.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetTexture("_EmissionMap", emission);
                    m.SetColor("_EmissionColor", Color.white * brightness);
                }
        }
    }
}
