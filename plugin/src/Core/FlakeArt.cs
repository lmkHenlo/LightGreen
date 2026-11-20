using System;

namespace LightGreen.Core
{
    /// <summary>
    /// Draws the snowflakes in code (nothing shipped): a square atlas of tiles × tiles cartoon flakes, each a round,
    /// slightly lumpy blob, mostly flat white with a faint blue shade on its lower right, the cel-shaded look. A crisp
    /// edge for the snowfall, a soft one for the few big flakes passing close to the camera, as if out of focus.
    /// Unity-free, so it can be checked outside the game.
    /// </summary>
    internal static class FlakeArt
    {
        /// <summary>The atlas as RGBA, 0–1, rows from the bottom (Unity's order): size = tile × tiles pixels square.</summary>
        internal static float[] Atlas(int tile, int tiles, float softness, int seed)
        {
            int size = tile * tiles;
            var rgba = new float[size * size * 4];
            var random = new Random(seed);
            for (int ty = 0; ty < tiles; ty++)
            for (int tx = 0; tx < tiles; tx++)
                DrawFlake(rgba, size, tx * tile, ty * tile, tile, softness, random);
            return rgba;
        }

        private static void DrawFlake(float[] rgba, int size, int x0, int y0, int tile, float softness, Random random)
        {
            // Lumps: a few low waves round the edge, each flake its own.
            const int Waves = 3;
            var freq = new int[Waves];
            var amp = new float[Waves];
            var phase = new float[Waves];
            for (int i = 0; i < Waves; i++)
            {
                freq[i] = 2 + i + random.Next(0, 2);
                amp[i] = 0.015f + (float)random.NextDouble() * 0.025f;
                phase[i] = (float)(random.NextDouble() * Math.PI * 2);
            }

            // Radius as a fraction of the tile's half width, leaving room for lumps, the soft edge and mipmaps.
            float radius = 0.62f;
            float half = tile * 0.5f;
            float edge = Math.Max(1.2f, softness * half);   // width of the edge, in pixels

            for (int y = 0; y < tile; y++)
            for (int x = 0; x < tile; x++)
            {
                float px = (x + 0.5f - half) / half, py = (y + 0.5f - half) / half;
                float r = (float)Math.Sqrt(px * px + py * py);
                float angle = (float)Math.Atan2(py, px);
                float lump = 1f;
                for (int i = 0; i < Waves; i++) lump += amp[i] * (float)Math.Sin(freq[i] * angle + phase[i]);
                float rim = radius * lump;

                // Coverage: 1 inside, falling to 0 across the edge.
                float distPx = (r - rim) * half;
                float a = Clamp01(0.5f - distPx / edge);
                a = a * a * (3f - 2f * a);
                if (a <= 0f) continue;

                // Shade: flat white, a cool blue toward the lower right, in two soft cel steps.
                float toward = (px * 0.6f - py * 0.8f) / Math.Max(rim, 1e-3f);
                float shade = Smooth(0.25f, 0.55f, toward) * 0.55f + Smooth(0.6f, 0.85f, toward) * 0.45f;
                float red = Lerp(1f, 0.80f, shade), green = Lerp(1f, 0.88f, shade), blue = 1f;

                int i4 = ((y0 + y) * size + (x0 + x)) * 4;
                rgba[i4] = red;
                rgba[i4 + 1] = green;
                rgba[i4 + 2] = blue;
                rgba[i4 + 3] = a;
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Smooth(float from, float to, float v)
        {
            float t = Clamp01((v - from) / (to - from));
            return t * t * (3f - 2f * t);
        }
    }
}
