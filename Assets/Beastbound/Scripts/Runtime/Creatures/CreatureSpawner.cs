using System;
using System.Collections.Generic;
using Beastbound.World;
using UnityEngine;

namespace Beastbound.Creatures
{
    /// <summary>Creates, tracks and removes creatures in the world (wild ones at their nests, plus the partner).</summary>
    public class CreatureSpawner : MonoBehaviour
    {
        private readonly List<Creature> _wild = new List<Creature>();
        private Transform _root;

        public IReadOnlyList<Creature> WildCreatures => _wild;
        public Creature Companion { get; private set; }

        private Transform Root
        {
            get
            {
                if (_root == null)
                {
                    _root = new GameObject("Creatures").transform;
                    _root.SetParent(transform, false);
                }
                return _root;
            }
        }

        /// <summary>Spawns one wild creature per nest, skipping nests whose creature was already bound.</summary>
        public void SpawnWild(WorldContext world, CreatureCatalog catalog, Func<string, bool> isNestCaptured)
        {
            DespawnWild();
            if (world == null || catalog == null) return;
            foreach (var nest in world.Nests)
            {
                if (isNestCaptured != null && isNestCaptured(nest.Id)) continue;
                var species = catalog.Get(nest.SpeciesId);
                if (species == null)
                {
                    Debug.LogWarning($"[Beastbound] Nest {nest.Id} references unknown species '{nest.SpeciesId}'.");
                    continue;
                }
                var record = CreatureRecord.Create(species.speciesId, nest.Level, nest.Personality, nest.Vigor, nest.Id);
                var rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                var creature = Creature.Spawn(species, record, nest.Position + Vector3.up * 0.2f, rot, CreatureRole.Wild, nest, Root);
                if (creature != null) _wild.Add(creature);
            }
        }

        public void RemoveWild(Creature creature)
        {
            if (creature == null) return;
            _wild.Remove(creature);
            Destroy(creature.gameObject);
        }

        public Creature SpawnCompanion(CreatureSpecies species, CreatureRecord record, Vector3 position, Quaternion rotation)
        {
            DespawnCompanion();
            if (species == null || record == null) return null;
            Companion = Creature.Spawn(species, record, position + Vector3.up * 0.2f, rotation, CreatureRole.Companion, null, Root);
            return Companion;
        }

        public void DespawnCompanion()
        {
            if (Companion != null) Destroy(Companion.gameObject);
            Companion = null;
        }

        public void DespawnWild()
        {
            foreach (var c in _wild)
            {
                if (c != null) Destroy(c.gameObject);
            }
            _wild.Clear();
        }

        public void DespawnAll()
        {
            DespawnWild();
            DespawnCompanion();
        }

        /// <summary>Calms every creature and sends wild ones home (after the player is defeated).</summary>
        public void ResetAllToCalm()
        {
            foreach (var c in _wild)
            {
                if (c != null && c.Brain != null) c.Brain.ResetToCalm();
            }
            if (Companion != null && Companion.Brain != null) Companion.Brain.ResetToCalm();
        }

        private void LateUpdate()
        {
            for (int i = _wild.Count - 1; i >= 0; i--)
            {
                if (_wild[i] == null) _wild.RemoveAt(i);
            }
        }
    }
}
