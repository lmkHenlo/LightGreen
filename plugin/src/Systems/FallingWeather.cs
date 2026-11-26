using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// What rain, cartoon rain and snow share: while their weather is the one falling (<see cref="Weather"/>), their
    /// particle layer follows the camera, drifts with the game's wind (eased, so a gust doesn't snap it) and is
    /// coloured for how dark the scene is. Each says how it drifts (<see cref="WindTarget"/>) and what colour it is
    /// (<see cref="ColorAt"/>); the sky for all three is <see cref="WeatherSky"/>'s.
    /// </summary>
    internal abstract class FallingWeather : IFeature
    {
        protected readonly EnvironmentController Environment;
        private readonly WeatherKind _kind;
        private readonly string _logKey;
        private Vector2 _wind;

        /// <summary>The particles, made by the weather's constructor.</summary>
        protected ParticleLayer Layer { get; set; }

        protected FallingWeather(EnvironmentController environment, WeatherKind kind, string logKey)
        {
            Environment = environment;
            _kind = kind;
            _logKey = "hole:" + logKey;
        }

        public abstract string Name { get; }

        public void Apply()
        {
            if (!GameEvents.InHole || Weather.Current != _kind)
            {
                Restore();
                return;
            }

            float strength = Weather.Strength;
            _wind = Vector2.MoveTowards(_wind, WindTarget(), Time.deltaTime * 0.5f);
            Color color = ColorAt(Mathf.Clamp01(Environment.SceneDarkness));
            color.a = 1f;

            Camera camera = GameManager.Camera;
            bool wasRunning = Layer.Running;
            Layer.Update(camera, strength, color, _wind);
            if (!wasRunning && Layer.Running) Plugin.LogChange(_logKey, $"{Name}: on, strength {strength:0.##}.");
            AfterFall(camera, strength, color);
        }

        /// <summary>The drift the particles move toward, in m/s across the ground, from <see cref="GameWind"/>.</summary>
        protected abstract Vector2 WindTarget();

        /// <summary>The particles' colour, for a scene this dark (0 day, 1 night, with the weather's own dimming).</summary>
        protected abstract Color ColorAt(float darkness);

        /// <summary>Anything more each frame while falling (rain's splashes).</summary>
        protected virtual void AfterFall(Camera camera, float strength, Color color)
        {
        }

        public virtual void Restore() => Layer.Stop();

        internal virtual void Destroy()
        {
            Restore();
            Layer.Destroy();
        }
    }
}
