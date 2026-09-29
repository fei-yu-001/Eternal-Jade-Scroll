using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // 分层纸偶 v2：单张立绘运行时切成 六关节——左右腿交替摆（跨步的骨架）、
    // 左右臂与同侧腿反相摆（真实行走的手脚交叉）、躯干缓随、头部再慢半拍。
    // 纸偶与真人的分界就在关节与相位差；总幅角收小（腿 ±6.5° 内）避免切缝旋转露出。
    public sealed class PuppetActor : MonoBehaviour
    {
        public enum Motion { Idle, Walk, Run }
        public static readonly string[] MotionNames = { "静立", "行走", "奔跑" };

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

        // 切区（人物比例，x 自左、v 自顶）：双腿在髋部分开、双臂沿腋下分出、躯干只取中带、头部独立。
        void BuildJoints(Texture2D view, Vector2 size)
        {
            regions[LegL] = (0f, .50f, .55f, 1f);
            regions[LegR] = (.50f, 1f, .55f, 1f);
            regions[Torso] = (.15f, .85f, .20f, .58f);
            regions[ArmL] = (0f, .18f, .20f, .62f);
            regions[ArmR] = (.82f, 1f, .20f, .62f);
            regions[Head] = (0f, 1f, 0f, .22f);

            var pivots = new[]
            {
                new Vector2(.5f, .02f), new Vector2(.5f, .02f),   // 腿：绕髋
                new Vector2(.5f, .15f),                            // 躯干：绕腰
                new Vector2(.65f, .04f), new Vector2(.35f, .04f),  // 臂：绕肩
                new Vector2(.5f, .3f),                             // 头：绕颈
            };
            for (var i = 0; i < 6; i++)
            {
                var (x0, x1, v0, v1) = regions[i];
                var rect = new GameObject("Puppet joint " + i, typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(root, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.pivot = pivots[i];
                rect.anchoredPosition = new Vector2(-x0 * size.x, -v0 * size.y);
                rect.sizeDelta = new Vector2((x1 - x0) * size.x, (v1 - v0) * size.y);
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
            for (var i = 0; i < 6; i++)
            {
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
                    armL = Mathf.Sin(time * 1.5f) * .5f * w;
                    armR = -Mathf.Sin(time * 1.5f) * .5f * w;
                    bob = Mathf.Sin(time * 1.9f) * 1.6f * w;
                    break;
                case Motion.Walk:
                {
                    var ph = time / (.34f / stepScale) * Mathf.PI;
                    var step = Mathf.FloorToInt(ph / Mathf.PI);
                    if (step != lastStep) { lastStep = step; footfalls++; }
                    legL = Mathf.Sin(ph) * 4.6f * w;
                    legR = -Mathf.Sin(ph) * 4.6f * w;
                    armL = -Mathf.Sin(ph) * 2.6f * w;
                    armR = Mathf.Sin(ph) * 2.6f * w;
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
                    legL = Mathf.Sin(ph) * 6.4f * w;
                    legR = -Mathf.Sin(ph) * 6.4f * w;
                    armL = -Mathf.Sin(ph) * 3.6f * w;
                    armR = Mathf.Sin(ph) * 3.6f * w;
                    body = Mathf.Sin(ph - .5f) * 2.2f * w - 3.4f * w;
                    neck = Mathf.Sin(ph - 1f) * 1.4f * w + 1.1f * w;
                    bob = Mathf.Abs(Mathf.Sin(ph)) * 5.6f * w;
                    sway = Mathf.Sin(ph * .5f) * 2.2f * w;
                    break;
                }
            }
            joints[LegL].localRotation = Quaternion.Euler(0, 0, legL);
            joints[LegR].localRotation = Quaternion.Euler(0, 0, legR);
            joints[ArmL].localRotation = Quaternion.Euler(0, 0, armL);
            joints[ArmR].localRotation = Quaternion.Euler(0, 0, armR);
            joints[Torso].localRotation = Quaternion.Euler(0, 0, body);
            joints[Head].localRotation = Quaternion.Euler(0, 0, neck);
            root.anchoredPosition = position + new Vector2(sway, -bob) * baseScale.y;
        }
    }
}
