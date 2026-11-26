using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightGreen.Core
{
    /// <summary>
    /// Material changes that can be undone. The first time a keyword or property of a material is read
    /// or set through this class, its stock value is remembered; Restore puts every one back. Values
    /// are always computed from the stock ones, so applying again never compounds.
    /// </summary>
    internal sealed class MaterialEdits
    {
        private sealed class Stock
        {
            internal readonly Dictionary<string, bool> Keywords = new Dictionary<string, bool>();
            internal readonly Dictionary<string, float> Floats = new Dictionary<string, float>();
            internal readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>();
            internal readonly Dictionary<string, Vector4> Vectors = new Dictionary<string, Vector4>();
        }

        private readonly Dictionary<Material, Stock> _stock = new Dictionary<Material, Stock>();

        // The game copies materials at runtime (cosmetics are instanced from their asset), and a copy of an edited
        // material starts out with LightGreen's values; these let the copy be recognised and given its real stock
        // values. First by name: a copy is called "<original> (Instance)", and the original's own stock value is
        // exact. Failing that, by value: which stock value each value LightGreen wrote replaced.
        private readonly Dictionary<string, Material> _byName = new Dictionary<string, Material>();
        // Kept per property: the shade colours take a different value on every material, and sharing one
        // capped table with them made the rim's few values fall out of it, so copies of rimmed materials
        // weren't recognised and kept the rim after Restore.
        private readonly Dictionary<string, Origins<Color>> _colorOrigins = new Dictionary<string, Origins<Color>>();
        private readonly Dictionary<string, Origins<Vector4>> _vectorOrigins = new Dictionary<string, Origins<Vector4>>();
        private readonly Dictionary<string, Origins<float>> _floatOrigins = new Dictionary<string, Origins<float>>();

        /// <summary>
        /// The stock value behind each value LightGreen wrote to one property. A value written over two different
        /// stock values says nothing about which one a copy had, so it's marked and never used.
        /// </summary>
        internal sealed class Origins<T>
        {
            private readonly Dictionary<T, T> _stock = new Dictionary<T, T>();
            private readonly HashSet<T> _ambiguous = new HashSet<T>();

            // Values change every frame during a time-of-day blend; keep the lookup from growing without end.
            internal void Remember(T value, T stock)
            {
                if (_stock.Count > 4096)
                {
                    _stock.Clear();
                    _ambiguous.Clear();
                }
                if (_ambiguous.Contains(value)) return;
                if (_stock.TryGetValue(value, out T known) && !EqualityComparer<T>.Default.Equals(known, stock))
                {
                    _stock.Remove(value);
                    _ambiguous.Add(value);
                    return;
                }
                _stock[value] = stock;
            }

            internal bool TryGet(T value, out T stock) => _stock.TryGetValue(value, out stock);
        }

        internal int Count => _stock.Count;

        private Stock For(Material m)
        {
            if (!_stock.TryGetValue(m, out Stock s))
            {
                _stock[m] = s = new Stock();
                string name = m.name;
                if (!name.EndsWith(InstanceSuffix) && (!_byName.TryGetValue(name, out Material known) || known == null))
                    _byName[name] = m;
            }
            return s;
        }

        private const string InstanceSuffix = " (Instance)";

        /// <summary>The tracked material a copy was made from, by its name, if there is one.</summary>
        private Material OriginalMaterialOf(Material copy)
        {
            string name = copy.name;
            if (!name.EndsWith(InstanceSuffix)) return null;
            while (name.EndsWith(InstanceSuffix)) name = name.Substring(0, name.Length - InstanceSuffix.Length);
            if (!_byName.TryGetValue(name, out Material original) || original == null || original == copy) return null;
            return _stock.ContainsKey(original) ? original : null;
        }

        internal bool StockKeyword(Material m, string keyword)
        {
            Stock s = For(m);
            if (!s.Keywords.TryGetValue(keyword, out bool on)) s.Keywords[keyword] = on = m.IsKeywordEnabled(keyword);
            return on;
        }

        /// <summary>
        /// True if the material's shader declares the property as an integer (Int), such as the toon
        /// shader's _FresnelEnable and _SnowEnable toggles. Unity warns when those are read or written as
        /// floats, so they go through GetInteger and SetInteger.
        /// </summary>
        private static bool IsInteger(Material m, string property)
        {
            Shader shader = m.shader;
            if (shader == null) return false;
            int index = shader.FindPropertyIndex(property);
            return index >= 0 && shader.GetPropertyType(index) == ShaderPropertyType.Int;
        }

        private static float ReadFloat(Material m, string property) =>
            IsInteger(m, property) ? m.GetInteger(property) : m.GetFloat(property);

        private static void WriteFloat(Material m, string property, float value)
        {
            if (IsInteger(m, property)) m.SetInteger(property, Mathf.RoundToInt(value));
            else m.SetFloat(property, value);
        }

        internal float StockFloat(Material m, string property)
        {
            Stock s = For(m);
            if (!s.Floats.TryGetValue(property, out float v)) s.Floats[property] = v = ReadFloat(m, property);
            return v;
        }

        internal Color StockColor(Material m, string property)
        {
            Stock s = For(m);
            if (!s.Colors.TryGetValue(property, out Color v)) s.Colors[property] = v = m.GetColor(property);
            return v;
        }

        internal Vector4 StockVector(Material m, string property)
        {
            Stock s = For(m);
            if (!s.Vectors.TryGetValue(property, out Vector4 v)) s.Vectors[property] = v = m.GetVector(property);
            return v;
        }

        internal void SetKeyword(Material m, string keyword, bool on)
        {
            StockKeyword(m, keyword);
            if (m.IsKeywordEnabled(keyword) == on) return;
            if (on) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }

        internal void SetFloat(Material m, string property, float value)
        {
            if (!m.HasProperty(property)) return;
            float stock = StockFloat(m, property);
            if (value != stock) Remember(_floatOrigins, property, value, stock);
            WriteFloat(m, property, value);
        }

        internal void SetColor(Material m, string property, Color value)
        {
            if (!m.HasProperty(property)) return;
            Color stock = StockColor(m, property);
            if (value != stock) Remember(_colorOrigins, property, value, stock);
            m.SetColor(property, value);
        }

        internal void SetVector(Material m, string property, Vector4 value)
        {
            if (!m.HasProperty(property)) return;
            Vector4 stock = StockVector(m, property);
            if (value != stock) Remember(_vectorOrigins, property, value, stock);
            m.SetVector(property, value);
        }

        private static void Remember<T>(Dictionary<string, Origins<T>> origins, string property, T value, T stock)
        {
            if (!origins.TryGetValue(property, out Origins<T> byValue)) origins[property] = byValue = new Origins<T>();
            byValue.Remember(value, stock);
        }

        /// <summary>
        /// The stock value of a copy's property: the original's (found by name) when the copy holds what LightGreen
        /// set on it, else the one LightGreen's value replaced, if only one did.
        /// </summary>
        private static bool Origin<T>(Dictionary<string, Origins<T>> origins, Dictionary<string, T> originalStock,
            string property, T value, T originalValue, out T stock)
        {
            if (originalStock != null && originalStock.TryGetValue(property, out stock) &&
                EqualityComparer<T>.Default.Equals(value, originalValue))
                return true;
            stock = default;
            return origins.TryGetValue(property, out Origins<T> byValue) && byValue.TryGet(value, out stock);
        }

        internal bool IsTracked(Material m) => _stock.ContainsKey(m);

        /// <summary>
        /// For a material seen for the first time: if this property holds a value LightGreen wrote to
        /// another material, the material is a copy of an edited one, so record the value it replaced as
        /// stock. Returns true if it did.
        /// </summary>
        internal bool AdoptColor(Material m, string property)
        {
            if (!m.HasProperty(property)) return false;
            Stock s = For(m);
            if (s.Colors.ContainsKey(property)) return false;
            Material original = OriginalMaterialOf(m);
            Color originalValue = original != null && original.HasProperty(property) ? original.GetColor(property) : default;
            if (!Origin(_colorOrigins, original != null ? _stock[original].Colors : null, property,
                    m.GetColor(property), originalValue, out Color stock)) return false;
            s.Colors[property] = stock;
            return true;
        }

        internal bool AdoptVector(Material m, string property)
        {
            if (!m.HasProperty(property)) return false;
            Stock s = For(m);
            if (s.Vectors.ContainsKey(property)) return false;
            Material original = OriginalMaterialOf(m);
            Vector4 originalValue = original != null && original.HasProperty(property) ? original.GetVector(property) : default;
            if (!Origin(_vectorOrigins, original != null ? _stock[original].Vectors : null, property,
                    m.GetVector(property), originalValue, out Vector4 stock)) return false;
            s.Vectors[property] = stock;
            return true;
        }

        internal bool AdoptFloat(Material m, string property)
        {
            if (!m.HasProperty(property)) return false;
            Stock s = For(m);
            if (s.Floats.ContainsKey(property)) return false;
            Material original = OriginalMaterialOf(m);
            float originalValue = original != null && original.HasProperty(property) ? ReadFloat(original, property) : 0f;
            if (!Origin(_floatOrigins, original != null ? _stock[original].Floats : null, property,
                    ReadFloat(m, property), originalValue, out float stock)) return false;
            s.Floats[property] = stock;
            return true;
        }

        /// <summary>Records a keyword's stock state when it's known to differ from the current one.</summary>
        internal void AdoptKeyword(Material m, string keyword, bool stock)
        {
            Stock s = For(m);
            if (!s.Keywords.ContainsKey(keyword)) s.Keywords[keyword] = stock;
        }

        /// <summary>Forgets materials that were destroyed with their scene.</summary>
        internal void ForgetDestroyed()
        {
            foreach (Material dead in _stock.Keys.Where(m => m == null).ToList())
                _stock.Remove(dead);
            foreach (string name in _byName.Where(pair => pair.Value == null).Select(pair => pair.Key).ToList())
                _byName.Remove(name);
        }

        internal void Restore()
        {
            foreach (KeyValuePair<Material, Stock> pair in _stock)
            {
                Material m = pair.Key;
                if (m == null) continue;
                Stock s = pair.Value;
                foreach (KeyValuePair<string, float> f in s.Floats) WriteFloat(m, f.Key, f.Value);
                foreach (KeyValuePair<string, Color> c in s.Colors) m.SetColor(c.Key, c.Value);
                foreach (KeyValuePair<string, Vector4> v in s.Vectors) m.SetVector(v.Key, v.Value);
                foreach (KeyValuePair<string, bool> k in s.Keywords)
                {
                    if (m.IsKeywordEnabled(k.Key) == k.Value) continue;
                    if (k.Value) m.EnableKeyword(k.Key);
                    else m.DisableKeyword(k.Key);
                }
            }
            _stock.Clear();
            _byName.Clear();
        }
    }
}
