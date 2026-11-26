using UnityEngine;

namespace LightGreen.Core
{
    /// <summary>
    /// Turns pictures drawn in code (RGBA floats, 0 to 1, row by row: <see cref="FlakeArt"/>, <see cref="RainArt"/>)
    /// into textures, for the weather's particles. One place for the conversion and the texture's settings.
    /// </summary>
    internal static class TextureArt
    {
        /// <summary>0-1 to 0-255, rounded; NaN gives 0.</summary>
        internal static byte ToByte(float v) => v > 0f ? (v < 1f ? (byte)Mathf.RoundToInt(v * 255f) : (byte)255) : (byte)0;

        /// <summary>RGBA floats as pixels.</summary>
        internal static Color32[] ToPixels(float[] rgba)
        {
            var pixels = new Color32[rgba.Length / 4];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(ToByte(rgba[i * 4]), ToByte(rgba[i * 4 + 1]), ToByte(rgba[i * 4 + 2]), ToByte(rgba[i * 4 + 3]));
            return pixels;
        }

        /// <summary>
        /// A square texture of the picture, with mipmaps so it stays smooth far away, clamped at its edges, kept out of
        /// scenes and saves, and no longer readable from script once uploaded (its pixels leave main memory).
        /// </summary>
        internal static Texture2D Make(string name, float[] rgba, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
            };
            texture.SetPixels32(ToPixels(rgba));
            texture.Apply(true, true);
            return texture;
        }
    }
}
