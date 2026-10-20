using FortressUnited.Core.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FortressUnited.Game
{
    /// <summary>
    /// Feeds the greybox player's movement into PlayerVitals and adds test
    /// keys until there are real weapons:
    ///   1 concussion   2 set on fire   3 infect   4 take 25 damage
    ///   5 heal (clears everything)   6 die / respawn   H show/hide medikit
    /// Concussion wears off over 10 seconds like a TFC conc grenade and
    /// wobbles the view.
    /// </summary>
    public sealed class VitalsTestDriver : MonoBehaviour
    {
        public PlayerController Player;
        public PlayerVitals Vitals;
        public GameObject Medkit;

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
                if (kb.hKey.wasPressedThisFrame && Medkit != null)
                    Medkit.SetActive(!Medkit.activeSelf);
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
                $"health {Vitals.Health:0}   1 conc  2 fire  3 infect  4 damage  5 heal  6 die/respawn  H medikit");
        }
    }
}
