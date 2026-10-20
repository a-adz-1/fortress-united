using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FortressUnited.Game
{
    /// <summary>
    /// A robot's body after it's destroyed. It ragdolls, sparking and
    /// glitching as it powers down, then freezes once it has settled (or
    /// after a few seconds regardless), so it never jitters or hangs off
    /// ledges in odd poses for long. After Lifetime seconds it beams out and
    /// is removed. Only MaxBodies stay at once; the oldest beams out early.
    /// A chest screen shows the last core pulses running down to a flat line.
    /// </summary>
    public sealed class Corpse : MonoBehaviour
    {
        public float Lifetime = 20;
        public float FadeSeconds = 1.5f;
        [Tooltip("Sparking and glitching while it powers down.")]
        public float PowerDownSeconds = 3;
        [Tooltip("Freeze when every part is slower than this (m/s) ...")]
        public float SettleSpeed = 0.12f;
        [Tooltip("... for this long.")]
        public float SettleTime = 0.6f;
        [Tooltip("Freeze regardless after this long.")]
        public float MaxPhysicsTime = 5;
        public static int MaxBodies = 12;

        public PlayerVitals Vitals { get; private set; }

        static readonly List<Corpse> all = new List<Corpse>();

        List<Rigidbody> bodies;
        float age, stillFor, fade = -1;
        bool frozen;
        EcgTrace ecg;
        Material screenMaterial;
        RobotFx fx;
        float nextSpark;
        Light beamLight;
        readonly List<Material> fadeMaterials = new List<Material>();
        readonly List<Color> baseColours = new List<Color>();

        /// <summary>
        /// Makes a body from a character standing in its bind pose: builds
        /// the ragdoll and sets it moving. Velocity in m/s (e.g. the player's
        /// velocity plus the kick from whatever killed them).
        /// </summary>
        public static Corpse Create(GameObject character, BodyBones bones, Vector3 velocity, float heartRate, Transform screenAnchor = null)
        {
            var c = character.AddComponent<Corpse>();
            var animator = character.GetComponentInChildren<Animator>();
            if (animator != null)
                animator.enabled = false;
            c.bodies = Ragdoll.Build(bones);
            foreach (var rb in c.bodies)
                rb.linearVelocity = velocity;
            // A little spin so bodies don't fall like planks
            bones.Spine.GetComponent<Rigidbody>().AddTorque(Random.insideUnitSphere * 8, ForceMode.VelocityChange);

            c.fx = character.GetComponent<RobotFx>();
            if (c.fx != null)
            {
                c.fx.SparkBurst(40);
                c.fx.Glitch = 1;
            }

            c.Vitals = new PlayerVitals { Health = 0 };
            c.Vitals.Dead = true;
            c.Vitals.SetHeartRate(heartRate);
            if (screenAnchor != null)
                c.AddScreen(screenAnchor);

            all.Add(c);
            if (all.Count > MaxBodies)
                all[0].FadeOut();
            return c;
        }

        void AddScreen(Transform anchor)
        {
            ecg = new EcgTrace(128, 48) { Grid = false, SweepSeconds = 2.5f };
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = "ECG Screen";
            quad.transform.SetParent(anchor, false);
            screenMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = ecg.Texture };
            var r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = screenMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        public void FadeOut()
        {
            if (fade < 0)
                fade = 0;
        }

        void Update()
        {
            age += Time.deltaTime;
            Vitals.Update(Time.deltaTime);
            ecg?.Step(Vitals, Time.deltaTime);

            // Powering down: sparks and glitching that die away
            if (fx != null && age < PowerDownSeconds)
            {
                float left = 1 - age / PowerDownSeconds;
                fx.Glitch = left;
                if (age >= nextSpark)
                {
                    fx.SparkBurst(Mathf.RoundToInt(Random.Range(4, 14) * left));
                    nextSpark = age + Random.Range(0.15f, 0.6f) / Mathf.Max(0.2f, left);
                }
            }
            else if (fx != null && fade < 0)
                fx.Glitch = 0;

            if (!frozen)
            {
                bool still = true;
                foreach (var rb in bodies)
                    if (rb.linearVelocity.sqrMagnitude > SettleSpeed * SettleSpeed) { still = false; break; }
                stillFor = still ? stillFor + Time.deltaTime : 0;
                if (stillFor >= SettleTime || age >= MaxPhysicsTime)
                    Freeze();
            }

            if (age >= Lifetime)
                FadeOut();
            if (fade >= 0)
            {
                if (fade == 0)
                    PrepareFade();
                fade += Time.deltaTime / FadeSeconds;
                // Beam out: the band sweeps up while the armour fades behind it
                if (fx != null)
                    fx.Teleport = Mathf.Clamp01(fade);
                if (beamLight != null)
                    beamLight.intensity = 3 * Mathf.Sin(Mathf.Clamp01(fade) * Mathf.PI);
                float alpha = 1 - Mathf.Clamp01((fade - 0.2f) / 0.8f);
                for (int i = 0; i < fadeMaterials.Count; i++)
                {
                    var c = baseColours[i];
                    c.a *= alpha;
                    fadeMaterials[i].color = c;
                }
                if (fade >= 1)
                    Destroy(gameObject);
            }
        }

        void Freeze()
        {
            frozen = true;
            foreach (var rb in bodies)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
        }

        /// <summary>Switches the body's materials to transparent so they can fade.</summary>
        void PrepareFade()
        {
            fade = 0.0001f;
            if (fx != null)
            {
                fx.Glitch = 0;
                var lightGo = new GameObject("Beam Light");
                lightGo.transform.SetParent(transform, false);
                lightGo.transform.position = bodies[0].position + Vector3.up * 0.3f;
                beamLight = lightGo.AddComponent<Light>();
                beamLight.type = LightType.Point;
                beamLight.color = fx.Colour;
                beamLight.range = 3;
                beamLight.intensity = 0;
                beamLight.shadows = LightShadows.None;
            }
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer)
                    continue;
                r.shadowCastingMode = ShadowCastingMode.Off;
                // Fade copies of the body's own materials (so other bodies aren't
                // affected); the robot overlay stays shared so RobotFx drives it
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader.name == RobotFx.ShaderName)
                        continue;
                    m = mats[i] = new Material(m);
                    if (m.HasProperty("_Surface"))
                    {
                        m.SetFloat("_Surface", 1);
                        m.SetFloat("_Blend", 0);
                        m.SetOverrideTag("RenderType", "Transparent");
                        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                        m.SetFloat("_ZWrite", 0);
                        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        m.renderQueue = (int)RenderQueue.Transparent;
                    }
                    fadeMaterials.Add(m);
                    baseColours.Add(m.color);
                }
                r.sharedMaterials = mats;
            }
        }

        void OnDestroy()
        {
            all.Remove(this);
            if (ecg != null)
                Destroy(ecg.Texture);
        }
    }
}
