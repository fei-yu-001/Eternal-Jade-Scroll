using System.Text;
using TwelveJade.Core;

namespace TwelveJade.Network
{
    /// <summary>
    /// 轻量 JSON 回写与字符串转义（M6-03-C）。
    ///
    /// Core/Json 只提供解析没有回写；网络层要把下载的 payload 存回本地、
    /// 要拼请求体，都离不开它。手写转义覆盖 ", \\、控制字符与常见 Unicode——
    /// 密码里出现引号也不能把请求体拼坏。
    /// </summary>
    public static class NetworkJson
    {
        public static string Escape(string text)
        {
            if (text == null) return "null";
            var builder = new StringBuilder(text.Length + 8);
            builder.Append('"');
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (ch < ' ') builder.Append("\\u").Append(((int)ch).ToString("x4"));
                        else builder.Append(ch);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>把 JsonValue 写回 JSON 文本（对象/数组/字符串/数字/布尔/null）。</summary>
        public static string Write(JsonValue value)
        {
            var builder = new StringBuilder();
            WriteValue(value, builder);
            return builder.ToString();
        }

        static void WriteValue(JsonValue value, StringBuilder builder)
        {
            switch (value.kind)
            {
                case JsonValue.Kind.Null:
                    builder.Append("null");
                    break;
                case JsonValue.Kind.Bool:
                    builder.Append(value.boolean ? "true" : "false");
                    break;
                case JsonValue.Kind.Number:
                    builder.Append(value.number.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case JsonValue.Kind.String:
                    builder.Append(Escape(value.text));
                    break;
                case JsonValue.Kind.Array:
                    builder.Append('[');
                    for (var i = 0; i < value.Count; i++)
                    {
                        if (i > 0) builder.Append(',');
                        WriteValue(value[i], builder);
                    }
                    builder.Append(']');
                    break;
                case JsonValue.Kind.Object:
                    builder.Append('{');
                    var first = true;
                    foreach (var key in value.Keys)
                    {
                        if (!first) builder.Append(',');
                        first = false;
                        builder.Append(Escape(key)).Append(':');
                        WriteValue(value[key], builder);
                    }
                    builder.Append('}');
                    break;
            }
        }
    }
}
