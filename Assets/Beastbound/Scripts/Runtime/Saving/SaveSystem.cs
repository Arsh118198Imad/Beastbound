using System;
using System.IO;
using UnityEngine;

namespace Beastbound.Saving
{
    /// <summary>
    /// Local JSON saves in the platform's per-user data folder (Application.persistentDataPath).
    ///
    /// Privacy promise (in response to complaints about other games): Beastbound only ever reads and writes
    /// its own two files in its own folder. No accounts, no telemetry, no network calls, no anti-cheat.
    ///
    /// Writes are atomic (temp file + replace) and keep a .bak copy, so a crash during saving can never
    /// corrupt the journey.
    /// </summary>
    public class SaveSystem
    {
        public const string SaveFileName = "journey.json";
        public const string SettingsFileName = "settings.json";

        private readonly string _directory;

        public SaveSystem(string directory = null)
        {
            _directory = string.IsNullOrEmpty(directory)
                ? Path.Combine(Application.persistentDataPath, "Saves")
                : directory;
        }

        public string Directory => _directory;
        public string SavePath => Path.Combine(_directory, SaveFileName);
        public string SettingsPath => Path.Combine(_directory, SettingsFileName);

        public bool HasSave => File.Exists(SavePath) || File.Exists(SavePath + ".bak");

        // ------------------------------------------------------------------ Journey

        public bool Save(SaveData data, out string error)
        {
            error = null;
            if (data == null)
            {
                error = "Nothing to save.";
                return false;
            }
            data.version = SaveData.CurrentVersion;
            data.savedAtUtc = DateTime.UtcNow.ToString("o");
            return WriteAtomic(SavePath, JsonUtility.ToJson(data, true), out error);
        }

        public SaveData Load(out string error)
        {
            error = null;
            var data = TryRead<SaveData>(SavePath, out error);
            if (data == null && File.Exists(SavePath + ".bak"))
            {
                Debug.LogWarning($"[Beastbound] Primary save unreadable ({error}); trying backup.");
                data = TryRead<SaveData>(SavePath + ".bak", out error);
            }
            if (data == null) return null;
            Migrate(data);
            return data;
        }

        public void DeleteSave()
        {
            TryDelete(SavePath);
            TryDelete(SavePath + ".bak");
            TryDelete(SavePath + ".tmp");
        }

        private static void Migrate(SaveData data)
        {
            if (data.collection == null) data.collection = new System.Collections.Generic.List<Creatures.CreatureRecord>();
            if (data.capturedNests == null) data.capturedNests = new System.Collections.Generic.List<string>();
            if (data.seenSpecies == null) data.seenSpecies = new System.Collections.Generic.List<string>();
            if (float.IsNaN(data.playerX) || float.IsNaN(data.playerY) || float.IsNaN(data.playerZ))
            {
                data.playerX = data.playerY = data.playerZ = 0f;
            }
            // Future: if (data.version < 2) { ...upgrade... }
            data.version = SaveData.CurrentVersion;
        }

        // ------------------------------------------------------------------ Settings

        public SettingsData LoadSettings()
        {
            var s = TryRead<SettingsData>(SettingsPath, out _) ?? new SettingsData();
            s.Sanitize();
            return s;
        }

        public bool SaveSettings(SettingsData settings, out string error)
        {
            if (settings == null)
            {
                error = "No settings.";
                return false;
            }
            settings.Sanitize();
            return WriteAtomic(SettingsPath, JsonUtility.ToJson(settings, true), out error);
        }

        // ------------------------------------------------------------------ IO helpers

        private bool WriteAtomic(string path, string json, out string error)
        {
            error = null;
            string tmp = path + ".tmp";
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                File.WriteAllText(tmp, json);
                if (File.Exists(path))
                {
                    File.Copy(path, path + ".bak", true);
                    File.Delete(path);
                }
                File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError($"[Beastbound] Could not write {path}: {e.Message}");
                TryDelete(tmp);
                return false;
            }
        }

        private static T TryRead<T>(string path, out string error) where T : class
        {
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    error = "File not found.";
                    return null;
                }
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "File is empty.";
                    return null;
                }
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Beastbound] Could not delete {path}: {e.Message}");
            }
        }
    }
}
