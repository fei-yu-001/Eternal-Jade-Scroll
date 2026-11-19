using TwelveJade.Core;
using UnityEngine;

namespace TwelveJade.Network
{
    /// <summary>
    /// 网络配置（M6-03-C）。来自 Resources/Config/network-config.json；
    /// 文件缺失或字段缺失时回退到线上账号与云存档服务的约定值——
    /// 离线第一原则：配置坏掉只降级，绝不阻塞游戏启动。
    /// </summary>
    public sealed class NetworkConfig
    {
        public string ServerBaseUrl = "https://jade.ddyu.online";
        public float TimeoutSeconds = 6f;
        public int MaxRetryCount = 3;
        public int RetryBackoffBaseMs = 500;
        public long MaxPayloadBytes = 262144;

        public static NetworkConfig Load()
        {
            var config = new NetworkConfig();
            var asset = Resources.Load<TextAsset>("Config/network-config");
            if (asset == null)
            {
                Debug.LogWarning("[NetworkConfig] 缺少 Resources/Config/network-config.json，使用默认配置。");
                return config;
            }
            try
            {
                var root = Json.Parse(asset.text);
                config.ServerBaseUrl = root["serverBaseUrl"].AsString(config.ServerBaseUrl).TrimEnd('/');
                config.TimeoutSeconds = (float)root["timeoutSeconds"].AsNumber(config.TimeoutSeconds);
                if (config.TimeoutSeconds <= 0f) config.TimeoutSeconds = 6f;
                config.MaxRetryCount = root["maxRetryCount"].AsInt(config.MaxRetryCount);
                if (config.MaxRetryCount < 0) config.MaxRetryCount = 0;
                config.RetryBackoffBaseMs = root["retryBackoffBaseMs"].AsInt(config.RetryBackoffBaseMs);
                if (config.RetryBackoffBaseMs <= 0) config.RetryBackoffBaseMs = 500;
                config.MaxPayloadBytes = (long)root["maxPayloadBytes"].AsNumber(config.MaxPayloadBytes);
                if (config.MaxPayloadBytes <= 0) config.MaxPayloadBytes = 262144;
            }
            catch (System.FormatException ex)
            {
                Debug.LogError("[NetworkConfig] 配置 JSON 解析失败，使用默认配置：" + ex.Message);
            }
            return config;
        }
    }
}
