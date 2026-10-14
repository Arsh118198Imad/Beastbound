using System;
using System.Collections;
using Beastbound.Audio;
using Beastbound.CameraSystem;
using Beastbound.Collection;
using Beastbound.Combat;
using Beastbound.Controls;
using Beastbound.Creatures;
using Beastbound.Player;
using Beastbound.Quests;
using Beastbound.Saving;
using Beastbound.UI;
using Beastbound.World;
using UnityEngine;

namespace Beastbound.Core
{
    public enum GameState
    {
        Boot,
        Title,
        Playing,
        Paused,
        Defeat
    }

    /// <summary>
    /// Owns the high-level game flow (title -> playing <-> paused, defeat -> retry) and holds references to
    /// every system. Systems talk to each other through this hub instead of searching the scene.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        private const float AutosaveInterval = 180f;

        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Boot;
        public WorldContext World { get; private set; }
        public PlayerController Player { get; private set; }
        public ThirdPersonCamera CameraRig { get; private set; }
        public BattleController Battle { get; private set; }
        public CollectionManager Collection { get; private set; }
        public CreatureSpawner Spawner { get; private set; }
        public ObjectiveTracker Objectives { get; private set; }
        public AudioManager Audio { get; private set; }
        public CreatureCatalog Catalog { get; private set; }
        public SaveSystem Saves { get; private set; }
        public SettingsData Settings { get; private set; }
        public UIManager UI { get; private set; }

        public float PlayTimeSeconds { get; private set; }
        public bool SessionActive { get; private set; }
        public bool HasSave => Saves != null && Saves.HasSave;

        public event Action<GameState> StateChanged;

        private float _nextAutosave;
        private Coroutine _hitStop;

