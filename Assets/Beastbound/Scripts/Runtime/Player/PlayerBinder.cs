using System.Collections;
using Beastbound.Audio;
using Beastbound.Combat;
using Beastbound.Controls;
using Beastbound.Core;
using Beastbound.CreatureAI;
using Beastbound.Creatures;
using Beastbound.Rules;
using UnityEngine;

namespace Beastbound.Player
{
    /// <summary>
    /// Finds the wild creature the player is looking at and handles "binding" (collecting) it.
    ///
    /// Fairness rules (deliberately the opposite of common complaints about capture systems):
    ///  * The exact chance is shown on screen before you throw. What you see is what is rolled.
    ///  * Bind Orbs are unlimited and free. Nothing can be bought to change the odds.
    ///  * Every failed attempt raises the next chance on that creature (Resonance).
    ///  * An exhausted creature (0 HP) is always a guaranteed bind.
    /// </summary>
    public class PlayerBinder : MonoBehaviour
    {
        public float bindRange = 7.5f;
        public float maxAngle = 75f;
        public float throwCooldown = 1.2f;
        public float shakeDuration = 1.5f;

        private PlayerController _player;
        private float _readyAt;
        private float _nextScan;
        private Creature _activeTarget;
        private GameObject _orb;
        private GameObject _ring;
        private static Mesh _ringMesh;

        public Creature CurrentTarget { get; private set; }
        public float CurrentChance => CurrentTarget != null ? CurrentTarget.CaptureChance : 0f;
        public bool IsBusy { get; private set; }

        private void Awake() => _player = GetComponent<PlayerController>();

        private void Update()
        {
            if (_player == null || !_player.IsAlive)
            {
                CurrentTarget = null;
                return;
            }
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + 0.1f;
                CurrentTarget = IsBusy ? null : FindTarget();
            }
            if (CurrentTarget != null && !CurrentTarget.CanBeBound) CurrentTarget = null;

            if (GameInput.BindPressed && !IsBusy && Time.time >= _readyAt)
            {
                if (CurrentTarget != null) StartCoroutine(BindRoutine(CurrentTarget));
                else if (GameManager.Instance != null && GameManager.Instance.State == GameState.Playing)
                    GameEvents.Notify("No creature in reach. Get closer and face it.", NotificationKind.Info);
            }
        }

        private Creature FindTarget()
        {
            var spawner = GameManager.Instance != null ? GameManager.Instance.Spawner : null;
            if (spawner == null) return null;
            var cam = _player.CameraTransform != null ? _player.CameraTransform : (Camera.main != null ? Camera.main.transform : null);
            Vector3 camForward = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up) : transform.forward;

