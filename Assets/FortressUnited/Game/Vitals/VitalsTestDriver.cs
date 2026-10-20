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
        public Color EnemyColour = new Color(1f, 0.3f, 0.25f);
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
            var dummy = Mannequin.Build(feet, Quaternion.LookRotation(-flat).eulerAngles.y, EnemyColour, out _, out _, colliders: true);
            dummy.name = "Target Dummy";
            dummies.Add(dummy.GetComponent<RobotFx>());
        }

        void TestShot()
        {
            var cam = Player.GetComponentInChildren<Camera>();
            if (cam == null || !Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 60))
                return;
            var target = hit.collider.GetComponentInParent<RobotFx>();
            if (target != null)
                target.Hit(hit.point, 20);
            if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(cam.transform.forward * 2, hit.point, ForceMode.VelocityChange);
        }

        void DropTestBody()
        {
            if (!SpotAhead(2.5f, out var feet, out var flat))
                return;
            var body = Mannequin.Build(feet + Vector3.up * 0.02f, Quaternion.LookRotation(-flat).eulerAngles.y + Random.Range(-30f, 30f),
                EnemyColour, out var bones, out var screen);
            // Thrown back and up by the blast
            Vector3 kick = flat * Random.Range(3f, 6f) + Vector3.up * Random.Range(3f, 5f) + Random.insideUnitSphere;
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
            GUI.Label(new Rect(10, Screen.height - 30, 900, 24),
                $"health {Vitals.Health:0}   1 conc  2 fire  3 infect  4 damage  5 heal  6 die/respawn  7 drop body  9 dummy  RMB shoot   Q/wheel switch  H hide  click swing");
        }
    }
}
