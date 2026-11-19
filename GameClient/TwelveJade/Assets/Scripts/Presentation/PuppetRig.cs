// V 线 W-A：单图网格形变的**纯数学层**。
//
// 为什么不写在 PuppetActor 里：这一层必须能在**没有 Unity 的机器上被断言**
// （`dotnet run --project Tools/PuppetRigChecks`）。所以：
//   · 本文件**不许** using UnityEngine、不许出现任何 Unity 类型；
//   · 自己的 PuppetVec2 代替 Vector2，自己的 Clamp/Lerp/Sin 代替 Mathf；
//   · 坐标一律用**图像归一化空间**：x∈[0,1] 自左向右，y∈[0,1] **自图顶向下**
//     （Unity 侧再翻成 UI 的 y 向上）。
//
// 为什么要它（治什么）：现有 `PuppetActor` 把立绘横切成 4 条**刚性色带**，
// 两条腿各绕髋硬转 ±5.2°，**没有膝盖、没有脚掌**，所以"走路僵硬"。
// 网格形变把同一张立绘细分成网格，按一条**两段腿链（大腿/小腿/脚）**做蒙皮，
// 于是膝盖能屈、脚掌能滚 —— 而**像素仍然是原图的像素**：
// 身份一致性闸门恒等通过（这是 W-A 相对"再生成一套美术"的根本好处）。
//
// 骨架几何**全部来自对盘上立绘的实测**（2026-10-06，命令与输出见
// `Tools/logs/v-06-meshdeform-report.md`），不是估的：
//   人物 bbox x 79..364 / y 36..707（图为 427×720）⇒ 身体中线 x=0.518，不是 0.5
//   双腿在 v=0.75 处才分开，两腿中心 x=0.41 / 0.625
//   ⇒ 旧代码把腿的枢轴放在**色带中心 0.25 / 0.75**，离腿的真实轴线 0.16 个图宽
//     （≈68px），一旋转腿就斜着甩出去 —— 这是"腿身错位"那类事故的根。
//
// 两个由**离线断言逼出来**的实现决定（都是本文件里最容易被写错的地方）：
//   1) 腿骨影响**按到腿轴的水平距离衰减**（LegRadius/LegFade）。最初写成"髋线以下
//      全归这条腿"，结果 x≈0/1 的顶点被腿链甩到 y=1.26（画布外 26%）、相邻边拉伸 5 倍。
//   2) 膝/踝用**渐进角**而不是"两个刚体变换做线性插值"。线性混合蒙皮在关节处会
//      "糖纸拧紧"：实测相邻行边长变化 0.12（边被拉长 5 倍）。改成"每个顶点绕关节
//      按高度渐升的角度旋转"后，弯曲是连续的，没有收缩。
using System;

namespace TwelveJade.Presentation
{
    public struct PuppetVec2
    {
        public float X;
        public float Y;
        public PuppetVec2(float x, float y) { X = x; Y = y; }
        public override string ToString() => "(" + X.ToString("0.####") + ", " + Y.ToString("0.####") + ")";
    }

    /// <summary>一帧的姿态度量。角度单位为度，位移/缩短单位为**图像比例**（1 = 整张图高）。</summary>
    public struct PuppetRigPose
    {
        public float HipBob;        // 正 = 整个人下沉
        public float HipSway;       // 正 = 向右
        public float LeanChest;     // 累计倾角
        public float LeanNeck;
        public float LeanHead;
        public float ThighL, ShinL, FootL;
        public float ThighR, ShinR, FootR;
        /// <summary>透视缩短：腿在画面里"变短"的比例（0.1 = 看起来短 10%）。
        /// 这是**正面/背面**走路的主要读感——正面看不到腿的前后摆，只能看到腿交替变短。</summary>
        public float ShortenL, ShortenR;

        public static PuppetRigPose Rest => default;   // 全零 = 站立原姿态

        public bool IsRest =>
            HipBob == 0f && HipSway == 0f && LeanChest == 0f && LeanNeck == 0f && LeanHead == 0f &&
            ThighL == 0f && ShinL == 0f && FootL == 0f && ThighR == 0f && ShinR == 0f && FootR == 0f &&
            ShortenL == 0f && ShortenR == 0f;

        /// <summary>侧身档只认左腿那一组（两条腿在贴图上重叠，只能用一条共用链）。</summary>
        public void CollapseToSingleLeg()
        {
            ThighR = ThighL;
            ShinR = ShinL;
            FootR = FootL;
            ShortenR = ShortenL;
        }
    }

