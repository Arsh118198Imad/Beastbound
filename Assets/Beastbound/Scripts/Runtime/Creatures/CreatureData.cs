using System;
using Beastbound.Rules;
using UnityEngine;

namespace Beastbound.Creatures
{
    public enum AttackKind
    {
        Melee,       // short-range swipe in front
        Projectile,  // thrown / fired object
        Charge       // telegraphed dash that hits anything in the path
    }

    /// <summary>Reusable attack data. Create more via Assets > Create > Beastbound > Attack.</summary>
    [CreateAssetMenu(menuName = "Beastbound/Attack", fileName = "NewAttack")]
    public class AttackDefinition : ScriptableObject
    {
        public string attackId = "tackle";
        public string displayName = "Tackle";
        [TextArea] public string description = "";
        public AttackKind kind = AttackKind.Melee;
        public Element element = Element.Neutral;
        [Min(0f)] public float power = 10f;
        [Tooltip("Distance at which the AI will choose this attack.")]
        [Min(0.5f)] public float range = 2.5f;
        [Min(0.1f)] public float cooldown = 2f;
        [Tooltip("Telegraph time before the hit lands. Longer = easier to dodge.")]
        [Min(0f)] public float windup = 0.45f;
        [Min(0f)] public float projectileSpeed = 14f;
        [Min(0f)] public float knockback = 3f;
        [Min(0.1f)] public float hitRadius = 1.1f;

        [Header("Projectile look")]
        public Color projectileColor = new Color(0.6f, 0.62f, 0.5f);
        [Tooltip("Black = no glow.")]
        public Color projectileGlow = Color.black;
        [Min(0.05f)] public float projectileSize = 0.28f;
        [Tooltip("Gravity on the projectile. 0 = flies straight (energy bolts).")]
        public float projectileGravity = -9f;
    }

    /// <summary>Colours for the procedural creature model, so variants can be made without new art.</summary>
    [Serializable]
    public class CreaturePalette
    {
        public Color body = new Color(0.55f, 0.6f, 0.52f);
        public Color bodyDark = new Color(0.38f, 0.42f, 0.36f);
        public Color belly = new Color(0.93f, 0.89f, 0.76f);
        public Color moss = new Color(0.36f, 0.62f, 0.3f);
        public Color petal = new Color(1f, 0.55f, 0.62f);
        public Color petalCenter = new Color(1f, 0.85f, 0.35f);
        public Color crystal = new Color(1f, 0.72f, 0.25f);
        public Color crystalGlow = new Color(1.4f, 0.8f, 0.2f);
        public Color eye = new Color(0.08f, 0.07f, 0.1f);
    }

    public enum CreatureModelKind
    {
        Pebbloom = 0,
        Thalorix = 1
    }

    /// <summary>
    /// Species definition. Designers can create more via Assets > Create > Beastbound > Creature Species
    /// (menu "Beastbound > Create Editable Creature Data" writes the default Pebbloom as editable assets).
    /// </summary>
    [CreateAssetMenu(menuName = "Beastbound/Creature Species", fileName = "NewSpecies")]
    public class CreatureSpecies : ScriptableObject
    {
        [Header("Identity")]
        public string speciesId = "new_species";
        public string displayName = "New Species";
        [TextArea(3, 6)] public string description = "";
        public Element element = Element.Neutral;
        public CreatureModelKind model = CreatureModelKind.Pebbloom;
        public CreaturePalette palette = new CreaturePalette();
        [Min(0.2f)] public float modelScale = 1f;

        [Header("Stats (level 1)")]
        public StatBlock baseStats = new StatBlock(60f, 10f, 10f, 10f);
        [Range(0.05f, 1f)] public float baseCaptureRate = 0.35f;
        public AttackDefinition[] attacks = new AttackDefinition[0];

        [Header("Movement")]
        [Min(0.1f)] public float walkSpeed = 2f;
        [Min(0.1f)] public float runSpeed = 5.5f;

        [Header("Senses")]
        [Min(1f)] public float sightRadius = 16f;
        [Range(30f, 360f)] public float fieldOfView = 150f;
        [Min(0.5f)] public float hearingRadius = 8f;
        [Min(0.5f)] public float closeRadius = 2.5f;

        [Header("Character")]
        [Tooltip("Shown in the Bestiary.")]
        public string habitat = "";
        [Tooltip("Short in-world fact unlocked when bound.")]
        public string lore = "";

        public bool IsValid(out string problem)
        {
            if (string.IsNullOrWhiteSpace(speciesId)) { problem = "speciesId is empty"; return false; }
            if (attacks == null || attacks.Length == 0) { problem = $"{speciesId} has no attacks"; return false; }
            foreach (var a in attacks)
            {
                if (a == null) { problem = $"{speciesId} has an empty attack slot"; return false; }
            }
            problem = null;
            return true;
        }
    }
}
