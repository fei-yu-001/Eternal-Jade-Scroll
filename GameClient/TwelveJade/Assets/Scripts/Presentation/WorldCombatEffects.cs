using System;
using UnityEngine;

namespace TwelveJade.Presentation
{
    /// <summary>
    /// 地图内战斗的纯展示层：只画，不结算。控制器自己判命中后调 Play("hit:...")，
    /// 这里不改血量/精力。全部节点在 Create 时一次建好并复用，固定预算、零纹理加载。
    /// 时间只由 Sample(now) 推进，now 不动即整层冻结；Clear 收干净。
    /// </summary>
    public sealed class WorldCombatEffects : MonoBehaviour
    {
        const int CasterBudget = 6, SlashBudget = 6, HitBudget = 10, ColumnBudget = 3, MoteBudget = 5, SheetBudget = 8;
        static readonly Color Gold = new Color(1f, .84f, .46f), Ember = new Color(1f, .60f, .32f),
            Blood = new Color(.95f, .45f, .36f), Teal = new Color(.40f, .86f, .74f), Pale = new Color(.84f, .93f, .90f);
        WorldCombatFxCaster[] casters;
        WorldCombatFxSlash[] slashes;
        WorldCombatFxHit[] hits;
        WorldCombatFxColumn[] columns;
        WorldCombatFxMote[] motes;
        WorldCombatFxSheet[] sheets;
        WorldCombatFxTelegraph telegraph;
        Texture2D swordSheet, thunderSheet, claritySheet;
        Func<Vector2, Vector2> project;
        Func<bool> reduced;
        int casterCursor, slashCursor, hitCursor, columnCursor, moteCursor, sheetCursor;
        bool installed;

        public bool Installed => installed;
        public int ActiveCount { get { int n = 0; n += Count(casters); n += Count(slashes); n += Count(hits); n += Count(columns); n += Count(motes); n += Count(sheets); return n; } }
        static int Count(WorldCombatFxGraphic[] pool) { int n = 0; if (pool != null) for (int i = 0; i < pool.Length; i++) if (pool[i] != null && pool[i].enabled) n++; return n; }

        public static WorldCombatEffects Create(UiKit ui, RectTransform worldRoot, Func<Vector2, Vector2> project, Func<bool> reduced)
        {
            if (worldRoot == null) throw new ArgumentNullException(nameof(worldRoot));
            var root = ui.Stretch(worldRoot, "World combat effects");
            var view = root.gameObject.AddComponent<WorldCombatEffects>();
            view.project = project; view.reduced = reduced;
            view.casters = view.Pool<WorldCombatFxCaster>(ui, CasterBudget, "Caster");
            view.slashes = view.Pool<WorldCombatFxSlash>(ui, SlashBudget, "Slash");
            view.hits = view.Pool<WorldCombatFxHit>(ui, HitBudget, "Hit");
            view.columns = view.Pool<WorldCombatFxColumn>(ui, ColumnBudget, "Bolt");
            view.motes = view.Pool<WorldCombatFxMote>(ui, MoteBudget, "Mote");
            view.sheets = view.Pool<WorldCombatFxSheet>(ui, SheetBudget, "Authored frame");
            // 现有特效表只在这里取一次,节点循环复用,运行期不再碰 Resources。
            view.swordSheet = ApprovedUiArt.Load("nov11/skill-sword");
            view.thunderSheet = ApprovedUiArt.Load("nov11/skill-thunder");
            view.claritySheet = ApprovedUiArt.Load("nov11/skill-clarity");
            for (int i = 0; i < view.sheets.Length; i++) view.sheets[i].Attach();
            view.telegraph = view.Node<WorldCombatFxTelegraph>(ui, "Boss zone telegraph");
            root.SetAsLastSibling();
            view.installed = true;
            return view;
        }

        T[] Pool<T>(UiKit ui, int count, string name) where T : WorldCombatFxGraphic
        {
            var pool = new T[count];
            for (int i = 0; i < count; i++) pool[i] = Node<T>(ui, name + " " + i);
            return pool;
        }

