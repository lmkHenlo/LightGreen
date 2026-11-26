using System;
using UnityEngine;

namespace LightGreen.Presets
{
    /// <summary>The times of day LightGreen can show. HoleDefault keeps each hole's own lighting.</summary>
    public enum TimeOfDay
    {
        HoleDefault,
        Dawn,
        Morning,
        Noon,
        GoldenHour,
        Sunset,
        BlueHour,
        Night,
    }

    /// <summary>
    /// How one time of day changes a hole. Every value is relative to the hole's own lighting or blends
    /// from it, so a preset suits any hole. Weights of 0 leave that part as the hole has it.
    /// </summary>
    [Serializable]
    public class TimePreset
    {
        public string name = "";

        [Tooltip("0 keeps the hole's sun; 1 uses this preset's sun height and colour.")]
        public float sunWeight;
        [Tooltip("Sun height above the horizon in degrees. Below 0 the sun has set and the scene light becomes the moon.")]
        public float sunElevation = 45f;
        public Color sunColor = Color.white;
        [Tooltip("Multiplies the hole's sun brightness.")]
        public float sunIntensity = 1f;

        [Tooltip("Once the sun is below the horizon: the moon's height in degrees, its colour, and its brightness (times the hole's sun).")]
        public float moonElevation = 40f;
        public Color moonColor = new Color(0.36f, 0.48f, 0.62f);
        public float moonIntensity = 0.85f;

        [Tooltip("How far the sky colours move toward the colours below (0 to 1).")]
        public float skyBlend;
        public Color skyColor = new Color(0.4f, 0.6f, 1f);
        public Color horizonColor = new Color(0.8f, 0.9f, 1f);
        public Color sunGlowColor = new Color(1f, 0.9f, 0.7f);

        [Tooltip("Multiplies the hole's ambient (fill) light. 0 = none.")]
        public float ambientIntensity = 1f;
        [Tooltip("Colour the ambient light is multiplied by (white = unchanged).")]
        public Color ambientTint = Color.white;

        [Tooltip("0 = day, 1 = night: shows the sky's stars and moon and dims the sun's glow in the sky.")]
        public float night;
        [Tooltip("Moon phase the sky shows at night (0 to 1).")]
        public float moonPhase = 0.5f;

        [Tooltip("How far the distance haze colour moves toward the sky's horizon colour (0 to 1).")]
        public float fogColorBlend;
        [Tooltip("Multiplies how far away the haze reaches full strength.")]
        public float fogDistance = 1f;

        [Tooltip("Rim light colour at this time of day; the biome and config scale its strength.")]
        public Color rimColor = new Color(0.75f, 0.85f, 1f);

        [Tooltip("Colour grading added on top of the course's own: white balance, saturation, contrast, exposure.")]
        public float temperature;
        public float tint;
        public float saturation;
        public float contrast;
        public float exposure;

        [Tooltip("Lens flare strength when looking toward the sun (0 = none).")]
        public float lensFlare;

        [Tooltip("How much the water takes on the sun's colour (0 to 1).")]
        public float waterTint;

