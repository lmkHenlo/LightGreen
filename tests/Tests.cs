using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LightGreen.Core;
using LightGreen.Presets;
using UnityEngine;

namespace LightGreen.Tests
{
    /// <summary>
    /// Offline checks for the parts of LightGreen that run without the game: .cube files, the filter maths, the
    /// JSON files and the built-in presets and filters, the HUD maths, the weather art, LightGreen's random numbers
    /// and the material copy lookup. Compiled with those source files and run by <c>build.ps1 -Test</c>; what needs
    /// the game (rendering, the HUD, lights) is checked by hand (docs/testing.md). Also LightGreen's own file handling
    /// (size limits, writes that replace a file whole) and the preset lookup the systems use every frame.
    /// </summary>
    public static class Tests
    {
        private static int _passed, _failed;

        /// <returns>The number of failed checks.</returns>
        public static int Run()
        {
            _passed = _failed = 0;
            CubeFiles();
            Clamping();
            Curves();
            FilterMaths();
            JsonFiles();
            BuiltInFilters();
            Hud();
            WeatherArt();
            RandomNumbers();
            MaterialOrigins();
            CubeLines();
            JsonLimits();
            Files();
            Pixels();
            PresetIndex();
            Console.WriteLine($"Tests: {_passed} passed, {_failed} failed.");
            return _failed;
        }

        private static void Check(bool ok, string what)
        {
            if (ok) _passed++;
            else
            {
                _failed++;
                Console.WriteLine("FAILED: " + what);
            }
        }

