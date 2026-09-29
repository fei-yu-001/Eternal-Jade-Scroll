using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // M2 行囊：24 格宣纸格（6×4）、类别页签、悬停浮签、拖拽换位、使用与丢弃。
    // 规则全在 Core 的 InventoryRules 里，这里只管画面与输入；格子尺寸与位置由常量算出，
    // 空档按空格补满 24 格——存档里也始终是 24 格。
    public sealed partial class FrontEndController
    {
        const int BagColumns = 6, BagRows = 4;
        const float BagCell = 120f, BagGap = 12f;

        ItemTable itemTable;
        GameObject bagOverlay;
        RectTransform bagGrid;
        readonly List<RectTransform> bagCells = new();
        readonly List<RawImage> bagCellIcons = new();
        readonly List<TMP_Text> bagCellCounts = new();
        readonly List<Button> bagCellButtons = new();
        TMP_Text bagHint, bagSummary, bagBagLine;
        RectTransform bagTip;
        TMP_Text bagTipTitle, bagTipBody;
        int bagTipSlot = -1;
        string bagCategory = "";
        int bagSelected = -1;
        int bagDragFrom = -1;

        ItemTable Table
        {
            get
            {
                if (itemTable != null) return itemTable;
                var asset = Resources.Load<TextAsset>("Config/items");
                if (asset == null)
                {
                    Debug.LogWarning("Missing Resources/Config/items.json; the bag falls back to an empty table.");
                    return itemTable = ItemTable.Parse(EmptyTableJson);
                }
                try { itemTable = ItemTable.Parse(asset.text); }
                catch (FormatException ex)
                {
                    Debug.LogWarning("items.json is malformed: " + ex.Message);
                    itemTable = ItemTable.Parse(EmptyTableJson);
                }
                return itemTable;
            }
        }

        // 兜底表：物品表缺失时行囊仍能打开，已有物品按 id 显示为"未登记之物"，不丢东西。
        const string EmptyTableJson =
            "{\"tiers\":[{\"id\":\"fanpin\",\"name\":\"凡品\",\"color\":\"#8C8C86\"}]," +
            "\"categories\":[{\"id\":\"qita\",\"name\":\"杂物\"}]," +
            "\"items\":[{\"id\":\"unknown-item\",\"name\":\"未登记之物\",\"tier\":\"fanpin\"," +
            "\"category\":\"qita\",\"icon\":\"missing-item\",\"stack\":99,\"price\":0," +
            "\"description\":\"这一件来历不明，像是从别处流过来的。\"}]}";

        public void ShowInventory()
        {
            if (bagOverlay != null) { bagOverlay.SetActive(true); userBag = activeSave?.bag; RedrawBag(); return; }
            if (activeSave == null) { Notify("先落笔创建一位行旅人，再打开行囊。"); return; }
            userBag = activeSave.bag;
            bagSelected = -1;
            bagCategory = "";
            if (content != null) content.GetComponent<CanvasGroup>().interactable = false;
            bagOverlay = ui.Rect(canvas, "Inventory", 0, 0, 1920, 1080).gameObject;
            var veil = bagOverlay.AddComponent<Image>();
            veil.sprite = UiKit.RoundedSprite();
            veil.type = Image.Type.Sliced;
            veil.color = new Color(0f, .035f, .03f, .72f);
            veil.raycastTarget = true;
            // 点格外的暗底才收行囊：面板自己的图片会吃掉射线，不会误触。
            var outside = bagOverlay.AddComponent<Button>();
            outside.targetGraphic = veil;
            outside.transition = Selectable.Transition.None;
            outside.onClick.AddListener(CloseInventory);
            RedrawBag();
        }

        ItemStack[] userBag;

        // 验收脚本用的最小接口：不打开面板也能查当前格数与提示格。
        public ItemStack[] CurrentBag => userBag;
        public int BagCellCount => bagCells.Count;
        public int BagTipSlot => bagTipSlot;
        public string BagCategory => bagCategory;

        public void CloseInventory()
        {
            if (bagOverlay == null) return;
            Destroy(bagOverlay);
            bagOverlay = null;
            bagGrid = null;
            bagCells.Clear();
            bagCellIcons.Clear();
            bagCellCounts.Clear();
            bagCellButtons.Clear();
            bagTip = null;
            bagTipSlot = -1;
            bagDragFrom = -1;
            if (content != null) content.GetComponent<CanvasGroup>().interactable = true;
        }

        // 页签：全部 + 物品表里定义的每一类。类别是数据，不是枚举——加一类 JSON 即可。
        void RedrawBag()
        {
            if (bagOverlay == null) return;
            foreach (var old in bagOverlay.GetComponentsInChildren<RectTransform>(true))
                if (old.gameObject != bagOverlay) Destroy(old.gameObject);
            bagCells.Clear();
            bagCellIcons.Clear();
            bagCellCounts.Clear();
            bagCellButtons.Clear();

            var box = ui.Panel(bagOverlay.transform, "Bag panel", 180, 96, 1560, 888,
                new Color(.045f, .115f, .10f, .985f), true).rectTransform;
            ui.Label(box, "行 囊", 48, 30, 400, 60, 40, UiKit.Paper, TextAlignmentOptions.TopLeft, true);
            bagSummary = ui.Label(box, "", 470, 34, 640, 34, 21, UiKit.Gold, TextAlignmentOptions.MidlineLeft);
            bagBagLine = ui.Label(box, "", 470, 68, 640, 30, 19, UiKit.Paper, TextAlignmentOptions.MidlineLeft);
            ui.Label(box, "左键选中 · 拖拽换位 · 双击使用", 470, 100, 640, 28, 17, UiKit.Muted);
            ui.Button(box, "整理", 1140, 42, 175, 58, BagSort);
            ui.Button(box, "合上行囊", 1335, 42, 185, 58, CloseInventory, true);

            var tabs = new List<(string id, string name)> { ("", "全部") };
            foreach (var category in Table.Categories) tabs.Add((category.Id, category.Name));
            for (var i = 0; i < tabs.Count; i++)
            {
                var (id, name) = tabs[i];
                ui.Button(box, name, 48 + i * 168, 132, 156, 54, () => { bagCategory = id; bagSelected = -1; RedrawBag(); },
                    bagCategory == id);
            }

            bagGrid = ui.Rect(box, "Bag grid", 48, 188, BagColumns * BagCell + (BagColumns - 1) * BagGap,
                BagRows * BagCell + (BagRows - 1) * BagGap);
            for (var i = 0; i < InventoryRules.SlotCount; i++)
            {
                var column = i % BagColumns;
                var row = i / BagColumns;
                var cell = ui.Rect(bagGrid, "Cell " + i, column * (BagCell + BagGap), row * (BagCell + BagGap), BagCell, BagCell);
                var background = cell.gameObject.AddComponent<Image>();
                background.sprite = UiKit.RoundedSprite();
                background.type = Image.Type.Sliced;
                background.color = new Color(.93f, .90f, .82f, .96f);
                background.raycastTarget = true;
                var index = i;
                var button = cell.gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => BagClick(index));
                var trigger = cell.gameObject.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => BagHover(index));
                trigger.triggers.Add(enter);
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => { if (bagTipSlot == index) HideTip(); });
                trigger.triggers.Add(exit);
                // 拖拽换位：按下记住来源，松开在目标格落位。
                var drag = cell.gameObject.AddComponent<BagDragHandler>();
                drag.Init(this, index);

                ui.Label(cell, (i + 1).ToString("00"), 8, 6, 60, 26, 14, new Color(.32f, .34f, .3f, .32f));
                var icon = new GameObject("Icon", typeof(RectTransform)).GetComponent<RectTransform>();
                icon.SetParent(cell, false);
                icon.anchorMin = icon.anchorMax = new Vector2(0, 1);
                icon.pivot = new Vector2(0, 1);
                icon.anchoredPosition = new Vector2(13, -16);
                icon.sizeDelta = new Vector2(BagCell - 26, BagCell - 48);
                var iconImage = icon.gameObject.AddComponent<RawImage>();
                iconImage.raycastTarget = false;
                iconImage.color = new Color(1, 1, 1, 0);
                var count = ui.Label(cell, "", BagCell - 58, BagCell - 62, 44, 34, 21, UiKit.Ink,
                    TextAlignmentOptions.BottomRight, true);
                bagCells.Add(cell);
                bagCellIcons.Add(iconImage);
                bagCellCounts.Add(count);
                bagCellButtons.Add(button);
            }

            bagHint = ui.Label(box, "点一件物品看看它的来历。挖到的、买来的、别人塞给你的，都在这一格里。",
                48, 728, 1364, 34, 20, UiKit.Muted);
            ui.Label(box, "行囊之外的东西，好汉也未必留得住——财不外露。", 48, 770, 1364, 32, 18, UiKit.Muted);
            BuildTip(box);
            RefreshBag();
        }

        // 说明面板常驻：没有选中物品时写"怎么用"，选中后换成那件东西的来历。
        // 早前是"常驻层 + 浮签层"两块面板互相切换显隐，两层只要有一处没恢复就留下空白；
        // 改为单面板换文字后，不再有互相遮盖的可能。
        void BuildTip(RectTransform parent)
        {
            bagTip = ui.Panel(parent, "Item tip", 930, 188, 570, 494, new Color(.02f, .07f, .06f, .55f), true).rectTransform;
            bagTipTitle = ui.Label(bagTip, "", 30, 30, 510, 50, 30, UiKit.Muted, TextAlignmentOptions.TopLeft, true);
            bagTipBody = ui.Label(bagTip, "", 30, 100, 510, 250, 22, UiKit.Muted);
            var tierLabel = ui.Label(bagTip, "", 30, 428, 510, 40, 21, UiKit.Muted, TextAlignmentOptions.MidlineLeft);
            tierLabel.name = "Tip tier";
            ResetTipText();
        }

        void ResetTipText()
        {
            bagTipTitle.text = "行 囊 里 有 什 么";
            bagTipTitle.color = UiKit.Muted;
            bagTipBody.text = "悬停或点选一件物品，这里会写明它的来历。";
            var tierText = bagTip.Find("Tip tier")?.GetComponent<TMP_Text>();
            if (tierText != null) { tierText.text = "左键选中 · 拖拽换位 · 双击使用"; tierText.color = UiKit.Muted; }
        }

        public void ShowTip(int index)
        {
            if (bagTip == null || userBag == null) return;
            var stack = index >= 0 && index < userBag.Length ? userBag[index] : null;
            if (stack == null || stack.IsEmpty) { HideTip(); return; }
            var def = Table.Find(stack.id);
            bagTipSlot = index;
            var tier = def == null ? null : Table.FindTier(def.Tier);
            var category = def == null ? null : Table.FindCategory(def.Category);
            bagTipTitle.text = def?.Name ?? "未登记之物";
            bagTipTitle.color = tier == null ? UiKit.Paper : UiKit.Hex(tier.Color.TrimStart('#'));
            bagTipBody.text = (def?.Description ?? "这一件来历不明，像是从别处流过来的。") +
                "\n\n类别：" + (category?.Name ?? "杂物") +
                "\n数量：" + stack.count + (def != null && def.Stack > 1 ? " / " + def.Stack : "") +
                (def != null && def.Price > 0 ? "\n市价：约 " + def.Price + " 文" : "");
            var tierText = bagTip.Find("Tip tier")?.GetComponent<TMP_Text>();
            if (tierText != null)
            {
                tierText.text = "品阶 · " + (tier?.Name ?? "未定");
                if (tier != null) tierText.color = UiKit.Hex(tier.Color.TrimStart('#'));
            }
        }

        public void HideTip()
        {
            bagTipSlot = -1;
            if (bagTip != null) ResetTipText();
        }

        bool BagFilterAllows(ItemDef def)
        {
            if (string.IsNullOrEmpty(bagCategory)) return true;
            if (def == null) return bagCategory == "qita";
            return def.Category == bagCategory;
        }

        void RefreshBag()
        {
            if (userBag == null) return;
            for (var i = 0; i < bagCells.Count; i++)
            {
                var stack = userBag[i];
                var def = stack == null || stack.IsEmpty ? null : Table.Find(stack.id);
                var visible = stack != null && !stack.IsEmpty && BagFilterAllows(def);
                var icon = bagCellIcons[i];
                var texture = def == null || !visible ? null : Resources.Load<Texture2D>("Art/Items/" + def.Icon);
                icon.texture = texture;
                icon.color = new Color(1, 1, 1, texture != null ? 1f : 0f);
                bagCellCounts[i].text = visible && stack.count > 1 ? stack.count.ToString() : "";
                var background = bagCells[i].GetComponent<Image>();
                var selected = bagSelected == i;
                background.color = !visible ? new Color(.88f, .86f, .79f, .55f)
                    : selected ? new Color(1f, .95f, .8f, 1f)
                    : new Color(.93f, .90f, .82f, .96f);
            }
            var used = InventoryRules.UsedSlots(userBag);
            var total = userBag.Sum(stack => stack == null ? 0 : stack.count);
            if (bagSummary != null)
                bagSummary.text = "铜钱 " + activeSave.coins + " 文　地方声望 " + activeSave.localReputation +
                    "（买卖让利 " + Mathf.RoundToInt(Reputation.Discount(activeSave.localReputation) * 100) + "%）";
            if (bagBagLine != null)
                bagBagLine.text = "第 " + activeSave.slot + " 卷 · " + activeSave.characterName +
                    "　行囊 " + used + " / " + InventoryRules.SlotCount + " 格 · 共 " + total + " 件";
            if (bagHint != null && bagSelected >= 0 && bagSelected < bagCells.Count)
            {
                var stack = userBag[bagSelected];
                var def = stack == null ? null : Table.Find(stack.id);
                bagHint.text = stack == null || stack.IsEmpty ? "点一件物品看看它的来历。"
                    : "已选中「" + (def?.Name ?? "未登记之物") + "」——把它拖到别的格子里，或双击使用。";
            }
        }

        void BagHover(int index)
        {
            // 浮签延迟 150ms 弹出，避免鼠标扫过时一路刮蹭。
            CancelInvoke(nameof(ShowTipNow));
            bagTipSlot = index;
            Invoke(nameof(ShowTipNow), .15f);
        }

        void ShowTipNow()
        {
            if (bagTipSlot < 0 || bagTipSlot >= bagCells.Count) return;
            if (!bagCells[bagTipSlot].gameObject.activeInHierarchy) return;
            ShowTip(bagTipSlot);
        }

        public void BagClick(int index)
        {
            if (userBag == null) return;
            bagSelected = bagSelected == index ? -1 : index;
            if (index >= 0 && index < userBag.Length && !userBag[index].IsEmpty) ShowTip(index);
            else HideTip();
            RefreshBag();
        }

        // 双击 = 使用：干粮与伤药是真会用掉的，其余物品只给一句"还用不上"。
        public void BagUse(int index)
        {
            if (userBag == null || activeSave == null) return;
            if (index < 0 || index >= userBag.Length) return;
            var stack = userBag[index];
            if (stack == null || stack.IsEmpty) return;
            var def = Table.Find(stack.id);
            if (def == null) { Notify("这一件来历不明，还看不出用法。"); return; }
            if (def.Category == "renwu")
            {
                Notify("「" + def.Name + "」往后有大用，此刻还动不得。");
                return;
            }
            if (def.Category != "chishi" && def.Category != "yaodan")
            {
                Notify("「" + def.Name + "」不是入口的东西。");
                return;
            }
            InventoryRules.Remove(userBag, stack.id, 1);
            activeSave.bag = userBag;
            PersistBag();
            Notify("用了 1 件「" + def.Name + "」。");
            RefreshBag();
            HideTip();
        }

        public void BagSort()
        {
            if (userBag == null) return;
            InventoryRules.Sort(userBag, Table);
            activeSave.bag = userBag;
            PersistBag();
            bagSelected = -1;
            RefreshBag();
            Notify("行囊已整理。");
        }

        public void BagDrop(int from, int to)
        {
            if (userBag == null || from < 0 || to < 0 || from == to) return;
            if (InventoryRules.TryMove(userBag, Table, from, to))
            {
                activeSave.bag = userBag;
                PersistBag();
                bagSelected = to;
            }
            RefreshBag();
        }

        void PersistBag()
        {
            Guard(() => repository.Write(activeSave));
        }

        // 拖拽：用 IDragHandler 记录来源，用 IDropHandler 在目标格落位。
        sealed class BagDragHandler : MonoBehaviour, IBeginDragHandler, IDropHandler, IPointerClickHandler
        {
            FrontEndController owner;
            int index;
            public void Init(FrontEndController controller, int slot) { owner = controller; index = slot; }

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (owner.userBag == null || index >= owner.userBag.Length) return;
                owner.bagDragFrom = owner.userBag[index].IsEmpty ? -1 : index;
            }

            public void OnDrop(PointerEventData eventData)
            {
                if (owner.bagDragFrom < 0) return;
                owner.BagDrop(owner.bagDragFrom, index);
                owner.bagDragFrom = -1;
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.clickCount >= 2) owner.BagUse(index);
            }
        }
    }
}
