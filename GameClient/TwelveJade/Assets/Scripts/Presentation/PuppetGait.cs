// V 线 W-A：**相位 → 姿态**的纯数学层（与 PuppetRig.cs 一样，不许 using UnityEngine）。
//
// 相位仍然只由**真实位移**驱动（P2 硬判据：站着不动相位必须不动）——
// 这一层只负责"给我一个相位，我给你一帧姿态"，不持有任何时钟。
//
// 为什么分成"正面/背面"与"侧身"两套幅值：
//   侧身（画面里是侧面）：腿的前后摆 = 图像里的左右移动，**膝盖弯曲完全可见**，
//                        所以屈膝幅放大（34°）、大腿 14°、透视缩短只需 0.03；
//   正面/背面：图像内旋转只会让腿**左右劈开**，那不是走路。所以大腿收到 6°、屈膝 16°，
//              主要读感交给**透视缩短**（摆动腿看起来变短 10%）＋起伏＋髋摆＋躯干弯曲。
//   两套都由离线断言标定（抬脚量、左右交替量见 Tools/logs/v-06-meshdeform-report.md）。
using System;

namespace TwelveJade.Presentation
{
    /// <summary>步态参数。全部可调；默认值由"脚抬起量 / 左右交替量"实测标定。</summary>
    [Serializable]
    public struct PuppetGaitParams
    {
        public float ThighDeg;      // 大腿前后摆幅
        public float KneeDeg;       // 摆动相屈膝幅
        public float FootDeg;       // 摆动相脚掌滚动幅
        public float ShortenPeak;   // 摆动相透视缩短峰值（图像高度比例）
        public float BobUnits;      // 起伏（图像高度比例）
        public float SwayUnits;     // 髋部左右摆（图像宽度比例）
        public float SwayRate;
        public float TorsoDeg;      // 躯干倾角
        public float TorsoLead;
        public float NeckDeg;
        public float NeckLead;

        /// <summary>行走档。`legsSplit=false` 给侧身用（幅值放大，因为膝盖在侧面看得见）。</summary>
        public static PuppetGaitParams Walk(bool legsSplit) => legsSplit
            ? new PuppetGaitParams
            {
                ThighDeg = 6f, KneeDeg = 16f,
                FootDeg = 7f, ShortenPeak = 0.10f,
                BobUnits = 0.010f, SwayUnits = 0.007f, SwayRate = 1f,
                TorsoDeg = 1.5f, TorsoLead = 0.55f, NeckDeg = 1.1f, NeckLead = 1.05f,
            }
            : new PuppetGaitParams
            {
                ThighDeg = 14f, KneeDeg = 34f,
                FootDeg = 12f, ShortenPeak = 0.03f,
                BobUnits = 0.014f, SwayUnits = 0.002f, SwayRate = 1f,
                TorsoDeg = 2.5f, TorsoLead = 0.55f, NeckDeg = 1.4f, NeckLead = 1.05f,
            };

        /// <summary>奔跑档：同结构、幅值更大。</summary>
        public static PuppetGaitParams Run(bool legsSplit) => legsSplit
            ? new PuppetGaitParams
            {
                ThighDeg = 9f, KneeDeg = 24f,
                FootDeg = 10f, ShortenPeak = 0.16f,
                BobUnits = 0.018f, SwayUnits = 0.009f, SwayRate = 1f,
                TorsoDeg = 2.2f, TorsoLead = 0.55f, NeckDeg = 1.4f, NeckLead = 1.05f,
            }
            : new PuppetGaitParams
            {
                ThighDeg = 20f, KneeDeg = 46f,
                // WALK-RUN-SIDE：16° 时摆动相脚掌绕踝转约 13°，鞋尖被推到 y>GroundY，
                // 地面硬夹把整排鞋底压成一条线（polish 报告 §5）。收到 12° 后鞋尖不再触地，
                // Tools/PuppetRigChecks 的对应断言已同步改为 12° + 上下限。
                FootDeg = 12f, ShortenPeak = 0.05f,
                BobUnits = 0.024f, SwayUnits = 0.003f, SwayRate = 1f,
                TorsoDeg = 3.4f, TorsoLead = 0.55f, NeckDeg = 1.8f, NeckLead = 1.05f,
            };
    }

