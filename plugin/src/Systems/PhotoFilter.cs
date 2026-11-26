using System;
using System.Collections.Generic;
using LightGreen.Core;
using LightGreen.Presets;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LightGreen.Systems
{
    /// <summary>
    /// The photo filter (Filters.Filter, cycled with Keys.FilterKey): its colours as a lookup table for URP's Color
    /// Lookup, which applies last, after tonemapping, so a filter sits on top of LightGreen's look like a photo
    /// editor's. Its lens extras (vignette, distortion, fringes, bloom) are mixed into LightGreen's own by
    /// <see cref="VolumeLayer"/>. The HUD is never filtered. No film grain.
    /// </summary>
    internal sealed class PhotoFilter : IFeature
    {
        public string Name => "Photo filter";

        /// <summary>The active filter, or null for None (or a name that isn't there).</summary>
        internal PhotoFilterPreset Active { get; private set; }
        /// <summary>Filters.Strength, 0 to 1.</summary>
        internal float Strength { get; private set; }
        /// <summary>The active filter's colour table, sized for the current pipeline; null when there's none.</summary>
        internal Texture2D Lut => Active != null && _lut != null && _lutSize == LutSize() ? _lut : null;

        private Texture2D _lut;
        private int _lutSize;
        private string _builtName;
        private int _builtRevision = -1;
        private Rgb[] _strip;
        private Color32[] _pixels;

        public void Apply()
        {
            string wanted = Settings.Filter.Value;
            FilterEntry entry = FilterStore.Find(wanted);
            if (entry == null && !string.IsNullOrWhiteSpace(wanted) &&
                !string.Equals(wanted.Trim(), FilterStore.NoFilter, StringComparison.OrdinalIgnoreCase))
                Plugin.LogChange("filter:missing", $"Photo filter \"{wanted}\" isn't in {System.IO.Path.GetFileName(FilterStore.FilePath)} " +
                                                   $"or the {System.IO.Path.GetFileName(FilterStore.FolderPath)} folder; showing none.");

            Strength = Mathf.Clamp01(Settings.FilterStrength.Value);
            if (entry == null)
            {
                Active = null;
                return;
            }

            int size = LutSize();
            if (_lut != null && entry.Name == _builtName && FilterStore.Revision == _builtRevision && size == _lutSize)
            {
                Active = entry.Preset;
                return;
            }
            // A .cube still being read: the filter shown before (or none) stays until it's ready, a frame or so later.
            if (!entry.TryGetSpec(out FilterSpec spec)) return;
            Active = entry.Preset;
            Build(entry, spec, size);
        }

        /// <summary>Nothing to put back: the volume layer hands the picture back to the game.</summary>
        public void Restore()
        {
        }

        /// <summary>The next (or previous) filter, None included, for Keys.FilterKey.</summary>
        internal static string Step(string current, int direction)
        {
            var names = new List<string> { FilterStore.NoFilter };
            names.AddRange(FilterStore.Names);
            int at = names.FindIndex(n => string.Equals(n, (current ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (at < 0) at = 0;
            int next = ((at + direction) % names.Count + names.Count) % names.Count;
            return names[next];
        }

        private void Build(FilterEntry entry, FilterSpec spec, int size)
        {
            if (_lut == null || _lutSize != size)
            {
                if (_lut != null) UnityEngine.Object.Destroy(_lut);
                // URP's Color Lookup wants a strip size² wide and size high, in a linear (not sRGB) format.
                _lut = new Texture2D(size * size, size, TextureFormat.RGBA32, false, true)
                {
                    name = "LightGreen photo filter",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 0,
                    hideFlags = HideFlags.DontSave,
                };
                _lutSize = size;
                _strip = new Rgb[size * size * size];
                _pixels = new Color32[_strip.Length];
            }

            LutBuilder.Fill(spec, size, _strip);
            for (int i = 0; i < _strip.Length; i++)
            {
                Rgb c = _strip[i];
                _pixels[i] = new Color32(TextureArt.ToByte(c.R), TextureArt.ToByte(c.G), TextureArt.ToByte(c.B), 255);
            }
            _lut.SetPixels32(_pixels);
            _lut.Apply(false, false);

            _builtName = entry.Name;
            _builtRevision = FilterStore.Revision;
            Plugin.LogChange("filter", $"Photo filter: {entry.Name} ({size}³ colour table).");
        }


        /// <summary>The colour table size the current pipeline grades with (its Grading LUT size, 32 by default).</summary>
        private static int LutSize()
        {
            try
            {
                UniversalRenderPipelineAsset asset = UniversalRenderPipeline.asset;
                if (asset != null && asset.colorGradingLutSize >= 16) return asset.colorGradingLutSize;
            }
            catch (Exception e)
            {
                CoreLog.FailedOnce("filter:lutsize", "Photo filter: couldn't read the pipeline's colour table size, so 32 is used", e);
            }
            return 32;
        }

        internal void Destroy()
        {
            if (_lut != null) UnityEngine.Object.Destroy(_lut);
            _lut = null;
            _lutSize = 0;
        }
    }
}
