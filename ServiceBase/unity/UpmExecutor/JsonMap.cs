#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
namespace ServiceBase.Upm
{
    // Small read-only JSON parser for Unity's manifest/lock dictionaries; no MTP dependency.
    internal sealed class JsonMap
    {
        readonly string text; int at;
        JsonMap(string value) { text = value; }
        internal static Dictionary<string, object> Read(string value)
        {
            var parser = new JsonMap(value); var result = parser.Value(0) as Dictionary<string, object>;
            parser.Space(); if (result == null || parser.at != value.Length) throw new FormatException("Invalid JSON object");
            return result;
        }
        internal static Dictionary<string, object> Object(Dictionary<string, object> map, string key)
        { object value; if (!map.TryGetValue(key, out value) || !(value is Dictionary<string, object>)) throw new FormatException("Missing JSON object"); return (Dictionary<string, object>)value; }
        internal static string String(Dictionary<string, object> map, string key)
        { object value; return map.TryGetValue(key, out value) ? value as string : null; }
        void Space() { while (at < text.Length && char.IsWhiteSpace(text[at])) at++; }
        bool Take(char c) { Space(); if (at < text.Length && text[at] == c) { at++; return true; } return false; }
        void Need(char c) { if (!Take(c)) throw new FormatException("Invalid JSON syntax"); }
        object Value(int depth)
        {
            if (depth > 64) throw new FormatException("JSON nesting limit"); Space();
            if (at >= text.Length) throw new FormatException("Truncated JSON");
            if (text[at] == '"') return Text();
            if (Take('{')) {
                var result = new Dictionary<string, object>(); if (Take('}')) return result;
                do { var key = Text(); Need(':'); if (result.ContainsKey(key)) throw new FormatException("Duplicate JSON key"); result.Add(key, Value(depth + 1)); } while (Take(','));
                Need('}'); return result;
            }
            if (Take('[')) { var result = new List<object>(); if (Take(']')) return result; do { result.Add(Value(depth + 1)); } while (Take(',')); Need(']'); return result; }
            int start = at; while (at < text.Length && !char.IsWhiteSpace(text[at]) && ",]}".IndexOf(text[at]) < 0) at++;
            string token = text.Substring(start, at - start);
            if (token == "null") return null; if (token == "true") return true; if (token == "false") return false;
            double number; if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
            throw new FormatException("Invalid JSON value");
        }
        string Text()
        {
            Need('"'); var result = new StringBuilder();
            while (at < text.Length) {
                char c = text[at++]; if (c == '"') return result.ToString();
                if (c < 32) throw new FormatException("Invalid JSON string");
                if (c == '\\') {
                    if (at >= text.Length) throw new FormatException("Truncated escape"); c = text[at++];
                    switch (c) {
                        case '"': case '\\': case '/': break;
                        case 'b': c = '\b'; break; case 'f': c = '\f'; break; case 'n': c = '\n'; break; case 'r': c = '\r'; break; case 't': c = '\t'; break;
                        case 'u': if (at + 4 > text.Length) throw new FormatException(); c = (char)int.Parse(text.Substring(at,4), NumberStyles.HexNumber); at += 4; break;
                        default: throw new FormatException("Invalid escape");
                    }
                }
                result.Append(c);
            }
            throw new FormatException("Unterminated string");
        }
    }
}
#endif
