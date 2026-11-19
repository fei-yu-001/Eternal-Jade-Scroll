using System;

namespace TwelveJade.Core
{
    // M4-01 战斗规则垂直切片（大纲十二：2D 动作战斗）。只做规则与状态，不做表现：
    // 四类动作（轻击/重击/闪避/等待）、起手-命中-收招-硬直-死亡状态机、无敌窗、伤害算术。
    // 时间一律由调用方传入（float 秒，可为暂停/加速的游戏时钟）；不使用随机数，行为完全确定，
    // 测试用固定数值推进时间。状态机的写入只走 CombatRules，表现层只读——生命与精力不允许直接改。
    //
    // 任务书 K（2.0 深度层）：部位锁敌与弱点暴击、精准闪避"看破"、招式表（妖兽专属前摇/判定/后摇）、
    // 流血与霸体反震、精力透支破绽、每单位战斗档案（CombatTuning）。
    // 这一层的每一个新入口都带"缺省即基线"的约束：Tuning/CurrentMove/PartStrike 为 null 时，
    // 状态机与伤害算术与 M4-01 逐字一致——既有 CombatChecks / CombatShot / FrontEndShotProps 三处
    // 只读夹具因此一条都不必改。改这里之前先想清楚这一点。
    public enum CombatPhase { Idle, Startup, Active, Recovery, Hitstun, Dead }

    public enum CombatActionType { Wait, Light, Heavy, Dodge }

    // 每单位战斗档案：null = 用 CombatRules 的基线常量（SaveChecks 钉死的那套）。
    // 这里只放"每单位可以不一样"的三个量；精力档位不放进来——8/20/12 同时被
    // CombatChecks.cs 与 CombatShot.cs 精确断言，做成可调参数只会制造第二真源。
    public sealed class CombatTuning
    {
        public CombatTuning(float regenPerSecond, float dodgeActiveSeconds, float riposteWindowSeconds)
        {
            if (regenPerSecond < 0f) throw new ArgumentException("精力回复速度不应为负。");
            if (dodgeActiveSeconds <= 0f) throw new ArgumentException("闪避无敌窗应为正数。");
            if (riposteWindowSeconds <= 0f) throw new ArgumentException("看破判定窗应为正数。");
            StaminaRegenPerSecond = regenPerSecond;
            DodgeActiveSeconds = dodgeActiveSeconds;
            RiposteWindowSeconds = riposteWindowSeconds;
        }

        public float StaminaRegenPerSecond { get; }
        public float DodgeActiveSeconds { get; }
        public float RiposteWindowSeconds { get; }
    }

    // 妖兽 2.0 招式：专属前摇/判定/后摇、射程、精力与附加效果。由 enemies.json 的 attacks
    // 登记（解析在 Scripts/Combat/EnemyProfileTable），Core 只认这份数据结构。
    public sealed class CombatMove
    {
        public CombatMove(string id, string name, CombatActionType action, int power,
            float startup, float active, float recovery, float range, int staminaCost)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("招式缺少 id。");
            if (startup <= 0f || active <= 0f || recovery <= 0f)
                throw new ArgumentException("招式 " + id + " 的三相时间都应为正数。");
            if (range <= 0f) throw new ArgumentException("招式 " + id + " 的射程应为正数。");
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? id : name;
            Action = action;
            Power = Math.Max(0, power);
            Startup = startup; Active = active; Recovery = recovery;
            Range = range;
            StaminaCost = Math.Max(0, staminaCost);
        }

