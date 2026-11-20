using BepInEx.Configuration;
using LightGreen.Presets;
using UnityEngine.InputSystem;

namespace LightGreen
{
    /// <summary>Ambient occlusion cost and detail (Quality.AmbientOcclusionQuality).</summary>
    internal enum AOQuality { Low, Medium, High }

    /// <summary>Where LightGreen's short messages go (General.Notices).</summary>
    internal enum NoticeTarget { Chat, Screen, Off }

    /// <summary>LightGreen's settings (BepInEx/config/com.lightgreen.cfg). Each feature adds its own as it's built.</summary>
    internal static class Settings
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<NoticeTarget> Notices;
        internal static ConfigEntry<float> NoticeDelay;
        internal static ConfigEntry<Key> ToggleKey;
        internal static ConfigEntry<Key> TimeKey;
        internal static ConfigEntry<Key> RainKey;
        internal static ConfigEntry<Key> FilterKey;

        internal static ConfigEntry<TimeOfDay> Time;
        internal static ConfigEntry<float> ProgressionMinutes;
        internal static ConfigEntry<float> TransitionSeconds;

        internal static ConfigEntry<float> DayBrightness;
        internal static ConfigEntry<float> NightBrightness;

        internal static ConfigEntry<string> Filter;
        internal static ConfigEntry<float> FilterStrength;

        internal static ConfigEntry<float> ShadowTint;
        internal static ConfigEntry<float> ColorGrading;
        internal static ConfigEntry<float> DistanceHaze;
        internal static ConfigEntry<float> LensFlare;
        internal static ConfigEntry<float> WaterTuning;
        internal static ConfigEntry<float> IceGlints;
        internal static ConfigEntry<float> TiltShift;
        internal static ConfigEntry<float> PhotoDepthOfField;
        internal static ConfigEntry<float> Vignette;
        internal static ConfigEntry<float> ChromaticAberration;
        internal static ConfigEntry<float> AmbientOcclusion;
        internal static ConfigEntry<float> EffectLights;
        internal static ConfigEntry<float> BallGlow;
        internal static ConfigEntry<bool> BallGlowOwnOnly;
        internal static ConfigEntry<float> Lightning;
        internal static ConfigEntry<float> CityNight;
        internal static ConfigEntry<float> Rain;
        internal static ConfigEntry<float> Snow;
        internal static ConfigEntry<float> CartoonRain;
        internal static ConfigEntry<float> Footprints;
        internal static ConfigEntry<float> FootprintSeconds;
        internal static ConfigEntry<int> FootprintMax;

        internal static ConfigEntry<bool> Grass;