        public void Initialize(WorldContext world, PlayerController player, ThirdPersonCamera cameraRig,
            BattleController battle, CollectionManager collection, CreatureSpawner spawner, ObjectiveTracker objectives,
            AudioManager audio, CreatureCatalog catalog, SaveSystem saves, SettingsData settings, UIManager ui)
        {
            Instance = this;
            World = world;
            Player = player;
            CameraRig = cameraRig;
            Battle = battle;
            Collection = collection;
            Spawner = spawner;
            Objectives = objectives;
            Audio = audio;
            Catalog = catalog;
            Saves = saves;
            Settings = settings ?? new SettingsData();
            UI = ui;

            if (Player != null && Player.Health != null) Player.Health.Died += OnPlayerDefeated;
            if (Collection != null) Collection.CreatureBound += _ => Objectives?.Refresh();
            if (Battle != null) Battle.BattleEnded += OnBattleEnded;
            ApplySettings(Settings, false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (Player != null && Player.Health != null) Player.Health.Died -= OnPlayerDefeated;
            Time.timeScale = 1f;
        }

        // ------------------------------------------------------------------ State

        private void SetState(GameState next)
        {
            State = next;
            switch (next)
            {
                case GameState.Paused: Time.timeScale = 0f; break;
                case GameState.Defeat: Time.timeScale = 0.3f; break;
                default: Time.timeScale = 1f; break;
            }
            GameInput.GameplayEnabled = next == GameState.Playing;
            bool freeCursor = next != GameState.Playing;
            Cursor.lockState = freeCursor ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = freeCursor;
            if (CameraRig != null) CameraRig.SetMode(next == GameState.Title ? ThirdPersonCamera.Mode.TitleOrbit : ThirdPersonCamera.Mode.Gameplay);
            StateChanged?.Invoke(next);
            UI?.OnGameStateChanged(next);
        }

        public void EnterTitle()
        {
            SetState(GameState.Title);
        }

        private void Update()
        {
            GameInput.Tick();
            if (State == GameState.Playing)
            {
                PlayTimeSeconds += Time.unscaledDeltaTime;
                if (Time.unscaledTime >= _nextAutosave)
                {
                    _nextAutosave = Time.unscaledTime + AutosaveInterval;
                    SaveGame(true);
                }
                // Ignore keys on the frame a menu closed, so one Esc press cannot close and re-open the pause menu.
                bool menuJustChanged = UI != null && UI.MenuChangedFrame == Time.frameCount;
                if (!menuJustChanged)
                {
                    if (GameInput.PausePressed) Pause();
                    else if (GameInput.BestiaryPressed) OpenBestiary();
                    else if (GameInput.HelpPressed) UI?.ToggleControlsOverlay();
                }
            }
        }

        private void OnApplicationQuit()
        {
            if (SessionActive && (State == GameState.Playing || State == GameState.Paused)) SaveGame(true);
        }

        // ------------------------------------------------------------------ Journey flow

        public void StartNewGame()
        {
            Collection.Clear();
            Objectives.ResetProgress();
            Battle.ResetAll(false);
            Spawner.DespawnAll();
            Spawner.SpawnWild(World, Catalog, Collection.IsNestCaptured);
            Player.Respawn(World.PlayerSpawn, World.PlayerSpawnRotation);
            World.DayNight?.SetHour(9.5f);
            PlayTimeSeconds = 0f;
            SessionActive = true;
            CameraRig.SetTarget(Player.transform, true);
            _nextAutosave = Time.unscaledTime + AutosaveInterval;
            SetState(GameState.Playing);
            GameEvents.Notify("Welcome to Sunmeadow Vale, Warden.", NotificationKind.Discovery);
            SaveGame(true);
        }

        public bool ContinueGame()
        {
            var data = Saves.Load(out string error);
            if (data == null)
            {
                GameEvents.Notify($"Could not load your journey: {error}", NotificationKind.Warning);
                return false;
            }

            Battle.ResetAll(false);
            Spawner.DespawnAll();
            Collection.Load(data.collection, data.partnerUid, data.capturedNests, data.seenSpecies);
            Spawner.SpawnWild(World, Catalog, Collection.IsNestCaptured);

            Vector3 pos = new Vector3(data.playerX, data.playerY, data.playerZ);
            Quaternion rot = Quaternion.Euler(0f, data.playerYaw, 0f);
            bool valid = World.IsInsidePlayArea(pos, 1f) && World.WaterDepth(pos) < 0.8f && pos.sqrMagnitude > 0.01f;
            if (!valid)
            {
                pos = World.PlayerSpawn;
                rot = World.PlayerSpawnRotation;
            }
            else
            {
                pos.y = Mathf.Max(pos.y, World.GroundHeight(pos.x, pos.z) + 0.1f);
            }
            Player.Respawn(pos, rot);
            Player.Health.SetFraction(Mathf.Clamp(data.playerHealthFraction, 0.25f, 1f));

            var partner = Collection.Partner;
            if (partner != null)
            {
                Vector3 cPos = pos - rot * Vector3.forward * 2f;
                World.TryFindWalkableNear(cPos, 3f, out cPos, 8);
                Spawner.SpawnCompanion(Catalog.Get(partner.speciesId), partner, cPos, rot);
            }

            Objectives.Load(data.objectiveStep, data.bestiaryOpened);
            World.DayNight?.SetHour(data.timeOfDay);
            PlayTimeSeconds = Mathf.Max(0f, data.playTimeSeconds);
            SessionActive = true;
            CameraRig.SetTarget(Player.transform, true);
            _nextAutosave = Time.unscaledTime + AutosaveInterval;
            SetState(GameState.Playing);
            GameEvents.Notify("Journey restored.", NotificationKind.Success);
            return true;
        }

        public bool SaveGame(bool silent)
        {
            if (!SessionActive || Saves == null || Player == null) return false;
            var p = Player.transform.position;
            var data = new SaveData
            {
                playTimeSeconds = PlayTimeSeconds,
                playerX = p.x,
                playerY = p.y,
                playerZ = p.z,
                playerYaw = Player.transform.eulerAngles.y,
                playerHealthFraction = Player.Health != null && !Player.Health.IsDead ? Player.Health.Fraction : 1f,
                timeOfDay = World != null && World.DayNight != null ? World.DayNight.hour : 9.5f,
                partnerUid = Collection.PartnerUid ?? "",
                objectiveStep = Objectives.Step,
                bestiaryOpened = Objectives.BestiaryOpened
            };
            foreach (var r in Collection.Records) data.collection.Add(r.Clone());
            data.capturedNests.AddRange(Collection.CapturedNests);
            data.seenSpecies.AddRange(Collection.SeenSpecies);

            bool ok = Saves.Save(data, out string error);
            if (!ok) GameEvents.Notify($"Saving failed: {error}", NotificationKind.Warning);
            else if (!silent)
            {
                GameEvents.Notify("Journey saved.", NotificationKind.Success);
                AudioManager.PlayUi(SfxId.Save);
            }
            return ok;
        }

        public void Pause()
        {
            if (State != GameState.Playing) return;
            SetState(GameState.Paused);
            UI?.OpenPauseMenu();
        }

        public void OpenBestiary()
        {
            if (State != GameState.Playing) return;
            SetState(GameState.Paused);
            UI?.OpenBestiary();
        }

        /// <summary>Called by the UI when the last menu closes.</summary>
        public void Resume()
        {
            if (State != GameState.Paused) return;
            SetState(GameState.Playing);
        }

        public void ReturnToTitle()
        {
            if (State == GameState.Defeat) RecoverFromDefeat();
            if (SessionActive) SaveGame(true);
            Player.Binder?.CancelBinding();
            Battle.ResetAll(false);
            Spawner.ResetAllToCalm();
            SessionActive = false;
            SetState(GameState.Title);
        }

        public void RetryAfterDefeat()
        {
            RecoverFromDefeat();
            SetState(GameState.Playing);
            GameEvents.Notify("The Waystone restored your strength.", NotificationKind.Info);
        }

        private void RecoverFromDefeat()
        {
            Battle.ResetAll(false);
            Spawner.ResetAllToCalm();
            Player.Respawn(World.PlayerSpawn, World.PlayerSpawnRotation);
            var companion = Spawner.Companion;
            if (companion != null)
            {
                Vector3 c = World.PlayerSpawn - World.PlayerSpawnRotation * Vector3.forward * 2f;
                World.TryFindWalkableNear(c, 3f, out c, 8);
                companion.Motor.Teleport(c + Vector3.up * 0.2f);
            }
            CameraRig.SetTarget(Player.transform, true);
        }

        public void QuitGame()
        {
            if (SessionActive && State != GameState.Title) SaveGame(true);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnPlayerDefeated()
        {
            if (State != GameState.Playing) return;
            Player.Binder?.CancelBinding();
            Battle.ResetAll(true);
            AudioManager.PlayUi(SfxId.Defeat);
            SetState(GameState.Defeat);
        }

        private void OnBattleEnded(BattleOutcome outcome, Creature creature)
        {
            if (outcome == BattleOutcome.Victory) UI?.ShowBanner("Victory!", "Bind the exhausted Pebbloom before it recovers.");
        }

        // ------------------------------------------------------------------ Settings

        public void ApplySettings(SettingsData settings, bool save)
        {
            if (settings == null) return;
            settings.Sanitize();
            Settings = settings;
            GraphicsSettingsApplier.Apply(settings);
            World?.Vegetation?.ApplyQuality(settings.quality);
            CameraRig?.ApplySettings(settings.mouseSensitivity, settings.invertY, settings.fieldOfView);
            Audio?.SetVolumes(settings.masterVolume, settings.sfxVolume, settings.musicVolume);
            if (World != null && World.DayNight != null) World.DayNight.dayLengthMinutes = settings.dayLengthMinutes;
            if (save && Saves != null) Saves.SaveSettings(settings, out _);
        }

        // ------------------------------------------------------------------ Juice

        /// <summary>Freezes time for a few milliseconds on heavy hits (a classic "impact" trick).</summary>
        public void HitStop(float seconds)
        {
            if (State != GameState.Playing || seconds <= 0f) return;
            if (_hitStop != null) StopCoroutine(_hitStop);
            _hitStop = StartCoroutine(HitStopRoutine(seconds));
        }

        private IEnumerator HitStopRoutine(float seconds)
        {
            Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(seconds);
            if (State == GameState.Playing) Time.timeScale = 1f;
            _hitStop = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }

    /// <summary>Maps the Low/Medium/High/Ultra setting and display options onto Unity's settings.</summary>
    public static class GraphicsSettingsApplier
    {
        public static void Apply(SettingsData s)
        {
            switch (s.quality)
            {
                case 0:
                    QualitySettings.shadows = ShadowQuality.HardOnly;
                    QualitySettings.shadowResolution = ShadowResolution.Low;
                    QualitySettings.shadowDistance = 45f;
                    QualitySettings.shadowCascades = 1;
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.lodBias = 0.7f;
                    break;
                case 1:
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowResolution = ShadowResolution.Medium;
                    QualitySettings.shadowDistance = 70f;
                    QualitySettings.shadowCascades = 2;
                    QualitySettings.antiAliasing = 2;
                    QualitySettings.lodBias = 1f;
                    break;
                case 2:
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowResolution = ShadowResolution.High;
                    QualitySettings.shadowDistance = 100f;
                    QualitySettings.shadowCascades = 4;
                    QualitySettings.antiAliasing = 4;
                    QualitySettings.lodBias = 1.5f;
                    break;
                default:
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
                    QualitySettings.shadowDistance = 150f;
                    QualitySettings.shadowCascades = 4;
                    QualitySettings.antiAliasing = 8;
                    QualitySettings.lodBias = 2f;
                    break;
            }

            QualitySettings.realtimeReflectionProbes = true; // sky reflections on water and crystals
            QualitySettings.vSyncCount = s.vsync ? 1 : 0;
            int cap = SettingsData.FrameCaps[Mathf.Clamp(s.frameCapIndex, 0, SettingsData.FrameCaps.Length - 1)];
            Application.targetFrameRate = s.vsync || cap <= 0 ? -1 : cap;

#if !UNITY_EDITOR
            var mode = s.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.fullScreenMode != mode) Screen.fullScreenMode = mode;
#endif
        }
    }
}