        T Node<T>(UiKit ui, string name) where T : WorldCombatFxGraphic
        {
            var rect = ui.Rect(transform, name, 0, 0, WorldCombatFxGraphic.NodeSize, WorldCombatFxGraphic.NodeSize);
            var node = rect.gameObject.AddComponent<T>();
            node.enabled = false;
            return node;
        }

        public void Play(string eventId, Vector2 origin, Vector2 target, float now, bool boss)
        {
            if (project == null || string.IsNullOrEmpty(eventId)) return;
            var a = project(origin);
            var b = project(target);
            if (!Finite(a) || !Finite(b)) return;
            float span = Mathf.Clamp(Vector2.Distance(a, b), 34, 230);
            switch (eventId)
            {
                case "cast:light":
                    Fire(casters, ref casterCursor, now, 0, .12f, a, b, 44, Gold, boss).SetStyle(0);
                    Fire(slashes, ref slashCursor, now, .12f, .22f, a + Vector2.up * 42, b + Vector2.up * 42, span, Gold, false).SetStyle(0);
                    break;
                case "cast:heavy":
                    Fire(casters, ref casterCursor, now, 0, .28f, a, b, 92, Ember, boss).SetStyle(1);
                    Fire(slashes, ref slashCursor, now, .28f, .34f, a + Vector2.up * 45, b + Vector2.up * 45, span * 1.12f, Ember, false).SetStyle(2);
                    SheetFire(now, .28f, a, b, 155, swordSheet, 1, 3, Ember, false, .28f);
                    break;
                // 御剑启动 .22s:蓄光先走,剑到 .22 才出。
                case "cast:sword":
                    Fire(casters, ref casterCursor, now, 0, .22f, a, b, 52, Gold, boss).SetStyle(0);
                    SheetFire(now, .55f, a, b, 155, swordSheet, 0, 4, Gold, false, .22f)?.Travel(a, b);
                    Fire(slashes, ref slashCursor, now, .22f, .28f, a, b, span, Gold, boss).SetStyle(0);
                    break;
                // 五雷启动 .62s:先蓄光,雷在 .62 落到 b。
                case "cast:thunder":
                    Fire(casters, ref casterCursor, now, 0, .62f, a, b, 64, new Color(.62f, .86f, 1f), false).SetStyle(2);
                    Fire(casters, ref casterCursor, now, 0, .62f, b, b, 70, new Color(.52f, .76f, 1f), false).SetStyle(2);
                    SheetFire(now, .38f, b, b, span * 1.15f, thunderSheet, 0, 4, new Color(.86f, .95f, 1f), boss, .62f);
                    Fire(columns, ref columnCursor, now, .62f, .30f, b, b, span * .7f, new Color(.72f, .90f, 1f), boss);
                    break;
                // 清心启动 .45s:起手蓄气,青莲在 .45 开。
                case "cast:clarity":
                    Fire(casters, ref casterCursor, now, 0, .45f, a, b, 54, Teal, boss).SetStyle(3);
                    SheetFire(now, .55f, a, a, 132, claritySheet, 0, 4, Teal, boss, .45f);
                    Fire(motes, ref moteCursor, now, .45f, .70f, a, a, 52, Teal, boss).SetStyle(1);
                    break;
                // hit 的 target 才是受击者,表现全部落在 b。
                case "hit:player":
                    SheetFire(now, .30f, b, b, (boss ? 46 : 38) * 2.4f, thunderSheet, 2, 2, Blood, boss);
                    Fire(hits, ref hitCursor, now, 0, .36f, b, b, boss ? 46 : 38, Blood, boss);
                    Fire(casters, ref casterCursor, now, 0, .24f, b, b, 34, Blood, boss).SetStyle(0);
                    break;
                case "hit:foe":
                    SheetFire(now, .32f, b, b, (boss ? 74 : 52) * 2.3f, swordSheet, 2, 2, Gold, boss);
                    Fire(hits, ref hitCursor, now, 0, .40f, b, b, boss ? 74 : 52, Gold, boss);
                    break;
                case "heal":
                    SheetFire(now, .70f, a, a, 140, claritySheet, 0, 4, Teal, boss);
                    Fire(motes, ref moteCursor, now, 0, .95f, a, a, 58, Teal, boss).SetStyle(0);
                    break;
                case "dodge":
                    Fire(slashes, ref slashCursor, now, 0, .30f, a, b, span, Pale, boss).SetStyle(1);
                    break;
                // 预警只画地面蓄势,不画受击爆炸;真正的爆点是控制器的 hit。
                case "warn:boss":
                    Fire(casters, ref casterCursor, now, 0, .46f, b, b, 70, new Color(1f, .78f, .48f), true).SetStyle(1);
                    Fire(hits, ref hitCursor, now, 0, .52f, b, b, Mathf.Max(58, span), new Color(1f, .62f, .34f), true).SetStyle(1);
                    break;
                case "death":
                    SheetFire(now, .60f, a, a, 124, claritySheet, 2, 2, new Color(.80f, .80f, .74f), boss);
                    Fire(motes, ref moteCursor, now, 0, 1.2f, a, a, 66, new Color(.74f, .72f, .66f), boss).SetStyle(2);
                    Fire(hits, ref hitCursor, now, 0, .5f, a, a, 30, new Color(.55f, .52f, .48f), boss).SetStyle(0);
                    break;
            }
        }

