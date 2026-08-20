// A small, forgiving JSON reader/writer.
//
// This replaces DataContractJsonSerializer, which the predecessor used and which is wrong
// for this job three times over:
//   * it is sensitive to member ordering, and the live usage response is NOT alphabetically
//     ordered (seven_day_cowork arrives after seven_day_sonnet)
//   * it skips field initializers, so any setting absent from an older config file reads
//     back as default(T) - that is how a missing "scale" would silently become 0 and render
//     the widget at zero size
//   * it cannot enumerate keys it was not compiled to know about, which is exactly what
//     discovering new usage buckets requires
//
// Navigation never throws: an absent path yields JNode.Null, so walking into a response
// shape the account does not have is safe.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Vibespan
{
    public enum JKind { Null, Bool, Number, String, Array, Object }

    public class JNode
    {
        public JKind Kind = JKind.Null;
        public bool BoolValue;
        public double NumberValue;
        public string StringValue;
        public List<JNode> Items;
        public List<string> Keys;             // insertion order preserved, for round-tripping
        public Dictionary<string, JNode> Map;

        public static readonly JNode Null = new JNode();

        public static JNode NewObject()
        {
            var n = new JNode();
            n.Kind = JKind.Object;
            n.Keys = new List<string>();
            n.Map = new Dictionary<string, JNode>(StringComparer.Ordinal);
            return n;
        }
        public static JNode NewArray()
        {
            var n = new JNode();
            n.Kind = JKind.Array;
            n.Items = new List<JNode>();
            return n;
        }
        public static JNode Str(string v)
        {
            if (v == null) return Null;
            var n = new JNode(); n.Kind = JKind.String; n.StringValue = v; return n;
        }
        public static JNode Num(double v) { var n = new JNode(); n.Kind = JKind.Number; n.NumberValue = v; return n; }
        public static JNode Bool(bool v) { var n = new JNode(); n.Kind = JKind.Bool; n.BoolValue = v; return n; }

        public JNode this[string key]
        {
            get
            {
                if (Kind != JKind.Object || Map == null) return Null;
                JNode v;
                return Map.TryGetValue(key, out v) ? v : Null;
            }
        }
        public JNode this[int i]
        {
            get
            {
                if (Kind != JKind.Array || Items == null || i < 0 || i >= Items.Count) return Null;
                return Items[i];
            }
        }

        public int Count { get { return Kind == JKind.Array && Items != null ? Items.Count : 0; } }
        public bool IsNull { get { return Kind == JKind.Null; } }
        public bool Exists { get { return Kind != JKind.Null; } }
        public IList<string> Fields { get { return Keys != null ? (IList<string>)Keys : new List<string>(); } }

        public string AsString(string fallback) { return Kind == JKind.String ? StringValue : fallback; }
        public bool AsBool(bool fallback) { return Kind == JKind.Bool ? BoolValue : fallback; }
        public double AsDouble(double fallback) { return Kind == JKind.Number ? NumberValue : fallback; }
        public int AsInt(int fallback) { return Kind == JKind.Number ? (int)Math.Round(NumberValue) : fallback; }
        public long AsLong(long fallback) { return Kind == JKind.Number ? (long)NumberValue : fallback; }

        // Numbers sometimes arrive as strings and vice versa; be liberal on the way in.
        public double AsNumberLoose(double fallback)
        {
            if (Kind == JKind.Number) return NumberValue;
            if (Kind == JKind.String)
            {
                double d;
                if (double.TryParse(StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            }
            return fallback;
        }

        public void Set(string key, JNode value)
        {
            if (Kind != JKind.Object) return;
            if (!Map.ContainsKey(key)) Keys.Add(key);
            Map[key] = value ?? Null;
        }
        public void Set(string key, string v) { Set(key, Str(v)); }
        public void Set(string key, double v) { Set(key, Num(v)); }
        public void Set(string key, bool v) { Set(key, Bool(v)); }
        public void Add(JNode item) { if (Kind == JKind.Array) Items.Add(item ?? Null); }

        public override string ToString() { return JsonWriter.Write(this, false); }
        public string ToPretty() { return JsonWriter.Write(this, true); }
    }

    public static class JsonReader
    {
        // Returns JNode.Null on malformed input rather than throwing: a corrupt config or a
        // truncated response should degrade to defaults, never crash the widget.
        public static JNode Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return JNode.Null;
            int i = 0;
            try
            {
                JNode v = ParseValue(text, ref i);
                return v ?? JNode.Null;
            }
            catch { return JNode.Null; }
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        static JNode ParseValue(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return JNode.Null;
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return JNode.Str(ParseString(s, ref i));
            if (c == 't' && Match(s, i, "true")) { i += 4; return JNode.Bool(true); }
            if (c == 'f' && Match(s, i, "false")) { i += 5; return JNode.Bool(false); }
            if (c == 'n' && Match(s, i, "null")) { i += 4; return JNode.Null; }
            return ParseNumber(s, ref i);
        }

        static bool Match(string s, int i, string word)
        {
            if (i + word.Length > s.Length) return false;
            for (int k = 0; k < word.Length; k++) if (s[i + k] != word[k]) return false;
            return true;
        }

        static JNode ParseObject(string s, ref int i)
        {
            var o = JNode.NewObject();
            i++;                                   // {
            Ws(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (i < s.Length)
            {
                Ws(s, ref i);
                if (i >= s.Length || s[i] != '"') break;
                string key = ParseString(s, ref i);
                Ws(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                JNode val = ParseValue(s, ref i);
                o.Set(key, val);
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return o;
        }

        static JNode ParseArray(string s, ref int i)
        {
            var a = JNode.NewArray();
            i++;                                   // [
            Ws(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (i < s.Length)
            {
                a.Add(ParseValue(s, ref i));
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return a;
        }

        static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;                                   // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
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
                        if (i + 4 <= s.Length)
                        {
                            int cp;
                            if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                             CultureInfo.InvariantCulture, out cp))
                                sb.Append((char)cp);
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        static JNode ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' ||
                                    s[i] == '-' || s[i] == '+')) i++;
            double d;
            if (i > start && double.TryParse(s.Substring(start, i - start), NumberStyles.Float,
                                             CultureInfo.InvariantCulture, out d))
                return JNode.Num(d);
            return JNode.Null;
        }
    }

    public static class JsonWriter
    {
        public static string Write(JNode n, bool pretty)
        {
            var sb = new StringBuilder();
            W(sb, n, pretty, 0);
            return sb.ToString();
        }

        static void Indent(StringBuilder sb, int depth) { sb.Append(' ', depth * 2); }

        static void W(StringBuilder sb, JNode n, bool pretty, int depth)
        {
            if (n == null) { sb.Append("null"); return; }
            switch (n.Kind)
            {
                case JKind.Null: sb.Append("null"); break;
                case JKind.Bool: sb.Append(n.BoolValue ? "true" : "false"); break;
                case JKind.Number: sb.Append(Number(n.NumberValue)); break;
                case JKind.String: Escape(sb, n.StringValue); break;
                case JKind.Array:
                    if (n.Items.Count == 0) { sb.Append("[]"); break; }
                    sb.Append('[');
                    for (int i = 0; i < n.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) { sb.Append('\n'); Indent(sb, depth + 1); }
                        W(sb, n.Items[i], pretty, depth + 1);
                    }
                    if (pretty) { sb.Append('\n'); Indent(sb, depth); }
                    sb.Append(']');
                    break;
                case JKind.Object:
                    if (n.Keys.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    for (int i = 0; i < n.Keys.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) { sb.Append('\n'); Indent(sb, depth + 1); }
                        Escape(sb, n.Keys[i]);
                        sb.Append(':');
                        if (pretty) sb.Append(' ');
                        W(sb, n.Map[n.Keys[i]], pretty, depth + 1);
                    }
                    if (pretty) { sb.Append('\n'); Indent(sb, depth); }
                    sb.Append('}');
                    break;
            }
        }

        static string Number(double d)
        {
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
                return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        static void Escape(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }
    }
}
