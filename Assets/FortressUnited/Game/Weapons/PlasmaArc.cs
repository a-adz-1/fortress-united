using System.Collections.Generic;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// A live plasma arc jumping between emitter points on a model (the
    /// plasma spanner's jaws): a few jagged bolts redrawn many times a second,
    /// each with a bright core and a soft glow, plus a flickering light.
    /// Surge() makes it flare, for a swing or a hit. Emitters are found by
    /// name (Arc_*) under the model, as tools/meshy/prepare_model.py
    /// --marker writes them.
    /// </summary>
    public sealed class PlasmaArc : MonoBehaviour
    {
        public Color Color = new Color(0.35f, 0.75f, 1f);
        public int Bolts = 3;
        public int Segments = 14;
        [Tooltip("Sideways jitter as a fraction of the bolt's length.")]
        public float Jaggedness = 0.18f;
        public float CoreWidth = 0.0012f, GlowWidth = 0.006f;
        public float RedrawInterval = 0.045f;
        public float LightRange = 0.6f, LightIntensity = 0.8f;

        readonly List<Transform> emitters = new List<Transform>();
        readonly List<LineRenderer> cores = new List<LineRenderer>(), glows = new List<LineRenderer>();
        Light glowLight;
        float nextRedraw;
        float surge;
        static Material lineMaterial;

        public static PlasmaArc Attach(Transform model, Color color)
        {
            var arc = model.gameObject.AddComponent<PlasmaArc>();
            arc.Color = color;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Arc_"))
                    arc.emitters.Add(t);
            return arc;
        }

        /// <summary>Flare up for a moment (amount 1 = full surge).</summary>
        public void Surge(float amount = 1) => surge = Mathf.Max(surge, amount);

        void Start()
        {
            if (lineMaterial == null)
                lineMaterial = new Material(Shader.Find("Sprites/Default")) { name = "PlasmaArc" };

            int maxBolts = Bolts + 3;
            for (int i = 0; i < maxBolts; i++)
            {
                glows.Add(Line($"Glow {i}", 0));
                cores.Add(Line($"Core {i}", 1));
            }

            var lightGo = new GameObject("Plasma Light");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.position = Centre();
            glowLight = lightGo.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = Color;
            glowLight.range = LightRange;
            glowLight.shadows = LightShadows.None;
        }

        LineRenderer Line(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = lineMaterial;
            lr.useWorldSpace = false;
            lr.positionCount = Segments + 1;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingOrder = order;
            lr.enabled = false;
            return lr;
        }

        Vector3 Centre()
        {
            Vector3 c = Vector3.zero;
            foreach (var e in emitters) c += e.position;
            return emitters.Count > 0 ? c / emitters.Count : transform.position;
        }

        void Update()
        {
            if (emitters.Count < 2)
                return;
            surge = Mathf.MoveTowards(surge, 0, Time.deltaTime * 3);

            if (Time.time >= nextRedraw)
            {
                nextRedraw = Time.time + RedrawInterval * Random.Range(0.6f, 1.4f);
                Redraw();
            }

            glowLight.intensity = LightIntensity * (0.7f + Random.value * 0.5f) * (1 + surge * 2.5f);
            glowLight.transform.position = Centre();
        }

        void Redraw()
        {
            int active = Bolts + Mathf.RoundToInt(surge * 3);
            for (int i = 0; i < cores.Count; i++)
            {
                bool on = i < active && Random.value > 0.12f;   // the odd dropout makes it crackle
                cores[i].enabled = glows[i].enabled = on;
                if (!on)
                    continue;

                // Mostly jaw to jaw; sometimes into the back of the jaw
                Transform a = emitters[0], b = emitters[1];
                int ia = Random.Range(0, emitters.Count), ib = Random.Range(0, emitters.Count - 1);
                if (ib >= ia) ib++;
                if (Random.value < 0.35f || emitters.Count == 2) { a = Find("Arc_Top") ?? a; b = Find("Arc_Bottom") ?? b; }
                else { a = emitters[ia]; b = emitters[ib]; }

                Vector3 p0 = transform.InverseTransformPoint(a.position);
                Vector3 p1 = transform.InverseTransformPoint(b.position);
                // Start points wander a little over the emitter
                float spread = Vector3.Distance(p0, p1) * 0.08f;
                p0 += Random.insideUnitSphere * spread;
                p1 += Random.insideUnitSphere * spread;

                var points = Bolt(p0, p1, Jaggedness * (1 + surge * 0.8f));
                cores[i].SetPositions(points);
                glows[i].SetPositions(points);

                float scale = transform.lossyScale.x;
                float w = 1 + surge;
                cores[i].widthMultiplier = CoreWidth * w / scale;
                glows[i].widthMultiplier = GlowWidth * w * Random.Range(0.7f, 1.2f) / scale;
                var white = Color.Lerp(Color, Color.white, 0.75f);
                cores[i].startColor = cores[i].endColor = white;
                var glow = Color; glow.a = 0.35f + 0.25f * surge;
                glows[i].startColor = glows[i].endColor = glow;
            }
        }

        Transform Find(string name)
        {
            foreach (var e in emitters)
                if (e.name == name)
                    return e;
            return null;
        }

        Vector3[] Bolt(Vector3 from, Vector3 to, float jag)
        {
            var pts = new Vector3[Segments + 1];
            Vector3 dir = to - from;
            float len = dir.magnitude;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(dir, Vector3.right);
            side.Normalize();
            Vector3 side2 = Vector3.Cross(dir.normalized, side);
            // A bowed path plus jitter that is largest mid-bolt
            float bow = Random.Range(-1f, 1f) * len * 0.25f;
            float bow2 = Random.Range(-1f, 1f) * len * 0.15f;
            for (int i = 0; i <= Segments; i++)
            {
                float t = (float)i / Segments;
                float envelope = Mathf.Sin(t * Mathf.PI);
                Vector3 p = from + dir * t;
                p += side * (bow * envelope + Random.Range(-1f, 1f) * len * jag * envelope);
                p += side2 * (bow2 * envelope + Random.Range(-1f, 1f) * len * jag * 0.6f * envelope);
                pts[i] = p;
            }
            pts[0] = from;
            pts[Segments] = to;
            return pts;
        }
    }
}
