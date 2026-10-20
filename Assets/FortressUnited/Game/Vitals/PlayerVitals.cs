using UnityEngine;

namespace FortressUnited.Game
{
    /// <summary>
    /// A player's vital signs for the medikit screen, driven by what's
    /// happening in the game: heart rate climbs with speed (bunny hopping
    /// pushes it hard), each jump, concussion, fire, infection and damage,
    /// and settles back at rest; SpO2 drops while underwater; temperature
    /// rises when burning or infected; blood pressure follows health. Also
    /// generates the ECG trace, which goes irregular when concussed and
    /// flatlines on death.
    /// </summary>
    public sealed class PlayerVitals
    {
        // Inputs, set by the game every frame
        public float Speed;            // horizontal speed, units/s
        public float MaxSpeed = 400;   // the class's max speed
        public bool Underwater;        // eyes under water
        public float Health = 100, MaxHealth = 100;
        public float Concussion;       // 0-100, TFC's concussion wobble amount
        public bool Burning;
        public bool Infected;
        public bool Dead;

        // Outputs
        public float HeartRate { get; private set; } = 72;
        public float SpO2 { get; private set; } = 98;
        public float Temperature { get; private set; } = 36.6f;
        public int Systolic { get; private set; } = 118;
        public int Diastolic { get; private set; } = 76;

        public string Status
        {
            get
            {
                if (Dead) return "FLATLINE";
                if (Burning) return "BURNING";
                if (Concussion > 0) return "CONCUSSED";
                if (Infected) return "INFECTED";
                if (Underwater && SpO2 < 92) return "HYPOXIA";
                if (Health < 30) return "CRITICAL";
                if (HeartRate > 150) return "MAX EFFORT";
                if (HeartRate > 110) return "ELEVATED";
                return "STABLE";
            }
        }

        // ECG state
        float beatPhase;          // 0-1 through the current beat
        float beatLength = 1;     // scale on the current beat (irregular when concussed)
        float wander;
        readonly System.Random random = new System.Random();

        /// <summary>A jump: a small kick to the heart rate.</summary>
        public void OnJump() => HeartRate = Mathf.Min(220, HeartRate + 2.5f);

        /// <summary>Taking damage: a sharp spike.</summary>
        public void OnDamage(float amount) => HeartRate = Mathf.Min(220, HeartRate + Mathf.Clamp(amount * 0.6f, 4, 30));

        public void Update(float dt)
        {
            if (Dead)
            {
                HeartRate = Mathf.MoveTowards(HeartRate, 0, 120 * dt);
                SpO2 = Mathf.MoveTowards(SpO2, 0, 20 * dt);
                Systolic = Diastolic = 0;
                return;
            }

            float h = Mathf.Clamp01(Health / MaxHealth);

            // Target heart rate
            float bhopCap = MaxSpeed * 1.7f;
            float effort = Mathf.Clamp01((Speed - 100) / Mathf.Max(1, bhopCap - 100));
            float target = 72 + effort * effort * 30 + effort * 60;       // up to ~160 at the bunny hop cap
            target += Concussion / 100f * 35;
            if (Burning) target += 25;
            if (Infected) target += 15;
            target += (1 - h) * 30;
            target = Mathf.Clamp(target, 50, 210);

            // Climbs fast, recovers slowly
            float rate = target > HeartRate ? 1 / 1.5f : 1 / 5f;
            HeartRate += (target - HeartRate) * (1 - Mathf.Exp(-rate * dt));

            // Oxygen
            SpO2 = Underwater
                ? Mathf.Max(60, SpO2 - 1.6f * dt)
                : Mathf.Min(98, SpO2 + 6 * dt);

            // Temperature: fire heats quickly, fever from infection
            float tempTarget = Burning ? 40.2f : Infected ? 38.4f : 36.6f;
            float tempRate = Burning ? 0.8f : 0.15f;
            Temperature = Mathf.MoveTowards(Temperature, tempTarget, tempRate * dt);

            // Blood pressure follows health and effort
            Systolic = Mathf.RoundToInt(118 * (0.55f + 0.45f * h) + (HeartRate - 72) * 0.25f);
            Diastolic = Mathf.RoundToInt(76 * (0.6f + 0.4f * h) + (HeartRate - 72) * 0.1f);
        }

        /// <summary>
        /// Advances the ECG by dt seconds and returns the trace height
        /// (about -0.3 to 1 for a beat).
        /// </summary>
        public float EcgSample(float dt)
        {
            if (Dead || HeartRate < 1)
                return (float)(random.NextDouble() - 0.5) * 0.02f;

            float beatsPerSecond = HeartRate / 60f;
            beatPhase += dt * beatsPerSecond / beatLength;
            if (beatPhase >= 1)
            {
                beatPhase -= 1;
                // Concussion: irregular rhythm; infection: occasional early beats
                float irregular = Concussion / 100f;
                beatLength = 1 + (float)(random.NextDouble() * 2 - 1) * 0.45f * irregular;
                if (Infected && random.NextDouble() < 0.15)
                    beatLength *= 0.6f;
            }

            float h = Mathf.Clamp01(Health / MaxHealth);
            float amp = 0.45f + 0.55f * h;
            float p = beatPhase;
            float v = Bump(p, 0.12f, 0.035f) * 0.12f        // P wave
                    - Bump(p, 0.30f, 0.010f) * 0.12f        // Q
                    + Bump(p, 0.33f, 0.012f) * 1.00f        // R spike
                    - Bump(p, 0.36f, 0.012f) * 0.25f        // S
                    + Bump(p, 0.56f, 0.060f) * 0.30f;       // T wave
            v *= amp;

            // Concussion: shaky trace and wandering baseline
            if (Concussion > 0)
            {
                float c = Concussion / 100f;
                wander += dt * 1.7f;
                v += Mathf.Sin(wander) * 0.12f * c + (float)(random.NextDouble() - 0.5) * 0.18f * c;
            }
            return v;
        }

        static float Bump(float x, float centre, float width)
        {
            float d = (x - centre) / width;
            return Mathf.Exp(-d * d);
        }
    }
}
