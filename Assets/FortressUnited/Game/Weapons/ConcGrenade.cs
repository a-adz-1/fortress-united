using FortressUnited.Core.Movement;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// The concussion grenade, with TFC's mechanics (player.cpp
    /// TeamFortress_TakeConcussionBlast); fuse and throw as HandGrenade.
    /// - Blast radius 280 units. Anyone inside is thrown away from it:
    ///   velocity = offset * (280 - dist/2) * 0.03, with the vertical part
    ///   x1.5. Within 16 units (held in the hand) it multiplies their own
    ///   velocity instead (x0.33 then the same factor): the hand-conc jump.
    /// - Enemies (and the thrower) are concussed; teammates only thrown.
    /// The look is ours: a pulse of team rings and light, not an explosion.
    /// </summary>
    public sealed class ConcGrenade : HandGrenade
    {
        public const float BlastRadius = 280;      // units

        protected override void Effect(Vector3 at)
        {
            var pulse = new GameObject("Conc Pulse");
            pulse.transform.position = at;
            pulse.AddComponent<ConcPulse>().Play(Team);
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
            flash.range = ConcGrenade.BlastRadius * HandGrenade.Unit;
            flash.shadows = LightShadows.None;
            FxParticles.Sparks(transform, Color.Lerp(team, Color.white, 0.5f)).Emit(50);
            Destroy(gameObject, 1);
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / 0.45f);
            float radius = Mathf.Lerp(0.1f, ConcGrenade.BlastRadius * HandGrenade.Unit * 0.8f, 1 - (1 - t) * (1 - t));
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
