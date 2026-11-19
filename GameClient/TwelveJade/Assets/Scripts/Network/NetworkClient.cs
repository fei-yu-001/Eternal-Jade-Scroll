using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace TwelveJade.Network
{
    /// <summary>业务错误：服务端返回了非 0 的业务 code（4xx 一律不重试，直接上抛）。</summary>
    public class ApiException : Exception
    {
        public int Code { get; }
        public int HttpStatus { get; }

        public ApiException(int code, string message, int httpStatus)
            : base($"[{code}] {message}")
        {
            Code = code;
            HttpStatus = httpStatus;
        }
    }

    /// <summary>网络层结果：成功带 ApiResponse；失败带 ApiException（业务/网络/超时统一归一）。</summary>
    public sealed class ApiResult
    {
        public bool Ok { get; }
        public ApiResponse Response { get; }
        public ApiException Error { get; }

        ApiResult(bool ok, ApiResponse response, ApiException error)
        {
            Ok = ok;
            Response = response;
            Error = error;
        }

        public static ApiResult Success(ApiResponse response) => new ApiResult(true, response, null);
        public static ApiResult Failure(ApiException error) => new ApiResult(false, null, error);
    }

    /// <summary>一次请求的规格。NeedsAuth = 携带 X-Auth-Token 头。</summary>
    public sealed class RequestSpec
    {
        public string Method = "GET";
        public string Path = "/";
        public string JsonBody;
        public bool NeedsAuth;
        /// <summary>true = 这本身是续期请求（40102 时不再套娃续期）。</summary>
        public bool IsRefreshCall;

        public static RequestSpec Get(string path, bool needsAuth = false) =>
            new RequestSpec { Method = UnityWebRequest.kHttpVerbGET, Path = path, NeedsAuth = needsAuth };

        public static RequestSpec Post(string path, string jsonBody, bool needsAuth = false) =>
            new RequestSpec { Method = UnityWebRequest.kHttpVerbPOST, Path = path, JsonBody = jsonBody, NeedsAuth = needsAuth };
    }

    /// <summary>
    /// 健壮网络通信层（M6-03-C · ADR-023）。
    ///
    /// 职责：URL 拼装、超时、指数退避重试（仅网络错误与 5xx；4xx 业务错误绝不重试）、
    /// X-Auth-Token 注入、401/40102 时自动续期一次并重放原请求、续期失败清空令牌并广播。
    /// 传输层可注入：生产用 UnityWebRequestTransport，测试用脚本化假传输。
    /// </summary>
    public class NetworkClient
    {
        /// <summary>身份凭证请求头（与服务端 AuthTokens.HEADER 对应，客户端侧常量独立成文）。</summary>
        public const string AuthHeaderName = "X-Auth-Token";
        public interface ITransport
        {
            /// <summary>发起一次 HTTP 请求；完成（含失败）时回调 done 恰好一次。</summary>
            IEnumerator Send(string method, string url, string jsonBody, float timeoutSeconds,
                string authToken, Action<TransportResult> done);
        }

        public sealed class TransportResult
        {
            public int HttpStatus;      // 0 = 网络层失败（连接拒绝/DNS/超时）
            public string Body;
            public string NetworkErrorMessage;
            public bool IsNetworkError => HttpStatus == 0;
            public bool IsServerError => HttpStatus >= 500;
        }

        readonly NetworkConfig config;
        readonly ITransport transport;

        /// <summary>当前令牌的读取口（由 LocalCredentialManager 提供实现）。</summary>
        Func<string> tokenProvider;
        /// <summary>令牌写回口：续期成功写新令牌；续期失败写 null（清空）。</summary>
        Action<string> tokenWriter;

        /// <summary>续期失败（令牌彻底不可用）时广播——UI 层据此提示重新登录。</summary>
        public event Action SessionExpired;

        /// <summary>测试钩子：true 时退避等待退化为 yield null，批处理测试可同步驱动。</summary>
        public bool SkipBackoffWaitsForTests;

        public NetworkConfig Config => config;

        public NetworkClient(NetworkConfig config, ITransport transport = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
#if UNITY_OPENHARMONY && !UNITY_EDITOR
            this.transport = transport ?? new HarmonyHttpTransport();
#else
            this.transport = transport ?? new UnityWebRequestTransport();
#endif
        }

        public void BindCredentials(Func<string> tokenProvider, Action<string> tokenWriter)
        {
            this.tokenProvider = tokenProvider;
            this.tokenWriter = tokenWriter;
        }

        /// <summary>退避间隔：baseMs × 2^attempt（第 1 次重试等 baseMs，第 2 次等 2×baseMs……）。</summary>
        public float BackoffSeconds(int attempt) =>
            config.RetryBackoffBaseMs * (float)Math.Pow(2, Math.Max(0, attempt - 1)) / 1000f;

        /// <summary>该传输结果是否值得重试：网络层失败与 5xx/502/503/504 抖动；4xx 一律不重试。</summary>
        public bool ShouldRetry(TransportResult result) =>
            result == null || result.IsNetworkError || result.HttpStatus >= 500;

        /// <summary>
        /// 发送一个 JSON API 请求并完成重试/续期/重放全流程；完成时 done 恰好回调一次。
        /// 以协程驱动（调用方 StartCoroutine 或测试里手动 MoveNext）。
        /// </summary>
        public IEnumerator SendJson(RequestSpec spec, Action<ApiResult> done)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (done == null) throw new ArgumentNullException(nameof(done));

            var attempt = 0;
            var replayedAfterRefresh = false;
            while (true)
            {
                var token = spec.NeedsAuth ? tokenProvider?.Invoke() : null;
                var url = config.ServerBaseUrl + spec.Path;
                TransportResult result = null;
                yield return transport.Send(spec.Method, url, spec.JsonBody, config.TimeoutSeconds, token,
                    r => result = r);

                // 传输层异常：按可重试处理（含退避）。
                if (result == null)
                {
                    done(ApiResult.Failure(new ApiException(0, "传输层无响应", 0)));
                    yield break;
                }
                // 重试判据只从 ShouldRetry 出：它同时被单测断言，生产走同一条路才是"验的就是跑的那条路"。
                // 早先这里是把条件抄了一遍，改任何一边都会让测试与线上行为悄悄分叉。
                if (ShouldRetry(result))
                {
                    if (attempt < config.MaxRetryCount)
                    {
                        attempt++;
                        yield return BackoffWait(attempt);
                        continue;
                    }
                    done(ApiResult.Failure(new ApiException(0,
                        string.IsNullOrEmpty(result.NetworkErrorMessage) ? "网络不可达" : result.NetworkErrorMessage,
                        result.HttpStatus)));
                    yield break;
                }

                // 业务响应：解析统一返回体。
                if (!ApiResponse.TryParse(result.Body, out var response, out var parseError))
                {
                    done(ApiResult.Failure(new ApiException(0, "响应解析失败：" + parseError, result.HttpStatus)));
                    yield break;
                }

                // 令牌过期：自动续期一次并重放原请求（续期请求自身绝不再套娃）。
                if (spec.NeedsAuth && !spec.IsRefreshCall && !replayedAfterRefresh &&
                    result.HttpStatus == 401 && response.Code == 40102)
                {
                    var refreshed = false;
                    yield return TryRefreshToken(ok => refreshed = ok);
                    if (refreshed)
                    {
                        replayedAfterRefresh = true;
                        continue; // 用新令牌重放原请求
                    }
                    done(ApiResult.Failure(new ApiException(40102, "登录状态已过期，请重新登录", 401)));
                    yield break;
                }

                done(response.Code == 0
                    ? ApiResult.Success(response)
                    : ApiResult.Failure(new ApiException(response.Code, response.Message, result.HttpStatus)));
                yield break;
            }
        }

        /// <summary>令牌续期：成功写回新令牌；失败清空令牌并广播 SessionExpired。</summary>
        IEnumerator TryRefreshToken(Action<bool> done)
        {
            var oldToken = tokenProvider?.Invoke();
            if (string.IsNullOrEmpty(oldToken))
            {
                done(false);
                yield break;
            }
            var spec = new RequestSpec
            {
                Method = UnityWebRequest.kHttpVerbPOST,
                Path = "/api/auth/refresh",
                NeedsAuth = true,
                IsRefreshCall = true,
            };
            var url = config.ServerBaseUrl + spec.Path;
            TransportResult result = null;
            yield return transport.Send(spec.Method, url, null, config.TimeoutSeconds, oldToken, r => result = r);
            ApiResponse response = null;
            // Data 可能整个缺席（服务端 code:0 但没带 data）：先判空再问有没有 token，
            // 否则这里抛 NRE，调用方看到的是"续期崩溃"而不是"续期没成功"。
            var ok = result != null && !result.IsNetworkError && result.HttpStatus == 200 &&
                     ApiResponse.TryParse(result.Body, out response, out _) && response != null &&
                     response.IsSuccess && response.Data != null && response.Data.Has("token");
            if (ok)
            {
                tokenWriter?.Invoke(response.Data["token"].AsString(""));
                done(string.IsNullOrEmpty(tokenProvider?.Invoke()) == false);
            }
            else
            {
                tokenWriter?.Invoke(null); // 续期失败：清空本地令牌
                SessionExpired?.Invoke();  // 通知 UI：登录状态已过期
                done(false);
            }
        }

        IEnumerator BackoffWait(int attempt)
        {
            if (SkipBackoffWaitsForTests) yield return null;
            else yield return new WaitForSecondsRealtime(BackoffSeconds(attempt));
        }

        /// <summary>默认传输：UnityWebRequest（带超时与 X-Auth-Token 头）。</summary>
        sealed class UnityWebRequestTransport : ITransport
        {
            public IEnumerator Send(string method, string url, string jsonBody, float timeoutSeconds,
                string authToken, Action<TransportResult> done)
            {
                var request = new UnityWebRequest(url, method);
                if (!string.IsNullOrEmpty(jsonBody))
                {
                    var bytes = Encoding.UTF8.GetBytes(jsonBody);
                    request.uploadHandler = new UploadHandlerRaw(bytes);
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, Mathf.RoundToInt(timeoutSeconds));
                if (!string.IsNullOrEmpty(authToken)) request.SetRequestHeader(AuthHeaderName, authToken);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                    done(new TransportResult { HttpStatus = (int)request.responseCode, Body = request.downloadHandler.text });
                else if (request.responseCode != 0)
                    // 有 HTTP 状态码的失败（4xx/5xx）不算网络层失败，交给上层按业务处理。
                    done(new TransportResult { HttpStatus = (int)request.responseCode, Body = request.downloadHandler?.text });
                else
                    done(new TransportResult { HttpStatus = 0, NetworkErrorMessage = request.error ?? "网络不可达" });
            }
        }
    }
}
