using System;
using UnityEngine;

namespace LightGreen.Presets
{
    /// <summary>The filters file (BepInEx/config/LightGreen.filters.json).</summary>
    [Serializable]
    public class FilterFile
    {
        [Tooltip("Raised when LightGreen's built-in filters change; an older file is backed up and replaced.")]
        public int version;
        public PhotoFilterPreset[] filters = Array.Empty<PhotoFilterPreset>();
    }

    /// <summary>
    /// One photo filter. Every value's neutral is 0 (or empty), so a filter only does what it sets. Colours work
    /// like a photo editor's, on the finished picture; they apply in the order listed. The HUD is never filtered.
    /// </summary>
    [Serializable]
    public class PhotoFilterPreset
    {
        public string name = "";

        [Tooltip("A .cube file in BepInEx/config/LightGreen.filters to apply first (empty = none).")]
        public string lut = "";

        [Tooltip("Nine values: the new red, green and blue, each from (red, green, blue). Empty = none.")]
        public float[] channelMixer = Array.Empty<float>();
        [Tooltip("-1 to 1: cooler to warmer.")]
        public float warmth;
        [Tooltip("-1 to 1: greener to more magenta.")]
        public float tint;
        [Tooltip("-1 to 1.")]
        public float brightness;
        [Tooltip("-1 to 1.")]
        public float contrast;
        [Tooltip("-1 (grey) to 1.")]
        public float saturation;

        [Tooltip("Tone curves as x, y pairs from 0 to 1, e.g. [0, 0.08, 1, 0.95]. Empty = none. curve is for all three.")]
        public float[] curve = Array.Empty<float>();
        public float[] curveRed = Array.Empty<float>();
        public float[] curveGreen = Array.Empty<float>();
        public float[] curveBlue = Array.Empty<float>();

        [Tooltip("Split toning: shadows lean toward splitShadows and highlights toward splitHighlights (mid grey = no change).")]
        public Color splitShadows = new Color(0.5f, 0.5f, 0.5f);
        public Color splitHighlights = new Color(0.5f, 0.5f, 0.5f);
        [Tooltip("-1 to 1: more of the picture counts as shadow, or as highlight.")]
        public float splitBalance;
        [Tooltip("0 to 1. 0 = no split toning.")]
        public float splitStrength;

        [Tooltip("Gradient map: brightness picks a colour along these, dark to light (two for a duotone).")]
        public Color[] gradient = Array.Empty<Color>();
        [Tooltip("0 to 1. 0 = no gradient map.")]
        public float gradientStrength;

        [Tooltip("0 to 1: blacks rise to this, for a faded print.")]
        public float fade;
        [Tooltip("Colour steps per channel, 2 or more. 0 = off.")]
        public int posterize;

        [Tooltip("0 to 1: darker (or vignetteColor) corners. Off in VR.")]
        public float vignette;
        public Color vignetteColor = Color.black;
        [Tooltip("0 to 1: how gradual the vignette's edge is.")]
        public float vignetteSmoothness = 0.4f;
        [Tooltip("-1 to 1: barrel (above 0) or pincushion lens distortion. Off in VR.")]
        public float distortion;
        [Tooltip("0 to 1: colour fringes at the edges. Off in VR.")]
        public float aberration;
        [Tooltip("0 to 1: extra glow around bright parts, added to the scene's own.")]
        public float bloom;
        [Tooltip("How bright a part has to be to glow, for this filter's bloom.")]
        public float bloomThreshold = 0.6f;
    }

    /// <summary>LightGreen's built-in filters, written to the filters file on first start.</summary>
    internal static class DefaultFilters
    {
        /// <summary>Raise when the built-in filters change, so existing files are replaced (with a backup).</summary>
        internal const int Version = 1;

        private static Color C(float r, float g, float b) => new Color(r, g, b);

