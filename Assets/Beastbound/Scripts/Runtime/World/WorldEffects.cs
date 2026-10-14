using System.Collections.Generic;
using Beastbound.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Beastbound.World
{
    /// <summary>Animated water plane: gentle vertex swell plus a scrolling ripple texture.</summary>
    public class WaterSurface : MonoBehaviour
    {
        private Mesh _mesh;
        private Vector3[] _base;
        private Vector3[] _work;
        private Material _material;
        private float _nextUpdate;
        private DayNightCycle _dayNight;

        public void Build(float size, int resolution)
        {
            resolution = Mathf.Clamp(resolution, 4, 200);
            int n = resolution + 1;
            _base = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            float half = size * 0.5f;
            float step = size / resolution;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                _base[z * n + x] = new Vector3(-half + x * step, 0f, -half + z * step);
                uvs[z * n + x] = new Vector2(x / (float)resolution, z / (float)resolution);
            }
            var tris = new int[resolution * resolution * 6];
            int t = 0;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i00 = z * n + x, i10 = i00 + 1, i01 = i00 + n, i11 = i01 + 1;
                tris[t++] = i00; tris[t++] = i11; tris[t++] = i10;
                tris[t++] = i00; tris[t++] = i01; tris[t++] = i11;
            }
            _work = (Vector3[])_base.Clone();
            _mesh = new Mesh { name = "Water" };
            _mesh.MarkDynamic();
            _mesh.vertices = _work;
            _mesh.uv = uvs;
            _mesh.triangles = tris;
            _mesh.RecalculateNormals();
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 2f, size));

            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            _material = MaterialFactory.Unique("Water", new Color(0.18f, 0.45f, 0.58f), 0.93f);
            MaterialFactory.SetTexture(_material, BuildRippleTexture());
            MaterialFactory.SetTextureScale(_material, new Vector2(size / 6f, size / 6f));
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        private static Texture2D BuildRippleTexture()
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true) { name = "WaterRipples", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                // Tileable sine interference pattern
                float u = x / (float)s * Mathf.PI * 2f, v = y / (float)s * Mathf.PI * 2f;
                float w = Mathf.Sin(u * 2f + Mathf.Sin(v * 3f)) * 0.5f + Mathf.Sin(v * 2f + Mathf.Cos(u)) * 0.5f;
                float k = Mathf.Lerp(0.82f, 1.08f, w * 0.5f + 0.5f);
                px[y * s + x] = new Color(k, k, k, 1f);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        private void Update()
        {
            if (_mesh == null) return;
            float t = Time.time;
            MaterialFactory.SetTextureOffset(_material, new Vector2(t * 0.012f, t * 0.008f));

            if (_dayNight == null) _dayNight = FindAnyObjectByType<DayNightCycle>();
            if (_dayNight != null)
            {
                // Dim reflections at night so the water does not glow with the day sky.
                MaterialFactory.SetSmoothness(_material, Mathf.Lerp(0.55f, 0.93f, _dayNight.Daylight));
            }

            if (Time.time < _nextUpdate) return; // 30 Hz swell is plenty
            _nextUpdate = Time.time + 1f / 30f;
            for (int i = 0; i < _base.Length; i++)
            {
                var p = _base[i];
                p.y = Mathf.Sin(p.x * 0.25f + t * 1.1f) * 0.035f + Mathf.Sin(p.z * 0.31f + t * 0.8f) * 0.03f;
                _work[i] = p;
            }
            _mesh.vertices = _work;
            _mesh.RecalculateNormals();
        }
    }

    /// <summary>
    /// Turns vegetation chunks on/off by distance and quality level. Grass is split into 3 density layers;
    /// lower quality settings simply show fewer layers.
    /// </summary>
    public class VegetationCuller : MonoBehaviour
    {
        public enum Kind { Props, Flora, Grass }

        private struct Entry
        {
            public Renderer Renderer;
            public Vector3 Center;
            public Kind Kind;
            public int Layer;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private float _grassDistance = 75f;
        private float _floraDistance = 90f;
        private int _grassLayers = 3;
        private float _next;

        public void Register(Renderer renderer, Vector3 center, Kind kind, int layer)
        {
            if (renderer == null) return;
            _entries.Add(new Entry { Renderer = renderer, Center = center, Kind = kind, Layer = layer });
        }

        /// <param name="quality">0 = Low, 1 = Medium, 2 = High, 3 = Ultra</param>
        public void ApplyQuality(int quality)
        {
            switch (Mathf.Clamp(quality, 0, 3))
            {
                case 0: _grassLayers = 1; _grassDistance = 45f; _floraDistance = 55f; break;
                case 1: _grassLayers = 2; _grassDistance = 60f; _floraDistance = 75f; break;
                case 2: _grassLayers = 3; _grassDistance = 80f; _floraDistance = 95f; break;
                default: _grassLayers = 3; _grassDistance = 120f; _floraDistance = 130f; break;
            }
            _next = 0f;
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.3f;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 c = cam.transform.position;
            c.y = 0f;
            const float halfDiagonal = 29f; // chunk centre to corner
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Renderer == null) continue;
                float d = Vector3.Distance(c, e.Center) - halfDiagonal;
                bool visible;
                switch (e.Kind)
                {
                    case Kind.Grass: visible = e.Layer < _grassLayers && d < _grassDistance; break;
                    case Kind.Flora: visible = d < _floraDistance; break;
                    default: visible = true; break;
                }
                if (e.Renderer.enabled != visible) e.Renderer.enabled = visible;
            }
        }
    }

    /// <summary>Slow bob and spin for floating crystals.</summary>
    public class FloatingSpin : MonoBehaviour
    {
        public float spinSpeed = 25f;
        public float bobHeight = 0.18f;
        public float bobSpeed = 1.3f;
        private Vector3 _start;

        private void Start() => _start = transform.localPosition;

        private void Update()
        {
            transform.localPosition = _start + Vector3.up * Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        }
    }

    /// <summary>
    /// A few dozen drifting motes around the camera: pollen by day, fireflies at night. Cheap life for the scene.
    /// </summary>
    public class AmbientMotes : MonoBehaviour
    {
        private const int Count = 40;
        private const float Radius = 16f;
        private readonly Transform[] _motes = new Transform[Count];
        private readonly Vector3[] _seeds = new Vector3[Count];
        private Material _dayMat, _nightMat;
        private DayNightCycle _dayNight;
        private bool _night;
        private Renderer[] _renderers;

        public void Initialize(DayNightCycle dayNight)
        {
            _dayNight = dayNight;
            _dayMat = MaterialFactory.Emissive("Pollen", new Color(1f, 0.95f, 0.7f), new Color(0.6f, 0.55f, 0.3f), 0.2f);
            _nightMat = MaterialFactory.Emissive("Firefly", new Color(0.8f, 1f, 0.5f), new Color(1.6f, 2.2f, 0.6f), 0.2f);
            var mesh = MeshKit.Primitive(PrimitiveType.Sphere);
            _renderers = new Renderer[Count];
            for (int i = 0; i < Count; i++)
            {
                var go = MeshKit.AddPart(transform, "Mote" + i, mesh, _dayMat, Vector3.zero, Vector3.one * 0.05f, Quaternion.identity, false);
                _motes[i] = go.transform;
                _renderers[i] = go.GetComponent<Renderer>();
                _seeds[i] = new Vector3(Random.value * 100f, Random.value * 100f, Random.value * 100f);
            }
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || _renderers == null) return;
            bool night = _dayNight != null && _dayNight.Daylight < 0.35f;
            if (night != _night)
            {
                _night = night;
                foreach (var r in _renderers) r.sharedMaterial = night ? _nightMat : _dayMat;
            }
            Vector3 center = cam.transform.position;
            float t = Time.time * (night ? 0.25f : 0.12f);
            for (int i = 0; i < Count; i++)
            {
                var s = _seeds[i];
                var offset = new Vector3(
                    (Mathf.PerlinNoise(s.x, t) - 0.5f) * 2f * Radius,
                    (Mathf.PerlinNoise(s.y, t) - 0.5f) * 5f,
                    (Mathf.PerlinNoise(s.z, t) - 0.5f) * 2f * Radius);
                _motes[i].position = center + offset;
                float scale = night ? 0.07f * (0.6f + 0.4f * Mathf.Sin(Time.time * 3f + s.x)) : 0.04f;
                _motes[i].localScale = Vector3.one * scale;
            }
        }
    }
}
