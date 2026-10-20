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
    /// Models under Art/Characters (tools/meshy/prepare_character.py) import
    /// as Humanoid, so any humanoid animation plays on any class, with
    /// their clips looping in place.
    /// </summary>
    public sealed class ArtPostprocessor : AssetPostprocessor
    {
        const string Root = "Assets/FortressUnited/Art/";
        const string Characters = Root + "Characters/";
        // Clips that play once rather than loop
        static readonly HashSet<string> OneShots = new HashSet<string> { "Jump", "Heal", "Death" };
        static readonly string[] TextureSuffixes = { "_BaseColor", "_Red_BaseColor", "_Normal", "_MetallicSmoothness", "_Emission" };

        // Bump to make Unity reimport everything this processes
        public override uint GetVersion() => 9;
        // After URP's own material description preprocessor
        public override int GetPostprocessOrder() => 100;

        void OnPreprocessTexture()
        {
            // Team logos: readable (decals use a tinted copy), clamped, alpha from the PNG
            if (assetPath.StartsWith("Assets/FortressUnited/Resources/Teams/"))
            {
                var logo = (TextureImporter)assetImporter;
                logo.isReadable = true;
                logo.wrapMode = TextureWrapMode.Clamp;
                logo.alphaSource = TextureImporterAlphaSource.FromInput;
                logo.alphaIsTransparency = true;
                logo.mipmapEnabled = true;
                return;
            }
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
            // Declare the textures, so they import first and loading them in
            // OnPostprocessMaterial works (and edits to them reimport the model)
            string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(assetPath);
            foreach (var suffix in TextureSuffixes)
            {
                string tex = $"{dir}/{name}{suffix}.png";
                if (File.Exists(tex))
                    context.DependsOnArtifact(tex);
            }

            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.useFileScale = true;
            mi.globalScale = 1;
            if (assetPath.StartsWith(Characters))
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = true;
                mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Characters))
                return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                // Strip the Blender "Rig|" prefixes; loop in place, the game moves the body
                int bar = c.name.LastIndexOf('|');
                if (bar >= 0) c.name = c.name.Substring(bar + 1);
                c.loopTime = !OneShots.Contains(c.name);
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }

        // Models import their materials via material descriptions, which calls this
        void OnPreprocessMaterialDescription(UnityEditor.AssetImporters.MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (assetPath.StartsWith(Root))
                SetUp(material, description.materialName);
        }

        void OnPostprocessMaterial(Material material)
        {
            if (assetPath.StartsWith(Root))
                SetUp(material, material.name);
        }

        void SetUp(Material material, string materialName)
        {
            string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(assetPath);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null && material.shader != lit)
                material.shader = lit;

            if (materialName.StartsWith("Screen"))
            {
                material.SetColor("_BaseColor", new Color(0.02f, 0.05f, 0.06f));
                return;
            }

            // Team-skinned art (sentries) has _Red_ / _Blue_ base colours; red is the default
            var baseTex = Load(dir, name + "_BaseColor") ?? Load(dir, name + "_Red_BaseColor");
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
