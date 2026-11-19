using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TwelveJade.Core;

// Independent checks against unmodified production Core. No Unity or test navigation implementation.
static class Program
{
    static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
    static readonly List<object> Checks = new List<object>();
    static readonly List<string> Failures = new List<string>();
    static string output, mapText, plotText;
    static int sequence;
    static void Emit(object value)
    {
        string line = JsonSerializer.Serialize(value, Options);
        Console.WriteLine(line);
        File.AppendAllText(Path.Combine(output, "checks.jsonl"), line + Environment.NewLine);
    }
    static void Check(string name, bool pass, string detail = "")
    {
        var row = new { kind = "check", name, pass, detail };
        Checks.Add(row); if (!pass) Failures.Add(name); Emit(row);
    }
    static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    static float Number(JsonValue p, string key)
    {
        if (p[key].kind != JsonValue.Kind.Number) throw new FormatException("Expected numeric " + key);
        float value = p[key].AsFloat(float.NaN);
        if (!float.IsFinite(value)) throw new FormatException("Non-finite " + key);
        return value;
    }
    static TownMap Fresh() => TownMap.Parse(mapText);

    static int Main(string[] args)
    {
        if (args.Length != 3) { Console.Error.WriteLine("Usage: TownNavigatorChecks <config-directory> <new-evidence-directory> <label>"); return 2; }
        string config = Path.GetFullPath(args[0]); output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
        { Console.Error.WriteLine("Evidence directory must be new/empty; no overwrites."); return 2; }
        Directory.CreateDirectory(output);
        string mapPath = Path.Combine(config, "town-map.json"), plotsPath = Path.Combine(config, "building-plots.json");
        string mapSha = Sha(mapPath), plotsSha = Sha(plotsPath);
        Emit(new { kind = "input", label = args[2], mapPath, mapSHA256 = mapSha, plotsPath, plotsSHA256 = plotsSha,
            core = "Real TownMap/TownElevation/TownNavigator/Json; frozen build manifest held by runner", noUnity = true });
        mapText = File.ReadAllText(mapPath); plotText = File.ReadAllText(plotsPath);
        Run("current-map-and-plots", CurrentMapChecks);
        Run("original-ElevationChecks-17", SeventeenUnitLedge);
        Run("original-ElevationChecks-16", SixteenUnitExternalGap);
        Run("blocked-only-16-fallback", BlockedOnlyGap);
        Run("sealed-wall-and-cliff", SealedWallAndCliff);
        Run("npc-exclusion-path-context", NpcPathContext);
        Check("frozen configuration bytes unchanged", Sha(mapPath) == mapSha && Sha(plotsPath) == plotsSha);
        var summary = new { status = Failures.Count == 0 ? "PASS" : "FAIL", label = args[2],
            passed = Checks.Count - Failures.Count, failed = Failures.Count, failures = Failures,
            checks = Checks, scope = "Independent navigation regression only; existing 52/22 NPC suites not rerun; Unity not run", overall = "NOT_APPROVED" };
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Emit(new { kind = "summary", summary.status, summary.label, summary.passed, summary.failed, summary.overall });
        return Failures.Count == 0 ? 0 : 1;
    }
    static void Run(string name, Action action)
    { try { action(); } catch (Exception e) { Check(name + " unexpected exception", false, e.ToString()); } }

