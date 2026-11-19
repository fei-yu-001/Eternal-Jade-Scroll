using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    /// <summary>起手蓄光：脚下微亮，光点向手位收拢。</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class WorldCombatFxCaster : WorldCombatFxGraphic
    {
        int style;
        public void SetStyle(int value) => style = value;

        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();
            float k = Ease; if (k <= .002f) return;
            float spread = style == 1 ? 96 : style == 2 ? 66 : 48;
            GroundRing(v, 12 + spread * (.35f + .65f * (1 - t)), (12 + spread * (.35f + .65f * (1 - t))) * .32f, 2.6f,
                new Color(tint.r, tint.g, tint.b, .34f * k));
            int count = style == 1 ? 11 : 7;
            for (int i = 0; i < count; i++)
            {
                float seed = Hash(i * 37 + 5);
                float a = (seed * 360f + t * 110f * (i % 2 == 0 ? 1 : -1)) * Mathf.Deg2Rad;
                float reach = spread * (1 - t * .8f) * (.5f + .5f * Hash(i * 11 + 3));
                var from = new Vector2(Mathf.Cos(a) * reach, Mathf.Sin(a) * reach * .38f + 8);
                var to = new Vector2(Mathf.Cos(a) * 9, 26 + seed * 44 * (.4f + .6f * t));
                Line(v, from, to, 2.2f + 1.6f * (1 - t), new Color(tint.r, tint.g, tint.b, .5f * k));
            }
            float core = (style == 1 ? 30 : 19) * (.45f + .75f * t);
            Ellipse(v, new Vector2(0, 16 + 44 * t), core * .5f, core, new Color(1, .97f, .86f, .26f * k));
        }
    }

    /// <summary>剑弧 / 闪身残影：局部 +x 指向目标，弧面在起手侧张开。</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class WorldCombatFxSlash : WorldCombatFxGraphic
    {
        int style;
        public void SetStyle(int value) => style = value;

        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();
            float k = Ease; if (k <= .002f) return;
            float reach = style == 1 ? radius * .78f : radius;
            float span = Mathf.Lerp(1.2f, .5f, t);
            for (int i = 0; i < 18; i++)
            {
                float a = Mathf.Lerp(-span * .5f, span * .5f, i / 18f);
                float b = Mathf.Lerp(-span * .5f, span * .5f, (i + 1) / 18f);
                float taper = Mathf.Sin((i + 1f) / 19f * Mathf.PI);
                float thick = (style == 1 ? 11f : 17f) * taper * (1 - t * .5f);
                var soft = new Color(tint.r, tint.g, tint.b, .34f * k * taper);
                ArcBand(v, reach - 6 - thick * .5f, reach + thick * .5f, a, b, soft);
                if (taper > .55f) ArcBand(v, reach - 1.4f, reach + 1.4f, a, b, new Color(1, .98f, .88f, .5f * k * taper));
            }
            for (int i = 0; i < 4; i++)
            {
                float drift = (Hash(i * 19 + 7) - .5f) * 26;
                float len = reach * (.5f + .45f * t);
                Line(v, new Vector2(6, drift * .4f), new Vector2(len, drift * (1 - t)), 1.6f,
                    new Color(tint.r, tint.g, tint.b, .3f * k * (1 - t)));
            }
        }
    }

    /// <summary>命中：碎石、金火与地面震环，一次 Play 一簇。</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class WorldCombatFxHit : WorldCombatFxGraphic
    {
        int style; // 0 命中碎石金火, 1 只推地面震环（Boss 蓄力预警不吃爆炸）
        public void SetStyle(int value) => style = value;

        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();
            float k = Ease; if (k <= .002f) return;
            float ring = 14 + radius * 1.05f * Out;
            GroundRing(v, ring, ring * .38f, 3.4f + 2 * (1 - t), new Color(tint.r, tint.g, tint.b, .40f * k * (1 - t * .7f)));
            if (style == 1)
            {
                GroundRing(v, radius * (.55f + .45f * t), radius * (.55f + .45f * t) * .36f, 2.2f,
                    new Color(tint.r, tint.g, tint.b, .34f * k));
                for (int i = 0; i < 6; i++)
                {
                    float a = (Hash(i * 71 + 5) * 360f) * Mathf.Deg2Rad;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * .36f);
                    Line(v, d * radius * .2f, d * radius * (.55f + .4f * k), 1.6f, new Color(tint.r, tint.g, tint.b, .30f * k));
                }
                return;
            }
            Ellipse(v, new Vector2(0, 20), radius * .30f * (1 - t * .4f), radius * .30f * (1 - t * .4f) * (1 - t) + 2,
                new Color(1, .98f, .9f, .42f * (1 - t)));
            int chips = boss ? 13 : 9;
            for (int i = 0; i < chips; i++)
            {
                float seed = Hash(i * 53 + 11);
                float a = (seed * 360f + i * 7f) * Mathf.Deg2Rad;
                float travel = radius * (.35f + .75f * seed) * Out;
                var at = new Vector2(Mathf.Cos(a) * travel, Mathf.Sin(a) * travel * .55f + 22 + 26 * seed * Out - 24 * t * t);
                var n = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * 3.2f;
                var up = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * .5f;
                Quad(v, at - n, at + n * .3f + up * 5f, at + n * .3f - up * 5f, at - up * 4f,
                    new Color(.36f + .3f * seed, .34f + .2f * seed, .28f + .12f * seed, .8f * (1 - t * .6f)));
            }
            float flame = radius * (.34f + .5f * In);
            Ellipse(v, new Vector2(0, 26 + 10 * t), flame * .48f, flame, new Color(tint.r, tint.g, tint.b, .22f * k));
            Ellipse(v, new Vector2(0, 30 + 14 * t), flame * .22f, flame * .6f, new Color(1, .93f, .68f, .30f * k));
        }
    }

    /// <summary>雷光：自天而降的折线电弧与落点白闪。</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class WorldCombatFxColumn : WorldCombatFxGraphic
    {
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();
            float k = Mathf.Clamp01(1 - t); if (k <= .002f) return;
            const float height = 178;
            var previous = new Vector2((Hash(1) - .5f) * 22, height);
            for (int i = 1; i <= 7; i++)
            {
                float f = i / 7f;
                var at = new Vector2((Hash(i * 31 + 9) - .5f) * 30 * (1 - f * .7f), height * (1 - f));
                if (Hash(i * 17 + 3) > .18f) Line(v, previous, at, 3.4f - i * .28f, new Color(tint.r, tint.g, tint.b, .72f * k));
                if (i == 3 || i == 5)
                    Line(v, at, at + new Vector2((Hash(i * 7) - .5f) * 70, -34), 1.7f, new Color(tint.r, tint.g, tint.b, .38f * k));
                previous = at;
            }
            Ellipse(v, Vector2.zero, radius * .55f * (1 - t * .35f), radius * .2f, new Color(1, .98f, .9f, .34f * k));
            GroundRing(v, radius * .9f, radius * .34f, 2.6f, new Color(tint.r, tint.g, tint.b, .45f * k));
        }
    }

    /// <summary>浮起碎光：0 青莲瓣、1 清心微光、2 落败灰烬。</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class WorldCombatFxMote : WorldCombatFxGraphic
    {
        int style;
        public void SetStyle(int value) => style = value;

        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();
            float k = Ease; if (k <= .002f) return;
            int count = style == 0 ? 7 : 9;
            for (int i = 0; i < count; i++)
            {
                float seed = Hash(i * 41 + 13), seed2 = Hash(i * 23 + 5);
                float a = (seed * 360f + t * 40f) * Mathf.Deg2Rad;
                float spread = radius * (.25f + .6f * seed2);
                var at = new Vector2(Mathf.Cos(a) * spread * (1 - t * .35f), 12 + (34 + 96 * seed) * t);
                float sway = Mathf.Sin((t * 2 + seed) * Mathf.PI) * 9;
                var fade = new Color(tint.r, tint.g, tint.b, .62f * k * (1 - t * .6f));
                if (style == 0)
                {
                    var dir = new Vector2(sway * .1f, 1).normalized;
                    var side = new Vector2(-dir.y, dir.x) * 5.2f;
                    var tip = at + dir * 15f + side * .4f;
                    Quad(v, at - side * .32f, at + side, tip + side * .28f, tip - side * .55f, fade);
                    Line(v, at, tip, .9f, new Color(1, 1, 1, .28f * k));
                }
                else if (style == 1) Line(v, at, at + new Vector2(sway * .3f, 8), 2.2f, fade);
                else Ellipse(v, at + new Vector2(sway, 0), 2.6f, 2.6f, new Color(.52f, .52f, .48f, .5f * k * (1 - t)));
            }
            if (style == 0)
                GroundRing(v, radius * (.5f + .4f * t), radius * (.5f + .4f * t) * .34f, 2f, new Color(tint.r, tint.g, tint.b, .26f * k));
        }
    }

    /// <summary>Boss 地面预警：固定半径的双环、裂纹与进度弧。</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class WorldCombatFxTelegraph : WorldCombatFxGraphic
    {
        float progress, zone = 120;
        public void Show(Vector2 foot, float size, float value, bool heavy)
        {
            radius = zone = Mathf.Max(24, size); progress = Mathf.Clamp01(value); boss = heavy;
            var rect = rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(.5f, 0);
            rect.sizeDelta = new Vector2(NodeSize, NodeSize); rect.anchoredPosition = foot;
            enabled = true; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();
            var danger = boss ? new Color(1, .42f, .26f, 1) : new Color(1, .62f, .30f, 1);
            Ellipse(v, new Vector2(0, 2), zone, zone * .36f, new Color(danger.r, danger.g, danger.b, .13f + .07f * progress));
            GroundRing(v, zone, zone * .36f, 3.2f, new Color(danger.r, danger.g, danger.b, .62f));
            GroundRing(v, zone * .62f, zone * .22f, 1.4f, new Color(danger.r, danger.g, danger.b, .30f));
            for (int i = 0; i < 7; i++)
            {
                float a = (Hash(i * 29 + 3) * 360f) * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * .36f);
                Line(v, d * zone * .18f, d * zone * (1 - .06f * Hash(i + 2)), 1.7f, new Color(danger.r, danger.g, danger.b, .34f));
            }
            float sweep = progress * Mathf.PI * 2;
            for (int i = 0; i < 26; i++)
            {
                float a = Mathf.PI * .5f - sweep * i / 26f, b = Mathf.PI * .5f - sweep * (i + 1) / 26f;
                var da = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * .36f); var db = new Vector2(Mathf.Cos(b), Mathf.Sin(b) * .36f);
                var c = new Color(1, .88f, .56f, .9f);
                Triangle(v, da * (zone - 2.6f), da * (zone + 2.6f), db * (zone + 2.6f), c);
                Triangle(v, da * (zone - 2.6f), db * (zone + 2.6f), db * (zone - 2.6f), c);
            }
        }
    }
}
