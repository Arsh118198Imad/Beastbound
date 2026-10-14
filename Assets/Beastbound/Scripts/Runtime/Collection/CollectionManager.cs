using System;
using System.Collections.Generic;
using Beastbound.Core;
using Beastbound.Creatures;
using Beastbound.Rules;
using UnityEngine;

namespace Beastbound.Collection
{
    /// <summary>
    /// The player's Bestiary: every bound creature, which one is the active partner, and which nests are
    /// already empty. Pure bookkeeping plus the hand-off from "wild creature" to "partner".
    /// </summary>
    public class CollectionManager : MonoBehaviour
    {
        private readonly List<CreatureRecord> _records = new List<CreatureRecord>();
        private readonly HashSet<string> _capturedNests = new HashSet<string>();
        private readonly HashSet<string> _seenSpecies = new HashSet<string>();

        public IReadOnlyList<CreatureRecord> Records => _records;
        public string PartnerUid { get; private set; }
        public int Count => _records.Count;

        public event Action<CreatureRecord> CreatureBound;
        public event Action Changed;

        private void OnEnable() => GameEvents.WildCreatureSpotted += OnSpotted;
        private void OnDisable() => GameEvents.WildCreatureSpotted -= OnSpotted;

        private void OnSpotted(string speciesId)
        {
            if (!string.IsNullOrEmpty(speciesId) && _seenSpecies.Add(speciesId)) Changed?.Invoke();
        }

        public bool HasSeen(string speciesId) => _seenSpecies.Contains(speciesId);
        public IEnumerable<string> SeenSpecies => _seenSpecies;
        public bool IsNestCaptured(string nestId) => !string.IsNullOrEmpty(nestId) && _capturedNests.Contains(nestId);
        public IEnumerable<string> CapturedNests => _capturedNests;

        public CreatureRecord Partner => Find(PartnerUid);

        public CreatureRecord Find(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            foreach (var r in _records)
            {
                if (r.uid == uid) return r;
            }
            return null;
        }

        /// <summary>Called by the binder when a bind succeeds.</summary>
        public void BindWildCreature(Creature wild, Awareness awarenessAtThrow)
        {
            var gm = GameManager.Instance;
            if (wild == null || gm == null) return;

            var record = wild.Record.Clone();
            record.boundAtUtc = DateTime.UtcNow.ToString("o");
            _records.Add(record);
            _seenSpecies.Add(record.speciesId);
            if (wild.Nest != null) _capturedNests.Add(wild.Nest.Id);

            gm.Battle?.MarkBound(wild);
            Vector3 pos = wild.transform.position;
            Quaternion rot = wild.transform.rotation;
            var species = wild.Species;
            string name = wild.DisplayName;
            gm.Spawner?.RemoveWild(wild);

            bool becomesPartner = Partner == null;
            if (becomesPartner)
            {
                PartnerUid = record.uid;
                gm.Spawner?.SpawnCompanion(species, record, pos, rot);
            }

            string how = awarenessAtThrow == Awareness.Asleep ? " while it slept"
                : awarenessAtThrow == Awareness.Unaware ? " without being noticed" : "";
            GameEvents.Notify($"{name} bound{how}! It joined your Bestiary." +
                              (becomesPartner ? " It will follow you as your partner." : ""), NotificationKind.Discovery);

            CreatureBound?.Invoke(record);
            Changed?.Invoke();
            gm.SaveGame(true);
        }

        /// <summary>Makes another bound creature the active partner (it appears next to the player).</summary>
        public void SetPartner(string uid)
        {
            var gm = GameManager.Instance;
            var record = Find(uid);
            if (record == null || gm == null || uid == PartnerUid) return;
            PartnerUid = uid;
            var player = gm.Player;
            if (player != null && gm.Spawner != null)
            {
                var species = gm.Catalog.Get(record.speciesId);
                Vector3 pos = player.transform.position - player.transform.forward * 2f;
                if (gm.World != null) gm.World.TryFindWalkableNear(pos, 3f, out pos, 8);
                gm.Spawner.SpawnCompanion(species, record, pos, player.transform.rotation);
            }
            GameEvents.Notify($"{record.DisplayName(gm.Catalog.Get(record.speciesId))} is now your partner.", NotificationKind.Success);
            Changed?.Invoke();
        }

        public void Clear()
        {
            _records.Clear();
            _capturedNests.Clear();
            _seenSpecies.Clear();
            PartnerUid = null;
            Changed?.Invoke();
        }

        public void Load(IEnumerable<CreatureRecord> records, string partnerUid, IEnumerable<string> capturedNests, IEnumerable<string> seenSpecies)
        {
            Clear();
            if (records != null)
            {
                foreach (var r in records)
                {
                    if (r == null || string.IsNullOrEmpty(r.uid) || string.IsNullOrEmpty(r.speciesId)) continue;
                    r.level = Mathf.Clamp(r.level, CreatureStatRules.MinLevel, CreatureStatRules.MaxLevel);
                    r.vigor = CreatureStatRules.ClampVigor(r.vigor);
                    _records.Add(r);
                    _seenSpecies.Add(r.speciesId);
                }
            }
            if (capturedNests != null) foreach (var n in capturedNests) if (!string.IsNullOrEmpty(n)) _capturedNests.Add(n);
            if (seenSpecies != null) foreach (var s in seenSpecies) if (!string.IsNullOrEmpty(s)) _seenSpecies.Add(s);
            PartnerUid = Find(partnerUid) != null ? partnerUid : (_records.Count > 0 ? _records[0].uid : null);
            Changed?.Invoke();
        }
    }
}
