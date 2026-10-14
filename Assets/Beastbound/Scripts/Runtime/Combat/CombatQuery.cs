using System.Collections.Generic;
using Beastbound.Core;
using UnityEngine;

namespace Beastbound.Combat
{
    /// <summary>Physics queries for finding things to hit. Allocation-free after warm-up.</summary>
    public static class CombatQuery
    {
        private static readonly Collider[] Buffer = new Collider[32];
        private static readonly HashSet<Health> Seen = new HashSet<Health>();

        /// <summary>
        /// Fills <paramref name="results"/> with hostile Health components inside the sphere.
        /// If <paramref name="forward"/> is non-zero, only targets within <paramref name="maxAngle"/> degrees are kept.
        /// </summary>
        public static void FindHostiles(Vector3 center, float radius, Faction attacker, GameObject self,
            List<Health> results, Vector3 forward = default, float maxAngle = 180f)
        {
            results.Clear();
            Seen.Clear();
            int count = Physics.OverlapSphereNonAlloc(center, radius, Buffer, Layers.CharacterMask,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var col = Buffer[i];
                if (col == null) continue;
                var health = col.GetComponentInParent<Health>();
                if (health == null || health.IsDead || health.gameObject == self) continue;
                if (Health.AreAllies(attacker, health.Faction)) continue;
                if (!Seen.Add(health)) continue;

                if (forward.sqrMagnitude > 0.0001f && maxAngle < 180f)
                {
                    Vector3 to = health.transform.position - center;
                    to.y = 0f;
                    Vector3 f = forward;
                    f.y = 0f;
                    if (to.sqrMagnitude > 0.01f && Vector3.Angle(f, to) > maxAngle) continue;
                }
                results.Add(health);
            }
        }

        /// <summary>Closest hostile within radius, or null.</summary>
        public static Health FindClosestHostile(Vector3 center, float radius, Faction attacker, GameObject self,
            List<Health> scratch)
        {
            FindHostiles(center, radius, attacker, self, scratch);
            Health best = null;
            float bestSqr = float.MaxValue;
            foreach (var h in scratch)
            {
                float d = (h.transform.position - center).sqrMagnitude;
                if (d < bestSqr)
                {
                    bestSqr = d;
                    best = h;
                }
            }
            return best;
        }

        /// <summary>True if nothing in the environment blocks the line between two points.</summary>
        public static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.01f) return true;
            return !Physics.Raycast(from, dir / dist, dist, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);
        }
    }
}
