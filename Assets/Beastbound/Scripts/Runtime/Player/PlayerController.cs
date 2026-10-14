using Beastbound.Audio;
using Beastbound.Combat;
using Beastbound.Controls;
using Beastbound.Core;
using UnityEngine;

namespace Beastbound.Player
{
    /// <summary>
    /// Third-person movement: camera-relative walking/running/sneaking, jumping with coyote time and input
    /// buffering, wading in water, knockback and respawning. Combat and binding live in separate components.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        public float walkSpeed = 3.9f;
        public float runSpeed = 7f;
        public float sneakSpeed = 2.1f;
        public float wadeMultiplier = 0.55f;

        [Header("Feel")]
        public float acceleration = 30f;
        public float deceleration = 26f;
        [Range(0f, 1f)] public float airControl = 0.45f;
        public float turnSpeed = 720f;
        public float jumpHeight = 1.25f;
        public float gravity = -24f;
        public float coyoteTime = 0.12f;
        public float jumpBufferTime = 0.15f;

        private CharacterController _cc;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;
        private float _lastGroundedTime = -10f;
        private float _jumpPressedTime = -10f;
        private Vector3 _knockback;
        private float _footstepDistance;
        private bool _wasGrounded = true;
        private float _forcedUntil;
        private Vector3 _forcedVelocity;

        public Health Health { get; private set; }
        public PlayerCombat Combat { get; private set; }
        public PlayerBinder Binder { get; private set; }
        public PlayerAvatar Avatar { get; private set; }
        public Transform CameraTransform { get; set; }

        public bool IsAlive => Health != null && !Health.IsDead;
        public bool IsGrounded { get; private set; } = true;
        public bool IsSneaking { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsWading { get; private set; }
        public float PlanarSpeed { get; private set; }
        public Vector3 MoveInputWorld { get; private set; }

        /// <summary>0.15 (still) to ~1.3 (sprinting/fighting). Creatures hear further when this is high.</summary>
        public float NoiseLevel { get; private set; } = 0.2f;

        /// <summary>Combat sets this below 1 while attacking so swings feel weighty.</summary>
        public float MoveSpeedMultiplier { get; set; } = 1f;

        /// <summary>When true the character turns to face <see cref="FacingTarget"/> instead of its movement.</summary>
        public Vector3? FacingTarget { get; set; }

        public static PlayerController Create(Transform parent, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Player");
            go.layer = Layers.Characters;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.75f;
            cc.radius = 0.34f;
            cc.center = new Vector3(0f, 0.875f, 0f);
            cc.slopeLimit = 48f;
            cc.stepOffset = 0.4f;
            cc.skinWidth = 0.05f;

            var player = go.AddComponent<PlayerController>();
            player.Health = go.AddComponent<Health>();
            player.Health.Configure(Faction.Player, 100f, 10f, Rules.Element.Neutral);
            player.Avatar = go.AddComponent<PlayerAvatar>();
            player.Avatar.Build();
            player.Combat = go.AddComponent<PlayerCombat>();
            player.Binder = go.AddComponent<PlayerBinder>();
            player.Health.Damaged += player.OnDamaged;
            return player;
        }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
        }

        private void OnDestroy()
        {
            if (Health != null) Health.Damaged -= OnDamaged;
        }

        private void OnDamaged(DamageInfo info)
        {
            _knockback += new Vector3(info.Direction.x, 0f, info.Direction.z) * info.Knockback;
            Avatar?.PlayHurt();
            GameManager.Instance?.CameraRig?.Shake(0.25f, 0.18f);
            AudioManager.PlaySfx(SfxId.HitHeavy, transform.position + Vector3.up, 0.9f, 0.85f);
        }

        /// <summary>Overrides movement for a short time (dodge rolls).</summary>
        public void ForceMove(Vector3 velocity, float duration)
        {
            _forcedVelocity = new Vector3(velocity.x, 0f, velocity.z);
            _forcedUntil = Time.time + duration;
        }

