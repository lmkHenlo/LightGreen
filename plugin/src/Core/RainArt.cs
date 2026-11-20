using System;

namespace LightGreen.Core
{
    /// <summary>
    /// Draws the cartoon rain's picture in code (nothing shipped), as RGBA 0–1, rows from the bottom (Unity's order):
    /// a crisp white disc, which the particle system stretches along the fall into a bold, tapered streak. Unity-free, so
    /// it can be checked outside the game.
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

        private static float Distance(int x, int y, float half)
        {
            float dx = x + 0.5f - half, dy = y + 0.5f - half;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>1 inside the radius, 0 outside, across an edge this many pixels wide.</summary>
        private static float Cover(float r, float radius, float edge) => Smooth(Clamp01(0.5f - (r - radius) / edge));

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
