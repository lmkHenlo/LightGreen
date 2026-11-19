using System;

namespace LightGreen.Core
{
    /// <summary>A colour as three floats, 0 to 1, in the display (sRGB) encoding photo editors work in.</summary>
    internal struct Rgb
    {
        internal float R, G, B;

        internal Rgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        internal static Rgb Grey(float v) => new Rgb(v, v, v);

        internal float Luminance => 0.2126f * R + 0.7152f * G + 0.0722f * B;

        internal Rgb Clamped => new Rgb(Clamp01(R), Clamp01(G), Clamp01(B));

        internal static Rgb Lerp(Rgb a, Rgb b, float t) =>
            new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

        internal static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        public override string ToString() => $"({R:0.###}, {G:0.###}, {B:0.###})";
    }

    /// <summary>
    /// A tone curve through points given as x, y pairs (0 to 1), smooth and never overshooting between them
    /// (monotone cubic, Fritsch–Carlson), flat past the first and last point.
    /// </summary>
    internal sealed class Curve
    {
        private readonly float[] _x, _y, _m;

        private Curve(float[] x, float[] y, float[] m)
        {
            _x = x;
            _y = y;
            _m = m;
        }

        /// <summary>A curve from flat x, y pairs, or null when there are fewer than two points (no curve).</summary>
        internal static Curve FromPairs(float[] pairs)
        {
            if (pairs == null || pairs.Length < 4) return null;
            int n = pairs.Length / 2;
            var points = new (float x, float y)[n];
            for (int i = 0; i < n; i++) points[i] = (Rgb.Clamp01(pairs[2 * i]), pairs[2 * i + 1]);
            Array.Sort(points, (a, b) => a.x.CompareTo(b.x));

            // Drop points on the same x as the one before.
            int count = 0;
            for (int i = 0; i < n; i++)
                if (count == 0 || points[i].x > points[count - 1].x + 1e-6f) points[count++] = points[i];
            if (count < 2) return null;

            var x = new float[count];
            var y = new float[count];
            for (int i = 0; i < count; i++)
            {
                x[i] = points[i].x;
                y[i] = points[i].y;
            }

            var d = new float[count - 1];
            for (int i = 0; i < count - 1; i++) d[i] = (y[i + 1] - y[i]) / (x[i + 1] - x[i]);
            var m = new float[count];
            m[0] = d[0];
            m[count - 1] = d[count - 2];
            for (int i = 1; i < count - 1; i++) m[i] = d[i - 1] * d[i] <= 0f ? 0f : (d[i - 1] + d[i]) * 0.5f;
            for (int i = 0; i < count - 1; i++)
            {
                if (Math.Abs(d[i]) < 1e-9f)
                {
                    m[i] = 0f;
                    m[i + 1] = 0f;
                    continue;
                }
                float a = m[i] / d[i], b = m[i + 1] / d[i];
                float s = a * a + b * b;
                if (s > 9f)
                {
                    float t = 3f / (float)Math.Sqrt(s);
                    m[i] = t * a * d[i];
                    m[i + 1] = t * b * d[i];
                }
            }
            return new Curve(x, y, m);
        }

        internal float Evaluate(float v)
        {
            int last = _x.Length - 1;
            if (v <= _x[0]) return _y[0];
            if (v >= _x[last]) return _y[last];
            int k = 0;
            while (k < last - 1 && v > _x[k + 1]) k++;
            float h = _x[k + 1] - _x[k];
            float t = (v - _x[k]) / h;
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * _y[k] + (t3 - 2f * t2 + t) * h * _m[k] +
                   (-2f * t3 + 3f * t2) * _y[k + 1] + (t3 - t2) * h * _m[k + 1];
        }
    }

    /// <summary>
    /// The colour part of a photo filter, ready to build a lookup table from. Every value's neutral is 0 (or
    /// empty), so a filter only does what it names.
    /// </summary>
    internal sealed class FilterSpec
    {
        internal CubeLut Cube;
        internal float[] ChannelMixer;     // 9 values, rows red, green, blue; null = none
        internal float Warmth, Tint;       // -1 to 1
        internal float Brightness, Contrast, Saturation;   // -1 to 1
        internal Curve Master, Red, Green, Blue;
        internal Rgb SplitShadows = Rgb.Grey(0.5f), SplitHighlights = Rgb.Grey(0.5f);
        internal float SplitBalance, SplitStrength;
        internal Rgb[] Gradient;           // colours from dark to light; null = none
        internal float GradientStrength;
        internal float Fade;               // 0 to 1: the black point rises to this
        internal int Posterize;            // levels per channel; 0 = off
    }

