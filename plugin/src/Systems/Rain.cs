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
    /// <item>Overcast: the sun dims and greys, and the sky and fog turn grey (the time of day and post-processing do
    /// it, from <see cref="EnvironmentController.Overcast"/>); at once on a new hole, eased over 10 s otherwise.</item>
    /// </list>
    /// No wet look: surfaces stay as the game draws them (wet surfaces and puddles needed LightGreen's own shaders).
    /// <para>The sky is the rain's job for the snow and the cartoon rain too (<see cref="Snow"/> and
    /// <see cref="CartoonRain"/> draw only their own particles), so they never pull it different ways: snow's overcast is
    /// bright and pale blue (<see cref="EnvironmentController.Snowy"/>), cartoon rain's a soft blue mist
    /// (<see cref="EnvironmentController.Misty"/>), eased over the same 10 s. Only one weather falls at a time
    /// (<see cref="Weather"/>).</para>
    /// </summary>
    internal sealed class Rain : IFeature
    {
        private readonly EnvironmentController _environment;

        internal Rain(EnvironmentController environment) => _environment = environment;

        public string Name => "Rain";

        private readonly ParticleLayer _layer = new ParticleLayer("LightGreen rain",
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

        /// <summary>Seconds for the sky to turn grey or clear.</summary>
        private const float OvercastSeconds = 10f;

        private ParticleSystem _splashes;
        private float _splashDebt;
        private Vector2 _wind;
        private bool _fresh = true;
        private bool _touchedSky;

        /// <summary>A new hole: if it's raining, it starts overcast.</summary>
        internal void Rescan() => _fresh = true;

        public void Apply()
        {
            if (!GameEvents.InHole)
            {
                Restore();
                return;
            }

            WeatherKind kind = Weather.Current;
            bool raining = kind == WeatherKind.Rain;
            float strength = raining ? Weather.Strength : 0f;

            // The sky: overcast in any weather, eased (at once on a new hole). It keeps its last kind of cloud while it
            // clears; going from one weather to another, the old kind's share hands over to the new one's.
            bool weather = kind != WeatherKind.Clear;
            float overcastTarget = weather ? Mathf.Clamp01(Weather.Strength) : 0f;
            float snowyTarget = weather ? (kind == WeatherKind.Snow ? 1f : 0f) : _environment.Snowy;
            float mistyTarget = weather ? (kind == WeatherKind.CartoonRain ? 1f : 0f) : _environment.Misty;
            if (weather || _touchedSky)
            {
                bool atOnce = _fresh && weather;
                float step = Time.deltaTime / OvercastSeconds;
                _environment.Overcast = atOnce ? overcastTarget : Mathf.MoveTowards(_environment.Overcast, overcastTarget, step);
                _environment.Snowy = atOnce ? snowyTarget : Mathf.MoveTowards(_environment.Snowy, snowyTarget, step);
                _environment.Misty = atOnce ? mistyTarget : Mathf.MoveTowards(_environment.Misty, mistyTarget, step);
                _touchedSky = _environment.Overcast > 0f;
            }
            _fresh = false;

            if (!raining)
            {
                _layer.Stop();
                StopSplashes();
                return;
            }

            Vector2 target = GameWind.Direction * (DriftAtCalm + DriftPerKmh * GameWind.SpeedKmh);
            _wind = Vector2.MoveTowards(_wind, target, Time.deltaTime * 0.5f);
            float dark = Mathf.Clamp01(_environment.SceneDarkness);
            float b = Mathf.Lerp(1f, 0.35f, dark);
            Color color = RainColor * b;
            color.a = 1f;

            Camera camera = GameManager.Camera;
            bool wasRunning = _layer.Running;
            _layer.Update(camera, strength, color, _wind);
            if (!wasRunning && _layer.Running) Plugin.LogChange("hole:rain", $"Rain: on, strength {strength:0.##}.");
            Splash(camera, strength, color);
        }

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

        public void Restore()
        {
            _layer.Stop();
            StopSplashes();
            if (_touchedSky)
            {
                _environment.Overcast = 0f;
                _environment.Snowy = 0f;
                _environment.Misty = 0f;
                _touchedSky = false;
            }
            _fresh = true;
        }

        internal void Destroy()
        {
            Restore();
            _layer.Destroy();
            if (_splashes != null)
            {
                Object.Destroy(_splashes.GetComponent<ParticleSystemRenderer>().sharedMaterial);
                Object.Destroy(_splashes.gameObject);
            }
            _splashes = null;
        }
    }
}
