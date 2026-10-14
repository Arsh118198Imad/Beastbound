using Beastbound.Core;
using Beastbound.Creatures;
using Beastbound.World;
using UnityEngine;

namespace Beastbound.CreatureAI
{
    /// <summary>
    /// Moves a creature with a CharacterController: acceleration, turning, gravity, knockback, water avoidance,
    /// play-area clamping and "am I stuck?" detection. The brain only says where and how fast.
    /// </summary>
    public class CreatureMotor : MonoBehaviour
    {
        public float acceleration = 16f;
        public float turnSpeed = 540f;
        public float gravity = -22f;

        private Creature _creature;
        private CharacterController _cc;
        private Vector3 _velocity;
        private Vector3 _desired;
        private float _vertical;
        private Vector3 _knockback;
        private Vector3? _faceTarget;
        private float _stuckTimer;
        private Vector3 _lastPos;

        public bool Frozen { get; set; }
        public bool IsStuck { get; private set; }
        public float CurrentSpeed { get; private set; }
        public Vector3 Velocity => _velocity;

        public void Initialize(Creature creature)
        {
            _creature = creature;
            _cc = GetComponent<CharacterController>();
            _lastPos = transform.position;
        }

        /// <summary>Walks towards a point. Returns true once within <paramref name="stopDistance"/>.</summary>
        public bool MoveTowards(Vector3 target, float speed, float stopDistance = 0.4f)
        {
            Vector3 to = target - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist <= stopDistance)
            {
                _desired = Vector3.zero;
                return true;
            }
            float arrive = Mathf.Clamp01((dist - stopDistance) / 1.5f); // ease in on arrival
            _desired = to / dist * speed * Mathf.Max(0.35f, arrive);
            return false;
        }

        public void MoveDirection(Vector3 direction, float speed)
        {
            direction.y = 0f;
            _desired = direction.sqrMagnitude > 0.0001f ? direction.normalized * speed : Vector3.zero;
        }

        public void Stop() => _desired = Vector3.zero;

        /// <summary>Look at a point instead of the movement direction (cleared each frame).</summary>
        public void FaceTowards(Vector3 point) => _faceTarget = point;

        public void AddKnockback(Vector3 impulse)
        {
            impulse.y = 0f;
            _knockback += impulse;
        }

        public void Teleport(Vector3 position)
        {
            if (_cc != null) _cc.enabled = false;
            transform.position = position;
            if (_cc != null) _cc.enabled = true;
            _velocity = Vector3.zero;
            _knockback = Vector3.zero;
            _lastPos = position;
        }

        private void Update()
        {
            if (_cc == null || !_cc.enabled) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;

            Vector3 desired = Frozen ? Vector3.zero : _desired;

            // Refuse to walk into deep water or out of the play area (creatures stay on land).
            if (world != null && desired.sqrMagnitude > 0.01f)
            {
                Vector3 ahead = transform.position + desired.normalized * 1.2f;
                bool currentlyWet = world.WaterDepth(transform.position) > 0.15f;
                if ((!currentlyWet && world.WaterDepth(ahead) > 0.15f) || !world.IsInsidePlayArea(ahead, 2f))
                {
                    desired = Vector3.zero;
                    IsStuck = true;
                }
            }

            _velocity = Vector3.MoveTowards(_velocity, desired, acceleration * dt);
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, 18f * dt);

            if (_cc.isGrounded && _vertical < 0f) _vertical = -2f;
            else _vertical += gravity * dt;

            Vector3 motion = (_velocity + _knockback) * dt + Vector3.up * _vertical * dt;
            _cc.Move(motion);

            // Safety net: never fall through the world.
            if (world != null)
            {
                float ground = world.GroundHeight(transform.position.x, transform.position.z);
                if (transform.position.y < ground - 2f) Teleport(new Vector3(transform.position.x, ground + 0.2f, transform.position.z));
            }

            // Rotation
            Vector3 face = Vector3.zero;
            if (_faceTarget.HasValue)
            {
                face = _faceTarget.Value - transform.position;
            }
            else if (_velocity.sqrMagnitude > 0.05f)
            {
                face = _velocity;
            }
            face.y = 0f;
            if (face.sqrMagnitude > 0.0001f && !Frozen)
            {
                var targetRot = Quaternion.LookRotation(face.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * dt);
            }
            _faceTarget = null;

            // Speed + stuck detection
            Vector3 moved = transform.position - _lastPos;
            moved.y = 0f;
            CurrentSpeed = moved.magnitude / dt;
            _lastPos = transform.position;
            if (desired.sqrMagnitude > 0.25f && CurrentSpeed < desired.magnitude * 0.2f)
            {
                _stuckTimer += dt;
                if (_stuckTimer > 1.2f) IsStuck = true;
            }
            else
            {
                _stuckTimer = 0f;
                if (desired.sqrMagnitude > 0.25f) IsStuck = false;
            }

            if (_creature != null && _creature.Animator != null) _creature.Animator.Speed = Frozen ? 0f : CurrentSpeed;
        }

        /// <summary>Clears the stuck flag after the brain has reacted to it.</summary>
        public void ClearStuck()
        {
            IsStuck = false;
            _stuckTimer = 0f;
        }
    }
}
