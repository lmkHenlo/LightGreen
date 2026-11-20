using System;

namespace LightGreen.Core
{
    /// <summary>
    /// Draws the cartoon rain's pictures in code (nothing shipped), as RGBA 0–1, rows from the bottom (Unity's order):
    /// a crisp white disc, which the particle system stretches along the fall into a bold streak with round ends, and
    /// a ripple, two thin concentric rings, laid flat where a drop lands. Unity-free, so it can be checked outside the game.
    /// </summary>
    internal static class RainArt
    {
        /// <summary>A white disc filling the square, with an edge about a pixel and a half wide.</summary>
        internal static float[] Disc(int size)
        {
            var rgba = new float[size * size * 4];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Distance(x, y, half);
                Set(rgba, size, x, y, 1f, Cover(r, half * 0.85f, 1.5f));
            }
            return rgba;
        }

        /// <summary>Two thin white rings, the inner one fainter, like the rings a drop leaves on a puddle.</summary>
        internal static float[] Ripple(int size)
        {
            var rgba = new float[size * size * 4];
            float half = size * 0.5f;
            float width = Math.Max(1.5f, size * 0.03f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Distance(x, y, half);
                float outer = Band(r, half * 0.86f, width);
                float inner = Band(r, half * 0.5f, width) * 0.7f;
                Set(rgba, size, x, y, 1f, Math.Max(outer, inner));
            }
            return rgba;
        }

        private static float Distance(int x, int y, float half)
        {
            float dx = x + 0.5f - half, dy = y + 0.5f - half;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>1 inside the radius, 0 outside, across an edge this many pixels wide.</summary>
        private static float Cover(float r, float radius, float edge) => Smooth(Clamp01(0.5f - (r - radius) / edge));

        /// <summary>A ring of this width at this radius, its edges a pixel soft.</summary>
        private static float Band(float r, float radius, float width) =>
            Smooth(Clamp01(0.5f + (width * 0.5f - Math.Abs(r - radius))));

        private static void Set(float[] rgba, int size, int x, int y, float white, float alpha)
        {
            int i = (y * size + x) * 4;
            rgba[i] = white;
            rgba[i + 1] = white;
            rgba[i + 2] = white;
            rgba[i + 3] = alpha;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