        private static void Refused(string what, string text, params string[] messageHas)
        {
            try
            {
                CubeLut.Parse(text);
                Check(false, what + ": accepted");
            }
            catch (InvalidDataException e)
            {
                Check(messageHas.All(e.Message.Contains), $"{what}: message \"{e.Message}\"");
            }
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-5f;

        private static void CubeFiles()
        {
            CubeLut identity = CubeLut.Parse("LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
            Rgb c = identity.Sample(new Rgb(0.25f, 0.5f, 0.75f));
            Check(Near(c.R, 0.25f) && Near(c.G, 0.5f) && Near(c.B, 0.75f), "3D identity cube samples through " + c);

            CubeLut oneD = CubeLut.Parse("TITLE \"x\"\nLUT_1D_SIZE 2\n0 0 0\n1 1 1\n");
            Check(Near(oneD.Sample(new Rgb(0.3f, 0.3f, 0.3f)).R, 0.3f) && oneD.Title == "x", "1D cube and its title");

            CubeLut vendor = CubeLut.Parse("LUT_IN_VIDEO_RANGE\nLUT_1D_SIZE 2\n0 0 0\n1 1 1\n");
            Check(vendor.Size == 2, "a keyword LightGreen doesn't know is skipped");

            CubeLut outside = CubeLut.Parse("LUT_1D_SIZE 2\n-0.5 0 0\n1.5 1 1\n");
            Check(Near(outside.Sample(new Rgb(0f, 0f, 0f)).R, -0.5f), "values outside 0-1 load (the filter clamps them)");

            Refused("too few entries", "LUT_3D_SIZE 2\n0 0 0\n", "1 entries", "needs 8");
            Refused("no size line", "TITLE \"x\"\n", "no LUT_3D_SIZE");
            Refused("NaN entry", "LUT_1D_SIZE 2\nNaN 0 0\n1 1 1\n", "line 2", "usable number");
            Refused("lower-case nan entry", "LUT_1D_SIZE 2\n0 0 0\nnan 1 1\n", "line 3");
            Refused("Infinity entry", "LUT_1D_SIZE 2\nInfinity 0 0\n1 1 1\n", "line 2");
            Refused("number too big for a float", "LUT_1D_SIZE 2\n1e999 0 0\n1 1 1\n", "line 2");
            Refused("NaN in DOMAIN_MAX", "DOMAIN_MAX NaN 1 1\nLUT_1D_SIZE 2\n0 0 0\n1 1 1\n", "line 1");
        }

        private static void Clamping()
        {
            Check(Rgb.Clamp01(float.NaN) == 0f, "Clamp01(NaN) is 0");
            Check(Rgb.Clamp01(float.PositiveInfinity) == 1f && Rgb.Clamp01(float.NegativeInfinity) == 0f, "Clamp01 of infinities");
            Check(Rgb.Clamp01(0.5f) == 0.5f && Rgb.Clamp01(-0.1f) == 0f && Rgb.Clamp01(1.1f) == 1f, "Clamp01 of ordinary values");
            Rgb clamped = new Rgb(float.NaN, 2f, -1f).Clamped;
            Check(clamped.R == 0f && clamped.G == 1f && clamped.B == 0f, "Rgb.Clamped with a NaN channel");
        }

        private static void Curves()
        {
            Curve curve = Curve.FromPairs(new[] { 0f, 0f, 0.5f, 0.6f, 1f, 1f });
            Check(Near(curve.Evaluate(0.5f), 0.6f), "curve goes through its points");
            float previous = -1f;
            bool rising = true;
            for (int i = 0; i <= 100; i++)
            {
                float v = curve.Evaluate(i / 100f);
                if (v < previous - 1e-6f) rising = false;
                previous = v;
            }
            Check(rising, "curve through rising points never falls");
            Check(Curve.FromPairs(new[] { 0.5f, 0.5f, 0.5f, 0.7f }) == null, "points at the same x give no curve");
        }

        private static void FilterMaths()
        {
            const int n = 32;
            var strip = new Rgb[n * n * n];
            LutBuilder.Fill(new FilterSpec(), n, strip);
            float worst = 0f;
            for (int b = 0; b < n; b++)
                for (int g = 0; g < n; g++)
                    for (int r = 0; r < n; r++)
                    {
                        Rgb p = strip[g * n * n + b * n + r];
                        worst = Math.Max(worst, Math.Abs(p.R - r / (n - 1f)) + Math.Abs(p.G - g / (n - 1f)) + Math.Abs(p.B - b / (n - 1f)));
                    }
            Check(worst < 1e-5f, "a neutral filter gives the identity table (worst error " + worst + ")");
        }

        private static void JsonFiles()
        {
            string presets = Json.Serialize(DefaultPresets.Create());
            PresetFile p = Json.Deserialize<PresetFile>(presets);
            Check(p.version == DefaultPresets.Version && p.biomes.Length == 7 && p.times.Length == 8, "default presets read back");
            Check(Json.Serialize(p) == presets, "default presets write out the same");

            string filters = Json.Serialize(DefaultFilters.Create());
            FilterFile f = Json.Deserialize<FilterFile>(filters);
            Check(f.filters.Length == DefaultFilters.Create().filters.Length && Json.Serialize(f) == filters, "default filters round trip");

            PresetFile lenient = Json.Deserialize<PresetFile>("// a comment\n{ \"Version\": 3, \"biomes\": [ {\"biome\":\"Coast\", \"unknown\": 1,}, ], }");
            Check(lenient.version == 3 && lenient.biomes[0].biome == "Coast", "comments, trailing commas, unknown keys and any key case");
            try
            {
                Json.Deserialize<PresetFile>("{\"version\": 1,5}");
                Check(false, "a comma decimal is refused");
            }
            catch (InvalidDataException e)
            {
                Check(e.Message.Contains("line 1, column 15"), "a comma decimal is refused with its place: " + e.Message);
            }
        }

        private static void BuiltInFilters()
        {
            PhotoFilterPreset[] filters = DefaultFilters.Create().filters;
            Check(filters.Length == 27, "27 built-in filters");
            Check(filters.Select(f => f.name.ToLowerInvariant()).Distinct().Count() == filters.Length, "built-in filter names are all different");
            const int n = 16;
            foreach (PhotoFilterPreset preset in filters)
            {
                var strip = new Rgb[n * n * n];
                LutBuilder.Fill(FilterStore.ToSpec(preset, null), n, strip);
                Check(strip.All(x => x.R >= 0f && x.R <= 1f && x.G >= 0f && x.G <= 1f && x.B >= 0f && x.B <= 1f),
                    $"filter {preset.name} stays within 0-1");
            }
        }

        private static void Hud()
        {
            Check(HudMath.Scale(0) == HudMath.Hidden && HudMath.Scale(100) == 1f && HudMath.Scale(50) == 0.5f, "HUD sizes from percent");
            Check(HudMath.Edge(0f, 0f) == 0f && HudMath.Edge(1f, 1f) == 1f && HudMath.Edge(0.5f, 0.5f) == 0.5f, "HUD edges a piece shrinks toward");
        }

        private static void WeatherArt()
        {
            float[] a = FlakeArt.Atlas(64, 2, 0.03f, 17), b = FlakeArt.Atlas(64, 2, 0.03f, 17);
            Check(a.SequenceEqual(b) && a.All(v => v >= 0f && v <= 1f), "flake art is the same each time and within 0-1");
            float[] disc = RainArt.Disc(32);
            Check(disc.Length == 32 * 32 * 4 && disc.All(v => v >= 0f && v <= 1f), "rain disc within 0-1");
        }

        private static void RandomNumbers()
        {
            bool values = true, ranges = true, circle = true;
            float least = 1f, most = 0f;
            for (int i = 0; i < 20000; i++)
            {
                float v = LocalRandom.Value;
                values &= v >= 0f && v < 1f;
                least = Math.Min(least, v);
                most = Math.Max(most, v);
                float r = LocalRandom.Range(0.07f, 0.14f);
                ranges &= r >= 0.07f && r <= 0.14f;
                circle &= LocalRandom.InsideUnitCircle.sqrMagnitude <= 1f + 1e-5f;
            }
            Check(values && least < 0.01f && most > 0.99f, "LocalRandom.Value covers 0 to 1");
            Check(ranges, "LocalRandom.Range stays in its range");
            Check(circle, "LocalRandom.InsideUnitCircle stays inside the circle");
        }

        private static void MaterialOrigins()
        {
            var origins = new MaterialEdits.Origins<float>();
            origins.Remember(0.8f, 0.5f);
            origins.Remember(0.8f, 0.5f);
            Check(origins.TryGet(0.8f, out float stock) && stock == 0.5f, "a value written over one stock value leads back to it");

            origins.Remember(0.9f, 0.4f);
            origins.Remember(0.9f, 0.6f);
            Check(!origins.TryGet(0.9f, out _), "a value written over two stock values leads to neither");
            origins.Remember(0.9f, 0.4f);
            Check(!origins.TryGet(0.9f, out _), "and stays that way");

            var colors = new MaterialEdits.Origins<Color>();
            colors.Remember(Color.red, Color.white);
            Check(colors.TryGet(Color.red, out Color c) && c == Color.white, "colours too");
        }

        private static void CubeLines()
        {
            CubeLut spaced = CubeLut.Parse("LUT_1D_SIZE 2\n\t0  0\t0 # first\n+1 1.0 .5e1 extra\n");
            Rgb top = spaced.Sample(new Rgb(1f, 1f, 1f));
            Check(Near(top.R, 1f) && Near(top.B, 5f), "colour lines with tabs, extra spaces, a sign, an exponent and extra words");
            Refused("a colour line with two numbers", "LUT_1D_SIZE 2\n0 0\n1 1 1\n", "line 2", "missing or unreadable");
            Refused("a colour line with a word", "LUT_1D_SIZE 2\n0 x 0\n1 1 1\n", "line 2", "missing or unreadable");
            Refused("colour before the size", "0 0 0\nLUT_1D_SIZE 2\n", "line 1", "before LUT_3D_SIZE");
            Refused("too many entries", "LUT_1D_SIZE 2\n0 0 0\n1 1 1\n1 1 1\n", "line 4", "more than the 2");
            using (var reader = new StringReader("LUT_1D_SIZE 2\n0 0 0\n1 1 1\n"))
                Check(CubeLut.Parse(reader).Size == 2, "a .cube read from a reader, as from a file");
        }

        private static void JsonLimits()
        {
            string deep = new string('[', Json.MaxDepth) + new string(']', Json.MaxDepth);
            Check(Json.Parse(deep) is List<object>, $"{Json.MaxDepth} levels of nesting read");
            try
            {
                Json.Parse(new string('[', 100000) + new string(']', 100000));
                Check(false, "deep nesting refused");
            }
            catch (InvalidDataException e)
            {
                Check(e.Message.Contains("nested more than"), "100,000 levels of nesting refused, not a stack overflow: " + e.Message);
            }
        }

        private static void Files()
        {
            string folder = Path.Combine(Path.GetTempPath(), "LightGreen-files-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string path = Path.Combine(folder, "settings.json");
                SafeFile.WriteText(path, "first");
                SafeFile.WriteText(path, "second");
                Check(File.ReadAllText(path) == "second" && !File.Exists(path + ".tmp"), "a file written twice holds the second text and leaves no temp file");
                Check(SafeFile.ReadText(path, 100) == "second", "a small file reads under its limit");
                try
                {
                    SafeFile.ReadText(path, 3);
                    Check(false, "a file over its limit is refused");
                }
                catch (InvalidDataException e)
                {
                    Check(e.Message.Contains("LightGreen reads files up to"), "a file over its limit is refused: " + e.Message);
                }
                var file = new JsonFile(Path.Combine(folder, "filters.json"));
                file.Write(DefaultFilters.Create());
                var read = (FilterFile)Json.FromTree(typeof(FilterFile), file.Read(), "");
                Check(read.filters.Length == DefaultFilters.Create().filters.Length, "a settings file written and read back");
                string backup = file.Backup(3);
                Check(backup.EndsWith(".v3.bak", StringComparison.Ordinal) && File.Exists(backup), "a settings file backed up by version");
                string cube = Path.Combine(folder, "two.cube");
                File.WriteAllText(cube, "LUT_1D_SIZE 2\n0 0 0\n1 1 1\n");
                Check(FilterStore.ReadCube(cube).Size == 2, "a .cube file read as the worker thread reads it");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static void Pixels()
        {
            Check(TextureArt.ToByte(float.NaN) == 0 && TextureArt.ToByte(-1f) == 0 && TextureArt.ToByte(2f) == 255, "pixel bytes of NaN and out-of-range values");
            Check(TextureArt.ToByte(0.5f) == 128 && TextureArt.ToByte(1f) == 255 && TextureArt.ToByte(0f) == 0, "pixel bytes round as before");
            Color32[] pixels = TextureArt.ToPixels(new[] { 1f, 0f, 0.5f, 1f, 0f, 1f, 0f, 0f });
            Check(pixels.Length == 2 && pixels[0].r == 255 && pixels[0].b == 128 && pixels[1].g == 255 && pixels[1].a == 0, "pixels from RGBA floats");
        }

        private static void PresetIndex()
        {
            BiomePreset snow = PresetStore.ForBiome(Biome.Snow);
            Check(snow != null && string.Equals(snow.biome, "Snow", StringComparison.OrdinalIgnoreCase), "the Snow biome's preset");
            Check(ReferenceEquals(snow, PresetStore.ForBiome(Biome.Snow)), "the same preset each time (no new one each frame)");
            TimePreset noon = PresetStore.ForTime(TimeOfDay.Noon);
            Check(noon != null && noon.name == "Noon" && ReferenceEquals(noon, PresetStore.ForTime(TimeOfDay.Noon)), "the Noon preset, the same each time");

            var blend = new TimePreset();
            TimePreset night = PresetStore.ForTime(TimeOfDay.Night);
            TimePreset into = TimePreset.Lerp(noon, night, 0.5f, blend);
            TimePreset fresh = TimePreset.Lerp(noon, night, 0.5f);
            Check(ReferenceEquals(into, blend) && Near(into.night, fresh.night) && Near(into.sunIntensity, fresh.sunIntensity) &&
                  into.name == fresh.name, "a blend written into a reused preset matches a new one");
            TimePreset self = TimePreset.Lerp(blend, night, 1f, blend);
            Check(Near(self.night, night.night) && Near(self.exposure, night.exposure), "a blend into one of its own ends");
        }
    }
}
