using System.IO;
using UnityEditor;
using UnityEngine;

namespace FortressUnited.EditorTools
{
    /// <summary>
    /// Makes an imported Asset Store pack's pieces placeable with fu_prop: each prefab
    /// in the pack's prop folders gets a copy in Resources/ThirdParty/Props named
    /// kit_&lt;name&gt; (lower case, spaces as underscores), which PropLibrary finds by name.
    /// The pack and the copies stay out of git (their licence forbids sharing them);
    /// a map using kit_ props shows nothing for players without the pack.
    ///
    ///     Unity -batchmode -projectPath . -executeMethod FortressUnited.EditorTools.AssetPackProps.Register -quit
    /// </summary>
    public static class AssetPackProps
    {
        const string Out = "Assets/FortressUnited/Resources/ThirdParty/Props";
        static readonly string[] Packs = { "Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Prefabs" };

        [MenuItem("Fortress United/Register Asset Pack Props", priority = 50)]
        public static void Register()
        {
            Directory.CreateDirectory(Out);
            int n = 0;
            foreach (var pack in Packs)
            {
                if (!AssetDatabase.IsValidFolder(pack)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { pack }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string name = "kit_" + Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
                    string dst = $"{Out}/{name}.prefab";
                    var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(src);
                    PrefabUtility.SaveAsPrefabAsset(instance, dst);
                    Object.DestroyImmediate(instance);
                    n++;
                }
            }
            AssetDatabase.SaveAssets();
            File.WriteAllLines(Path.Combine(Out, "..", "props.txt"), System.Array.ConvertAll(
                AssetDatabase.FindAssets("t:Prefab", new[] { Out }), g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g))));
            Debug.Log($"Fortress United: registered {n} asset pack props in {Out}");
            RegisterSurfaces();
        }

        const string Kit = "Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Textures/";
        const string SurfaceOut = "Assets/FortressUnited/Resources/ThirdParty/Kit";

        /// <summary>
        /// The kit's wall and floor texture sets, as materials the game loads by name
        /// (Resources/ThirdParty/Kit/Wall_01 etc.) for restyling map surfaces (KitSurface).
        /// </summary>
        public static void RegisterSurfaces()
        {
            if (!AssetDatabase.IsValidFolder(Kit.TrimEnd('/'))) return;
            Directory.CreateDirectory(SurfaceOut);
            int n = 0;
            foreach (var set in new[] { "Wall_01", "Wall_07", "Wall_08", "Floor_01", "Floor_02" })
            {
                Texture2D Load(string suffix, string ext = ".png") =>
                    AssetDatabase.LoadAssetAtPath<Texture2D>($"{Kit}HD_{set}_Dif_Style_02_{suffix}{ext}");
                var alb = Load("Alb");
                if (alb == null) continue;
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = set };
                m.SetTexture("_BaseMap", alb);
                m.SetTexture("_BumpMap", Load("Norm"));
                m.SetTexture("_MetallicGlossMap", Load("Metal", ".tif"));
                m.SetTexture("_EmissionMap", Load("Illum"));
                AssetDatabase.CreateAsset(m, $"{SurfaceOut}/{set}.mat");
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Fortress United: registered {n} asset pack surface sets in {SurfaceOut}");
        }
    }
}
