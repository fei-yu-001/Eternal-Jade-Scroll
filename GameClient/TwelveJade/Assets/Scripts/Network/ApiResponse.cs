using System;
using TwelveJade.Core;

namespace TwelveJade.Network
{
    /// <summary>
    /// 服务端统一返回体的客户端解析（ADR-023）。
    ///
    /// 服务端实际输出 { code, message, data }（data 为 null 时字段省略——
    /// ADR-023 提到的 timestamp 字段服务端并不输出，解析层对缺失字段一律容错，
    /// 不因多出来的或缺失的字段炸掉）。
    /// </summary>
    public sealed class ApiResponse
    {
        public int Code { get; }
        public string Message { get; }
        public JsonValue Data { get; }

        public bool IsSuccess => Code == 0;

        ApiResponse(int code, string message, JsonValue data)
        {
            Code = code;
            Message = message;
            Data = data;
        }

        public static bool TryParse(string body, out ApiResponse response, out string error)
        {
            response = null;
            error = null;
            if (string.IsNullOrWhiteSpace(body)) { error = "响应体为空。"; return false; }
            JsonValue root;
            try { root = Json.Parse(body); }
            catch (FormatException ex) { error = "响应不是合法 JSON：" + ex.Message; return false; }
            if (root.kind != JsonValue.Kind.Object) { error = "响应根节点不是对象。"; return false; }
            var code = root["code"].AsInt(int.MinValue);
            if (code == int.MinValue) { error = "响应缺少可读的 code 字段。"; return false; }
            response = new ApiResponse(code, root["message"].AsString(""),
                root["data"].kind == JsonValue.Kind.Null ? JsonValue.Null() : root["data"]);
            return true;
        }

        public static ApiResponse Parse(string body)
        {
            var ok = TryParse(body, out var response, out var error);
            if (!ok) throw new FormatException(error);
            return response;
        }
    }
}