    /// <summary>骨架几何：全部是**归一化坐标**，进 [SerializeField] 即可换角色而不改代码。</summary>
    [Serializable]
    public struct PuppetRigDefinition
    {
        public float BodyCenterX;   // 身体中线（实测 0.518，**不是 0.5**）
        public float HipV;          // 髋／骨盆线：整条腿链绕它前后摆
        public float KneeV;
        public float AnkleV;
        public float LegCenterX;    // **左**腿轴线（实测 0.41）；右腿 = 关于 BodyCenterX 镜像
        public float ChestV;
        public float NeckV;
        public float HipBlendV;     // 躯干↔腿 混合半宽
        public float KneeBlendV;    // 膝渐进角带宽半宽
        public float AnkleBlendV;   // 踝渐进角带宽半宽
        public float BobAtHead;     // 头部起伏幅度 / 髋部起伏幅度
        public float LegRadius;     // 腿骨影响半径（水平方向；超出即交回躯干）
        public float LegFade;       // 影响半径内的衰减宽度
        public float GroundY;       // 地面行：形变后的顶点**不许低于**这一行（脚不穿地/不出画布）

        /// <summary>默认值取自对 `farmer-male-0-front.png` 的实测横断面。</summary>
        public static PuppetRigDefinition Default => new PuppetRigDefinition
        {
            BodyCenterX = 0.518f,
            HipV = 0.52f,
            KneeV = 0.78f,
            AnkleV = 0.93f,
            LegCenterX = 0.41f,
            ChestV = 0.30f,
            NeckV = 0.16f,
            HipBlendV = 0.07f,
            KneeBlendV = 0.08f,
            AnkleBlendV = 0.03f,
            BobAtHead = 0.6f,
            LegRadius = 0.28f,
            LegFade = 0.10f,
            GroundY = 1.0f,
        };

        public float LegCenterXFor(bool left) => left ? LegCenterX : 2f * BodyCenterX - LegCenterX;

        /// <summary>侧身档：两条腿在贴图上前后重叠，只能用**一条共用链**，轴线落在身体中线。</summary>
        public PuppetRigDefinition WithSingleLeg()
        {
            var copy = this;
            copy.LegCenterX = BodyCenterX;
            return copy;
        }
    }

    public static class PuppetRig
    {
        // 网格分辨率：竖向要够密，膝部渐进角带（半宽 0.08）才有若干行可以过渡。
        public const int GridX = 16;
        public const int GridY = 40;
        public const int Count = (GridX + 1) * (GridY + 1);
        public const int TriangleCount = GridX * GridY * 2;

        public const int BoneHip = 0, BoneKnee = 1, BoneAnkle = 2;

        /// <summary>正面/背面档：**图像内摆幅**到脚底还剩几成（WALK-POLISH 标定）。
        /// 正面看腿的前后摆是"进出屏幕"，图像内的旋转只会把整只鞋横向推走
        /// （实测踝线 20~30px），两只鞋反相时再被两腿权重平均，就成了压扁的楔形。
        /// 所以让大腿/膝的图像内角度从髋线起随深度线性收到这个比例，
        /// 到鞋底基本没了：**抬脚仍然由缩短/踝线位移给**，步态不回到"冻住腿"。
        /// 侧身档走 1：那里摆幅是真的可见摆动，一路传到脚才算走路。</summary>
        public const float SwingFollowFront = 0.0f;

        /// <summary>铺一张规则网格（含边界），顺序为**先行后列**：i + j*(GridX+1)。</summary>
        public static void BuildGrid(PuppetVec2[] dst)
        {
            if (dst == null || dst.Length < Count) throw new ArgumentException("dst too small", nameof(dst));
            var n = 0;
            for (var j = 0; j <= GridY; j++)
                for (var i = 0; i <= GridX; i++)
                    dst[n++] = new PuppetVec2(i / (float)GridX, j / (float)GridY);
        }