        // 出手的"到点"由 Core 启动相位给,这里只负责延后,不另立时间轴。
        T Fire<T>(T[] pool, ref int cursor, float now, float wait, float life, Vector2 foot, Vector2 aim, float size, Color tint, bool boss)
            where T : WorldCombatFxGraphic
        {
            var node = pool[cursor]; cursor = (cursor + 1) % pool.Length;
            node.Begin(now, wait, life, foot, aim, boss ? size * 1.3f : size, tint, boss);
            return node;
        }

        WorldCombatFxSheet SheetFire(float now, float life, Vector2 foot, Vector2 aim, float size, Texture2D texture, int first, int frames, Color tint, bool boss, float wait = 0)
        {
            if (texture == null) return null;
            var node = sheets[sheetCursor]; sheetCursor = (sheetCursor + 1) % sheets.Length;
            node.Begin(now, wait, life, foot, aim, boss ? size * 1.3f : size, tint, boss);
            node.SetSheet(texture, first, frames, boss ? size * 1.3f : size);
            return node;
        }

        static bool Finite(Vector2 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y);

        /// <summary>Boss 地面预警：visible=false 时收起。半径是真实判定半径,不做展示放大。</summary>
        public void Telegraph(Vector2 center, float radius, float progress, bool visible, bool boss)
        {
            if (telegraph == null) return;
            if (!visible || project == null) { telegraph.Hide(); return; }
            var foot = project(center);
            if (!Finite(foot)) { telegraph.Hide(); return; }
            telegraph.Show(foot, radius, progress, boss);
        }

        public void Sample(float now)
        {
            // 世界根会按脚底深度重排 placeables；本层每次自愈到最后一个兄弟位,
            // 保证特效压在地面/人物之上,而 UI 在别的画布上,天然不被遮。
            var parent = transform.parent;
            if (parent != null && transform.GetSiblingIndex() != parent.childCount - 1) transform.SetAsLastSibling();
            bool low = reduced != null && reduced();
            Step(casters, now, low); Step(slashes, now, low); Step(hits, now, low);
            Step(columns, now, low); Step(motes, now, low); Step(sheets, now, low);
        }

        static void Step(WorldCombatFxGraphic[] pool, float now, bool low)
        {
            if (pool == null) return;
            for (int i = 0; i < pool.Length; i++) pool[i]?.Advance(now, low);
        }

        public void Clear()
        {
            Hide(casters); Hide(slashes); Hide(hits); Hide(columns); Hide(motes); Hide(sheets);
            if (telegraph != null) telegraph.Hide();
        }

        static void Hide(WorldCombatFxGraphic[] pool)
        {
            if (pool == null) return;
            for (int i = 0; i < pool.Length; i++) pool[i]?.Hide();
        }

        void OnDestroy()
        {
            Clear();
            project = null; reduced = null;
            casters = null; slashes = null; hits = null; columns = null; motes = null; sheets = null;
            swordSheet = null; thunderSheet = null; claritySheet = null;
            telegraph = null; installed = false;
        }
    }
}
