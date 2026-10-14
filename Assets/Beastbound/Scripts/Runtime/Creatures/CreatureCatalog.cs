using System.Collections.Generic;
using Beastbound.Rules;
using UnityEngine;

namespace Beastbound.Creatures
{
    /// <summary>
    /// Registry of all species. Uses designer-made assets when the bootstrap provides them and falls back
    /// to built-in defaults otherwise, so the prototype always has valid creature data.
    /// </summary>
    public class CreatureCatalog
    {
        private readonly Dictionary<string, CreatureSpecies> _byId = new Dictionary<string, CreatureSpecies>();

        public IEnumerable<CreatureSpecies> All => _byId.Values;
        public int Count => _byId.Count;

        public CreatureCatalog(IEnumerable<CreatureSpecies> overrides)
        {
            if (overrides != null)
            {
                foreach (var s in overrides)
                {
                    if (s == null) continue;
                    if (!s.IsValid(out string problem))
                    {
                        Debug.LogWarning($"[Beastbound] Ignoring invalid species asset '{s.name}': {problem}");
                        continue;
                    }
                    _byId[s.speciesId] = s;
                }
            }
            if (!_byId.ContainsKey(DefaultContent.PebbloomId))
            {
                var pebbloom = DefaultContent.CreatePebbloom();
                _byId[pebbloom.speciesId] = pebbloom;
            }
        }

        public CreatureSpecies Get(string speciesId)
        {
            if (!string.IsNullOrEmpty(speciesId) && _byId.TryGetValue(speciesId, out var s)) return s;
            return null;
        }
    }

    /// <summary>Built-in original content. All names, designs and numbers are original to Beastbound.</summary>
    public static class DefaultContent
    {
        public const string PebbloomId = "pebbloom";

        public static AttackDefinition CreatePebbleToss()
        {
            var a = ScriptableObject.CreateInstance<AttackDefinition>();
            a.name = "Attack_PebbleToss";
            a.attackId = "pebble_toss";
            a.displayName = "Pebble Toss";
            a.description = "Flicks a mossy pebble from its tail-bloom in a gentle arc.";
            a.kind = AttackKind.Projectile;
            a.element = Element.Stone;
            a.power = 9f;
            a.range = 13f;
            a.cooldown = 2.6f;
            a.windup = 0.55f;
            a.projectileSpeed = 15f;
            a.knockback = 2.5f;
            a.hitRadius = 0.55f;
            return a;
        }

        public static AttackDefinition CreateBrambleRush()
        {
            var a = ScriptableObject.CreateInstance<AttackDefinition>();
            a.name = "Attack_BrambleRush";
            a.attackId = "bramble_rush";
            a.displayName = "Bramble Rush";
            a.description = "Tucks its petal-ears back and barrels forward. Clearly telegraphed — dodge sideways!";
            a.kind = AttackKind.Charge;
            a.element = Element.Verdant;
            a.power = 14f;
            a.range = 7f;
            a.cooldown = 4.2f;
            a.windup = 0.7f;
            a.projectileSpeed = 11f; // dash speed for charges
            a.knockback = 6f;
            a.hitRadius = 1.1f;
            return a;
        }

        public static AttackDefinition CreatePetalSwipe()
        {
            var a = ScriptableObject.CreateInstance<AttackDefinition>();
            a.name = "Attack_PetalSwipe";
            a.attackId = "petal_swipe";
            a.displayName = "Petal Swipe";
            a.description = "A quick swat with a hardened petal-ear.";
            a.kind = AttackKind.Melee;
            a.element = Element.Verdant;
            a.power = 8f;
            a.range = 2.2f;
            a.cooldown = 1.6f;
            a.windup = 0.35f;
            a.knockback = 3f;
            a.hitRadius = 1.3f;
            return a;
        }

        public static CreatureSpecies CreatePebbloom()
        {
            var s = ScriptableObject.CreateInstance<CreatureSpecies>();
            s.name = "Species_Pebbloom";
            s.speciesId = PebbloomId;
            s.displayName = "Pebbloom";
            s.element = Element.Verdant;
            s.model = CreatureModelKind.Pebbloom;
            s.description = "A round, pebble-backed creature that carries a living flower on its tail. " +
                            "Moss grows on the stone plates of its back, and an amber crystal on its brow " +
                            "glows brighter when it is happy.";
            s.habitat = "Meadows, lakeshores and old ruins of Sunmeadow Vale.";
            s.lore = "Travellers say a Pebbloom's tail-flower closes at dusk and opens again only " +
                     "for someone it trusts.";
            s.palette = new CreaturePalette();
            s.modelScale = 1f;
            s.baseStats = new StatBlock(58f, 11f, 12f, 10f);
            s.baseCaptureRate = 0.32f;
            s.attacks = new[] { CreatePebbleToss(), CreateBrambleRush(), CreatePetalSwipe() };
            s.walkSpeed = 1.9f;
            s.runSpeed = 5.6f;
            s.sightRadius = 16f;
            s.fieldOfView = 150f;
            s.hearingRadius = 8f;
            s.closeRadius = 2.4f;
            return s;
        }
    }
}
