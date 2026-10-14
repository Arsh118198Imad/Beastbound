using System;
using System.Collections.Generic;
using Beastbound.Audio;
using Beastbound.Combat;
using Beastbound.Core;
using Beastbound.Creatures;
using Beastbound.Rules;
using UnityEngine;

namespace Beastbound.CreatureAI
{
    public enum CreatureState
    {
        Idle,
        Wander,
        Sleep,
        Curious,
        Flee,
        Combat,
        Return,
        Exhausted,
        Binding,
        Follow,
        Assist
    }

    /// <summary>What the HUD should draw above a creature.</summary>
    public enum CreatureIndicator
    {
        None,
        Suspicious,  // "?"
        Noticed,     // "!"
        Hostile,     // red "!"
        Sleeping,    // "Zz"
        Exhausted,   // "~"
        Friendly     // companion heart
    }

    /// <summary>
    /// Decision-making state machine for wild creatures and companions. Personality decides how a wild
    /// creature reacts once it notices the player:
    ///   Bold / Stubborn  -> fights,   Timid -> flees,   Curious / Playful / Sleepy -> approaches to look.
    /// Any creature that is attacked fights back (Timid ones flee again when badly hurt).
    /// Creatures are never killed: at 0 HP they become Exhausted and can be bound with a 100% chance.
    /// </summary>
    public class CreatureBrain : MonoBehaviour
    {
        private const float ExhaustedDurationWild = 25f;
        private const float ExhaustedDurationCompanion = 8f;
        private const float LeashFromTarget = 26f;
        private const float LeashFromHome = 45f;

        private Creature _c;
        private float _stateTime;
        private float _timer;
        private Vector3 _target;
        private float _disengagedAt = -100f;
        private float _nextChirp;

        // Combat
        private Health _combatTarget;
        private AttackDefinition _pendingAttack;
        private float _windupEnd;
        private float _recoverUntil;
        private bool _charging;
        private float _chargeEnd;
        private Vector3 _chargeDir;
        private readonly HashSet<Health> _chargeHits = new HashSet<Health>();
        private readonly Dictionary<AttackDefinition, float> _cooldownUntil = new Dictionary<AttackDefinition, float>();
        private readonly List<Health> _scratch = new List<Health>();
        private float _strafeSign = 1f;
        private float _nextStrafeFlip;

        // Binding
        private CreatureState _stateBeforeBinding;

        public CreatureState State { get; private set; } = CreatureState.Idle;
        public Health CombatTarget => _combatTarget;
        public bool IsWindingUp => _pendingAttack != null;

        public event Action<CreatureState, CreatureState> StateChanged;

        public void Initialize(Creature creature)
        {
            _c = creature;
            _c.Health.Damaged += OnDamaged;
            _c.Health.Died += OnExhausted;
            _strafeSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            _nextChirp = Time.time + UnityEngine.Random.Range(4f, 12f);

            if (_c.Role == CreatureRole.Companion) SetState(CreatureState.Follow);
            else if (_c.Record.personality == Personality.Sleepy) EnterSleep(UnityEngine.Random.Range(30f, 60f));
            else EnterIdle();
        }

        private void OnDestroy()
        {
            if (_c != null && _c.Health != null)
            {
                _c.Health.Damaged -= OnDamaged;
                _c.Health.Died -= OnExhausted;
            }
            NotifyBattle(false);
        }

        // ------------------------------------------------------------------ State plumbing

        private void SetState(CreatureState next)
        {
            if (State == next && _stateTime > 0f) return;
            var previous = State;
            bool wasCombat = previous == CreatureState.Combat;
            State = next;
            _stateTime = 0f;
            _pendingAttack = null;
            _charging = false;
            if (_c.Motor != null)
            {
                _c.Motor.Frozen = next == CreatureState.Binding || next == CreatureState.Exhausted;
                _c.Motor.Stop();
                _c.Motor.ClearStuck();
            }
            if (wasCombat && next != CreatureState.Combat) NotifyBattle(false);
            if (next == CreatureState.Combat && _c.IsWild) NotifyBattle(true);
            StateChanged?.Invoke(previous, next);
        }