    static List<object> RegisterPlots(TownMap map)
    {
        // Mirrors WorldPropLayer.PlaceAll: blocks=false skips; capsule wins over rects; no collider inflation.
        const string source = "world-prop"; var registration = new List<object>();
        var plots = Json.Parse(plotText)["plots"].Items; int capsules = 0, rectangles = 0, skipped = 0, fallback = 0;
        foreach (var p in plots)
        {
            string id = p["id"].AsString("");
            if (p.Has("blocks") && !p["blocks"].AsBool(true)) { skipped++; continue; }
            float x = Number(p, "footX"), y = Number(p, "footY");
            if (p.Has("collisionCapsule"))
            {
                var c = p["collisionCapsule"];
                float x0 = x + Number(c, "x0"), x1 = x + Number(c, "x1"), cy = y + Number(c, "y"), radius = Number(c, "radius");
                map.RegisterFootCapsule(source, x0, x1, cy, radius); capsules++;
                registration.Add(new { id, shape = "capsule", x0, x1, y = cy, radius });
            }
            else if (p.Has("collisionRects"))
            {
                foreach (var r in p["collisionRects"].Items)
                {
                    float x0 = x + Number(r, "x0"), x1 = x + Number(r, "x1"), y0 = y + Number(r, "y0"), y1 = y + Number(r, "y1");
                    map.RegisterBlocker(source, x0, y0, x1, y1); rectangles++;
                    registration.Add(new { id, shape = "rectangle", x0, x1, y0, y1 });
                }
            }
            else
            {
                float width = Number(p, "width"), depth = p["collisionDepth"].AsFloat(0), radius = p["radius"].AsFloat(0);
                if (depth <= 0) depth = radius > 0 ? radius * 2 : 24;
                float x0 = x - width / 2, x1 = x + width / 2, y0 = y + Math.Max(0, p["collisionTop"].AsFloat(0)), y1 = y + depth;
                map.RegisterBlocker(source, x0, y0, x1, y1); rectangles++; fallback++;
                registration.Add(new { id, shape = "fallback-rectangle", x0, x1, y0, y1 });
            }
        }
        Check("current 100 plots register exact 79 capsules and 147 rectangles", plots.Count == 100 && capsules == 79 && rectangles == 147 && skipped == 0 && fallback == 0 && map.ExternalBlockerCount(source) == 226,
            "capsules=" + capsules + " rectangles=" + rectangles + " skipped=" + skipped + " fallback=" + fallback);
        File.WriteAllText(Path.Combine(output, "registered-plots-" + (++sequence) + ".json"), JsonSerializer.Serialize(registration, Options));
        return registration;
    }

    static List<(float x, float y)> Route(string name, TownMap map, (float x, float y) start, (float x, float y) target, bool expectedFound = true, string exclude = null)
    {
        bool startStand = map.CanStand(start.x, start.y, exclude), targetStand = map.CanStand(target.x, target.y, exclude);
        var watch = Stopwatch.StartNew();
        var path = exclude == null ? TownNavigator.FindPath(map, start.x, start.y, target.x, target.y) : TownNavigator.FindPath(map, start.x, start.y, target.x, target.y, exclude);
        watch.Stop();
        var edges = new List<object>(); var previous = start; bool valid = path != null && path.Count > 0;
        if (path != null) foreach (var next in path)
        {
            bool traverse = map.CanTraverse(previous.x, previous.y, next.x, next.y, exclude);
            bool stand = map.CanStand(next.x, next.y, exclude); valid &= traverse && stand;
            edges.Add(new { from = previous, to = next, traversable = traverse, standable = stand }); previous = next;
        }
        bool exactTarget = path != null && path.Count > 0 && Math.Abs(previous.x - target.x) < .01f && Math.Abs(previous.y - target.y) < .01f;
        Emit(new { kind = "route", name, start, target, excludeNpcId = exclude, startStand, targetStand, expectedFound,
            found = path != null && path.Count > 0, elapsedMs = watch.Elapsed.TotalMilliseconds,
            allEdgesValid = valid, exactTarget, path, edges });
        Check(name, expectedFound ? startStand && targetStand && valid && exactTarget : path == null,
            expectedFound ? "Requires nonempty route, exact target and every real CanTraverse/CanStand; null is never vacuous PASS." : "Requires null route; collision/terrain refusal must remain intact.");
        return path;
    }

