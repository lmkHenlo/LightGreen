using System;
using BepInEx.Configuration;

namespace LightGreen.Systems
{
    internal enum WeatherKind { Clear, Rain, CartoonRain, Snow }

    /// <summary>
    /// Which weather is falling: only one at a time. Effects.Snow, Effects.CartoonRain and Effects.Rain each say how heavy
    /// theirs is; <see cref="Start"/> keeps at most one above 0, and should two be on anyway, snow comes first, then cartoon rain.
    /// Keys.RainKey steps clear, rain, cartoon rain, snow, clear.
    /// </summary>
    internal static class Weather
    {
        private const float On = 0.01f;

        private static readonly WeatherKind[] Falling = { WeatherKind.Rain, WeatherKind.CartoonRain, WeatherKind.Snow };

        // How heavy each weather last was above off, for RainKey to bring it back as it was (1 at first).
        private static float _lastRain = 1f, _lastCartoonRain = 1f, _lastSnow = 1f;

        /// <summary>
        /// Keeps one weather at a time: if more than one is on at start, the first of snow, cartoon rain and rain stays;
        /// turning one on later turns the others off. <paramref name="changed"/> is told each change, with its notice.
        /// </summary>
        internal static void Start(Action<string> changed)
        {
            WeatherKind kept = Current;
            foreach (WeatherKind other in Falling)
            {
                if (other == kept || Entry(other).Value <= On) continue;
                Entry(other).Value = 0f;
                Plugin.Log.LogInfo($"Only one weather shows at a time: {Name(kept)} is kept, and Effects.{Entry(other).Definition.Key} is now 0.");
            }
            Remember();
            foreach (WeatherKind kind in Falling)
            {
                ConfigEntry<float> entry = Entry(kind);
                entry.SettingChanged += (_, __) =>
                {
                    if (entry.Value > On)
                        foreach (WeatherKind other in Falling)
                            if (other != kind && Entry(other).Value > On) Entry(other).Value = 0f;
                    Remember();
                    changed?.Invoke(Notice());
                };
            }
        }

        /// <summary>RainKey: clear, rain, cartoon rain, snow, clear; each comes back as heavy as it last was.</summary>
        internal static void Step()
        {
            WeatherKind now = Current;
            WeatherKind next = Next(now);
            if (next == WeatherKind.Clear) Entry(now).Value = 0f;
            else Entry(next).Value = next == WeatherKind.Rain ? _lastRain : next == WeatherKind.Snow ? _lastSnow : _lastCartoonRain;
        }

        private static void Remember()
        {
            if (Settings.Rain.Value > On) _lastRain = Settings.Rain.Value;
            if (Settings.CartoonRain.Value > On) _lastCartoonRain = Settings.CartoonRain.Value;
            if (Settings.Snow.Value > On) _lastSnow = Settings.Snow.Value;
        }

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
