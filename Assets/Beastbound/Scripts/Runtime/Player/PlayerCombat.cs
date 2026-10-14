using System.Collections.Generic;
using Beastbound.Audio;
using Beastbound.Combat;
using Beastbound.Controls;
using Beastbound.Core;
using Beastbound.Rules;
using UnityEngine;

namespace Beastbound.Player
{
    /// <summary>
    /// The player's real-time combat kit, wielding a Warden's staff:
    ///   Staff Strike (3-hit combo), Ember Spark (ranged, Ember beats Verdant), Dodge (short dash with i-frames).
    /// Attacks softly auto-aim at the nearest hostile so fights feel good on mouse and gamepad alike.
    /// </summary>
    public class PlayerCombat : MonoBehaviour
    {
        [Header("Stats")]
        public float attackStat = 12f;

        [Header("Staff Strike")]
        public float[] comboPower = { 10f, 10f, 16f };
        public float strikeDuration = 0.42f;
        public float strikeHitTime = 0.16f;
        public float strikeRange = 2.5f;
        public float comboResetTime = 0.75f;

        [Header("Ember Spark")]
        public float sparkPower = 20f;
        public float sparkCooldown = 4.5f;
        public float sparkSpeed = 22f;

        [Header("Dodge")]
        public float dodgeSpeed = 12.5f;
        public float dodgeDuration = 0.3f;
        public float dodgeCooldown = 0.7f;

        [Header("Recovery")]
        public float regenDelay = 4f;
        public float regenPerSecond = 12f;

        private PlayerController _player;
        private readonly List<Health> _scratch = new List<Health>();
        private int _comboIndex;
        private float _attackStart = -10f;
        private bool _hitApplied = true;
        private bool _queuedAttack;
        private float _lastAttackEnd = -10f;
        private float _sparkReadyAt;
        private float _dodgeReadyAt;
        private float _lastDamagedTime = -10f;
        private float _noiseUntil;
        private Health _aimTarget;

        public bool IsAttacking => Time.time - _attackStart < strikeDuration;
        public float SparkCooldown01 => sparkCooldown <= 0f ? 1f : Mathf.Clamp01(1f - (_sparkReadyAt - Time.time) / sparkCooldown);
        public float DodgeCooldown01 => dodgeCooldown <= 0f ? 1f : Mathf.Clamp01(1f - (_dodgeReadyAt - Time.time) / dodgeCooldown);
        public float NoiseBoost => Time.time < _noiseUntil ? 1.3f : 0f;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
        }

        private void Start()
        {
            if (_player != null && _player.Health != null) _player.Health.Damaged += _ => _lastDamagedTime = Time.time;
        }

        public void ResetState()
        {
            _comboIndex = 0;
            _attackStart = -10f;
            _hitApplied = true;
            _queuedAttack = false;
            _sparkReadyAt = 0f;
            _dodgeReadyAt = 0f;
            if (_player != null)
            {
                _player.MoveSpeedMultiplier = 1f;
                _player.FacingTarget = null;
            }
        }

        private void Update()
        {
            if (_player == null || !_player.IsAlive || Time.deltaTime <= 0f) return;

            HandleRegen();
            if (!GameInput.GameplayEnabled) return;

            bool binding = _player.Binder != null && _player.Binder.IsBusy;

            if (GameInput.DodgePressed && Time.time >= _dodgeReadyAt && !binding) Dodge();
            if (GameInput.AttackPressed && !binding)
            {
                if (IsAttacking) _queuedAttack = true;
                else StartStrike();
            }
            if (GameInput.SparkPressed && Time.time >= _sparkReadyAt && !binding) CastSpark();

            UpdateStrike();
        }

        private void HandleRegen()
        {
            var battle = GameManager.Instance != null ? GameManager.Instance.Battle : null;
            bool inBattle = battle != null && battle.InBattle;
            if (!inBattle && Time.time - _lastDamagedTime > regenDelay)
            {
                _player.Health.Heal(regenPerSecond * Time.deltaTime);
            }
        }

        // ------------------------------------------------------------------ Staff Strike

        private void StartStrike()
        {
            if (Time.time - _lastAttackEnd > comboResetTime) _comboIndex = 0;
            _attackStart = Time.time;
            _hitApplied = false;
            _queuedAttack = false;
            _noiseUntil = Time.time + 0.6f;

            _aimTarget = FindAimTarget(6f);
            _player.FacingTarget = _aimTarget != null ? _aimTarget.transform.position : (Vector3?)null;
            if (_aimTarget == null && _player.MoveInputWorld.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(_player.MoveInputWorld, Vector3.up);
            }
            // Small lunge toward the target for impact.
            Vector3 lunge = _aimTarget != null
                ? Vector3.ProjectOnPlane(_aimTarget.transform.position - transform.position, Vector3.up)
                : transform.forward;
            float lungeDist = _aimTarget != null ? Mathf.Clamp(lunge.magnitude - 1.6f, 0f, 2f) : 0.6f;
            if (lunge.sqrMagnitude > 0.01f) _player.ForceMove(lunge.normalized * (lungeDist / 0.12f), 0.12f);

            _player.MoveSpeedMultiplier = 0.3f;
            _player.Avatar?.PlayStrike(_comboIndex);
            AudioManager.PlaySfx(SfxId.Swing, transform.position + Vector3.up, 0.7f, 0.9f + _comboIndex * 0.12f);
        }

