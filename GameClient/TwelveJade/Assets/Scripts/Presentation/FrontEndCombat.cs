using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // M4-03 战斗前台：把 Core/Combat.cs 的规则接到可试玩的战斗页。
    // 表现层只做输入、动画与显示——伤害、精力、硬直、死亡与奖励全部调 Core；
    // 时间走可注入的战斗时钟：帧循环喂 unscaledDeltaTime，验收脚本用 CombatClockAdvance 直接推进。
    // 玩家战斗属性是占位（正式属性随养成系统进 Core/存档，见 09 日志技术债 T017）。
    public sealed partial class FrontEndController
    {
        const float StagePxPerUnit = 260f;   // 战斗场地：1 单位 = 260px（StrikeRange 1.25 ≈ 325px）
        const float StageCenterX = 960f;
        const float StageGroundY = 780f;
        const float PlayerSpeed = .4f;       // 单位/秒
        const float StageMinX = -1.5f, StageMaxX = 1.5f;

        CombatActorState combatPlayer, combatFoe;
        EnemyDef combatFoeDef;
        string combatEncounterId = "";
        float combatNow, foeNextActAt, foeFlashUntil;
        bool combatSettled;
        SpoilsResult combatSpoils;
        PuppetActor combatPlayerActor;
        RectTransform combatFoeRoot, combatSettlePanel;
        RawImage combatFoeImage;
        TMP_Text combatPlayerState, combatFoeState, combatPlayerHpText, combatFoeHpText;
        Image combatPlayerHpFill, combatPlayerStaminaFill, combatFoeHpFill;
        readonly List<(RectTransform rect, TMP_Text text, float born)> combatFloaters = new();

        public float CombatNow => combatNow;
        public CombatActorState CombatPlayer => combatPlayer;
        public CombatActorState CombatFoe => combatFoe;
        public bool CombatSettled => combatSettled;
        public SpoilsResult LastSpoils => combatSpoils;
        public string CombatPlayerPhase => combatPlayer?.Phase.ToString() ?? "";
        public string CombatFoePhase => combatFoe?.Phase.ToString() ?? "";

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

        // 玩家战斗属性占位：出身只给一点风味差异，正式数值随养成系统落 Core/存档（T017）。
        static (int hp, int attack, int defense, int stamina) PlayerStatsFor(string characterId) =>
            characterId == "traveller" ? (55, 7, 2, 60) :
            characterId == "merchant" ? (50, 6, 2, 70) : (60, 8, 1, 60);

        public void ShowCombat(string encounterId = "encounter-boar-01")
        {
            if (activeSave == null) { Notify("先落笔创建一位行旅人，再战。"); return; }
            EnemyTable enemies;
            try { enemies = CombatEnemies; }
            catch (InvalidOperationException ex) { Notify(ex.Message); return; }
            var foeDef = enemies.Find("boar-demon");
            if (foeDef == null) { Notify("妖兽表里没有野猪妖。"); return; }

            combatEncounterId = encounterId ?? "";
            combatFoeDef = foeDef;
            combatNow = 0f;
            foeNextActAt = 1.2f;
            combatSettled = false;
            combatSpoils = null;
            combatFloaters.Clear();
            CloseInventory();
            BeginPage("combat");

            var stats = PlayerStatsFor(activeSave.characterId);
            combatPlayer = new CombatActorState("player", activeSave.characterName, stats.hp, stats.attack, stats.defense, stats.stamina);
            combatFoe = new CombatActorState(foeDef.Id, foeDef.Name, foeDef.MaxHp, foeDef.Attack, foeDef.Defense, foeDef.MaxStamina);
            combatPlayer.X = -1.2f;
            combatFoe.X = 1.2f;

            ui.Panel(content, "Combat veil", 0, 0, 1920, 1080, new Color(.91f, .88f, .79f, .98f));
            ui.Label(content, foeDef.Name, 100, 44, 700, 60, 42, UiKit.Ink, TextAlignmentOptions.TopLeft, true);
            ui.Label(content, "A/D 移动 · J 轻击 · K 重击 · L 闪避 · Esc 撤离", 104, 114, 900, 30, 20,
                new Color(.24f, .28f, .26f, .8f));
            ui.Button(content, "撤离", 1620, 44, 200, 54, ShowTown, true);

            // 地面：一条淡墨横带当战场底线。
            ui.Panel(content, "Stage ground", 460, StageGroundY - 3, 1000, 6, new Color(.2f, .24f, .22f, .18f));

            // 玩家：分层纸偶侧身位。
            var preset = System.Array.Find(presets, p => p.id == activeSave.characterId) ?? presets[0];
            // 正面图：六关节切线按正面标定，侧身图会被切散。
            var view = preset.Facing(0, activeSave.gender, activeSave.faceStyle);
            var bodyHeight = 230f;
            combatPlayerActor = PuppetActor.Create(content, view,
                new Vector2(StageX(combatPlayer.X), -StageGroundY), new Vector2(bodyHeight * .593f, bodyHeight));

            // 妖兽：抠底立绘，镜像面向玩家。
            var foeTexture = Resources.Load<Texture2D>("Art/NPC/" + foeDef.Id);
            combatFoeRoot = new GameObject("Foe " + foeDef.Id, typeof(RectTransform)).GetComponent<RectTransform>();
            combatFoeRoot.SetParent(content, false);
            combatFoeRoot.anchorMin = combatFoeRoot.anchorMax = new Vector2(0, 1);
            combatFoeRoot.pivot = new Vector2(.5f, 0f);
            combatFoeRoot.sizeDelta = new Vector2(250f, 250f);
            combatFoeImage = combatFoeRoot.gameObject.AddComponent<RawImage>();
            combatFoeImage.texture = foeTexture != null ? foeTexture : Texture2D.whiteTexture;
            combatFoeImage.uvRect = new Rect(1f, 0f, -1f, 1f);
            combatFoeImage.raycastTarget = false;

            BuildCombatBars();
            FocusFirst();
        }

        void BuildCombatBars()
        {
            // 玩家：生命 + 精力。
            ui.Label(content, combatPlayer.Name, 100, 170, 320, 34, 24, UiKit.Ink, TextAlignmentOptions.TopLeft, true);
            combatPlayerHpFill = CombatBar(100, 208, UiKit.Jade, out combatPlayerHpText);
            combatPlayerStaminaFill = CombatBar(100, 258, UiKit.Gold, out _);
            combatPlayerState = ui.Label(content, "静立", 100, 292, 320, 30, 19, new Color(.24f, .28f, .26f, .85f));

            // 妖兽：生命。
            ui.Label(content, combatFoe.Name, 1500, 170, 320, 34, 24, UiKit.Ink, TextAlignmentOptions.TopRight, true);
            combatFoeHpFill = CombatBar(1520, 208, new Color(.66f, .2f, .16f, .95f), out combatFoeHpText);
            combatFoeState = ui.Label(content, "静立", 1500, 240, 320, 30, 19, new Color(.24f, .28f, .26f, .85f),
                TextAlignmentOptions.TopRight);

            // 动作按钮：键盘之外给鼠标一条路。
            ui.Button(content, "左 移", 470, 950, 170, 58, () => NudgePlayer(-1f));
            ui.Button(content, "右 移", 660, 950, 170, 58, () => NudgePlayer(1f));
            ui.Button(content, "轻 击", 880, 950, 170, 58, () => CombatRules.TryAct(combatPlayer, CombatActionType.Light, combatNow), true);
            ui.Button(content, "重 击", 1070, 950, 170, 58, () => CombatRules.TryAct(combatPlayer, CombatActionType.Heavy, combatNow));
            ui.Button(content, "闪 避", 1260, 950, 170, 58, () => CombatRules.TryAct(combatPlayer, CombatActionType.Dodge, combatNow));
        }

        Image CombatBar(float x, float y, Color color, out TMP_Text label)
        {
            var back = ui.Panel(content, "Bar back", x, y, 300, 16, new Color(.2f, .24f, .22f, .25f));
            var fillRect = ui.Rect(back.transform, "Fill", 2, 2, 296, 12);
            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = color;
            fill.raycastTarget = false;
            fillRect.anchorMin = fillRect.anchorMax = new Vector2(0, .5f);
            fillRect.pivot = new Vector2(0, .5f);
            fillRect.anchoredPosition = Vector2.zero;
            // 数字放血条下方：上方留给名字，避免两行文字打架。
            label = ui.Label(back.transform, "", 0, 22, 300, 26, 18, UiKit.Ink);
            return fill;
        }

        // 战斗钟：帧循环喂真实时长，验收脚本用 CombatClockAdvance 按需推进（可注入、可暂停）。
        // 内部切成 ≤0.03s 的小步：一次掉帧 0.5s 会整段跨过 0.08s 的命中窗，让这一击凭空挥空。
        public void CombatClockAdvance(float seconds)
        {
            var left = Mathf.Max(0f, seconds);
            var guard = 0;
            while (left > 0f && guard++ < 4000)
            {
                var step = Mathf.Min(left, .03f);
                UpdateCombat(step);
                left -= step;
            }
        }

        // 鼠标党的一次挪步：与按住 A/D 同一套速度。
        void NudgePlayer(float direction) => MovePlayer(direction * .18f);

        // 验收钩子：把双方摆到指定站位（Core 坐标），与 SetCoinsForCheck 同一套约定。
        public void CombatPlaceForCheck(float playerX, float foeX)
        {
            if (combatPlayer == null || combatFoe == null) return;
            combatPlayer.X = playerX;
            combatFoe.X = foeX;
        }

        void MovePlayer(float distance)
        {
            if (combatPlayer == null || combatPlayer.IsDead || combatSettled) return;
            combatPlayer.X = Mathf.Clamp(combatPlayer.X + distance, StageMinX, StageMaxX);
        }

        void UpdateCombat(float dt)
        {
            if (page != "combat" || combatPlayer == null || combatFoe == null) return;
            combatNow += dt;
            // dt 已由 CombatClockAdvance 切成小步；这里假定一步不会跨过最短的命中窗。

            ReadCombatInput(dt);
            RunFoeAi(dt);
            CombatRules.Tick(combatPlayer, combatNow);
            CombatRules.Tick(combatFoe, combatNow);

            // 短动作：只播表现，Core 该不该动、动了多少血都不归这里管。
            if (combatPlayerActor != null)
            {
                if (combatPlayer.Phase == CombatPhase.Startup)
                    combatPlayerActor.PlayAction(
                        combatPlayer.CurrentAction == CombatActionType.Heavy ? PuppetActor.Action.Heavy : PuppetActor.Action.Light,
                        Time.unscaledTime, true);
                else if (combatPlayer.CurrentAction == CombatActionType.Dodge &&
                         (combatPlayer.Phase == CombatPhase.Startup || combatPlayer.Phase == CombatPhase.Active))
                    combatPlayerActor.PlayAction(PuppetActor.Action.Dodge, Time.unscaledTime, true);
                else if (combatPlayer.Phase == CombatPhase.Hitstun)
                    combatPlayerActor.PlayAction(PuppetActor.Action.Flinch, Time.unscaledTime, true);
            }

            // 命中结算：命中帧内每帧尝试，Core 的"一次起手只落一击"挡重复扣血。
            StrikeIfActive(combatPlayer, combatFoe,
                combatPlayer.CurrentAction == CombatActionType.Heavy
                    ? combatFoeDef.MultiplierOf("head") : combatFoeDef.MultiplierOf("body"));
            StrikeIfActive(combatFoe, combatPlayer, 1f);
            CombatRules.Regenerate(combatPlayer, dt);

            if (!combatSettled)
            {
                if (combatFoe.IsDead) SettleVictory();
                else if (combatPlayer.IsDead) SettleDefeat();
            }
            RefreshCombatView();
        }

        void ReadCombatInput(float dt)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            var move = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move += 1f;
            if (move != 0f) MovePlayer(move * PlayerSpeed * dt);
            if (combatPlayer.IsDead || combatSettled) return;
            if (keyboard.jKey.wasPressedThisFrame) CombatRules.TryAct(combatPlayer, CombatActionType.Light, combatNow);
            if (keyboard.kKey.wasPressedThisFrame) CombatRules.TryAct(combatPlayer, CombatActionType.Heavy, combatNow);
            if (keyboard.lKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
                CombatRules.TryAct(combatPlayer, CombatActionType.Dodge, combatNow);
        }

        // 野猪 AI（最小可玩）：不在命中距离就逼近，进了距离按固定节奏出轻击。
        void RunFoeAi(float dt)
        {
            if (combatFoe.IsDead || combatPlayer.IsDead || combatSettled) return;
            if (combatFoe.Phase != CombatPhase.Idle) return;
            var dx = combatPlayer.X - combatFoe.X;
            var speed = combatFoeDef.MoveSpeed / StagePxPerUnit;
            if (Mathf.Abs(dx) > CombatRules.StrikeRange * .8f)
                combatFoe.X += Mathf.Sign(dx) * Mathf.Min(Mathf.Abs(dx), speed * dt);
            else if (combatNow >= foeNextActAt &&
                     CombatRules.TryAct(combatFoe, CombatActionType.Light, combatNow).Executed)
                foeNextActAt = combatNow + 1.5f;
        }

        void StrikeIfActive(CombatActorState attacker, CombatActorState defender, float partMultiplier)
        {
            if (attacker.Phase != CombatPhase.Active ||
                (attacker.CurrentAction != CombatActionType.Light && attacker.CurrentAction != CombatActionType.Heavy)) return;
            var result = CombatRules.ResolveStrike(attacker, defender, combatNow, partMultiplier);
            if (result.Hit)
            {
                if (defender == combatFoe) foeFlashUntil = combatNow + .12f;
                SpawnFloater(defender, "-" + result.Damage, attacker == combatPlayer);
            }
            else if (defender == combatPlayer && result.Reason.Contains("闪"))
                SpawnFloater(defender, "闪！", false);
        }

        void SettleVictory()
        {
            combatSettled = true;
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
            PersistBag();
            combatSettlePanel = ui.Panel(content, "Combat settle", 560, 340, 800, 380,
                new Color(.045f, .115f, .10f, .97f), true).rectTransform;
            ui.Label(combatSettlePanel, combatFoeDef.Name + " 倒下了", 44, 34, 712, 60, 34, UiKit.Paper,
                TextAlignmentOptions.TopLeft, true);
            ui.Label(combatSettlePanel, firstKill
                    ? "拾获：" + combatSpoils.Describe(Table)
                    : "这头畜生是冲着同伙的气味来的——身上没什么可剥的了。",
                44, 116, 712, 110, 24, UiKit.Gold);
            ui.Label(combatSettlePanel, "山风把腥气压下去之前，先回镇上歇口气。", 44, 210, 712, 44, 20, UiKit.Muted);
            ui.Button(combatSettlePanel, "返回青石镇", 226, 272, 348, 62, ShowTown, true);
            FocusFirst();
        }

        void SettleDefeat()
        {
            combatSettled = true;
            combatSettlePanel = ui.Panel(content, "Combat settle", 560, 340, 800, 380,
                new Color(.045f, .115f, .10f, .97f), true).rectTransform;
            ui.Label(combatSettlePanel, "眼前一黑……", 44, 34, 712, 60, 34, UiKit.Paper,
                TextAlignmentOptions.TopLeft, true);
            ui.Label(combatSettlePanel, "再睁眼时，是村民把你抬回了镇口。行囊还在，命也还在。", 44, 116, 712, 90, 24, UiKit.Muted);
            ui.Button(combatSettlePanel, "返回青石镇", 226, 272, 348, 62, ShowTown, true);
            FocusFirst();
        }

        float StageX(float worldX) => StageCenterX + worldX * StagePxPerUnit;

        static string PhaseName(CombatPhase phase) => phase switch
        {
            CombatPhase.Idle => "静立",
            CombatPhase.Startup => "前摇",
            CombatPhase.Active => "命中帧",
            CombatPhase.Recovery => "收招",
            CombatPhase.Hitstun => "硬直",
            _ => "倒下",
        };

        void RefreshCombatView()
        {
            if (combatPlayerActor == null || combatFoeRoot == null) return;
            // 玩家：命中帧向对手突进半步，受击小幅后退——经 SetPosition 走，纸偶的 Update 不会再覆盖。
            var playerOffset = 0f;
            if (combatPlayer.Phase == CombatPhase.Active) playerOffset = LungeOffset(combatPlayer) * 40f;
            else if (combatPlayer.Phase == CombatPhase.Hitstun) playerOffset = -14f;
            combatPlayerActor.SetPosition(new Vector2(StageX(combatPlayer.X) + playerOffset, -StageGroundY));

            // 妖兽：突进 + 受击红闪 + 倒下淡出。
            var foeOffset = 0f;
            if (combatFoe.Phase == CombatPhase.Active) foeOffset = -LungeOffset(combatFoe) * 46f;
            else if (combatFoe.Phase == CombatPhase.Hitstun) foeOffset = 14f;
            combatFoeRoot.anchoredPosition = new Vector2(StageX(combatFoe.X) + foeOffset, -StageGroundY);
            if (combatFoe.IsDead)
                combatFoeImage.color = new Color(.75f, .75f, .75f, .35f);
            else if (combatFoe.Phase == CombatPhase.Hitstun || combatNow < foeFlashUntil)
                combatFoeImage.color = new Color(1f, .55f, .5f, 1f);
            else
                combatFoeImage.color = Color.white;

            // 血条与状态字。
            combatPlayerHpFill.rectTransform.sizeDelta =
                new Vector2(296f * Mathf.Clamp01((float)combatPlayer.Hp / combatPlayer.MaxHp), 12);
            combatPlayerStaminaFill.rectTransform.sizeDelta =
                new Vector2(296f * Mathf.Clamp01((float)combatPlayer.Stamina / combatPlayer.MaxStamina), 12);
            combatFoeHpFill.rectTransform.sizeDelta =
                new Vector2(296f * Mathf.Clamp01((float)combatFoe.Hp / combatFoe.MaxHp), 12);
            combatPlayerHpText.text = combatPlayer.Hp + " / " + combatPlayer.MaxHp;
            combatFoeHpText.text = combatFoe.Hp + " / " + combatFoe.MaxHp;
            combatPlayerState.text = PhaseName(combatPlayer.Phase) + " · 精力 " + combatPlayer.Stamina;
            combatFoeState.text = PhaseName(combatFoe.Phase);

            // 伤害浮字。
            for (var i = combatFloaters.Count - 1; i >= 0; i--)
            {
                var (rect, text, born) = combatFloaters[i];
                var t = (combatNow - born) / .7f;
                if (t >= 1f) { Destroy(rect.gameObject); combatFloaters.RemoveAt(i); continue; }
                rect.anchoredPosition += new Vector2(0f, 46f * Time.unscaledDeltaTime);
                text.alpha = 1f - t;
            }
        }

        // 命中帧内的突进包络：进半步、退回来，正弦滑出。进度由 Core 的 ActiveProgress 给出。
        float LungeOffset(CombatActorState actor) =>
            Mathf.Sin(CombatRules.ActiveProgress(actor, combatNow) * Mathf.PI);

        void SpawnFloater(CombatActorState actor, string message, bool dealt)
        {
            var rect = ui.Rect(content, "Floater", StageX(actor.X) - 40f, StageGroundY - 300f, 80f, 40f);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = brushFont;
            text.text = message;
            text.fontSize = 34;
            text.color = dealt ? UiKit.Ink : new Color(.66f, .2f, .16f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            combatFloaters.Add((rect, text, combatNow));
        }
    }
}
