using System;
using UnityEngine;

namespace TwelveJade.Network
{
    /// <summary>网络连接运行状态（M6-03-C 离线优先状态机）。</summary>
    public enum ConnectionState
    {
        /// <summary>联网未登录（游客）：云功能不可用，单机功能完整。</summary>
        OnlineGuest,
        /// <summary>联网已认证：云存档全功能可用。</summary>
        OnlineAuthenticated,
        /// <summary>离线单机：一切云端能力降级，本地存档读写不受任何影响。</summary>
        OfflineLocal,
    }

    /// <summary>
    /// 键值存储抽象：生产走 PlayerPrefs，测试注入内存实现——
    /// 为了让"密码绝不落盘"这条铁律可以被自动化断言（枚举写入的键逐一核对）。
    /// </summary>
    public interface IKeyValueStore
    {
        string GetString(string key, string fallback);
        void SetString(string key, string value);
        void DeleteKey(string key);
        bool HasKey(string key);
    }

    sealed class PlayerPrefsStore : IKeyValueStore
    {
        public string GetString(string key, string fallback) => PlayerPrefs.GetString(key, fallback);
        public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
        public void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);
        public bool HasKey(string key) => PlayerPrefs.HasKey(key);
    }

    /// <summary>
    /// 本地凭据管理器（M6-03-C · 离线优先容灾）。
    ///
    /// 铁律一：本类**没有任何密码字段或密码键**——明文密码只在登录/注册请求里瞬时存在，
    /// 绝不写进 PlayerPrefs 或任何本地文件（NetworkShot 有专项断言）。
    /// 铁律二：存档读写永远走本地 SaveRepository，本类只管身份与状态，
    /// 主游玩线程绝不因网络阻塞。
    /// </summary>
    public static class LocalCredentialManager
    {
        public const string KeyToken = "tj.network.authToken";
        public const string KeyUserId = "tj.network.userId";
        public const string KeyUsername = "tj.network.username";
        public const string KeyLastSyncTime = "tj.network.lastSyncTime";
        public const string KeyOfflineFlag = "tj.network.offlineMode";

        static IKeyValueStore store = new PlayerPrefsStore();

        /// <summary>当前所有可能写入的键（测试用：落盘键必须全部出自这份白名单）。</summary>
        public static readonly string[] KnownKeys =
            { KeyToken, KeyUserId, KeyUsername, KeyLastSyncTime, KeyOfflineFlag };

        /// <summary>测试注入内存键值存储；传 null 恢复 PlayerPrefs。</summary>
        public static void SetStoreForTests(IKeyValueStore custom) =>
            store = custom ?? new PlayerPrefsStore();

        public static ConnectionState Current { get; private set; } = ConnectionState.OnlineGuest;

        public static bool IsOfflineLocal => Current == ConnectionState.OfflineLocal;
        public static bool IsOnlineAuthenticated => Current == ConnectionState.OnlineAuthenticated;

        public static string Token => store.GetString(KeyToken, null);
        public static long UserId => long.TryParse(store.GetString(KeyUserId, ""), out var id) ? id : 0;
        public static string Username => store.GetString(KeyUsername, null);
        public static bool HasStoredToken => !string.IsNullOrEmpty(Token);

        /// <summary>登录/注册成功：持久化令牌与身份，进入联网已认证状态。</summary>
        public static void StoreCredentials(string token, long userId, string username)
        {
            store.SetString(KeyToken, token ?? "");
            store.SetString(KeyUserId, userId.ToString());
            store.SetString(KeyUsername, username ?? "");
            store.DeleteKey(KeyOfflineFlag);
            Current = ConnectionState.OnlineAuthenticated;
        }

        /// <summary>每次成功的云端同步刷新最近同步时间（ISO8601，UTC）。</summary>
        public static void UpdateLastSyncTime()
        {
            store.SetString(KeyLastSyncTime, DateTime.UtcNow.ToString("O"));
        }

        public static string LastSyncTime => store.GetString(KeyLastSyncTime, null);

        /// <summary>仅清除令牌（改密失败/过期广播时用），保留用户名以便登录框回显。</summary>
        public static void ClearToken() => store.DeleteKey(KeyToken);

        /// <summary>登出：清空全部凭据，回到联网游客态。</summary>
        public static void ForgetCredentials()
        {
            foreach (var key in KnownKeys) store.DeleteKey(key);
            Current = ConnectionState.OnlineGuest;
        }

        /// <summary>进入离线单机模式（探测失败或用户主动选择"离线游玩"）。</summary>
        public static void EnterOfflineLocal()
        {
            store.SetString(KeyOfflineFlag, "1");
            Current = ConnectionState.OfflineLocal;
        }

        /// <summary>健康探测成功：按本地是否有令牌回到对应联网状态。</summary>
        public static void SetOnline()
        {
            store.DeleteKey(KeyOfflineFlag);
            Current = HasStoredToken
                ? ConnectionState.OnlineAuthenticated
                : ConnectionState.OnlineGuest;
        }

        /// <summary>测试辅助：清空全部状态与存储。</summary>
        public static void ResetForTests() => ForgetCredentials();
    }
}
