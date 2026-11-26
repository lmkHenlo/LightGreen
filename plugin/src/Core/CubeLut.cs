using System;
using System.Globalization;
using System.IO;

namespace LightGreen.Core
{
    /// <summary>
    /// A colour lookup table read from a .cube file, the plain-text format Lightroom, Photoshop, Resolve and most
    /// LUT packs use: an optional TITLE, LUT_3D_SIZE or LUT_1D_SIZE, optional DOMAIN_MIN and DOMAIN_MAX (or
    /// LUT_3D_INPUT_RANGE / LUT_1D_INPUT_RANGE), then one "r g b" line per entry with red changing fastest.
    /// </summary>
    internal sealed class CubeLut
    {
        internal const int Max3DSize = 129;
        internal const int Max1DSize = 65536;

        internal string Title { get; private set; } = "";
        internal bool Is3D { get; private set; }
        internal int Size { get; private set; }

        private Rgb[] _data;
        private Rgb _min = Rgb.Grey(0f), _max = Rgb.Grey(1f);

        private static readonly char[] Separators = { ' ', '\t' };

        /// <summary>Reads a .cube file's text. Throws <see cref="InvalidDataException"/> saying what's wrong.</summary>
        internal static CubeLut Parse(string text)
        {
            using (var reader = new StringReader(text ?? "")) return Parse(reader);
        }

        /// <summary>
        /// Reads a .cube file line by line (a 65-point cube has 274,625 lines). Touches nothing of Unity's, so it can run
        /// on a worker thread. Throws <see cref="InvalidDataException"/> saying what's wrong.
        /// </summary>
        internal static CubeLut Parse(TextReader reader)
        {
            var lut = new CubeLut();
            int size3 = 0, size1 = 0, count = 0, lineNumber = 0;
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    line = line.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    // Colour lines, nearly all of the file, are read in place: no split, no strings per number.
                    if (IsNumberStart(line[0]))
                    {
                        if (lut._data == null) throw Bad(lineNumber, "colour values before LUT_3D_SIZE or LUT_1D_SIZE");
                        if (count >= lut._data.Length) throw Bad(lineNumber, $"more than the {lut._data.Length} entries the size gives");
                        lut._data[count++] = ParseRgb(line, lineNumber);
                        continue;
                    }

                    string[] parts = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                    string key = parts[0].ToUpperInvariant();
                    if (key == "TITLE")
                    {
                        lut.Title = line.Substring(5).Trim().Trim('"');
                        continue;
                    }
                    if (key == "LUT_3D_SIZE" || key == "LUT_1D_SIZE")
                    {
                        if (lut._data != null) throw Bad(lineNumber, "a second size line");
                        int n = ParseInt(parts, 1, lineNumber);
                        bool is3D = key == "LUT_3D_SIZE";
                        if (n < 2 || n > (is3D ? Max3DSize : Max1DSize))
                            throw Bad(lineNumber, $"size {n} (LightGreen reads 2 to {(is3D ? Max3DSize : Max1DSize)})");
                        if (is3D) size3 = n;
                        else size1 = n;
                        lut.Is3D = is3D;
                        lut.Size = n;
                        lut._data = new Rgb[is3D ? n * n * n : n];
                        continue;
                    }
                    if (key == "DOMAIN_MIN")
                    {
                        lut._min = ParseRgb(parts, 1, lineNumber);
                        continue;
                    }
                    if (key == "DOMAIN_MAX")
                    {
                        lut._max = ParseRgb(parts, 1, lineNumber);
                        continue;
                    }
                    if (key == "LUT_3D_INPUT_RANGE" || key == "LUT_1D_INPUT_RANGE")
                    {
                        float lo = ParseFloat(parts, 1, lineNumber), hi = ParseFloat(parts, 2, lineNumber);
                        lut._min = Rgb.Grey(lo);
                        lut._max = Rgb.Grey(hi);
                        continue;
                    }
                    // A keyword LightGreen doesn't need (some programs add their own). A word that's a number,
                    // NaN or Infinity, is a colour line, and is refused below with its line number.
                    if (char.IsLetter(parts[0][0]) && !IsNumberWord(key)) continue;

                    if (lut._data == null) throw Bad(lineNumber, "colour values before LUT_3D_SIZE or LUT_1D_SIZE");
                    if (count >= lut._data.Length) throw Bad(lineNumber, $"more than the {lut._data.Length} entries the size gives");
                    lut._data[count++] = ParseRgb(parts, 0, lineNumber);
                }
            }

