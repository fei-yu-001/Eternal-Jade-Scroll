using System;

namespace TwelveJade.Core
{
    // M4-01 战斗规则垂直切片（大纲十二：2D 动作战斗）。只做规则与状态，不做表现：
    // 四类动作（轻击/重击/闪避/等待）、起手-命中-收招-硬直-死亡状态机、无敌窗、伤害算术。
    // 时间一律由调用方传入（float 秒，可为暂停/加速的游戏时钟）；不使用随机数，行为完全确定，
    // 测试用固定数值推进时间。状态机的写入只走 CombatRules，表现层只读——生命与精力不允许直接改。
    public enum CombatPhase { Idle, Startup, Active, Recovery, Hitstun, Dead }

    public enum CombatActionType { Wait, Light, Heavy, Dodge }

    public sealed class CombatActorState
    {
        public CombatActorState(string id, string name, int hp, int attack, int defense, int stamina)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("战斗单位缺少 id。");
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? id : name;
            MaxHp = ClampStat(hp, 1);
            Hp = MaxHp;
            Attack = ClampStat(attack, 0);
            Defense = ClampStat(defense, 0);
            MaxStamina = ClampStat(stamina, 1);
            Stamina = MaxStamina;
            Phase = CombatPhase.Idle;
        }

        public string Id { get; }
        public string Name { get; }
        public int MaxHp { get; }
        public int Hp { get; internal set; }
        public int Attack { get; }
        public int Defense { get; }
        public int MaxStamina { get; }
        public int Stamina { get; internal set; }
        // 同一条轴上的位置：Core 不关心单位与朝向，命中只看两点的距离。
        public float X { get; set; }
        public CombatPhase Phase { get; internal set; }
        public float PhaseUntil { get; internal set; }
        public CombatActionType CurrentAction { get; internal set; }
        public bool Invulnerable { get; internal set; }
        // 一次起手只落一击：命中帧内重复结算不再扣血（审计要求"重复执行不会重复扣血"）。
        public bool StruckThisAction { get; internal set; }
        // 当前动作的起手时刻：三个阶段都锚定在它上面，掉帧不会拉长动作总时长。
        internal float ActionStart = -1f;
        // 精力回复的零头：不足 1 点的部分攒着，低帧率下也能回满。
        internal float RegenRemainder;
        public bool IsDead => Phase == CombatPhase.Dead;

