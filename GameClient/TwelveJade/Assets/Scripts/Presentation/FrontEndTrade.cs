using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using TwelveJade.Economy;
using UnityEngine;
using UnityEngine.InputSystem;
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
        // M3-02：双栏列表放进固定高度视口，行数超出可滚，面板与结算区不跟着行数变形。
        RectTransform tradeShelfContent, tradeSackContent;
        ScrollRect tradeShelfScroll, tradeSackScroll;
        TMP_Text tradeShelfEmpty, tradeSackEmpty;
        readonly List<TradeRow> tradeShelf = new();
        readonly List<TradeRow> tradeSack = new();
        RawImage tradeCoin;
        Vector2 tradeCoinFrom, tradeCoinTo;
        float tradeCoinStart = -1f;

        // 列表几何：行高恒定 62（56 行体 + 6 间距），视口固定高 526——八行以内不滚，九行起出滚动条。
        const float RowPitch = 62f, ListTop = 62f, ListHeight = 526f, ListWidth = 600f;

        sealed class TradeRow
        {
            public string ItemId = "";
            public RectTransform Rect;
            public Image Background;
            public TMP_Text Name, Detail, Price;
        }

        // 验收注入：临时换一份商品表（如九行长货架），验完 RestoreMerchantFixture 恢复正式表。
        // 只换内存里的引用，不落盘。
        private MerchantTable merchantFixture;

        public void LoadMerchantFixtureForCheck(string json)
        {
            merchantFixture = MerchantTable.Parse(json);
            merchants = merchantFixture;
            tradeMerchantId = merchantFixture.Merchants[0].Id;
            tradeBlack = false;
            tradePick = "";
            tradeQuantity = 1;
            if (page == "trade") ShowTrade(tradeMerchantId, tradeBlack);
        }

        public void RestoreMerchantFixture()
        {
            if (merchantFixture == null) return;
            merchantFixture = null;
            merchants = null;
            if (page == "trade") ShowTrade(Merchants.Merchants[0].Id);
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
        // 黑市页签下货架可能为空（fixture 的 hidden 是空表）：越界返回空串而不是抛。
        public string SelfPriceOf(int index) =>
            index >= 0 && index < tradeShelf.Count ? tradeShelf[index].Price.text : "";
        public string NameOf(string itemId) => Table.Find(itemId)?.Name ?? "";
        public int MerchantStockLeft(string itemId)
        {
            var merchant = Merchants.Find(tradeMerchantId);
            var state = activeSave == null ? null : MerchantLedger.Of(activeSave, merchant);
            return state == null ? 0 : MerchantLedger.Left(state, itemId);
        }
        // 验收用：白日货架按当前商品表应有几行——与 TradeShelf 同一套列取口径算出来，
        // 让夹具比"表里真有几行"而不是钉死一个数字（商品表加一件就得多改一处断言，那是双源）。
        public int RealShelfRowsForCheck()
        {
            var merchant = Merchants.Find(tradeMerchantId);
            if (merchant == null) return 0;
            var state = activeSave == null ? null : MerchantLedger.Of(activeSave, merchant);
            if (state == null) return 0;
            var rows = 0;
            foreach (var line in merchant.Stock)
            {
                if (Table.Find(line.itemId) == null) continue;
                if (MerchantLedger.Left(state, state.stock, line.itemId) <= 0) continue;
                rows++;
            }
            return rows;
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
        // 验收钩子：货架滚动列表与空状态文案（M3-02 的 Play 断言用）。
        public ScrollRect ShelfScroll => tradeShelfScroll;
        public string ShelfEmptyNotice => tradeShelfEmpty != null && tradeShelfEmpty.gameObject.activeSelf
            ? tradeShelfEmpty.text : "";
        // 验收钩子：把当前商人的白日货架按表清空/补满——验「库存为零显示空状态」与
        // 「黑市仅独门货」，只动临时存档里的 MerchantState，不碰配置表。
        public void SetMerchantStockEmptyForCheck(bool empty)
        {
            var merchant = Merchants.Find(tradeMerchantId);
            var state = activeSave == null ? null : MerchantLedger.Of(activeSave, merchant);
            if (state == null) return;
            foreach (var line in state.stock)
            {
                var def = merchant.Stock.FirstOrDefault(s => s.itemId == line.itemId);
                if (def != null) line.count = empty ? 0 : def.count;
            }
        }

        // ---------------- ADR-025 对话半身立绘槽位 ----------------
        // 立绘是"锦上添花"，不是"前置条件"：美术没灌进来时对话必须照常开。
        // 所以无图不是错误路径而是另一条正常路径 —— 居中纯文字框；有图才让出左半边。
        // 槽位节点两种情况下都建，美术一旦落到 Resources/Art/Portraits/ 就自动生效。
        // TB-37 通用度量：两页共用（对话 veil / 交易 veil / 分支居中算式都从这两个数出发）。
        const float ScreenW = 1920f, ScreenH = 1080f;
        const float PortraitX = 100f, PortraitY = 180f, PortraitWidth = 512f, PortraitHeight = 768f;
        // TB-33 对话态构图（AB-2 规格，照参考图 2/3/5 落到 1920×1080；y 轴向下，UiKit.Rect 左上角锚定）：
        // 对话框收成单一底栏 y 780~1060（高 280）；名牌叠在框左上角外侧 y 742~790（跨框顶 10px）；
        // 分支从"全宽长条"改成居中短条，块底收在 736——排在框上方不压框（距框顶 44），也不碰名牌
        // （距牌顶 6）；6 条时块高 416、块顶 320，即"780 起向上不超 460"的占屏上界。
        // 正文框内上留 40、行距 46、最多 3 行，超出翻页，不许把框撑高。
        // 这些数以前散在方法体里写死（330/420/770/200/44/26/24/6），收成命名常量之后算式可验：
        //   条块高 = 条数×72-16；6 条 ⇒ 416；块顶 = 736-416 = 320；框+条块+名牌 ⇒ 占屏 ≤ 42%。
        const float BoxY = 780f, BoxHeight = 280f;
        const float BoxPadX = 44f;                              // 框内左右边距（旧写死的 88 = 2×44）
        const float BodyPadTop = 40f, BodyLinePitch = 46f;      // 正文上留 40、行距 46
        const int BodyMaxLines = 3;                             // 一页最多 3 行，超出翻页
        const float BodyHeight = BodyLinePitch * BodyMaxLines;  // 138：3 行的预留高
        const float MemoryY = 182f, MemoryHeight = 24f;         // "他记得你"一行，挂在正文下方
        const float ActionY = 210f, ActionHeight = 60f;         // 底排动作条（告辞 / 做买卖）
        const float PlatePadX = 20f;                            // 名牌内边距
        const float NamePlateWidth = 400f, NamePlateHeight = 48f, NamePlateY = 742f;
        const float BranchStripWidth = 560f, BranchStripHeight = 56f, BranchStripGap = 16f;
        const float BranchRowPitch = BranchStripHeight + BranchStripGap;  // 72：条高 + 间隔
        const float BranchBlockBottom = 736f;                   // 条块收口：6 条 ⇒ 块顶 320
        const int BranchMaxRows = 6;
        const float BranchStripX = (ScreenW - BranchStripWidth) * .5f;          // 680：水平居中
        const float FallbackBoxX = 470f, FallbackBoxWidth = 980f;   // 无立绘：居中文字框（P-02 原构图）
        const float PortraitBoxX = 580f, PortraitBoxWidth = 1200f;  // 有立绘：左立绘 + 右对话框
        const float PortraitFadeSeconds = .28f;
        static readonly string[] PortraitPaths = { "Art/Portraits/", "Art/Portraits/portrait-" };

        // ---- TB-37 交易页构图（与对话页同一份常量区：通用度量共用 ScreenW/ScreenH，页面度量
        // 各自命名；颜色走 UiKit 的 Trade* 命名色，方法体不再写死任何色值）----
        const float TradePadX = 100f;                       // 页左距：商号/页签/双栏/记忆行同源
        const float TradeNameY = 44f, TradeNameH = 56f, TradeNameSize = 38f;
        const float TradeTitleY = 104f, TradeTitleH = 30f, TradeTitleSize = 20f;
        const float TradeCoinX = 820f, TradeCoinY = 54f, TradeCoinW = 780f, TradeCoinH = 40f, TradeCoinSize = 22f;
        const float TradeHomeX = 1620f, TradeHomeY = 44f, TradeHomeW = 200f, TradeHomeH = 54f;
        const float TradeTabY = 150f, TradeTabW = 180f, TradeTabH = 54f, TradeTabPitch = 196f;
        const float TradeTabNoteX = 496f, TradeTabNoteY = 160f, TradeTabNoteW = 700f, TradeTabNoteH = 34f, TradeTabNoteSize = 19f;
        const float TradeColumnY = 232f, TradeColumnW = 640f, TradeColumnH = 600f;
        const float TradeSackX = 1050f;                     // 右栏 = 左栏 TradePadX + 950
        const float TradeColTitleX = 26f, TradeColTitleY = 18f, TradeColTitleW = 400f, TradeColTitleH = 36f, TradeColTitleSize = 22f;
        const float TradeMemoryY = 900f, TradeMemoryW = 1300f, TradeMemoryH = 40f, TradeMemorySize = 21f;
        const float TradeNoticeY = 946f, TradeNoticeW = 1300f, TradeNoticeH = 40f, TradeNoticeSize = 21f;
        const float TradeSettleX = 780f;                    // 结算区夹在两栏之间
        // 行内小件（MakeRow）：行高 RowPitch=62 恒定；名字/图标/价/余量四格各自定位。
        const float TradeRowHeight = 56f;
        const float TradeRowNameX = 14f, TradeRowNameY = 4f, TradeRowNameW = 260f, TradeRowNameH = 30f, TradeRowNameSize = 22f;
        const float TradeRowIconX = 276f, TradeRowIconY = 4f, TradeRowIconSize = 48f;
        const float TradeRowPriceX = 330f, TradeRowPriceY = 4f, TradeRowPriceW = 140f, TradeRowPriceH = 30f, TradeRowPriceSize = 19f;
        const float TradeRowDetailX = 470f, TradeRowDetailY = 4f, TradeRowDetailW = 116f, TradeRowDetailH = 30f, TradeRowDetailSize = 18f;
        // 列表视口与空状态（TradeList）：视口左边距 20；空文案居中、48 高。
        const float TradeListViewportX = 20f;
        const float TradeEmptyH = 48f, TradeEmptySize = 20f;
        // 结算区（BuildSettle）：260×600 深 panel，标题 + 三个动作钮（买/卖/多买，间距 68）。
        const float TradeSettleW = 260f, TradeSettleH = 600f;
        const float TradeSettleTextX = 20f, TradeSettleTextY = 22f, TradeSettleTextW = 220f, TradeSettleTextH = 120f, TradeSettleTextSize = 21f;
        const float TradeSettleBtnX = 30f, TradeSettleBtnW = 200f, TradeSettleBtnH = 56f, TradeSettleBtnSmallH = 52f;
        const float TradeSettleBtnFirstY = 300f, TradeSettleBtnPitch = 68f;
        // 铜钱飞扣（FlyCoins/UpdateTrade）：200ms；买 = 从对方(-110,40) 飞进自己(0,-30)，卖反之。
        const float TradeCoinFlySeconds = .2f;
        static readonly Vector2 TradeCoinFromBuy = new Vector2(-110f, 40f), TradeCoinToBuy = new Vector2(0f, -30f);

        RectTransform talkPortraitSlot = null; // Legacy preview handle; intentionally absent from the live UI.
        Image talkPortraitImage = null; // Legacy preview handle; intentionally absent from the live UI.
        RectTransform talkBoxRect = null; // Legacy preview handle; intentionally absent from the live UI.
        TMP_Text talkLine = null; // Legacy preview handle; intentionally absent from the live UI.
        // 正文翻页（TB-33）：按 TMP 实际折行切页，每页最多 BodyMaxLines 行；提示签即翻页按钮。
        readonly List<string> talkPages = new();
        int talkPage;
        TMP_Text talkPageHint = null; // Legacy preview handle; intentionally absent from the live UI.
        // TB-36 分支键盘导航状态：上下键移动选中、数字键 1-6 直选（预览页 digitKeys 同模式）、
        // 回车/空格确认。选中态由 PaintTalkBranchSelection 落笔在每条 Chip 上。
        readonly List<DialogueOption> talkBranchOptions = new();
        readonly List<Image> talkBranchFrames = new();
        string talkBranchNodeId = "";
        int talkBranchIndex = -1;
        Sprite portraitFixture;
        string portraitFixtureNpcId;

        public bool TalkPortraitSlotExists => talkPortraitSlot != null;
        public bool TalkPortraitShown => talkPortraitImage != null;
        public bool TalkPortraitPreservesAspect => talkPortraitImage != null && talkPortraitImage.preserveAspect;
        public Vector2 TalkPortraitSize => talkPortraitSlot == null ? Vector2.zero : talkPortraitSlot.sizeDelta;
        public bool TalkPortraitHasFade => talkPortraitSlot != null &&
            talkPortraitSlot.GetComponent<CanvasGroup>() != null;
        /// <summary>对话正文（验收据此分辨"白日台词"与"黑市按语"）。</summary>
        public string TalkLineText => talkLine != null ? talkLine.text : "";
        public float TalkPortraitAlpha => talkPortraitSlot == null ? -1f
            : (talkPortraitSlot.GetComponent<CanvasGroup>() is { } g ? g.alpha : -1f);
        /// <summary>对话框几何（x, y, 宽, 高）：验收据此分辨"有图/无图"两套构图。</summary>
        public Vector4 TalkBoxGeometry => talkBoxRect == null ? Vector4.zero
            : new Vector4(talkBoxRect.anchoredPosition.x, talkBoxRect.anchoredPosition.y,
                talkBoxRect.sizeDelta.x, talkBoxRect.sizeDelta.y);
        public IReadOnlyList<string> PortraitResourcePaths => PortraitPaths;

        /// <summary>验收注入：临时给某个 NPC 指定一张立绘，验完清掉。
        /// 不往 Resources 里写测试资产——T027 的导入缓存陷阱不值得为一条断言再踩一次。</summary>
        public void SetPortraitFixtureForCheck(string npcId, Sprite sprite)
        {
            portraitFixtureNpcId = npcId;
            portraitFixture = sprite;
        }

        public void ClearPortraitFixtureForCheck()
        {
            portraitFixture = null;
            portraitFixtureNpcId = null;
        }

        // D-21 / T090：本仓所有立绘资源的 textureType 都是 Default（`ArtImportGuard.cs:34` 的全局策略，
        // NPC 与 Props 同为一档）。Default 资源用 `Resources.Load<Sprite>` 取**必返 null**，
        // 于是"图在盘上、玩家看不见"。裁决是不改导入器、改加载方式：改 .meta 会被 ArtImportGuard
        // 在下次导入按回 Default，能被自动还原的修复等于没修。
        // Sprite 按 npcId 造一次并缓存（含"查过但没有"的 null），对话每帧重画不该反复问 Resources。
        const float PortraitPixelsPerUnit = 100f;
        readonly Dictionary<string, Sprite> portraitCache = new Dictionary<string, Sprite>();

        // TB-38 立绘亮度门槛（阈值依据 Tools/logs/td-34/ 实测标定）：luma = mean(.299R+.587G+.114B)，
        // 0-255 域，与标定脚本同一把尺子。负样本（旧黑剪影）实测 4.7~14.0，正样本（已换真图）
        // 173.3~182.2——80 高于负最大 5.7x、低于正最小 2.2x。门槛只报警、不改挂载：ADR-025 的
        // 挂不挂由图在不在库里定，判据不能多一份；这里管的是"不许静默"——暗图每次搭话都报。
        const float PortraitLumaFloor = 80f;
        readonly Dictionary<string, float> portraitLuma = new Dictionary<string, float>();

        Sprite PortraitFor(string npcId)
        {
            if (portraitFixture != null && portraitFixtureNpcId == npcId) return portraitFixture;
            if (portraitCache.TryGetValue(npcId, out var cached)) return cached;
            Sprite built = null;
            foreach (var prefix in PortraitPaths)
            {
                var texture = Resources.Load<Texture2D>(prefix + npcId);
                if (texture == null) continue;
                built = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), PortraitPixelsPerUnit);
                // 加载后即量亮度，随 sprite 一并缓存——门槛是加载路径的属性，不是搭话路径的。
                // 注入的夹具 sprite 不走这里（验收样张，不受风格门槛管）。
                portraitLuma[npcId] = MeanLuma(texture);
                break;
            }
            portraitCache[npcId] = built;
            return built;
        }

        // Resources 立绘的 textureType 是 Default、isReadable=0，GetPixels 直接抛。
        // Graphics.CopyTexture 做 GPU 级拷贝到一张可读副本上再逐像素算——不降采样，
        // 与 td-34 的全像素平均同口径；sRGB 纹理拷贝不转色彩空间，与 PIL 读文件字节一致。
        static float MeanLuma(Texture2D source)
        {
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            Graphics.CopyTexture(source, copy);
            var pixels = copy.GetPixels32();
            UnityEngine.Object.Destroy(copy);
            double sum = 0;
            foreach (var pixel in pixels) sum += .299 * pixel.r + .587 * pixel.g + .114 * pixel.b;
            return (float)(sum / pixels.Length);
        }

        /// <summary>TB-38 验收钩子：该 NPC 的立绘是否低于亮度门槛（无图 false——门槛只管图太黑）。</summary>
        public bool PortraitGateRejected(string npcId) =>
            portraitLuma.TryGetValue(npcId, out var luma) && luma < PortraitLumaFloor;

        /// <summary>TB-38 验收钩子：该 NPC 立绘的实测平均亮度（无图 -1，可复算对账 td-34）。</summary>
        public float PortraitLumaOf(string npcId) =>
            portraitLuma.TryGetValue(npcId, out var luma) ? luma : -1f;

        /// <summary>验收用：换美术或换口径后，缓存必须能清干净。</summary>
        public void ClearPortraitCacheForCheck() { portraitCache.Clear(); portraitLuma.Clear(); }

        /// <summary>夹具优先：注入过的 NPC 走注入，其余走真实资源。</summary>
        public bool PortraitResourcePresent(string npcId) => PortraitFor(npcId) != null;

        /// <summary>立绘淡入：按"绝对时刻"算 alpha，而不是逐帧累加。
        /// 批量模式下玩家循环节拍极稀疏（T030），逐帧累加会永远停在半透明；
        /// 按时刻算则只要补一帧就直接落到该到的位置。</summary>
        sealed class TalkPortraitFade : MonoBehaviour
        {
            CanvasGroup group;
            float startedAt, seconds;

            public void Configure(CanvasGroup target, float duration)
            {
                if (duration <= 0f) { target.alpha = 1f; Destroy(this); return; }
                group = target; startedAt = Time.unscaledTime; seconds = duration;
            }

            void Update()
            {
                group.alpha = Mathf.Clamp01((Time.unscaledTime - startedAt) / seconds);
                if (group.alpha >= 1f) Destroy(this);
            }
        }

        // ---------------- ADR-027 分支对白网与记忆回响 ----------------
        // 对白分支接在既有搭话框下面，不动原框的几何——FrontEndShotProps 对
        // 「Talk box 唯一」「框体 x/宽」「正文是 NPC 原台词」都有断言在，改动即回归。
        DialogueTree dialogueTree;
        DialogueView talkView;
        string talkNpcId = "";
        int talkTrades;
        string fortuneVerdict = "";

        // 对白表与作息表同一套懒加载：解析失败不弹黄框炸场，退回"只搭话不分支"。
        DialogueTree Dialogues
        {
            get
            {
                if (dialogueTree != null) return dialogueTree;
                var asset = Resources.Load<TextAsset>("Config/dialogues");
                if (asset == null) return dialogueTree = DialogueTree.Parse(FallbackDialogue);
                try { dialogueTree = DialogueTree.Parse(asset.text); }
                catch (FormatException ex)
                {
                    Debug.LogWarning("dialogues.json is malformed: " + ex.Message);
                    dialogueTree = DialogueTree.Parse(FallbackDialogue);
                }
                return dialogueTree;
            }
        }

        // 兜底对白：空表也要能搭话。与商人兜底表同理——自己过不了自己的校验等于没兜底。
        const string FallbackDialogue =
            "{\"nodes\":[{\"id\":\"fallback-bye\",\"npc\":\"none\",\"text\":\"……\",\"next\":\"\"}]}";

        /// <summary>验收用：某人此刻的记忆阶（0 陌路 / 1 熟络 / 2 故交 / 3 患难）。</summary>
        public int TalkMemoryTier(string npcId) =>
            (int)MemoryEcho.TierOf(activeSave, npcId, TradesOf(npcId));

        /// <summary>验收用：某人此刻可选的分支条数（记忆阶越高，选项越多）。</summary>
        public int TalkBranchCount(string npcId)
        {
            var tree = Dialogues;
            var entry = tree.EntryNodeFor(npcId);
            if (entry.Length == 0 || activeSave == null) return 0;
            var view = DialogueRules.Open(activeSave, tree.Table, npcId, entry);
            return tree.BranchOptions(activeSave, view, TradesOf(npcId), Quests).Count;
        }

        /// <summary>验收用：摸骨相面后留下的断语。</summary>
        public string FortuneVerdict => fortuneVerdict;
        public int FortuneCost => FortunePrice;

        int TradesOf(string npcId)
        {
            var merchant = Merchants.Find(npcId);
            if (merchant == null || activeSave == null) return 0;
            return MerchantLedger.Of(activeSave, merchant)?.trades ?? 0;
        }

        // 人物表与任务表：对白推进要真落关系与任务状态，就得拿到真表；
        // 拿不到（资产缺失/表坏了）也不许把对话卡死，退回"只推进台词"。
        NpcTable npcTableForCheck;
        QuestTable questTableForCheck;

        NpcTable NpcTableForCheck()
        {
            if (npcTableForCheck != null) return npcTableForCheck;
            var asset = Resources.Load<TextAsset>("Config/npcs");
            if (asset == null) return null;
            try { npcTableForCheck = NpcTable.Parse(asset.text); }
            catch (FormatException) { return null; }
            return npcTableForCheck;
        }

        QuestTable QuestTableForCheck()
        {
            if (questTableForCheck != null) return questTableForCheck;
            var asset = Resources.Load<TextAsset>("Config/quests");
            if (asset == null) return null;
            try { questTableForCheck = QuestTable.Parse(asset.text); }
            catch (FormatException) { return null; }
            return questTableForCheck;
        }

        // ---- M4 沈怀安摸骨相面 ----
        // 十九命格断语库：与 CharacterGen.Destinies 一一对应，按存档命格取值。
        // 隐藏超品（玉楼引 / 无名）走同一条"摸不透"的分支——这是卷一末尾的伏笔，
        // 不是随机台词，所以两条命格共用一句，不要拆成两份。
        // 断语库在 Core（FortuneReading）：判据只有一份，表现层不抄第二遍。
        const int FortunePrice = FortuneReading.Price;

        /// <summary>摸骨看相：扣 5 文，给一句切中要害的断语。钱不够就别让人白摸。</summary>
        public string ReadFortune()
        {
            if (activeSave == null) return fortuneVerdict = "";
            if (activeSave.coins < FortunePrice)
            {
                fortuneVerdict = "（你摸遍全身只摸出几个铜板。）罢了罢了——命这东西，钱不够就先别问。";
                return fortuneVerdict;
            }
            activeSave.coins -= FortunePrice;
            Guard(() => repository.Write(activeSave));
            fortuneVerdict = FortuneReading.VerdictFor(activeSave.destiny);
            return fortuneVerdict;
        }

        // 搭话：先见一面，认个脸，报个到；之后买卖都从这里进。
        public void TalkTo(string npcId) => OpenTownDialogue(npcId);

        // 正文分页：按 TMP 实际折行切，每页最多 BodyMaxLines 行。框高是常量——长文本翻页，
        // 不把框撑高（AB-2"最多 3 行，超出要能翻页"）。一页装得下时整句原样上屏：
        // 验收有"正文是 NPC 原台词"的整句断言，装得下就不做无谓重组。
        void SetTalkText(string text)
        {
            talkPages.Clear();
            if (string.IsNullOrEmpty(text)) talkPages.Add("");
            else
            {
                talkLine.text = text;
                talkLine.ForceMeshUpdate();
                var info = talkLine.textInfo;
                if (info.lineCount <= BodyMaxLines) talkPages.Add(text);
                else
                {
                    var lines = new List<string>();
                    for (var i = 0; i < info.lineCount; i++)
                    {
                        var from = info.lineInfo[i].firstCharacterIndex;
                        var to = i + 1 < info.lineCount ? info.lineInfo[i + 1].firstCharacterIndex : text.Length;
                        lines.Add(text.Substring(from, to - from));
                    }
                    for (var i = 0; i < lines.Count; i += BodyMaxLines)
                        talkPages.Add(string.Join("\n", lines.Skip(i).Take(BodyMaxLines)));
                }
            }
            ShowTalkPage(0);
        }

        void ShowTalkPage(int index)
        {
            talkPage = index;
            if (talkLine != null) talkLine.text = talkPages[index];
            if (talkPageHint == null) return;
            var more = talkPages.Count > 1;
            talkPageHint.gameObject.SetActive(more);
            talkPageHint.text = index + 1 < talkPages.Count ? "· 点击继续 ·" : "· 完 ·";
            // 禁用态（TB-36）：到底的提示签不邀请点击——Muted 色 + AdvanceTalkPage 守卫双保险。
            talkPageHint.color = index + 1 < talkPages.Count ? UiKit.Paper : UiKit.Muted;
        }

        void AdvanceTalkPage()
        {
            if (talkPage + 1 < talkPages.Count) ShowTalkPage(talkPage + 1);
        }

        DialogueView OpenEntryView(string npcId)
        {
            var tree = Dialogues;
            var entry = tree.EntryNodeFor(npcId);
            if (entry.Length == 0 || activeSave == null) return null;
            return DialogueRules.Open(activeSave, tree.Table, npcId, entry);
        }

        // 分支短条（TB-33）：居中 560 宽签条排在对话框上方，块底收在 BranchBlockBottom——
        // 条数多的一侧向上长（6 条块顶 320，不越过"780 起向上不超 460"的占屏上界），
        // 条数少的一侧贴框：不压框（距框顶 44），也不碰名牌（距牌顶 6）。
        // 容器是无底色的布局节点；一帧内被建两次也要先摘掉旧的（T107 的同一形态）。
        // TB-36：容器上挂键盘轮询件（FrontEndController.Update 在主线文件、不在本线白名单，
        // TalkPortraitFade 同一先例——挂件自带 Update），选项与条框登记进导航状态。
        void BuildTalkBranches()
        {
            if (activeSave == null || talkView == null) return;
            HideStaleBranchPanels();
            var options = Dialogues.BranchOptions(activeSave, talkView, talkTrades, Quests);
            if (options.Count == 0) return;
            var count = Math.Min(options.Count, BranchMaxRows);
            var blockHeight = count * BranchRowPitch - BranchStripGap;
            var block = ui.Rect(modal, "Talk branches", BranchStripX,
                BranchBlockBottom - blockHeight, BranchStripWidth, blockHeight);
            block.gameObject.AddComponent<TalkBranchKeys>().owner = this;
            talkBranchOptions.Clear();
            talkBranchFrames.Clear();
            talkBranchNodeId = talkView.NodeId;
            talkBranchIndex = -1;
            for (var i = 0; i < count; i++)
            {
                var option = options[i];
                var nodeId = talkBranchNodeId;
                var button = ui.Chip(block, "Button " + option.Text, 0f, i * BranchRowPitch, BranchStripWidth,
                    BranchStripHeight, option.Text, () => OnTalkBranch(nodeId, option));
                talkBranchOptions.Add(option);
                talkBranchFrames.Add(button.targetGraphic as Image);
            }
        }

        // TB-36：选中态随键盘移动（循环），落笔在每条 Chip 的 Gold 底上（UiKit.SetChipSelected）。
        void MoveTalkBranch(int step)
        {
            if (talkBranchOptions.Count == 0) return;
            talkBranchIndex = (talkBranchIndex + step + talkBranchOptions.Count) % talkBranchOptions.Count;
            PaintTalkBranchSelection();
        }

        // 数字键直选（预览页 digitKeys 同一模式）：按下即确认，不先停留选中态。
        void PickTalkBranch(int index)
        {
            if (index < 0 || index >= talkBranchOptions.Count) return;
            talkBranchIndex = index;
            ConfirmTalkBranch();
        }

        void ConfirmTalkBranch()
        {
            if (talkBranchIndex < 0 || talkBranchIndex >= talkBranchOptions.Count) return;
            OnTalkBranch(talkBranchNodeId, talkBranchOptions[talkBranchIndex]);
        }

        void PaintTalkBranchSelection()
        {
            for (var i = 0; i < talkBranchFrames.Count; i++)
                ui.SetChipSelected(talkBranchFrames[i], i == talkBranchIndex);
        }

        // TB-36 分支键盘导航：上下键（或 W/S）移动、数字键 1-6 直选、回车/空格确认。
        // 轮询走挂件自己的 Update——FrontEndController.Update 在主线文件里，不在本线白名单
        // （TalkPortraitFade 同一先例）。Esc 关对话由主线 Update 管，这里不重复。
        sealed class TalkBranchKeys : MonoBehaviour
        {
            internal FrontEndController owner;

            void Update()
            {
                if (owner == null) return;
                var keyboard = Keyboard.current;
                if (keyboard == null) return;
                if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
                    owner.MoveTalkBranch(-1);
                else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
                    owner.MoveTalkBranch(1);
                else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame ||
                    keyboard.spaceKey.wasPressedThisFrame)
                    owner.ConfirmTalkBranch();
                else if (keyboard.digit1Key.wasPressedThisFrame) owner.PickTalkBranch(0);
                else if (keyboard.digit2Key.wasPressedThisFrame) owner.PickTalkBranch(1);
                else if (keyboard.digit3Key.wasPressedThisFrame) owner.PickTalkBranch(2);
                else if (keyboard.digit4Key.wasPressedThisFrame) owner.PickTalkBranch(3);
                else if (keyboard.digit5Key.wasPressedThisFrame) owner.PickTalkBranch(4);
                else if (keyboard.digit6Key.wasPressedThisFrame) owner.PickTalkBranch(5);
            }
        }

        // 选一支：效果由 Core 的 DialogueRules 落（标记/线索/关系），这里只负责
        // 认出挂在其上的动作，并把对话推进到下一节点。
        void OnTalkBranch(string nodeId, DialogueOption option)
        {
            if (dialoguePanel == null || talkView == null || talkView.NodeId != nodeId) return;
            int index = talkView.Options.ToList().IndexOf(option);
            if (index >= 0) dialoguePanel.SelectOption(index);
        }

        /// <summary>任务对账的运行期调用点之一（另一个在行囊页：做完一道方子之后）。
        /// 结算回执只写日志、不抢话——气运不当场弹窗（大纲 1.3），任务了结由章节页
        /// 的"已了结"状态呈现。真要把回执做成人眼看得见的东西，那是要用户逐张验图的观感项，
        /// 不在本 Gate 里靠断言宣布完成。</summary>
        void DriveQuests(QuestSignal signal)
        {
            if (activeSave == null) return;
            var report = QuestSettlement.Drive(activeSave, Quests, Table, Karma, signal);
            if(town!=null)
            {
                try{AtlasExploration.Commit(activeSave,()=>AtlasExploration.RevealEarnedEvents(activeSave,town,Quests),repository.Write);}
                catch(Exception ex){Debug.LogError("Atlas earned event save failed: "+ex.Message);}
            }
            RefreshQuestOverheads();
            if (report.Completed.Count > 0)
                Debug.Log("[quest] 结算：" + string.Join("、", report.Completed) +
                    " 发奖铜钱 " + report.CoinsGranted + " 文、物品 " + report.ItemsGranted +
                    " 件（装不下 " + report.ItemsLeftover + " 件）、顺带记下 " +
                    report.KarmaRecorded.Count + " 件善事");
            if (report.Unwired.Count > 0)
                Debug.Log("[quest] 没有运行期生产者的目标（登记，不硬推）：" +
                    string.Join("；", report.Unwired));
        }

        // 分支推进后重画：正文换成节点台词，分支面板按新节点重建。
        void RenderBranchResult(string said)
        {
            if (talkView == null || string.IsNullOrEmpty(talkView.NodeId)) { CloseModal(); return; }
            var npc = town?.Npcs.FirstOrDefault(n => n.Id == talkNpcId);
            if (npc == null || modal == null) { CloseModal(); return; }
            var box = modal.Find("Talk box");
            if (box == null) { CloseModal(); return; }
            if (talkLine != null)
                SetTalkText((string.IsNullOrEmpty(fortuneVerdict) ? "" : fortuneVerdict + "\n") + talkView.Text);
            HideStaleBranchPanels();
            BuildTalkBranches();
        }

        /// <summary>把上一节点的分支面板**当场摘掉**（T107）。
        ///
        /// `Destroy` 是帧末才生效的：同一帧里旧面板仍然在渲染，新面板压在它上面，
        /// 透过那层 92% 不透明的底就能看见旧一层选项的残字——两层文字叠在一起。
        /// 真人玩是一帧闪烁，批处理夹具的同步截图正好抓在这一帧上，于是把它拍成了"画面缺陷"。
        /// 两者都是同一个根因，所以修法只有一个：先 `SetActive(false)`（本帧立即不渲染）再 Destroy。</summary>
        void HideStaleBranchPanels()
        {
            if (modal == null) return;
            foreach (Transform child in modal)
                if (child.name == "Talk branches")
                {
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                }
        }

        // 认领机缘：对白里点了【acceptQuest】才接，不做满屏感叹号。
        // ---------------- 气运善行簿的落账口（O3 / T043 / T097） ----------------

        KarmaTable karmaTable;

        /// <summary>善行清单（karma.json）。与配方表同一套加载口径：缺文件就一句警告 + 空表，
        /// 空表意味着 Record 一律拒收（表外 id 不记账）——比凭空造一条账安全。</summary>
        KarmaTable Karma
        {
            get
            {
                if (karmaTable != null) return karmaTable;
                var asset = Resources.Load<TextAsset>(KarmaTable.ResourcePath);
                if (asset == null)
                {
                    Debug.LogWarning("Missing Resources/" + KarmaTable.ResourcePath + "; 善行簿今天不收账。");
                    return karmaTable = KarmaTable.Empty;
                }
                try { karmaTable = KarmaTable.Parse(asset.text); }
                catch (FormatException ex)
                {
                    Debug.LogWarning("karma.json is malformed: " + ex.Message);
                    karmaTable = KarmaTable.Empty;
                }
                return karmaTable;
            }
        }

        /// <summary>把"这件事被世界看见了"记进账簿。**Record 只改内存**，
        /// 所以本方法的调用点必须紧跟一次 repository.Write（T097 落盘责任）；
        /// 表里查不到的 id 由 KarmaTracker 当场拒收，不在这里编一条账。</summary>
        bool RecordKarma(string eventId)
        {
            if (activeSave == null || string.IsNullOrEmpty(eventId)) return false;
            var recorded = KarmaTracker.Record(activeSave, Karma, eventId, activeSave.WorldTimeNow());
            if (recorded) Debug.Log("[karma] 记下第 " + KarmaTracker.TotalCount(activeSave) + " 件事：" + eventId);
            return recorded;
        }

        /// <summary>走到某个对白节点 = 那件事被看见。映射表在 Economy/KarmaTriggers（唯一一处）。</summary>
        bool RecordKarmaAtNode(string nodeId) => RecordKarma(KarmaTriggers.ForDialogueNode(nodeId));

        /// <summary>验收钩子：沿真实记账路径把气运记到 n 件，供"气运够了才多送的那一卦"出图。
        /// 与 GiveItem / SetCoinsForCheck 同一类，只在验收脚本里出现，游戏逻辑不这么记账；
        /// id 全部取自 karma.json 的 deed 列（本域铁律二：C# 里不写善事名）。
        /// 记完紧跟一次 Write——Record 只动内存（T097）。</summary>
        public int RecordKarmaForCheck(int deeds)
        {
            if (activeSave == null || deeds <= 0) return 0;
            var recorded = 0;
            foreach (var def in Karma.Deeds.Take(deeds))
                if (RecordKarma(def.Id)) recorded++;
            if (recorded > 0) Guard(() => repository.Write(activeSave));
            return recorded;
        }

        /// <summary>接一条对白选项指定的任务（T083 的运行期入口之一）。
        /// 任务 id 由 dialogues.json 的 `acceptQuest` 字段给——以前这里是
        /// `nodeId == "yaolao-rescue" ? "quest-herbal-rescue" : ""` 一个硬编码，
        /// 而且传进来的是**当前节点**而不是去向节点，所以点"答应下来"其实一条也没接上。</summary>
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

            ui.Panel(content, "Trade veil", 0, 0, ScreenW, ScreenH, UiKit.TradeVeil);
            ui.Label(content, merchant.Name, TradePadX, TradeNameY, 700, TradeNameH, TradeNameSize, UiKit.Ink, TextAlignmentOptions.TopLeft, true);
            ui.Label(content, merchant.Title, TradePadX + 4f, TradeTitleY, 700, TradeTitleH, TradeTitleSize, UiKit.TradeTitleGray);
            tradeCoinLine = ui.Label(content, "", TradeCoinX, TradeCoinY, TradeCoinW, TradeCoinH, TradeCoinSize, UiKit.Ink, TextAlignmentOptions.MidlineRight);
            ui.Button(content, "回到青石镇", TradeHomeX, TradeHomeY, TradeHomeW, TradeHomeH, ShowTown, true);

            // 页签：白日铺 / 黑市（命格才看得见）。黑市加价四成，独门货只在黑市。
            ui.Button(content, "白日铺", TradePadX, TradeTabY, TradeTabW, TradeTabH, () => SwitchShelf(false), !tradeBlack);
            var blackVisible = Trade.SeesBlackMarket(activeSave);
            ui.Button(content, "黑 市", TradePadX + TradeTabPitch, TradeTabY, TradeTabW, TradeTabH, () => SwitchShelf(true), tradeBlack, blackVisible);
            if (blackVisible && tradeBlack)
                ui.Label(content, "黑市：价钱贵四成，独门货只在夜里出。", TradeTabNoteX, TradeTabNoteY, TradeTabNoteW, TradeTabNoteH, TradeTabNoteSize,
                    UiKit.TradeBlackRed);

            tradeShelf.Clear();
            tradeSack.Clear();
            var left = TradeShelf(TradePadX, TradeColumnY, "他 的 货");
            var right = TradeSack(TradeSackX, TradeColumnY, "你 的 行 囊");
            tradeMemoryLine = ui.Label(content, "", TradePadX, TradeMemoryY, TradeMemoryW, TradeMemoryH, TradeMemorySize, UiKit.Gold);
            tradeNotice = ui.Label(content, "", TradePadX, TradeNoticeY, TradeNoticeW, TradeNoticeH, TradeNoticeSize, UiKit.TradeNoticeBrown);
            BuildSettle(TradeSettleX, TradeColumnY, merchant, state);
            RefreshTrade();
        }

        void SwitchShelf(bool black)
        {
            tradePick = "";
            tradeQuantity = 1;
            ShowTrade(tradeMerchantId, black);
        }

        // 左栏：他手上的货。行挂在固定高度视口的 content 上，九行起可滚（T013 的"最多 8 行"解除）。
        RectTransform TradeShelf(float x, float y, string title)
        {
            var panel = ui.Panel(content, "Shelf", x, y, TradeColumnW, TradeColumnH, UiKit.TradePaper, true).rectTransform;
            ui.Label(panel, title, TradeColTitleX, TradeColTitleY, TradeColTitleW, TradeColTitleH, TradeColTitleSize, UiKit.Ink, TextAlignmentOptions.TopLeft, true);
            var merchant = Merchants.Find(tradeMerchantId);
            var state = activeSave == null ? null : MerchantLedger.Of(activeSave, merchant);
            var list = tradeBlack ? merchant.Hidden : merchant.Stock;
            tradeShelfContent = TradeList(panel, "Shelf", out tradeShelfScroll, out tradeShelfEmpty);
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
                var row = MakeRow(tradeShelfContent, order, itemId, item, shown + " 文/件", "余 " + left,
                    "Shelf row", () => PickTrade(itemId, 1));
                tradeShelf.Add(row);
                order++;
            }
            FinishList(tradeShelfContent, tradeShelfScroll, tradeShelfEmpty, order, "货都卖完了，明日请早。");
            return panel;
        }

        // 右栏：他能收的（任务物与不收的类别置灰出现，不凭空消失——玩家得知道他带了什么）。
        RectTransform TradeSack(float x, float y, string title)
        {
            var panel = ui.Panel(content, "Sack", x, y, TradeColumnW, TradeColumnH, UiKit.TradePaper, true).rectTransform;
            ui.Label(panel, title, TradeColTitleX, TradeColTitleY, TradeColTitleW, TradeColTitleH, TradeColTitleSize, UiKit.Ink, TextAlignmentOptions.TopLeft, true);
            var merchant = Merchants.Find(tradeMerchantId);
            tradeSackContent = TradeList(panel, "Sack", out tradeSackScroll, out tradeSackEmpty);
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
                var row = MakeRow(tradeSackContent, rows, id, item, price, "有 " + stack.count, "Sack row",
                    () => { if (sellable) PickTrade(id, -1); else TradeSay("「" + item.Name + "」他不收。"); });
                row.Background.color = sellable
                    ? UiKit.TradePaper : UiKit.TradePaperDim;
                tradeSack.Add(row);
                rows++;
            }
            FinishList(tradeSackContent, tradeSackScroll, tradeSackEmpty, rows, "你身上没有他要的货。");
            return panel;
        }

        // 固定高度列表视口：行放在 viewport 下的 content 里，行数一多 content 变高、滚轮滚动，
        // 超出部分被 RectMask2D 裁掉——面板本体与中间结算区完全不动。
        RectTransform TradeList(RectTransform panel, string name, out ScrollRect scroll, out TMP_Text empty)
        {
            var viewport = ui.Rect(panel, name + " viewport", TradeListViewportX, ListTop, ListWidth, ListHeight);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = ui.Rect(viewport, "Content", 0, 0, ListWidth, ListHeight);
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            empty = ui.Label(viewport, "", 0, ListHeight * .5f - 24f, ListWidth, TradeEmptyH, TradeEmptySize,
                UiKit.TradeEmptyGray, TextAlignmentOptions.Center);
            empty.gameObject.SetActive(false);
            return content;
        }

        // 行数落定后把 content 撑到实际高度、回到顶部；一行都没有时亮出空状态文案，不建空按钮。
        static void FinishList(RectTransform content, ScrollRect scroll, TMP_Text empty, int rows, string emptyText)
        {
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(rows * RowPitch, 1f));
            scroll.verticalNormalizedPosition = 1f;
            if (rows == 0)
            {
                empty.text = emptyText;
                empty.gameObject.SetActive(true);
            }
        }

        TradeRow MakeRow(RectTransform parent, int order, string itemId, ItemDef item, string price,
            string note, string rowTag, Action onClick)
        {
            // 行高恒定，在 content 里从顶往下排；滚动只挪 content，行的相对位置永远不变。
            var rect = ui.Rect(parent, "Row " + itemId, 0, order * RowPitch, ListWidth, TradeRowHeight);
            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = UiKit.RoundedSprite();
            background.type = Image.Type.Sliced;
            background.color = UiKit.TradeRow;
            background.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            // 左右两栏的行必须分属两个名字空间：以前两边都叫 "Shelf row N"，
            // 按名字取控件的验收会撞见对方那一栏——"点货架首行"其实点到了自己行囊里那件不收的货。
            button.gameObject.name = rowTag + " " + order;
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            var name = ui.Label(rect, item.Name, TradeRowNameX, TradeRowNameY, TradeRowNameW, TradeRowNameH, TradeRowNameSize, UiKit.Ink, TextAlignmentOptions.MidlineLeft, true);
            var tier = Table.FindTier(item.Tier);
            if (tier != null) name.color = UiKit.Hex(tier.Color.TrimStart('#'));
            var icon = Resources.Load<Texture2D>("Art/Items/" + item.Icon);
            var art = new GameObject("Icon", typeof(RectTransform)).GetComponent<RectTransform>();
            art.SetParent(rect, false);
            art.anchorMin = art.anchorMax = new Vector2(0, 1);
            art.pivot = new Vector2(0, 1);
            art.anchoredPosition = new Vector2(TradeRowIconX, TradeRowIconY);
            art.sizeDelta = new Vector2(TradeRowIconSize, TradeRowIconSize);
            var artImage = art.gameObject.AddComponent<RawImage>();
            artImage.texture = icon;
            artImage.raycastTarget = false;
            if (icon == null) artImage.color = Color.clear;
            button.onClick.AddListener(() => onClick());
            return new TradeRow
            {
                ItemId = itemId,
                Rect = rect,
                Background = background,
                Name = name,
                Price = ui.Label(rect, price, TradeRowPriceX, TradeRowPriceY, TradeRowPriceW, TradeRowPriceH, TradeRowPriceSize, UiKit.Ink, TextAlignmentOptions.MidlineLeft),
                Detail = ui.Label(rect, note, TradeRowDetailX, TradeRowDetailY, TradeRowDetailW, TradeRowDetailH, TradeRowDetailSize, UiKit.TradeEmptyGray, TextAlignmentOptions.MidlineRight),
            };
        }

        // 中间：铜钱结算区。
        void BuildSettle(float x, float y, MerchantDef merchant, MerchantState state)
        {
            var panel = ui.Panel(content, "Settle", x, y, TradeSettleW, TradeSettleH, UiKit.TradeSettle, true).rectTransform;
            tradeSettle = panel;
            tradeSettleText = ui.Label(panel, "点左边或右边的一件东西", TradeSettleTextX, TradeSettleTextY, TradeSettleTextW, TradeSettleTextH, TradeSettleTextSize, UiKit.Paper);
            tradeTipPanel = panel;
            ui.Button(panel, "买 一 件", TradeSettleBtnX, TradeSettleBtnFirstY, TradeSettleBtnW, TradeSettleBtnH, () => CommitTrade(1), true);
            ui.Button(panel, "卖 一 件", TradeSettleBtnX, TradeSettleBtnFirstY + TradeSettleBtnPitch, TradeSettleBtnW, TradeSettleBtnH, () => CommitTrade(-1));
            ui.Button(panel, "多买几件", TradeSettleBtnX, TradeSettleBtnFirstY + TradeSettleBtnPitch * 2f, TradeSettleBtnW, TradeSettleBtnSmallH, () => CommitTrade(12));
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

        /// <summary>成交落盘。D-11 把"交易达成"列为三类判定点之一，所以账要记在写档之前；
        /// 映射表目前对交易是空的（KarmaTriggers.FromTrade），因为 0.3 里没有一件善事是
        /// "按市价付了钱"就算的——机制接好了，等一条真正站得住的判定点，不虚记一笔。</summary>
        void PersistTrade(string tradeKind = null)
        {
            RecordKarma(KarmaTriggers.ForTrade(tradeKind));
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
            tradeCoinFrom = buying ? TradeCoinFromBuy : TradeCoinToBuy;
            tradeCoinTo = buying ? TradeCoinToBuy : TradeCoinFromBuy;
            tradeCoin.rectTransform.anchoredPosition = tradeCoinFrom;
            tradeCoinStart = Time.unscaledTime;
            tradeCoin.gameObject.SetActive(true);
        }

        void UpdateTrade()
        {
            if (page != "trade" || tradeCoin == null || tradeCoinStart < 0f) return;
            var t = Mathf.Clamp01((Time.unscaledTime - tradeCoinStart) / TradeCoinFlySeconds);
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
                    ? UiKit.TradeRowPicked
                    : UiKit.TradeRow;
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
