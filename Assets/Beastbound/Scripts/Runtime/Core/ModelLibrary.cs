using System.Collections.Generic;
using UnityEngine;

namespace Beastbound.Core
{
    /// <summary>One placed copy of an imported model: its root, its bones by name and its own materials.</summary>
    public class ModelInstance
    {
        public Transform Root;
        public readonly Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();
        public readonly List<Material> Materials = new List<Material>();
        /// <summary>Materials that glow (veins, eyes, crystals) with their imported emission, for pulsing.</summary>
        public readonly List<(Material mat, string prop, Color baseColor)> Glow = new List<(Material, string, Color)>();

        public Transform Bone(string name) => Bones.TryGetValue(name, out var t) ? t : null;

        /// <summary>Scales every glowing material's imported emission (1 = as authored).</summary>
        public void SetGlow(float intensity)
        {
            foreach (var g in Glow)
            {
                if (g.mat != null) g.mat.SetColor(g.prop, g.baseColor * intensity);
            }
        }
    }

    /// <summary>
    /// Loads the Blender-made models (art/blender/*.py → .glb) from Resources/Models. They are imported by the
    /// glTFast package (menu Beastbound > Setup > 1. Install Packages). When a model is missing, Load returns
    /// null and callers fall back to the procedural primitive models, so the game always runs.
    /// </summary>
    public static class ModelLibrary
    {
        private static readonly Dictionary<string, GameObject> Loaded = new Dictionary<string, GameObject>();
        private static bool _reportedMissing;

        // Property names used by glTFast's shaders, then by URP/Lit and Standard.
        private static readonly string[] BaseColorProps = { "baseColorFactor", "_BaseColor", "_Color" };
        private static readonly string[] EmissionProps = { "emissiveFactor", "_EmissionColor" };

        public static GameObject Prefab(string name)
        {
            if (Loaded.TryGetValue(name, out var cached)) return cached;
            var prefab = Resources.Load<GameObject>("Models/" + name);
            Loaded[name] = prefab;
            if (prefab == null && !_reportedMissing)
            {
                _reportedMissing = true;
                Debug.Log("[Beastbound] Imported models not found (Assets/Beastbound/Resources/Models). Using the simple " +
                          "built-in models. Run Beastbound > Setup > 1. Install Packages to import them.");
            }
            return prefab;
        }

        public static bool Has(string name) => Prefab(name) != null;

        /// <summary>Mesh of a named child (static props such as rocks), or null.</summary>
        public static Mesh Mesh(string name, string meshObject)
        {
            var prefab = Prefab(name);
            if (prefab == null) return null;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name == meshObject && mf.sharedMesh != null) return mf.sharedMesh;
            }
            return null;
        }

        /// <summary>Shared material of a named child (static props), or null.</summary>
        public static Material SharedMaterial(string name, string meshObject)
        {
            var prefab = Prefab(name);
            if (prefab == null) return null;
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name == meshObject) return r.sharedMaterial;
            }
            return null;
        }

        /// <summary>
        /// Instantiates a model under <paramref name="parent"/> with its own material copies (so glow and tint can
        /// change per creature). Returns null when the model is not available.
        /// </summary>
        public static ModelInstance Instantiate(string name, Transform parent, int layer)
        {
            var prefab = Prefab(name);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = name;
            var inst = new ModelInstance { Root = go.transform };
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
                if (!inst.Bones.ContainsKey(t.name)) inst.Bones[t.name] = t;
            }

            var copies = new Dictionary<Material, Material>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
                var shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    var m = shared[i];
                    if (m == null) continue;
                    if (!copies.TryGetValue(m, out var copy))
                    {
                        copy = new Material(m) { name = m.name };
                        copies[m] = copy;
                        inst.Materials.Add(copy);
                    }
                    shared[i] = copy;
                }
                r.sharedMaterials = shared;
                if (r is SkinnedMeshRenderer smr)
                {
                    // Poses (lying down, lunges, rolls) move far from the bind pose; keep generous bounds
                    // instead of recomputing them every frame.
                    var b = smr.localBounds;
                    b.Expand(b.size.magnitude);
                    smr.localBounds = b;
                }
            }

            foreach (var m in inst.Materials)
            {
                if (m.name.Contains("Shine")) continue;
                string prop = FirstProperty(m, EmissionProps);
                if (prop == null) continue;
                var c = m.GetColor(prop);
                if (c.maxColorComponent > 0.001f) inst.Glow.Add((m, prop, c));
            }
            return inst;
        }

        /// <summary>Shifts hue/brightness of materials whose name contains <paramref name="nameContains"/>.</summary>
        public static void Tint(ModelInstance inst, string nameContains, float amount)
        {
            if (inst == null || Mathf.Abs(amount) < 0.0001f) return;
            foreach (var m in inst.Materials)
            {
                if (!m.name.Contains(nameContains)) continue;
                string prop = FirstProperty(m, BaseColorProps);
                if (prop == null) continue;
                var c = m.GetColor(prop);
                Color.RGBToHSV(c, out float h, out float s, out float v);
                var shifted = Color.HSVToRGB(Mathf.Repeat(h + amount * 0.5f, 1f), s, Mathf.Clamp01(v + amount * 0.5f));
                shifted.a = c.a;
                m.SetColor(prop, shifted);
            }
        }

        private static string FirstProperty(Material m, string[] names)
        {
            foreach (var n in names)
            {
                if (m.HasProperty(n)) return n;
            }
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Loaded.Clear();
            _reportedMissing = false;
        }
    }
}
