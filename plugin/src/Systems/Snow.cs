using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// Cartoon snow on every course while Effects.Snow is above 0: soft, round, slightly lumpy white flakes (drawn in
    /// code by <see cref="FlakeArt"/>, so nothing is shipped) drifting with the game's wind, swaying and turning as they
    /// fall. Four layers round the camera, from the particle layer:
    /// <list type="bullet">
    /// <item>near: a few big, soft flakes passing close to the lens, as if out of focus (they may cover the ball for a
    /// moment: wanted, for the look);</item>
    /// <item>close: medium, crisp flakes within about 10 m, where they're 15–35 px on screen and their lumpy outline and
    /// blue shade read (the main layer's flakes are mostly 10–25 m away, a few pixels across);</item>
    /// <item>main: the snowfall itself, crisp-edged, big enough at 20 m that the lumpy shape and blue shade still show
    /// (smaller, they read as dust and vanish against white cloud);</item>
    /// <item>far: tiny flakes for depth.</item>
    /// </list>
    /// About 4,850 flakes at strength 1 (the close layer's 440 among them), against the rain's 2,800; only the near and
    /// close ones are big on screen:
    /// the earlier snowfall (about 8,200, many of them large and far) cost too much frame rate.
    /// Magical rather than real: the flakes are a little brighter than white, so the game's bloom gives them a soft
    /// glow; at night they dim and turn blue. The sky (bright and pale blue) is set with the rain's, in <see cref="Rain"/>;
    /// the fog and a slightly colder, less saturated grade in <see cref="VolumeLayer"/>.
    /// Nothing settles on the ground: that needed shaders. Only one weather at a time: with snow on, there's no rain.
    /// </summary>
    internal sealed class Snow : IFeature
    {
        private readonly EnvironmentController _environment;
        private readonly ParticleLayer _layer;
        private readonly Texture2D _crisp;
        private readonly Texture2D _soft;

        /// <summary>Whether it's snowing: Effects.Snow above 0 (and then the rain stays off).</summary>
        internal static bool Falling => Weather.Current == WeatherKind.Snow;

        /// <summary>Drift with the wind: m/s at calm and per km/h. Slower than rain: snow floats.</summary>
        private const float DriftAtCalm = 0.25f;
        private const float DriftPerKmh = 0.09f;

        /// <summary>A little over white, so bloom lifts the flakes; and at full night, how dim and how blue.</summary>
        private const float DayGlow = 1.15f;
        private const float NightDim = 0.35f;
        private static readonly Color NightTint = new Color(0.8f, 0.88f, 1f);

        private Vector2 _wind;

        internal Snow(EnvironmentController environment)
        {
            _environment = environment;
            _crisp = Atlas("LightGreen snowflakes", 0.03f, 17);
            _soft = Atlas("LightGreen snowflakes (soft)", 0.35f, 29);
            _layer = new ParticleLayer("LightGreen snow",
                new ParticleLayer.Spec
                {
                    Name = "near", Width = 14f, Height = 8f, Lift = 1.5f, Density = 0.024f, Fall = 0.9f,
                    MinSize = 0.06f, MaxSize = 0.11f, MaxScreenSize = 0.04f, Alpha = 0.85f,
                    Texture = _soft, Tiles = 2, Sway = 0.35f, Spin = 0.6f,
                },
                new ParticleLayer.Spec
                {
                    Name = "close", Width = 20f, Height = 10f, Lift = 2f, Density = 0.11f, Fall = 1f,
                    MinSize = 0.08f, MaxSize = 0.13f, MaxScreenSize = 0.025f, Alpha = 1f,
                    Texture = _crisp, Tiles = 2, Sway = 0.3f, Spin = 0.7f,
                },
                new ParticleLayer.Spec
                {
                    Name = "main", Width = 50f, Height = 24f, Lift = 6f, Density = 0.06f, Fall = 1.1f,
                    MinSize = 0.07f, MaxSize = 0.12f, MaxScreenSize = 0.02f, Alpha = 1f,
                    Texture = _crisp, Tiles = 2, Sway = 0.3f, Spin = 0.8f,
                },
                new ParticleLayer.Spec
                {
                    Name = "far", Width = 160f, Height = 50f, Lift = 18f, Density = 0.0006f, Fall = 1.4f,
                    MinSize = 0.35f, MaxSize = 0.55f, MaxScreenSize = 0.004f, Alpha = 0.8f,
                    Texture = _crisp, Tiles = 2,
                });
        }

        public string Name => "Snow";

        public void Apply()
        {
            if (!GameEvents.InHole || !Falling)
            {
                _layer.Stop();
                return;
            }

            float strength = Mathf.Max(0f, Settings.Snow.Value);
            Vector2 target = GameWind.Direction * (DriftAtCalm + DriftPerKmh * GameWind.SpeedKmh);
            _wind = Vector2.MoveTowards(_wind, target, Time.deltaTime * 0.5f);

            float dark = Mathf.Clamp01(_environment.SceneDarkness);
            float glow = Mathf.Lerp(DayGlow, NightDim, dark);
            Color tint = Color.Lerp(Color.white, NightTint, dark);
            var color = new Color(tint.r * glow, tint.g * glow, tint.b * glow, 1f);

            bool wasRunning = _layer.Running;
            _layer.Update(GameManager.Camera, strength, color, _wind);
            if (!wasRunning && _layer.Running) Plugin.LogChange("hole:snow", $"Snow: on, strength {strength:0.##}.");
        }

        public void Restore() => _layer.Stop();

        internal void Destroy()
        {
            _layer.Destroy();
            if (_crisp != null) Object.Destroy(_crisp);
            if (_soft != null) Object.Destroy(_soft);
        }

        /// <summary>A 2 × 2 atlas of 64-pixel flakes from <see cref="FlakeArt"/>, with mipmaps so far flakes stay smooth.</summary>
        private static Texture2D Atlas(string name, float softness, int seed)
        {
            const int Tile = 64, Tiles = 2, Size = Tile * Tiles;
            return TextureArt.Make(name, FlakeArt.Atlas(Tile, Tiles, softness, seed), Size);
        }
    }
}
