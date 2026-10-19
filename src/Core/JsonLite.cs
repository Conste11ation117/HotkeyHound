using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HotkeyHound.Core
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>极简 JSON 值。对象成员保持插入顺序，便于报告里稳定输出。</summary>
    public sealed class JsonValue
    {
        public JsonKind Kind = JsonKind.Null;
        public string Str;
        public double Num;
        public bool Bool;
        public List<JsonValue> Items;
        public List<KeyValuePair<string, JsonValue>> Members;
        private Dictionary<string, JsonValue> _index;

        public bool IsObject { get { return Kind == JsonKind.Object; } }
        public bool IsArray { get { return Kind == JsonKind.Array; } }
        public bool IsString { get { return Kind == JsonKind.String; } }
        public bool IsNumber { get { return Kind == JsonKind.Number; } }
        public bool IsBool { get { return Kind == JsonKind.Bool; } }
        public bool IsNull { get { return Kind == JsonKind.Null; } }

        public int Count
        {
            get
            {
                if (Kind == JsonKind.Array) return Items.Count;
                if (Kind == JsonKind.Object) return Members.Count;
                return 0;
            }
        }

        public IEnumerable<KeyValuePair<string, JsonValue>> Pairs
        {
            get
            {
                if (Members == null) yield break;
                foreach (var kv in Members) yield return kv;
            }
        }

        public IEnumerable<JsonValue> Elements
        {
            get
            {
                if (Items == null) yield break;
                foreach (var v in Items) yield return v;
            }
        }

        public JsonValue this[string key]
        {
            get
            {
                JsonValue v;
                if (TryGet(key, out v)) return v;
                return null;
            }
        }

        public bool TryGet(string key, out JsonValue value)
        {
            value = null;
            if (Kind != JsonKind.Object || _index == null) return false;
            return _index.TryGetValue(key, out value);
        }

        internal void AddMember(string key, JsonValue value)
        {
            if (Members == null) Members = new List<KeyValuePair<string, JsonValue>>();
            if (_index == null) _index = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            Members.Add(new KeyValuePair<string, JsonValue>(key, value));
            _index[key] = value;
        }

        /// <summary>把数字当整数读；非数字返回 fallback。</summary>
        public int AsInt(int fallback)
        {
            if (Kind == JsonKind.Number) return (int)Math.Round(Num);
            if (Kind == JsonKind.Bool) return Bool ? 1 : 0;
            if (Kind == JsonKind.String)
            {
                double d;
                if (double.TryParse(Str, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return (int)Math.Round(d);
            }
            return fallback;
        }

        public bool AsBool(bool fallback)
        {
            if (Kind == JsonKind.Bool) return Bool;
            if (Kind == JsonKind.Number) return Num != 0;
            if (Kind == JsonKind.String) return string.Equals(Str, "true", StringComparison.OrdinalIgnoreCase);
            return fallback;
        }

        public string AsString(string fallback)
        {
            if (Kind == JsonKind.String) return Str;
            if (Kind == JsonKind.Number) return Num.ToString(CultureInfo.InvariantCulture);
            if (Kind == JsonKind.Bool) return Bool ? "true" : "false";
            return fallback;
        }
    }

    public sealed class JsonLiteException : Exception
    {
        public JsonLiteException(string message) : base(message) { }
    }

    public static class JsonLite
    {
        public static JsonValue ParseFile(string path)
        {
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new JsonLiteException(L10n.T("json.nullInput"));
            int pos = 0;
            var v = ParseValue(text, ref pos, 0);
            SkipWs(text, ref pos);
            return v;
        }

        /// <summary>解析失败返回 null，不抛异常（适配器只读场景用）。</summary>
        public static JsonValue TryParse(string text)
        {
            try { return Parse(text); }
            catch (Exception) { return null; }
        }

        public static JsonValue TryParseFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return ParseFile(path);
            }
            catch (Exception) { return null; }
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                break;
            }
        }

        // 嵌套深度上限。这个解析器是互递归（ParseValue ↔ ParseObject/ParseArray），没有上限时，
        // 一个畸形或被截断的配置文件（几万个 '['）就能把调用栈打穿——StackOverflowException
        // 是 .NET 里唯一 catch 不住的异常，进程当场消失，外面 TryParse 的 catch 也救不回来。
        // 真实的 PowerToys / OBS / Everything 配置最深不到 10 层，64 层足够宽松。
        private const int MaxDepth = 64;

        private static JsonValue ParseValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new JsonLiteException(L10n.F("json.tooDeep", MaxDepth));
            SkipWs(s, ref i);
            if (i >= s.Length) throw new JsonLiteException(L10n.T("json.unexpectedEnd"));
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i, depth);
                case '[': return ParseArray(s, ref i, depth);
                case '"': return MakeString(ParseString(s, ref i));
                case 't':
                    Expect(s, ref i, "true");
                    return MakeBool(true);
                case 'f':
                    Expect(s, ref i, "false");
                    return MakeBool(false);
                case 'n':
                    Expect(s, ref i, "null");
                    return new JsonValue();
                default:
                    return ParseNumber(s, ref i);
            }
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new JsonLiteException(L10n.F("json.expected", i, word));
            i += word.Length;
        }

        private static JsonValue ParseObject(string s, ref int i, int depth)
        {
            var o = new JsonValue();
            o.Kind = JsonKind.Object;
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new JsonLiteException(L10n.F("json.expectedMember", i));
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new JsonLiteException(L10n.F("json.expectedColon", i));
                i++;
                JsonValue val = ParseValue(s, ref i, depth + 1);
                o.AddMember(key, val);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new JsonLiteException(L10n.T("json.unclosedObject"));
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return o; }
                throw new JsonLiteException(L10n.F("json.expectedCommaOrBrace", i));
            }
        }

        private static JsonValue ParseArray(string s, ref int i, int depth)
        {
            var a = new JsonValue();
            a.Kind = JsonKind.Array;
            a.Items = new List<JsonValue>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Items.Add(ParseValue(s, ref i, depth + 1));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new JsonLiteException(L10n.T("json.unclosedArray"));
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return a; }
                throw new JsonLiteException(L10n.F("json.expectedCommaOrBracket", i));
            }
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new JsonLiteException(L10n.T("json.unclosedString"));
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new JsonLiteException(L10n.T("json.escapeUnfinished"));
                char e = s[i++];
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
                        if (i + 4 > s.Length) throw new JsonLiteException(L10n.T("json.unicodeEscapeIncomplete"));
                        sb.Append((char)ushort.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new JsonLiteException(L10n.F("json.unknownEscape", e));
                }
            }
        }

        private static JsonValue ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') { i++; continue; }
                break;
            }
            if (i == start) throw new JsonLiteException(L10n.F("json.badValue", start));
            string token = s.Substring(start, i - start);
            double d;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new JsonLiteException(L10n.F("json.badNumber", token));
            var v = new JsonValue();
            v.Kind = JsonKind.Number;
            v.Num = d;
            return v;
        }

        private static JsonValue MakeString(string s)
        {
            var v = new JsonValue();
            v.Kind = JsonKind.String;
            v.Str = s;
            return v;
        }

        private static JsonValue MakeBool(bool b)
        {
            var v = new JsonValue();
            v.Kind = JsonKind.Bool;
            v.Bool = b;
            return v;
        }
    }
}