        // 属性上限同时就是溢出护栏：int.MaxValue 也会被压进安全范围（见 CombatChecks 极端属性用例）。
        internal static int ClampStat(int value, int min) => Math.Max(min, Math.Min(99999, value));
    }

    public sealed class CombatResult
    {
        public bool Executed;
        public bool Hit;
        public int Damage;
        public int DefenderHp;
        public int AttackerStamina;
        public string Reason = "";

        public static CombatResult Refused(CombatActorState actor, string reason) =>
            new CombatResult { Executed = false, Reason = reason, AttackerStamina = actor.Stamina };

        public static CombatResult Missed(CombatActorState attacker, CombatActorState defender, string reason) =>
            new CombatResult
            {
                Executed = true,
                Hit = false,
                Reason = reason,
                DefenderHp = defender.Hp,
                AttackerStamina = attacker.Stamina,
            };
    }

    public static class CombatRules
    {
        public const int LightStaminaCost = 8, HeavyStaminaCost = 20, DodgeStaminaCost = 12;
        public const int LightBaseDamage = 10, HeavyBaseDamage = 22;
        public const int MinDamage = 1;
        public const float StrikeRange = 1.25f;
        public const float HitstunSeconds = .30f;
        // 相位比较容差：浮点累加会让 20.1f+0.12f 变成 20.220001，严格比较会永远差一帧。
        public const float PhaseEpsilon = .0005f;
        // 只在 Idle 回复：没有它精力只出不进，切打到后半程谁都动不了（审核关注点，见开发日志）。
        public const float StaminaRegenPerSecond = 6f;

        public static (float startup, float active, float recovery) Timing(CombatActionType action) =>
            action switch
            {
                CombatActionType.Light => (.12f, .08f, .18f),
                CombatActionType.Heavy => (.28f, .10f, .34f),
                CombatActionType.Dodge => (.05f, .22f, .12f),
                _ => (0f, 0f, 0f),
            };

        public static int StaminaCost(CombatActionType action) =>
            action == CombatActionType.Light ? LightStaminaCost :
            action == CombatActionType.Heavy ? HeavyStaminaCost :
            action == CombatActionType.Dodge ? DodgeStaminaCost : 0;

        // 起手：只有 Idle 能动作；硬直/前摇/收招/死亡一律明确拒绝并给出原因（给 M4-03 做提示文案）。
        public static CombatResult TryAct(CombatActorState actor, CombatActionType action, float now)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            if (actor.IsDead) return CombatResult.Refused(actor, "已倒下，动不了了。");
            if (actor.Phase != CombatPhase.Idle) return CombatResult.Refused(actor, "这一下还没收干净。");
            var cost = StaminaCost(action);
            if (actor.Stamina < cost) return CombatResult.Refused(actor, "精力不够了。");
            actor.Stamina -= cost;
            actor.CurrentAction = action;
            actor.StruckThisAction = false;
            if (action == CombatActionType.Wait)
                return new CombatResult { Executed = true, AttackerStamina = actor.Stamina };
            var timing = Timing(action);
            actor.Phase = CombatPhase.Startup;
            actor.ActionStart = now;
            // 三阶段都锚定起手时刻：Tick 少调几次也不会把动作拉长（批处理节拍不匀也安全）。
            actor.PhaseUntil = now + timing.startup;
            actor.Invulnerable = false;
            return new CombatResult { Executed = true, AttackerStamina = actor.Stamina };
        }

        // 推进状态机：把所有已到期的阶段一次走完；闪避只在命中帧（Active）内无敌。
        public static void Tick(CombatActorState actor, float now)
        {
            if (actor == null || actor.IsDead) return;
            var guard = 0;
            while (guard++ < 8)
            {
                switch (actor.Phase)
                {
                    case CombatPhase.Startup:
                        if (now < actor.PhaseUntil - PhaseEpsilon) return;
                        actor.Phase = CombatPhase.Active;
                        actor.Invulnerable = actor.CurrentAction == CombatActionType.Dodge;
                        actor.PhaseUntil = actor.ActionStart + Timing(actor.CurrentAction).startup
                            + Timing(actor.CurrentAction).active;
                        break;
                    case CombatPhase.Active:
                        if (now < actor.PhaseUntil - PhaseEpsilon) return;
                        actor.Phase = CombatPhase.Recovery;
                        actor.Invulnerable = false;
                        actor.PhaseUntil = actor.ActionStart + Timing(actor.CurrentAction).startup
                            + Timing(actor.CurrentAction).active + Timing(actor.CurrentAction).recovery;
                        break;
                    case CombatPhase.Recovery:
                        if (now < actor.PhaseUntil - PhaseEpsilon) return;
                        actor.Phase = CombatPhase.Idle;
                        actor.CurrentAction = CombatActionType.Wait;
                        return;
                    case CombatPhase.Hitstun:
                        if (now < actor.PhaseUntil - PhaseEpsilon) return;
                        actor.Phase = CombatPhase.Idle;
                        actor.CurrentAction = CombatActionType.Wait;
                        return;
                    default:
                        return;
                }
            }
        }

        // 命中帧内的进度（0..1），表现层做突进/挥击动画用；不在命中帧返回 0。
        public static float ActiveProgress(CombatActorState actor, float now)
        {
            if (actor == null || actor.Phase != CombatPhase.Active) return 0f;
            var timing = Timing(actor.CurrentAction);
            if (timing.active <= 0f) return 0f;
            var progress = (now - (actor.ActionStart + timing.startup)) / timing.active;
            return (float)Math.Min(1.0, Math.Max(0.0, progress));
        }

        // 精力回复：只按调用方给的时长结算（M4-03 用可暂停时钟逐帧调），Idle 之外一律不回。
        public static void Regenerate(CombatActorState actor, float seconds)
        {
            if (actor == null || actor.IsDead || seconds <= 0f || actor.Phase != CombatPhase.Idle) return;
            if (actor.Stamina >= actor.MaxStamina)
            {
                actor.RegenRemainder = 0f;
                return;
            }
            var sum = seconds * StaminaRegenPerSecond + actor.RegenRemainder;
            var whole = (int)Math.Floor(sum);
            if (whole <= 0)
            {
                // 不足 1 点的部分先记着，攒够再发，免得低帧率下永远回不上来。
                actor.RegenRemainder = Math.Min(sum, 1f);
                return;
            }
            actor.RegenRemainder = sum - whole;
            actor.Stamina = Math.Min(actor.MaxStamina, actor.Stamina + whole);
        }

        // 命中结算：攻击方必须正处于命中帧且这一击尚未落过；距离不够、闪避无敌、目标已倒下都算落空。
        // 伤害 = round((攻击 + 招式基准) × 部位倍率) - 防御，最少 1，且不超过目标剩余生命。
        public static CombatResult ResolveStrike(CombatActorState attacker, CombatActorState defender,
            float now, float partMultiplier = 1f)
        {
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            if (defender == null) throw new ArgumentNullException(nameof(defender));
            if (attacker.IsDead) return CombatResult.Missed(attacker, defender, "攻击方已倒下。");
            var striking = attacker.Phase == CombatPhase.Active &&
                (attacker.CurrentAction == CombatActionType.Light || attacker.CurrentAction == CombatActionType.Heavy);
            if (!striking) return CombatResult.Missed(attacker, defender, "不在命中帧。");
            if (attacker.StruckThisAction) return CombatResult.Missed(attacker, defender, "这一击已经落过。");
            attacker.StruckThisAction = true;
            if (defender.IsDead) return CombatResult.Missed(attacker, defender, "目标已倒下。");
            if (Math.Abs(attacker.X - defender.X) > StrikeRange) return CombatResult.Missed(attacker, defender, "够不着。");
            if (defender.Invulnerable) return CombatResult.Missed(attacker, defender, "被闪开了。");

            var baseDamage = attacker.CurrentAction == CombatActionType.Heavy ? HeavyBaseDamage : LightBaseDamage;
            var power = (int)Math.Round((attacker.Attack + baseDamage) * partMultiplier);
            var damage = Math.Min(Math.Max(MinDamage, power - defender.Defense), defender.Hp);
            defender.Hp -= damage;
            if (defender.Hp <= 0)
            {
                defender.Hp = 0;
                defender.Phase = CombatPhase.Dead;
                defender.PhaseUntil = 0f;
            }
            else
            {
                // 受击硬直：被打断当前动作，硬直结束前什么都做不了。
                defender.Phase = CombatPhase.Hitstun;
                defender.PhaseUntil = now + HitstunSeconds;
                defender.CurrentAction = CombatActionType.Wait;
            }
            defender.Invulnerable = false;
            return new CombatResult
            {
                Executed = true,
                Hit = true,
                Damage = damage,
                DefenderHp = defender.Hp,
                AttackerStamina = attacker.Stamina,
            };
        }
    }
}
