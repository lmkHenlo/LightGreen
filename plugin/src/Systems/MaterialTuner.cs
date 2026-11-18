using System.Collections.Generic;
using LightGreen.Core;
using LightGreen.Presets;
using UnityEngine;

namespace LightGreen.Systems
{
    /// <summary>
    /// Reversible changes to the game's own materials, with the game's own shaders: shadow tint on toon materials
    /// (Effects.ShadowTint), sun highlights and time-of-day colour on water (Effects.WaterTuning), and glints on
    /// snow terrain (Effects.IceGlints). Every value is computed from the material's stock value, so applying again
    /// never compounds, and Restore puts every material back as the game had it.
    /// </summary>
    internal sealed class MaterialTuner : IFeature
    {
        private const string ToonShader = "Custom/ToonShader";
        private const string WaterShader = "Water 2.0";

        private readonly EnvironmentController _env;
        private readonly MaterialEdits _edits = new MaterialEdits();
        private readonly List<Material> _toon = new List<Material>();
        private readonly List<Material> _water = new List<Material>();
        private readonly List<Material> _terrain = new List<Material>();
        private readonly HashSet<Material> _known = new HashSet<Material>();
        private float _nextScan;
        private int _scansSinceRescan;
        private bool _announce;
        private bool _forceApply = true;
        private Values _applied;

        internal MaterialTuner(EnvironmentController env) => _env = env;

        public string Name => "Materials";

        /// <summary>Everything the materials depend on; materials are only touched when this changes.</summary>
        private struct Values
        {
            internal Color ShadeTint;
            internal float ShadeStrength;
            internal Color Specular;
            internal Color WaterTint;
            internal Color WaterFresnel;
            internal float WaterBlend;
            internal float Glints;

            internal bool Same(Values o) =>
                Close(ShadeTint, o.ShadeTint) && Mathf.Abs(ShadeStrength - o.ShadeStrength) < 1e-3f &&
                Close(Specular, o.Specular) && Close(WaterTint, o.WaterTint) && Close(WaterFresnel, o.WaterFresnel) &&
                Mathf.Abs(WaterBlend - o.WaterBlend) < 1e-3f && Mathf.Abs(Glints - o.Glints) < 1e-3f;

            private static bool Close(Color a, Color b) =>
                Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a) < 2e-3f;
        }

        /// <summary>
        /// Looks for new materials soon (a hole or cosmetics loaded). With announce, the next scan that finds
        /// materials logs the counts (once per biome change, not on hole changes or the ToggleKey).
        /// </summary>
        internal void Rescan(bool announce = false)
        {
            _nextScan = 0f;
            _scansSinceRescan = 0;
            _announce |= announce;
        }

        public void Apply()
        {
            if (!GameEvents.InHole)
            {
                Restore();
                return;
            }
            if (Time.unscaledTime >= _nextScan) Scan();

            BiomePreset biome = PresetStore.ForBiome(GameEvents.CurrentBiome);
            TimePreset time = _env.Current;
            float tint = Settings.ShadowTint.Value;
            float water = Settings.WaterTuning.Value;
            float glints = Settings.IceGlints.Value * biome.glints * (Vr.IsRunning ? 0.5f : 1f);

            Color sun = _env.SunColor;
            float sunMax = Mathf.Max(0.001f, Mathf.Max(sun.r, Mathf.Max(sun.g, sun.b)));
            Color sunHue = new Color(sun.r / sunMax, sun.g / sunMax, sun.b / sunMax, 1f);

            var v = new Values
            {
                ShadeTint = biome.shadeTint,
                ShadeStrength = Mathf.Clamp01(biome.shadeTintStrength * tint),
                Specular = sunHue * (biome.waterSpecular * water),
                WaterTint = sunHue,
                WaterFresnel = _env.HorizonColor,
                WaterBlend = Mathf.Clamp01(time.waterTint * water),
                Glints = glints,
            };
            if (!_forceApply && v.Same(_applied)) return;
            _forceApply = false;
            _applied = v;

            foreach (Material m in _toon) if (m != null) ApplyToon(m, v);
            foreach (Material m in _water) if (m != null) ApplyWater(m, v);
            foreach (Material m in _terrain) if (m != null) ApplyTerrain(m, v);
        }

        // ---- Finding materials --------------------------------------------------------------------

