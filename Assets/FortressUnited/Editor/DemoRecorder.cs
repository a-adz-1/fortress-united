using System.Collections.Generic;
using FortressUnited.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FortressUnited.EditorTools
{
    /// <summary>
    /// Records the scripted showcase (Game/Demo/DemoDirector.cs) as frames
    /// for a video. From the command line:
    ///   DEMO_MAP=/path/to/2fort.bsp DEMO_OUT=/tmp/frames [DEMO_MODE=melee] Unity -batchmode
    ///     -projectPath . -executeMethod FortressUnited.EditorTools.DemoRecorder.Record
    /// then: ffmpeg -framerate 30 -i /tmp/frames/frame_%05d.jpg -c:v libx264 -pix_fmt yuv420p demo.mp4
    /// It enters Play mode and quits Unity when the recording is done.
    /// </summary>
    public static class DemoRecorder
    {
        const string Art = "Assets/FortressUnited/Art/";

        [MenuItem("Fortress United/Record Demo", priority = 30)]
        public static void Record()
        {
            string map = System.Environment.GetEnvironmentVariable("DEMO_MAP");
            if (string.IsNullOrEmpty(map))
                map = PlayerPrefs.GetString(MapLoader.LastMapKey, "");
            string output = System.Environment.GetEnvironmentVariable("DEMO_OUT");
            if (string.IsNullOrEmpty(output))
                output = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fortress-united-demo");

            RenderPipelineSetup.SetUp();      // makes sure the renderer has its features (decals, ambient occlusion)
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var d = new GameObject("Demo").AddComponent<DemoDirector>();
            d.MapPath = map;
            d.OutputDir = output;
            T Load<T>(string p) where T : Object => AssetDatabase.LoadAssetAtPath<T>(Art + p);
            d.Medic = Load<GameObject>("Characters/Medic/Medic_Red.fbx");
            d.Sentry1 = Load<GameObject>("Sentry/Sentry_L1.fbx");
            d.Sentry2 = Load<GameObject>("Sentry/Sentry_L2.fbx");
            d.Sentry3 = Load<GameObject>("Sentry/Sentry_L3.fbx");
            d.SentryBlueSkin1 = Load<Texture2D>("Sentry/Sentry_L1_Blue_BaseColor.png");
            d.SentryBlueSkin2 = Load<Texture2D>("Sentry/Sentry_L2_Blue_BaseColor.png");
            d.SentryBlueSkin3 = Load<Texture2D>("Sentry/Sentry_L3_Blue_BaseColor.png");
            d.Conc = Load<GameObject>("Grenades/ConcGrenade.fbx");
            d.Frag = Load<GameObject>("Grenades/FragGrenade.fbx");
            d.Spanner = Load<GameObject>("Spanner/Spanner_Blue.fbx");
            d.Medkit = Load<GameObject>("Medkit/Medkit_Blue.fbx");
            d.MeleeShowcase = System.Environment.GetEnvironmentVariable("DEMO_MODE") == "melee";
            d.LookTour = System.Environment.GetEnvironmentVariable("DEMO_MODE") == "look";
            d.OriginalTextures = System.Environment.GetEnvironmentVariable("DEMO_MATERIALS") == "off";
            d.KeepTextures = System.Environment.GetEnvironmentVariable("DEMO_MATERIALS") == "keep";
            // DEMO_SIZE=2560x1440 renders bigger (scale it down when encoding, for smoother edges)
            var size = System.Environment.GetEnvironmentVariable("DEMO_SIZE");
            if (!string.IsNullOrEmpty(size) && size.Contains("x"))
            {
                var wh = size.Split('x');
                d.Width = int.Parse(wh[0]);
                d.Height = int.Parse(wh[1]);
            }
            d.Stage = System.Environment.GetEnvironmentVariable("DEMO_STAGE") ?? "open";
            d.Arms = Load<GameObject>("Characters/Medic/Medic_Blue_Arms.fbx");
            d.ShockProd = Load<GameObject>("Weapons/ShockProd.fbx");
            d.ShockProdSkin = Load<Texture2D>("Weapons/ShockProd_Blue_BaseColor.png");
            d.Killswitch = Load<GameObject>("Weapons/Killswitch.fbx");
            d.KillswitchSkin = Load<Texture2D>("Weapons/Killswitch_Blue_BaseColor.png");
            var clips = new List<AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Art + "Characters/Medic/Medic_Red.fbx"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
                    clips.Add(c);
            d.MedicClips = clips.ToArray();
            Debug.Log($"Demo: recording {map} to {output}");
            EditorApplication.EnterPlaymode();
        }
    }
}
