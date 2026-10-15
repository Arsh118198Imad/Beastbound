using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
#if BB_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace Beastbound.EditorTools
{
    /// <summary>
    /// Menu "Beastbound > Setup": one-time project setup for the high-end look.
    ///   1. Install Packages: URP (render pipeline), Input System (gamepads) and glTFast (imports the Blender
    ///      models in Assets/Beastbound/Resources/Models). Unity picks the versions made for your editor.
    ///   2. Configure Graphics: creates a URP asset (HDR, soft shadows, SSAO), switches the project to it and to
    ///      Linear colour, and re-imports the models so their materials use URP shaders. Runs automatically
    ///      after step 1 finishes; the menu item is there to run it again.
    /// </summary>
    [InitializeOnLoad]
    public static class GraphicsSetup
    {
        private static readonly string[] Packages =
        {
            "com.unity.render-pipelines.universal",
            "com.unity.inputsystem",
            "com.unity.cloud.gltfast"
        };

        private const string SettingsFolder = "Assets/Beastbound/Settings";
        private const string ModelsFolder = "Assets/Beastbound/Resources/Models";
        private const string ConfigurePendingKey = "Beastbound.ConfigureGraphicsPending";

#if UNITY_2021_2_OR_NEWER
        private static AddAndRemoveRequest _request;
#else
        private static Request _request;
#endif

        static GraphicsSetup()
        {
#if BB_URP
            if (EditorPrefs.GetBool(ConfigurePendingKey, false))
            {
                EditorPrefs.SetBool(ConfigurePendingKey, false);
                EditorApplication.delayCall += () => ConfigureGraphics();
            }
#endif
        }

        [MenuItem("Beastbound/Setup/1. Install Packages (URP, Input System, glTFast)", priority = 100)]
        public static void InstallPackages()
        {
            if (_request != null && !_request.IsCompleted)
            {
                Debug.Log("[Beastbound] Package installation is already running.");
                return;
            }
            if (!EditorUtility.DisplayDialog("Install packages",
                    "Beastbound will install three Unity packages from the Unity Registry:\n\n" +
                    "• Universal Render Pipeline (lighting, shadows, post-processing)\n" +
                    "• Input System (gamepads)\n" +
                    "• glTFast (imports the Blender models)\n\n" +
                    "This needs an internet connection and takes a few minutes. If Unity asks to enable the new " +
                    "input backends and restart, click Yes. Graphics are configured automatically afterwards.",
                    "Install", "Cancel"))
            {
                return;
            }
            EditorPrefs.SetBool(ConfigurePendingKey, true);
#if UNITY_2021_2_OR_NEWER
            _request = Client.AddAndRemove(Packages, null);
#else
            // Older editors cannot add several packages in one request; Beastbound targets Unity 6.
            _request = Client.Add(Packages[0]);
#endif
            EditorApplication.update += WatchRequest;
            Debug.Log("[Beastbound] Installing packages… (see the progress bar at the bottom right of the editor)");
        }

        private static void WatchRequest()
        {
            if (_request == null || !_request.IsCompleted) return;
            EditorApplication.update -= WatchRequest;
            if (_request.Status == StatusCode.Success)
            {
                Debug.Log("[Beastbound] Packages installed. Unity will recompile, then configure the graphics.");
            }
            else
            {
                EditorPrefs.SetBool(ConfigurePendingKey, false);
                string message = _request.Error != null ? _request.Error.message : "unknown error";
                Debug.LogError("[Beastbound] Package installation failed: " + message);
                EditorUtility.DisplayDialog("Package installation failed", message +
                    "\n\nYou can install them by hand: Window > Package Manager > Unity Registry > " +
                    "Universal RP, Input System and glTFast.", "OK");
            }
            _request = null;
        }

        [MenuItem("Beastbound/Setup/2. Configure Graphics (URP)", priority = 101)]
        public static void ConfigureGraphics()
        {
#if BB_URP
            EnsureFolder(SettingsFolder);
            string rendererPath = SettingsFolder + "/BeastboundRenderer.asset";
            string assetPath = SettingsFolder + "/BeastboundURP.asset";

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, rendererPath);
            }
            // Post-processing shaders/textures (normally filled in automatically; set here in case they are not).
            var rso = new SerializedObject(rendererData);
            var post = rso.FindProperty("postProcessData");
            if (post != null && post.objectReferenceValue == null)
            {
                post.objectReferenceValue = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                rso.ApplyModifiedPropertiesWithoutUndo();
            }
            AddSsao(rendererData);
            EditorUtility.SetDirty(rendererData);

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            asset.supportsHDR = true;
            asset.shadowDistance = 90f;
            asset.shadowCascadeCount = 4;
            asset.msaaSampleCount = 4;
            var so = new SerializedObject(asset);
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetInt(so, "m_MainLightShadowmapResolution", 4096);
            SetBool(so, "m_AdditionalLightShadowsSupported", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
            PlayerSettings.colorSpace = ColorSpace.Linear;

            ReimportModels();
            Debug.Log("[Beastbound] Graphics configured: URP with HDR, soft shadows and SSAO. Press Play.");
            EditorUtility.DisplayDialog("Beastbound graphics ready",
                "The project now uses the Universal Render Pipeline with post-processing, and the Blender models " +
                "have been imported.\n\nOpen the scene (Beastbound > Open Prototype Scene) and press Play.", "OK");
#else
            EditorUtility.DisplayDialog("URP is not installed yet",
                "Run Beastbound > Setup > 1. Install Packages first. Graphics are configured automatically " +
                "when it finishes.", "OK");
#endif
        }

#if BB_URP
        /// <summary>Adds screen-space ambient occlusion (soft contact shadows) to the renderer, once.</summary>
        private static void AddSsao(UniversalRendererData data)
        {
            var type = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
            if (type == null) return;
            foreach (var f in data.rendererFeatures)
            {
                if (f != null && f.GetType() == type) return;
            }
            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            feature.name = "SSAO";
            AssetDatabase.AddObjectToAsset(feature, data);
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId)) return;
            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            if (list == null || map == null) return;
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
#endif

        [MenuItem("Beastbound/Setup/3. Re-import Models", priority = 102)]
        public static void ReimportModels()
        {
            if (!AssetDatabase.IsValidFolder(ModelsFolder)) return;
            foreach (var guid in AssetDatabase.FindAssets("", new[] { ModelsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".glb")) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p != null && p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value;
        }

        private static void SetInt(SerializedObject so, string name, int value)
        {
            var p = so.FindProperty(name);
            if (p != null && p.propertyType == SerializedPropertyType.Integer) p.intValue = value;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
