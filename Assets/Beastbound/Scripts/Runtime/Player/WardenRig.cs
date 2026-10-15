using Beastbound.Core;
using Beastbound.Creatures;
using UnityEngine;

namespace Beastbound.Player
{
    /// <summary>Movement state the avatar animates from each frame.</summary>
    public struct AvatarState
    {
        public float Speed;        // planar m/s
        public float RunSpeed;     // for the simple fallback avatar
        public bool Grounded, Sneaking, Wading, Running, Dead;
        public float VerticalVelocity;
        public float Accel;        // forward acceleration, m/s²
        public float Turn;         // rad/s, positive = turning left (web convention)
    }

    /// <summary>
    /// Procedural animation for the skinned Warden (Blender rig): stride-locked gait with pelvis bob, sway,
    /// hip drop and shoulder counter-rotation, knee flex and heel-to-toe roll, head stabilisation, banking into
    /// turns, idle breathing and weight shifts, staff combos, casting, throwing, dodge rolls and flinches.
    /// A port of SkinnedAvatar in web/js/player.js, using the same pose numbers.
    /// </summary>
    public class WardenRig
    {
        private static readonly string[] BoneNames =
        {
            "hips", "torso", "head", "armL", "armR", "forearmL", "forearmR", "legL", "legR", "shinL", "shinR", "footL", "footR"
        };

        private readonly Transform _root;
        private readonly ModelInstance _model;
        private readonly BonePoser _p;
        private float _phase, _crouch, _lean, _runK, _gait, _bank, _defeat;

        public float Strike, Cast, Throw, Dodge, Hurt, Land;
        public int StrikeIndex;

        public WardenRig(Transform root, ModelInstance model)
        {
            _root = root;
            _model = model;
            _p = new BonePoser(root, model.Bones, BoneNames);
        }

        public void Reset()
        {
            Strike = Cast = Throw = Dodge = Hurt = Land = 0f;
            _defeat = 0f;
        }

        public void Update(float dt, float t, AvatarState s)
        {
            var P = _p;
            // Gait is driven by distance travelled (stride length), so feet plant instead of sliding.
            _runK = Mathf.Lerp(_runK, s.Running ? Mathf.Clamp01((s.Speed - 3.6f) / 3f) : 0f, QuadrupedRig.Damp(5f, dt));
            _gait = Mathf.Lerp(_gait, Mathf.Clamp01(s.Speed / 1.1f), QuadrupedRig.Damp(7f, dt));
            float stride = s.Sneaking ? 1.3f : Mathf.Lerp(1.55f, 2.6f, _runK); // metres per full two-step cycle
            if (s.Grounded) _phase += s.Speed / stride * Mathf.PI * 2f * dt;
            if (s.Speed < 0.2f)
            {
                // Settle into a neutral stance when stopping.
                float target = Mathf.Round(_phase / Mathf.PI) * Mathf.PI;
                _phase += (target - _phase) * Mathf.Min(1f, dt * 6f);
            }
            _crouch = QuadrupedRig.Approach(_crouch, s.Sneaking ? 1f : 0f, dt * 5f);
            _defeat = QuadrupedRig.Approach(_defeat, s.Dead ? 1f : 0f, dt * 2.5f);
            float c = _crouch, g = _gait, r = _runK;
            float sin = Mathf.Sin(_phase), cos = Mathf.Cos(_phase);
            float squash = 0f;
            if (Land > 0f) { Land -= dt; squash = Mathf.Sin(Land / 0.15f * Mathf.PI) * 0.08f; }
            float flinch = 0f;
            if (Hurt > 0f) { Hurt -= dt; flinch = Mathf.Sin(Hurt / 0.25f * Mathf.PI) * -16f; }

            // Pelvis: bob twice per cycle (high over the planted foot), sway onto the stance leg, hip drop and twist.
            float idle = 1f - g;
            float bob = s.Grounded ? Mathf.Cos(_phase * 2f) * Mathf.Lerp(0.022f, 0.04f, r) * g : 0f;
            float swayX = cos * 0.028f * g * (1f - r * 0.5f) + Mathf.Sin(t * 0.55f) * 0.012f * idle; // idle weight shift
            float hipsY = bob - c * 0.2f - squash - (s.Wading ? 0.04f : 0f) - 0.01f * r;
            float hipYaw = sin * Mathf.Lerp(7f, 10f, r) * g;
            P.Set("hips", 0f, hipYaw, cos * 3.5f * g);
            // Torso: lean with speed and acceleration, counter-rotate the shoulders, breathe at rest.
            float accelLean = Mathf.Clamp(s.Accel * 1.1f, -9f, 10f);
            float lean = g * Mathf.Lerp(5f, 13f, r) + c * 16f + accelLean;
            _lean = Mathf.Lerp(_lean, lean, QuadrupedRig.Damp(7f, dt));
            float breathe = Mathf.Sin(t * 1.7f) * 1.4f * idle;
            P.Set("torso", _lean + flinch + breathe, -hipYaw * 1.7f, -cos * 2f * g);
            // Head: stays level and steady, looks into turns, glances around when idle.
            float turnLook = Mathf.Clamp(s.Turn * 9f, -18f, 18f);
            float glance = Mathf.Sin(t * 0.37f) * 14f * idle * (1f - c);
            P.Set("head", -_lean * 0.75f - flinch * 0.3f - breathe * 0.5f, hipYaw * 0.7f + turnLook + glance, 0f);

            // Legs: thigh swing, knee flex peaking mid-swing, heel strike / flat stance / toe-off at the ankle.
            float thighAmp = (s.Sneaking ? 24f : Mathf.Lerp(26f, 46f, r)) * g;
            float kneeAmp = (s.Sneaking ? 44f : Mathf.Lerp(52f, 100f, r)) * g;
            if (s.Grounded)
            {
                for (int i = 0; i < 2; i++)
                {
                    string side = i == 0 ? "L" : "R";
                    float ph = _phase + (i == 0 ? 0f : Mathf.PI), sp = Mathf.Sin(ph), cp = Mathf.Cos(ph);
                    float thigh = -sp * thighAmp - c * 38f - _lean * 0.25f;
                    float swing = Mathf.Max(0f, cp); // leg travelling forward
                    float knee = Mathf.Pow(swing, 1.3f) * kneeAmp + (1f - swing) * Mathf.Lerp(6f, 16f, r) * g + c * 62f;
                    float heelToe = (-10f * Mathf.Max(0f, sp) + 24f * Mathf.Max(0f, -sp) * Mathf.Max(0f, cp + 0.2f)) * g;
                    P.Set("leg" + side, thigh);
                    P.Set("shin" + side, knee);
                    P.Set("foot" + side, heelToe - (thigh + knee) * 0.85f - c * 6f);
                }
            }
            else
            {
                float k = s.VerticalVelocity > 0f ? 1f : 0.6f;
                P.Set("legL", -40f * k); P.Set("shinL", 70f * k); P.Set("footL", -10f);
                P.Set("legR", 15f * k); P.Set("shinR", 35f * k); P.Set("footR", 0f);
            }
            // Lean the whole body into turns, more at speed.
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(-s.Turn * s.Speed * 2.2f, -16f, 16f), QuadrupedRig.Damp(6f, dt));

