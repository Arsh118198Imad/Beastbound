using UnityEngine;
using UnityEngine.Rendering;

namespace Beastbound.World
{
    /// <summary>
    /// Hand-placed landmarks of Sunmeadow Vale. Terrain shaping, paths, vegetation and spawns all read
    /// from here, so moving the lake or the ruins is a one-line change.
    /// </summary>
    public static class ValeLayout
    {
        public const float TerrainSize = 260f;
        public const float PlayableRadius = 92f;
        public const float WaterLevel = 0f;

        public static readonly Vector2 Waystone = new Vector2(-6f, -10f);
        public static readonly Vector2 PlayerSpawn = new Vector2(-6f, 2.5f); // just outside the stone circle
        public static readonly Vector2 LakeCenter = new Vector2(40f, 28f);
        public const float LakeRadius = 19f;
        public static readonly Vector2 Ruins = new Vector2(-50f, -42f);

        public static readonly Vector2[] Stream =
        {
            new Vector2(-95f, 62f), new Vector2(-70f, 55f), new Vector2(-48f, 60f), new Vector2(-25f, 50f),
            new Vector2(-5f, 52f), new Vector2(14f, 40f), new Vector2(26f, 32f)
        };

        public static readonly Vector2[][] Paths =
        {
            new[] { Waystone, new Vector2(4f, 2f), new Vector2(14f, 12f), new Vector2(22f, 14f) },
            new[] { Waystone, new Vector2(-22f, -22f), new Vector2(-38f, -30f), Ruins },
            new[] { Waystone, new Vector2(12f, -22f), new Vector2(30f, -34f), new Vector2(48f, -36f) },
            new[] { new Vector2(4f, 2f), new Vector2(-10f, 22f), new Vector2(-24f, 34f) }
        };
    }

    /// <summary>
    /// Heightfield for the vale: procedural height function, exact height queries matching the rendered
    /// mesh, mesh generation and a painted colour texture. No Unity Terrain component is used, which keeps
    /// the world working identically in the Built-in pipeline and URP.
    /// </summary>
    public class TerrainField
    {
        public readonly int Resolution;   // vertices per side
        public readonly float Size;
        public readonly float CellSize;
        public readonly float HalfSize;
        private readonly float[] _heights;
        private readonly Noise _noise;

        public TerrainField(int seed, int resolution = 177, float size = ValeLayout.TerrainSize)
        {
            Resolution = Mathf.Clamp(resolution, 16, 255);
            Size = size;
            HalfSize = size * 0.5f;
            CellSize = size / (Resolution - 1);
            _noise = new Noise(seed, 16);
            _heights = new float[Resolution * Resolution];
            for (int z = 0; z < Resolution; z++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    float wx = -HalfSize + x * CellSize;
                    float wz = -HalfSize + z * CellSize;
                    _heights[z * Resolution + x] = ComputeHeight(wx, wz);
                }
            }
        }

        public Noise Noise => _noise;

        // ------------------------------------------------------------------ Shaping

