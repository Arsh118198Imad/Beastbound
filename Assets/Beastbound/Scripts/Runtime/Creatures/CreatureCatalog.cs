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
            if (!_byId.ContainsKey(DefaultContent.ThalorixId))
            {
                var thalorix = DefaultContent.CreateThalorix();
                _byId[thalorix.speciesId] = thalorix;
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
        public const string ThalorixId = "thalorix";

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

        public static AttackDefinition CreateThornfangLunge()
        {
            var a = ScriptableObject.CreateInstance<AttackDefinition>();
            a.name = "Attack_ThornfangLunge";
            a.attackId = "thornfang_lunge";
            a.displayName = "Thornfang Lunge";
            a.description = "Lowers its horns, the crystals flare, then it springs. Watch for the crouch and dodge sideways.";
            a.kind = AttackKind.Charge;
            a.element = Element.Verdant;
            a.power = 17f;
            a.range = 9f;
            a.cooldown = 4.6f;
            a.windup = 0.85f;
            a.projectileSpeed = 12.5f; // dash speed for charges
            a.knockback = 7f;
            a.hitRadius = 1.4f;
            return a;
        }

        public static AttackDefinition CreateVeinlightBolt()
        {
            var a = ScriptableObject.CreateInstance<AttackDefinition>();
            a.name = "Attack_VeinlightBolt";
            a.attackId = "veinlight_bolt";
            a.displayName = "Veinlight Bolt";
            a.description = "Gathers the blue light from its veins into a bolt and looses it from its horns.";
            a.kind = AttackKind.Projectile;
            a.element = Element.Verdant;
            a.power = 11f;
            a.range = 15f;
            a.cooldown = 2.9f;
            a.windup = 0.6f;
            a.projectileSpeed = 17f;
            a.knockback = 3f;
            a.hitRadius = 0.6f;
            a.projectileColor = new Color(0.35f, 0.78f, 1f);
            a.projectileGlow = new Color(0.45f, 1.1f, 1.5f);
            a.projectileSize = 0.3f;
            a.projectileGravity = -1.5f;
            return a;
        }

        public static AttackDefinition CreateGuardianSwipe()
        {
            var a = ScriptableObject.CreateInstance<AttackDefinition>();
            a.name = "Attack_GuardianSwipe";
            a.attackId = "guardian_swipe";
            a.displayName = "Guardian Swipe";
            a.description = "A heavy forepaw swipe that clears the space in front of it.";
            a.kind = AttackKind.Melee;
            a.element = Element.Verdant;
            a.power = 12f;
            a.range = 2.9f;
            a.cooldown = 1.9f;
            a.windup = 0.45f;
            a.knockback = 4.5f;
            a.hitRadius = 1.7f;
            return a;
        }

        public static CreatureSpecies CreateThalorix()
        {
            var s = ScriptableObject.CreateInstance<CreatureSpecies>();
            s.name = "Species_Thalorix";
            s.speciesId = ThalorixId;
            s.displayName = "Thalorix";
            s.element = Element.Verdant;
            s.model = CreatureModelKind.Thalorix;
            s.description = "A maned forest guardian, part wolf and part lion. Blue light runs through the veins of its " +
                            "legs and face, and its swept-back horns end in living crystal that brightens when it is " +
                            "about to strike.";
            s.habitat = "Deep, quiet woodland at the western edge of Sunmeadow Vale.";
            s.lore = "Old wardens say a Thalorix chooses whom it follows. It will not be rushed — only patience, " +
                     "or a fair fight, earns its trust.";
            s.palette = new CreaturePalette
            {
                body = new Color(0.3f, 0.33f, 0.36f), bodyDark = new Color(0.16f, 0.18f, 0.2f),
                belly = new Color(0.55f, 0.55f, 0.52f), moss = new Color(0.2f, 0.3f, 0.28f),
                petal = new Color(0.25f, 0.6f, 0.85f), petalCenter = new Color(0.5f, 0.85f, 1f),
                crystal = new Color(0.45f, 0.85f, 1f), crystalGlow = new Color(0.3f, 0.9f, 1.4f)
            };
            s.modelScale = 2f; // collider, aim point and marker height (the visual model has its own scale)
            s.baseStats = new StatBlock(92f, 15f, 15f, 12f);
            s.baseCaptureRate = 0.16f;
            s.attacks = new[] { CreateThornfangLunge(), CreateVeinlightBolt(), CreateGuardianSwipe() };
            s.walkSpeed = 2.3f;
            s.runSpeed = 7.2f;
            s.sightRadius = 20f;
            s.fieldOfView = 160f;
            s.hearingRadius = 10f;
            s.closeRadius = 3.2f;
            return s;
        }
    }
}
