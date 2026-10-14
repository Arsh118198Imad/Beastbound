using UnityEngine;
using UnityEngine.Rendering;

namespace Beastbound.World
{
    /// <summary>
    /// Drives sun/moon direction, light colour, ambient light, fog and sky exposure from a 24h clock.
    /// Nights are blue and readable rather than pitch black. Creatures read <see cref="IsNight"/> to sleep.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        [Tooltip("Real-time minutes for a full 24h in-game day.")]
        public float dayLengthMinutes = 24f;
        [Range(0f, 24f)] public float hour = 9.5f;
        public bool paused;

        private Light _sun;
        private Material _skybox;
        private ReflectionProbe _probe;
        private float _nextProbeRefresh;

        private Gradient _sunColor;
        private Gradient _skyAmbient;
        private Gradient _equatorAmbient;
        private Gradient _groundAmbient;
        private Gradient _fogColor;

        /// <summary>0 at night, 1 in full daylight.</summary>
        public float Daylight { get; private set; } = 1f;
        public bool IsNight => hour < 5.5f || hour > 20f;
        public Light Sun => _sun;

        public void Initialize(Light sun)
        {
            _sun = sun;
            if (_sun != null)
            {
                _sun.type = LightType.Directional;
                _sun.shadows = LightShadows.Soft;
                _sun.shadowStrength = 0.85f;
                RenderSettings.sun = _sun;
            }

            // Clone the skybox so we never modify the shared built-in asset.
            if (RenderSettings.skybox != null)
            {
                _skybox = new Material(RenderSettings.skybox) { name = "BeastboundSky" };
            }
            else
            {
                var shader = Shader.Find("Skybox/Procedural");
                if (shader != null) _skybox = new Material(shader) { name = "BeastboundSky" };
            }
            if (_skybox != null)
            {
                RenderSettings.skybox = _skybox;
                if (_skybox.HasProperty("_SkyTint")) _skybox.SetColor("_SkyTint", new Color(0.45f, 0.62f, 0.85f));
                if (_skybox.HasProperty("_AtmosphereThickness")) _skybox.SetFloat("_AtmosphereThickness", 0.9f);
                if (_skybox.HasProperty("_SunSize")) _skybox.SetFloat("_SunSize", 0.045f);
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0042f;

            BuildGradients();
            CreateReflectionProbe();
            Apply();
        }

        private void CreateReflectionProbe()
        {
            // Sky-only realtime probe so shiny surfaces (water, crystals) reflect the current sky without baking.
            var go = new GameObject("SkyReflectionProbe");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 10f, 0f);
            _probe = go.AddComponent<ReflectionProbe>();
            _probe.mode = ReflectionProbeMode.Realtime;
            _probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            _probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            _probe.size = new Vector3(1000f, 400f, 1000f);
            _probe.cullingMask = 0; // sky only: cheap
            _probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            _probe.resolution = 64;
            _probe.hdr = true;
        }

