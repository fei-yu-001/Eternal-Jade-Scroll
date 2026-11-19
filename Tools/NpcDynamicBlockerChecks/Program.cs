using System;
using System.Collections.Generic;
using System.Text.Json;
using TwelveJade.Core;
using State = TwelveJade.Core.NpcLocalPatrol.PatrolState;

// 窄回归：NPC 动态圆心 + 按 ID 自排除。只覆盖本次改动的行为面，不重跑全套地图。
static class Program
{
    static readonly List<object> checks = new List<object>();
    static int failed;
    static void Check(string name, bool pass, string detail = "")
    { checks.Add(new { name, pass, detail }); if (!pass) failed++; }

    const string SelfNpc = "{\"id\":\"npc-self\",\"name\":\"Self\",\"art\":\"a\",\"height\":170,\"x\":200,\"y\":200,\"radius\":22}";
    const string OtherNpc = "{\"id\":\"npc-other\",\"name\":\"Other\",\"art\":\"a\",\"height\":170,\"x\":330,\"y\":200,\"radius\":22}";
    const string Prop = "{\"slug\":\"jar\",\"x\":500,\"y\":500,\"height\":60,\"radius\":18}";

    static TownMap Map(string npcs) => TownMap.Parse(
        "{\"name\":\"dynamic blocker check\",\"artWidth\":640,\"artHeight\":640," +
        "\"spawnX\":80,\"spawnY\":80,\"walkable\":[{\"x\":0,\"y\":0,\"width\":640,\"height\":640}]," +
        "\"blocked\":[],\"props\":[" + Prop + "],\"npcs\":[" + npcs + "]}");

