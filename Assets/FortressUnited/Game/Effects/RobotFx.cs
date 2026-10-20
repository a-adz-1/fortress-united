using System.Collections.Generic;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// Everything that shows on a robot's body, in place of TFC's blood and
    /// flames (RobotOverlay shader, drawn over every mesh of the character):
    /// - Hit: rings of the team colour run out from the hit point; big hits
    ///   flash the outline
    /// - Heat (on fire): orange pulse and steam venting
    /// - Virus (infected): green corrupted rows and green sparks
    /// - Glitch (concussed, or powering down): flickering rows
    /// - Teleport: beams out bottom to top
    /// - Sparks: a burst from the chest, e.g. on death
    /// </summary>
    public sealed class RobotFx : MonoBehaviour
    {
        public Color Colour = TeamColours.Blue;
        public float Intensity = 2.5f;

        [Range(0, 1)] public float Heat;
        [Range(0, 1)] public float Virus;
        [Range(0, 1)] public float Glitch;
        /// <summary>Beam-out progress 0-1; below 0 is off.</summary>
        public float Teleport = -1;

        public Material Material { get; private set; }
        Transform core;
        ParticleSystem sparks, virusSparks, steam;
        readonly Vector4[] hits = new Vector4[4];
        Vector4 strength = Vector4.zero;
        int next;
        float flash;
        float nextVirusCrackle;
        readonly List<Renderer> renderers = new List<Renderer>();

        static readonly int HitPos = Shader.PropertyToID("_HitPos");
        static readonly int HitStrength = Shader.PropertyToID("_HitStrength");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int ColourId = Shader.PropertyToID("_Color");
        static readonly int NowId = Shader.PropertyToID("_Now");
        static readonly int HeatId = Shader.PropertyToID("_Heat");
        static readonly int VirusId = Shader.PropertyToID("_Virus");
        static readonly int GlitchId = Shader.PropertyToID("_Glitch");
        static readonly int TeleportId = Shader.PropertyToID("_Teleport");
        static readonly int BodyY = Shader.PropertyToID("_BodyY");

        public const string ShaderName = "FortressUnited/RobotOverlay";

        /// <param name="core">Where sparks and steam come from (the chest); defaults to the body's centre.</param>
        public static RobotFx Attach(GameObject character, Color colour, Transform core = null)
        {
            var fx = character.AddComponent<RobotFx>();
            fx.Colour = colour;
            fx.core = core;
            fx.Setup();
            return fx;
        }

        void Setup()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning("Fortress United: RobotOverlay shader not found");
                return;
            }
            Material = new Material(shader) { name = "RobotOverlay" };
            Material.SetColor(ColourId, TeamColours.Glow(Colour, Intensity));
            for (int i = 0; i < hits.Length; i++)
                hits[i] = new Vector4(0, 0, 0, -1000);
            Material.SetVectorArray(HitPos, hits);

            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer))
                    continue;
                var mats = new List<Material>(r.sharedMaterials) { Material };
                r.sharedMaterials = mats.ToArray();
                renderers.Add(r);
            }

            if (core == null)
            {
                core = new GameObject("Core").transform;
                core.SetParent(transform, false);
                core.position = Bounds().center;
            }
            sparks = FxParticles.Sparks(core, new Color(1f, 0.85f, 0.5f));
            virusSparks = FxParticles.Sparks(core, new Color(0.4f, 1f, 0.5f));
            steam = FxParticles.Steam(core);
        }

        /// <summary>A hit at a world position. Damage scales the ripple; ~25 is a normal hit.</summary>
        public void Hit(Vector3 point, float damage)
        {
            if (Material == null)
                return;
            hits[next] = new Vector4(point.x, point.y, point.z, Time.timeSinceLevelLoad);
            strength[next] = Mathf.Clamp(damage / 25f, 0.4f, 2.5f);
            next = (next + 1) % hits.Length;
            flash = Mathf.Max(flash, Mathf.Clamp(damage / 50f, 0.2f, 1.2f));
            Material.SetVectorArray(HitPos, hits);
            Material.SetVector(HitStrength, strength);
        }

        /// <summary>A shower of sparks from the chest.</summary>
        public void SparkBurst(int count = 30) => sparks?.Emit(count);

        void Update()
        {
            if (Material == null)
                return;
            float now = Time.timeSinceLevelLoad;
            flash = Mathf.MoveTowards(flash, 0, Time.deltaTime * 3);
            Material.SetFloat(NowId, now);
            Material.SetFloat(FlashId, flash);
            Material.SetFloat(HeatId, Heat);
            Material.SetFloat(VirusId, Virus);
            Material.SetFloat(GlitchId, Glitch);
            Material.SetFloat(TeleportId, Teleport);
            if (Teleport >= 0 || Virus > 0)
            {
                var b = Bounds();
                Material.SetVector(BodyY, new Vector4(b.min.y, b.max.y, 0, 0));
            }

            // Steam vents while hot
            var emission = steam.emission;
            emission.rateOverTime = Heat * 40;

            // Infected circuits crackle now and then
            if (Virus > 0 && now >= nextVirusCrackle)
            {
                virusSparks.Emit(Random.Range(3, 8));
                nextVirusCrackle = now + Random.Range(0.4f, 1.6f) / Mathf.Max(0.2f, Virus);
            }
        }

        Bounds Bounds()
        {
            var b = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        void OnDestroy()
        {
            if (Material != null)
                Destroy(Material);
        }
    }
}
