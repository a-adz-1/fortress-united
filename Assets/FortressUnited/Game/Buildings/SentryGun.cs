using System.Collections.Generic;
using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// An engineer's sentry gun, behaving like TFC's (tfc/sentry.cpp), on the
    /// cut-up models from tools/meshy/prepare_sentry.py. Thinks 10 times a
    /// second as TFC does; the parts are smoothed between thinks.
    /// - Idle: sweeps 50 degrees either side of where it was built, speeding
    ///   up to 60/s and slowing near each end, with the odd small tilt.
    /// - Targets: within 500 units (12.7 m) anywhere, 500-1000 only in front
    ///   (view cone 0.7), never beyond 1000. Tracks at up to 180/s, tilts at
    ///   30/s, within +-50 degrees.
    /// - Fires once within 10 degrees of the target: level 1 every 0.2 s,
    ///   levels 2-3 every 0.1 s alternating barrels; 16 damage a bullet,
    ///   every 4th a tracer. Level 3 also fires a rocket every 3 s.
    /// Team colour comes from swapping the base colour texture.
    /// </summary>
    public sealed class SentryGun : MonoBehaviour
    {
        const float Unit = 0.0254f;            // GoldSrc unit in metres
        const int BaseTurnRate = 6;

        public int Level { get; private set; } = 1;
        public Color TeamColour = new Color(0.3f, 0.7f, 1f);
        public Texture2D TeamBaseColor;
        /// <summary>Things it shoots at (enemy robots), supplied by the game.</summary>
        public static readonly List<Transform> Targets = new List<Transform>();
        /// <summary>Called for each bullet or rocket that hits something.</summary>
        public System.Action<Transform, Vector3, float> OnHit;

        Transform turret, gun, gunL, gunR, rockets, barrel, barrelL, barrelR, sensor, eye, rocketMuzzle;
        readonly List<Transform> muzzles = new List<Transform>();

        // TFC state, angles in degrees: yaw world-relative to the build direction, pitch up positive
        float buildYaw, curYaw, curPitch, goalYaw, goalPitch, turnRate = BaseTurnRate;
        bool turningRight = true;
        Transform enemy;
        float nextThink, nextAttack, nextRocket;
        int shots;

        // Smoothed for display
        float showYaw, showPitch, recoil, spin, spinSpeed;
        // Rest pose of each moving part, and the sentry's axes in its parent's space
        // (imported parts carry an axis-conversion rotation, so turns are applied on top)
        readonly Dictionary<Transform, Quaternion> restRot = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<Transform, Vector3> restPos = new Dictionary<Transform, Vector3>();
        readonly Dictionary<Transform, Vector3> turnAxis = new Dictionary<Transform, Vector3>();
        Light eyeLight, flash;
        float flashUntil;

        public static SentryGun Build(GameObject prefab, Vector3 position, float yaw, Color team, Texture2D teamBaseColor, int level)
        {
            // The model goes inside a clean holder: imported models carry an axis
            // conversion rotation on their root, so the holder gives true up/right/forward
            var go = new GameObject($"Sentry (level {level})");
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            Instantiate(prefab, go.transform, false).name = prefab.name;
            var s = go.AddComponent<SentryGun>();
            s.Level = level;
            s.TeamColour = team;
            s.TeamBaseColor = teamBaseColor;
            s.buildYaw = yaw;
            s.Setup();
            return s;
        }

        Transform Find(string n)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == n)
                    return t;
            return null;
        }

        void Setup()
        {
            turret = Find("Turret");
            gun = Find("Gun");
            gunL = Find("Gun_L");
            gunR = Find("Gun_R");
            rockets = Find("Rockets");
            barrel = Find("Barrel");
            barrelL = Find("Barrel_L");
            barrelR = Find("Barrel_R");
            sensor = Find("Sensor");
            eye = Find("Eye");
            rocketMuzzle = Find("RocketMuzzle");
            foreach (var n in new[] { "Muzzle", "Muzzle_Low", "Muzzle_High", "Muzzle_L", "Muzzle_R" })
                if (Find(n) is Transform m)
                    muzzles.Add(m);

            // Rest pose and axes, taken while everything is at rest
            void Rest(Transform t, Vector3 worldAxis)
            {
                if (t == null) return;
                restRot[t] = t.localRotation;
                restPos[t] = t.localPosition;
                turnAxis[t] = t.parent.InverseTransformDirection(worldAxis).normalized;
            }
            Rest(turret, transform.up);
            foreach (var g in new[] { gun, gunL, gunR, rockets })
                Rest(g, transform.right);
            foreach (var b in new[] { barrel, barrelL, barrelR })
                Rest(b, transform.forward);

            // Team skin
            if (TeamBaseColor != null)
                foreach (var r in GetComponentsInChildren<Renderer>())
                    foreach (var m in r.materials)
                        if (m.HasProperty("_BaseMap"))
                            m.SetTexture("_BaseMap", TeamBaseColor);

            // Targeting light in the sensor
            var eyeAt = eye != null ? eye : (turret != null ? turret : transform);
            eyeLight = new GameObject("Eye Light").AddComponent<Light>();
            eyeLight.transform.SetParent(eyeAt, false);
            eyeLight.type = LightType.Point;
            eyeLight.color = TeamColour;
            eyeLight.range = 1.2f;
            eyeLight.intensity = 0.6f;
            eyeLight.shadows = LightShadows.None;

            flash = new GameObject("Muzzle Flash").AddComponent<Light>();
            flash.transform.SetParent(transform, false);
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.8f, 0.45f);
            flash.range = 4;
            flash.intensity = 0;
            flash.shadows = LightShadows.None;

            // TFC: sweep bounds 50 either side, start turning right, level
            goalYaw = -50;
            nextThink = Time.time + 0.5f;
            nextRocket = Time.time;
        }

        void Update()
        {
            while (Time.time >= nextThink)
            {
                nextThink += 0.1f;
                if (nextThink < Time.time) nextThink = Time.time + 0.1f;
                Think();
            }
            Animate();
        }

        // ------------------------------------------------------------ TFC logic

        void Think()
        {
            if (enemy != null && !Valid(enemy))
                enemy = null;
            if (enemy == null)
            {
                enemy = FindTarget();
                if (enemy == null)
                {
                    if (!MoveTurret())
                    {
                        // Reached the end of the sweep: go the other way, sometimes tilt a little
                        turningRight = !turningRight;
                        goalYaw = turningRight ? -50 : 50;
                        if (Random.value < 0.3f)
                            goalPitch = (int)Random.Range(-10f, 10f);
                    }
                    return;
                }
                // TFC FoundTarget: brief pause before the first shot; rockets after 0.5 s
                nextAttack = Time.time + 0.1f;
                if (nextRocket < Time.time)
                    nextRocket = Time.time + 0.5f;
            }
            Attack();
        }

        void Attack()
        {
            Vector3 from = EyePosition();
            Vector3 to = AimPoint(enemy);
            Vector3 d = to - from;
            float yawWorld = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            goalYaw = Mathf.DeltaAngle(buildYaw, yawWorld);
            goalPitch = Mathf.Clamp(Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -50, 50);
            MoveTurret();

            float off = new Vector2(Mathf.DeltaAngle(curYaw, goalYaw), goalPitch - curPitch).magnitude;
            if (Time.time >= nextAttack && off <= 10)
            {
                Fire();
                nextAttack = Time.time + (Level == 1 ? 0.2f : 0.1f);
            }
        }

        /// <summary>TFC MoveTurret: returns true while still moving.</summary>
        bool MoveTurret()
        {
            bool moving = false;
            if (curPitch != goalPitch)
            {
                float dir = goalPitch > curPitch ? 1 : -1;
                curPitch += 0.1f * BaseTurnRate * 5 * dir;
                if ((dir > 0 && curPitch > goalPitch) || (dir < 0 && curPitch < goalPitch))
                    curPitch = goalPitch;
                moving = true;
            }
            float dist = Mathf.DeltaAngle(curYaw, goalYaw);
            if (Mathf.Abs(dist) > 0.001f)
            {
                float a = Mathf.Abs(dist);
                if (enemy == null)
                {
                    if (a > 30) { if (turnRate < BaseTurnRate * 10) turnRate += BaseTurnRate; }
                    else if (turnRate > BaseTurnRate * 5) turnRate -= BaseTurnRate;
                }
                else if (a > 30 && turnRate < BaseTurnRate * 30)
                    turnRate += BaseTurnRate * 3;
                float step = 0.1f * turnRate;
                curYaw = a <= step || a < 0.05f * BaseTurnRate ? goalYaw : curYaw + step * Mathf.Sign(dist);
                moving = true;
            }
            if (!moving)
                turnRate = BaseTurnRate;
            return moving;
        }

        Transform FindTarget()
        {
            Transform best = null;
            float bestDist = float.MaxValue;
            foreach (var t in Targets)
            {
                if (!Valid(t))
                    continue;
                float dist = Vector3.Distance(EyePosition(), AimPoint(t)) / Unit;
                if (dist >= 1000) continue;                              // RANGE_FAR
                if (dist >= 500 && !InViewCone(t)) continue;             // RANGE_MID needs the cone
                if (!Visible(t)) continue;
                if (dist < bestDist) { best = t; bestDist = dist; }
            }
            return best;
        }

        bool Valid(Transform t) => t != null && t.gameObject.activeInHierarchy && t.GetComponent<Corpse>() == null;

        bool InViewCone(Transform t)
        {
            float yaw = (buildYaw + curYaw) * Mathf.Deg2Rad;
            var fwd = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var los = new Vector2(t.position.x - transform.position.x, t.position.z - transform.position.z).normalized;
            return Vector2.Dot(los, fwd) > 0.7f;
        }

        bool Visible(Transform t)
        {
            Vector3 from = EyePosition(), to = AimPoint(t);
            if (!Physics.Linecast(from, to, out var hit))
                return true;
            return hit.transform == t || hit.transform.IsChildOf(t);
        }

        Vector3 EyePosition() => transform.position + Vector3.up * (21.2f + 22) * Unit;   // base offset + view_ofs
        static Vector3 AimPoint(Transform t) => t.position + Vector3.up * 1.1f;

        void Fire()
        {
            // Level 3 rockets every 3 seconds, from the pod
            if (Level == 3 && rocketMuzzle != null && Time.time > nextRocket)
            {
                nextRocket = Time.time + 3;
                SentryRocket.Launch(rocketMuzzle.position, (AimPoint(enemy) - rocketMuzzle.position).normalized, TeamColour, this);
            }

            var muzzle = muzzles.Count > 0 ? muzzles[shots % muzzles.Count] : (gun != null ? gun : transform);
            Vector3 dir = (AimPoint(enemy) - muzzle.position).normalized;
            Vector3 end = muzzle.position + dir * 2048 * Unit;
            if (Physics.Raycast(muzzle.position, dir, out var hit, 2048 * Unit))
            {
                end = hit.point;
                OnHit?.Invoke(hit.transform, hit.point, 16);
            }
            if (shots % 4 == 0)
                Tracer.Show(muzzle.position, end);
            shots++;

            flash.transform.position = muzzle.position;
            flashUntil = Time.time + 0.05f;
            recoil = 1;
            spinSpeed = 1;
        }

        // ------------------------------------------------------------ visuals

        void Animate()
        {
            float k = 1 - Mathf.Exp(-Time.deltaTime * 15);
            showYaw += Mathf.DeltaAngle(showYaw, curYaw) * k;
            showPitch += (curPitch - showPitch) * k;

            Turn(turret, showYaw);
            foreach (var g in new[] { gun, gunL, gunR, rockets })
                Turn(g, -showPitch);   // positive about the right axis tips the front down

            // Barrel kick (levels 1-2) and gatling spin (level 3)
            recoil = Mathf.MoveTowards(recoil, 0, Time.deltaTime * 12);
            if (barrel != null)
                barrel.localPosition = restPos[barrel] - turnAxis[barrel] * 0.04f * recoil / Mathf.Max(0.0001f, barrel.parent.lossyScale.x);
            spinSpeed = Mathf.MoveTowards(spinSpeed, enemy != null ? 1 : 0, Time.deltaTime * (enemy != null ? 4 : 0.7f));
            spin += spinSpeed * 1400 * Time.deltaTime;
            Turn(barrelL, spin);
            Turn(barrelR, -spin);

            flash.intensity = Time.time < flashUntil ? 4 : 0;
            // The eye brightens and flickers while locked on
            eyeLight.intensity = enemy != null ? 2.5f + Random.value : 0.6f;
        }

        /// <summary>Turns a part from its rest pose about its axis (degrees).</summary>
        void Turn(Transform t, float degrees)
        {
            if (t != null && restRot.TryGetValue(t, out var rest))
                t.localRotation = Quaternion.AngleAxis(degrees, turnAxis[t]) * rest;
        }
    }

    /// <summary>A short glowing streak from a muzzle to where the bullet landed.</summary>
    public static class Tracer
    {
        static Material material;

        public static void Show(Vector3 from, Vector3 to)
        {
            if (material == null)
                material = new Material(Shader.Find("Sprites/Default")) { name = "Tracer" };
            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = material;
            lr.positionCount = 2;
            Vector3 dir = (to - from).normalized;
            float len = Mathf.Min(3, Vector3.Distance(from, to));
            lr.SetPosition(0, from);
            lr.SetPosition(1, from + dir * len);
            lr.widthMultiplier = 0.02f;
            lr.startColor = new Color(1f, 0.9f, 0.6f, 1);
            lr.endColor = new Color(1f, 0.7f, 0.3f, 0);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<TracerMover>().Set(from, to, len);
        }

        sealed class TracerMover : MonoBehaviour
        {
            Vector3 from, to, dir;
            float len, travelled, total;
            LineRenderer lr;

            public void Set(Vector3 f, Vector3 t, float l)
            {
                from = f; to = t; len = l;
                dir = (t - f).normalized;
                total = Vector3.Distance(f, t);
                lr = GetComponent<LineRenderer>();
            }

            void Update()
            {
                travelled += 120 * Time.deltaTime;   // fast, but visible
                if (travelled - len > total) { Destroy(gameObject); return; }
                lr.SetPosition(0, from + dir * Mathf.Min(total, Mathf.Max(0, travelled - len)));
                lr.SetPosition(1, from + dir * Mathf.Min(total, travelled));
            }
        }
    }

    /// <summary>The level 3 sentry's rocket: a glowing dart with a smoke trail that bursts on impact.</summary>
    public sealed class SentryRocket : MonoBehaviour
    {
        Vector3 velocity;
        SentryGun owner;
        float dieAt;

        public static void Launch(Vector3 at, Vector3 dir, Color team, SentryGun owner)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "Sentry Rocket";
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir) * Quaternion.Euler(90, 0, 0));
            go.transform.localScale = new Vector3(0.06f, 0.12f, 0.06f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            mat.color = Color.Lerp(team, Color.white, 0.6f) * 3;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(go.transform, false);
            light.color = team;
            light.range = 3;
            light.intensity = 2;
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.4f;
            trail.widthMultiplier = 0.08f;
            trail.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            trail.startColor = new Color(0.9f, 0.9f, 0.95f, 0.5f);
            trail.endColor = new Color(0.9f, 0.9f, 0.95f, 0);
            var r = go.AddComponent<SentryRocket>();
            r.velocity = dir * 1000 * 0.0254f;   // TFC rockets fly at 1000 units/s
            r.owner = owner;
            r.dieAt = Time.time + 5;
        }

        void Update()
        {
            Vector3 step = velocity * Time.deltaTime;
            if (Physics.Raycast(transform.position, velocity.normalized, out var hit, step.magnitude) || Time.time > dieAt)
            {
                Vector3 at = hit.collider != null ? hit.point : transform.position;
                // Blast: everything nearby, more the closer it is
                foreach (var col in Physics.OverlapSphere(at, 2.5f))
                {
                    float dmg = Mathf.Lerp(100, 20, Vector3.Distance(col.ClosestPoint(at), at) / 2.5f);
                    if (owner != null)
                        owner.OnHit?.Invoke(col.transform, col.ClosestPoint(at), dmg);
                }
                var burst = new GameObject("Rocket Burst");
                burst.transform.position = at;
                FxParticles.Sparks(burst.transform, new Color(1f, 0.7f, 0.3f)).Emit(60);
                var l = burst.AddComponent<Light>();
                l.color = new Color(1f, 0.7f, 0.4f);
                l.range = 6;
                l.intensity = 6;
                Destroy(l, 0.15f);
                Destroy(burst, 2);
                Destroy(gameObject);
                return;
            }
            transform.position += step;
        }
    }
}
