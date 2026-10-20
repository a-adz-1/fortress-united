using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FortressUnited.Game
{
    /// <summary>
    /// A body after death. It ragdolls, then freezes once it has settled (or
    /// after a few seconds regardless), so it never jitters or hangs off
    /// ledges in odd poses for long. After Lifetime seconds it fades out and
    /// is removed. Only MaxBodies stay at once; the oldest fades early.
    /// A chest screen shows the last heartbeats running down to a flat line.
    /// </summary>
    public sealed class Corpse : MonoBehaviour
    {
        public float Lifetime = 20;
        public float FadeSeconds = 1.5f;
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
                float alpha = 1 - Mathf.Clamp01(fade);
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
            var ripple = GetComponent<HitRipple>();
            if (ripple != null)
                Destroy(ripple);
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                // Drop the shield ripple layer, then fade instances of the rest
                var keep = new List<Material>();
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader.name != "FortressUnited/ShieldRipple")
                        keep.Add(m);
                r.sharedMaterials = keep.ToArray();
                foreach (var m in r.materials)   // instances, so other bodies aren't affected
                {
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
