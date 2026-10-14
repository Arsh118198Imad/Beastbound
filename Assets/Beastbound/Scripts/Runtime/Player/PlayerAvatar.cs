using Beastbound.Core;
using UnityEngine;

namespace Beastbound.Player
{
    /// <summary>
    /// Procedural placeholder character (a young "Warden" with a staff) plus code-driven animation:
    /// walk/run/sneak cycles, jumping, staff combos, casting, throwing, dodge rolls and hit flinches.
    /// To use an imported rigged character later, replace this component; gameplay calls only the Play* methods.
    /// </summary>
    public class PlayerAvatar : MonoBehaviour
    {
        private Transform _root, _hips, _torso, _head, _armL, _armR, _legL, _legR, _staffTip;
        private Material _staffGlowMat;
        private float _phase;
        private float _crouch;
        private float _lean;

        private float _strikeTimer;
        private int _strikeIndex;
        private float _castTimer;
        private float _throwTimer;
        private float _dodgeTimer;
        private float _hurtTimer;
        private float _landTimer;
        private float _jumpTimer;
        private bool _grounded = true;
        private float _verticalVelocity;
        private float _defeatBlend;
        private Combat.Health _health;

        private const float StrikeTime = 0.42f;
        private const float DodgeTime = 0.3f;

        public void Build()
        {
            var tunic = MaterialFactory.Lit("Warden_Tunic", new Color(0.16f, 0.5f, 0.52f), 0.2f);
            var tunicDark = MaterialFactory.Lit("Warden_TunicDark", new Color(0.12f, 0.36f, 0.4f), 0.2f);
            var skin = MaterialFactory.Lit("Warden_Skin", new Color(0.93f, 0.76f, 0.62f), 0.3f);
            var hair = MaterialFactory.Lit("Warden_Hair", new Color(0.32f, 0.19f, 0.12f), 0.35f);
            var scarf = MaterialFactory.Lit("Warden_Scarf", new Color(0.93f, 0.45f, 0.22f), 0.15f);
            var leather = MaterialFactory.Lit("Warden_Leather", new Color(0.45f, 0.3f, 0.18f), 0.25f);
            var pants = MaterialFactory.Lit("Warden_Pants", new Color(0.28f, 0.24f, 0.22f), 0.15f);
            var boots = MaterialFactory.Lit("Warden_Boots", new Color(0.2f, 0.14f, 0.1f), 0.3f);
            var leaf = MaterialFactory.Lit("Warden_Leaf", new Color(0.4f, 0.75f, 0.3f), 0.2f);
            var wood = MaterialFactory.Lit("Warden_Staff", new Color(0.52f, 0.36f, 0.2f), 0.3f);
            var eye = MaterialFactory.Lit("Warden_Eye", new Color(0.1f, 0.1f, 0.14f), 0.8f);
            _staffGlowMat = MaterialFactory.Unique("Warden_StaffGlow", new Color(0.55f, 1f, 0.9f), 0.9f);
            MaterialFactory.SetEmission(_staffGlowMat, new Color(0.4f, 1.6f, 1.4f));

            Mesh sphere = MeshKit.Primitive(PrimitiveType.Sphere);
            Mesh capsule = MeshKit.Primitive(PrimitiveType.Capsule);
            Mesh cube = MeshKit.Primitive(PrimitiveType.Cube);
            Mesh cylinder = MeshKit.Primitive(PrimitiveType.Cylinder);

            _root = Pivot(transform, "Avatar", Vector3.zero);
            _hips = Pivot(_root, "Hips", new Vector3(0f, 0.95f, 0f));
            _torso = Pivot(_hips, "Torso", Vector3.zero);
            MeshKit.AddPart(_torso, "Chest", capsule, tunic, new Vector3(0f, 0.32f, 0f), new Vector3(0.48f, 0.34f, 0.34f), Quaternion.identity);
            MeshKit.AddPart(_torso, "TunicSkirt", MeshKit.Frustum(0.3f, 0.22f, 0.3f, 10, "Skirt"), tunicDark,
                new Vector3(0f, -0.12f, 0f), Vector3.one, Quaternion.identity);
            MeshKit.AddPart(_torso, "Belt", cylinder, leather, new Vector3(0f, 0.08f, 0f), new Vector3(0.5f, 0.035f, 0.37f), Quaternion.identity);
            MeshKit.AddPart(_torso, "Scarf", sphere, scarf, new Vector3(0f, 0.6f, 0.02f), new Vector3(0.5f, 0.14f, 0.42f), Quaternion.identity);
            MeshKit.AddPart(_torso, "ScarfTail", cube, scarf, new Vector3(0.12f, 0.45f, -0.2f), new Vector3(0.1f, 0.3f, 0.03f), Quaternion.Euler(15f, 0f, -10f));
            MeshKit.AddPart(_torso, "Backpack", cube, leather, new Vector3(0f, 0.36f, -0.24f), new Vector3(0.34f, 0.38f, 0.18f), Quaternion.identity);
            MeshKit.AddPart(_torso, "Bedroll", cylinder, scarf, new Vector3(0f, 0.6f, -0.26f), new Vector3(0.13f, 0.2f, 0.13f), Quaternion.Euler(0f, 0f, 90f));

            _head = Pivot(_torso, "Head", new Vector3(0f, 0.8f, 0f));
            MeshKit.AddPart(_head, "Face", sphere, skin, Vector3.zero, new Vector3(0.34f, 0.36f, 0.33f), Quaternion.identity);
            MeshKit.AddPart(_head, "Hair", sphere, hair, new Vector3(0f, 0.06f, -0.03f), new Vector3(0.37f, 0.33f, 0.36f), Quaternion.identity);
            MeshKit.AddPart(_head, "Fringe", sphere, hair, new Vector3(0.05f, 0.12f, 0.1f), new Vector3(0.28f, 0.12f, 0.2f), Quaternion.Euler(0f, 0f, -12f));
            MeshKit.AddPart(_head, "EyeL", sphere, eye, new Vector3(-0.07f, 0.0f, 0.155f), new Vector3(0.045f, 0.065f, 0.03f), Quaternion.identity, false);
            MeshKit.AddPart(_head, "EyeR", sphere, eye, new Vector3(0.07f, 0.0f, 0.155f), new Vector3(0.045f, 0.065f, 0.03f), Quaternion.identity, false);
            MeshKit.AddPart(_head, "Leaf", sphere, leaf, new Vector3(-0.1f, 0.2f, -0.02f), new Vector3(0.06f, 0.2f, 0.1f), Quaternion.Euler(0f, 0f, 30f));

            _armL = Pivot(_torso, "ArmL", new Vector3(-0.3f, 0.55f, 0f));
            _armR = Pivot(_torso, "ArmR", new Vector3(0.3f, 0.55f, 0f));
            foreach (var arm in new[] { _armL, _armR })
            {
                MeshKit.AddPart(arm, "Sleeve", capsule, tunic, new Vector3(0f, -0.22f, 0f), new Vector3(0.14f, 0.24f, 0.14f), Quaternion.identity);
                MeshKit.AddPart(arm, "Hand", sphere, skin, new Vector3(0f, -0.5f, 0f), new Vector3(0.12f, 0.12f, 0.12f), Quaternion.identity);
            }

            // Staff held in the right hand
            var staff = Pivot(_armR, "Staff", new Vector3(0f, -0.5f, 0f));
            staff.localRotation = Quaternion.Euler(80f, 0f, 0f);
            MeshKit.AddPart(staff, "Shaft", cylinder, wood, new Vector3(0f, 0.25f, 0f), new Vector3(0.05f, 0.75f, 0.05f), Quaternion.identity);
            MeshKit.AddPart(staff, "Wrap", cylinder, leaf, new Vector3(0f, 0.85f, 0f), new Vector3(0.07f, 0.08f, 0.07f), Quaternion.identity);
            _staffTip = MeshKit.AddPart(staff, "Crystal", MeshKit.Crystal(5, 0.5f, 1.2f, 0.5f), _staffGlowMat,
                new Vector3(0f, 1.05f, 0f), Vector3.one * 0.12f, Quaternion.identity, false).transform;

            _legL = Pivot(_hips, "LegL", new Vector3(-0.12f, 0f, 0f));
            _legR = Pivot(_hips, "LegR", new Vector3(0.12f, 0f, 0f));
            foreach (var leg in new[] { _legL, _legR })
            {
                MeshKit.AddPart(leg, "Leg", capsule, pants, new Vector3(0f, -0.42f, 0f), new Vector3(0.17f, 0.42f, 0.17f), Quaternion.identity);
                MeshKit.AddPart(leg, "Boot", sphere, boots, new Vector3(0f, -0.86f, 0.05f), new Vector3(0.17f, 0.14f, 0.27f), Quaternion.identity);
            }

            Layers.SetLayerRecursively(_root.gameObject, gameObject.layer);
        }

