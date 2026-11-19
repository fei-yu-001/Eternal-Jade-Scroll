using System;
using System.Collections;
using System.Text;
using TMPro;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Network.UI
{
    /// <summary>
    /// 云存档管理中心（M6-03-C · M8）：三槽位卡片对比本地/云端状态，
    /// 提供上传（模式 B）、下载覆盖（模式 A）、另存备用槽（模式 C）三个入口。
    /// 覆盖类操作一律二次点击确认（防误触）；网络期间以 LoadingSpinner 阻断。
    /// </summary>
    public static class CloudSaveDialog
    {
        static RectTransform openDialog;

        public static bool IsOpen => openDialog != null;

        public static void Open()
        {
            if (openDialog != null) Close();
            var kit = NetworkUi.Kit;
            var canvas = NetworkUi.OverlayCanvas();

            openDialog = kit.Rect(canvas.transform, "CloudSaveDialog", 0, 0, 1920, 1080);
            openDialog.anchorMin = Vector2.zero;
            openDialog.anchorMax = Vector2.one;
            openDialog.offsetMin = Vector2.zero;
            openDialog.offsetMax = Vector2.zero;

            var backdrop = openDialog.gameObject.AddComponent<Image>();
            backdrop.sprite = UiKit.RoundedSprite();
            backdrop.type = Image.Type.Sliced;
            backdrop.color = new Color(0f, .04f, .03f, .86f);
            backdrop.raycastTarget = true;

            kit.Label(openDialog, "云端存档 · 三槽对照", 560, 110, 800, 50, 32, UiKit.Gold,
                TextAlignmentOptions.Center, true);
            kit.Label(openDialog, "本地较新建议上传 · 云端较新建议下载 · 方向拿不准请先看清楚再动手",
                460, 168, 1000, 36, 20, UiKit.Muted, TextAlignmentOptions.Center);

            for (var slot = 1; slot <= 3; slot++)
                BuildSlotCard(kit, slot, 200 + (slot - 1) * 530);

            kit.Button(openDialog, "刷 新", 860, 850, 200, 56, Refresh);
            kit.Button(openDialog, "告 辞", 1250, 850, 160, 56, Close);
            Refresh();
        }

        public static void Close()
        {
            if (openDialog == null) return;
            var dying = openDialog.gameObject;
            openDialog = null;
            UnityEngine.Object.Destroy(dying);
        }

        static void BuildSlotCard(UiKit kit, int slot, float x)
        {
            var card = kit.Panel(openDialog, "SlotCard " + slot, x, 250, 500, 540,
                new Color(.045f, .115f, .10f, .97f), true);
            kit.Label(card.transform, "槽位 " + slot, 24, 16, 300, 44, 28, UiKit.Gold,
                TextAlignmentOptions.MidlineLeft, true);
            var localLabel = kit.Label(card.transform, "本地：…", 24, 70, 452, 130, 21, UiKit.Paper,
                TextAlignmentOptions.TopLeft);
            localLabel.gameObject.name = "SlotLocalText " + slot;
            var cloudLabel = kit.Label(card.transform, "云端：…", 24, 210, 452, 130, 21, UiKit.Muted,
                TextAlignmentOptions.TopLeft);
            cloudLabel.gameObject.name = "SlotCloudText " + slot;
            var verdictLabel = kit.Label(card.transform, "", 24, 350, 452, 40, 22, UiKit.Gold,
                TextAlignmentOptions.MidlineLeft);
            verdictLabel.gameObject.name = "SlotVerdictText " + slot;

            var uploadButton = kit.Button(card.transform, "上传覆盖云端", 24, 402, 220, 54, null);
            var downloadButton = kit.Button(card.transform, "下载覆盖本地", 268, 402, 208, 54, null);
            var spareButton = kit.Button(card.transform, "另存备用槽", 24, 466, 220, 50, null);

            Arm(uploadButton, () => UploadSlot(slot, verdictLabel));
            Arm(downloadButton, () => DownloadSlot(slot, slot, verdictLabel));
            Arm(spareButton, () => DownloadSlot(slot, AlternateSlot(slot), verdictLabel));
        }

        static UnityEngine.UI.Button Arm(UnityEngine.UI.Button button, Action action)
        {
            button.onClick.AddListener(() =>
            {
                var confirm = button.GetComponent<ConfirmOnClick>();
                if (confirm == null)
                {
                    confirm = button.gameObject.AddComponent<ConfirmOnClick>();
                    return; // 第一次点击：只武装确认态（按钮文字不改动，卡片上的判定文案会提示）
                }
                UnityEngine.Object.Destroy(confirm);
                action();
            });
            return button;
        }

        /// <summary>二次点击确认：第一次点击武装，5 秒内不再点自动解除（防误触）。</summary>
        sealed class ConfirmOnClick : MonoBehaviour
        {
            float armedAt;

            void OnEnable() => armedAt = Time.unscaledTime;
            void Update()
            {
                if (Time.unscaledTime - armedAt > 5f) Destroy(this);
            }
        }

        static int AlternateSlot(int sourceSlot) => sourceSlot == 1 ? 2 : 1;

        static void Refresh()
        {
            var runtime = NetworkRuntime.Instance;
            if (runtime == null) { ToastNotice.Show("网络运行时未就绪"); return; }
            LoadingSpinner.Show("正在拉取云端槽位…");
            NetworkUi.Host.StartCoroutine(RefreshCards(runtime.Cloud, () => LoadingSpinner.Hide()));
        }

        static IEnumerator RefreshCards(CloudSaveService cloud, Action onDone)
        {
            var slots = default(System.Collections.Generic.List<CloudSlotMetadata>);
            ApiException error = null;
            yield return cloud.ListSlots((list, err) => { slots = list; error = err; });
            LoadingSpinner.Hide();
            if (openDialog == null) yield break;
            if (error != null)
            {
                ToastNotice.Show("拉取云端槽位失败：" + error.Message);
                yield break;
            }
            foreach (var meta in slots)
            {
                var localText = FindChildText("SlotLocalText " + meta.slot);
                var cloudText = FindChildText("SlotCloudText " + meta.slot);
                var verdictText = FindChildText("SlotVerdictText " + meta.slot);
                if (localText == null) continue;

                var localLine = meta.localPresent
                    ? $"本地：{meta.localCharacterName} · 铜钱 {meta.localCoins} · 修改于 {ShortTime(UnixMsToIso(meta.localTimestamp))}\n" +
                      cloud.PayloadSizeText(meta.localPayloadBytes)
                    : "本地：空槽位";
                localText.text = localLine;
                cloudText.text = meta.present
                    ? $"云端：已存在 · schema v{meta.schemaVersion} · 同步于 {ShortTime(meta.updatedAt)}"
                    : "云端：空槽位";
                if (verdictText != null)
                {
                    var side = meta.localPresent && meta.present
                        ? CloudSaveService.Compare(CountedLocalTime(meta), meta.updatedAt)
                        : ConflictSide.Unknown;
                    verdictText.text = meta.localPresent && meta.present
                        ? CloudSaveService.VerdictText(side)
                        : meta.localPresent ? "仅本地有档（可上传）"
                        : meta.present ? "仅云端有档（可下载）" : "两侧均为空";
                }
            }
        }

        static string CountedLocalTime(CloudSlotMetadata meta) =>
            UnixMsToIso(meta.localTimestamp);

        static string UnixMsToIso(long unixMs) =>
            unixMs <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToString("O");

        static string ShortTime(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "未知";
            return iso.Length > 16 ? iso.Substring(0, 16).Replace('T', ' ') : iso;
        }

        static TMPro.TMP_Text FindChildText(string name)
        {
            if (openDialog == null) return null;
            foreach (var text in openDialog.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (text.gameObject.name == name) return text;
            return null;
        }

        static void UploadSlot(int slot, TMPro.TMP_Text verdict)
        {
            var runtime = NetworkRuntime.Instance;
            if (runtime == null) { ToastNotice.Show("网络运行时未就绪"); return; }
            LoadingSpinner.Show("正在上传槽位 " + slot + "…");
            NetworkUi.Host.StartCoroutine(UploadFlow(runtime.Cloud, slot, verdict));
        }

        static IEnumerator UploadFlow(CloudSaveService cloud, int slot, TMPro.TMP_Text verdict)
        {
            string updatedAt = null;
            ApiException error = null;
            yield return cloud.UploadSlot(slot, (time, err) => { updatedAt = time; error = err; });
            LoadingSpinner.Hide();
            if (openDialog == null) yield break;
            if (error != null)
            {
                // 槽位冲突（服务端 40903）要说人话，且这句只有一个出处（CloudErrors）。
                // 其余错误照旧带原文——那里面往往有玩家能自救的信息（体积超限、令牌过期）。
                var conflict = CloudErrors.TextForUpload(error);
                if (conflict != null)
                {
                    UnityEngine.Debug.Log("[cloud] 上传撞槽位冲突：" + error.Message);
                    ToastNotice.Show(conflict);
                    yield break;
                }
                ToastNotice.Show("上传失败：" + error.Message);
                yield break;
            }
            LocalCredentialManager.UpdateLastSyncTime();
            ToastNotice.Show("槽位 " + slot + " 上传成功");
            if (verdict != null) verdict.text = "已上传（" + ShortTime(updatedAt) + "）";
            Refresh();
        }

        static void DownloadSlot(int sourceSlot, int targetSlot, TMPro.TMP_Text verdict)
        {
            var runtime = NetworkRuntime.Instance;
            if (runtime == null) { ToastNotice.Show("网络运行时未就绪"); return; }
            LoadingSpinner.Show("正在下载槽位 " + sourceSlot + "…");
            NetworkUi.Host.StartCoroutine(DownloadFlow(runtime.Cloud, sourceSlot, targetSlot, verdict));
        }

        static IEnumerator DownloadFlow(CloudSaveService cloud, int sourceSlot, int targetSlot,
            TMPro.TMP_Text verdict)
        {
            ApiException error = null;
            yield return cloud.DownloadToSlot(sourceSlot, targetSlot, err => error = err);
            LoadingSpinner.Hide();
            if (openDialog == null) yield break;
            if (error != null)
            {
                ToastNotice.Show("下载失败：" + error.Message);
                yield break;
            }
            ToastNotice.Show(targetSlot == sourceSlot
                ? "槽位 " + targetSlot + " 已被云端覆盖"
                : "云端槽位 " + sourceSlot + " 已另存到本地槽位 " + targetSlot);
            if (verdict != null) verdict.text = "已下载至本地槽位 " + targetSlot;
            Refresh();
        }
    }
}