        /// <summary>按姿态把**原网格**形变后写入 dst（原网格只读，绝不就地改写）。
        /// 这个签名是有意为之：写成"读 rest / 写 dst"，调用方就犯不了
        /// "在已经形变过的网格上再形变一次"的错（那会让位移逐帧累积，人越走越歪）。</summary>
        public static void Skin(PuppetVec2[] rest, PuppetVec2[] dst, in PuppetRigDefinition rig,
                                in PuppetRigPose pose, bool legsSplit)
        {
            if (rest == null) throw new ArgumentNullException(nameof(rest));
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            if (rest.Length < Count || dst.Length < Count) throw new ArgumentException("buffer too small");

            // 站姿：逐点照抄。这既是"站着不许动"的硬判据（P2 的几何版），
            // 也避免加权平均的浮点误差让静止的人每帧抖千分之一像素。
            if (pose.IsRest)
            {
                for (var n = 0; n < Count; n++) dst[n] = rest[n];
                return;
            }

            var def = legsSplit ? rig : rig.WithSingleLeg();
            for (var n = 0; n < Count; n++)
            {
                var v = rest[n];
                // 1) 髋部平移：起伏/摆动在髋部最大、到颈部衰减到 BobAtHead（头比髋稳，走起来才不像整块平移）
                var bw = BobWeight(v.Y, def);
                var ox = pose.HipSway * bw;
                var oy = pose.HipBob * bw;
                // 腿的竖向权重：髋线附近平滑过渡，躯干那一侧不受腿骨影响
                var legMask = Smoothstep((v.Y - (def.HipV - def.HipBlendV)) / (2f * Math.Max(1e-5f, def.HipBlendV)));

                // 2) 躯干：绕髋做**渐进弯曲**，角度随高度增长（不是刚性整块转）
                var torso = RotateAbout(v, new PuppetVec2(def.BodyCenterX, def.HipV), LeanAt(v.Y, pose, def));
                torso.X += ox;
                torso.Y += oy;

                // 3) 腿：两段链 + 脚掌 + 透视缩短。影响按**到腿轴的水平距离**衰减：
                //    超出 LegRadius 的顶点交回躯干（否则 x≈0/1 的顶点会被甩到画布外）。
                //    正面/背面：左右腿各绕自己的轴线，中线处两条腿各半，平滑过渡。
                //    侧身：一条共用链落在身体中线，左右**不分开**（贴图上两腿重叠）。
                PuppetVec2 leg;
                if (legsSplit)
                {
                    var wl = LegWeight(v.X, def.LegCenterXFor(true), def) * legMask;
                    var wr = LegWeight(v.X, def.LegCenterXFor(false), def) * legMask;
                    var wt = Math.Max(0f, 1f - wl - wr);
                    var sum = wl + wr + wt;
                    var pl = LegPoint(v, def.LegCenterXFor(true), def,
                                      pose.ThighL, pose.ShinL, pose.FootL, pose.ShortenL, SwingFollowFront);
                    var pr = LegPoint(v, def.LegCenterXFor(false), def,
                                      pose.ThighR, pose.ShinR, pose.FootR, pose.ShortenR, SwingFollowFront);
                    leg = new PuppetVec2((pl.X * wl + pr.X * wr + torso.X * wt) / sum,
                                         (pl.Y * wl + pr.Y * wr + torso.Y * wt) / sum);
                }
                else
                {
                    var wl = LegWeight(v.X, def.BodyCenterX, def) * legMask;
                    var wt = 1f - wl;
                    var pl = LegPoint(v, def.BodyCenterX, def, pose.ThighL, pose.ShinL, pose.FootL, pose.ShortenL);
                    leg = new PuppetVec2(pl.X * wl + torso.X * wt, pl.Y * wl + torso.Y * wt);
                }
                leg.X += ox;
                leg.Y += oy;
                // 地面约束：形变只许把人往上抬，不许把任何顶点压到地面行以下。
                // 这不只是"好看"——离线断言实测过：不做这条，膝弯会把脚外沿甩到 y=1.07
                // （画布外 7%），相邻边被拉长近一倍。脚本来就该踩在地上或抬起来。
                if (leg.Y > def.GroundY) leg.Y = def.GroundY;
                dst[n] = leg;
            }
        }

        /// <summary>腿骨的水平影响权重：到该腿轴线的距离 ≥ LegRadius 时为 0（交回躯干），
        /// 半径内按 LegFade 平滑升到 1。**这是防止远离腿轴的顶点被甩飞的关键**。</summary>
        public static float LegWeight(float x, float axisX, in PuppetRigDefinition rig)
        {
            var d = Math.Abs(x - axisX);
            if (d >= rig.LegRadius) return 0f;
            return Smoothstep((rig.LegRadius - d) / Math.Max(1e-5f, rig.LegFade));
        }