        private void NotifyBattle(bool engaged)
        {
            var battle = GameManager.Instance != null ? GameManager.Instance.Battle : null;
            if (battle == null || _c == null || !_c.IsWild) return;
            if (engaged) battle.Engage(_c);
            else battle.Disengage(_c);
        }

        public CreatureIndicator Indicator
        {
            get
            {
                if (_c == null) return CreatureIndicator.None;
                if (_c.Role == CreatureRole.Companion) return CreatureIndicator.Friendly;
                switch (State)
                {
                    case CreatureState.Sleep: return CreatureIndicator.Sleeping;
                    case CreatureState.Exhausted: return CreatureIndicator.Exhausted;
                    case CreatureState.Combat: return CreatureIndicator.Hostile;
                    case CreatureState.Curious:
                    case CreatureState.Flee: return CreatureIndicator.Noticed;
                    case CreatureState.Binding: return CreatureIndicator.None;
                }
                return _c.Perception != null && _c.Perception.Suspicion > 0.15f ? CreatureIndicator.Suspicious : CreatureIndicator.None;
            }
        }

        // ------------------------------------------------------------------ Update

        private void Update()
        {
            if (_c == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _stateTime += dt;

            switch (State)
            {
                case CreatureState.Idle: TickIdle(); break;
                case CreatureState.Wander: TickWander(); break;
                case CreatureState.Sleep: TickSleep(); break;
                case CreatureState.Curious: TickCurious(); break;
                case CreatureState.Flee: TickFlee(); break;
                case CreatureState.Combat: TickCombat(); break;
                case CreatureState.Return: TickReturn(dt); break;
                case CreatureState.Exhausted: TickExhausted(); break;
                case CreatureState.Binding: break; // held still until EndBinding
                case CreatureState.Follow: TickFollow(); break;
                case CreatureState.Assist: TickCombat(); break;
            }

            UpdatePose();
            MaybeChirp();
        }

        private void UpdatePose()
        {
            if (_c.Animator == null) return;
            CreaturePose pose = CreaturePose.Normal;
            switch (State)
            {
                case CreatureState.Sleep: pose = CreaturePose.Sleeping; break;
                case CreatureState.Exhausted: pose = CreaturePose.Exhausted; break;
                case CreatureState.Binding: pose = CreaturePose.Binding; break;
                case CreatureState.Curious:
                case CreatureState.Flee: pose = CreaturePose.Alert; break;
                case CreatureState.Combat:
                case CreatureState.Assist: pose = _pendingAttack != null ? CreaturePose.Windup : CreaturePose.Alert; break;
            }
            _c.Animator.Pose = pose;
        }

        private void MaybeChirp()
        {
            if (Time.time < _nextChirp) return;
            _nextChirp = Time.time + UnityEngine.Random.Range(6f, 16f);
            if (State == CreatureState.Sleep || State == CreatureState.Exhausted || State == CreatureState.Binding) return;
            AudioManager.PlaySfx(SfxId.CreatureChirp, _c.Center, 0.6f, UnityEngine.Random.Range(0.9f, 1.15f));
        }

        // ------------------------------------------------------------------ Wild behaviours

        private bool ShouldReact()
        {
            if (!_c.IsWild || _c.Perception == null) return false;
            if (Time.time - _disengagedAt < 5f) return false; // brief cooldown after losing interest
            return _c.Perception.IsAware;
        }

        private void ReactToPlayer()
        {
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null) return;
            AudioManager.PlaySfx(SfxId.CreatureAlert, _c.Center, 0.8f);
            if (_c.Animator != null) _c.Animator.PlayHappy(); // startled hop
            Core.GameEvents.RaiseWildCreatureSpotted(_c.Species.speciesId);

            switch (_c.Record.personality)
            {
                case Personality.Bold:
                case Personality.Stubborn:
                    EnterCombat(player.Health);
                    break;
                case Personality.Timid:
                    SetState(CreatureState.Flee);
                    break;
                default:
                    SetState(CreatureState.Curious);
                    break;
            }
        }

