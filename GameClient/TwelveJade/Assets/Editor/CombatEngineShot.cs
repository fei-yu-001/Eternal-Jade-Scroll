using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TwelveJade.Combat;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    // 任务书 K（M8）：战斗引擎全维无头套件。
    //
    // 分两段跑，一次批处理全覆盖：
    //   A 段 逻辑层——enemies.json 2.0 对表与坏表拒绝、部位倍率数学、招式三相时序、精力流转、
    //          看破判定、流血与霸体、行为树、装备战力与耐久、19 命途映射、战败账单。
    //          全部用固定数值推进时钟，不碰机器时钟、不用随机数。
    //   B 段 表现层——真页面开战、Tab 换部位、临战整备、白光频闪、暴击跳字、精力条补间、
    //          战败结算面板与淡出罩，并出图到 Tools/screenshots/（数值达标不等于画面达标）。
    //
    // 两条红线贯穿全篇，各留了一条"反向断言"把它们钉住：
    //   1) 裸装裸命 + 默认平攻时，伤害必须与 M4-01/M4-03 基线逐字相同（野猪 60→44）。
    //      这条一漂，CombatShot.cs 与 FrontEndShotProps.cs 就红——那两份夹具不在战斗域写权限内。
    //   2) 战败绝不产生未捕获异常，且战斗失败不发任何奖励（铜钱一分不动）。
    [InitializeOnLoad]
    public static class CombatEngineShot
    {
        const int Width = 1920, Height = 1080;
        const string SessionKey = "CombatEngineShot.Active";
        const string Tag = "[CombatEngineShot] ";

        static FrontEndController controller;
        static Camera camera;
        static RenderTexture target;
        static string shotsDir;
        static readonly List<string> problems = new();
        static readonly List<string> okLines = new();
        static float runStart;
        static bool ran, inViewSuite;
        static int slotUsed;
        static int logicChecks, viewChecks;

        static ItemTable items;
        static string ItemsJson = "";
        static EnemyTable legacyEnemies;
        static EnemyProfileTable profiles;
        static CombatResult lastResult;

        static CombatEngineShot()
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            shotsDir = SessionState.GetString(SessionKey + ".Dir", "");
            runStart = Time.realtimeSinceStartup;
            Attach();
        }

        static void Attach()
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Pump;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Pump;
        }

        public static void RunSuite()
        {
            SessionState.SetBool(SessionKey, true);
            var project = Directory.GetParent(Application.dataPath).FullName;
            var repoRoot = Directory.GetParent(Directory.GetParent(project).FullName).FullName;
            shotsDir = Path.Combine(repoRoot, "Tools", "screenshots");
            Directory.CreateDirectory(shotsDir);
            // 与 CombatShot 同一套临时存档约定：验收绝不写进玩家真实存档。
            var scratch = Path.Combine(Path.GetTempPath(), "twelve-jade-combat-engine-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", scratch);
            SessionState.SetString(SessionKey + ".Dir", shotsDir);
            Debug.Log(Tag + "scratch saves at " + scratch);
            Attach();
            EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity", OpenSceneMode.Single);
            runStart = Time.realtimeSinceStartup;
            EditorApplication.isPlaying = true;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!EditorApplication.isPlaying) return;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (condition.StartsWith("[Licensing::Module]")) return;
            if (stackTrace != null && stackTrace.Contains("UnityEditor.Search")) return;
            // T105 的真根因：Check 失败时先 Add 了一次，紧跟着那句 Debug.LogError 又回调回本方法，
            // 同一条失败在 problems 里留下两条 ⇒ 收尾那行 problem 数正好是真实失败数的两倍。
            // 只放过"本夹具自己播报的 FAIL"，产品代码报错与异常一条都不放过。
            if (condition.StartsWith(Tag + "FAIL:")) return;
            problems.Add(type + ": " + condition);
        }

        static void Pump()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            try { Step(); }
            catch (Exception ex) { Fail("驱动异常: " + ex); }
        }

        static void Step()
        {
            if (Time.realtimeSinceStartup - runStart > 600f) { Fail("验收超时: 600 秒内没跑完"); return; }
            if (!EditorApplication.isPlaying) return;
            if (controller == null)
            {
                controller = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                if (controller == null) return;
                SetupCapture();
                Debug.Log(Tag + "play mode ready");
            }
            if (ran) return;
            ran = true;
            try
            {
                RunLogicSuite();
                inViewSuite = true;
                RunViewSuite();
            }
            catch (Exception ex) { Fail("验收异常: " + ex); }
            Finish();
        }

        // ============================ A 段：逻辑层 ============================

        static void RunLogicSuite()
        {
            var enemiesAsset = Resources.Load<TextAsset>("Config/enemies");
            var itemsAsset = Resources.Load<TextAsset>("Config/items");
            Check("enemies.json 与 items.json 都能从 Resources 读到",
                enemiesAsset != null && itemsAsset != null);
            items = ItemTable.Parse(itemsAsset.text);
            ItemsJson = itemsAsset.text;
            legacyEnemies = EnemyTable.Parse(enemiesAsset.text);
            profiles = EnemyProfileTable.Load(enemiesAsset.text);

            Check("parts 与 hitProfile 对表通过（部位数据没有第二真源）", Throws(() => profiles.CrossCheck(legacyEnemies)) == "");
            profiles.CrossCheckDrops(items, legacyEnemies);

            SuiteSchema();
            SuiteBadTables();
            SuitePartsAndMath();
            SuiteMoveTimings();
            SuiteStamina();
            SuiteRiposte();
            SuiteBleedAndBulwark();
            SuiteEnemyAi();
            SuiteEquipment();
            SuiteDurability();
            SuiteDestiny();
            SuiteDefeat();
            SuiteTempo();
            SuitePills();
            SuiteContractIds();
            Debug.Log(Tag + "A 段逻辑层完成：" + logicChecks + " 项");
        }

        // ---- M1 表与三级生态 ----
        static void SuiteSchema()
        {
            Check("妖兽 2.0 表三只齐备", profiles.Profiles.Count == 3, profiles.Profiles.Count + " 只");
            Check("三只妖 id 逐字正确", string.Join(",", profiles.Profiles.Select(p => p.Id)) ==
                "boar-demon,nether-wolf,stone-mandrill");
            Check("三大生态位排得出序", string.Join("→", profiles.InOrder.Select(p => p.Habitat)) ==
                "镇郊草伏→北山石涧→后山古冢入口");
            foreach (var p in profiles.Profiles)
            {
                Check(p.Id + " 至少一处弱点", p.WeakPoints.Any());
                Check(p.Id + " 平攻面存在且不是弱点", p.DefaultPart != null && !p.DefaultPart.WeakPoint, p.DefaultPart?.Name);
                Check(p.Id + " 招式表非空", p.Moves.Count >= 1);
                Check(p.Id + " 赏钱为正", p.Coins > 0);
                Check(p.Id + " 与 Core 妖兽表逐字段同参", legacyEnemies.Find(p.Id).MaxHp == p.MaxHp &&
                    legacyEnemies.Find(p.Id).Coins == p.Coins && legacyEnemies.Find(p.Id).Attack == p.Attack);
            }

            var boar = profiles.Find("boar-demon");
            Check("野猪冻结项未动（60/9/2/50/24）",
                boar.MaxHp == 60 && boar.Attack == 9 && boar.Defense == 2 && boar.MaxStamina == 50 && boar.Coins == 24);
            // N1 / D-07：野猪从 legacy 切到 profile2。三只妖从此同一套口径，不再有"半接入"的那一只。
            Check("野猪招式集已切 profile2", boar.Ai.MoveSet == EnemyMoveSet.Profile2);
            Check("三只妖全在 profile2（谁改回 legacy 这条当场红）",
                profiles.Profiles.All(p => p.Ai.MoveSet == EnemyMoveSet.Profile2),
                string.Join(",", profiles.Profiles.Where(p => p.Ai.MoveSet != EnemyMoveSet.Profile2).Select(p => p.Id)));
            var tusk = boar.FindMove("tusk");
            var rush = boar.FindMove("rush");
            Check("尖牙挑击已改为快而轻（power 6 / .18-.10-.22 / 1.3 / 8 精）",
                tusk.Power == 6 && Math.Abs(tusk.Startup - .18f) < .0001f && Math.Abs(tusk.Active - .10f) < .0001f &&
                Math.Abs(tusk.Recovery - .22f) < .0001f && Math.Abs(tusk.Range - 1.3f) < .0001f && tusk.StaminaCost == 8);
            Check("蛮牛冲撞慢而长带击退（power 10 / .50-.20-.60 / 1.65 / 20 精 / 击退 .45）",
                rush.Power == 10 && Math.Abs(rush.Startup - .5f) < .0001f && Math.Abs(rush.Active - .2f) < .0001f &&
                Math.Abs(rush.Recovery - .6f) < .0001f && Math.Abs(rush.Range - 1.65f) < .0001f &&
                rush.StaminaCost == 20 && Math.Abs(rush.Knockback - .45f) < .0001f &&
                rush.Action == CombatActionType.Heavy);
            // D-07 的硬边界：玩家裸装 60 气血，敌方单发命中结算伤害必须 ≤18（至少吃得下 3 发）。
            Check("野猪两招对裸装玩家的单发伤害都 ≤18",
                BoarStrikeDamage(tusk) <= 18 && BoarStrikeDamage(rush) <= 18,
                "挑击 " + BoarStrikeDamage(tusk) + " / 冲撞 " + BoarStrikeDamage(rush));
            Check("冲撞正好吃满单发上限（威胁靠慢与击退，不靠秒人）", BoarStrikeDamage(rush) == 18,
                BoarStrikeDamage(rush) + "");
            Check("野猪头部弱点与躯干抵抗倍率未动", Math.Abs(boar.FindPart("head").Multiplier - 1.5f) < .001f &&
                Math.Abs(boar.FindPart("head").DodgeChance - .2f) < .001f &&
                Math.Abs(boar.FindPart("body").Multiplier - 1f) < .001f &&
                Math.Abs(boar.FindPart("body").DodgeChance - .05f) < .001f);
            // D-23 新口径（架构师 2026-10-05 裁决）：`[4,12]` 只约束**躯干平砍基线**；
            // 弱点 / 重击 / 看破必暴是掌握机制的奖励路径，2~3 刀是设计目标而不是违约。
            // 所以护栏从"命中数区间"换成"深度路径不得快于躯干路径的 1/3"——按**时间**量（下面实测）。
            var lightBody = SwingsToKill(CombatActionType.Light, "body");
            var lightHead = SwingsToKill(CombatActionType.Light, "head");
            var heavyBody = SwingsToKill(CombatActionType.Heavy, "body");
            var heavyHead = SwingsToKill(CombatActionType.Heavy, "head");
            Check("躯干平砍基线仍是 4 刀放倒野猪（D-23 里它是量别的尺子，不再是区间下界）",
                lightBody == 4, lightBody + " 刀");
            Check("深度路径的命中数如实记：弱点 3 / 重击 3 / 重击弱点 2 刀（奖励，不是违约）",
                lightHead == 3 && heavyBody == 3 && heavyHead == 2,
                "轻头 " + lightHead + " / 重躯 " + heavyBody + " / 重头 " + heavyHead);
            // 真正的护栏在时间上：走完整状态机（起手→判定帧→收招，精力不够就原地等回精），
            // 把四条路径的击杀耗时量出来。谁把弱点倍率或暴击加成堆到"秒杀"，这条当场红。
            var tTorso = SecondsToKill(CombatActionType.Light, "body");
            var tHead = SecondsToKill(CombatActionType.Light, "head");
            var tHeavy = SecondsToKill(CombatActionType.Heavy, "body");
            var tHeavyHead = SecondsToKill(CombatActionType.Heavy, "head");
            var fastestDepth = Math.Min(tHead, Math.Min(tHeavy, tHeavyHead));
            Check("D-23 护栏：最快的深度路径不得快于躯干平砍的 1/3（拦\u201c倍率堆到秒杀\u201d，不拦奖励）",
                fastestDepth >= tTorso / 3f - .001f,
                "躯干平砍 " + tTorso.ToString("0.##") + " 秒 → 下限 " + (tTorso / 3f).ToString("0.##") +
                " 秒；最快深度路径 " + fastestDepth.ToString("0.##") + " 秒");
            Check("重击反而比平砍慢是精力等待的结果，不是笔误（躯干重击 > 躯干平砍）",
                tHeavy > tTorso && tHeavyHead > tHead,
                "轻躯 " + tTorso.ToString("0.##") + " / 轻头 " + tHead.ToString("0.##") +
                " / 重躯 " + tHeavy.ToString("0.##") + " / 重头 " + tHeavyHead.ToString("0.##") + " 秒");
            Check("裸装玩家吃得下三发冲撞（60 − 3×18 > 0，威胁靠慢与击退表达）",
                60 - 3 * BoarStrikeDamage(rush) > 0, "三发后剩 " + (60 - 3 * BoarStrikeDamage(rush)));
            Check("野猪掉落仍是 larou×2 + caoyao×3（EnemyChecks 钉死，只能追加）",
                DropCount(boar, "larou") == 2 && DropCount(boar, "caoyao") == 3);

            var wolf = profiles.Find("nether-wolf");
            Check("冥狼 90/24/4/110/35 逐字落地", wolf.MaxHp == 90 && wolf.Attack == 24 && wolf.Defense == 4 &&
                wolf.MaxStamina == 110 && wolf.Coins == 35);
            Check("冥狼后腿弱筋 1.4x 抵抗 25%", Math.Abs(wolf.FindPart("leg").Multiplier - 1.4f) < .001f &&
                Math.Abs(wolf.FindPart("leg").DodgeChance - .25f) < .001f);
            Check("冥狼移速高于野猪（高移速生态位）", wolf.MoveSpeed > boar.MoveSpeed);
            Check("冥狼前爪重击挂 3 秒流血", Math.Abs(wolf.FindMove("rend").BleedSeconds - 3f) < .001f);
            Check("冥狼行为树 wolf-pounce / profile2", wolf.Ai.Behavior == "wolf-pounce" &&
                wolf.Ai.MoveSet == EnemyMoveSet.Profile2);
            Check("冥狼掉落已接契约表（冥狼皮×2、冥狼牙×1）",
                DropCount(wolf, "wolf-fur") == 2 && DropCount(wolf, "wolf-fang") == 1 &&
                DropCount(wolf, "caoyao") == 0);

            var ape = profiles.Find("stone-mandrill");
            Check("山魈 200/30/15/70/60 逐字落地", ape.MaxHp == 200 && ape.Attack == 30 && ape.Defense == 15 &&
                ape.MaxStamina == 70 && ape.Coins == 60);
            Check("山魈后背旧伤 2.0x 抵抗 35%", Math.Abs(ape.FindPart("back-scar").Multiplier - 2f) < .001f &&
                Math.Abs(ape.FindPart("back-scar").DodgeChance - .35f) < .001f);
            Check("山魈石化前胸 0.6x 且标霸体", Math.Abs(ape.FindPart("stone-chest").Multiplier - .6f) < .001f &&
                ape.FindPart("stone-chest").Bulwark);
            Check("撼地重拳 .6/.25/.7 带击退与霸体", Math.Abs(ape.FindMove("smash").Startup - .6f) < .001f &&
                Math.Abs(ape.FindMove("smash").Active - .25f) < .001f &&
                Math.Abs(ape.FindMove("smash").Recovery - .7f) < .001f &&
                Math.Abs(ape.FindMove("smash").Knockback - .35f) < .001f &&
                ape.FindMove("smash").Bulwark);
            Check("山魈平攻面就是石化前胸（正面硬吃 0.6x）", ape.DefaultPart.Id == "stone-chest");
            Check("最高弱点挑选落在后背旧伤", ape.BestWeakPoint.Id == "back-scar");
            Check("山魈掉落已接契约表（山魈石壳×1、铁矿×3）",
                DropCount(ape, "mandrill-stone") == 1 && DropCount(ape, "iron-ore") == 3 &&
                DropCount(ape, "fulu") == 0);

            Check("野猪掉落已追加兽胆，且 larou×2/caoyao×3 未被替换（EnemyChecks 钉死）",
                DropCount(boar, "beast-gall") == 1 && DropCount(boar, "larou") == 2 && DropCount(boar, "caoyao") == 3);
            // D-10：不要求 pending 归零（items.json 由员工 O 落名，本轮我这边可能是干净检出）。
            // 牙齿留在"表外 id 当场红"——有人自造掉落 id 一样过不去。归零由架构师在合流时验。
            Check("掉落 pending 只可能是 ADR-031 那 6 个 id（O 未落名时如实降级，不硬抛）",
                profiles.PendingDropItems.Keys.All(id => ContractItemIds.Contains(id)),
                string.Join(",", profiles.PendingDropItems.Keys));
            Check("三只妖的部位总数为 6（每妖两部位）", profiles.Profiles.Sum(p => p.Parts.Count) == 6);
        }

        static int DropCount(EnemyProfile profile, string itemId) =>
            legacyEnemies.Find(profile.Id).Drops.FirstOrDefault(d => d.ItemId == itemId)?.Count ?? 0;

        // ---- 坏表必须被拒（配表出错要在解析期炸，不是运行时悄悄降级）----
        static void SuiteBadTables()
        {
            var boarParts = "\"hitProfile\":[{\"part\":\"body\",\"multiplier\":1.0}],";
            var cases = new[]
            {
                ("部位倍率两处对不上", "{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0," +
                    "\"stamina\":10,\"moveSpeed\":1," + boarParts +
                    "\"parts\":[{\"id\":\"body\",\"multiplier\":1.5,\"name\":\"身\"},{\"id\":\"head\",\"multiplier\":1,\"name\":\"头\"}]," +
                    "\"attacks\":[{\"id\":\"x\",\"name\":\"撞\",\"action\":\"light\",\"power\":10,\"startup\":.12,\"active\":.08,\"recovery\":.18,\"range\":1.25}],\"ai\":{\"behavior\":\"b\",\"moveset\":\"profile2\",\"preferredRange\":1,\"aggressionSeconds\":1},\"drops\":[],\"coins\":0}]}"),
                ("parts 漏了 hitProfile 里的部位", "{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0," +
                    "\"stamina\":10,\"moveSpeed\":1,\"hitProfile\":[{\"part\":\"body\",\"multiplier\":1},{\"part\":\"tail\",\"multiplier\":1}]," +
                    "\"parts\":[{\"id\":\"body\",\"multiplier\":1,\"name\":\"身\"},{\"id\":\"head\",\"multiplier\":1,\"name\":\"头\"}]," +
                    "\"attacks\":[{\"id\":\"x\",\"name\":\"撞\",\"action\":\"light\",\"power\":10,\"startup\":.12,\"active\":.08,\"recovery\":.18,\"range\":1.25}],\"ai\":{\"behavior\":\"b\",\"moveset\":\"profile2\",\"preferredRange\":1,\"aggressionSeconds\":1},\"drops\":[],\"coins\":0}]}"),
                ("legacy 招式集却没有基线轻击镜像", "{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0," +
                    "\"stamina\":10,\"moveSpeed\":1," + boarParts +
                    "\"parts\":[{\"id\":\"body\",\"multiplier\":1,\"name\":\"身\"},{\"id\":\"head\",\"multiplier\":1.5,\"name\":\"头\"}]," +
                    "\"attacks\":[{\"id\":\"x\",\"name\":\"撞\",\"action\":\"light\",\"power\":22,\"startup\":.4,\"active\":.2,\"recovery\":.5,\"range\":1.25}],\"ai\":{\"behavior\":\"b\",\"moveset\":\"legacy\",\"preferredRange\":1,\"aggressionSeconds\":1},\"drops\":[],\"coins\":0}]}"),
                ("招式前摇为 0", "{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0," +
                    "\"stamina\":10,\"moveSpeed\":1," + boarParts +
                    "\"parts\":[{\"id\":\"body\",\"multiplier\":1,\"name\":\"身\"},{\"id\":\"head\",\"multiplier\":1.5,\"name\":\"头\"}]," +
                    "\"attacks\":[{\"id\":\"x\",\"name\":\"撞\",\"action\":\"light\",\"power\":10,\"startup\":0,\"active\":.08,\"recovery\":.18,\"range\":1.25}],\"ai\":{\"behavior\":\"b\",\"moveset\":\"profile2\",\"preferredRange\":1,\"aggressionSeconds\":1},\"drops\":[],\"coins\":0}]}"),
                ("招式动作类型不合法", "{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0," +
                    "\"stamina\":10,\"moveSpeed\":1," + boarParts +
                    "\"parts\":[{\"id\":\"body\",\"multiplier\":1,\"name\":\"身\"},{\"id\":\"head\",\"multiplier\":1.5,\"name\":\"头\"}]," +
                    "\"attacks\":[{\"id\":\"x\",\"name\":\"撞\",\"action\":\"dodge\",\"power\":10,\"startup\":.12,\"active\":.08,\"recovery\":.18,\"range\":1.25}],\"ai\":{\"behavior\":\"b\",\"moveset\":\"profile2\",\"preferredRange\":1,\"aggressionSeconds\":1},\"drops\":[],\"coins\":0}]}"),
                ("只有一个部位", "{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0," +
                    "\"stamina\":10,\"moveSpeed\":1," + boarParts +
                    "\"parts\":[{\"id\":\"body\",\"multiplier\":1,\"name\":\"身\"}]," +
                    "\"attacks\":[{\"id\":\"x\",\"name\":\"撞\",\"action\":\"light\",\"power\":10,\"startup\":.12,\"active\":.08,\"recovery\":.18,\"range\":1.25}],\"ai\":{\"behavior\":\"b\",\"moveset\":\"profile2\",\"preferredRange\":1,\"aggressionSeconds\":1},\"drops\":[],\"coins\":0}]}"),
                ("moveset 写错", "{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0," +
                    "\"stamina\":10,\"moveSpeed\":1," + boarParts +
                    "\"parts\":[{\"id\":\"body\",\"multiplier\":1,\"name\":\"身\"},{\"id\":\"head\",\"multiplier\":1.5,\"name\":\"头\"}]," +
                    "\"attacks\":[{\"id\":\"x\",\"name\":\"撞\",\"action\":\"light\",\"power\":10,\"startup\":.12,\"active\":.08,\"recovery\":.18,\"range\":1.25}],\"ai\":{\"behavior\":\"b\",\"moveset\":\"v2\",\"preferredRange\":1,\"aggressionSeconds\":1},\"drops\":[],\"coins\":0}]}"),
                ("缺生态位", "{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0," +
                    "\"stamina\":10,\"moveSpeed\":1," + boarParts +
                    "\"parts\":[{\"id\":\"body\",\"multiplier\":1,\"name\":\"身\"},{\"id\":\"head\",\"multiplier\":1.5,\"name\":\"头\"}]," +
                    "\"attacks\":[{\"id\":\"x\",\"name\":\"撞\",\"action\":\"light\",\"power\":10,\"startup\":.12,\"active\":.08,\"recovery\":.18,\"range\":1.25}],\"ai\":{\"behavior\":\"b\",\"moveset\":\"profile2\",\"preferredRange\":1,\"aggressionSeconds\":1},\"drops\":[],\"coins\":0}]}"),
            };
            foreach (var (label, json) in cases)
            {
                var error = Throws(() =>
                {
                    var table = EnemyProfileTable.Load(json);
                    table.CrossCheck(EnemyTable.Parse(json));
                });
                Check("坏表被拒：" + label, error.Length > 0, error);
            }
        }

        // ---- M2 部位打击数学 ----
        static void SuitePartsAndMath()
        {
            var hero = Fighter("hero", hp: 300, attack: 10, defense: 1, stamina: 100);
            var boar = Fighter("boar", hp: 99999, attack: 9, defense: 2, stamina: 60);
            var ape = Fighter("ape", hp: 99999, attack: 30, defense: 15, stamina: 60);

            Check("躯干平攻：(10+10)×1.0−2 = 18",
                Strike(hero, boar, CombatActionType.Light, 0f, CombatDepth.StrikeFor(profiles.Find("boar-demon"), "")).Damage == 18);
            Check("躯干平攻不算暴击（既有夹具的地基）", !lastResult.Critical);
            var head = Strike(hero, boar, CombatActionType.Light, 1f, CombatDepth.StrikeFor(profiles.Find("boar-demon"), "head"));
            Check("弱点头部：round(20×1.5)−2 = 28", head.Damage == 28, head.Damage + "");
            Check("弱点命中判为暴击并报出部位", head.Critical && head.PartId == "head");

            var scar = Strike(hero, ape, CombatActionType.Light, 2f, CombatDepth.StrikeFor(profiles.Find("stone-mandrill"), "back-scar"));
            Check("山魈后背旧伤：round(20×2.0)−15 = 25", scar.Damage == 25, scar.Damage + "");
            var chest = Strike(hero, ape, CombatActionType.Light, 3f, CombatDepth.StrikeFor(profiles.Find("stone-mandrill"), "stone-chest"));
            Check("石化前胸 0.6x 被高防压到最少 1 点", chest.Damage == 1 && !chest.Critical, chest.Damage + "");

            var boosted = CombatDepth.StrikeFor(profiles.Find("boar-demon"), "head", critBonus: .2f);
            Check("暴击倍率加成进算术：round(20×1.5×1.2)−2 = 34",
                Strike(hero, boar, CombatActionType.Light, 4f, boosted).Damage == 34);

            var ignores = new PartStrike { PartId = "back-scar", Multiplier = 2f, WeakPoint = true, IgnoreDefense = true };
            Check("无视防御：round(20×2.0) = 40", Strike(hero, ape, CombatActionType.Light, 5f, ignores).Damage == 40);

            var legacySame = StrikeLegacy(hero, boar, CombatActionType.Heavy, 6f, 1.5f);
            var newSame = Strike(hero, boar, CombatActionType.Heavy, 7f, new PartStrike { Multiplier = 1.5f });
            Check("旧入口与新入口同数：重击 1.5x 都是 46", legacySame.Damage == 46 && newSame.Damage == 46,
                legacySame.Damage + " / " + newSame.Damage);

            // 确定性抵抗计（任务书"受闪避抵抗率提升"）：0.25 是二进制精确分数，第 4 下必然落空。
            var wolfProfile = profiles.Find("nether-wolf");
            var dummy = Fighter("dummy", hp: 99999, attack: 1, defense: 0, stamina: 100);
            var leg = CombatDepth.StrikeFor(wolfProfile, "leg");
            var hits = 0; var whiffs = 0; var hpAtWhiff = 0; var hpBeforeWhiff = 0;
            for (var i = 0; i < 4; i++)
            {
                var before = dummy.Hp;
                var r = Strike(hero, dummy, CombatActionType.Light, 10f + i * 1f, leg);
                if (r.Hit) hits++;
                else { whiffs++; hpBeforeWhiff = before; hpAtWhiff = dummy.Hp; }
            }
            Check("冥狼后腿抵抗 25%：前三下打中、第四下落空", hits == 3 && whiffs == 1, "命中 " + hits + " 落空 " + whiffs);
            Check("落空如实标记并给出原因", !lastResult.Hit && lastResult.Whiffed && lastResult.Reason.Contains("挡开"),
                lastResult.Reason);
            Check("抵抗计落空后归零（不会连着空砍）", dummy.DodgeGauge == 0f, dummy.DodgeGauge + "");
            var bodyAgain = Strike(hero, dummy, CombatActionType.Light, 14f, CombatDepth.StrikeFor(wolfProfile, "body"));
            Check("换到精瘦狼躯照样打中（抵抗 10% 从零攒）", bodyAgain.Hit);
            var zeroGauge = Strike(hero, dummy, CombatActionType.Light, 15f,
                CombatDepth.StrikeFor(profiles.Find("stone-mandrill"), "stone-chest"));
            Check("打 0 抵抗部位时抵抗计被清空", zeroGauge.Hit && dummy.DodgeGauge == 0f);
            Check("落空那一刀不扣守方一滴血", hpAtWhiff == hpBeforeWhiff && hpBeforeWhiff > 0,
                hpBeforeWhiff + " → " + hpAtWhiff);
        }

        // ---- M1/M3 招式三相时序 ----
        static void SuiteMoveTimings()
        {
            var wolfProfile = profiles.Find("nether-wolf");
            var wolf = Fighter("wolf", hp: 90, attack: 24, defense: 4, stamina: 110);
            var post = Fighter("post", hp: 99999, attack: 0, defense: 0, stamina: 100);
            var bite = wolfProfile.FindMove("bite");

            CombatRules.TryAct(wolf, CombatActionType.Light, 0f, post, bite);
            Check("招式起手落在 Startup", wolf.Phase == CombatPhase.Startup);
            CombatRules.Tick(wolf, .19f);
            Check("前摇没走完不许进判定帧（.19 < .20）", wolf.Phase == CombatPhase.Startup);
            CombatRules.Tick(wolf, .20f);
            Check("前摇走完进判定帧（.20）", wolf.Phase == CombatPhase.Active);
            CombatRules.Tick(wolf, .34f);
            Check("判定帧持续到 .35（.34 还在窗内）", wolf.Phase == CombatPhase.Active);
            CombatRules.Tick(wolf, .35f);
            Check("判定帧结束进收招（.35）", wolf.Phase == CombatPhase.Recovery);
            CombatRules.Tick(wolf, .69f);
            Check("收招持续到 .70", wolf.Phase == CombatPhase.Recovery);
            CombatRules.Tick(wolf, .70f);
            Check("三相走完回到静立且招式槽清空", wolf.Phase == CombatPhase.Idle && wolf.CurrentMove == null);

            var apeProfile = profiles.Find("stone-mandrill");
            var ape = Fighter("ape", hp: 99999, attack: 30, defense: 15, stamina: 70);
            ape.X = 1f; post.X = 1.4f;
            var smash = apeProfile.FindMove("smash");
            CombatRules.TryAct(ape, CombatActionType.Heavy, 0f, post, smash);
            CombatRules.Tick(ape, .55f);
            Check("山魈重拳前摇 .60 没走完（掉帧也拉不长动作）", ape.Phase == CombatPhase.Startup);
            CombatRules.Tick(ape, .60f);
            Check("山魈重拳前摇 .60 走完进判定帧", ape.Phase == CombatPhase.Active);
            var progress = CombatRules.ActiveProgress(ape, .75f);
            Check("判定帧内进度落在 0..1 的中后段", progress > .5f && progress <= 1f, progress.ToString("0.###"));
            var landed = CombatRules.ResolveStrike(ape, post, .80f);
            Check("重拳基准伤害按招式表走：(30+38)−0 = 68", landed.Damage == 68, landed.Damage + "");
            Check("击退把对手推开 .35 个单位", Math.Abs(post.X - 1.75f) < .001f, post.X + "");
            CombatRules.Tick(ape, 1.6f);
            Check("重拳三相走完（含 .70 收招）回到静立", ape.Phase == CombatPhase.Idle);

            var far = Fighter("far", hp: 99999, attack: 0, defense: 0, stamina: 100);
            far.X = 6f;
            CombatRules.TryAct(ape, CombatActionType.Heavy, 2f, far, smash);
            CombatRules.Tick(ape, 2.7f);
            var miss = CombatRules.ResolvePartStrike(ape, far, 2.8f, CombatDepth.StrikeFor(apeProfile, "back-scar"));
            Check("超出招式射程就够不着并标记空砍", !miss.Hit && miss.Whiffed && miss.Reason.Contains("够不着"));
            CombatRules.Tick(ape, 2.9f);
            Check("空砍进入收招", ape.Phase == CombatPhase.Recovery);
            CombatRules.Tick(ape, 3.6f);
            Check("空砍后摇被加长（whiffPenalty .25：3.6 还没收干净）", ape.Phase == CombatPhase.Recovery);
            CombatRules.Tick(ape, 3.8f);
            Check("加长后的后摇在 3.80 收干净", ape.Phase == CombatPhase.Idle);
        }

        // ---- M3 精力流转 ----
        static void SuiteStamina()
        {
            Check("精力档位仍是基线 8/20/12（只读夹具锁定，不做可调参数）",
                CombatRules.LightStaminaCost == 8 && CombatRules.HeavyStaminaCost == 20 && CombatRules.DodgeStaminaCost == 12);
            Check("2.0 档案回复速度 25/秒", CombatDepth.Standard20.StaminaRegenPerSecond == 25f);
            Check("2.0 档案无敌窗 .25 秒", Math.Abs(CombatDepth.Standard20.DodgeActiveSeconds - .25f) < .0001f);
            Check("2.0 档案看破窗 .15 秒", Math.Abs(CombatDepth.Standard20.RiposteWindowSeconds - .15f) < .0001f);

            var tuned = Fighter("tuned", hp: 60, attack: 5, defense: 0, stamina: 100);
            tuned.Tuning = CombatDepth.Standard20;
            CombatRules.TryAct(tuned, CombatActionType.Heavy, 0f);
            CombatRules.Tick(tuned, .72f);
            CombatRules.Regenerate(tuned, .4f);
            Check("档案单位 .4 秒回 10 点（25/秒）", tuned.Stamina == 90, tuned.Stamina + "");
            var bare = Fighter("bare", hp: 60, attack: 5, defense: 0, stamina: 100);
            CombatRules.TryAct(bare, CombatActionType.Heavy, 0f);
            CombatRules.Tick(bare, .72f);
            CombatRules.Regenerate(bare, .4f);
            Check("无档案单位仍按基线 6/秒（缺省即基线）", bare.Stamina == 82, bare.Stamina + "");

            var busy = Fighter("busy", hp: 60, attack: 5, defense: 0, stamina: 40);
            busy.Tuning = CombatDepth.Standard20;
            CombatRules.TryAct(busy, CombatActionType.Light, 0f);
            CombatRules.Regenerate(busy, 1f);
            Check("动作中一律不回精力", busy.Stamina == 32, busy.Stamina + "");
            var full = Fighter("full", hp: 60, attack: 5, defense: 0, stamina: 10);
            full.Tuning = CombatDepth.Standard20;
            CombatRules.Regenerate(full, 10f);
            Check("回复不越过上限", full.Stamina == full.MaxStamina);
            var sliver = Fighter("sliver", hp: 60, attack: 5, defense: 0, stamina: 100);
            sliver.Tuning = new CombatTuning(1.5f, .25f, .15f);
            CombatRules.TryAct(sliver, CombatActionType.Light, 0f);
            CombatRules.Tick(sliver, .39f);
            CombatRules.Regenerate(sliver, .2f);
            Check("不足 1 点的零头攒住不丢", sliver.Stamina == 92, sliver.Stamina + "");
            CombatRules.Regenerate(sliver, .5f);
            Check("零头攒够就发（低帧率也能回精）", sliver.Stamina == 93, sliver.Stamina + "");

            var drained = Fighter("drained", hp: 60, attack: 5, defense: 0, stamina: 15);
            var refused = CombatRules.TryAct(drained, CombatActionType.Heavy, 0f);
            Check("精力不够时重击被拒且一分不扣", !refused.Executed && drained.Stamina == 15);
            Check("Core 的拒绝路径保持干净（不会自己把人锁进硬直）", drained.Phase == CombatPhase.Idle);
            var broke = Fighter("broke", hp: 60, attack: 5, defense: 0, stamina: 5);
            Check("精力不够时闪避同样被拒", !CombatRules.TryAct(broke, CombatActionType.Dodge, 0f).Executed &&
                broke.Stamina == 5);
            CombatRules.EnterExposure(drained, 0f);
            Check("表现层施加的透支破绽把人锁进硬直", drained.Phase == CombatPhase.Hitstun);
            Check("破绽期间轻击做不了", !CombatRules.TryAct(drained, CombatActionType.Light, .1f).Executed);
            CombatRules.Tick(drained, .34f);
            Check("破绽 .35 秒还没走完", drained.Phase == CombatPhase.Hitstun);
            CombatRules.Tick(drained, .35f);
            Check("破绽走完恢复静立", drained.Phase == CombatPhase.Idle);
            CombatRules.EnterExposure(drained, .36f);
            Check("破绽中闪避同样被拒（不给白嫖位移）", !CombatRules.TryAct(drained, CombatActionType.Dodge, .4f).Executed);

            var apeTuned = Fighter("apeTuned", hp: 60, attack: 5, defense: 0, stamina: 60);
            apeTuned.Tuning = CombatDepth.Standard20;
            CombatRules.TryAct(apeTuned, CombatActionType.Dodge, 0f);
            CombatRules.Tick(apeTuned, .05f);
            CombatRules.Tick(apeTuned, .28f);
            Check("档案下 .28 仍在 .25 秒无敌窗内", apeTuned.Invulnerable);
            CombatRules.Tick(apeTuned, .31f);
            Check("档案下 .31 出窗", !apeTuned.Invulnerable);
            var apeBare = Fighter("apeBare", hp: 60, attack: 5, defense: 0, stamina: 60);
            CombatRules.TryAct(apeBare, CombatActionType.Dodge, 0f);
            CombatRules.Tick(apeBare, .05f);
            CombatRules.Tick(apeBare, .28f);
            Check("无档案仍是基线 .22 窗（CombatChecks 的出窗时刻不破）", !apeBare.Invulnerable);

            var wolfProfile = profiles.Find("nether-wolf");
            var wolf = Fighter("wolf", hp: 90, attack: 24, defense: 4, stamina: 12);
            Check("招式自带精力档生效（撕咬 8 点）",
                CombatRules.TryAct(wolf, CombatActionType.Light, 0f, null, wolfProfile.FindMove("bite")).Executed &&
                wolf.Stamina == 4, wolf.Stamina + "");
        }

        // ---- M3 精准闪避与看破 ----
        static void SuiteRiposte()
        {
            var apeProfile = profiles.Find("stone-mandrill");
            var smash = apeProfile.FindMove("smash");

            // 正例：判定帧刚开（.05 ≤ .15 窗）起闪 → 让开这一击 → 看破。
            var hero = Fighter("hero", hp: 100, attack: 10, defense: 1, stamina: 100);
            hero.Tuning = CombatDepth.Standard20;
            var ape = Fighter("ape", hp: 99999, attack: 30, defense: 15, stamina: 100);
            hero.X = 0f; ape.X = 1f;
            CombatRules.TryAct(ape, CombatActionType.Heavy, 0f, hero, smash);
            CombatRules.Tick(ape, .6f);
            Check("山魈进判定帧（看破窗口打开）", ape.Phase == CombatPhase.Active);
            CombatRules.TryAct(hero, CombatActionType.Dodge, .65f, ape);
            Check("判定帧刚开就起闪：这一闪被记为看得准", hero.RipostePending);
            CombatRules.Tick(hero, .71f);
            Check("闪避进入无敌窗", hero.Phase == CombatPhase.Active && hero.Invulnerable);
            var evaded = CombatRules.ResolveStrike(ape, hero, .8f);
            Check("无敌窗内这一击被让开", !evaded.Hit && evaded.Reason.Contains("闪"), evaded.Reason);
            Check("让开的那一刻真的看破了", hero.CounterReady && !hero.RipostePending);
            CombatRules.Tick(hero, 1.1f);   // 先把玩家从闪避三相里推回静立，否则这一击根本发不出去
            var punish = Strike(hero, ape, CombatActionType.Light, 1.2f,
                CombatDepth.StrikeFor(apeProfile, "stone-chest", counterReady: true));
            Check("看破一击自动落到最高弱点（不打石化前胸）", punish.PartId == "back-scar" && punish.Critical);
            Check("看破一击无视防御：round(20×2.0) = 40", punish.Damage == 40, punish.Damage + "");
            Check("看破只用一次", !hero.CounterReady);
            CombatRules.Tick(ape, 3f);
            var follow = Strike(hero, ape, CombatActionType.Light, 3.1f,
                CombatDepth.StrikeFor(apeProfile, "stone-chest"));
            Check("下一击回到普通结算（不再无视防御）", !follow.Critical && follow.Damage == 1, follow.Damage + "");

            // 反例一：判定帧已经过半才想起闪（.20 > .15 窗）→ 不算看得准，而且这一刀照吃。
            var late = Fighter("late", hp: 100, attack: 10, defense: 1, stamina: 100);
            late.Tuning = CombatDepth.Standard20;
            var ape2 = Fighter("ape2", hp: 99999, attack: 30, defense: 15, stamina: 100);
            late.X = 0f; ape2.X = 1f;
            CombatRules.TryAct(ape2, CombatActionType.Heavy, 0f, late, smash);
            CombatRules.Tick(ape2, .6f);
            CombatRules.TryAct(late, CombatActionType.Dodge, .8f, ape2);
            Check("判定帧已过半（.20 > .15）起闪不武装看破", !late.RipostePending);
            var tooLate = CombatRules.ResolveStrike(ape2, late, .84f);
            Check("起闪太晚：这一刀照吃，也没换到反击权", tooLate.Hit && late.Hp < 100 && !late.CounterReady,
                tooLate.Reason + " / 余血 " + late.Hp);

            // 反例二：对手前摇才刚起（还有 .50 秒才落判定帧）→ 不算看得准。
            var earlyDuck = Fighter("earlyDuck", hp: 100, attack: 5, defense: 0, stamina: 100);
            earlyDuck.Tuning = CombatDepth.Standard20;
            var ape3 = Fighter("ape3", hp: 99999, attack: 30, defense: 15, stamina: 100);
            earlyDuck.X = 0f; ape3.X = 1f;
            CombatRules.TryAct(ape3, CombatActionType.Heavy, 0f, earlyDuck, smash);
            CombatRules.TryAct(earlyDuck, CombatActionType.Dodge, .1f, ape3);
            Check("前摇才起步就闪（还有 .50 秒）不武装", !earlyDuck.RipostePending);

            // 正例（预判）：Core 在对手进判定帧的那一帧就把这一击结算掉，所以"看破"在真页面上
            // 只可能来自判定帧前 .15 秒内的起闪——这条也正是玩家做得到的那一种。
            var ahead = Fighter("ahead", hp: 100, attack: 10, defense: 1, stamina: 100);
            ahead.Tuning = CombatDepth.Standard20;
            ahead.X = 0f;
            CombatRules.TryAct(ahead, CombatActionType.Dodge, .5f, ape3);
            Check("预判在判定帧前 .10 秒起闪：算看得准", ahead.RipostePending);
            CombatRules.Tick(ape3, .6f);
            CombatRules.Tick(ahead, .62f);
            var slipped = CombatRules.ResolveStrike(ape3, ahead, .65f);
            Check("预判到位：这一击被让开并换来反击权", !slipped.Hit && ahead.CounterReady, slipped.Reason);

            // 反例三：对手静立 → 给不出看破窗。
            var idle = Fighter("idle", hp: 100, attack: 5, defense: 0, stamina: 100);
            var quiet = Fighter("quiet", hp: 100, attack: 5, defense: 0, stamina: 100);
            CombatRules.TryAct(idle, CombatActionType.Dodge, 0f, quiet);
            Check("静立对手给不出看破窗", !idle.RipostePending);
            Check("等待动作不武装看破", !CombatRules.TryAct(idle, CombatActionType.Wait, .1f, quiet).Reason.Contains("看破"));
        }

        // ---- 流血与霸体反震 ----
        static void SuiteBleedAndBulwark()
        {
            var wolfProfile = profiles.Find("nether-wolf");
            var wolf = Fighter("wolf", hp: 90, attack: 24, defense: 4, stamina: 110);
            var rider = Fighter("rider", hp: 200, attack: 5, defense: 1, stamina: 100);
            wolf.X = 0f; rider.X = .8f;
            CombatRules.TryAct(wolf, CombatActionType.Heavy, 0f, rider, wolfProfile.FindMove("rend"));
            CombatRules.Tick(wolf, .4f);
            var torn = CombatRules.ResolveStrike(wolf, rider, .45f);
            Check("前爪重击撕咬先咬实：(24+18)−1 = 41", torn.Damage == 41, torn.Damage + "");
            Check("这一口挂住 3 秒流血", Math.Abs(torn.BleedSeconds - 3f) < .001f && rider.BleedDamage == 3);
            CombatRules.Tick(rider, .9f);
            Check("流血未到第一口不许掉血", rider.Hp == 159, rider.Hp + "");
            CombatRules.Tick(rider, .96f);
            Check("满 .5 秒掉第一口（3 点）", rider.Hp == 156, rider.Hp + "");
            CombatRules.Tick(rider, 1.4f);
            Check("两口之间不许提前掉血", rider.Hp == 156, rider.Hp + "");
            CombatRules.Tick(rider, 1.5f);
            Check("第二口照掉", rider.Hp == 153, rider.Hp + "");
            CombatRules.Tick(rider, 3.4f);
            Check("流血期间硬直/静立都拦不住它（状态机不吞流血）", rider.Hp == 150, rider.Hp + "");
            CombatRules.Tick(rider, 3.6f);
            Check("3 秒后血自动止住且不再掉", rider.BleedDamage == 0 && rider.Hp == 150, rider.Hp + "");
            CombatRules.Regenerate(rider, 1f);
            Check("流血不止到把人放倒为止（这里还活着）", !rider.IsDead);

            var apeProfile = profiles.Find("stone-mandrill");
            var ape = Fighter("ape", hp: 99999, attack: 30, defense: 15, stamina: 100);
            var jabber = Fighter("jabber", hp: 100, attack: 10, defense: 1, stamina: 100);
            jabber.X = 0f; ape.X = 1f;
            CombatRules.TryAct(ape, CombatActionType.Heavy, 0f, jabber, apeProfile.FindMove("smash"));
            CombatRules.Tick(ape, .6f);
            Check("山魈重拳判定期间霸体成立", ape.Phase == CombatPhase.Active && ape.CurrentMove.Bulwark);
            var apeHpBefore = ape.Hp;
            CombatRules.TryAct(jabber, CombatActionType.Light, .62f);
            CombatRules.Tick(jabber, .75f);
            var reflected = CombatRules.ResolvePartStrike(jabber, ape, .78f,
                CombatDepth.StrikeFor(apeProfile, "stone-chest"));
            Check("打正面霸体仍掉血（0.6x 被高防压到 1 点）", reflected.Hit && reflected.Damage == 1);
            Check("霸体不吃硬直（还在自己的判定帧里）", ape.Phase == CombatPhase.Active && ape.Hp < apeHpBefore);
            Check("反震把攻击方震出破绽", reflected.Recoiled && jabber.Phase == CombatPhase.Hitstun);
            Check("反震掉攻击方 6 点（守方攻 30÷5）", jabber.Hp == 94, jabber.Hp + "");

            var sneaky = Fighter("sneaky", hp: 100, attack: 10, defense: 1, stamina: 100);
            var back = Strike(sneaky, ape, CombatActionType.Light, 5f, CombatDepth.StrikeFor(apeProfile, "back-scar"));
            Check("绕到后背旧伤不打霸体（不反震且算暴击）", back.Critical && !back.Recoiled);
        }

        // ---- M1 行为树 ----
        static void SuiteEnemyAi()
        {
            var wolfProfile = profiles.Find("nether-wolf");
            var state = EnemyAi.NewState(0f);
            var wolf = Fighter("wolf", hp: 90, attack: 24, defense: 4, stamina: 110);
            var prey = Fighter("prey", hp: 100, attack: 5, defense: 0, stamina: 60);
            wolf.X = 0f; prey.X = .8f;
            var first = EnemyAi.Choose(wolfProfile, state, wolf, prey, 0f);
            Check("冥狼第一下出锁喉撕咬", first != null && first.Id == "bite", first?.Id);
            EnemyAi.MarkActed(state, 0f, wolfProfile.Ai.AggressionSeconds, "bite");
            Check("出手后进入 1.15 秒冷却", Math.Abs(state.NextActAt - 1.15f) < .001f && state.Chain == 1);
            var tooSoon = EnemyAi.Choose(wolfProfile, state, wolf, prey, 1.0f);
            Check("冷却没走完不出手", tooSoon == null);
            var second = EnemyAi.Choose(wolfProfile, state, wolf, prey, 1.2f);
            Check("连段第二下仍压着咬", second != null && second.Id == "bite");
            EnemyAi.MarkActed(state, 1.2f, wolfProfile.Ai.AggressionSeconds, "bite");
            Check("连段计数按 3 拍走", state.Chain == 2);
            var third = EnemyAi.Choose(wolfProfile, state, wolf, prey, 2.4f);
            Check("第三拍换成前爪重击撕咬（挂流血那下）", third != null && third.Id == "rend", third?.Id);

            var distant = Fighter("distant", hp: 100, attack: 5, defense: 0, stamina: 60);
            distant.X = 4f;
            Check("够不着就不出招（交给逼近分支）",
                EnemyAi.Choose(wolfProfile, EnemyAi.NewState(0f), wolf, distant, 0f) == null);
            Check("逼近方向指向猎物", Math.Abs(EnemyAi.ApproachDirection(wolfProfile, wolf, distant) - 1f) < .001f);
            Check("站到位就不再挪（方向 0）",
                Math.Abs(EnemyAi.ApproachDirection(wolfProfile, wolf, prey)) < .0001f,
                EnemyAi.ApproachDirection(wolfProfile, wolf, prey) + "");

            var boarProfile = profiles.Find("boar-demon");
            var boar = Fighter("boar", hp: 60, attack: 9, defense: 2, stamina: 50);
            boar.X = 0f;
            // N1 之后野猪归行为树管：第一拍尖牙挑击，第三拍才舍得撞。
            var boarAi = EnemyAi.NewState(0f);
            var firstBoarMove = EnemyAi.Choose(boarProfile, boarAi, boar, prey, 0f);
            Check("野猪切 profile2 后行为树接管出手（第一拍尖牙挑击）",
                firstBoarMove != null && firstBoarMove.Id == "tusk", firstBoarMove?.Id ?? "null");
            EnemyAi.MarkActed(boarAi, 0f, boarProfile.Ai.AggressionSeconds, "tusk");
            EnemyAi.MarkActed(boarAi, 1.5f, boarProfile.Ai.AggressionSeconds, "tusk");
            var thirdBoarMove = EnemyAi.Choose(boarProfile, boarAi, boar, prey, 3f);
            Check("野猪第三拍才甩蛮牛冲撞（慢而长的威胁表达）",
                thirdBoarMove != null && thirdBoarMove.Id == "rush", thirdBoarMove?.Id ?? "null");
            // 反向守卫：legacy 招式集是留给"没登记 2.0 数据的妖"的兜底通道——声明 legacy 时
            // 行为树必须撒手（出手交回表现层的旧分支），否则旧手感会被悄悄抢跑。
            var legacyProfile = new EnemyProfile("probe", "探针", "镇郊草伏", 60, 9, 2, 50, 60f, 24,
                boarProfile.Parts, boarProfile.Moves,
                new EnemyAiProfile("boar-charge", EnemyMoveSet.Legacy, 1.1f, 1.5f));
            Check("声明 legacy 时行为树立刻撒手（反向守卫，防旧手感被抢跑）",
                EnemyAi.Choose(legacyProfile, EnemyAi.NewState(0f), boar, prey, 0f) == null);
            var tired = Fighter("tired", hp: 90, attack: 24, defense: 4, stamina: 5);
            tired.X = 0f;
            Check("精力不够就不出空招", EnemyAi.Choose(wolfProfile, EnemyAi.NewState(0f), tired, prey, 0f) == null);

            var fallen = Fighter("fallen", hp: 1, attack: 1, defense: 0, stamina: 50);
            var executioner = Fighter("executioner", hp: 100, attack: 99, defense: 0, stamina: 100);
            fallen.X = executioner.X = 0f;
            CombatRules.TryAct(executioner, CombatActionType.Light, 0f);
            CombatRules.Tick(executioner, .13f);
            CombatRules.ResolveStrike(executioner, fallen, .14f);
            Check("先把它打死（用例前置）", fallen.IsDead);
            Check("已倒下的单位不再出手",
                EnemyAi.Choose(wolfProfile, EnemyAi.NewState(0f), fallen, prey, 0f) == null);
        }

        // ---- M4 装备战力 ----
        static void SuiteEquipment()
        {
            var catalog = GearCatalog.ForTown(items);
            Check("装备档位表至少四条落地", catalog.Entries.Count >= 4, catalog.Entries.Count + " 条");
            Check("柴刀登记为主手兵刃 +8 攻", catalog.Find("cudao").AttackBonus == 8 &&
                catalog.Find("cudao").Slot == GearSlot.Weapon);
            Check("铁剑登记为主手兵刃 +15 攻", catalog.Find("tiejian").AttackBonus == 15);
            Check("粗布袍登记为护具 +4 防", catalog.Find("cubupao").DefenseBonus == 4);

            var gear = new CombatEquipmentService(catalog, CombatDestinyTable.Standard, "");
            Check("默认空载：总攻等于裸攻（红线）", gear.TotalAttack(10) == 10 && gear.EquippedCount == 0);
            Check("默认空载：总防等于裸防", gear.TotalDefense(3) == 3);
            Check("默认空载：暴击加成为 0", Math.Abs(gear.CritBonus) < .0001f);
            Check("装不上没登记的东西时如实给理由",
                !gear.Equip(GearSlot.Weapon, "没有这个东西") && gear.LastRefusal.Length > 0);
            Check("装错槽位被拒（袍子装不进主手）",
                !gear.Equip(GearSlot.Weapon, "cubupao") && gear.LastRefusal.Contains("护具"));
            Check("穿上柴刀：总攻 10→18", gear.Equip(GearSlot.Weapon, "cudao") && gear.TotalAttack(10) == 18);
            Check("换装铁剑：总攻精确增加 15 点（25）", gear.Equip(GearSlot.Weapon, "tiejian") && gear.TotalAttack(10) == 25);
            Check("主手只有一格：换剑不留下第二把刀", gear.Weapon.Entry.ItemId == "tiejian" && gear.EquippedCount == 1);
            Check("穿上粗布袍：总防 3→7", gear.Equip(GearSlot.Armor, "cubupao") && gear.TotalDefense(3) == 7);

            var actor = Fighter("actor", hp: 60, attack: 10, defense: 3, stamina: 60);
            gear.ApplyTo(actor, 10, 3);
            Check("ApplyTo 把兵刃修正量交给 Core（+15）", actor.AttackModifier == 15, actor.AttackModifier + "");
            Check("ApplyTo 把护具修正量交给 Core（+4）", actor.DefenseModifier == 4);
            Check("ApplyTo 一并挂上 2.0 档案（回复 25/秒）", actor.Tuning != null &&
                Math.Abs(actor.Tuning.StaminaRegenPerSecond - 25f) < .001f);

            var foe = Fighter("foe", hp: 99999, attack: 0, defense: 2, stamina: 50);
            var hit = Strike(actor, foe, CombatActionType.Light, 0f, CombatDepth.StrikeFor(profiles.Find("boar-demon"), ""));
            Check("装备真的进伤害算术：(10+15+10)−2 = 33", hit.Damage == 33, hit.Damage + "");
            Check("卸下兵刃后总攻回到裸值", gear.Unequip(GearSlot.Weapon) != null && gear.TotalAttack(10) == 10);
            gear.ApplyTo(actor, 10, 3);
            var after = Strike(actor, foe, CombatActionType.Light, 1f, CombatDepth.StrikeFor(profiles.Find("boar-demon"), ""));
            Check("卸下后伤害回到 18（与基线一致）", after.Damage == 18, after.Damage + "");
            Check("卸下护具后总防回到裸值", gear.Unequip(GearSlot.Armor) != null && gear.TotalDefense(3) == 3);

            var bare = Fighter("bare", hp: 60, attack: 10, defense: 3, stamina: 60);
            var bareService = new CombatEquipmentService(catalog, CombatDestinyTable.Standard, "");
            bareService.ApplyTo(bare, 10, 3);
            Check("空穿一整场：两个修正量始终是 0", bare.AttackModifier == 0 && bare.DefenseModifier == 0);
            Check("配饰槽挂着符箓并带暴击倍率", catalog.Find("fulu").CritBonus > 0f &&
                catalog.Find("fulu").Slot == GearSlot.Accessory);
            var withTrinket = new CombatEquipmentService(catalog, CombatDestinyTable.Standard, "");
            withTrinket.Equip(GearSlot.Accessory, "fulu");
            Check("配饰暴击加成进 CritBonus", Math.Abs(withTrinket.CritBonus - catalog.Find("fulu").CritBonus) < .001f);
            Check("整备行写得出一句人话", withTrinket.Describe().Contains("耐久"));
            Check("卸下没穿的那格返回空（不抛）", withTrinket.Unequip(GearSlot.Armor) == null);
        }

        // ---- M5 耐久损耗与修补 ----
        static void SuiteDurability()
        {
            var catalog = GearCatalog.ForTown(items);
            var gear = new CombatEquipmentService(catalog, CombatDestinyTable.Standard, "");
            gear.Equip(GearSlot.Weapon, "cudao");
            Check("柴刀满耐久 60", gear.Weapon.Durability == 60 && !gear.Weapon.IsDulled);
            for (var i = 0; i < 10; i++) gear.WearWeapon(CombatActionType.Light);
            Check("十次轻击精确扣 10 点（50）", gear.Weapon.Durability == 50, gear.Weapon.Durability + "");
            for (var i = 0; i < 5; i++) gear.WearWeapon(CombatActionType.Heavy);
            Check("五次重击再扣 10 点（40）", gear.Weapon.Durability == 40, gear.Weapon.Durability + "");
            gear.WearWeapon(CombatActionType.Wait);
            Check("等待动作不掉耐久", gear.Weapon.Durability == 40);
            gear.Equip(GearSlot.Armor, "cubupao");
            for (var i = 0; i < 3; i++) gear.WearArmor();
            Check("受创三次：护具扣 6 点（74）", gear.Armor.Durability == 74, gear.Armor.Durability + "");

            for (var i = 0; i < 60; i++) gear.WearWeapon(CombatActionType.Light);
            Check("耐久打穿也压不到负数", gear.Weapon.Durability == 0);
            Check("器刃钝损成立", gear.Weapon.IsDulled && gear.WeaponDulled);
            Check("钝损后攻击加成安全回退为 0", gear.TotalAttack(10) == 10 && gear.Weapon.EffectiveAttack == 0);
            Check("钝损不删装备（还在身上）", gear.IsEquipped(GearSlot.Weapon) && gear.Weapon.Entry.ItemId == "cudao");
            gear.WearWeapon(CombatActionType.Heavy);
            Check("钝损后继续挥砍不报错也不越界", gear.Weapon.Durability == 0);
            Check("耐久状况比例落在 0..1", gear.Weapon.Condition == 0f && gear.Armor.Condition > .9f);

            var save = NewSave(coins: 100);
            var repair = gear.RepairAtSmithy(save, GearSlot.Weapon);
            Check("赵大修补：满耐久 60 回来了", repair.Done && gear.Weapon.Durability == 60);
            Check("修补价按每 10 点 1 文、最低 4 文（6 文）", repair.CoinsPaid == 6 && save.coins == 94,
                "付 " + repair.CoinsPaid + " 余 " + save.coins);
            Check("修补只动指定那一格（护具不变）", gear.Armor.Durability == 74);
            var poor = NewSave(coins: 2);
            gear.WearWeapon(CombatActionType.Heavy);
            var refused = gear.RepairAtSmithy(poor, GearSlot.Weapon);
            Check("铜钱不够：不扣钱、不补耐久、给出台阶", !refused.Done && poor.coins == 2 &&
                gear.Weapon.Durability == 58 && refused.Reason.Length > 0, refused.Reason);
            Check("没穿的格子修不了（不抛异常）", !gear.RepairAtSmithy(poor, GearSlot.Accessory).Done);
            gear.Equip(GearSlot.Weapon, "cudao");
            Check("满耐久时无需再磨", gear.RepairAtSmithy(poor, GearSlot.Weapon).Reason.Contains("不用磨"));
            Check("存档为 null 也不炸", !gear.RepairAtSmithy(null, GearSlot.Weapon).Done);
            var all = gear.RepairAllAtSmithy(NewSave(coins: 500));
            Check("整备一次：只对有缺损的那件收钱", all.Done && all.DurabilityRestored >= 2 && all.CoinsPaid >= 4,
                all.CoinsPaid + " 文");

            var frugal = new CombatEquipmentService(catalog, CombatDestinyTable.Standard, "caixing");
            frugal.Equip(GearSlot.Weapon, "cudao");
            for (var i = 0; i < 3; i++) frugal.WearWeapon(CombatActionType.Light);
            Check("财星照命：轻击被减免吃满，一点不磨", frugal.Weapon.Durability == 60);
            for (var i = 0; i < 10; i++) frugal.WearWeapon(CombatActionType.Heavy);
            Check("重手仍磨 1 点（减免 1 只抵掉 1）", frugal.Weapon.Durability == 50, frugal.Weapon.Durability + "");
        }

        // ---- M4 十九命途映射 ----
        static void SuiteDestiny()
        {
            var table = CombatDestinyTable.Standard;
            Check("命途战斗映射 19 条齐备", table.Count == 19, table.Count + " 条");
            var missing = CharacterGen.Destinies.Select(d => d.Id).Where(id => table.Find(id) == null).ToList();
            Check("每条命格都在战斗表里（无悬空引用）", missing.Count == 0, string.Join(",", missing));
            var ghost = table.Ids.Where(id => CharacterGen.FindDestiny(id) == null).ToList();
            Check("战斗表里没有凭空多出的命格", ghost.Count == 0, string.Join(",", ghost));
            var flat = table.Ids.Where(id => table.Find(id).Modifiers.AttackBonus != 0 ||
                table.Find(id).Modifiers.DefenseBonus != 0).ToList();
            Check("命途不给裸攻防（护住基线伤害与建档随机性）", flat.Count == 0, string.Join(",", flat));
            Check("杀星入命加暴击倍率 .15", Math.Abs(table.For("shaxing").CritBonus - .15f) < .001f);
            Check("安乐命加体力回复 8/秒", table.For("anle").RegenBonus == 8);
            Check("灾星过命加流血 3 点", table.For("fallingstar").BleedDamage == 3);
            Check("侠骨同时给倍率与流血", Math.Abs(table.For("xia").CritBonus - .05f) < .001f && table.For("xia").BleedDamage == 2);
            Check("旧债是纯剧情钩子（零加成）", table.For("debtor").IsEmpty);
            var mapped = table.Ids.Count(id => !table.For(id).IsEmpty);
            Check("至少八条命格有实战手感", mapped >= 8, mapped + " 条");
            Check("认不出的命格给零加成并如实记下", table.For("没有这条命").IsEmpty &&
                table.UnresolvedIds.Contains("没有这条命"));

            var catalog = GearCatalog.ForTown(items);
            var gear = new CombatEquipmentService(catalog, table, "anle");
            Check("命途回复并进战斗档案（25+8=33）", Math.Abs(gear.StaminaRegenPerSecond - 33f) < .001f,
                gear.StaminaRegenPerSecond + "");
            gear.SetDestiny("scholar");
            Check("换命格后看破窗跟着变（.15+.06）", Math.Abs(gear.RiposteWindowSeconds - .21f) < .001f,
                gear.RiposteWindowSeconds.ToString("0.###"));
            gear.SetDestiny(null);
            Check("空命格不吃任何加成", Math.Abs(gear.CritBonus) < .0001f && gear.StaminaRegenPerSecond == 25f);

            var hero = Fighter("hero", hp: 300, attack: 10, defense: 1, stamina: 100);
            var ape = Fighter("ape", hp: 99999, attack: 30, defense: 15, stamina: 100);
            var slay = CombatDepth.StrikeFor(profiles.Find("stone-mandrill"), "back-scar",
                critBonus: table.For("shaxing").CritBonus);
            Check("杀星入命在弱点上的数学：round(20×2.0×1.15)−15 = 31",
                Strike(hero, ape, CombatActionType.Light, 0f, slay).Damage == 31);
        }

        // ---- M7 战败保护账单 ----
        static void SuiteDefeat()
        {
            Check("醒来气血按三成算（60→18）", CombatDefeatProtection.AwakeHpFor(60) == 18);
            Check("气血上限只有 1 也至少留一口气", CombatDefeatProtection.AwakeHpFor(1) == 1);
            Check("非法上限（0/负数）也当 1 处理", CombatDefeatProtection.AwakeHpFor(0) == 1 &&
                CombatDefeatProtection.AwakeHpFor(-99) == 1);
            Check("药资按 10% 算（100→10）", CombatDefeatProtection.MedicalFeeFor(100) == 10);
            Check("钱少时向下取整（25→2）", CombatDefeatProtection.MedicalFeeFor(25) == 2);
            Check("身无分文不收钱", CombatDefeatProtection.MedicalFeeFor(0) == 0 && CombatDefeatProtection.MedicalFeeFor(-5) == 0);
            var nullPlan = CombatDefeatProtection.Plan(null, 60);
            Check("存档为 null 也给得出一张账单（战败面板不空引用）",
                nullPlan.AwakeHp == 18 && nullPlan.CoinsBefore == 0);
            var broke = CombatDefeatProtection.Apply(NewSave(coins: 0), 60);
            Check("没钱时药资全免并写明缘由", broke.FeeWaived && broke.MedicalFee == 0 && broke.Reason.Contains("药老"));
            var wallet = NewSave(coins: 100);
            var applied = CombatDefeatProtection.Apply(wallet, 60);
            Check("落账版本扣 10 文（剩 90）", applied.MedicalFee == 10 && wallet.coins == 90);
            var quotedWallet = NewSave(coins: 100);
            var quoted = CombatDefeatProtection.Plan(quotedWallet, 60);
            Check("Plan 只算不扣（账单口径与落账口径分离：前台显示的一定是真扣过的那份）",
                quoted.MedicalFee == 10 && quotedWallet.coins == 100,
                "该付 " + quoted.MedicalFee + " 文，存档仍是 " + quotedWallet.coins + " 文");
            var poorWallet = NewSave(coins: 5);
            var poor = CombatDefeatProtection.Apply(poorWallet, 60);
            Check("铜钱不够一副药：一分不收、绝不为负、给得出理由",
                poor.MedicalFee == 0 && poorWallet.coins == 5 && poor.FeeWaived, poor.Reason);
            Check("账单文案齐备（淡出语/醒来地点/药老台词）",
                applied.FadeLine.Contains("不省人事") && applied.WakeAt.Contains("药老药圃") &&
                applied.ElderLine.Contains("莫要强撑"));

            var hero = Fighter("hero", hp: 60, attack: 5, defense: 0, stamina: 60);
            var killer = Fighter("killer", hp: 100, attack: 99, defense: 0, stamina: 100);
            hero.X = killer.X = 0f;
            var threw = "";
            try
            {
                CombatRules.TryAct(killer, CombatActionType.Light, 0f);
                CombatRules.Tick(killer, .13f);
                var lethal = CombatRules.ResolveStrike(killer, hero, .14f);
                CombatRules.Tick(hero, .5f);
                CombatRules.Regenerate(hero, 2f);
                CombatRules.EnterExposure(hero, .6f);
                Check("致死一击把血锁在 0 而不是负数", lethal.Hit && lethal.Damage == 60 && hero.Hp == 0 && hero.IsDead,
                    lethal.Damage + "/" + hero.Hp);
                Check("倒下之后不回精也不复活", hero.Stamina == 60 && hero.Phase == CombatPhase.Dead);
                CombatRules.RestoreHp(hero, CombatDefeatProtection.AwakeHpFor(hero.MaxHp));
                Check("扶起来之后能重新行动（不锁死）", hero.Phase == CombatPhase.Idle &&
                    CombatRules.TryAct(hero, CombatActionType.Light, 1f).Executed);
                Check("醒来气血正好是三成（18）", hero.Hp == 18, hero.Hp + "");
            }
            catch (Exception ex) { threw = ex.GetType().Name + ": " + ex.Message; }
            Check("战败全链路不抛未捕获异常", threw == "", threw);
            var zeroed = Fighter("zero", hp: 1, attack: 1, defense: 0, stamina: 1);
            CombatRules.RestoreHp(zeroed, 0);
            Check("把人扶到 0 会被拦成 1（不给死循环开路）", zeroed.Hp == 1);
            Check("对着空单位扶人什么都不发生（不抛）", Throws(() => CombatRules.RestoreHp(null, 5)) == "");
        }

        // ---- N4：精力口径钉死 + 节拍实测（D-06）----
        // 这四个数是玩家今天真实手感的全部来源；改任何一个都必须连带 Tools/SaveChecks/CombatChecks.cs
        // 的四处期望一起改（8/20/12 与 6/秒），而那处改动需要架构师批准——先把墙砌在这里。
        static void SuiteTempo()
        {
            Check("精力与回复基线常量：8/20/12 与 6/秒（改它必须连带 CombatChecks.cs 四处期望，需架构师批）",
                CombatRules.LightStaminaCost == 8 && CombatRules.HeavyStaminaCost == 20 &&
                CombatRules.DodgeStaminaCost == 12 && CombatRules.StaminaRegenPerSecond == 6f);
            Check("招式三相与基准伤害基线未动（.12/.08/.18 与 .28/.10/.34，伤害 10/22）",
                CombatRules.Timing(CombatActionType.Light) == (.12f, .08f, .18f) &&
                CombatRules.Timing(CombatActionType.Heavy) == (.28f, .10f, .34f) &&
                CombatRules.LightBaseDamage == 10 && CombatRules.HeavyBaseDamage == 22);
            Check("破绽窗 .35 秒与流血节拍 .5 秒未动", Math.Abs(CombatRules.ExposureSeconds - .35f) < .0001f &&
                Math.Abs(CombatRules.BleedTickSeconds - .5f) < .0001f);

            // 实测一：从满精力起能连打几记重击（动作期间不回精，所以纯靠存量）
            var spam = Fighter("spam", hp: 60, attack: 8, defense: 1, stamina: 60);
            spam.Tuning = CombatDepth.Standard20;
            var heavies = 0;
            for (var t = 0f; t < 30f; t += .8f)
            {
                if (!CombatRules.TryAct(spam, CombatActionType.Heavy, t).Executed) break;
                heavies++;
                CombatRules.Tick(spam, t + .72f);
                CombatRules.Regenerate(spam, .08f);   // 两招之间只喘 .08 秒（贴着打）
            }
            Check("玩家贴脸连打：60 精力够三记重击，第四记被拒并露破绽", heavies == 3 && spam.Stamina < 20,
                "连打 " + heavies + " 记，剩 " + spam.Stamina);

            // 实测二：把精力打空之后，回够一记重击要等多久（25/秒 vs 6/秒）
            var drainedTuned = Fighter("tuned", hp: 60, attack: 8, defense: 1, stamina: 60);
            drainedTuned.Tuning = CombatDepth.Standard20;
            var drainedBare = Fighter("bare", hp: 60, attack: 8, defense: 1, stamina: 60);
            foreach (var actor in new[] { drainedTuned, drainedBare })
            {
                while (CombatRules.TryAct(actor, CombatActionType.Heavy, 0f).Executed)
                    CombatRules.Tick(actor, 99f);
            }
            var tunedWait = SecondsUntilHeavy(drainedTuned);
            var bareWait = SecondsUntilHeavy(drainedBare);
            Check("档案单位（25/秒）回够一记重击要 0.8 秒", Math.Abs(tunedWait - .8f) < .01f, tunedWait.ToString("0.##") + " 秒");
            Check("无档案单位（6/秒）回够一记重击要 3.34 秒",
                Math.Abs(bareWait - 20f / 6f) < .02f, bareWait.ToString("0.##") + " 秒");
            // 上一轮这里钉的是"两套回复速度相差 3 倍以上（这是事实，不是笔误）"——
            #region Q2/T072 定档时退役
            // 退役的理由不是"数字变了"，而是**前提变了**：那条把 4.18 倍当成"玩家 vs 妖"的开放缺陷，
            // 而实测下来真页面里玩家与妖都吃 Standard20（倍差 1.0），4.18 倍只存在于"没挂档案的裸单位"。
            // 同一个比值由下面那条"裸单位与档案单位的等待差仍是 4.17 倍"继续钉住，判据数量没有减少。
            #endregion

            // 实测三（Q2/T072 定档）：三只妖按各自的 AI 节拍打满 30 秒，基线 6/秒 与档案 25/秒 各量一遍。
            // 走的是运行期同一套东西（EnemyAi.Choose 挑招 + 招式自带 stamina 扣 + Regenerate(dt) 回精），
            // 所以"回精率够不够"是量出来的，不是拿 25/6 除出来的。
            foreach (var p in profiles.Profiles)
            {
                var bare = FoeSustainedSwings(p, null);
                var tuned = FoeSustainedSwings(p, CombatDepth.Standard20);
                // 节拍上限：t=0 第一下，之后每 aggressionSeconds 一下 → floor(30/T)+1。
                var ceiling = (int)(30f / p.Ai.AggressionSeconds) + 1;
                Check(p.Id + " 挂 2.0 档案后打得满自己的节拍（30 秒内 ≥ 节拍上限的九成）",
                    tuned.swings >= ceiling * .9f,
                    "25/秒 " + tuned.swings + " 下 / 节拍上限 " + ceiling + " 下；不挂档案（基线 6/秒）只有 " +
                    bare.swings + " 下，火力少 " + (100f * (tuned.swings - bare.swings) / tuned.swings).ToString("0.#") + "%");
            }

            // 上一轮那条"倍差 >3"的反向断言在此退役，理由按 D-24 的要求写清楚：
            // 4.18 倍差是**裸单位 vs 档案单位**的差，而真页面里已经没有裸单位——
            // 玩家由 combatGear.ApplyTo 挂一次 Standard20，妖由 combatFoe.Tuning 挂同一个常量。
            // 守卫从"承认差距"改成"钉住敌我同档案"（见 B 段两条），基线 6/秒 本身一字未动。
            Check("裸单位与档案单位的等待差仍是 4.17 倍（事实留档：它只属于\u201c没挂档案\u201d那条路）",
                Math.Abs(bareWait / tunedWait - 4.17f) < .06f,
                "倍差 " + (bareWait / tunedWait).ToString("0.##") + "（" + tunedWait.ToString("0.##") +
                " 秒 vs " + bareWait.ToString("0.##") + " 秒）");

            // ==== N4-Polish 1（D-28 落地）：未表态即红，对 2.0 表逐只扫 ====
            // 妖的档案只有一个真源（FrontEndCombat.TuningForFoe），这里逐只问一次"它进战斗拿什么档案"。
            // 答 null 就是红：新参战路径忘挂档案时掉的是基线 6/秒，而"静默回退"正是 T072/T076
            // 两次事故的形态（山魈 −42.9% 火力就是这么来的）。玩家那一侧的同一条判据在 B 段（要真页面）。
            var untunedFoes = profiles.Profiles
                .Where(p => FrontEndController.TuningForFoe(p) == null).Select(p => p.Id).ToList();
            Check("D-28：2.0 表每一只妖进战斗都带档案（答 null 即红，不许静默掉到基线 6/秒）",
                untunedFoes.Count == 0, untunedFoes.Count + " 只未表态 / 共 " + profiles.Profiles.Count +
                " 只：" + string.Join(",", untunedFoes));

            // ==== N4-Polish 2（T109 定性）：精力到底是不是"玩家可打的资源" ====
            // 结构性实测：Core 里扣精力的只有一处——`actor.Stamina -= cost`（自己出手，Combat.cs:261）。
            // ResolveStrike 只扣气血，没有任何代码扣**对手**的精力。所以"把妖的精力打空"这条战术
            // 在模型里从来不存在，与回率是 6/秒 还是 25/秒 无关：玩家能做的只有"等它自己把 20 点花完"。
            // ⇒ 我登记 T109 时把问题问成了"敌我同档会不会让妖瘫不掉"，方向偏了。判据按实测结论改：
            //   钉住"妖的精力不受玩家打击影响"这条**结构**，而不是钉某个倍率。
            //   （将来若真加"受击扣精力"，这条会红着要求写理由——那是新机制，要过架构师。）
            var t109Foe = Fighter("t109-foe", hp: 2000, attack: 9, defense: 0, stamina: 30);
            var t109Hero = Fighter("t109-hero", hp: 999, attack: 20, defense: 0, stamina: 999);
            // 先让妖**自己**花掉一记重击的 20 点：新造出来的单位精力是满的（构造里 Stamina = MaxStamina），
            // 拿满条去验"打不动精力"是假绿——半空的那条才有意义。
            CombatRules.TryAct(t109Foe, CombatActionType.Heavy, 1f);
            var t109StaBefore = t109Foe.Stamina;
            for (var i = 0; i < 12; i++)
                StrikeLegacy(t109Hero, t109Foe, CombatActionType.Light, 10f + i * .9f, 1f);
            Check("T109 定性：妖自己花掉 20 点后，玩家连打十二记仍一点没动（精力不是可打的资源）",
                t109StaBefore == 10 && t109Foe.Stamina == t109StaBefore && t109Foe.Hp < 2000 && !t109Foe.IsDead,
                "精力 " + t109StaBefore + " → " + t109Foe.Stamina + "（上限 " + t109Foe.MaxStamina +
                "），气血 2000 → " + t109Foe.Hp + "：打掉了气血，打不掉精力");
            // 自查后删掉我这一轮刚写的三条"挂档案后不因精力拒招"守卫：实测 tuned 与 bare **都是 0 次拒招**
            // （`EnemyAi.Choose` 在精力不够时直接不选那一招，走不到 `TryAct` 的拒绝分支），
            // 所以它"实现坏了也不会红"——留着就是我审别人时抓的那种 T103 空守卫。
            // 有牙齿的那条还在：上面"实测三"的 30 秒出手数（山魈不挂档案火力少 42.9%）。
        }

        // 把一只 2.0 妖按它自己的 AI 节拍打满 30 秒：返回真的出手了几下、有几拍是被"精力不够"挡下的。
        // 不结算命中（不调 ResolveStrike），所以陪练靶永远不会倒——这里量的是续航，不是伤害。
        // 注意 starved 恒为 0 不代表"没被精力卡"：精力不够时 TryAct 被拒但不记 NextActAt，
        // 下一拍（.05 秒后）就重试，代价摊进"总出手数"里——所以看的是 swings 的缺口，不是 starved。
        static (int swings, int starved) FoeSustainedSwings(EnemyProfile profile, CombatTuning tuning,
            float seconds = 30f)
        {
            var foe = Fighter("sustain-" + profile.Id + (tuning == null ? "-bare" : "-20"),
                hp: profile.MaxHp, attack: profile.Attack, defense: profile.Defense, stamina: profile.MaxStamina);
            foe.Tuning = tuning;
            var dummy = Fighter("dummy-" + profile.Id, hp: 999, attack: 0, defense: 0, stamina: 999);
            dummy.X = foe.X + .6f;
            var ai = EnemyAi.NewState(0f);
            var swings = 0;
            var starved = 0;
            for (var t = 0f; t < seconds; t += .05f)
            {
                CombatRules.Tick(foe, t);
                CombatRules.Regenerate(foe, .05f);
                if (foe.Phase != CombatPhase.Idle || t < ai.NextActAt) continue;
                var move = EnemyAi.Choose(profile, ai, foe, dummy, t);
                if (move == null) continue;
                var result = CombatRules.TryAct(foe, move.Action, t, dummy, move);
                if (result.Executed)
                {
                    swings++;
                    EnemyAi.MarkActed(ai, t, profile.Ai.AggressionSeconds, move.Id);
                }
                else if (result.Reason.Contains("精力")) starved++;
            }
            return (swings, starved);
        }

        static float SecondsUntilHeavy(CombatActorState actor)
        {
            var now = 100f;
            for (var i = 0; i < 400; i++)
            {
                CombatRules.Regenerate(actor, .02f);
                now += .02f;
                if (actor.Stamina >= CombatRules.HeavyStaminaCost) return now - 100f;
            }
            return -1f;
        }

        // 野猪某一招打在裸装玩家身上的结算伤害（D-07 单发上限的算法与 Core 逐字同式）
        static int BoarStrikeDamage(CombatMove move)
        {
            var boar = Fighter("boar", hp: 60, attack: 9, defense: 2, stamina: 50);
            var victim = Fighter("victim", hp: 60, attack: 8, defense: 1, stamina: 60);
            boar.X = 0f; victim.X = 1f;
            CombatRules.TryAct(boar, move.Action, 0f, victim, move);
            CombatRules.Tick(boar, move.Startup + .001f);
            var result = CombatRules.ResolveStrike(boar, victim, move.Startup + .002f);
            return result.Damage;
        }

        // 一刀一刀真砍到野猪倒下，数一共要几刀（D-07 第四条不变量的取证方式：数出来，不是推出来）。
        // 走 Strike 的完整节拍（起手→判定帧→收招→回精），所以这里数的是"命中数"而不是"伤害除法"。
        static int SwingsToKill(CombatActionType action, string partId)
        {
            var player = Fighter("slayer-" + action + "-" + partId, hp: 60, attack: 8, defense: 1, stamina: 60);
            var foe = Fighter("doomed-boar", hp: 60, attack: 9, defense: 2, stamina: 50);
            foe.Tuning = CombatDepth.Standard20;
            var strike = CombatDepth.StrikeFor(profiles.Find("boar-demon"), partId, 0f, false);
            var hits = 0;
            for (var t = 0f; t < 60f && !foe.IsDead; t += 1f)
            {
                Strike(player, foe, action, t, strike);
                hits++;
            }
            return foe.IsDead ? hits : -1;
        }

        // 从满精力开打到放倒野猪要**多少秒**（D-23 护栏用的就是它，不是命中数）。
        // 与真页面同一套节拍：每 .05 秒 Tick 一次 + Regenerate(dt) 回精；Idle 就起手、Active 就结算；
        // 精力不够时按前台 PlayerAct 的口径吃一次破绽窗（.35 秒）——不把这条算进去会低估重击的代价。
        // 玩家挂 Standard20（真页面由 combatGear.ApplyTo 挂），裸装 attack 8 / defense 1。
        static float SecondsToKill(CombatActionType action, string partId)
        {
            var player = Fighter("ttk-" + action + "-" + partId, hp: 60, attack: 8, defense: 1, stamina: 60);
            player.Tuning = CombatDepth.Standard20;
            var foe = Fighter("ttk-prey", hp: 60, attack: 9, defense: 2, stamina: 50);
            foe.Tuning = CombatDepth.Standard20;
            foe.X = player.X + .6f;
            var strike = CombatDepth.StrikeFor(profiles.Find("boar-demon"), partId, 0f, false);
            for (var t = 0f; t < 120f; t += .05f)
            {
                CombatRules.Tick(player, t);
                CombatRules.Tick(foe, t);
                CombatRules.Regenerate(player, .05f);
                if (foe.IsDead) return t;
                if (player.Phase == CombatPhase.Idle)
                {
                    var act = CombatRules.TryAct(player, action, t);
                    if (!act.Executed && act.Reason.Contains("精力")) CombatRules.EnterExposure(player, t);
                }
                else if (player.Phase == CombatPhase.Active)
                    CombatRules.ResolvePartStrike(player, foe, t, strike);
            }
            return foe.IsDead ? 120f : -1f;
        }

        // ---- N3：战斗内服药（D-09）----
        // 回复量的真源只能是 items.json：字段优先、描述兜底、两处都没有就拒用。
        // 这里用一份合成 JSON 证明"数值是从物品数据里读出来的"，而不是表现层写死的。
        static void SuitePills()
        {
            // 合成表必须自带 tiers/categories：ItemTable.Parse 对它们是硬要求
            // （"物品表至少要有一个品阶"），漏了会把整个套件带崩——这是本轮真实踩到的坑。
            const string synthetic = "{\"tiers\":[{\"id\":\"fanpin\",\"name\":\"凡品\",\"color\":\"#8C8C86\"}]," +
                "\"categories\":[{\"id\":\"yaodan\",\"name\":\"丹药\"},{\"id\":\"chishi\",\"name\":\"吃食\"}]," +
                "\"items\":[" +
                "{\"id\":\"pill-field\",\"name\":\"凝血金疮散\",\"tier\":\"fanpin\",\"category\":\"yaodan\"," +
                "\"icon\":\"elixir-bottle\",\"combatHeal\":45," +
                "\"stack\":9,\"price\":35,\"description\":\"药圃药炉精炼的止血散，战斗中服用瞬间止血、回复气血。\"}," +
                "{\"id\":\"pill-text\",\"name\":\"描金散\",\"tier\":\"fanpin\",\"category\":\"yaodan\"," +
                "\"icon\":\"elixir-bottle\",\"stack\":5,\"price\":12," +
                "\"description\":\"战斗中服下，瞬回 30 点气血。\"}," +
                "{\"id\":\"pill-vague\",\"name\":\"土方金疮药\",\"tier\":\"fanpin\",\"category\":\"yaodan\"," +
                "\"icon\":\"elixir-bottle\",\"stack\":5,\"price\":14," +
                "\"description\":\"止血生肌的土方药，走江湖的第一件家当。\"}," +
                "{\"id\":\"not-food\",\"name\":\"干粮\",\"tier\":\"fanpin\",\"category\":\"chishi\"," +
                "\"icon\":\"dryfood\",\"stack\":9,\"price\":4," +
                "\"description\":\"压得实，路上顶饿。\"}]}";
            var fake = ItemTable.Parse(synthetic);

            Check("字段 combatHeal 优先：45 点且来源写明是字段",
                CombatConsumables.EffectFor(fake, synthetic, "pill-field").HealHp == 45 &&
                CombatConsumables.EffectFor(fake, synthetic, "pill-field").Source.Contains("combatHeal"));
            Check("描述兜底：浮签写 30 就回 30（描述即契约）",
                CombatConsumables.EffectFor(fake, synthetic, "pill-text").HealHp == 30 &&
                CombatConsumables.EffectFor(fake, synthetic, "pill-text").Source == "描述文案");
            Check("没写明数量 → 回复量 0（绝不替物品表编一个数）",
                CombatConsumables.EffectFor(fake, synthetic, "pill-vague").HealHp == 0);
            Check("丹药之外的类别一律不可在战斗中服用",
                CombatConsumables.EffectFor(fake, synthetic, "not-food").HealHp == 0);
            // T070（架构师 2026-10-05 20:39 转给开发者3号）：给 jinchuang-san 补 combatHeal=45、
            // jinchuangyao 补 25。原来这条钉的是"当前表里恰好没写数字"——那是**数据快照**，
            // 字段一落它必红，红在快照上而不是红在行为上，正是 T094/T095 立口径要避免的形态。
            // 现在钉的是行为：读到的数必须等于 items.json 原文里写着的数，且原文一旦有数就必须是契约值。
            // 落数前读到 0（并如实报"没写明"）、落数后读到 45/25，同一条断言两种状态下都成立且都有牙齿。
            Check("凝血金疮散：读数 == 原文 combatHeal（T070 契约 45；未落数时如实 0 并报来源）",
                RawFieldAgrees("jinchuang-san", 45, out var sanRead, out var sanRaw),
                "原文 " + sanRaw + " / 读到 " + sanRead + "（" +
                CombatConsumables.EffectFor(items, ItemsJson, "jinchuang-san").Source + "）");
            Check("金疮药：读数 == 原文 combatHeal（T070 契约 25）",
                RawFieldAgrees("jinchuangyao", 25, out var yaoRead, out var yaoRaw),
                "原文 " + yaoRaw + " / 读到 " + yaoRead);

            var save = NewSave(coins: 100);
            InventoryRules.Add(save.bag, fake, "pill-field", 3);
            var hero = Fighter("hero", hp: 60, attack: 8, defense: 1, stamina: 60);
            // "吃药回血"的前提是真挨了打。挨打必须走 起手→Tick 到判定帧→结算 的完整节拍，
            // 直接对一个没起手的人调 ResolveStrike 只会得到"不在命中帧"，
            // 于是人满着血、Consume 按口径拒用，后面四条一起假红（本轮实际踩过）。
            // 一口 18（(9+10)×1 − 1），三口 54 → 剩 6 点：还活着，且亏空 54 > 45，
            // 这样"回 45"才是原值而不是被 maxHp 钳过的值。
            var biter = Fighter("biter", hp: 60, attack: 9, defense: 2, stamina: 50);
            var firstBite = StrikeLegacy(biter, hero, CombatActionType.Light, 0f, 1f);
            var secondBite = StrikeLegacy(biter, hero, CombatActionType.Light, 1f, 1f);
            Check("野猪一口咬掉 18（服药段的前提：亏空靠真打攒出来）",
                firstBite.Hit && secondBite.Hit && firstBite.Damage == 18 && hero.Hp == 24,
                "伤 " + firstBite.Damage + " 血 " + hero.Hp);
            StrikeLegacy(biter, hero, CombatActionType.Light, 2f, 1f);
            CombatRules.Tick(hero, 3f);   // 受击硬直 .30s 走完；不回到 Idle 就谈不上"低头灌药"
            Check("挨完三口还活着（6 点气血）——不然这条套件验的是尸体",
                !hero.IsDead && hero.Hp == 6 && hero.Phase == CombatPhase.Idle, hero.Hp + " " + hero.Phase);
            var before = hero.Hp;
            var dose = CombatConsumables.Consume(save, fake, synthetic, "pill-field", hero, 4f);
            Check("服药执行：按物品字段回血（45），不越过 maxHp", dose.Executed && dose.Healed == 45 &&
                dose.HpAfter == before + 45, "伤前 " + before + " 回 " + dose.Healed + " 后 " + dose.HpAfter);
            Check("浮签上的数字 == 实际结算的数字（描述即契约的可执行版）",
                dose.Healed == CombatConsumables.EffectFor(fake, synthetic, "pill-field").HealHp);
            Check("行囊真的少了一件（落盘前的内存账先对上）",
                InventoryRules.Count(save.bag, "pill-field") == 2,
                InventoryRules.Count(save.bag, "pill-field") + "");

            // 钳制口径单独立一条：亏空不足 45 时只回缺口那么多，且绝不超过 maxHp。
            var shallow = Fighter("shallow", hp: 60, attack: 8, defense: 1, stamina: 60);
            StrikeLegacy(Fighter("biter2", hp: 60, attack: 9, defense: 2, stamina: 50), shallow,
                CombatActionType.Light, 0f, 1f);
            CombatRules.Tick(shallow, 1f);
            var small = CombatConsumables.Consume(save, fake, synthetic, "pill-field", shallow, 2f);
            Check("亏空不够 45 就只回缺口那么点，且封顶 maxHp",
                small.Healed == 18 && small.HpAfter == 60, "回 " + small.Healed + " 到 " + small.HpAfter);

            Check("服药要付破绽窗：灌药这一瞬人被锁住", hero.Phase == CombatPhase.Hitstun &&
                Math.Abs(dose.ExposureSeconds - CombatRules.ExposureSeconds) < .0001f);
            Check("气血满时拒用（只此一个口径）",
                !CombatConsumables.Consume(save, fake, synthetic, "pill-field",
                    Fighter("full", hp: 60, attack: 8, defense: 1, stamina: 60), 2f).Executed);
            Check("没写明数量的药不敢乱服",
                !CombatConsumables.Consume(save, fake, synthetic, "pill-vague", hero, 3f).Executed);
            var none = NewSave(coins: 0);
            Check("行囊里没有这一味 → 拒用且不动生命",
                !CombatConsumables.Consume(none, fake, synthetic, "pill-field", hero, 4f).Executed);
            var dead = Fighter("dead", hp: 1, attack: 1, defense: 0, stamina: 5);
            var killer = Fighter("killer", hp: 60, attack: 99, defense: 0, stamina: 60);
            dead.X = killer.X = 0f;
            CombatRules.TryAct(killer, CombatActionType.Light, 0f);
            CombatRules.Tick(killer, .13f);
            CombatRules.ResolveStrike(killer, dead, .14f);
            Check("已经倒下的人灌不进药（不抛、不复活）",
                !CombatConsumables.Consume(save, fake, synthetic, "pill-field", dead, 5f).Executed && dead.IsDead);
            var usable = CombatConsumables.UsableIn(save, fake, synthetic);
            Check("可服清单只列写明回复量的那些", usable.Count == 1 && usable[0] == "pill-field",
                string.Join(",", usable));
        }

        // items.json 原文里"人眼能看到的那个数"：结构化字段用正则**独立**读（不再调一次解析器，
        // 否则就是拿被测件验证被测件）；原文没字段时退到描述文案——那本来就是 CombatConsumables
        // 的第二真源，读法必须一致。
        static int RawHealOf(string itemId)
        {
            var m = Regex.Match(ItemsJson,
                "\"id\"\\s*:\\s*\"" + Regex.Escape(itemId) + "\"[^}]*?\"combatHeal\"\\s*:\\s*(\\d+)");
            if (m.Success) return int.Parse(m.Groups[1].Value);
            return CombatConsumables.TryReadHealFromDescription(items.Find(itemId)?.Description ?? "", out var fromText)
                ? fromText : 0;
        }

        static bool RawFieldAgrees(string itemId, int contractValue, out int read, out int raw)
        {
            raw = RawHealOf(itemId);
            read = CombatConsumables.EffectFor(items, ItemsJson, itemId)?.HealHp ?? -1;
            return read == raw && (raw == 0 || raw == contractValue);
        }

        // ---- N5：ADR-031 契约表与 pending 白名单（D-10）----
        // 跨线引用不硬抛，但也不能什么 id 都能混进 pending——只允许契约表里那 6 个。
        static readonly string[] ContractItemIds =
        {
            "beast-gall", "wolf-fur", "wolf-fang", "mandrill-stone", "iron-ore", "dark-iron-armor",
        };

        static void SuiteContractIds()
        {
            var catalog = GearCatalog.ForTown(items);
            var strayGear = catalog.PendingItemIds.Where(id => !ContractItemIds.Contains(id)).ToList();
            Check("装备 pending 只允许出现 ADR-031 那 6 个 id", strayGear.Count == 0, string.Join(",", strayGear));
            var strayDrops = profiles.PendingDropItems.Keys.Where(id => !ContractItemIds.Contains(id)).ToList();
            Check("掉落 pending 同样只允许契约表 id（有人自造 id 当场红）", strayDrops.Count == 0,
                string.Join(",", strayDrops));
            var armor = catalog.Find("dark-iron-armor");
            var armorPending = catalog.IsPending("dark-iron-armor");
            Check("玄铁甲档位：要么已生效（防 +12），要么进 pending，不许抛错",
                armorPending || (armor != null && armor.DefenseBonus == 12 && armor.Slot == GearSlot.Armor),
                armorPending ? "pending（等员工 O 落 items.json）" : "已生效");
            Check("items.json 落名后玄铁甲自动生效且是 +12 防的重甲档",
                !armorPending || items.Find("dark-iron-armor") == null);
            var gear = new CombatEquipmentService(catalog, CombatDestinyTable.Standard, "");
            if (!armorPending)
            {
                gear.Equip(GearSlot.Armor, "dark-iron-armor");
                Check("重甲档实测：总防 1→13", gear.TotalDefense(1) == 13, gear.TotalDefense(1) + "");
            }
            else
            {
                Check("未落名时装不上玄铁甲（拒装并给理由，不崩）",
                    !gear.Equip(GearSlot.Armor, "dark-iron-armor") && gear.LastRefusal.Length > 0);
            }
            Check("契约表六个 id 一个都没少（架构师定名即终局文字）", ContractItemIds.Length == 6);
        }

        // ============================ B 段：表现层 ============================

        static void RunViewSuite()
        {
            slotUsed = FreeSlot();
            Check("有空档位供验收使用", slotUsed > 0);
            controller.ShowCharacterCreation(slotUsed);
            Check("建档成功", controller.CreateFromDraft(slotUsed) != null);
            // 建档时命格是随机掷的；战斗表的暴击加成会改动弱点伤害期望。
            // 这一段把命格钉成"旧债"（零映射）——A 段已经单独验过有加成那几条。
            controller.ActiveSave.destiny = "debtor";

            var boar = profiles.Find("boar-demon");
            controller.ShowCombat("encounter-boar-01");
            Check("战斗页打开", controller.CurrentPage == "combat");
            Check("本场妖兽挂了 2.0 生态档", controller.CombatFoeProfile != null && controller.CombatFoeProfile.Id == "boar-demon");
            Check("默认平攻不锁部位", controller.CombatLockedPartId == "");
            Check("裸装开局：身上一件都没有（红线）", controller.CombatGear != null && controller.CombatGear.EquippedCount == 0);
            Check("野猪在表现层已吃满 2.0 招式表（N1/D-07 切完）",
                controller.CombatFoeProfile.Ai.MoveSet == EnemyMoveSet.Profile2);

            // Q2/T072 定档（D-24）：节奏差必须在**真页面**上量，而不是在合成裸单位上量。
            // 上一轮我登记的"玩家 6/秒 vs 妖 25/秒 差 4.18 倍"方向说反了：真页面里玩家每场都被
            // combatGear.ApplyTo 挂上 Standard20（25/秒），而妖从不挂档案（6/秒）——快的是玩家。
            var pagePlayerRegen = controller.CombatPlayer.Tuning?.StaminaRegenPerSecond ??
                CombatRules.StaminaRegenPerSecond;
            var pageFoeRegen = controller.CombatFoe.Tuning?.StaminaRegenPerSecond ??
                CombatRules.StaminaRegenPerSecond;
            Check("真页面：挂了 2.0 表的妖吃 2.0 档案（FrontEndCombat 那行被删就当场红）",
                controller.CombatFoe.Tuning != null &&
                Math.Abs(pageFoeRegen - CombatDepth.Standard20.StaminaRegenPerSecond) < .001f,
                "野猪 " + pageFoeRegen + "/秒");
            Check("真页面：玩家与本场妖的回精倍差 ≤2（D-24 定档，实测两边同一个 Standard20）",
                pagePlayerRegen / pageFoeRegen <= 2f && pageFoeRegen / pagePlayerRegen <= 2f,
                "玩家 " + pagePlayerRegen + "/秒 vs 野猪 " + pageFoeRegen + "/秒，倍差 " +
                (pagePlayerRegen / pageFoeRegen).ToString("0.##"));
            // D-28 的"敌我皆然"落在真页面上：两侧任何一个人没表态，掉的都是基线而不是"某个合理值"。
            Check("D-28：真页面这一场，玩家与妖的档案都已表态（任一侧 null 即红）",
                controller.CombatPlayer.Tuning != null && controller.CombatFoe.Tuning != null,
                "玩家 " + (controller.CombatPlayer.Tuning == null ? "未表态" : "25/秒") + "，妖 " +
                (controller.CombatFoe.Tuning == null ? "未表态" : "25/秒"));

            // ==== Q2-3 构图守卫 ====
            // 本轮两次"断言全绿、图上是坏的"（T075 提示条压按钮、整备行被动作行切掉）都因为
            // 没有一条断言看过两个控件的矩形是否相交。几何问题用几何断言拦一道、看图拦第二道。
            var overlaps = OverlappingButtonPairs();
            Check("战斗页没有任何两颗按钮互相重叠（T075 与整备行压字都是这么漏过去的）",
                overlaps.Count == 0, string.Join(" ｜ ", overlaps));
            var gearRow = new[] { "装兵刃", "装护具", "装配饰", "磨 刃", "服 药" }
                .Select(FindActiveButton).ToList();
            var actionRow = new[] { "左 移", "右 移", "轻 击", "重 击", "闪 避" }
                .Select(FindActiveButton).ToList();
            Check("底部两排十颗按钮全在（一颗缺失就没法比网格）",
                gearRow.All(b => b != null) && actionRow.All(b => b != null));
            var columnDrift = gearRow.Zip(actionRow, (g, a) =>
                Mathf.Abs(g.GetComponent<RectTransform>().anchoredPosition.x -
                          a.GetComponent<RectTransform>().anchoredPosition.x)).Max();
            var widthDrift = gearRow.Zip(actionRow, (g, a) =>
                Mathf.Abs(g.GetComponent<RectTransform>().rect.width -
                          a.GetComponent<RectTransform>().rect.width)).Max();
            Check("两排按钮共用同一套列位与列宽（Q2-3① 网格对齐）",
                columnDrift < .5f && widthDrift < .5f,
                "列位最大漂移 " + columnDrift.ToString("0.###") + "px，列宽最大差 " + widthDrift.ToString("0.###") + "px");
            Check("玩家纸偶占画面高 ≥28%（架构师看图：原来只占约 20%）",
                controller.CombatPlayerHeightRatio >= .28f,
                "玩家 " + (controller.CombatPlayerHeightRatio * 100f).ToString("0.#") + "% · 妖 " +
                (controller.CombatFoeHeightRatio * 100f).ToString("0.#") + "%");
            Check("人机分了前后脚点（Q2-3③ 纵深，0 就是站在同一条线上）",
                controller.CombatGroundDepthPx >= 40f, controller.CombatGroundDepthPx.ToString("0.#") + "px");
            Check("纵深走 sizeDelta 不走 localScale（不碰 FrontEndShotProps 钉的降级口径）",
                Mathf.Abs(controller.CombatFoeScale - 1f) < .001f, "scale=" + controller.CombatFoeScale);

            // ==== N4-Polish 3②／I-5.5／I-5.6：脚点必须踩在地上，地面必须是贴面 ====
            // 这条是**看图**抓出来的（B 席一次、主架构师一次，断言当时全绿）：地带原来按"立绘底边"铺，
            // 而野猪那张 Type-C 的可见底边是扬尘、蹄子在其上方约 40px ⇒ 妖的脚落在地带上沿之外，读起来浮空。
            // 断言拦得住"几何关系错了"，拦不住"这张画本身画得不对"——后者仍然只有看图能发现。
            Check("地面是真贴面而不是浅灰圆角矩形（取不到贴面就红，不许静默降级成 UI 块）",
                controller.CombatGroundTexture != null,
                controller.CombatGroundTexture == null ? "Art/Fx/ink-stroke 取不到"
                    : "Art/Fx/ink-stroke " + controller.CombatGroundTexture.width + "×" +
                      controller.CombatGroundTexture.height);
            Check("主角脚下有与妖同源的接触影（I-5.5 同一屏同一种着地表达）",
                controller.CombatPlayerShadowUsesSameWash,
                controller.CombatPlayerShadowUsesSameWash ? "与地面同一张笔触" : "影子缺失或另用一张图");
            // 拍构图这张之前必须先推一次钟：妖的 anchoredPosition 由 RefreshCombatView 落位，
            // 刚开战时它还停在 RectTransform 的原点（pivot 在下、往画面上方长出去），拍出来就是"妖不见了"。
            // .05 秒内玩家没起手、妖的第一拍在 1.2 秒，所以这一拍不会引入任何战斗结果。
            // 同一句话也是下面两条脚点判据的前提：没落位时量到的"立绘底边"是 0，不是 746。
            controller.CombatClockAdvance(.05f);
            var band = controller.CombatGroundBandY();
            var inset = controller.CombatFoeFootInset;
            var spriteBottom = controller.CombatFoeSpriteBottomY();
            var hoofY = spriteBottom - inset;
            var footY = controller.CombatPlayerFootY;
            // 两半都要钉：①蹄子落在地带里；②地带的远端**不等于立绘底边**（inset=0 就是老 bug 的形状——
            // 只钉①是空守卫：把脚锚点改成 0，地带远端跟着一起挪，断言照样绿。这一版实测过，确实绿了）。
            Check("妖的可见脚点踩在地带之内，且地带远端不是立绘底边（浮空当场红）",
                !float.IsNaN(hoofY) && !float.IsNaN(band.x) && inset > 0f &&
                hoofY >= band.x - .5f && hoofY <= band.y,
                "脚锚点 " + inset.ToString("0.#") + "px，蹄子 y=" + hoofY.ToString("0.#") +
                "（立绘底边 " + spriteBottom.ToString("0.#") + "），地带 y=" +
                band.x.ToString("0.#") + ".." + band.y.ToString("0.#"));
            Check("玩家的脚点也踩在同一条地带里（两人共用地面，不是各站一块）",
                !float.IsNaN(band.x) && footY >= band.x && footY <= band.y + .5f,
                "玩家脚点 y=" + footY.ToString("0.#") + "，地带 y=" + band.x.ToString("0.#") + ".." + band.y.ToString("0.#"));
            // N4-Polish 3③（实测出来的缺字）：战斗页标签走手写体 ZhiMangXing，它的 cmap 里有 U+00D7「×」
            // 但**没有 U+00B7「·」**（同目录 NotoSerifCJKsc 两个都有）。缺字被 TMP 降级成一个贴地的点，
            // "(1x·抵抗 5%)" 在画面上就成了 "(1x.抵抗 5%)"。文案已改成全角逗号，这条钉住它别改回去。
            var lockLabel = controller.CombatLockLabel;
            Check("部位标签里没有手写体缺的那个中点 U+00B7（缺字会降级成句点，看图才发现）",
                lockLabel.Length > 0 && lockLabel.IndexOf('·') < 0 && !lockLabel.Contains("x)"),
                "当前标签：" + lockLabel);
            // ==== D-47 选②：不改数值，只落口径与文案 ====
            // 妖那根精力条一直在诱导玩家"再打几下它就空了"，而实测玩家碰不到对手的精力。
            // 按裁决在条下面钉一句玩家看得懂的话。
            // 期望串**写在夹具这边**（字面量），不是拿生产常量比生产常量——那样改文案的人自说自话就绿了；
            // 现在改 `StaminaHintCopy` 会当场红，逼着改的人到夹具里签一次字（口径要有人过目）。
            const string expectedStaminaHint = "精力是它自己的节拍，打不空";
            var hintRect = controller.CombatStaminaHintRect();
            Check("战斗页逐字印着 D-47 那句玩家口径（夹具侧字面量，改文案得来这里签一次）",
                AnyText(expectedStaminaHint) && FrontEndController.CombatStaminaHintCopy == expectedStaminaHint &&
                !float.IsNaN(hintRect.x),
                "规定：" + expectedStaminaHint + "，生产常量：" + FrontEndController.CombatStaminaHintCopy);
            var hintBoxes = Resources.FindObjectsOfTypeAll<Button>()
                .Where(b => b.gameObject.scene.IsValid() && b.gameObject.activeInHierarchy)
                .Select(b => (name: b.gameObject.name.Replace("Button ", ""), rect: WorldRect(b.transform as RectTransform)))
                .ToList();
            var hintHits = hintBoxes
                .Where(p => hintRect.xMin < p.rect.xMax - 1f && p.rect.xMin < hintRect.xMax - 1f &&
                            hintRect.yMin < p.rect.yMax - 1f && p.rect.yMin < hintRect.yMax - 1f)
                .Select(p => p.name).ToList();
            Check("那句口径不压任何一颗按钮（它该在妖的状态字下面那条空档里，比的是 " + hintBoxes.Count +
                " 颗按钮的实际矩形）",
                hintHits.Count == 0 && hintBoxes.Count >= 10,
                "压到：" + string.Join("/", hintHits) + "，句矩形 " + hintRect + "，按钮数 " + hintBoxes.Count);
            Check("接触影贴着玩家的脚走（突进半步之后影子不留在原地）",
                Mathf.Abs(controller.CombatPlayerShadowCenterX() - controller.CombatPlayerCenterX()) < 1f,
                "影子中心 x=" + controller.CombatPlayerShadowCenterX().ToString("0.#") +
                "，玩家 x=" + controller.CombatPlayerCenterX().ToString("0.#"));
            Capture("k-combat-09-layout");

            controller.CombatPlaceForCheck(-.4f, .5f);
            controller.CombatActForCheck(CombatActionType.Light);
            Check("玩家起手扣基线 8 点精力（60→52）", controller.CombatPlayer.Stamina == 52, controller.CombatPlayer.Stamina + "");
            controller.CombatClockAdvance(.13f);
            Check("基线不破：平攻躯干仍是野猪 60→44", controller.CombatFoe.Hp == 44, controller.CombatFoe.Hp + "");
            Check("平攻不计暴击", controller.CombatCriticalCount == 0);
            var flash = controller.CombatFoeFlashUntil - controller.CombatNow;
            Check("受击白光频闪被点亮（≤.08 秒）", flash > 0f && flash <= .081f, flash.ToString("0.###"));
            Check("伤害跳字落在浮动字层里", CountFloaters() >= 1, CountFloaters() + " 个");
            Check("换部位按钮在页面上", FindActiveButton("换部位") != null);
            Capture("k-combat-01-bodylock");

            // 弱点锁敌：把沙包扶满再砍，逐刀核数。
            RefreshSides();
            controller.CombatCycleLockedPartForCheck();
            Check("Tab 一次锁定到獠牙头颅", controller.CombatLockedPartId == "head", controller.CombatLockedPartId);
            // 期望串跟着 N4-Polish 3③ 的文案改：`1.5x` → `×1.5`（手写体没有中点"·"，分隔符换成全角逗号）。
            // 这是**换期望值**，不是放宽——部位名与倍率两样都还得在标签里，缺任一条照样红。
            Check("锁定标签写出部位名与倍率", LockLabelText().Contains("獠牙头颅") && LockLabelText().Contains("×1.5"),
                LockLabelText());
            controller.CombatActForCheck(CombatActionType.Light);
            controller.CombatClockAdvance(.13f);
            Check("弱点命中按 1.5x 结算（(8+10)×1.5−2 = 25）", controller.CombatFoe.Hp == 35,
                controller.CombatFoe.Hp + "");
            Check("弱点命中计入暴击", controller.CombatCriticalCount == 1);
            Check("暴击跳字字号更大（≥46）", FloaterFontSizeMax() >= 46f, FloaterFontSizeMax() + "");
            Check("跳字待在静态 UI 之下（不飘到\u201c换部位/部位标签\u201d那一排，Q2-3 附带修的时间口径）",
                controller.CombatFloaterTopY() >= 270f,
                "最靠上的跳字 y=" + controller.CombatFloaterTopY().ToString("0.#") + "（部位标签占 222..262）");
            Capture("k-combat-02-critical");

            controller.CombatCycleLockedPartForCheck();
            Check("Tab 循环走到下一个部位", controller.CombatLockedPartId == "body", controller.CombatLockedPartId);
            controller.CombatSetLockedPartForCheck("不存在部位");
            Check("锁到不存在的部位时如实退回平攻", controller.CombatLockedPartId == "");

            // 临战整备：铁剑要先进行囊才穿得上（装备绝不自动穿）。
            InventoryRules.Add(controller.ActiveSave.bag, items, "tiejian", 1);
            Check("装备未登记物品被拒且不崩", !controller.CombatEquipForCheck("不存在的剑"));
            Check("装备铁剑成功", controller.CombatEquipForCheck("tiejian"));
            Check("总攻精确 +15（出身 8 → 23）", controller.CombatGear.TotalAttack(8) == 23);
            Check("Core 修正量同步 +15", controller.CombatPlayer.AttackModifier == 15);
            Check("整备行写得出兵刃名与耐久", GearLabelText().Contains("铁剑") && GearLabelText().Contains("耐久"),
                GearLabelText());
            RefreshSides();
            controller.CombatActForCheck(CombatActionType.Light);
            controller.CombatClockAdvance(.13f);
            Check("装备改写这一刀：(8+15+10)−2 = 31", controller.CombatFoe.Hp == 29, controller.CombatFoe.Hp + "");
            Check("一刀之后铁剑耐久 119", controller.CombatGear.Weapon.Durability == 119,
                controller.CombatGear.Weapon.Durability + "");
            // 重击这一刀另起一场来验：刚开战的 1.2 秒是野猪的出招间隔（foeNextActAt=1.2），
            // 重击 .28 秒就落，落点确定；在原场子里它随时可能一记轻敲把我们打断成"没落地也就不扣耐久"。
            controller.ShowTown();
            controller.ShowCombat("encounter-boar-01");
            controller.CombatPlaceForCheck(-.4f, .5f);
            Check("重击耐久用例另开一场且重新整备", controller.CombatEquipForCheck("tiejian"));
            controller.CombatActForCheck(CombatActionType.Heavy);
            controller.CombatClockAdvance(.3f);
            Check("重击一刀扣 2 点耐久（118）", controller.CombatGear.Weapon.Durability == 118,
                controller.CombatGear.Weapon.Durability + "");
            Check("新场的开场仍是满血满精力（战败前科不被误继承）", controller.CombatPlayer.Hp == 60,
                controller.CombatPlayer.Hp + "");
            Capture("k-combat-03-gear");

            var coinsBefore = controller.ActiveSave.coins;
            var repaired = controller.CombatRepairForCheck();
            Check("磨刃补满耐久并按价收钱", repaired.Done && controller.CombatGear.Weapon.Durability == 120 &&
                repaired.CoinsPaid >= 4, repaired.CoinsPaid + " 文");
            Check("磨刃的钱真的落进存档", controller.ActiveSave.coins == coinsBefore - repaired.CoinsPaid);
            Check("满耐久再磨不重复收钱",
                !controller.CombatRepairForCheck().Done && controller.ActiveSave.coins == coinsBefore - repaired.CoinsPaid);

            // 看破：盯住对手判定帧起闪，让开这一击并讨回一下。
            // 起闪时机不能靠采样碰运气：Core 认的是"距判定帧还剩 RiposteWindowSeconds(.15)"这一半预判，
            // 而闪避起手本身要 Dodge startup(.05) 才拿到无敌帧。正确的一按落在
            // 剩余前摇 ∈ [.05, .15] 这 .10 秒里——这一段恰好是玩家眼睛看得见的东西（前摇动画）。
            // 上一版每 .02 秒采一次、且在看到 Startup 的第一帧就按，等于永远按在剩余 .18 秒，
            // 系统性早了 .03 秒以上，看破永远点不亮（本轮实测的第三条假红）。
            RefreshSides();
            CombatRules.RestoreHp(controller.CombatPlayer, controller.CombatPlayer.MaxHp);
            controller.CombatSetLockedPartForCheck("head");
            var sawRiposte = false;
            var pressAttempts = 0;
            var (dodgeStartup, dodgeActive, _) = CombatRules.Timing(CombatActionType.Dodge);
            for (var i = 0; i < 900 && !sawRiposte; i++)
            {
                controller.CombatClockAdvance(.01f);
                var foeNow = controller.CombatFoe;
                if (foeNow == null || foeNow.Phase != CombatPhase.Startup) continue;
                if (controller.CombatPlayerPhase != "Idle") continue;
                if (controller.CombatPlayer.Stamina < CombatRules.DodgeStaminaCost) continue;
                var toActive = foeNow.PhaseUntil - controller.CombatNow;
                if (toActive < dodgeStartup || toActive > CombatRules.RiposteWindowSeconds + .002f) continue;
                pressAttempts++;
                controller.CombatActForCheck(CombatActionType.Dodge);
                for (var k = 0; k < 40 && !sawRiposte; k++)
                {
                    controller.CombatClockAdvance(.01f);
                    sawRiposte = controller.CombatCounterReady;
                }
                CombatRules.RestoreHp(controller.CombatPlayer, controller.CombatPlayer.MaxHp);
            }
            Check("预判野猪前摇起闪能在真页面上看破", sawRiposte,
                "按了 " + pressAttempts + " 次，无敌窗 " + dodgeStartup + "+" + dodgeActive + "s");
            if (sawRiposte)
            {
                RefreshSides();
                var critBefore = controller.CombatCriticalCount;
                var before = controller.CombatFoe.Hp;
                controller.CombatActForCheck(CombatActionType.Light);
                controller.CombatClockAdvance(.13f);
                var punish = before - controller.CombatFoe.Hp;
                Check("看破反击无视防御并按弱点结算（≥40）", punish >= 40, punish + "");
                Check("看破反击这一刀把暴击计数加上 1", controller.CombatCriticalCount == critBefore + 1,
                    critBefore + " → " + controller.CombatCriticalCount);
                Check("反击之后看破状态清空", !controller.CombatCounterReady);
            }
            Capture("k-combat-04-riposte");

            // 精力条补间：把玩家挂空再静置，条子应该追上真值。
            // 静置这段挨打是常态，所以逐拍扶住人——我们要验的是条子，不是能不能活。
            var idleFor = 0;
            while (controller.CombatPlayer.Stamina < controller.CombatPlayer.MaxStamina && idleFor++ < 200)
            {
                CombatRules.RestoreHp(controller.CombatPlayer, controller.CombatPlayer.MaxHp);
                controller.CombatClockAdvance(.05f);
            }
            CombatRules.RestoreHp(controller.CombatPlayer, controller.CombatPlayer.MaxHp);
            controller.CombatClockAdvance(.6f);
            Check("精力条补间最终收敛到真实值（不留半格）",
                Mathf.Abs(controller.CombatPlayerStaminaBarRatio - 1f) < .001f &&
                controller.CombatPlayer.Stamina == controller.CombatPlayer.MaxStamina,
                controller.CombatPlayerStaminaBarRatio.ToString("0.###"));

            // 战斗内服药（D-09/N3）：回复量的真源在 items.json。当前那味金疮药没写明数字，
            // 所以这条路径的正确行为是"拒用"；O 落了 combatHeal 字段之后同一段断言自动改走"回血"分支。
            Check("服药按钮在页面上（与轻/重/闪同一排，玩家点得到而不是只有键盘党知道）",
                FindActiveButton("服 药") != null);
            CombatRules.RestoreHp(controller.CombatPlayer, 30);
            var usablePills = controller.CombatUsablePillsForCheck();
            var bagBefore = usablePills.Count > 0 ? InventoryRules.Count(controller.ActiveSave.bag, usablePills[0]) : 0;
            var jinchuangBefore = InventoryRules.Count(controller.ActiveSave.bag, "jinchuangyao");
            var hpBeforePill = controller.CombatPlayer.Hp;
            if (usablePills.Count > 0)
            {
                var dose = controller.CombatConsumeForCheck(usablePills[0]);
                Check("有回复量的丹药：按物品数据字段回血、扣一件、付破绽窗",
                    dose.Executed && dose.Healed > 0 && dose.HpAfter == Math.Min(60, hpBeforePill + dose.Healed) &&
                    InventoryRules.Count(controller.ActiveSave.bag, usablePills[0]) == bagBefore - 1 &&
                    controller.CombatPlayerPhase == "Hitstun",
                    usablePills[0] + " 回 " + dose.Healed + "（" + dose.Effect.Source + "）");
                var pillDisk = controller.Repository.Read(slotUsed).Data;
                Check("服药落盘往返｜重新读档，那件药真的少了一个",
                    InventoryRules.Count(pillDisk.bag, usablePills[0]) == bagBefore - 1,
                    "磁盘 " + InventoryRules.Count(pillDisk.bag, usablePills[0]) + " vs 服药前 " + bagBefore);
                // 提示条确实挂在屏幕上——它是下面那条负判据的前提（取不到就跳过＝假绿，这一族已经栽过）。
                Check("服药提示条此刻真的在屏幕上（负判据的前提：不是\"没找到文字\"蒙过去的绿）",
                    AnyText("服下"));
            }
            else
            {
                var refused = controller.CombatConsumeForCheck("jinchuangyao");
                Check("没写明回复量的丹药：拒用、血不动、件数不动（不假装有效）",
                    !refused.Executed && controller.CombatPlayer.Hp == hpBeforePill &&
                    InventoryRules.Count(controller.ActiveSave.bag, "jinchuangyao") == jinchuangBefore,
                    refused.Reason + " · 件数 " + jinchuangBefore + " → " +
                    InventoryRules.Count(controller.ActiveSave.bag, "jinchuangyao"));
                // 这里原来还有一条「当前 items.json 的丹药一律没写明数字」——钉的是数据快照，
                // 且只在 else 里跑（见下方 D-29 补桩段的说明）。F-6 第 3 件按架构师令删除：
                // 它的判据已由下面两条「两个分支都跑」的护栏接管，留着反而会在
                // "表里有数、但行囊里恰好没那味药" 的混合数据态里假红。
            }

            // ==== D-29 补的那记死桩（F-6 第 3 件）====
            // 原来 else 分支里那条"当前 items.json 的丹药一律没写明数字"钉的是数据快照；
            // T070 落了 combatHeal 之后 usablePills.Count > 0 ⇒ else 永远走不到 ⇒
            // 这条既不红、也根本不执行——正是 T081/D-31 那一族"判据根本不跑"。那条已删，
            // 下面两条是它的替代：**两个分支都跑**，断言的是行为与一致性，不依赖数据偶然成立。
            var pillsInTable = items.Items.Where(d => d.Category == "yaodan").ToList();
            var disagree = pillsInTable.Where(d =>
                (CombatConsumables.EffectFor(items, ItemsJson, d.Id).HealHp > 0) != (RawHealOf(d.Id) > 0)).ToList();
            Check("真表每一条丹药：可服 ⟺ 原文写明数量（全表对表，两分支都跑）",
                disagree.Count == 0, disagree.Count + " 味不一致 / 共 " + pillsInTable.Count + " 味：" +
                string.Join(",", disagree.Select(d => d.Id)));
            var shouldList = pillsInTable.Where(d => RawHealOf(d.Id) > 0 &&
                InventoryRules.Count(controller.ActiveSave.bag, d.Id) > 0).Select(d => d.Id).ToList();
            Check("页面可服清单 == 表里有数且行囊里有的那些（不多列也不漏列）",
                usablePills.Count == shouldList.Count && !shouldList.Any(id => !usablePills.Contains(id)),
                "页面 [" + string.Join("/", usablePills) + "] vs 应有 [" + string.Join("/", shouldList) + "]");
            // N4-Polish 3①：提示条是玩家会读的一行字，不该印内部字段名（原来那句写着"数值来自字段
            // combatHeal"）。判据不动——"数值确实从表里读"仍由 A 段那组 Effect.Source 断言守着，
            // 这里只查"屏幕上看得见的话里有没有内部名"，且两个分支都跑。
            var leakyTexts = UnityEngine.Object.FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsSortMode.None)
                .Count(t => t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy &&
                    (t.text.Contains("combatHeal") || t.text.Contains("数值来自")));
            Check("战斗页面上任何一处可见文字都不印内部字段名（combatHeal /「数值来自」）",
                leakyTexts == 0, leakyTexts + " 段可见文字带着内部名（0 = 干净）");
            Capture("k-combat-07-pill");
            RefreshSides();

            // 蛮牛冲撞的手感作证：profile2 之后野猪的威胁来自"慢 + 长 + 击退"，不是秒人。
            var pushed = 0f;
            // F-6 第 1 件（定性）：原来这条只采**一个瞬间**的余量（`foeStamina > 0`）。
            // 冲撞一记就花 20 点，所以"刚好打在冲撞后"时读到 0 是完全正常的——它测的是运气，
            // 不是"会不会回精"。3号 在 519f46d 交接里预告的那条红就是这个形状（他的树还没有 foe Tuning）。
            // 现在量这条断言真正声称的东西：这一窗里精力有没有出现过**净上升**
            // （单步正向增量只可能来自 Regenerate；出手那一步一定是负的）。
            // 谁把 `Regenerate(combatFoe, dt)` 删了，这一窗里一步都不会涨 ⇒ 当场红，且不会假红。
            var foeStaPrev = controller.CombatFoe.Stamina;
            var foeStaMaxStepGain = 0;
            for (var i = 0; i < 400 && pushed <= .2f; i++)
            {
                var xBefore = controller.CombatPlayer.X;
                CombatRules.RestoreHp(controller.CombatPlayer, controller.CombatPlayer.MaxHp);
                controller.CombatClockAdvance(.05f);
                pushed = Math.Abs(controller.CombatPlayer.X - xBefore);
                var foeStaNow = controller.CombatFoe.Stamina;
                if (foeStaNow - foeStaPrev > foeStaMaxStepGain) foeStaMaxStepGain = foeStaNow - foeStaPrev;
                foeStaPrev = foeStaNow;
            }
            Check("冲撞把玩家撞开（knockback .45 单位，profile2 才有的手感）", pushed > .2f,
                pushed.ToString("0.###") + " 单位");
            var foeStamina = controller.CombatFoe.Stamina;
            Check("妖兽也会回精（量的是这一窗里出现过净上升，不是某一瞬的余量）",
                foeStaMaxStepGain > 0, "单步最大回升 +" + foeStaMaxStepGain + " 点，此刻余 " + foeStamina);
            Capture("k-combat-08-charge");

            // 收掉这一场：胜利结算 + 掉落入包。
            var spoilsCoinsBefore = controller.ActiveSave.coins;
            for (var i = 0; i < 12 && !controller.CombatFoe.IsDead; i++)
            {
                controller.CombatPlaceForCheck(-.4f, .5f);
                controller.CombatActForCheck(CombatActionType.Heavy);
                controller.CombatClockAdvance(.78f);
            }
            Check("带装备的连段能收掉野猪妖", controller.CombatFoe.IsDead);
            Check("胜利结算面板唯一", ActiveObjects("Combat settle") == 1);
            Check("赏钱与掉落照旧入档", controller.ActiveSave.coins == spoilsCoinsBefore + boar.Coins &&
                InventoryRules.Count(controller.ActiveSave.bag, "larou") >= 2,
                controller.ActiveSave.coins + "");
            Check("面板报得出倒下的是谁", AnyText("野猪妖 倒下了"));
            // ==== T051 写入半边（交接包 L-4 第 7 条）====
            // 3号 交了存储半边（SaveData.gear + GearLedger），但全仓没有一处调用 Record ⇒ 字段永远空着。
            // 这两条钉的就是"有人写了、而且写得回来"：台账里的 itemId/耐久必须与身上这件逐字相等，
            // 且要小于满耐久（这一场真的磨过），并且重新读档还在。
            var wornWeapon = controller.CombatGear.Weapon;
            var ledgerWeapon = GearLedger.Of(controller.ActiveSave, GearSlots.Weapon);
            Check("打完一场，身上这件兵刃的耐久真的写进了穿戴台账（终结「字段在、没人写」）",
                wornWeapon != null && ledgerWeapon != null &&
                ledgerWeapon.itemId == wornWeapon.Entry.ItemId &&
                ledgerWeapon.durability == wornWeapon.Durability &&
                ledgerWeapon.durability < wornWeapon.Entry.MaxDurability,
                "台账 " + (ledgerWeapon == null ? "没有这一条" : ledgerWeapon.itemId + " 耐久 " + ledgerWeapon.durability) +
                "，身上 " + (wornWeapon == null ? "空手" : wornWeapon.Entry.ItemId + " 耐久 " +
                    wornWeapon.Durability + "/" + wornWeapon.Entry.MaxDurability));
            // 第二半（"重新读档还在"）今天恢复成真断言了。它上一批必红，红因不在写入半边：
            // `SaveModels.cs` 的 `GearLine`(:217) 与 `CraftLine`(:243) 头上缺 `[Serializable]`，
            // 而产线 codec 是 JsonUtility ⇒ 缺它整个字段**静默不写**（不报错、不红、读档归零）。
            // 那是 3号 的域，他按 T118 修好（`3b5fc8f`），恢复由架构师 R-2 点名给本线
            // （"T118 战斗侧判据形状 → 2号"，`1ca4a0e`）。原来那段"今天实测它必红"的说明一并改掉——
            // **留一条说不实话的注释比留一条说不实话的判据更坏**：下一个人会照着它去改存储层。
            var diskSave = controller.Repository.Read(slotUsed).Data;
            var diskWeapon = GearLedger.Of(diskSave, GearSlots.Weapon);
            Check("落盘往返｜重新读档，台账里那条穿戴还在、耐久是扣后的值（T118 战斗侧那半，形状是 3号 给的）",
                diskWeapon != null && ledgerWeapon != null &&
                diskWeapon.itemId == ledgerWeapon.itemId &&
                diskWeapon.durability == ledgerWeapon.durability,
                "磁盘 " + (diskWeapon == null ? "没有这一条" : diskWeapon.itemId + " 耐久 " + diskWeapon.durability) +
                "，内存 " + (ledgerWeapon == null ? "没有这一条" :
                    ledgerWeapon.itemId + " 耐久 " + ledgerWeapon.durability));
            // 这一条钉的是"整个数组没落盘"那一族（3号 原文：内存 2 条、磁盘 0 条）。
            // 今天身上只有一件穿戴（B 段只装过铁剑），所以它是**整表在不在**的检查，不是"多行只回第一行"
            // 的检查——这一句区别写在这里，免得下一个人把它当成后者而放心加第二个槽。
            var wornRows = GearLedger.CountOf(controller.ActiveSave);
            Check("落盘往返｜磁盘上的台账条数与内存一致（JsonUtility 静默不写整个字段时这里是 0）",
                GearLedger.CountOf(diskSave) == wornRows && wornRows >= 1,
                "磁盘 " + GearLedger.CountOf(diskSave) + " 条，内存 " + wornRows + " 条（有账的槽位：" +
                string.Join("/", GearSlots.All.Where(s => GearLedger.Of(controller.ActiveSave, s) != null)) + "）");
            // 条件之一不成立时，存储层会**静默**把这一行抹掉，这条钉的是"我这行写得出去的全部前提"：
            var keepSlot = GearSlots.All.Contains(ledgerWeapon?.slot);
            var keepItem = !string.IsNullOrWhiteSpace(ledgerWeapon?.itemId);
            var keepDurability = ledgerWeapon != null && ledgerWeapon.durability >= 0;
            Check("台账里这条满足存档的保留条件（槽位已知 / itemId 非空 / 耐久≥0）——落盘那半见上面两条",
                keepSlot && keepItem && keepDurability,
                "slot=" + ledgerWeapon?.slot + " 已知=" + keepSlot + "，itemId 非空=" + keepItem +
                "，耐久 " + ledgerWeapon?.durability + "≥0=" + keepDurability);
            Capture("k-combat-05-victory");

            // 战败保护：站桩挨打，逐条验"不崩 / 有面板 / 有淡出 / 钱不动"。
            controller.ShowTown();
            controller.ShowCombat("encounter-boar-01");
            var defeatCoins = controller.ActiveSave.coins;
            Check("撤离重进满血（无战败前科时不该带伤）", controller.CombatPlayer.Hp == 60,
                controller.CombatPlayer.Hp + "");
            var guard = 0;
            while (!controller.CombatPlayer.IsDead && guard++ < 600) controller.CombatClockAdvance(.06f);
            Check("站桩挨打会真的倒下（不是死循环）", controller.CombatPlayer.IsDead);
            Check("战败出现唯一一张结算面板", ActiveObjects("Combat settle") == 1);
            Check("战败淡出罩另起物件（不抢面板名额）", ActiveObjects("Defeat fade") == 1);
            Check("气血归零不是负数", controller.CombatPlayer.Hp == 0);
            // D-08：战利品与药资是两件事，必须拆成两条独立断言（上一轮混在一条里，
            // 于是要么"不发奖励"要么"不扣药资"，永远只能对一半）。
            Check("断言 A｜战败不发战利品：赏钱 0 且掉落 0 件", controller.LastSpoils == null ||
                (controller.LastSpoils.CoinsGranted == 0 && controller.LastSpoils.Drops.Count == 0),
                controller.LastSpoils?.Describe());
            var expectedFee = (int)Math.Floor(defeatCoins * CombatDefeatProtection.MedicalFeeRatio);
            Check("断言 B｜药资按 10% 另计并真扣进内存档",
                controller.ActiveSave.coins == Math.Max(0, defeatCoins - expectedFee) &&
                controller.LastDefeatSettlement?.MedicalFee == expectedFee,
                defeatCoins + " → " + controller.ActiveSave.coins + "（药资 " + expectedFee + "）");
            Check("扣款后铜钱不为负", controller.ActiveSave.coins >= 0);
            var onDisk = controller.Repository.Read(slotUsed).Data;
            Check("落盘往返｜重新读档，铜钱仍是扣后的值（不是只在内存里扣）",
                onDisk.coins == controller.ActiveSave.coins && onDisk.coins == Math.Max(0, defeatCoins - expectedFee),
                "磁盘 " + onDisk.coins);
            Check("战败账单算出三成气血（18）", controller.LastDefeatSettlement != null &&
                controller.LastDefeatSettlement.AwakeHp == 18);
            Check("账单写明醒来地点", controller.LastDefeatSettlement?.WakeAt == "青石镇药老药圃",
                controller.LastDefeatSettlement == null ? "账单物件为空" : controller.LastDefeatSettlement.WakeAt);
            Check("面板上看得见药老的话", AnyText("莫要强撑"));
            Check("面板上写得出淡出语", AnyText("眼前一黑"));
            // 图上真看得见才算数：AnyText 读得到 TMP 里的字符串，却读不到"这行字被淡出罩涂黑了"——
            // k-combat-06 实测就是一张全黑图（断言全绿、玩家一个字看不见）。层级压序必须单独断言。
            Check("账单面板压在淡出罩之上（淡出到位时账单不能被涂黑）",
                controller.CombatSettleAboveFade, "罩子不透明度 " + controller.CombatFadeAlpha.ToString("0.###"));
            Check("面板上写得出药资那一行，数字来自本次结算",
                AnyText("药资 " + expectedFee + " 文已付"), "药资 " + expectedFee + " 文");
            Capture("k-combat-06-defeat");
            FindActiveButton("返回青石镇")?.onClick.Invoke();
            Check("战败后回得去青石镇", controller.CurrentPage == "town");
            var noNre = problems.Count(p => p.Contains("NullReference"));
            Check("整条战败链路没有 NullReference", noNre == 0,
                string.Join(" | ", problems.Where(p => p.Contains("NullReference"))));

            controller.ShowCombat("encounter-boar-01");
            Check("醒来第一场开场气血锁在 18", controller.CombatPlayer.Hp == 18, controller.CombatPlayer.Hp + "");
            Check("开场不是死亡相态", controller.CombatPlayerPhase == "Idle");
            Check("醒来的场次不残留结算面板", ActiveObjects("Combat settle") == 0);
            controller.ShowTown();
            controller.ShowCombat("encounter-boar-01");
            Check("三成气血只影响一场，之后恢复满血", controller.CombatPlayer.Hp == 60, controller.CombatPlayer.Hp + "");
            controller.ShowTown();
            SuiteFoeArtStates();
            Debug.Log(Tag + "B 段表现层完成：" + viewChecks + " 项");
        }

        // ==== N5-EnemyArt（P-2 触发式在册活第 1 行）：三只妖的取图状态回归 ====
        // 期望一律从 encounter.art 现推，**不写死"冥狼一定没图"**：夜4 的图一灌装，这批断言应当
        // 自动改成"读真图"而不是变成一片红；图被撤掉时也要自动改回"走回退"。
        // 主判据（"画面上能看出是回退"）的独立一侧来自**渲染结果**（像素），不来自我自己写的钩子——
        // 拿被检对象的自述当判据是空的（n4-polish-report §7 立的那条规矩）。
        static void SuiteFoeArtStates()
        {
            // 夹具侧字面量：改文案得来这儿签一次，不许悄悄换成另一句俏皮话（与 D-47 那条同一手法）。
            const string FallbackCopyExpected = "形影未至，暂以标记代之";
            // 阈值按"失败态长什么样"定，不按本次实测值定：
            //   旧版那块白方贴在那里 ⇒ 纯白 ≈100%；"妖不见了"那一版 ⇒ 底纸 ≈100%、声明色 0%。
            // 三个阈值各自离失败态都有数倍距离，实测数字逐条打在 detail 里，跑完当场可核。
            const int WhiteMaxPercent = 20, PaperMaxPercent = 60, TintedMinPercent = 15;
            // 样本闸：探针拿不到像素时比例是 0/0，"纯白 < 20%"会拿"一个像素都没有"判成通过。
            // 这一记就是本轮第一次跑出来的教训（槽位被算成 13×12，五条判据里有三条在空转）。
            // 8000 = 扁牌档（180×81 ≈ 1.5 万）再对折，低于任何一块真实标记的面积。
            const int MinProbePixels = 8000;
            var internalNameNeedles = new[] { "art", "texture", "resources", "rawimage", "npc", "enemyid" };

            var encounters = controller.World?.Encounters;
            Check("战斗页读得到遭遇表（取图的声明在这里，不在 id 拼串里）",
                encounters != null && encounters.Count == 3,
                encounters == null ? "World 未加载" : encounters.Count + " 条");
            if (encounters == null || encounters.Count == 0) return;

            var markers = new List<(string id, string shape, Color tint)>();
            foreach (var enc in encounters)
            {
                controller.ShowCombat(enc.Id);
                // 必须先推一次钟：妖的 anchoredPosition 由 RefreshCombatView 落位，刚开战时它还停在
                // RectTransform 的原点（在画面左上角之外），拍出来就是"妖不见了"。
                // 这条坑 B 段拍 k-combat-09 那张时踩过一次，注释还写在那儿——我这批又踩了一遍：
                // 第一次跑的 5 条红里有 3 条就是"槽位算成 13×12 / 标记落在画布外"。
                controller.CombatClockAdvance(.05f);
                var declared = string.IsNullOrEmpty(enc.Art) ? null : Resources.Load<Texture2D>(enc.Art);
                var expectReal = declared != null;
                var seen = string.IsNullOrEmpty(controller.CombatFoeArtSource)
                    ? "(回退绘制)" : controller.CombatFoeArtSource;
                Check(enc.Enemy + "：这一场该" + (expectReal ? "读真图" : "走回退绘制") +
                        "（期望从 encounter.art 现推，名单不写死）",
                    controller.CombatFoeArtIsFallback == !expectReal,
                    "声明=" + (string.IsNullOrEmpty(enc.Art) ? "(空)" : enc.Art) + "，实取=" + seen);
                Check(enc.Enemy + "：遭遇声明没有落空的那一句（改名或漏件当场报，不许静默换图）",
                    !controller.CombatFoeDeclaredArtMissing,
                    controller.CombatFoeDeclaredArtMissing ? "声明了 " + enc.Art + " 却取不到" : "一致");

                if (expectReal)
                {
                    Check(enc.Enemy + "：真图取的就是声明那一张，且此刻真的画在屏幕上",
                        controller.CombatFoeArtSource == enc.Art && controller.CombatFoePictureVisible,
                        controller.CombatFoeArtSource);
                    // 真图那一档只看"纯白侧"（图取空了就会退回整块白），声明色那一侧对它没有意义，
                    // 所以 expect 传 Color.clear，不参与这一条判定。
                    // 新钩子的坐标口径先自证一次：同一块立绘，用 anchoredPosition 读出来的下缘
                    // 与用 rect 读出来的高度必须对得上（对不上就是"槽位算成 13×12"那类空转，
                    // 探针会拿着 0 个像素判通过）。
                    var slotReal = controller.CombatFoeSlotRect();
                    Check(enc.Enemy + "：取图钩子量出来的那块格子与立绘的落位自洽（探针的取样区不是空气）",
                        Mathf.Abs(slotReal.yMax - controller.CombatFoeSpriteBottomY()) < 1f &&
                        Mathf.Abs(slotReal.width - controller.CombatFoeHeightRatio * 1080f) < 1f,
                        "槽位 " + slotReal.width.ToString("0") + "×" + slotReal.height.ToString("0") +
                        "，下缘 " + slotReal.yMax.ToString("0.#") + " vs 立绘底边 " +
                        controller.CombatFoeSpriteBottomY().ToString("0.#"));
                    var real = CaptureAndProbeFoe("k-combat-10-foe-art-" + enc.Enemy, Color.clear);
                    Check(enc.Enemy + "：真图那一档在像素上也不是整块纯白（图取空了会在这里红）",
                        real.total >= MinProbePixels && real.white * 100 < WhiteMaxPercent * real.total,
                        "纯白 " + Percent(real.white, real.total) + "%，底纸 " +
                        Percent(real.paper, real.total) + "%（" + real.total + " 个像素），槽位 " +
                        slotReal.width.ToString("0") + "×" + slotReal.height.ToString("0"));
                }
                else
                {
                    // 这一档就是本批要修的东西：以前这里落 Texture2D.whiteTexture，
                    // 一块 316 的白方贴在半空的战场上，读起来像"这只妖长得就是一块白"。
                    var slot = controller.CombatFoeSlotRect();
                    Check(enc.Enemy + "：没图时立绘组件关掉，画面上不许留静默白块（也不许撑满整格）",
                        !controller.CombatFoePictureVisible &&
                        !float.IsNaN(slot.xMin) && slot.width < 260f && slot.height < 260f,
                        "槽位 " + slot.width.ToString("0") + "×" + slot.height.ToString("0"));
                    var marker = controller.CombatFoeMarkerRect();
                    var label = controller.CombatFoeMarkerLabelRect();
                    Check(enc.Enemy + "：回退标记与它那行字都在场，且整块落在画布内（画面上真看得到）",
                        marker != null && marker.gameObject.activeInHierarchy && label != null &&
                        slot.xMin >= 0f && slot.xMax <= 1920f && slot.yMin >= 0f && slot.yMax <= 1080f,
                        "标记=" + (marker != null ? "在场" : "无") + "，标注=" + (label != null ? "在场" : "无"));
                    Check(enc.Enemy + "：那行字写得出「" + FallbackCopyExpected + "」（夹具侧字面量，改文案得来签）",
                        controller.CombatFoeFallbackCopy == FallbackCopyExpected &&
                        AnyText(FallbackCopyExpected), controller.CombatFoeFallbackCopy);
                    var lower = controller.CombatFoeFallbackCopy.ToLower();
                    var leaked = internalNameNeedles.FirstOrDefault(n => lower.Contains(n));
                    Check(enc.Enemy + "：那句标注里没有内部字段名（N4 抓过的那族病不许在回退文案里回来）",
                        leaked == null, leaked ?? "干净");
                    // 颜色与形状必须来自同一条遭遇声明：这是"两页同源"的机器侧证据。
                    // 解析失败用一个不可能出现的负色（比对必红），不写"解析不出来就跳过"那种 Coverage 假绿。
                    var want = ParseTint(enc.Tint) ?? new Color(-1f, -1f, -1f);
                    var got = controller.CombatFoeMarkerTint;
                    Check(enc.Enemy + "：回退标记的配色就是遭遇声明那个 tint（两边各解析一遍再比）",
                        Mathf.Abs(want.r - got.r) < .011f &&
                        Mathf.Abs(want.g - got.g) < .011f && Mathf.Abs(want.b - got.b) < .011f,
                        "声明 " + (enc.Tint ?? "(空)") + " → 实画 " +
                        got.r.ToString("0.##") + "," + got.g.ToString("0.##") + "," + got.b.ToString("0.##"));
                    Check(enc.Enemy + "：回退标记的形状沿用遭遇声明的 shape",
                        controller.CombatFoeMarkerShape == (string.IsNullOrEmpty(enc.Shape) ? "ring" : enc.Shape),
                        controller.CombatFoeMarkerShape);
                    var band = controller.CombatGroundBandY();
                    Check(enc.Enemy + "：回退标记的脚也踩在同一条地带里（回退档不是浮在半空的一块色）",
                        !float.IsNaN(band.x) && !float.IsNaN(slot.yMax) &&
                        slot.yMax >= band.x - .5f && slot.yMax <= band.y + 1f,
                        "标记下缘 " + slot.yMax.ToString("0.#") + "，地带 " +
                        band.x.ToString("0.#") + ".." + band.y.ToString("0.#"));
                    var probe = CaptureAndProbeFoe("k-combat-11-foe-fallback-" + enc.Enemy, got);
                    Check(enc.Enemy + "：像素上读得出这是回退——槽位里有一片声明色、不是整块纯白也不是一块空的底纸（这条不靠我自己的钩子）",
                        probe.total >= MinProbePixels &&
                        probe.white * 100 < WhiteMaxPercent * probe.total &&
                        probe.paper * 100 < PaperMaxPercent * probe.total &&
                        probe.tinted * 100 >= TintedMinPercent * probe.total,
                        "声明色 " + Percent(probe.tinted, probe.total) + "%，纯白 " +
                        Percent(probe.white, probe.total) + "%，底纸 " + Percent(probe.paper, probe.total) +
                        "%，共 " + probe.total + " 像素，槽位 " +
                        slot.width.ToString("0") + "×" + slot.height.ToString("0"));
                }

                // 强制回退这一手不是为了看图新鲜：夜4 三张图灌齐之后，"没图"这个状态就没有真数据可跑，
                // 回退画法会退成一条没人走过的死支路（F-6 那记死桩的同族）。钩子走的正是同一段画法。
                controller.CombatSetFoeFallbackForCheck(true);
                controller.CombatClockAdvance(.05f);   // 新造的标记要过一次刷新才进画布批次
                Check(enc.Enemy + "：强行按回退档画时立绘关掉、标记与标注同时出现（两条支路都活着）",
                    controller.CombatFoeArtIsFallback && !controller.CombatFoePictureVisible &&
                    controller.CombatFoeMarkerRect() != null &&
                    controller.CombatFoeMarkerLabelRect() != null);
                markers.Add((enc.Enemy, controller.CombatFoeMarkerShape, controller.CombatFoeMarkerTint));
                if (expectReal)
                {
                    var forced = CaptureAndProbeFoe("k-combat-12-foe-marker-" + enc.Enemy,
                        controller.CombatFoeMarkerTint);
                    Check(enc.Enemy + "：同一只妖改画回退之后，像素上一样读得出（三种形状都要有真样本）",
                        forced.total >= MinProbePixels &&
                        forced.tinted * 100 >= TintedMinPercent * forced.total &&
                        forced.white * 100 < WhiteMaxPercent * forced.total &&
                        forced.paper * 100 < PaperMaxPercent * forced.total,
                        "声明色 " + Percent(forced.tinted, forced.total) + "%，纯白 " +
                        Percent(forced.white, forced.total) + "%，底纸 " + Percent(forced.paper, forced.total) +
                        "%，共 " + forced.total + " 像素，形状 " + controller.CombatFoeMarkerShape);
                }
                controller.CombatSetFoeFallbackForCheck(false);
                Check(enc.Enemy + "：撤掉强制回退后回到按声明取的那一张（降级可逆，不留残留）",
                    expectReal
                        ? !controller.CombatFoeArtIsFallback && controller.CombatFoePictureVisible &&
                          controller.CombatFoeMarkerRect() == null
                        : controller.CombatFoeArtIsFallback && !controller.CombatFoePictureVisible);
            }

            // T069 的口径搬到战斗页：同一种图形代表 60/90/200 三档风险，等于把选择权偷偷拿掉。
            // 三只都强行按回退画一遍，这条才有真实样本可比（否则"两两可辨"在只有一只缺图的今天是空的）。
            for (var i = 0; i < markers.Count; i++)
                for (var j = i + 1; j < markers.Count; j++)
                    Check("三只妖的回退标记必须形状或配色至少一项不同（同形同色 = 点下去之前不知道面对的是什么）",
                        markers[i].shape != markers[j].shape ||
                        markers[i].tint != markers[j].tint,
                        markers[i].id + " " + markers[i].shape + "/" + Quantize(markers[i].tint) + " vs " +
                        markers[j].id + " " + markers[j].shape + "/" + Quantize(markers[j].tint));
            Check("三只妖的形影状态此刻各有归属（几只真图几只回退，报告里要说得出数字）",
                markers.Count == 3,
                "真图 " + encounters.Count(e => !string.IsNullOrEmpty(e.Art) &&
                    Resources.Load<Texture2D>(e.Art) != null) + " 只，回退 " +
                (3 - encounters.Count(e => !string.IsNullOrEmpty(e.Art) &&
                    Resources.Load<Texture2D>(e.Art) != null)) + " 只");
            controller.ShowTown();
        }

        static string Percent(int part, int total) =>
            total == 0 ? "无样本" : (part * 100 / total).ToString();

        // 夹具自己解析一遍 tint（与生产代码各写一遍），比出来的才是"两页同源"而不是"同一个函数自证"。
        static Color? ParseTint(string tint)
        {
            if (string.IsNullOrEmpty(tint)) return null;
            var parts = tint.Split(',');
            if (parts.Length != 3) return null;
            float r, g, b;
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out r) ||
                !float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out g) ||
                !float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out b)) return null;
            return new Color(r, g, b);
        }

        static string Quantize(Color c) =>
            (c.r * 5).ToString("0") + "," + (c.g * 5).ToString("0") + "," + (c.b * 5).ToString("0");

        // 渲染一帧：存图，同时数妖槽位那块矩形里的像素。
        // 为什么要数像素——"回退画得看得出来"这句话，拿我自己加的钩子回答自己（"我把白块关了"）是空的；
        // 判据的独立一侧必须是渲染结果。白块那版在这里的读数是 纯白≈100% / 声明色=0%。
        // 比色按**合成之后**的期望值比：标记色带 .85 alpha（EncounterTint 给的），叠在底纸上屏幕读到的是
        // 混色。实测冥狼声明 (0.85,0.3,0.22) → 屏上 (0.859,0.447,0.38)；拿原值比会整块判成 0%，
        // 那是一条"实现是对的、判据在撒谎"的红——第一次跑就是这么红的。
        static (int white, int tinted, int paper, int total) CaptureAndProbeFoe(string label, Color expect)
        {
            var a = expect.a;
            // expect.a == 0（Color.clear）= 调用方明说"这一维不判"：真图那一档只看纯白与底纸两维。
            var judgeTint = a > 0f;
            var over = new Color(expect.r * a + UiKit.Paper.r * (1f - a),
                expect.g * a + UiKit.Paper.g * (1f - a),
                expect.b * a + UiKit.Paper.b * (1f - a));
            ShowPageContent();
            Canvas.ForceUpdateCanvases();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            camera.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(shotsDir, label + ".png"), tex.EncodeToPNG());

            var slot = controller.CombatFoeSlotRect();
            var white = 0; var tinted = 0; var paper = 0; var total = 0;
            if (!float.IsNaN(slot.xMin))
            {
                var x0 = Mathf.Max(0, Mathf.CeilToInt(slot.xMin));
                var x1 = Mathf.Min(Width - 1, Mathf.FloorToInt(slot.xMax));
                var y0 = Mathf.Max(0, Height - 1 - Mathf.FloorToInt(slot.yMax));
                var y1 = Mathf.Min(Height - 1, Height - 1 - Mathf.CeilToInt(slot.yMin));
                for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                    {
                        var c = tex.GetPixel(x, y);
                        total++;
                        if (c.r > .985f && c.g > .985f && c.b > .985f) white++;
                        else if (Mathf.Abs(c.r - UiKit.Paper.r) < .02f &&
                                 Mathf.Abs(c.g - UiKit.Paper.g) < .02f &&
                                 Mathf.Abs(c.b - UiKit.Paper.b) < .02f) paper++;
                        else if (judgeTint && Mathf.Abs(c.r - over.r) < .12f &&
                                 Mathf.Abs(c.g - over.g) < .12f &&
                                 Mathf.Abs(c.b - over.b) < .12f) tinted++;
                    }
            }
            UnityEngine.Object.Destroy(tex);
            Debug.Log(Tag + "像素探针 " + label + "：纯白 " + Percent(white, total) +
                "% / 底纸 " + Percent(paper, total) + "% / 声明色 " +
                (judgeTint ? Percent(tinted, total) + "%" : "不判") + "（共 " + total + " 个像素）");
            return (white, tinted, paper, total);
        }

        // 把双方都扶满并等回到静立：表现层的每一子用例都要一个干净的开局。
        // 扶玩家这一手是必需的——Setup 期间野猪照打不误，人不扶就会被自己的前置步骤打死，
        // 后面所有断言都变成"验了一场已经结算的战斗"。
        static void RefreshSides()
        {
            for (var guard = 0; guard < 80; guard++)
            {
                if (controller.CombatPlayer != null && !controller.CombatPlayer.IsDead)
                    CombatRules.RestoreHp(controller.CombatPlayer, controller.CombatPlayer.MaxHp);
                if (controller.CombatFoe != null && !controller.CombatFoe.IsDead)
                    CombatRules.RestoreHp(controller.CombatFoe, controller.CombatFoe.MaxHp);
                if (controller.CombatPlayerPhase == "Idle" && controller.CombatFoePhase == "Idle") break;
                controller.CombatClockAdvance(.05f);
            }
            CombatRules.RestoreHp(controller.CombatFoe, controller.CombatFoe.MaxHp);
            CombatRules.RestoreHp(controller.CombatPlayer, controller.CombatPlayer.MaxHp);
            controller.CombatPlaceForCheck(-.4f, .5f);
        }

        // ============================== 工具 ==============================

        static CombatActorState Fighter(string id, int hp, int attack, int defense, int stamina) =>
            new CombatActorState(id, id, hp, attack, defense, stamina);

        static SaveData NewSave(int coins)
        {
            var save = new SaveData { slot = 1, characterId = "farmer", characterName = "甲", coins = coins };
            save.bag = InventoryRules.NewBag();
            return save;
        }

        // 起手 → 推到判定帧 → 结算 → 走完收招；返回结算结果（与 CombatChecks 同一套节奏）。
        static CombatResult Strike(CombatActorState attacker, CombatActorState defender,
            CombatActionType action, float start, PartStrike strike)
        {
            defender.X = attacker.X + CombatRules.StrikeRange * .5f;
            CombatRules.TryAct(attacker, action, start);
            var timing = CombatRules.Timing(action);
            CombatRules.Tick(attacker, start + timing.startup + .001f);
            lastResult = CombatRules.ResolvePartStrike(attacker, defender, start + timing.startup + .002f, strike);
            CombatRules.Tick(attacker, start + timing.startup + timing.active + timing.recovery + .01f);
            CombatRules.Regenerate(attacker, 20f);   // 用例之间把精力补满：这一段验的是结算算术，不是续航
            return lastResult;
        }

        static CombatResult StrikeLegacy(CombatActorState attacker, CombatActorState defender,
            CombatActionType action, float start, float multiplier)
        {
            defender.X = attacker.X + CombatRules.StrikeRange * .5f;
            CombatRules.TryAct(attacker, action, start);
            var timing = CombatRules.Timing(action);
            CombatRules.Tick(attacker, start + timing.startup + .001f);
            lastResult = CombatRules.ResolveStrike(attacker, defender, start + timing.startup + .002f, multiplier);
            CombatRules.Tick(attacker, start + timing.startup + timing.active + timing.recovery + .01f);
            CombatRules.Regenerate(attacker, 20f);   // 用例之间把精力补满：这一段验的是结算算术，不是续航
            return lastResult;
        }

        static string Throws(Action action)
        {
            try { action(); return ""; }
            catch (FormatException ex) { return ex.Message; }
            catch (Exception ex) { return "非 FormatException: " + ex.GetType().Name + " " + ex.Message; }
        }

        static string LockLabelText() => FindText(t => t.text.StartsWith("打击部位"));
        static string GearLabelText() => FindText(t => t.text.Contains("耐久"));

        static string FindText(Func<TMPro.TextMeshProUGUI, bool> match)
        {
            return Resources.FindObjectsOfTypeAll<TMPro.TextMeshProUGUI>()
                .Where(t => t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy && match(t))
                .Select(t => t.text).FirstOrDefault() ?? "";
        }

        static int CountFloaters() => Resources.FindObjectsOfTypeAll<RectTransform>()
            .Count(rect => rect.gameObject.scene.IsValid() && rect.gameObject.activeInHierarchy &&
                rect.gameObject.name == "Floater");

        static float FloaterFontSizeMax() => Resources.FindObjectsOfTypeAll<TMPro.TextMeshProUGUI>()
            .Where(t => t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy && t.gameObject.name == "Floater")
            .Select(t => (float)t.fontSize).DefaultIfEmpty(0f).Max();

        static void SetupCapture()
        {
            camera = Camera.main;
            Check("主相机存在", camera != null);
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            Check("Canvas 存在", canvas != null);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.Render();
            Canvas.ForceUpdateCanvases();
        }

        // 批处理里整个 Run 同帧跑完：页面渐入的 CanvasGroup 没等到真实帧，alpha 还停在 0。
        // 截图是验画面不是验渐入动画，先把当前页拉满再拍。
        static void ShowPageContent()
        {
            foreach (var group in Resources.FindObjectsOfTypeAll<CanvasGroup>())
                if (group.gameObject.scene.IsValid() && group.gameObject.name.StartsWith("Page "))
                    group.alpha = 1f;
        }

        static void Capture(string label)
        {
            ShowPageContent();
            Canvas.ForceUpdateCanvases();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            camera.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            var path = Path.Combine(shotsDir, label + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            Debug.Log(Tag + "captured " + path);
        }

        static Button FindActiveButton(string text) => Resources.FindObjectsOfTypeAll<Button>()
            .FirstOrDefault(button => button.gameObject.scene.IsValid() && button.gameObject.activeInHierarchy &&
                button.gameObject.name == "Button " + text);

        // 当前可见按钮的两两相交对（世界坐标 AABB）。
        // 只比按钮、不比文字：文字叠字靠看图，按钮叠按钮是会点错的问题，必须机器拦下来。
        static List<string> OverlappingButtonPairs()
        {
            var found = new List<string>();
            var buttons = Resources.FindObjectsOfTypeAll<Button>()
                .Where(b => b.gameObject.scene.IsValid() && b.gameObject.activeInHierarchy)
                .Select(b => (name: b.gameObject.name.Replace("Button ", ""), rect: WorldRect(b.transform as RectTransform)))
                .ToList();
            for (var i = 0; i < buttons.Count; i++)
                for (var j = i + 1; j < buttons.Count; j++)
                    if (buttons[i].rect.xMin < buttons[j].rect.xMax - 1f &&
                        buttons[j].rect.xMin < buttons[i].rect.xMax - 1f &&
                        buttons[i].rect.yMin < buttons[j].rect.yMax - 1f &&
                        buttons[j].rect.yMin < buttons[i].rect.yMax - 1f)
                        found.Add(buttons[i].name + "×" + buttons[j].name);
            return found;
        }

        static Rect WorldRect(RectTransform r)
        {
            var a = r.TransformPoint(r.rect.min);
            var b = r.TransformPoint(r.rect.max);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static int ActiveObjects(string name) => Resources.FindObjectsOfTypeAll<RectTransform>()
            .Count(rect => rect.gameObject.scene.IsValid() && rect.gameObject.activeInHierarchy &&
                rect.gameObject.name == name);

        static bool AnyText(string needle) => Resources.FindObjectsOfTypeAll<TMPro.TextMeshProUGUI>()
            .Any(t => t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy && t.text.Contains(needle));

        static int FreeSlot()
        {
            foreach (var slot in controller.Repository.ReadAll())
                if (slot.State == SlotState.Empty) return slot.Slot;
            return 0;
        }

        static void Check(string name, bool condition, string detail = null)
        {
            var full = string.IsNullOrEmpty(detail) ? name : name + " · " + detail;
            if (condition)
            {
                okLines.Add(full);
                if (inViewSuite) viewChecks++; else logicChecks++;
                Debug.Log(Tag + "ok: " + full);
            }
            else
            {
                problems.Add("断言失败: " + full);
                Debug.LogError(Tag + "FAIL: " + full);
            }
        }

        static void Finish()
        {
            SessionState.SetBool(SessionKey, false);
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Pump;
            if (slotUsed > 0) controller?.Repository.Delete(slotUsed);
            var scratch = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            try { if (!string.IsNullOrEmpty(scratch) && Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (Exception ex) { Debug.LogWarning("无法清理验收临时目录：" + ex.Message); }
            if (target != null && camera != null) { camera.targetTexture = null; target.Release(); }
            Debug.Log(Tag + "断言计数：A 段 " + logicChecks + " 项 / B 段 " + viewChecks + " 项 / 合计 " +
                okLines.Count + " 项通过");
            // T105：翻倍的真根因在 OnLog（失败播报回调回自己，一条失败记两条），已在源头拦掉；
            // 收尾这里不再逐条 LogError（那会再走一遍回调），只出一行汇总 + 去重后的条数。
            // 判据可比性优先：若将来又出现同一条被记两遍，"去重后"那半句会当场把它暴露出来，
            // 而不是让数字悄悄翻倍。
            var distinct = problems.Distinct().Count();
            Debug.Log(Tag + "suite finished, " + problems.Count + " problem(s)" +
                (distinct == problems.Count ? "" : "（去重后 " + distinct + " 条）"));
            if (problems.Count > 0)
                Debug.LogWarning(Tag + "问题清单（每条已在上面就地报过一次，这里只汇总，不再重复计）：\n  - " +
                    string.Join("\n  - ", problems.Distinct()));
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
        }

        static void Fail(string message)
        {
            SessionState.SetBool(SessionKey, false);
            Debug.LogError(message);
            EditorApplication.update -= Pump;
            // T105 的可比性补一句：崩溃的运行原来连 `suite finished` 都不出，"日志里没这行"会被
            // 读成"没跑"或"0 条"。中断必须另出一行、措辞与判据行不同，既不冒充判据也不留空白。
            Debug.LogWarning(Tag + "aborted after " + problems.Count +
                " problem(s)（中途崩溃，未跑完，这行不是判据行）");
            var scratch = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            try { if (!string.IsNullOrEmpty(scratch) && Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch { /* 尽力清理 */ }
            EditorApplication.Exit(1);
        }
    }
}
