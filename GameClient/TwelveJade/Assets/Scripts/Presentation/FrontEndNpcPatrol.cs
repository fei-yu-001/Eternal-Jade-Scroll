using System.Collections.Generic;
using UnityEngine;
using TwelveJade.Core;

namespace TwelveJade.Presentation
{
    // 城镇 NPC 巡游宿主（p2 接线）：一个原 MapNpc ID 一个临时 NpcLocalPatrol。
    //
    // 位置真源只有一条链：巡逻实例 X/Y → 写进 TownMap 动态圆心（碰撞）→ 再同步表现（Rect/BaseY）。
    // 顺序不能反：碰撞没写进去，画面就先走，就是"人走了、空气墙还在"。
    // 圆心写不进去时不硬撑：停掉该实例，画面与碰撞一起留在地图上一次成功的圆心。
    //
    // 活动半径/速度/停留是**局部活动参数**，与 MapNpc.Radius（碰撞半径）无关，两者不许互相冒充。
    // reduceMotion 只管视觉浮动，不参与这里——它不能取消 NPC 的真实活动。
    public sealed partial class FrontEndController
    {
        const float NpcPatrolRadius = 64f;      // 世界单位：小范围踱步，不离自己的摊位
        const float NpcPatrolSpeed = 26f;       // 世界单位/秒：明显慢于玩家步行
        const float NpcPatrolDwellSeconds = 3.2f;

        readonly Dictionary<string, NpcLocalPatrol> npcPatrols = new();
        readonly Dictionary<string, float> npcPatrolHeight = new();
        readonly Dictionary<string, NpcFrameAnimator> npcPatrolAnimators = new();
        readonly Dictionary<string, Placeable> npcPatrolPlaceables = new();
        readonly Dictionary<string, RectTransform> npcHeadMarkers = new();
        readonly HashSet<string> npcDialogueLeases = new();
        bool npcPatrolsLive;

        // ---------------- 取证/接点 ----------------

        /// <summary>验收：某 NPC 的巡逻实例（未建或已离场为 null）。</summary>
        public NpcLocalPatrol NpcPatrolForShot(string npcId) =>
            npcId != null && npcPatrols.TryGetValue(npcId, out var patrol) ? patrol : null;

        /// <summary>验收：这座城镇此刻是否还持有活的巡逻实例（离场/重建期间为 false）。</summary>
        public bool NpcPatrolsLiveForShot => npcPatrolsLive;

        /// <summary>验收：NPC 的立绘 Rect（跟位置同一份，不是另建的影子节点）。离场/重建后旧节点已毁，返回 null。</summary>
        public RectTransform TownNpcRectForShot(string npcId)
        {
            if (npcId == null || !npcRects.TryGetValue(npcId, out var rect) || rect == null) return null;
            return rect;
        }

        /// <summary>验收：NPC 当前纵深排序键（脚底投影 Y）。取不到返回 null。</summary>
        public float? TownNpcBaseYForShot(string npcId)
        {
            if (npcId == null || !npcPatrolPlaceables.TryGetValue(npcId, out var placeable) || placeable == null)
                return null;
            return placeable.BaseY;
        }

        /// <summary>验收：已登记的头标 Rect。头标由对话/任务宿主挂在 NPC Rect 下，
        /// 父节点动它就是动，宿主不另算一份头标坐标。未登记或节点已毁 = null。</summary>
        public RectTransform NpcHeadMarkerForShot(string npcId)
        {
            if (npcId == null || !npcHeadMarkers.TryGetValue(npcId, out var marker) || marker == null) return null;
            return marker;
        }

        /// <summary>对话/任务宿主登记自己的头标节点（可为 null 表示摘除）。
        /// 只登记不重排：位置由父节点 NPC Rect 决定，这里再算一遍就会分裂成两份坐标。</summary>
        public bool AttachNpcHeadMarker(string npcId, RectTransform marker)
        {
            if (string.IsNullOrEmpty(npcId) || FindNpc(npcId) == null) return false;
            if (marker == null) npcHeadMarkers.Remove(npcId);
            else npcHeadMarkers[npcId] = marker;
            return true;
        }