        /// <summary>
        /// The blend of two presets, <paramref name="t"/> 0 to 1. Written into <paramref name="into"/> when given (reused
        /// each frame by a blend, so it makes no garbage; it may be <paramref name="a"/> or <paramref name="b"/>), else a
        /// new preset.
        /// </summary>
        internal static TimePreset Lerp(TimePreset a, TimePreset b, float t, TimePreset into = null)
        {
            t = Mathf.Clamp01(t);
            // Sun height only blends meaningfully between presets that both set it.
            float elevationA = a.sunWeight > 0f ? a.sunElevation : b.sunElevation;
            float elevationB = b.sunWeight > 0f ? b.sunElevation : a.sunElevation;
            TimePreset result = into ?? new TimePreset();
            result.name = t < 0.5f ? a.name : b.name;
            result.sunWeight = Mathf.Lerp(a.sunWeight, b.sunWeight, t);
            result.sunElevation = Mathf.Lerp(elevationA, elevationB, t);
            result.sunColor = Color.Lerp(a.sunColor, b.sunColor, t);
            result.sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t);
            result.moonElevation = Mathf.Lerp(a.moonElevation, b.moonElevation, t);
            result.moonColor = Color.Lerp(a.moonColor, b.moonColor, t);
            result.moonIntensity = Mathf.Lerp(a.moonIntensity, b.moonIntensity, t);
            result.skyBlend = Mathf.Lerp(a.skyBlend, b.skyBlend, t);
            result.skyColor = Color.Lerp(a.skyColor, b.skyColor, t);
            result.horizonColor = Color.Lerp(a.horizonColor, b.horizonColor, t);
            result.sunGlowColor = Color.Lerp(a.sunGlowColor, b.sunGlowColor, t);
            result.ambientIntensity = Mathf.Lerp(a.ambientIntensity, b.ambientIntensity, t);
            result.ambientTint = Color.Lerp(a.ambientTint, b.ambientTint, t);
            result.night = Mathf.Lerp(a.night, b.night, t);
            result.moonPhase = Mathf.Lerp(a.moonPhase, b.moonPhase, t);
            result.fogColorBlend = Mathf.Lerp(a.fogColorBlend, b.fogColorBlend, t);
            result.fogDistance = Mathf.Lerp(a.fogDistance, b.fogDistance, t);
            result.rimColor = Color.Lerp(a.rimColor, b.rimColor, t);
            result.temperature = Mathf.Lerp(a.temperature, b.temperature, t);
            result.tint = Mathf.Lerp(a.tint, b.tint, t);
            result.saturation = Mathf.Lerp(a.saturation, b.saturation, t);
            result.contrast = Mathf.Lerp(a.contrast, b.contrast, t);
            result.exposure = Mathf.Lerp(a.exposure, b.exposure, t);
            result.lensFlare = Mathf.Lerp(a.lensFlare, b.lensFlare, t);
            result.waterTint = Mathf.Lerp(a.waterTint, b.waterTint, t);
            return result;
        }
    }

    /// <summary>How LightGreen styles one biome, whatever the time of day.</summary>
    [Serializable]
    public class BiomePreset
    {
        public string biome = "";

        [Tooltip("The hue shadows lean toward. Each material keeps its own shade brightness.")]
        public Color shadeTint = new Color(0.6f, 0.65f, 1f);
        [Tooltip("How far shade colours move toward the tint (0 to 1).")]
        public float shadeTintStrength = 0.3f;

        [Tooltip("Multiplies where the haze starts and where it is full. Lower = more haze.")]
        public float fogStart = 0.8f;
        public float fogEnd = 0.75f;
        [Tooltip("How far the haze colour moves toward the sky's horizon colour (0 to 1).")]
        public float fogColorBlend = 0.4f;

        [Tooltip("Colour grading for this biome, added on top of the course's own.")]
        public float temperature;
        public float tint;
        public float saturation;
        public float contrast;
        public float exposure;

        [Tooltip("Multiplies the rim light strength in this biome.")]
        public float rim = 1f;
        [Tooltip("Ice glint strength on terrain that supports it (0 = stock).")]
        public float glints;
        [Tooltip("Sun highlight strength on water (0 = stock: no highlight).")]
        public float waterSpecular = 0.5f;
        [Tooltip("Multiplies the ambient occlusion strength in this biome (0 = the course's own).")]
        public float ambientOcclusion = 1f;
    }

    /// <summary>The presets file: one entry per biome and one per time of day.</summary>
    [Serializable]
    public class PresetFile
    {
        public int version = 1;
        public BiomePreset[] biomes = Array.Empty<BiomePreset>();
        public TimePreset[] times = Array.Empty<TimePreset>();
    }

    /// <summary>LightGreen's built-in presets, written to the presets file the first time.</summary>
    internal static class DefaultPresets
    {
        /// <summary>
        /// Raised when the defaults change. A presets file with an older version is backed up and replaced,
        /// so tuned defaults reach players who never edited it.
        /// </summary>
        internal const int Version = 6;

        internal static PresetFile Create() => new PresetFile
        {
            version = Version,
            biomes = new[]
            {
                new BiomePreset { biome = "DrivingRange", shadeTint = new Color(0.55f, 0.7f, 0.95f), shadeTintStrength = 0.25f, fogStart = 0.9f, fogEnd = 0.85f, saturation = 2f, waterSpecular = 0.5f },
                new BiomePreset { biome = "Coast", shadeTint = new Color(0.35f, 0.8f, 0.85f), shadeTintStrength = 0.3f, fogStart = 0.85f, fogEnd = 0.8f, fogColorBlend = 0.5f, temperature = 2f, saturation = 4f, waterSpecular = 0.7f },
                new BiomePreset { biome = "Forest", shadeTint = new Color(0.45f, 0.7f, 0.6f), shadeTintStrength = 0.3f, fogStart = 0.75f, fogEnd = 0.7f, fogColorBlend = 0.35f, temperature = -1f, saturation = 3f, contrast = 3f },
                new BiomePreset { biome = "Desert", shadeTint = new Color(0.85f, 0.55f, 0.45f), shadeTintStrength = 0.35f, fogStart = 0.7f, fogEnd = 0.6f, fogColorBlend = 0.5f, temperature = 2f, saturation = 1f, contrast = 2f },
                new BiomePreset { biome = "Snow", shadeTint = new Color(0.55f, 0.55f, 1f), shadeTintStrength = 0.4f, fogStart = 0.75f, fogEnd = 0.65f, fogColorBlend = 0.5f, temperature = -4f, saturation = 2f, glints = 2.5f },
                new BiomePreset { biome = "City", shadeTint = new Color(0.6f, 0.5f, 0.95f), shadeTintStrength = 0.3f, fogStart = 0.85f, fogEnd = 0.75f, fogColorBlend = 0.4f, saturation = 2f, contrast = 3f, rim = 1.2f },
                new BiomePreset { biome = "Unknown", shadeTintStrength = 0.2f },
            },
            times = new[]
            {
                new TimePreset
                {
                    name = "HoleDefault", rimColor = new Color(0.75f, 0.85f, 1f), lensFlare = 0.15f, waterTint = 0f,
                },
                new TimePreset
                {
                    // First light: the sun just over the horizon, soft and pink, a cool sky warming at the horizon, the last stars fading,
                    // and morning mist.
                    name = "Dawn", sunWeight = 1f, sunElevation = 3f, sunColor = new Color(1f, 0.72f, 0.62f), sunIntensity = 0.75f,
                    skyBlend = 0.55f, skyColor = new Color(0.32f, 0.42f, 0.8f), horizonColor = new Color(1f, 0.7f, 0.66f), sunGlowColor = new Color(1f, 0.74f, 0.6f),
                    ambientIntensity = 1f, ambientTint = new Color(0.92f, 0.92f, 1f), night = 0.3f, moonPhase = 0.5f,
                    fogColorBlend = 0.7f, fogDistance = 0.75f, rimColor = new Color(1f, 0.8f, 0.76f),
                    temperature = -2f, tint = 4f, exposure = -0.05f, lensFlare = 0.5f, waterTint = 0.4f,
                },
                new TimePreset
                {
                    name = "Morning", sunWeight = 1f, sunElevation = 20f, sunColor = new Color(1f, 0.92f, 0.8f), sunIntensity = 0.9f,
                    skyBlend = 0.35f, skyColor = new Color(0.5f, 0.72f, 1f), horizonColor = new Color(1f, 0.86f, 0.72f), sunGlowColor = new Color(1f, 0.85f, 0.65f),
                    ambientIntensity = 0.95f, fogColorBlend = 0.6f, fogDistance = 0.85f, rimColor = new Color(1f, 0.9f, 0.8f),
                    temperature = -3f, saturation = 2f, lensFlare = 0.35f, waterTint = 0.2f,
                },
                new TimePreset
                {
                    name = "Noon", sunWeight = 1f, sunElevation = 62f, sunColor = new Color(1f, 0.98f, 0.95f), sunIntensity = 1.05f,
                    skyBlend = 0.15f, skyColor = new Color(0.32f, 0.58f, 1f), horizonColor = new Color(0.75f, 0.88f, 1f), sunGlowColor = new Color(1f, 0.97f, 0.9f),
                    ambientIntensity = 1f, fogColorBlend = 0.3f, fogDistance = 1.1f, rimColor = new Color(0.8f, 0.9f, 1f),
                    saturation = 3f, contrast = 2f, lensFlare = 0.1f, waterTint = 0f,
                },
                new TimePreset
                {
                    name = "GoldenHour", sunWeight = 1f, sunElevation = 8f, sunColor = new Color(1f, 0.78f, 0.52f), sunIntensity = 1f,
                    skyBlend = 0.5f, skyColor = new Color(0.45f, 0.6f, 0.95f), horizonColor = new Color(1f, 0.74f, 0.45f), sunGlowColor = new Color(1f, 0.7f, 0.35f),
                    ambientIntensity = 1.08f, fogColorBlend = 0.6f, fogDistance = 0.85f, rimColor = new Color(1f, 0.72f, 0.4f),
                    temperature = 7f, saturation = 3f, contrast = 2f, lensFlare = 0.6f, waterTint = 0.45f,
                },
                new TimePreset
                {
                    name = "Sunset", sunWeight = 1f, sunElevation = 2.5f, sunColor = new Color(1f, 0.6f, 0.38f), sunIntensity = 0.9f,
                    skyBlend = 0.65f, skyColor = new Color(0.36f, 0.38f, 0.72f), horizonColor = new Color(1f, 0.52f, 0.32f), sunGlowColor = new Color(1f, 0.5f, 0.25f),
                    ambientIntensity = 1.15f, fogColorBlend = 0.6f, fogDistance = 0.8f, rimColor = new Color(1f, 0.55f, 0.35f),
                    temperature = 10f, tint = 2f, saturation = 2f, contrast = 2f, lensFlare = 0.7f, waterTint = 0.5f,
                },
                new TimePreset
                {
                    // Just after sunset, the sun a few degrees below the horizon: dim blue light from a low moon,
                    // a deep blue sky with a lavender horizon and the first stars.
                    name = "BlueHour", sunWeight = 1f, sunElevation = -6f, moonElevation = 20f, moonColor = new Color(0.5f, 0.6f, 0.95f), moonIntensity = 0.45f,
                    skyBlend = 0.75f, skyColor = new Color(0.12f, 0.2f, 0.55f), horizonColor = new Color(0.55f, 0.5f, 0.75f), sunGlowColor = new Color(0.8f, 0.6f, 0.72f),
                    ambientIntensity = 0.85f, ambientTint = new Color(0.75f, 0.82f, 1f), night = 0.5f, moonPhase = 0.5f,
                    fogColorBlend = 0.75f, fogDistance = 0.8f, rimColor = new Color(0.6f, 0.75f, 1f),
                    temperature = -8f, saturation = -4f, contrast = 2f, exposure = -0.12f, lensFlare = 0.1f, waterTint = 0.6f,
                },
                new TimePreset
                {
                    // Moonlight, after the game's own night hole (Snow 13): the same light, blue and dim; a dark
                    // sky with stars and moon; cool, dimmer fill light.
                    name = "Night", sunWeight = 1f, sunElevation = -30f, moonElevation = 40f, moonColor = new Color(0.36f, 0.48f, 0.62f), moonIntensity = 0.85f,
                    skyBlend = 0.85f, skyColor = new Color(0.03f, 0.06f, 0.18f), horizonColor = new Color(0.12f, 0.16f, 0.32f), sunGlowColor = new Color(0.55f, 0.65f, 0.9f),
                    ambientIntensity = 0.6f, ambientTint = new Color(0.7f, 0.8f, 1f), night = 1f, moonPhase = 0.5f,
                    fogColorBlend = 0.8f, fogDistance = 0.8f, rimColor = new Color(0.7f, 0.85f, 1.2f),
                    temperature = -12f, saturation = -12f, contrast = 3f, exposure = -0.25f, lensFlare = 0.05f, waterTint = 0.6f,
                },
            },
        };
    }
}
