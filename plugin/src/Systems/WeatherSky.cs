using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// The sky under the weather, for all three kinds, so they never pull it different ways: overcast while any weather
    /// falls, at once on a new hole and eased over 10 s otherwise. Rain's is grey; snow's bright and pale blue
    /// (<see cref="EnvironmentController.Snowy"/>); cartoon rain's a soft blue mist (<see cref="EnvironmentController.Misty"/>).
    /// It keeps its last kind of cloud while it clears, and going from one weather to another, the old kind's share
    /// hands over to the new one's. The time of day, the sky and post-processing draw it; this only says how much. Runs
    /// before the time of day, so they use this frame's values.
    /// </summary>
    internal sealed class WeatherSky : IFeature, IMapAware
    {
        /// <summary>Seconds for the sky to turn grey or clear.</summary>
        private const float OvercastSeconds = 10f;

        private readonly EnvironmentController _environment;
        private bool _fresh = true;
        private bool _touched;

        internal WeatherSky(EnvironmentController environment) => _environment = environment;

        public string Name => "Weather sky";

        public void Apply()
        {
            if (!GameEvents.InHole)
            {
                Restore();
                return;
            }

            WeatherKind kind = Weather.Current;
            bool weather = kind != WeatherKind.Clear;
            if (weather || _touched)
            {
                float overcast = weather ? Mathf.Clamp01(Weather.Strength) : 0f;
                float snowy = weather ? (kind == WeatherKind.Snow ? 1f : 0f) : _environment.Snowy;
                float misty = weather ? (kind == WeatherKind.CartoonRain ? 1f : 0f) : _environment.Misty;
                if (!(_fresh && weather))
                {
                    float step = Time.deltaTime / OvercastSeconds;
                    overcast = Mathf.MoveTowards(_environment.Overcast, overcast, step);
                    snowy = Mathf.MoveTowards(_environment.Snowy, snowy, step);
                    misty = Mathf.MoveTowards(_environment.Misty, misty, step);
                }
                _environment.SetWeatherSky(overcast, snowy, misty);
                _touched = overcast > 0f;
            }
            _fresh = false;
        }

        public void Restore()
        {
            if (_touched)
            {
                _environment.SetWeatherSky(0f, 0f, 0f);
                _touched = false;
            }
            _fresh = true;
        }

        /// <summary>A new map or hole: if a weather is on, it starts overcast.</summary>
        public void OnBiomeChanged() => _fresh = true;

        public void OnHoleChanged() => _fresh = true;
    }
}
