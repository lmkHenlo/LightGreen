using HTraceAO.Rendering.URP.Infrastructure;
using LightGreen.Core;
using LightGreen.Presets;
using UnityEngine;
using UnityEngine.Rendering;
using AOMode = HTraceAO.Rendering.Shared.Globals.AmbientOcclusionMode;
using AOInjection = HTraceAO.Rendering.Shared.Globals.URPInjectionPoint;
using AOResolution = HTraceAO.Rendering.Shared.Globals.GTAOResolutionMode;
using AODenoising = HTraceAO.Rendering.Shared.Globals.GTAODenoisingMode;

namespace LightGreen.Systems
{
    /// <summary>
    /// Ambient occlusion on every course. The game's renderer already has HTrace AO, driven by volumes, but only
    /// the Snow courses turn it on (screen-space AO, faint). This adds a global volume that turns it on everywhere
    /// as GTAO (horizon-based, at the resolution Quality.AmbientOcclusionQuality picks, denoised over time), laid
    /// over the image after opaque objects are drawn by HTrace's own composition. Off in VR.
    ///
    /// After opaque objects, HTrace rebuilds normals from the depth URP already has, so there is no second draw of
    /// the scene (drawing it before opaque objects made HTrace ask for a depth-and-normals prepass, the whole scene
    /// drawn twice). The price is that it darkens lit and shaded sides alike, so the intensity is kept low.
    ///
    /// HTrace still turns URP's screen-space occlusion keyword on and leaves its AO texture bound after compositing,
    /// so the next frame's Lit and terrain materials would read last frame's AO and darken twice. Before each camera
    /// renders, that texture is set back to white while this is on.
    ///
    /// HTrace works the occlusion out from a depth drawn without the
    /// grass blades (<see cref="GrassFreeDepth"/>), so open grass doesn't speckle.
    /// </summary>
    internal sealed class AmbientOcclusion : IFeature
    {
        /// <summary>Above LightGreen's other volumes and the courses' own (1,000), below underwater (9,000).</summary>
        private const float Priority = 2002f;

        /// <summary>HTrace AO's intensity at strength 1 (its own default is 2; the Snow courses use 0.275 screen-space AO).</summary>
        private const float TunedIntensity = 0.5f;

        /// <summary>How far, in metres, a surface looks for what occludes it.</summary>
        private const float Radius = 1f;

        private static readonly int OcclusionTextureId = Shader.PropertyToID("_ScreenSpaceOcclusionTexture");

        private GameObject _root;
        private UnityEngine.Rendering.Volume _volume;
        private VolumeProfile _profile;
        private HTraceAOVolume _ao;
        private bool _hooked;
        private AOQuality? _loggedQuality;

        public string Name => "Ambient occlusion";

        public void Apply()
        {
            BiomePreset biome = PresetStore.ForBiome(GameEvents.CurrentBiome);
            float s = Settings.AmbientOcclusion.Value * Mathf.Max(0f, biome.ambientOcclusion);
            if (!GameEvents.InHole || s <= 0f || Vr.IsRunning)
            {
                Restore();
                return;
            }

            EnsureVolume();
            _ao.Enable.Override(true);
            _ao.AmbientOcclusionMode.Override(AOMode.GTAO);
            _ao.InjectionPoint.Override(AOInjection.AfterOpaque);
            _ao.ReconstructNormals.Override(true);
            ApplyQuality(Settings.AmbientOcclusionQuality.Value);
            _ao.GTAOWorldSpaceRadius.Override(Radius);
            _ao.GTAODenoising.Override(AODenoising.SpatioTemporal);
            _ao.Intensity.Override(Mathf.Clamp(TunedIntensity * s, 0f, 4f));
            _ao.DirectLightingOcclusion.Override(0f);
            _volume.enabled = true;
            if (Settings.AmbientOcclusionQuality.Value != _loggedQuality)
            {
                // Once, and when the quality changes; not on every ToggleKey press.
                _loggedQuality = Settings.AmbientOcclusionQuality.Value;
                Plugin.Log.LogInfo($"Ambient occlusion: on, {_loggedQuality} quality.");
            }

            Hook(true);
            GrassFreeDepth.ForOcclusion(true);
        }

        /// <summary>
        /// Resolution, slices and steps. The main pass costs about pixels x slices x steps; HTrace's own default is
        /// full resolution, 2 x 16. Half computes every other column, Quarter every other column and row, and the
        /// spatial and temporal denoiser fills in.
        /// </summary>
        private void ApplyQuality(AOQuality quality)
        {
            switch (quality)
            {
                case AOQuality.Low:
                    _ao.GTAOResolution.Override(AOResolution.Quarter);
                    _ao.GTAOSliceCount.Override(2);
                    _ao.GTAOStepCount.Override(12);
                    break;
                case AOQuality.High:
                    _ao.GTAOResolution.Override(AOResolution.Full);
                    _ao.GTAOSliceCount.Override(2);
                    _ao.GTAOStepCount.Override(16);
                    break;
                default:
                    _ao.GTAOResolution.Override(AOResolution.Half);
                    _ao.GTAOSliceCount.Override(2);
                    _ao.GTAOStepCount.Override(16);
                    break;
            }
        }

        /// <summary>Hands ambient occlusion back to the courses' own volumes.</summary>
        public void Restore()
        {
            if (_volume != null) _volume.enabled = false;
            Hook(false);
            GrassFreeDepth.ForOcclusion(false);
        }

        private void Hook(bool on)
        {
            if (on == _hooked) return;
            if (on) RenderPipelineManager.beginCameraRendering += ClearOcclusionTexture;
            else RenderPipelineManager.beginCameraRendering -= ClearOcclusionTexture;
            _hooked = on;
        }

        /// <summary>No occlusion for materials that sample URP's AO texture while lighting (see the class summary).</summary>
        private static void ClearOcclusionTexture(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalTexture(OcclusionTextureId, Texture2D.whiteTexture);
        }

        internal void Destroy()
        {
            Restore();
            GrassFreeDepth.Destroy();
            if (_root != null) Object.Destroy(_root);
            if (_profile != null) Object.Destroy(_profile);
            _root = null;
        }

        private void EnsureVolume()
        {
            if (_root != null) return;
            _root = new GameObject("LightGreen ambient occlusion") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(_root);

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.hideFlags = HideFlags.DontSave;
            _ao = _profile.Add<HTraceAOVolume>();

            _volume = _root.AddComponent<UnityEngine.Rendering.Volume>();
            _volume.isGlobal = true;
            _volume.priority = Priority;
            _volume.weight = 1f;
            _volume.sharedProfile = _profile;
            _volume.enabled = false;
        }
    }
}
