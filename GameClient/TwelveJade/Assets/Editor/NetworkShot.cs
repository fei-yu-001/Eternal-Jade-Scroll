using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;
using TwelveJade.Network;
using TwelveJade.Network.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    /// <summary>
    /// 网络域自动化验证夹具（M6-03-C · M9）：无头批处理，10 大场景。
    /// 全部用假传输/内存存储/临时目录——不需要真实后端、不碰玩家真实存档。
    /// 执行：Unity.exe -batchmode -executeMethod TwelveJade.Editor.NetworkShot.RunTests
    /// 收尾输出：[NetworkShot] finished, N problem(s)，0 才算过。
    /// </summary>
    public static class NetworkShot
    {
        static readonly List<string> problems = new List<string>();
        static int okCount;

        public static void RunTests()
        {
            Debug.Log("[NetworkShot] begin");
            // 假传输只挡住了"不打真服务"，挡不住"往开发者机器上写东西"：凭据默认落 PlayerPrefs，
            // 而 S4 在 S6 换内存存储之前就会写一次。开局就换成内存实现，验收跑完机器上干干净净。
            LocalCredentialManager.SetStoreForTests(new MemoryStore());
            try
            {
                Scenario1_ConfigParse();
                Scenario2_ApiResponseParse();
                Scenario3_PayloadPrecheck();
                Scenario4_AutoRefreshOn40102();
                Scenario5_LockoutMessage();
                Scenario6_CredentialsNeverStorePassword();
                Scenario7_OfflineLocalSaveRead();
                Scenario8_ConflictCompare();
                Scenario9_BackupBundleRoundTrip();
                Scenario10_DialogLifecycle();
                Scenario11_ListSlotsRealContract();
                Scenario12_UploadRequestBody();
                Scenario13_DownloadThreeModes();
                Scenario14_CrossTimezoneVerdict();
                Scenario15_UploadSlotConflict();
                Scenario16_LiveConflictEnvelope();
            }
            catch (Exception ex)
            {
                problems.Add("夹具异常: " + ex);
            }
            Debug.Log($"[NetworkShot] ok: {okCount} 项断言通过");
            Debug.Log("[NetworkShot] finished, " + problems.Count + " problem(s)");
            foreach (var problem in problems) Debug.LogError(problem);
            EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
        }

        static void Check(bool condition, string message, string detail = null)
        {
            if (condition) { okCount++; Debug.Log("[NetworkShot] ok: " + message); }
            else problems.Add("断言失败: " + message + (detail == null ? "" : " · " + detail));
        }

        // ---------- 场景 1：network-config.json 解析正常，BaseUrl 合法 ----------
        static void Scenario1_ConfigParse()
        {
            var config = NetworkConfig.Load();
            var client = new NetworkClient(config) { SkipBackoffWaitsForTests = true };
            Check(config != null, "S1 配置应可加载");
            Check(config.ServerBaseUrl.StartsWith("http://", StringComparison.Ordinal) ||
                  config.ServerBaseUrl.StartsWith("https://", StringComparison.Ordinal),
                "S1 BaseUrl 应是合法 http(s) 地址", config.ServerBaseUrl);
            Check(config.ServerBaseUrl.Contains("18080"), "S1 默认端口应为 18080", config.ServerBaseUrl);
            Check(config.TimeoutSeconds > 0, "S1 超时应为正数");
            Check(config.MaxRetryCount >= 0, "S1 重试次数应非负");
            Check(config.MaxPayloadBytes == 262144, "S1 payload 上限应为 262144 字节",
                config.MaxPayloadBytes + "");
            Check(Math.Abs(client.BackoffSeconds(1) - .5f) < .001f &&
                  Math.Abs(client.BackoffSeconds(2) - 1f) < .001f,
                "S1 指数退避：第 1 次 500ms、第 2 次 1000ms");
            Check(client.ShouldRetry(new NetworkClient.TransportResult { HttpStatus = 0 }) &&
                  client.ShouldRetry(new NetworkClient.TransportResult { HttpStatus = 503 }) &&
                  !client.ShouldRetry(new NetworkClient.TransportResult { HttpStatus = 404 }) &&
                  !client.ShouldRetry(new NetworkClient.TransportResult { HttpStatus = 429 }),
                "S1 重试判定：网络错误与 5xx 重试，4xx 不重试");
        }

        // ---------- 场景 2：ApiResponse 正确解析后端真实 JSON 样例 ----------
        static void Scenario2_ApiResponseParse()
        {
            var ok = ApiResponse.Parse(
                "{\"code\":0,\"message\":\"成功\",\"data\":{\"userId\":1,\"token\":\"abc-def\"}}");
            Check(ok.IsSuccess && ok.Code == 0, "S2 成功样例 code=0");
            Check(ok.Data.Has("token") && ok.Data["token"].AsString("") == "abc-def", "S2 data.token 解析正确");
            Check(!ok.Data.Has("timestamp"), "S2 缺失的 timestamp 字段不炸解析（ADR-023 容错）");

            var fail = ApiResponse.Parse("{\"code\":40904,\"message\":\"登录失败次数过多，请稍后再试\"}");
            Check(!fail.IsSuccess && fail.Code == 40904, "S2 失败样例 code=40904");
            Check(fail.Data == null || fail.Data.kind != JsonValue.Kind.Object || !fail.Data.Has("userId"),
                "S2 无 data 字段时 Data 不含业务键");

            var bad = ApiResponse.TryParse("不是JSON", out _, out var error);
            Check(!bad && !string.IsNullOrEmpty(error), "S2 非法响应体应报可读错误");

            var client = NewClient(new ScriptedTransport(_ =>
                new NetworkClient.TransportResult { HttpStatus = 200, Body = "{\"code\":0,\"message\":\"成功\"}" }));
            ApiResult result = null;
            Drive(client.SendJson(RequestSpec.Get("/api/health"), r => result = r));
            Check(result != null && result.Ok, "S2 NetworkClient 端到端解析（假传输）");
        }

        // ---------- 场景 3：超过 256KB 的数据在上传前被正确拦截 ----------
        static void Scenario3_PayloadPrecheck()
        {
            var cloud = NewCloudService(out var repository, out var codec);
            var info = repository.Create(1, "hero", "预检员");
            var payload = codec.Serialize(info);
            // 版本号从真源取，不在夹具里抄一份（T087 同一条裁定）：抄的那份每次升版都会变成假红。
            Check(payload.Contains("\"schemaVersion\": " + SaveData.CurrentSchemaVersion) ||
                payload.Contains("\"schemaVersion\":" + SaveData.CurrentSchemaVersion),
                "S3 本地序列化应带当前 schemaVersion",
                "期望 " + SaveData.CurrentSchemaVersion + " · " + payload.Substring(0, Math.Min(80, payload.Length)));

            var underLimit = "{\"coins\":1,\"bag\":[],\"pad\":\"" + new string('x', 200 * 1024) + "\"}";
            CheckCode(() => cloud.PrecheckPayload(underLimit), "S3 200KB payload 预检通过");

            var overLimit = "{\"coins\":1,\"bag\":[],\"pad\":\"" + new string('x', 300 * 1024) + "\"}";
            var threw = false;
            try { cloud.PrecheckPayload(overLimit); }
            catch (PayloadTooLargeException ex)
            {
                threw = true;
                Check(ex.ActualBytes > ex.LimitBytes && ex.LimitBytes == 262144,
                    "S3 超限异常携带实际字节与上限", ex.ActualBytes + "/" + ex.LimitBytes);
            }
            Check(threw, "S3 300KB payload 被预检拦截");
        }

        // ---------- 场景 4：模拟 40102 Token 过期触发自动续期 ----------
        static void Scenario4_AutoRefreshOn40102()
        {
            var tokens = new List<string>();
            var client = NewClient(new ScriptedTransport(url =>
            {
                if (url.EndsWith("/api/auth/refresh"))
                    return new NetworkClient.TransportResult
                    {
                        HttpStatus = 200,
                        Body = "{\"code\":0,\"message\":\"成功\",\"data\":{\"userId\":7,\"token\":\"refreshed-token\"}}"
                    };
                // 业务请求第一次返回 401/40102（令牌过期）
                return new NetworkClient.TransportResult
                {
                    HttpStatus = 401,
                    Body = "{\"code\":40102,\"message\":\"令牌无效或未登录\"}"
                };
            }));
            LocalCredentialManager.StoreCredentials("expired-token", 7, "续期员");
            client.BindCredentials(
                tokenProvider: () => LocalCredentialManager.Token,
                tokenWriter: token =>
                {
                    // 与 NetworkRuntime 同款写回：新令牌必须能被下一次请求读到。
                    if (string.IsNullOrEmpty(token)) LocalCredentialManager.ClearToken();
                    else LocalCredentialManager.StoreCredentials(token,
                        LocalCredentialManager.UserId, LocalCredentialManager.Username);
                    tokens.Add(token);
                });

            ApiResult result = null;
            Drive(client.SendJson(RequestSpec.Get("/api/me", needsAuth: true), r => result = r));
            // 上面模拟的业务端点永远 401：第一次 40102 → 续期成功 → 重放 → 仍 401/40102
            // （真实世界重放会成功；这里验证的是"续期被触发 + 新令牌被写回 + 重放确实发生"）。
            Check(result != null && !result.Ok && result.Error.Code == 40102,
                "S4 业务请求在续期后重放仍 40102 时如实上抛");
            Check(tokens.Contains("refreshed-token"), "S4 续期返回的新 token 已写回凭据");
            Check(LocalCredentialManager.Token == "refreshed-token", "S4 本地令牌已更新为续期令牌");
        }

        // ---------- 场景 5：模拟 40904 限流返回正确的锁定文案 ----------
        static void Scenario5_LockoutMessage()
        {
            var client = NewClient(new ScriptedTransport(_ =>
                new NetworkClient.TransportResult
                {
                    HttpStatus = 429,
                    Body = "{\"code\":40904,\"message\":\"登录失败次数过多，请稍后再试\"}"
                }));
            var auth = new AuthService(client);
            AuthResult result = null;
            Drive(auth.Login("locked_user", "pass1234", r => result = r));
            Check(result != null && !result.Success && result.IsLockedOut, "S5 登录命中限流应置 IsLockedOut");
            Check(result.Code == 40904, "S5 业务 code 应为 40904", result.Code + "");
            Check(result.Message.Contains("锁定"), "S5 锁定文案应包含“锁定”", result.Message);
        }

        // ---------- 场景 6：本地凭据存取，确认密码未存入 ----------
        static void Scenario6_CredentialsNeverStorePassword()
        {
            var store = new MemoryStore();
            LocalCredentialManager.SetStoreForTests(store);
            LocalCredentialManager.StoreCredentials("token-xyz", 42, "存档侠");

            Check(LocalCredentialManager.HasStoredToken && LocalCredentialManager.Token == "token-xyz",
                "S6 令牌已存取");
            Check(LocalCredentialManager.UserId == 42 && LocalCredentialManager.Username == "存档侠",
                "S6 userId 与用户名已存取");
            foreach (var key in store.Keys)
                Check(!key.ToLowerInvariant().Contains("password") && !key.ToLowerInvariant().Contains("passwd"),
                    "S6 落盘键不含密码字样：" + key);
            Check(store.Keys.All(key => LocalCredentialManager.KnownKeys.Contains(key)),
                "S6 落盘键全部出自凭据白名单（密码无处可写）",
                string.Join(",", store.Keys));
            Check(!store.Values.Any(v => v != null && v.Contains("S3cret!密码")),
                "S6 任何存储值中不含明文密码内容");
            LocalCredentialManager.ForgetCredentials();
            Check(!LocalCredentialManager.HasStoredToken && !store.HasKey(LocalCredentialManager.KeyToken),
                "S6 忘却凭据后令牌被清除");
        }

        // ---------- 场景 7：离线模式下读取本地存档，游戏规则不受影响 ----------
        static void Scenario7_OfflineLocalSaveRead()
        {
            var root = NewTempDir();
            var repository = new SaveRepository(root, new Presentation.UnityJsonCodec());
            var created = repository.Create(1, "hero", "离线行者");
            Check(created != null && created.coins == ItemTable.StartingCoins, "S7 离线前本地建档成功");

            LocalCredentialManager.EnterOfflineLocal();
            Check(LocalCredentialManager.IsOfflineLocal, "S7 状态机进入 OfflineLocal");
            var read = repository.Read(1);
            Check(read.CanLoad && read.Data.characterName == "离线行者" && read.Data.slot == 1,
                "S7 离线模式读取本地存档内容完整");
            Check(read.Data.schemaVersion == SaveData.CurrentSchemaVersion,
                "S7 本地存档 schemaVersion 等于当前版", read.Data.schemaVersion + "");
            read.Data.coins += 7;
            repository.Write(read.Data);
            Check(repository.Read(1).Data.coins == created.coins + 7, "S7 离线模式存盘写回生效");
            Check(SaveChecksProbe(root), "S7 该临时存档通过 SaveRepository 全套校验");
            LocalCredentialManager.ResetForTests();
        }

        // ---------- 场景 8：冲突时间戳计算 ----------
        static void Scenario8_ConflictCompare()
        {
            var now = DateTime.UtcNow;
            var localNewer = now.ToString("O");
            var cloudOlder = now.AddHours(-3).ToString("O");
            var cloudNewer = now.AddHours(+3).ToString("O");
            Check(CloudSaveService.Compare(localNewer, cloudOlder) == ConflictSide.LocalNewer,
                "S8 本地较新 → LocalNewer");
            Check(CloudSaveService.Compare(localNewer, cloudNewer) == ConflictSide.CloudNewer,
                "S8 云端较新 → CloudNewer");
            Check(CloudSaveService.Compare("", cloudNewer) == ConflictSide.Unknown,
                "S8 本地时间缺失 → Unknown（不猜方向）");
            Check(CloudSaveService.Compare("垃圾时间", cloudNewer) == ConflictSide.Unknown,
                "S8 无法解析的时间 → Unknown");
            // W-Server-Time 起服务端 updatedAt 是带 UTC 偏移的 ISO8601（Instant，Z 结尾），必须可解析
            Check(DateTimeOffset.TryParse("2026-10-05T12:09:33.69094Z", out _),
                "S8 服务端带偏移的 ISO8601 格式可被 TryParse 接受");
            Check(CloudSaveService.VerdictText(ConflictSide.LocalNewer).Contains("本地"),
                "S8 判定文案可读");
        }

        // ---------- 场景 9：全量备份 Bundle 导出与恢复一致性 ----------
        static void Scenario9_BackupBundleRoundTrip()
        {
            var root = NewTempDir();
            var repository = new SaveRepository(root, new Presentation.UnityJsonCodec());
            var codec = new Presentation.UnityJsonCodec();
            var slot1 = repository.Create(1, "hero", "备份甲");
            slot1.coins = 888;
            repository.Write(slot1);
            var slot2 = repository.Create(2, "mage", "备份乙");
            slot2.coins = 66;
            repository.Write(slot2);

            var bundle = LocalBackupUtility.ExportAllLocalSavesToBundle(repository, codec);
            Check(bundle.Contains("\"version\":1") && bundle.Contains("\"sha256\":\""), "S9 备份含版本与校验和");

            // 校验和自证：改一个字符必须被拒
            var tampered = bundle.Replace("备份甲", "备份被改");
            var tamperRejected = false;
            try { LocalBackupUtility.ImportBundleToLocalSaves(tampered, repository, codec, true); }
            catch (InvalidOperationException ex) { tamperRejected = ex.Message.Contains("校验和"); }
            Check(tamperRejected, "S9 篡改后的备份被校验和拒绝");

            // 一致性：导出 → 清空 → 导入 → 槽位语义字段一致（updatedUtc 由 Write 刷新为恢复时刻，
            // 因此不做逐字节比较，比对玩家可见字段）。
            var name1 = repository.Read(1).Data.characterName;
            var coins1 = repository.Read(1).Data.coins;
            var name2 = repository.Read(2).Data.characterName;
            var coins2 = repository.Read(2).Data.coins;
            repository.Delete(1);
            repository.Delete(2);
            Check(repository.Read(1).State == SlotState.Empty, "S9 恢复前槽位 1 已清空");
            LocalBackupUtility.ImportBundleToLocalSaves(bundle, repository, codec, true);
            Check(repository.Read(1).Data.characterName == name1 && repository.Read(1).Data.coins == coins1,
                "S9 槽位 1 恢复一致");
            Check(repository.Read(2).Data.characterName == name2 && repository.Read(2).Data.coins == coins2,
                "S9 槽位 2 恢复一致");
            Check(repository.Read(3).State == SlotState.Empty, "S9 空槽位 3 恢复后仍为空（忠实还原）");
        }

        // ---------- 场景 10：AuthDialog / CloudSaveDialog 实例化与生命周期无泄漏 ----------
        static void Scenario10_DialogLifecycle()
        {
            var canvasGo = new GameObject("NetworkShotCanvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            AuthDialog.Open();
            Check(AuthDialog.IsOpen, "S10 AuthDialog 应已打开");
            var authRoot = GameObject.Find("AuthDialog");
            Check(authRoot != null, "S10 AuthDialog 根节点存在");
            Check(authRoot != null && authRoot.transform.Find("AuthErrorLabel") != null,
                "S10 AuthDialog 含错误提示标签");
            Check(authRoot != null && authRoot.transform.Find("PasswordInput") != null,
                "S10 AuthDialog 含密码输入框（遮罩型）");

            CloudSaveDialog.Open();
            // AuthDialog 是单例：CloudSaveDialog 打开不影响它（两者可并存）
            Check(AuthDialog.IsOpen && CloudSaveDialog.IsOpen, "S10 两对话框可并存");
            foreach (var slot in new[] { 1, 2, 3 })
                Check(GameObject.Find("SlotCard " + slot) != null, "S10 SlotCard " + slot + " 已构建");
            Check(GameObject.Find("SlotVerdictText 2") != null, "S10 槽位判定文案节点存在");

            AuthDialog.Close();
            CloudSaveDialog.Close();
            Check(!AuthDialog.IsOpen && !CloudSaveDialog.IsOpen, "S10 关闭后对话框状态复位");
            // Destroy 为帧末延迟执行，批处理内不再断言场景对象即时消失；
            // 泄漏防护由单例根引用置空（IsOpen=false）保证，任何后续 Open 都从零重建。
            UnityEngine.Object.DestroyImmediate(canvasGo);
        }

        // ---------- 场景 11–14：云存档三条核心操作，对**真后端抓回的响应原文** ----------
        //
        // S1–S10 用的是"任何请求都回 code:0"的假传输，所以 ListSlots / UploadSlot /
        // DownloadToSlot 这三条真正会写玩家存档的路径，一条都没被验过。
        // 下面场景的响应体取自 2026-10-05 对本地真后端（W-Server-Time 已合入，
        // updatedAt 为带 UTC 偏移的 ISO8601）实测 curl 回来的原文，不是照想象编的——
        // 假传输最大的风险就是客户端和自己的想象对表。

        const string RealListBody = "{\"code\":0,\"message\":\"成功\",\"data\":{\"slots\":[" +
            "{\"slot\":1,\"present\":true,\"updatedAt\":\"2026-10-05T12:09:33.69094Z\"}," +
            "{\"slot\":2,\"present\":false,\"updatedAt\":null}," +
            "{\"slot\":3,\"present\":false,\"updatedAt\":null}]}}";
        const string RealUploadReceipt = "{\"code\":0,\"message\":\"成功\",\"data\":{\"slot\":1," +
            "\"persisted\":true,\"migratable\":false,\"updatedAt\":\"2026-10-05T12:09:33.69094Z\"}}";
        const string RealDownloadEmpty = "{\"code\":0,\"message\":\"成功\",\"data\":{\"slot\":1,\"present\":false}}";

        static void Scenario11_ListSlotsRealContract()
        {
            var repository = RepoWithSave(1, "槽位侠", 456);
            var cloud = NewCloudServiceOn(NewClient(new RecordingTransport(
                ("GET", "/api/save/list", RealListBody))), repository);

            List<CloudSlotMetadata> slots = null;
            ApiException error = null;
            Drive(cloud.ListSlots((s, e) => { slots = s; error = e; }));
            Check(error == null, "S11 真契约 list 应解析成功", error?.Message);
            Check(slots != null && slots.Count == 3, "S11 固定回三槽位", slots == null ? "null" : slots.Count + "");
            Check(slots != null && slots[0].present && slots[0].slot == 1, "S11 槽位 1 云端存在");
            Check(slots != null && !slots[1].present && !slots[2].present, "S11 槽位 2/3 云端为空");
            Check(slots != null && slots[0].localPresent && slots[0].localCoins == 456,
                "S11 本地状态已并入对照（铜钱读得对）",
                slots == null ? "null" : slots[0].localCoins + "");
            Check(slots != null && slots[0].localTimestamp > 0, "S11 本地更新时间已换算成 Unix 毫秒");
            // G5 要求 UI 显示"当前字节大小与百分比占用"——体积只在 CloudSaveService 量一次，
            // 文案也从它出；这里断言的是"玩家看得见的那句话真的存在"。
            Check(slots != null && slots[0].localPayloadBytes > 0, "S11 本地档体积应被量出",
                slots == null ? "null" : slots[0].localPayloadBytes + "");
            var sizeText = cloud.PayloadSizeText(slots == null ? 0 : slots[0].localPayloadBytes);
            Check(sizeText.Contains("KB") && sizeText.Contains("%"),
                "S11 体积文案应同时给出 KB 与占上限的百分比", sizeText);
            Check(cloud.PayloadSizeText(0) == "存档 0 KB", "S11 零体积不该算出负数或炸");
            Check(cloud.PayloadUsageRatio("{\"a\":1}") > 0 && cloud.PayloadUsageRatio("{\"a\":1}") < .01,
                "S11 占用比例应在 0–1 之间且小档接近 0");

            // 回执形状不认识时（data 里没有 slots）必须给可读错误，而不是 NRE 把面板炸成空白。
            var broken = NewCloudServiceOn(NewClient(new RecordingTransport(
                ("GET", "/api/save/list", "{\"code\":0,\"message\":\"成功\",\"data\":null}"))),
                RepoWithSave(1, "空", 1));
            List<CloudSlotMetadata> brokenSlots = null;
            ApiException brokenError = null;
            try
            {
                Drive(broken.ListSlots((s, e) => { brokenSlots = s; brokenError = e; }));
            }
            catch (Exception ex)
            {
                Check(false, "S11 回执缺 slots 不应抛异常", ex.GetType().Name + ": " + ex.Message);
            }
            Check(brokenError != null && brokenSlots == null,
                "S11 回执缺 slots 应给可读错误而不是半截数据", brokenError?.Message);
        }

        static void Scenario12_UploadRequestBody()
        {
            var transport = new RecordingTransport(("POST", "/api/save/upload", RealUploadReceipt));
            var client = NewClient(transport);
            LocalCredentialManager.StoreCredentials("upload-token-1", 9, "上传侠");
            client.BindCredentials(() => LocalCredentialManager.Token,
                t => LocalCredentialManager.StoreCredentials(t ?? "", LocalCredentialManager.UserId,
                    LocalCredentialManager.Username));
            var root = NewTempDir();
            var repository = new SaveRepository(root, new Presentation.UnityJsonCodec());
            var save = repository.Create(1, "hero", "上传侠");
            save.coins = 777;
            repository.Write(save);
            var cloud = new CloudSaveService(client, () => repository);

            string updatedAt = null;
            ApiException error = null;
            Drive(cloud.UploadSlot(1, (u, e) => { updatedAt = u; error = e; }));
            Check(error == null, "S12 上传应成功", error?.Message);
            Check(updatedAt == "2026-10-05T12:09:33.69094Z", "S12 回执里的 updatedAt 应回传给 UI", updatedAt);
            Check(transport.Requests.Count == 1, "S12 一次上传只发一个请求", transport.Requests.Count + "");
            var sent = transport.Requests[0];
            Check(sent.method == "POST" && sent.url.EndsWith("/api/save/upload", StringComparison.Ordinal),
                "S12 方法与路径正确", sent.method + " " + sent.url);
            Check(sent.token == "upload-token-1", "S12 需鉴权的请求必须带 X-Auth-Token", sent.token ?? "(空)");
            // 请求体形状必须与服务端 @RequestBody Map 的期望一致：slot / schemaVersion / save 三个键。
            var parsed = Json.TryParse(sent.body, out var body, out var parseError);
            Check(parsed, "S12 请求体应是合法 JSON", parseError);
            if (parsed)
            {
                Check(body["slot"].AsInt(0) == 1, "S12 请求体 slot 正确", body["slot"].AsInt(0) + "");
                Check(body["schemaVersion"].AsInt(0) == SaveData.CurrentSchemaVersion,
                    "S12 请求体 schemaVersion 等于当前版（与服务端 SUPPORTED_SCHEMA 同批）",
                    body["schemaVersion"].AsInt(0) + "");
                Check(body["save"].kind == JsonValue.Kind.Object &&
                      body["save"]["coins"].AsInt(-1) == 777,
                    "S12 save 应是本地存档原文对象（服务端只存不读）", body["save"]["coins"].AsInt(-1) + "");
            }
            Check(!string.IsNullOrEmpty(LocalCredentialManager.LastSyncTime), "S12 成功后应记下一次同步时刻");
        }

        static void Scenario13_DownloadThreeModes()
        {
            var repository = RepoWithSave(1, "本机档", 111);
            var codec = new Presentation.UnityJsonCodec();
            // "云端那份"直接拿一份合法存档改数——不新建槽位（同槽 Create 会被档位管理器拒），
            // 也免得自己手写 SaveData 写出一份服务端根本不会给的形状。
            var cloudCopy = repository.Read(1).Data;
            cloudCopy.coins = 999;
            cloudCopy.characterName = "云端档";
            var cloudPayload = codec.Serialize(cloudCopy);
            var downloadBody = "{\"code\":0,\"message\":\"成功\",\"data\":{\"slot\":1,\"present\":true," +
                "\"schemaVersion\":" + SaveData.CurrentSchemaVersion +
                ",\"updatedAt\":\"2026-10-05T12:09:33.69094Z\",\"save\":" + cloudPayload + "}}";

            // 模式 A：云端覆盖本地同槽
            var cloud = NewCloudServiceOn(NewClient(new RecordingTransport(
                ("GET", "/api/save/download", downloadBody))), repository);
            ApiException errorA = null;
            Drive(cloud.DownloadToSlot(1, 1, e => errorA = e));
            Check(errorA == null, "S13 模式 A（云端覆盖本地）应成功", errorA?.Message);
            Check(CoinsAt(repository, 1) == 999, "S13 模式 A 之后本地槽 1 已是云端那份", CoinsAt(repository, 1) + "");

            // 模式 C：另存到空槽，两份进度互不覆盖
            var repositoryC = RepoWithSave(1, "本机档", 111);
            var cloudC = NewCloudServiceOn(NewClient(new RecordingTransport(
                ("GET", "/api/save/download", downloadBody))), repositoryC);
            ApiException errorC = null;
            Drive(cloudC.DownloadToSlot(1, 2, e => errorC = e));
            Check(errorC == null, "S13 模式 C 落到空槽应成功", errorC?.Message);
            Check(CoinsAt(repositoryC, 2) == 999 && SlotOf(repositoryC, 2) == 2,
                "S13 模式 C 写入目标槽并把 slot 改成目标号",
                CoinsAt(repositoryC, 2) + "/" + SlotOf(repositoryC, 2));
            Check(CoinsAt(repositoryC, 1) == 111, "S13 模式 C 不该动源槽位（两份进度互不覆盖）");

            // 模式 C 防误触：目标槽非空必须拒绝，且拒绝得明明白白
            // （槽 2 刚被上一步写满，所以"另存到槽 2"现在就是撞车场景）
            var cloudD = NewCloudServiceOn(NewClient(new RecordingTransport(
                ("GET", "/api/save/download", downloadBody))), repositoryC);
            ApiException errorD = null;
            Drive(cloudD.DownloadToSlot(1, 2, e => errorD = e));
            Check(errorD != null && errorD.Code == CloudErrors.LocalSlotBusy,
                "S13 另存到非空槽应被拒绝（客户端本地码 " + CloudErrors.LocalSlotBusy + "），不是静默覆盖",
                errorD?.Message);
            // 这条是本轮补的：客户端过去把本地拒绝也标 40903，与服务端 SLOT_CONFLICT 同号。
            // W3-A 一上线（并发上传真的吐 409/40903 了），同一句提示（当时是"该槽位刚被另一次上传占用"，
            // D-50② 之后已换成不含归因的说法）就会在"玩家点了两次上传"和"玩家想另存到满槽"两件事上冒出来。
            Check(errorD != null && errorD.Code != CloudErrors.SlotConflict,
                "S13 本地防误触拒绝不得占用服务端的槽位冲突码", errorD?.Code.ToString());
            Check(errorD != null && errorD.Message.Contains("防误触"),
                "S13 拒绝理由要让玩家看懂", errorD?.Message);
            Check(CoinsAt(repositoryC, 2) == 999, "S13 被拒绝之后目标槽原样未动");

            // 云端空槽：present:false 且不带 save → 可读错误，绝不凭空造槽
            var cloudE = NewCloudServiceOn(NewClient(new RecordingTransport(
                ("GET", "/api/save/download", RealDownloadEmpty))), repositoryC);
            ApiException errorE = null;
            Drive(cloudE.DownloadToSlot(3, 3, e => errorE = e));
            Check(errorE != null && errorE.Code == 40001, "S13 云端空槽应报\"没有可下载的存档\"", errorE?.Message);
            Check(repositoryC.Read(3).State == SlotState.Empty, "S13 云端空槽回执不该在本地凭空造出槽位");
        }

        static void Scenario14_CrossTimezoneVerdict()
        {
            // W-Server-Time 之后，真后端的 updatedAt 已带 UTC 偏移（Instant，Z 结尾）。
            // 但"裸时间（无偏移）判 Unknown"的防御必须保留：它挡的是一切不带时区的来源——
            // 历史存量数据、第三方回放、将来某处的格式回归。裸时间按本机时区补齐会在
            // 服务端换机房时整体翻向（用户点下载覆盖掉自己更新的进度），宁可不猜方向。
            Check(!CloudSaveService.HasExplicitOffset("2026-10-04T20:32:37.167741"),
                "S14 服务端那种裸时间应被识别为「不带时区」");
            Check(CloudSaveService.HasExplicitOffset("2026-10-04T12:32:37.167+00:00"),
                "S14 带 +00:00 的算带时区");
            Check(CloudSaveService.HasExplicitOffset("2026-10-04T12:32:37Z"), "S14 带 Z 的算带时区");
            Check(!CloudSaveService.HasExplicitOffset(""), "S14 空串不算带时区");
            Check(!CloudSaveService.HasExplicitOffset("垃圾时间"), "S14 乱码不算带时区");

            var nowUtc = DateTime.UtcNow.ToString("O");
            Check(CloudSaveService.Compare(nowUtc, "2026-10-04T20:32:37.167741") == ConflictSide.Unknown,
                "S14 云端不带时区 → Unknown（不猜方向，交人工判）");
            Check(CloudSaveService.Compare(nowUtc, DateTime.UtcNow.AddHours(-5).ToString("O"))
                    == ConflictSide.LocalNewer,
                "S14 两侧都带时区时仍能正常判本地较新");
            // 正向契约（W-Server-Time 后的服务端新格式）：云端带 Z 偏移且确实比本地新时，
            // 比对必须给出 CloudNewer，而不是退回 Unknown——否则服务端修好了客户端照样不敢判。
            Check(CloudSaveService.Compare(nowUtc, DateTime.UtcNow.AddHours(+5).ToString("O"))
                    == ConflictSide.CloudNewer,
                "S14 云端带 Z 偏移且比本地新 → 必须判 CloudNewer");
            Check(CloudSaveService.VerdictText(ConflictSide.Unknown).Contains("时区"),
                "S14 判定文案要说清为什么判不了", CloudSaveService.VerdictText(ConflictSide.Unknown));
        }

        // ---------- 场景 15：并发上传撞槽位（W3-A 的客户端那一半） ----------
        //
        // 服务端裁定是"有界重试一次，仍失败才 409/40903"；客户端这一半只有两条要求：
        // ① 不许静默——要给玩家一句人话；② 那句人话只准有一个出处，不许 UI 再抄一份。
        // 另外钉住一件事：40903 是**服务端的** SLOT_CONFLICT，客户端自己过去拿它当本地拒绝的码，
        // W3-A 一上线同一句话会在两件事上冒出来，所以本地那条挪到了 9xxxx 段。
        static void Scenario15_UploadSlotConflict()
        {
            var repository = RepoWithSave(1, "撞槽侠", 888);
            var client = NewClient(new ScriptedTransport(_ => new NetworkClient.TransportResult
            {
                HttpStatus = 409,
                Body = "{\"code\":40903,\"message\":\"槽位冲突\"}"
            }));
            LocalCredentialManager.StoreCredentials("conflict-token-1", 9, "撞槽侠");
            var cloud = NewCloudServiceOn(client, repository);

            string updatedAt = null;
            ApiException error = null;
            Drive(cloud.UploadSlot(1, (u, e) => { updatedAt = u; error = e; }));
            Check(error != null && error.Code == CloudErrors.SlotConflict,
                "S15 服务端的 409/40903 要作为槽位冲突交给调用方（不吞掉、不当成功）", error?.Message);
            Check(error != null && error.HttpStatus == 409,
                "S15 HTTP 状态一并带上（UI 兜底判断用）", error?.HttpStatus.ToString());
            Check(updatedAt == null,
                "S15 撞槽位时不许回传时间——那会被上层当成上传成功并刷新同步时间", updatedAt);
            Check(CloudErrors.TextForUpload(error) == CloudErrors.UploadConflictText,
                "S15 冲突时给玩家的那句必须是裁定原文", CloudErrors.TextForUpload(error));
            Check(CloudErrors.UploadConflictText == "这个槽位的存档没能上传，请稍后再试。",
                "S15 文案逐字钉住（D-50②：3号 定稿的非诊断说法，改它＝改对玩家的承诺）", CloudErrors.UploadConflictText);
            // D-50② 说"措辞不许承诺诊断"，光换一句新话不够——下一句写回归因也得当场红。
            // 为什么这条能机器判：40903 的生产者是服务端 DataIntegrityViolationException 的兜底
            // handler，它接到的未必是"两次上传撞进来"，所以任何替玩家断定原因的措辞都是假承诺。
            Check(!CloudErrors.UploadConflictText.Contains("占用") &&
                  !CloudErrors.UploadConflictText.Contains("另一次"),
                "S15 文案不许替玩家诊断原因（只准说\"没写进去 + 稍后再试\"这两件玩家确知的事）",
                CloudErrors.UploadConflictText);
            Check(CloudErrors.UploadConflictText.Contains("稍后再试"),
                "S15 文案要给出路：不能只报失败不给下一步", CloudErrors.UploadConflictText);
            Check(CloudErrors.TextForUpload(new ApiException(40901, "存档版本高于本服务器支持的版本", 409)) == null,
                "S15 只有槽位冲突才给这句，别的错误保留原文（体积超限被说成撞槽位更糟）");
            Check(CloudErrors.TextForUpload(null) == null, "S15 没出错时不该编一句话出来");

            var serviceSource = AssetSource("Scripts", "Network", "CloudSaveService.cs");
            var dialogSource = AssetSource("Scripts", "UI", "Auth", "CloudSaveDialog.cs");
            // D-50③：这条注释是写给将来读代码的人的——40903 是兜底路径，不是一条常见错误。
            // 判据要"实现坏了会红"：注释被删掉就让这条红一次，而不是只在报告里说一句。
            var fallbackNote = serviceSource.Contains("128") && serviceSource.Contains("兜底");
            Check(fallbackNote,
                "S15 CloudErrors 的注释里写明「H2 实测 128 次并发零命中，属兜底路径」（D-50③）",
                "注释里现读不到那句（源码 " + serviceSource.Length + " 字节）");
            Check(CodeOnly(serviceSource).Contains("ApiException(CloudErrors.LocalSlotBusy") &&
                  !CodeOnly(serviceSource).Contains("ApiException(40903"),
                "S15 客户端不再自己编造 40903（那个码归服务端）");
            Check(!dialogSource.Contains(CloudErrors.UploadConflictText) &&
                  dialogSource.Contains("CloudErrors.TextForUpload"),
                "S15 那句话只在 CloudErrors 一处，UI 是调用它而不是抄它");
        }

        /// <summary>读 Assets 下的源码做单出处核对（与 SaveChecks 的 CodeOnly 同一口径：剥行注释）。</summary>
        static string AssetSource(params string[] tail)
        {
            var parts = new List<string> { Application.dataPath };
            parts.AddRange(tail);
            var path = Path.Combine(parts.ToArray());
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        static string CodeOnly(string source) => string.Join("\n", (source ?? "").Split('\n').Select(line =>
        {
            var cut = line.IndexOf("//", StringComparison.Ordinal);
            return cut < 0 ? line : line.Substring(0, cut);
        }));

        // ---------- 场景 16：真后端的 409 信封 + 40903 处置（S15 的实测补全） ----------
        //
        // S15 用的是我自己写的假响应。这一场把它升级成两件事：
        //   ① 信封形状取自**真后端实测原文**（起 jar、打 /api/save/upload，见 h4-ratchet-report §4），
        //      证明客户端解析的是服务端真发出来的那种 409 + 业务码，不是我编的形状；
        //   ② 客户端按**码**处置，不看服务端那句 message 改没改。
        //      跨端那句文案现在**故意不一致**（D-50②）：服务端那句仍带归因，客户端那句改成非诊断——
        //      真正的不变量是"玩家只可能看到 CloudErrors 这一句"，它钉在控制台的第 23 节里。
        static void Scenario16_LiveConflictEnvelope()
        {
            // ① 真后端实测：schemaVersion 高于 SUPPORTED_SCHEMA → HTTP 409 + {"code":40901,...}
            // RecordingTransport 恒答 200，带不回状态码；这一场要验的就是"409 + 业务码"这一对，
            // 所以用能指定 HttpStatus 的 ScriptedTransport，响应体逐字取真后端实测原文。
            var live = new ScriptedTransport(_ => new NetworkClient.TransportResult
            {
                HttpStatus = 409,
                Body = "{\"code\":40901,\"message\":\"存档版本 99 高于本服务器支持的 11。\"}"
            });
            var repository = RepoWithSave(1, "实测侠", 123);
            LocalCredentialManager.StoreCredentials("live-token-1", 9, "实测侠");
            var cloud = NewCloudServiceOn(NewClient(live), repository);
            string updatedAt = null;
            ApiException error = null;
            Drive(cloud.UploadSlot(1, (u, e) => { updatedAt = u; error = e; }));
            Check(error != null && error.Code == 40901,
                "S16 真后端的 40901 信封要解析成业务码（不是当成网络错）", error?.Message);
            Check(error != null && error.HttpStatus == 409,
                "S16 HTTP 状态要带回来（客户端按码处置、按状态兜底）", error?.HttpStatus.ToString());
            Check(updatedAt == null, "S16 真后端拒绝时不许回传时间", updatedAt);
            Check(CloudErrors.TextForUpload(error) == null,
                "S16 版本过高不是撞槽位，不能套那句「撞槽位」的话", CloudErrors.TextForUpload(error));

            // ② 两侧文案是否逐字一致，改到控制台套件里查（CheckCloudConflictTexts）：
            //    副本工程只带本线白名单，GameServer 整棵树不在里面，夹具里读它是读空——
            //    而"读空"会伪装成"护栏红了一次"，其实护栏根本没跑（T081 那一族）。
            // ③ 拿服务端那句原文拼成信封喂进来：客户端仍按**码**处置，不看文案改没改
            var asEnvelope = "{\"code\":40903,\"message\":\"" + CloudErrors.UploadConflictText + "\"}";
            var race = NewCloudServiceOn(NewClient(new RecordingTransport(
                ("POST", "/api/save/upload", asEnvelope))), RepoWithSave(1, "撞槽侠", 777));
            string raceTime = null;
            ApiException raceError = null;
            Drive(race.UploadSlot(1, (u, e) => { raceTime = u; raceError = e; }));
            Check(raceError != null && raceError.Code == CloudErrors.SlotConflict,
                "S16 40903 要判成槽位冲突", raceError?.Message);
            Check(CloudErrors.TextForUpload(raceError) == CloudErrors.UploadConflictText &&
                  raceTime == null,
                "S16 撞槽位时给的就是那句裁定原文，且不回传时间（不静默当成功）",
                CloudErrors.TextForUpload(raceError));
        }

        /// <summary>读某槽位的铜钱；读不到返回 -1。断言里用它，失败时就是一条"数值不对"，
        /// 而不是把整个夹具炸成 NullReferenceException 让后面的场景一起没跑。</summary>
        static int CoinsAt(SaveRepository repository, int slot) =>
            repository.Read(slot).CanLoad ? repository.Read(slot).Data.coins : -1;

        static int SlotOf(SaveRepository repository, int slot) =>
            repository.Read(slot).CanLoad ? repository.Read(slot).Data.slot : -1;

        static CloudSaveService NewCloudServiceOn(NetworkClient client, SaveRepository repository)
        {
            var repoLocal = repository;
            return new CloudSaveService(client, () => repoLocal);
        }

        /// <summary>开一个临时档仓并在指定槽位建好一档（每个调用点都是全新的空目录，槽位不冲突）。</summary>
        static SaveRepository RepoWithSave(int slot, string characterName, int coins)
        {
            var repository = new SaveRepository(NewTempDir(), new Presentation.UnityJsonCodec());
            var save = repository.Create(slot, "hero", characterName);
            save.coins = coins;
            repository.Write(save);
            return repository;
        }

        // ---------- 基础设施 ----------

        static NetworkClient NewClient(NetworkClient.ITransport transport)
        {
            var client = new NetworkClient(NetworkConfig.Load(), transport)
            {
                SkipBackoffWaitsForTests = true,
            };
            return client;
        }

        static CloudSaveService NewCloudService(out SaveRepository repository, out IJsonCodec codec)
        {
            var root = NewTempDir();
            repository = new SaveRepository(root, new Presentation.UnityJsonCodec());
            codec = new Presentation.UnityJsonCodec();
            var repoLocal = repository;
            return new CloudSaveService(NewClient(new ScriptedTransport(_ =>
                new NetworkClient.TransportResult { HttpStatus = 200, Body = "{\"code\":0,\"message\":\"成功\"}" })),
                () => repoLocal);
        }

        static void Drive(IEnumerator coroutine)
        {
            // 嵌套协程的手动驱动：`yield return 内部枚举器` 只会把它当 Current 抛出，
            // 不推进就不会执行——必须压栈逐层 MoveNext（真机由 Unity 驱动，这里同步等价）。
            // 假传输在首次 MoveNext 内同步完成；退避等待被 SkipBackoffWaitsForTests 退化为 null。
            var stack = new Stack<IEnumerator>();
            stack.Push(coroutine);
            var guard = 0;
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                if (current.MoveNext())
                {
                    if (++guard > 100000) throw new InvalidOperationException("协程疑似死循环");
                    if (current.Current is IEnumerator nested) stack.Push(nested);
                }
                else stack.Pop();
            }
        }

        static void CheckCode(Action action, string message)
        {
            try { action(); Check(true, message); }
            catch (Exception ex) { Check(false, message, ex.Message); }
        }

        static bool SaveChecksProbe(string root)
        {
            // 用与 SaveChecks 相同的口径（SaveRepository 校验）对临时档做一次健康探查。
            var repository = new SaveRepository(root, new Presentation.UnityJsonCodec());
            return repository.ReadAll().All(info => info.State != SlotState.Corrupt);
        }

        static string NewTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "network-shot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>脚本化假传输：按 URL 决定响应，测试里代替真实后端。</summary>
        sealed class ScriptedTransport : NetworkClient.ITransport
        {
            public delegate NetworkClient.TransportResult Responder(string url);

            readonly Responder responder;

            public ScriptedTransport(Responder responder) => this.responder = responder;

            public IEnumerator Send(string method, string url, string jsonBody, float timeoutSeconds,
                string authToken, Action<NetworkClient.TransportResult> done)
            {
                done(responder(url));
                yield break;
            }
        }

        /// <summary>录制型假传输：按 URL 命中脚本里的响应，同时记下每次请求的
        /// 方法/URL/请求体/令牌——"客户端到底发出去了什么"必须能被断言，
        /// 只看响应等于只验了一半契约。</summary>
        sealed class RecordingTransport : NetworkClient.ITransport
        {
            readonly (string method, string urlSuffix, string body)[] script;

            public readonly List<(string method, string url, string body, string token)> Requests = new();

            public RecordingTransport(params (string method, string urlSuffix, string body)[] script)
            {
                this.script = script;
            }

            public IEnumerator Send(string method, string url, string jsonBody, float timeoutSeconds,
                string authToken, Action<NetworkClient.TransportResult> done)
            {
                Requests.Add((method, url, jsonBody, authToken));
                // 用 Contains 而不是 EndsWith：download 的 URL 带查询串
                // （/api/save/download?slot=1），EndsWith 匹配不到脚本里写的裸路径。
                var hit = script.FirstOrDefault(s =>
                    s.method == method && url.Contains(s.urlSuffix));
                done(hit.body == null
                    ? new NetworkClient.TransportResult { HttpStatus = 404, Body = "{\"code\":40001,\"message\":\"脚本里没有这条响应\"}" }
                    : new NetworkClient.TransportResult { HttpStatus = 200, Body = hit.body });
                yield break;
            }
        }

        sealed class MemoryStore : IKeyValueStore
        {
            readonly Dictionary<string, string> data = new Dictionary<string, string>();

            public IReadOnlyCollection<string> Keys => data.Keys;
            public IEnumerable<string> Values => data.Values;

            public string GetString(string key, string fallback) =>
                data.TryGetValue(key, out var value) ? value : fallback;

            public void SetString(string key, string value) => data[key] = value;
            public void DeleteKey(string key) => data.Remove(key);
            public bool HasKey(string key) => data.ContainsKey(key);
        }
    }
}
