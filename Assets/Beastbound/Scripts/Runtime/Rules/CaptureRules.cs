using System;

namespace Beastbound.Rules
{
    /// <summary>How aware a wild creature is of the player at the moment a bind is attempted.</summary>
    public enum Awareness
    {
        Asleep = 0,
        Unaware = 1,
        Alert = 2,
        Hostile = 3,
        Exhausted = 4
    }

    /// <summary>
    /// Pure capture ("binding") maths. Deliberately free of UnityEngine so it can be unit-tested anywhere
    /// and, later, run on a server for multiplayer.
    ///
    /// Design rule (a direct answer to player complaints about hidden capture RNG in other games):
    /// every factor here is shown to the player in the HUD, there are no paid items that change the odds,
    /// and every failed attempt permanently raises the next attempt's chance ("Resonance").
    /// </summary>
    public static class CaptureRules
    {
        public const float MinChance = 0.05f;
        public const float MaxChance = 1f;

        /// <summary>Bonus added per previous failed attempt on the same creature.</summary>
        public const float ResonancePerFailure = 0.12f;

        /// <summary>Returns the multiplier that sneaking / sleeping / fighting applies.</summary>
        public static float AwarenessMultiplier(Awareness awareness)
        {
            switch (awareness)
            {
                case Awareness.Asleep: return 2.0f;
                case Awareness.Unaware: return 1.5f;
                case Awareness.Alert: return 1.0f;
                case Awareness.Hostile: return 0.85f;
                case Awareness.Exhausted: return 100f;
                default: return 1f;
            }
        }

        /// <summary>1 at full health, rising to 3 as health approaches zero.</summary>
        public static float HealthMultiplier(float currentHealth, float maxHealth)
        {
            if (maxHealth <= 0f) return 1f;
            float fraction = Clamp01(currentHealth / maxHealth);
            return 1f + 2f * (1f - fraction);
        }

        /// <summary>
        /// Final chance in [MinChance, 1]. An exhausted creature (0 HP) is always a guaranteed bind.
        /// </summary>
        public static float CalculateChance(float baseRate, float currentHealth, float maxHealth,
            Awareness awareness, int failedAttempts)
        {
            if (awareness == Awareness.Exhausted || (maxHealth > 0f && currentHealth <= 0f))
                return MaxChance;

            float chance = Math.Max(0f, baseRate)
                           * HealthMultiplier(currentHealth, maxHealth)
                           * AwarenessMultiplier(awareness);
            chance += Math.Max(0, failedAttempts) * ResonancePerFailure;

            if (chance < MinChance) chance = MinChance;
            if (chance > MaxChance) chance = MaxChance;
            return chance;
        }

        /// <summary>Resolves an attempt given a uniformly distributed roll in [0,1).</summary>
        public static bool Resolve(float chance, double roll01)
        {
            return roll01 < chance;
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
