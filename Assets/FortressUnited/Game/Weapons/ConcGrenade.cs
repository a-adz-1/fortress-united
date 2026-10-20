using FortressUnited.Core.Movement;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// The concussion grenade, with TFC's mechanics (tf_wpn_grenades.cpp,
    /// player.cpp TeamFortress_TakeConcussionBlast):
    /// - Primed, it goes off 3.8 s later (0.8 s pin + 3 s), thrown or not.
    /// - Thrown at 600 units/s forward and 200 up; bounces.
    /// - Blast radius 280 units. Anyone inside is thrown away from it:
    ///   velocity = offset * (280 - dist/2) * 0.03, with the vertical part
    ///   x1.5. Within 16 units (held in the hand) it multiplies their own
    ///   velocity instead (x0.33 then the same factor): the hand-conc jump.
    /// - Enemies (and the thrower) are concussed; teammates only thrown.
    /// The look is ours: team-coloured glow strips that blink faster as it's
    /// about to go, and a pulse of rings and light instead of an explosion.
    /// </summary>
    public sealed class ConcGrenade : MonoBehaviour
    {
        public const float Unit = 0.0254f;
        public const float PinTime = 0.8f, PrimeTime = 3f;
        public const float BlastRadius = 280;      // units
        const float Gravity = 800;                  // units/s/s, TFC's sv_gravity

        public Color Team = TeamColours.Blue;
        public float GoesOffAt;
        /// <summary>Called on detonation with the blast centre (world).</summary>
        public System.Action<Vector3> OnBlast;

        Vector3 velocity;                           // m/s
        Material glow;
        Light lamp;
        bool held;
        Transform follow;

        /// <summary>A primed grenade held by the player (follows the given transform).</summary>
        public static ConcGrenade Prime(GameObject prefab, Transform hand, Color team, System.Action<Vector3> onBlast)
        {
            var g = Make(prefab, hand.position, team);
            g.GoesOffAt = Time.time + PinTime + PrimeTime;
            g.OnBlast = onBlast;
            g.held = true;
            g.follow = hand;
            return g;
        }

        /// <summary>Throws a held grenade, TFC-style: 600 forward, 200 up.</summary>
        public void Throw(Vector3 forward, Vector3 up)
        {
            held = false;
            follow = null;
            velocity = (forward * 600 + up * (200 + Random.Range(-10f, 10f))) * Unit;
        }

        static ConcGrenade Make(GameObject prefab, Vector3 at, Color team)
        {
            GameObject go;
            if (prefab != null)
            {
                go = new GameObject("Conc Grenade");
                Instantiate(prefab, go.transform, false);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Conc Grenade";
                DestroyImmediate(go.GetComponent<Collider>());
                go.transform.localScale = Vector3.one * 0.09f;
            }
            go.transform.position = at;
            var g = go.AddComponent<ConcGrenade>();
            g.Team = team;

            // Team glow: the model's emission mask tinted with the team colour
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

        void Update()
        {
            float left = GoesOffAt - Time.time;
            // Blinks faster as it's about to go
            float rate = left < 1 ? 10 : left < 2 ? 5 : 2;
            bool on = Mathf.Repeat(Time.time * rate, 1) < 0.6f;
            if (glow != null)
                glow.SetColor("_EmissionColor", TeamColours.Glow(Team, on ? 1.6f : 0.25f));
            lamp.intensity = on ? 1.2f : 0.2f;

            if (held && follow != null)
                transform.position = follow.position;
            else
                Fly(Time.deltaTime);

            if (left <= 0)
                Detonate();
        }

        void Fly(float dt)
        {
            velocity += Vector3.down * Gravity * Unit * dt;
            Vector3 step = velocity * dt;
            if (Physics.SphereCast(transform.position, 0.04f, step.normalized, out var hit, step.magnitude))
            {
                // Bounce, losing some speed (TFC grenades bounce at about half)
                transform.position = hit.point + hit.normal * 0.045f;
                velocity = Vector3.Reflect(velocity, hit.normal) * 0.5f;
                if (velocity.magnitude < 0.5f)
                    velocity = Vector3.zero;
                return;
            }
            transform.position += step;
            transform.Rotate(velocity.magnitude * 60 * dt, 0, 0, Space.Self);
        }

        void Detonate()
        {
            OnBlast?.Invoke(transform.position);
            var pulse = new GameObject("Conc Pulse");
            pulse.transform.position = transform.position;
            pulse.AddComponent<ConcPulse>().Play(Team);
            Destroy(gameObject);
        }

        /// <summary>
        /// TFC's concussion blast on a player's velocity (GoldSrc units/s),
        /// or null when they are out of range.
        /// </summary>
        public static Vec3? Blast(Vec3 playerOrigin, Vec3 playerVelocity, Vec3 grenadeOrigin)
        {
            Vec3 dir = playerOrigin - grenadeOrigin;
            float dist = dir.Length();
            if (dist > BlastRadius)
                return null;
            float mult = (BlastRadius - dist * 0.5f) * 0.03f;
            if (dist < 16)
                dir = playerVelocity * 0.33f;           // in hand: multiply your own speed
            return new Vec3(dir.X * mult, dir.Y * mult, dir.Z * mult * 1.5f);
        }
    }

    /// <summary>The conc's pulse: rings of the team colour bursting outward, a flash and sparks.</summary>
    public sealed class ConcPulse : MonoBehaviour
    {
        LineRenderer[] rings;
        Light flash;
        float age;
        Color team;

        public void Play(Color teamColour)
        {
            team = teamColour;
            var mat = new Material(Shader.Find("Sprites/Default"));
            rings = new LineRenderer[3];
            for (int i = 0; i < rings.Length; i++)
            {
                var go = new GameObject($"Ring {i}");
                go.transform.SetParent(transform, false);
                go.transform.localRotation = i == 0 ? Quaternion.identity : Quaternion.Euler(i == 1 ? 60 : -60, i * 50, 0);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = mat;
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.positionCount = 64;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rings[i] = lr;
            }
            flash = gameObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = team;
            flash.range = ConcGrenade.BlastRadius * ConcGrenade.Unit;
            flash.shadows = LightShadows.None;
            FxParticles.Sparks(transform, Color.Lerp(team, Color.white, 0.5f)).Emit(50);
            Destroy(gameObject, 1);
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / 0.45f);
            float radius = Mathf.Lerp(0.1f, ConcGrenade.BlastRadius * ConcGrenade.Unit * 0.8f, 1 - (1 - t) * (1 - t));
            for (int i = 0; i < rings.Length; i++)
            {
                var lr = rings[i];
                for (int k = 0; k < lr.positionCount; k++)
                {
                    float a = k * Mathf.PI * 2 / lr.positionCount;
                    lr.SetPosition(k, new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
                }
                lr.widthMultiplier = 0.12f * (1 - t);
                var c = Color.Lerp(Color.white, team, t);
                c.a = 1 - t;
                lr.startColor = lr.endColor = c;
            }
            flash.intensity = 6 * (1 - t);
        }
    }
}