    public static class PuppetGait
    {
        /// <summary>把相位（弧度）与档位参数换算成一帧姿态。</summary>
        /// <param name="legsSplit">侧身档传 false：两条腿在贴图上重叠，只能用**一条共用链**
        /// （轴线落在身体中线），左右**不分开**；但这条共用链**照样走完整走周期**，
        /// 于是侧身档不再是"腿冻住"，而是"一条腿在走"。
        /// ⚠ 这说的是**网格形变模式 MeshDeform**（姿态会被 `CollapseToSingleLeg()` 折叠）：
        ///   侧身抬脚量实测 **0.0415 图高（30px @720）**，见 `Tools/PuppetRigChecks` 那条断言。
        ///   **色带模式 Slices 不是这样**——那一支在 `PuppetActor` 里把腿摆幅直接清零
        ///   （`if (!legsSplit) { legL = 0f; legR = 0f; }`），侧身**完全不摆腿**。
        ///   两种模式行为不同，读这一段时必须先问"现在走的是哪个模式"（B-02 口径统一）。</param>
        /// <remarks>相位约定（左腿），这是本层最容易写错的地方，写错了就会出现
        /// "支撑腿还在屈膝"或"脚最高时膝盖是直的"：
        ///   φ=0     **中立站姿**：腿竖直、膝伸直、重心最高、脚放平 —— 与旧色带模式在 φ=0 的表现一致，
        ///           所以玩家站着不动时看到的是"站着"而不是"被冻住的迈步姿"
        ///   φ=π/2   触地：腿在前、重心最低
        ///   φ=π     摆动中段：膝最大屈、脚最高
        ///   φ=3π/2  离地：腿在后
        /// 内部把腿/膝/起伏的相位再推 90°（`pw = φ + π/2`）：
        ///   ⇒ 大腿 = A·cos(pw)；屈膝与缩短 = 只在 sin(pw)&lt;0 的那半个周期；支撑相 = sin(pw)&gt;0。
        ///   三者互补，不会同时成立（离线断言 `支撑相不屈膝` 钉住这一点）。</remarks>
        /// <summary>B-03 flipbook 帧索引：**只由累计位移算** ——
        /// `floor(strideDistance / cycleDistance × frameCount) % frameCount`。
        ///
        /// 这一条是 P2（"站着不许原地踏步"）的**索引版**：公式里没有任何时钟项，
        /// 位移不变 ⇒ 返回值必然不变。放到本文件是因为它是**纯 C#**，
        /// 能被 `Tools/PuppetRigChecks` 真跑断言（`PuppetActor` 引 UnityEngine，进不去）。
        ///
        /// **参数非法返回 -1**（`cycleDistance <= 0` 或 `frameCount <= 0`），
        /// 不夹紧、不猜 —— 调用方拿到 -1 必须自己决定怎么报，不许静默吞掉。</summary>
        public static int FrameIndexAt(float strideDistance, float cycleDistance, int frameCount)
        {
            if (cycleDistance <= 0f || frameCount <= 0) return -1;
            var idx = (int)Math.Floor(strideDistance / cycleDistance * frameCount) % frameCount;
            if (idx < 0) idx += frameCount;   // 倒着走（负位移）也要落在合法区间
            return idx;
        }

