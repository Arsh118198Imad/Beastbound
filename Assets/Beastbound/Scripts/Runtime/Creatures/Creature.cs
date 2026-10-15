using Beastbound.Combat;
using Beastbound.Core;
using Beastbound.CreatureAI;
using Beastbound.Rules;
using Beastbound.World;
using UnityEngine;

namespace Beastbound.Creatures
{
    public enum CreatureRole
    {
        Wild,
        Companion
    }

    /// <summary>
    /// Root component of a creature in the world. Holds identity (species + individual record) and wires
    /// together the separate systems: Health (combat numbers), Motor (movement), Perception (senses),
    /// Brain (decisions) and Animator (visuals).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class Creature : MonoBehaviour
    {
        public CreatureSpecies Species { get; private set; }
        public CreatureRecord Record { get; private set; }
        public StatBlock Stats { get; private set; }
        public CreatureRole Role { get; private set; }
        public NestPoint Nest { get; private set; }
        public Health Health { get; private set; }
        public CreatureMotor Motor { get; private set; }
        public CreaturePerception Perception { get; private set; }
        public CreatureBrain Brain { get; private set; }
        public CreatureAnimator Animator { get; private set; }

        /// <summary>Failed bind attempts on this individual. Each one raises the next chance (Resonance).</summary>
        public int FailedBindAttempts { get; set; }

        public string DisplayName => Record != null ? Record.DisplayName(Species) : "Creature";
        public bool IsWild => Role == CreatureRole.Wild;
        public Vector3 HomePosition => Nest != null ? Nest.Position : transform.position;
        public float HomeRadius => Nest != null ? Nest.HomeRadius : 10f;

        /// <summary>Approximate centre of the body, for aiming and UI markers.</summary>
        public Vector3 Center => transform.position + Vector3.up * 0.55f * (Species != null ? Species.modelScale : 1f);
        public Vector3 MarkerPoint => transform.position + Vector3.up * 1.75f * (Species != null ? Species.modelScale : 1f);

        public static Creature Spawn(CreatureSpecies species, CreatureRecord record, Vector3 position, Quaternion rotation,
            CreatureRole role, NestPoint nest, Transform parent)
        {
            if (species == null || record == null)
            {
                Debug.LogError("[Beastbound] Cannot spawn creature: missing species or record.");
                return null;
            }

            var go = new GameObject($"{species.displayName} ({role}, {record.personality})");
            go.layer = Layers.Characters;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);

            float scale = Mathf.Max(0.2f, species.modelScale);
            var cc = go.AddComponent<CharacterController>();
            cc.radius = 0.42f * scale;
            cc.height = 0.95f * scale;
            cc.center = new Vector3(0f, 0.5f * scale, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.3f * scale;
            cc.skinWidth = 0.04f;

            var creature = go.AddComponent<Creature>();
            creature.Species = species;
            creature.Record = record;
            creature.Role = role;
            creature.Nest = nest;
            creature.Stats = record.ComputeStats(species);

            creature.Health = go.AddComponent<Health>();
            creature.Health.Configure(role == CreatureRole.Wild ? Faction.Wild : Faction.Companion,
                creature.Stats.maxHealth, creature.Stats.defense, species.element);

            creature.Animator = CreatureVisuals.Attach(go, species, record.uid);

            creature.Motor = go.AddComponent<CreatureMotor>();
            creature.Perception = go.AddComponent<CreaturePerception>();
            creature.Brain = go.AddComponent<CreatureBrain>();
            creature.Motor.Initialize(creature);
            creature.Perception.Initialize(creature);
            creature.Brain.Initialize(creature);
            return creature;
        }

        /// <summary>Capture awareness used by the binding formula.</summary>
        public Awareness CurrentAwareness
        {
            get
            {
                if (Brain == null) return Awareness.Alert;
                switch (Brain.State)
                {
                    case CreatureState.Exhausted: return Awareness.Exhausted;
                    case CreatureState.Sleep: return Awareness.Asleep;
                    case CreatureState.Combat: return Awareness.Hostile;
                    case CreatureState.Curious:
                    case CreatureState.Flee: return Awareness.Alert;
                    default: return Perception != null && Perception.Suspicion >= 0.5f ? Awareness.Alert : Awareness.Unaware;
                }
            }
        }

        public float CaptureChance
        {
            get
            {
                if (Health == null || Species == null) return 0f;
                return CaptureRules.CalculateChance(Species.baseCaptureRate, Health.Current, Health.Max,
                    CurrentAwareness, FailedBindAttempts);
            }
        }

        public bool CanBeBound => IsWild && Brain != null && Brain.State != CreatureState.Binding;
    }
}
