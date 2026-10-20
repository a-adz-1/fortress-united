using FortressUnited.Core.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FortressUnited.Game
{
    /// <summary>
    /// Feeds the greybox player's movement into PlayerVitals and adds test
    /// keys until there are real weapons:
    ///   1 concussion   2 set on fire   3 infect   4 take 25 damage
    ///   5 heal (clears everything)   6 die / respawn
    ///   7 drop an enemy body ahead, as if hit by a rocket
    ///   8 dummies do the repair kit push
    ///   0 build a sentry ahead; again next to it (or hit it with the
    ///     spanner) to upgrade (levels 1-3), then the spanner repairs it
    ///   X detonate your sentry   G throw a test grenade
    ///   F prime a conc grenade, F again to throw it; hold it and it goes
    ///     off in your hand (TFC's hand-conc: bunny hop, prime, launch)
    ///   9 stand a target dummy ahead (it copies your conc/fire/virus state);
    ///     right click fires a test shot
    /// Concussion is TFC's: strength 100 fading to 0 over 15 seconds (7 for
    /// medics), swaying the view with the engine's idle sway at that scale,
    /// plus a light haze of static. Target dummies mirror your state, so you
    /// can see what other players see when you're concussed, on fire or
    /// infected.
    /// </summary>
    public sealed class VitalsTestDriver : MonoBehaviour
    {
        public PlayerController Player;
        public PlayerVitals Vitals;
        public Color EnemyColour = TeamColours.Red;
        [Tooltip("Enemy class model for dummies and bodies (the primitive mannequin if empty).")]
        public GameObject EnemyModel;
        public readonly System.Collections.Generic.Dictionary<string, AnimationClip> EnemyClips = new System.Collections.Generic.Dictionary<string, AnimationClip>();
        readonly System.Collections.Generic.List<RobotCharacter> enemies = new System.Collections.Generic.List<RobotCharacter>();
        readonly System.Collections.Generic.Dictionary<RobotCharacter, float> enemyHealth = new System.Collections.Generic.Dictionary<RobotCharacter, float>();

        [Tooltip("Sentry levels 1-3 and their team base colours, for the 0 key.")]
        public GameObject[] SentryModels = new GameObject[3];
        public Texture2D[] SentryTeamTextures = new Texture2D[3];
        public Color TeamColour = TeamColours.Blue;
        SentryGun sentry;
        [Tooltip("Conc grenade model (glow tinted per team).")]
        public GameObject ConcModel;
        ConcGrenade heldConc;
        Transform hand;
        [Tooltip("Medics shake off concussion in 7 seconds instead of 15.")]
        public bool Medic = true;

        // TFC's idle sway (cl_dll/view.cpp V_AddIdle, default cvars)
        const float IdlePitchCycle = 1, IdleYawCycle = 2, IdleRollCycle = 0.5f;
        const float IdlePitchLevel = 0.3f, IdleYawLevel = 0.3f, IdleRollLevel = 0.1f;

        readonly System.Collections.Generic.List<RobotFx> dummies = new System.Collections.Generic.List<RobotFx>();
        ScreenHaze haze;
        float concStart = -1000;

        bool wasOnGround;
        float burnUntil;

        float ConcDuration => Medic ? 7 : 15;

        void Update()
        {
            if (Player == null || Player.State == null || Vitals == null)
                return;
            var s = Player.State;
            var kb = Keyboard.current;

            Vitals.Speed = new Vec3(s.Velocity.X, s.Velocity.Y, 0).Length();
            Vitals.MaxSpeed = Player.MaxSpeed;
            Vitals.Underwater = s.WaterLevel >= 3;

            bool onGround = s.OnGround != -1;
            if (wasOnGround && !onGround && s.Velocity.Z > 0)
                Vitals.OnJump();
            wasOnGround = onGround;

            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) { concStart = Time.time; Vitals.OnDamage(10); }
                if (kb.digit2Key.wasPressedThisFrame) { burnUntil = Time.time + 6; Vitals.OnDamage(10); }
                if (kb.digit3Key.wasPressedThisFrame) Vitals.Infected = true;
                if (kb.digit4Key.wasPressedThisFrame) Damage(25);
                if (kb.digit5Key.wasPressedThisFrame)
                {
                    Vitals.Health = Vitals.MaxHealth;
                    Vitals.Infected = false;
                    burnUntil = 0;
                    concStart = -1000;
                }
                if (kb.digit7Key.wasPressedThisFrame)
                    DropTestBody();
                if (kb.digit9Key.wasPressedThisFrame)
                    StandDummy();
                if (kb.digit0Key.wasPressedThisFrame)
                    BuildSentry();
                if (kb.xKey.wasPressedThisFrame && sentry != null)
                    sentry.Explode();
                if (kb.fKey.wasPressedThisFrame)
                    ConcKey();
                if (kb.gKey.wasPressedThisFrame)
                {
                    var cam = Player.GetComponentInChildren<Camera>();
                    if (cam != null)
                        TestGrenade.Throw(cam.transform.position + cam.transform.forward * 0.5f, cam.transform.forward * 12 + Vector3.up * 3, HitSomething);
                }
                if (kb.digit8Key.wasPressedThisFrame)
                {
                    enemies.RemoveAll(e => e == null);
                    foreach (var e in enemies)
                        e.Anim?.Heal();
                }
                if (kb.digit6Key.wasPressedThisFrame)
                {
                    Vitals.Dead = !Vitals.Dead;
                    if (!Vitals.Dead)
                    {
                        Vitals.Health = Vitals.MaxHealth;
                        Vitals.Infected = false;
                        burnUntil = 0;
                        concStart = -1000;
                    }
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked)
                TestShot();

            Vitals.Burning = Time.time < burnUntil;
            if (Vitals.Burning && Mathf.Repeat(Time.time, 1) < Time.deltaTime)
                Damage(4);
            if (Vitals.Infected && Mathf.Repeat(Time.time + 0.5f, 2) < Time.deltaTime)
                Damage(3);

            // TFC: m_iConcussion = 100 * time left / duration (player.cpp CheckTimeBasedDamage)
            float left = concStart + ConcDuration - Time.time;
            Vitals.Concussion = left > 0 ? Mathf.Floor(100 * left / ConcDuration) : 0;

            // ... and the client sways the view by it (view.cpp V_AddIdle)
            float scale = Vitals.Concussion, t = Time.time;
            Player.ViewOffset = new Vec3(
                scale * Mathf.Sin(t * IdlePitchCycle) * IdlePitchLevel,
                scale * Mathf.Sin(t * IdleYawCycle) * IdleYawLevel,
                scale * Mathf.Sin(t * IdleRollCycle) * IdleRollLevel);

            if (haze == null)
                haze = ScreenHaze.Create();
            haze.Amount = Vitals.Concussion / 100f;

            // Dummies show your state as others would see it
            dummies.RemoveAll(d => d == null);
            foreach (var d in dummies)
            {
                d.Heat = Mathf.MoveTowards(d.Heat, Vitals.Burning ? 1 : 0, Time.deltaTime * 2);
                d.Virus = Vitals.Infected ? 1 : 0;
                d.Glitch = Vitals.Concussion / 100f;
            }
        }

        /// <summary>Somewhere ahead on the floor, short of any wall.</summary>
        bool SpotAhead(float distance, out Vector3 feet, out Vector3 flat)
        {
            feet = flat = Vector3.zero;
            var cam = Player.GetComponentInChildren<Camera>();
            if (cam == null)
                return false;
            flat = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            Vector3 eye = cam.transform.position;
            if (Physics.SphereCast(eye, 0.3f, flat, out var wall, distance + 0.5f))
                distance = Mathf.Max(0.6f, wall.distance - 0.5f);
            Vector3 spot = eye + flat * distance;
            feet = Physics.Raycast(spot, Vector3.down, out var floor, 4) ? floor.point : spot - Vector3.up * 1.6f;
            return true;
        }

        void StandDummy()
        {
            if (!SpotAhead(3, out var feet, out var flat))
                return;
            float yaw = Quaternion.LookRotation(-flat).eulerAngles.y;
            if (EnemyModel != null)
            {
                // A real class model walking back and forth across your view
                var c = RobotCharacter.Spawn(EnemyModel, feet, yaw + 90, EnemyColour, EnemyClips);
                c.name = "Target Dummy";
                enemies.Add(c);
                var capsule = c.gameObject.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0, 0.9f, 0);
                capsule.height = 1.8f;
                capsule.radius = 0.28f;
                c.gameObject.AddComponent<DummyWalker>();
                dummies.Add(c.Fx);
                enemyHealth[c] = 150;
                SentryGun.Targets.Add(c.transform);
                return;
            }
            var dummy = Mannequin.Build(feet, yaw, EnemyColour, out _, out _, colliders: true);
            dummy.name = "Target Dummy";
            dummies.Add(dummy.GetComponent<RobotFx>());
        }

        void ConcKey()
        {
            var cam = Player.GetComponentInChildren<Camera>();
            if (cam == null)
                return;
            if (heldConc != null)
            {
                heldConc.Throw(cam.transform.forward, cam.transform.up);
                heldConc = null;
                return;
            }
            if (hand == null)
            {
                hand = new GameObject("Grenade Hand").transform;
                hand.SetParent(cam.transform, false);
                hand.localPosition = new Vector3(0.22f, -0.22f, 0.35f);
            }
            heldConc = ConcGrenade.Prime(ConcModel, hand, TeamColour, ConcBlast);
        }

        /// <summary>A conc went off: TFC's push on the player, glitches and shoves for everything else.</summary>
        void ConcBlast(Vector3 at)
        {
            var s = Player.State;
            Vec3 gren = GoldSrcSpace.FromUnity(at);
            var push = ConcGrenade.Blast(s.Origin, s.Velocity, gren);
            if (push.HasValue)
            {
                s.Velocity = push.Value;
                s.OnGround = -1;
                concStart = Time.time;          // the thrower is concussed too, as in TFC
                Vitals.OnDamage(5);
            }
            float radius = ConcGrenade.BlastRadius * ConcGrenade.Unit;
            foreach (var col in Physics.OverlapSphere(at, radius))
            {
                var fx = col.GetComponentInParent<RobotFx>();
                if (fx != null && col.GetComponentInParent<SentryGun>() == null)
                    fx.Hit(col.ClosestPoint(at), 8);
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic)
                    col.attachedRigidbody.AddExplosionForce(8, at, radius, 0.5f, ForceMode.VelocityChange);
            }
        }

        void BuildSentry()
        {
            if (!SpotAhead(2.5f, out var feet, out var flat))
                return;
            float yaw = Quaternion.LookRotation(flat).eulerAngles.y;
            if (sentry != null && !sentry.Dead && Vector3.Distance(sentry.transform.position, feet) < 4)
            {
                Upgrade(sentry);
                return;
            }
            Place(1, feet, yaw, false);
        }

        void Upgrade(SentryGun s)
        {
            if (s.Level >= 3)
            {
                // Fully upgraded: the spanner repairs instead
                s.Repair(50);
                return;
            }
            var at = s.transform.position;
            float yaw = s.transform.eulerAngles.y;
            int level = s.Level + 1;
            Destroy(s.gameObject);
            Place(level, at, yaw, true);
        }

        void Place(int level, Vector3 feet, float yaw, bool upgrade)
        {
            var model = SentryModels.Length >= level ? SentryModels[level - 1] : null;
            if (model == null)
            {
                Debug.LogWarning($"Fortress United: no sentry level {level} model");
                return;
            }
            var skin = SentryTeamTextures.Length >= level ? SentryTeamTextures[level - 1] : null;
            sentry = SentryGun.Build(model, feet, yaw, TeamColour, skin, level, upgrade);
            sentry.OnHit = HitSomething;
            SentryGun.OnSpanner = s => { if (s == sentry) Upgrade(s); };
        }

        /// <summary>A bullet or blast landed: ripple robots, hurt enemies, push bodies.</summary>
        void HitSomething(Transform t, Vector3 point, float damage)
        {
            if (t == null)
                return;
            var building = t.GetComponentInParent<SentryGun>();
            if (building != null)
            {
                building.TakeDamage(point, damage);
                return;
            }
            var fx = t.GetComponentInParent<RobotFx>();
            if (fx != null)
                fx.Hit(point, damage);
            var rb = t.GetComponentInParent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
                rb.AddForceAtPosition((point - (sentry != null ? sentry.transform.position : point - Vector3.forward)).normalized * damage * 0.05f, point, ForceMode.VelocityChange);
            var c = t.GetComponentInParent<RobotCharacter>();
            if (c != null && enemyHealth.TryGetValue(c, out float hp))
            {
                hp -= damage;
                enemyHealth[c] = hp;
                if (hp <= 0)
                {
                    enemyHealth.Remove(c);
                    SentryGun.Targets.Remove(c.transform);
                    Vector3 away = sentry != null ? (c.transform.position - sentry.transform.position).normalized : -c.transform.forward;
                    c.Die(away * 3 + Vector3.up * 2, 160);
                }
            }
        }

        void TestShot()
        {
            var cam = Player.GetComponentInChildren<Camera>();
            if (cam == null || !Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 60))
                return;
            HitSomething(hit.collider.transform, hit.point, 20);
            if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(cam.transform.forward * 2, hit.point, ForceMode.VelocityChange);
        }

        void DropTestBody()
        {
            if (!SpotAhead(2.5f, out var feet, out var flat))
                return;
            float yaw = Quaternion.LookRotation(-flat).eulerAngles.y + Random.Range(-30f, 30f);
            // Thrown back and up by the blast
            Vector3 kick = flat * Random.Range(3f, 6f) + Vector3.up * Random.Range(3f, 5f) + Random.insideUnitSphere;
            if (EnemyModel != null)
            {
                // Blown back: the death clip starts, then the ragdoll takes over
                var c = RobotCharacter.Spawn(EnemyModel, feet + Vector3.up * 0.02f, yaw, EnemyColour, EnemyClips);
                c.Die(kick, Random.Range(120f, 170f));
                return;
            }
            var body = Mannequin.Build(feet + Vector3.up * 0.02f, yaw, EnemyColour, out var bones, out var screen);
            Corpse.Create(body, bones, kick, Random.Range(120f, 170f), screen);
        }

        void Damage(float amount)
        {
            if (Vitals.Dead)
                return;
            Vitals.Health = Mathf.Max(0, Vitals.Health - amount);
            Vitals.OnDamage(amount);
            if (Vitals.Health <= 0)
                Vitals.Dead = true;
        }

        void OnGUI()
        {
            if (Player == null || !Player.ShowSpeed)
                return;
            if (heldConc != null)
                GUI.Label(new Rect(Screen.width / 2 - 60, Screen.height / 2 + 40, 200, 24),
                    $"CONC  {Mathf.Max(0, heldConc.GoesOffAt - Time.time):0.0}");
            GUI.Label(new Rect(10, Screen.height - 30, 900, 24),
                $"health {Vitals.Health:0}   1 conc  2 fire  3 infect  4 damage  5 heal  6 die/respawn  7 drop body  8 dummies heal  9 dummy  0 sentry  X det  G grenade  F conc  RMB shoot   Q/wheel switch  H hide  click swing");
        }
    }
}
