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
        const int CasterBudget = 6, SlashBudget = 6, ColumnBudget = 3, MoteBudget = 5, SheetBudget = 8, MortalBudget = 18;
        static readonly Color Gold = new Color(1f, .84f, .46f), Teal = new Color(.40f, .86f, .74f),
            InkLight = new Color(.14f, .15f, .16f), InkHeavy = new Color(.11f, .12f, .13f),
            InkDust = new Color(.56f, .52f, .44f), InkHeavyDust = new Color(.44f, .40f, .33f),
            InkPlayerHit = new Color(.56f, .18f, .15f), InkFoeHit = new Color(.14f, .13f, .12f),
            InkHerb = new Color(.34f, .48f, .35f), InkAsh = new Color(.38f, .37f, .34f),
            InkWarn = new Color(.72f, .46f, .24f);
        WorldCombatFxCaster[] casters;
        WorldCombatFxSlash[] slashes;
        WorldCombatFxColumn[] columns;
        WorldCombatFxMote[] motes;
        WorldCombatFxSheet[] sheets;
        WorldCombatMortalFx[] mortals;
        WorldCombatFxTelegraph telegraph;
        Texture2D swordSheet, thunderSheet, claritySheet;
        Func<Vector2, Vector2> project;
        Func<bool> reduced;
        int casterCursor, slashCursor, columnCursor, moteCursor, sheetCursor, mortalCursor;
        bool installed;

        public bool Installed => installed;
        public int ActiveCount { get { int n = 0; n += Count(casters); n += Count(slashes); n += Count(columns); n += Count(motes); n += Count(sheets); n += Count(mortals); return n; } }
        public int MortalActiveCount => Count(mortals);
        public int SheetActiveCount => Count(sheets);
        public int CasterActiveCount => Count(casters);
        public int SlashActiveCount => Count(slashes);
        public int ColumnActiveCount => Count(columns);
        public int MoteActiveCount => Count(motes);
        public int MortalPoolSize => mortals?.Length ?? 0;
        public int ActiveMortalKindCount(MortalFxKind kind)
        {
            int n = 0;
            if (mortals != null) for (int i = 0; i < mortals.Length; i++) if (mortals[i] != null && mortals[i].enabled && mortals[i].Kind == kind) n++;
            return n;
        }
        static int Count(WorldCombatFxGraphic[] pool) { int n = 0; if (pool != null) for (int i = 0; i < pool.Length; i++) if (pool[i] != null && pool[i].enabled) n++; return n; }

        public static WorldCombatEffects Create(UiKit ui, RectTransform worldRoot, Func<Vector2, Vector2> project, Func<bool> reduced)
        {
            if (worldRoot == null) throw new ArgumentNullException(nameof(worldRoot));
            var root = ui.Stretch(worldRoot, "World combat effects");
            var view = root.gameObject.AddComponent<WorldCombatEffects>();
            view.project = project; view.reduced = reduced;
            view.casters = view.Pool<WorldCombatFxCaster>(ui, CasterBudget, "Caster");
            view.slashes = view.Pool<WorldCombatFxSlash>(ui, SlashBudget, "Slash");
            view.columns = view.Pool<WorldCombatFxColumn>(ui, ColumnBudget, "Bolt");
            view.motes = view.Pool<WorldCombatFxMote>(ui, MoteBudget, "Mote");
            view.sheets = view.Pool<WorldCombatFxSheet>(ui, SheetBudget, "Authored frame");
            view.mortals = view.Pool<WorldCombatMortalFx>(ui, MortalBudget, "Mortal ink");
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

        public bool TelegraphVisible => telegraph != null && telegraph.enabled;

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
            var dir = Direction(a, b);
            switch (eventId)
            {
                case "cast:light":
                    MortalFire(now, 0, .20f, a, dir, 36, 4, InkDust, false, MortalFxKind.StepDust);
                    MortalFire(now, .12f, .18f, a, dir, span, 40, InkLight, false, MortalFxKind.LightSlash);
                    break;
                case "cast:heavy":
                    MortalFire(now, 0, .28f, a, dir, 52, 4, InkHeavyDust, boss, MortalFxKind.StepDust);
                    MortalFire(now, .28f, .24f, a, dir, span * 1.08f, 44, InkHeavy, boss, MortalFxKind.HeavySlash);
                    MortalFire(now, .28f, .24f, a + dir * 18f, dir, 46, 4, InkDust, boss, MortalFxKind.StepDust);
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
                // hit 的 target 才是受击者,表现全部落在 b 并沿真实攻击方向扩散。
                case "hit:player":
                    MortalFire(now, 0, .26f, b, dir, boss ? 54 : 42, 34, InkPlayerHit, boss, MortalFxKind.ImpactGrit);
                    break;
                case "hit:foe":
                    MortalFire(now, 0, .28f, b, dir, boss ? 68 : 50, 36, InkFoeHit, boss, MortalFxKind.ImpactGrit);
                    break;
                case "heal":
                    MortalFire(now, 0, .62f, a, Vector2.up, 44, 16, InkHerb, false, MortalFxKind.HerbBreath);
                    break;
                case "dodge":
                    MortalFire(now, 0, .28f, a, dir, span, 12, InkDust, boss, MortalFxKind.StepDust);
                    break;
                // 预警只画地面蓄势,不误作命中。
                case "warn:boss":
                    MortalFire(now, 0, .48f, b, Vector2.right, Mathf.Max(56, span), 2, InkWarn, true, MortalFxKind.BossWarnRing);
                    break;
                case "death":
                    MortalFire(now, 0, .78f, a, dir, 52, 6, InkAsh, boss, MortalFxKind.DefeatAsh);
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

        void MortalFire(float now, float wait, float life, Vector2 foot, Vector2 direction, float size, float lift, Color tint, bool boss, MortalFxKind kind)
        {
            var node = mortals[mortalCursor]; mortalCursor = (mortalCursor + 1) % mortals.Length;
            node.BeginMortal(now, wait, life, foot, direction, boss ? size * 1.18f : size, lift, tint, boss, kind);
        }

        static Vector2 Direction(Vector2 from, Vector2 to)
        {
            var d = to - from;
            return d.sqrMagnitude > .25f ? d.normalized : Vector2.right;
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
            Step(casters, now, low); Step(slashes, now, low);
            Step(columns, now, low); Step(motes, now, low); Step(sheets, now, low);
            Step(mortals, now, low);
        }

        static void Step(WorldCombatFxGraphic[] pool, float now, bool low)
        {
            if (pool == null) return;
            for (int i = 0; i < pool.Length; i++) pool[i]?.Advance(now, low);
        }

        public void Clear()
        {
            Hide(casters); Hide(slashes); Hide(columns); Hide(motes); Hide(sheets);
            Hide(mortals);
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
            casters = null; slashes = null; columns = null; motes = null; sheets = null;
            mortals = null;
            swordSheet = null; thunderSheet = null; claritySheet = null;
            telegraph = null; installed = false;
        }
    }
}
