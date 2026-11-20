using BepInEx.Configuration;

namespace LightGreen.Systems
{
    internal enum WeatherKind { Clear, Rain, CartoonRain, Snow }

    /// <summary>
    /// Which weather is falling: only one at a time. Effects.Snow, Effects.CartoonRain and Effects.Rain each say how heavy
    /// theirs is; the plugin keeps at most one above 0, and should two be on anyway, snow comes first, then cartoon rain.
    /// Keys.RainKey steps clear, rain, cartoon rain, snow, clear.
    /// </summary>
    internal static class Weather
    {
        private const float On = 0.01f;

        internal static WeatherKind Current =>
            Settings.Snow.Value > On ? WeatherKind.Snow
            : Settings.CartoonRain.Value > On ? WeatherKind.CartoonRain
            : Settings.Rain.Value > On ? WeatherKind.Rain
            : WeatherKind.Clear;

        /// <summary>The setting for a kind of weather (null for clear).</summary>
        internal static ConfigEntry<float> Entry(WeatherKind kind) =>
            kind == WeatherKind.Rain ? Settings.Rain
            : kind == WeatherKind.CartoonRain ? Settings.CartoonRain
            : kind == WeatherKind.Snow ? Settings.Snow
            : null;

        /// <summary>How heavy the current weather is (0 when clear).</summary>
        internal static float Strength
        {
            get
            {
                ConfigEntry<float> entry = Entry(Current);
                return entry == null ? 0f : UnityEngine.Mathf.Max(0f, entry.Value);
            }
        }

        /// <summary>The next weather RainKey goes to.</summary>
        internal static WeatherKind Next(WeatherKind kind) =>
            kind == WeatherKind.Clear ? WeatherKind.Rain
            : kind == WeatherKind.Rain ? WeatherKind.CartoonRain
            : kind == WeatherKind.CartoonRain ? WeatherKind.Snow
            : WeatherKind.Clear;

        internal static string Name(WeatherKind kind) =>
            kind == WeatherKind.Rain ? "rain"
            : kind == WeatherKind.CartoonRain ? "cartoon rain"
            : kind == WeatherKind.Snow ? "snow"
            : "clear";

        /// <summary>"Weather: cartoon rain (1)", or "Weather: clear".</summary>
        internal static string Notice()
        {
            WeatherKind kind = Current;
            return kind == WeatherKind.Clear ? "Weather: clear" : $"Weather: {Name(kind)} ({Strength:0.##})";
        }
    }
}
