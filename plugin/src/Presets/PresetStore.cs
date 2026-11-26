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

        private static JsonFile _disk;
        private static JsonFile Disk => _disk ?? (_disk = new JsonFile(FilePath));
        private static PresetFile _file = DefaultPresets.Create();
        private static bool _fileWasEmpty;
        private static ManualLogSource _log;

        /// <summary>Raised after the file was reloaded, so systems can apply the new values.</summary>
        internal static event Action Reloaded;

        internal static void Load(ManualLogSource log)
        {
            _log = log;
            try
            {
                if (!Disk.Exists)
                {
                    Disk.Write(DefaultPresets.Create());
                    _log.LogInfo($"Wrote the default presets to {FilePath}. Edit it while the game runs to tune the looks.");
                }
                Read();
                if (_file.version < DefaultPresets.Version || _fileWasEmpty)
                {
                    string backup = Disk.Backup(_file.version);
                    Disk.Write(DefaultPresets.Create());
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
                _byBiome = null;
                _byTime = null;
                _log.LogError($"Couldn't read {FilePath} ({e.Message}); using the built-in presets.");
            }
        }

        /// <summary>Reloads the file if it changed. Call once a frame.</summary>
        internal static void Poll()
        {
            if (!Disk.Edited()) return;
            try
            {
                Read();
                _log.LogInfo("Presets reloaded.");
                Reloaded?.Invoke();
            }
            catch (Exception e)
            {
                Disk.Seen();
                _log.LogError($"Couldn't reload the presets ({e.Message}); keeping the previous ones.");
            }
        }

        private static void Read()
        {
            object tree = Disk.Read();
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
            _byBiome = null;
            _byTime = null;
        }

        // Each biome's and time's preset, looked up once per load: several systems ask every frame, and looking up by name
        // each time made garbage (the enum's name and a closure) every frame.
        private static BiomePreset[] _byBiome;
        private static TimePreset[] _byTime;

        internal static BiomePreset ForBiome(Biome biome)
        {
            if (_byBiome == null) Index();
            int i = (int)biome;
            return i >= 0 && i < _byBiome.Length ? _byBiome[i] : _byBiome[(int)Biome.None];
        }

        internal static TimePreset ForTime(TimeOfDay time)
        {
            if (_byTime == null) Index();
            int i = (int)time;
            return i >= 0 && i < _byTime.Length ? _byTime[i] : _byTime[(int)TimeOfDay.HoleDefault];
        }

        /// <summary>The presets by biome and time: the file's by name, else its "Unknown" biome, else defaults.</summary>
        private static void Index()
        {
            BiomePreset unknown = _file.biomes.FirstOrDefault(b => string.Equals(b.biome, "Unknown", StringComparison.OrdinalIgnoreCase))
                                  ?? new BiomePreset();
            var biomes = (Biome[])Enum.GetValues(typeof(Biome));
            var byBiome = new BiomePreset[biomes.Max(b => (int)b) + 1];
            foreach (Biome biome in biomes)
            {
                string name = biome.ToString();
                byBiome[(int)biome] = _file.biomes.FirstOrDefault(b => string.Equals(b.biome, name, StringComparison.OrdinalIgnoreCase)) ?? unknown;
            }
            var times = (TimeOfDay[])Enum.GetValues(typeof(TimeOfDay));
            var byTime = new TimePreset[times.Max(t => (int)t) + 1];
            foreach (TimeOfDay time in times)
            {
                string name = time.ToString();
                byTime[(int)time] = _file.times.FirstOrDefault(t => string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                                    ?? new TimePreset { name = name };
            }
            _byBiome = byBiome;
            _byTime = byTime;
        }
    }
}
