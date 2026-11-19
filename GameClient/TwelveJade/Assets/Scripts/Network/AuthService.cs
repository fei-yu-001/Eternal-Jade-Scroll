using System;
using System.Collections;

namespace TwelveJade.Network
{
    /// <summary>认证/云存档操作的用户可读结果。</summary>
    public sealed class AuthResult
    {
        public bool Success;
        public int Code;                // 成功 0；失败为业务码或 0（网络层失败）
        public string Message;
        public string Token;
        public long UserId;
        public string Username;
        /// <summary>登录命中 40904（HTTP 429）：账号处于失败锁定中。</summary>
        public bool IsLockedOut;
        /// <summary>传输层失败（服务器不可达）：调用方应走离线降级提示。</summary>
        public bool NetworkFailure;

        public static AuthResult Ok(string token, long userId, string username) =>
            new AuthResult { Success = true, Code = 0, Message = "成功", Token = token, UserId = userId, Username = username };

        public static AuthResult Fail(int code, string message,
            bool lockedOut = false, bool networkFailure = false) =>
            new AuthResult { Success = false, Code = code, Message = message,
                IsLockedOut = lockedOut, NetworkFailure = networkFailure };
    }

    /// <summary>
    /// 认证全流程服务（M6-03-C）：注册、登录、续期、改密、登出、身份探测。
    /// 客户端前置校验与服务端规则一致（用户名 3–32、密码 6–64、均不含空白），
    /// 但前置校验只为了少打一次无效请求——服务端才是规则的真源。
    /// </summary>
    public class AuthService
    {
        readonly NetworkClient client;

        public AuthService(NetworkClient client)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>注册成功即登录：令牌与身份写入凭据管理器。</summary>
        public IEnumerator Register(string username, string password, Action<AuthResult> done)
        {
            var check = Validate(username, password);
            if (check != null) { done(check); yield break; }
            yield return SendAuth("/api/auth/register", username, password, needsAuth: false, done);
        }

        public IEnumerator Login(string username, string password, Action<AuthResult> done)
        {
            var check = Validate(username, password);
            if (check != null) { done(check); yield break; }
            yield return SendAuth("/api/auth/login", username, password, needsAuth: false, result =>
            {
                if (result.Success && result.Code == 0)
                    LocalCredentialManager.StoreCredentials(result.Token, result.UserId, result.Username);
                done(result);
            });
        }

        /// <summary>手动续期：一般由 NetworkClient 的拦截器自动完成，这里暴露给 UI 的"延长登录"入口。</summary>
        public IEnumerator RefreshToken(Action<AuthResult> done)
        {
            yield return client.SendJson(RequestSpec.Post("/api/auth/refresh", null, needsAuth: true), result =>
            {
                done(FinishAuthCall(result, storeNewToken: true));
            });
        }

        /// <summary>改密：新密码不得与旧密码一致、长度 6–64、无空白（与服务端口径一致）。</summary>
        public IEnumerator ChangePassword(string oldPassword, string newPassword, Action<AuthResult> done)
        {
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6 || newPassword.Length > 64)
            { done(AuthResult.Fail(40001, "新密码需 6–64 个字符。")); yield break; }
            if (HasWhitespace(newPassword))
            { done(AuthResult.Fail(40001, "新密码不允许含空白字符。")); yield break; }
            if (newPassword == oldPassword)
            { done(AuthResult.Fail(40001, "新密码不得与旧密码相同。")); yield break; }
            var body = "{\"oldPassword\":" + NetworkJson.Escape(oldPassword) +
                       ",\"newPassword\":" + NetworkJson.Escape(newPassword) + "}";
            yield return client.SendJson(RequestSpec.Post("/api/auth/password", body, needsAuth: true), result =>
            {
                done(FinishAuthCall(result, storeNewToken: false));
            });
        }

        /// <summary>身份探测：GET /api/me，用于刷新界面上的用户名显示。</summary>
        public IEnumerator GetCurrentUserInfo(Action<AuthResult> done)
        {
            yield return client.SendJson(RequestSpec.Get("/api/me", needsAuth: true), result =>
            {
                var auth = FinishAuthCall(result, storeNewToken: false);
                if (auth.Success) auth.Username = result.Response?.Data["username"].AsString("");
                done(auth);
            });
        }

        /// <summary>登出：服务端令牌置空 + 本地凭据清空（幂等）。</summary>
        public IEnumerator Logout(Action<AuthResult> done)
        {
            yield return client.SendJson(RequestSpec.Post("/api/auth/logout", null, needsAuth: true), result =>
            {
                LocalCredentialManager.ForgetCredentials();
                done(result.Ok
                    ? AuthResult.Ok(null, 0, null)
                    : AuthResult.Fail(result.Error.Code, result.Error.Message));
            });
        }

        // ---------- 内部 ----------

        IEnumerator SendAuth(string path, string username, string password, bool needsAuth,
            Action<AuthResult> done)
        {
            var body = "{\"username\":" + NetworkJson.Escape(username) +
                       ",\"password\":" + NetworkJson.Escape(password) + "}";
            yield return client.SendJson(RequestSpec.Post(path, body, needsAuth), result =>
            {
                var auth = FinishAuthCall(result, storeNewToken: false);
                if (auth.Success)
                {
                    auth.Token = result.Response.Data["token"].AsString("");
                    auth.UserId = (long)result.Response.Data["userId"].AsNumber(0);
                    LocalCredentialManager.StoreCredentials(auth.Token, auth.UserId, auth.Username);
                }
                done(auth);
            });
        }

        static AuthResult FinishAuthCall(ApiResult result, bool storeNewToken)
        {
            if (!result.Ok)
            {
                var error = result.Error;
                var lockedOut = error.Code == 40904;
                var message = lockedOut ? "失败次数过多，账号已临时锁定，请稍后再试" : error.Message;
                return AuthResult.Fail(error.Code, message, lockedOut, error.HttpStatus == 0);
            }
            var token = result.Response.Data.Has("token") ? result.Response.Data["token"].AsString("") : null;
            var userId = (long)result.Response.Data["userId"].AsNumber(0);
            if (storeNewToken && !string.IsNullOrEmpty(token))
                LocalCredentialManager.StoreCredentials(token, userId, LocalCredentialManager.Username);
            return AuthResult.Ok(token, userId, null);
        }

        static AuthResult Validate(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || username.Length < 3 || username.Length > 32)
                return AuthResult.Fail(40001, "用户名需 3–32 个字符。");
            foreach (var ch in username)
                if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                    return AuthResult.Fail(40001, "用户名只能包含汉字、字母、数字或下划线。");
            if (string.IsNullOrEmpty(password) || password.Length < 6 || password.Length > 64)
                return AuthResult.Fail(40001, "密码需 6–64 个字符。");
            if (HasWhitespace(password))
                return AuthResult.Fail(40001, "密码不允许包含空格或换行。");
            return null;
        }

        static bool HasWhitespace(string text)
        {
            foreach (var ch in text)
                if (char.IsWhiteSpace(ch)) return true;
            return false;
        }
    }
}
