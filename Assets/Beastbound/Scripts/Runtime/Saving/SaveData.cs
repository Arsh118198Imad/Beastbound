using System;
using System.Collections.Generic;
using Beastbound.Creatures;

namespace Beastbound.Saving
{
    /// <summary>
    /// Everything needed to resume a journey. Plain serialisable data (JsonUtility). Bump
    /// <see cref="CurrentVersion"/> and add a migration in SaveSystem when the format changes.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string savedAtUtc = "";
        public float playTimeSeconds;

        public float playerX, playerY, playerZ;
        public float playerYaw;
        public float playerHealthFraction = 1f;
        public float timeOfDay = 9.5f;

        public List<CreatureRecord> collection = new List<CreatureRecord>();
        public string partnerUid = "";
        public List<string> capturedNests = new List<string>();
        public List<string> seenSpecies = new List<string>();

        public int objectiveStep;
        public bool bestiaryOpened;
    }

    /// <summary>Player preferences, stored separately from the journey so "New Game" never resets them.</summary>
    [Serializable]
    public class SettingsData
    {
        public static readonly int[] FrameCaps = { 30, 60, 120, 144, 0 };
        public static readonly string[] QualityNames = { "Low", "Medium", "High", "Ultra" };

        public int version = 1;
        public float mouseSensitivity = 2.2f;
        public bool invertY;
        public float fieldOfView = 60f;
        public float masterVolume = 0.8f;
        public float sfxVolume = 0.9f;
        public float musicVolume = 0.6f;
        public int quality = 2;
        public bool fullscreen = true;
        public bool vsync = true;
        public int frameCapIndex = 1;
        public bool showFps;
        public bool showControlsHint = true;
        public float dayLengthMinutes = 24f;

        public void Sanitize()
        {
            mouseSensitivity = Clamp(mouseSensitivity, 0.2f, 8f, 2.2f);
            fieldOfView = Clamp(fieldOfView, 50f, 100f, 60f);
            masterVolume = Clamp(masterVolume, 0f, 1f, 0.8f);
            sfxVolume = Clamp(sfxVolume, 0f, 1f, 0.9f);
            musicVolume = Clamp(musicVolume, 0f, 1f, 0.6f);
            dayLengthMinutes = Clamp(dayLengthMinutes, 2f, 120f, 24f);
            if (quality < 0 || quality >= QualityNames.Length) quality = 2;
            if (frameCapIndex < 0 || frameCapIndex >= FrameCaps.Length) frameCapIndex = 1;
        }

        private static float Clamp(float v, float min, float max, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
            return v < min ? min : (v > max ? max : v);
        }
    }
}
