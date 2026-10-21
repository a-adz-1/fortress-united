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
        }
    }
}
