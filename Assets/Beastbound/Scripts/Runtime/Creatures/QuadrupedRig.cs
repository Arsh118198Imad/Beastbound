using Beastbound.Core;
using UnityEngine;

namespace Beastbound.Creatures
{
    /// <summary>Per-model tuning for the skinned creature models and their procedural animation.</summary>
    public class QuadrupedConfig
    {
        public string ModelName;
        public float Scale = 1f;       // size of the imported model
        public float LieDrop = 0.6f;   // how far the body sinks when lying down
        public float Bob = 0.05f;      // gait bounce height
        public bool Hop;               // hopping (Pebbloom) instead of trotting
        public float StrideLen = 1.5f; // metres per gait cycle at walking speed
        public float LieHead = 1f;     // how far the head lowers when resting

        public static readonly QuadrupedConfig Thalorix = new QuadrupedConfig
        {
            ModelName = "thalorix", Scale = 1.2f, LieDrop = 0.6f, Bob = 0.05f, Hop = false, StrideLen = 2.3f, LieHead = 1f
        };

        public static readonly QuadrupedConfig Pebbloom = new QuadrupedConfig
        {
            ModelName = "pebbloom", Scale = 1f, LieDrop = 0.2f, Bob = 0.08f, Hop = true, StrideLen = 0.95f, LieHead = 0.3f
        };

        public static QuadrupedConfig For(CreatureModelKind kind) =>
            kind == CreatureModelKind.Thalorix ? Thalorix : Pebbloom;
    }

    /// <summary>
    /// Procedural animation for the skinned quadrupeds (Blender rigs with named bones): stride-locked four-beat
    /// walk blending into a trot, spine bend into turns, breathing, look-around, ears, tail follow-through,
    /// lie-down for sleep/exhaustion, wind-up crouch, lunge, hurt squash, binding shake and glow pulse.
    /// A port of QuadrupedAnimator in web/js/creatures.js, using the same pose numbers.
    /// </summary>
    public class QuadrupedRig
    {
        private static readonly string[] BoneNames =
        {
            "hips", "spine", "chest", "neck", "head", "earL", "earR", "eyeL", "eyeR", "tail1", "tail2", "tail3", "bloom",
            "legFL1", "legFL2", "legFL3", "legFR1", "legFR2", "legFR3", "legHL1", "legHL2", "legHL3", "legHR1", "legHR2", "legHR3"
        };
        private static readonly string[] LegOrder = { "FL", "HR", "FR", "HL" };

        private readonly Transform _root;      // moved/rotated/scaled as a whole
        private readonly ModelInstance _model;
        private readonly BonePoser _poser;
        private readonly QuadrupedConfig _cfg;
        private readonly float _s;

        private float _smoothed, _phase, _turnS, _blend, _windup, _alert, _glow;
        private float _hurt, _lunge, _happy;

        public QuadrupedRig(Transform root, ModelInstance model, QuadrupedConfig cfg)
        {
            _root = root;
            _model = model;
            _cfg = cfg;
            _s = cfg.Scale;
            _poser = new BonePoser(root, model.Bones, BoneNames);
            _phase = Random.value * 10f;
        }

        public void PlayHurt() => _hurt = 0.25f;
        public void PlayLunge() => _lunge = 0.3f;
        public void PlayHappy() => _happy = 0.8f;
        public void Glow(float amount) => _glow = Mathf.Max(_glow, amount);

