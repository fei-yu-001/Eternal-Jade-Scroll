using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TwelveJade.Core
{
    // 核心层自带一份极小的 JSON 读取器：配置表（物品表、青石镇地图数据）要在没有 Unity 的
    // 测试进程里解析——JsonUtility 属 Unity 运行时，System.Text.Json 又不在 Unity 里，
    // 与其引依赖，不如自己读。只实现 JSON 标准的一个子集，够读配置即可。
    public sealed class JsonValue
    {
        public enum Kind { Null, Bool, Number, String, Array, Object }

        readonly List<JsonValue> elements;
        readonly Dictionary<string, JsonValue> members;

        JsonValue(Kind valueKind)
        {
            kind = valueKind;
            if (valueKind == Kind.Array) elements = new List<JsonValue>();
            if (valueKind == Kind.Object) members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
        }

        public Kind kind { get; }

        // 缺字段与 null 一律读成 Missing：读配置的代码不必层层判空，只判 IsNull 即可。
        public static readonly JsonValue Missing = new JsonValue(Kind.Null);

        public static JsonValue Null() => Missing;

        public static JsonValue Bool(bool value)
        {
            var item = new JsonValue(Kind.Bool);
            item.boolean = value;
            return item;
        }

        public static JsonValue Number(double value)
        {
            var item = new JsonValue(Kind.Number);
            item.number = value;
            return item;
        }

        public static JsonValue String(string value)
        {
            var item = new JsonValue(Kind.String);
            item.text = value;
            return item;
        }

        public static JsonValue Array(IEnumerable<JsonValue> values)
        {
            var item = new JsonValue(Kind.Array);
            if (values != null) item.elements.AddRange(values);
            return item;
        }

        public static JsonValue Object()
        {
            return new JsonValue(Kind.Object);
        }

        public static JsonValue Object(IEnumerable<KeyValuePair<string, JsonValue>> values)
        {
            var item = new JsonValue(Kind.Object);
            if (values != null)
                foreach (var pair in values) item.members[pair.Key] = pair.Value;
            return item;
        }

        public JsonValue Add(string key, JsonValue value)
        {
            if (members == null) throw new InvalidOperationException("只有对象可以按名字添加成员。");
            members[key] = value;
            return this;
        }

        public bool boolean { get; private set; }
        public double number { get; private set; }
        public string text { get; private set; }

        public bool IsNull => kind == Kind.Null;
        public int Count => elements?.Count ?? members?.Count ?? 0;
        public IReadOnlyList<JsonValue> Items => elements ?? (IReadOnlyList<JsonValue>)System.Array.Empty<JsonValue>();
        public JsonValue this[int index] =>
            elements != null && index >= 0 && index < elements.Count ? elements[index] : Missing;
        public JsonValue this[string key] =>
            members != null && members.TryGetValue(key, out var value) ? value : Missing;
        public bool Has(string key) => members != null && members.ContainsKey(key);
        public IEnumerable<string> Keys => members?.Keys ?? Enumerable.Empty<string>();

        public string AsString(string fallback) => kind == Kind.String ? text : fallback;
        public bool AsBool(bool fallback) => kind == Kind.Bool ? boolean : fallback;
        public double AsNumber(double fallback) => kind == Kind.Number ? number : fallback;

        public int AsInt(int fallback)
        {
            if (kind != Kind.Number) return fallback;
            return Math.Abs(number - Math.Round(number)) > .0001 ? fallback : (int)Math.Round(number);
        }

        public float AsFloat(float fallback) => kind == Kind.Number ? (float)number : fallback;

        public string Describe() => kind switch
        {
            Kind.Null => "空值",
            Kind.Bool => "布尔值",
            Kind.Number => "数字",
            Kind.String => "字符串",
            Kind.Array => "数组",
            _ => "对象",
        };
    }

    public static class Json
    {
        public static JsonValue Parse(string source)
        {
            if (!TryParse(source, out var value, out var error)) throw new FormatException(error);
            return value;
        }

        public static bool TryParse(string source, out JsonValue value, out string error)
        {
            value = null;
            error = null;
            if (string.IsNullOrEmpty(source)) { error = "内容为空。"; return false; }
            try
            {
                var reader = new Reader(source);
                value = reader.ReadValue();
                reader.SkipWhitespace();
                if (!reader.AtEnd) throw reader.Error("末尾有多余内容。");
                return true;
            }
            catch (FormatException ex)
            {
                value = null;
                error = ex.Message;
                return false;
            }
        }

        // 递归下降读取：报错一律带字符位置，改配置表时能一眼找到写错的地方。
        sealed class Reader
        {
            readonly string source;
            int index;

            public Reader(string text) { source = text; }

            public bool AtEnd => index >= source.Length;
            public FormatException Error(string message) => new FormatException("JSON 第 " + (index + 1) + " 字符：" + message);

            public void SkipWhitespace()
            {
                while (index < source.Length && (source[index] == ' ' || source[index] == '\t' ||
                                                 source[index] == '\n' || source[index] == '\r')) index++;
            }

            char Peek()
            {
                if (AtEnd) throw Error("内容意外结束。");
                return source[index];
            }

            void Expect(char value)
            {
                if (AtEnd || source[index] != value) throw Error("应为 '" + value + "'。");
                index++;
            }

            public JsonValue ReadValue()
            {
                SkipWhitespace();
                return Peek() switch
                {
                    '{' => ReadObject(),
                    '[' => ReadArray(),
                    '"' => JsonValue.String(ReadString()),
                    't' => ReadLiteral("true") ? JsonValue.Bool(true) : throw Error("布尔字面量有误。"),
                    'f' => ReadLiteral("false") ? JsonValue.Bool(false) : throw Error("布尔字面量有误。"),
                    'n' => ReadLiteral("null") ? JsonValue.Null() : throw Error("空值字面量有误。"),
                    _ => ReadNumber(),
                };
            }

            JsonValue ReadObject()
            {
                var result = JsonValue.Object();
                Expect('{');
                SkipWhitespace();
                if (!AtEnd && Peek() == '}')
                {
                    index++;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    var key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    result.Add(key, ReadValue());
                    SkipWhitespace();
                    if (AtEnd) throw Error("对象没有收尾的 '}'。");
                    if (Peek() == ',')
                    {
                        index++;
                        continue;
                    }
                    Expect('}');
                    return result;
                }
            }

            JsonValue ReadArray()
            {
                var items = new List<JsonValue>();
                Expect('[');
                SkipWhitespace();
                if (!AtEnd && Peek() == ']')
                {
                    index++;
                    return JsonValue.Array(items);
                }
                while (true)
                {
                    items.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd) throw Error("数组没有收尾的 ']'。");
                    if (Peek() == ',')
                    {
                        index++;
                        continue;
                    }
                    Expect(']');
                    return JsonValue.Array(items);
                }
            }

            string ReadString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("字符串没有收尾的双引号。");
                    var current = source[index++];
                    if (current == '"') break;
                    if (current != '\\')
                    {
                        builder.Append(current);
                        continue;
                    }
                    if (AtEnd) throw Error("转义符后内容意外结束。");
                    var escape = source[index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (index + 4 > source.Length) throw Error("\\u 转义需要 4 位十六进制。");
                            var code = source.Substring(index, 4);
                            if (!ushort.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
                                throw Error("\\u 转义不是合法的十六进制：" + code);
                            builder.Append((char)parsed);
                            index += 4;
                            break;
                        default: throw Error("不认识的转义符：\\" + escape);
                    }
                }
                return builder.ToString();
            }

            JsonValue ReadNumber()
            {
                var start = index;
                if (!AtEnd && (Peek() == '-' || Peek() == '+')) index++;
                while (!AtEnd && (char.IsDigit(source[index]) || source[index] == '.' ||
                                  source[index] == 'e' || source[index] == 'E')) index++;
                var slice = source.Substring(start, index - start);
                if (slice.Length == 0 || !double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw Error("不是合法的数字：" + (slice.Length == 0 ? "(空)" : slice));
                return JsonValue.Number(value);
            }

            bool ReadLiteral(string literal)
            {
                if (index + literal.Length > source.Length ||
                    string.CompareOrdinal(source, index, literal, 0, literal.Length) != 0) return false;
                index += literal.Length;
                return true;
            }
        }
    }
}
