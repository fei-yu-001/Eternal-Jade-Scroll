using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // 分层纸偶 v3：单张立绘运行时切成六块，按地平线切——头、躯干（含双袖）、左腿、右腿，
    // 另有两块袖摆跟随躯干同步微摆，让宽袖长袍走起来有"袖子甩一甩"的生气。
    //
    // v2 的教训（Tools/split-puppet-layers.py 量过）：现有国风立绘的袖子与躯干在腋下是
    // 连成一片的，沿腋下切出"独立手臂"等于把整条袖子从身上搬走，一摆就露底色。
    // 所以 v3 不再追求六关节的解剖学正确，改为"少切、稳住"：只有腿真的分得开才分腿。
    // 纸偶与真人的分界仍然在关节与相位差上，但宁可少一块也不要在玩家眼前露馅。
    public sealed class PuppetActor : MonoBehaviour
    {
        public enum Motion { Idle, Walk, Run }
        public static readonly string[] MotionNames = { "静立", "行走", "奔跑" };

        // M4-05 战斗短动作：一次性触发，靠 countdown 自己走完。表现层专用，不回写 Core 状态。
        public enum Action { Light, Heavy, Dodge, Flinch }

        float actionUntil;
        Action activeAction = Action.Light;
        float actionClock;

        // 触发一次短动作；Core 的阶段没到（硬直/收招）时不播放，表现跟着规则走。
        public void PlayAction(Action action, float now, bool coreReady)
        {
            if (!coreReady) return;
            activeAction = action;
            actionClock = 0f;
            actionUntil = now + ActionDuration(action);
        }

        public static float ActionDuration(Action action) =>
            action == Action.Heavy ? .55f : action == Action.Light ? .34f :
            action == Action.Dodge ? .30f : .28f;

        public bool InAction(float now) => now < actionUntil;

        RectTransform root;
        readonly RectTransform[] joints = new RectTransform[6];
        readonly (float x0, float x1, float v0, float v1)[] regions =
            new (float, float, float, float)[6];
        readonly RawImage[] slices = new RawImage[6];
        Motion motion = Motion.Idle;
        float time, stepScale = 1f;
        int footfalls, lastStep;
        bool mirror;
        Vector2 position;
        Vector3 baseScale = Vector3.one;

        // 关节索引。
        const int LegL = 0, LegR = 1, Torso = 2, ArmL = 3, ArmR = 4, Head = 5;

        public Vector2 Position => position;
        public Motion CurrentMotion => motion;
        public RectTransform Root => root;
        // 步频随速度联动；每跨过半步记一次「落地」，供脚步尘点取用。
        public int Footfalls => footfalls;

        // basePoint = 脚底（枢轴取底边中点）：人物站在哪条地平线上，即是谁站在前面。
        public static PuppetActor Create(Transform parent, Texture2D view, Vector2 basePoint, Vector2 size)
        {
            var go = new GameObject("Puppet actor", typeof(RectTransform));
            var root = go.GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = new Vector2(0, 1);
            root.pivot = new Vector2(.5f, 0f);
            root.sizeDelta = size;
            var actor = go.AddComponent<PuppetActor>();
            actor.root = root;
            actor.BuildJoints(view, size);
            actor.position = basePoint;
            root.anchoredPosition = basePoint;
            return actor;
        }

        // 切区（人物比例，x 自左、v 自顶）。
        //
        // v4 的教训（Tools/split-puppet-layers.py 与逐行测量）：这批立绘的袖子与躯干在
        // 腋下粘连，**切不出独立手臂**；若硬把躯干收窄成中带，中带两侧就有几十像素透明，
        // 一摆动就露出底色方块。
        //
        // 纸偶的正确分层是"叠印"而不是"剪影"：
        //   · 躯干/腿/头各自取**全宽**条带——条带内该透的地方本来就透，拼接后严丝合缝；
        //   · 袖块取与躯干**完全相同**的贴图与区域，只靠微小错位与相位差制造"袖子甩动"的错觉。
        // 好处：接缝处永远不会露出不属于人物的像素，也不会出现"一个脑袋变两个"。
        // 代价：袖块不做解剖学摆臂——这批立绘本来也没有能独立摆的手臂。
        void BuildJoints(Texture2D view, Vector2 size)
        {
            // 四块，各占自己的条带，**互不重叠**：
            //   头   0 – 0.22
            //   躯干 0.22 – 0.76（含双袖；下摆到腰下仍连成一片，所以一直连到真正分腿处）
            //   左腿 / 右腿 0.76 – 1（中线在此才分开，见 split-puppet-layers.py 测量）
            // ArmL/ArmR 不再切独立块：这批立绘腋下与躯干粘连，量过没有可切的缝；
            // 强行切就会在同一处画出第二个上半身（叠印）或背景方块（剪影）。袖子随躯干整体摆。
            regions[Head] = (0f, 1f, 0f, .22f);
            regions[Torso] = (0f, 1f, .22f, .76f);
            regions[LegL] = (0f, .50f, .76f, 1f);
            regions[LegR] = (.50f, 1f, .76f, 1f);
            regions[ArmL] = (0f, 1f, .22f, .76f);
            regions[ArmR] = (0f, 1f, .22f, .76f);

            // 枢轴写在自己 Rect 的归一化空间：pivot.y = 0 是**底边**、1 是顶边。
            //   腿块（v 0.76–1）：髋在条带**顶** → 0.96；绕 0.04 就变成绕脚踝当钟摆甩，整条腿会甩离身体。
            //   躯干块（v 0.22–0.76）：胯在条带**底** → 0.04。
            //   头块（v 0–0.22）：颈在条带**底** → 0.12。
            var pivots = new[]
            {
                new Vector2(.5f, .96f), new Vector2(.5f, .96f),   // 腿：绕髋
                new Vector2(.5f, .04f),                            // 躯干：绕胯
                new Vector2(.5f, .04f), new Vector2(.5f, .04f),   // 袖位（未建对象，占位）
                new Vector2(.5f, .12f),                            // 头：绕颈
            };
            // 只建 4 块：头、躯干、左右腿。袖位保留在索引里但不建对象（见上）。
            foreach (var i in new[] { LegL, LegR, Torso, Head })
            {
                var (x0, x1, v0, v1) = regions[i];
                var rect = new GameObject("Puppet joint " + i, typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(root, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.pivot = pivots[i];
                rect.sizeDelta = new Vector2((x1 - x0) * size.x, (v1 - v0) * size.y);
                // anchoredPosition 定位的是**枢轴点**，不是矩形左上角。
                // 想要条带左上角落在 (-x0*size.x, -v0*size.y)，就得把 pivot 造成的偏移加回来。
                // 少了这一项，枢轴偏离中心多少，整块就错位多少——小图看不出来，230px 的战斗页会整块飞出去。
                rect.anchoredPosition = new Vector2(-x0 * size.x, -v0 * size.y) +
                    Vector2.Scale(pivots[i], rect.sizeDelta);
                var image = rect.gameObject.AddComponent<RawImage>();
                image.texture = view;
                image.raycastTarget = false;
                joints[i] = rect;
                slices[i] = image;
            }
            ApplyMirror();
        }

        void ApplyMirror()
        {
            foreach (var i in new[] { LegL, LegR, Torso, Head })
            {
                if (slices[i] == null) continue;
                var (x0, x1, v0, v1) = regions[i];
                slices[i].uvRect = new Rect(mirror ? x1 : x0, 1f - v1, mirror ? -(x1 - x0) : (x1 - x0), v1 - v0);
            }
        }

        public void SetMotion(Motion value)
        {
            if (motion == value) return;
            motion = value;
            time = 0f;
        }

        // 步频联动：速度越快步子越密（1 = 基准速度）。走与跑的基准不同，各自再乘速度比。
        public void SetStepScale(float scale)
        {
            stepScale = Mathf.Clamp(scale, .55f, 1.9f);
        }

        // 鸟瞰透视：角色越往北（画的上端）越小。
        public void SetScale(float scale)
        {
            baseScale = Vector3.one * scale;
            root.localScale = baseScale;
        }

        public void SetPosition(Vector2 value)
        {
            position = value;
            root.anchoredPosition = value;
        }

        public void SetMirror(bool value)
        {
            if (mirror == value) return;
            mirror = value;
            ApplyMirror();
            // 转向缓冲：镜像不做硬切，给 80ms 的挤压过渡（不做 180° 翻面）。
            turnTime = 0f;
        }

        float turnTime = 1f;

        void Update()
        {
            time += Time.unscaledDeltaTime;
            turnTime += Time.unscaledDeltaTime;
            var w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / .25f));
            // 转身的挤压：纵轴压 6%、横轴撑 6%，80ms 内消散——比直接翻面顺眼得多。
            var turn = Mathf.Clamp01(turnTime / .08f);
            var squash = Mathf.Sin(turn * Mathf.PI) * .06f;
            root.localScale = Vector3.Scale(baseScale, new Vector3(1f + squash, 1f - squash, 1f));
            float legL = 0f, legR = 0f, armL = 0f, armR = 0f, body = 0f, neck = 0f, bob = 0f, sway = 0f;
            switch (motion)
            {
                case Motion.Idle:
                    neck = Mathf.Sin(time * 1.6f) * .9f * w;
                    armL = Mathf.Sin(time * 1.5f) * .6f * w;
                    armR = -Mathf.Sin(time * 1.5f) * .6f * w;
                    bob = Mathf.Sin(time * 1.9f) * 1.6f * w;
                    break;
                case Motion.Walk:
                {
                    var ph = time / (.34f / stepScale) * Mathf.PI;
                    var step = Mathf.FloorToInt(ph / Mathf.PI);
                    if (step != lastStep) { lastStep = step; footfalls++; }
                    // 腿是唯一真关节：绕髋前后摆。袖子只跟半拍、幅度是腿的四成。
                    legL = Mathf.Sin(ph) * 5.2f * w;
                    legR = -Mathf.Sin(ph) * 5.2f * w;
                    armL = -Mathf.Sin(ph - .5f) * 2.0f * w;
                    armR = Mathf.Sin(ph - .5f) * 2.0f * w;
                    body = Mathf.Sin(ph - .55f) * 1.5f * w - 1f * w;
                    neck = Mathf.Sin(ph - 1.05f) * 1.1f * w;
                    bob = Mathf.Abs(Mathf.Sin(ph)) * 3.2f * w;
                    sway = Mathf.Sin(ph * .5f) * 1.4f * w;
                    break;
                }
                case Motion.Run:
                {
                    var ph = time / (.26f / stepScale) * Mathf.PI;
                    var step = Mathf.FloorToInt(ph / Mathf.PI);
                    if (step != lastStep) { lastStep = step; footfalls++; }
                    legL = Mathf.Sin(ph) * 7.2f * w;
                    legR = -Mathf.Sin(ph) * 7.2f * w;
                    armL = -Mathf.Sin(ph - .4f) * 3.0f * w;
                    armR = Mathf.Sin(ph - .4f) * 3.0f * w;
                    body = Mathf.Sin(ph - .5f) * 2.2f * w - 3.4f * w;
                    neck = Mathf.Sin(ph - 1f) * 1.4f * w + 1.1f * w;
                    bob = Mathf.Abs(Mathf.Sin(ph)) * 5.6f * w;
                    sway = Mathf.Sin(ph * .5f) * 2.2f * w;
                    break;
                }
            }
            joints[LegL].localRotation = Quaternion.Euler(0, 0, legL);
            joints[LegR].localRotation = Quaternion.Euler(0, 0, legR);
            joints[Torso].localRotation = Quaternion.Euler(0, 0, body);
            joints[Head].localRotation = Quaternion.Euler(0, 0, neck);
            // 袖位已并入躯干，这里的 armL/armR 只喂给"整体上下身跟随"（见下方 torso 合成）。
            // 短动作叠加：出拳前冲、闪避侧移、受击后仰、蓄力下沉。
            var actionOffset = Vector2.zero;
            var actionLean = 0f;
            if (Time.unscaledTime < actionUntil)
            {
                actionClock = Mathf.Min(actionClock + Time.unscaledDeltaTime, ActionDuration(activeAction));
                var t = actionClock / Mathf.Max(.0001f, ActionDuration(activeAction));
                switch (activeAction)
                {
                    case Action.Light:
                        actionOffset = new Vector2(Mathf.Sin(t * Mathf.PI) * 26f, 0f);
                        actionLean = Mathf.Sin(t * Mathf.PI) * 4f;
                        break;
                    case Action.Heavy:
                        // 前段下沉蓄力，后段冲出。
                        actionOffset = new Vector2(-Mathf.Sin(t * Mathf.PI) * 10f + Mathf.Sin((t - .5f) * Mathf.PI * 2f) * 22f,
                            t < .4f ? -Mathf.Sin(t / .4f * Mathf.PI) * 10f : 0f);
                        actionLean = t < .4f ? -8f : 7f;
                        break;
                    case Action.Dodge:
                        actionOffset = new Vector2(Mathf.Sin(t * Mathf.PI) * 44f, 0f);
                        actionLean = -Mathf.Sin(t * Mathf.PI) * 6f;
                        break;
                    case Action.Flinch:
                        actionOffset = new Vector2(-Mathf.Sin(t * Mathf.PI) * 16f, 0f);
                        actionLean = Mathf.Sin(t * Mathf.PI) * 9f;
                        break;
                }
            }
            root.anchoredPosition = position + new Vector2(sway, -bob) * baseScale.y + actionOffset;
            if (actionLean != 0f)
                root.localRotation = Quaternion.Euler(0f, 0f, actionLean);
            else if (!mirror) root.localRotation = Quaternion.identity;
        }
    }
}