        public bool IsForcedMoving => Time.time < _forcedUntil;

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _cc.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _cc.enabled = true;
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _knockback = Vector3.zero;
            _forcedUntil = 0f;
        }

        public void Respawn(Vector3 position, Quaternion rotation)
        {
            Teleport(position, rotation);
            Health.CanBeDamaged = true;
            Health.RestoreFull();
            GameInput.ClearSneakToggle();
            Binder?.CancelBinding();
            Combat?.ResetState();
            Avatar?.ResetPose();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _cc == null || !_cc.enabled) return;
            var gm = GameManager.Instance;
            var world = gm != null ? gm.World : null;

            bool canControl = IsAlive && GameInput.GameplayEnabled;
            Vector2 input = canControl ? GameInput.Move : Vector2.zero;

            // Camera-relative direction
            Transform cam = CameraTransform != null ? CameraTransform : (Camera.main != null ? Camera.main.transform : null);
            Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
            if (forward.sqrMagnitude < 0.01f) forward = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 moveDir = forward * input.y + right * input.x;
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
            MoveInputWorld = moveDir;

            IsWading = world != null && world.WaterDepth(transform.position) > 0.35f;
            IsSneaking = canControl && GameInput.SneakActive && !IsWading;
            IsRunning = canControl && GameInput.RunHeld && !IsSneaking && !IsWading && input.sqrMagnitude > 0.1f;

            float speed = IsSneaking ? sneakSpeed : (IsRunning ? runSpeed : walkSpeed);
            if (IsWading) speed *= wadeMultiplier;
            speed *= MoveSpeedMultiplier;

            // Grounding / jumping
            IsGrounded = _cc.isGrounded;
            if (IsGrounded)
            {
                _lastGroundedTime = Time.time;
                if (_verticalVelocity < 0f) _verticalVelocity = -2f;
                if (!_wasGrounded)
                {
                    Avatar?.PlayLand();
                    AudioManager.PlaySfx(SfxId.Land, transform.position, 0.5f);
                }
            }
            _wasGrounded = IsGrounded;

            if (canControl && GameInput.JumpPressed) _jumpPressedTime = Time.time;
            bool canJump = Time.time - _lastGroundedTime <= coyoteTime && !IsWading && MoveSpeedMultiplier > 0.5f;
            if (canJump && Time.time - _jumpPressedTime <= jumpBufferTime)
            {
                _verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
                _jumpPressedTime = -10f;
                _lastGroundedTime = -10f;
                Avatar?.PlayJump();
                AudioManager.PlaySfx(SfxId.Jump, transform.position, 0.5f);
            }
            // Short hop when releasing jump early
            float g = gravity;
            if (_verticalVelocity > 0f && !(canControl && GameInput.JumpHeld)) g *= 1.8f;
            _verticalVelocity += g * dt;
            _verticalVelocity = Mathf.Max(_verticalVelocity, -40f);

            // Horizontal velocity
            Vector3 targetVel = moveDir * speed;
            float control = IsGrounded ? 1f : airControl;
            float rate = (targetVel.sqrMagnitude > _planarVelocity.sqrMagnitude ? acceleration : deceleration) * control;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVel, rate * dt);

            Vector3 horizontal = _planarVelocity;
            if (Time.time < _forcedUntil) horizontal = _forcedVelocity;
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, 20f * dt);

            Vector3 motion = (horizontal + _knockback) * dt + Vector3.up * _verticalVelocity * dt;
            _cc.Move(motion);

            // Soft world boundary
            if (world != null && !world.IsInsidePlayArea(transform.position))
            {
                Vector3 clamped = world.ClampToPlayArea(transform.position);
                Vector3 push = clamped - transform.position;
                push.y = 0f;
                _cc.Move(push);
                if (Time.frameCount % 120 == 0) GameEvents.Notify("The mountains are too steep to cross — for now.");
            }
            // Fell out of the world (should never happen, but never soft-lock the player)
            if (transform.position.y < -30f && world != null)
            {
                Teleport(world.PlayerSpawn, world.PlayerSpawnRotation);
            }

            // Rotation
            Vector3 face = Vector3.zero;
            if (FacingTarget.HasValue) face = FacingTarget.Value - transform.position;
            else if (Time.time < _forcedUntil) face = _forcedVelocity;
            else if (moveDir.sqrMagnitude > 0.01f) face = moveDir;
            face.y = 0f;
            if (face.sqrMagnitude > 0.0001f)
            {
                var targetRot = Quaternion.LookRotation(face.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * dt);
            }

            // Stats used by AI, camera and animation
            Vector3 planar = _cc.velocity;
            planar.y = 0f;
            PlanarSpeed = planar.magnitude;
            float noise = PlanarSpeed < 0.2f ? 0.15f : (IsSneaking ? 0.35f : (IsRunning ? 1.15f : 0.7f));
            if (!IsGrounded) noise = Mathf.Max(noise, 0.8f);
            if (Combat != null) noise = Mathf.Max(noise, Combat.NoiseBoost);
            NoiseLevel = noise;

            // Footsteps
            if (IsGrounded && PlanarSpeed > 0.5f)
            {
                _footstepDistance += PlanarSpeed * dt;
                float stride = IsRunning ? 2.1f : 1.5f;
                if (_footstepDistance > stride)
                {
                    _footstepDistance = 0f;
                    float vol = IsSneaking ? 0.12f : (IsRunning ? 0.45f : 0.3f);
                    AudioManager.PlaySfx(IsWading ? SfxId.Splash : SfxId.Footstep, transform.position, vol, Random.Range(0.9f, 1.1f));
                }
            }

            Avatar?.Tick(PlanarSpeed, runSpeed, IsGrounded, IsSneaking, IsWading, _verticalVelocity);
        }
    }
}
