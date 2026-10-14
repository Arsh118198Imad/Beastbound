using System.Collections.Generic;
using Beastbound.Core;
using Beastbound.Rules;
using UnityEngine;
using UnityEngine.Rendering;
using Swatch = Beastbound.World.WorldPalette.Swatch;

namespace Beastbound.World
{
    /// <summary>
    /// Generates Sunmeadow Vale: terrain, water, a distant mountain panorama, forests, rocks, grass,
    /// flowers, the Waystone circle, the old ruins and the creature nests. Deterministic for a given seed.
    ///
    /// All vegetation is merged into per-chunk meshes that share one palette material, so the whole vale
    /// renders in a few dozen draw calls.
    /// </summary>
    public class WorldBuilder
    {
        private const float ChunkSize = 40f;

        private readonly int _seed;
        private readonly System.Random _rng;
        private TerrainField _terrain;
        private Transform _root;
        private Transform _colliderRoot;
        private readonly Dictionary<Vector2Int, Chunk> _chunks = new Dictionary<Vector2Int, Chunk>();
        private readonly List<Vector3> _treePositions = new List<Vector3>();

        private Material _paletteMat;
        private Material _grassMat;
        private Material _stoneMat;
        private Material _stoneMossMat;

        private Mesh _pineTrunk, _broadTrunk, _cone1, _cone2, _cone3, _flowerStem, _flowerHead, _reed, _mushStem;
        private Mesh[] _canopies, _rocks;
        private Mesh _sphere;

        private class Chunk
        {
            public readonly MeshBatch Props = new MeshBatch();     // trees and rocks (cast shadows)
            public readonly MeshBatch Flora = new MeshBatch();     // flowers, reeds, mushrooms
            public readonly MeshBatch[] Grass = { new MeshBatch(), new MeshBatch(), new MeshBatch() };
        }

        public WorldBuilder(int seed)
        {
            _seed = seed;
            _rng = new System.Random(seed);
        }

        public WorldContext Build(Transform parent, Light sun)
        {
            float startTime = Time.realtimeSinceStartup;
            _root = new GameObject("World").transform;
            _root.SetParent(parent, false);
            _colliderRoot = new GameObject("PropColliders").transform;
            _colliderRoot.SetParent(_root, false);

            _terrain = new TerrainField(_seed);
            var context = _root.gameObject.AddComponent<WorldContext>();
            context.Initialize(_terrain);

            CreateSharedAssets();
            BuildTerrain();
            BuildWater();
            BuildFarMountains();
            BuildWaystone(context.WaystonePosition);
            BuildRuins();
            ScatterTrees();
            ScatterRocks();
            ScatterGrass();
            ScatterFlora();
            var culler = FinalizeChunks();
            context.Vegetation = culler;
            DefineNests(context);

            var dayNight = _root.gameObject.AddComponent<DayNightCycle>();
            dayNight.Initialize(sun);
            context.DayNight = dayNight;

            var motes = new GameObject("AmbientMotes").AddComponent<AmbientMotes>();
            motes.transform.SetParent(_root, false);
            motes.Initialize(dayNight);

            Debug.Log($"[Beastbound] World generated in {(Time.realtimeSinceStartup - startTime) * 1000f:0} ms (seed {_seed}).");
            return context;
        }

        // ------------------------------------------------------------------ Assets

