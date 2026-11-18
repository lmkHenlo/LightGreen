using System.Linq;
using LightGreen.Core;
using LightGreen.Presets;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LightGreen.Systems
{
    /// <summary>
    /// The single source of truth for the time of day: it moves and recolours the hole's sun, drives
    /// the sky material and the ambient light, and publishes the values other systems use (sun colour,
    /// horizon colour, the hole's stock fog). Everything it changes is captured first and put back on
    /// Restore, or when the hole changes.
    /// </summary>
    internal sealed class EnvironmentController : IFeature
    {
        public string Name => "Time of day";

        /// <summary>Each frame on a hole; off a hole, everything back as the game had it.</summary>
        public void Apply()
        {
            if (!GameEvents.InHole)
            {
                Restore();
                return;
            }
            Update();
        }

        /// <summary>
        /// Degrees over which the light fades out as the sun reaches the horizon, and the moon's fades in as
        /// it sinks below, so the one scene light can be the sun by day and the moon by night.
        /// </summary>
        private const float HorizonFade = 3f;

        private static readonly TimeOfDay[] DayCycle = {
            TimeOfDay.Dawn, TimeOfDay.Morning, TimeOfDay.Noon, TimeOfDay.GoldenHour, TimeOfDay.Sunset, TimeOfDay.BlueHour, TimeOfDay.Night,
        };

        private readonly MaterialEdits _sky = new MaterialEdits();

        // The hole's own values, captured when it loads.
        private int _sceneHandle = -1;
        private Light _sun;
        private Material _skyMaterial;
        private Quaternion _stockSunRotation;
        private Color _stockSunColor;
        private float _stockSunIntensity;
        private SphericalHarmonicsL2 _stockAmbientProbe;
        private bool _captured;
        private bool _hadSun;
        private float _nextSunSearch;
        private bool _sunChanged;
        private bool _ambientChanged;

        // Time-of-day blending.
        private TimeOfDay _target = (TimeOfDay)(-1);
        private TimePreset _from;
        private TimePreset _to;
        private float _blend = 1f;
        private float _dayPhase;

        // Ambient refresh.
        private Vector4 _lastSkyKey;
        private float _nextAmbientRefresh;

        internal TimePreset Current { get; private set; } = new TimePreset();
        /// <summary>The scene's sun, once found.</summary>
        internal Light Sun => _sun;
        internal Color SunColor { get; private set; } = Color.white;
        internal float SunElevation { get; private set; } = 45f;
        /// <summary>Direction to the sun itself (below the horizon at night, when the scene light is the moon).</summary>
        internal Vector3 SunDirection { get; private set; } = Vector3.up;
        internal Color HorizonColor { get; private set; } = Color.grey;
        internal Color StockFogColor { get; private set; } = Color.grey;
        internal float StockFogStart { get; private set; }
        internal float StockFogEnd { get; private set; } = 600f;
        internal bool HasScene => _captured;

        /// <summary>
        /// 0 for the time of day's own sky, up to 1 for a fully overcast, rainy sky: the sun (or moon) dims and
        /// greys, and the sky and fog turn grey. Set by the rain each frame; eased there.
        /// </summary>
        internal float Overcast { get; set; }

        /// <summary>The sun's brightness under a full overcast, and how far the sky's colours go to grey.</summary>
        private const float OvercastSun = 0.45f;
        private const float OvercastSkyGrey = 0.7f;

        /// <summary>
        /// 0 in daylight, 1 at night: the time of day's own night setting, or, for holes the game itself sets
        /// at night (the City's Causeway and Sky Lane, a Snow hole), how dim the scene's main light is. Ball
        /// glow and the City at night follow it.
        /// </summary>
        internal float Darkness
        {
            get
            {
                float night = Mathf.Clamp01(Current.night);
                float dimSun = 1f;
                if (_sun != null && _sun.isActiveAndEnabled)
                {
                    Color c = _sun.color * _sun.intensity;
                    float brightness = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                    dimSun = Mathf.Clamp01((0.55f - brightness) / 0.35f);
                }
                return Mathf.Max(night, dimSun);
            }
        }

        /// <summary>The time of day being shown, for the on-screen notice.</summary>
        internal string TimeLabel => Settings.ProgressionMinutes.Value > 0f ? $"{Current.name} (day cycle)" : Settings.Time.Value.ToString();

        internal void Update()
        {
            CaptureIfNewScene();
            if (!_captured) return;
            UpdateTime();
            ApplySun();
            ApplySky();
            ApplyAmbient();
        }

        internal void OnPresetsReloaded() => _target = (TimeOfDay)(-1);

        // ---- Scene capture ------------------------------------------------------------------

        private static Light FindSun()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled) return RenderSettings.sun;
            return Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .Where(l => l.type == LightType.Directional && l.isActiveAndEnabled)
                .OrderByDescending(l => l.intensity)
                .FirstOrDefault();
        }

        private void CaptureIfNewScene()
        {
            int scene = SceneManager.GetActiveScene().handle;
            Material sky = RenderSettings.skybox;
            bool sunDestroyed = _hadSun && _sun == null;
            if (_captured && scene == _sceneHandle && sky == _skyMaterial && !sunDestroyed)
            {
                // The hole may add its sun a moment after the scene becomes active.
                if (!_hadSun && Time.unscaledTime >= _nextSunSearch)
                {
                    _nextSunSearch = Time.unscaledTime + 1f;
                    CaptureSun(FindSun());
                }
                return;
            }

            Restore();
            _sceneHandle = scene;
            _skyMaterial = sky;
            CaptureSun(FindSun());
            _nextSunSearch = Time.unscaledTime + 1f;
            _stockAmbientProbe = RenderSettings.ambientProbe;
            StockFogColor = RenderSettings.fogColor;
            StockFogStart = RenderSettings.fogStartDistance;
            StockFogEnd = RenderSettings.fogEndDistance;
            HorizonColor = _skyMaterial != null && _skyMaterial.HasProperty("_HorizonColor") ? _skyMaterial.GetColor("_HorizonColor") : StockFogColor;
            _captured = true;
            _target = (TimeOfDay)(-1); // re-pick the preset against the new hole
            Plugin.LogChange("hole:lighting", $"Scene lighting captured: sun {(_sun != null ? _sun.name : "not found yet")}, " +
                               $"sky {(_skyMaterial != null ? _skyMaterial.name : "none")}, fog {StockFogStart:0}-{StockFogEnd:0} m.");
        }

        private void CaptureSun(Light sun)
        {
            if (sun == null) return;
            _sun = sun;
            _hadSun = true;
            _stockSunRotation = sun.transform.rotation;
            _stockSunColor = sun.color;
            _stockSunIntensity = sun.intensity;
        }

        // ---- Time of day ---------------------------------------------------------------------

        private void UpdateTime()
        {
            float minutes = Settings.ProgressionMinutes.Value;
            if (minutes > 0f)
            {
                _dayPhase = (_dayPhase + Time.deltaTime / (minutes * 60f)) % 1f;
                float position = _dayPhase * DayCycle.Length;
                int i = Mathf.FloorToInt(position) % DayCycle.Length;
                float t = Mathf.SmoothStep(0f, 1f, position - Mathf.Floor(position));
                Current = TimePreset.Lerp(PresetStore.ForTime(DayCycle[i]), PresetStore.ForTime(DayCycle[(i + 1) % DayCycle.Length]), t);
                _target = (TimeOfDay)(-1);
                return;
            }

            TimeOfDay wanted = Settings.Time.Value;
            if (wanted != _target)
            {
                bool first = _target == (TimeOfDay)(-1) && _from == null;
                _from = first ? PresetStore.ForTime(wanted) : Current;
                _to = PresetStore.ForTime(wanted);
                _target = wanted;
                _blend = first ? 1f : 0f;
            }

            float seconds = Settings.TransitionSeconds.Value;
            _blend = seconds <= 0f ? 1f : Mathf.Min(1f, _blend + Time.deltaTime / seconds);
            Current = _blend >= 1f ? _to : TimePreset.Lerp(_from, _to, Mathf.SmoothStep(0f, 1f, _blend));
        }

        /// <summary>Moves the time-of-day setting to the next preset.</summary>
        internal static void StepTime()
        {
            int next = ((int)Settings.Time.Value + 1) % System.Enum.GetValues(typeof(TimeOfDay)).Length;
            Settings.Time.Value = (TimeOfDay)next;
        }

        // ---- Sun --------------------------------------------------------------------------------

        private void ApplySun()
        {
            if (_sun == null)
            {
                SunColor = Color.white;
                return;
            }

            float w = Mathf.Clamp01(Current.sunWeight);
            float overcast = Mathf.Clamp01(Overcast);
            float yaw = _stockSunRotation.eulerAngles.y;
            if (w > 0f || overcast > 0.001f)
            {
                Quaternion rotation = _stockSunRotation;
                Color sunColor = _stockSunColor;
                float sunIntensity = _stockSunIntensity;
                if (w > 0f)
                {
                    // The scene has one directional light. With the sun up it's the sun; with the sun below the
                    // horizon it becomes the moon, high on the opposite side. Its brightness passes through zero at
                    // the horizon, so the switch can't be seen.
                    float elevation = Current.sunElevation;
                    Quaternion light;
                    Color color;
                    float intensity;
                    if (elevation >= 0f)
                    {
                        light = Quaternion.Euler(Mathf.Max(elevation, 0.5f), yaw, 0f);
                        color = Current.sunColor;
                        intensity = Current.sunIntensity * Mathf.SmoothStep(0f, 1f, elevation / HorizonFade);
                    }
                    else
                    {
                        light = Quaternion.Euler(Current.moonElevation, yaw + 180f, 0f);
                        color = Current.moonColor;
                        intensity = Current.moonIntensity * Mathf.SmoothStep(0f, 1f, -elevation / HorizonFade);
                    }
                    rotation = Quaternion.Slerp(_stockSunRotation, light, w);
                    sunColor = Color.Lerp(_stockSunColor, color, w);
                    sunIntensity = _stockSunIntensity * Mathf.Lerp(1f, intensity, w);

                    // Where the sun itself is, for the sky and lens flare, even below the horizon.
                    SunDirection = Quaternion.Slerp(_stockSunRotation, Quaternion.Euler(elevation, yaw, 0f), w) * Vector3.back;
                }
                else
                {
                    SunDirection = -(_stockSunRotation * Vector3.forward);
                }

                // Rain: a dimmer, greyer sun through the cloud.
                if (overcast > 0f)
                {
                    float grey = sunColor.grayscale;
                    sunColor = Color.Lerp(sunColor, new Color(grey, grey, grey, sunColor.a), 0.6f * overcast);
                    sunIntensity *= Mathf.Lerp(1f, OvercastSun, overcast);
                }

                _sun.transform.rotation = rotation;
                _sun.color = sunColor;
                _sun.intensity = sunIntensity;
                _sunChanged = true;
            }
            else
            {
                if (_sunChanged) RestoreSun();
                SunDirection = -_sun.transform.forward;
            }

            SunElevation = Mathf.Asin(Mathf.Clamp(SunDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
            SunColor = _sun.color;
        }

        private void RestoreSun()
        {
            if (_sun == null || !_sunChanged) return;
            _sun.transform.rotation = _stockSunRotation;
            _sun.color = _stockSunColor;
            _sun.intensity = _stockSunIntensity;
            _sunChanged = false;
        }

        // ---- Sky ---------------------------------------------------------------------------------

        private void ApplySky()
        {
            Material sky = _skyMaterial;
            if (sky == null) return;

            float blend = Mathf.Clamp01(Current.skyBlend);
            Blend(sky, "_SkyColor", Current.skyColor, blend);
            Blend(sky, "_HorizonColor", Current.horizonColor, blend);
            Blend(sky, "_SunGradientColor", Current.sunGlowColor, blend);

            // Rain: the sky's colours toward a soft grey.
            float overcast = Mathf.Clamp01(Overcast);
            if (overcast > 0f)
            {
                Grey(sky, "_SkyColor", OvercastSkyGrey * overcast);
                Grey(sky, "_HorizonColor", OvercastSkyGrey * overcast);
                Grey(sky, "_SunGradientColor", OvercastSkyGrey * overcast);
            }

            // Night: the sky's own stars and moon, and less of the sun's glow around the moon.
            float night = Mathf.Clamp01(Current.night);
            BlendFloat(sky, "_StarsExposure", 1f, night);
            BlendFloat(sky, "_MoonCycle", Current.moonPhase, night);
            if (sky.HasProperty("_SunScatterIntensity"))
                _sky.SetFloat(sky, "_SunScatterIntensity", _sky.StockFloat(sky, "_SunScatterIntensity") * Mathf.Lerp(1f, 0.2f, night));

            if (sky.HasProperty("_SunDirection") && _sun != null)
            {
                Vector4 stock = _sky.StockVector(sky, "_SunDirection");
                Vector3 toSun = SunDirection;
                _sky.SetVector(sky, "_SunDirection", _sunChanged ? new Vector4(toSun.x, toSun.y, toSun.z, stock.w) : stock);
            }

            HorizonColor = sky.HasProperty("_HorizonColor") ? sky.GetColor("_HorizonColor") : StockFogColor;
        }

        /// <summary>Moves one of the sky's colours (as set this frame) toward grey of the same brightness, a little darker.</summary>
        private void Grey(Material sky, string property, float amount)
        {
            if (!sky.HasProperty(property)) return;
            Color c = sky.GetColor(property);
            float g = c.grayscale * 0.9f;
            Color value = Color.Lerp(c, new Color(g, g, g, c.a), amount);
            _sky.SetColor(sky, property, value);
        }

        private void BlendFloat(Material sky, string property, float target, float blend)
        {
            if (!sky.HasProperty(property)) return;
            _sky.SetFloat(sky, property, Mathf.Lerp(_sky.StockFloat(sky, property), target, blend));
        }

        private void Blend(Material sky, string property, Color target, float blend)
        {
            if (!sky.HasProperty(property)) return;
            Color stock = _sky.StockColor(sky, property);
            Color value = Color.Lerp(stock, target, blend);
            value.a = stock.a;
            _sky.SetColor(sky, property, value);
        }

        // ---- Ambient --------------------------------------------------------------------------------

        /// <summary>How much of a sky colour change reaches the ambient (fill) light.</summary>
        private const float AmbientFromSky = 0.5f;

        /// <summary>
        /// Shifts the hole's own ambient light by the change in sky colour, instead of recomputing it from
        /// the sky: the game's ambient isn't what Unity would compute from its sky (recomputing it washed
        /// every toon surface pale), so with the sky at stock the ambient stays exactly stock.
        /// </summary>
        private void ApplyAmbient()
        {
            // Until LightGreen changes it, follow the engine's own value (it can finish updating after load).
            if (!_ambientChanged) _stockAmbientProbe = RenderSettings.ambientProbe;
            if (_skyMaterial == null) return;

            // A darker sky lowers the fill light only a little; the preset's intensity and tint do the rest.
            Color skyDelta = Floor(Delta(_skyMaterial, "_SkyColor"), -0.2f);
            Color horizonDelta = Floor(Delta(_skyMaterial, "_HorizonColor"), -0.2f);
            float scale = Mathf.Max(0f, Current.ambientIntensity);
            Color tint = Current.ambientTint;

            var key = new Vector4(skyDelta.r + skyDelta.g * 3.1f + skyDelta.b * 7.3f,
                                  horizonDelta.r + horizonDelta.g * 3.1f + horizonDelta.b * 7.3f, scale,
                                  tint.r + tint.g * 3.1f + tint.b * 7.3f);
            bool unchanged = (key - _lastSkyKey).sqrMagnitude < 1e-8f;
            if (unchanged && (!_ambientChanged || Time.unscaledTime < _nextAmbientRefresh)) return;
            _lastSkyKey = key;
            _nextAmbientRefresh = Time.unscaledTime + 2f; // re-assert now and then in case the engine resets it

            bool stock = skyDelta.maxColorComponent < 1e-4f && -MinComponent(skyDelta) < 1e-4f &&
                         horizonDelta.maxColorComponent < 1e-4f && -MinComponent(horizonDelta) < 1e-4f &&
                         Mathf.Approximately(scale, 1f) && tint.r > 0.999f && tint.g > 0.999f && tint.b > 0.999f;
            if (stock)
            {
                if (_ambientChanged) RenderSettings.ambientProbe = _stockAmbientProbe;
                _ambientChanged = false;
                return;
            }

            var shift = new SphericalHarmonicsL2();
            shift.Clear();
            shift.AddAmbientLight(horizonDelta * AmbientFromSky);
            shift.AddDirectionalLight(Vector3.up, (skyDelta - horizonDelta) * AmbientFromSky, 1f);
            // Per colour channel, so a tint (blue at night) colours the fill light.
            SphericalHarmonicsL2 probe = _stockAmbientProbe + shift;
            float r = scale * Mathf.Max(0f, tint.r);
            float g = scale * Mathf.Max(0f, tint.g);
            float b = scale * Mathf.Max(0f, tint.b);
            for (int c = 0; c < 9; c++)
            {
                probe[0, c] *= r;
                probe[1, c] *= g;
                probe[2, c] *= b;
            }
            RenderSettings.ambientProbe = probe;
            _ambientChanged = true;
        }

        private Color Delta(Material sky, string property)
        {
            if (!sky.HasProperty(property)) return Color.clear;
            Color d = sky.GetColor(property) - _sky.StockColor(sky, property);
            d.a = 0f;
            return d;
        }

        private static Color Floor(Color c, float min) =>
            new Color(Mathf.Max(c.r, min), Mathf.Max(c.g, min), Mathf.Max(c.b, min), 0f);

        private static float MinComponent(Color c) => Mathf.Min(c.r, Mathf.Min(c.g, c.b));

        // ---- Restore -----------------------------------------------------------------------------------

        /// <summary>Puts the hole's sun, sky and ambient light back as the game had them.</summary>
        public void Restore()
        {
            if (!_captured) return;
            RestoreSun();
            _sky.Restore();
            // RenderSettings belong to the active scene: only put them back if it's still the same one.
            if (SceneManager.GetActiveScene().handle == _sceneHandle && _ambientChanged)
                RenderSettings.ambientProbe = _stockAmbientProbe;
            _ambientChanged = false;
            _lastSkyKey = Vector4.zero;
            _captured = false;
            _hadSun = false;
            _sun = null;
            _skyMaterial = null;
            _sceneHandle = -1;
        }
    }
}