        private static Transform Pivot(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.gameObject.layer = parent.gameObject.layer;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        // ------------------------------------------------------------------ Triggers

        public void PlayStrike(int comboIndex) { _strikeTimer = StrikeTime; _strikeIndex = comboIndex; }
        public void PlayCast() => _castTimer = 0.35f;
        public void PlayThrow() => _throwTimer = 0.4f;
        public void PlayDodge() => _dodgeTimer = DodgeTime;
        public void PlayHurt() => _hurtTimer = 0.25f;
        public void PlayLand() => _landTimer = 0.15f;
        public void PlayJump() => _jumpTimer = 0.2f;

        public void ResetPose()
        {
            _strikeTimer = _castTimer = _throwTimer = _dodgeTimer = _hurtTimer = _landTimer = _jumpTimer = 0f;
            _defeatBlend = 0f;
        }

        // ------------------------------------------------------------------ Animation

        /// <summary>Called by PlayerController every frame with the current movement state.</summary>
        public void Tick(float speed, float runSpeed, bool grounded, bool sneaking, bool wading, float verticalVelocity)
        {
            if (_root == null) return;
            float dt = Time.deltaTime;
            float t = Time.time;
            _grounded = grounded;
            _verticalVelocity = verticalVelocity;

            float move = Mathf.Clamp01(speed / Mathf.Max(0.1f, runSpeed));
            _phase += dt * speed * (sneaking ? 3.4f : 2.6f);
            _crouch = Mathf.MoveTowards(_crouch, sneaking ? 1f : 0f, dt * 5f);
            _lean = Mathf.Lerp(_lean, move * 12f + _crouch * 18f, 1f - Mathf.Exp(-8f * dt));

            if (_health == null) _health = GetComponent<Combat.Health>();
            bool defeated = _health != null && _health.IsDead;
            _defeatBlend = Mathf.MoveTowards(_defeatBlend, defeated ? 1f : 0f, dt * 2.5f);

            // Hips: bob, crouch, land squash
            float bob = grounded ? Mathf.Abs(Mathf.Sin(_phase)) * 0.06f * Mathf.Clamp01(speed / 2f) : 0f;
            float landSquash = 0f;
            if (_landTimer > 0f) { _landTimer -= dt; landSquash = Mathf.Sin(_landTimer / 0.15f * Mathf.PI) * 0.1f; }
            _hips.localPosition = new Vector3(0f, 0.95f + bob - _crouch * 0.22f - landSquash - (wading ? 0.05f : 0f), 0f);

            // Torso lean and flinch
            float flinch = 0f;
            if (_hurtTimer > 0f) { _hurtTimer -= dt; flinch = Mathf.Sin(_hurtTimer / 0.25f * Mathf.PI) * -18f; }
            float twist = Mathf.Sin(_phase) * 6f * move;
            _torso.localRotation = Quaternion.Euler(_lean + flinch, twist, 0f);
            _head.localRotation = Quaternion.Euler(-_lean * 0.5f, -twist * 0.5f, 0f);

            // Legs
            float swingAmp = Mathf.Lerp(28f, 50f, move) * Mathf.Clamp01(speed / 1.2f);
            float legL, legR;
            if (grounded)
            {
                legL = Mathf.Sin(_phase) * swingAmp - _crouch * 25f;
                legR = -Mathf.Sin(_phase) * swingAmp - _crouch * 25f;
            }
            else
            {
                float tuck = verticalVelocity > 0f ? 1f : 0.6f;
                legL = -35f * tuck;
                legR = 20f * tuck;
            }
            _legL.localRotation = Quaternion.Euler(legL, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(legR, 0f, 0f);

            // Arms (default swing, overridden by actions)
            float armSwing = Mathf.Sin(_phase) * swingAmp * 0.8f;
            Quaternion armL = Quaternion.Euler(-armSwing + _crouch * 20f, 0f, -8f - (grounded ? 0f : 35f));
            Quaternion armR = Quaternion.Euler(armSwing + _crouch * 20f, 0f, 8f + (grounded ? 0f : 35f));

            if (_strikeTimer > 0f)
            {
                _strikeTimer -= dt;
                float k = 1f - Mathf.Clamp01(_strikeTimer / StrikeTime); // 0..1
                float swing = k < 0.35f ? Mathf.SmoothStep(0f, 1f, k / 0.35f) : 1f;
                switch (_strikeIndex)
                {
                    case 0: armR = Quaternion.Euler(Mathf.Lerp(-130f, -20f, swing), Mathf.Lerp(-40f, 30f, swing), 20f); break;
                    case 1: armR = Quaternion.Euler(-80f, Mathf.Lerp(60f, -60f, swing), 30f); break;
                    default: armR = Quaternion.Euler(Mathf.Lerp(-190f, -10f, swing), 0f, 5f); break;
                }
                armL = Quaternion.Euler(-30f, 0f, -20f);
            }
            else if (_castTimer > 0f)
            {
                _castTimer -= dt;
                armR = Quaternion.Euler(-95f, 0f, 5f);
                armL = Quaternion.Euler(-80f, 0f, -5f);
            }
            else if (_throwTimer > 0f)
            {
                _throwTimer -= dt;
                float k = 1f - Mathf.Clamp01(_throwTimer / 0.4f);
                armR = Quaternion.Euler(Mathf.Lerp(-170f, -40f, Mathf.SmoothStep(0f, 1f, k)), 0f, 10f);
            }
            _armL.localRotation = armL;
            _armR.localRotation = armR;

            // Root: dodge roll, defeat collapse
            Quaternion rootRot = Quaternion.identity;
            Vector3 rootPos = Vector3.zero;
            if (_dodgeTimer > 0f)
            {
                _dodgeTimer -= dt;
                float k = 1f - Mathf.Clamp01(_dodgeTimer / DodgeTime);
                rootRot = Quaternion.Euler(k * 360f, 0f, 0f);
                rootPos = Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.45f);
                _hips.localPosition += Vector3.down * Mathf.Sin(k * Mathf.PI) * 0.35f;
            }
            if (_defeatBlend > 0f)
            {
                rootRot = Quaternion.Slerp(rootRot, Quaternion.Euler(-80f, 0f, 0f), _defeatBlend);
                rootPos += Vector3.up * 0.25f * _defeatBlend;
            }
            _root.localRotation = rootRot;
            _root.localPosition = rootPos;

            // Staff crystal pulse
            if (_staffGlowMat != null)
            {
                float pulse = 1f + Mathf.Sin(t * 3f) * 0.15f + (_castTimer > 0f ? 1.5f : 0f) + (_throwTimer > 0f ? 1f : 0f);
                MaterialFactory.SetEmission(_staffGlowMat, new Color(0.4f, 1.6f, 1.4f) * pulse);
            }
            if (_jumpTimer > 0f) _jumpTimer -= dt;
        }

        /// <summary>World position of the staff crystal (spell/orb origin, VFX).</summary>
        public Vector3 StaffTipPosition => _staffTip != null ? _staffTip.position : transform.position + Vector3.up * 1.5f;
    }
}
