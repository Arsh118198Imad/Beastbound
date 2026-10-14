using System;
using Beastbound.Audio;
using Beastbound.CameraSystem;
using Beastbound.Collection;
using Beastbound.Combat;
using Beastbound.Creatures;
using Beastbound.Player;
using Beastbound.Quests;
using Beastbound.Saving;
using Beastbound.UI;
using Beastbound.World;
using UnityEngine;

namespace Beastbound.Core
{
    /// <summary>
    /// The only component the prototype scene needs. On Play it generates the world and creates every
    /// system: player, camera, creatures, combat, collection, audio, saving and UI.
    ///
    /// Menu "Beastbound > Create Prototype Scene" makes a scene containing just this object.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Optional designer-made species assets. Leave empty to use the built-in Pebbloom.")]
        public CreatureSpecies[] speciesOverrides = new CreatureSpecies[0];

        [Tooltip("Same seed = same world layout.")]
        public int worldSeed = 20261014;

        [Tooltip("Editor convenience: jump straight into a new journey without the title screen.")]
        public bool skipTitleScreen;

        private string _fatalError;

        private void Awake()
        {
            if (GameManager.Instance != null)
            {
                Debug.LogWarning("[Beastbound] A second GameBootstrap was found and disabled.");
                enabled = false;
                return;
            }

            try
            {
                Boot();
            }
            catch (Exception e)
            {
                _fatalError = e.Message;
                Debug.LogException(e);
                Time.timeScale = 1f;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Boot()
        {
            Time.timeScale = 1f;
            var sun = FindOrCreateSun();
            var cam = FindOrCreateCamera();

            var saves = new SaveSystem();
            var settings = saves.LoadSettings();
            var catalog = new CreatureCatalog(speciesOverrides);

            // World
            var world = new WorldBuilder(worldSeed).Build(transform, sun);

            // Systems (all on this GameObject; each is a small focused component)
            var gm = gameObject.AddComponent<GameManager>();
            var audio = gameObject.AddComponent<AudioManager>();
            audio.Initialize();
            var battle = gameObject.AddComponent<BattleController>();
            var collection = gameObject.AddComponent<CollectionManager>();
            var spawner = gameObject.AddComponent<CreatureSpawner>();
            var objectives = gameObject.AddComponent<ObjectiveTracker>();

            // Player + camera
            var player = PlayerController.Create(transform, world.PlayerSpawn, world.PlayerSpawnRotation);
            var rig = cam.GetComponent<ThirdPersonCamera>();
            if (rig == null) rig = cam.gameObject.AddComponent<ThirdPersonCamera>();
            player.CameraTransform = cam.transform;
            rig.SetTarget(player.transform, true);

            // UI
            var ui = UIManager.Create(transform);

            gm.Initialize(world, player, rig, battle, collection, spawner, objectives, audio, catalog, saves, settings, ui);
            spawner.SpawnWild(world, catalog, collection.IsNestCaptured);

            if (skipTitleScreen && Application.isEditor) gm.StartNewGame();
            else gm.EnterTitle();
        }

        private static Light FindOrCreateSun()
        {
            Light sun = RenderSettings.sun;
            if (sun == null || sun.type != LightType.Directional)
            {
                sun = null;
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l.type == LightType.Directional) { sun = l; break; }
                }
            }
            if (sun == null)
            {
                var go = new GameObject("Sun");
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.cullingMask &= ~Layers.PreviewMask; // the Bestiary portrait has its own lights
            return sun;
        }

        private static Camera FindOrCreateCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            if (FindAnyObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            cam.cullingMask &= ~Layers.PreviewMask;
            cam.clearFlags = CameraClearFlags.Skybox;
            return cam;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_fatalError)) return;
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Box(new Rect(40, 40, Screen.width - 80, 180),
                "Beastbound could not start:\n" + _fatalError +
                "\n\nOpen Window > General > Console for details, and see README.md > Troubleshooting.", style);
        }
    }
}
