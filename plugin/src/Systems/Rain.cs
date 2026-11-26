using LightGreen.Core;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// Rain on every course, the driving range and Snow holes included (the game has no weather of its own), while
    /// Effects.Rain is above 0. Off by default: it's only a look, and could be mistaken for the Thunderstorm item.
    /// <list type="bullet">
    /// <item>Streaks round the camera, from the particle layer: near, mid and a faint far layer, slanted by the game's
    /// wind, capped on screen so none hides the ball. Each fills a hollow cylinder nested inside the next (near 2–15 m,
    /// mid 15–45 m, far 45–120 m), so no big far drop falls past the lens; about 2,800 at strength 1. Half opaque up
    /// close (0.5, 0.35 mid, 0.16 far): fainter, it read as haze rather than rain.</item>
    /// <item>Splashes: small droplet bursts where rain lands within 15 m of the camera, placed by a few ground checks a
    /// frame (cheaper than particle collision).</item>
    /// <item>Overcast: the sun dims and greys, and the sky and fog turn grey: set by <see cref="WeatherSky"/>, which keeps
    /// the sky for all three weathers, and drawn by the time of day and post-processing.</item>
    /// </list>
    /// No wet look: surfaces stay as the game draws them (wet surfaces and puddles needed LightGreen's own shaders).
    /// Only one weather falls at a time (<see cref="Weather"/>).
    /// </summary>
    internal sealed class Rain : FallingWeather
    {
        internal Rain(EnvironmentController environment) : base(environment, WeatherKind.Rain, "rain") => Layer = MakeLayer();

        public override string Name => "Rain";

        private static ParticleLayer MakeLayer() => new ParticleLayer("LightGreen rain",
            new ParticleLayer.Spec
            {
                Name = "near", Width = 30f, Height = 16f, Lift = 4f, Density = 0.12f, Fall = 8f,
                MinSize = 0.016f, MaxSize = 0.026f, MaxScreenSize = 0.005f, Alpha = 0.5f, Stretch = 0.06f,
                Clearance = 2f,
            },
            new ParticleLayer.Spec
            {
                Name = "mid", Width = 90f, Height = 40f, Lift = 10f, Density = 0.004f, Fall = 9f,
                MinSize = 0.03f, MaxSize = 0.045f, MaxScreenSize = 0.004f, Alpha = 0.35f, Stretch = 0.08f,
                Clearance = 15f,
            },
            new ParticleLayer.Spec
            {
                Name = "far", Width = 240f, Height = 70f, Lift = 25f, Density = 0.0002f, Fall = 10f,
                MinSize = 0.15f, MaxSize = 0.2f, MaxScreenSize = 0.006f, Alpha = 0.16f, Stretch = 0.1f,
                Clearance = 45f,
            });

        private static readonly Color RainColor = new Color(0.72f, 0.78f, 0.88f);

        /// <summary>Drift with the wind: m/s at calm and per km/h.</summary>
        private const float DriftAtCalm = 0.3f;
        private const float DriftPerKmh = 0.12f;

        /// <summary>Splashes per second round the camera at strength 1, and how far out.</summary>
        private const float SplashesPerSecond = 1200f;
        private const float SplashRadius = 15f;

        private ParticleSystem _splashes;
        private float _splashDebt;

        protected override Vector2 WindTarget() => GameWind.Direction * (DriftAtCalm + DriftPerKmh * GameWind.SpeedKmh);

        protected override Color ColorAt(float darkness) => RainColor * Mathf.Lerp(1f, 0.35f, darkness);

        protected override void AfterFall(Camera camera, float strength, Color color) => Splash(camera, strength, color);

        // ---- Splashes -----------------------------------------------------------------------------------------

        private void Splash(Camera camera, float strength, Color color)
        {
            if (camera == null || !EnsureSplashes()) return;
            _splashDebt += SplashesPerSecond * strength * Time.deltaTime;
            int count = Mathf.Min(Mathf.FloorToInt(_splashDebt), 60);
            _splashDebt -= Mathf.FloorToInt(_splashDebt);

            Vector3 eye = camera.transform.position;
            var emit = new ParticleSystem.EmitParams { startColor = new Color(color.r, color.g, color.b, 0.6f) };
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = LocalRandom.InsideUnitCircle * SplashRadius;
                var origin = new Vector3(eye.x + offset.x, eye.y + 20f, eye.z + offset.y);
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                // A few droplets thrown up and out.
                for (int d = 0; d < 3; d++)
                {
                    Vector2 out2 = LocalRandom.InsideUnitCircle * 0.8f;
                    emit.position = hit.point + hit.normal * 0.02f;
                    emit.velocity = new Vector3(out2.x, LocalRandom.Range(1.2f, 2.2f), out2.y);
                    emit.startSize = LocalRandom.Range(0.02f, 0.035f);
                    emit.startLifetime = LocalRandom.Range(0.2f, 0.32f);
                    _splashes.Emit(emit, 1);
                }
            }
            if (!_splashes.gameObject.activeSelf) _splashes.gameObject.SetActive(true);
        }

        private bool EnsureSplashes()
        {
            if (_splashes != null) return true;
            Material source = ParticleLayer.SharedMaterial();
            if (source == null) return false;

            var go = new GameObject("LightGreen rain: splashes") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            _splashes = go.AddComponent<ParticleSystem>();
            _splashes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _splashes.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1f;
            main.maxParticles = 1500;
            main.startSpeed = 0f;
            var emission = _splashes.emission;
            emission.enabled = false;
            var shape = _splashes.shape;
            shape.enabled = false;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.maxParticleSize = 0.004f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = new Material(source) { name = "LightGreen rain: splashes", hideFlags = HideFlags.DontSave };
            _splashes.Play();
            return true;
        }

        private void StopSplashes()
        {
            if (_splashes == null) return;
            _splashes.Clear();
            _splashDebt = 0f;
        }

        // ---- Restore -------------------------------------------------------------------------------------------

        public override void Restore()
        {
            base.Restore();
            StopSplashes();
        }

        internal override void Destroy()
        {
            base.Destroy();
            if (_splashes != null)
            {
                Object.Destroy(_splashes.GetComponent<ParticleSystemRenderer>().sharedMaterial);
                Object.Destroy(_splashes.gameObject);
            }
            _splashes = null;
        }
    }
}
