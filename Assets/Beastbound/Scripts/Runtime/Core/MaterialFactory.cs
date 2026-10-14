using System.Collections.Generic;
using UnityEngine;

namespace Beastbound.Core
{
    /// <summary>
    /// Creates materials at runtime that work in both the Built-in Render Pipeline and URP.
    ///
    /// Trick: we clone the material Unity assigns to a freshly created primitive. That is always the active
    /// pipeline's default lit material (Standard or URP/Lit) and is always included in builds, so we never
    /// depend on Shader.Find succeeding. Property names for both pipelines are set when present.
    /// </summary>
    public static class MaterialFactory
    {
        private static Material _template;
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        public static Material Template
        {
            get
            {
                if (_template == null)
                {
                    var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    var renderer = probe.GetComponent<MeshRenderer>();
                    _template = renderer != null && renderer.sharedMaterial != null
                        ? new Material(renderer.sharedMaterial)
                        : new Material(Shader.Find("Standard"));
                    _template.name = "Beastbound_Template";
                    Object.DestroyImmediate(probe);
                }
                return _template;
            }
        }

        /// <summary>Opaque lit material with a flat colour. Cached by name.</summary>
        public static Material Lit(string name, Color color, float smoothness = 0.2f, float metallic = 0f)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var m = new Material(Template) { name = name, enableInstancing = true };
            SetColor(m, color);
            SetSmoothness(m, smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            Cache[name] = m;
            return m;
        }

        /// <summary>Opaque lit material using a texture as albedo.</summary>
        public static Material Textured(string name, Texture2D texture, Color tint, float smoothness = 0.1f)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var m = new Material(Template) { name = name, enableInstancing = true };
            SetColor(m, tint);
            SetSmoothness(m, smoothness);
            SetTexture(m, texture);
            Cache[name] = m;
            return m;
        }

        /// <summary>Lit material that also glows. Emission needs the _EMISSION keyword in the Standard shader.</summary>
        public static Material Emissive(string name, Color baseColor, Color emission, float smoothness = 0.6f)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var m = new Material(Template) { name = name, enableInstancing = true };
            SetColor(m, baseColor);
            SetSmoothness(m, smoothness);
            SetEmission(m, emission);
            Cache[name] = m;
            return m;
        }

        /// <summary>Uncached copy, for materials whose colour changes at runtime (hit flashes, water).</summary>
        public static Material Unique(string name, Color color, float smoothness = 0.2f)
        {
            var m = new Material(Template) { name = name };
            SetColor(m, color);
            SetSmoothness(m, smoothness);
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void SetSmoothness(Material m, float smoothness)
        {
            if (m == null) return;
            smoothness = Mathf.Clamp01(smoothness);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        }

        public static void SetTexture(Material m, Texture texture)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
        }

        public static void SetTextureOffset(Material m, Vector2 offset)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseMap")) m.SetTextureOffset("_BaseMap", offset);
            if (m.HasProperty("_MainTex")) m.SetTextureOffset("_MainTex", offset);
        }

        public static void SetTextureScale(Material m, Vector2 scale)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", scale);
            if (m.HasProperty("_MainTex")) m.SetTextureScale("_MainTex", scale);
        }

        public static void SetEmission(Material m, Color emission)
        {
            if (m == null || !m.HasProperty("_EmissionColor")) return;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        /// <summary>Releases cached materials (called when the world is torn down).</summary>
        public static void ClearCache()
        {
            foreach (var m in Cache.Values)
            {
                if (m != null) Object.Destroy(m);
            }
            Cache.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
            _template = null;
        }
    }
}