        /// <summary>腿的完整形变：两段链（含渐进角）＋ 透视缩短。</summary>
        /// <param name="swingFollow">**图像内摆幅**到脚底还剩几成（1 = 刚性跟随，侧身档的值）。
        /// 正面/背面档传 &lt;1：大腿/膝的图像内角度会把鞋底横向推走 20~30px，
        /// 摆动鞋整只向身体中线滑过去、和支撑鞋的区域重叠，再被两腿权重平均，
        /// 于是整只鞋被压成楔形（WALK-POLISH 实测：鞋面积在 0.67~1.39 倍之间跳）。
        /// 侧身档保持 1：那里摆幅是真的可见摆动。</param>
        public static PuppetVec2 LegPoint(PuppetVec2 v, float centerX, in PuppetRigDefinition rig,
                                          float thighDeg, float shinDeg, float footDeg, float shorten,
                                          float swingFollow = 1f)
        {
            var p = LegChain(v, centerX, rig, thighDeg, shinDeg, footDeg, swingFollow);
            if (shorten != 0f)
            {
                // 沿竖直方向收向髋线：正面看就是"这条腿变短了"（透视缩短），
                // 而不是把腿左右劈开。只在腿的竖向权重内生效。
                var legMask = Smoothstep((v.Y - (rig.HipV - rig.HipBlendV)) / (2f * Math.Max(1e-5f, rig.HipBlendV)));
                var squash = rig.HipV + (p.Y - rig.HipV) * (1f - shorten * legMask);
                // WALK-POLISH：**踝线以下不跟着按比例压**，改成整块平移到"踝线该到的高度"。
                // 一起压的后果是鞋跟着腿一起被压扁（鞋高按 10% 缩，观感是鞋底塌成一条线），
                // 而抬脚量本来就是由踝线的位移给的 —— 平移既不丢抬脚，也不动鞋的比例。
                // 两式在 v = AnkleV 处**恒等**（legMask 在那里已经是 1），所以踝口不会 kink。
                var footW = Smoothstep((v.Y - (rig.AnkleV - rig.AnkleBlendV)) / (2f * Math.Max(1e-5f, rig.AnkleBlendV)));
                if (footW > 0f)
                {
                    var ankleMask = Smoothstep((rig.AnkleV - (rig.HipV - rig.HipBlendV)) /
                                               (2f * Math.Max(1e-5f, rig.HipBlendV)));
                    var rigid = p.Y - shorten * ankleMask * (rig.AnkleV - rig.HipV);
                    squash = Lerp(squash, rigid, footW);
                }
                p.Y = squash;
            }
            return p;
        }

        /// <summary>两段腿链：绕髋转大腿 → 绕**已摆到位的膝**转小腿 → 绕**已摆到位的踝**转脚。
        /// 膝/踝角按高度**渐升**（渐进角），不是把两个刚体变换线性插值——
        /// 后者会在关节处"糖纸拧紧"（实测相邻边被拉长 5 倍）。
        /// `swingFollow` &lt;1 时，大腿/膝的图像内角度从**髋线**起随深度线性收到这个比例：
        /// 脚掌只吃到很小一部分摆幅，于是**两腿链在鞋处的位移差**从 30px 降到几 px
        /// （见 `SwingFollowFront`），两腿权重平均就不会再把摆动鞋压扁。</summary>
        public static PuppetVec2 LegChain(PuppetVec2 v, float centerX, in PuppetRigDefinition rig,
                                          float thighDeg, float shinDeg, float footDeg,
                                          float swingFollow = 1f)
        {
            var hip = new PuppetVec2(centerX, rig.HipV);
            var kneeRest = new PuppetVec2(centerX, rig.KneeV);
            var ankleRest = new PuppetVec2(centerX, rig.AnkleV);

            // 膝、踝**摆到位之后**的位置：作为后续旋转的枢轴。
            // 踝枢轴必须用**踝线处的实际跟随比例**，否则脚掌会绕着一个偏掉几 px 的点转，
            // 在踝口撕出一个 kink。
            var kneePosed = RotateAbout(kneeRest, hip, thighDeg * FollowAt(rig.KneeV, rig, swingFollow));
            var anklePosed = RotateAbout(RotateAbout(ankleRest, hip,
                                                     thighDeg * FollowAt(rig.AnkleV, rig, swingFollow)),
                                         kneePosed,
                                         shinDeg * FollowAt(rig.AnkleV, rig, swingFollow));

            var kRamp = Smoothstep((v.Y - (rig.KneeV - rig.KneeBlendV)) / (2f * Math.Max(1e-5f, rig.KneeBlendV)));
            var fRamp = Smoothstep((v.Y - (rig.AnkleV - rig.AnkleBlendV)) / (2f * Math.Max(1e-5f, rig.AnkleBlendV)));

            var p = RotateAbout(v, hip, thighDeg * FollowAt(v.Y, rig, swingFollow));
            if (shinDeg != 0f)
                p = RotateAbout(p, kneePosed, shinDeg * kRamp * FollowAt(v.Y, rig, swingFollow));
            if (footDeg != 0f) p = RotateAbout(p, anklePosed, footDeg * fRamp);
            return p;
        }

