using TwelveJade.Combat;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Two actual canvas layers: rear casting glow, foreground telegraph/impact. Shared locked cast geometry.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class BossDomainEffects : MaskableGraphic
    {
        BossCombatBrain brain;
        CombatPhase phase;
        float clock, bossX, center, unit, ground;
        bool front, reduced, alive;
        public bool WarningVisible => isActiveAndEnabled && alive && front && brain != null && brain.Pending && phase == CombatPhase.Startup;
        public void Configure(bool foreground, float centerX, float pxPerUnit, float groundY)
        { front = foreground; center = centerX; unit = pxPerUnit; ground = -groundY; }
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        public void Sample(BossCombatBrain value, CombatActorState actor, float now, bool low, bool settled)
        {
            brain = value; phase = actor?.Phase ?? CombatPhase.Dead; bossX = actor?.X ?? 0;
            clock = now; reduced = low; alive = actor != null && !actor.IsDead && !settled; SetVerticesDirty();
        }
        Vector2 Point(float x, float y = 0) => new Vector2(center + x * unit, ground + y);
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear(); if (!alive || brain == null) return;
            var jade = brain.Stage == 2 ? new Color(.74f, .91f, .47f, .8f) : new Color(.38f, .88f, .76f, .8f);
            bool charge = brain.Pending && phase == CombatPhase.Startup;
            bool active = brain.Pending && phase == CombatPhase.Active;
            float p = charge ? brain.StartupProgress(clock) : brain.ActiveProgress(clock);
            if (!front)
            {
                // A grounded perspective ring, behind the body. Optional pulse never moves its foot.
                float pulse = reduced ? 1 : 1 + .025f * Mathf.Sin(clock * 2);
                Ellipse(v, Point(bossX, 5), 155 * pulse, 27, 3, new Color(jade.r, jade.g, jade.b, charge ? .64f : .26f));
                Ellipse(v, Point(bossX, 5), 170 * pulse, 32, 10, new Color(jade.r, jade.g, jade.b, charge ? .13f : .06f));
                if (charge)
                {
                    Ellipse(v, Point(bossX, 160), 105, 125, 4, new Color(jade.r, jade.g, jade.b, .12f + p * .19f));
                    for (int i = 0; i < 5; i++)
                    {
                        float x = bossX + (i - 2) * .22f;
                        Line(v, Point(x, 20), Point(x, 30 + p * (105 + i * 10)), 2, new Color(jade.r, jade.g, jade.b, .35f));
                    }
                }
                return;
            }
            if (!charge && !active) return;
            float min = brain.ZoneMin, max = brain.ZoneMax;
            var danger = new Color(1, .52f, .22f, charge ? .22f : .38f);
            // All bounds are the actual immutable gate. Front layer keeps it legible across the large body.
            Line(v, Point(min, 2), Point(max, 2), 22, danger);
            Line(v, Point(min, -10), Point(max, -10), 3, new Color(1, .80f, .46f, .95f));
            Line(v, Point(min, -16), Point(min, 17), 4, jade);
            Line(v, Point(max, -16), Point(max, 17), 4, jade);
            if (charge)
            {
                Line(v, Point(min, 6), Point(Mathf.Lerp(min, max, p), 6), 4, jade);
                if (brain.Skill == BossSkill.FallingStone)
                {
                    var foot = Point(brain.TargetX, 5);
                    Ellipse(v, foot, BossCombatBrain.StoneRadius * unit, 24, 3, jade);
                    Line(v, foot + new Vector2(-12, -12), foot + new Vector2(12, 12), 3, jade);
                    Line(v, foot + new Vector2(-12, 12), foot + new Vector2(12, -12), 3, jade);
                    // Materialising stone stays overhead in startup, already locked to the real target.
                    Rock(v, Point(brain.TargetX, 370 - p * 190), 38 + p * 16, new Color(.25f, .31f, .25f, .45f + p * .45f), jade);
                }
                return;
            }
            if (brain.Skill == BossSkill.Sweep)
            {
                var at = Point(brain.OriginX, 145); float reach = Mathf.Abs(max - min) * unit;
                for (int i = 0; i < 30; i++)
                {
                    float a = Mathf.Lerp(-45, 50, i / 30f) * Mathf.Deg2Rad;
                    float b = Mathf.Lerp(-45, 50, (i + 1) / 30f) * Mathf.Deg2Rad;
                    var x = at + new Vector2(brain.Facing * Mathf.Cos(a) * reach, Mathf.Sin(a) * 120);
                    var y = at + new Vector2(brain.Facing * Mathf.Cos(b) * reach, Mathf.Sin(b) * 120);
                    Line(v, x, y, reduced ? 5 : 13, new Color(.77f, .93f, .67f, .55f));
                    Line(v, x, y, 3, new Color(1, .94f, .70f, .95f));
                }
            }
            else if (brain.Skill == BossSkill.Fissure)
            {
                float wave = brain.WaveX(clock);
                var a = Point(brain.OriginX, 0); var b = Point(wave, 0);
                for (int i = 0; i < 14; i++)
                {
                    var x = Vector2.Lerp(a, b, i / 14f) + Vector2.up * (i % 2 == 0 ? 0 : 9);
                    var y = Vector2.Lerp(a, b, (i + 1) / 14f) + Vector2.up * (i % 2 == 0 ? 9 : 0);
                    Line(v, x, y, 9, new Color(.1f, .15f, .10f, .95f)); Line(v, x, y, 3, jade);
                }
                float height = reduced ? 66 : 80 + Mathf.Sin(p * Mathf.PI) * 36;
                Rock(v, Point(wave, height * .35f), height * .5f, new Color(.2f, .25f, .18f, .92f), jade);
                Ellipse(v, Point(wave, 2), BossCombatBrain.WaveRadius * unit, 14, 4, jade);
            }
            else if (brain.Skill == BossSkill.FallingStone)
            {
                // Damage is at Active onset; rock is already grounded, then dissolves, never a delayed visual impact.
                Rock(v, Point(brain.TargetX, 48), 62, new Color(.22f, .27f, .20f, 1 - p * .7f), jade);
                Ellipse(v, Point(brain.TargetX, 2), BossCombatBrain.StoneRadius * unit, 27, reduced ? 4 : 8, jade);
                for (int i = 0; i < 5; i++)
                {
                    float x = brain.TargetX + (i - 2) * .16f;
                    Line(v, Point(x, 3), Point(x + (i - 2) * .05f, reduced ? 28 : 35 + (1-p) * 50), 3, jade);
                }
            }
        }
        void Rock(VertexHelper v, Vector2 at, float size, Color shade, Color edge)
        {
            Vector2[] points = { new Vector2(-.68f,-.4f), new Vector2(-.9f,.1f), new Vector2(-.45f,.85f), new Vector2(.3f,1), new Vector2(.85f,.2f), new Vector2(.5f,-.5f) };
            int start = v.currentVertCount; v.AddVert(at, new Color(shade.r * 1.45f, shade.g * 1.45f, shade.b * 1.45f, shade.a) * color, Vector2.zero);
            for (int i = 0; i <= points.Length; i++)
            {
                v.AddVert(at + points[i % points.Length] * size, shade * color, Vector2.zero);
                if (i > 0) v.AddTriangle(start, start + i, start + i + 1);
            }
            for (int i = 0; i < points.Length; i++) Line(v, at + points[i]*size, at + points[(i+1)%points.Length]*size, 2, edge);
            Line(v, at + points[2]*size, at, 3, edge); Line(v, at, at + points[4]*size, 3, edge);
        }
        void Ellipse(VertexHelper v, Vector2 at, float rx, float ry, float width, Color tint)
        {
            for (int i = 0; i < 48; i++)
            { float a = i*Mathf.PI/24, b=(i+1)*Mathf.PI/24; Line(v, at+new Vector2(Mathf.Cos(a)*rx,Mathf.Sin(a)*ry),at+new Vector2(Mathf.Cos(b)*rx,Mathf.Sin(b)*ry),width,tint); }
        }
        void Line(VertexHelper v, Vector2 a, Vector2 b, float width, Color tint)
        {
            var d=b-a; if(d.sqrMagnitude < .0001f) return;
            var n=new Vector2(-d.y,d.x).normalized*width*.5f; int k=v.currentVertCount;
            v.AddVert(a-n,tint*color,Vector2.zero);v.AddVert(a+n,tint*color,Vector2.zero);
            v.AddVert(b+n,tint*color,Vector2.zero);v.AddVert(b-n,tint*color,Vector2.zero);
            v.AddTriangle(k,k+1,k+2);v.AddTriangle(k,k+2,k+3);
        }
        protected override void OnDisable() { alive=false; brain=null; base.OnDisable(); }
    }
}
