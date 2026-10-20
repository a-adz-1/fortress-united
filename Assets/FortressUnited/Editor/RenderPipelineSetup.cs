using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FortressUnited.EditorTools
{
    /// <summary>
    /// Creates the project's URP asset (HDR on, for glowing effects) with the
    /// decal feature (team logos on models), and
    /// makes it the active pipeline for graphics and every quality level.
    /// Without it Unity falls back to the built-in renderer and every URP
    /// material draws magenta.
    /// </summary>
    public static class RenderPipelineSetup
    {
        /// <summary>The decal feature, for team logos projected onto models (LogoDecal).</summary>
        static void AddDecals(UniversalRendererData data)
        {
            if (data == null || data.rendererFeatures.Exists(f => f is DecalRendererFeature))
                return;
            var feature = ScriptableObject.CreateInstance<DecalRendererFeature>();
            feature.name = "Decals";
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature);
            // The renderer keeps a map of its features' file IDs alongside the list
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long id);
            var so = new SerializedObject(data);
            var map = so.FindProperty("m_RendererFeatureMap");
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
            Debug.Log("Fortress United: decal feature added to the renderer");
        }

        const string Folder = "Assets/FortressUnited/Settings";
        const string AssetPath = Folder + "/FortressUnited_URP.asset";
        const string RendererPath = Folder + "/FortressUnited_Renderer.asset";

        [MenuItem("Fortress United/Set Up Render Pipeline", priority = 40)]
        public static void SetUp()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (asset == null)
            {
                if (!AssetDatabase.IsValidFolder(Folder))
                    AssetDatabase.CreateFolder("Assets/FortressUnited", "Settings");
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                asset.supportsHDR = true;
                asset.msaaSampleCount = 4;
                asset.shadowDistance = 60;
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            AddDecals(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath));

            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"Fortress United: render pipeline set to {AssetPath}");
        }
    }
}
