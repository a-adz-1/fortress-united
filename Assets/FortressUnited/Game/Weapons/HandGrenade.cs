using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// What TFC's hand grenades share (tf_wpn_grenades.cpp CTFPrimeGrenade):
    /// primed, one goes off 3.8 s later (0.8 s pin + 3 s) whether thrown or
    /// still held; thrown at 600 units/s forward and 200 up; it bounces. Its
    /// team glow (the model's emission mask) blinks faster as it's about to
    /// go. Subclasses say what the blast does and how it looks.
    /// </summary>
    public abstract class HandGrenade : MonoBehaviour
    {
        public const float Unit = 0.0254f;
        public const float PinTime = 0.8f, PrimeTime = 3f;
        const float Gravity = 800;                  // units/s/s, TFC's sv_gravity

        public Color Team = TeamColours.Blue;
        public float GoesOffAt;
        /// <summary>Called on detonation with the blast centre (world) and whether it went off in the hand.</summary>
        public System.Action<Vector3, bool> OnBlast;
        public bool Held { get; private set; }

        Vector3 velocity;                           // m/s
        Material glow;
        Light lamp;
        Transform follow;

        /// <summary>A primed grenade held by the player (follows the given transform).</summary>
        public static T Prime<T>(GameObject prefab, Transform hand, Color team, System.Action<Vector3, bool> onBlast) where T : HandGrenade
        {
            GameObject go;
            if (prefab != null)
            {
                go = new GameObject(typeof(T).Name);
                Instantiate(prefab, go.transform, false);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = typeof(T).Name;
                DestroyImmediate(go.GetComponent<Collider>());
                go.transform.localScale = Vector3.one * 0.08f;
            }
            go.transform.position = hand.position;
            var g = go.AddComponent<T>();
            g.Team = team;
            g.GoesOffAt = Time.time + PinTime + PrimeTime;
            g.OnBlast = onBlast;
            g.Held = true;
            g.follow = hand;

            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var m = r.material;
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", TeamColours.Glow(team, 1.6f));
                g.glow = m;
            }
            g.lamp = new GameObject("Glow").AddComponent<Light>();
            g.lamp.transform.SetParent(go.transform, false);
            g.lamp.type = LightType.Point;
            g.lamp.color = team;
            g.lamp.range = 0.8f;
            g.lamp.shadows = LightShadows.None;
            return g;
        }

        /// <summary>Throws a held grenade, TFC-style: 600 forward, 200 up.</summary>
        public void Throw(Vector3 forward, Vector3 up)
        {
            Held = false;
            follow = null;
            velocity = (forward * 600 + up * (200 + Random.Range(-10f, 10f))) * Unit;
        }

        void Update()
        {
            float left = GoesOffAt - Time.time;
            float rate = left < 1 ? 10 : left < 2 ? 5 : 2;
            bool on = Mathf.Repeat(Time.time * rate, 1) < 0.6f;
            if (glow != null)
                glow.SetColor("_EmissionColor", TeamColours.Glow(Team, on ? 1.6f : 0.25f));
            lamp.intensity = on ? 1.2f : 0.2f;

            if (Held && follow != null)
                transform.position = follow.position;
            else
                Fly(Time.deltaTime);

            if (left <= 0)
            {
                OnBlast?.Invoke(transform.position, Held);
                Effect(transform.position);
                Destroy(gameObject);
            }
        }

        /// <summary>The look of the blast.</summary>
        protected abstract void Effect(Vector3 at);

        void Fly(float dt)
        {
            velocity += Vector3.down * Gravity * Unit * dt;
            Vector3 step = velocity * dt;
            if (Physics.SphereCast(transform.position, 0.04f, step.normalized, out var hit, step.magnitude))
            {
                // Bounce, losing about half the speed
                transform.position = hit.point + hit.normal * 0.045f;
                velocity = Vector3.Reflect(velocity, hit.normal) * 0.5f;
                if (velocity.magnitude < 0.5f)
                    velocity = Vector3.zero;
                return;
            }
            transform.position += step;
            transform.Rotate(velocity.magnitude * 60 * dt, 0, 0, Space.Self);
        }
    }
}
