using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FortressUnited.Game
{
    /// <summary>
    /// Post-processing for the greybox: bloom, so the plasma arc, screens
    /// and shield ripples glow, and a touch of tonemapping.
    /// </summary>
    public static class GreyboxPostFx
    {
        public static void Add(Camera cam)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.3f);      // only real glows bloom, not sunlit white walls
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.65f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);      // film-like roll-off: sunlit white walls stay detailed instead of clipping

            var go = new GameObject("Post Processing");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }
    }
}
