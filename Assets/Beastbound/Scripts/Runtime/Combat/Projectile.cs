using System;
using System.Collections.Generic;
using Beastbound.Core;
using Beastbound.Rules;
using UnityEngine;

namespace Beastbound.Combat
{
    /// <summary>
    /// Simple travelling projectile (pebbles, spark bursts). Moves manually (no Rigidbody), checks for
    /// characters with an overlap sphere and for terrain/props with a raycast along its path.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        private Faction _faction;
        private GameObject _owner;
        private float _power;
        private float _attack;
        private Element _element;
        private Vector3 _velocity;
        private float _gravity;
        private float _radius;
        private float _knockback;
        private float _dieAt;
        private bool _done;
        private Transform _homingTarget;
        private float _homingStrength;
        private readonly List<Health> _hits = new List<Health>();

        public event Action<Vector3> Impact;

        public static Projectile Spawn(string name, Vector3 position, Vector3 velocity, Faction faction, GameObject owner,
            float power, float attack, Element element, float radius, float knockback, Material material,
            float visualScale, float gravity = 0f, float lifetime = 3f)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var p = go.AddComponent<Projectile>();
            p._faction = faction;
            p._owner = owner;
            p._power = power;
            p._attack = attack;
            p._element = element;
            p._velocity = velocity;
            p._gravity = gravity;
            p._radius = radius;
            p._knockback = knockback;
            p._dieAt = Time.time + lifetime;

            var visual = MeshKit.AddPart(go.transform, "Visual", MeshKit.Primitive(PrimitiveType.Sphere), material,
                Vector3.zero, Vector3.one * visualScale, Quaternion.identity, false);
            visual.layer = 0;
            return p;
        }

        /// <summary>Gently steers towards a target (used by the player's Spark Burst for forgiving aim).</summary>
        public void SetHoming(Transform target, float strength)
        {
            _homingTarget = target;
            _homingStrength = strength;
        }

        private void Update()
        {
            if (_done) return;
            float dt = Time.deltaTime;
            if (Time.time > _dieAt)
            {
                Finish(transform.position);
                return;
            }

            if (_homingTarget != null && _homingStrength > 0f)
            {
                Vector3 to = (_homingTarget.position + Vector3.up * 0.5f) - transform.position;
                float speed = _velocity.magnitude;
                _velocity = Vector3.RotateTowards(_velocity, to.normalized * speed, _homingStrength * dt, 0f);
            }

            _velocity += Vector3.up * _gravity * dt;
            Vector3 start = transform.position;
            Vector3 step = _velocity * dt;
            float dist = step.magnitude;

            if (dist > 0.0001f && Physics.SphereCast(start, _radius * 0.5f, step / dist, out RaycastHit hit, dist,
                    Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point;
                CheckCharacters(hit.point);
                Finish(hit.point);
                return;
            }

            transform.position = start + step;
            transform.Rotate(Vector3.right, 540f * dt, Space.Self);
            if (CheckCharacters(transform.position)) Finish(transform.position);
        }

        private bool CheckCharacters(Vector3 point)
        {
            CombatQuery.FindHostiles(point, _radius, _faction, _owner, _hits);
            if (_hits.Count == 0) return false;
            foreach (var h in _hits)
            {
                Vector3 dir = _velocity;
                dir.y = 0f;
                h.TakeHit(_power, _attack, _element, _faction, _owner, point, dir, _knockback);
            }
            return true;
        }

        private void Finish(Vector3 point)
        {
            if (_done) return;
            _done = true;
            Impact?.Invoke(point);
            Destroy(gameObject);
        }
    }
}