        private void CreateSharedAssets()
        {
            _paletteMat = MaterialFactory.Textured("WorldPalette", WorldPalette.Texture, Color.white, 0.08f);
            _grassMat = MaterialFactory.Textured("Grass", WorldPalette.GrassTexture, Color.white, 0.05f);
            _stoneMat = MaterialFactory.Lit("RuinStone", WorldPalette.ColorOf(Swatch.Stone), 0.12f);
            _stoneMossMat = MaterialFactory.Lit("RuinStoneMoss", WorldPalette.ColorOf(Swatch.StoneMoss), 0.05f);

            _sphere = MeshKit.Primitive(PrimitiveType.Sphere);
            _pineTrunk = MeshKit.Frustum(0.28f, 0.14f, 2.4f, 7, "PineTrunk");
            _broadTrunk = MeshKit.Frustum(0.34f, 0.2f, 3f, 7, "BroadTrunk");
            _cone1 = MeshKit.Frustum(1.9f, 0f, 3.2f, 9, "PineCone1");
            _cone2 = MeshKit.Frustum(1.5f, 0f, 2.8f, 9, "PineCone2");
            _cone3 = MeshKit.Frustum(1.05f, 0f, 2.4f, 9, "PineCone3");
            _flowerStem = MeshKit.Frustum(0.014f, 0.01f, 0.32f, 4, "FlowerStem");
            _flowerHead = MeshKit.Crystal(5, 0.07f, 0.04f, 0.03f);
            _reed = MeshKit.Frustum(0.035f, 0f, 1.3f, 4, "Reed");
            _mushStem = MeshKit.Frustum(0.05f, 0.04f, 0.18f, 6, "MushroomStem");

            _canopies = new Mesh[4];
            for (int i = 0; i < _canopies.Length; i++) _canopies[i] = MeshKit.Rock(_seed + 100 + i, 0.16f, 1);
            _rocks = new Mesh[6];
            for (int i = 0; i < _rocks.Length; i++) _rocks[i] = MeshKit.Rock(_seed + 200 + i, 0.3f, 1);
        }

        // ------------------------------------------------------------------ Terrain, water, panorama

