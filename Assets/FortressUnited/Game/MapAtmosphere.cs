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
    /// Mappers choose the time of day and override the sun and fog with
    /// worldspawn keys (MapSettings).
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

            var settings = MapSettings.Current;
            if (settings.Time == "dusk") Dusk(sun, sky);
            else if (settings.Time == "night") Night(sun, sky);
            RenderSettings.fogDensity = settings.Fog * (settings.Time == "dusk" ? 1.5f : 1);
            RenderSettings.fog = settings.Fog > 0;

            // The map's own sky: always if asked, or by default when it's dark (space, night),
            // which a daytime sky can't stand in for
            bool space = false;
            Material spaceSky = null;
            if (map != null && settings.Sky != "procedural")
            {
                var own = settings.Sky == "space" ? null : MapSky.Load(settings.SkyName, map.GameFolders);
                // A starfield (the space sky, or a near-black one) becomes the modern space sky
                bool starfield = settings.Sky == "space" ||
                    (settings.Sky == "auto" && own.HasValue && (own.Value.brightness < 0.02f || LooksLikeSpace(settings.SkyName)));
                if (starfield)
                {
                    spaceSky = new Material(Shader.Find("FortressUnited/SpaceSky")) { name = "Space sky" };
                    RenderSettings.skybox = spaceSky;
                    space = true;
                    Space(sun);
                }
                else if (own.HasValue && (settings.Sky == "map" || own.Value.brightness < 0.12f))
                {
                    RenderSettings.skybox = own.Value.sky;
                    space = own.Value.brightness < 0.12f;
                    if (space) Space(sun);
                }
            }
            DynamicGI.UpdateEnvironment();

            if (map != null)
            {
                MapEntities.ReadVolumes(map);
                MapLights.Build(map, sun);
                if (space)
                    UseMapSunColour(map, sun);
                if (spaceSky != null)
                {
                    // The sky's sun where the map's sun is, and its giant planet across the sky from it
                    Vector3 toSun = -sun.transform.forward;
                    spaceSky.SetVector("_SunDir", toSun);
                    spaceSky.SetColor("_SunColor", sun.color);
                    var flat = new Vector3(-toSun.x, 0, -toSun.z);
                    if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
                    spaceSky.SetVector("_PlanetDir", (flat.normalized * 0.9f + Vector3.up * 0.3f).normalized);
                }
                if (settings.Time == "dusk")
                    sun.transform.rotation = Quaternion.Euler(Mathf.Min(sun.transform.eulerAngles.x, 14), sun.transform.eulerAngles.y, 0);
                MapEntities.BuildSigns(map);
                int lights = MapEntities.BuildLights(map), props = MapEntities.BuildProps(map), fx = MapEntities.BuildParticles(map);
                if (lights + props + fx > 0)
                    Debug.Log($"Fortress United: mapper extras: {lights} lights, {props} props, {fx} particle effects");
                MapScreens.Place(map);
                MapDetail.Build(map);
                CaptureReflections(map.transform);
            }
            return sun;
        }

        /// <summary>
        /// Space (a dark map sky): no air, so no haze; hard sunlight and a moderate fill,
        /// so lit ground is bright against a black sky and shadows stay readable.
        /// </summary>
        static void Space(Light sun)
        {
            RenderSettings.fog = false;
            sun.intensity = 1.7f;
            // Fill strong enough that shadows on the ground stay readable (GoldSrc lit these maps evenly)
            RenderSettings.ambientSkyColor = new Color(0.3f, 0.29f, 0.36f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.32f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.24f);
        }

        static bool LooksLikeSpace(string sky)
        {
            string s = (sky ?? "").ToLowerInvariant();
            return s.Contains("space") || s.Contains("star") || s.Contains("neb") || s.Contains("galax") || s.Contains("void");
        }

        /// <summary>In space the map's own sun colour is used in full (on a day map only a hint of it).</summary>
        static void UseMapSunColour(MapLoader map, Light sun)
        {
            if (MapSettings.Current.SunColor.HasValue) return;
            foreach (var e in map.Bsp.Entities)
            {
                if (e.ClassName != "light_environment") continue;
                var p = e.Get("_light").Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3) return;
                float r = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
                float g = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                float b = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);
                float m = Mathf.Max(r, Mathf.Max(g, b), 1);
                sun.color = Color.Lerp(Color.white, new Color(r / m, g / m, b / m), 0.6f);
                return;
            }
        }

        /// <summary>Dusk: a low warm sun, a violet sky, long shadows, rooms carried by their fixtures.</summary>
        static void Dusk(Light sun, Material sky)
        {
            sun.color = new Color(1f, 0.6f, 0.38f);
            sun.intensity = 1.4f;
            sky.SetColor("_SkyTint", new Color(0.6f, 0.42f, 0.55f));
            sky.SetFloat("_AtmosphereThickness", 1.7f);
            sky.SetFloat("_Exposure", 1.0f);
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.3f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.26f, 0.21f, 0.26f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.07f, 0.08f);
            RenderSettings.fogColor = new Color(0.62f, 0.46f, 0.48f);
        }

        /// <summary>Night: a cool dim moon, a dark sky; the glows, signs and fixtures light the map.</summary>
        static void Night(Light sun, Material sky)
        {
            sun.color = new Color(0.55f, 0.66f, 1f);
            sun.intensity = 0.35f;
            sky.SetColor("_SkyTint", new Color(0.2f, 0.25f, 0.45f));
            sky.SetFloat("_Exposure", 0.18f);
            sky.SetFloat("_SunSize", 0.02f);
            RenderSettings.ambientSkyColor = new Color(0.07f, 0.09f, 0.16f);
            RenderSettings.ambientEquatorColor = new Color(0.05f, 0.055f, 0.09f);
            RenderSettings.ambientGroundColor = new Color(0.02f, 0.02f, 0.03f);
            RenderSettings.fogColor = new Color(0.05f, 0.06f, 0.11f);
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
