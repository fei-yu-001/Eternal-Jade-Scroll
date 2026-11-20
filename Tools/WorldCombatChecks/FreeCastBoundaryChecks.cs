using System;
using System.Collections.Generic;
using TwelveJade.Combat;
using TwelveJade.Core;

// 空放边界回归：只验"没有敌人也出手"之后,接入/解除目标不会改写已钉死的落点、
// 雷法不穿墙、空放不发幻影命中、死亡与解除目标不互相复活、冷却不因换目标清掉、
// 闪避大小步一致。全部固定 dt,自建存档与敌人,不复制 Program.cs 的同义用例。
public static class FreeCastBoundaryChecks
{
    static Action<string, bool, string> check;
    static void Check(string name, bool pass, string detail = "") { check(name, pass, detail); }

    public static void Run(Action<string, bool, string> report)
    {
        check = report ?? throw new ArgumentNullException(nameof(report));
        try { Body(); }
        catch (Exception e) { Check("unexpected exception", false, e.ToString()); }
    }

    static void Body()
    {
        MidFlightTargetKeepsTheLockedLanding();
        MidFlightTargetKeepsTheSwordLine();
        ThunderStopsAtTheWallWithOrWithoutTarget();
        NoTargetStaysUnsettled();
        DeadBodyThroughClearTarget();
        DeadTargetThroughClearTarget();
        ClearTargetMidFlightKeepsClockAndCooldown();
        DodgeStepsAreSizeInvariant();
        DodgeAcceleratesAndSettles();
        DeathEventSurvivesTargetSwitch();
        CancelPreservesTheRealClock();
        NoTargetDeathSettles();
    }

    // ---------- 夹具（自建,与 Program.cs 不共用） ----------

    static SaveData Save()
    {
        var save = new SaveData { characterId = "free-cast", characterName = "空放" };
        // 后期技能能力夹具：青石镇旧学习记录不能自行解锁技能。
        save.MarkFlag("chapter-1-left-qingshi");
        save.MarkFlag("cultivation-initiation");
        save.manualStudies = new[]
        {
            new ManualStudyEntry { id = "sword", learned = true, attempts = 1 },
            new ManualStudyEntry { id = "thunder", learned = true, attempts = 1 },
            new ManualStudyEntry { id = "clarity", learned = true, attempts = 1 },
        };
        return save;
    }

    static readonly CombatMove Jab = new CombatMove("jab", "试探", CombatActionType.Light, 6, .1f, .1f, .2f, 1.25f, 6);

    // moveSpeed 0 让妖只在贴身射程内动手,远处时保持静止,落点才可复现。
    static EnemyProfile Profile()
    {
        return new EnemyProfile("check-foe", "试妖", "check", 120, 12, 2, 80, 0f, 0,
            new[] { new EnemyPart("body", "躯干", 1f, 0f, false, false) },
            new[] { Jab }, new EnemyAiProfile("check", EnemyMoveSet.Profile2, 2f, 9f));
    }

    static CombatActorState Player(int hp = 200) { return new CombatActorState("player", "试剑", hp, 30, 2, 200); }

    static CombatActorState Foe(int hp = 400) { return new CombatActorState("check-foe", "试妖", hp, 12, 2, 80); }

    // 无人场：敌人位与敌人点都退化为出手点,这正是新支持的空放形态。
    static WorldCombatSession Empty(CombatActorState player, Func<float, float, float, float, bool> canTraverse = null)
    {
        player.X = 0f;
        return new WorldCombatSession(player, null, null, Save(),
            new WorldCombatPoint(0f, 0f), new WorldCombatPoint(0f, 0f), canTraverse);
    }

    static WorldCombatSession With(CombatActorState player, CombatActorState foe, float fx, float fy,
        Func<float, float, float, float, bool> canTraverse = null)
    {
        player.X = 0f;
        return new WorldCombatSession(player, foe, Profile(), Save(),
            new WorldCombatPoint(0f, 0f), new WorldCombatPoint(fx, fy), canTraverse);
    }

    static void Drive(WorldCombatSession session, float seconds, float dt = .02f)
    {
        for (var elapsed = 0f; elapsed < seconds; elapsed += dt) session.Tick(dt);
    }

    static int Count(WorldCombatSession session, string id)
    {
        var hits = 0;
        while (session.TryDequeueEvent(out var evt)) if (evt.Id == id) hits++;
        return hits;
    }

    static bool Peek(WorldCombatSession session, string id)
    {
        var any = false;
        while (session.TryDequeueEvent(out var evt)) if (evt.Id == id) any = true;
        return any;
    }