        private void BuildTerrain()
        {
            var go = new GameObject("Terrain");
            go.transform.SetParent(_root, false);
            go.isStatic = true;
            var mesh = _terrain.BuildMesh();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialFactory.Textured("Terrain", _terrain.BuildColorTexture(768), Color.white, 0.04f);
            mr.shadowCastingMode = ShadowCastingMode.On;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private void BuildWater()
        {
            var go = new GameObject("Water");
            go.layer = Layers.Water;
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(0f, ValeLayout.WaterLevel, 0f);
            go.AddComponent<WaterSurface>().Build(ValeLayout.TerrainSize, 72);
        }

        private void BuildFarMountains()
        {
            var parent = new GameObject("FarMountains").transform;
            parent.SetParent(_root, false);
            var mat = MaterialFactory.Lit("FarMountain", WorldPalette.ColorOf(Swatch.FarMountain), 0.02f);
            var snow = MaterialFactory.Lit("FarSnow", new Color(0.9f, 0.93f, 1f), 0.1f);
            int count = 18;
            for (int i = 0; i < count; i++)
            {
                float angle = (i + (float)_rng.NextDouble() * 0.6f) / count * Mathf.PI * 2f;
                float radius = 215f + (float)_rng.NextDouble() * 80f;
                float width = 70f + (float)_rng.NextDouble() * 70f;
                float height = 50f + (float)_rng.NextDouble() * 60f;
                var mesh = MeshKit.Rock(_seed + 300 + i, 0.32f, 2);
                var pos = new Vector3(Mathf.Cos(angle) * radius, -12f, Mathf.Sin(angle) * radius);
                var rot = Quaternion.Euler(0f, (float)_rng.NextDouble() * 360f, 0f);
                var m = MeshKit.AddPart(parent, "Mountain" + i, mesh, mat, pos, new Vector3(width, height, width * 0.8f), rot);
                m.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                if (height > 80f)
                {
                    var cap = MeshKit.AddPart(m.transform, "Snow", mesh, snow, new Vector3(0f, 0.36f, 0f),
                        new Vector3(0.42f, 0.3f, 0.42f), Quaternion.identity);
                    cap.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        // ------------------------------------------------------------------ Landmarks

        private void BuildWaystone(Vector3 center)
        {
            var parent = new GameObject("WaystoneCircle").transform;
            parent.SetParent(_root, false);
            parent.position = center;

            var pedestalMesh = MeshKit.Frustum(1.5f, 1.15f, 0.7f, 8, "Pedestal");
            var pedestal = MeshKit.AddPart(parent, "Pedestal", pedestalMesh, _stoneMat,
                new Vector3(0f, -0.15f, 0f), Vector3.one, Quaternion.identity);
            var pedCol = pedestal.AddComponent<MeshCollider>();
            pedCol.sharedMesh = pedestalMesh;
            pedCol.convex = true;

            var glow = MaterialFactory.Emissive("WaystoneGlow", new Color(0.4f, 0.95f, 0.9f), new Color(0.3f, 1.6f, 1.5f), 0.9f);
            var crystal = MeshKit.AddPart(parent, "Crystal", MeshKit.Crystal(6, 0.55f, 1.6f, 0.8f), glow,
                new Vector3(0f, 2f, 0f), Vector3.one, Quaternion.identity);
            crystal.AddComponent<FloatingSpin>();

            var lightGo = new GameObject("WaystoneLight");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.45f, 1f, 0.92f);
            light.range = 12f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;

            int stones = 8;
            for (int i = 0; i < stones; i++)
            {
                float a = i / (float)stones * Mathf.PI * 2f + 0.2f;
                var p = new Vector3(Mathf.Cos(a) * 7.5f, 0f, Mathf.Sin(a) * 7.5f);
                p.y = _terrain.HeightAt(center.x + p.x, center.z + p.z) - center.y;
                bool broken = i == 2 || i == 5;
                float h = broken ? 1.1f + (float)_rng.NextDouble() * 0.5f : 2.4f + (float)_rng.NextDouble() * 1f;
                var stone = CreateSolidBlock(parent, "StandingStone" + i, _stoneMat,
                    p + Vector3.up * (h * 0.5f - 0.2f), new Vector3(0.95f, h, 0.6f),
                    Quaternion.Euler((float)_rng.NextDouble() * 6f - 3f, -a * Mathf.Rad2Deg + 90f, (float)_rng.NextDouble() * 8f - 4f));
                MeshKit.AddPart(stone.transform, "Moss", _sphere, _stoneMossMat, new Vector3(0f, 0.5f, 0f),
                    new Vector3(1.1f, 0.12f, 1.15f), Quaternion.identity, false);
                if (broken)
                {
                    var fallen = p + new Vector3(Mathf.Cos(a + 0.4f), 0f, Mathf.Sin(a + 0.4f)) * 1.8f;
                    CreateSolidBlock(parent, "Fallen" + i, _stoneMat, fallen + Vector3.up * 0.3f,
                        new Vector3(0.9f, 0.6f, 1.6f), Quaternion.Euler(0f, (float)_rng.NextDouble() * 180f, 6f));
                }
            }
        }

        private void BuildRuins()
        {
            var parent = new GameObject("OldRuins").transform;
            parent.SetParent(_root, false);
            Vector3 c = new Vector3(ValeLayout.Ruins.x, 0f, ValeLayout.Ruins.y);
            c.y = _terrain.HeightAt(c.x, c.z);
            parent.position = c;
            parent.rotation = Quaternion.Euler(0f, 35f, 0f);

            // Great arch
            CreateSolidBlock(parent, "PillarL", _stoneMat, new Vector3(-2.6f, 2.4f, 0f), new Vector3(1.2f, 5.2f, 1.2f), Quaternion.identity);
            CreateSolidBlock(parent, "PillarR", _stoneMat, new Vector3(2.6f, 2.4f, 0f), new Vector3(1.2f, 5.2f, 1.2f), Quaternion.Euler(0f, 0f, 2f));
            var lintel = CreateSolidBlock(parent, "Lintel", _stoneMat, new Vector3(0f, 5.4f, 0f), new Vector3(7f, 1f, 1.4f), Quaternion.Euler(0f, 0f, -3f));
            MeshKit.AddPart(lintel.transform, "Moss", _sphere, _stoneMossMat, new Vector3(-0.1f, 0.5f, 0f),
                new Vector3(0.8f, 0.25f, 1.1f), Quaternion.identity, false);

            // Broken walls and scattered blocks
            CreateSolidBlock(parent, "WallA", _stoneMat, new Vector3(-7f, 0.8f, 4f), new Vector3(5f, 1.8f, 0.9f), Quaternion.Euler(0f, 15f, 0f));
            CreateSolidBlock(parent, "WallB", _stoneMat, new Vector3(6.5f, 0.6f, 5f), new Vector3(4f, 1.3f, 0.9f), Quaternion.Euler(0f, -20f, 0f));
            CreateSolidBlock(parent, "WallC", _stoneMossMat, new Vector3(0.5f, 0.5f, 9f), new Vector3(6f, 1.1f, 0.9f), Quaternion.Euler(0f, 3f, 0f));
            for (int i = 0; i < 9; i++)
            {
                var p = new Vector3((float)_rng.NextDouble() * 18f - 9f, 0.3f, (float)_rng.NextDouble() * 16f - 6f);
                if (Mathf.Abs(p.x) < 3.5f && Mathf.Abs(p.z) < 2f) continue; // keep the archway clear
                float s = 0.6f + (float)_rng.NextDouble() * 0.7f;
                CreateSolidBlock(parent, "Block" + i, i % 3 == 0 ? _stoneMossMat : _stoneMat, p,
                    new Vector3(s * 1.3f, s * 0.8f, s), Quaternion.Euler(0f, (float)_rng.NextDouble() * 90f, (float)_rng.NextDouble() * 10f));
            }

            // Old rune crystals
            var rune = MaterialFactory.Emissive("RuneGlow", new Color(0.7f, 0.6f, 1f), new Color(0.9f, 0.6f, 1.8f), 0.9f);
            var crystalMesh = MeshKit.Crystal(5, 0.3f, 0.9f, 0.3f);
            MeshKit.AddPart(parent, "RuneA", crystalMesh, rune, new Vector3(-2.6f, 5.4f, 0.75f), Vector3.one * 0.5f, Quaternion.Euler(90f, 0f, 0f), false);
            MeshKit.AddPart(parent, "RuneB", crystalMesh, rune, new Vector3(-6f, 0.4f, 6f), Vector3.one, Quaternion.Euler(10f, 0f, 15f));
            MeshKit.AddPart(parent, "RuneC", crystalMesh, rune, new Vector3(5f, 0.3f, 8f), Vector3.one * 0.8f, Quaternion.Euler(-12f, 0f, -20f));
        }

        /// <summary>Cube with a BoxCollider, positioned relative to <paramref name="parent"/>.</summary>
        private static GameObject CreateSolidBlock(Transform parent, string name, Material mat, Vector3 localPos, Vector3 scale, Quaternion localRot)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
            return go;
        }

        // ------------------------------------------------------------------ Scattering

        private Chunk ChunkAt(Vector3 p)
        {
            var key = new Vector2Int(Mathf.FloorToInt(p.x / ChunkSize), Mathf.FloorToInt(p.z / ChunkSize));
            if (!_chunks.TryGetValue(key, out var chunk))
            {
                chunk = new Chunk();
                _chunks[key] = chunk;
            }
            return chunk;
        }

        private float Rand(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

        private bool NearLandmark(float x, float z, float waystoneClear, float ruinsClear)
        {
            var p = new Vector2(x, z);
            return Vector2.Distance(p, ValeLayout.Waystone) < waystoneClear || Vector2.Distance(p, ValeLayout.Ruins) < ruinsClear;
        }

        private void ScatterTrees()
        {
            int placed = 0;
            for (int attempt = 0; attempt < 6000 && placed < 260; attempt++)
            {
                float x = Rand(-125f, 125f), z = Rand(-125f, 125f);
                float h = _terrain.HeightAt(x, z);
                if (h < 0.6f || h > 30f) continue;
                if (_terrain.SlopeAt(x, z) > 30f) continue;
                if (NearLandmark(x, z, 13f, 11f)) continue;
                if (_terrain.PathMask(x, z) > 0.1f) continue;
                if (Vector2.Distance(new Vector2(x, z), ValeLayout.PlayerSpawn) < 9f) continue;
                float forest = _terrain.ForestMask(x, z);
                float chance = 0.035f + forest * 0.9f + Noise.SmoothStep(80f, 110f, new Vector2(x, z).magnitude) * 0.3f;
                if (_rng.NextDouble() > chance) continue;
                var pos = new Vector3(x, h, z);
                bool crowded = false;
                foreach (var t in _treePositions)
                {
                    if ((t - pos).sqrMagnitude < 12f) { crowded = true; break; }
                }
                if (crowded) continue;

                _treePositions.Add(pos);
                placed++;
                float s = Rand(0.8f, 1.35f);
                bool nearLandmark = NearLandmark(x, z, 40f, 30f);
                if (h > 8f || (forest > 0.5f && _rng.NextDouble() < 0.55)) AddPine(pos, s);
                else AddBroadleaf(pos, s, nearLandmark && _rng.NextDouble() < 0.4);
                AddTreeCollider(pos, s);
            }
        }

        private void AddPine(Vector3 pos, float s)
        {
            var chunk = ChunkAt(pos);
            var rot = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            var trunk = WorldPalette.UV(Swatch.TrunkDark);
            chunk.Props.Add(_pineTrunk, Matrix4x4.TRS(pos + Vector3.down * 0.2f, rot, Vector3.one * s), trunk);
            var a = WorldPalette.UV(Swatch.Pine);
            var b = WorldPalette.UV(Swatch.PineDark);
            chunk.Props.Add(_cone1, Matrix4x4.TRS(pos + Vector3.up * 1.6f * s, rot, Vector3.one * s), b);
            chunk.Props.Add(_cone2, Matrix4x4.TRS(pos + Vector3.up * 3.1f * s, rot, Vector3.one * s), a);
            chunk.Props.Add(_cone3, Matrix4x4.TRS(pos + Vector3.up * 4.5f * s, rot, Vector3.one * s), b);
        }

        private void AddBroadleaf(Vector3 pos, float s, bool blossom)
        {
            var chunk = ChunkAt(pos);
            var rot = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            chunk.Props.Add(_broadTrunk, Matrix4x4.TRS(pos + Vector3.down * 0.2f, rot, Vector3.one * s), WorldPalette.UV(Swatch.Trunk));
            Swatch main = blossom ? Swatch.Blossom : (_rng.NextDouble() < 0.06 ? Swatch.LeafAutumn : Swatch.Leaf);
            Swatch light = blossom ? Swatch.BlossomLight : Swatch.LeafLight;
            int blobs = 3 + _rng.Next(2);
            chunk.Props.Add(_canopies[_rng.Next(_canopies.Length)],
                Matrix4x4.TRS(pos + Vector3.up * 3.9f * s, rot, new Vector3(2.4f, 1.9f, 2.4f) * s), WorldPalette.UV(main));
            for (int i = 0; i < blobs; i++)
            {
                float a = i / (float)blobs * Mathf.PI * 2f + Rand(0f, 1f);
                var offset = new Vector3(Mathf.Cos(a) * 1.3f, Rand(-0.3f, 0.6f), Mathf.Sin(a) * 1.3f) * s;
                chunk.Props.Add(_canopies[_rng.Next(_canopies.Length)],
                    Matrix4x4.TRS(pos + Vector3.up * 3.5f * s + offset, rot, Vector3.one * Rand(1.3f, 1.8f) * s),
                    WorldPalette.UV(i % 2 == 0 ? light : main));
            }
        }

        private void AddTreeCollider(Vector3 pos, float s)
        {
            var go = new GameObject("TreeCollider");
            go.transform.SetParent(_colliderRoot, false);
            go.transform.position = pos;
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.32f * s;
            col.height = 4f * s;
            col.center = new Vector3(0f, 2f * s, 0f);
        }

        private void ScatterRocks()
        {
            int placed = 0;
            for (int attempt = 0; attempt < 3000 && placed < 110; attempt++)
            {
                float x = Rand(-122f, 122f), z = Rand(-122f, 122f);
                float d = new Vector2(x, z).magnitude;
                float h = _terrain.HeightAt(x, z);
                if (h < -0.6f || h > 40f) continue;
                if (NearLandmark(x, z, 10f, 0f)) continue;
                if (_terrain.PathMask(x, z) > 0.2f) continue;
                if (Vector2.Distance(new Vector2(x, z), ValeLayout.PlayerSpawn) < 6f) continue;
                float chance = 0.08f + Noise.SmoothStep(60f, 100f, d) * 0.5f
                               + (Vector2.Distance(new Vector2(x, z), ValeLayout.Ruins) < 25f ? 0.4f : 0f);
                if (_rng.NextDouble() > chance) continue;
                placed++;

                bool big = d > 75f && _rng.NextDouble() < 0.5;
                float s = big ? Rand(2.2f, 4.5f) : Rand(0.45f, 1.9f);
                var scale = new Vector3(s * Rand(0.9f, 1.4f), s * Rand(0.6f, 1f), s * Rand(0.9f, 1.3f));
                var rot = Quaternion.Euler(Rand(-8f, 8f), Rand(0f, 360f), Rand(-8f, 8f));
                var pos = new Vector3(x, h - s * 0.15f, z);
                var mesh = _rocks[_rng.Next(_rocks.Length)];
                Swatch sw = _rng.NextDouble() < 0.35 ? Swatch.RockMoss : (_rng.NextDouble() < 0.5 ? Swatch.Rock : Swatch.RockDark);
                ChunkAt(pos).Props.Add(mesh, Matrix4x4.TRS(pos, rot, scale), WorldPalette.UV(sw));

                if (s > 0.6f)
                {
                    var go = new GameObject("RockCollider");
                    go.transform.SetParent(_colliderRoot, false);
                    go.transform.SetPositionAndRotation(pos, rot);
                    go.transform.localScale = scale;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                    mc.convex = true;
                }
            }
        }

        private void ScatterGrass()
        {
            const float spacing = 1.25f;
            int index = 0;
            for (float z = -100f; z < 100f; z += spacing)
            {
                for (float x = -100f; x < 100f; x += spacing)
                {
                    index++;
                    float px = x + Rand(-0.6f, 0.6f) * spacing;
                    float pz = z + Rand(-0.6f, 0.6f) * spacing;
                    if (new Vector2(px, pz).magnitude > 98f) continue;
                    float h = _terrain.HeightAt(px, pz);
                    if (h < 0.3f || h > 16f) continue;
                    if (_terrain.SlopeAt(px, pz) > 28f) continue;
                    float path = _terrain.PathMask(px, pz);
                    float density = 0.75f * (1f - path * 0.9f) * (1f - _terrain.ForestMask(px, pz) * 0.45f);
                    if (NearLandmark(px, pz, 3f, 5f)) density *= 0.3f;
                    if (_rng.NextDouble() > density) continue;

                    var basePos = new Vector3(px, h - 0.03f, pz);
                    var chunk = ChunkAt(basePos);
                    int layer = index % 3;
                    AddTuft(chunk.Grass[layer], basePos, _terrain.NormalAt(px, pz));
                }
            }
        }

        private void AddTuft(MeshBatch batch, Vector3 basePos, Vector3 groundNormal)
        {
            int blades = 3 + _rng.Next(3);
            int variant = _rng.Next(8);
            Vector3 normal = Vector3.Lerp(groundNormal, Vector3.up, 0.6f).normalized;
            for (int i = 0; i < blades; i++)
            {
                float angle = Rand(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var right = new Vector3(-dir.z, 0f, dir.x);
                Vector3 p = basePos + dir * Rand(0f, 0.18f);
                float height = Rand(0.32f, 0.78f);
                float width = Rand(0.06f, 0.11f);
                Vector3 tip = p + Vector3.up * height + dir * Rand(0.05f, 0.22f);
                Vector3 a = p + right * width * 0.5f;
                Vector3 b = p - right * width * 0.5f;
                var uvBase = WorldPalette.GrassUV(variant, 0f);
                var uvTip = WorldPalette.GrassUV(variant, 1f);
                batch.AddTriangle(a, tip, b, normal, uvBase, uvTip, uvBase);
                batch.AddTriangle(b, tip, a, normal, uvBase, uvTip, uvBase); // back face, same lighting
            }
        }

        private void ScatterFlora()
        {
            Swatch[] flowerColors = { Swatch.FlowerYellow, Swatch.FlowerViolet, Swatch.FlowerWhite, Swatch.FlowerRed, Swatch.FlowerBlue };
            var stemUV = WorldPalette.UV(Swatch.Stem);

            // Flowers grow in colour patches.
            for (int i = 0; i < 2600; i++)
            {
                float x = Rand(-95f, 95f), z = Rand(-95f, 95f);
                float patch = _terrain.Noise.Fbm(x * 0.05f, z * 0.05f, 2, 10);
                if (patch < 0.52f) continue;
                float h = _terrain.HeightAt(x, z);
                if (h < 0.5f || h > 12f || _terrain.SlopeAt(x, z) > 25f || _terrain.PathMask(x, z) > 0.3f) continue;
                int colorIndex = Mathf.Clamp((int)(_terrain.Noise.Sample(x * 0.02f, z * 0.02f, 11) * flowerColors.Length), 0, flowerColors.Length - 1);
                var pos = new Vector3(x, h - 0.02f, z);
                float s = Rand(0.8f, 1.4f);
                var rot = Quaternion.Euler(Rand(-8f, 8f), Rand(0f, 360f), Rand(-8f, 8f));
                var chunk = ChunkAt(pos);
                chunk.Flora.Add(_flowerStem, Matrix4x4.TRS(pos, rot, Vector3.one * s), stemUV);
                chunk.Flora.Add(_flowerHead, Matrix4x4.TRS(pos + rot * Vector3.up * 0.32f * s, rot, Vector3.one * s * 1.3f),
                    WorldPalette.UV(flowerColors[colorIndex]));
            }

            // Mushrooms at the feet of trees.
            var capUV = WorldPalette.UV(Swatch.MushroomCap);
            var mStemUV = WorldPalette.UV(Swatch.MushroomStem);
            foreach (var tree in _treePositions)
            {
                if (_rng.NextDouble() > 0.35) continue;
                int count = 1 + _rng.Next(3);
                for (int i = 0; i < count; i++)
                {
                    float a = Rand(0f, Mathf.PI * 2f);
                    var p = tree + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Rand(0.7f, 1.6f);
                    p.y = _terrain.HeightAt(p.x, p.z) - 0.02f;
                    float s = Rand(0.7f, 1.6f);
                    var chunk = ChunkAt(p);
                    chunk.Flora.Add(_mushStem, Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * s), mStemUV);
                    chunk.Flora.Add(_sphere, Matrix4x4.TRS(p + Vector3.up * 0.18f * s, Quaternion.identity,
                        new Vector3(0.24f, 0.12f, 0.24f) * s), capUV);
                }
            }

            // Reeds along the waterline.
            var reedUV = WorldPalette.UV(Swatch.Reed);
            for (int i = 0; i < 9000; i++)
            {
                float x = Rand(-100f, 100f), z = Rand(-100f, 100f);
                float h = _terrain.HeightAt(x, z);
                if (h < -0.35f || h > 0.35f) continue;
                int count = 3 + _rng.Next(4);
                for (int r = 0; r < count; r++)
                {
                    var p = new Vector3(x + Rand(-0.4f, 0.4f), 0f, z + Rand(-0.4f, 0.4f));
                    p.y = _terrain.HeightAt(p.x, p.z) - 0.05f;
                    var rot = Quaternion.Euler(Rand(-10f, 10f), 0f, Rand(-10f, 10f));
                    ChunkAt(p).Flora.Add(_reed, Matrix4x4.TRS(p, rot, new Vector3(1f, Rand(0.6f, 1.2f), 1f)), reedUV);
                }
            }
        }

        // ------------------------------------------------------------------ Chunk meshes

        private VegetationCuller FinalizeChunks()
        {
            var parent = new GameObject("Vegetation").transform;
            parent.SetParent(_root, false);
            var culler = parent.gameObject.AddComponent<VegetationCuller>();

            foreach (var kv in _chunks)
            {
                var key = kv.Key;
                var chunk = kv.Value;
                var center = new Vector3((key.x + 0.5f) * ChunkSize, 0f, (key.y + 0.5f) * ChunkSize);
                string suffix = $"_{key.x}_{key.y}";

                if (!chunk.Props.IsEmpty)
                {
                    var r = CreateChunkRenderer(parent, "Props" + suffix, chunk.Props, _paletteMat, true);
                    culler.Register(r, center, VegetationCuller.Kind.Props, 0);
                }
                if (!chunk.Flora.IsEmpty)
                {
                    var r = CreateChunkRenderer(parent, "Flora" + suffix, chunk.Flora, _paletteMat, false);
                    culler.Register(r, center, VegetationCuller.Kind.Flora, 0);
                }
                for (int i = 0; i < chunk.Grass.Length; i++)
                {
                    if (chunk.Grass[i].IsEmpty) continue;
                    var r = CreateChunkRenderer(parent, $"Grass{i}{suffix}", chunk.Grass[i], _grassMat, false);
                    culler.Register(r, center, VegetationCuller.Kind.Grass, i);
                }
            }
            _chunks.Clear();
            return culler;
        }

        private static Renderer CreateChunkRenderer(Transform parent, string name, MeshBatch batch, Material mat, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = batch.Build(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return mr;
        }

        // ------------------------------------------------------------------ Nests

        private void DefineNests(WorldContext context)
        {
            AddNest(context, "nest_meadow", new Vector2(22f, -16f), Personality.Bold, 4, 1.03f);
            AddNest(context, "nest_lakeshore", new Vector2(18f, 38f), Personality.Timid, 3, 1.07f);
            AddNest(context, "nest_ruins", new Vector2(-42f, -32f), Personality.Sleepy, 5, 0.98f);
            AddNest(context, "nest_grove", new Vector2(-24f, 30f), Personality.Curious, 3, 1.0f);
            AddNest(context, "nest_knoll", new Vector2(50f, -30f), Personality.Playful, 4, 0.95f);
        }

        private void AddNest(WorldContext context, string id, Vector2 xz, Personality personality, int level, float vigor)
        {
            var desired = new Vector3(xz.x, 0f, xz.y);
            context.TryFindWalkableNear(desired, 8f, out var pos, 30);
            context.Nests.Add(new NestPoint
            {
                Id = id,
                SpeciesId = Creatures.DefaultContent.PebbloomId,
                Position = pos,
                Personality = personality,
                Level = level,
                Vigor = vigor
            });
        }
    }
}