        public static PuppetRigPose Pose(float phase, in PuppetGaitParams p, bool legsSplit)
        {
            var pw = phase + (float)(Math.PI / 2);
            var sw = (float)Math.Sin(pw);
            var cw = (float)Math.Cos(pw);
            var pose = new PuppetRigPose();

            // 腿：大腿前后摆（φ=0 时竖直）；两条腿反相。
            pose.ThighL = cw * p.ThighDeg;
            pose.ThighR = -cw * p.ThighDeg;
            // 膝：只在**摆动相**（sin(pw)<0）屈膝。max(0,…) 保证不出现"反关节"。
            var swingL = Math.Max(0f, -sw);
            var swingR = Math.Max(0f, sw);
            pose.ShinL = -p.KneeDeg * swingL;
            pose.ShinR = -p.KneeDeg * swingR;

            // 脚掌：**支撑相放平、摆动相松垂**。放平不是写死 0，而是抵消大腿+膝的累计角
            // （-（大腿+膝）），这样支撑脚的鞋底才真的贴着地，而不是随腿一起转进地里。
            var stanceL = PuppetRig.Smoothstep(sw);
            var stanceR = PuppetRig.Smoothstep(-sw);
            var rollL = -p.FootDeg * swingL;
            var rollR = -p.FootDeg * swingR;
            pose.FootL = PuppetRig.Lerp(rollL, -(pose.ThighL + pose.ShinL), stanceL);
            pose.FootR = PuppetRig.Lerp(rollR, -(pose.ThighR + pose.ShinR), stanceR);

            // 透视缩短：摆动腿在画面里变短（正面/背面走路的主要读感）
            pose.ShortenL = p.ShortenPeak * swingL;
            pose.ShortenR = p.ShortenPeak * swingR;

            // 起伏：**触地最低、支撑中段最高**（φ=0 时恰好为 0 = 站立高度）
            pose.HipBob = (0.5f + 0.5f * (float)Math.Cos(2f * pw)) * p.BobUnits;
            // 髋部左右摆：**摆向支撑腿**（支撑腿是左腿时向左，即负方向）；用 phase 而非 pw，
            // 这样 φ=0 时也是 0（站立不许歪着）
            pose.HipSway = -(float)Math.Sin(phase * p.SwayRate) * p.SwayUnits;
            // 躯干/颈：慢半拍的跟随，累积成 LeanChest→LeanNeck→LeanHead
            var torso = (float)Math.Sin(phase - p.TorsoLead) * p.TorsoDeg;
            var neck = (float)Math.Sin(phase - p.NeckLead) * p.NeckDeg;
            pose.LeanChest = torso;
            pose.LeanNeck = torso + neck;
            pose.LeanHead = torso + neck;   // 头随颈，颈部以上不再额外加角
            if (!legsSplit) pose.CollapseToSingleLeg();
            return pose;
        }

        /// <summary>**落地节拍**：相位走过 π/2 + kπ 时有一只脚落地（k 偶 = 左脚、k 奇 = 右脚，
        /// 与 `Pose` 的约定一致）。返回这一帧跨过了几次落地，`leftFirst` 给出第一次是哪只脚。
        ///
        /// 三条口径（都有离线断言）：
        ///   ① `fromPhase == toPhase`（站着不动）⇒ **恒为 0** —— 与 P2"时间空转不许摆腿"同一口径，
        ///      即"站着不许有脚步声"；
        ///   ② 相位只许前进（它由真实位移累加），倒退/为零一律 0；
        ///   ③ 单帧跨越次数**封顶** `maxPerFrame`：传送、镜头切换、卡顿导致的相位大跳
        ///      不许一次爆出一串脚步声。</summary>
        public static int FootfallsCrossed(float fromPhase, float toPhase, int maxPerFrame, out bool leftFirst)
        {
            leftFirst = true;
            if (maxPerFrame <= 0) return 0;
            if (toPhase <= fromPhase) return 0;
            const float eps = 1e-6f;
            var first = (int)Math.Floor((fromPhase - Math.PI / 2) / Math.PI + eps) + 1;
            var last = (int)Math.Floor((toPhase - Math.PI / 2) / Math.PI - eps);
            var count = last - first + 1;
            if (count <= 0) return 0;
            leftFirst = (((first % 2) + 2) % 2) == 0;
            return Math.Min(count, maxPerFrame);
        }

        /// <summary>站姿/中立：<see cref="Idle"/> 的便捷入口（呼吸幅度与漂移都为 0）。</summary>
        public static PuppetRigPose Idle() => Idle(0f, 0f, 0f, 0f);

        /// <summary>一周期里的第 `frameIndex` 帧对应的相位（弧度）。</summary>
        public static float PhaseAt(int frameIndex, int frameCount) =>
            (float)(2.0 * Math.PI * frameIndex / Math.Max(1, frameCount));