        private void EnterIdle()
        {
            SetState(CreatureState.Idle);
            _timer = UnityEngine.Random.Range(2f, 5f);
        }

        private void EnterSleep(float duration)
        {
            SetState(CreatureState.Sleep);
            _timer = duration;
            _c.Perception?.Calm();
        }

        private void TickIdle()
        {
            _c.Motor.Stop();
            if (ShouldReact()) { ReactToPlayer(); return; }
            if (_stateTime < _timer) return;

            var dayNight = GameManager.Instance != null && GameManager.Instance.World != null ? GameManager.Instance.World.DayNight : null;
            bool night = dayNight != null && dayNight.IsNight;
            float sleepChance = _c.Record.personality == Personality.Sleepy ? 0.55f : 0.08f;
            if (night) sleepChance = 0.9f;
            if (UnityEngine.Random.value < sleepChance)
            {
                if ((transform.position - _c.HomePosition).sqrMagnitude > 9f && night)
                {
                    SetState(CreatureState.Return);
                    return;
                }
                EnterSleep(night ? 45f : UnityEngine.Random.Range(15f, 35f));
                return;
            }
            PickWanderTarget();
        }

        private void PickWanderTarget()
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            Vector2 r = UnityEngine.Random.insideUnitCircle * _c.HomeRadius;
            var desired = _c.HomePosition + new Vector3(r.x, 0f, r.y);
            if (world != null && !world.TryFindWalkableNear(desired, 4f, out desired, 6))
            {
                EnterIdle();
                return;
            }
            _target = desired;
            SetState(CreatureState.Wander);
        }

        private void TickWander()
        {
            if (ShouldReact()) { ReactToPlayer(); return; }
            bool arrived = _c.Motor.MoveTowards(_target, _c.Species.walkSpeed, 0.6f);
            if (arrived || _c.Motor.IsStuck || _stateTime > 20f) EnterIdle();
        }

        private void TickSleep()
        {
            _c.Motor.Stop();
            if (_c.Perception != null && _c.Perception.IsAware && Time.time - _disengagedAt > 5f)
            {
                ReactToPlayer();
                return;
            }
            var dayNight = GameManager.Instance != null && GameManager.Instance.World != null ? GameManager.Instance.World.DayNight : null;
            bool night = dayNight != null && dayNight.IsNight;
            if (_stateTime > _timer && !night) EnterIdle();
        }

        private void TickCurious()
        {
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null || !player.IsAlive) { EnterIdle(); return; }
            Vector3 to = player.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;

            if (dist > 24f || _stateTime > 22f)
            {
                LoseInterest();
                return;
            }

