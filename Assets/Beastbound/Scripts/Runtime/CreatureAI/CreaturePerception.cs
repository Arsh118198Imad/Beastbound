using Beastbound.Combat;
using Beastbound.Core;
using Beastbound.Creatures;
using Beastbound.Rules;
using UnityEngine;

namespace Beastbound.CreatureAI
{
    /// <summary>
    /// Sight and hearing. Builds "suspicion" over time instead of detecting instantly, which makes sneaking
    /// readable and fair: the HUD shows "?" while suspicion rises and "!" once the creature has noticed you.
    /// </summary>
    public class CreaturePerception : MonoBehaviour
    {
        private const float TickInterval = 0.1f;
        private const float MemorySeconds = 5f;

        private Creature _creature;
        private float _nextTick;
        private float _lastTick = -1f;
        private float _lastDetectedTime = -100f;

        /// <summary>0..1. At 1 the creature is aware of the player.</summary>
        public float Suspicion { get; private set; }
        public bool IsAware => Suspicion >= 0.999f;
        public bool PlayerVisible { get; private set; }
        public float DistanceToPlayer { get; private set; } = float.MaxValue;

        public void Initialize(Creature creature) => _creature = creature;

        public void ForceAware()
        {
            Suspicion = 1f;
            _lastDetectedTime = Time.time;
        }

        public void Calm(float to = 0f) => Suspicion = Mathf.Clamp01(to);

        private void Update()
        {
            if (Time.time < _nextTick) return;
            float dt = _lastTick < 0f ? TickInterval : Mathf.Min(0.5f, Time.time - _lastTick);
            _lastTick = Time.time;
            _nextTick = Time.time + TickInterval;
            if (_creature == null || !_creature.IsWild) return;

            var gm = GameManager.Instance;
            var player = gm != null ? gm.Player : null;
            if (player == null || !player.IsAlive || gm.State == GameState.Title)
            {
                PlayerVisible = false;
                DistanceToPlayer = float.MaxValue;
                Suspicion = Mathf.Max(0f, Suspicion - 0.3f * dt);
                return;
            }

            var species = _creature.Species;
            Vector3 eye = _creature.transform.position + Vector3.up * 0.8f;
            Vector3 target = player.transform.position + Vector3.up * 1.2f;
            Vector3 to = target - eye;
            float dist = to.magnitude;
            DistanceToPlayer = dist;

            bool sleeping = _creature.Brain != null && _creature.Brain.State == CreatureState.Sleep;
            bool night = gm.World != null && gm.World.DayNight != null && gm.World.DayNight.IsNight;
            bool sleepyType = _creature.Record.personality == Personality.Sleepy;

            float rate = 0f;
            PlayerVisible = false;

            // Sight (not while asleep)
            float sightRadius = species.sightRadius * (night ? 0.65f : 1f) * (player.IsSneaking ? 0.6f : 1f);
            if (!sleeping && dist < sightRadius)
            {
                Vector3 flat = to;
                flat.y = 0f;
                if (Vector3.Angle(_creature.transform.forward, flat) <= species.fieldOfView * 0.5f &&
                    CombatQuery.HasLineOfSight(eye, target))
                {
                    PlayerVisible = true;
                    rate += Mathf.Lerp(1.8f, 0.35f, dist / sightRadius);
                }
            }

            // Hearing (scaled by how much noise the player makes)
            float hearing = species.hearingRadius * player.NoiseLevel * (sleeping ? 0.55f : 1f) * (sleepyType ? 0.7f : 1f);
            if (dist < hearing)
            {
                rate += Mathf.Lerp(1.1f, 0.25f, dist / hearing) * (sleeping ? 0.6f : 1f);
            }

            // Too close to ignore (unless you tiptoe past a sleeper)
            if (dist < species.closeRadius && !(sleeping && player.IsSneaking)) rate += 3f;

            if (rate > 0f)
            {
                Suspicion = Mathf.Clamp01(Suspicion + rate * dt);
                if (IsAware) _lastDetectedTime = Time.time;
            }
            else if (!IsAware || Time.time - _lastDetectedTime > MemorySeconds)
            {
                Suspicion = Mathf.Max(0f, Suspicion - 0.22f * dt);
            }
        }
    }
}