        private float ComputeHeight(float x, float z)
        {
            var p = new Vector2(x, z);
            float d = p.magnitude;

            // Rolling meadow, kept above water by a soft floor.
            float h = 1.4f + (_noise.Fbm(x * 0.011f, z * 0.011f, 4, 0) - 0.5f) * 8f;
            h += (_noise.Fbm(x * 0.05f, z * 0.05f, 2, 6) - 0.5f) * 1.0f;
            h = Noise.SmoothMax(h, 0.55f, 0.6f);

            // A couple of grassy knolls for view points.
            h += Bump(p, new Vector2(52f, -30f), 16f, 5f);
            h += Bump(p, new Vector2(-28f, 36f), 14f, 3.5f);

            // Mountain ring that frames the valley and forms the play boundary.
            float ring = Noise.SmoothStep(80f, 128f, d + (_noise.Sample(x * 0.02f, z * 0.02f, 8) - 0.5f) * 18f);
            float mountains = 16f + _noise.Ridged(x * 0.012f, z * 0.012f, 4, 9) * 46f;
            h += ring * mountains;

            // Lake (shallow enough to wade: the player is slowed, not drowned).
            float lakeDist = Vector2.Distance(p, ValeLayout.LakeCenter) + (_noise.Sample(x * 0.06f, z * 0.06f, 12) - 0.5f) * 7f;
            float lake = 1f - Noise.SmoothStep(ValeLayout.LakeRadius - 8f, ValeLayout.LakeRadius + 6f, lakeDist);
            h = Mathf.Lerp(h, -1.15f, lake);

            // Stream feeding the lake.
            float streamDist = Noise.DistanceToPolyline(p, ValeLayout.Stream) + (_noise.Sample(x * 0.08f, z * 0.08f, 13) - 0.5f) * 2f;
            float stream = (1f - Noise.SmoothStep(1.6f, 5.5f, streamDist)) * (1f - ring);
            h = Mathf.Lerp(h, -0.55f, stream);

            // Flat clearing around the Waystone and the ruins.
            h = Mathf.Lerp(h, 1.3f, 1f - Noise.SmoothStep(7f, 16f, Vector2.Distance(p, ValeLayout.Waystone)));
            h = Mathf.Lerp(h, 1.6f, 1f - Noise.SmoothStep(8f, 18f, Vector2.Distance(p, ValeLayout.Ruins)));
            return h;
        }

        private static float Bump(Vector2 p, Vector2 c, float radius, float height)
        {
            float t = 1f - Mathf.Clamp01(Vector2.Distance(p, c) / radius);
            return t * t * (3f - 2f * t) * height;
        }

        /// <summary>0..1 how strongly a point is on a dirt path.</summary>
        public float PathMask(float x, float z)
        {
            var p = new Vector2(x, z);
            float best = float.MaxValue;
            foreach (var path in ValeLayout.Paths) best = Mathf.Min(best, Noise.DistanceToPolyline(p, path));
            best += (_noise.Sample(x * 0.3f, z * 0.3f, 14) - 0.5f) * 0.8f;
            return 1f - Noise.SmoothStep(0.9f, 2.0f, best);
        }

        /// <summary>0..1 forest density used for trees and darker ground.</summary>
        public float ForestMask(float x, float z)
        {
            float n = _noise.Fbm(x * 0.018f, z * 0.018f, 3, 3);
            return Noise.SmoothStep(0.5f, 0.66f, n);
        }

        // ------------------------------------------------------------------ Queries

        /// <summary>Height at world XZ, exactly matching the rendered triangles.</summary>
        public float HeightAt(float x, float z)
        {
            float fx = (x + HalfSize) / CellSize;
            float fz = (z + HalfSize) / CellSize;
            fx = Mathf.Clamp(fx, 0f, Resolution - 1.0001f);
            fz = Mathf.Clamp(fz, 0f, Resolution - 1.0001f);
            int ix = (int)fx;
            int iz = (int)fz;
            float tx = fx - ix;
            float tz = fz - iz;
            float h00 = _heights[iz * Resolution + ix];
            float h10 = _heights[iz * Resolution + ix + 1];
            float h01 = _heights[(iz + 1) * Resolution + ix];
            float h11 = _heights[(iz + 1) * Resolution + ix + 1];
            // Quads are split along the (0,0)-(1,1) diagonal (see BuildMesh).
            if (tx >= tz) return h00 + (h10 - h00) * tx + (h11 - h10) * tz;
            return h00 + (h11 - h01) * tx + (h01 - h00) * tz;
        }

        public Vector3 NormalAt(float x, float z)
        {
            float e = CellSize;
            float hL = HeightAt(x - e, z), hR = HeightAt(x + e, z);
            float hD = HeightAt(x, z - e), hU = HeightAt(x, z + e);
            return new Vector3(hL - hR, 2f * e, hD - hU).normalized;
        }

