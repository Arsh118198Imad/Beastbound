using System.Collections.Generic;
using Beastbound.Core;
using UnityEngine;

namespace Beastbound.Audio
{
    /// <summary>
    /// Plays sound effects from a small pool of AudioSources, plus crossfaded ambience (day/night) and music
    /// (exploration/battle). Volumes come from Settings. Static helpers are safe to call even when no
    /// AudioManager exists (they simply do nothing).
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        private const int PoolSize = 16;

        private static AudioManager _instance;
        private readonly Dictionary<SfxId, AudioClip> _clips = new Dictionary<SfxId, AudioClip>();
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private int _next;
        private AudioSource _ambDay, _ambNight, _musicExplore, _musicBattle;
        private float _sfxVolume = 0.9f;
        private float _musicVolume = 0.6f;
        private float _battleBlend;
        private float _nightBlend;

        public static AudioManager Instance => _instance;

        public void Initialize()
        {
            _instance = this;
            foreach (SfxId id in System.Enum.GetValues(typeof(SfxId)))
            {
                _clips[id] = ProceduralAudio.Build(id);
            }
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Sfx" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 2f;
                src.maxDistance = 40f;
                src.dopplerLevel = 0f;
                _pool.Add(src);
            }
            _ambDay = CreateLoop("AmbienceDay", ProceduralAudio.BuildDayAmbience());
            _ambNight = CreateLoop("AmbienceNight", ProceduralAudio.BuildNightAmbience());
            _musicExplore = CreateLoop("MusicExplore", ProceduralAudio.BuildExploreMusic());
            _musicBattle = CreateLoop("MusicBattle", ProceduralAudio.BuildBattleMusic());
        }

        private AudioSource CreateLoop(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = 0f;
            src.ignoreListenerPause = false;
            src.Play();
            return src;
        }

        public void SetVolumes(float master, float sfx, float music)
        {
            AudioListener.volume = Mathf.Clamp01(master);
            _sfxVolume = Mathf.Clamp01(sfx);
            _musicVolume = Mathf.Clamp01(music);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            bool battle = gm != null && gm.Battle != null && gm.Battle.InBattle && gm.State == GameState.Playing;
            bool night = gm != null && gm.World != null && gm.World.DayNight != null && gm.World.DayNight.Daylight < 0.3f;
            float dt = Time.unscaledDeltaTime;
            _battleBlend = Mathf.MoveTowards(_battleBlend, battle ? 1f : 0f, dt * 0.8f);
            _nightBlend = Mathf.MoveTowards(_nightBlend, night ? 1f : 0f, dt * 0.2f);

            float menuDuck = gm != null && (gm.State == GameState.Paused || gm.State == GameState.Defeat) ? 0.5f : 1f;
            if (_musicExplore != null) _musicExplore.volume = _musicVolume * 0.55f * (1f - _battleBlend) * menuDuck;
            if (_musicBattle != null) _musicBattle.volume = _musicVolume * 0.6f * _battleBlend * menuDuck;
            if (_ambDay != null) _ambDay.volume = _sfxVolume * 0.5f * (1f - _nightBlend);
            if (_ambNight != null) _ambNight.volume = _sfxVolume * 0.5f * _nightBlend;
        }

        private void Play(SfxId id, Vector3? position, float volume, float pitch)
        {
            if (!_clips.TryGetValue(id, out var clip) || clip == null || _pool.Count == 0) return;
            var src = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            src.transform.position = position ?? Vector3.zero;
            src.spatialBlend = position.HasValue ? 0.85f : 0f;
            src.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
            src.volume = Mathf.Clamp01(volume * _sfxVolume);
            src.clip = clip;
            src.Play();
        }

        /// <summary>Positional sound effect.</summary>
        public static void PlaySfx(SfxId id, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            if (_instance != null) _instance.Play(id, position, volume, pitch);
        }

        /// <summary>Non-positional sound (UI, stingers).</summary>
        public static void PlayUi(SfxId id, float volume = 0.8f, float pitch = 1f)
        {
            if (_instance != null) _instance.Play(id, null, volume, pitch);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;
    }
}
