using Beastbound.Core;
using UnityEngine;

namespace Beastbound.Creatures
{
    public enum CreaturePose
    {
        Normal,
        Sleeping,
        Alert,
        Exhausted,
        Binding,
        Windup
    }

    /// <summary>
    /// Procedural animation for creature models: hopping gait, breathing, ear and tail motion, sleeping and
    /// hit reactions. Driven entirely by values the AI sets each frame, so swapping in an Animator-based
    /// model later only means replacing this component.
    /// </summary>
    public class CreatureAnimator : MonoBehaviour
    {
        private CreatureModelParts _parts;
        private Color _crystalGlow;
        private float _phase;
        private float _speedSmoothed;
        private float _hurtTimer;
        private float _lungeTimer;
        private float _happyTimer;
        private float _poseBlend;
        private Vector3 _rootBasePos;
        private Quaternion _headBaseRot;
        private Quaternion _earLBase, _earRBase, _tailBase;
        private Vector3[] _eyeBaseScale;
        private float _glowBoost;
        private QuadrupedRig _rig;
        private float _lastYaw;
        private float _turnRate;

        /// <summary>Current planar speed in m/s (set by the motor).</summary>
        public float Speed { get; set; }
        public float MaxSpeed { get; set; } = 5f;
        public CreaturePose Pose { get; set; } = CreaturePose.Normal;

        public void Initialize(CreatureModelParts parts, Color crystalGlow)
        {
            _parts = parts;
            _crystalGlow = crystalGlow;
            if (parts == null) return;
            _rootBasePos = parts.Root.localPosition;
            _headBaseRot = parts.Head.localRotation;
            _earLBase = parts.EarLeft.localRotation;
            _earRBase = parts.EarRight.localRotation;
            _tailBase = parts.Tail.localRotation;
            _eyeBaseScale = new Vector3[parts.Eyes.Length];
            for (int i = 0; i < parts.Eyes.Length; i++) _eyeBaseScale[i] = parts.Eyes[i].localScale;
            _phase = Random.value * 10f;
        }

        /// <summary>Uses a skinned Blender model (bones driven by <see cref="QuadrupedRig"/>) instead of primitives.</summary>
        public void InitializeSkinned(QuadrupedRig rig)
        {
            _rig = rig;
            _lastYaw = transform.localEulerAngles.y;
        }

        public bool IsSkinned => _rig != null;

        /// <summary>Animate while the game is paused (Bestiary portrait).</summary>
        public bool UseUnscaledTime { get; set; }

        public void PlayHurt() { _hurtTimer = 0.25f; _rig?.PlayHurt(); }
        public void PlayLunge() { _lungeTimer = 0.3f; _rig?.PlayLunge(); }
        public void PlayHappy() { _happyTimer = 0.8f; _rig?.PlayHappy(); }

        /// <summary>Brightens the brow crystal briefly (binding, happiness).</summary>
        public void Glow(float amount) { _glowBoost = Mathf.Max(_glowBoost, amount); _rig?.Glow(amount); }