        /// <summary>Slope in degrees (0 = flat).</summary>
        public float SlopeAt(float x, float z) => Vector3.Angle(NormalAt(x, z), Vector3.up);

        // ------------------------------------------------------------------ Mesh + texture

        public Mesh BuildMesh()
        {
            int n = Resolution;
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    verts[i] = new Vector3(-HalfSize + x * CellSize, _heights[i], -HalfSize + z * CellSize);
                    uvs[i] = new Vector2(x / (float)(n - 1), z / (float)(n - 1));
                }
            }
            var tris = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int z = 0; z < n - 1; z++)
            {
                for (int x = 0; x < n - 1; x++)
                {
                    int i00 = z * n + x, i10 = i00 + 1, i01 = i00 + n, i11 = i01 + 1;
                    // Two clockwise triangles (Unity front faces) sharing the (0,0)-(1,1) diagonal.
                    tris[t++] = i00; tris[t++] = i11; tris[t++] = i10;
                    tris[t++] = i00; tris[t++] = i01; tris[t++] = i11;
                }
            }
            var mesh = new Mesh { name = "ValeTerrain" };
            if (verts.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Paints sand, grass, forest floor, paths, rock and snow into a colour map.</summary>
        public Texture2D BuildColorTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "ValeTerrainColor",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            var pixels = new Color32[size * size];

            Color sandWet = new Color(0.45f, 0.42f, 0.32f);
            Color lakeBed = new Color(0.22f, 0.3f, 0.26f);
            Color sand = new Color(0.86f, 0.79f, 0.6f);
            Color grassLush = new Color(0.33f, 0.58f, 0.2f);
            Color grassSun = new Color(0.58f, 0.7f, 0.27f);
            Color forestFloor = new Color(0.24f, 0.38f, 0.16f);
            Color dirt = new Color(0.62f, 0.5f, 0.34f);
            Color rock = new Color(0.47f, 0.47f, 0.5f);
            Color rockDark = new Color(0.33f, 0.33f, 0.37f);
            Color snow = new Color(0.94f, 0.96f, 1f);

            for (int py = 0; py < size; py++)
            {
                float z = -HalfSize + (py + 0.5f) / size * Size;
                for (int px = 0; px < size; px++)
                {
                    float x = -HalfSize + (px + 0.5f) / size * Size;
                    float h = HeightAt(x, z);
                    Vector3 n = NormalAt(x, z);
                    float detail = _noise.Sample(x * 0.35f, z * 0.35f, 15);
                    float patches = _noise.Fbm(x * 0.04f, z * 0.04f, 2, 4);
                    Color c;

                    if (h < ValeLayout.WaterLevel - 0.02f)
                    {
                        float depth = Mathf.Clamp01(-h / 1.2f);
                        c = Color.Lerp(sandWet, lakeBed, depth);
                    }
                    else if (h < ValeLayout.WaterLevel + 0.45f)
                    {
                        c = Color.Lerp(sand, grassSun, Noise.SmoothStep(0.25f, 0.45f, h));
                    }
                    else
                    {
                        c = Color.Lerp(grassLush, grassSun, Noise.SmoothStep(0.35f, 0.7f, patches));
                        c = Color.Lerp(c, forestFloor, ForestMask(x, z) * 0.75f);
                        c = Color.Lerp(c, dirt, PathMask(x, z) * 0.9f);
                    }

                    float steep = Noise.SmoothStep(0.86f, 0.7f, n.y);
                    float high = Noise.SmoothStep(14f, 24f, h);
                    c = Color.Lerp(c, Color.Lerp(rock, rockDark, detail), Mathf.Max(steep, high * 0.85f));
                    float snowAmount = Noise.SmoothStep(36f, 44f, h + detail * 4f) * Noise.SmoothStep(0.6f, 0.8f, n.y);
                    c = Color.Lerp(c, snow, snowAmount);

                    c *= 0.9f + detail * 0.2f;
                    c.a = 1f;
                    pixels[py * size + px] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }
    }
}