        /// <summary>**站立姿态**：腿/膝/脚/缩短**一律为 0**，只叠加热呼吸与重心漂移那几条加性层。
        ///
        /// 为什么必须单独有这一个入口（这是接进游戏前抓出来的真陷阱）：
        /// **走周期里不存在"两条腿都中立"的相位** —— 一条腿在支撑相时，另一条必然在摆动相
        /// （屈膝、缩短）。所以玩家站着不动时若沿用 `Pose(φ)` 的任何一个 φ，
        /// 人物都会是一个"迈步姿"（一条腿弯着），而不是"站着"。
        /// 老色带模式在 φ=0 恰好两条腿都是竖直的（它只有 sin 项、没有膝盖），所以它没这个问题；
        /// 网格形变有膝盖之后这个问题才冒出来。离线断言 `站立姿态腿角恒为 0` 钉住它。</summary>
        public static PuppetRigPose Idle(float breathNeckDeg, float leanDeg, float bobUnits, float swayUnits)
        {
            var pose = PuppetRigPose.Rest;
            pose.LeanChest = leanDeg;
            pose.LeanNeck = breathNeckDeg;
            pose.LeanHead = breathNeckDeg;
            pose.HipBob = bobUnits;
            pose.HipSway = swayUnits;
            return pose;
        }
    }

    /// <summary>B-12：`CharacterActor.Motion`（7 档）→ `PuppetActor.Motion`（3 档）的**显式映射表**。
    ///
    /// 为什么不能继续写 `(PuppetActor.Motion)gait` 强转：两个枚举前 3 档恰好同名同序，强转今天能过；
    /// 但 CharacterActor 有 7 档，一旦把"潜行/下蹲/跳跃/游泳"接进纸偶预览，
    /// 强转要么产出未定义枚举值（switch 落空 ⇒ 什么都不动），要么被越界判断**静默丢弃**
    /// ——表现是"点了没反应"，日志一行不留。强转是隐式契约，表是显式契约。
    ///
    /// 为什么放在这个文件：`PuppetActor.cs` / `CharacterActor.cs` 都依赖 UnityEngine，
    /// 进不了 `Tools/PuppetRigChecks`（那个工程只纳纯 C#）。放这里 ⇒ 映射表**能被离线门禁真跑断言**。
    /// </summary>
    public static class PuppetMotionMap
    {
        // 下标顺序必须与 CharacterActor.Motion 一致：Idle, Walk, Run, Sneak, Crouch, Jump, Swim
        public const int Idle = 0, Walk = 1, Run = 2, Sneak = 3, Crouch = 4, Jump = 5, Swim = 6;
        public const int CharacterMotionCount = 7;   // 对齐 CharacterActor.MotionNames.Length
        public const int PuppetMotionCount = 3;      // 对齐 PuppetActor.MotionNames.Length

        // 值 = PuppetActor 档位（0 静立 / 1 行走 / 2 奔跑）。
        // 借档规则（**全是降级，不是等价**）：潜行＝慢走（形状同行走，快慢靠步频/速度，不靠换档）；
        // 下蹲＝无循环步态 ⇒ 静立；跳跃＝一次性动作、不属循环档 ⇒ 静立；游泳＝循环动作 ⇒ 暂借行走。
        static readonly int[] map = { 0, 1, 2, 1, 0, 0, 1 };

        /// <summary>映射。**越界返回 -1**——不夹紧、不猜，由调用方决定怎么报（不许静默吞掉）。</summary>
        public static int Map(int characterMotion)
        {
            if (characterMotion < 0 || characterMotion >= map.Length) return -1;
            return map[characterMotion];
        }

        /// <summary>这一档是不是"降级借档"（PuppetActor 还没有专属表现）。
        /// 借档不算错，但**必须被看见**：调用方按它决定要不要打日志。</summary>
        public static bool IsDegraded(int characterMotion)
        {
            if (characterMotion < 0 || characterMotion >= map.Length) return false;
            return characterMotion >= PuppetMotionCount;   // 前 3 档一一对应，从第 4 档起全是借档
        }

        /// <summary>表长（门禁用：与 CharacterMotionCount 对不上就是枚举漂移）。</summary>
        public static int MapLength => map.Length;
        /// <summary>原样取表项（门禁用；越界 -1）。</summary>
        public static int MapAt(int i) => (i >= 0 && i < map.Length) ? map[i] : -1;
    }
}