        internal static ConfigEntry<int> SunShadowResolution;
        internal static ConfigEntry<float> SunShadowDistance;
        internal static ConfigEntry<AOQuality> AmbientOcclusionQuality;


        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                "Turns LightGreen on. Off: the game looks exactly as it does without the mod.");
            Notices = config.Bind("General", "Notices", NoticeTarget.Chat,
                "Where LightGreen says what a key or a config edit changed (\"Filter: Noir\"). Chat: a line in the game's chat box, " +
                "seen only by you (the top-left corner when there is no chat box). Screen: the top-left corner. Off: nowhere.");
            NoticeDelay = config.Bind("General", "NoticeDelay", 0.5f, new ConfigDescription(
                "Chat only: seconds to wait after the last key press before posting, so stepping through filters posts one line, " +
                "not one per press. 0 posts every change at once.",
                new AcceptableValueRange<float>(0f, 2f)));

            ToggleKey = config.Bind("Keys", "ToggleKey", Key.F8,
                "Switches LightGreen off and on while playing, to compare with the game's own look.");
            TimeKey = config.Bind("Keys", "TimeKey", Key.F9,
                "Steps through the times of day (it changes TimeOfDay.Time).");
            RainKey = config.Bind("Keys", "RainKey", Key.F10,
                "Steps the weather: clear, rain, cartoon rain, snow, then clear again (it changes Effects.Rain, Effects.CartoonRain " +
                "and Effects.Snow). " +
                "Each comes back as heavy as it last was, or 1.");
            FilterKey = config.Bind("Keys", "FilterKey", Key.F11,
                "Steps to the next photo filter (it changes Filters.Filter); with Shift, the previous one.");

            Time = config.Bind("TimeOfDay", "Time", TimeOfDay.HoleDefault,
                "HoleDefault keeps each hole's own sun and sky. Dawn, Morning, Noon, GoldenHour, Sunset, BlueHour and Night move the sun " +
                "and recolour the sky, light, haze and water. Only you see it; nothing is sent to other players.");
            ProgressionMinutes = config.Bind("TimeOfDay", "ProgressionMinutes", 0f,
                new ConfigDescription("Minutes for a full day (Dawn through Night and round again). 0 = the time stays put.",
                    new AcceptableValueRange<float>(0f, 240f)));
            TransitionSeconds = config.Bind("TimeOfDay", "TransitionSeconds", 3f,
                new ConfigDescription("How long a change of time of day takes to blend in.", new AcceptableValueRange<float>(0f, 30f)));

            DayBrightness = config.Bind("Brightness", "Day", 1f,
                new ConfigDescription("How bright daylight is, on every hole and at every time of day: the sun, the fill light, the " +
                    "sky and the haze together. 1 = as LightGreen and the hole have it. Lights and glows aren't changed.",
                    new AcceptableValueRange<float>(0.5f, 1.5f)));
            NightBrightness = config.Bind("Brightness", "Night", 1f,
                new ConfigDescription("How bright night is (lower = darker), at Night and on holes the game sets at night; dusk falls " +
                    "between Day and Night. Lit windows, lampposts, glowing balls and explosions keep their brightness, so they " +
                    "stand out more in a darker night.",
                    new AcceptableValueRange<float>(0.25f, 1.5f)));

            Filter = config.Bind("Filters", "Filter", "None", new ConfigDescription(
                "A photo filter on top of LightGreen's look, like a photo editor's: None, a filter from LightGreen.filters.json, " +
                "or a .cube file in the LightGreen.filters folder (its name without .cube). The list below is every filter " +
                "LightGreen has found, kept up to date as filters are added or removed. The HUD isn't filtered. " +
                "Keys.FilterKey steps through them.", new FilterChoices()));
            FilterStrength = config.Bind("Filters", "Strength", 1f,
                new ConfigDescription("How much of the photo filter shows: 0 = none, 1 = all of it.", new AcceptableValueRange<float>(0f, 1f)));

            ShadowTint = Effect(config, "ShadowTint", 1f, 4f, "Shadows lean toward the biome's hue (cool in Snow, warm in Desert).");
            ColorGrading = Effect(config, "ColorGrading", 1f, 3f, "Per-biome and time-of-day white balance, saturation and contrast.");
            DistanceHaze = Effect(config, "DistanceHaze", 1f, 4f, "Distant terrain fades toward the horizon colour.");
            LensFlare = Effect(config, "LensFlare", 1f, 4f, "Streaks and ghosts when looking toward a low sun. Off in VR.");
            WaterTuning = Effect(config, "WaterTuning", 1f, 3f, "Sun highlights on water, and water that follows the time of day.");
            IceGlints = Effect(config, "IceGlints", 1f, 3f, "Sparkle on snow terrain. Halved in VR.");
            TiltShift = Effect(config, "TiltShift", 1f, 2f, "The hole-overview flyover looks like a miniature. Off in VR.");
            PhotoDepthOfField = Effect(config, "PhotoDepthOfField", 1f, 4f,
                "Photo mode focuses on what's in the middle of the frame and softens the rest. Off in VR.");
            Vignette = Effect(config, "Vignette", 0f, 4f, "Darker screen corners, on top of the course's own. Off in VR.");
            ChromaticAberration = Effect(config, "ChromaticAberration", 0f, 4f, "Colour fringes at the screen edges. Off in VR.");
            AmbientOcclusion = Effect(config, "AmbientOcclusion", 1f, 4f,
                "Soft darkening where surfaces meet: under and around objects, in corners and crevices. Uses the game's own " +
                "ambient occlusion (HTrace AO), turned on for every course, with the grass blades left out so open grass stays " +
                "even. Off in VR.");
            EffectLights = Effect(config, "EffectLights", 1f, 4f,
                "Explosions light the ground and players around them for a moment (the big ones with shadows), muzzle " +
                "flashes pop, and rocket and freeze-bomb trails carry a light. Gentler with the game's Explosion flashes option off. " +
                "Needs the game's Dynamic Lighting setting on.");
            BallGlow = Effect(config, "BallGlow", 1f, 3f,
                "Balls glow evenly all over in the dark, your own the brightest, so they stay easy to find at night. " +
                "Off in daylight.");
            BallGlowOwnOnly = config.Bind("Effects", "BallGlowOwnOnly", false,
                "Only your own ball glows (a calmer scene in a crowded lobby).");
            Lightning = Effect(config, "Lightning", 1f, 3f,
                "The Thunderstorm item's lightning lights where it lands (with a shadow) and the ground for a long way round, " +
                "flickering two or three times. With the game's Explosion flashes option off, and in VR, it is one gentle pulse instead. " +
                "Needs the game's Dynamic Lighting setting on.");
            CityNight = Effect(config, "CityNight", 1f, 4f,
                "City holes at night, as the game's own night City holes look: building windows and vehicle lights glow, " +
                "lampposts light the street (the nearest with shadows), and bright light blooms. Follows how dark it is; " +
                "nothing by day. The lampposts need the game's Dynamic Lighting setting on.");
            Rain = Effect(config, "Rain", 0f, 3f,
                "Rain on every course: streaks, splashes where it lands, and a grey overcast sky. Surfaces don't get wet. " +
                "Off by default: it's only a look, and could be mistaken for the Thunderstorm item. The value is how heavy. " +
                "Keys.RainKey steps through clear, rain, cartoon rain and snow while playing. Only one weather shows at a time: " +
                "turning one on turns the others off.");
            CartoonRain = Effect(config, "CartoonRain", 0f, 3f,
                "Cartoon rain on every course: bold, bright white streaks, easier to see than Effects.Rain, ripple rings where " +
                "drops land, and a soft blue mist that keeps the colours bright. Off by default. The value is how heavy. " +
                "Keys.RainKey steps through clear, rain, cartoon rain and snow. Only one weather shows at a time: turning one " +
                "on turns the others off.");
            Snow = Effect(config, "Snow", 0f, 3f,
                "Snow on every course: soft, round cartoon flakes drifting with the wind, a few big ones close by, and a bright, " +
                "pale blue sky. Nothing settles on the ground. Off by default. The value is how heavy. Keys.RainKey steps through " +
                "clear, rain, cartoon rain and snow. Only one weather shows at a time: turning one on turns the others off, and " +
                "if more than one is on when the game starts, snow is kept, then cartoon rain.");
            Footprints = Effect(config, "Footprints", 1f, 2f,
                "Footprints in snow last longer (Effects.FootprintSeconds) and show as blue-grey dents that read on white, " +
                "fading out at the end. Sand keeps the game's prints, and so does a player a smoke bomb hides from you. " +
                "The value is how dark the prints are.");
            FootprintSeconds = config.Bind("Effects", "FootprintSeconds", 45f,
                new ConfigDescription("How long a footprint in snow lasts, in seconds (the game's last about 2).",
                    new AcceptableValueRange<float>(2f, 180f)));
            FootprintMax = config.Bind("Effects", "FootprintMax", 300,
                new ConfigDescription("How many footprints in snow there can be at once; past it the oldest fade out. " +
                    "Lower it for big lobbies on a slower PC.", new AcceptableValueRange<int>(20, 2000)));

            Grass = config.Bind("Shading", "Grass", true,
                "Draws the game's grass blades. false hides them: the terrain's own colour shows instead, and drawing the " +
                "grass costs nothing. The grass comes back with LightGreen off (ToggleKey).");

            SunShadowResolution = config.Bind("Quality", "SunShadowResolution", 4096,
                new ConfigDescription("Sun shadow map size in pixels: sharper shadows. 0 = the game's setting.",
                    new AcceptableValueList<int>(0, 2048, 4096, 8192)));
            SunShadowDistance = config.Bind("Quality", "SunShadowDistance", 200f,
                new ConfigDescription("How far away (metres) the sun still casts shadows. 0 = the game's setting.",
                    new AcceptableValueRange<float>(0f, 500f)));
            AmbientOcclusionQuality = config.Bind("Quality", "AmbientOcclusionQuality", AOQuality.Medium,
                "Ambient occlusion detail. Low: quarter resolution, cheapest. Medium: half resolution. High: full resolution.");

            DropRemovedSettings(config);
        }

        /// <summary>Settings an earlier LightGreen had that no longer exist; removed from the config file on load.</summary>
        private static readonly ConfigDefinition[] Removed =
        {
            new ConfigDefinition("Effects", "FilmGrain"),
            new ConfigDefinition("Quality", "AmbientOcclusionIgnoresGrass"),   // always on now
            new ConfigDefinition("Effects", "Snowfall"),   // removed: not worth its cost
        };

        /// <summary>
        /// BepInEx keeps lines it no longer has a setting for (orphaned entries) and writes them back on every save.
        /// This drops the ones LightGreen has removed, so they leave the file instead of lingering there.
        /// </summary>
        private static void DropRemovedSettings(ConfigFile config)
        {
            try
            {
                var orphans = HarmonyLib.Traverse.Create(config).Property("OrphanedEntries")
                    .GetValue<System.Collections.Generic.Dictionary<ConfigDefinition, string>>();
                if (orphans == null) return;
                bool removed = false;
                foreach (ConfigDefinition definition in Removed)
                    removed |= orphans.Remove(definition);
                if (removed) config.Save();
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"Couldn't tidy removed settings from the config file: {e.Message}");
            }
        }

        /// <summary>
        /// An effect's strength: 0 = off, 1 = tuned. Each has its own most: where turning it up stops making a
        /// difference, or where it starts to look broken, flash too hard or cost too much.
        /// </summary>
        private static ConfigEntry<float> Effect(ConfigFile config, string key, float value, float most, string description) =>
            config.Bind("Effects", key, value, new ConfigDescription(
                description + (most > 2f ? $" 0 = off, 1 = tuned, 2 = double, up to {most:0}." : " 0 = off, 1 = tuned, 2 = double (the most)."),
                new AcceptableValueRange<float>(0f, most)));
    }
}
