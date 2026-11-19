using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    /// <summary>
    /// 世界层纯展示特效的公共基座。节点贴在投影后的脚下点、以脚为轴心向上生长，
    /// 只在被 Sample 驱动时重建网格，不加载纹理、不射线、不碰伤害。
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public abstract class WorldCombatFxGraphic : MaskableGraphic
    {
        public const float NodeSize = 420f;
        protected float born, delay, life = 1, t;
        protected float radius = 48;
        protected bool boss, low;
        bool playing;
        protected Color tint = Color.white;

        protected override void Awake() { base.Awake(); raycastTarget = false; }

        public void Begin(float now, float wait, float duration, Vector2 foot, Vector2 target, float size, Color color, bool heavy)
        {
            playing = true;
            born = now; delay = Mathf.Max(0, wait); life = Mathf.Max(.01f, duration);
            radius = size; tint = color; boss = heavy; t = 0;
            var rect = rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(.5f, 0);
            rect.sizeDelta = new Vector2(NodeSize, NodeSize);
            rect.anchoredPosition = foot;
            var delta = target - foot;
            rect.localRotation = Quaternion.Euler(0, 0, delta.sqrMagnitude > .25f ? Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg : 0);
            enabled = true;
            Sync();
            SetVerticesDirty();
        }

        /// <summary>返回是否仍然存活；now 不前进时整片特效静止，暂停即冻结。</summary>
        public virtual bool Advance(float now, bool reduced)
        {
            if (!playing) return false;
            low = reduced;
            float local = now - born - delay;
            if (local < 0) { if (enabled) enabled = false; Sync(); return true; }
            if (local >= life) { Hide(); return false; }
            if (!enabled) enabled = true;
            t = local / life;
            Sync();
            SetVerticesDirty();
            return true;
        }

        /// <summary>子类挂靠的附加表现（如复用纹理表帧）在这里跟着 t 走。</summary>
        protected virtual void Sync() { }

        public void Hide() { playing = false; if (enabled) enabled = false; Sync(); }

        protected float Ease => Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
        protected float Out => 1 - (1 - Mathf.Clamp01(t)) * (1 - Mathf.Clamp01(t));
        protected float In => Mathf.Clamp01(t) * Mathf.Clamp01(t);
        protected static float Hash(int value)
        {
            uint h = (uint)(value * 747796405 + 2891336453);
            h = ((h >> (int)((h >> 28) + 4)) ^ h) * 277803737;
            h = (h >> 22) ^ h;
            return (h & 0xFFFFFF) / 16777216f;
        }

        protected static void Triangle(VertexHelper v, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int i = v.currentVertCount;
            v.AddVert(a, color, Vector2.zero); v.AddVert(b, color, Vector2.zero); v.AddVert(c, color, Vector2.zero);
            v.AddTriangle(i, i + 1, i + 2);
        }

        protected static void Line(VertexHelper v, Vector2 a, Vector2 b, float width, Color color)
        {
            var d = b - a; if (d.sqrMagnitude < .0004f) return;
            var n = new Vector2(-d.y, d.x).normalized * (width * .5f);
            int i = v.currentVertCount;
            v.AddVert(a - n, color, Vector2.zero); v.AddVert(a + n, color, Vector2.zero);
            v.AddVert(b + n, color, Vector2.zero); v.AddVert(b - n, color, Vector2.zero);
            v.AddTriangle(i, i + 1, i + 2); v.AddTriangle(i, i + 2, i + 3);
        }

        protected static void Quad(VertexHelper v, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int i = v.currentVertCount;
            v.AddVert(a, color, Vector2.zero); v.AddVert(b, color, Vector2.zero);
            v.AddVert(c, color, Vector2.zero); v.AddVert(d, color, Vector2.zero);
            v.AddTriangle(i, i + 1, i + 2); v.AddTriangle(i, i + 2, i + 3);
        }

        protected static void Ellipse(VertexHelper v, Vector2 c, float rx, float ry, Color color)
        {
            const int steps = 26;
            for (int i = 0; i < steps; i++)
            {
                float a = i * Mathf.PI * 2 / steps, b = (i + 1) * Mathf.PI * 2 / steps;
                var p = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                var q = new Vector2(Mathf.Cos(b) * rx, Mathf.Sin(b) * ry);
                Triangle(v, c, c + p, c + q, color);
            }
        }

        protected static void GroundRing(VertexHelper v, float rx, float ry, float width, Color color)
        {
            const int steps = 34;
            for (int i = 0; i < steps; i++)
            {
                float a = i * Mathf.PI * 2 / steps, b = (i + 1) * Mathf.PI * 2 / steps;
                var da = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var db = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                var p = new Vector2(da.x * (rx - width), da.y * (ry - width * .42f));
                var q = new Vector2(da.x * rx, da.y * ry);
                var r = new Vector2(db.x * rx, db.y * ry);
                var s = new Vector2(db.x * (rx - width), db.y * (ry - width * .42f));
                Triangle(v, p, q, r, color); Triangle(v, p, r, s, color);
            }
        }

        /// <summary>扇形光带：局部 +x 为朝向，角度按屏幕逆时针。</summary>
        protected static void ArcBand(VertexHelper v, float inner, float outer, float from, float to, Color color)
        {
            const int steps = 12;
            for (int i = 0; i < steps; i++)
            {
                float a = Mathf.Lerp(from, to, i / (float)steps), b = Mathf.Lerp(from, to, (i + 1) / (float)steps);
                var da = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var db = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Triangle(v, da * inner, da * outer, db * outer, color);
                Triangle(v, da * inner, db * outer, db * inner, color);
            }
        }
    }
}
