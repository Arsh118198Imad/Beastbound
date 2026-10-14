using Beastbound.Controls;
using Beastbound.Core;
using UnityEngine;

namespace Beastbound.CameraSystem
{
    /// <summary>
    /// Orbit follow camera: mouse/stick look with adjustable sensitivity and inversion, smooth follow,
    /// collision so walls never block the view, scroll zoom, a slight FOV kick when running, screen shake,
    /// and a slow cinematic orbit for the title screen.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ThirdPersonCamera : MonoBehaviour
    {
        public enum Mode { Gameplay, TitleOrbit }

        [Header("Framing")]
        public float distance = 5.2f;
        public float minDistance = 2.2f;
        public float maxDistance = 9f;
        public float pivotHeight = 1.55f;
        public float shoulderOffset = 0.35f;
        public float minPitch = -35f;
        public float maxPitch = 70f;

        [Header("Feel")]
        [Tooltip("Degrees of rotation per mouse unit. Overridden by Settings.")]
        public float mouseSensitivity = 2.2f;
        [Tooltip("Degrees per second at full stick deflection.")]
        public float stickSpeed = 190f;
        public bool invertY;
        public float followSmoothing = 14f;
        public float collisionRadius = 0.25f;
        public float baseFov = 60f;

        private Camera _cam;
        private Transform _target;
        private float _yaw;
        private float _pitch = 14f;
        private float _currentDistance;
        private Vector3 _smoothedPivot;
        private float _shakeAmount;
        private float _shakeUntil;
        private float _orbitAngle;
        private Mode _mode = Mode.TitleOrbit;

        public Camera Camera => _cam;
        public Mode CurrentMode => _mode;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 900f;
            _cam.fieldOfView = baseFov;
            _currentDistance = distance;
        }

        public void SetTarget(Transform target, bool snap)
        {
            _target = target;
            if (target != null && snap)
            {
                _yaw = target.eulerAngles.y;
                _pitch = 14f;
                _smoothedPivot = target.position + Vector3.up * pivotHeight;
                _currentDistance = distance;
                UpdateGameplay(0f, true);
            }
        }

        public void SetMode(Mode mode)
        {
            _mode = mode;
            if (mode == Mode.Gameplay && _target != null)
            {
                _smoothedPivot = _target.position + Vector3.up * pivotHeight;
            }
        }

        public void ApplySettings(float sensitivity, bool invert, float fov)
        {
            mouseSensitivity = Mathf.Clamp(sensitivity, 0.1f, 10f);
            invertY = invert;
            baseFov = Mathf.Clamp(fov, 50f, 100f);
        }

        public void Shake(float amount, float duration)
        {
            _shakeAmount = Mathf.Max(_shakeAmount, amount);
            _shakeUntil = Mathf.Max(_shakeUntil, Time.unscaledTime + duration);
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (_mode == Mode.TitleOrbit) UpdateTitleOrbit(dt);
            else UpdateGameplay(dt, false);
        }

        private void UpdateTitleOrbit(float dt)
        {
            _orbitAngle += dt * 4f;
            float rad = _orbitAngle * Mathf.Deg2Rad;
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            Vector3 focus = world != null ? world.PlayerSpawn + new Vector3(15f, 0f, 15f) : Vector3.zero;
            Vector3 pos = focus + new Vector3(Mathf.Cos(rad) * 55f, 0f, Mathf.Sin(rad) * 55f);
            float ground = world != null ? world.GroundHeight(pos.x, pos.z) : 0f;
            pos.y = Mathf.Max(ground, 0f) + 16f;
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation((focus + Vector3.up * 4f) - pos, Vector3.up);
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, 55f, 1f - Mathf.Exp(-3f * dt));
        }

        private void UpdateGameplay(float dt, bool snap)
        {
            if (_target == null) return;

            // Look input (mouse already frame-independent; stick scaled by time)
            Vector2 mouse = GameInput.LookMouse;
            Vector2 stick = GameInput.LookStick;
            float ySign = invertY ? 1f : -1f;
            _yaw += mouse.x * mouseSensitivity + stick.x * stickSpeed * (mouseSensitivity / 2.2f) * dt;
            _pitch += ySign * (mouse.y * mouseSensitivity + stick.y * stickSpeed * 0.7f * (mouseSensitivity / 2.2f) * dt);
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            float zoom = GameInput.Zoom;
            if (Mathf.Abs(zoom) > 0f) distance = Mathf.Clamp(distance - zoom * 0.7f, minDistance, maxDistance);

            Vector3 pivotTarget = _target.position + Vector3.up * pivotHeight;
            _smoothedPivot = snap ? pivotTarget : Vector3.Lerp(_smoothedPivot, pivotTarget, 1f - Mathf.Exp(-followSmoothing * dt));

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = _smoothedPivot + rot * Vector3.right * shoulderOffset;
            Vector3 back = rot * Vector3.back;

            // Collision: pull the camera in front of anything between it and the player.
            float wanted = distance;
            if (Physics.SphereCast(pivot, collisionRadius, back, out RaycastHit hit, wanted, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                wanted = Mathf.Max(0.6f, hit.distance - 0.05f);
            }
            // Pull in fast when blocked, ease out slowly when clear (prevents popping).
            float lerpSpeed = wanted < _currentDistance ? 25f : 4f;
            _currentDistance = snap ? wanted : Mathf.Lerp(_currentDistance, wanted, 1f - Mathf.Exp(-lerpSpeed * dt));

            Vector3 pos = pivot + back * _currentDistance;

            // Keep the camera above the terrain surface.
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world != null)
            {
                float ground = Mathf.Max(world.GroundHeight(pos.x, pos.z), world.WaterLevel) + 0.35f;
                if (pos.y < ground) pos.y = ground;
            }

            if (Time.unscaledTime < _shakeUntil)
            {
                pos += Random.insideUnitSphere * _shakeAmount * 0.35f;
            }
            else
            {
                _shakeAmount = 0f;
            }

            transform.position = pos;
            Vector3 look = pivot - pos;
            transform.rotation = Quaternion.LookRotation(look.sqrMagnitude > 0.0001f ? look : -back, Vector3.up);

            // FOV kick while sprinting
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            float fovTarget = baseFov + (player != null && player.IsRunning ? 6f : 0f);
            _cam.fieldOfView = snap ? fovTarget : Mathf.Lerp(_cam.fieldOfView, fovTarget, 1f - Mathf.Exp(-5f * dt));
        }
    }
}
