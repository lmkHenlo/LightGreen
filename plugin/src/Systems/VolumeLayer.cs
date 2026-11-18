using Game.Rendering.Fog;
using LightGreen.Core;
using LightGreen.Presets;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LightGreen.Systems
{
    /// <summary>
    /// LightGreen's post-processing: two global volumes above the course's own (priority 1,000) and below
    /// the game's underwater and under-fog volume (9,000), so those still win. The main volume carries
    /// colour grading, distance haze, lens flare and the optional lens effects; the camera volume
    /// carries depth of field for the overview flyover and photo mode, faded in and out by weight.
    /// Grading adds to the course's own values instead of replacing them. The lens effects (flare, depth of field,
    /// vignette, chromatic aberration) are off while VR is running.
    /// </summary>
    internal sealed class VolumeLayer : IFeature
    {
        private readonly EnvironmentController _env;

        internal VolumeLayer(EnvironmentController env) => _env = env;

        public string Name => "Post-processing";

        public void Apply()
        {
            if (!GameEvents.InHole)
            {
                Restore();
                return;
            }
            Update(_env, PresetStore.ForBiome(GameEvents.CurrentBiome), Vr.IsRunning);
        }

        public void Restore() => Disable();

        private const float MainPriority = 2000f;
        private const float CameraPriority = 2001f;

        private GameObject _root;
        private UnityEngine.Rendering.Volume _main;
        private UnityEngine.Rendering.Volume _camera;
        private VolumeProfile _mainProfile;
        private VolumeProfile _cameraProfile;

        private ColorAdjustments _color;
        private WhiteBalance _white;
        private ScreenSpaceLensFlare _flare;
        private EnvironmentFogOverride _fog;
        private Vignette _vignette;
        private ChromaticAberration _aberration;
        private Bloom _bloom;
        private DepthOfField _dof;

        /// <summary>Fog in the rain, at full overcast: where it starts and ends, times the dry values.</summary>
        private const float OvercastFogStart = 0.5f;
        private const float OvercastFogEnd = 0.6f;

        /// <summary>Bloom on City holes at night, at full night and CityNight 1: the threshold drops to this (from
        /// the course's own, URP's 0.9 by default) so lit windows and lamps bloom, and the intensity rises to this.</summary>
        private const float NightBloomThreshold = 0.8f;
        private const float NightBloomIntensity = 0.4f;
        private float _courseBloomThreshold = 0.9f, _courseBloomIntensity;

        // The course's own values, which grading adds to.
        private VolumeProfile _courseProfile;
        private float _courseContrast, _courseSaturation, _courseExposure, _courseTemperature, _courseTint, _courseVignette;
        private bool _courseRead;

        private float _dofWeight;
        private float _focusDistance = 20f;

        private void Update(EnvironmentController env, BiomePreset biome, bool vrComfort)
        {
            EnsureVolumes();
            ReadCourseIfChanged();
            _main.enabled = true;
            _camera.enabled = true;

            TimePreset time = env.Current;

            // Colour grading: the course's values, plus the biome's and the time of day's.
            float grading = Settings.ColorGrading.Value;
            _color.contrast.Override(Mathf.Clamp(_courseContrast + Mathf.Clamp(biome.contrast + time.contrast, -20f, 20f) * grading, -100f, 100f));
            _color.saturation.Override(Mathf.Clamp(_courseSaturation + Mathf.Clamp(biome.saturation + time.saturation, -25f, 25f) * grading, -100f, 100f));
            _color.postExposure.Override(_courseExposure + Mathf.Clamp(biome.exposure + time.exposure, -0.5f, 0.5f) * grading);
            _white.temperature.Override(Mathf.Clamp(_courseTemperature + (biome.temperature + time.temperature) * grading, -100f, 100f));
            _white.tint.Override(Mathf.Clamp(_courseTint + (biome.tint + time.tint) * grading, -100f, 100f));

            // Distance haze: the game's own fog, moved where the presets put it and coloured toward the horizon.
            // In the rain (env.Overcast) the fog closes in and greys, whether or not the haze is on.
            float haze = Settings.DistanceHaze.Value;
            float overcast = Mathf.Clamp01(env.Overcast);
            if ((haze > 0f || overcast > 0f) && env.HasScene)
            {
                float start = Mathf.Lerp(env.StockFogStart, env.StockFogStart * biome.fogStart * time.fogDistance, Mathf.Clamp01(haze));
                float end = Mathf.Lerp(env.StockFogEnd, env.StockFogEnd * biome.fogEnd * time.fogDistance, Mathf.Clamp01(haze));
                start *= Mathf.Lerp(1f, OvercastFogStart, overcast);
                end *= Mathf.Lerp(1f, OvercastFogEnd, overcast);
                // Linear fog needs a start at or past the camera and an end past the start.
                start = Mathf.Max(start, 0f);
                end = Mathf.Max(end, start + 1f);
                float colorBlend = Mathf.Clamp01((biome.fogColorBlend + time.fogColorBlend) * 0.5f * haze);
                Color fog = Color.Lerp(env.StockFogColor, env.HorizonColor, colorBlend);
                float grey = fog.grayscale * 0.85f;
                fog = Color.Lerp(fog, new Color(grey, grey, grey, fog.a), 0.7f * overcast);
                _fog.fogColor.Override(fog);
                _fog.fogStart.Override(start);
                _fog.fogEnd.Override(end);
                _fog.active = true;
            }
            else
            {
                _fog.active = false;
            }

            // Lens flare, strongest with a low sun.
            float lowSun = 1f - Mathf.InverseLerp(20f, 60f, env.SunElevation);
            float sunUp = Mathf.Clamp01(env.SunElevation / 3f); // no flare once the sun has set
            float flare = vrComfort ? 0f : Settings.LensFlare.Value * time.lensFlare * Mathf.Lerp(0.3f, 1f, lowSun) * sunUp;
            _flare.intensity.Override(flare);
            _flare.tintColor.Override(Color.Lerp(Color.white, env.SunColor, 0.6f));

            // Optional lens character; off in VR.
            float lens = vrComfort ? 0f : 1f;
            _vignette.intensity.Override(Mathf.Clamp01(_courseVignette + 0.25f * Settings.Vignette.Value * lens));
            _aberration.intensity.Override(Mathf.Clamp01(0.25f * Settings.ChromaticAberration.Value * lens));

            // City at night: lit windows and lamps bloom. Elsewhere, and by day, the course's own bloom.
            float nightBloom = GameEvents.CurrentBiome == Biome.City
                ? Mathf.Clamp01(env.Darkness * Settings.CityNight.Value) : 0f;
            if (nightBloom > 0.01f)
            {
                _bloom.threshold.Override(Mathf.Lerp(_courseBloomThreshold, Mathf.Min(_courseBloomThreshold, NightBloomThreshold), nightBloom));
                _bloom.intensity.Override(Mathf.Max(_courseBloomIntensity, NightBloomIntensity * nightBloom));
            }
            else
            {
                _bloom.threshold.overrideState = false;
                _bloom.intensity.overrideState = false;
            }

            UpdateDepthOfField(vrComfort);
        }

        // ---- Depth of field: overview flyover and photo mode --------------------------------------

        private void UpdateDepthOfField(bool vrComfort)
        {
            float target = 0f;
            float focalLength = 50f, aperture = 5.6f;
            bool photo = false, overview = false;
            try
            {
                photo = PhotoCameraModule.Active;
                overview = CameraModuleController.CurrentModuleType == CameraModuleType.Overview;
            }
            catch { /* the camera system isn't up yet */ }

            if (!vrComfort && photo && Settings.PhotoDepthOfField.Value > 0f)
            {
                target = 1f;
                focalLength = 100f;
                aperture = Mathf.Clamp(4f / Settings.PhotoDepthOfField.Value, 1f, 32f);
            }
            else if (!vrComfort && overview && Settings.TiltShift.Value > 0f)
            {
                target = 1f;
                focalLength = 300f;
                aperture = Mathf.Clamp(2f / Settings.TiltShift.Value, 1f, 32f);
            }

            Camera cam = GameManager.Camera;
            if (target > 0f && cam != null)
            {
                Transform t = cam.transform;
                float wanted = Physics.Raycast(t.position, t.forward, out RaycastHit hit, 1500f, ~0, QueryTriggerInteraction.Ignore)
                    ? hit.distance : 200f;
                _focusDistance = Mathf.Lerp(_focusDistance, wanted, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
                _dof.focusDistance.Override(Mathf.Max(0.1f, _focusDistance));
                _dof.focalLength.Override(focalLength);
                _dof.aperture.Override(aperture);
            }

            // Fade so switching camera mode doesn't pop.
            _dofWeight = Mathf.MoveTowards(_dofWeight, target, Time.unscaledDeltaTime / 0.6f);
            _camera.weight = _dofWeight;
            _dof.active = _dofWeight > 0f;
        }

        // ---- Setup ----------------------------------------------------------------------------------

        private void EnsureVolumes()
        {
            if (_root != null) return;
            _root = new GameObject("LightGreen volumes") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(_root);

            _mainProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _mainProfile.hideFlags = HideFlags.DontSave;
            _main = _root.AddComponent<UnityEngine.Rendering.Volume>();
            _main.isGlobal = true;
            _main.priority = MainPriority;
            _main.weight = 1f;
            _main.sharedProfile = _mainProfile;

            _color = _mainProfile.Add<ColorAdjustments>();
            _white = _mainProfile.Add<WhiteBalance>();
            _flare = _mainProfile.Add<ScreenSpaceLensFlare>();
            _fog = _mainProfile.Add<EnvironmentFogOverride>();
            _vignette = _mainProfile.Add<Vignette>();
            _aberration = _mainProfile.Add<ChromaticAberration>();
            _bloom = _mainProfile.Add<Bloom>();

            _cameraProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _cameraProfile.hideFlags = HideFlags.DontSave;
            _camera = _root.AddComponent<UnityEngine.Rendering.Volume>();
            _camera.isGlobal = true;
            _camera.priority = CameraPriority;
            _camera.weight = 0f;
            _camera.sharedProfile = _cameraProfile;
            _dof = _cameraProfile.Add<DepthOfField>();
            _dof.mode.Override(DepthOfFieldMode.Bokeh);
            _courseRead = false;
        }

        /// <summary>The course's own grading, so LightGreen's adds to it. The driving range has none.</summary>
        private void ReadCourseIfChanged()
        {
            VolumeProfile profile = null;
            try
            {
                int index = CourseManager.CurrentHoleGlobalIndex;
                var holes = GameManager.AllCourses.allHoles;
                if (index >= 0 && index < holes.Count && holes[index] != null && holes[index].ParentCourse != null)
                    profile = holes[index].ParentCourse.VolumeProfile;
                if (LightGreen.Core.GameEvents.CurrentBiome == LightGreen.Core.Biome.DrivingRange) profile = null;
            }
            catch { profile = null; }

            if (_courseRead && profile == _courseProfile) return;
            _courseProfile = profile;
            _courseRead = true;

            _courseContrast = _courseSaturation = _courseExposure = _courseTemperature = _courseTint = _courseVignette = 0f;
            _courseBloomThreshold = 0.9f;   // URP's default
            _courseBloomIntensity = 0f;
            if (profile == null) return;
            if (profile.TryGet(out Bloom bloom) && bloom.active)
            {
                if (bloom.threshold.overrideState) _courseBloomThreshold = bloom.threshold.value;
                if (bloom.intensity.overrideState) _courseBloomIntensity = bloom.intensity.value;
            }
            if (profile.TryGet(out ColorAdjustments c) && c.active)
            {
                if (c.contrast.overrideState) _courseContrast = c.contrast.value;
                if (c.saturation.overrideState) _courseSaturation = c.saturation.value;
                if (c.postExposure.overrideState) _courseExposure = c.postExposure.value;
            }
            if (profile.TryGet(out WhiteBalance w) && w.active)
            {
                if (w.temperature.overrideState) _courseTemperature = w.temperature.value;
                if (w.tint.overrideState) _courseTint = w.tint.value;
            }
            if (profile.TryGet(out Vignette v) && v.active && v.intensity.overrideState) _courseVignette = v.intensity.value;
        }

        /// <summary>Hands the picture back to the game's own volumes.</summary>
        internal void Disable()
        {
            if (_main != null) _main.enabled = false;
            if (_camera != null) _camera.enabled = false;
            _dofWeight = 0f;
        }

        internal void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            if (_mainProfile != null) Object.Destroy(_mainProfile);
            if (_cameraProfile != null) Object.Destroy(_cameraProfile);
            _root = null;
        }
    }
}
