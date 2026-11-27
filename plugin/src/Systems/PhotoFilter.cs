using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    /// <para>The table is worked out on a worker thread (<see cref="LutBuilder.Fill"/> is plain arithmetic), and only
    /// copied into its texture on the game's thread: done in the frame of the pick it measured 6.7-7.6 ms. Until it's
    /// ready, a frame or two later, the filter before stays on screen, its lens extras with it. One table is worked
    /// out at a time; stepping past filters quickly skips the ones passed.</para>
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

        /// <summary>A table being worked out: for which filter, file revision and size. Its buffers are
        /// <see cref="_strip"/> and <see cref="_pixels"/>, which nothing else touches until it's done.</summary>
        private sealed class Job
        {
            internal string Name;
            internal int Revision;
            internal int Size;
            internal Task Task;
        }

        private Job _job;
        private string _failedName;
        private int _failedRevision = -1;

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
            if (entry.Name == _failedName && FilterStore.Revision == _failedRevision)
            {
                Active = null;
                return;
            }

            // A table still being worked out: the filter shown before (or none) stays until it's done.
            if (_job != null)
            {
                if (!_job.Task.IsCompleted) return;
                Job done = _job;
                _job = null;
                bool wantedNow = done.Name == entry.Name && done.Revision == FilterStore.Revision && done.Size == size;
                if (done.Task.IsFaulted)
                {
                    Plugin.Log.LogError($"Photo filter \"{done.Name}\": its colour table couldn't be worked out " +
                                        $"({done.Task.Exception?.GetBaseException()}); showing none.");
                    _failedName = done.Name;
                    _failedRevision = done.Revision;
                    if (wantedNow) Active = null;
                    return;
                }
                if (wantedNow)
                {
                    Upload(done);
                    Active = entry.Preset;
                    return;
                }
                // Done for a filter stepped past: start the one wanted now.
            }

            // A .cube still being read: the filter shown before (or none) stays until it's ready, a frame or so later.
            if (!entry.TryGetSpec(out FilterSpec spec)) return;
            Start(entry, spec, size);
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

        /// <summary>Starts working out a filter's table on a worker thread (only when no other one is running).</summary>
        private void Start(FilterEntry entry, FilterSpec spec, int size)
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

            Rgb[] strip = _strip;
            Color32[] pixels = _pixels;
            _job = new Job
            {
                Name = entry.Name,
                Revision = FilterStore.Revision,
                Size = size,
                Task = Task.Run(() => Work(spec, size, strip, pixels)),
            };
        }

        /// <summary>The table's colours as pixels: arithmetic only, on a worker thread.</summary>
        internal static void Work(FilterSpec spec, int size, Rgb[] strip, Color32[] pixels)
        {
            LutBuilder.Fill(spec, size, strip);
            for (int i = 0; i < strip.Length; i++)
            {
                Rgb c = strip[i];
                pixels[i] = new Color32(TextureArt.ToByte(c.R), TextureArt.ToByte(c.G), TextureArt.ToByte(c.B), 255);
            }
        }

        /// <summary>A finished table into the texture URP's Color Lookup reads, on the game's thread.</summary>
        private void Upload(Job job)
        {
            _lut.SetPixels32(_pixels);
            _lut.Apply(false, false);
            _builtName = job.Name;
            _builtRevision = job.Revision;
            Plugin.LogChange("filter", $"Photo filter: {job.Name} ({job.Size}³ colour table).");
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
