using UnityEngine;

namespace Beastbound.World
{
    /// <summary>Seeded fractal noise helpers built on Mathf.PerlinNoise. Same seed = same world.</summary>
    public class Noise
    {
        private readonly Vector2[] _offsets;

        public Noise(int seed, int layers = 8)
        {
            var rng = new System.Random(seed);
            _offsets = new Vector2[Mathf.Max(1, layers)];
            for (int i = 0; i < _offsets.Length; i++)
            {
                _offsets[i] = new Vector2((float)rng.NextDouble() * 10000f - 5000f, (float)rng.NextDouble() * 10000f - 5000f);
            }
        }

        /// <summary>Single Perlin sample in [0,1] using offset layer <paramref name="layer"/>.</summary>
        public float Sample(float x, float z, int layer = 0)
        {
            var o = _offsets[layer % _offsets.Length];
            return Mathf.Clamp01(Mathf.PerlinNoise(x + o.x, z + o.y));
        }

        /// <summary>Fractal Brownian motion in roughly [0,1].</summary>
        public float Fbm(float x, float z, int octaves, int layer = 0, float lacunarity = 2f, float gain = 0.5f)
        {
            float amp = 1f, freq = 1f, sum = 0f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Sample(x * freq, z * freq, layer + i) * amp;
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>Ridged noise in [0,1] (sharp crests) for mountains.</summary>
        public float Ridged(float x, float z, int octaves, int layer = 0)
        {
            float amp = 1f, freq = 1f, sum = 0f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Sample(x * freq, z * freq, layer + i) * 2f - 1f);
                sum += n * n * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Smooth maximum: like Mathf.Max but without a hard crease.</summary>
        public static float SmoothMax(float a, float b, float k)
        {
            return (a + b + Mathf.Sqrt((a - b) * (a - b) + k)) * 0.5f;
        }

        /// <summary>Distance from p to the closest point on a polyline (XZ plane).</summary>
        public static float DistanceToPolyline(Vector2 p, Vector2[] points)
        {
            float best = float.MaxValue;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 a = points[i], b = points[i + 1];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                float d = (a + ab * t - p).magnitude;
                if (d < best) best = d;
            }
            return best;
        }
    }
}
