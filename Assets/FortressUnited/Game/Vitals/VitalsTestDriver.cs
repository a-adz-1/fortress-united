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
    /// Concussion wears off over 10 seconds like a TFC conc grenade and
    /// wobbles the view.
    /// </summary>
    public sealed class VitalsTestDriver : MonoBehaviour
    {
        public PlayerController Player;
        public PlayerVitals Vitals;
        public Color EnemyColour = new Color(1f, 0.3f, 0.25f);

        bool wasOnGround;
        float burnUntil;
        float concussTime;

        const float ConcDuration = 10;

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
                if (kb.digit1Key.wasPressedThisFrame) { concussTime = ConcDuration; Vitals.OnDamage(10); }
                if (kb.digit2Key.wasPressedThisFrame) { burnUntil = Time.time + 6; Vitals.OnDamage(10); }
                if (kb.digit3Key.wasPressedThisFrame) Vitals.Infected = true;
                if (kb.digit4Key.wasPressedThisFrame) Damage(25);
                if (kb.digit5Key.wasPressedThisFrame)
                {
                    Vitals.Health = Vitals.MaxHealth;
                    Vitals.Infected = false;
                    burnUntil = 0;
                    concussTime = 0;
                }
                if (kb.digit7Key.wasPressedThisFrame)
                    DropTestBody();
                if (kb.digit6Key.wasPressedThisFrame)
                {
                    Vitals.Dead = !Vitals.Dead;
                    if (!Vitals.Dead)
                    {
                        Vitals.Health = Vitals.MaxHealth;
                        Vitals.Infected = false;
                        burnUntil = 0;
                        concussTime = 0;
                    }
                }
            }

            Vitals.Burning = Time.time < burnUntil;
            if (Vitals.Burning && Mathf.Repeat(Time.time, 1) < Time.deltaTime)
                Damage(4);
            if (Vitals.Infected && Mathf.Repeat(Time.time + 0.5f, 2) < Time.deltaTime)
                Damage(3);

            concussTime = Mathf.Max(0, concussTime - Time.deltaTime);
            Vitals.Concussion = concussTime / ConcDuration * 100;

            // Concussion wobble, as TFC's conc does with the view
            float c = Vitals.Concussion / 100f;
            Player.ViewOffset = new Vec3(Mathf.Sin(Time.time * 2.3f) * 6 * c, Mathf.Sin(Time.time * 1.7f) * 6 * c, Mathf.Sin(Time.time * 1.1f) * 3 * c);
        }

        void DropTestBody()
        {
            var cam = Player.GetComponentInChildren<Camera>();
            if (cam == null)
                return;
            Vector3 flat = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            Vector3 eye = cam.transform.position;
            float dist = 2.5f;
            if (Physics.SphereCast(eye, 0.3f, flat, out var wall, dist + 0.5f))
                dist = Mathf.Max(0.6f, wall.distance - 0.5f);
            Vector3 spot = eye + flat * dist;
            Vector3 feet = Physics.Raycast(spot, Vector3.down, out var floor, 4) ? floor.point : spot - Vector3.up * 1.6f;

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
                $"health {Vitals.Health:0}   1 conc  2 fire  3 infect  4 damage  5 heal  6 die/respawn  7 drop body   Q/wheel switch  H hide  click swing");
        }
    }
}