        public string Id { get; }
        public string Name { get; }
        public CombatActionType Action { get; }
        // 招式基准伤害：替代 LightBaseDamage/HeavyBaseDamage 参与 (攻击 + 基准) × 倍率。
        public int Power { get; }
        public float Startup { get; }
        public float Active { get; }
        // 空砍（被闪开/被挡开）时的后摇加长量：破绽窗口，越重的招式罚得越狠。
        public float WhiffPenalty { get; set; }
        public float Recovery { get; }
        public float Range { get; }
        public int StaminaCost { get; }
        public float BleedSeconds { get; set; }
        public float Knockback { get; set; }
        // 霸体：这一招起手/判定期间挨打不进入硬直（石铠山魈正面），并且反震攻击方。
        public bool Bulwark { get; set; }
    }

    // 一次部位打击的请求：锁哪个部位、倍率多少、该部位的抵抗率、是否弱点与暴击加成。
    public sealed class PartStrike
    {
        public string PartId = "";
        public float Multiplier = 1f;
        // 该部位的闪避抵抗率（0..1）：确定性累计成"抵抗计"，攒满 1 就落空——
        // Core 不用随机数，同一段操作序列永远得到同一个结果。
        public float DodgeChance;
        public bool WeakPoint;
        // 弱点命中的额外暴击倍率加成（命途/配饰给的），0 = 不加。
        public float CritBonus;
        // 看破反击：无视守方防御。
        public bool IgnoreDefense;
        // 打的是霸体部位（石化前胸）：守方招式起手/判定期间挨这一下要反震攻击方。
        public bool Bulwark;
    }

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
        // 装备/命途的战斗修正量（任务书 K4）：由战力服务写入，Core 只负责把它折进伤害算术
        // 并压进同一条溢出护栏。默认 0 = 基线；裸装单位与 M4-01 的算术逐字一致。
        public int AttackModifier { get; set; }
        public int DefenseModifier { get; set; }
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

        // ---- 任务书 K 2.0 深度层（默认值全部等于基线行为）----
        // 每单位档案：null = 用 CombatRules 基线常量。
        public CombatTuning Tuning { get; set; }
        // 当前锁定的打击部位（表现层写、结算层读；null/空 = 躯干平攻）。
        public string TargetPartId { get; set; }
        // 当前招式（妖兽 2.0 招式表）：null = 用 Core 的轻/重击基线三相。
        public CombatMove CurrentMove { get; internal set; }
        public float ActiveStartedAt { get; internal set; }
        // 部位抵抗累计：见 PartStrike.DodgeChance 的确定性口径。
        public float DodgeGauge { get; internal set; }
        // 精准闪避已"待看破"：无敌窗内真的把一击闪开的那一刻才转成 CounterReady。
        public bool RipostePending { get; internal set; }
        // 看破待发：下一次攻击无视防御、必打弱点暴击。
        public bool CounterReady { get; internal set; }
        public int BleedDamage { get; internal set; }
        public float BleedUntil { get; internal set; }
        internal float BleedNextAt;
        // 空砍后摇加长量：Tick 在"判定帧→收招"那一步把它并进去，收招结束清零。
        // 必须是"下一次转移时并入"而不是直接改 PhaseUntil——Tick 那条转移会按招式重算
        // PhaseUntil，直接改会被冲掉，空砍就白罚了。
        internal float WhiffExtension;

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
        // 2.0：这一击是否弱点/看破暴击、是否被挡开、打在哪个部位、有没有被反震。
        public bool Critical;
        public bool Whiffed;
        public string PartId = "";
        public bool Recoiled;
        public float BleedSeconds;

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
        // 任务书 K3：看破判定窗——敌方进入判定帧后多久之内起闪才算"看得准"。
        public const float RiposteWindowSeconds = .15f;
        // 精力透支后露出的破绽时长（只由调用方显式施加，TryAct 的拒绝路径不带副作用）。
        public const float ExposureSeconds = .35f;
        public const float BleedTickSeconds = .5f;

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

        // 实际三相：招式表优先，其次闪避档（CombatTuning 可把无敌窗从 .22 拉到 .25），最后基线。
        // CurrentMove/Tuning 都为 null 时逐字等于 Timing(action)——基线夹具靠这一点不受影响。
        public static (float startup, float active, float recovery) TimingOf(CombatActorState actor)
        {
            if (actor?.CurrentMove != null)
            {
                var move = actor.CurrentMove;
                return (move.Startup, move.Active, move.Recovery);
            }
            if (actor != null && actor.CurrentAction == CombatActionType.Dodge && actor.Tuning != null)
                return (Timing(CombatActionType.Dodge).startup, actor.Tuning.DodgeActiveSeconds,
                    Timing(CombatActionType.Dodge).recovery);
            return Timing(actor?.CurrentAction ?? CombatActionType.Wait);
        }

        // 这一招打出去要费多少精力：招式表自带则用它，否则回到基线档位。
        static int CostOf(CombatActorState actor, CombatActionType action, CombatMove move) =>
            move != null && action != CombatActionType.Dodge && action != CombatActionType.Wait
                ? move.StaminaCost : StaminaCost(action);

        // 起手：只有 Idle 能动作；硬直/前摇/收招/死亡一律明确拒绝并给出原因（给 M4-03 做提示文案）。
        public static CombatResult TryAct(CombatActorState actor, CombatActionType action, float now) =>
            TryAct(actor, action, now, null, null);

        // 带对手上下文的起手：opponent 正处于（或刚进入）判定帧时，这一记闪避被记为"看得准"。
        public static CombatResult TryAct(CombatActorState actor, CombatActionType action, float now,
            CombatActorState opponent) => TryAct(actor, action, now, opponent, null);

        // 指定招式起手（妖兽 2.0 招式表；玩家侧传 null 走基线轻/重击）。
        public static CombatResult TryAct(CombatActorState actor, CombatActionType action, float now,
            CombatActorState opponent, CombatMove move)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            if (actor.IsDead) return CombatResult.Refused(actor, "已倒下，动不了了。");
            if (actor.Phase != CombatPhase.Idle) return CombatResult.Refused(actor, "这一下还没收干净。");
            // 看破窗口只看真闪避：其它动作不该顺手把 RipostePending 点亮。
            var precise = action == CombatActionType.Dodge && IsInRiposteWindow(opponent, now, actor);
            var cost = CostOf(actor, action, move);
            if (actor.Stamina < cost) return CombatResult.Refused(actor, "精力不够了。");
            actor.Stamina -= cost;
            actor.CurrentAction = action;
            actor.StruckThisAction = false;
            if (action == CombatActionType.Wait)
                return new CombatResult { Executed = true, AttackerStamina = actor.Stamina };
            actor.CurrentMove = action == CombatActionType.Dodge ? null : move;
            var timing = TimingOf(actor);
            actor.Phase = CombatPhase.Startup;
            actor.ActionStart = now;
            // 三阶段都锚定起手时刻：Tick 少调几次也不会把动作拉长（批处理节拍不匀也安全）。
            actor.PhaseUntil = now + timing.startup;
            actor.Invulnerable = false;
            if (precise) actor.RipostePending = true;
            return new CombatResult { Executed = true, AttackerStamina = actor.Stamina };
        }

        // 推进一步状态机的相位到期判断。
        static bool Due(float now, float until) => now >= until - PhaseEpsilon;

        // 看破判定：对手已进入判定帧且进入时长不超过看破窗；或者对手还在前摇、但判定帧
        // 在 .15 秒内就要开——这一半是"预判"，不是宽松的额外权限。
        // 为什么必须有预判那一半：Core 的结算由调用方在对手进入判定帧的那一帧就发起，
        // 等玩家"看到判定帧再闪"永远慢一帧，这一刀照吃，看破就成了纸面机制。
        // 山魈那种 .60 秒长前摇仍然要求你压到最后 .15 秒才起闪，早闪不算看得准。
        public static bool IsInRiposteWindow(CombatActorState opponent, float now, CombatActorState dodger)
        {
            if (opponent == null || opponent.IsDead) return false;
            var window = dodger?.Tuning?.RiposteWindowSeconds ?? RiposteWindowSeconds;
            if (opponent.Phase == CombatPhase.Active)
                return now - opponent.ActiveStartedAt <= window + PhaseEpsilon;
            if (opponent.Phase == CombatPhase.Startup)
                return opponent.PhaseUntil - now <= window + PhaseEpsilon;
            return false;
        }

        // 推进状态机：把所有已到期的阶段一次走完；闪避只在命中帧（Active）内无敌。
        public static void Tick(CombatActorState actor, float now)
        {
            if (actor == null || actor.IsDead) return;
            TickBleed(actor, now);
            var guard = 0;
            while (guard++ < 8)
            {
                var timing = TimingOf(actor);
                switch (actor.Phase)
                {
                    case CombatPhase.Startup:
                        if (!Due(now, actor.PhaseUntil)) return;
                        actor.Phase = CombatPhase.Active;
                        actor.Invulnerable = actor.CurrentAction == CombatActionType.Dodge;
                        actor.ActiveStartedAt = actor.ActionStart + timing.startup;
                        actor.PhaseUntil = actor.ActionStart + timing.startup + timing.active;
                        break;
                    case CombatPhase.Active:
                        if (!Due(now, actor.PhaseUntil)) return;
                        actor.Phase = CombatPhase.Recovery;
                        actor.Invulnerable = false;
                        actor.PhaseUntil = actor.ActionStart + timing.startup + timing.active + timing.recovery
                            + actor.WhiffExtension;
                        break;
                    case CombatPhase.Recovery:
                        if (!Due(now, actor.PhaseUntil)) return;
                        actor.Phase = CombatPhase.Idle;
                        actor.CurrentAction = CombatActionType.Wait;
                        actor.CurrentMove = null;
                        actor.WhiffExtension = 0f;
                        return;
                    case CombatPhase.Hitstun:
                        if (!Due(now, actor.PhaseUntil)) return;
                        actor.Phase = CombatPhase.Idle;
                        actor.CurrentAction = CombatActionType.Wait;
                        actor.CurrentMove = null;
                        actor.WhiffExtension = 0f;
                        return;
                    default:
                        return;
                }
            }
        }

        // 命中帧内的进度（0..1），表现层做突进/挥击动画用；不在命中帧返回 0。
        // Read-only shared action clock for presentation across the Core assembly boundary.
        public static float ActionElapsed(CombatActorState actor, float now) =>
            actor == null || actor.ActionStart < 0 ? 0f : Math.Max(0f, now - actor.ActionStart);

        public static float ActiveProgress(CombatActorState actor, float now)
        {
            if (actor == null || actor.Phase != CombatPhase.Active) return 0f;
            var timing = TimingOf(actor);
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
            var rate = actor.Tuning?.StaminaRegenPerSecond ?? StaminaRegenPerSecond;
            var sum = seconds * rate + actor.RegenRemainder;
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

        // 收招只结束当前动作；不推进世界时间，也不结算、清除流血或改写气血。
        public static void CancelAction(CombatActorState actor)
        {
            if (actor == null || actor.IsDead) return;
            actor.Phase = CombatPhase.Idle;
            actor.PhaseUntil = 0f;
            actor.CurrentAction = CombatActionType.Wait;
            actor.CurrentMove = null;
            actor.Invulnerable = false;
            actor.StruckThisAction = false;
            actor.CounterReady = false;
            actor.RipostePending = false;
            actor.WhiffExtension = 0f;
        }

        // 回镇将息（任务书 K5/M7）：把气血扶到指定值，最少留一口气——
        // 生命只走 Core 的这道口，表现层不许直接改 Hp。已倒下的单位顺手扶起来，
        // 免得"醒来"这一步在 Dead 相态上写数字写出个说不清的状态。
        public static void RestoreHp(CombatActorState actor, int hp)
        {
            if (actor == null) return;
            actor.Hp = Math.Max(MinDamage, Math.Min(actor.MaxHp, hp));
            if (actor.Phase != CombatPhase.Dead) return;
            actor.Phase = CombatPhase.Idle;
            actor.PhaseUntil = 0f;
            actor.CurrentAction = CombatActionType.Wait;
            actor.CurrentMove = null;
            actor.Invulnerable = false;
            actor.StruckThisAction = false;
            actor.CounterReady = false;
            actor.RipostePending = false;
            actor.BleedDamage = 0;
            actor.BleedUntil = 0f;
        }

        // 精力透支的破绽：调用方（表现层/战斗前台）在 TryAct 因"精力不够"被拒后显式施加。
        // 刻意不做成 TryAct 的副作用——CombatChecks.cs 里"重击被拒后同一时刻轻击仍要能打出去"
        // 这条断言依赖拒绝路径干净，把惩罚塞进 Core 的拒绝分支会当场打破只读夹具。
        public static void EnterExposure(CombatActorState actor, float now, float seconds = ExposureSeconds)
        {
            if (actor == null || actor.IsDead) return;
            if (actor.Phase != CombatPhase.Idle) return;
            actor.CurrentAction = CombatActionType.Wait;
            actor.CurrentMove = null;
            actor.Invulnerable = false;
            actor.Phase = CombatPhase.Hitstun;
            actor.PhaseUntil = now + Math.Max(0f, seconds);
        }

        // 流血：每 BleedTickSeconds 掉一口血。只在存活时结算，能把人放倒。
        public static void ApplyBleed(CombatActorState actor, float now, int damagePerTick, float seconds)
        {
            if (actor == null || actor.IsDead || seconds <= 0f || damagePerTick <= 0) return;
            actor.BleedDamage = Math.Max(1, damagePerTick);
            actor.BleedUntil = now + seconds;
            if (actor.BleedNextAt <= 0f || actor.BleedNextAt < now) actor.BleedNextAt = now + BleedTickSeconds;
        }

        static void TickBleed(CombatActorState actor, float now)
        {
            if (actor.BleedDamage <= 0) return;
            if (now >= actor.BleedUntil)
            {
                actor.BleedDamage = 0;
                actor.BleedNextAt = 0f;
                return;
            }
            if (now < actor.BleedNextAt) return;
            var ticks = 1;
            actor.BleedNextAt += BleedTickSeconds;
            var damage = Math.Min(actor.BleedDamage * ticks, actor.Hp);
            actor.Hp -= damage;
            if (actor.Hp <= 0)
            {
                actor.Hp = 0;
                actor.Phase = CombatPhase.Dead;
                actor.PhaseUntil = 0f;
                actor.Invulnerable = false;
                actor.BleedDamage = 0;
            }
        }

        // 命中结算：攻击方必须正处于命中帧且这一击尚未落过；距离不够、闪避无敌、目标已倒下都算落空。
        // 伤害 = round((攻击 + 招式基准) × 部位倍率) - 防御，最少 1，且不超过目标剩余生命。
        public static CombatResult ResolveStrike(CombatActorState attacker, CombatActorState defender,
            float now, float partMultiplier = 1f) =>
            ResolvePartStrike(attacker, defender, now, new PartStrike
            {
                PartId = "",
                Multiplier = partMultiplier,
            });

        // 部位打击结算（任务书 K2/M2）：弱点即暴击、部位抵抗累计成空砍、看破反击无视防御。
        // 躯干倍率 1.0 且非弱点时，伤害算术与 ResolveStrike 逐字相同。
        public static CombatResult ResolvePartStrike(CombatActorState attacker, CombatActorState defender,
            float now, PartStrike strike)
        {
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            if (defender == null) throw new ArgumentNullException(nameof(defender));
            strike ??= new PartStrike();
            if (attacker.IsDead) return CombatResult.Missed(attacker, defender, "攻击方已倒下。");
            var striking = attacker.Phase == CombatPhase.Active &&
                (attacker.CurrentAction == CombatActionType.Light || attacker.CurrentAction == CombatActionType.Heavy);
            if (!striking) return CombatResult.Missed(attacker, defender, "不在命中帧。");
            if (attacker.StruckThisAction) return CombatResult.Missed(attacker, defender, "这一击已经落过。");
            attacker.StruckThisAction = true;
            if (defender.IsDead) return CombatResult.Missed(attacker, defender, "目标已倒下。");
            var reach = attacker.CurrentMove?.Range ?? StrikeRange;
            if (Math.Abs(attacker.X - defender.X) > reach)
            {
                ExtendWhiffRecovery(attacker, now);
                var outOfRange = CombatResult.Missed(attacker, defender, "够不着。");
                outOfRange.Whiffed = true;
                return outOfRange;
            }
            if (defender.Invulnerable)
            {
                ExtendWhiffRecovery(attacker, now);
                var evaded = CombatResult.Missed(attacker, defender, "被闪开了。");
                evaded.Whiffed = true;
                // 无敌窗内真的把这一击让过去了，且起手那一刻看得准——转成看破反击。
                if (defender.RipostePending)
                {
                    defender.RipostePending = false;
                    defender.CounterReady = true;
                }
                return evaded;
            }
            // 部位抵抗计：每次打这个部位攒一点抵抗率，攒满一次就落空。确定性，无随机数。
            if (strike.DodgeChance > 0f)
            {
                defender.DodgeGauge += Math.Min(strike.DodgeChance, 1f);
                if (defender.DodgeGauge >= 1f)
                {
                    defender.DodgeGauge -= 1f;
                    ExtendWhiffRecovery(attacker, now);
                    var blocked = CombatResult.Missed(attacker, defender, "被挡开了。");
                    blocked.Whiffed = true;
                    blocked.PartId = strike.PartId ?? "";
                    return blocked;
                }
            }
            else defender.DodgeGauge = 0f;

            // 看破反击：无视防御，并把倍率抬到守方最弱的部位（必打要害）。
            var counterReady = attacker.CounterReady;
            if (counterReady)
            {
                attacker.CounterReady = false;
                strike = new PartStrike
                {
                    PartId = strike.PartId,
                    Multiplier = Math.Max(1f, strike.Multiplier),
                    DodgeChance = 0f,
                    WeakPoint = true,
                    CritBonus = strike.CritBonus,
                    IgnoreDefense = true,
                };
            }
            var multiplier = Math.Max(0f, strike.Multiplier);
            var isCrit = strike.WeakPoint || counterReady;
            if (isCrit) multiplier *= 1f + Math.Max(0f, strike.CritBonus);

            var baseDamage = attacker.CurrentMove != null ? attacker.CurrentMove.Power
                : attacker.CurrentAction == CombatActionType.Heavy ? HeavyBaseDamage : LightBaseDamage;
            var rawAttack = CombatActorState.ClampStat(attacker.Attack + attacker.AttackModifier, 0);
            var power = (int)Math.Round((rawAttack + baseDamage) * multiplier);
            var effectiveDefense = strike.IgnoreDefense ? 0
                : CombatActorState.ClampStat(defender.Defense + defender.DefenseModifier, 0);
            var damage = Math.Min(Math.Max(MinDamage, power - effectiveDefense), defender.Hp);
            defender.Hp -= damage;

            var result = new CombatResult
            {
                Executed = true,
                Hit = true,
                Damage = damage,
                DefenderHp = defender.Hp,
                AttackerStamina = attacker.Stamina,
                Critical = isCrit,
                PartId = strike.PartId ?? "",
            };

            if (defender.Hp <= 0)
            {
                defender.Hp = 0;
                defender.Phase = CombatPhase.Dead;
                defender.PhaseUntil = 0f;
                defender.Invulnerable = false;
                defender.BleedDamage = 0;
                return result;
            }

            // 霸体：守方正处于这记霸体招式的起手/判定期间，且打的是它的霸体部位——
            // 不吃硬直，反倒把攻击方震出破绽（石铠山魈正面）。
            var bulwark = defender.CurrentMove != null && defender.CurrentMove.Bulwark && strike.Bulwark &&
                (defender.Phase == CombatPhase.Startup || defender.Phase == CombatPhase.Active);
            if (bulwark)
            {
                result.Recoiled = true;
                var recoil = Math.Min(Math.Max(1, defender.Attack / 5), attacker.Hp);
                attacker.Hp -= recoil;
                if (attacker.Hp <= 0)
                {
                    attacker.Hp = 0;
                    attacker.Phase = CombatPhase.Dead;
                    attacker.PhaseUntil = 0f;
                }
                else
                {
                    // 反震必须直接把这一下打断：攻方此刻正在判定帧里，走 EnterExposure 的
                    // "只在 Idle 才罚"门槛会把它原样放过，霸体就成了摆设。
                    attacker.Phase = CombatPhase.Hitstun;
                    attacker.PhaseUntil = now + HitstunSeconds;
                    attacker.CurrentAction = CombatActionType.Wait;
                    attacker.CurrentMove = null;
                }
                return result;
            }

            // 受击硬直：被打断当前动作，硬直结束前什么都做不了。
            defender.Phase = CombatPhase.Hitstun;
            defender.PhaseUntil = now + HitstunSeconds;
            defender.CurrentAction = CombatActionType.Wait;
            defender.CurrentMove = null;
            defender.Invulnerable = false;

            // 招式附加效果：流血与击退只在打中之后的这条路径上生效。
            var move = attacker.CurrentMove;
            if (move != null && move.BleedSeconds > 0f)
            {
                ApplyBleed(defender, now, Math.Max(1, (attacker.Attack + baseDamage) / 12), move.BleedSeconds);
                result.BleedSeconds = move.BleedSeconds;
            }
            if (move != null && move.Knockback > 0f)
                defender.X += Math.Sign(defender.X - attacker.X == 0 ? 1f : Math.Sign(defender.X - attacker.X))
                    * move.Knockback;
            return result;
        }

        // 空砍惩罚：收招加长，破绽更大。只有招式表登记了 WhiffPenalty 才加长；
        // 基线轻/重击（无招式表）因此与 M4-01 完全一致（不罚）。
        static void ExtendWhiffRecovery(CombatActorState attacker, float now)
        {
            var move = attacker.CurrentMove;
            if (move == null || move.WhiffPenalty <= 0f) return;
            attacker.WhiffExtension = move.WhiffPenalty;
        }
    }
}
