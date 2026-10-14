using System;
using System.Collections.Generic;
using Beastbound.Audio;
using Beastbound.Core;
using Beastbound.CreatureAI;
using Beastbound.Creatures;
using UnityEngine;

namespace Beastbound.Combat
{
    public enum BattleOutcome
    {
        Victory,   // the creature was exhausted
        Bound,     // the creature was bound (collected) mid-fight
        Escaped,   // the creature or the player left
        Defeat     // the player was overwhelmed
    }

    /// <summary>
    /// Tracks which wild creatures are currently fighting the player. Battles are seamless (no separate
    /// battle screen): a battle starts when a creature turns hostile and ends when it is exhausted, bound,
    /// loses interest, or the player is defeated.
    /// </summary>
    public class BattleController : MonoBehaviour
    {
        private readonly List<Creature> _engaged = new List<Creature>();
        private readonly HashSet<Creature> _bound = new HashSet<Creature>();

        public Creature PrimaryEnemy { get; private set; }
        public bool InBattle => _engaged.Count > 0;
        public IReadOnlyList<Creature> Engaged => _engaged;

        public event Action<Creature> BattleStarted;
        public event Action<BattleOutcome, Creature> BattleEnded;

        public void Engage(Creature creature)
        {
            if (creature == null || _engaged.Contains(creature)) return;
            bool first = _engaged.Count == 0;
            _engaged.Add(creature);
            PrimaryEnemy = creature;
            if (first)
            {
                AudioManager.PlayUi(SfxId.BattleStart);
                BattleStarted?.Invoke(creature);
            }
        }

        /// <summary>Call right before a bound creature is removed so the result is reported as "Bound".</summary>
        public void MarkBound(Creature creature)
        {
            if (creature != null && _engaged.Contains(creature)) _bound.Add(creature);
        }

        public void Disengage(Creature creature)
        {
            int index = _engaged.IndexOf(creature);
            if (index < 0) return;

            BattleOutcome outcome;
            if (_bound.Remove(creature)) outcome = BattleOutcome.Bound;
            else if (creature != null && creature.Brain != null && creature.Brain.State == CreatureState.Binding) return; // still deciding
            else if (creature != null && creature.Brain != null && creature.Brain.State == CreatureState.Exhausted) outcome = BattleOutcome.Victory;
            else outcome = BattleOutcome.Escaped;

            _engaged.RemoveAt(index);
            PrimaryEnemy = _engaged.Count > 0 ? _engaged[_engaged.Count - 1] : null;
            if (outcome == BattleOutcome.Victory) AudioManager.PlayUi(SfxId.Victory);
            BattleEnded?.Invoke(outcome, creature);
        }

        /// <summary>Ends every fight at once (player defeated, world reset).</summary>
        public void ResetAll(bool playerDefeated)
        {
            var copy = new List<Creature>(_engaged);
            _engaged.Clear();
            _bound.Clear();
            PrimaryEnemy = null;
            if (playerDefeated)
            {
                foreach (var c in copy) BattleEnded?.Invoke(BattleOutcome.Defeat, c);
            }
        }

        private void LateUpdate()
        {
            // Drop references to creatures that were destroyed without disengaging.
            for (int i = _engaged.Count - 1; i >= 0; i--)
            {
                if (_engaged[i] == null) _engaged.RemoveAt(i);
            }
            if (PrimaryEnemy == null && _engaged.Count > 0) PrimaryEnemy = _engaged[_engaged.Count - 1];
        }
    }
}