        private void UpdateStrike()
        {
            float t = Time.time - _attackStart;
            if (!_hitApplied && t >= strikeHitTime)
            {
                _hitApplied = true;
                ApplyStrikeHit();
            }
            if (_attackStart > 0f && t >= strikeDuration && _player.MoveSpeedMultiplier < 1f)
            {
                _lastAttackEnd = Time.time;
                _player.MoveSpeedMultiplier = 1f;
                _player.FacingTarget = null;
                _comboIndex = (_comboIndex + 1) % comboPower.Length;
                if (_queuedAttack) StartStrike();
            }
        }

        private void ApplyStrikeHit()
        {
            Vector3 center = transform.position + Vector3.up * 0.9f + transform.forward * 1.1f;
            CombatQuery.FindHostiles(center, strikeRange * 0.6f, Faction.Player, gameObject, _scratch, transform.forward, 80f);
            float power = comboPower[Mathf.Clamp(_comboIndex, 0, comboPower.Length - 1)];
            bool finisher = _comboIndex == comboPower.Length - 1;
            bool hitAny = false;
            foreach (var h in _scratch)
            {
                float dealt = h.TakeHit(power, attackStat, Element.Neutral, Faction.Player, gameObject, h.transform.position + Vector3.up * 0.5f,
                    h.transform.position - transform.position, finisher ? 6f : 2.5f);
                if (dealt > 0f) hitAny = true;
            }
            if (hitAny)
            {
                AudioManager.PlaySfx(finisher ? SfxId.HitHeavy : SfxId.Hit, center, 0.9f);
                GameManager.Instance?.HitStop(finisher ? 0.08f : 0.045f);
                GameManager.Instance?.CameraRig?.Shake(finisher ? 0.2f : 0.1f, 0.12f);
            }
        }

        // ------------------------------------------------------------------ Ember Spark

        private void CastSpark()
        {
            _sparkReadyAt = Time.time + sparkCooldown;
            _noiseUntil = Time.time + 0.8f;
            var target = FindAimTarget(18f);
            Vector3 origin = transform.position + Vector3.up * 1.3f + transform.forward * 0.6f;
            Vector3 dir;
            if (target != null)
            {
                dir = (target.transform.position + Vector3.up * 0.5f - origin).normalized;
                transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(dir, Vector3.up).normalized, Vector3.up);
            }
            else
            {
                var cam = _player.CameraTransform != null ? _player.CameraTransform : (Camera.main != null ? Camera.main.transform : transform);
                dir = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }

            var mat = MaterialFactory.Emissive("EmberSpark", new Color(1f, 0.6f, 0.25f), new Color(3f, 1.4f, 0.35f), 0.5f);
            var p = Projectile.Spawn("EmberSpark", origin, dir * sparkSpeed, Faction.Player, gameObject, sparkPower, attackStat,
                Element.Ember, 0.6f, 4f, mat, 0.35f, 0f, 1.6f);
            if (target != null) p.SetHoming(target.transform, 3f);
            p.Impact += pos => AudioManager.PlaySfx(SfxId.SparkImpact, pos, 0.8f);
            _player.Avatar?.PlayCast();
            AudioManager.PlaySfx(SfxId.Spark, origin, 0.8f);
        }

        // ------------------------------------------------------------------ Dodge

        private void Dodge()
        {
            _dodgeReadyAt = Time.time + dodgeCooldown;
            Vector3 dir = _player.MoveInputWorld.sqrMagnitude > 0.01f ? _player.MoveInputWorld.normalized : -transform.forward;
            _player.ForceMove(dir * dodgeSpeed, dodgeDuration);
            _player.Health.SetInvulnerable(dodgeDuration + 0.05f);
            _player.Avatar?.PlayDodge();
            // Cancel an in-progress swing: dodging always takes priority.
            _attackStart = -10f;
            _hitApplied = true;
            _queuedAttack = false;
            _player.MoveSpeedMultiplier = 1f;
            _player.FacingTarget = null;
            AudioManager.PlaySfx(SfxId.Dodge, transform.position, 0.6f);
        }

        // ------------------------------------------------------------------ Aim assist

        private Health FindAimTarget(float radius)
        {
            CombatQuery.FindHostiles(transform.position, radius, Faction.Player, gameObject, _scratch);
            var cam = _player.CameraTransform != null ? _player.CameraTransform : (Camera.main != null ? Camera.main.transform : null);
            Vector3 preferred = _player.MoveInputWorld.sqrMagnitude > 0.01f
                ? _player.MoveInputWorld
                : (cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up) : transform.forward);
            Health best = null;
            float bestScore = float.MaxValue;
            foreach (var h in _scratch)
            {
                Vector3 to = h.transform.position - transform.position;
                to.y = 0f;
                float angle = Vector3.Angle(preferred, to);
                if (angle > 100f) continue;
                float score = to.magnitude + angle * 0.05f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = h;
                }
            }
            return best;
        }
    }
}