    static void CurrentMapChecks()
    {
        var map = Fresh(); RegisterPlots(map);
        var start = (x: 4223.05029296875f, y: 3003.5576171875f);
        foreach (var target in new[] { (3000f, 2090f), (3420f, 2090f), (3800f, 2090f) })
            Route("actual south start -> ancestral " + target.Item1, map, start, target);
        Check("north bridge direct movement remains legal", map.CanTraverse(4236, 3000, 4236, 2200) && map.CanTraverse(4236, 2200, 4236, 3000));
        Route("actual south start -> north bridge", map, start, (4236, 2200));
        Route("north bridge -> actual south start", map, (4236, 2200), start);
        Check("north bridge to eastern ancestral candidate direct edge", map.CanTraverse(4236, 2200, 3800, 2090));
        float riverY = map.Elevation.RiverY(4700);
        Check("water is not standable and cannot be swept across", !map.CanStand(4700, riverY) && !map.CanTraverse(4700, riverY - 250, 4700, riverY + 250));
        Route("water target refused", map, (4700, 3000), (4700, riverY), false);
        Route("water start refused", map, (4700, riverY), start, false);
        Check("ancestral cliff cannot be climbed directly either way", !map.CanTraverse(3000, 2370, 3000, 2350) && !map.CanTraverse(3000, 2350, 3000, 2370));
        var gate = Json.Parse(plotText)["plots"].Items.Single(p => p["id"].AsString("") == "plot-town-gate-south");
        var pier = gate["collisionRects"].Items[0];
        var center = (Number(gate, "footX") + (Number(pier, "x0") + Number(pier, "x1")) / 2,
            Number(gate, "footY") + (Number(pier, "y0") + Number(pier, "y1")) / 2);
        Check("actual gate pier interior remains blocked", !map.CanStand(center.Item1, center.Item2));
        Route("gate pier target refused", map, start, center, false);
        Route("gate pier start refused", map, center, (3120, 2352), false);
        var capPlot = Json.Parse(plotText)["plots"].Items.Single(p => p["id"].AsString("") == "plot-ancestral-lion-west");
        var cap = capPlot["collisionCapsule"];
        var capCenter = (Number(capPlot, "footX") + (Number(cap, "x0") + Number(cap, "x1")) / 2, Number(capPlot, "footY") + Number(cap, "y"));
        Check("actual lion capsule blocks center and crossing", !map.CanStand(capCenter.Item1, capCenter.Item2) && !map.CanTraverse(capCenter.Item1, capCenter.Item2 - 30, capCenter.Item1, capCenter.Item2 + 30));
        Route("lion capsule target refused", map, start, capCenter, false);
    }

    static void SeventeenUnitLedge()
    {
        var map = Fresh(); RegisterPlots(map);
        // Exact original ElevationChecks.RunNavigationWithPlots endpoints and collision context.
        Route("original17 ancestral ledge forward", map, (3450, 2452), (3120, 2352));
        Route("original17 ancestral ledge reverse", map, (3120, 2352), (3450, 2452));
    }
    static void SixteenUnitExternalGap()
    {
        var map = Fresh();
        // Original test clears placed-object blockers before creating these exact two rectangles.
        map.RegisterBlocker("navigation-regression", 1000, 7300, 1200, 7490);
        map.RegisterBlocker("navigation-regression", 1000, 7506, 1200, 7700);
        var route = Route("original16 external narrow gap", map, (980, 7480), (1220, 7480));
        Check("original16 uses the open strip, endpoints not in gap", route != null && route.Any(p => p.y > 7490 && p.y < 7506));
    }

