using System;
using System.Collections.Generic;
using System.Text.Json;
using TwelveJade.Combat;
using TwelveJade.Core;

// 世界原地 2D 战斗核心的窄回归。只覆盖这一层自己的判据：2D 距离、锁点闪避、
// 墙体与体宽、冷却、单次结算、事件协议、大小步一致。全部固定 dt，不掷骰子。
static class Program
{
    static readonly List<object> checks = new List<object>();
    static int failed;
    static void Check(string name, bool pass, string detail = "")
    { checks.Add(new { name, pass, detail }); if (!pass) failed++; }

    static int Main()
    {
        try { Run(); } catch (Exception e) { Check("unexpected exception", false, e.ToString()); }
        Console.WriteLine(JsonSerializer.Serialize(
            new { status = failed == 0 ? "PASS" : "FAIL", checks },
            new JsonSerializerOptions { WriteIndented = true }));
        return failed == 0 ? 0 : 1;
    }

    static void Run()
    {
        TwoDimensionalReach();
        SkillCastHitsOnce();
        ThunderHasLimitedLandingRange();
        FreeCastingKeepsCostsAndCooldowns();
        FreeDodgeRespectsBodyAndFacing();
        UnlearnedFreeCastIsRefused();
        LockedPointCanBeEvaded();
        BarrierBlocksMeleeAndChase();
        BarrierStopsTheProjectile();
        CooldownRefusesSpam();
        VictorySettlesExactlyOnce();
        EventsCarryTheOwningPoints();
        CancelAbortsAndIsImmediatelyReusable();
        ConstructorKeepsCurrentHp();
        BossKeepsItsFootprint();
        TelegraphCannotCrossAWall();
        BigStepMatchesSmallSteps();
        BadDeltasAreIgnored();
        FreeCastBoundaryChecks.Run(Check);
    }

    // ---------- 夹具 ----------

