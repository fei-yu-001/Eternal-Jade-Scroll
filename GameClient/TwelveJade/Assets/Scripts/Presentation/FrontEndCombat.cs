using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Combat;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // M4-03 战斗前台：把 Core/Combat.cs 的规则接到可试玩的战斗页。
    // 任务书 K（M6/M7）在这层加了三件事：部位锁敌与弱点暴击的呈现、装备整备与耐久、战败保护转场。
    //
    // 表现层只做输入、动画与显示——伤害、精力、硬直、死亡与奖励全部调 Core；
    // 时间走可注入的战斗时钟：帧循环喂 unscaledDeltaTime，验收脚本用 CombatClockAdvance 直接推进。
    // 玩家战斗属性仍是占位（正式属性随养成系统进 Core/存档，见 09 日志技术债 T017）。
    //
    // 【野猪妖为什么现在也走 2.0 招式表】任务书 K 那一轮它被钉在 moveset=legacy：
    // CombatShot.cs 与 FrontEndShotProps.cs 的"野猪 60→44""12 次重击内击倒"吃的就是 M4-03 那套
    // 固定轻击（伤害 18、三相 .12/.08/.18）。本轮（N1 / D-07）切到 profile2，之所以两条断言仍不破：
    // maxHp=60、def=2 与"轻击打躯干 16 点"这条算式一个字没动，改的是**敌方招式**——
    // 尖牙挑击 power 6（对玩家 14 点）、蛮牛冲撞 power 10（18 点，正好吃满 D-07 的单发上限）+ .45 击退。
    // 冥狼与山魈本来就吃满 2.0；三只妖从此同一套口径，不再有"半接入"的那一只。
    public sealed partial class FrontEndController
    {
        // Reserve the right 400px lotus. This is one shared presentation transform, never a Core X/range change.
        // Boss full 720px quads remain x19.75..1500.25 even at +/-3.25; the wheel starts at1512.
        float StagePxPerUnit => IsMountainLord ? 117f : 150f;
        const float StageCenterX = 760f;
        const float StageGroundY = 960f;
        const float PlayerSpeed = ActionCombatMotor.WalkSpeed;       // 单位/秒

        // 妖兽真图／特写尺寸；保持原资源取图优先级。
        const float CombatPortraitSize = 512f, CombatSilhouetteSize = 420f;

        // N5-EnemyArt 的回退标记尺寸（画布 px）。刻意比降级立绘那一档（313）小一圈：
        // 一块撑满槽位的纯色方更容易被读成"妖就长这样"，而 180 的形状 + 一句字面标注读出来是"这里空着"。
        // 形状与配色不在这儿定，来自同一条遭遇声明（tint / shape，与镇页 T069 同一份配置）。
        const float FoeMarkerW = 180f, FoeMarkerPlateH = 81f;
        const float FoeMarkerLabelW = 320f, FoeMarkerLabelH = 26f, FoeMarkerLabelSize = 18f;

        // One horizontal combat lane; the foe image bottom includes its authored transparent foot inset.
        const float PlayerGroundY = StageGroundY;
        const float FoeGroundY = StageGroundY + 40f;
        const float FoeDepthScale = .98f;
        const float PlayerBodyHeight = 336f;   // 侧向素材保持原比例。

        // ==== N4-Polish 3②／I-5.5／I-5.6：脚点线、地面贴面与着地语言 ====
        // 妖的立绘（Type-C 特写）画的是"扬尘 + 蹄子"，可见底边是扬尘的下缘，蹄子在其上方约 40px。
        // 原来的"地带"是按**立绘底边**铺的，于是蹄子落在地带之上沿之外 40 像素——读起来妖浮在半空。
        // 这条只有看图能发现（B 席与主架构师各抓一次，断言当时全绿），所以：
        //   ① 地带的远端改成按**脚点线**铺（FoeFootY）；
        //   ② 地面不再用 ui.Panel——它钉死 RoundedSprite，一块浅灰圆角矩形读起来就是 UI 面板；
        //      改用仓库里已有的水墨笔触 `Art/Fx/ink-stroke` 铺一条带磨损边的地（真取不到才回退，且会红）；
        //   ③ 主角脚下补同一种墨渍接触影，与妖的扬尘同一种着地表达（I-5.5 要的是"同一屏同一种"）。
        // FoeFootInset 是**素材的脚锚点**，属于美术声明，不能从 alpha 包围盒算出来（扬尘本身就是可见像素）。
        const float FoeFootInset = 40f;
        const float GroundWashAlpha = .30f, FootShadowAlpha = .26f;
        const float PlayerShadowW = 200f, PlayerShadowH = 28f;
        // uv 用整张笔触（0,0,1,1）。第二版试过只切中段（(.02,.42,.96,.20)），
        // **看图否掉了**：那条钩形笔触的中段被切成两块互不相连的墨，左边剩一个硬边三角，
        // 比整张铺更像 UI 阴影；而三条几何断言在两版里都是绿的。
        // ⇒ 这条正好是"断言全绿不等于画面对"的活样本，记进 n4-polish-report §4。
        static readonly Rect WashUv = new Rect(0f, 0f, 1f, 1f);
        static readonly Rect ShadowUv = new Rect(0f, 0f, 1f, 1f);
        // 妖真正踩到地面的那条线（画布 y，自上而下）：立绘底边往上抬一个脚锚点。
        const float FoeFootY = FoeGroundY - FoeFootInset;

        // D-47（T109 选②：不改数值，只落口径与文案）玩家可见的那句话。
        // 单独抽成常量是为了让夹具能逐字钉住它——口径漂了要红，而不是悄悄换成另一句俏皮话。
        const string StaminaHintCopy = "精力是它自己的节拍，打不空";

        // N5-EnemyArt：这只妖的形影图还没到（夜4 未出图或改名把路径打断）时，画面上写得出"这是回退"。
        // 用词里不许出现内部字段名（art / texture / Resources），也不许用中点 U+00B7——
        // 战斗页标签若走手写体 ZhiMangXing 会缺那个字（n4-polish-report §5 实测的 cmap 缺口）。
        const string FoeFallbackCopy = "形影未至，暂以标记代之";

        // 跳字：寿命与上升量都按战斗钟算（Q2-3 附带修的一条时间口径混用）。
        const float FloaterLifeSeconds = .7f, FloaterRise = 46f, FloaterRiseCritical = 62f;

        // 单次命中短白闪；低动态减弱亮度，不做反复频闪或摇屏。
        const float FoeFlashSeconds = .08f;
        // 血条/精力条补间：一秒走满这条余量，减动效开关下直接吸附。
        const float BarTweenPerSecond = 6f;
        const float DefeatFadeSeconds = .9f;

        CombatActorState combatPlayer, combatFoe;
        EnemyDef combatFoeDef;
        EnemyProfile combatFoeProfile;
        EnemyAiState foeAi;
        CombatEquipmentService combatGear;
        string lockedPartId = "";
        Texture2D combatFoePortrait;
        string combatEncounterId = "";
        float combatNow, foeNextActAt, foeFlashUntil;
        bool combatSettled;
        SpoilsResult combatSpoils;
        PuppetActor combatPlayerActor;
        RectTransform combatFoeRoot, combatSettlePanel, combatFadeRect;
        RawImage combatFoeImage;
        // N5-EnemyArt：这只妖此刻读的是真图还是回退标记。回退时立绘组件关掉（绝不留 whiteTexture 白块），
        // 改画遭遇声明里的颜色 + 形状标记，并在标记上写一句"这是回退"。
        bool combatFoeFallback;
        Texture2D combatFoeArt;
        MapEncounter combatFoeEncounter;
        string combatFoeArtSource = "";
        bool combatFoeDeclaredArtMissing;
        string combatFoeMarkerShape = "";
        Image combatFoeMarker;
        TMP_Text combatFoeMarkerLabel;
        Color combatFoeMarkerTint;
        TMP_Text combatPlayerState, combatFoeState, combatPlayerHpText, combatFoeHpText;
        TMP_Text combatFoeStaminaHint;   // D-47 选②落的那句玩家口径（静态文案，不进每帧刷新）
        TMP_Text combatLockLabel, combatGearLabel, combatFoeStaminaText;
        Image combatPlayerHpFill, combatPlayerStaminaFill, combatFoeHpFill, combatFoeStaminaFill, combatFadeImage;
        float playerHpShown, playerStaminaShown, foeHpShown, foeStaminaShown;
        ActionCombatMotor combatPlayerMotor, combatFoeMotor;
        ActionCombatEffects combatEffects;
        BossCombatBrain combatBoss;
        PlayerSkillCombat combatSkills;
        CombatSkillWheel combatSkillWheel;
        CombatSkillEffects combatSkillEffects;
        string combatWheelPill;
        float combatWheelPillRefreshAt;
        public CombatResult CombatSkillForCheck(string id) => PlayerSkillAct(id);
        public float CombatSkillCooldownForCheck(string id) => combatSkills?.CooldownLeft(id, combatNow) ?? 0;
        public bool CombatSkillLearnedForCheck(string id) => combatSkills != null && combatSkills.Learned(activeSave, id);
        public Button CombatSkillButtonForCheck(int slot) => combatSkillWheel != null ? combatSkillWheel.SlotButton(slot) : null;
        public bool CombatSkillLotusReady => combatSkillWheel != null && combatSkillWheel.ArtworkReady;
        public string CombatActiveLearnedSkill => combatSkills != null && combatSkills.OwnsAction(combatPlayer) ? combatSkills.Current.Id : "";
        public float CombatSkillLockedX => combatSkills?.TargetX ?? float.NaN;
        EnemyCombatView combatEnemyView;
        EnemyCombatShadow combatEnemyShadow;
        BossDomainEffects combatBossRearFx, combatBossFrontFx;
        string combatBackgroundSource = "";
        float combatFoeHpWidth = 296f;
        bool IsMountainLord => combatFoeDef != null && combatFoeDef.Id == BossCombatBrain.EnemyId;
        public bool CombatBossActive => combatBoss != null;
        public int CombatBossStage => combatBoss?.Stage ?? 0;
        public string CombatBossSkill => combatBoss?.Cast?.Id ?? "";
        public float CombatBossLockedOrigin => combatBoss?.OriginX ?? float.NaN;
        public float CombatBossLockedTarget => combatBoss?.TargetX ?? float.NaN;
        public Vector2 CombatBossDangerInterval => combatBoss != null ? new Vector2(combatBoss.ZoneMin, combatBoss.ZoneMax) : new Vector2(float.NaN, float.NaN);
        public bool CombatBossWarningVisible => combatBossFrontFx != null && combatBossFrontFx.WarningVisible;
        public int CombatFoeWalkFrame => combatEnemyView != null ? combatEnemyView.WalkFrame : -1;
        public int CombatFoeAttackFrame => combatEnemyView != null ? combatEnemyView.StrikeFrame : -1;
        public float CombatFoeWalkDistance => combatEnemyView != null ? combatEnemyView.StrideDistance : 0;
        public bool CombatFoeWalkArtReady => combatEnemyView != null && combatEnemyView.WalkArtReady;
        public bool CombatBossAttackArtReady => combatEnemyView != null && combatEnemyView.StrikeArtReady;
        public Vector2 CombatFoeRegisteredFoot() => combatEnemyView != null ? combatEnemyView.RegisteredFootInParent() : new Vector2(float.NaN, float.NaN);
        public string CombatArenaArtSource => combatBackgroundSource;
        ActionCombatPose combatPose;
        ActionCombatHitFlash combatPlayerFlash, combatFoeFlash;
        RectTransform combatGearDrawer;
        bool combatGearOpen, combatPointerLeft, combatPointerRight, combatNativeLeft, combatReleased;
        int combatInputFrame = -1;
        float combatInputMove, combatPlayerFlashUntil, combatObservedPlayerX;
        int? wakeHpNext;
        readonly List<(RectTransform rect, TMP_Text text, float born, bool critical, Vector2 from)> combatFloaters = new();

        // 只给验收与调试用的计数器：不做玩法判定，只证明"这条路真的走过"。
        int combatCriticalCount, combatWhiffCount, combatDodgeCount, combatRiposteCount, combatExposureCount;

        public int CombatPlayerFacing => combatPlayerMotor?.Facing ?? 0;
        public int CombatFoeFacing => combatFoeMotor?.Facing ?? 0;
        public int CombatActionSequence => combatPlayerMotor?.Sequence ?? 0;
        public void CombatFaceForCheck(int facing) { if (combatPlayer?.Phase == CombatPhase.Idle) combatPlayerMotor?.Face(facing); }
        public int CombatComboStep => combatPlayerMotor?.ComboStep ?? 0;
        public bool CombatActionBuffered => combatPlayerMotor?.HasBufferedAction ?? false;
        public bool CombatGearDrawerOpen => combatGearDrawer != null && combatGearDrawer.gameObject.activeInHierarchy;
        public int CombatSlashCount => combatEffects != null ? combatEffects.SlashCount : 0;
        public int CombatImpactCount => combatEffects != null ? combatEffects.ImpactCount : 0;
        public bool CombatEnemyWarningVisible => CombatBossWarningVisible || (combatEffects != null && combatEffects.WarningVisible);
        public bool CombatFoeWhiteFlashReady => combatFoeFlash != null && combatFoeFlash.HasSilhouette;
        public float CombatFoeWhiteFlashStrength => combatFoeFlash != null ? combatFoeFlash.Strength : 0;
        public bool CombatPlayerAttackVisible => combatPose != null && combatPose.StrikeVisible;
        public int CombatPlayerAttackFrame => combatPose != null ? combatPose.StrikeFrame : -1;
        public bool CombatPlayerAttackMirrored => combatPose != null && combatPose.StrikeMirrored;
        public bool CombatTravelHiddenDuringAttack => combatPose != null && combatPose.TravelRenderersHidden;
        public bool CombatAttackWhiteFlashActive => combatPose != null && combatPose.StrikeWhiteFlashActive;
        public Vector2 CombatAttackFootPosition() => combatPose != null ? combatPose.StrikeFootInParent() : new Vector2(float.NaN, float.NaN);
        public int CombatPlayerFrame => combatPlayerActor != null ? combatPlayerActor.FrameIndexForCheck : -1;
        public float CombatGaitDistance => combatPlayerActor != null ? combatPlayerActor.StrideDistanceForCheck : 0;
        public void CombatMoveForCheck(float direction, float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0 || float.IsNaN(direction) || float.IsInfinity(direction)) return;
            var previous = combatInputMove;
            try { combatInputMove = Mathf.Clamp(direction, -1, 1); AdvanceActionCombatClock(seconds, false); }
            finally { combatInputMove = previous; }
        }
        public float CombatNow => combatNow;
        public CombatActorState CombatPlayer => combatPlayer;
        public CombatActorState CombatFoe => combatFoe;
        public bool CombatSettled => combatSettled;
        public SpoilsResult LastSpoils => combatSpoils;
        public string CombatPlayerPhase => combatPlayer?.Phase.ToString() ?? "";
        public string CombatFoePhase => combatFoe?.Phase.ToString() ?? "";
        /// <summary>验收钩子：当前锁定的打击部位（空 = 平攻默认部位）。</summary>
        public string CombatLockedPartId => lockedPartId ?? "";
        /// <summary>验收钩子：本场妖兽的 2.0 生态档（null = 该妖没登记 2.0 数据）。</summary>
        public EnemyProfile CombatFoeProfile => combatFoeProfile;
        /// <summary>验收钩子：这一局的战力与耐久服务（未开战时为 null）。</summary>
        public CombatEquipmentService CombatGear => combatGear;
        public bool CombatCounterReady => combatPlayer != null && combatPlayer.CounterReady;
        public int CombatCriticalCount => combatCriticalCount;
        public int CombatWhiffCount => combatWhiffCount;
        public int CombatDodgeCount => combatDodgeCount;
        public int CombatRiposteCount => combatRiposteCount;
        public int CombatExposureCount => combatExposureCount;
        public float CombatFoeFlashUntil => foeFlashUntil;
        /// <summary>验收钩子：精力条当前画出来的比例（补间中间值，不是真实精力）。</summary>
        public float CombatPlayerStaminaBarRatio => playerStaminaShown;
        /// <summary>验收钩子：血条当前画出来的比例。</summary>
        public float CombatPlayerHpBarRatio => playerHpShown;
        public DefeatSettlement LastDefeatSettlement { get; private set; }

        EnemyTable CombatEnemies
        {
            get
            {
                if (combatEnemies != null) return combatEnemies;
                var asset = Resources.Load<TextAsset>("Config/enemies");
                if (asset == null) throw new InvalidOperationException("妖兽表缺失：Resources/Config/enemies.json。");
                return combatEnemies = EnemyTable.Parse(asset.text);
            }
        }
        EnemyTable combatEnemies;

        // 妖兽 2.0 表：读不出来不能让战斗入口整个失效——
        // 表现层退回 legacy（倍率 1、无抵抗、无专属招式），并把原因如实留在日志里。
        // 失败是粘性的：只警告一次，绝不在每场战斗上重复刷错误（那是把降级刷成噪音）。
        EnemyProfileTable CombatProfiles
        {
            get
            {
                if (combatProfiles != null || combatProfilesFailed) return combatProfiles;
                try
                {
                    var table = EnemyProfileTable.Load(EnemiesJson);
                    table.CrossCheck(CombatEnemies);
                    table.CrossCheckDrops(Table, CombatEnemies);
                    combatProfiles = table;
                }
                catch (Exception ex)
                {
                    combatProfilesFailed = true;
                    Debug.LogWarning("[FrontEndCombat] 妖兽 2.0 表对表未过，战斗退回 legacy 口径：" + ex.Message);
                }
                return combatProfiles;
            }
        }
        EnemyProfileTable combatProfiles;
        bool combatProfilesFailed;
        string EnemiesJson
        {
            get
            {
                var asset = Resources.Load<TextAsset>("Config/enemies");
                if (asset == null) throw new InvalidOperationException("妖兽表缺失：Resources/Config/enemies.json。");
                return asset.text;
            }
        }

        // 装备档位表：物品表读不到就当作"没有可穿的"，开战不受影响。
        GearCatalog CombatGearCatalog
        {
            get
            {
                if (combatGearCatalog != null || combatGearCatalogFailed) return combatGearCatalog;
                try { combatGearCatalog = GearCatalog.ForTown(Table); }
                catch (Exception ex)
                {
                    combatGearCatalogFailed = true;
                    Debug.LogWarning("[FrontEndCombat] 装备档位表对表未过，本场不可整备：" + ex.Message);
                }
                return combatGearCatalog;
            }
        }
        GearCatalog combatGearCatalog;
        bool combatGearCatalogFailed;

        // 玩家战斗属性占位：出身只给一点风味差异，正式数值随养成系统落 Core/存档（T017）。
        static (int hp, int attack, int defense, int stamina) PlayerStatsFor(string characterId) =>
            characterId == "traveller" ? (55, 7, 2, 60) :
            characterId == "merchant" ? (50, 6, 2, 70) : (60, 8, 1, 60);

        public void ShowCombat(string encounterId)
        {
            ShowWorldCombat(encounterId);
        }

        void ShowLegacyCombat(string encounterId)
        {
            if (activeSave == null) { Notify("先落笔创建一位行旅人，再战。"); return; }
            EnemyTable enemies;
            try { enemies = CombatEnemies; }
            catch (InvalidOperationException ex) { Notify(ex.Message); return; }
            // 敌人来自遭遇表，不再写死 boar-demon：写死会让"新增了遭遇却还在打野猪"
            // 这种错误看起来像正常开战。遭遇表里查不到就拒开战，而不是退回默认敌人。
            // 默认值同样是硬编码的温床，因此签名里不留默认值。
            //
            // 遭遇表挂在世界配置上，而世界只在进城镇页时才加载。调用方里有"建档后直接开战"
            // 的路径（CombatShot 验收就走这条），此时 World 还是 null，任何遭遇 id 都会查不到，
            // 战斗入口静默失效。这里自己补一次加载——读不出来时 LoadWorld 已提示过，直接返回。
            if (World == null && !LoadWorld()) return;
            var encounter = World?.Encounters.FirstOrDefault(e => e.Id == encounterId);
            if (encounter == null)
            {
                // 诊断信息带上世界状态与遭遇条数：只说"查不到"分不清是配置没配、
                // 世界没加载，还是 Resources 读到了旧版本——这三种修法完全不同。
                Debug.LogError("[FrontEndCombat] 遭遇表里没有 " + encounterId + "，拒绝开战（World=" +
                    (World == null ? "null" : "loaded") + "，遭遇数=" + (World?.Encounters.Count ?? -1) + "）。");
                Notify("此处并无可战之事（遭遇 " + encounterId + " 不在遭遇表里）。");
                return;
            }
            var foeDef = enemies.Find(encounter.Enemy);
            if (foeDef == null) { Notify("妖兽表里没有" + encounter.Enemy + "。"); return; }

            var selectedProfile = LoadProfileFor(foeDef.Id);
            if (foeDef.Id == BossCombatBrain.EnemyId && (selectedProfile == null ||
                selectedProfile.FindMove(BossCombatBrain.SweepId) == null || selectedProfile.FindMove(BossCombatBrain.FissureId) == null || selectedProfile.FindMove(BossCombatBrain.StoneId) == null))
            { Debug.LogError("[BossCombat] Missing registered mountain-lord moves."); Notify("山君招式资料尚未齐备。"); return; }
            CloseInventory();
            BeginPage("combat");
            combatReleased = false;
            combatPlayerMotor = new ActionCombatMotor(1); combatFoeMotor = new ActionCombatMotor(-1);
            combatPointerLeft = combatPointerRight = combatGearOpen = false; combatInputMove = 0;
            combatInputFrame = Time.frameCount; combatPlayerFlashUntil = 0;
            combatFadeImage = null; combatFadeRect = combatSettlePanel = combatGearDrawer = null;
            content.gameObject.AddComponent<ActionCombatLifetime>().Configure(ReleaseActionCombat);
            combatEncounterId = encounterId ?? "";
            combatFoeDef = foeDef;
            combatFoeProfile = selectedProfile;
            combatBoss = IsMountainLord ? new BossCombatBrain(combatFoeProfile) : null;
            combatSkills = new PlayerSkillCombat(); combatSkillWheel = null; combatSkillEffects = null;
            combatWheelPill = null; combatWheelPillRefreshAt = 0;
            combatEnemyView = null; combatEnemyShadow = null; combatBossRearFx = combatBossFrontFx = null;
            combatNow = 0f;
            foeNextActAt = 1.2f;
            foeAi = EnemyAi.NewState(1.2f);
            combatSettled = false;
            combatSpoils = null;
            LastDefeatSettlement = null;
            combatFloaters.Clear();
            combatCriticalCount = combatWhiffCount = combatDodgeCount = combatRiposteCount = combatExposureCount = 0;
            lockedPartId = "";
            foeFlashUntil = 0f;
            var stats = PlayerStatsFor(activeSave.characterId);
            stats.hp += CultivationRules.HpBonus(activeSave) + ManualStudyRules.HpBonus(activeSave);
            stats.stamina += CultivationRules.StaminaBonus(activeSave) + ManualStudyRules.StaminaBonus(activeSave);
            stats.attack += ManualStudyRules.AttackBonus(activeSave);
            combatPlayer = new CombatActorState("player", activeSave.characterName, stats.hp, stats.attack, stats.defense, stats.stamina);
            combatFoe = new CombatActorState(foeDef.Id, foeDef.Name, foeDef.MaxHp, foeDef.Attack, foeDef.Defense, foeDef.MaxStamina);
            // Q2/T072：挂了 2.0 表的妖就吃 2.0 档案，与玩家那一侧 combatGear.ApplyTo 读的是**同一个**
            // CombatDepth.Standard20（不是第二真源，是一份常量两处读）。
            // 为什么必须挂（实测见 CombatEngineShot 的"实测三"）：冥狼 110 精力 / 每 1.15 秒一下、
            // 山魈 70 精力 / 每 2.2 秒一下，按基线 6/秒 回精撑不满 30 秒就抬不起爪子——
            // 上一轮 T076 补的 Regenerate(combatFoe, dt) 只把野猪（50 精 / 1.5 秒 / 每下 8 点）救了一半。
            // 顺带把"玩家 25/秒 vs 妖 6/秒"的 4.17 倍差归零：D-24 要求同级节奏差 ≤2 倍。
            combatFoe.Tuning = TuningForFoe(combatFoeProfile);
            combatPlayer.X = -1.2f;
            combatFoe.X = IsMountainLord ? 1.55f : 1.2f;

            // 装备/命途在这一刻折算进战斗修正量：裸装裸命时两个 Modifier 都是 0，
            // 伤害算术与 M4-03 逐字相同（既有夹具吃的就是这一条）。
            combatGear = new CombatEquipmentService(CombatGearCatalog, CombatDestinyTable.Standard, activeSave.destiny);
            combatGear.RestoreLedger(activeSave);
            combatGear.ApplyTo(combatPlayer, stats.attack, stats.defense);

            // 战败醒来：上一场被打趴的话，这一场开场只有三成气血（至少留一口气）。
            if (wakeHpNext.HasValue)
            {
                CombatRules.RestoreHp(combatPlayer, wakeHpNext.Value);
                wakeHpNext = null;
            }
            playerHpShown = (float)combatPlayer.Hp / combatPlayer.MaxHp;
            playerStaminaShown = (float)combatPlayer.Stamina / combatPlayer.MaxStamina;
            foeHpShown = 1f;
            foeStaminaShown = 1f;

            combatBackgroundSource = IsMountainLord ? "Art/Background/qingshi-boss-domain" : "Art/Background/memo-terrace";
            var painted = Resources.Load<Texture2D>(combatBackgroundSource);
            if (painted == null && IsMountainLord)
            {
                Debug.LogWarning("[BossCombat] Stone-valley background is not installed; using the existing terrace temporarily.");
                combatBackgroundSource = "Art/Background/memo-terrace";
                painted = Resources.Load<Texture2D>(combatBackgroundSource);
            }
            if (painted != null)
            {
                float h = 1920f * painted.height / painted.width;
                var background = ui.Rect(content, "Action combat painted terrace", 0, 1080 - h, 1920, h).gameObject.AddComponent<RawImage>();
                background.texture = painted; background.raycastTarget = false;
            }
            else Debug.LogError("[ActionCombat] Missing authored battle background: Art/Background/memo-terrace");
            ui.Stretch(content, "Action combat HUD edge wash").gameObject.AddComponent<ActionCombatVignette>();
            combatGroundTex = InkWashTexture();
            combatGroundWash = InkWash(content, combatGroundTex, 170, FoeFootY - 12, 1580,
                42, new Color(.17f, .21f, .18f, GroundWashAlpha), WashUv);
            combatPlayerShadow = InkWash(content, combatGroundTex,
                StageX(combatPlayer.X) - PlayerShadowW / 2, PlayerGroundY - PlayerShadowH + 8,
                PlayerShadowW, PlayerShadowH, new Color(.13f, .16f, .14f, FootShadowAlpha), ShadowUv, mirror: true);

            combatEnemyShadow = ui.Rect(content, "Enemy actual foot contact shadow", 0, 0, 1920, 1080).gameObject.AddComponent<EnemyCombatShadow>();
            if (combatBoss != null)
            {
                combatBossRearFx = ui.Rect(content, "Mountain lord rear domain glow", 0, 0, 1920, 1080).gameObject.AddComponent<BossDomainEffects>();
                combatBossRearFx.Configure(false, StageCenterX, StagePxPerUnit, FoeFootY);
            }
            var preset = System.Array.Find(presets, p => p.id == activeSave.characterId) ?? presets[0];
            var entry = TownCharacterArt.Find("hero-" + (activeSave.gender == "female" ? "female" : "male") + "-side");
            var stand = TownCharacterArt.Hero(activeSave.gender, "side", false);
            var walk = TownCharacterArt.Hero(activeSave.gender, "side", true);
            var view = stand != null ? stand : preset.Facing(0, activeSave.gender, activeSave.faceStyle);
            combatPlayerActor = PuppetActor.Create(content, view,
                new Vector2(StageX(combatPlayer.X), -PlayerGroundY),
                new Vector2(PlayerBodyHeight * (view == null ? .75f : (float)view.width / view.height), PlayerBodyHeight));
            if (walk != null && entry != null)
            {
                combatPlayerActor.SetFrameSheet(walk, entry.frames, entry.cycleDistance);
                combatPlayerActor.SetGaitMode(PuppetActor.GaitMode.Frames);
            }
            combatNativeLeft = entry != null && entry.nativeFacing == "left";
            combatPlayerActor.SetGaitConveyor(0); combatPlayerActor.SetMotion(PuppetActor.Motion.Idle);
            combatPlayerActor.SetMirror(combatNativeLeft); combatObservedPlayerX = combatPlayer.X;
            var strikes = ApprovedUiArt.Load("nov10/combat-hero-" + (activeSave.gender == "female" ? "female" : "male") + "-strikes");
            combatPose = combatPlayerActor.gameObject.AddComponent<ActionCombatPose>();
            combatPose.Configure(combatPlayerActor, strikes, PlayerBodyHeight);
            combatPlayerFlash = combatPlayerActor.gameObject.AddComponent<ActionCombatHitFlash>();
            combatPlayerFlash.Configure(combatPlayerActor.transform, view, walk, strikes);

            // ==== N5-EnemyArt：妖兽形影的取图三步（声明 → 约定 → 可见回退）====
            // ① 声明档：读遭遇表里那句 `art`。镇页画的与战斗页画的必须是**同一份声明**——
            //    以前这里按 id 反推 `Art/NPC/{id}`，等于给"这只妖用哪张图"设了第二个真源：
            //    D-51 把 shanxiao.png 改名成 stone-mandrill.png 那一笔，靠的就是"文件名必须等于 id"这条
            //    没写进配置的约定，改名时漏改一处就会静默换图（本轮把它收掉，只留 Type-C 那一档约定）。
            // ② 特写档：ADR-025 的 Type-C 图按 id 命名放 Resources/Art/Combat/{enemyId}，配置里没有字段，
            //    只能按约定取；命中它才放大到 512 并加待机呼吸。
            // ③ 两处都取不到 = 夜4 的图还没到 ⇒ 画**看得出是回退**的标记（颜色与形状沿用同一条遭遇声明，
            //    与镇页 T069 同一口径），绝不落回 Texture2D.whiteTexture：
            //    一块白方贴在那里，"图没接上"就读起来像"这只妖长得就是一块白"。
            // 本仓 textureType 全局是 Default（Editor/ArtImportGuard.cs:34 明文设的，NPC/Props/Portraits 同为一档），
            // 所以取图一律用 Load<Texture2D> 而不是 Load<Sprite>——T090 那条：Default 资源用 Load<Sprite>
            // 必返 null，特写档从写下那天起就没成立过。改 .meta 不算修好：下次导入会被按回 Default。
            combatFoeEncounter = encounter;
            combatFoeDeclaredArtMissing = false;
            combatFoeArtSource = "";
            Texture2D foeArt = null;
            var declaredArt = combatFoeEncounter?.Art;
            if (!string.IsNullOrEmpty(declaredArt))
            {
                foeArt = Resources.Load<Texture2D>(declaredArt);
                if (foeArt == null)
                {
                    combatFoeDeclaredArtMissing = true;
                    Debug.LogError("战斗页取不到遭遇声明的形影：Resources/" + declaredArt + "（" +
                        combatFoeEncounter.Id + "）——镇页与战斗页读的是同一句声明，这里取不到就是改名或漏件；" +
                        "已回退成标记，但这不算图到位。");
                }
                else combatFoeArtSource = declaredArt;
            }
            var foeCloseUp = foeArt != null ? null
                : Resources.Load<Texture2D>("Art/Combat/" + foeDef.Id);
            if (foeArt == null && foeCloseUp != null) combatFoeArtSource = "Art/Combat/" + foeDef.Id;
            if (foeArt == null) foeArt = foeCloseUp;
            var foeSize = IsMountainLord ? 720f : (foeCloseUp != null ? CombatPortraitSize : CombatSilhouetteSize) * FoeDepthScale;
            combatFoePortrait = foeCloseUp;
            combatFoeRoot = new GameObject("Foe " + foeDef.Id, typeof(RectTransform)).GetComponent<RectTransform>();
            combatFoeRoot.SetParent(content, false);
            combatFoeRoot.anchorMin = combatFoeRoot.anchorMax = new Vector2(0, 1);
            combatFoeRoot.pivot = new Vector2(.5f, 0f);
            combatFoeRoot.sizeDelta = new Vector2(foeSize, foeSize);
            combatFoeImage = combatFoeRoot.gameObject.AddComponent<RawImage>();
            combatFoeImage.uvRect = new Rect(1f, 0f, -1f, 1f);
            combatFoeImage.raycastTarget = false;
            combatFoeArt = foeArt;
            ApplyFoeArt();
            var foeWalk = Resources.Load<Texture2D>("Art/Characters/Nov11/" + foeDef.Id + "-walk");
            var foeStrikes = IsMountainLord ? Resources.Load<Texture2D>("Art/Characters/Nov11/" + foeDef.Id + "-strikes") : null;
            combatEnemyView = combatFoeRoot.gameObject.AddComponent<EnemyCombatView>();
            combatEnemyView.Configure(combatFoeImage, foeArt, foeWalk, foeStrikes, foeSize, IsMountainLord ? foeSize * EnemyCombatFrameClock.FootInset / EnemyCombatFrameClock.CellPixels : FoeFootInset);
            combatFoeFlash = combatFoeRoot.gameObject.AddComponent<ActionCombatHitFlash>();
            combatFoeFlash.Configure(combatFoeRoot, foeArt, foeWalk, foeStrikes);
            // At close range the large ape stays behind the foreground hero; all telegraphs remain above both.
            combatPlayerActor.transform.SetAsLastSibling();
            if (combatBoss != null)
            {
                combatBossFrontFx = ui.Rect(content, "Mountain lord foreground telegraph and impact", 0, 0, 1920, 1080).gameObject.AddComponent<BossDomainEffects>();
                combatBossFrontFx.Configure(true, StageCenterX, StagePxPerUnit, FoeFootY);
            }
            combatEffects = ui.Rect(content, "Action combat blade and telegraph", 0, 0, 1920, 1080).gameObject.AddComponent<ActionCombatEffects>();
            var skillFxRoot = ui.Rect(content, "Player learned skill effects", 0, 0, 1920, 1080);
            combatSkillEffects = skillFxRoot.gameObject.AddComponent<CombatSkillEffects>();
            combatSkillEffects.Configure(ui, skillFxRoot, StageCenterX, StagePxPerUnit, PlayerGroundY);
            BuildCombatBars();
            RefreshCombatView(0);
            FocusFirst();
        }

        // 2.0 表读不到（或这只妖没登记）时退成 null：部位锁敌与专属招式自动关掉。
        EnemyProfile LoadProfileFor(string enemyId) => CombatProfiles?.Find(enemyId);

        // 把"这一场的妖此刻该画成什么"落一遍：取到图就走立绘组件，取不到就画回退标记。
        // 验收钩子 CombatSetFoeFallbackForCheck 也走这里——强制回退用的是**同一段画法**，
        // 不是另写一条测试专用路径；否则夹具验的是测试代码，玩家看到的还是老那块白。
        void ApplyFoeArt()
        {
            if (combatFoeMarker != null) Destroy(combatFoeMarker.gameObject);
            if (combatFoeMarkerLabel != null) Destroy(combatFoeMarkerLabel.gameObject);
            combatFoeMarker = null;
            combatFoeMarkerLabel = null;
            combatFoeFallback = combatFoeArt == null;
            // 关掉而不是塞 whiteTexture：留一张 whiteTexture 就是"静默画成一个圈"的那一档。
            // 组件留着不删，根 rect 也就还在——脚点、身高两类钩子不必为回退档分叉。
            combatFoeImage.enabled = !combatFoeFallback;
            combatFoeImage.texture = combatFoeFallback ? null : combatFoeArt;
            if (!combatFoeFallback) return;

            var shape = combatFoeEncounter?.Shape;
            if (string.IsNullOrEmpty(shape)) shape = "ring";
            combatFoeMarkerShape = shape;
            combatFoeMarkerTint = EncounterTint(combatFoeEncounter?.Tint);   // 复用镇页那份解析，不另写第二份
            var rotated = shape == "diamond";
            var h = shape == "plate" ? FoeMarkerPlateH : FoeMarkerW;
            // 标记的"脚"就是它的可见下缘（立绘那 40px 扬尘空白是素材的事，标记没有）。
            // 菱形转 45° 之后最低点是一个角，比正方形下缘低 s(√2−1)/2 ⇒ 中心按旋转后的下缘高度抬。
            var centerY = FoeFootInset + (rotated ? FoeMarkerW * Mathf.Sqrt(2f) * .5f : h * .5f);

            var rect = new GameObject("Foe fallback marker", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(combatFoeRoot, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(FoeMarkerW, h);
            rect.anchoredPosition = new Vector2(0f, centerY);
            if (rotated) rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            combatFoeMarker = rect.gameObject.AddComponent<Image>();
            combatFoeMarker.sprite = UiKit.RoundedSprite();
            combatFoeMarker.type = Image.Type.Sliced;
            combatFoeMarker.color = combatFoeMarkerTint;
            combatFoeMarker.raycastTarget = false;

            // 字面标注挂在根 rect 上、与标记同一个中心，而不是标记的孩子——
            // 挂在菱形下面那行字会跟着转 45°，玩家读到的是斜排的字。
            combatFoeMarkerLabel = ui.Label(combatFoeRoot, FoeFallbackCopy, 0f, 0f,
                FoeMarkerLabelW, FoeMarkerLabelH, FoeMarkerLabelSize, UiKit.White, TextAlignmentOptions.Center);
            var labelRect = combatFoeMarkerLabel.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(.5f, 0f);
            labelRect.pivot = new Vector2(.5f, .5f);
            labelRect.anchoredPosition = new Vector2(0f, centerY);
        }

        void BuildCombatBars()
        {
            CombatText(content, "Combat player name", combatPlayer.Name, 74, 42, 420, 42, 30, true);
            CombatText(content, "Combat enemy name", combatFoe.Name, IsMountainLord ? 600 : 1430, 42,
                IsMountainLord ? 720 : 416, 46, IsMountainLord ? 36 : 30, true,
                IsMountainLord ? TextAlignmentOptions.Center : TextAlignmentOptions.TopRight);
            combatPlayerHpFill = CombatBar(76, 98, UiKit.Jade, out combatPlayerHpText);
            combatPlayerStaminaFill = CombatBar(76, 148, UiKit.Gold, out _);
            combatPlayerState = CombatText(content, "Combat player phase", "", 76, 208, 560, 36, 22);
            combatFoeHpWidth = IsMountainLord ? 836f : 296f;
            combatFoeHpFill = CombatBar(IsMountainLord ? 540 : 1544, 98, new Color(.76f, .28f, .19f, 1), out combatFoeHpText, IsMountainLord ? 840 : 300);
            if (IsMountainLord)
            {
                combatFoeHpFill.transform.parent.name = "Mountain lord independent health bar";
                var half = ui.Rect(combatFoeHpFill.transform.parent, "Half health phase threshold", 418, -3, 3, 22).gameObject.AddComponent<Image>();
                half.color = new Color(.88f, .82f, .51f, .9f); half.raycastTarget = false;
            }
            combatFoeStaminaFill = CombatBar(1544, 148, new Color(.43f, .67f, .65f, 1), out combatFoeStaminaText);
            combatFoeState = CombatText(content, "Combat foe telegraph name", "", IsMountainLord ? 610 : 1250, 208,
                IsMountainLord ? 700 : 594, 38, IsMountainLord ? 27 : 23, false,
                IsMountainLord ? TextAlignmentOptions.Center : TextAlignmentOptions.TopRight);
            combatFoeStaminaHint = CombatText(content, "Combat foe stamina meaning", StaminaHintCopy, 1394, 250, 450, 30, 18, false, TextAlignmentOptions.TopRight);
            combatLockLabel = CombatText(content, "Combat selected weak point", "", 610, 152, 700, 42, 22, false, TextAlignmentOptions.Center);
            CombatCommand(content, "Combat withdraw", "撤离", 1620, 294, 220, 72, LeaveActionCombat);
            CombatCommand(content, "Combat preparation", "整备 · E", 76, 294, 236, 72, ToggleCombatGear);

            var left = CombatCommand(content, "Combat hold left", "左行", 76, 1000, 172, 64, () => NudgePlayer(-1));
            left.gameObject.AddComponent<ActionCombatHold>().Configure(value => combatPointerLeft = value);
            var right = CombatCommand(content, "Combat hold right", "右行", 252, 1000, 172, 64, () => NudgePlayer(1));
            right.gameObject.AddComponent<ActionCombatHold>().Configure(value => combatPointerRight = value);
            combatSkillWheel = CombatSkillWheel.Create(ui, content, ReadCombatSkillSlot, ActivateCombatSkillSlot, () => settings.reduceMotion, PlayClick);
            CombatText(content, "Combat input hint", IsMountainLord ? "横扫退避 · 裂地闪身 · 落石先锁定后落下" : "A / D 移动　连续轻击衔接三式　闪身可穿过敌人　Tab 察隙",
                440, IsMountainLord ? 354 : 256, 880, 38, 20, false, TextAlignmentOptions.Center);

            combatGearDrawer = ui.Rect(content, "Combat preparation drawer", 360, 260, 1200, 600);
            ImmortalUi.Art(ui, combatGearDrawer, "Combat preparation scroll", "nov8/tianji-scroll-fairyland", 0, 0, 1200, 600, true);
            combatGearLabel = ImmortalUi.Text(ui, combatGearDrawer, "Combat gear ledger", "", 210, 150, 780, 144, 23, UiKit.Ink);
            CombatCommand(combatGearDrawer, "Combat equip weapon", "兵刃", 210, 330, 228, 70, () => EquipBest(GearSlot.Weapon));
            CombatCommand(combatGearDrawer, "Combat equip armor", "护具", 486, 330, 228, 70, () => EquipBest(GearSlot.Armor));
            CombatCommand(combatGearDrawer, "Combat equip accessory", "配饰", 762, 330, 228, 70, () => EquipBest(GearSlot.Accessory));
            CombatCommand(combatGearDrawer, "Combat repair gear", "磨刃", 350, 428, 228, 70, RepairGear);
            CombatCommand(combatGearDrawer, "Combat resume", "回到战局", 622, 428, 260, 70, ToggleCombatGear);
            combatGearDrawer.gameObject.SetActive(false);
            RefreshCombatLock(); RefreshCombatGearLabel();
        }

        TMP_Text CombatText(Transform parent, string name, string value, float x, float y, float w, float h,
            float size, bool brush = false, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var text = ImmortalUi.Text(ui, parent, name, value, x, y, w, h, size, UiKit.Hex("F0E6CC"), brush, align);
            text.extraPadding = false; text.margin = new Vector4(2, 2, 2, 2); text.raycastTarget = false; return text;
        }
        Button CombatCommand(Transform parent, string name, string value, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
            => MenuMemoArt.Command(ui, parent, name, value, new Rect(x, y, w, h), action, () => settings.reduceMotion, PlayClick, 25, false);

        void ToggleCombatGear()
        {
            if (combatSettled || combatGearDrawer == null) return;
            combatGearOpen = !combatGearOpen; combatInputMove = 0; combatPointerLeft = combatPointerRight = false;
            combatPlayerMotor?.ClearInput(); combatGearDrawer.gameObject.SetActive(combatGearOpen);
            if (combatGearOpen) { combatGearDrawer.SetAsLastSibling(); RefreshCombatGearLabel(); }
        }
        void ReleaseActionCombat()
        {
            combatPointerLeft = combatPointerRight = combatGearOpen = false; combatInputMove = 0;
            combatPlayerMotor?.ClearInput(); combatFoeMotor?.ClearInput();
            combatBoss?.Cancel(); combatSkills?.Cancel();
            if (combatReleased) return; combatReleased = true;
            if (combatPlayer != null && !combatSettled && activeSave != null) { RecordGearLedger(); PersistBag(); }
        }
        void LeaveActionCombat() { ReleaseActionCombat(); ShowTown(); }

        Image CombatBar(float x, float y, Color color, out TMP_Text label, float width = 300)
        {
            var back = ui.Panel(content, "Bar back", x, y, width, 16, new Color(.2f, .24f, .22f, .25f));
            var fillRect = ui.Rect(back.transform, "Fill", 2, 2, width - 4, 12);
            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = color;
            fill.raycastTarget = false;
            fillRect.anchorMin = fillRect.anchorMax = new Vector2(0, .5f);
            fillRect.pivot = new Vector2(0, .5f);
            fillRect.anchoredPosition = Vector2.zero;
            // 数字放血条下方：上方留给名字，避免两行文字打架。
            label = CombatText(back.transform, "Combat numeric value", "", 0, 18, width, 28, 20);
            return fill;
        }

        // 战斗钟：帧循环喂真实时长，验收脚本用 CombatClockAdvance 按需推进（可注入、可暂停）。
        // 内部切成 ≤0.03s 的小步：一次掉帧 0.5s 会整段跨过 0.08s 的命中窗，让这一击凭空挥空。
        public void CombatClockAdvance(float seconds) => AdvanceActionCombatClock(seconds, true);
        void AdvanceActionCombatClock(float seconds, bool sampleInput)
        {
            if (page != "combat" || combatPlayer == null || combatFoe == null || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0) return;
            // Poll button edges ONCE per rendered frame, before subdividing the injected clock.
            if (sampleInput && combatInputFrame != Time.frameCount) { combatInputFrame = Time.frameCount; ReadCombatInput(0); }
            if (combatGearOpen || modal != null || bagOverlay != null) return;
            var left = Mathf.Max(0f, seconds); var guard = 0;
            while (left > 0f && guard++ < 4000 && page == "combat")
            {
                var step = Mathf.Min(left, .03f); UpdateCombat(step); left -= step;
            }
        }

        // 点击可转向；按住由 ActionCombatHold 喂给同一移动输入。
        void NudgePlayer(float direction) { if (!combatGearOpen && ActionCombatMotor.CanWalk(combatPlayer)) combatPlayerMotor?.Face(direction); }

        // 验收钩子：把双方摆到指定站位（Core 坐标），与 SetCoinsForCheck 同一套约定。
        public void CombatPlaceForCheck(float playerX, float foeX)
        {
            if (combatPlayer == null || combatFoe == null) return;
            combatPlayer.X = playerX;
            combatFoe.X = foeX;
            combatObservedPlayerX = playerX;
        }

        /// <summary>验收钩子：本场战斗对应的遭遇 id（讨伐标记就是往存档里记这个）。</summary>
        public string CombatEncounterId => combatEncounterId;
        /// <summary>验收钩子：妖兽特写是否走了 Type-C 大图路径（false = 降级为剪影）。</summary>
        public bool CombatPortraitShown => combatFoePortrait != null;
        /// <summary>验收钩子：这一场的妖走的是回退绘制（true）还是读到了真图（false）。</summary>
        public bool CombatFoeArtIsFallback => combatFoeFallback;
        /// <summary>验收钩子：真图从哪一句取到的（遭遇声明的 art / Art/Combat 约定档；空 = 没取到）。</summary>
        public string CombatFoeArtSource => combatFoeArtSource ?? "";
        /// <summary>验收钩子：遭遇声明写了 art 却取不到（改名打断路径或漏件）——这条绝不许静默。</summary>
        public bool CombatFoeDeclaredArtMissing => combatFoeDeclaredArtMissing;
        /// <summary>验收钩子：立绘组件此刻真的画着一张非白块的图（回退档为 false）。</summary>
        public bool CombatFoePictureVisible => combatFoeImage != null && combatFoeImage.gameObject.activeInHierarchy && combatFoeImage.enabled &&
            combatFoeImage.texture != null && combatFoeImage.texture != Texture2D.whiteTexture;
        /// <summary>验收钩子：回退标记的形状（来自遭遇声明；没有标记在场时为空）。
        /// 判据是"标记在不在"，不是"我打算怎么画"——后者会把上一次回退留下的残值当成本次状态。</summary>
        public string CombatFoeMarkerShape => combatFoeMarker != null ? combatFoeMarkerShape : "";
        /// <summary>验收钩子：回退标记此刻的配色（来自遭遇声明的 tint，与镇页同源）。
        /// 没有标记在场时回 Color.clear ——同样不拿残值冒充当前状态。</summary>
        public Color CombatFoeMarkerTint => combatFoeMarker != null && combatFoeMarker.gameObject.activeInHierarchy ? combatFoeMarker.color : Color.clear;
        public RectTransform CombatFoeMarkerRect() => combatFoeMarker != null ? combatFoeMarker.rectTransform : null;
        public RectTransform CombatFoeMarkerLabelRect() =>
            combatFoeMarkerLabel != null && combatFoeMarkerLabel.gameObject.activeInHierarchy
                ? combatFoeMarkerLabel.rectTransform : null;
        /// <summary>验收钩子：回退标记那句玩家看得懂的标注原文（不许出现内部字段名）。</summary>
        public string CombatFoeFallbackCopy => FoeFallbackCopy;
        /// <summary>当前怪物图／回退标记的实际角点包围盒，换算到 content 画布坐标（y 向下）。
        /// 包含受击／倒地倾斜与实际缩放；不再返回旧版固定槽位代替真实画面。</summary>
        public Rect CombatFoeSlotRect()
        {
            if (combatFoeRoot == null) return new Rect(float.NaN, float.NaN, 0, 0);
            var target = combatFoeFallback && combatFoeMarker != null ? combatFoeMarker.rectTransform : combatFoeRoot;
            var corners = new Vector3[4]; target.GetWorldCorners(corners);
            float x0 = float.PositiveInfinity, y0 = float.PositiveInfinity, x1 = float.NegativeInfinity, y1 = float.NegativeInfinity;
            foreach (var corner in corners)
            {
                var local = content.InverseTransformPoint(corner);
                x0 = Mathf.Min(x0, local.x); x1 = Mathf.Max(x1, local.x);
                y0 = Mathf.Min(y0, -local.y); y1 = Mathf.Max(y1, -local.y);
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }
        /// <summary>验收钩子：把当前这一场的妖强行按回退档画（true）或按配置取到的图画（false）。
        /// 存在的理由：夜4 的三张图一旦灌齐，"没图"这个状态就没有真数据可跑，
        /// 回退画法会变成一条没人走过的死支路——这条钩子让它始终可被实测。</summary>
        public void CombatSetFoeFallbackForCheck(bool forceFallback)
        {
            if (combatFoeImage == null) return;
            combatFoeArt = forceFallback ? null : (combatFoeArt != null ? combatFoeArt
                : Resources.Load<Texture2D>(combatFoeArtSource));
            ApplyFoeArt();
        }
        public float CombatFoeScale => combatFoeRoot != null ? combatFoeRoot.localScale.x : 0f;
        /// <summary>验收钩子：玩家纸偶占画面高的比例（Q2-3②；画布参考分辨率 1920×1080）。</summary>
        public float CombatPlayerHeightRatio =>
            combatPlayerActor != null ? combatPlayerActor.GetComponent<RectTransform>().rect.height / 1080f : 0f;
        /// <summary>验收钩子：妖兽占画面高的比例（降级档是抠底立绘，也得看得见）。</summary>
        public float CombatFoeHeightRatio => combatFoeRoot != null ? combatFoeRoot.rect.height / 1080f : 0f;
        /// <summary>验收钩子：玩家与妖的脚点差（Q2-3③ 纵深；为 0 就是站在同一条地平线上）。</summary>
        public float CombatGroundDepthPx => combatEnemyView != null ? PlayerGroundY + combatEnemyView.RegisteredFootInParent().y : float.NaN;
        /// <summary>验收钩子：地面贴面纹理。取不到就是 null——夹具据此判红，不许静默退回灰块。</summary>
        public Texture2D CombatGroundTexture => combatGroundTex;
        /// <summary>验收钩子：玩家脚下的接触影是否与地面同一张贴面（I-5.5"同一种着地表达"的机器侧证据）。</summary>
        public bool CombatPlayerShadowUsesSameWash =>
            combatPlayerShadow != null && combatPlayerShadow.texture != null &&
            combatPlayerShadow.texture == combatGroundTex;
        /// <summary>验收钩子：地带的实际上下沿（画布 y，自上而下）。从场景里的 RectTransform 现读，不读常量。</summary>
        public Vector2 CombatGroundBandY() =>
            combatGroundWash == null ? new Vector2(float.NaN, float.NaN)
            : new Vector2(-combatGroundWash.rectTransform.anchoredPosition.y,
                -combatGroundWash.rectTransform.anchoredPosition.y + combatGroundWash.rectTransform.sizeDelta.y);
        /// <summary>验收钩子：妖立绘的实际底边 y（夹具拿它减脚锚点算"看得见的蹄子线"）。</summary>
        public float CombatFoeSpriteBottomY() => combatFoeRoot == null ? float.NaN : CombatFoeSlotRect().yMax;
        /// <summary>验收钩子：素材声明的脚锚点（立绘可见底边→蹄子的空白高度）。</summary>
        public float CombatFoeFootInset => combatEnemyView != null ? combatEnemyView.DisplayedFootInset : FoeFootInset;
        /// <summary>验收钩子：玩家接触影的中心 x（跟着纸偶走，命中突进时也该贴着脚）。</summary>
        public float CombatPlayerShadowCenterX() =>
            combatPlayerShadow == null ? float.NaN
            : combatPlayerShadow.rectTransform.anchoredPosition.x + PlayerShadowW / 2f;
        /// <summary>验收钩子：玩家的脚点线 y（画布，自上而下）。</summary>
        public float CombatPlayerFootY => PlayerGroundY;
        /// <summary>验收钩子：玩家纸偶当前的中心 x。</summary>
        public float CombatPlayerCenterX() =>
            combatPlayerActor == null ? float.NaN : combatPlayerActor.GetComponent<RectTransform>().anchoredPosition.x;
        /// <summary>验收钩子：部位锁敌标签当前印的那一行——它走手写体，缺字只有读这行才看得见。</summary>
        public string CombatLockLabel => combatLockLabel != null ? combatLockLabel.text : "";
        /// <summary>验收钩子：D-47 那句玩家口径的**规定原文**（夹具拿它与实际印出的字逐字比）。</summary>
        public static string CombatStaminaHintCopy => StaminaHintCopy;
        /// <summary>验收钩子：那句口径实际印在哪个矩形（画布世界坐标）；没印出来返回 NaN 矩形让断言红。</summary>
        public Rect CombatStaminaHintRect()
        {
            if (combatFoeStaminaHint == null || !combatFoeStaminaHint.gameObject.activeInHierarchy)
                return new Rect(float.NaN, float.NaN, float.NaN, float.NaN);
            var r = combatFoeStaminaHint.rectTransform;
            var a = r.TransformPoint(r.rect.min);
            var b = r.TransformPoint(r.rect.max);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        RawImage combatGroundWash, combatPlayerShadow;
        Texture2D combatGroundTex;

        // 地表贴面的真源：仓库里已有的水墨笔触（I-5.6 明令"先用已有贴面顶、不要自己生图"）。
        // 导入设置是 textureType=Default，所以 Texture2D 与 Sprite 两种取法都要试一次。
        static Texture2D InkWashTexture()
        {
            var tex = Resources.Load<Texture2D>("Art/Fx/ink-stroke");
            if (tex != null) return tex;
            var spr = Resources.Load<Sprite>("Art/Fx/ink-stroke");
            return spr != null ? spr.texture : null;
        }

        // ui.Art 会保比例（立绘要用它），地面与脚下阴影要的是"把这条笔触铺满这块矩形"，
        // 所以这里直接开 RawImage。不做 alpha 包围盒裁剪：实测这张 1142×498 的笔触可见范围是
        // 列 20..1141、行 16..497，透明边距不到 2%，裁它没有收益
        // （而且 `Texture2D` 上根本没有 alphaBbox 这个 API——别照着 Sprite 的记忆写，编译不过）。
        RawImage InkWash(Transform parent, Texture2D tex, float x, float y, float w, float h,
            Color tint, Rect uv, bool mirror = false)
        {
            var img = ui.Rect(parent, "Ink wash", x, y, w, h).gameObject.AddComponent<RawImage>();
            img.raycastTarget = false; img.color = tint;
            if (tex != null)
            {
                img.texture = tex;
                img.uvRect = mirror ? new Rect(uv.x + uv.width, uv.y, -uv.width, uv.height) : uv;
            }
            return img;
        }

        /// <summary>
        /// D-28（F-5.1 裁"未表态即红"）：妖进战斗拿哪份档案，真源只有这一处。
        /// 夹具会拿它对全表扫一遍——新参战路径忘了挂档案，掉到的不是"某个合理值"而是基线 6/秒，
        /// 那正是 T072/T076 两次事故的形态（山魈 −42.9% 火力就是这么来的）。
        /// </summary>
        public static CombatTuning TuningForFoe(EnemyProfile profile) =>
            profile != null ? CombatDepth.Standard20 : null;

        /// <summary>验收钩子：当前跳字里最靠上的那个的画布 y（越小越靠上；无跳字返回 9999）。</summary>
        public float CombatFloaterTopY()
        {
            var top = 9999f;
            foreach (var f in combatFloaters) top = Mathf.Min(top, -f.rect.anchoredPosition.y);
            return top;
        }

        /// <summary>验收钩子：等价于按 Tab，在 2.0 部位表里循环锁定。</summary>
        public void CombatCycleLockedPartForCheck() => CycleLockedPart();
        /// <summary>验收钩子：直接锁到某个部位 id（空串 = 平攻默认部位）。</summary>
        public void CombatSetLockedPartForCheck(string partId)
        {
            lockedPartId = combatFoeProfile?.FindPart(partId) != null ? partId : "";
            RefreshCombatLock();
        }

        /// <summary>验收钩子：按 J/1 那一下（与键盘走同一条路径，含精力透支惩罚）。</summary>
        public void CombatActForCheck(CombatActionType action) => PlayerAct(action);
        /// <summary>验收钩子：临战整备装一件（返回是否穿上）。</summary>
        public bool CombatEquipForCheck(string itemId) => EquipItem(itemId);
        /// <summary>验收钩子：赵大磨刃（三件一起）。</summary>
        public RepairResult CombatRepairForCheck() => combatGear?.RepairAllAtSmithy(activeSave) ?? new RepairResult();

        void MovePlayer(float distance)
        {
            if (combatPlayer == null || combatPlayer.IsDead || combatSettled || combatGearOpen) return;
            combatPlayerMotor?.Walk(combatPlayer, combatFoe, distance);
        }

        // 出手统一入口：精力不够被打断的"破绽"只在这里施加（Core 的拒绝路径保持干净）。
        CombatResult PlayerAct(CombatActionType action)
        {
            if (combatPlayer == null || combatSettled || combatGearOpen || combatPlayerMotor == null)
                return new CombatResult { Reason = "当前不能出手。" };
            var result = combatPlayerMotor.Request(combatPlayer, combatFoe, action, combatNow, combatInputMove);
            HandlePlayerActionResult(result); return result;
        }
        void HandlePlayerActionResult(CombatResult result)
        {
            if (result == null) return;
            if (result.Executed)
                combatSkillWheel?.MarkUsed(combatPlayer.CurrentAction == CombatActionType.Dodge ? 2 : combatPlayer.CurrentAction == CombatActionType.Heavy ? 1 : 0);
            if (result.Executed && combatPlayer.CurrentAction == CombatActionType.Dodge) combatDodgeCount++;
            if (!result.Executed && result.Reason.Contains("精力"))
            {
                CombatRules.EnterExposure(combatPlayer, combatNow); combatExposureCount++;
                SpawnFloater(combatPlayer, "喘！", false);
            }
        }

        bool CombatSkillInputReady => page == "combat" && !combatSettled && !combatGearOpen && modal == null && bagOverlay == null && combatPlayer != null && !combatPlayer.IsDead;
        CombatResult PlayerSkillAct(string id)
        {
            if (!CombatSkillInputReady || combatSkills == null) return new CombatResult { Reason = "当前不能施术。" };
            var result = combatSkills.Request(id, activeSave, combatPlayer, combatFoe, combatNow, combatPlayerMotor.Facing);
            HandlePlayerActionResult(result);
            if (result.Executed)
            {
                combatPlayerMotor.ClearInput(); combatPlayerMotor.ObserveStart(combatPlayer, combatNow);
                combatSkillWheel?.MarkUsed(id == "sword" ? 4 : id == "thunder" ? 5 : 6);
            }
            else Notify(result.Reason);
            return result;
        }
        void ResolveLearnedPlayerSkill()
        {
            if (combatSkills == null) return;
            var part = CombatDepth.StrikeFor(combatFoeProfile, lockedPartId, combatGear?.CritBonus ?? 0f, combatPlayer.CounterReady);
            var outcome = combatSkills.Resolve(combatPlayer, combatFoe, combatNow, part);
            if (outcome == null) return;
            if (outcome.Strike != null)
            {
                RegisterPlayerWear(outcome.Strike); ShowStrikeFeedback(outcome.Strike, combatFoe, true);
                if (outcome.Strike.Critical && outcome.Strike.Hit) combatCriticalCount++;
                if (outcome.Strike.Whiffed) combatWhiffCount++;
            }
            if (outcome.Healed > 0) SpawnFloater(combatPlayer, "+" + outcome.Healed, false);
            combatSkillEffects?.Impact(outcome, combatFoe.X, combatPlayer.X, combatNow);
        }
        void ActivateCombatSkillSlot(int slot)
        {
            if (!CombatSkillInputReady) return;
            if (slot == 0) PlayerAct(CombatActionType.Light);
            else if (slot == 1) PlayerAct(CombatActionType.Heavy);
            else if (slot == 2) PlayerAct(CombatActionType.Dodge);
            else if (slot == 3)
            {
                int before = combatPlayer.Hp; ConsumePill(null); combatWheelPillRefreshAt = 0;
                if (combatPlayer.Hp > before) combatSkillWheel?.MarkUsed(3);
            }
            else if (slot >= 4 && slot <= 6) PlayerSkillAct(CombatSkillWheel.Ids[slot]);
            else if (slot == 7) { CycleLockedPart(); combatSkillWheel?.MarkUsed(7); }
        }
        CombatSkillSlotState ReadCombatSkillSlot(int slot)
        {
            bool ready = CombatSkillInputReady;
            var state = new CombatSkillSlotState { Learned = true, Available = ready, Status = "" };
            if (slot >= 4 && slot <= 6)
            {
                state.Sealed = !PrologueRules.CanCultivate(activeSave);
                string id = CombatSkillWheel.Ids[slot]; var definition = PlayerSkillCombat.Find(id);
                state.Learned = combatSkills != null && combatSkills.Learned(activeSave, id);
                state.Cooldown = combatSkills?.CooldownLeft(id, combatNow) ?? 0; state.Duration = definition.Cooldown;
                state.Available = ready && combatSkills.Refusal(id, activeSave, combatPlayer, combatFoe, combatNow).Length == 0;
                state.Status = state.Cooldown > 0 ? state.Cooldown.ToString("0.0") + "秒" : combatPlayer != null && combatPlayer.Stamina < definition.Move.StaminaCost ? "缺精"
                    : id == "clarity" && combatPlayer != null && combatPlayer.Hp >= combatPlayer.MaxHp ? "满血" : definition.Move.StaminaCost + "精";
            }
            else if (slot == 3)
            {
                if (combatWheelPill == null || combatNow >= combatWheelPillRefreshAt)
                { combatWheelPill = FirstUsablePill(); combatWheelPillRefreshAt = combatNow + .2f; }
                int count = string.IsNullOrEmpty(combatWheelPill) ? 0 : InventoryRules.Count(activeSave.bag, combatWheelPill);
                state.Available = ready && count > 0 && combatPlayer.Hp < combatPlayer.MaxHp;
                state.Status = count > 0 ? count + "份" : "无伤药";
            }
            else if (slot == 7) { state.Available = ready && combatFoeProfile != null; state.Status = "择隙"; }
            else state.Status = (slot == 0 ? CombatRules.LightStaminaCost : slot == 1 ? CombatRules.HeavyStaminaCost : CombatRules.DodgeStaminaCost) + "精";
            return state;
        }

        void CycleLockedPart()
        {
            if (combatFoeProfile == null) { Notify("这只妖兽还没登记 2.0 部位表，先按躯干打。"); return; }
            lockedPartId = CombatDepth.NextLockedPart(combatFoeProfile, lockedPartId);
            RefreshCombatLock();
        }

        bool EquipItem(string itemId)
        {
            if (combatGear == null) return false;
            var ok = combatGear.Equip(itemId);
            if (!ok) Notify(combatGear.LastRefusal);
            ApplyGearToPlayer();
            RecordGearLedger();   // 穿上/换下也是台账事件：同槽覆盖，空槽撤条
            RefreshCombatGearLabel();
            return ok;
        }

        // 从行囊里挑这一格里最贵（≈最好）的那件穿上；空手挑就如实说没有。
        void EquipBest(GearSlot slot)
        {
            if (combatGear == null) return;
            var catalog = CombatGearCatalog;
            if (catalog == null) { Notify("装备档位表没对上物品表，先穿不上。"); return; }
            var candidates = catalog.ForSlot(slot)
                .Where(e => InventoryRules.Count(activeSave?.bag ?? InventoryRules.NewBag(), e.ItemId) > 0 &&
                    (PrologueRules.CanCultivate(activeSave) || Table.Find(e.ItemId)?.Tier == "fanpin"))
                .OrderByDescending(e => Table.Find(e.ItemId)?.Price ?? 0)
                .ThenByDescending(e => slot switch
                {
                    GearSlot.Weapon => e.AttackBonus,
                    GearSlot.Armor => e.DefenseBonus,
                    _ => e.CritBonus,
                })
                .FirstOrDefault();
            if (candidates == null)
            {
                Notify(slot switch
                {
                    GearSlot.Weapon => "行囊里没有能握在手里家伙。",
                    GearSlot.Armor => "行囊里没有能披身上的东西。",
                    _ => "行囊里没有能带在身上的物件。",
                });
                return;
            }
            EquipItem(candidates.ItemId);
        }

        void ApplyGearToPlayer()
        {
            if (combatGear == null || combatPlayer == null) return;
            var stats = PlayerStatsFor(activeSave?.characterId ?? "");
            combatGear.ApplyTo(combatPlayer, stats.attack + ManualStudyRules.AttackBonus(activeSave), stats.defense);
        }

        void RepairGear()
        {
            if (combatGear == null) return;
            var total = combatGear.RepairAllAtSmithy(activeSave);
            if (total.Done)
            {
                PersistTradeCoins();
                Notify("赵大叮叮当当磨了一气，花去 " + total.CoinsPaid + " 文。");
            }
            else Notify(total.Reason);
            RefreshCombatGearLabel();
        }

        // 铜钱落盘：整备与修补都会动钱，走 FrontEndTrade 已有的那条持久化路径。
        void PersistTradeCoins()
        {
            try { PersistTrade(); }
            catch (Exception ex) { Debug.LogWarning("[FrontEndCombat] 铜钱没写进档：" + ex.Message); }
        }

        // items.json 原文：服药的回复量必须从物品数据里读（ItemDef 只有八个字段，装不下效果值）。
        // 表现层写死"45"是本轮最不该犯的错——浮签一个数、结算另一个数，就是 T047 的根因。
        string CombatItemsJson
        {
            get
            {
                if (!string.IsNullOrEmpty(combatItemsJson)) return combatItemsJson;
                var asset = Resources.Load<TextAsset>("Config/items");
                if (asset == null)
                {
                    Debug.LogWarning("[FrontEndCombat] items.json 读不到，战斗内服药一律拒用。");
                    return combatItemsJson = "";
                }
                return combatItemsJson = asset.text;
            }
        }
        string combatItemsJson;

        ConsumeResult lastConsume;
        /// <summary>验收钩子：上一次服药的结算（含回复量与它的来源）。</summary>
        public ConsumeResult CombatLastConsume => lastConsume;
        /// <summary>验收钩子：战败账单面板是否压在淡出罩之上（压住就等于账单被涂黑，玩家读不见）。</summary>
        public bool CombatSettleAboveFade => combatSettlePanel != null && combatFadeRect != null &&
            combatSettlePanel.GetSiblingIndex() > combatFadeRect.GetSiblingIndex();
        /// <summary>验收钩子：淡出罩当前的不透明度（取证时用来证明"这张图黑是淡出到位，不是没渲染"）。</summary>
        public float CombatFadeAlpha => combatFadeImage != null ? combatFadeImage.color.a : -1f;
        /// <summary>验收钩子：指定物品走一次战斗内服药（等价于点「服 药」）。</summary>
        public ConsumeResult CombatConsumeForCheck(string itemId) => ConsumePill(itemId);
        /// <summary>验收钩子：行囊里当前"写明能回多少"的丹药 id 列表。</summary>
        public List<string> CombatUsablePillsForCheck() =>
            CombatConsumables.UsableIn(activeSave, Table, CombatItemsJson);

        ConsumeResult ConsumePill(string itemId)
        {
            if (combatPlayer == null || combatSettled || combatGearOpen)
                return lastConsume = new ConsumeResult { Reason = "没在打架。" };
            var id = string.IsNullOrWhiteSpace(itemId) ? FirstUsablePill() : itemId;
            if (string.IsNullOrEmpty(id))
            {
                Notify("行囊里没有写明回血数的丹药——没写明的东西，我不敢替你喝。");
                return lastConsume = new ConsumeResult { Reason = "没有可服的丹药。" };
            }
            var result = CombatConsumables.Consume(activeSave, Table, CombatItemsJson, id, combatPlayer, combatNow);
            lastConsume = result;
            if (result.Executed)
            {
                PersistBag();   // 落盘往返：读档后这一件必须真的少了
                SpawnFloater(combatPlayer, "+" + result.Healed, true);
                // N4-Polish 3①：原来这句末尾带着"（数值来自字段 combatHeal）"——那是**夹具用来证明
                // "数值不是写死的"**的证据链（Effect.Source），不是给玩家看的话。玩家只需要知道
                // 回了多少、代价是什么。判据不动：来源仍然逐条断言（CombatEngineShot 的"字段优先"那组）。
                Notify("服下" + result.Effect.Name + "，回了 " + result.Healed + " 点气血，" +
                    result.ExposureSeconds.ToString("0.##") + " 秒内门户大开。");
            }
            else Notify(result.Reason);
            return result;
        }

        string FirstUsablePill()
        {
            var usable = CombatConsumables.UsableIn(activeSave, Table, CombatItemsJson);
            return usable.Count > 0 ? usable[0] : "";
        }

        void UpdateCombat(float dt)
        {
            if (page != "combat" || combatPlayer == null || combatFoe == null) return;
            combatNow += dt;
            if (!combatSettled)
            {
                combatPlayerMotor.ResetTravel(); combatFoeMotor.ResetTravel();
                CombatRules.Tick(combatPlayer, combatNow); CombatRules.Tick(combatFoe, combatNow);
                combatBoss?.ObserveHealth(combatFoe);
                HandlePlayerActionResult(combatPlayerMotor.Flush(combatPlayer, combatFoe, combatNow));
                if (!combatPlayer.IsDead && !combatFoe.IsDead)
                {
                    MovePlayer(combatInputMove * PlayerSpeed * dt);
                    RunFoeAi(dt);
                    // Learned casts have their own spatial gate; no light/heavy lunge or instant melee hit is added.
                    combatPlayerMotor.Step(combatPlayer, combatFoe, combatNow, !(combatSkills?.OwnsAction(combatPlayer) ?? false));
                    combatFoeMotor.Step(combatFoe, combatPlayer, combatNow, false);
                    ResolveLearnedPlayerSkill(); StrikePlayerIfActive(); StrikeFoeIfActive();
                    combatPlayer.X = ActionCombatMotor.ClampX(combatPlayer.X); combatFoe.X = ActionCombatMotor.ClampX(combatFoe.X);
                    CombatRules.Regenerate(combatPlayer, dt); CombatRules.Regenerate(combatFoe, dt);
                }
                if (combatFoe.IsDead) SettleVictory();
                else if (combatPlayer.IsDead) SettleDefeat();
            }
            RefreshCombatView(dt);
        }

        void ReadCombatInput(float dt)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame) ToggleCombatGear();
            combatInputMove = (combatPointerRight ? 1 : 0) - (combatPointerLeft ? 1 : 0);
            if (combatGearOpen || combatSettled || modal != null || bagOverlay != null || combatPlayer.IsDead) { combatInputMove = 0; return; }
            if (keyboard == null) return;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) combatInputMove -= 1;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) combatInputMove += 1;
            combatInputMove = Mathf.Clamp(combatInputMove, -1, 1);
            if (combatPlayer.Phase == CombatPhase.Idle && combatInputMove != 0) combatPlayerMotor.Face(combatInputMove);
            if (keyboard.lKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame) PlayerAct(CombatActionType.Dodge);
            else if (keyboard.kKey.wasPressedThisFrame || keyboard.digit2Key.wasPressedThisFrame) PlayerAct(CombatActionType.Heavy);
            else if (keyboard.jKey.wasPressedThisFrame || keyboard.digit1Key.wasPressedThisFrame) PlayerAct(CombatActionType.Light);
            else if (keyboard.uKey.wasPressedThisFrame || keyboard.digit4Key.wasPressedThisFrame) PlayerSkillAct("sword");
            else if (keyboard.iKey.wasPressedThisFrame || keyboard.digit5Key.wasPressedThisFrame) PlayerSkillAct("thunder");
            else if (keyboard.oKey.wasPressedThisFrame || keyboard.digit6Key.wasPressedThisFrame) PlayerSkillAct("clarity");
            if (keyboard.tabKey.wasPressedThisFrame) { CycleLockedPart(); combatSkillWheel?.MarkUsed(7); }
            if (keyboard.qKey.wasPressedThisFrame || keyboard.digit3Key.wasPressedThisFrame) ConsumePill(null);
        }

        // 妖兽均沿用已登记的招式表。未登记时保留原轻击回退。
        void RunFoeAi(float dt)
        {
            if (combatFoe.IsDead || combatPlayer.IsDead || combatSettled) return;
            if (combatFoe.Phase != CombatPhase.Idle) return;
            combatFoeMotor.Face(combatPlayer.X - combatFoe.X);
            if (combatBoss != null)
            {
                var bossMove = combatBoss.Choose(combatFoe, combatPlayer, combatNow);
                if (bossMove != null && combatBoss.TryBegin(combatFoe, combatPlayer, bossMove, combatNow).Executed)
                { combatFoeMotor.ObserveStart(combatFoe, combatNow); return; }
                float gap = combatPlayer.X - combatFoe.X;
                if (Mathf.Abs(gap) > combatFoeProfile.Ai.PreferredRange)
                    combatFoeMotor.Walk(combatFoe, combatPlayer, Mathf.Sign(gap) * Mathf.Min(Mathf.Abs(gap) - combatFoeProfile.Ai.PreferredRange, combatFoeDef.MoveSpeed / 75f * dt));
                return;
            }

            if (combatFoeProfile != null && combatFoeProfile.Ai.MoveSet == EnemyMoveSet.Profile2)
            {
                var move = EnemyAi.Choose(combatFoeProfile, foeAi, combatFoe, combatPlayer, combatNow);
                if (move != null &&
                    CombatRules.TryAct(combatFoe, move.Action, combatNow, combatPlayer, move).Executed)
                {
                    combatFoeMotor.ObserveStart(combatFoe, combatNow);
                    EnemyAi.MarkActed(foeAi, combatNow, combatFoeProfile.Ai.AggressionSeconds, move.Id);
                    return;
                }
                var direction = EnemyAi.ApproachDirection(combatFoeProfile, combatFoe, combatPlayer);
                if (direction != 0f)
                {
                    var speed = combatFoeDef.MoveSpeed / 75f;
                    var dx = combatPlayer.X - combatFoe.X;
                    combatFoeMotor.Walk(combatFoe, combatPlayer, Mathf.Sign(dx) * Mathf.Min(Mathf.Abs(dx), speed * dt));
                }
                return;
            }

            var dd = combatPlayer.X - combatFoe.X;
            var moveSpeed = combatFoeDef.MoveSpeed / 75f;
            if (Mathf.Abs(dd) > CombatRules.StrikeRange * .8f)
                combatFoeMotor.Walk(combatFoe, combatPlayer, Mathf.Sign(dd) * Mathf.Min(Mathf.Abs(dd), moveSpeed * dt));
            else if (combatNow >= foeNextActAt &&
                     CombatRules.TryAct(combatFoe, CombatActionType.Light, combatNow).Executed)
            { combatFoeMotor.ObserveStart(combatFoe, combatNow); foeNextActAt = combatNow + 1.5f; }
        }

        // 玩家这一刀打在哪儿：躯干平攻 or 弱点锁敌（任务书 K3/M2）。
        void StrikePlayerIfActive()
        {
            if (combatSkills?.OwnsAction(combatPlayer) ?? false) return;
            if (!combatPlayerMotor.TryStrike(combatPlayer)) return;
            combatEffects?.Slash(new Vector2(StageX(combatPlayer.X), -PlayerGroundY), combatPlayerMotor.ActionFacing,
                combatPlayer.CurrentAction == CombatActionType.Heavy, combatPlayerMotor.ComboStep, false, combatNow);
            var strike = CombatDepth.StrikeFor(combatFoeProfile, lockedPartId,
                combatGear?.CritBonus ?? 0f, combatPlayer.CounterReady);
            var result = combatPlayerMotor.Faces(combatPlayer, combatFoe) ? ResolvePlayerStrike(strike) : combatPlayerMotor.DirectionMiss(combatPlayer, combatFoe);
            RegisterPlayerWear(result);
            ShowStrikeFeedback(result, combatFoe, dealt: true);
            if (result.Critical && result.Hit) combatCriticalCount++;
            if (result.Whiffed) combatWhiffCount++;
        }

        CombatResult ResolvePlayerStrike(PartStrike strike)
        {
            // 没有 2.0 部位档（老表读不出/这只妖没登记）就退回 M4-03 口径：
            // 重击挑头、轻击打身，倍率查 Core 的 hitProfile。
            if (combatFoeProfile == null)
                return CombatRules.ResolveStrike(combatPlayer, combatFoe, combatNow,
                    combatPlayer.CurrentAction == CombatActionType.Heavy
                        ? combatFoeDef.MultiplierOf("head") : combatFoeDef.MultiplierOf("body"));
            return CombatRules.ResolvePartStrike(combatPlayer, combatFoe, combatNow, strike);
        }

        void StrikeFoeIfActive()
        {
            if (combatBoss != null)
            {
                var bossResult = combatBoss.Resolve(combatFoe, combatPlayer, combatNow);
                if (bossResult == null) return;
                ShowStrikeFeedback(bossResult, combatPlayer, dealt: false);
                if (bossResult.Hit) { combatGear?.WearArmor(); RecordGearLedger(); }
                if (bossResult.Executed && !bossResult.Hit && bossResult.Reason.Contains("闪") && combatPlayer.CounterReady) combatRiposteCount++;
                return;
            }
            if (!combatFoeMotor.TryStrike(combatFoe)) return;
            combatEffects?.Slash(new Vector2(StageX(combatFoe.X), -FoeFootY), combatFoeMotor.ActionFacing,
                combatFoe.CurrentAction == CombatActionType.Heavy, 0, true, combatNow);
            // 妖兽打玩家永远按躯干（倍率 1、无抵抗）——既有夹具的伤害期望建立在这条上。
            var result = combatFoeMotor.Faces(combatFoe, combatPlayer) ? CombatRules.ResolveStrike(combatFoe, combatPlayer, combatNow) : combatFoeMotor.DirectionMiss(combatFoe, combatPlayer);
            ShowStrikeFeedback(result, combatPlayer, dealt: false);
            if (result.Hit)
            {
                combatGear?.WearArmor();
                RecordGearLedger();   // T051 写入半边：挨一下就记一次账，不等结算
            }
            if (result.Executed && !result.Hit && result.Reason.Contains("闪") && combatPlayer.CounterReady)
                combatRiposteCount++;   // 跳字交给 ShowStrikeFeedback，两处都刷会叠成两个"闪！"
        }

        // ==== T051 写入半边（交接包 L-4 第 7 条）====
        // 3号 交了存储半边（`SaveData.gear` + `GearLedger.Record/Clear/Of`），但全仓**没有一处调用它**
        // ⇒ "字段在、没人写"，正是 T043/T100 那一族。这一半只做"写"：耐久或穿戴一变就往台账记一条，
        //    跟着已有的落盘点存出去。**读回半边不在本轮**（还没有人拿它恢复耐久），所以 T051 保持 OPEN。
        // 记的是"此刻身上这件的此刻耐久"，同槽覆盖；脱下来整条撤掉，不留 itemId 为空的半截记录。
        void RecordGearLedger()
        {
            if (combatGear == null) return;
            RecordGearSlot(GearSlots.Weapon, combatGear.Weapon);
            RecordGearSlot(GearSlots.Armor, combatGear.Armor);
            RecordGearSlot(GearSlots.Accessory, combatGear.Accessory);
        }

        void RecordGearSlot(string slot, EquippedGear gear)
        {
            if (gear == null) { GearLedger.Clear(activeSave, slot); return; }
            GearLedger.Record(activeSave, slot, gear.Entry.ItemId, gear.Durability);
        }

        void RegisterPlayerWear(CombatResult result)
        {
            if (!result.Hit || combatGear == null) return;
            combatGear.WearWeapon(combatPlayer.CurrentAction);
            RecordGearLedger();   // 挥一刀就记一次（钝损那一刀会把 durability 记成 0，台账与画面同步）
            // 钝损那一刻如实提示：加成已经归零，玩家不该靠猜。
            if (combatGear.WeaponDulled && combatGear.Weapon != null && combatGear.Weapon.Durability == 0)
                RefreshCombatGearLabel();
        }

        void ShowStrikeFeedback(CombatResult result, CombatActorState defender, bool dealt)
        {
            if (result.Hit)
            {
                if (defender == combatFoe) foeFlashUntil = combatNow + FoeFlashSeconds;
                else combatPlayerFlashUntil = combatNow + FoeFlashSeconds;
                combatEffects?.Impact(new Vector2(StageX(defender.X), -(defender == combatFoe ? FoeFootY : PlayerGroundY)), combatNow);
                SpawnFloater(defender, (result.Critical ? "会心 " : "-") + result.Damage, dealt, result.Critical);
            }
            else if (defender == combatPlayer && result.Reason.Contains("闪"))
                SpawnFloater(defender, "闪！", false);
        }

        void SettleVictory()
        {
            if (combatSettled) return;
            combatSettled = true; combatBoss?.Cancel(); combatSkills?.Cancel();
            // 一次性奖励：按遭遇 id 记档（M4-04），重复讨伐不再发放；Esc/失败不发。
            var firstKill = activeSave.MarkFlag(combatEncounterId);
            // 章推进：这场战斗是轴线上某段（如 NightRoar/BeastFight）的 encounters，
            // 赢了就把该段结算掉——ChapterLedger 自己保证只结算一次。
            if (chapterTable != null)
            {
                foreach (var def in chapterTable.Segments)
                    if (def.EncounterId == combatEncounterId)
                        ChapterLedger.Enter(activeSave, chapterTable, def.Segment, activeSave.WorldTimeNow(), out _);
                PersistChapter();
            }
            combatSpoils = firstKill ? Core.CombatSpoils.Grant(activeSave, combatFoeDef, Table) : new SpoilsResult();
            RecordGearLedger();   // 这一场磨掉的耐久跟着这一次落盘出去（T051 写入半边）
            PersistBag();
            combatPlayerMotor?.ClearInput(); combatInputMove = 0;
            combatSettlePanel = ui.Rect(content, "Combat settle", 460, 280, 1000, 500);
            ImmortalUi.Art(ui, combatSettlePanel, "Combat outcome scroll", "nov8/tianji-scroll-fairyland", 0, 0, 1000, 500, true);
            ui.Label(combatSettlePanel, combatFoeDef.Name + " 倒下了", 190, 120, 620, 60, 34, UiKit.Ink,
                TextAlignmentOptions.TopLeft, true);
            ui.Label(combatSettlePanel, firstKill
                    ? "拾获：" + combatSpoils.Describe(Table)
                    : "这头畜生是冲着同伙的气味来的——身上没什么可剥的了。",
                190, 190, 620, 100, 24, UiKit.Ink);
            var weakLine = combatFoeProfile == null ? "" :
                "破在" + combatFoeProfile.DefaultPart.Name + "上，共会心 " + combatCriticalCount + " 次。";
            ui.Label(combatSettlePanel, weakLine.Length == 0
                    ? "山风把腥气压下去之前，先回镇上歇口气。"
                    : weakLine + "　山风把腥气压下去之前，先回镇上歇口气。",
                190, 292, 620, 60, 20, UiKit.Ink);
            CombatCommand(combatSettlePanel, "Combat return town", "返回青石镇", 326, 366, 348, 76, LeaveActionCombat);
            FocusFirst();
        }

        // 战败保护（任务书 K5/M7）：气血归零绝不把游戏带进未捕获异常，
        // 结算面板第一时间出现（既有夹具当场就要数到它），淡出与药老台词随后叠上去。
        void SettleDefeat()
        {
            if (combatSettled) return;
            combatSettled = true; combatBoss?.Cancel(); combatSkills?.Cancel();
            // D-08：战败扣药资从"只有夹具在踩的死路径"接成运行期真路径。
            // 这里只调 Apply 不调 Plan：Apply 内部第一步就是 Plan(save, maxHp)，
            // 返回的同一个对象既带账单文案又带实扣数字；再单独调一次 Plan 的结果无处可用，
            // 留一个"调了但扔掉返回值"的语句正是本轮要修的毛病（T050 的反向形态）。
            // Plan 的只读口径由 CombatEngineShot 的"Plan 只算不扣"那条断言钉住。
            LastDefeatSettlement = CombatDefeatProtection.Apply(activeSave, combatPlayer?.MaxHp ?? 1);
            if (LastDefeatSettlement.MedicalFee > 0) PersistTradeCoins();
            // 打输了也磨了刀：这一场的耐久变化不能因为"没打赢"就不落账（T051 写入半边）。
            RecordGearLedger();
            PersistBag();
            // 醒来三成气血：下一场开战的起点，不是这一场能把人救回来。
            wakeHpNext = LastDefeatSettlement.AwakeHp;

            combatPlayerMotor?.ClearInput(); combatInputMove = 0;
            combatSettlePanel = ui.Rect(content, "Combat settle", 460, 280, 1000, 500);
            ImmortalUi.Art(ui, combatSettlePanel, "Combat outcome scroll", "nov8/tianji-scroll-fairyland", 0, 0, 1000, 500, true);
            ui.Label(combatSettlePanel, LastDefeatSettlement.FadeLine, 190, 120, 620, 60, 34, UiKit.Ink,
                TextAlignmentOptions.TopLeft, true);
            ui.Label(combatSettlePanel, "再睁眼时，是村民把你抬回了" + LastDefeatSettlement.WakeAt +
                "。行囊还在，命也还在。", 190, 184, 620, 86, 24, UiKit.Ink);
            ui.Label(combatSettlePanel, "药老：" + LastDefeatSettlement.ElderLine +
                (LastDefeatSettlement.FeeWaived ? "（" + LastDefeatSettlement.Reason + "）" :
                 "　药资 " + LastDefeatSettlement.MedicalFee + " 文已付，余 " +
                 LastDefeatSettlement.CoinsAfter + " 文。"),
                190, 276, 620, 70, 20, UiKit.Ink);
            CombatCommand(combatSettlePanel, "Combat return town", "返回青石镇", 326, 366, 348, 76, LeaveActionCombat);

            // 淡出罩：单独一个物件，名字绝不叫 Combat settle——那只面板要被夹具数到唯一一张。
            var fade = ui.Panel(content, "Defeat fade", 0, 0, 1920, 1080, new Color(0f, 0f, 0f, 0f));
            combatFadeImage = fade;
            combatFadeRect = fade.rectTransform;
            combatFadeImage.raycastTarget = false;
            // 罩子必须在面板之下：同级后建者画在上层，淡出到 alpha=1 时会把账单整张涂黑
            // （k-combat-06 实测就是一帧全黑，"药资 32 文已付"那行玩家一个字都看不见）。
            combatSettlePanel.SetAsLastSibling();
            FocusFirst();
        }

        float StageX(float worldX) => StageCenterX + worldX * StagePxPerUnit;

        static string PhaseName(CombatPhase phase) => phase switch
        {
            CombatPhase.Idle => "静立",
            CombatPhase.Startup => "蓄势",
            CombatPhase.Active => "出手",
            CombatPhase.Recovery => "收势",
            CombatPhase.Hitstun => "踉跄",
            _ => "倒下",
        };

        void RefreshCombatView(float dt)
        {
            if (combatPlayerActor == null || combatFoeRoot == null) return;
            var playerPoint = new Vector2(StageX(combatPlayer.X), -PlayerGroundY);
            float moved = Mathf.Abs(combatPlayer.X - combatObservedPlayerX);
            combatObservedPlayerX = combatPlayer.X;
            bool walking = moved > .00001f && combatPlayer.Phase == CombatPhase.Idle && !combatSettled;
            combatPlayerActor.SetPosition(playerPoint);
            combatPlayerActor.SetMotion(walking ? PuppetActor.Motion.Walk : PuppetActor.Motion.Idle);
            if (walking) combatPlayerActor.AdvanceGait(moved * StagePxPerUnit);
            combatPlayerActor.SetMirror((combatPlayerMotor.Facing < 0) != combatNativeLeft);
            // Victory stops the battle clock while the final hit may still be Active. Restore the
            // stand presentation at settlement without changing the real Core phase or action clock.
            var playerPosePhase = combatSettled && !combatPlayer.IsDead ? CombatPhase.Idle : combatPlayer.Phase;
            if (combatSkills != null && combatSkills.OwnsAction(combatPlayer) && combatSkills.Current.Id == "clarity") playerPosePhase = CombatPhase.Idle;
            combatPose.Sample(playerPoint, combatPlayerMotor.ActionFacing, playerPosePhase, combatPlayer.CurrentAction,
                CombatRules.ActiveProgress(combatPlayer, combatNow), settings.reduceMotion,
                CombatRules.ActionElapsed(combatPlayer, combatNow), CombatRules.TimingOf(combatPlayer).active);
            if (combatPlayerShadow != null) combatPlayerShadow.rectTransform.anchoredPosition = new Vector2(
                playerPoint.x - PlayerShadowW / 2, -(PlayerGroundY - PlayerShadowH + 8));

            bool foeWalking = !combatSettled && combatFoe.Phase == CombatPhase.Idle && Mathf.Abs(combatFoeMotor.LastDelta) > .000001f;
            var foePosePhase = combatSettled && !combatFoe.IsDead ? CombatPhase.Idle : combatFoe.Phase;
            combatEnemyView?.Sample(new Vector2(StageX(combatFoe.X), -FoeFootY),
                combatFoe.Phase == CombatPhase.Idle ? combatFoeMotor.Facing : combatFoeMotor.ActionFacing,
                foePosePhase, combatBoss?.Skill ?? BossSkill.None, CombatRules.ActiveProgress(combatFoe, combatNow),
                foeWalking ? Mathf.Abs(combatFoeMotor.LastDelta) : 0, foeWalking, settings.reduceMotion || (IsMountainLord && combatFoe.IsDead), combatFoeFallback);
            combatEnemyShadow?.Sample(new Vector2(StageX(combatFoe.X), -FoeFootY), IsMountainLord);
            combatBossRearFx?.Sample(combatBoss, combatFoe, combatNow, settings.reduceMotion, combatSettled);
            combatBossFrontFx?.Sample(combatBoss, combatFoe, combatNow, settings.reduceMotion, combatSettled);
            combatFoeFlash?.SetStrength(combatNow < foeFlashUntil ? (settings.reduceMotion ? .45f : .9f) : 0);
            combatPlayerFlash?.SetStrength(combatNow < combatPlayerFlashUntil ? (settings.reduceMotion ? .45f : .9f) : 0);
            if (combatFoeMarker != null) combatFoeMarker.color = combatFoe.IsDead
                ? new Color(combatFoeMarkerTint.r, combatFoeMarkerTint.g, combatFoeMarkerTint.b, .35f) : combatFoeMarkerTint;
            if (combatFoeMarkerLabel != null) combatFoeMarkerLabel.alpha = combatFoe.IsDead ? .35f : 1;
            float startup = CombatRules.TimingOf(combatFoe).startup;
            combatEffects?.Sample(combatNow, settings.reduceMotion, combatBoss == null && !combatSettled && combatFoe.Phase == CombatPhase.Startup,
                new Vector2(StageX(combatFoe.X), -FoeFootY), combatFoeMotor.ActionFacing,
                (combatFoe.CurrentMove?.Range ?? CombatRules.StrikeRange) * StagePxPerUnit,
                startup <= 0 ? 0 : 1 - (combatFoe.PhaseUntil - combatNow) / startup);

            combatSkillEffects?.Sample(combatSkills, combatPlayer, combatNow, settings.reduceMotion, combatSettled);
            combatSkillWheel?.Sample(combatNow, dt);

            // 血条与精力条：按目标值补间，减动效开关下吸附。
            var barDt = settings.reduceMotion ? 0 : dt;
            TweenBar(ref playerHpShown, (float)combatPlayer.Hp / combatPlayer.MaxHp, barDt);
            TweenBar(ref playerStaminaShown, (float)combatPlayer.Stamina / combatPlayer.MaxStamina, barDt);
            TweenBar(ref foeHpShown, (float)combatFoe.Hp / combatFoe.MaxHp, barDt);
            TweenBar(ref foeStaminaShown, (float)combatFoe.Stamina / combatFoe.MaxStamina, barDt);
            combatPlayerHpFill.rectTransform.sizeDelta = new Vector2(296f * Mathf.Clamp01(playerHpShown), 12);
            combatPlayerStaminaFill.rectTransform.sizeDelta = new Vector2(296f * Mathf.Clamp01(playerStaminaShown), 12);
            combatFoeHpFill.rectTransform.sizeDelta = new Vector2(combatFoeHpWidth * Mathf.Clamp01(foeHpShown), 12);
            combatFoeStaminaFill.rectTransform.sizeDelta = new Vector2(296f * Mathf.Clamp01(foeStaminaShown), 12);
            combatPlayerHpText.text = combatPlayer.Hp + " / " + combatPlayer.MaxHp;
            combatFoeHpText.text = combatFoe.Hp + " / " + combatFoe.MaxHp;
            if (combatFoeStaminaText != null) combatFoeStaminaText.text = "精力 " + combatFoe.Stamina;
            combatPlayerState.text = PhaseName(combatPlayer.Phase) + " · 精力 " + combatPlayer.Stamina +
                (combatPlayer.CounterReady ? " · 看破" : "");
            combatFoeState.text = combatFoe.Phase == CombatPhase.Startup ? (combatFoe.CurrentMove?.Name ?? "扑击") + " · 蓄势"
                : combatFoe.Phase == CombatPhase.Recovery ? "收势 · 可乘隙" : PhaseName(combatFoe.Phase);
            if (combatBoss != null)
                combatFoeState.text = (combatBoss.Stage == 2 ? "玉脉暴起 · " : "镇谷 · ") + combatFoeState.text;
            if (combatPlayer.Phase == CombatPhase.Startup || combatPlayer.Phase == CombatPhase.Active)
                combatPlayerState.text = (combatPlayer.CurrentMove?.Name ?? (combatPlayer.CurrentAction == CombatActionType.Dodge ? "闪身" : "静立")) + " · 精力 " + combatPlayer.Stamina;

            // 战败淡出：真实帧推进（批处理里没真实帧就停在当前透明度，不影响任何断言）。
            if (combatFadeImage != null && combatFadeImage.color.a < 1f && combatSettled && combatPlayer.IsDead)
            {
                var alpha = Mathf.Clamp01(combatFadeImage.color.a + dt / DefeatFadeSeconds);
                combatFadeImage.color = new Color(0f, 0f, 0f, alpha);
            }

            // 伤害浮字。上升量按**战斗钟**算，不按真实帧时长算：
            // 原来寿命用 combatNow、位移用 Time.unscaledDeltaTime，两套时间混在一个元素上——
            // 批量推进战斗钟的夹具里它会飘得比真人在玩时高得多（k-combat-04 实测飘到"换部位"按钮上），
            // 而且"这一帧它该在哪"不可复现，等于给取证图埋随机性。
            for (var i = combatFloaters.Count - 1; i >= 0; i--)
            {
                var (rect, text, born, critical, from) = combatFloaters[i];
                var t = (combatNow - born) / FloaterLifeSeconds;
                if (t >= 1f) { Destroy(rect.gameObject); combatFloaters.RemoveAt(i); continue; }
                rect.anchoredPosition = from + new Vector2(0f, (critical ? FloaterRiseCritical : FloaterRise) * t);
                text.alpha = 1f - t;
            }
        }

        static void TweenBar(ref float shown, float target, float dt)
        {
            if (dt <= 0f) { shown = target; return; }
            var step = dt * BarTweenPerSecond;
            if (Mathf.Abs(target - shown) <= step) shown = target;
            else shown += Mathf.Sign(target - shown) * step;
        }

        void SpawnFloater(CombatActorState actor, string message, bool dealt, bool critical = false)
        {
            // 跳字挂在"这个人的头顶"上，而不是挂在一条公共地平线上：
            // Q2-3③ 给了纵深之后玩家与妖的脚点不同（近大远小、一前一后），
            // 再共用 StageGroundY 减一个固定值，妖的跳字就会压在自己身上。
            var headTopY = actor == combatFoe
                ? CombatFoeSlotRect().yMin
                : PlayerGroundY - PlayerBodyHeight;
            var rect = ui.Rect(content, "Floater", StageX(actor.X) - (critical ? 70f : 40f),
                headTopY - (critical ? 60f : 30f), critical ? 140f : 80f, critical ? 52f : 40f);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = brushFont;
            text.text = message;
            text.fontSize = critical ? 46 : 34;
            // 暴击走金色（任务书 K5）；普通命中沿用手感：打出去是墨色，挨上去是朱色。
            text.color = critical ? new Color(.78f, .58f, .16f, 1f)
                : dealt ? UiKit.Ink : new Color(.66f, .2f, .16f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            combatFloaters.Add((rect, text, combatNow, critical, rect.anchoredPosition));
        }

        void RefreshCombatLock()
        {
            if (combatLockLabel == null) return;
            combatLockLabel.text = combatFoeProfile == null
                ? "躯干"
                : "察隙　" + CombatDepth.PartLabel(combatFoeProfile, lockedPartId);
        }

        void RefreshCombatGearLabel()
        {
            if (combatGearLabel == null) return;
            combatGearLabel.text = (combatGear?.Describe() ?? "").Replace("　", "\n") +
                "\n总攻 " + (combatGear?.TotalAttack(PlayerStatsFor(activeSave?.characterId ?? "").attack + ManualStudyRules.AttackBonus(activeSave)) ?? 0) +
                "　总防 " + (combatGear?.TotalDefense(PlayerStatsFor(activeSave?.characterId ?? "").defense) ?? 0);
        }
    }
}