        private void BuildGradients()
        {
            // Keys are hour/24.
            _sunColor = MakeGradient(
                (0f, new Color(0.35f, 0.45f, 0.75f)), (0.23f, new Color(0.45f, 0.45f, 0.7f)),
                (0.27f, new Color(1f, 0.6f, 0.35f)), (0.35f, new Color(1f, 0.9f, 0.75f)),
                (0.5f, new Color(1f, 0.97f, 0.9f)), (0.72f, new Color(1f, 0.85f, 0.65f)),
                (0.8f, new Color(1f, 0.5f, 0.3f)), (0.86f, new Color(0.4f, 0.42f, 0.7f)));
            _skyAmbient = MakeGradient(
                (0f, new Color(0.12f, 0.16f, 0.3f)), (0.25f, new Color(0.35f, 0.35f, 0.5f)),
                (0.35f, new Color(0.55f, 0.68f, 0.85f)), (0.75f, new Color(0.55f, 0.6f, 0.78f)),
                (0.82f, new Color(0.45f, 0.35f, 0.45f)), (0.9f, new Color(0.12f, 0.16f, 0.3f)));
            _equatorAmbient = MakeGradient(
                (0f, new Color(0.1f, 0.13f, 0.22f)), (0.27f, new Color(0.55f, 0.42f, 0.38f)),
                (0.38f, new Color(0.5f, 0.55f, 0.55f)), (0.75f, new Color(0.55f, 0.5f, 0.45f)),
                (0.82f, new Color(0.5f, 0.32f, 0.3f)), (0.9f, new Color(0.1f, 0.13f, 0.22f)));
            _groundAmbient = MakeGradient(
                (0f, new Color(0.06f, 0.07f, 0.1f)), (0.3f, new Color(0.25f, 0.24f, 0.18f)),
                (0.5f, new Color(0.3f, 0.3f, 0.22f)), (0.8f, new Color(0.22f, 0.18f, 0.15f)),
                (0.9f, new Color(0.06f, 0.07f, 0.1f)));
            _fogColor = MakeGradient(
                (0f, new Color(0.1f, 0.13f, 0.22f)), (0.25f, new Color(0.6f, 0.5f, 0.55f)),
                (0.35f, new Color(0.7f, 0.8f, 0.9f)), (0.72f, new Color(0.72f, 0.78f, 0.85f)),
                (0.8f, new Color(0.85f, 0.55f, 0.45f)), (0.88f, new Color(0.1f, 0.13f, 0.22f)));
        }

        private static Gradient MakeGradient(params (float t, Color c)[] keys)
        {
            // Unity gradients support at most 8 colour keys.
            int count = Mathf.Min(keys.Length, 8);
            var colorKeys = new GradientColorKey[count];
            for (int i = 0; i < count; i++) colorKeys[i] = new GradientColorKey(keys[i].c, keys[i].t);
            var g = new Gradient();
            g.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        public void SetHour(float newHour)
        {
            hour = Mathf.Repeat(newHour, 24f);
            Apply();
            _nextProbeRefresh = 0f;
        }

        private void Update()
        {
            if (!paused && dayLengthMinutes > 0.01f)
            {
                hour = Mathf.Repeat(hour + Time.deltaTime * 24f / (dayLengthMinutes * 60f), 24f);
            }
            Apply();
        }

        private void Apply()
        {
            float t = hour / 24f;
            // Sun elevation: -90 at midnight, +90 at noon (sunrise 6:00, sunset 18:00 nominal; we stretch day a bit).
            float sunAngle = (hour - 6f) / 12f * 180f;
            float elevation = Mathf.Sin(sunAngle * Mathf.Deg2Rad);
            Daylight = Mathf.Clamp01(elevation * 2.2f + 0.25f);

            if (_sun != null)
            {
                bool moon = elevation < -0.05f;
                // Moon uses the opposite direction so night still has a soft key light and shadows.
                float angle = moon ? Mathf.Repeat(sunAngle + 180f, 360f) : sunAngle;
                _sun.transform.rotation = Quaternion.Euler(Mathf.Clamp(angle, 8f, 172f), -35f, 0f);
                _sun.color = _sunColor.Evaluate(t);
                _sun.intensity = moon ? 0.28f : Mathf.Lerp(0.35f, 1.25f, Mathf.Clamp01(elevation * 1.6f));
                _sun.shadowStrength = moon ? 0.5f : 0.85f;
            }

            RenderSettings.ambientSkyColor = _skyAmbient.Evaluate(t);
            RenderSettings.ambientEquatorColor = _equatorAmbient.Evaluate(t);
            RenderSettings.ambientGroundColor = _groundAmbient.Evaluate(t);
            RenderSettings.fogColor = _fogColor.Evaluate(t);

            if (_skybox != null && _skybox.HasProperty("_Exposure"))
            {
                _skybox.SetFloat("_Exposure", Mathf.Lerp(0.18f, 1.25f, Daylight));
            }

            if (_probe != null && Time.unscaledTime >= _nextProbeRefresh)
            {
                _nextProbeRefresh = Time.unscaledTime + 20f;
                _probe.RenderProbe();
            }
        }
    }
}
