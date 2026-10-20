using UnityEngine;
using UnityEngine.Rendering;

namespace FortressUnited.Game
{
    /// <summary>
    /// The maps' mood: a bright day (the default theme; night, dusk, snow come
    /// later) with the sun at the map's own angle, blue sky, light haze, and
    /// rooms lit by their own fixtures on top (MapLights),
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
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.8f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(32, -40, 0);

            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_AtmosphereThickness", 0.9f);
            sky.SetColor("_SkyTint", new Color(0.46f, 0.52f, 0.62f));
            sky.SetColor("_GroundColor", new Color(0.36f, 0.38f, 0.42f));
            sky.SetFloat("_Exposure", 1.35f);
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Bright day outside; rooms still get their own fixtures' light (MapLights) on top
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.66f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.41f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.19f, 0.18f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0025f;
            RenderSettings.fogColor = new Color(0.66f, 0.74f, 0.86f);
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
