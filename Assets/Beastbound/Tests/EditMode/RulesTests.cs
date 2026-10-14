using Beastbound.Rules;
using NUnit.Framework;

namespace Beastbound.Tests
{
    public class CaptureRulesTests
    {
        [Test]
        public void ExhaustedCreature_IsGuaranteed()
        {
            Assert.AreEqual(1f, CaptureRules.CalculateChance(0.1f, 0f, 50f, Awareness.Exhausted, 0));
            Assert.AreEqual(1f, CaptureRules.CalculateChance(0.1f, 0f, 50f, Awareness.Hostile, 0), "0 HP is always guaranteed");
        }

        [Test]
        public void SleepingIsEasierThanAlert()
        {
            float asleep = CaptureRules.CalculateChance(0.3f, 50f, 50f, Awareness.Asleep, 0);
            float alert = CaptureRules.CalculateChance(0.3f, 50f, 50f, Awareness.Alert, 0);
            Assert.Greater(asleep, alert);
        }

        [Test]
        public void LowerHealthRaisesChance()
        {
            float full = CaptureRules.CalculateChance(0.3f, 50f, 50f, Awareness.Alert, 0);
            float half = CaptureRules.CalculateChance(0.3f, 25f, 50f, Awareness.Alert, 0);
            Assert.Greater(half, full);
        }

        [Test]
        public void ResonanceRaisesChanceAfterFailures()
        {
            float first = CaptureRules.CalculateChance(0.2f, 50f, 50f, Awareness.Hostile, 0);
            float third = CaptureRules.CalculateChance(0.2f, 50f, 50f, Awareness.Hostile, 2);
            Assert.AreEqual(first + 2 * CaptureRules.ResonancePerFailure, third, 0.0001f);
        }

        [Test]
        public void ChanceIsAlwaysWithinBounds()
        {
            Assert.AreEqual(CaptureRules.MinChance, CaptureRules.CalculateChance(0f, 50f, 50f, Awareness.Hostile, 0));
            Assert.AreEqual(1f, CaptureRules.CalculateChance(1f, 1f, 50f, Awareness.Asleep, 10));
            Assert.AreEqual(0.3f, CaptureRules.CalculateChance(0.3f, 10f, 0f, Awareness.Alert, 0), 0.0001f, "Invalid max health falls back to the base rate");
        }

        [Test]
        public void ResolveUsesTheShownChance()
        {
            Assert.IsTrue(CaptureRules.Resolve(0.6f, 0.59));
            Assert.IsFalse(CaptureRules.Resolve(0.6f, 0.61));
            Assert.IsTrue(CaptureRules.Resolve(1f, 0.9999));
        }
    }

    public class DamageRulesTests
    {
        [Test]
        public void EqualStats_GiveRoughlyBasePower()
        {
            float dmg = DamageRules.Calculate(10f, 10f, 10f, Element.Neutral, Element.Neutral, 0.5);
            Assert.AreEqual(10f, dmg, 0.6f);
        }

        [Test]
        public void ElementAdvantage_IsApplied()
        {
            Assert.AreEqual(1.25f, DamageRules.ElementMultiplier(Element.Ember, Element.Verdant));
            Assert.AreEqual(0.8f, DamageRules.ElementMultiplier(Element.Verdant, Element.Ember));
            Assert.AreEqual(1f, DamageRules.ElementMultiplier(Element.Neutral, Element.Verdant));
            Assert.AreEqual(1f, DamageRules.ElementMultiplier(Element.Verdant, Element.Verdant));
        }

        [Test]
        public void ChartHasNoMutualAdvantage()
        {
            foreach (Element a in System.Enum.GetValues(typeof(Element)))
            foreach (Element b in System.Enum.GetValues(typeof(Element)))
            {
                Assert.IsFalse(DamageRules.Beats(a, b) && DamageRules.Beats(b, a), $"{a} and {b} beat each other");
            }
        }

        [Test]
        public void DamageIsAtLeastOne_AndZeroForZeroPower()
        {
            Assert.AreEqual(DamageRules.MinimumDamage, DamageRules.Calculate(0.1f, 1f, 999f, Element.Neutral, Element.Neutral, 0));
            Assert.AreEqual(0f, DamageRules.Calculate(0f, 10f, 10f, Element.Neutral, Element.Neutral, 0.5));
        }

        [Test]
        public void VarianceIsBounded()
        {
            float low = DamageRules.Calculate(100f, 10f, 10f, Element.Neutral, Element.Neutral, 0);
            float high = DamageRules.Calculate(100f, 10f, 10f, Element.Neutral, Element.Neutral, 1);
            Assert.GreaterOrEqual(low, 91f);
            Assert.LessOrEqual(high, 109f);
        }
    }

    public class CreatureStatRulesTests
    {
        [Test]
        public void LevelOne_NeutralPersonality_EqualsBase()
        {
            var b = new StatBlock(60, 10, 10, 10);
            var s = CreatureStatRules.Compute(b, 1, Personality.Playful, 1f);
            Assert.AreEqual(60f, s.maxHealth);
            Assert.AreEqual(10f, s.attack);
        }

        [Test]
        public void HigherLevel_HasHigherStats()
        {
            var b = new StatBlock(60, 10, 10, 10);
            var lv1 = CreatureStatRules.Compute(b, 1, Personality.Bold, 1f);
            var lv10 = CreatureStatRules.Compute(b, 10, Personality.Bold, 1f);
            Assert.Greater(lv10.maxHealth, lv1.maxHealth);
            Assert.Greater(lv10.attack, lv1.attack);
        }

        [Test]
        public void VigorIsClampedAndStarred()
        {
            Assert.AreEqual(CreatureStatRules.MaxVigor, CreatureStatRules.ClampVigor(5f));
            Assert.AreEqual(CreatureStatRules.MinVigor, CreatureStatRules.ClampVigor(-1f));
            Assert.AreEqual(1f, CreatureStatRules.ClampVigor(float.NaN));
            Assert.AreEqual(3, CreatureStatRules.VigorStars(1.1f));
            Assert.AreEqual(2, CreatureStatRules.VigorStars(1.0f));
            Assert.AreEqual(1, CreatureStatRules.VigorStars(0.9f));
        }

        [Test]
        public void PersonalityModifiersApply()
        {
            var b = new StatBlock(100, 100, 100, 100);
            Assert.Greater(CreatureStatRules.Compute(b, 1, Personality.Bold, 1f).attack, 100f);
            Assert.Greater(CreatureStatRules.Compute(b, 1, Personality.Stubborn, 1f).defense, 100f);
            Assert.Greater(CreatureStatRules.Compute(b, 1, Personality.Timid, 1f).speed, 100f);
            foreach (Personality p in System.Enum.GetValues(typeof(Personality)))
            {
                Assert.IsFalse(string.IsNullOrEmpty(CreatureStatRules.Describe(p)));
            }
        }
    }
}
