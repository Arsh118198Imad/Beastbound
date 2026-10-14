using System;

namespace Beastbound.Rules
{
    public enum Element
    {
        Neutral = 0,
        Verdant = 1,
        Ember = 2,
        Tide = 3,
        Gale = 4,
        Stone = 5
    }

    /// <summary>Pure damage maths shared by the player, companions and wild creatures.</summary>
    public static class DamageRules
    {
        public const float MinimumDamage = 1f;

        /// <summary>
        /// Attack/defence ratio scaling: equal stats give 1.0x, double attack gives ~1.17x, double defence ~0.83x.
        /// Smooth and bounded so no single stat can trivialise a fight.
        /// </summary>
        public static float StatScale(float attack, float defense)
        {
            attack = Math.Max(1f, attack);
            defense = Math.Max(1f, defense);
            return 0.5f + attack / (attack + defense);
        }

        /// <summary>Simple, readable type chart. Verdant beats Tide and Stone, Ember beats Verdant, etc.</summary>
        public static float ElementMultiplier(Element attackElement, Element defenderElement)
        {
            if (attackElement == Element.Neutral || defenderElement == Element.Neutral) return 1f;
            if (Beats(attackElement, defenderElement)) return 1.25f;
            if (Beats(defenderElement, attackElement)) return 0.8f;
            return 1f;
        }

        public static bool Beats(Element a, Element b)
        {
            switch (a)
            {
                case Element.Verdant: return b == Element.Tide || b == Element.Stone;
                case Element.Ember: return b == Element.Verdant || b == Element.Gale;
                case Element.Tide: return b == Element.Ember || b == Element.Stone;
                case Element.Gale: return b == Element.Verdant;
                case Element.Stone: return b == Element.Ember || b == Element.Gale;
                default: return false;
            }
        }

        /// <param name="variance01">A roll in [0,1]; mapped to a ±8% spread so hits feel organic but predictable.</param>
        public static float Calculate(float power, float attack, float defense,
            Element attackElement, Element defenderElement, double variance01)
        {
            if (power <= 0f) return 0f;
            double v = variance01 < 0 ? 0 : (variance01 > 1 ? 1 : variance01);
            float spread = 0.92f + (float)v * 0.16f;
            float damage = power * StatScale(attack, defense) * ElementMultiplier(attackElement, defenderElement) * spread;
            return Math.Max(MinimumDamage, (float)Math.Round(damage));
        }
    }
}