        internal static FilterFile Create() => new FilterFile
        {
            version = Version,
            filters = new[]
            {
                new PhotoFilterPreset
                {
                    name = "Mono",
                    saturation = -1f,
                    contrast = 0.05f,
                },
                new PhotoFilterPreset
                {
                    // Black and white through a red filter: darker skies and water, brighter skin, wood and sand.
                    name = "Mono Red",
                    channelMixer = new[] { 0.9f, 0.3f, -0.2f, 0.9f, 0.3f, -0.2f, 0.9f, 0.3f, -0.2f },
                    contrast = 0.12f,
                },
                new PhotoFilterPreset
                {
                    name = "Sepia",
                    gradient = new[] { C(0.12f, 0.07f, 0.03f), C(0.55f, 0.40f, 0.26f), C(1f, 0.94f, 0.80f) },
                    gradientStrength = 0.9f,
                    fade = 0.04f,
                },
                new PhotoFilterPreset
                {
                    name = "Faded Film",
                    curve = new[] { 0f, 0.07f, 0.25f, 0.27f, 0.75f, 0.74f, 1f, 0.93f },
                    contrast = -0.08f,
                    saturation = -0.2f,
                    warmth = 0.15f,
                },
                new PhotoFilterPreset
                {
                    name = "Vivid",
                    curve = new[] { 0f, 0f, 0.25f, 0.21f, 0.75f, 0.80f, 1f, 1f },
                    saturation = 0.35f,
                    contrast = 0.08f,
                },
                new PhotoFilterPreset
                {
                    name = "Warm",
                    warmth = 0.45f,
                    splitHighlights = C(0.80f, 0.62f, 0.40f),
                    splitShadows = C(0.55f, 0.48f, 0.42f),
                    splitStrength = 0.35f,
                },
                new PhotoFilterPreset
                {
                    name = "Cool",
                    warmth = -0.45f,
                    splitShadows = C(0.36f, 0.52f, 0.62f),
                    splitHighlights = C(0.46f, 0.52f, 0.56f),
                    splitStrength = 0.35f,
                },
                new PhotoFilterPreset
                {
                    // Teal shadows, warm highlights and skin.
                    name = "Teal & Orange",
                    splitShadows = C(0.12f, 0.58f, 0.66f),
                    splitHighlights = C(0.92f, 0.56f, 0.22f),
                    splitStrength = 0.9f,
                    saturation = 0.1f,
                    contrast = 0.1f,
                },
                new PhotoFilterPreset
                {
                    // Film processed in the wrong chemicals: yellow-green highlights, blue shadows.
                    name = "Cross Process",
                    curveRed = new[] { 0f, 0f, 0.25f, 0.17f, 0.75f, 0.85f, 1f, 1f },
                    curveGreen = new[] { 0f, 0.04f, 0.25f, 0.22f, 0.75f, 0.84f, 1f, 1f },
                    curveBlue = new[] { 0f, 0.20f, 1f, 0.78f },
                    saturation = 0.15f,
                    contrast = 0.05f,
                },
                new PhotoFilterPreset
                {
                    name = "Duotone",
                    gradient = new[] { C(0.10f, 0.11f, 0.32f), C(0.98f, 0.64f, 0.52f) },
                    gradientStrength = 1f,
                },
                new PhotoFilterPreset
                {
                    // Fewer colour steps, like a printed poster or a comic.
                    name = "Poster",
                    posterize = 5,
                    saturation = 0.2f,
                    contrast = 0.08f,
                },
                new PhotoFilterPreset
                {
                    // A plastic toy camera: saturated cross-process, dark corners, a bulging lens.
                    name = "Toy Camera",
                    curveRed = new[] { 0f, 0f, 0.25f, 0.20f, 0.75f, 0.82f, 1f, 1f },
                    curveBlue = new[] { 0f, 0.10f, 1f, 0.88f },
                    saturation = 0.35f,
                    contrast = 0.15f,
                    vignette = 0.5f,
                    vignetteSmoothness = 0.35f,
                    distortion = 0.25f,
                    aberration = 0.2f,
                },
                new PhotoFilterPreset
                {
                    // Pastel and soft, with a glow around bright parts.
                    name = "Dreamy",
                    fade = 0.06f,
                    contrast = -0.15f,
                    saturation = -0.1f,
                    brightness = 0.04f,
                    splitHighlights = C(0.62f, 0.50f, 0.56f),
                    splitStrength = 0.4f,
                    bloom = 0.6f,
                    bloomThreshold = 0.6f,
                },
            },
        };
    }
}