    /// <summary>
    /// Builds a photo filter's colour lookup table in URP's strip layout: width size × size, height size; blue
    /// picks the tile, red runs across a tile, green up. URP applies it last, after tonemapping, on display-encoded
    /// colours, so filters work like a photo editor's on top of whatever the scene looks like.
    /// </summary>
    internal static class LutBuilder
    {
        /// <summary>Fills <paramref name="strip"/> (length size³) with the filter applied to every table entry.</summary>
        internal static void Fill(FilterSpec spec, int size, Rgb[] strip)
        {
            if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
            if (strip == null || strip.Length != size * size * size) throw new ArgumentException("wrong table length", nameof(strip));
            float step = 1f / (size - 1);
            int width = size * size;
            for (int b = 0; b < size; b++)
            for (int g = 0; g < size; g++)
            for (int r = 0; r < size; r++)
                strip[g * width + b * size + r] = Apply(spec, new Rgb(r * step, g * step, b * step));
        }

        /// <summary>One colour through the filter, in this order: LUT file, channel mixer, warmth and tint,
        /// brightness, contrast, saturation, curves, split toning, gradient map, fade, posterize.</summary>
        internal static Rgb Apply(FilterSpec f, Rgb c)
        {
            if (f.Cube != null) c = f.Cube.Sample(c);

            if (f.ChannelMixer != null && f.ChannelMixer.Length == 9)
            {
                float[] m = f.ChannelMixer;
                c = new Rgb(m[0] * c.R + m[1] * c.G + m[2] * c.B,
                            m[3] * c.R + m[4] * c.G + m[5] * c.B,
                            m[6] * c.R + m[7] * c.G + m[8] * c.B);
            }

            if (f.Warmth != 0f || f.Tint != 0f)
                c = new Rgb(c.R * (1f + 0.15f * f.Warmth), c.G * (1f - 0.12f * f.Tint), c.B * (1f - 0.15f * f.Warmth));

            if (f.Brightness != 0f)
            {
                float k = 1f + f.Brightness;
                c = new Rgb(c.R * k, c.G * k, c.B * k);
            }
            if (f.Contrast != 0f)
            {
                float k = 1f + f.Contrast;
                c = new Rgb((c.R - 0.5f) * k + 0.5f, (c.G - 0.5f) * k + 0.5f, (c.B - 0.5f) * k + 0.5f);
            }
            if (f.Saturation != 0f)
            {
                float l = c.Luminance, k = 1f + f.Saturation;
                c = new Rgb(l + (c.R - l) * k, l + (c.G - l) * k, l + (c.B - l) * k);
            }

            c = c.Clamped;
            if (f.Master != null) c = new Rgb(f.Master.Evaluate(c.R), f.Master.Evaluate(c.G), f.Master.Evaluate(c.B));
            if (f.Red != null) c.R = f.Red.Evaluate(c.R);
            if (f.Green != null) c.G = f.Green.Evaluate(c.G);
            if (f.Blue != null) c.B = f.Blue.Evaluate(c.B);
            c = c.Clamped;

            if (f.SplitStrength > 0f)
            {
                // Shadows lean toward one colour and highlights toward the other (soft light: mid grey is neutral).
                float t = SmoothStep(0.15f, 0.85f, c.Luminance + f.SplitBalance * 0.35f);
                Rgb tone = Rgb.Lerp(f.SplitShadows, f.SplitHighlights, t);
                c = Rgb.Lerp(c, SoftLight(c, tone), Rgb.Clamp01(f.SplitStrength));
            }

            if (f.Gradient != null && f.Gradient.Length >= 2 && f.GradientStrength > 0f)
                c = Rgb.Lerp(c, SampleGradient(f.Gradient, c.Luminance), Rgb.Clamp01(f.GradientStrength));

            if (f.Fade > 0f)
            {
                float fade = Rgb.Clamp01(f.Fade);
                c = new Rgb(fade + c.R * (1f - fade), fade + c.G * (1f - fade), fade + c.B * (1f - fade));
            }

            c = c.Clamped;
            if (f.Posterize >= 2)
            {
                float levels = f.Posterize - 1;
                c = new Rgb((float)Math.Round(c.R * levels) / levels, (float)Math.Round(c.G * levels) / levels,
                            (float)Math.Round(c.B * levels) / levels);
            }
            return c;
        }

        private static Rgb SoftLight(Rgb a, Rgb b) => new Rgb(SoftLight(a.R, b.R), SoftLight(a.G, b.G), SoftLight(a.B, b.B));

        private static float SoftLight(float a, float b) => (1f - 2f * b) * a * a + 2f * b * a;

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Rgb.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static Rgb SampleGradient(Rgb[] stops, float v)
        {
            float p = Rgb.Clamp01(v) * (stops.Length - 1);
            int i = Math.Min((int)p, stops.Length - 2);
            return Rgb.Lerp(stops[i], stops[i + 1], p - i);
        }
    }
}