        /// <summary>对话租约：现有对话流程开始/结束时调用。开启即清路线并原地停；
        /// 结束先停留再重新规划。这里不新建对话系统，只借 NpcLocalPatrol 已有的对话暂停语义。</summary>
        public bool SetNpcPatrolDialogue(string npcId, bool active)
        {
            if (string.IsNullOrEmpty(npcId)) return false;
            if (active) npcDialogueLeases.Add(npcId);
            else npcDialogueLeases.Remove(npcId);
            var found = npcPatrols.TryGetValue(npcId, out var patrol);
            patrol?.SetDialogue(active);
            if (active && npcPatrolAnimators.TryGetValue(npcId, out var animator) && animator != null)
                animator.SetMovement(Vector2.zero);
            return found;
        }

        // ---------------- 生命周期 ----------------

        /// <summary>建实例：先把**全部** NPC 的真实日程圆心写进地图，再开始规划。
        /// 顺序保证规划第一帧时，别人的碰撞圆也已经在正确位置，不会按旧锚点绕路。</summary>
        void BuildNpcPatrols()
        {
            // 只重置巡逻实例本身；立绘尺寸/Placeable 是 BuildTownNpcs 的产物，这里不许清。
            foreach (var old in npcPatrols.Values) old.Stop();
            npcPatrols.Clear();
            npcDialogueLeases.Clear();
            npcPatrolsLive = false;
            if (town == null || activeSave == null) return;
            var homes = new List<(string id, Vector2 foot)>(town.Npcs.Count);
            foreach (var npc in town.Npcs)
            {
                var foot = NpcStandingAt(npc);
                if (!town.SetNpcPosition(npc.Id, foot.x, foot.y))
                {
                    Debug.LogError("[NpcPatrol] 圆心不合法，跳过 " + npc.Id + "（" + foot.x + "," + foot.y + "）。");
                    continue;
                }
                homes.Add((npc.Id, foot));
            }
            foreach (var (id, foot) in homes)
            {
                npcPatrols[id] = new NpcLocalPatrol(foot.x, foot.y, NpcPatrolRadius, NpcPatrolSpeed,
                    NpcPatrolDwellSeconds, StableSeed(id),
                    (sx, sy, tx, ty) => TownNavigator.FindPath(town, sx, sy, tx, ty, id),
                    (x0, y0, x1, y1) => NpcPatrolCanTraverse(id, x0, y0, x1, y1));
            }
            npcPatrolAnimators.Clear();
            foreach (var pair in npcRects)
                if (pair.Value != null && pair.Value.TryGetComponent<NpcFrameAnimator>(out var animator))
                    npcPatrolAnimators[pair.Key] = animator;
            npcPatrolsLive = npcPatrols.Count > 0;
        }

        bool NpcPatrolCanTraverse(string id, float x0, float y0, float x1, float y1)
        {
            if (!town.CanTraverse(x0, y0, x1, y1, id)) return false;
            var npc = FindNpc(id);
            if (npc == null || !npc.Blocks) return true;
            // The town map stores NPC circles but not the traveler. Reserve that same radius
            // around the traveler for the entire NPC step, including a crossing between ends.
            float dx = x1 - x0, dy = y1 - y0, length2 = dx * dx + dy * dy;
            float t = length2 <= .0001f ? 0f : Mathf.Clamp01(((worldPos.x - x0) * dx + (worldPos.y - y0) * dy) / length2);
            float ox = x0 + dx * t - worldPos.x, oy = y0 + dy * t - worldPos.y;
            return ox * ox + oy * oy > npc.Radius * npc.Radius;
        }

