using System;
using System.Collections.Generic;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Core
{
    // M4-01 战斗规则测试：伤害算术、精力门槛、闪避无敌窗、硬直封动作、死亡终止、
    // 重复结算不重复扣血、极端属性不溢出。全部用固定数值推进时间，不碰机器时钟。
    public static class CombatChecks
    {
        public static void Run()
        {
            // 伤害算术：轻击 (10+10)-2=18，重击 (10+22)-2=30。
            var hero = NewFighter(hp: 100, attack: 10, defense: 4, stamina: 100);
            var boar = NewFighter(id: "boar", hp: 100, attack: 9, defense: 2, stamina: 60);
            Check("light damage follows the formula", StrikeFor(hero, boar, CombatActionType.Light, 0f, out var r1) &&
                r1.Damage == 18 && r1.DefenderHp == 82, r1.Reason);
            Check("heavy damage follows the formula", StrikeFor(hero, boar, CombatActionType.Heavy, 1f, out var r2) &&
                r2.Damage == 30 && r2.DefenderHp == 52, r2.Reason);

            // 部位倍率：头 1.5 倍，四舍五入 (10+10)*1.5=30，再减防御。
            Check("part multiplier scales damage",
                StrikeFor(hero, boar, CombatActionType.Light, 2f, out var r3, partMultiplier: 1.5f) &&
                r3.Damage == 28, r3.Reason);

            // 最小伤害：防御高过攻击也至少打 1，不出现负数。
            var tank = NewFighter(id: "tank", hp: 100, attack: 0, defense: 99999, stamina: 10);
            Check("minimum damage is one", StrikeFor(hero, tank, CombatActionType.Light, 3f, out var r4) &&
                r4.Damage == 1 && r4.DefenderHp == 99, r4.Reason);

            // 精力门槛：不够就拒，一毫不扣。
            var tired = NewFighter(id: "tired", hp: 50, attack: 5, defense: 0, stamina: 10);
            var heavy = CombatRules.TryAct(tired, CombatActionType.Heavy, 0f);
            Check("stamina gates the heavy attack", !heavy.Executed && heavy.Reason.Contains("精力") &&
                tired.Stamina == 10);
            Check("light still affordable", CombatRules.TryAct(tired, CombatActionType.Light, 0f).Executed &&
                tired.Stamina == 2);

            // 状态机：起手/命中/收招各有窗口，收招前不能再次起手，命中帧只落一击。
            var duelist = NewFighter(id: "duelist", hp: 60, attack: 8, defense: 1, stamina: 100);
            var straw = NewFighter(id: "straw", hp: 200, attack: 0, defense: 0, stamina: 100);
            straw.X = 1f;
            CombatRules.TryAct(duelist, CombatActionType.Light, 10f);
            Check("startup blocks another action",
                !CombatRules.TryAct(duelist, CombatActionType.Light, 10.05f).Executed);
            Check("no strike during startup",
                !CombatRules.ResolveStrike(duelist, straw, 10.05f).Hit);
            CombatRules.Tick(duelist, 10.12f);
            Check("active phase arrives", duelist.Phase == CombatPhase.Active);
            Check("strike lands during active", CombatRules.ResolveStrike(duelist, straw, 10.13f).Hit);
            Check("one strike per action",
                !CombatRules.ResolveStrike(duelist, straw, 10.14f).Hit);
            CombatRules.Tick(duelist, 10.20f);
            Check("recovery follows active", duelist.Phase == CombatPhase.Recovery);
            CombatRules.Tick(duelist, 10.38f);
            Check("back to idle after recovery", duelist.Phase == CombatPhase.Idle);

            // 距离：够不着就是落空，不扣血。
            straw.X = 5f;
            CombatRules.TryAct(duelist, CombatActionType.Light, 11f);
            CombatRules.Tick(duelist, 11.12f);
            var far = CombatRules.ResolveStrike(duelist, straw, 11.13f);
            Check("out of range whiffs", !far.Hit && far.Reason.Contains("够不着") && straw.Hp == 182);

            // 闪避无敌窗：Active 帧内打不中，出了窗口照打。
            var dancer = NewFighter(id: "dancer", hp: 40, attack: 5, defense: 0, stamina: 100);
            var bully = NewFighter(id: "bully", hp: 60, attack: 12, defense: 0, stamina: 100);
            dancer.X = 0f; bully.X = 0.5f;
            CombatRules.TryAct(dancer, CombatActionType.Dodge, 20f);
            CombatRules.Tick(dancer, 20.05f);
            CombatRules.TryAct(bully, CombatActionType.Light, 20.10f);
            CombatRules.Tick(bully, 20.22f);
            var evaded = CombatRules.ResolveStrike(bully, dancer, 20.25f);
            Check("dodge is invulnerable in its window",
                dancer.Invulnerable && !evaded.Hit && evaded.Reason.Contains("闪"), evaded.Reason);
            CombatRules.Tick(dancer, 20.28f);
            Check("dodge window closes", !dancer.Invulnerable);
            // 攻击方这一击已经落过，走完收招再补一刀。
            CombatRules.Tick(bully, 20.49f);
            CombatRules.TryAct(bully, CombatActionType.Light, 20.50f);
            CombatRules.Tick(bully, 20.62f);
            var caught = CombatRules.ResolveStrike(bully, dancer, 20.65f);
            Check("after the window the hit lands", caught.Hit && dancer.Hp == 18, caught.Reason);

            // 硬直：受击打断当前动作，硬直结束前什么都不能做。
            var stunned = NewFighter(id: "stunned", hp: 60, attack: 8, defense: 0, stamina: 100);
            var other = NewFighter(id: "other", hp: 60, attack: 8, defense: 0, stamina: 100);
            stunned.X = other.X = 0f;
            CombatRules.TryAct(stunned, CombatActionType.Heavy, 30f);
            CombatRules.TryAct(other, CombatActionType.Light, 30f);
            CombatRules.Tick(other, 30.12f);
            CombatRules.ResolveStrike(other, stunned, 30.13f);
            Check("hitstun interrupts the victim", stunned.Phase == CombatPhase.Hitstun);
            Check("hitstun blocks actions",
                !CombatRules.TryAct(stunned, CombatActionType.Light, 30.2f).Executed);
            CombatRules.Tick(stunned, 30.43f);
            Check("hitstun ends", stunned.Phase == CombatPhase.Idle &&
                CombatRules.TryAct(stunned, CombatActionType.Light, 30.44f).Executed);

            // 死亡终止：打空血进入 Dead，不再扣血，也不能再动作。
            var doomed = NewFighter(id: "doomed", hp: 5, attack: 0, defense: 0, stamina: 10);
            var killer = NewFighter(id: "killer", hp: 60, attack: 8, defense: 0, stamina: 100);
            doomed.X = killer.X = 0f;
            CombatRules.TryAct(killer, CombatActionType.Light, 40f);
            CombatRules.Tick(killer, 40.12f);
            var lethal = CombatRules.ResolveStrike(killer, doomed, 40.13f);
            Check("lethal strike clamps to remaining hp", lethal.Hit && lethal.Damage == 5 &&
                doomed.Hp == 0 && doomed.IsDead);
            Check("a dead target takes no further damage",
                !CombatRules.ResolveStrike(killer, doomed, 40.14f).Hit && doomed.Hp == 0);
            Check("the dead cannot act",
                !CombatRules.TryAct(doomed, CombatActionType.Light, 40.15f).Executed);

            // 精力回复：只在 Idle 回，满则停，前摇/硬直期间不回。
            //
            // 【战斗口径：精力是"单位自己的节拍器"，不是玩家可打的资源】（D-47 裁决，T109 定性收口）
            // 全仓只有两处写精力：`Combat.cs:261 actor.Stamina -= cost`（出手者扣自己）与
            // `Combat.cs:370 回复取 min 封顶`。`ResolveStrike` 只扣气血，**没有任何代码扣对手的精力**。
            // ⇒ "把对手的精力打空"这条杠杆在模型里不存在：玩家再怎么能打，也碰不到对方的精力条；
            //   一个单位抬不抬得起下一招，只取决于"它自己的招式消耗 vs 它自己的回率"。
            // 敌我读同一份 `CombatDepth.Standard20` 是刻意设计（T072/T081 修的就是"妖没挂档案"），
            // 不要为了"给玩家一根杠杆"把它拆成两档——那会造出第二个真源（T045/T048 反复告诫的病）。
            // ⇒ 将来若真要引入"受击扣精力"，那是新机制：**先重开一条 D 编号**，别顺手改这里的常量。
            // 玩家侧的出口文案在 `FrontEndCombat.StaminaHintCopy`，由 CombatEngineShot 逐字钉住。
            //
            // 【默认值护栏：未显式挂档案即红】（D-28 / F-5.1，N4-Polish 改写注释）
            // 下面四处期望钉的是 `CombatRules.StaminaRegenPerSecond = 6f` 这条**基线**。
            // 它的角色已经变了：M/N 两轮之前它是"手感护栏"（玩家真的按 6/秒 回），
            // 现在真页面里敌我双方每场都被挂上 CombatDepth.Standard20（25/秒），6/秒 在运行期已无使用者，
            // 于是这四处退化成"默认值护栏"——它们守的是**那个兜底值本身**，用来证明
            // "谁忘了挂档案就会掉到这里"。真正的防线在战斗夹具：D-28 已裁成
            // "任何进入战斗的单位（敌我皆然）Tuning 为空即断言失败"（CombatEngineShot 的 A/B 两段各一条）。
            // 所以：改这四个数字 = 改共享基线，必须单独一笔并过架构师；别顺手把它们改成 25。
            var weary = NewFighter(id: "weary", hp: 40, attack: 5, defense: 0, stamina: 60);
            CombatRules.TryAct(weary, CombatActionType.Heavy, 49f);
            CombatRules.Tick(weary, 49.73f);
            Check("heavy drains and returns to idle", weary.Stamina == 40 && weary.Phase == CombatPhase.Idle);
            CombatRules.Regenerate(weary, 2f);
            // 默认值护栏①：2 秒 × 6/秒 = +12（挂档案的单位这里会是 +50，见 D-28 那条红）
            Check("stamina regenerates while idle", weary.Stamina == 52);
            CombatRules.TryAct(weary, CombatActionType.Dodge, 50f);
            CombatRules.Regenerate(weary, 2f);
            // 默认值护栏②：忙时一律不回——这条与回率无关，是相位闸门
            Check("no regen while busy", weary.Stamina == 40);
            CombatRules.Tick(weary, 50.05f);
            CombatRules.Tick(weary, 50.27f);
            CombatRules.Tick(weary, 50.39f);
            CombatRules.Regenerate(weary, 3f);
            // 默认值护栏③：回到 Idle 之后 3 秒 × 6/秒 = +18
            Check("regen resumes after returning to idle", weary.Stamina == 58);
            CombatRules.Regenerate(weary, 999f);
            Check("regen caps at the maximum", weary.Stamina == weary.MaxStamina);
            var tiny = NewFighter(id: "tiny", hp: 10, attack: 1, defense: 0, stamina: 10);
            CombatRules.TryAct(tiny, CombatActionType.Light, 80f);
            CombatRules.Tick(tiny, 80.39f);
            CombatRules.Regenerate(tiny, 0.01f);
            Check("sub-one-point regen accumulates instead of vanishing", tiny.Stamina == 2);
            CombatRules.Regenerate(tiny, 0.2f);
            // 默认值护栏④：攒够 1 点才落地（0.2 秒 × 6/秒 = 1.2 → +1，不足 1 点不凭空消失）
            Check("accumulated regen pays out", tiny.Stamina == 3);

            // 极端属性：int.MaxValue 也会被压进护栏，算术不溢出、不出负数。
            var extreme = new CombatActorState("extreme", "极限", int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
            var sandbag = new CombatActorState("sandbag", "沙包", int.MaxValue, 0, int.MaxValue, int.MaxValue);
            extreme.X = sandbag.X = 0f;
            Check("extreme stats are clamped", extreme.Attack == 99999 && extreme.Hp == 99999);
            CombatRules.TryAct(extreme, CombatActionType.Light, 60f);
            CombatRules.Tick(extreme, 60.12f);
            var absurd = CombatRules.ResolveStrike(extreme, sandbag, 60.13f);
            Check("extreme strike stays in range", absurd.Hit && absurd.Damage == 10 &&
                sandbag.Hp == 99989);

            // 等待：不花精力、不进状态机，纯粹"这一拍不动"。
            var patient = NewFighter(id: "patient", hp: 30, attack: 5, defense: 0, stamina: 50);
            var waited = CombatRules.TryAct(patient, CombatActionType.Wait, 70f);
            Check("wait costs nothing", waited.Executed && patient.Stamina == 50 &&
                patient.Phase == CombatPhase.Idle);
        }

        // 起手 → 推进到命中帧 → 结算 → 走完收招回到 Idle；返回结算结果。
        // Tick 只在调用时推进状态机，所以收招也要显式走完，下一个用例才拿得到 Idle。
        static bool StrikeFor(CombatActorState attacker, CombatActorState defender,
            CombatActionType action, float start, out CombatResult result, float partMultiplier = 1f)
        {
            defender.X = attacker.X = 0f;
            if (!CombatRules.TryAct(attacker, action, start).Executed)
            {
                result = new CombatResult { Reason = "起手失败" };
                return false;
            }
            var timing = CombatRules.Timing(action);
            CombatRules.Tick(attacker, start + timing.startup + .001f);
            result = CombatRules.ResolveStrike(attacker, defender, start + timing.startup + .002f, partMultiplier);
            CombatRules.Tick(attacker, start + timing.startup + timing.active + timing.recovery + .01f);
            return result.Hit;
        }

        static CombatActorState NewFighter(string id = "hero", int hp = 60, int attack = 8,
            int defense = 1, int stamina = 60) =>
            new CombatActorState(id, id, hp, attack, defense, stamina);

        static void Check(string name, bool condition, string detail = null)
        {
            if (!condition) throw new Exception("FAIL: " + name + (detail == null ? "" : "（" + detail + "）"));
            Console.WriteLine("PASS: " + name + (detail == null ? "" : "（" + detail + "）"));
        }
    }
}
