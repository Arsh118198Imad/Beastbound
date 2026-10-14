using System;
using Beastbound.Rules;

namespace Beastbound.Creatures
{
    /// <summary>
    /// One individual creature as stored in the Bestiary and in save files. Plain data so it serialises with
    /// JsonUtility and could later be sent over a network unchanged.
    /// </summary>
    [Serializable]
    public class CreatureRecord
    {
        public string uid;
        public string speciesId;
        public string nickname;
        public int level = 1;
        public Personality personality = Personality.Curious;
        public float vigor = 1f;
        public string originNestId;
        public string boundAtUtc;

        public static CreatureRecord Create(string speciesId, int level, Personality personality, float vigor, string nestId)
        {
            return new CreatureRecord
            {
                uid = Guid.NewGuid().ToString("N"),
                speciesId = speciesId,
                nickname = "",
                level = Math.Max(CreatureStatRules.MinLevel, Math.Min(CreatureStatRules.MaxLevel, level)),
                personality = personality,
                vigor = CreatureStatRules.ClampVigor(vigor),
                originNestId = nestId,
                boundAtUtc = ""
            };
        }

        public CreatureRecord Clone()
        {
            return (CreatureRecord)MemberwiseClone();
        }

        public string DisplayName(CreatureSpecies species)
        {
            if (!string.IsNullOrWhiteSpace(nickname)) return nickname;
            return species != null ? species.displayName : speciesId;
        }

        public StatBlock ComputeStats(CreatureSpecies species)
        {
            var baseStats = species != null ? species.baseStats : new StatBlock(50, 10, 10, 10);
            return CreatureStatRules.Compute(baseStats, level, personality, vigor);
        }
    }
}