            if (lut._data == null) throw new InvalidDataException("no LUT_3D_SIZE or LUT_1D_SIZE line");
            if (count != lut._data.Length)
                throw new InvalidDataException($"{count} entries, but size {(size3 > 0 ? size3 : size1)} needs {lut._data.Length}");
            if (lut._max.R <= lut._min.R || lut._max.G <= lut._min.G || lut._max.B <= lut._min.B)
                throw new InvalidDataException("DOMAIN_MAX isn't above DOMAIN_MIN");
            return lut;
        }

        /// <summary>A colour through the table (trilinear for 3D, per channel for 1D).</summary>
        internal Rgb Sample(Rgb c)
        {
            float x = Position(c.R, _min.R, _max.R), y = Position(c.G, _min.G, _max.G), z = Position(c.B, _min.B, _max.B);
            if (!Is3D) return new Rgb(Sample1D(x, 0), Sample1D(y, 1), Sample1D(z, 2));

            int n = Size;
            int x0 = Math.Min((int)x, n - 2), y0 = Math.Min((int)y, n - 2), z0 = Math.Min((int)z, n - 2);
            float fx = x - x0, fy = y - y0, fz = z - z0;
            Rgb c00 = Rgb.Lerp(At(x0, y0, z0), At(x0 + 1, y0, z0), fx);
            Rgb c10 = Rgb.Lerp(At(x0, y0 + 1, z0), At(x0 + 1, y0 + 1, z0), fx);
            Rgb c01 = Rgb.Lerp(At(x0, y0, z0 + 1), At(x0 + 1, y0, z0 + 1), fx);
            Rgb c11 = Rgb.Lerp(At(x0, y0 + 1, z0 + 1), At(x0 + 1, y0 + 1, z0 + 1), fx);
            return Rgb.Lerp(Rgb.Lerp(c00, c10, fy), Rgb.Lerp(c01, c11, fy), fz);
        }

        private Rgb At(int r, int g, int b) => _data[r + g * Size + b * Size * Size];

        private float Position(float v, float min, float max) => Rgb.Clamp01((v - min) / (max - min)) * (Size - 1);

        private float Sample1D(float p, int channel)
        {
            int i = Math.Min((int)p, Size - 2);
            float t = p - i;
            float a = Channel(_data[i], channel), b = Channel(_data[i + 1], channel);
            return a + (b - a) * t;
        }

        private static float Channel(Rgb c, int channel) => channel == 0 ? c.R : channel == 1 ? c.G : c.B;

        private static bool IsNumberStart(char c) => (c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.';

        /// <summary>The first three numbers of a colour line, read where they stand in it.</summary>
        private static Rgb ParseRgb(string text, int line)
        {
            float r = 0f, g = 0f, b = 0f;
            int at = 0;
            for (int n = 0; n < 3; n++)
            {
                while (at < text.Length && (text[at] == ' ' || text[at] == '\t')) at++;
                int start = at;
                while (at < text.Length && text[at] != ' ' && text[at] != '\t') at++;
                if (at == start ||
                    !float.TryParse(text.AsSpan(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    throw Bad(line, "a missing or unreadable number");
                if (float.IsNaN(v) || float.IsInfinity(v))
                    throw Bad(line, $"\"{text.Substring(start, at - start)}\" isn't a usable number");
                if (n == 0) r = v;
                else if (n == 1) g = v;
                else b = v;
            }
            return new Rgb(r, g, b);
        }

        private static Rgb ParseRgb(string[] parts, int at, int line) =>
            new Rgb(ParseFloat(parts, at, line), ParseFloat(parts, at + 1, line), ParseFloat(parts, at + 2, line));

        private static float ParseFloat(string[] parts, int at, int line)
        {
            if (at >= parts.Length || !float.TryParse(parts[at], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                throw Bad(line, "a missing or unreadable number");
            // NaN would pass every clamp and reach the screen; an infinity (or a number too big for a float) means
            // a broken file.
            if (float.IsNaN(v) || float.IsInfinity(v)) throw Bad(line, $"\"{parts[at]}\" isn't a usable number");
            return v;
        }

        /// <summary>Words that read as numbers (upper case): never a keyword.</summary>
        private static bool IsNumberWord(string word) =>
            word == "NAN" || word == "INF" || word == "INFINITY";

        private static int ParseInt(string[] parts, int at, int line)
        {
            if (at >= parts.Length || !int.TryParse(parts[at], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                throw Bad(line, "a missing or unreadable size");
            return v;
        }

        private static InvalidDataException Bad(int line, string what) => new InvalidDataException($"line {line}: {what}");
    }
}
