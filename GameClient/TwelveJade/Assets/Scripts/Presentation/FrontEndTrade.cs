using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // M3 交易：与货郎搭话后进入。买卖双栏，中间铜钱结算。
    // 价格、存货、记忆全在 Core 的 Trade / MerchantLedger 里算完，UI 只负责把结果显示出来。
    public sealed partial class FrontEndController
    {
        MerchantTable merchants;
        string tradeMerchantId = "";
        bool tradeBlack;
        string tradePick = "";        // 当前选中的条目（item id）
        int tradeQuantity = 1;
        RectTransform tradeSettle, tradeTipPanel;
        TMP_Text tradeSettleText, tradeCoinLine, tradeMemoryLine, tradeNotice;
        readonly List<TradeRow> tradeShelf = new();
        readonly List<TradeRow> tradeSack = new();
        RawImage tradeCoin;
        Vector2 tradeCoinFrom, tradeCoinTo;
        float tradeCoinStart = -1f;

        sealed class TradeRow
        {
            public string ItemId = "";
            public RectTransform Rect;
            public Image Background;
            public TMP_Text Name, Detail, Price;
        }

        MerchantTable Merchants
        {
            get
            {
                if (merchants != null) return merchants;
                var asset = Resources.Load<TextAsset>("Config/merchants");
                if (asset == null)
                {
                    Debug.LogWarning("Missing Resources/Config/merchants.json; trade is unavailable.");
                    return merchants = MerchantTable.Parse(EmptyMerchantTable);
                }
                try { merchants = MerchantTable.Parse(asset.text); }
                catch (FormatException ex)
                {
                    Debug.LogWarning("merchants.json is malformed: " + ex.Message);
                    merchants = MerchantTable.Parse(EmptyMerchantTable);
                }
                return merchants;
            }
        }

        // 兜底表：配置缺失或损坏时也要能打开交易页。记忆行必须是非空短句——
        // MerchantTable.Parse 会拒绝空对白，兜底表自己过不了自己的校验就等于没兜底。
        const string EmptyMerchantTable =
            "{\"merchants\":[{\"id\":\"none\",\"name\":\"货郎\",\"title\":\"走南闯北的散商\",\"greeting\":\"今日只看货，不谈旧事。\"," +
            "\"buys\":[\"chishi\"],\"stock\":[{\"itemId\":\"ganliang\",\"count\":1}],\"hidden\":[]," +
            "\"memory\":[{\"at\":0,\"line\":\"今日只看货，不谈旧事。\"}]}]}";

        public string TradeMerchantId => tradeMerchantId;
        public int TradeRowCount => tradeShelf.Count + tradeSack.Count;
        public string TradeNotice => tradeNotice != null ? tradeNotice.text : "";
        public string TradeMemoryLine => tradeMemoryLine != null ? tradeMemoryLine.text : "";
        // 验收用：货架行序（首/末行）、当前选中的 id、结算区标题、黑市开关。
        public IReadOnlyList<string> ShelfOrder => tradeShelf.Select(r => r.ItemId).ToList();
        public string TradePick => tradePick;
        public bool TradeOnBlackMarket => tradeBlack;
        public void SetBlackMarketForCheck(bool black) => SwitchShelf(black);
        // 验收钩子：临时换命格，验证黑市页签的可见性门槛是确定的，不靠掷出来的命格。
        public void SetDestinyForCheck(string destiny)
        {
            activeSave.destiny = destiny;
            ShowTrade(tradeMerchantId, tradeBlack);
        }
        public bool BlackTabVisible() => Trade.SeesBlackMarket(activeSave);
        // 选中物品的显示名：直接从物品表取，比去切结算区的多行文本可靠。
        public string SettleTitle => Table.Find(tradePick)?.Name ?? "";
        public string SelfPriceOf(int index) => tradeShelf[index].Price.text;
        public string NameOf(string itemId) => Table.Find(itemId)?.Name ?? "";
        public int MerchantStockLeft(string itemId)
        {
            var merchant = Merchants.Find(tradeMerchantId);
            var state = activeSave == null ? null : MerchantLedger.Of(activeSave, merchant);
            return state == null ? 0 : MerchantLedger.Left(state, itemId);
        }
        public int CurrentBagCount(string itemId) =>
            activeSave == null ? 0 : InventoryRules.Count(activeSave.bag, itemId);
        // 验收脚本用的小动作：给一件货、把钱调到指定数。游戏逻辑本身不这么调。
        public void GiveItem(string itemId, int count) =>
            InventoryRules.Add(activeSave.bag, Table, itemId, count);
        public void SetCoinsForCheck(int coins) => activeSave.coins = coins;
        public int TradeCoins => activeSave?.coins ?? 0;
        public int TradeTrades => activeSave == null ? 0 :
            MerchantLedger.Of(activeSave, Merchants.Find(tradeMerchantId))?.trades ?? 0;

        // 搭话：先见一面，认个脸，报个到；之后买卖都从这里进。
        public void TalkTo(string npcId)
        {
            if (activeSave == null) { Notify("先落笔创建一位行旅人，再与人搭话。"); return; }
            var npc = town?.Npcs.FirstOrDefault(n => n.Id == npcId);
            if (npc == null) return;
            var merchant = string.IsNullOrEmpty(npc.Merchant) ? (MerchantDef)null : Merchants.Find(npc.Merchant);
            var state = merchant == null ? null : MerchantLedger.Of(activeSave, merchant);
            var memory = merchant == null ? "" : merchant.MemoryLine(state.trades);
            CloseModal();
            content.GetComponent<CanvasGroup>().interactable = false;
            modal = ui.Panel(canvas, "Talk", 0, 0, 1920, 1080, new Color(0f, .035f, .03f, .82f), true).rectTransform;
            var box = ui.Panel(modal, "Talk box", 470, 330, 980, 420, new Color(.045f, .115f, .10f, .97f), true).rectTransform;
            ui.Label(box, npc.Name, 44, 36, 600, 52, 34, UiKit.Paper, TextAlignmentOptions.TopLeft, true);
            ui.Label(box, npc.Line, 44, 104, 890, 90, 24, UiKit.Paper);
            if (!string.IsNullOrEmpty(memory))
                ui.Label(box, "他记得你：" + memory, 44, 196, 890, 90, 22, UiKit.Gold);
            ui.Button(box, "告 辞", 44, 320, 280, 62, CloseModal);
            if (merchant != null)
            {
                var id = merchant.Id;
                ui.Button(box, "做 买 卖", 656, 320, 280, 62, () => ShowTrade(id), true);
            }
        }

        public void ShowTrade(string merchantId, bool black = false)
        {
            var merchant = Merchants.Find(merchantId);
            if (activeSave == null) { Notify("先落笔创建一位行旅人。"); return; }
            if (merchant == null) { Notify("这位不作成买卖。"); return; }
            CloseInventory();
            if (bagOverlay != null) CloseInventory();
            tradeMerchantId = merchantId;
            // black 由调用方决定：白日铺传 false，页签切换传当前档，否则一进来就被打回白日。
            tradeBlack = black;
            tradePick = "";
            tradeQuantity = 1;
            var state = MerchantLedger.Of(activeSave, merchant);
            BeginPage("trade");
            CloseModal();

            ui.Panel(content, "Trade veil", 0, 0, 1920, 1080, new Color(.91f, .88f, .79f, .98f));
            ui.Label(content, merchant.Name, 100, 44, 700, 56, 38, UiKit.Ink, TextAlignmentOptions.TopLeft, true);
            ui.Label(content, merchant.Title, 104, 104, 700, 30, 20, new Color(.28f, .32f, .29f, .9f));
            tradeCoinLine = ui.Label(content, "", 820, 54, 780, 40, 22, UiKit.Ink, TextAlignmentOptions.MidlineRight);
            ui.Button(content, "回到青石镇", 1620, 44, 200, 54, ShowTown, true);

            // 页签：白日铺 / 黑市（命格才看得见）。黑市加价四成，独门货只在黑市。
            ui.Button(content, "白日铺", 100, 150, 180, 54, () => SwitchShelf(false), !tradeBlack);
            var blackVisible = Trade.SeesBlackMarket(activeSave);
            ui.Button(content, "黑 市", 296, 150, 180, 54, () => SwitchShelf(true), tradeBlack, blackVisible);
            if (blackVisible && tradeBlack)
                ui.Label(content, "黑市：价钱贵四成，独门货只在夜里出。", 496, 160, 700, 34, 19,
                    new Color(.55f, .2f, .16f, .95f));

            tradeShelf.Clear();
            tradeSack.Clear();
            var left = TradeShelf(100, 232, "他 的 货");
            var right = TradeSack(1050, 232, "你 的 行 囊");
            tradeMemoryLine = ui.Label(content, "", 100, 900, 1300, 40, 21, UiKit.Gold);
            tradeNotice = ui.Label(content, "", 100, 946, 1300, 40, 21, new Color(.4f, .3f, .18f, .95f));
            BuildSettle(780, 232, merchant, state);
            RefreshTrade();
        }

        void SwitchShelf(bool black)
        {
            tradePick = "";
            tradeQuantity = 1;
            ShowTrade(tradeMerchantId, black);
        }

        // 左栏：他手上的货。
        RectTransform TradeShelf(float x, float y, string title)
        {
            var panel = ui.Panel(content, "Shelf", x, y, 640, 600, new Color(.93f, .90f, .82f, .96f), true).rectTransform;
            ui.Label(panel, title, 26, 18, 400, 36, 22, UiKit.Ink, TextAlignmentOptions.TopLeft, true);
            var merchant = Merchants.Find(tradeMerchantId);
            var state = activeSave == null ? null : MerchantLedger.Of(activeSave, merchant);
            var list = tradeBlack ? merchant.Hidden : merchant.Stock;
            var order = 0;
            foreach (var line in list)
            {
                // 先把 id 存进局部变量：回调只认这个 string，不再回头读货架或循环变量。
                var itemId = line.itemId;
                var item = Table.Find(itemId);
                if (item == null) continue;
                var left = MerchantLedger.Left(state, tradeBlack ? state.hidden : state.stock, itemId);
                if (left <= 0 && !tradeBlack) continue;
                // P1：显示价必须与结算价同一个表达式，黑市才不会出现"标价 380、实收 532"。
                var shown = tradeBlack ? Trade.BlackPrice(item) : Trade.BuyPrice(item, activeSave.localReputation);
                var row = MakeRow(panel, order, itemId, item, shown + " 文/件", "余 " + left,
                    () => PickTrade(itemId, 1));
                tradeShelf.Add(row);
                order++;
            }
            return panel;
        }

        // 右栏：他能收的（任务物与不收的类别不出现）。
        RectTransform TradeSack(float x, float y, string title)
        {
            var panel = ui.Panel(content, "Sack", x, y, 640, 600, new Color(.93f, .90f, .82f, .96f), true).rectTransform;
            ui.Label(panel, title, 26, 18, 400, 36, 22, UiKit.Ink, TextAlignmentOptions.TopLeft, true);
            var merchant = Merchants.Find(tradeMerchantId);
            var rows = 0;
            for (var i = 0; i < activeSave.bag.Length; i++)
            {
                var stack = activeSave.bag[i];
                if (stack.IsEmpty) continue;
                var item = Table.Find(stack.id);
                if (item == null) continue;
                var sellable = Trade.CanSell(item, merchant);
                var price = sellable ? Trade.SellPrice(item, activeSave.localReputation) + " 文/件" : "不收";
                var id = stack.id;
                var row = MakeRow(panel, rows, id, item, price, "有 " + stack.count,
                    () => { if (sellable) PickTrade(id, -1); else TradeSay("「" + item.Name + "」他不收。"); });
                row.Background.color = sellable
                    ? new Color(.93f, .90f, .82f, .96f) : new Color(.88f, .86f, .79f, .6f);
                tradeSack.Add(row);
                rows++;
            }
            return panel;
        }

        TradeRow MakeRow(RectTransform parent, int order, string itemId, ItemDef item, string price, string note, Action onClick)
        {
            var rect = ui.Rect(parent, "Row " + itemId, 20, 66 + order * 62, 600, 56);
            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = UiKit.RoundedSprite();
            background.type = Image.Type.Sliced;
            background.color = new Color(.98f, .96f, .9f, 1f);
            background.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.gameObject.name = "Shelf row " + order;
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            var name = ui.Label(rect, item.Name, 14, 4, 260, 30, 22, UiKit.Ink, TextAlignmentOptions.MidlineLeft, true);
            var tier = Table.FindTier(item.Tier);
            if (tier != null) name.color = UiKit.Hex(tier.Color.TrimStart('#'));
            var icon = Resources.Load<Texture2D>("Art/Items/" + item.Icon);
            var art = new GameObject("Icon", typeof(RectTransform)).GetComponent<RectTransform>();
            art.SetParent(rect, false);
            art.anchorMin = art.anchorMax = new Vector2(0, 1);
            art.pivot = new Vector2(0, 1);
            art.anchoredPosition = new Vector2(276, 4);
            art.sizeDelta = new Vector2(48, 48);
            var artImage = art.gameObject.AddComponent<RawImage>();
            artImage.texture = icon;
            artImage.raycastTarget = false;
            if (icon == null) artImage.color = new Color(1, 1, 1, 0);
            button.onClick.AddListener(() => onClick());
            return new TradeRow
            {
                ItemId = itemId,
                Rect = rect,
                Background = background,
                Name = name,
                Price = ui.Label(rect, price, 330, 4, 140, 30, 19, UiKit.Ink, TextAlignmentOptions.MidlineLeft),
                Detail = ui.Label(rect, note, 470, 4, 116, 30, 18, new Color(.35f, .38f, .34f, .95f), TextAlignmentOptions.MidlineRight),
            };
        }

        // 中间：铜钱结算区。
        void BuildSettle(float x, float y, MerchantDef merchant, MerchantState state)
        {
            var panel = ui.Panel(content, "Settle", x, y, 260, 600, new Color(.045f, .115f, .10f, .97f), true).rectTransform;
            tradeSettle = panel;
            tradeSettleText = ui.Label(panel, "点左边或右边的一件东西", 20, 22, 220, 120, 21, UiKit.Paper);
            tradeTipPanel = panel;
            ui.Button(panel, "买 一 件", 30, 300, 200, 56, () => CommitTrade(1), true);
            ui.Button(panel, "卖 一 件", 30, 368, 200, 56, () => CommitTrade(-1));
            ui.Button(panel, "多买几件", 30, 432, 200, 52, () => CommitTrade(12));
        }

        // 选中条目：买（正数）或卖（负数）。
        public void PickTrade(string itemId, int direction)
        {
            tradePick = itemId;
            tradeQuantity = direction;
            RefreshTrade();
        }

        public void CommitTrade(int bulk)
        {
            if (activeSave == null || string.IsNullOrEmpty(tradePick)) return;
            var merchant = Merchants.Find(tradeMerchantId);
            var state = MerchantLedger.Of(activeSave, merchant);
            var item = Table.Find(tradePick);
            if (item == null) return;
            var buying = tradeQuantity > 0;
            if (!buying && Trade.CanSell(item, merchant)) ConfirmSell(item, state, merchant, bulk);
            else DoTrade(item, state, merchant, buying, bulk);
        }

        void ConfirmSell(ItemDef item, MerchantState state, MerchantDef merchant, int bulk)
        {
            var count = Math.Max(1, Math.Min(bulk, InventoryRules.Count(activeSave.bag, item.Id)));
            var price = Trade.SellPrice(item, activeSave.localReputation) * count;
            Confirm("卖出「" + item.Name + "」" + count + " 件？", "到手 " + price + " 文。卖出去的东西要不回来。",
                "确认卖出", () => DoTrade(item, state, merchant, false, count));
        }

        void DoTrade(ItemDef item, MerchantState state, MerchantDef merchant, bool buying, int bulk)
        {
            var amount = Math.Max(1, Math.Min(bulk, Trade.MaxQuantity));
            int moved;
            if (buying)
            {
                // 钱不够不该整批拒绝：先把请求量裁到"买得起"与"货架上还有"，
                // 再交给 Core 成交——它本身就会按实际钱数与库存返回部分成交。
                var unit = tradeBlack ? Trade.BlackPrice(item) : Trade.BuyPrice(item, activeSave.localReputation);
                var shelf = MerchantLedger.Left(state, tradeBlack ? state.hidden : state.stock, item.Id);
                if (shelf <= 0) { TradeSay("「" + item.Name + "」他这儿没了。"); return; }
                var wanted = Math.Min(amount, shelf);
                if (unit > 0)
                {
                    var affordable = activeSave.coins / unit;
                    if (affordable < wanted) TradeSay("钱只够买 " + affordable + " 件，先欠着。");
                    wanted = Math.Min(wanted, affordable);
                }
                if (wanted <= 0) { TradeSay("一文钱也掏不出来了。"); return; }
                moved = MerchantLedger.Buy(activeSave, state, Table, item.Id, wanted, tradeBlack);
            }
            else
            {
                var owned = InventoryRules.Count(activeSave.bag, item.Id);
                if (owned <= 0) { TradeSay("你身上没有「" + item.Name + "」。"); return; }
                moved = MerchantLedger.Sell(activeSave, state, Table, merchant, item.Id, Math.Min(amount, owned));
            }
            if (moved <= 0) { TradeSay("这桩买卖没做成。"); RefreshTrade(); return; }
            PersistTrade();
            FlyCoins(buying);
            TradeSay(buying
                ? "买进 " + moved + " 件「" + item.Name + "」，付了 " + (tradeBlack ? Trade.BlackPrice(item) : Trade.BuyPrice(item, activeSave.localReputation)) * moved + " 文。"
                : "卖出 " + moved + " 件「" + item.Name + "」，进账 " + Trade.SellPrice(item, activeSave.localReputation) * moved + " 文。");
            RefreshTrade();
        }

        void PersistTrade()
        {
            Guard(() => repository.Write(activeSave));
        }

        // 200ms 铜钱飞扣：铜钱从对方那边飞进自己兜里（卖）或飞出去（买）。
        void FlyCoins(bool buying)
        {
            if (tradeSettle == null) return;
            if (tradeCoin == null)
            {
                // 组件要在创建时就挂上：只给 RectTransform 再去 GetComponent<RawImage> 会拿到 null。
                tradeCoin = new GameObject("Coin", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                tradeCoin.transform.SetParent(tradeSettle, false);
                var rect = tradeCoin.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(30, 30);
                var icon = Resources.Load<Texture2D>("Art/Items/coins");
                tradeCoin.texture = icon != null ? icon : Texture2D.whiteTexture;
                tradeCoin.raycastTarget = false;
            }
            var coins = (RectTransform)tradeCoin.transform.parent;
            tradeCoinFrom = buying ? new Vector2(-110f, 40f) : new Vector2(0f, -30f);
            tradeCoinTo = buying ? new Vector2(0f, -30f) : new Vector2(-110f, 40f);
            tradeCoin.rectTransform.anchoredPosition = tradeCoinFrom;
            tradeCoinStart = Time.unscaledTime;
            tradeCoin.gameObject.SetActive(true);
        }

        void UpdateTrade()
        {
            if (page != "trade" || tradeCoin == null || tradeCoinStart < 0f) return;
            var t = Mathf.Clamp01((Time.unscaledTime - tradeCoinStart) / .2f);
            var eased = 1f - (1f - t) * (1f - t);
            tradeCoin.rectTransform.anchoredPosition = Vector2.Lerp(tradeCoinFrom, tradeCoinTo, eased);
            tradeCoin.color = new Color(1f, 1f, 1f, 1f - t * t);
            if (t >= 1f) { tradeCoin.gameObject.SetActive(false); tradeCoinStart = -1f; }
        }

        void TradeSay(string message)
        {
            if (tradeNotice != null) tradeNotice.text = message;
        }

        void RefreshTrade()
        {
            var merchant = Merchants.Find(tradeMerchantId);
            if (merchant == null || activeSave == null) return;
            var state = MerchantLedger.Of(activeSave, merchant);
            if (tradeCoinLine != null)
                tradeCoinLine.text = "铜钱 " + activeSave.coins + " 文　地方声望 " + activeSave.localReputation +
                    "（让利 " + Mathf.RoundToInt(Reputation.Discount(activeSave.localReputation) * 100) + "%）" +
                    "　行囊 " + InventoryRules.UsedSlots(activeSave.bag) + " / " + InventoryRules.SlotCount;
            if (tradeMemoryLine != null)
            {
                var line = merchant.MemoryLine(state.trades);
                tradeMemoryLine.text = string.IsNullOrEmpty(line)
                    ? merchant.Greeting
                    : "他记得你：" + line;
            }
            foreach (var row in tradeShelf.Concat(tradeSack))
            {
                if (row.Background == null) continue;
                row.Background.color = row.ItemId == tradePick
                    ? new Color(1f, .95f, .78f, 1f)
                    : new Color(.98f, .96f, .9f, 1f);
            }
            if (tradeSettleText != null)
            {
                var item = Table.Find(tradePick);
                if (item == null) { tradeSettleText.text = "点左边或右边的一件东西"; return; }
                var unit = tradeQuantity > 0
                    ? (tradeBlack ? Trade.BlackPrice(item) : Trade.BuyPrice(item, activeSave.localReputation))
                    : Trade.SellPrice(item, activeSave.localReputation);
                var shortfall = tradeQuantity > 0 ? Trade.Missing(activeSave.coins, unit) : 0;
                tradeSettleText.text = (tradeQuantity > 0 ? "买\n" : "卖\n") + item.Name +
                    "\n\n单价 " + unit + " 文" +
                    (tradeQuantity > 0 && tradeBlack ? "（黑市）" : "") +
                    "\n数量 " + InventoryRules.Count(activeSave.bag, item.Id) + " / 上限 " + item.Stack +
                    (shortfall > 0 ? "\n\n还差 " + shortfall + " 文" : "");
            }
        }
    }
}