            Creature best = null;
            float bestScore = float.MaxValue;
            foreach (var c in spawner.WildCreatures)
            {
                if (c == null || !c.CanBeBound) continue;
                Vector3 to = c.transform.position - transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist > bindRange) continue;
                float angle = Mathf.Min(Vector3.Angle(camForward, to), Vector3.Angle(transform.forward, to));
                if (angle > maxAngle && dist > 2f) continue;
                if (!CombatQuery.HasLineOfSight(transform.position + Vector3.up * 1.4f, c.Center)) continue;
                float score = dist + angle * 0.04f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best;
        }

        private IEnumerator BindRoutine(Creature target)
        {
            IsBusy = true;
            _activeTarget = target;
            _readyAt = Time.time + throwCooldown;

            // Lock the odds at throw time: the number on screen is the number rolled.
            float chance = target.CaptureChance;
            Awareness awareness = target.CurrentAwareness;

            var lookDir = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
            if (lookDir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
            _player.Avatar?.PlayThrow();
            AudioManager.PlaySfx(SfxId.BindThrow, transform.position + Vector3.up, 0.8f);

            // Orb flight
            var orbMat = MaterialFactory.Emissive("BindOrb", new Color(0.55f, 1f, 0.9f), new Color(0.6f, 2.4f, 2f), 0.9f);
            var orb = MeshKit.AddPart(null, "BindOrb", MeshKit.Primitive(PrimitiveType.Sphere), orbMat, Vector3.zero,
                Vector3.one * 0.3f, Quaternion.identity, false);
            _orb = orb;
            Vector3 start = transform.position + Vector3.up * 1.5f + transform.forward * 0.4f;
            const float flight = 0.4f;
            float t = 0f;
            while (t < flight)
            {
                if (target == null) break;
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / flight);
                Vector3 end = target.Center;
                orb.transform.position = Vector3.Lerp(start, end, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.2f;
                yield return null;
            }

            if (target == null || !target.CanBeBound)
            {
                Destroy(orb);
                Finish();
                yield break;
            }

            // Shake phase: the creature is held in a ring of light.
            target.Brain.BeginBinding();
            Destroy(orb);
            var ring = CreateRing(target);
            _ring = ring;
            int pulses = 3;
            for (int i = 0; i < pulses; i++)
            {
                AudioManager.PlaySfx(SfxId.BindShake, target.Center, 0.8f, 1f + i * 0.12f);
                float pulseEnd = Time.time + shakeDuration / pulses;
                while (Time.time < pulseEnd)
                {
                    if (target == null) break;
                    if (ring != null)
                    {
                        float s = 1.4f + Mathf.Sin(Time.time * 12f) * 0.1f;
                        ring.transform.position = target.transform.position + Vector3.up * 0.5f;
                        ring.transform.localScale = new Vector3(s, 0.03f, s);
                        ring.transform.Rotate(Vector3.up, 360f * Time.deltaTime, Space.World);
                    }
                    yield return null;
                }
                if (target == null) break;
            }
            if (ring != null) Destroy(ring);
            if (target == null)
            {
                Finish();
                yield break;
            }

            bool success = CaptureRules.Resolve(chance, Random.value);
            var gm = GameManager.Instance;
            if (success && gm != null && gm.Collection != null)
            {
                AudioManager.PlaySfx(SfxId.BindSuccess, target.Center, 1f);
                gm.Collection.BindWildCreature(target, awareness);
            }
            else
            {
                target.FailedBindAttempts++;
                target.Brain.BreakFree();
                AudioManager.PlaySfx(SfxId.BindFail, target.Center, 0.9f);
                int nextPct = Mathf.RoundToInt(target.CaptureChance * 100f);
                GameEvents.Notify($"{target.DisplayName} broke free! Resonance builds — next attempt: {nextPct}%.", NotificationKind.Warning);
            }
            Finish();
        }

        private void Finish()
        {
            IsBusy = false;
            _activeTarget = null;
            _orb = null;
            _ring = null;
        }

        /// <summary>Aborts an attempt in progress (respawn, returning to the title screen).</summary>
        public void CancelBinding()
        {
            StopAllCoroutines();
            if (_orb != null) Destroy(_orb);
            if (_ring != null) Destroy(_ring);
            if (_activeTarget != null && _activeTarget.Brain != null && _activeTarget.Brain.State == CreatureState.Binding)
            {
                _activeTarget.Brain.ResetToCalm();
            }
            Finish();
        }

        private static GameObject CreateRing(Creature target)
        {
            var mat = MaterialFactory.Emissive("BindRing", new Color(0.5f, 1f, 0.9f), new Color(0.5f, 2f, 1.8f), 0.9f);
            if (_ringMesh == null) _ringMesh = MeshKit.Frustum(0.5f, 0.5f, 1f, 24, "BindRing");
            var ring = MeshKit.AddPart(null, "BindRing", _ringMesh, mat,
                target.transform.position + Vector3.up * 0.5f, new Vector3(1.4f, 0.03f, 1.4f), Quaternion.identity, false);
            return ring;
        }

        private void OnDisable()
        {
            CancelBinding();
        }
    }
}