    static float Distance(WorldCombatPoint a, WorldCombatPoint b)
    {
        var dx = a.X - b.X; var dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    static bool Wall(float x) { return x < 60f; }
    static bool Crosses(float x0, float x1) { return Wall(x0) != Wall(x1); }

    // ---------- 用例 ----------

    static void MidFlightTargetKeepsTheLockedLanding()
    {
        // 空放雷的落点按射程 650 钉死,飞行中接入站得更远的妖不能把落点拉过去。
        var far = Empty(Player());
        far.Aim(1f, 0f);
        Check("empty thunder accepted", far.Act(5).Executed);
        far.TryDequeueEvent(out var cast);
        Check("landing stays at the range cap", Math.Abs(cast.Target.X - 650f) <= .01f, cast.Target.X.ToString());
        var locked = cast.Target;
        var cooldown = far.Cooldown("thunder");
        far.SetTarget(Foe(), Profile(), new WorldCombatPoint(900f, 0f));
        Check("acquisition does not move the lock", Math.Abs(far.EventPoint.X - locked.X) <= .01f);
        Drive(far, 1.2f);
        Check("far target outside the lock takes no damage", far.Foe.Hp == far.Foe.MaxHp, "foe hp " + far.Foe.Hp);
        Check("landing point itself is unchanged", Math.Abs(far.FoePoint.X - 900f) <= 18.01f, far.FoePoint.X.ToString());
        Check("acquisition keeps the cooldown", far.Cooldown("thunder") > 0f && far.Cooldown("thunder") < cooldown);

        // 同样的空放,接进站在落点上的妖必须只打一下。
        var onPoint = Empty(Player());
        onPoint.Aim(1f, 0f);
        onPoint.Act(5);
        onPoint.SetTarget(Foe(), Profile(), new WorldCombatPoint(600f, 0f));
        Drive(onPoint, 1.2f);
        Check("target standing on the lock is hit", onPoint.Foe.Hp < onPoint.Foe.MaxHp, "foe hp " + onPoint.Foe.Hp);
        Check("locked landing hits exactly once", Count(onPoint, "hit:foe") == 1);
    }

    static void MidFlightTargetKeepsTheSwordLine()
    {
        // 剑气的准星是出手时的朝向线,换目标不重新瞄准。
        var onLine = Empty(Player());
        onLine.Aim(0f, -1f);
        Check("empty sword accepted", onLine.Act(4).Executed);
        onLine.TryDequeueEvent(out var cast);
        Check("empty sword aims north", cast.Target.X == 0f && cast.Target.Y == -440f, cast.Target.Y.ToString());
        Drive(onLine, .1f);
        onLine.SetTarget(Foe(), Profile(), new WorldCombatPoint(0f, -200f));
        Drive(onLine, 1f);
        Check("target on the original line is cut", onLine.Foe.Hp < onLine.Foe.MaxHp, "foe hp " + onLine.Foe.Hp);

        var offLine = Empty(Player());
        offLine.Aim(0f, -1f);
        offLine.Act(4);
        offLine.TryDequeueEvent(out var cast2);
        Drive(offLine, .1f);
        offLine.SetTarget(Foe(), Profile(), new WorldCombatPoint(200f, 0f));
        Drive(offLine, 1f);
        Check("target east of an empty north cast is missed", offLine.Foe.Hp == offLine.Foe.MaxHp, "foe hp " + offLine.Foe.Hp);
        Check("line cast point still north", cast2.Target.X == 0f && cast2.Target.Y == -440f);
    }

    static void ThunderStopsAtTheWallWithOrWithoutTarget()
    {
        var blocked = Empty(Player(), (x0, y0, x1, y1) => !Crosses(x0, x1));
        blocked.Aim(1f, 0f);
        Check("walled empty thunder accepted", blocked.Act(5).Executed);
        blocked.TryDequeueEvent(out var cast);
        Check("empty landing clamps at the wall", cast.Target.X <= 60.01f && cast.Target.X > 0f, cast.Target.X.ToString());
        // 妖站在落点（墙后 620）上：有墙不穿,没墙就打中,差别只有这堵墙。
        blocked.SetTarget(Foe(), Profile(), new WorldCombatPoint(620f, 0f));
        Drive(blocked, 1.2f);
        Check("thunder behind the wall does no damage", blocked.Foe.Hp == blocked.Foe.MaxHp, "foe hp " + blocked.Foe.Hp);
        Check("wall block is named", blocked.LastMessage.Contains("挡住"), blocked.LastMessage);
        Check("no phantom foe hit behind the wall", !Peek(blocked, "hit:foe"));

        var open = Empty(Player());
        open.Aim(1f, 0f);
        open.Act(5);
        open.SetTarget(Foe(), Profile(), new WorldCombatPoint(620f, 0f));
        Drive(open, 1.2f);
        Check("same layout without a wall hits", open.Foe.Hp < open.Foe.MaxHp, "foe hp " + open.Foe.Hp);
    }

    static void NoTargetStaysUnsettled()
    {
        var session = Empty(Player());
        Drive(session, .2f);
        session.Act(0); Drive(session, .3f);
        session.Act(4); Drive(session, .9f);
        session.Act(5); Drive(session, 1.1f);
        session.Act(6); Drive(session, .7f);
        Check("empty session never settles", !session.Settled);
        Check("empty session has no target", !session.HasTarget && session.Foe == null);
        Check("empty foe point degrades to the player", Distance(session.FoePoint, session.PlayerPoint) < .001f);
        Check("no boss telegraph without a boss", session.TelegraphRadius == 0f);
        Check("no phantom foe hit while empty", Count(session, "hit:foe") == 0);
        Check("no death event while empty", Count(session, "death") == 0);
        var target = session.Act(7);
        Check("lock cycling refuses without a foe", !target.Executed && target.Reason.Contains("部位"), target.Reason);
    }

    static void DeadBodyThroughClearTarget()
    {
        // Hp 只在 Core 里写,零点靠真挨一刀：1 点血的人被贴身妖一击就倒。
        var player = Player(1);
        var foe = Foe();
        var session = With(player, foe, 100f, 0f);
        for (var i = 0; i < 700 && !session.Settled; i++) session.Tick(.02f);
        Check("dead player settles", session.Settled && player.IsDead, "hp " + player.Hp);
        Check("death emitted once with a target", Count(session, "death") == 1);
        session.ClearTarget();
        Check("clearing keeps the body dead", session.Settled && player.IsDead, player.Phase.ToString());
        Check("dead body cannot act", !session.Act(0).Executed);
        session.Tick(.5f);
        Check("clearing does not revive or re-emit death", player.IsDead && Count(session, "death") == 0);
    }

    static void DeadTargetThroughClearTarget()
    {
        var player = Player();
        var foe = Foe(4);
        var session = With(player, foe, 124f, 0f);
        for (var i = 0; i < 80 && !session.Settled; i++) { if (!session.Settled) session.Act(1); session.Tick(.05f); }
        Check("kill settles", session.Settled && foe.IsDead, "foe hp " + foe.Hp);
        var deaths = 0;
        while (session.TryDequeueEvent(out var evt)) if (evt.Id == "death") deaths++;
        var hp = player.Hp;
        session.ClearTarget();
        Check("clearing drops the corpse", !session.HasTarget && session.Foe == null);
        Check("clearing does not revive the player", player.Hp == hp && hp > 0, "hp " + player.Hp);
        Check("cleared victory reopens the field", !session.Settled);
        session.Tick(.4f);
        Check("corpse does not come back", session.Foe == null && !session.Settled);
        Check("corpse emits no second death", Count(session, "death") == 0);
    }

    static void ClearTargetMidFlightKeepsClockAndCooldown()
    {
        var player = Player();
        var session = Empty(player);
        session.Aim(1f, 0f);
        session.Act(5);
        Drive(session, .3f);
        var cooldown = session.Cooldown("thunder");
        var stamina = player.Stamina;
        Check("cooldown is running mid flight", cooldown > 0f, "cooldown " + cooldown);
        session.SetTarget(Foe(), Profile(), new WorldCombatPoint(300f, 0f));
        session.ClearTarget();
        Check("cooldown survives lock and unlock", session.Cooldown("thunder") == cooldown, session.Cooldown("thunder").ToString());
        Check("stamina charged only once", player.Stamina == stamina, "stamina " + player.Stamina);
        Check("in flight skill is closed", session.ActiveSkill.Length == 0, session.ActiveSkill);
        Check("pose returns to the cast point", Math.Abs(player.X) < .01f, "player x " + player.X);
        Check("unlock keeps the body idle", player.Phase == CombatPhase.Idle, player.Phase.ToString());
        Check("no phantom hit after unlock", Count(session, "hit:foe") == 0);
        Drive(session, 1.4f);
        Check("cooldown keeps ticking after unlock", session.Cooldown("thunder") < cooldown, session.Cooldown("thunder").ToString());
        Drive(session, 9f);
        Check("recast is possible once the clock runs out", session.Act(5).Executed, session.LastMessage);
    }

    static void DodgeStepsAreSizeInvariant()
    {
        CombatActorState bigPlayer, smallPlayer;
        var big = Empty(bigPlayer = Player());
        var small = Empty(smallPlayer = Player());
        big.Aim(0f, -1f); small.Aim(0f, -1f);
        Check("empty dodge accepted on the big step run", big.Act(2).Executed);
        Check("empty dodge accepted on the small step run", small.Act(2).Executed);
        for (var i = 0; i < 4; i++) big.Tick(.25f);
        Drive(small, 1f);
        Check("big steps and small steps land on the same point",
            Distance(big.PlayerPoint, small.PlayerPoint) < .001f, Distance(big.PlayerPoint, small.PlayerPoint).ToString());
        Check("empty dodge covers exactly one dodge distance",
            Math.Abs(Distance(big.PlayerPoint, new WorldCombatPoint(0f, 0f)) - WorldCombatSession.DodgeDistanceWorld) < .01f,
            Distance(big.PlayerPoint, new WorldCombatPoint(0f, 0f)).ToString());

        // 与空放同一采样率：目标方向不能改变位移量,量化误差才单独露出来。
        var player = Player();
        var away = With(player, Foe(), 200f, 0f);
        away.Act(2);
        Drive(away, 1f);
        var origin = new WorldCombatPoint(0f, 0f);
        Check("targeted dodge travels as far as an empty one",
            Math.Abs(Distance(away.PlayerPoint, origin) - Distance(small.PlayerPoint, origin)) < .001f,
            Distance(away.PlayerPoint, origin) + " vs " + Distance(small.PlayerPoint, origin));
        Check("targeted dodge still retreats", away.PlayerPoint.X < -50f, away.PlayerPoint.X.ToString());
    }

    static void DodgeAcceleratesAndSettles()
    {
        var player = Player(); var session = Empty(player);
        session.Aim(1f, 0f); session.Act(2);
        var timing = CombatRules.TimingOf(player);
        session.Tick(timing.startup);
        var start = session.PlayerPoint.X;
        var lengths = new float[4];
        for (int i = 0; i < 4; i++)
        {
            float before = session.PlayerPoint.X;
            session.Tick(timing.active / 4f);
            lengths[i] = session.PlayerPoint.X - before;
        }
        Check("dodge holds position until the active phase", Math.Abs(start) < .001f);
        Check("dodge accelerates into the middle of the step", lengths[1] > lengths[0] * 1.5f);
        Check("dodge brakes before planting the foot", lengths[3] < lengths[2] * .7f);
        Check("dodge motion remains forward throughout", Array.TrueForAll(lengths, length => length > 0));
        float landed = session.PlayerPoint.X;
        session.Tick(timing.recovery + .05f);
        Check("dodge recovery stays at the intended endpoint", Math.Abs(landed - WorldCombatSession.DodgeDistanceWorld) < .01f &&
            Math.Abs(session.PlayerPoint.X - landed) < .001f);
    }

    static void CancelPreservesTheRealClock()
    {
        var player = Player();
        var session = Empty(player);
        CombatRules.ApplyBleed(player, session.Now, 4, 5f);
        session.Act(0); session.Tick(.1f);
        var hp = player.Hp; var stamina = player.Stamina; var now = session.Now;
        session.Cancel();
        Check("cancel leaves elapsed time unchanged", session.Now == now);
        Check("cancel cannot fast forward bleeding", player.Hp == hp);
        Check("cancel retains the interrupted action cost", player.Stamina == stamina);
        Check("cancel leaves the player ready", player.Phase == CombatPhase.Idle);
        session.Tick(.4f);
        Check("bleeding still ticks at the real time", player.Hp == hp - 4, player.Hp.ToString());
        var dodge = Empty(Player());
        dodge.Act(2); dodge.Tick(.12f);
        var stop = dodge.PlayerPoint;
        dodge.Cancel(); dodge.Tick(.6f);
        Check("cancelled dodge cannot keep moving", Distance(stop, dodge.PlayerPoint) < .001f);
    }

    static void NoTargetDeathSettles()
    {
        var player = Player(); var session = Empty(player);
        CombatRules.ApplyBleed(player, session.Now, player.Hp, 2f);
        session.Tick(.5f);
        Check("bleeding death settles without an enemy", session.Settled && player.IsDead);
        Check("targetless death emits once", Count(session, "death") == 1);
        session.ClearTarget(); session.Tick(.5f);
        Check("clear target cannot resurrect", player.IsDead && session.Settled);
        Check("clear target cannot emit a second death", Count(session, "death") == 0);
    }

    static void DeathEventSurvivesTargetSwitch()
    {
        // 控制器可能先收剑再读事件；解除目标不该把还没取的 death 一起吞掉。
        var player = Player();
        var foe = Foe(4);
        var session = With(player, foe, 124f, 0f);
        for (var i = 0; i < 80 && !session.Settled; i++) { if (!session.Settled) session.Act(1); session.Tick(.05f); }
        session.ClearTarget();
        var deaths = 0;
        while (session.TryDequeueEvent(out var evt)) if (evt.Id == "death") deaths++;
        Check("death stays readable after clearing the target", deaths == 1, "deaths " + deaths);
    }
}
