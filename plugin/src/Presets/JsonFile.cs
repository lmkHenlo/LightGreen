using System;
using System.IO;
using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Presets
{
    /// <summary>
    /// One of LightGreen's JSON settings files in BepInEx/config (the presets, the photo filters): read with a size
    /// limit, written whole or not at all (<see cref="SafeFile"/>), backed up before LightGreen replaces it with newer
    /// defaults, and checked at most once a second for edits saved while the game runs.
    /// </summary>
    internal sealed class JsonFile
    {
        internal readonly string Path;
        private DateTime _readWriteTime;
        private float _nextCheck;

        internal JsonFile(string path) => Path = path;

        internal bool Exists => File.Exists(Path);

        /// <summary>The file parsed into a tree (see <see cref="Json.Parse"/>); remembers its time, for <see cref="Edited"/>.</summary>
        internal object Read()
        {
            _readWriteTime = File.GetLastWriteTimeUtc(Path);
            return Json.Parse(SafeFile.ReadText(Path, SafeFile.MaxSettingsBytes));
        }

        internal void Write(object value) => SafeFile.WriteText(Path, Json.Serialize(value));

        /// <summary>A copy of the file as it is, named after its version (".v3.bak"); returns its path.</summary>
        internal string Backup(int version)
        {
            string backup = Path + $".v{version}.bak";
            File.Copy(Path, backup, true);
            return backup;
        }

        /// <summary>True, at most once a second, when the file was saved since it was last read.</summary>
        internal bool Edited()
        {
            if (Time.unscaledTime < _nextCheck) return false;
            _nextCheck = Time.unscaledTime + 1f;
            return File.Exists(Path) && File.GetLastWriteTimeUtc(Path) != _readWriteTime;
        }

        /// <summary>Takes the file as it is now as seen, after a reload that failed, so the same error isn't logged each second.</summary>
        internal void Seen() => _readWriteTime = File.Exists(Path) ? File.GetLastWriteTimeUtc(Path) : default;
    }
}
