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

        private readonly SkyLook _skyLook = new SkyLook();

        // The hole's own values, captured when it loads.
        private int _sceneHandle = -1;
        private Light _sun;
        private Material _skyMaterial;
        private Quaternion _stockSunRotation;
        private Color _stockSunColor;
        private float _stockSunIntensity;
        private bool _captured;
        private bool _hadSun;
        private float _nextSunSearch;
        private bool _sunChanged;

        // Time-of-day blending.
        private TimeOfDay _target = (TimeOfDay)(-1);
        private TimePreset _from;
        private TimePreset _to;
        private float _blend = 1f;
        private float _dayPhase;

        // Ambient refresh.

        // Day and night brightness (Brightness.Day, Brightness.Night).
        private float _sunScaleApplied = 1f;
        // The sun as the time of day sets it, before the weather dims it (for Darkness), while LightGreen sets the sun.
        private Color _clearSunColor = Color.white;
        private float _clearSunIntensity = 1f;

        internal TimePreset Current { get; private set; } = new TimePreset();
        // The blend shown during a change of time or a day cycle: written in place each frame, so it makes no garbage.
        private readonly TimePreset _blended = new TimePreset();
        /// <summary>The scene's sun, once found.</summary>
        internal Light Sun => _sun;
        /// <summary>True while LightGreen has the sun moved or recoloured (false: as the game has it).</summary>
        internal bool SunMoved => _sunChanged;
        internal Color SunColor { get; private set; } = Color.white;
        internal float SunElevation { get; private set; } = 45f;
        /// <summary>Direction to the sun itself (below the horizon at night, when the scene light is the moon).</summary>
        internal Vector3 SunDirection { get; private set; } = Vector3.up;
        /// <summary>The sky's horizon colour as shown this frame (the hole's fog colour when there's no sky).</summary>
        internal Color HorizonColor => _skyLook.HorizonColor;
        internal Color StockFogColor { get; private set; } = Color.grey;
        internal float StockFogStart { get; private set; }
        internal float StockFogEnd { get; private set; } = 600f;
        internal bool HasScene => _captured;

        /// <summary>
        /// Brightness.Day or Brightness.Night, blended by how dark the scene is: what the scene's brightness is
        /// multiplied by this frame (1 = as the time of day has it).
        /// </summary>
        internal float Brightness { get; private set; } = 1f;
        /// <summary>The part of <see cref="Brightness"/> done with light: the sun or moon, the fill light, the sky and fog colours.</summary>
        internal float LightScale { get; private set; } = 1f;
        /// <summary>The rest, in stops of exposure, for brightening past what lighting can show (0 most of the time).</summary>
        internal float ExposureBoost { get; private set; }

        /// <summary>
        /// Brightening done with light, at most. Above this the rest is exposure. Toon surfaces stop getting
        /// brighter once the light is bright enough, so past here more light would show as nothing.
        /// </summary>
        private const float MaxLightBrightening = 1.5f;

        /// <summary>
        /// 0 for the time of day's own sky, up to 1 for a fully overcast, rainy sky: the sun (or moon) dims and
        /// greys, and the sky and fog turn grey. Set by <see cref="WeatherSky"/> each frame; eased there.
        /// </summary>
        internal float Overcast { get; private set; }

        /// <summary>
        /// How much of the overcast is snow rather than rain, 0 to 1: snow's sky is bright and pale blue, not grey, and
        /// its sun dims less. Set and eased with <see cref="Overcast"/>.
        /// </summary>
        internal float Snowy { get; private set; }

        /// <summary>
        /// How much of the overcast is cartoon rain's soft blue mist, 0 to 1: the colours stay bright, the sky and fog go
        /// a misty blue, the sun dims a little. Snowy + Misty is at most 1; the rest is rain's grey. Set and eased with
        /// <see cref="Overcast"/>.
        /// </summary>
        internal float Misty { get; private set; }

        /// <summary>The weather's share of the sky this frame (see <see cref="WeatherSky"/>, the only one that sets it).</summary>
        internal void SetWeatherSky(float overcast, float snowy, float misty)
        {
            Overcast = overcast;
            Snowy = snowy;
            Misty = misty;
        }

        /// <summary>The sun's brightness under a full overcast (the sky's own colours: <see cref="SkyLook"/>).</summary>
        private const float OvercastSun = 0.45f;

        /// <summary>Under a full snow sky: the sun's brightness and its pale cold tint.</summary>
        private const float SnowSun = 0.85f;
        private static readonly Color SnowSunTint = new Color(0.9f, 0.95f, 1f);

        /// <summary>Under full cartoon-rain mist: the sun's brightness.</summary>
        private const float MistSun = 0.7f;

        /// <summary>
        /// 0 in daylight, 1 at night: the time of day's own night setting, or, for holes the game itself sets
        /// at night (the City's Causeway and Sky Lane, a Snow hole), how dim the scene's main light is. The
        /// weather doesn't count: rain or snow by day isn't night. Ball glow and the City at night follow it.
        /// </summary>
        internal float Darkness
        {
            get
            {
                // The sun as the time of day has it, before the weather and before Brightness.Day and Night:
                // making the day darker mustn't switch night things on, nor a brighter night switch them off.
                bool lit = _sun != null && _sun.isActiveAndEnabled;
                if (!lit) return DarknessOf(Color.black, 0f, false);
                return _sunChanged
                    ? DarknessOf(_clearSunColor, _clearSunIntensity, true)
                    : DarknessOf(_sun.color, _sun.intensity, true);
            }
        }

        /// <summary>
        /// How dark the lit scene is, 0 to 1: <see cref="Darkness"/>, plus the sun dimmed by rain, snow or cartoon
        /// rain's mist. Brightness.Day and Night blend by it, and the weather's own colours dim with it.
        /// </summary>
        internal float SceneDarkness
        {
            get
            {
                bool lit = _sun != null && _sun.isActiveAndEnabled;
                return DarknessOf(lit ? _sun.color : Color.black, lit ? _sun.intensity / _sunScaleApplied : 0f, lit);
            }
        }

        private float DarknessOf(Color sunColor, float sunIntensity, bool hasSun)
        {
            float night = Mathf.Clamp01(Current.night);
            float dimSun = 1f;
            if (hasSun)
            {
                Color c = sunColor * sunIntensity;
                float brightness = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                dimSun = Mathf.Clamp01((0.55f - brightness) / 0.35f);
            }
            return Mathf.Max(night, dimSun);
        }

        /// <summary>Works out <see cref="Brightness"/> from how dark the scene is, and splits it into light and exposure.</summary>
        private void SetBrightness(float darkness)
        {
            float day = Mathf.Max(0f, Settings.DayBrightness.Value);
            float night = Mathf.Max(0f, Settings.NightBrightness.Value);
            Brightness = Mathf.Lerp(day, night, Mathf.Clamp01(darkness));
            LightScale = Mathf.Min(Brightness, MaxLightBrightening);
            ExposureBoost = Brightness > LightScale && LightScale > 0f ? Mathf.Log(Brightness / LightScale, 2f) : 0f;
        }

        internal static bool Unscaled(float scale) => Mathf.Abs(scale - 1f) < 1e-3f;

        internal void Update()
        {
            CaptureIfNewScene();
            if (!_captured) return;
            UpdateTime();
            ApplySun();
            _skyLook.Apply(this);
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
            StockFogColor = RenderSettings.fogColor;
            StockFogStart = RenderSettings.fogStartDistance;
            StockFogEnd = RenderSettings.fogEndDistance;
            _skyLook.Capture(sky, StockFogColor);
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
                Current = TimePreset.Lerp(PresetStore.ForTime(DayCycle[i]), PresetStore.ForTime(DayCycle[(i + 1) % DayCycle.Length]), t, _blended);
                _target = (TimeOfDay)(-1);
                return;
            }

            TimeOfDay wanted = Settings.Time.Value;
            if (wanted != _target)
            {
                bool first = _target == (TimeOfDay)(-1) && _from == null;
                // From what's showing: a copy when that's the blend, which the next frames write over.
                _from = first ? PresetStore.ForTime(wanted) : Current == _blended ? TimePreset.Lerp(Current, Current, 0f) : Current;
                _to = PresetStore.ForTime(wanted);
                _target = wanted;
                _blend = first ? 1f : 0f;
            }

            float seconds = Settings.TransitionSeconds.Value;
            _blend = seconds <= 0f ? 1f : Mathf.Min(1f, _blend + Time.deltaTime / seconds);
            Current = _blend >= 1f ? _to : TimePreset.Lerp(_from, _to, Mathf.SmoothStep(0f, 1f, _blend), _blended);
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
                SetBrightness(Mathf.Clamp01(Current.night));   // no sun (yet): the time of day's own night setting
                return;
            }

            float w = Mathf.Clamp01(Current.sunWeight);
            float overcast = Mathf.Clamp01(Overcast);
            float yaw = _stockSunRotation.eulerAngles.y;
            // How bright the scene is before Brightness.Day and Night, from the sun the time of day gives.
            SetBrightness(DarknessOf(_stockSunColor, _stockSunIntensity, true));
            if (w > 0f || overcast > 0.001f || !Unscaled(LightScale))
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

                _clearSunColor = sunColor;
                _clearSunIntensity = sunIntensity;

                // Rain: a dimmer, greyer sun through the cloud. Snow: a little dimmer and cold white. Cartoon rain:
                // a little dimmer, its colour kept.
                if (overcast > 0f)
                {
                    float snowy = Mathf.Clamp01(Snowy);
                    float misty = Mathf.Clamp01(Misty);
                    float rainy = 1f - Mathf.Clamp01(snowy + misty);
                    float grey = sunColor.grayscale;
                    sunColor = Color.Lerp(sunColor, new Color(grey, grey, grey, sunColor.a), 0.6f * overcast * rainy);
                    Color cold = new Color(SnowSunTint.r * grey, SnowSunTint.g * grey, SnowSunTint.b * grey, sunColor.a) / Mathf.Max(SnowSunTint.grayscale, 1e-3f);
                    sunColor = Color.Lerp(sunColor, cold, 0.5f * overcast * snowy);
                    float dimmed = OvercastSun * rainy + SnowSun * snowy + MistSun * misty;
                    sunIntensity *= Mathf.Lerp(1f, dimmed, overcast);
                }

                // Day and night brightness, from how dark this sun leaves the scene.
                SetBrightness(DarknessOf(sunColor, sunIntensity, true));

                _sun.transform.rotation = rotation;
                _sun.color = sunColor;
                _sun.intensity = sunIntensity * LightScale;
                _sunScaleApplied = LightScale > 0f ? LightScale : 1f;
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
            _sunScaleApplied = 1f;
            _sunChanged = false;
        }

        // ---- Restore -----------------------------------------------------------------------------------

        /// <summary>Puts the hole's sun, sky and ambient light back as the game had them.</summary>
        public void Restore()
        {
            Brightness = LightScale = 1f;
            ExposureBoost = 0f;
            _sunScaleApplied = 1f;
            if (!_captured) return;
            RestoreSun();
            // RenderSettings belong to the active scene: only put them back if it's still the same one.
            _skyLook.Restore(SceneManager.GetActiveScene().handle == _sceneHandle);
            _captured = false;
            _hadSun = false;
            _sun = null;
            _skyMaterial = null;
            _sceneHandle = -1;
        }
    }
}
