using System.IO;
using System.Linq;
using Beastbound.Core;
using Beastbound.Creatures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Beastbound.EditorTools
{
    /// <summary>
    /// Editor menu "Beastbound": creates/opens the prototype scene and optional editable data assets.
    /// The scene only contains a camera, a light and a GameBootstrap; everything else is generated on Play.
    /// </summary>
    [InitializeOnLoad]
    public static class PrototypeSceneBuilder
    {
        public const string ScenePath = "Assets/Beastbound/Scenes/SunmeadowVale.unity";
        private const string DataFolder = "Assets/Beastbound/Data";
        private const string DontAskKey = "Beastbound.DontAskToCreateScene";
        private const string AskedThisSessionKey = "Beastbound.AskedThisSession";

        static PrototypeSceneBuilder()
        {
            EditorApplication.delayCall += OfferSceneCreationOnce;
        }

        private static void OfferSceneCreationOnce()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(ScenePath) || EditorPrefs.GetBool(DontAskKey, false)) return;
            if (SessionState.GetBool(AskedThisSessionKey, false)) return;
            SessionState.SetBool(AskedThisSessionKey, true);

            int choice = EditorUtility.DisplayDialogComplex(
                "Welcome to Beastbound",
                "The prototype scene has not been created yet.\n\nCreate and open it now? " +
                "(You can also do this later from the menu: Beastbound > Create Prototype Scene.)",
                "Create Scene", "Not Now", "Don't Ask Again");
            if (choice == 0) CreatePrototypeScene();
            else if (choice == 2) EditorPrefs.SetBool(DontAskKey, true);
        }

        [MenuItem("Beastbound/Create Prototype Scene", priority = 0)]
        public static void CreatePrototypeScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Replace scene?",
                    $"{ScenePath} already exists. Replace it with a fresh prototype scene?", "Replace", "Cancel"))
            {
                return;
            }

            EnsureFolder("Assets/Beastbound/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var bootstrapGo = new GameObject("Beastbound");
            var bootstrap = bootstrapGo.AddComponent<GameBootstrap>();
            AssignDataAssetsIfPresent(bootstrap);

            // Give the default camera sensible far plane / name; GameBootstrap finishes the setup on Play.
            var cam = Camera.main;
            if (cam != null)
            {
                cam.farClipPlane = 900f;
                cam.transform.position = new Vector3(0f, 12f, -30f);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            Selection.activeGameObject = bootstrapGo;
            Debug.Log($"[Beastbound] Created {ScenePath}. Press Play to start.");
        }

        [MenuItem("Beastbound/Open Prototype Scene", priority = 1)]
        public static void OpenPrototypeScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (EditorUtility.DisplayDialog("Scene not found", "The prototype scene does not exist yet. Create it now?", "Create", "Cancel"))
                    CreatePrototypeScene();
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Beastbound/Create Editable Creature Data", priority = 20)]
        public static void CreateCreatureDataAssets()
        {
            EnsureFolder(DataFolder);
            string speciesPath = DataFolder + "/Species_Pebbloom.asset";
            if (File.Exists(speciesPath))
            {
                EditorUtility.DisplayDialog("Already exists", $"{speciesPath} already exists. Edit it in the Inspector.", "OK");
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(speciesPath);
                return;
            }

            var species = DefaultContent.CreatePebbloom();
            // Save each attack as its own asset so it can be reused by other species.
            for (int i = 0; i < species.attacks.Length; i++)
            {
                var attack = species.attacks[i];
                string attackPath = $"{DataFolder}/{attack.name}.asset";
                AssetDatabase.CreateAsset(attack, attackPath);
                species.attacks[i] = AssetDatabase.LoadAssetAtPath<AttackDefinition>(attackPath);
            }
            AssetDatabase.CreateAsset(species, speciesPath);
            AssetDatabase.SaveAssets();

            var bootstrap = Object.FindAnyObjectByType<GameBootstrap>();
            if (bootstrap != null)
            {
                AssignDataAssetsIfPresent(bootstrap);
                EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
            }
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(speciesPath);
            Debug.Log("[Beastbound] Created editable creature data in " + DataFolder +
                      ". Changes to these assets are used the next time you press Play.");
        }

        [MenuItem("Beastbound/Open Save Folder", priority = 40)]
        public static void OpenSaveFolder()
        {
            string dir = Path.Combine(Application.persistentDataPath, "Saves");
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        [MenuItem("Beastbound/Delete Save Data", priority = 41)]
        public static void DeleteSaveData()
        {
            if (!EditorUtility.DisplayDialog("Delete save data?", "This deletes your Beastbound journey and settings on this computer.", "Delete", "Cancel"))
                return;
            string dir = Path.Combine(Application.persistentDataPath, "Saves");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Debug.Log("[Beastbound] Save data deleted.");
        }

        private static void AssignDataAssetsIfPresent(GameBootstrap bootstrap)
        {
            if (!AssetDatabase.IsValidFolder(DataFolder)) return;
            var guids = AssetDatabase.FindAssets("t:CreatureSpecies", new[] { DataFolder });
            var species = guids.Select(g => AssetDatabase.LoadAssetAtPath<CreatureSpecies>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null).ToArray();
            if (species.Length == 0) return;
            Undo.RecordObject(bootstrap, "Assign creature data");
            bootstrap.speciesOverrides = species;
            EditorUtility.SetDirty(bootstrap);
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
