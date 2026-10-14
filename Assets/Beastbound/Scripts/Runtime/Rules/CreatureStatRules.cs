using System;

namespace Beastbound.Rules
{
    /// <summary>
    /// Personality changes both stats and behaviour. It is always shown to the player (never hidden),
    /// so "hunting for a good one" is about exploring, not re-rolling with paid items.
    /// </summary>
    public enum Personality
    {
        Bold = 0,     // Notices you and fights. +Attack
        Curious = 1,  // Approaches to inspect you. +Speed
        Timid = 2,    // Runs away when noticed. +Speed, -Attack
        Sleepy = 3,   // Naps often, slow to notice. +Health, -Speed
        Playful = 4,  // Approaches, circles around you. Balanced
        Stubborn = 5  // Hard to knock down. +Defense, -Speed
    }

    [Serializable]
    public struct StatBlock
    {
        public float maxHealth;
        public float attack;
        public float defense;
        public float speed;

        public StatBlock(float maxHealth, float attack, float defense, float speed)
        {
            this.maxHealth = maxHealth;
            this.attack = attack;
            this.defense = defense;
            this.speed = speed;
        }

        public override string ToString()
        {
            return $"HP {maxHealth:0} / ATK {attack:0} / DEF {defense:0} / SPD {speed:0}";
        }
    }

    public static class CreatureStatRules
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 50;
        public const float MinVigor = 0.9f;
        public const float MaxVigor = 1.1f;

        /// <summary>Stats grow 8% of base per level above 1.</summary>
        public static float LevelMultiplier(int level)
        {
            level = Math.Max(MinLevel, Math.Min(MaxLevel, level));
            return 1f + 0.08f * (level - 1);
        }

        public static StatBlock PersonalityModifiers(Personality personality)
        {
            switch (personality)
            {
                case Personality.Bold: return new StatBlock(1.0f, 1.15f, 1.0f, 1.0f);
                case Personality.Curious: return new StatBlock(1.0f, 1.0f, 1.0f, 1.1f);
                case Personality.Timid: return new StatBlock(1.0f, 0.9f, 1.0f, 1.2f);
                case Personality.Sleepy: return new StatBlock(1.15f, 1.0f, 1.0f, 0.85f);
                case Personality.Stubborn: return new StatBlock(1.05f, 1.0f, 1.2f, 0.9f);
                default: return new StatBlock(1f, 1f, 1f, 1f);
            }
        }

        /// <summary>Vigor is a visible 0.9–1.1 quality roll per individual. Clamped so nothing is ever a "dud".</summary>
        public static float ClampVigor(float vigor)
        {
            if (float.IsNaN(vigor)) return 1f;
            return Math.Max(MinVigor, Math.Min(MaxVigor, vigor));
        }

        /// <summary>Converts vigor into 1–3 stars for display.</summary>
        public static int VigorStars(float vigor)
        {
            vigor = ClampVigor(vigor);
            if (vigor >= 1.05f) return 3;
            if (vigor >= 0.97f) return 2;
            return 1;
        }

        public static StatBlock Compute(StatBlock baseStats, int level, Personality personality, float vigor)
        {
            float lv = LevelMultiplier(level);
            float vg = ClampVigor(vigor);
            StatBlock p = PersonalityModifiers(personality);
            return new StatBlock(
                (float)Math.Round(baseStats.maxHealth * lv * vg * p.maxHealth),
                (float)Math.Round(baseStats.attack * lv * vg * p.attack),
                (float)Math.Round(baseStats.defense * lv * vg * p.defense),
                (float)Math.Round(baseStats.speed * lv * vg * p.speed));
        }

        public static string Describe(Personality personality)
        {
            switch (personality)
            {
                case Personality.Bold: return "Bold — stands its ground and challenges intruders.";
                case Personality.Curious: return "Curious — wanders closer to see what you are.";
                case Personality.Timid: return "Timid — bolts at the first sign of danger.";
                case Personality.Sleepy: return "Sleepy — dozes often and notices you late.";
                case Personality.Playful: return "Playful — loves to circle and chase.";
                case Personality.Stubborn: return "Stubborn — shrugs off hits others would not.";
                default: return personality.ToString();
            }
        }
    }
}
