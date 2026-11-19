using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TwelveJade.Presentation;

// 窄几何检查：落叶漂流只验"整叶在水带内、避真实桥/码头排除区、方向、长帧、冻结、上限、重建"。
static class Program
{
    static readonly List<object> checks = new List<object>();
    static int failed;
    static void Check(string name, bool pass, string detail = "")
    { checks.Add(new { name, pass, detail }); if (!pass) failed++; }

    static int Main()
    {
        try { Run(); } catch (Exception e) { Check("unexpected exception", false, e.ToString()); }
        Console.WriteLine(JsonSerializer.Serialize(new { status = failed == 0 ? "PASS" : "FAIL", checks },
            new JsonSerializerOptions { WriteIndented = true }));
        return failed == 0 ? 0 : 1;
    }

    static void Run()
    {
        var json = File.ReadAllText(Environment.GetEnvironmentVariable("TOWN_WATER_MAP") ?? "../../GameClient/TwelveJade/Assets/Resources/Config/town-map.json");
        var real = TownWaterGeometry.FromTownMapJson(json);
        if (Environment.GetEnvironmentVariable("LEAF_REGRESSION_ONLY") == "1") { RegressionChecks.Run(Check, real); return; }
        Check("real map has bridge/ferry exclusions", real.Exclusions.Length > 0, "exclusions=" + real.Exclusions.Length);

        var field = new TownWaterLeafField(real, 20261106);
        Check("leaf budget capped at 24", field.Count <= TownWaterLeafField.MaxLeaves, "count=" + field.Count);
        Check("all leaves spawn inside the water band", AllLegal(field, out var bad), bad);
        Check("flow direction follows the definition", Math.Abs(field.Direction - Math.Sign(real.FlowDirection)) < .001f);

        var cutouts = new TownWaterGeometry.Exclusion[real.Exclusions.Length];
        for (int i = 0; i < cutouts.Length; i++) cutouts[i] = real.Exclusions[i].Expanded(real.DeckClearance);
        bool everVisibleOnDeck = false, everOutOfBand = false, everNaNs = false;
        int maxVisible = 0;
        for (int step = 0; step < 90 * 60; step++)
        {
            field.Advance(1f / 60f, false);
            int visible = 0;
            for (int i = 0; i < field.Count; i++)
            {
                if (!field.TryGetLeaf(i, out var leaf)) continue;
                if (leaf.Visibility > .02f) visible++;
                if (float.IsNaN(leaf.X) || float.IsNaN(leaf.Across) || float.IsInfinity(leaf.X)) everNaNs = true;
                if (Math.Abs(leaf.Across) + leaf.Radius > field.HalfWidth + .001f) everOutOfBand = true;
                if (leaf.Visibility > .02f && OnDeck(cutouts, real, leaf)) everVisibleOnDeck = true;
            }
            if (visible > maxVisible) maxVisible = visible;
            if (visible > TownWaterLeafField.MaxLeaves) everOutOfBand = true;
        }
        Check("90s at 60fps: every leaf stays inside the water band", !everOutOfBand);
        Check("90s at 60fps: no leaf visibly crosses a bridge or deck", !everVisibleOnDeck);
        Check("90s at 60fps: no NaN/Infinity positions", !everNaNs);
        Check("visible leaves stay within the cap", maxVisible <= TownWaterLeafField.MaxLeaves, "maxVisible=" + maxVisible);

        // 长帧：一次 8 秒也必须分步消费，不能从桥这头跳到那头。
        var longField = new TownWaterLeafField(real, 7);
        longField.Advance(8f, false);
        Check("single 8s frame cannot tunnel through a bridge", AllLegal(longField, out var longBad), longBad);

        // 反向水流：同一条河，方向取负就必须往负 X 漂。
        var reversed = new TownWaterGeometry.Definition
        {
            WorldWidth = 2000f, WorldHeight = 2000f, BaseY = 1000f, HalfWidth = 120f, BankInset = 10f,
            DeckClearance = 4f, FlowDirection = -1f, Waves = Array.Empty<TownWaterGeometry.Wave>(),
            Exclusions = Array.Empty<TownWaterGeometry.Exclusion>()
        };
        var reverseField = new TownWaterLeafField(reversed, 11);
        reverseField.TryGetLeaf(0, out var before);
        reverseField.Advance(4f, false);
        reverseField.TryGetLeaf(0, out var after);
        Check("reverse flow drifts toward -X", after.X < before.X, before.X + " -> " + after.X);

        // reduceMotion：位置、摆动、相位、透明度全部不许推进。
        var frozen = new TownWaterLeafField(real, 20261106);
        frozen.Advance(2f, false);
        frozen.TryGetLeaf(0, out var beforeFreeze);
        float frozenElapsed = frozen.Elapsed;
        for (int i = 0; i < 120; i++) frozen.Advance(1f / 60f, true);
        frozen.TryGetLeaf(0, out var afterFreeze);
        Check("reduceMotion freezes position and visibility", beforeFreeze.X == afterFreeze.X &&
            beforeFreeze.Across == afterFreeze.Across && beforeFreeze.Visibility == afterFreeze.Visibility);
        Check("reduceMotion freezes the phase clock", frozen.Elapsed == frozenElapsed, frozenElapsed + " -> " + frozen.Elapsed);

        // 固定 seed 可复现；重建换 count 必须真换，且旧实例不残留（新对象从 0 起算）。
        var a = new TownWaterLeafField(real, 20261106);
        var b = new TownWaterLeafField(real, 20261106);
        a.Advance(6f, false);
        b.Advance(6f, false);
        a.TryGetLeaf(3, out var la); b.TryGetLeaf(3, out var lb);
        Check("same seed reproduces the same drift", la.X == lb.X && la.Across == lb.Across && la.Visibility == lb.Visibility);
        var rebuilt = new TownWaterLeafField(real, 99, 8);
        Check("new field honours the new count and restarts the clock (not Unity Configure)", rebuilt.Count == 8 && rebuilt.Elapsed == 0f);
        var capped = new TownWaterLeafField(real, 5, 1000);
        Check("requested count above the cap is clamped", capped.Count == TownWaterLeafField.MaxLeaves, "count=" + capped.Count);
        RegressionChecks.Run(Check, real);
    }

    static bool AllLegal(TownWaterLeafField field, out string detail)
    {
        for (int i = 0; i < field.Count; i++)
        {
            if (!field.TryGetLeaf(i, out var leaf)) { detail = "leaf " + i + " missing"; return false; }
            if (!field.IsFootprintLegal(leaf.X, leaf.Across, leaf.Radius))
            { detail = "leaf " + i + " illegal at " + leaf.X + "," + leaf.Across; return false; }
            if (Math.Abs(leaf.Across) + leaf.Radius > field.HalfWidth + .001f)
            { detail = "leaf " + i + " outside band"; return false; }
        }
        detail = "";
        return true;
    }

    static bool OnDeck(TownWaterGeometry.Exclusion[] cutouts, TownWaterGeometry.Definition definition,
        TownWaterLeafField.LeafState leaf)
    {
        float y = definition.CenterY(leaf.X) + leaf.Across;
        float r = leaf.Radius;
        foreach (var hole in cutouts)
        {
            if (leaf.X + r < hole.MinX || leaf.X - r > hole.MaxX) continue;
            if (y + r < hole.MinY || y - r > hole.MaxY) continue;
            return true;
        }
        return false;
    }
}