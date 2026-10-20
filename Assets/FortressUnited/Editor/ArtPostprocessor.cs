using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FortressUnited.EditorTools
{
    /// <summary>
    /// Imports art under Assets/FortressUnited/Art by naming convention (the
    /// names tools/meshy/prepare_model.py writes):
    ///   Name.fbx                       the model
    ///   Name_BaseColor.png             colour
    ///   Name_Normal.png                normal map
    ///   Name_MetallicSmoothness.png    R metallic, A smoothness (URP layout)
    ///   Name_Emission.png              glow (optional)
    /// Materials named "Screen..." are left for the game to draw on.
    /// </summary>
    public sealed class ArtPostprocessor : AssetPostprocessor
    {
        const string Root = "Assets/FortressUnited/Art/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root))
                return;
            var ti = (TextureImporter)assetImporter;
            string n = Path.GetFileNameWithoutExtension(assetPath);
            if (n.EndsWith("_Normal"))
                ti.textureType = TextureImporterType.NormalMap;
            else if (n.EndsWith("_MetallicSmoothness"))
            {
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
            }
            ti.maxTextureSize = 2048;
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root))
                return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.useFileScale = true;
            mi.globalScale = 1;
        }

        void OnPostprocessMaterial(Material material)
        {
            if (!assetPath.StartsWith(Root))
                return;
            string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(assetPath);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null && material.shader != lit)
                material.shader = lit;

            if (material.name.StartsWith("Screen"))
            {
                material.SetColor("_BaseColor", new Color(0.02f, 0.05f, 0.06f));
                return;
            }

            var baseTex = Load(dir, name + "_BaseColor");
            var normal = Load(dir, name + "_Normal");
            var ms = Load(dir, name + "_MetallicSmoothness");
            var emission = Load(dir, name + "_Emission");

            if (baseTex != null)
            {
                material.SetTexture("_BaseMap", baseTex);
                material.SetColor("_BaseColor", Color.white);
            }
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 1);
                material.EnableKeyword("_NORMALMAP");
            }
            if (ms != null)
            {
                material.SetTexture("_MetallicGlossMap", ms);
                material.SetFloat("_Smoothness", 1);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else
            {
                material.SetFloat("_Smoothness", 0.3f);
            }
            if (emission != null)
            {
                material.SetTexture("_EmissionMap", emission);
                material.SetColor("_EmissionColor", Color.white);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }

        static Texture2D Load(string dir, string name) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}.png");

        /// <summary>When textures arrive after their model, reimport the model to hook them up.</summary>
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var models = new HashSet<string>();
            foreach (var path in imported)
            {
                if (!path.StartsWith(Root) || !path.EndsWith(".png"))
                    continue;
                string dir = Path.GetDirectoryName(path).Replace('\\', '/');
                string n = Path.GetFileNameWithoutExtension(path);
                int cut = n.LastIndexOf('_');
                if (cut <= 0)
                    continue;
                string fbx = $"{dir}/{n.Substring(0, cut)}.fbx";
                if (File.Exists(fbx))
                    models.Add(fbx);
            }
            foreach (var fbx in models)
                AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceUpdate);
        }
    }
}