    static TownMap Small(string blocked = "[]", string npcs = "[]", string elevation = "") => TownMap.Parse(
        "{\"name\":\"navigation regression fixture\",\"artWidth\":640,\"artHeight\":640,\"worldWidth\":640,\"worldHeight\":640," +
        "\"spawn\":{\"x\":80,\"y\":80},\"walkable\":[{\"x\":0,\"y\":0,\"width\":640,\"height\":640}],\"props\":[],\"npcs\":" + npcs + ",\"blocked\":" + blocked + elevation + "}");
    static void BlockedOnlyGap()
    {
        // The wall spans the whole map vertically. Its only opening is y=(297,313), with no 32-unit row.
        // No external collider or elevation boundary can refine this gap in the coarse stage.
        var map = Small("[{\"x\":240,\"y\":0,\"width\":160,\"height\":297},{\"x\":240,\"y\":313,\"width\":160,\"height\":327}]");
        Check("blocked-only gap fixture has no external or elevation refinement", map.Elevation.IsFlat && map.ExternalBlockerCount("world-prop") == 0 && map.Blocked.Count == 2);
        Check("blocked-only16 contains no regular32 row", !Enumerable.Range(0, 21).Any(i => i * 32 > 297 && i * 32 < 313));
        Check("blocked-only16 is open but direct endpoints blocked", map.CanTraverse(220, 305, 420, 305) && !map.CanTraverse(200, 280, 440, 280));
        var route = Route("blocked-only16 fallback route", map, (200, 280), (440, 280));
        Check("blocked-only16 path visits the only open row band", route != null && route.Any(p => p.y > 297 && p.y < 313));
        // Reflection diagnoses which production phase ran; it never substitutes a navigation implementation.
        var search = typeof(TownNavigator).GetMethod("Search", BindingFlags.Static | BindingFlags.NonPublic);
        if (search == null) Emit(new { kind = "phase-diagnostic", name = "blocked-only16", status = "BASELINE_HAS_NO_SPLIT_SEARCH" });
        else
        {
            var coarse = (List<(float x, float y)>)search.Invoke(null, new object[] { map, 200f, 280f, 440f, 280f, null, false });
            var full = (List<(float x, float y)>)search.Invoke(null, new object[] { map, 200f, 280f, 440f, 280f, null, true });
            Check("blocked-only16 proves coarse miss and original-grid fallback hit", coarse == null && full != null && full.Count > 0);
            Emit(new { kind = "phase-diagnostic", name = "blocked-only16", coarseFound = coarse != null, fullFound = full != null, full });
        }
    }
    static void SealedWallAndCliff()
    {
        var wall = Small("[{\"x\":240,\"y\":0,\"width\":160,\"height\":640}]");
        Check("sealed blocked wall cannot be swept through", !wall.CanTraverse(200, 280, 440, 280));
        Route("sealed blocked wall has no route", wall, (200, 280), (440, 280), false);
        string elevation = ",\"terrainShape\":{\"river\":{\"baseY\":1000,\"waterHalf\":30,\"wave\":[]},\"mainRoad\":{\"baseX\":600,\"amp\":0,\"periodY\":1000,\"half\":50},\"bridges\":[]}," +
            "\"elevation\":{\"version\":1,\"stepHeight\":24,\"defaultLevel\":0,\"maxStepHeight\":5,\"sampleDistance\":4,\"regions\":[{\"id\":\"sealed-cliff\",\"shape\":\"rect\",\"level\":2,\"feather\":0,\"rect\":[240,200,400,400]}]}";
        var cliff = Small(elevation: elevation);
        Check("cliff fixture endpoints standable but edge rejected", cliff.CanStand(200, 300) && cliff.CanStand(300, 300) && !cliff.CanTraverse(200, 300, 300, 300));
        Route("isolated 48-height cliff has no route", cliff, (200, 300), (300, 300), false);
    }
    static void NpcPathContext()
    {
        // Targeted route-context checks only; parent already ran the existing 52/22 patrol suites.
        const string npcs = "[{\"id\":\"npc-self\",\"name\":\"Self\",\"art\":\"a\",\"height\":170,\"x\":200,\"y\":200,\"radius\":22}," +
            "{\"id\":\"npc-other\",\"name\":\"Other\",\"art\":\"a\",\"height\":170,\"x\":330,\"y\":200,\"radius\":22}]";
        var map = Small(npcs: npcs);
        Route("NPC default own-center route refused", map, (200, 200), (440, 200), false);
        Route("NPC self-excluded path routes around other NPC", map, (200, 200), (440, 200), true, "npc-self");
        Route("NPC self exclusion cannot target another NPC", map, (200, 200), (330, 200), false, "npc-self");
        Route("NPC unknown exclusion refuses own center", map, (200, 200), (440, 200), false, "unknown");
        Check("NPC self exclusion keeps other-circle sweep blocked", !map.CanTraverse(200, 200, 440, 200, "npc-self"));
        Check("NPC dynamic self relocation accepted", map.SetNpcPosition("npc-self", 200, 400));
        Route("NPC dynamic self-excluded route", map, (200, 400), (440, 200), true, "npc-self");
        Route("NPC dynamic center still blocks default route", map, (200, 400), (440, 200), false);
        Check("NPC vacated anchor released", map.CanStand(200, 200));
    }
}
