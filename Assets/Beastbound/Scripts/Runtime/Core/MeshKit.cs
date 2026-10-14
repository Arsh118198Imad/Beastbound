using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Beastbound.Core
{
    /// <summary>
    /// Procedural mesh helpers used for prototype art. Everything here is generated from code, so the
    /// project ships with zero third-party art. Each helper returns a plain Mesh that can later be swapped
    /// for an artist-made model without touching gameplay code.
    /// </summary>
    public static class MeshKit
    {
        private static readonly Dictionary<PrimitiveType, Mesh> PrimitiveCache = new Dictionary<PrimitiveType, Mesh>();

        /// <summary>Unity's built-in primitive meshes (sphere, capsule, cylinder, cube, quad, plane).</summary>
        public static Mesh Primitive(PrimitiveType type)
        {
            if (PrimitiveCache.TryGetValue(type, out var mesh) && mesh != null) return mesh;
            var go = GameObject.CreatePrimitive(type);
            mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            PrimitiveCache[type] = mesh;
            return mesh;
        }

        /// <summary>
        /// Adds a visual-only child (MeshFilter + MeshRenderer, no collider) to <paramref name="parent"/>.
        /// </summary>
        public static GameObject AddPart(Transform parent, string name, Mesh mesh, Material material,
            Vector3 localPosition, Vector3 localScale, Quaternion localRotation, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.layer = parent != null ? parent.gameObject.layer : 0;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        public static GameObject AddPart(Transform parent, string name, PrimitiveType type, Material material,
            Vector3 localPosition, Vector3 localScale)
        {
            return AddPart(parent, name, Primitive(type), material, localPosition, localScale, Quaternion.identity);
        }

        /// <summary>Tapered cylinder (or cone when topRadius is 0) with caps, smooth sides.</summary>
        public static Mesh Frustum(float bottomRadius, float topRadius, float height, int segments, string name = "Frustum")
        {
            segments = Mathf.Max(3, segments);
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            float slope = (bottomRadius - topRadius) / Mathf.Max(0.0001f, height);
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var n = new Vector3(dir.x, slope, dir.z).normalized;
                verts.Add(dir * bottomRadius);
                verts.Add(dir * topRadius + Vector3.up * height);
                normals.Add(n);
                normals.Add(n);
                uvs.Add(new Vector2(i / (float)segments, 0f));
                uvs.Add(new Vector2(i / (float)segments, 1f));
            }
            for (int i = 0; i < segments; i++)
            {
                int b = i * 2;
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b + 2); tris.Add(b + 1); tris.Add(b + 3);
            }

            AddCap(verts, normals, uvs, tris, 0f, bottomRadius, segments, false);
            if (topRadius > 0.0001f) AddCap(verts, normals, uvs, tris, height, topRadius, segments, true);

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddCap(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris,
            float y, float radius, int segments, bool up)
        {
            int center = verts.Count;
            verts.Add(new Vector3(0f, y, 0f));
            normals.Add(up ? Vector3.up : Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                normals.Add(up ? Vector3.up : Vector3.down);
                uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = center + 1 + i;
                int b = center + 2 + i;
                if (up) { tris.Add(center); tris.Add(b); tris.Add(a); }
                else { tris.Add(center); tris.Add(a); tris.Add(b); }
            }
        }

        /// <summary>
        /// Low-poly "gem" (two pyramids joined at the base). Used for crystals and the Waystone.
        /// </summary>
        public static Mesh Crystal(int sides = 6, float radius = 0.5f, float topHeight = 1f, float bottomHeight = 0.5f)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var top = new Vector3(0f, topHeight, 0f);
            var bottom = new Vector3(0f, -bottomHeight, 0f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                AddFlatTri(verts, tris, top, p1, p0);
                AddFlatTri(verts, tris, bottom, p0, p1);
            }
            var mesh = new Mesh { name = "Crystal" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddFlatTri(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>
        /// Flat-shaded, noise-displaced icosphere: a stylised rock. Deterministic for a given seed.
        /// </summary>
        public static Mesh Rock(int seed, float roughness = 0.25f, int subdivisions = 1)
        {
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 100f;
            float oy = (float)rng.NextDouble() * 100f;

            BuildIcosphere(subdivisions, out var positions, out var indices);

            // Displace shared positions first so the surface stays watertight, then split for flat shading.
            for (int i = 0; i < positions.Count; i++)
            {
                var p = positions[i];
                float n = Mathf.PerlinNoise(p.x * 1.7f + ox, p.z * 1.7f + oy) * 0.7f
                          + Mathf.PerlinNoise(p.y * 2.3f + oy, p.x * 2.3f + ox) * 0.3f;
                float scale = 1f + (n - 0.5f) * 2f * roughness;
                p *= scale;
                if (p.y < -0.35f) p.y = -0.35f + (p.y + 0.35f) * 0.2f; // squash the base so rocks sit on the ground
                positions[i] = p;
            }

            var verts = new List<Vector3>(indices.Count);
            var tris = new List<int>(indices.Count);
            for (int i = 0; i < indices.Count; i += 3)
            {
                AddFlatTri(verts, tris, positions[indices[i]], positions[indices[i + 1]], positions[indices[i + 2]]);
            }
            var mesh = new Mesh { name = "Rock_" + seed };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Smooth icosphere with unit radius.</summary>
        public static Mesh Icosphere(int subdivisions, string name = "Icosphere")
        {
            BuildIcosphere(subdivisions, out var positions, out var indices);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(positions);
            mesh.SetNormals(positions.ConvertAll(p => p.normalized));
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildIcosphere(int subdivisions, out List<Vector3> positions, out List<int> indices)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            positions = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < positions.Count; i++) positions[i] = positions[i].normalized;

            indices = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };

            var midCache = new Dictionary<long, int>();
            var pos = positions;
            for (int s = 0; s < Mathf.Clamp(subdivisions, 0, 4); s++)
            {
                var next = new List<int>(indices.Count * 4);
                for (int i = 0; i < indices.Count; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    int ab = Midpoint(a, b, pos, midCache);
                    int bc = Midpoint(b, c, pos, midCache);
                    int ca = Midpoint(c, a, pos, midCache);
                    next.Add(a); next.Add(ab); next.Add(ca);
                    next.Add(b); next.Add(bc); next.Add(ab);
                    next.Add(c); next.Add(ca); next.Add(bc);
                    next.Add(ab); next.Add(bc); next.Add(ca);
                }
                indices = next;
            }
        }

        private static int Midpoint(int a, int b, List<Vector3> positions, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int index)) return index;
            positions.Add(((positions[a] + positions[b]) * 0.5f).normalized);
            index = positions.Count - 1;
            cache[key] = index;
            return index;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PrimitiveCache.Clear();
    }

    /// <summary>
    /// Accumulates many small meshes into one big mesh (one draw call). Optionally forces every vertex's UV
    /// to a single palette cell so many differently coloured objects can share one material.
    /// </summary>
    public class MeshBatch
    {
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _tris = new List<int>();

        public int VertexCount => _verts.Count;
        public bool IsEmpty => _verts.Count == 0;

        public void Add(Mesh mesh, Matrix4x4 matrix, Vector2? uvOverride = null)
        {
            if (mesh == null) return;
            var mv = mesh.vertices;
            var mn = mesh.normals;
            var mu = mesh.uv;
            var mt = mesh.triangles;
            int start = _verts.Count;
            var normalMatrix = matrix.inverse.transpose;
            bool hasNormals = mn != null && mn.Length == mv.Length;
            bool hasUvs = mu != null && mu.Length == mv.Length;

            for (int i = 0; i < mv.Length; i++)
            {
                _verts.Add(matrix.MultiplyPoint3x4(mv[i]));
                _normals.Add(hasNormals ? normalMatrix.MultiplyVector(mn[i]).normalized : Vector3.up);
                _uvs.Add(uvOverride ?? (hasUvs ? mu[i] : Vector2.zero));
            }
            bool flip = matrix.determinant < 0f; // mirrored transforms would turn faces inside out
            for (int i = 0; i < mt.Length; i += 3)
            {
                if (flip)
                {
                    _tris.Add(start + mt[i]); _tris.Add(start + mt[i + 2]); _tris.Add(start + mt[i + 1]);
                }
                else
                {
                    _tris.Add(start + mt[i]); _tris.Add(start + mt[i + 1]); _tris.Add(start + mt[i + 2]);
                }
            }
        }

        /// <summary>Adds one triangle with explicit data (used for grass blades).</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 uvA, Vector2 uvB, Vector2 uvC)
        {
            int i = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c);
            _normals.Add(normal); _normals.Add(normal); _normals.Add(normal);
            _uvs.Add(uvA); _uvs.Add(uvB); _uvs.Add(uvC);
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (_verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_verts);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Clear()
        {
            _verts.Clear();
            _normals.Clear();
            _uvs.Clear();
            _tris.Clear();
        }
    }
}