        /// <summary>图像内摆幅在某一高度上的跟随比例：髋线以上为 1，到脚底线性收到 `footFollow`。
        /// 用**深度线性**而不是阶跃：20~30px 的位移差若挤在踝口那 2 行网格里，
        /// 鞋头照样会被编成楔形（WALK-POLISH 先试过按踝线阶跃分边，测出来更差）。</summary>
        static float FollowAt(float y, in PuppetRigDefinition rig, float footFollow)
        {
            if (footFollow >= 1f || y <= rig.HipV) return 1f;
            var t = (y - rig.HipV) / Math.Max(1e-5f, 1f - rig.HipV);
            return Lerp(1f, footFollow, t);
        }

        /// <summary>某个关节**摆到位之后**的位置（夹具/调试/标定用）。</summary>
        public static PuppetVec2 JointPoint(int bone, in PuppetRigDefinition rig, bool legsSplit,
                                            float thighDeg, float shinDeg, float footDeg, bool left)
        {
            var def = legsSplit ? rig : rig.WithSingleLeg();
            var cx = def.LegCenterXFor(left);
            var hip = new PuppetVec2(cx, def.HipV);
            var knee = RotateAbout(new PuppetVec2(cx, def.KneeV), hip, thighDeg);
            var ankle = RotateAbout(RotateAbout(new PuppetVec2(cx, def.AnkleV), hip, thighDeg), knee, shinDeg);
            switch (bone)
            {
                case BoneHip: return hip;
                case BoneKnee: return knee;
                case BoneAnkle: return ankle;
                default: throw new ArgumentOutOfRangeException(nameof(bone));
            }
        }

        // ---- 小工具（不引 Mathf）----
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static PuppetVec2 Lerp(PuppetVec2 a, PuppetVec2 b, float t) =>
            new PuppetVec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        public static float Smoothstep(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }
        public static float Distance(PuppetVec2 a, PuppetVec2 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>绕枢轴旋转（图像坐标系 y 向下；正角 = 下半身向图像左侧摆）。</summary>
        public static PuppetVec2 RotateAbout(PuppetVec2 v, PuppetVec2 pivot, float degrees)
        {
            if (degrees == 0f) return v;
            var r = degrees * (float)Math.PI / 180f;
            var c = (float)Math.Cos(r);
            var s = (float)Math.Sin(r);
            var dx = v.X - pivot.X;
            var dy = v.Y - pivot.Y;
            return new PuppetVec2(pivot.X + dx * c - dy * s, pivot.Y + dx * s + dy * c);
        }

        static float BobWeight(float y, in PuppetRigDefinition rig)
        {
            if (y >= rig.HipV) return 1f;
            if (y <= rig.NeckV) return rig.BobAtHead;
            return Lerp(rig.BobAtHead, 1f, (y - rig.NeckV) / Math.Max(1e-5f, rig.HipV - rig.NeckV));
        }

        static float LeanAt(float y, in PuppetRigPose pose, in PuppetRigDefinition rig)
        {
            if (y >= rig.HipV) return 0f;
            if (y >= rig.ChestV) return Lerp(0f, pose.LeanChest, (rig.HipV - y) / Math.Max(1e-5f, rig.HipV - rig.ChestV));
            if (y >= rig.NeckV) return Lerp(pose.LeanChest, pose.LeanNeck, (rig.ChestV - y) / Math.Max(1e-5f, rig.ChestV - rig.NeckV));
            return pose.LeanHead;
        }
    }
}
