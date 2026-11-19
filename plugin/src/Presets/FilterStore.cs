using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Presets
{
    /// <summary>A filter ready to use: its settings, and its colour part for building the lookup table.</summary>
    internal sealed class FilterEntry
    {
        internal string Name;
        internal PhotoFilterPreset Preset;
        internal FilterSpec Spec;
    }

    /// <summary>
    /// Loads the photo filters: LightGreen.filters.json (written with LightGreen's built-in filters the first time)
    /// and every .cube file in the LightGreen.filters folder, each of which becomes a filter named after the file.
    /// Both are checked once a second, so edits and new files show in game as soon as they're saved.
    /// </summary>
    internal static class FilterStore
    {
        internal const string NoFilter = "None";

        internal static string FilePath => Path.Combine(Paths.ConfigPath, "LightGreen.filters.json");
        internal static string FolderPath => Path.Combine(Paths.ConfigPath, "LightGreen.filters");

        /// <summary>Goes up by one whenever the filters change, so the lookup table is rebuilt.</summary>
        internal static int Revision { get; private set; }

        /// <summary>Every filter's name in F11 order, without None.</summary>
        internal static IReadOnlyList<string> Names => _names;

        private static readonly List<string> _names = new List<string>();
        private static readonly Dictionary<string, FilterEntry> _entries = new Dictionary<string, FilterEntry>(StringComparer.OrdinalIgnoreCase);
        private static FilterFile _file = DefaultFilters.Create();
        private static bool _fileWasEmpty;
        private static DateTime _loadedWriteTime;
        private static string _folderFingerprint = "";
        private static float _nextCheck;
        private static int _fromJson;
        private static ManualLogSource _log;

        internal static void Load(ManualLogSource log)
        {
            _log = log;
            try
            {
                if (!Directory.Exists(FolderPath)) Directory.CreateDirectory(FolderPath);
            }
            catch (Exception e)
            {
                _log.LogWarning($"Couldn't make the folder for your .cube filters ({FolderPath}): {e.Message}");
            }

            try
            {
                if (!File.Exists(FilePath))
                {
                    File.WriteAllText(FilePath, Json.Serialize(DefaultFilters.Create()));
                    _log.LogInfo($"Wrote the built-in photo filters to {FilePath}. Edit it while the game runs to change them.");
                }
                ReadFile();
                if (_fileWasEmpty)
                {
                    // A test build wrote this file with JsonUtility, which saved only "version".
                    File.WriteAllText(FilePath, Json.Serialize(DefaultFilters.Create()));
                    ReadFile();
                    _log.LogInfo($"Wrote the built-in photo filters to {FilePath} (the file had only a version number).");
                }
                if (_file.version < DefaultFilters.Version)
                {
                    string backup = FilePath + $".v{_file.version}.bak";
                    File.Copy(FilePath, backup, true);
                    File.WriteAllText(FilePath, Json.Serialize(DefaultFilters.Create()));
                    ReadFile();
                    _log.LogInfo($"Updated the photo filters to LightGreen's new built-in ones (version {DefaultFilters.Version}). " +
                                 $"Your previous file is at {backup}.");
                }
            }
            catch (Exception e)
            {
                _file = DefaultFilters.Create();
                _log.LogError($"Couldn't read {FilePath} ({e.Message}); using the built-in photo filters.");
            }

            Rebuild(announce: true);
        }

        /// <summary>Reloads the file or the folder if either changed. Call once a frame.</summary>
        internal static void Poll()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;

            bool changed = false;
            try
            {
                if (File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != _loadedWriteTime)
                {
                    ReadFile();
                    changed = true;
                }
            }
            catch (Exception e)
            {
                _loadedWriteTime = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : default;
                _log.LogError($"Couldn't reload the photo filters ({e.Message}); keeping the previous ones.");
            }

            if (FolderFingerprint() != _folderFingerprint) changed = true;
            if (!changed) return;
            Rebuild(announce: false);
            _log.LogInfo($"Photo filters reloaded: {Summary()}.");
        }

        /// <summary>The filter with this name, or null for None or a name that isn't there.</summary>
        internal static FilterEntry Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), NoFilter, StringComparison.OrdinalIgnoreCase)) return null;
            return _entries.TryGetValue(name.Trim(), out FilterEntry entry) ? entry : null;
        }

        private static void ReadFile()
        {
            _loadedWriteTime = File.GetLastWriteTimeUtc(FilePath);
            object tree = Json.Parse(File.ReadAllText(FilePath));
            var loaded = (FilterFile)Json.FromTree(typeof(FilterFile), tree, "");
            _fileWasEmpty = !Json.HasKey(tree, "filters");
            loaded.filters = loaded.filters ?? Array.Empty<PhotoFilterPreset>();
            _file = loaded;
        }

        private static void Rebuild(bool announce)
        {
            var cubes = ReadFolder();
            _names.Clear();
            _entries.Clear();

            foreach (PhotoFilterPreset preset in _file.filters)
            {
                if (preset == null || string.IsNullOrWhiteSpace(preset.name)) continue;
                string name = preset.name.Trim();
                if (string.Equals(name, NoFilter, StringComparison.OrdinalIgnoreCase) || _entries.ContainsKey(name))
                {
                    _log.LogWarning($"Photo filters: skipped a second filter called \"{name}\".");
                    continue;
                }
                CubeLut cube = null;
                if (!string.IsNullOrWhiteSpace(preset.lut))
                {
                    string file = Path.GetFileNameWithoutExtension(preset.lut.Trim());
                    if (!cubes.TryGetValue(file, out cube))
                        _log.LogWarning($"Photo filter \"{name}\": there's no readable {file}.cube in {FolderPath}; using the filter without it.");
                }
                Add(name, preset, cube);
            }

            _fromJson = _names.Count;

            // Each .cube file is a filter of its own too.
            foreach (KeyValuePair<string, CubeLut> cube in cubes.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
            {
                string name = _entries.ContainsKey(cube.Key) || string.Equals(cube.Key, NoFilter, StringComparison.OrdinalIgnoreCase)
                    ? cube.Key + " (LUT)" : cube.Key;
                if (_entries.ContainsKey(name)) continue;
                Add(name, new PhotoFilterPreset { name = name, lut = cube.Key }, cube.Value);
            }

            Revision++;
            if (announce) _log.LogInfo($"Photo filters: {Summary()}.");
        }

        private static string Summary() =>
            $"{_fromJson} in {Path.GetFileName(FilePath)}, {_names.Count - _fromJson} from .cube files in {Path.GetFileName(FolderPath)}";

        private static void Add(string name, PhotoFilterPreset preset, CubeLut cube)
        {
            _entries[name] = new FilterEntry { Name = name, Preset = preset, Spec = ToSpec(preset, cube) };
            _names.Add(name);
        }

        /// <summary>The .cube files in the folder, read, by file name without .cube. Unreadable ones are logged and left out.</summary>
        private static Dictionary<string, CubeLut> ReadFolder()
        {
            var cubes = new Dictionary<string, CubeLut>(StringComparer.OrdinalIgnoreCase);
            _folderFingerprint = FolderFingerprint();
            if (!Directory.Exists(FolderPath)) return cubes;
            string[] files;
            try
            {
                files = Directory.GetFiles(FolderPath, "*.cube");
            }
            catch (Exception e)
            {
                _log.LogWarning($"Couldn't list {FolderPath}: {e.Message}");
                return cubes;
            }
            foreach (string path in files)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    cubes[name] = CubeLut.Parse(File.ReadAllText(path));
                }
                catch (Exception e)
                {
                    _log.LogWarning($"Skipped the photo filter {Path.GetFileName(path)}: {e.Message}.");
                }
            }
            return cubes;
        }

        /// <summary>The .cube files' names, sizes and times, to notice a file added, removed or saved.</summary>
        private static string FolderFingerprint()
        {
            try
            {
                if (!Directory.Exists(FolderPath)) return "";
                var sb = new StringBuilder();
                foreach (string path in Directory.GetFiles(FolderPath, "*.cube").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(path);
                    sb.Append(info.Name).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append(';');
                }
                return sb.ToString();
            }
            catch
            {
                return _folderFingerprint;
            }
        }

        internal static FilterSpec ToSpec(PhotoFilterPreset p, CubeLut cube) => new FilterSpec
        {
            Cube = cube,
            ChannelMixer = p.channelMixer != null && p.channelMixer.Length == 9 ? p.channelMixer : null,
            Warmth = Clamp(p.warmth, -1f, 1f),
            Tint = Clamp(p.tint, -1f, 1f),
            Brightness = Clamp(p.brightness, -1f, 1f),
            Contrast = Clamp(p.contrast, -1f, 1f),
            Saturation = Clamp(p.saturation, -1f, 1f),
            Master = Curve.FromPairs(p.curve),
            Red = Curve.FromPairs(p.curveRed),
            Green = Curve.FromPairs(p.curveGreen),
            Blue = Curve.FromPairs(p.curveBlue),
            SplitShadows = ToRgb(p.splitShadows),
            SplitHighlights = ToRgb(p.splitHighlights),
            SplitBalance = Clamp(p.splitBalance, -1f, 1f),
            SplitStrength = Clamp(p.splitStrength, 0f, 1f),
            Gradient = p.gradient != null && p.gradient.Length >= 2 ? p.gradient.Select(ToRgb).ToArray() : null,
            GradientStrength = Clamp(p.gradientStrength, 0f, 1f),
            Fade = Clamp(p.fade, 0f, 1f),
            Posterize = p.posterize >= 2 ? Math.Min(p.posterize, 64) : 0,
        };

        private static Rgb ToRgb(Color c) => new Rgb(c.r, c.g, c.b);

        private static float Clamp(float v, float min, float max) => float.IsNaN(v) ? 0f : Mathf.Clamp(v, min, max);
    }
}
