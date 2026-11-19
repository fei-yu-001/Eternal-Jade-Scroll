// V 线 W-A 的**离线断言**：网格形变的数学层（PuppetRig / PuppetGait）。
//
// 为什么不放进 SaveChecks：那个工程管的是"存档 / 任务 / 对白 / NPC / 战斗 / 经济"，
// 塞进表现层的骨架数学会让它名不副实。这里照同一范式单开一个，
// 只纳**不依赖 UnityEngine** 的两个文件，于是**没有 Unity 的机器上也有门禁**。
//
// 判据口径沿用本仓：退出码 0 不算通过，只认最后一行 `suite finished, 0 problem(s)`。
using System;
using TwelveJade.Presentation;

namespace TwelveJade.Checks
{
    internal static class Program
    {
        static int problems;

        static void Check(bool ok, string name, string detail)
        {
            if (ok)
            {
                Console.WriteLine("  ok   " + name + (detail.Length > 0 ? "  [" + detail + "]" : ""));
                return;
            }
            problems++;
            Console.WriteLine("  FAIL " + name + "  [" + detail + "]");
        }

        static int Main()
        {
            var rig = PuppetRigDefinition.Default;
            var rest = new PuppetVec2[PuppetRig.Count];
            var dst = new PuppetVec2[PuppetRig.Count];
            PuppetRig.BuildGrid(rest);

            Console.WriteLine("== W-A 单图网格形变 · 离线断言 ==");
            Console.WriteLine("网格 " + PuppetRig.GridX + "x" + PuppetRig.GridY +
                              "  顶点 " + PuppetRig.Count + "  三角 " + PuppetRig.TriangleCount);
            Console.WriteLine("骨架 hip=" + rig.HipV + " knee=" + rig.KneeV + " ankle=" + rig.AnkleV +
                              "  中线x=" + rig.BodyCenterX + "  左腿x=" + rig.LegCenterX);

            // 1) 网格本身
            {
                var ok = PuppetRig.Count == (PuppetRig.GridX + 1) * (PuppetRig.GridY + 1)
                         && rest.Length == PuppetRig.Count
                         && rest[0].X == 0f && rest[0].Y == 0f
                         && rest[PuppetRig.Count - 1].X == 1f && rest[PuppetRig.Count - 1].Y == 1f;
                var inRange = true;
                foreach (var v in rest) if (v.X < 0f || v.X > 1f || v.Y < 0f || v.Y > 1f) inRange = false;
                Check(ok && inRange, "网格完整且落在 [0,1]^2",
                      "四角与顶点数正确，越界点 " + (inRange ? "0" : ">0"));
            }

            // 2) 站姿就是原样（P2"站着不动"的几何版：不是角度不动，而是**一个顶点都不许动**）
            {
                PuppetRig.Skin(rest, dst, rig, PuppetRigPose.Rest, true);
                var max = 0f;
                for (var i = 0; i < PuppetRig.Count; i++)
                    max = Math.Max(max, PuppetRig.Distance(rest[i], dst[i]));
                Check(max == 0f, "站姿(Rest) ⇒ 顶点零位移", "最大位移 " + max.ToString("0.######"));
            }

            // 2b) 【语义】相位 0 = **左腿**支撑中段（左腿中立、重心最高、不歪）。
            //     右腿此时**本来就在摆动相**（屈膝）——走周期里不存在"两条腿都中立"的相位。
            //     这一条最初被写成"两腿全为 0"，跑出来才发现右膝是 16°，
            //     于是改成"只断左腿 + 另立一条站立姿态断言"。
            {
                var q = PuppetGait.Pose(0f, PuppetGaitParams.Walk(true), true);
                var ok = Math.Abs(q.ThighL) < 1e-4f && Math.Abs(q.ShinL) < 1e-4f
                         && Math.Abs(q.FootL) < 1e-4f && Math.Abs(q.ShortenL) < 1e-4f
                         && Math.Abs(q.HipBob) < 1e-4f && Math.Abs(q.HipSway) < 1e-4f
                         && Math.Abs(q.ShinR) > 1f;   // 右腿必须在摆动相（有屈膝）
                Check(ok, "相位 0 = 左腿支撑中段（左腿中立 / 右腿在摆动相）",
                      "L: thigh=" + q.ThighL.ToString("0.####") + " knee=" + q.ShinL.ToString("0.####") +
                      " bob=" + q.HipBob.ToString("0.####") + " sway=" + q.HipSway.ToString("0.####") +
                      " ／ R: knee=" + q.ShinR.ToString("0.#"));
            }

            // 2c) 【语义】站立姿态：**腿角一律为 0**。走周期里没有"两腿都中立"的相位，
            //     所以站着不动必须走这个入口，不许沿用任何 φ 的 Pose()。
            {
                var idle = PuppetGait.Idle(1.1f, 1.5f, 0.01f, 0.005f);
                var ok = Math.Abs(idle.ThighL) < 1e-6f && Math.Abs(idle.ThighR) < 1e-6f
                         && Math.Abs(idle.ShinL) < 1e-6f && Math.Abs(idle.ShinR) < 1e-6f
                         && Math.Abs(idle.FootL) < 1e-6f && Math.Abs(idle.FootR) < 1e-6f
                         && Math.Abs(idle.ShortenL) < 1e-6f && Math.Abs(idle.ShortenR) < 1e-6f;
                var breathKept = Math.Abs(idle.LeanNeck - 1.1f) < 1e-6f && Math.Abs(idle.HipBob - 0.01f) < 1e-6f;
                Check(ok && breathKept, "站立姿态：腿角恒为 0，只留呼吸/起伏",
                      "thigh=" + idle.ThighL.ToString("0.######") + " knee=" + idle.ShinL.ToString("0.######") +
                      " neck=" + idle.LeanNeck.ToString("0.####") + " bob=" + idle.HipBob.ToString("0.####"));
            }

            // 3) 顶点只读 + 幂等
            {
                var snapshot = (PuppetVec2[])rest.Clone();
                var pose = PuppetGait.Pose(1.234f, PuppetGaitParams.Walk(true), true);
                PuppetRig.Skin(rest, dst, rig, pose, true);
                PuppetRig.Skin(rest, dst, rig, pose, true);
                var first = (PuppetVec2[])dst.Clone();
                PuppetRig.Skin(rest, dst, rig, pose, true);
                var idem = true;
                for (var i = 0; i < PuppetRig.Count; i++)
                    if (PuppetRig.Distance(first[i], dst[i]) > 1e-6f) idem = false;
                var untouched = true;
                for (var i = 0; i < PuppetRig.Count; i++)
                    if (snapshot[i].X != rest[i].X || snapshot[i].Y != rest[i].Y) untouched = false;
                Check(idem && untouched, "Skin 幂等且不改写原网格", "幂等=" + idem + " 原网格未动=" + untouched);
            }

            // 4) 相位周期性
            {
                var max = 0f;
                for (var k = 0; k < 64; k++)
                {
                    var ph = k * 0.1f;
                    var a = PuppetGait.Pose(ph, PuppetGaitParams.Walk(false), false);
                    var b = PuppetGait.Pose(ph + 2f * (float)Math.PI, PuppetGaitParams.Walk(false), false);
                    max = Math.Max(max, Math.Abs(a.ThighL - b.ThighL));
                    max = Math.Max(max, Math.Abs(a.ShinL - b.ShinL));
                    max = Math.Max(max, Math.Abs(a.HipBob - b.HipBob));
                    max = Math.Max(max, Math.Abs(a.HipSway - b.HipSway));
                    max = Math.Max(max, Math.Abs(a.LeanNeck - b.LeanNeck));
                }
                Check(max < 1e-4f, "姿态关于相位 2π 周期", "最大分量差 " + max.ToString("0.########"));
            }

            // 5) 【核心】摆动相真的把脚抬起来 —— 这是"僵硬"的解药，必须真的发生
            {
                var p = PuppetGaitParams.Walk(false);
                var liftMax = 0f; var liftMin = 0f; var liftMaxAt = 0f; var liftMinAt = 0f;
                for (var k = 0; k < 256; k++)
                {
                    var ph = k * 2f * (float)Math.PI / 256f;
                    var pose = PuppetGait.Pose(ph, p, false);
                    var ankle = PuppetRig.LegPoint(new PuppetVec2(rig.BodyCenterX, rig.AnkleV),
                        rig.BodyCenterX, rig, pose.ThighL, pose.ShinL, pose.FootL, pose.ShortenL);
                    if (k == 0 || ankle.Y > liftMax) { liftMax = ankle.Y; liftMaxAt = ph; }
                    if (k == 0 || ankle.Y < liftMin) { liftMin = ankle.Y; liftMinAt = ph; }
                }
                var lift = liftMax - liftMin;
                Console.WriteLine("       踝高最低 y=" + liftMax.ToString("0.####") +
                                  " @φ=" + liftMaxAt.ToString("0.##") +
                                  "   最高 y=" + liftMin.ToString("0.####") +
                                  " @φ=" + liftMinAt.ToString("0.##") +
                                  "   抬脚量=" + lift.ToString("0.####") +
                                  " (" + (lift * 720f).ToString("0") + "px @720)");
                // 支撑相 = 腿向后（sinφ<0）；摆动相 = 腿向前（sinφ>0）。
                // 注意：脚踝"最高"不等于"屈膝最大"——大腿本身绕髋转到两侧时也会把脚抬高，
                // 所以真正该断言的不是"最高点恰在 φ=0"，而是：
                //   ① 抬脚量够（下面）
                //   ② 脚最低时腿是竖直的（|sinφ|≈0，此时膝也该是伸直的）
                //   ③ 脚最高时膝盖**确实是屈的**（摆腿靠屈膝缩短，而不是靠大腿甩）
                Check(lift >= 0.02f, "侧身档：摆动相抬脚量 ≥ 0.02 图高",
                      lift.ToString("0.####") + " (" + (lift * 720f).ToString("0") + "px @720，限 0.02)");
                var kneeAtTop = Math.Abs(PuppetGait.Pose(liftMinAt, p, false).ShinL);
                Check(kneeAtTop >= 0.5f * p.KneeDeg, "脚最高时膝盖确实屈着（≥ 50% 屈膝幅）",
                      "|ShinL|=" + kneeAtTop.ToString("0.#") + " ≥ " + (0.5f * p.KneeDeg).ToString("0.#"));
                var thighAtLow = Math.Abs(PuppetGait.Pose(liftMaxAt, p, false).ThighL);
                Check(thighAtLow < 0.15f * p.ThighDeg, "脚最低时大腿接近竖直",
                      "|ThighL|=" + thighAtLow.ToString("0.###") + " < " + (0.15f * p.ThighDeg).ToString("0.###"));
            }

            // 6) 膝：支撑相几乎伸直、摆动相屈、且**永不反关节**
            {
                var p = PuppetGaitParams.Walk(false);
                var stance = PuppetGait.Pose(0f, p, false);                          // φ=0 支撑中段：膝应伸直
                var swing = PuppetGait.Pose((float)Math.PI, p, false);               // φ=π 摆动中段：膝应最大屈
                var maxKnee = 0f; var anyReverse = false; var stanceMax = 0f;
                for (var k = 0; k < 256; k++)
                {
                    var ph = k * 2f * (float)Math.PI / 256f;
                    var pose = PuppetGait.Pose(ph, p, false);
                    maxKnee = Math.Max(maxKnee, Math.Abs(pose.ShinL));
                    if (pose.ShinL > 1e-4f) anyReverse = true;
                    if (Math.Cos(ph) > 0.9f) stanceMax = Math.Max(stanceMax, Math.Abs(pose.ShinL));
                }
                Check(Math.Abs(stance.ShinL) < 1e-3f, "支撑中段不屈膝",
                      "φ=0 时 |ShinL|=" + Math.Abs(stance.ShinL).ToString("0.#####"));
                Check(stanceMax < 1e-3f, "整个支撑相（cosφ>0.9）都不屈膝",
                      "max|ShinL|=" + stanceMax.ToString("0.#####"));
                Check(Math.Abs(swing.ShinL) >= 25f, "摆动中段屈膝 ≥ 25°",
                      "φ=π 时 |ShinL|=" + Math.Abs(swing.ShinL).ToString("0.#"));
                Check(!anyReverse, "膝只向后折（不反关节）", "反关节相位 " + (anyReverse ? "有" : "无"));
                Check(maxKnee > 0f, "屈膝幅非零", "max|ShinL|=" + maxKnee.ToString("0.#"));
            }

            // 7) 侧身档**不劈腿**：下半身是刚体旋转，不是左右分开
            {
                var p = PuppetGaitParams.Walk(false);
                var pose = PuppetGait.Pose(0.7f, p, false);
                PuppetRig.Skin(rest, dst, rig, pose, false);
                // 取小腿段内三点（膝混合带 0.83 以下、脚混合带 0.905 以上）：
                // 它们共用同一个变换 ⇒ 两两距离必须守恒。
                var probe = new[] { new PuppetVec2(0.44f, 0.85f), new PuppetVec2(0.60f, 0.85f), new PuppetVec2(0.52f, 0.89f) };
                var worst = 0f;
                for (var i = 0; i < probe.Length; i++)
                {
                    var a = PuppetRig.LegChain(probe[i], rig.BodyCenterX, rig, pose.ThighL, pose.ShinL, pose.FootL);
                    for (var j = 0; j < probe.Length; j++)
                    {
                        var b = PuppetRig.LegChain(probe[j], rig.BodyCenterX, rig, pose.ThighL, pose.ShinL, pose.FootL);
                        var d0 = PuppetRig.Distance(probe[i], probe[j]);
                        var d1 = PuppetRig.Distance(a, b);
                        worst = Math.Max(worst, Math.Abs(d1 - d0));
                    }
                }
                Check(worst < 1e-4f, "侧身档：腿部为刚体（左右不分开）", "两两距离最大变化 " + worst.ToString("0.######"));
            }

            // 8) 正面档**要**分腿：左右腿在摆动相必须走到不同位置
            {
                var p = PuppetGaitParams.Walk(true);
                var maxDiff = 0f;
                for (var k = 0; k < 256; k++)
                {
                    var ph = k * 2f * (float)Math.PI / 256f;
                    var pose = PuppetGait.Pose(ph, p, true);
                    var l = PuppetRig.LegPoint(new PuppetVec2(rig.LegCenterXFor(true), rig.AnkleV),
                        rig.LegCenterXFor(true), rig, pose.ThighL, pose.ShinL, pose.FootL, pose.ShortenL);
                    var r = PuppetRig.LegPoint(new PuppetVec2(rig.LegCenterXFor(false), rig.AnkleV),
                        rig.LegCenterXFor(false), rig, pose.ThighR, pose.ShinR, pose.FootR, pose.ShortenR);
                    maxDiff = Math.Max(maxDiff, Math.Abs(l.Y - r.Y));
                }
                Check(maxDiff >= 0.02f, "正面档：左右腿交替抬起（高度差 ≥ 0.02 图高）",
                      "最大高度差 " + maxDiff.ToString("0.####"));
            }

            // 9) 平滑：相位走一小步，顶点不许跳（防"抽搐"）
            {
                var p = PuppetGaitParams.Walk(false);
                var a = new PuppetVec2[PuppetRig.Count];
                var b = new PuppetVec2[PuppetRig.Count];
                var worst = 0f;
                for (var k = 0; k < 628; k++)
                {
                    var ph = k * 0.01f;
                    PuppetRig.Skin(rest, a, rig, PuppetGait.Pose(ph, p, false), false);
                    PuppetRig.Skin(rest, b, rig, PuppetGait.Pose(ph + 0.01f, p, false), false);
                    for (var i = 0; i < PuppetRig.Count; i++)
                        worst = Math.Max(worst, PuppetRig.Distance(a[i], b[i]));
                }
                Check(worst < 0.004f, "时间平滑：Δφ=0.01 时顶点位移 < 0.004", "最大 " + worst.ToString("0.#####"));
            }

            // 10) 空间平滑：相邻网格行的位移差有界 ⇒ 髋线接缝不会裂开
            {
                var p = PuppetGaitParams.Walk(false);
                var worst = 0f; var worstAt = ""; var worstPh = 0f; var worstN = 0;
                for (var k = 0; k < 64; k++)
                {
                    var ph = k * 2f * (float)Math.PI / 64f;
                    PuppetRig.Skin(rest, dst, rig, PuppetGait.Pose(ph, p, false), false);
                    for (var j = 0; j < PuppetRig.GridY; j++)
                    {
                        // 跳过最底一条带：源图那里（y>0.985）已经是鞋底以下的透明余白，
                        // 地面约束会把这几行压平，量它没有意义。可见区域才量。
                        if ((j + 1) / (float)PuppetRig.GridY > 0.985f) continue;
                        for (var i = 0; i <= PuppetRig.GridX; i++)
                        {
                            var n0 = i + j * (PuppetRig.GridX + 1);
                            var n1 = i + (j + 1) * (PuppetRig.GridX + 1);
                            var d = PuppetRig.Distance(dst[n0], dst[n1]) -
                                    PuppetRig.Distance(rest[n0], rest[n1]);
                            if (Math.Abs(d) > worst)
                            {
                                worst = Math.Abs(d); worstAt = "col " + i + " row " + j;
                                worstPh = ph; worstN = n0;
                            }
                        }
                    }
                }
                var pose = PuppetGait.Pose(worstPh, p, false);
                PuppetRig.Skin(rest, dst, rig, pose, false);   // 重算到最坏相位，否则打印的是最后一帧
                Console.WriteLine("       最坏边 " + worstAt + " @φ=" + worstPh.ToString("0.###") +
                                  "  rest=" + rest[worstN] + "→" + rest[worstN + PuppetRig.GridX + 1] +
                                  "  def=" + dst[worstN] + "→" + dst[worstN + PuppetRig.GridX + 1]);
                Console.WriteLine("       pose: thigh=" + pose.ThighL.ToString("0.#") +
                                  " knee=" + pose.ShinL.ToString("0.#") +
                                  " foot=" + pose.FootL.ToString("0.#") +
                                  " shorten=" + pose.ShortenL.ToString("0.###") +
                                  " bob=" + pose.HipBob.ToString("0.####") +
                                  " sway=" + pose.HipSway.ToString("0.####"));
                Check(worst < 0.04f, "空间平滑：相邻行边长变化 < 0.04（接缝不裂）",
                      "最大 " + worst.ToString("0.#####") + " @" + worstAt + "（历史 bug 值 0.120）");
            }

            // 11) 幅度有界 + 地面约束。界按"历史 bug 值 / 当前值"两点标定，不拍脑袋：
            //     位移：历史 bug 0.383（顶点被甩到画布外）／当前 0.223 ⇒ 限 0.30
            //     奔跑：历史 bug 0.523 ／当前 0.307 ⇒ 限 0.45
            {
                var worst = 0f; var lowMin = 2f; var lowMax = -2f;
                var xMin = 2f; var xMax = -2f;
                foreach (var legsSplit in new[] { true, false })
                {
                    var p = PuppetGaitParams.Walk(legsSplit);
                    for (var k = 0; k < 128; k++)
                    {
                        var ph = k * 2f * (float)Math.PI / 128f;
                        PuppetRig.Skin(rest, dst, rig, PuppetGait.Pose(ph, p, legsSplit), legsSplit);
                        var low = 0f;
                        for (var i = 0; i < PuppetRig.Count; i++)
                        {
                            worst = Math.Max(worst, PuppetRig.Distance(rest[i], dst[i]));
                            low = Math.Max(low, dst[i].Y);
                            xMin = Math.Min(xMin, dst[i].X);
                            xMax = Math.Max(xMax, dst[i].X);
                        }
                        lowMin = Math.Min(lowMin, low);
                        lowMax = Math.Max(lowMax, low);
                    }
                }
                Check(worst <= 0.30f, "行走档位移有界 ≤ 0.30 图高（历史 bug 0.383）",
                      "最大 " + worst.ToString("0.####"));
                Check(lowMax <= 1.0001f, "地面约束：任何顶点都不低于地面行（脚不穿地）",
                      "最低点 " + lowMax.ToString("0.####") + " ≤ 1.0");
                Check(lowMin >= 0.93f, "支撑脚始终踩在地上（最低点不低于 0.93）",
                      "最低点下界 " + lowMin.ToString("0.####"));
                // 横向允许 5% 的外溢：源图人物 bbox 是 x 0.185..0.853，外溢那几列落在
                // **透明余白**里（UV 采到的是全透明像素），既不显形也不撕边；
                // 纵向则由 GroundY 硬约束，一脚都不许穿出画布。
                Check(xMin >= -0.05f && xMax <= 1.05f, "顶点横向外溢 ≤ 5%（落在透明余白内）",
                      "x " + xMin.ToString("0.###") + ".." + xMax.ToString("0.###"));
            }

            // 12) 枢轴按实测：不再是旧代码的色带中心 0.25 / 0.75
            {
                var l = rig.LegCenterXFor(true);
                var r = rig.LegCenterXFor(false);
                var oldL = 0.25f; var oldR = 0.75f;
                Check(Math.Abs(l - 0.41f) < 1e-4f && Math.Abs(r - 0.626f) < 1e-3f,
                      "腿枢轴取实测轴线 0.41 / 0.626（旧值 0.25 / 0.75）",
                      "L=" + l.ToString("0.###") + " R=" + r.ToString("0.###") +
                      "  与旧值差 " + Math.Abs(l - oldL).ToString("0.###") + "/" + Math.Abs(r - oldR).ToString("0.###"));
                Check(Math.Abs(rig.BodyCenterX - 0.5f) > 0.01f,
                      "身体中线取实测 0.518（旧代码隐含假设 0.5）",
                      "center=" + rig.BodyCenterX.ToString("0.###"));
            }

            // 13) 跑档也要成立（幅值更大，边界容易破）
            {
                var p = PuppetGaitParams.Run(false);
                var worstLow = 2f; var worstMax = 0f;
                for (var k = 0; k < 128; k++)
                {
                    var ph = k * 2f * (float)Math.PI / 128f;
                    PuppetRig.Skin(rest, dst, rig, PuppetGait.Pose(ph, p, false), false);
                    for (var i = 0; i < PuppetRig.Count; i++)
                    {
                        worstMax = Math.Max(worstMax, PuppetRig.Distance(rest[i], dst[i]));
                        if (i == 0 || dst[i].Y > worstLow) { }
                    }
                    var low = 0f;
                    for (var i = 0; i < PuppetRig.Count; i++) low = Math.Max(low, dst[i].Y);
                    if (low < worstLow) worstLow = low;
                }
                Check(worstMax <= 0.45f, "奔跑档位移有界 ≤ 0.45 图高（历史 bug 0.523）", "最大 " + worstMax.ToString("0.####"));
                Check(worstLow >= 0.90f, "奔跑档最低点不穿地", "最低行 " + worstLow.ToString("0.###"));
            }

            // 14) W-C 脚步：**落地节拍**（纯函数）。三条口径都要成立。
            {
                // ① 站着不动（相位不前进）⇒ 一次落地都不许有
                bool lf;
                var standing = PuppetGait.FootfallsCrossed(1.234f, 1.234f, 2, out lf);
                Check(standing == 0, "脚步：相位不动 ⇒ 零落地（站着不许有脚步声，P2 的音频版）",
                      "跨越次数 " + standing);
                // ② 走满一个周期 ⇒ 恰好两次落地，且左右交替
                var a = PuppetGait.FootfallsCrossed(0f, 2f * (float)Math.PI, 4, out lf);
                var firstIsLeft = lf;
                var b = PuppetGait.FootfallsCrossed(0f, 2f * (float)Math.PI, 4, out lf);
                Check(a == 2, "脚步：一个走周期恰好两次落地", "跨越次数 " + a);
                var leftAt = PuppetGait.FootfallsCrossed(0f, (float)Math.PI / 2 + 1e-3f, 4, out lf);
                var firstHalfLeft = lf;
                var rightAt = PuppetGait.FootfallsCrossed((float)Math.PI / 2 + 1e-3f, (float)Math.PI * 1.5f + 1e-3f, 4, out lf);
                var secondHalfLeft = lf;
                Check(leftAt == 1 && firstHalfLeft && rightAt == 1 && !secondHalfLeft,
                      "脚步：先后半周期左右交替（先左后右）",
                      "前半左=" + firstHalfLeft + " 后半左=" + secondHalfLeft + " 首=" + (firstIsLeft ? "左" : "右"));
                // ③ 相位大跳（传送/卡顿）不许爆出一串：单帧封顶
                var jump = PuppetGait.FootfallsCrossed(0f, 50f, 2, out lf);
                Check(jump == 2, "脚步：相位大跳被单帧封顶（不爆发）", "50 弧度跨越被压到 " + jump);
                // ④ 与帧率无关：把同样的行程拆成不同帧数，总数一致
                var total = 0;
                var prev = 0f;
                for (var k = 1; k <= 7; k++) { var ph = k * 2f * (float)Math.PI / 7f;
                    total += PuppetGait.FootfallsCrossed(prev, ph, 4, out lf); prev = ph; }
                var totalOne = PuppetGait.FootfallsCrossed(0f, 2f * (float)Math.PI, 4, out lf);
                Check(total == totalOne, "脚步：落地次数只由行程决定（与帧率无关）",
                      "7 帧合计 " + total + " / 单帧 " + totalOne);
            }

            // 15) W-C 脚步：**程序化合成**（纯 DSP）。零素材文件，但要经得起量化检查。
            {
                var spec = FootstepSpec.For(FootstepSurface.Sand);
                var buf = FootstepSynth.Render(spec);
                Check(buf.Length == FootstepSynth.SampleCount(spec),
                      "脚步合成：长度 = 采样率 × 时长", buf.Length + " 采样");
                var peak = FootstepSynth.PeakOf(buf);
                Check(Math.Abs(peak - spec.Peak) < .01f, "脚步合成：峰值恰好等于目标（不削顶也不偏轻）",
                      "峰值 " + peak.ToString("0.###") + "（目标 " + spec.Peak.ToString("0.##") + "）");
                var finite = true; var outOfRange = 0;
                foreach (var v in buf)
                {
                    if (float.IsNaN(v) || float.IsInfinity(v)) finite = false;
                    if (Math.Abs(v) > 1.0001f) outOfRange++;
                }
                Check(finite && outOfRange == 0, "脚步合成：无 NaN/Inf、全部 |x| ≤ 1",
                      "异常 " + (finite ? 0 : 1) + " / 越界 " + outOfRange);
                Check(Math.Abs(buf[0]) < 1e-6f, "脚步合成：起振为 0（不爆头）", "首样本 " + buf[0].ToString("0.######"));
                var head = FootstepSynth.Rms(buf, 0, buf.Length / 4);
                var tail = FootstepSynth.Rms(buf, buf.Length * 3 / 4, buf.Length);
                Check(head > tail * 3f, "脚步合成：能量集中在前段（有衰减包络，不是一段平噪声）",
                      "前段 RMS " + head.ToString("0.####") + " ／ 尾段 " + tail.ToString("0.####"));
                Check(Math.Abs(buf[buf.Length - 1]) < .1f * peak, "脚步合成：结尾已衰减（不留尾巴）",
                      "末样本 " + buf[buf.Length - 1].ToString("0.####"));
                var again = FootstepSynth.Render(spec);
                var same = true;
                for (var i = 0; i < buf.Length; i++) if (buf[i] != again[i]) { same = false; break; }
                Check(same, "脚步合成：确定性（同 spec+Seed ⇒ 逐样本一致）", "重算一致=" + same);
                var stone = FootstepSynth.Render(FootstepSpec.For(FootstepSurface.Stone));
                var differs = Math.Abs(stone.Length - buf.Length) > 8;
                Check(differs, "脚步合成：不同地面材质产出不同（时长为证）",
                      "沙 " + buf.Length + " ／ 石 " + stone.Length + " 采样");
                // 左右两条（换种子 + 微调音高）必须真的不同，否则两步听起来像一步
                var left = FootstepSpec.For(FootstepSurface.Sand);
                var right = left; right.Seed = left.Seed + 7919; right.ThumpHz *= 1.06f;
                var rb = FootstepSynth.Render(right);
                var diff = 0;
                for (var i = 0; i < Math.Min(buf.Length, rb.Length); i++) if (buf[i] != rb[i]) diff++;
                Check(diff > buf.Length / 4, "脚步合成：左右两步不完全相同（避免机械重复）",
                      "差异样本 " + diff + "/" + buf.Length);
            }

            // ---- B-12：动作枚举映射表（CharacterActor 7 档 → PuppetActor 3 档）-----------------
            // 为什么能在这里断言：映射表 `PuppetMotionMap` 是纯 C#（放在 PuppetGait.cs），
            // 不依赖 UnityEngine ⇒ 没有 Unity 的机器上这条契约也是**真跑出来的**，不是申报。
            Console.WriteLine();
            Console.WriteLine("== B-12 动作枚举映射 · 离线断言 ==");
            {
                Check(PuppetMotionMap.MapLength == PuppetMotionMap.CharacterMotionCount,
                    "映射表长 == CharacterActor 档位数（枚举漂移会在这里红）",
                    "表长 " + PuppetMotionMap.MapLength + " / 声明 " + PuppetMotionMap.CharacterMotionCount);

                var bad = "";
                for (var i = 0; i < PuppetMotionMap.MapLength; i++)
                {
                    var v = PuppetMotionMap.MapAt(i);
                    if (v < 0 || v >= PuppetMotionMap.PuppetMotionCount) bad += " " + i + "→" + v;
                }
                Check(bad.Length == 0, "每个映射值都落在 PuppetActor 合法档位内（0..2）",
                    bad.Length == 0 ? "7 项全在范围内" : "越界项:" + bad);

                Check(PuppetMotionMap.Map(PuppetMotionMap.Idle) == 0
                      && PuppetMotionMap.Map(PuppetMotionMap.Walk) == 1
                      && PuppetMotionMap.Map(PuppetMotionMap.Run) == 2,
                    "前 3 档一一对应（静立/行走/奔跑不错档）",
                    "Idle→" + PuppetMotionMap.Map(PuppetMotionMap.Idle)
                    + " Walk→" + PuppetMotionMap.Map(PuppetMotionMap.Walk)
                    + " Run→" + PuppetMotionMap.Map(PuppetMotionMap.Run));

                Check(PuppetMotionMap.Map(-1) == -1
                      && PuppetMotionMap.Map(PuppetMotionMap.CharacterMotionCount) == -1,
                    "越界返回 -1（不夹紧、不猜，交给调用方去报）",
                    "Map(-1)=" + PuppetMotionMap.Map(-1)
                    + " Map(" + PuppetMotionMap.CharacterMotionCount + ")="
                    + PuppetMotionMap.Map(PuppetMotionMap.CharacterMotionCount));

                Check(PuppetMotionMap.IsDegraded(PuppetMotionMap.Sneak)
                      && PuppetMotionMap.IsDegraded(PuppetMotionMap.Crouch)
                      && PuppetMotionMap.IsDegraded(PuppetMotionMap.Jump)
                      && PuppetMotionMap.IsDegraded(PuppetMotionMap.Swim)
                      && !PuppetMotionMap.IsDegraded(PuppetMotionMap.Idle)
                      && !PuppetMotionMap.IsDegraded(PuppetMotionMap.Walk)
                      && !PuppetMotionMap.IsDegraded(PuppetMotionMap.Run),
                    "第 4 档起标为降级借档，前 3 档不标（借档必须被看见）",
                    "潜行=" + PuppetMotionMap.IsDegraded(PuppetMotionMap.Sneak)
                    + " 下蹲=" + PuppetMotionMap.IsDegraded(PuppetMotionMap.Crouch)
                    + " 跳跃=" + PuppetMotionMap.IsDegraded(PuppetMotionMap.Jump)
                    + " 游泳=" + PuppetMotionMap.IsDegraded(PuppetMotionMap.Swim));
            }

            // ---- B-03：flipbook 帧索引（P2 的索引版：位移为 0 ⇒ 索引不动）---------------------
            // 为什么能在这里断言：索引公式 `PuppetGait.FrameIndexAt` 是纯 C#，
            // 不依赖 UnityEngine ⇒ 没有 Unity 的机器上这条硬判据也是**真跑出来的**。
            Console.WriteLine();
            Console.WriteLine("== B-03 flipbook 帧索引 · 离线断言 ==");
            {
                const float cyc = 240f;
                const int n = 8;
                Check(PuppetGait.FrameIndexAt(0f, cyc, n) == 0,
                    "位移 0 ⇒ 帧索引 0（站着不动，索引不动）",
                    "FrameIndexAt(0)=" + PuppetGait.FrameIndexAt(0f, cyc, n));

                Check(PuppetGait.FrameIndexAt(cyc * .5f, cyc, n) == 4
                      && PuppetGait.FrameIndexAt(cyc, cyc, n) == 0
                      && PuppetGait.FrameIndexAt(cyc * 1.125f, cyc, n) == 1,
                    "半周期=4 ／ 满周期回 0 ／ 一又八分之一周期=1",
                    "半 " + PuppetGait.FrameIndexAt(cyc * .5f, cyc, n)
                    + " 满 " + PuppetGait.FrameIndexAt(cyc, cyc, n)
                    + " 1.125 " + PuppetGait.FrameIndexAt(cyc * 1.125f, cyc, n));

                var seqOk = true;
                var seq = "";
                for (var k = 0; k < 2 * n; k++)
                {
                    var v = PuppetGait.FrameIndexAt(cyc * (k / (float)n), cyc, n);
                    if (v != (k % n)) { seqOk = false; seq += " " + k + "→" + v; }
                }
                Check(seqOk, "走满两个周期，索引按 0..7 循环且不跳号", seqOk ? "16 步全对" : "错步:" + seq);

                Check(PuppetGait.FrameIndexAt(0f, 0f, n) == -1
                      && PuppetGait.FrameIndexAt(0f, cyc, 0) == -1
                      && PuppetGait.FrameIndexAt(0f, -cyc, n) == -1,
                    "参数非法返回 -1（cycleDistance<=0 / frameCount<=0，不夹紧不猜）",
                    "cyc0=" + PuppetGait.FrameIndexAt(0f, 0f, n)
                    + " n0=" + PuppetGait.FrameIndexAt(0f, cyc, 0)
                    + " cyc-=" + PuppetGait.FrameIndexAt(0f, -cyc, n));

                Check(PuppetGait.FrameIndexAt(-cyc * .25f, cyc, n) == 6,
                    "负位移（倒着走）也落在合法区间，不返回负数",
                    "FrameIndexAt(-0.25 周期)=" + PuppetGait.FrameIndexAt(-cyc * .25f, cyc, n));
            }

            // ---- B-14：步态参数按尺寸标定（**只出数，不定判据**）-------------------------------
            // 为什么要这一张表：同一组参数在 720px 大图上是"看得见在走"，
            // 缩到 170px 之后可能只剩几个像素——那时"参数没变"但"观感没了"。
            // 本段**不新增任何 Check**：阈值该由看图的人定（B-13 拍三态对照图后回来看这张表）。
            Console.WriteLine();
            Console.WriteLine("== B-14 步态参数按尺寸标定 · 离线扫描（只出数，不定判据）==");
            Console.WriteLine("  说明：抬脚量/起伏/缩短都按**角色图高**折算成像素；缩短的基准是腿长(踝-髋)");
            var b14 = new (string Label, PuppetGaitParams P, bool Split)[]
            {
                ("走·正背", PuppetGaitParams.Walk(true), true),
                ("走·侧身", PuppetGaitParams.Walk(false), false),
                ("跑·正背", PuppetGaitParams.Run(true), true),
                ("跑·侧身", PuppetGaitParams.Run(false), false),
            };
            var legLen = Math.Abs(rig.AnkleV - rig.HipV);
            foreach (var c in b14)
            {
                var liftMax = 0f; var liftMin = 0f;
                for (var k = 0; k < 256; k++)
                {
                    var ph = k * 2f * (float)Math.PI / 256f;
                    var pose = PuppetGait.Pose(ph, c.P, c.Split);
                    var ankle = PuppetRig.LegPoint(new PuppetVec2(rig.BodyCenterX, rig.AnkleV),
                        rig.BodyCenterX, rig, pose.ThighL, pose.ShinL, pose.FootL, pose.ShortenL);
                    if (k == 0 || ankle.Y > liftMax) liftMax = ankle.Y;
                    if (k == 0 || ankle.Y < liftMin) liftMin = ankle.Y;
                }
                var lift = liftMax - liftMin;
                Console.WriteLine("  " + c.Label
                    + "  抬脚量 " + lift.ToString("0.####") + " 图高  →  @170 " + (lift * 170f).ToString("0.0")
                    + "px / @230 " + (lift * 230f).ToString("0.0") + "px / @720 " + (lift * 720f).ToString("0") + "px");
                Console.WriteLine("  " + c.Label
                    + "  起伏   " + c.P.BobUnits.ToString("0.####") + " 图高  →  @170 " + (c.P.BobUnits * 170f).ToString("0.0")
                    + "px / @230 " + (c.P.BobUnits * 230f).ToString("0.0") + "px / @720 " + (c.P.BobUnits * 720f).ToString("0") + "px");
                Console.WriteLine("  " + c.Label
                    + "  缩短   " + c.P.ShortenPeak.ToString("0.###") + " 腿长  →  @170 " + (c.P.ShortenPeak * legLen * 170f).ToString("0.0")
                    + "px / @230 " + (c.P.ShortenPeak * legLen * 230f).ToString("0.0") + "px / @720 " + (c.P.ShortenPeak * legLen * 720f).ToString("0") + "px");
                Console.WriteLine("  " + c.Label
                    + "  幅值   大腿 " + c.P.ThighDeg.ToString("0.#") + "° / 屈膝 " + c.P.KneeDeg.ToString("0.#")
                    + "° / 脚掌 " + c.P.FootDeg.ToString("0.#") + "° / 髋摆 " + c.P.SwayUnits.ToString("0.####"));
            }

            // ---- B-10①：V 线能力的「存在性 + 不改默认」---------------------------------------
            // 这里只能断言**纯 C#** 那一半（PuppetRig / PuppetGait / PuppetGaitParams）。
            // 依赖 UnityEngine 的（GaitMode 枚举默认值、MeshDeform/Frames 渲染物）由夹具
            // `FrontEndShot.CheckFramesMode` 与既有网格断言覆盖，**没有 Unity 时那段未跑**。
            Console.WriteLine();
            Console.WriteLine("== B-10 V 线能力 · 存在性 + 不改默认 ==");
            {
                var wf = PuppetGaitParams.Walk(true);
                var ws = PuppetGaitParams.Walk(false);
                var rf = PuppetGaitParams.Run(true);
                var rs = PuppetGaitParams.Run(false);
                Check(wf.ThighDeg == 6f && wf.KneeDeg == 16f && Math.Abs(wf.ShortenPeak - .10f) < 1e-6f
                      && ws.ThighDeg == 14f && ws.KneeDeg == 34f && Math.Abs(ws.ShortenPeak - .03f) < 1e-6f,
                    "走档默认参数未被改动（正背 6°/16°/0.10，侧身 14°/34°/0.03）",
                    "正背 " + wf.ThighDeg + "°/" + wf.KneeDeg + "°/" + wf.ShortenPeak
                    + " 侧身 " + ws.ThighDeg + "°/" + ws.KneeDeg + "°/" + ws.ShortenPeak);
                Check(rf.ThighDeg == 9f && rf.KneeDeg == 24f && Math.Abs(rf.ShortenPeak - .16f) < 1e-6f
                      && rs.ThighDeg == 20f && rs.KneeDeg == 46f && Math.Abs(rs.ShortenPeak - .05f) < 1e-6f,
                    "跑档默认参数未被改动（正背 9°/24°/0.16，侧身 20°/46°/0.05）",
                    "正背 " + rf.ThighDeg + "°/" + rf.KneeDeg + "°/" + rf.ShortenPeak
                    + " 侧身 " + rs.ThighDeg + "°/" + rs.KneeDeg + "°/" + rs.ShortenPeak);
                // WALK-RUN-SIDE：脚掌幅从 16° 标定到 12°（16° 时鞋尖触地触发硬夹塌底，
                // 见 Tools/logs/walk-fix/polish-report.md §5）。不删断言，改成钉新值 + 上下限：
                // 低于 8° 脚掌滚动读感没了，高于 15° 又回到触地塌底区间。
                Check(Math.Abs(rs.FootDeg - 12f) < 1e-6f && rs.FootDeg >= 8f && rs.FootDeg <= 15f,
                    "跑·侧身脚掌幅标定为 12°（区间 [8°,15°]，WALK-RUN-SIDE）",
                    "FootDeg=" + rs.FootDeg.ToString("0.#"));
                Check(Math.Abs(rig.BodyCenterX - .518f) < 1e-3f && Math.Abs(rig.LegCenterX - .41f) < 1e-3f,
                    "骨架默认标定未被改动（BodyCenterX 0.518 / LegCenterX 0.41）",
                    "BodyCenterX=" + rig.BodyCenterX.ToString("0.###") + " LegCenterX=" + rig.LegCenterX.ToString("0.###"));
                // 三档"存在性"：侧身折叠（MeshDeform 的那条链）与 flipbook 索引（Frames 的那条）
                // 都在纯 C# 侧有对应实现 —— 断言它们**确实存在**，不是只有枚举名。
                var collapsed = PuppetGait.Pose(1.2f, ws, false);
                var spread = PuppetGait.Pose(1.2f, wf, true);
                Check(Math.Abs(collapsed.ThighL - collapsed.ThighR) < 1e-6f
                      && Math.Abs(spread.ThighL - spread.ThighR) > 1e-3f,
                    "侧身档折叠成单链（左右同角）、正背档左右反相 —— 两种形态都真的存在",
                    "侧身差 " + Math.Abs(collapsed.ThighL - collapsed.ThighR).ToString("0.####")
                    + " / 正背差 " + Math.Abs(spread.ThighL - spread.ThighR).ToString("0.####"));
                Check(PuppetGait.FrameIndexAt(0f, 240f, 8) == 0 && PuppetMotionMap.MapLength == 7,
                    "B-03 flipbook 索引 与 B-12 映射表 都在（存在性）",
                    "FrameIndexAt(0)=" + PuppetGait.FrameIndexAt(0f, 240f, 8) + " 映射表长 " + PuppetMotionMap.MapLength);
            }

            Console.WriteLine();
            Console.WriteLine("suite finished, " + problems + " problem(s)");
            return problems == 0 ? 0 : 1;
        }
    }
}