        /// <summary>停掉本次城镇的全部实例；clearMapCentres 时连地图动态圆心一起清，
        /// 恢复配置锚点（离场/重建必经，否则下个场景会残留旧圆心）。</summary>
        void StopNpcPatrols(bool clearMapCentres)
        {
            foreach (var patrol in npcPatrols.Values) patrol.Stop();
            npcPatrols.Clear();
            foreach (var animator in npcPatrolAnimators.Values)
                if (animator != null) animator.SetMovement(Vector2.zero);
            npcPatrolAnimators.Clear();
            npcPatrolHeight.Clear();
            npcPatrolPlaceables.Clear();
            npcHeadMarkers.Clear();
            npcDialogueLeases.Clear();
            npcPatrolsLive = false;
            if (clearMapCentres) town?.ClearNpcPositions();
        }

        /// <summary>每帧推进：暂停判据、真实位移、碰撞回写、表现同步。
        /// 暂停来自三处：显式对话租约、正在进行的搭话浮层、玩家正走向这位 NPC（防追逐）。</summary>
        void AdvanceNpcPatrols(float dt)
        {
            if (!npcPatrolsLive || town == null) return;
            foreach (var npc in town.Npcs)
            {
                if (!npcPatrols.TryGetValue(npc.Id, out var patrol)) continue;
                var id = npc.Id;
                npcPatrolAnimators.TryGetValue(id, out var animator);
                if (animator != null) animator.SetMovement(Vector2.zero);
                var previous = new Vector2(patrol.X, patrol.Y);
                var paused = npcDialogueLeases.Contains(id) ||
                    (modal != null && talkNpcId == id) ||
                    (hasTarget && travelContextId == id);
                patrol.SetDialogue(paused);
                if (paused) continue;
                // 空转、受阻、停留都返回 0：没有真实位移就不碰地图与画面。
                if (patrol.Advance(dt, true) <= 0f) continue;
                if (!town.SetNpcPosition(id, patrol.X, patrol.Y))
                {
                    // 圆心写不进碰撞：停死这个实例，位置停在上一次成功的那份上，画面绝不抢先。
                    patrol.Stop();
                    Debug.LogError("[NpcPatrol] " + id + " 圆心回写失败（" + patrol.X + "," + patrol.Y +
                        "），该实例停止，保持上一次有效圆心。");
                    continue;
                }
                SyncNpcVisual(id, patrol.X, patrol.Y);
                if (animator != null) animator.SetMovement(new Vector2(patrol.X, patrol.Y) - previous);
            }
        }

        /// <summary>NPC 此刻的脚底世界坐标：读**地图动态圆心**，与碰撞判定同一份数据。
        /// 没有动态圆心（未建/已清）时回落到日程锚点。</summary>
        Vector2 NpcLiveFoot(MapNpc npc)
        {
            if (town == null) return new Vector2(npc.X, npc.Y);
            return town.TryNpcPosition(npc.Id, out var x, out var y)
                ? new Vector2(x, y)
                : NpcStandingAt(npc);
        }

        /// <summary>把脚底世界坐标搬到立绘上：与 PlaceInWorld 建节点时逐字相同的换算，
        /// 不另造排序坐标系；BaseY 仍取脚底投影 Y。</summary>
        void SyncNpcVisual(string npcId, float x, float y)
        {
            if (!npcRects.TryGetValue(npcId, out var rect) || rect == null) return;
            var height = npcPatrolHeight.TryGetValue(npcId, out var h) ? h : 170f;
            var baseY = (y - height) + height / worldScale;
            var ground = town.Elevation.HeightAt(x, baseY);
            rect.anchoredPosition = new Vector2(x * worldScale, -((y - height) - ground) * worldScale);
            if (npcPatrolPlaceables.TryGetValue(npcId, out var placeable) && placeable != null)
                placeable.BaseY = baseY - ground;
        }

        /// <summary>稳定 seed：FNV-1a(id)。不用 GetHashCode——它不保证跨进程稳定，
        /// 同一个 NPC 每次进镇走出不同路线，取证就没法复现。</summary>
        static int StableSeed(string id)
        {
            var hash = 2166136261u;
            foreach (var ch in id) { hash ^= ch; hash *= 16777619u; }
            return (int)(hash & 0x7fffffffu);
        }
    }
}