        private void LateUpdate()
        {
            float dt = UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float t = UseUnscaledTime ? Time.unscaledTime : Time.time;
            if (_rig != null)
            {
                // Turn rate for the spine bend: web convention, rad/s with positive = turning left.
                float yaw = transform.localEulerAngles.y;
                float yawRate = dt > 0f ? Mathf.DeltaAngle(_lastYaw, yaw) / dt : 0f;
                _lastYaw = yaw;
                _turnRate = Mathf.Lerp(_turnRate, -yawRate * Mathf.Deg2Rad, QuadrupedRig.Damp(10f, dt));
                if (dt > 0f) _rig.Update(dt, t, Speed, MaxSpeed, _turnRate, Pose);
                return;
            }
            if (_parts == null || _parts.Root == null) return;

            _speedSmoothed = Mathf.Lerp(_speedSmoothed, Speed, 1f - Mathf.Exp(-10f * dt));
            float moveAmount = Mathf.Clamp01(_speedSmoothed / Mathf.Max(0.1f, MaxSpeed));
            float stride = Mathf.Lerp(7f, 15f, moveAmount);
            _phase += dt * (_speedSmoothed > 0.05f ? stride : 0f);

            bool sleeping = Pose == CreaturePose.Sleeping;
            bool exhausted = Pose == CreaturePose.Exhausted;
            bool lowPose = sleeping || exhausted;
            _poseBlend = Mathf.MoveTowards(_poseBlend, lowPose ? 1f : 0f, dt * 2.5f);

            // Root: hop + breathing + squash on hurt + lunge
            float breathe = Mathf.Sin(t * (sleeping ? 1.4f : 2.6f)) * (sleeping ? 0.04f : 0.025f);
            float hop = Mathf.Abs(Mathf.Sin(_phase)) * 0.09f * moveAmount;
            float happyHop = 0f;
            if (_happyTimer > 0f)
            {
                _happyTimer -= dt;
                happyHop = Mathf.Abs(Mathf.Sin(_happyTimer * 12f)) * 0.18f;
            }
            Vector3 pos = _rootBasePos + Vector3.up * (hop + happyHop - 0.14f * _poseBlend);
            if (Pose == CreaturePose.Windup) pos += Vector3.down * 0.06f + Vector3.back * 0.08f;
            if (_lungeTimer > 0f)
            {
                _lungeTimer -= dt;
                pos += Vector3.forward * Mathf.Sin(_lungeTimer / 0.3f * Mathf.PI) * 0.35f;
            }
            _parts.Root.localPosition = pos;

            Vector3 scale = new Vector3(1f - breathe * 0.5f, 1f + breathe, 1f - breathe * 0.5f);
            if (_hurtTimer > 0f)
            {
                _hurtTimer -= dt;
                float k = _hurtTimer / 0.25f;
                scale = Vector3.Scale(scale, new Vector3(1f + 0.18f * k, 1f - 0.22f * k, 1f + 0.18f * k));
            }
            if (Pose == CreaturePose.Windup) scale = Vector3.Scale(scale, new Vector3(1.08f, 0.9f, 1.08f));
            _parts.Root.localScale = scale * BaseScale;

            // Binding: rapid shake
            Quaternion rootRot = Quaternion.identity;
            if (Pose == CreaturePose.Binding)
            {
                rootRot = Quaternion.Euler(0f, Mathf.Sin(t * 38f) * 12f, Mathf.Sin(t * 31f) * 5f);
            }
            else if (exhausted)
            {
                rootRot = Quaternion.Euler(0f, 0f, 18f * _poseBlend);
            }
            _parts.Root.localRotation = rootRot;

            // Head
            float nod = Mathf.Sin(_phase * 2f) * 4f * moveAmount;
            float look = Mathf.Sin(t * 0.7f) * 10f * (1f - moveAmount) * (1f - _poseBlend);
            _parts.Head.localRotation = _headBaseRot * Quaternion.Euler(nod + 20f * _poseBlend, look, 0f);

            // Ears: perk when alert, droop when asleep/exhausted, flap when running
            float perk = Pose == CreaturePose.Alert || Pose == CreaturePose.Windup ? -14f : 0f;
            float droop = 55f * _poseBlend;
            float flap = Mathf.Sin(_phase * 2f + 0.5f) * 10f * moveAmount + Mathf.Sin(t * 1.7f) * 3f;
            if (Pose == CreaturePose.Windup) droop += 40f; // ears tucked back = "I'm about to charge"
            _parts.EarLeft.localRotation = _earLBase * Quaternion.Euler(droop * 0.6f, 0f, -(flap + perk) + droop * 0.5f);
            _parts.EarRight.localRotation = _earRBase * Quaternion.Euler(droop * 0.6f, 0f, (flap + perk) - droop * 0.5f);

            // Tail wag
            float wagSpeed = _happyTimer > 0f ? 18f : Mathf.Lerp(3f, 12f, moveAmount);
            float wag = Mathf.Sin(t * wagSpeed) * Mathf.Lerp(12f, 22f, moveAmount) * (1f - _poseBlend * 0.8f);
            _parts.Tail.localRotation = _tailBase * Quaternion.Euler(0f, wag, 0f);
            if (_parts.TailBloom != null)
            {
                float bloomOpen = Mathf.Lerp(1f, 0.55f, _poseBlend); // flower closes while sleeping
                _parts.TailBloom.localScale = new Vector3(bloomOpen, 1f, bloomOpen);
            }

            // Legs
            for (int i = 0; i < _parts.Legs.Length; i++)
            {
                float offset = (i == 0 || i == 3) ? 0f : Mathf.PI;
                float swing = Mathf.Sin(_phase + offset) * 35f * moveAmount;
                float tuck = 70f * _poseBlend * (i < 2 ? -1f : 1f);
                _parts.Legs[i].localRotation = Quaternion.Euler(swing + tuck, 0f, 0f);
            }

            // Eyes: blink, closed while sleeping
            float blink = Mathf.Repeat(t + _phase * 0.01f, 3.7f) < 0.12f ? 0.15f : 1f;
            float openness = sleeping ? 0.12f : (exhausted ? 0.45f : blink);
            for (int i = 0; i < _parts.Eyes.Length; i++)
            {
                var s = _eyeBaseScale[i];
                _parts.Eyes[i].localScale = new Vector3(s.x, s.y * openness, s.z);
            }

            // Crystal glow
            if (_parts.CrystalMaterial != null)
            {
                _glowBoost = Mathf.MoveTowards(_glowBoost, 0f, dt * 1.5f);
                float pulse = 0.7f + Mathf.Sin(t * 2f) * 0.15f + _glowBoost * 2f;
                if (sleeping) pulse *= 0.4f;
                MaterialFactory.SetEmission(_parts.CrystalMaterial, _crystalGlow * pulse);
            }
        }

        /// <summary>Uniform scale chosen when the model was built.</summary>
        public float BaseScale { get; set; } = 1f;
    }
}