        /// <param name="speed">Planar speed in m/s.</param>
        /// <param name="turn">Turn rate in rad/s, positive = turning left (web convention).</param>
        public void Update(float dt, float t, float speed, float maxSpeed, float turn, CreaturePose pose)
        {
            _smoothed = Mathf.Lerp(_smoothed, speed, Damp(8f, dt));
            float move = Mathf.Clamp01(_smoothed / Mathf.Max(0.1f, maxSpeed));
            float walk = Mathf.Clamp01(_smoothed / 0.8f);
            // Stride locked to ground distance (metres per gait cycle), so paws plant instead of sliding.
            float strideLen = _cfg.StrideLen * (1f + move * 0.45f);
            if (_smoothed > 0.05f) _phase += _smoothed / strideLen * Mathf.PI * 2f * dt;
            _turnS = Mathf.Lerp(_turnS, Mathf.Clamp(turn, -4f, 4f), Damp(6f, dt));
            bool sleeping = pose == CreaturePose.Sleeping, exhausted = pose == CreaturePose.Exhausted;
            bool low = sleeping || exhausted;
            _blend = Approach(_blend, low ? 1f : 0f, dt * 1.6f);
            _windup = Approach(_windup, pose == CreaturePose.Windup ? 1f : 0f, dt * 6f);
            _alert = Approach(_alert, pose == CreaturePose.Alert || pose == CreaturePose.Windup ? 1f : 0f, dt * 4f);
            float L = _blend, W = _windup, A = _alert, up = 1f - L;
            float breathe = Mathf.Sin(t * (sleeping ? 1.1f : 1.8f));

            // Root: trot bounce, lie down, crouch, lunge, hurt squash, binding shake.
            float happyHop = 0f;
            if (_happy > 0f) { _happy -= dt; happyHop = Mathf.Abs(Mathf.Sin(_happy * 10f)) * 0.22f; }
            float gait = _cfg.Hop ? Mathf.Abs(Mathf.Sin(_phase)) : Mathf.Abs(Mathf.Cos(_phase)) - 0.5f;
            float y = gait * _cfg.Bob * walk * up + happyHop - _cfg.LieDrop * L - 0.1f * W, z = -0.1f * W;
            if (_lunge > 0f) { _lunge -= dt; z += Mathf.Sin(_lunge / 0.3f * Mathf.PI) * 0.6f; }
            _root.localPosition = new Vector3(0f, y * _s, z * _s);
            float sx = 1f, sy = 1f + breathe * 0.012f, sz = 1f;
            if (_hurt > 0f) { _hurt -= dt; float k = _hurt / 0.25f; sx *= 1f + 0.07f * k; sy *= 1f - 0.09f * k; sz *= 1f + 0.05f * k; }
            _root.localScale = new Vector3(sx, sy, sz);
            if (pose == CreaturePose.Binding) _root.localRotation = BonePoser.Euler(0f, Mathf.Sin(t * 38f) * 7f, Mathf.Sin(t * 31f) * 3f);
            else if (exhausted) _root.localRotation = BonePoser.Euler(0f, 0f, 8f * L);
            else _root.localRotation = Quaternion.identity;

            // Spine: gait sway plus a C-curve bend into turns; the body rolls slightly toward the inside.
            float sway = Mathf.Sin(_phase) * 3f * walk * up;
            float bendT = _turnS * 7f * up;
            _poser.Set("hips", 0f, sway - bendT * 0.4f, -_turnS * 3f * walk);
            _poser.Set("spine", -3f * W, -sway * 0.5f + bendT * 0.5f, 0f);
            _poser.Set("chest", 4f * W + Mathf.Sin(_phase * 2f) * 1.5f * walk, -sway * 0.5f + bendT * 0.6f, 0f);
            float look = Mathf.Sin(t * 0.45f) * 22f * (1f - move) * up * (1f - A);
            float nod = Mathf.Sin(_phase * 2f + 0.6f) * 3f * walk; // head counters the body bob so the eyes stay steady
            float lh = _cfg.LieHead;
            _poser.Set("neck", -10f * A + 16f * W + (18f * L + (exhausted ? 22f * L : 0f)) * lh + breathe * 1.5f, look * 0.4f, 0f);
            _poser.Set("head", nod - 6f * A + 12f * W + 10f * L * lh, look * 0.6f, Mathf.Sin(t * 0.3f) * 3f * up + (sleeping ? 14f * L : 0f));

            // Ears: perk forward when alert, pin back during a wind-up, droop when resting.
            float flick = Mathf.Sin(t * 1.3f + 1f) * 4f + (Mathf.Repeat(t * 0.37f, 3f) < 0.12f ? 18f : 0f);
            float earX = 12f * A - 45f * W - 30f * L + flick;
            _poser.Set("earL", earX, 0f, -14f * L);
            _poser.Set("earR", earX, 0f, 14f * L);

            // Tail: wag with lag down the chain; raised when alert, low when resting, swings out of turns.
            float wagSpeed = _happy > 0f ? 14f : Mathf.Lerp(2f, 7f, move);
            float wagAmp = (_happy > 0f ? 26f : Mathf.Lerp(9f, 15f, move)) * (1f - L * 0.7f);
            _poser.Set("tail1", 16f * A - 14f * L + 8f * W, Mathf.Sin(t * wagSpeed) * wagAmp * 0.5f - _turnS * 10f, 0f);
            _poser.Set("tail2", 6f * A - 10f * L, Mathf.Sin(t * wagSpeed - 0.6f) * wagAmp * 0.6f, 0f);
            _poser.Set("tail3", -6f * L, Mathf.Sin(t * wagSpeed - 1.2f) * wagAmp * 0.8f, 0f);

            // Legs: four-beat walk (hind, fore, hind, fore) blending into a diagonal trot at speed.
            // Negative x swings a leg forward; lift bends the lower joints.
            float amp = Mathf.Lerp(22f, 34f, move) * walk * up;
            float trot = SmoothStep(move, 0.35f, 0.6f);
            foreach (var k in LegOrder)
            {
                float offset;
                switch (k)
                {
                    case "HL": offset = Mathf.Lerp(0f, Mathf.PI, trot); break;
                    case "FL": offset = Mathf.Lerp(Mathf.PI * 0.5f, 0f, trot); break;
                    case "HR": offset = Mathf.Lerp(Mathf.PI, 0f, trot); break;
                    default: offset = Mathf.Lerp(Mathf.PI * 1.5f, Mathf.PI, trot); break; // FR
                }
                float ph = _phase + offset;
                float swing = -Mathf.Sin(ph) * amp;
                float lift = Mathf.Max(0f, Mathf.Cos(ph)) * walk * up;
                if (k[0] == 'F')
                {
                    _poser.Set("leg" + k + "1", swing - 74f * L - 10f * W);
                    _poser.Set("leg" + k + "2", lift * 18f + 8f * L);
                    _poser.Set("leg" + k + "3", lift * 55f + 4f * L);
                }
                else
                {
                    _poser.Set("leg" + k + "1", swing - 62f * L - 16f * W);
                    _poser.Set("leg" + k + "2", lift * 40f + 128f * L + 26f * W);
                    _poser.Set("leg" + k + "3", -lift * 30f - 58f * L - 10f * W);
                }
            }

            // Eyes close for sleep and blinks; the tail-flower closes while resting; veins and crystals pulse.
            float blink = Mathf.Repeat(t + _phase * 0.01f, 4.3f) < 0.13f ? 0.1f : 1f;
            float open = sleeping ? 0.04f : exhausted ? 0.45f : blink;
            _poser.Scale("eyeL", open);
            _poser.Scale("eyeR", open);
            _poser.Scale("bloom", Mathf.Lerp(1f, 0.6f, L));
            _glow = Mathf.Max(0f, _glow - dt * 1.5f);
            float pulse = 0.85f + Mathf.Sin(t * 1.6f) * 0.15f + _glow * 1.5f;
            if (sleeping) pulse *= 0.35f;
            _model.SetGlow(pulse);
        }

        internal static float Damp(float k, float dt) => 1f - Mathf.Exp(-k * dt);
        internal static float Approach(float v, float target, float maxStep) => v + Mathf.Clamp(target - v, -maxStep, maxStep);

        /// <summary>three.js MathUtils.smoothstep(x, min, max).</summary>
        internal static float SmoothStep(float x, float min, float max)
        {
            if (x <= min) return 0f;
            if (x >= max) return 1f;
            x = (x - min) / (max - min);
            return x * x * (3f - 2f * x);
        }
    }
}
