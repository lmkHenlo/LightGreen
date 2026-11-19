using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LightGreen.Core
{
    /// <summary>
    /// LightGreen's own JSON for its presets and filters files. Unity's JsonUtility only serializes types from the
    /// game's own scripts, so with a plugin's classes it silently wrote nothing but the top-level numbers.
    /// <para>Maps public instance fields by reflection: numbers, bools, strings, enums (by name), colours
    /// (<c>[r, g, b]</c> or <c>[r, g, b, a]</c>; <c>{"r": …}</c> is read too), arrays of those, and nested classes.
    /// A field the file leaves out keeps the default its class gives it; unknown keys are ignored. Reading allows
    /// <c>//</c> comments and trailing commas, for hand editing.</para>
    /// </summary>
    internal static class Json
    {
        // ---- Object mapping ----

        internal static string Serialize(object value) => Write(ToTree(value));

        internal static T Deserialize<T>(string text) => (T)FromTree(typeof(T), Parse(text), "");

        internal static object ToTree(object value)
        {
            switch (value)
            {
                case null: return null;
                case string s: return s;
                case bool b: return b;
                case float f: return f;
                case double d: return d;
                case int i: return i;
                case Enum e: return e.ToString();
            }
            Type t = value.GetType();
            if (IsColor(t))
            {
                float a = (float)t.GetField("a").GetValue(value);
                var rgb = new List<object> { t.GetField("r").GetValue(value), t.GetField("g").GetValue(value), t.GetField("b").GetValue(value) };
                if (Math.Abs(a - 1f) > 1e-6f) rgb.Add(a);
                return rgb;
            }
            if (value is IList list)
            {
                var items = new List<object>(list.Count);
                foreach (object item in list) items.Add(ToTree(item));
                return items;
            }
            var obj = new List<KeyValuePair<string, object>>();
            foreach (FieldInfo field in Fields(t)) obj.Add(new KeyValuePair<string, object>(field.Name, ToTree(field.GetValue(value))));
            return obj;
        }

        internal static object FromTree(Type type, object node, string path)
        {
            if (type == typeof(string))
            {
                if (node == null || node is string) return node;
                throw Mismatch(path, "text in quotes", node);
            }
            if (type == typeof(float)) return (float)Number(node, path);
            if (type == typeof(double)) return Number(node, path);
            if (type == typeof(int))
            {
                double d = Number(node, path);
                if (d < int.MinValue || d > int.MaxValue) throw new InvalidDataException($"{Where(path)}{d} is out of range");
                return (int)Math.Round(d);
            }
            if (type == typeof(bool))
            {
                if (node is bool b) return b;
                throw Mismatch(path, "true or false", node);
            }
            if (type.IsEnum)
            {
                if (node is string s)
                {
                    foreach (string name in Enum.GetNames(type))
                        if (string.Equals(name, s.Trim(), StringComparison.OrdinalIgnoreCase)) return Enum.Parse(type, name);
                    throw new InvalidDataException($"{Where(path)}\"{s}\" isn't one of {string.Join(", ", Enum.GetNames(type))}");
                }
                return Enum.ToObject(type, (int)Math.Round(Number(node, path)));
            }
            if (IsColor(type)) return ReadColor(type, node, path);
            if (type.IsArray)
            {
                if (!(node is List<object> items)) throw Mismatch(path, "a list in [ ]", node);
                Type element = type.GetElementType();
                Array array = Array.CreateInstance(element, items.Count);
                for (int i = 0; i < items.Count; i++) array.SetValue(FromTree(element, items[i], $"{path}[{i}]"), i);
                return array;
            }

            if (!(node is List<KeyValuePair<string, object>> members)) throw Mismatch(path, "an object in { }", node);
            object instance = Activator.CreateInstance(type);   // field defaults for anything the file leaves out
            foreach (FieldInfo field in Fields(type))
            {
                int at = members.FindIndex(m => string.Equals(m.Key, field.Name, StringComparison.OrdinalIgnoreCase));
                if (at < 0) continue;
                field.SetValue(instance, FromTree(field.FieldType, members[at].Value, path.Length == 0 ? field.Name : $"{path}.{field.Name}"));
            }
            return instance;
        }

        /// <summary>Whether a parsed object (from <see cref="Parse"/>) has this key.</summary>
        internal static bool HasKey(object node, string key) =>
            node is List<KeyValuePair<string, object>> members &&
            members.Any(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));

        private static IEnumerable<FieldInfo> Fields(Type t) =>
            t.GetFields(BindingFlags.Instance | BindingFlags.Public)
             .Where(f => !f.IsNotSerialized && !f.IsInitOnly && !f.IsLiteral)
             .OrderBy(f => f.MetadataToken);

        private static bool IsColor(Type t) => t.FullName == "UnityEngine.Color";

        private static object ReadColor(Type type, object node, string path)
        {
            object color = Activator.CreateInstance(type);
            float r, g, b, a = 1f;
            if (node is List<object> items)
            {
                if (items.Count != 3 && items.Count != 4) throw new InvalidDataException($"{Where(path)}a colour needs 3 or 4 numbers, [r, g, b] or [r, g, b, a]");
                r = (float)Number(items[0], path + "[0]");
                g = (float)Number(items[1], path + "[1]");
                b = (float)Number(items[2], path + "[2]");
                if (items.Count == 4) a = (float)Number(items[3], path + "[3]");
            }
            else if (node is List<KeyValuePair<string, object>> members)
            {
                float Part(string key, float fallback)
                {
                    int at = members.FindIndex(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
                    return at < 0 ? fallback : (float)Number(members[at].Value, $"{path}.{key}");
                }
                r = Part("r", 0f);
                g = Part("g", 0f);
                b = Part("b", 0f);
                a = Part("a", 1f);
            }
            else throw Mismatch(path, "a colour, [r, g, b]", node);

            type.GetField("r").SetValue(color, r);
            type.GetField("g").SetValue(color, g);
            type.GetField("b").SetValue(color, b);
            type.GetField("a").SetValue(color, a);
            return color;
        }

        private static double Number(object node, string path)
        {
            switch (node)
            {
                case double d: return d;
                case float f: return f;
                case int i: return i;
                default: throw Mismatch(path, "a number", node);
            }
        }

        private static string Where(string path) => path.Length == 0 ? "" : path + ": ";

        private static InvalidDataException Mismatch(string path, string wanted, object node) =>
            new InvalidDataException($"{Where(path)}expected {wanted}, found {Describe(node)}");

        private static string Describe(object node)
        {
            switch (node)
            {
                case null: return "null";
                case string s: return $"\"{s}\"";
                case bool b: return b ? "true" : "false";
                case List<object> _: return "a list";
                case List<KeyValuePair<string, object>> _: return "an object";
                default: return Convert.ToString(node, CultureInfo.InvariantCulture);
            }
        }

        // ---- Writing ----

        /// <summary>Indented JSON. Lists of plain values (numbers, colours) stay on one line.</summary>
        internal static string Write(object node)
        {
            var sb = new StringBuilder();
            WriteNode(sb, node, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static void WriteNode(StringBuilder sb, object node, int depth)
        {
            switch (node)
            {
                case null:
                    sb.Append("null");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case float f:
                    sb.Append(FormatNumber(f));
                    return;
                case double d:
                    sb.Append(FormatNumber(d));
                    return;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    return;
                case List<KeyValuePair<string, object>> members:
                    if (members.Count == 0)
                    {
                        sb.Append("{}");
                        return;
                    }
                    sb.Append("{\n");
                    for (int m = 0; m < members.Count; m++)
                    {
                        Indent(sb, depth + 1);
                        WriteString(sb, members[m].Key);
                        sb.Append(": ");
                        WriteNode(sb, members[m].Value, depth + 1);
                        sb.Append(m < members.Count - 1 ? ",\n" : "\n");
                    }
                    Indent(sb, depth);
                    sb.Append('}');
                    return;
                case List<object> items:
                    if (items.Count == 0)
                    {
                        sb.Append("[]");
                        return;
                    }
                    if (items.All(x => !(x is List<KeyValuePair<string, object>>) && !(x is List<object> l && l.Any(y => y is List<object> || y is List<KeyValuePair<string, object>>))))
                    {
                        sb.Append('[');
                        for (int k = 0; k < items.Count; k++)
                        {
                            if (k > 0) sb.Append(", ");
                            WriteNode(sb, items[k], depth);
                        }
                        sb.Append(']');
                        return;
                    }
                    sb.Append("[\n");
                    for (int k = 0; k < items.Count; k++)
                    {
                        Indent(sb, depth + 1);
                        WriteNode(sb, items[k], depth + 1);
                        sb.Append(k < items.Count - 1 ? ",\n" : "\n");
                    }
                    Indent(sb, depth);
                    sb.Append(']');
                    return;
                default:
                    throw new InvalidOperationException($"can't write a {node.GetType().Name}");
            }
        }

        private static string FormatNumber(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
            string s = v.ToString("0.######", CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }

        private static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 4);

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- Reading ----

        /// <summary>
        /// Parses JSON into objects (ordered key/value lists), lists, doubles, strings, bools and null. Throws
        /// <see cref="InvalidDataException"/> with the line and column of the problem.
        /// </summary>
        internal static object Parse(string text)
        {
            var reader = new Reader(text ?? "");
            reader.SkipSpace();
            if (reader.AtEnd) throw reader.Error("the file is empty");
            object value = reader.Value();
            reader.SkipSpace();
            if (!reader.AtEnd) throw reader.Error("more after the end of the JSON");
            return value;
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            internal Reader(string s) => _s = s;

            internal bool AtEnd => _i >= _s.Length;

            internal InvalidDataException Error(string what)
            {
                int line = 1, column = 1;
                for (int k = 0; k < Math.Min(_i, _s.Length); k++)
                {
                    if (_s[k] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else column++;
                }
                return new InvalidDataException($"line {line}, column {column}: {what}");
            }

            internal void SkipSpace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') _i++;
                    else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                    {
                        while (_i < _s.Length && _s[_i] != '\n') _i++;
                    }
                    else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
                    {
                        int end = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                        if (end < 0) throw Error("a /* comment that isn't closed");
                        _i = end + 2;
                    }
                    else break;
                }
            }

            internal object Value()
            {
                SkipSpace();
                if (AtEnd) throw Error("the JSON ends too soon");
                char c = _s[_i];
                if (c == '{') return Object();
                if (c == '[') return List();
                if (c == '"') return String();
                if (c == '-' || c == '+' || c == '.' || char.IsDigit(c)) return Number();
                if (Word("true")) return true;
                if (Word("false")) return false;
                if (Word("null")) return null;
                throw Error($"unexpected '{c}'");
            }

            private bool Word(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) return false;
                _i += word.Length;
                return true;
            }

            private List<KeyValuePair<string, object>> Object()
            {
                var members = new List<KeyValuePair<string, object>>();
                _i++;
                while (true)
                {
                    SkipSpace();
                    if (AtEnd) throw Error("a { that isn't closed");
                    if (_s[_i] == '}')
                    {
                        _i++;
                        return members;
                    }
                    if (_s[_i] != '"') throw Error("expected a \"name\" in quotes");
                    string key = String();
                    SkipSpace();
                    if (AtEnd || _s[_i] != ':') throw Error($"expected : after \"{key}\"");
                    _i++;
                    members.Add(new KeyValuePair<string, object>(key, Value()));
                    SkipSpace();
                    if (AtEnd) throw Error("a { that isn't closed");
                    if (_s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (_s[_i] != '}') throw Error("expected , or } (is a comma missing?)");
                }
            }

            private List<object> List()
            {
                var items = new List<object>();
                _i++;
                while (true)
                {
                    SkipSpace();
                    if (AtEnd) throw Error("a [ that isn't closed");
                    if (_s[_i] == ']')
                    {
                        _i++;
                        return items;
                    }
                    items.Add(Value());
                    SkipSpace();
                    if (AtEnd) throw Error("a [ that isn't closed");
                    if (_s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (_s[_i] != ']') throw Error("expected , or ] (is a comma missing?)");
                }
            }

            private string String()
            {
                var sb = new StringBuilder();
                _i++;
                while (true)
                {
                    if (AtEnd) throw Error("text in quotes that isn't closed");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\n') throw Error("text in quotes that isn't closed on its line");
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd) throw Error("text in quotes that isn't closed");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length || !int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw Error("a bad \\u escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default: throw Error($"an unknown escape \\{e} (write \\\\ for a backslash, e.g. in a Windows path)");
                    }
                }
            }

            private double Number()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                string token = _s.Substring(start, _i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                {
                    _i = start;
                    throw Error($"\"{token}\" isn't a number (use a dot for decimals)");
                }
                return v;
            }
        }
    }
}
