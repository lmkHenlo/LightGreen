using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Presets
{
    /// <summary>
    /// Loads the presets file (BepInEx/config/LightGreen.presets.json), writing LightGreen's defaults
    /// the first time. The file is checked once a second, so edits show in game as soon as it's saved.
    /// </summary>
    internal static class PresetStore
    {
        internal static string FilePath => Path.Combine(Paths.ConfigPath, "LightGreen.presets.json");

        private static PresetFile _file = DefaultPresets.Create();
        private static bool _fileWasEmpty;
        private static DateTime _loadedWriteTime;
        private static float _nextCheck;
        private static ManualLogSource _log;

        /// <summary>Raised after the file was reloaded, so systems can apply the new values.</summary>
        internal static event Action Reloaded;

        internal static void Load(ManualLogSource log)
        {
            _log = log;
            try
            {
                if (!File.Exists(FilePath))
                {
                    File.WriteAllText(FilePath, Json.Serialize(DefaultPresets.Create()));
                    _log.LogInfo($"Wrote the default presets to {FilePath}. Edit it while the game runs to tune the looks.");
                }
                Read();
                if (_file.version < DefaultPresets.Version || _fileWasEmpty)
                {
                    string backup = FilePath + $".v{_file.version}.bak";
                    File.Copy(FilePath, backup, true);
                    File.WriteAllText(FilePath, Json.Serialize(DefaultPresets.Create()));
                    Read();
                    _log.LogInfo(_fileWasEmpty
                        ? $"Wrote the full presets to {FilePath}: LightGreen 0.1.1 and earlier saved only the version number there. " +
                          $"The old file is at {backup}."
                        : $"Updated the presets to LightGreen's new defaults (version {DefaultPresets.Version}). Your previous file is at {backup}.");
                }
            }
            catch (Exception e)
            {
                _file = DefaultPresets.Create();
                _log.LogError($"Couldn't read {FilePath} ({e.Message}); using the built-in presets.");
            }
        }

        /// <summary>Reloads the file if it changed. Call once a frame.</summary>
        internal static void Poll()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            try
            {
                if (!File.Exists(FilePath) || File.GetLastWriteTimeUtc(FilePath) == _loadedWriteTime) return;
                Read();
                _log.LogInfo("Presets reloaded.");
                Reloaded?.Invoke();
            }
            catch (Exception e)
            {
                _loadedWriteTime = File.GetLastWriteTimeUtc(FilePath);
                _log.LogError($"Couldn't reload the presets ({e.Message}); keeping the previous ones.");
            }
        }

        private static void Read()
        {
            _loadedWriteTime = File.GetLastWriteTimeUtc(FilePath);
            object tree = Json.Parse(File.ReadAllText(FilePath));
            var loaded = (PresetFile)Json.FromTree(typeof(PresetFile), tree, "");
            // LightGreen 0.1.1 and earlier wrote this file with JsonUtility, which saved only "version".
            _fileWasEmpty = !Json.HasKey(tree, "biomes") && !Json.HasKey(tree, "times");

            // Anything the file leaves out keeps LightGreen's default, so an older file keeps working.
            PresetFile defaults = DefaultPresets.Create();
            var biomes = new List<BiomePreset>(loaded.biomes ?? Array.Empty<BiomePreset>());
            foreach (BiomePreset b in defaults.biomes)
                if (!biomes.Any(x => string.Equals(x.biome, b.biome, StringComparison.OrdinalIgnoreCase))) biomes.Add(b);
            var times = new List<TimePreset>(loaded.times ?? Array.Empty<TimePreset>());
            foreach (TimePreset t in defaults.times)
                if (!times.Any(x => string.Equals(x.name, t.name, StringComparison.OrdinalIgnoreCase))) times.Add(t);
            loaded.biomes = biomes.ToArray();
            loaded.times = times.ToArray();
            _file = loaded;
        }

        internal static BiomePreset ForBiome(Biome biome)
        {
            string name = biome.ToString();
            return _file.biomes.FirstOrDefault(b => string.Equals(b.biome, name, StringComparison.OrdinalIgnoreCase))
                   ?? _file.biomes.FirstOrDefault(b => string.Equals(b.biome, "Unknown", StringComparison.OrdinalIgnoreCase))
                   ?? new BiomePreset();
        }

        internal static TimePreset ForTime(TimeOfDay time)
        {
            string name = time.ToString();
            return _file.times.FirstOrDefault(t => string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                   ?? new TimePreset { name = name };
        }
    }
}