            if (_c.Record.personality == Personality.Playful && dist < 7f)
            {
                // Circle around the player
                Vector3 tangent = Vector3.Cross(Vector3.up, to.normalized) * _strafeSign;
                Vector3 radial = (dist - 4f) * to.normalized;
                _c.Motor.MoveDirection(tangent * 3f + radial, _c.Species.walkSpeed * 1.8f);
                if (Time.time > _nextStrafeFlip)
                {
                    _nextStrafeFlip = Time.time + UnityEngine.Random.Range(3f, 6f);
                    _strafeSign = -_strafeSign;
                    _c.Animator?.PlayHappy();
                }
            }
            else if (dist > 3.6f)
            {
                _c.Motor.MoveTowards(player.transform.position, _c.Species.walkSpeed * 1.3f, 3.4f);
            }
            else
            {
                _c.Motor.Stop();
                _c.Motor.FaceTowards(player.transform.position);
            }
        }

        private void TickFlee()
        {
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null) { EnterIdle(); return; }
            Vector3 away = transform.position - player.transform.position;
            away.y = 0f;
            float dist = away.magnitude;
            if (dist > 24f || _stateTime > 14f)
            {
                LoseInterest();
                return;
            }

            if (_stateTime < 0.05f || _c.Motor.IsStuck || (transform.position - _target).sqrMagnitude < 2f)
            {
                _c.Motor.ClearStuck();
                _target = PickFleePoint(away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward);
            }
            _c.Motor.MoveTowards(_target, _c.Species.runSpeed, 0.5f);
        }

        private Vector3 PickFleePoint(Vector3 awayDir)
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            float[] angles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f };
            foreach (float a in angles)
            {
                Vector3 dir = Quaternion.Euler(0f, a + UnityEngine.Random.Range(-10f, 10f), 0f) * awayDir;
                Vector3 p = transform.position + dir * 9f;
                if (world == null || world.IsWalkable(p)) return p;
            }
            return transform.position - awayDir * 6f; // cornered: dash past
        }

        private void LoseInterest()
        {
            _disengagedAt = Time.time;
            _c.Perception?.Calm(0.3f);
            SetState(CreatureState.Return);
        }

        private void TickReturn(float dt)
        {
            _c.Health.Heal(_c.Health.Max * 0.08f * dt);
            if (Time.time - _disengagedAt > 5f && ShouldReact()) { ReactToPlayer(); return; }
            bool arrived = _c.Motor.MoveTowards(_c.HomePosition, _c.Species.walkSpeed * 1.4f, 1.2f);
            if (arrived || _stateTime > 30f)
            {
                if (_stateTime > 30f) _c.Motor.Teleport(_c.HomePosition + Vector3.up * 0.2f);
                EnterIdle();
            }
            else if (_c.Motor.IsStuck)
            {
                _c.Motor.ClearStuck();
                _c.Motor.AddKnockback(Quaternion.Euler(0f, 90f, 0f) * transform.forward * 3f); // sidestep obstacles
            }
        }

        // ------------------------------------------------------------------ Combat

        /// <summary>Starts (or retargets) a fight. Used by the AI itself and when the creature is attacked.</summary>
        public void EnterCombat(Health target)
        {
            if (target == null || target.IsDead) return;
            if (State == CreatureState.Exhausted || State == CreatureState.Binding) return;
            _combatTarget = target;
            if (_c.Role == CreatureRole.Companion)
            {
                if (State != CreatureState.Assist) SetState(CreatureState.Assist);
                return;
            }
            _c.Perception?.ForceAware();
            if (State != CreatureState.Combat)
            {
                // Short grace period before the first attack so fights never start with an unavoidable hit.
                _recoverUntil = Time.time + 0.8f;
                SetState(CreatureState.Combat);
            }
        }

        private void TickCombat()
        {
            bool companion = _c.Role == CreatureRole.Companion;
            var gm = GameManager.Instance;

            if (companion)
            {
                var enemy = gm != null && gm.Battle != null ? gm.Battle.PrimaryEnemy : null;
                _combatTarget = enemy != null && enemy.Health != null && !enemy.Health.IsDead ? enemy.Health : null;
                var player = gm != null ? gm.Player : null;
                if (_combatTarget == null || player == null ||
                    (player.transform.position - transform.position).sqrMagnitude > 22f * 22f)
                {
                    SetState(CreatureState.Follow);
                    return;
                }
            }
            else
            {
                if (_combatTarget == null || _combatTarget.IsDead)
                {
                    LoseInterest();
                    return;
                }
                float fromTarget = Vector3.Distance(transform.position, _combatTarget.transform.position);
                float fromHome = Vector3.Distance(transform.position, _c.HomePosition);
                if (fromTarget > LeashFromTarget || fromHome > LeashFromHome)
                {
                    GameEvents.Notify($"The {_c.DisplayName} lost interest.", NotificationKind.Info);
                    LoseInterest();
                    return;
                }
                if (_c.Record.personality == Personality.Timid && _c.Health.Fraction < 0.4f && !_charging && _pendingAttack == null)
                {
                    SetState(CreatureState.Flee);
                    return;
                }
            }

            Vector3 targetPos = _combatTarget.transform.position;
            Vector3 to = targetPos - transform.position;
            to.y = 0f;
            float dist = to.magnitude;

            if (_charging)
            {
                TickCharge();
                return;
            }

            if (_pendingAttack != null)
            {
                _c.Motor.Stop();
                _c.Motor.FaceTowards(targetPos);
                if (Time.time >= _windupEnd) ExecuteAttack(_pendingAttack, targetPos);
                return;
            }

            if (Time.time < _recoverUntil)
            {
                _c.Motor.FaceTowards(targetPos);
                Strafe(to, dist, 0.6f);
                return;
            }

            var attack = ChooseAttack(dist);
            if (attack != null)
            {
                _pendingAttack = attack;
                _windupEnd = Time.time + attack.windup;
                _c.Animator?.Glow(0.6f);
                AudioManager.PlaySfx(SfxId.CreatureWindup, _c.Center, 0.8f);
                return;
            }

            // Positioning: keep a mid-range distance and circle.
            if (dist > 8f) _c.Motor.MoveTowards(targetPos, _c.Species.runSpeed * 0.8f, 6f);
            else if (dist < 2.2f) _c.Motor.MoveDirection(-to, _c.Species.walkSpeed * 1.4f);
            else Strafe(to, dist, 1f);
            _c.Motor.FaceTowards(targetPos);
        }

        private void Strafe(Vector3 to, float dist, float speedScale)
        {
            if (Time.time > _nextStrafeFlip)
            {
                _nextStrafeFlip = Time.time + UnityEngine.Random.Range(1.5f, 3.5f);
                _strafeSign = -_strafeSign;
            }
            if (dist < 0.01f) return;
            Vector3 tangent = Vector3.Cross(Vector3.up, to / dist) * _strafeSign;
            Vector3 radial = (dist - 5f) * 0.3f * (to / dist);
            _c.Motor.MoveDirection(tangent + radial, _c.Species.walkSpeed * 1.2f * speedScale);
        }

        private AttackDefinition ChooseAttack(float dist)
        {
            var attacks = _c.Species.attacks;
            if (attacks == null) return null;
            AttackDefinition best = null;
            foreach (var a in attacks)
            {
                if (a == null) continue;
                if (_cooldownUntil.TryGetValue(a, out float until) && Time.time < until) continue;
                bool inRange;
                switch (a.kind)
                {
                    case AttackKind.Melee: inRange = dist <= a.range; break;
                    case AttackKind.Charge: inRange = dist >= 3f && dist <= a.range; break;
                    default: inRange = dist >= 3.5f && dist <= a.range; break;
                }
                if (!inRange) continue;
                if (best == null || a.power > best.power) best = a;
            }
            return best;
        }

        private void ExecuteAttack(AttackDefinition attack, Vector3 targetPos)
        {
            _pendingAttack = null;
            _cooldownUntil[attack] = Time.time + attack.cooldown * UnityEngine.Random.Range(0.9f, 1.2f);
            _recoverUntil = Time.time + 0.6f;
            var faction = _c.Health.Faction;

            switch (attack.kind)
            {
                case AttackKind.Projectile:
                {
                    Vector3 origin = _c.Center + transform.forward * 0.5f + Vector3.up * 0.3f;
                    Vector3 aim = targetPos + Vector3.up * 0.9f;
                    float speed = Mathf.Max(5f, attack.projectileSpeed);
                    float flightTime = Vector3.Distance(origin, aim) / speed;
                    const float g = -9f;
                    // Ballistic velocity that lands on the aim point after flightTime.
                    Vector3 velocity = (aim - origin) / flightTime - 0.5f * g * flightTime * Vector3.up;
                    var mat = MaterialFactory.Lit("PebbleProjectile", new Color(0.6f, 0.62f, 0.5f), 0.3f);
                    Projectile.Spawn("Pebble", origin, velocity, faction, gameObject, attack.power, _c.Stats.attack,
                        attack.element, attack.hitRadius, attack.knockback, mat, 0.28f, g, 4f);
                    _c.Animator?.PlayLunge();
                    AudioManager.PlaySfx(SfxId.PebbleThrow, origin, 0.8f);
                    break;
                }
                case AttackKind.Charge:
                {
                    Vector3 dir = targetPos - transform.position;
                    dir.y = 0f;
                    _chargeDir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
                    float distance = Mathf.Min(attack.range + 2f, dir.magnitude + 2.5f);
                    _chargeEnd = Time.time + distance / Mathf.Max(1f, attack.projectileSpeed);
                    _charging = true;
                    _chargeHits.Clear();
                    _currentCharge = attack;
                    AudioManager.PlaySfx(SfxId.Dodge, _c.Center, 0.8f, 0.7f);
                    break;
                }
                default:
                {
                    _c.Animator?.PlayLunge();
                    Vector3 center = _c.Center + transform.forward * 0.9f;
                    CombatQuery.FindHostiles(center, attack.hitRadius, faction, gameObject, _scratch, transform.forward, 70f);
                    foreach (var h in _scratch)
                    {
                        h.TakeHit(attack.power, _c.Stats.attack, attack.element, faction, gameObject, center,
                            h.transform.position - transform.position, attack.knockback);
                    }
                    AudioManager.PlaySfx(SfxId.Swing, center, 0.7f, 1.3f);
                    break;
                }
            }
        }

        private AttackDefinition _currentCharge;

        private void TickCharge()
        {
            var attack = _currentCharge;
            if (attack == null || Time.time >= _chargeEnd || _c.Motor.IsStuck)
            {
                _charging = false;
                _c.Motor.ClearStuck();
                _recoverUntil = Time.time + 0.9f; // vulnerable after a charge: reward for dodging
                return;
            }
            _c.Motor.MoveDirection(_chargeDir, attack.projectileSpeed);
            _c.Motor.FaceTowards(transform.position + _chargeDir);
            CombatQuery.FindHostiles(_c.Center + _chargeDir * 0.4f, attack.hitRadius, _c.Health.Faction, gameObject, _scratch);
            foreach (var h in _scratch)
            {
                if (!_chargeHits.Add(h)) continue;
                h.TakeHit(attack.power, _c.Stats.attack, attack.element, _c.Health.Faction, gameObject,
                    h.transform.position, _chargeDir, attack.knockback);
            }
        }

        private void OnDamaged(DamageInfo info)
        {
            _c.Animator?.PlayHurt();
            _c.Motor?.AddKnockback(info.Direction * info.Knockback);
            AudioManager.PlaySfx(SfxId.CreatureHurt, _c.Center, 0.8f, UnityEngine.Random.Range(0.95f, 1.1f));
            if (!_c.IsWild || _c.Health.IsDead) return;

            Health attacker = info.Source != null ? info.Source.GetComponent<Health>() : null;
            if (State == CreatureState.Combat)
            {
                // Retarget the companion if it is the one landing hits.
                if (attacker != null && attacker != _combatTarget && UnityEngine.Random.value < 0.35f) _combatTarget = attacker;
                return;
            }
            if (State == CreatureState.Binding || State == CreatureState.Exhausted) return;

            var playerHealth = GameManager.Instance != null && GameManager.Instance.Player != null ? GameManager.Instance.Player.Health : null;
            if (_c.Record.personality == Personality.Timid && _c.Health.Fraction < 0.4f)
            {
                SetState(CreatureState.Flee);
                return;
            }
            EnterCombat(attacker != null ? attacker : playerHealth);
        }

        private void OnExhausted()
        {
            if (State == CreatureState.Binding) return;
            SetState(CreatureState.Exhausted);
            _timer = _c.IsWild ? ExhaustedDurationWild : ExhaustedDurationCompanion;
            AudioManager.PlaySfx(SfxId.CreatureHurt, _c.Center, 1f, 0.7f);
            if (_c.IsWild)
            {
                GameEvents.Notify($"The {_c.DisplayName} is exhausted! Bind it now — 100% chance.", NotificationKind.Success);
            }
        }

        private void TickExhausted()
        {
            if (_stateTime < _timer) return;
            _c.Health.SetFraction(_c.IsWild ? 0.35f : 0.5f);
            if (_c.IsWild)
            {
                GameEvents.Notify($"The {_c.DisplayName} recovered and ran off.", NotificationKind.Info);
                LoseInterest();
            }
            else
            {
                SetState(CreatureState.Follow);
            }
        }

        // ------------------------------------------------------------------ Binding

        public void BeginBinding()
        {
            if (State == CreatureState.Binding) return;
            _stateBeforeBinding = State;
            _c.Health.CanBeDamaged = false;
            SetState(CreatureState.Binding);
            _c.Animator?.Glow(1f);
        }

        /// <summary>Called when a bind attempt fails: the creature breaks free and reacts.</summary>
        public void BreakFree()
        {
            if (State != CreatureState.Binding) return;
            _c.Health.CanBeDamaged = true;
            _c.Animator?.PlayHappy();
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;

            if (_stateBeforeBinding == CreatureState.Exhausted)
            {
                // Cannot normally happen (100% chance) but stay safe.
                SetState(CreatureState.Exhausted);
                _timer = 5f;
                return;
            }
            if (_c.Record.personality == Personality.Timid) SetState(CreatureState.Flee);
            else if (player != null) EnterCombat(player.Health);
            else EnterIdle();
        }

        // ------------------------------------------------------------------ Companion

        private void TickFollow()
        {
            var gm = GameManager.Instance;
            var player = gm != null ? gm.Player : null;
            if (player == null) return;

            if (gm.Battle != null && gm.Battle.PrimaryEnemy != null && gm.Battle.PrimaryEnemy.Health != null
                && !gm.Battle.PrimaryEnemy.Health.IsDead
                && (gm.Battle.PrimaryEnemy.transform.position - player.transform.position).sqrMagnitude < 18f * 18f)
            {
                EnterCombat(gm.Battle.PrimaryEnemy.Health);
                return;
            }

            Transform p = player.transform;
            Vector3 slot = p.position - p.forward * 2.2f + p.right * 1.3f;
            float dist = Vector3.Distance(transform.position, slot);
            if (dist > 25f)
            {
                var world = gm.World;
                Vector3 drop = slot;
                if (world != null) world.TryFindWalkableNear(slot, 3f, out drop, 8);
                _c.Motor.Teleport(drop + Vector3.up * 0.3f);
                return;
            }
            if (dist > 1.2f)
            {
                float speed = dist > 6f ? _c.Species.runSpeed : Mathf.Lerp(_c.Species.walkSpeed, _c.Species.runSpeed, (dist - 1.2f) / 5f);
                _c.Motor.MoveTowards(slot, speed, 0.8f);
            }
            else
            {
                _c.Motor.Stop();
                if (_stateTime > 4f && UnityEngine.Random.value < 0.004f) _c.Animator?.PlayHappy();
            }
        }

        /// <summary>Puts a wild creature back to a calm state (used after the player is defeated).</summary>
        public void ResetToCalm()
        {
            _combatTarget = null;
            _c.Health.CanBeDamaged = true;
            _c.Health.RestoreFull();
            _c.Perception?.Calm();
            _disengagedAt = Time.time;
            if (_c.Role == CreatureRole.Companion) SetState(CreatureState.Follow);
            else
            {
                _c.Motor.Teleport(_c.HomePosition + Vector3.up * 0.2f);
                EnterIdle();
            }
        }
    }
}
