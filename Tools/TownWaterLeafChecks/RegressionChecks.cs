using System;
using System.Collections.Generic;
using TwelveJade.Presentation;

static class RegressionChecks
{
    const float Bob = 1.2f;
    static readonly float[] U = { .5f, .16f, -.3f, -.5f, -.3f, .16f, 0f };
    static readonly float[] V = { 0f, .24f * .62f, .2f * .62f, 0f, -.2f * .62f, -.24f * .62f, 0f };
    static (float x, float y) Vertex(TownWaterGeometry.Definition d, TownWaterLeafField.LeafState leaf, int id, float elapsed, int vertex)
    {
        double cos = Math.Cos(leaf.Angle), sin = Math.Sin(leaf.Angle);
        float bob = (float)Math.Sin(id * 1.7f + elapsed * 1.9f) * Bob;
        float u = U[vertex] * leaf.Size, v = V[vertex] * leaf.Size;
        return (leaf.X + (float)(u * cos - v * sin), d.CenterY(leaf.X) + leaf.Across - (float)(u * sin + v * cos) - bob);
    }
    public static void Run(Action<string, bool, string> check, TownWaterGeometry.Definition real)
    {
        var flat = new TownWaterGeometry.Definition { WorldWidth = 1000, WorldHeight = 1000, BaseY = 500, HalfWidth = 50,
            Exclusions = new[] { new TownWaterGeometry.Exclusion(500, 200, 520, 800) } };
        var f = new TownWaterLeafField(flat, 1);
        var tip = new TownWaterLeafField.LeafState(483.5f, 0, 0, 22, 1);
        var vertex = Vertex(flat, tip, 0, 0, 0); var cut = flat.Exclusions[0].Expanded(flat.DeckClearance);
        check("reject footprint whose rendered tip enters expanded deck", cut.Contains(vertex.x, vertex.y) && !f.IsFootprintLegal(tip.X, tip.Across, tip.Radius), "tip=" + vertex + " radius=" + tip.Radius + " cutMinX=" + cut.MinX);
        var bent = new TownWaterGeometry.Definition { WorldWidth = 1000, WorldHeight = 1000, BaseY = 500, HalfWidth = 50,
            Waves = new[] { new TownWaterGeometry.Wave(120, 50, 0) } };
        var bentField = new TownWaterLeafField(bent, 1); var edge = new TownWaterLeafField.LeafState(100, 30, 0, 22, 1);
        vertex = Vertex(bent, edge, 0, 0, 0);
        check("reject curved-bank witness at actual tip X", Math.Abs(vertex.y - bent.CenterY(vertex.x)) > bent.HalfWidth - bent.BankInset && !bentField.IsFootprintLegal(edge.X, edge.Across, edge.Radius), "actualAcross=" + (vertex.y - bent.CenterY(vertex.x)) + " allowed=" + (bent.HalfWidth - bent.BankInset));
        check("footprint encloses tip plus full world-unit bob", tip.Radius >= tip.Size * .5f + Bob, "radius=" + tip.Radius + " required=" + (tip.Size * .5f + Bob));
        check("invalid footprint fails closed", !f.IsFootprintLegal(100, 0, -1) && !f.IsFootprintLegal(100, float.PositiveInfinity, 1), "negative radius / nonfinite across");

        var longFrame = new TownWaterLeafField(real, 7);
        for (int step = 0; step < 120; step++) longFrame.Advance(1f / 60, false);
        var before = Snapshot(longFrame); float elapsed = longFrame.Elapsed; longFrame.Advance(8, false);
        float maxJump = 0; int paired = 0;
        for (int i = 0; i < before.Length; i++) if (longFrame.TryGetLeaf(i, out var next) && before[i].Visibility > .01f && next.Visibility > .01f) { paired++; maxJump = Math.Max(maxJump, Math.Abs(next.X - before[i].X)); }
        check("8s stall cannot hide multi-frame movement or fade/rebirth", paired > 0 && maxJump <= .751f && longFrame.Elapsed - elapsed <= .0501f, "pairedVisible=" + paired + " maxJumpX=" + maxJump + " clockAdvance=" + (longFrame.Elapsed - elapsed));

        var a = new TownWaterLeafField(real, 23); var b = new TownWaterLeafField(real, 23);
        for (int i = 0; i < 120; i++) { a.Advance(1f / 60, false); b.Advance(1f / 60, false); }
        var frozen = Snapshot(a); elapsed = a.Elapsed;
        for (int i = 0; i < 120; i++) a.Advance(8, true);
        check("reduceMotion freezes every leaf angle/position/alpha/clock", Equal(frozen, Snapshot(a)) && a.Elapsed == elapsed, "all " + a.Count + " leaves, 120 stalled callbacks");
        a.Advance(.02f, false); b.Advance(.02f, false);
        check("unfreeze resumes without accumulated backlog", Equal(Snapshot(a), Snapshot(b)) && a.Elapsed == b.Elapsed, "matches an unfrozen control with identical active time");
        a.Advance(float.NaN, false); a.Advance(float.PositiveInfinity, false); a.Advance(-1, false);
        check("invalid dt leaves field unchanged", Equal(Snapshot(a), Snapshot(b)) && a.Elapsed == b.Elapsed, "NaN/Infinity/negative");
        check("fixed budget includes zero/negative requests", new TownWaterLeafField(real, 1, 0).Count == 0 && new TownWaterLeafField(real, 1, -1).Count == 0 && new TownWaterLeafField(real, 1, 1000).Count == 24, "no live-list growth");

        var ribbon = TownWaterGeometry.Build(real); var buckets = new Dictionary<int, List<int>>();
        for (int t = 0; t < ribbon.Indices.Count; t += 3) {
            var p = ribbon.Vertices[ribbon.Indices[t]]; var q = ribbon.Vertices[ribbon.Indices[t+1]]; var r = ribbon.Vertices[ribbon.Indices[t+2]];
            int lo = (int)(Math.Min(p.X, Math.Min(q.X, r.X)) / 32), hi = (int)(Math.Max(p.X, Math.Max(q.X, r.X)) / 32);
            for (int bin = lo; bin <= hi; bin++) { if (!buckets.TryGetValue(bin, out var list)) buckets[bin] = list = new List<int>(); list.Add(t); }
        }
        var moving = new TownWaterLeafField(real, 20261106); bool legal = true, continuous = true, fade = true; int samples = 0, recycled = 0;
        for (int step = 0; step < 240; step++) {
            var previous = Snapshot(moving); moving.Advance(.05f, false);
            for (int i = 0; i < moving.Count; i++) {
                if (!moving.TryGetLeaf(i, out var leaf)) continue;
                float jump = Math.Abs(leaf.X - previous[i].X);
                if (jump > .751f) { recycled++; fade &= leaf.Visibility == 0 && previous[i].Visibility <= .1751f; }
                if (leaf.Visibility <= .01f) continue;
                if (previous[i].Visibility > .01f) continuous &= jump <= .751f;
                for (int v = 0; v < 6; v++) {
                    var p = Vertex(real, leaf, i, moving.Elapsed, v); var q = Vertex(real, leaf, i, moving.Elapsed, (v+1)%6);
                    legal &= InRibbon(p.x, p.y, ribbon, buckets) && InRibbon((p.x+q.x)*.5f, (p.y+q.y)*.5f, ribbon, buckets); samples += 2;
                }
            }
        }
        check("12s real rendered perimeter samples stay in cut ribbon", legal && samples > 0, "actual tips/edge midpoints+world bob; samples=" + samples + "; expanded cutouts=" + ribbon.Cutouts.Length);
        check("recycling is hidden before the new location appears", fade && continuous && recycled > 0, "recycles=" + recycled + "; at most .75 X units between visible frames");
    }
    static TownWaterLeafField.LeafState[] Snapshot(TownWaterLeafField field)
    { var data = new TownWaterLeafField.LeafState[field.Count]; for (int i = 0; i < data.Length; i++) field.TryGetLeaf(i, out data[i]); return data; }
    static bool Equal(TownWaterLeafField.LeafState[] a, TownWaterLeafField.LeafState[] b)
    { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i].X != b[i].X || a[i].Across != b[i].Across || a[i].Angle != b[i].Angle || a[i].Size != b[i].Size || a[i].Visibility != b[i].Visibility) return false; return true; }
    static bool InRibbon(float x, float y, TownWaterGeometry.Ribbon ribbon, Dictionary<int, List<int>> buckets)
    {
        foreach (var cut in ribbon.Cutouts) if (cut.Contains(x,y)) return false;
        if (!buckets.TryGetValue((int)(x / 32), out var list)) return false;
        foreach (int t in list) {
            var a = ribbon.Vertices[ribbon.Indices[t]]; var b = ribbon.Vertices[ribbon.Indices[t+1]]; var c = ribbon.Vertices[ribbon.Indices[t+2]];
            double u = Cross(a,b,x,y), v = Cross(b,c,x,y), w = Cross(c,a,x,y);
            if ((u >= -.01 && v >= -.01 && w >= -.01) || (u <= .01 && v <= .01 && w <= .01)) return true;
        }
        return false;
    }
    static double Cross(TownWaterGeometry.Vertex a, TownWaterGeometry.Vertex b, float x, float y) => (double)(b.X-a.X)*(y-a.Y)-(double)(b.Y-a.Y)*(x-a.X);
}
