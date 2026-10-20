using UnityEngine;
using UnityEngine.Rendering;

namespace FortressUnited.Game
{
    /// <summary>
    /// The maps' mood: a low dusk sun (at the map's own angle), a tinted sky,
    /// dim ambient light with rooms lit by their own fixtures (MapLights), fog,
    /// and reflections captured from the map itself (a cubemap rendered from
    /// its middle), so glossy panels and water reflect the map and sky.
    /// Per-map themes (night, snow, desert) come later.
    /// </summary>
    public static class MapAtmosphere
    {
        public static Light Apply(MapLoader map)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.84f, 0.7f);
            sun.intensity = 2.1f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(32, -40, 0);

            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_AtmosphereThickness", 1.35f);
            sky.SetColor("_SkyTint", new Color(0.42f, 0.42f, 0.62f));
            sky.SetColor("_GroundColor", new Color(0.16f, 0.17f, 0.2f));
            sky.SetFloat("_Exposure", 1.15f);
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Low ambient: rooms are lit by their own fixtures (MapLights), so interiors have mood
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.4f, 0.54f);
            RenderSettings.ambientEquatorColor = new Color(0.27f, 0.26f, 0.31f);
            RenderSettings.ambientGroundColor = new Color(0.11f, 0.11f, 0.13f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.006f;
            RenderSettings.fogColor = new Color(0.45f, 0.47f, 0.58f);
            DynamicGI.UpdateEnvironment();

            if (map != null)
            {
                MapLights.Build(map, sun);
                MapScreens.Place(map);
                MapDetail.Build(map);
                CaptureReflections(map.transform);
            }
            return sun;
        }

        /// <summary>Renders the map and sky into a cubemap from the map's middle and uses it for reflections.</summary>
        static void CaptureReflections(Transform map)
        {
            var bounds = new Bounds(map.position, Vector3.zero);
            foreach (var r in map.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(r.bounds);
            var cube = new RenderTexture(256, 256, 16, RenderTextureFormat.DefaultHDR)
            {
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = true,
                name = "Map Reflections",
            };
            var go = new GameObject("Reflection Capture");
            var cam = go.AddComponent<Camera>();
            cam.transform.position = bounds.center;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.enabled = false;
            bool ok = cam.RenderToCubemap(cube);
            Object.Destroy(go);
            if (!ok)
                return;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
        }
    }
}
