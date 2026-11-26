using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Presets
{
    /// <summary>
    /// A filter ready to use: its settings, and its colour part for building the lookup table. A .cube file is read
    /// only when the filter is used (<see cref="TryGetSpec"/>), and only the one in use is kept: with hundreds of .cube
    /// files in the folder, reading them all at start held hundreds of MB, and the game hitched on its longer garbage
    /// collections. The read runs on a worker thread (a 65-point cube takes a tenth of a second or more), so picking a
    /// filter never stops the game; until it's ready, the filter before stays on screen.
    /// </summary>
    internal sealed class FilterEntry
    {
        internal string Name;
        internal PhotoFilterPreset Preset;
        internal string CubePath;   // the .cube file it uses, or null
        private FilterSpec _spec;
        private Task<CubeLut> _reading;

        /// <summary>
        /// The colour part, or false while its .cube is still being read (call again next frame). A .cube that can't be
        /// read is logged once it's done, and the filter is used without it.
        /// </summary>
        internal bool TryGetSpec(out FilterSpec spec)
        {
            spec = _spec;
            if (spec != null) return true;
            if (CubePath == null)
            {
                spec = _spec = FilterStore.ToSpec(Preset, null);
                return true;
            }
            if (_reading == null)
            {
                string path = CubePath;
                _reading = Task.Run(() => FilterStore.ReadCube(path));
                return false;
            }
            if (!_reading.IsCompleted) return false;

            CubeLut cube = null;
            if (_reading.Status == TaskStatus.RanToCompletion) cube = _reading.Result;
            else FilterStore.CubeFailed(this, _reading.Exception?.GetBaseException());
            _reading = null;
            spec = _spec = FilterStore.ToSpec(Preset, cube);
            FilterStore.CubeInUse(this);
            return true;
        }

        /// <summary>Lets go of the colour part (and its .cube), to be read again if used again.</summary>
        internal void Forget()
        {
            _spec = null;
            _reading = null;   // a read still going finishes on its own and is dropped
        }
    }

    /// <summary>
    /// Loads the photo filters: LightGreen.filters.json (written with LightGreen's built-in filters the first time)
    /// and every .cube file in the LightGreen.filters folder, each of which becomes a filter named after the file. The
    /// .cube files are only listed here; one is read when its filter is used.
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
        private static JsonFile _disk;
        private static JsonFile Disk => _disk ?? (_disk = new JsonFile(FilePath));
        private static FilterFile _file = DefaultFilters.Create();
        private static bool _fileWasEmpty;
        private static string _folderFingerprint = "";
        // The .cube folder is looked at on a worker thread: listing it and reading each file's size and time took
        // 10-15 ms with 439 files, once a second, which was a steady hitch.
        private static Task<string> _folderCheck;
        private static volatile Exception _folderError;
        private static float _nextFolderCheck;
        private static int _fromJson;
        private static ManualLogSource _log;
        private static FilterEntry _withCube;   // the one filter whose .cube is in memory

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
                if (!Disk.Exists)
                {
                    Disk.Write(DefaultFilters.Create());
                    _log.LogInfo($"Wrote the built-in photo filters to {FilePath}. Edit it while the game runs to change them.");
                }
                ReadFile();
                if (_fileWasEmpty)
                {
                    // A test build wrote this file with JsonUtility, which saved only "version".
                    Disk.Write(DefaultFilters.Create());
                    ReadFile();
                    _log.LogInfo($"Wrote the built-in photo filters to {FilePath} (the file had only a version number).");
                }
                if (_file.version < DefaultFilters.Version)
                {
                    string backup = Disk.Backup(_file.version);
                    Disk.Write(DefaultFilters.Create());
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
            bool changed = false;
            try
            {
                if (Disk.Edited())
                {
                    ReadFile();
                    changed = true;
                }
            }
            catch (Exception e)
            {
                Disk.Seen();
                _log.LogError($"Couldn't reload the photo filters ({e.Message}); keeping the previous ones.");
            }

            if (FolderChanged()) changed = true;
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
            object tree = Disk.Read();
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
            _withCube = null;

            foreach (PhotoFilterPreset preset in _file.filters)
            {
                if (preset == null || string.IsNullOrWhiteSpace(preset.name)) continue;
                string name = preset.name.Trim();
                if (string.Equals(name, NoFilter, StringComparison.OrdinalIgnoreCase) || _entries.ContainsKey(name))
                {
                    _log.LogWarning($"Photo filters: skipped a second filter called \"{name}\".");
                    continue;
                }
                string cube = null;
                if (!string.IsNullOrWhiteSpace(preset.lut))
                {
                    string file = Path.GetFileNameWithoutExtension(preset.lut.Trim());
                    if (!cubes.TryGetValue(file, out cube))
                        _log.LogWarning($"Photo filter \"{name}\": there's no {file}.cube in {FolderPath}; using the filter without it.");
                }
                Add(name, preset, cube);
            }

            _fromJson = _names.Count;

            // Each .cube file is a filter of its own too.
            foreach (KeyValuePair<string, string> cube in cubes.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
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

        private static void Add(string name, PhotoFilterPreset preset, string cubePath)
        {
            _entries[name] = new FilterEntry { Name = name, Preset = preset, CubePath = cubePath };
            _names.Add(name);
        }

        /// <summary>Reads and parses a .cube file, line by line and size-capped. Runs on a worker thread: nothing of Unity's.</summary>
        internal static CubeLut ReadCube(string path)
        {
            using (StreamReader reader = SafeFile.OpenText(path, SafeFile.MaxCubeBytes)) return CubeLut.Parse(reader);
        }

        internal static void CubeFailed(FilterEntry entry, Exception e) =>
            _log?.LogWarning($"Photo filter \"{entry.Name}\": couldn't read {Path.GetFileName(entry.CubePath)} ({e?.Message}); " +
                             "using the filter without it.");

        /// <summary>The .cube of the filter used before is let go, so only one is in memory.</summary>
        internal static void CubeInUse(FilterEntry entry)
        {
            if (_withCube != null && _withCube != entry) _withCube.Forget();
            _withCube = entry;
        }

        /// <summary>The .cube files in the folder, by file name without .cube: their paths, not read yet.</summary>
        private static Dictionary<string, string> ReadFolder()
        {
            var cubes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            foreach (string path in files) cubes[Path.GetFileNameWithoutExtension(path)] = path;
            return cubes;
        }

        /// <summary>
        /// Whether the last look at the folder (on a worker thread, every 2 s) found it changed; starts the next look.
        /// Only file names, sizes and times are read there, nothing of Unity's.
        /// </summary>
        private static bool FolderChanged()
        {
            if (_folderCheck != null)
            {
                if (!_folderCheck.IsCompleted) return false;
                string found = _folderCheck.Status == TaskStatus.RanToCompletion ? _folderCheck.Result : _folderFingerprint;
                _folderCheck = null;
                Exception error = _folderError;
                if (error != null)
                {
                    _folderError = null;
                    CoreLog.FailedOnce("filters:folder", $"Photo filters: couldn't look through {FolderPath} for changes", error);
                }
                return found != _folderFingerprint;
            }
            if (Time.unscaledTime < _nextFolderCheck) return false;
            _nextFolderCheck = Time.unscaledTime + 2f;
            string previous = _folderFingerprint;
            _folderCheck = Task.Run(() => FolderFingerprint(previous));
            return false;
        }

        /// <summary>The .cube files' names, sizes and times, to notice a file added, removed or saved.</summary>
        private static string FolderFingerprint() => FolderFingerprint(_folderFingerprint);

        /// <param name="fallback">What to return when the folder can't be read (the last fingerprint).</param>
        private static string FolderFingerprint(string fallback)
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
            catch (Exception e)
            {
                _folderError = e;   // logged on the game's thread (FolderChanged)
                return fallback;
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
