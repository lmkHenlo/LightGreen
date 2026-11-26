using System;
using System.IO;
using System.Text;

namespace LightGreen.Core
{
    /// <summary>
    /// Reading and writing LightGreen's own files. Reads are capped in size, so a wrong or huge file is refused instead
    /// of freezing the game while it's read. Writes go to a file beside the real one first and then take its place, so
    /// a crash or power cut mid-write leaves the old file whole rather than a cut-off one.
    /// </summary>
    internal static class SafeFile
    {
        /// <summary>The most a settings file (presets, filters) may be.</summary>
        internal const long MaxSettingsBytes = 4L * 1024 * 1024;

        /// <summary>The most a .cube file may be: a 129-point cube, the largest LightGreen reads, is about 60 MB.</summary>
        internal const long MaxCubeBytes = 96L * 1024 * 1024;

        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>The whole file as text. Throws <see cref="InvalidDataException"/> when it's over <paramref name="maxBytes"/>.</summary>
        internal static string ReadText(string path, long maxBytes)
        {
            CheckSize(path, maxBytes);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        /// <summary>The file for reading line by line, after the same size check as <see cref="ReadText"/>.</summary>
        internal static StreamReader OpenText(string path, long maxBytes)
        {
            CheckSize(path, maxBytes);
            return new StreamReader(path, Encoding.UTF8, true, 1 << 16);
        }

        private static void CheckSize(string path, long maxBytes)
        {
            long length = new FileInfo(path).Length;
            if (length > maxBytes)
                throw new InvalidDataException($"the file is {length / (1024.0 * 1024.0):0.#} MB; LightGreen reads files up to {maxBytes / (1024 * 1024)} MB");
        }

        /// <summary>Writes the file whole or not at all: to "<paramref name="path"/>.tmp" first, which then replaces it.</summary>
        internal static void WriteText(string path, string text)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text, Utf8);
            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }
            try
            {
                File.Replace(temp, path, null);
            }
            catch (Exception e) when (e is PlatformNotSupportedException || e is IOException || e is UnauthorizedAccessException)
            {
                // Some file systems can't swap files in one step: copy over instead (the temp file is complete).
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
        }
    }
}