            // Arms: counter-swing, soft elbows; actions override the right (staff) arm, which swings less.
            float asw = sin * Mathf.Lerp(0.8f, 1.05f, r) * thighAmp;
            Vector3 aL = new Vector3(asw + c * 15f, 0f, 6f + (s.Grounded ? 0f : 30f));
            Vector3 aR = new Vector3(-asw * Mathf.Lerp(0.6f, 0.35f, r) + c * 15f - 8f * r, 0f, -6f - (s.Grounded ? 0f : 30f));
            float fL = -14f - Mathf.Lerp(12f, 70f, r) * g - Mathf.Max(0f, -sin) * 12f * g;
            float fR = -16f - Mathf.Lerp(10f, 28f, r) * g;
            if (Strike > 0f)
            {
                Strike -= dt;
                float k = 1f - Mathf.Clamp01(Strike / 0.42f);
                float w = k < 0.35f ? QuadrupedRig.SmoothStep(k / 0.35f, 0f, 1f) : 1f;
                if (StrikeIndex == 0) aR = new Vector3(Mathf.Lerp(-130f, -20f, w), Mathf.Lerp(40f, -30f, w), -20f);
                else if (StrikeIndex == 1) aR = new Vector3(-80f, Mathf.Lerp(-60f, 60f, w), -30f);
                else aR = new Vector3(Mathf.Lerp(-190f, -10f, w), 0f, -5f);
                fR = -10f; aL = new Vector3(-30f, 0f, 20f); fL = -40f;
            }
            else if (Cast > 0f) { Cast -= dt; aR = new Vector3(-95f, 0f, -5f); aL = new Vector3(-80f, 0f, 5f); fR = -5f; fL = -5f; }
            else if (Throw > 0f)
            {
                Throw -= dt;
                aR = new Vector3(Mathf.Lerp(-170f, -40f, QuadrupedRig.SmoothStep(1f - Mathf.Clamp01(Throw / 0.4f), 0f, 1f)), 0f, -10f);
                fR = -5f;
            }
            P.Set("armL", aL.x, aL.y, aL.z);
            P.Set("armR", aR.x, aR.y, aR.z);
            P.Set("forearmL", fL);
            P.Set("forearmR", fR);

            // Root: dodge roll and defeat fall.
            Quaternion rootQ = BonePoser.Euler(0f, 0f, _bank);
            float ry = 0f, dodgeDrop = 0f;
            if (Dodge > 0f)
            {
                Dodge -= dt;
                float k = 1f - Mathf.Clamp01(Dodge / 0.3f);
                rootQ = BonePoser.Euler(k * 360f, 0f, 0f);
                ry = Mathf.Sin(k * Mathf.PI) * 0.45f;
                dodgeDrop = Mathf.Sin(k * Mathf.PI) * 0.35f;
            }
            P.Offset("hips", swayX, hipsY - dodgeDrop, 0f);
            if (_defeat > 0f)
            {
                rootQ = Quaternion.Slerp(rootQ, BonePoser.Euler(-80f, 0f, 0f), _defeat);
                ry += 0.25f * _defeat;
            }
            _root.localRotation = rootQ;
            _root.localPosition = new Vector3(0f, ry, 0f);
            _model.SetGlow(0.6f + Mathf.Sin(t * 3f) * 0.08f + (Cast > 0f ? 1f : 0f) + (Throw > 0f ? 0.6f : 0f));
        }
    }
}
