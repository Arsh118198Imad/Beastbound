using UnityEngine;
#if BB_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace Beastbound.Core
{
    /// <summary>
    /// Cinematic post-processing when the project runs on URP (menu Beastbound > Setup): ACES tonemapping,
    /// a warm golden-hour grade, bloom on glowing crystals and veins, a soft vignette and SMAA, plus shadow
    /// settings per graphics quality. Without URP (BB_URP undefined) every call does nothing.
    /// </summary>
    public static class PostFxRig
    {
#if BB_URP
        private static Bloom _bloom;
        private static UniversalAdditionalCameraData _cameraData;
#endif

        public static bool Active
        {
            get
            {
#if BB_URP
                return GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset;
#else
                return false;
#endif
            }
        }

        public static void Setup(Camera cam, Transform parent)
        {
#if BB_URP
            if (!Active || cam == null) return;
            _cameraData = cam.GetUniversalAdditionalCameraData();
            _cameraData.renderPostProcessing = true;
            _cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            _cameraData.antialiasingQuality = AntialiasingQuality.High;

            var go = new GameObject("PostProcessing");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "BeastboundPost";
            volume.sharedProfile = profile;

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES);

            _bloom = profile.Add<Bloom>(true);
            _bloom.threshold.Override(1.05f);
            _bloom.intensity.Override(0.55f);
            _bloom.scatter.Override(0.62f);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.35f);
            color.contrast.Override(14f);
            color.saturation.Override(12f);

            var white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(9f); // warm, late-afternoon light
            white.tint.Override(2f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.45f);
#endif
        }

        /// <summary>Matches the Settings > Graphics quality (0 Low … 3 Ultra).</summary>
        public static void ApplyQuality(int quality)
        {
#if BB_URP
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return;
            switch (quality)
            {
                case 0: asset.shadowDistance = 45f; asset.shadowCascadeCount = 1; asset.msaaSampleCount = 1; break;
                case 1: asset.shadowDistance = 70f; asset.shadowCascadeCount = 2; asset.msaaSampleCount = 2; break;
                case 2: asset.shadowDistance = 100f; asset.shadowCascadeCount = 4; asset.msaaSampleCount = 4; break;
                default: asset.shadowDistance = 150f; asset.shadowCascadeCount = 4; asset.msaaSampleCount = 4; break;
            }
            if (_bloom != null) _bloom.active = quality >= 1;
            if (_cameraData != null)
            {
                _cameraData.antialiasing = quality >= 1 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            }
#endif
        }
    }
}
