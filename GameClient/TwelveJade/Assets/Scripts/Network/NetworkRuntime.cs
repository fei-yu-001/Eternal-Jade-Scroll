using System;
using System.Collections;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEngine;

namespace TwelveJade.Network
{
    /// <summary>
    /// 网络域运行时接线（M6-03-C）：场景加载后自举一个宿主，
    /// 持有 NetworkClient / AuthService / CloudSaveService 单例并做健康探测。
    /// 与 WorldPropLayer 的自举同款模式——不改 FrontEndController 一行代码。
    /// </summary>
    public class NetworkRuntime : MonoBehaviour
    {
        public static NetworkRuntime Instance { get; private set; }

        public NetworkClient Client { get; private set; }
        public AuthService Auth { get; private set; }
        public CloudSaveService Cloud { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<NetworkRuntime>() != null) return;
            new GameObject("NetworkRuntime").AddComponent<NetworkRuntime>();
        }

        void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Client = new NetworkClient(NetworkConfig.Load());
            Client.BindCredentials(
                tokenProvider: () => LocalCredentialManager.Token,
                tokenWriter: token =>
                {
                    if (string.IsNullOrEmpty(token)) LocalCredentialManager.ClearToken();
                    else LocalCredentialManager.StoreCredentials(token, LocalCredentialManager.UserId,
                        LocalCredentialManager.Username);
                });
            Auth = new AuthService(Client);
            Cloud = new CloudSaveService(Client, CreateLocalRepository);
        }

        void Start() => ProbeNow();

        /// <summary>健康探测：服务可达 → 回到联网态；不可达 → 无感降级离线单机。</summary>
        public void ProbeNow() => StartCoroutine(ProbeOnce());

        IEnumerator ProbeOnce()
        {
            var done = false;
            ApiResult result = null;
            yield return Client.SendJson(RequestSpec.Get("/api/health"), r => { result = r; done = true; });
            if (!done) yield break;
            if (result != null && result.Ok) LocalCredentialManager.SetOnline();
            else LocalCredentialManager.EnterOfflineLocal();
        }

        /// <summary>
        /// 本地存档仓的根目录解析与 FrontEndController 同源（TWELVEJADE_SAVEDIR 环境变量
        /// 优先，批处理验收靠它隔离；普通运行落到 persistentDataPath/Saves）。
        /// 这里刻意自建实例而不是借用 controller——网络域必须能在主菜单（controller 未建）时工作。
        /// </summary>
        public static SaveRepository CreateLocalRepository()
        {
            var saveRoot = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            if (string.IsNullOrEmpty(saveRoot))
                saveRoot = System.IO.Path.Combine(Application.persistentDataPath, "Saves");
            return new SaveRepository(saveRoot, new UnityJsonCodec());
        }
    }
}