    static SaveData Save()
    {
        var save = new SaveData { characterId = "check", characterName = "试剑" };
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

    static EnemyProfile Profile(float moveSpeed = 0f, float preferredRange = 2f, float aggression = 9f)
    {
        return new EnemyProfile("check-foe", "试妖", "check", 120, 12, 2, 80, moveSpeed, 0,
            new[] { new EnemyPart("body", "躯干", 1f, 0f, false, false) },
            new[] { Jab }, new EnemyAiProfile("check", EnemyMoveSet.Profile2, preferredRange, aggression));
    }

    static EnemyProfile Boss()
    {
        var moves = new[]
        {
            new CombatMove(BossCombatBrain.SweepId, "撼山扫", CombatActionType.Heavy, 12, .8f, .2f, .6f, 1.65f, 16),
            new CombatMove(BossCombatBrain.FissureId, "裂地经行", CombatActionType.Heavy, 17, 1.1f, .7f, .8f, 6.5f, 24),
            new CombatMove(BossCombatBrain.StoneId, "镇印落石", CombatActionType.Heavy, 21, 1.2f, .2f, .8f, 6.5f, 28),
        };
        return new EnemyProfile(BossCombatBrain.EnemyId, "山君", "check", 400, 18, 8, 130, 60f, 0,
            new[] { new EnemyPart("body", "躯干", 1f, 0f, false, false) }, moves,
            new EnemyAiProfile("qingshi-domain", EnemyMoveSet.Profile2, 1.25f, 2.4f));
    }

    static void Drive(WorldCombatSession session, float seconds, float dt = .02f)
    {
        for (var elapsed = 0f; elapsed < seconds; elapsed += dt) session.Tick(dt);
    }

    static WorldCombatSession Build(CombatActorState player, CombatActorState foe, EnemyProfile profile,
        float px, float py, float fx, float fy, Func<float, float, float, float, bool> canTraverse = null)
    {
        player.X = 0f;
        return new WorldCombatSession(player, foe, profile, Save(),
            new WorldCombatPoint(px, py), new WorldCombatPoint(fx, fy),
            canTraverse ?? ((x0, y0, x1, y1) => true));
    }

    static WorldCombatSession Basic(float fx, float fy, out CombatActorState player, out CombatActorState foe,
        Func<float, float, float, float, bool> canTraverse = null, float moveSpeed = 0f, float preferredRange = 2f)
    {
        player = new CombatActorState("player", "试剑", 200, 30, 2, 80);
        foe = new CombatActorState("check-foe", "试妖", 80, 12, 2, 80);
        return Build(player, foe, Profile(moveSpeed, preferredRange), 0f, 0f, fx, fy, canTraverse);
    }

    // ---------- 用例 ----------

    static void TwoDimensionalReach()
    {
        var near = Basic(124f, 0f, out var player, out var foe);
        Check("melee act accepted", near.Act(0).Executed);
        Drive(near, .3f);
        Check("in reach hits", foe.Hp < foe.MaxHp, "foe hp " + foe.Hp);

        // 同样 126 单位，换成 Y 轴量：射程必须是 2D 的，不是只看 X。
        var far = Basic(0f, 126f, out var p2, out var f2);
        far.Act(0);
        Drive(far, .3f);
        Check("out of reach whiffs", f2.Hp == f2.MaxHp, "foe hp " + f2.Hp);
        Check("whiff says why", far.LastMessage.Contains("够不着"), far.LastMessage);
    }

    static void SkillCastHitsOnce()
    {
        var session = Basic(100f, 0f, out var player, out var foe);
        Check("sword accepted", session.Act(4).Executed);
        Drive(session, .35f);
        Check("skill owns the action", player.CurrentMove != null && player.CurrentMove.Id == "learned-sword",
            player.CurrentMove == null ? "null" : player.CurrentMove.Id);
        Drive(session, 1.2f);
        var hits = 0;
        while (session.TryDequeueEvent(out var evt)) if (evt.Id == "hit:foe") hits++;
        Check("one hit per cast", hits == 1, "hits " + hits);
        Check("hp dropped", foe.Hp < foe.MaxHp, "foe hp " + foe.Hp);

        // 同一段 Active 里既不能重复结算，也不能被近战那一路偷打一下。
        var single = Basic(100f, 0f, out var p2, out var f2);
        single.Act(4);
        Drive(single, .7f);
        var firstHp = f2.Hp;
        Drive(single, .2f);
        Check("no second tick of damage", f2.Hp == firstHp, "foe hp " + f2.Hp);
    }

    static void ThunderHasLimitedLandingRange()
    {
        var far = Basic(760f, 0f, out var p1, out var f1);
        Check("thunder can cast beyond reach", far.Act(5).Executed);
        far.TryDequeueEvent(out var cast);
        Check("thunder landing remains within 650", Math.Abs(cast.Target.X - 650) < .01f);
        Drive(far, 1f);
        Check("beyond landing radius takes no damage", f1.Hp == f1.MaxHp);
        var near = Basic(500f, 0f, out var p2, out var f2);
        Check("ranged thunder accepted", near.Act(5).Executed);
        Drive(near, 1f);
        Check("ranged thunder hits", f2.Hp < f2.MaxHp);
    }

    static void FreeCastingKeepsCostsAndCooldowns()
    {
        var player = new CombatActorState("player", "试剑", 200, 30, 2, 180);
        var session = Build(player, null, null, 0, 0, 0, 0);
        session.Aim(0, -1);
        Check("no enemy required to light attack", session.Act(0).Executed);
        Check("empty light pays stamina", player.Stamina == 172);
        session.TryDequeueEvent(out var light);
        Check("empty light follows north facing", light.Target.Y == -125 && light.Target.X == 0);
        Drive(session, .5f);
        Check("empty heavy accepted", session.Act(1).Executed); Drive(session, .8f);
        Check("empty sword accepted", session.Act(4).Executed);
        var cooldown = session.Cooldown("sword");
        session.ClearTarget();
        Check("clearing empty target preserves cooldown", session.Cooldown("sword") == cooldown);
        var foe = new CombatActorState("check-foe", "试妖", 120, 12, 2, 80);
        session.SetTarget(foe, Profile(), new WorldCombatPoint(200, 0));
        Check("target acquisition preserves cooldown", session.HasTarget && session.Cooldown("sword") == cooldown);
        Check("acquisition cannot bypass cooldown", !session.Act(4).Executed);
        session.ClearTarget(); Drive(session, 1.2f);
        Check("cooldown advances without a target", session.Cooldown("sword") < cooldown - 1);
        Check("empty thunder accepted", session.Act(5).Executed); Drive(session, 1.4f);
        CombatRules.RestoreHp(player, 80);
        Check("self healing requires no enemy", session.Act(6).Executed); Drive(session, .8f);
        Check("self healing applied once", player.Hp == 128);
        bool phantom = false;
        while (session.TryDequeueEvent(out var e)) if (e.Id == "hit:foe" || e.Id == "death") phantom = true;
        Check("empty attacks cause no phantom hit or settlement", !phantom && !session.Settled && !session.HasTarget);
    }

    static void FreeDodgeRespectsBodyAndFacing()
    {
        var player = new CombatActorState("player", "试剑", 200, 30, 2, 80);
        var session = Build(player, null, null, 0, 0, 0, 0, (x,y,tx,ty) => ty > -100);
        session.Aim(0,-1);
        Check("empty dodge accepted", session.Act(2).Executed);
        Drive(session,.4f);
        Check("empty dodge moves forward", session.PlayerPoint.Y < -50 && Math.Abs(session.PlayerPoint.X) < .01f);
        Check("dodge keeps foot body outside wall", session.PlayerPoint.Y - WorldCombatSession.PlayerRadiusWorld * .5f > -100);
        var away = Basic(100,0,out var p,out var f);
        away.Act(2); Drive(away,.4f);
        Check("targeted dodge retreats from enemy", away.PlayerPoint.X < -60);
    }

    static void UnlearnedFreeCastIsRefused()
    {
        var p = new CombatActorState("player", "试剑", 200, 30, 2, 80);
        var session = new WorldCombatSession(p,null,null,new SaveData(),new WorldCombatPoint(0,0),new WorldCombatPoint(0,0),null);
        Check("empty casting still requires study", !session.Act(4).Executed && p.Stamina == 80);
    }

    static void LockedPointCanBeEvaded()
    {
        // 起手期间往回跑，妖追过来就离开了自己的锁点。
        var miss = Basic(50f, 0f, out var player, out var foe, null, 240f, .2f);
        Check("thunder cast accepted", miss.Act(5).Executed);
        var lockPoint = miss.FoePoint;
        miss.SetPlayerPosition(0f, -260f);
        Drive(miss, 1.0f);
        Check("locked point is evadable", foe.Hp == foe.MaxHp, "foe hp " + foe.Hp);
        Check("evading foe left the lock", Distance(miss.FoePoint, lockPoint) > PlayerSkillCombat.ThunderRadius * 100f,
            "moved " + Distance(miss.FoePoint, lockPoint));
    }

    static bool Wall(float x) { return x < 60f; }
    static bool Crosses(float x0, float x1) { return Wall(x0) != Wall(x1); }

    static void BarrierBlocksMeleeAndChase()
    {
        var session = Basic(100f, 0f, out var player, out var foe, (x0, y0, x1, y1) => !Crosses(x0, x1), 200f, .5f);
        session.Act(0);
        Drive(session, .35f);
        Check("barrier blocks the strike", foe.Hp == foe.MaxHp, "foe hp " + foe.Hp);
        Check("blocked strike still says why", session.LastMessage.Contains("够不着"), session.LastMessage);
        Drive(session, 1.2f);
        Check("foe cannot walk through the wall", session.FoePoint.X > 60f, "foe x " + session.FoePoint.X);

        var blocked = session.SetPlayerPosition(200f, 0f);
        Check("player cannot cross the wall either", blocked.X < 60f, "player x " + blocked.X);
    }

    static void BarrierStopsTheProjectile()
    {
        var session = Basic(300f, 0f, out var player, out var foe, (x0, y0, x1, y1) => !Crosses(x0, x1), 0f, 4f);
        session.Act(4);
        Drive(session, 1.2f);
        Check("projectile stops at the wall", foe.Hp == foe.MaxHp, "foe hp " + foe.Hp);
    }

    static void CooldownRefusesSpam()
    {
        var session = Basic(300f, 0f, out var player, out var foe, null, 0f, 4f);
        Check("first cast accepted", session.Act(4).Executed);
        Drive(session, 1.2f);
        Check("cooldown is still running", session.Cooldown("sword") > 0f);
        var again = session.Act(4);
        Check("second cast refused", !again.Executed, again.Reason);
        Check("refusal names the cooldown", again.Reason.Contains("调息"), again.Reason);

        var bare = new CombatActorState("player", "试剑", 200, 30, 2, 80);
        var bareFoe = new CombatActorState("check-foe", "试妖", 80, 12, 2, 80);
        var unlearned = new WorldCombatSession(bare, bareFoe, Profile(), new SaveData(),
            new WorldCombatPoint(0f, 0f), new WorldCombatPoint(300f, 0f), null);
        var refused = unlearned.Act(4);
        Check("unlearned skill refused", !refused.Executed && refused.Reason.Contains("习得"), refused.Reason);
    }

    static void VictorySettlesExactlyOnce()
    {
        var session = Basic(100f, 0f, out var player, out var foe);
        CombatRules.RestoreHp(foe, 4);
        for (var i = 0; i < 60 && !session.Settled; i++)
        {
            if (!session.Settled) session.Act(1);
            session.Tick(.05f);
        }
        Check("death settles the session", session.Settled, "foe hp " + foe.Hp);
        var deaths = 0;
        while (session.TryDequeueEvent(out var evt)) if (evt.Id == "death") deaths++;
        Check("death event emitted once", deaths == 1, "deaths " + deaths);
        Check("settled session stops acting", !session.Act(0).Executed);
    }

    static void EventsCarryTheOwningPoints()
    {
        var session = Basic(100f, 0f, out var player, out var foe);
        session.Act(0);
        var cast = default(WorldCombatEvent);
        Check("cast event queued", session.TryDequeueEvent(out cast) && cast.Id == "cast:light", cast.Id);
        Check("cast extends along attack facing", cast.Origin.X == session.PlayerPoint.X && cast.Target.X == 125 && cast.Target.Y == 0);

        Drive(session, .6f);
        var hit = default(WorldCombatEvent);
        while (session.TryDequeueEvent(out var evt)) if (evt.Id == "hit:foe") hit = evt;
        Check("hit runs player -> foe", hit.Id == "hit:foe" && hit.Origin.X == session.PlayerPoint.X &&
            hit.Target.X == session.FoePoint.X, hit.Id + " " + hit.Origin.X + "->" + hit.Target.X);
        Check("event point is the hit point", session.EventPoint.X == session.FoePoint.X);
    }

    static void CancelAbortsAndIsImmediatelyReusable()
    {
        var session = Basic(300f, 0f, out var player, out var foe);
        session.Act(4);
        Drive(session, .3f);
        var sky = player.Phase;
        Check("cast is mid flight", sky == CombatPhase.Startup || sky == CombatPhase.Active, sky.ToString());
        var hpBefore = foe.Hp;
        session.Cancel();
        Check("cancel clears the active skill", session.ActiveSkill.Length == 0);
        Check("cancel keeps the session open", !session.Settled);
        Check("cancel does not touch hp", foe.Hp == hpBefore, "foe hp " + foe.Hp);
        Check("cancel resets the phase", player.Phase == CombatPhase.Idle, player.Phase.ToString());
        Check("cancel does not revive", player.Hp > 0);
        Check("can act right after cancel", session.Act(0).Executed, session.LastMessage);
    }

    static void ConstructorKeepsCurrentHp()
    {
        var player = new CombatActorState("player", "试剑", 200, 30, 2, 80);
        CombatRules.RestoreHp(player, 55);
        var foe = new CombatActorState("check-foe", "试妖", 80, 12, 2, 80);
        var session = Build(player, foe, Profile(), 0f, 0f, 100f, 0f);
        Check("constructor keeps current hp", session.Player.Hp == 55, "hp " + session.Player.Hp);
    }

    static void BossKeepsItsFootprint()
    {
        var player = new CombatActorState("player", "试剑", 200, 30, 2, 80);
        var foe = new CombatActorState(BossCombatBrain.EnemyId, "山君", 400, 18, 8, 130);
        var session = Build(player, foe, Boss(), 0f, 0f, 300f, 0f);
        Check("boss footprint is wider", session.FoeRadius > session.PlayerRadius, session.FoeRadius.ToString());
        var at = session.SetPlayerPosition(300f, 0f);
        var gap = Distance(at, session.FoePoint);
        Check("cannot stand inside the boss", gap >= session.Separation, "gap " + gap);

        // 窄口：岩壁贴着 boss 的半个身位，boss 追击时脚底偏置仍要全部通过。
        var walled = Build(new CombatActorState("player", "试剑", 200, 30, 2, 80),
            new CombatActorState(BossCombatBrain.EnemyId, "山君", 400, 18, 8, 130), Boss(), 0f, 0f, 400f, 0f,
            (x0, y0, x1, y1) => Math.Abs(y0) < 30f && Math.Abs(y1) < 30f);
        Drive(walled, 1.5f);
        Check("boss stays inside the corridor", Math.Abs(walled.FoePoint.Y) < 31f, "y " + walled.FoePoint.Y);
    }

    static void TelegraphCannotCrossAWall()
    {
        // 岩壁在 x=60，玩家在 0：落石只能砸到墙面，够不到人。
        var player = new CombatActorState("player", "试剑", 200, 30, 2, 80);
        var foe = new CombatActorState(BossCombatBrain.EnemyId, "山君", 400, 18, 8, 130);
        var session = Build(player, foe, Boss(), 0f, 0f, 300f, 0f, (x0, y0, x1, y1) => !Crosses(x0, x1));
        var seen = false;
        var hp = player.Hp;
        for (var i = 0; i < 200; i++)
        {
            session.Tick(.02f);
            if (session.TelegraphRadius > 0f) seen = true;
            if (session.LastEvent == "hit:player") break;
        }
        Check("a telegraph happened", seen);
        Check("wall shields the player", player.Hp == hp, "hp " + player.Hp);
    }

    static void BigStepMatchesSmallSteps()
    {
        WorldCombatSession Run(bool big)
        {
            var session = Basic(400f, 0f, out var _, out var _, null, 200f, .5f);
            if (big) for (var i = 0; i < 8; i++) session.Tick(.25f);
            else Drive(session, 2f);
            return session;
        }
        var big = Run(true);
        var small = Run(false);
        Check("big steps equal small steps", Distance(big.FoePoint, small.FoePoint) < .001f,
            Distance(big.FoePoint, small.FoePoint).ToString());
    }

    static void BadDeltasAreIgnored()
    {
        var session = Basic(400f, 0f, out var player, out var foe, null, 200f, .5f);
        var before = session.FoePoint;
        session.Tick(float.NaN);
        session.Tick(float.PositiveInfinity);
        session.Tick(0f);
        Check("bad deltas leave the clock alone", session.Now == 0f, "now " + session.Now);
        Check("bad deltas leave the field alone", Distance(before, session.FoePoint) < .001f);
        session.Tick(9999f);
        Check("huge delta still advances", session.Now > 0f, "now " + session.Now);
        Check("huge delta does not tunnel through the stop gap",
            Distance(session.PlayerPoint, session.FoePoint) >= session.Separation - .01f,
            "gap " + Distance(session.PlayerPoint, session.FoePoint));
    }

    static float Distance(WorldCombatPoint a, WorldCombatPoint b)
    {
        var dx = a.X - b.X; var dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }
}