        private void Scan()
        {
            // Often while a hole loads in (players and cosmetics appear), then rarely: the search walks
            // every material in memory.
            _nextScan = Time.unscaledTime + (_scansSinceRescan++ < 10 ? 3f : 20f);
            _edits.ForgetDestroyed();
            _known.RemoveWhere(m => m == null);
            _toon.RemoveAll(m => m == null);
            _water.RemoveAll(m => m == null);
            _terrain.RemoveAll(m => m == null);

            bool found = false;
            int toon = 0, water = 0;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || m.shader == null || !_known.Add(m)) continue;
                string shader = m.shader.name;
                if (shader == ToonShader)
                {
                    AdoptToon(m);
                    _toon.Add(m);
                    toon++;
                }
                else if (shader == WaterShader)
                {
                    AdoptWater(m);
                    _water.Add(m);
                    water++;
                }
            }

            foreach (Terrain t in Terrain.activeTerrains)
            {
                Material m = t != null ? t.materialTemplate : null;
                if (m == null || _terrain.Contains(m) || !m.HasProperty("_GlitterStrength")) continue;
                if (!_edits.IsTracked(m)) _edits.AdoptFloat(m, "_GlitterStrength");
                _terrain.Add(m);
                found = true;
            }

            if (toon + water > 0)
            {
                found = true;
                if (_announce)
                {
                    _announce = false;
                    // Counts include copies the game keeps loaded from earlier holes, so they grow over a session.
                    Plugin.Log.LogInfo($"Materials tuned: {_toon.Count} toon, {_water.Count} water, {_terrain.Count} terrain.");
                }
            }
            if (found) _forceApply = true;
        }

        /// <summary>
        /// A toon material copied from one LightGreen already edited (cosmetics are instanced at runtime) starts
        /// with LightGreen's values; record the values they replaced as its stock.
        /// </summary>
        private void AdoptToon(Material m)
        {
            if (_edits.IsTracked(m)) return;
            _edits.AdoptColor(m, "_1st_ShadeColor");
            _edits.AdoptColor(m, "_2nd_ShadeColor");
        }

        private void AdoptWater(Material m)
        {
            if (_edits.IsTracked(m)) return;
            _edits.AdoptColor(m, "_SpecularColor");
            _edits.AdoptColor(m, "_Color");
            _edits.AdoptColor(m, "_FresnelColor");
        }

        // ---- Toon: shadow tint --------------------------------------------------------------------

        private void ApplyToon(Material m, Values v)
        {
            Values tint = v;
            if (IsPlayerMaterial(m)) tint.ShadeStrength = 0f;
            TintShade(m, "_1st_ShadeColor", tint);
            TintShade(m, "_2nd_ShadeColor", tint);
        }

        /// <summary>
        /// Players, their cosmetics and held items fade out near the camera (_CAMERAFADE_ON) and aren't cut-out
        /// foliage. They keep their own shade colours, so team and cosmetic colours read the same in every biome.
        /// </summary>
        private bool IsPlayerMaterial(Material m)
        {
            if (!_edits.StockKeyword(m, "_CAMERAFADE_ON")) return false;
            if (!_edits.StockKeyword(m, "_ALPHACUTOFFENABLE_ON")) return true;
            // Cut-out with camera fade is mostly foliage, except the face parts.
            string name = m.name;
            return name.StartsWith("Eyes") || name.StartsWith("Eyebrows") || name.StartsWith("Mouth") || name.StartsWith("Cheeks");
        }

        /// <summary>Moves a shade colour's hue toward the tint while keeping the material's own brightness.</summary>
        private void TintShade(Material m, string property, Values v)
        {
            if (!m.HasProperty(property)) return;
            Color stock = _edits.StockColor(m, property);
            if (v.ShadeStrength <= 0f)
            {
                _edits.SetColor(m, property, stock);
                return;
            }
            float stockLum = Luminance(stock);
            float tintLum = Mathf.Max(0.001f, Luminance(v.ShadeTint));
            Color target = v.ShadeTint * (stockLum / tintLum);
            Color result = Color.Lerp(stock, target, v.ShadeStrength);
            result.a = stock.a;
            _edits.SetColor(m, property, result);
        }

        // ---- Water ---------------------------------------------------------------------------------

        private void ApplyWater(Material m, Values v)
        {
            if (m.HasProperty("_SpecularColor"))
            {
                Color stock = _edits.StockColor(m, "_SpecularColor");
                Color spec = stock + v.Specular;
                spec.a = stock.a;
                _edits.SetColor(m, "_SpecularColor", spec);
            }
            if (m.HasProperty("_Color"))
            {
                Color stock = _edits.StockColor(m, "_Color");
                Color tinted = Color.Lerp(stock, stock * v.WaterTint, v.WaterBlend);
                tinted.a = stock.a;
                _edits.SetColor(m, "_Color", tinted);
            }
            if (m.HasProperty("_FresnelColor"))
            {
                Color stock = _edits.StockColor(m, "_FresnelColor");
                Color fresnel = Color.Lerp(stock, v.WaterFresnel, v.WaterBlend * 0.5f);
                fresnel.a = stock.a;
                _edits.SetColor(m, "_FresnelColor", fresnel);
            }
        }

        // ---- Terrain glints ------------------------------------------------------------------------

        private void ApplyTerrain(Material m, Values v)
        {
            float stock = _edits.StockFloat(m, "_GlitterStrength");
            _edits.SetFloat(m, "_GlitterStrength", v.Glints > 0f ? Mathf.Max(stock, stock * (1f + v.Glints)) : stock);
        }

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>Puts every material back as the game had it.</summary>
        public void Restore()
        {
            if (_edits.Count == 0 && _toon.Count == 0 && _water.Count == 0 && _terrain.Count == 0) return;
            _edits.Restore();
            _toon.Clear();
            _water.Clear();
            _terrain.Clear();
            _known.Clear();
            _applied = default;
            _forceApply = true;
            Rescan();
        }
    }
}