    static float Distance(float ax, float ay, float bx, float by) =>
        (float)Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));

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
        DefaultStaticCompatibility();
        SelfExclusionOnlySkipsOneId();
        DynamicCentreMovesTheWall();
        SweepNarrowGapAndLongSegment();
        FailureBehaviour();
        ResetAndTwoNpcUpdates();
        RealPatrolWithRealNavigator();
    }

    // 1. 不传末参的旧调用语义零变化：NPC 圆仍按配置锚点挡人。
    static void DefaultStaticCompatibility()
    {
        var map = Map(SelfNpc + "," + OtherNpc);
        Check("default CanStand blocks both NPC anchors", !map.CanStand(200, 200) && !map.CanStand(330, 200));
        Check("default CanStand keeps props blocking", !map.CanStand(500, 500));
        Check("default CanStand free point open", map.CanStand(120, 120));
        Check("default CanTraverse crosses NPC circle", !map.CanTraverse(150, 200, 260, 200));
        Check("default FindPath refuses own circle as start", TownNavigator.FindPath(map, 200, 200, 260, 260) == null);
        bool same = true;
        for (int i = 0; i < 40; i++)
        {
            float x = 40 + i * 15, y = 60 + (i % 7) * 60;
            same &= map.CanStand(x, y) == map.CanStand(x, y, null);
            same &= map.CanTraverse(120, 120, x, y) == map.CanTraverse(120, 120, x, y, null);
        }
        Check("absent exclude arg equals explicit null", same);
        Check("no dynamic centre in a fresh map", map.NpcDynamicPositionCount == 0);
    }

    // 2. 排除只跳过匹配的那一个 ID；未知/空 ID 不排除任何人（fail-closed）。
    static void SelfExclusionOnlySkipsOneId()
    {
        var map = Map(SelfNpc + "," + OtherNpc);
        Check("self excluded at own centre", map.CanStand(200, 200, "npc-self"));
        Check("self exclusion keeps other NPC blocking", !map.CanStand(330, 200, "npc-self"));
        Check("other id exclusion does not free self", !map.CanStand(200, 200, "npc-other"));
        Check("unknown exclude id excludes nobody", !map.CanStand(200, 200, "ghost") && !map.CanStand(200, 200, ""));
        Check("self excluded sweep leaves other NPC blocking", !map.CanTraverse(200, 200, 330, 200, "npc-self"));
        Check("self excluded sweep reaches free point", map.CanTraverse(200, 200, 260, 200, "npc-self"));
        var path = TownNavigator.FindPath(map, 200, 200, 260, 260, "npc-self");
        Check("navigator walks out of own circle", path != null && path.Count > 0 &&
            path[path.Count - 1] == (260, 260));
    }

    // 3. 旧位空气墙释放，新位真实阻挡。
    static void DynamicCentreMovesTheWall()
    {
        var map = Map(SelfNpc + "," + OtherNpc);
        Check("move accepted", map.SetNpcPosition("npc-self", 200, 400) && map.NpcDynamicPositionCount == 1);
        Check("old anchor released", map.CanStand(200, 200) && map.CanTraverse(100, 200, 300, 200));
        Check("new centre blocks standing", !map.CanStand(200, 400));
        Check("new centre blocks sweep", !map.CanTraverse(200, 300, 200, 500));
        Check("other NPC untouched by self move", !map.CanStand(330, 200));
        Check("TryNpcPosition reads live centre", map.TryNpcPosition("npc-self", out var lx, out var ly) && lx == 200 && ly == 400);
        Check("TryNpcPosition falls back to anchor", map.TryNpcPosition("npc-other", out var ox, out var oy) && ox == 330 && oy == 200);
    }

    // 4. 真实扫掠：长段穿圆必挡；6 单位窄缝可过、但整段横扫不过。
    static void SweepNarrowGapAndLongSegment()
    {
        var map = Map(SelfNpc + "," + OtherNpc);
        map.RegisterBlocker("wall", 418, 150, 430, 250);
        map.SetNpcPosition("npc-self", 390, 200);
        Check("long segment through moved circle refused", !map.CanTraverse(390, 40, 390, 360));
        Check("long segment beside moved circle passes", map.CanTraverse(365, 40, 365, 360));
        Check("narrow gap itself is open", map.CanStand(415, 200) && map.CanTraverse(415, 120, 415, 280));
        Check("sweep across circle plus wall refused", !map.CanTraverse(380, 200, 440, 200));
        Check("wall still blocks after dynamic move", !map.CanTraverse(400, 160, 440, 160));
        Check("rectangle blocker untouched by exclusion", !map.CanTraverse(424, 100, 424, 300));
    }

    // 5. 未知 ID / 非有限 / 出世界：明确 false，且不产生半个实体。
    static void FailureBehaviour()
    {
        var map = Map(SelfNpc + "," + OtherNpc);
        Check("unknown id rejected", !map.SetNpcPosition("nobody", 100, 100) && map.NpcDynamicPositionCount == 0);
        Check("unknown id has no position", !map.TryNpcPosition("nobody", out _, out _));
        Check("unknown clear rejected", !map.ClearNpcPosition("nobody"));
        map.SetNpcPosition("npc-self", 240, 240);
        Check("NaN rejected without overwrite", !map.SetNpcPosition("npc-self", float.NaN, 200) &&
            map.TryNpcPosition("npc-self", out var nx, out var ny) && nx == 240 && ny == 240);
        Check("+Infinity rejected", !map.SetNpcPosition("npc-self", float.PositiveInfinity, 200));
        Check("-Infinity rejected", !map.SetNpcPosition("npc-self", 200, float.NegativeInfinity));
        Check("out-of-world rejected", !map.SetNpcPosition("npc-self", -5, 200) && !map.SetNpcPosition("npc-self", 700, 200));
        Check("empty id rejected", !map.SetNpcPosition("", 200, 200) && !map.SetNpcPosition(null, 200, 200));
        Check("rejected writes leave count at one", map.NpcDynamicPositionCount == 1);
    }

    // 6. 两个 NPC 同时动态；单清恢复一个锚点，全清恢复全部。
    static void ResetAndTwoNpcUpdates()
    {
        var map = Map(SelfNpc + "," + OtherNpc);
        Check("two updates accepted", map.SetNpcPosition("npc-self", 250, 250) &&
            map.SetNpcPosition("npc-other", 300, 250) && map.NpcDynamicPositionCount == 2);
        Check("both old anchors released", map.CanStand(200, 200) && map.CanStand(330, 200));
        Check("both new centres block", !map.CanStand(250, 250) && !map.CanStand(300, 250));
        Check("single clear restores one anchor", map.ClearNpcPosition("npc-self") &&
            map.CanStand(200, 200) == false && !map.CanStand(300, 250) && map.NpcDynamicPositionCount == 1);
        map.ClearNpcPositions();
        Check("clear all restores every anchor", map.NpcDynamicPositionCount == 0 &&
            !map.CanStand(200, 200) && !map.CanStand(330, 200) && map.CanStand(250, 250));
        var other = Map(OtherNpc);
        Check("state is per-instance, no static residue", other.NpcDynamicPositionCount == 0 &&
            !other.CanStand(330, 200) && other.CanStand(250, 250));
    }

    // 7. 真实 NpcLocalPatrol + 真实 TownNavigator：从自身圆心起步，绕开别人。
    static void RealPatrolWithRealNavigator()
    {
        var map = Map(SelfNpc + "," + OtherNpc);
        Check("legacy path from own centre is null (old bug)", TownNavigator.FindPath(map, 200, 200, 260, 320) == null);
        var patrol = new NpcLocalPatrol(200, 200, 120, 40, .25f, 23,
            (sx, sy, tx, ty) => TownNavigator.FindPath(map, sx, sy, tx, ty, "npc-self"),
            (x0, y0, x1, y1) => map.CanTraverse(x0, y0, x1, y1, "npc-self"));
        bool started = false, valid = true, minSeparation = true, tracked = true;
        string firstBad = null;
        float travelled = 0f; int moves = 0, blocked = 0;
        for (int i = 0; i < 1200; i++)
        {
            float px = patrol.X, py = patrol.Y;
            float moved = patrol.Advance(.05f, true);
            float actual = Distance(px, py, patrol.X, patrol.Y);
            travelled += actual;
            if (moved > 0)
            {
                moves++;
                // 宿主契约：实际位移成功后才把圆心写回地图，导航回调不回写。
                tracked &= map.SetNpcPosition("npc-self", patrol.X, patrol.Y);
            }
            bool stepOk = Math.Abs(moved - actual) < .0001f &&
                map.CanTraverse(px, py, patrol.X, patrol.Y, "npc-self") &&
                map.CanStand(patrol.X, patrol.Y, "npc-self");
            if (!stepOk && firstBad == null)
                firstBad = "i=" + i + " from(" + px + "," + py + ") to(" + patrol.X + "," + patrol.Y +
                    ") moved=" + moved + " actual=" + actual +
                    " traverse=" + map.CanTraverse(px, py, patrol.X, patrol.Y, "npc-self") +
                    " standSelf=" + map.CanStand(patrol.X, patrol.Y, "npc-self") +
                    " stand=" + map.CanStand(patrol.X, patrol.Y) + " state=" + patrol.State;
            valid &= stepOk;
            minSeparation &= Distance(patrol.X, patrol.Y, 330, 200) > 22f;
            // 自排除后，自己的圆心可站立；其他 NPC 依旧挡人。
            valid &= map.CanStand(patrol.X, patrol.Y) == false || i == 0;
            if (patrol.State == State.Blocked) blocked++;
            started |= patrol.State == State.Walking || patrol.State == State.Waiting;
        }
        Check("patrol actually walks from its own centre", moves > 50 && travelled > 100f, "moves=" + moves + " travelled=" + travelled.ToString("F1"));
        Check("every step passes real traversal with self excluded", valid, firstBad ?? "");
        Check("never enters other NPC circle", minSeparation);
        Check("dynamic centre follows host writes", tracked && map.TryNpcPosition("npc-self", out var fx, out var fy) &&
            Distance(fx, fy, patrol.X, patrol.Y) < .0001f);
        Check("other NPC still blocks while self moves", !map.CanStand(330, 200, "npc-self"));
        Check("legacy query at live centre still blocks (player sees NPC)", !map.CanStand(patrol.X, patrol.Y));
        Check("patrol reached walking state", started);
        map.ClearNpcPositions();
        Check("town exit clears dynamic centres", map.NpcDynamicPositionCount == 0);
        patrol.Advance(.05f, false);
        Check("leaving town stops patrol permanently", patrol.State == State.Stopped && patrol.Advance(.05f, true) == 0f);
    }
}