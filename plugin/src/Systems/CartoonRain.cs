using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// Cartoon rain on every course while Effects.CartoonRain is above 0, from the user's reference picture: easier to see
    /// than <see cref="Rain"/>, still read as rain. Bold, nearly white, crisp-edged streaks (a disc drawn in code by
    /// <see cref="RainArt"/>, stretched along the fall), slanted the same way: with the game's wind, and always at least a
    /// steady lean. Near, mid and far layers from the particle layer, about 1,950 at strength 1 (the rain about 2,800,
    /// fainter and thinner). Each layer fills a hollow cylinder round the camera, tilted along the fall, nested inside
    /// the next: near 3–15 m, mid 15–45 m, far 45–120 m. The near layer's 3 m clear zone stops a streak passing right
    /// by the lens (drawn as a long white slash, fanned out by perspective); the mid and far layers' stop their big
    /// drops falling past the camera, where they showed as fat grey ovals. No ground splashes or ripples (taken out at the user's request).
    /// Its sky, a soft blue mist that keeps the colours bright, is set in <see cref="Rain"/> with the others'. At night the
    /// streaks dim. Only one weather at a time (<see cref="Weather"/>).
    /// </summary>
    internal sealed class CartoonRain : IFeature
    {
        private readonly EnvironmentController _environment;
        private readonly ParticleLayer _layer;
        private readonly Texture2D _disc;

        private static readonly Color StreakColor = new Color(0.92f, 0.96f, 1f);

        /// <summary>Drift with the wind (m/s at calm and per km/h), and the least lean, so the streaks always slant.</summary>
        private const float DriftAtCalm = 0.3f;
        private const float DriftPerKmh = 0.12f;
        private const float LeastLean = 2.5f;
        private static readonly Vector2 CalmLean = new Vector2(0.6f, 0.8f);

        private Vector2 _wind;

        internal CartoonRain(EnvironmentController environment)
        {
            _environment = environment;
            _disc = TextureArt.Make("LightGreen cartoon rain: streak", RainArt.Disc(32), 32);
            _layer = new ParticleLayer("LightGreen cartoon rain",
                new ParticleLayer.Spec
                {
                    Name = "near", Width = 30f, Height = 16f, Lift = 4f, Density = 0.08f, Fall = 10f,
                    MinSize = 0.025f, MaxSize = 0.035f, MaxScreenSize = 0.006f, Alpha = 0.85f, Stretch = 0.06f, Texture = _disc,
                    Clearance = 3f,
                },
                new ParticleLayer.Spec
                {
                    Name = "mid", Width = 90f, Height = 40f, Lift = 10f, Density = 0.003f, Fall = 11f,
                    MinSize = 0.06f, MaxSize = 0.08f, MaxScreenSize = 0.006f, Alpha = 0.7f, Stretch = 0.1f, Texture = _disc,
                    Clearance = 15f,
                },
                new ParticleLayer.Spec
                {
                    Name = "far", Width = 240f, Height = 70f, Lift = 25f, Density = 0.00015f, Fall = 12f,
                    MinSize = 0.22f, MaxSize = 0.28f, MaxScreenSize = 0.008f, Alpha = 0.35f, Stretch = 0.12f, Texture = _disc,
                    Clearance = 45f,
                });
        }

        public string Name => "Cartoon rain";

        public void Apply()
        {
            if (!GameEvents.InHole || Weather.Current != WeatherKind.CartoonRain)
            {
                Restore();
                return;
            }

            float strength = Weather.Strength;
            Vector2 direction = GameWind.Direction.sqrMagnitude > 1e-4f ? GameWind.Direction.normalized : CalmLean;
            Vector2 target = direction * Mathf.Max(LeastLean, DriftAtCalm + DriftPerKmh * GameWind.SpeedKmh);
            _wind = Vector2.MoveTowards(_wind, target, Time.deltaTime * 0.5f);

            float dark = Mathf.Clamp01(_environment.SceneDarkness);
            Color color = StreakColor * Mathf.Lerp(1f, 0.4f, dark);
            color.a = 1f;

            bool wasRunning = _layer.Running;
            _layer.Update(GameManager.Camera, strength, color, _wind);
            if (!wasRunning && _layer.Running) Plugin.LogChange("hole:cartoonrain", $"Cartoon rain: on, strength {strength:0.##}.");
        }

        public void Restore() => _layer.Stop();

        internal void Destroy()
        {
            _layer.Destroy();
            if (_disc != null) Object.Destroy(_disc);
        }

    }
}
