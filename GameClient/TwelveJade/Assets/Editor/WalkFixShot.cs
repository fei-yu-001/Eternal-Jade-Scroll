// WALK-FIX（2026-10-17）：走路 / 奔跑的**判图夹具**。任务书 .batch/tasks/walk-fix-brief.md。
//
// 它回答一个问题：主角的腿到底动没动，动多少像素。
// 做法是**不借道城镇页**：自建相机 + uGUI Canvas + 一台真的 `PuppetActor`，直接把
// 走 / 跑 × 正 / 侧 四个组合各 8 个连续相位渲染成 PNG，并在同一张图上量三个数：
//   · charPixels  —— 立绘像素数。"网格模式整块消失"这种事故，这个数会直接掉到 0。
//   · legDiff     —— 腿带（v 0.76–1）与同组第 0 帧的像素差。**侧身档走路看不出来**这件事，
//                    在这里就是一个数字（不是"角度上有 5.2°"）。
//   · seamHole    —— 胯线（v=0.76，色带的接缝）上下 36px 带里的背景像素数。
//                    色带模式两条腿各转 ±5.2° 时会在接缝处撇出背景楔子（腿撕成两片的样子）。
//
// 相机与位置**每一帧都一样**：720×900、正交、1 世界单位 = 1 像素、立绘高 620px、脚底在 y=860。
// 所以 `before/` 与 `after/` 两组图天生同机位、同位置，可以直接叠着看。
//
// 相位怎么给的（必须披露，否则"连续相位"等于蒙）：
//   · `PuppetActor.Update` 是**真的**每一帧被调用（反射调一次），不是手搓姿态；
//   · 私有字段 `strideDistance` 被**直接钉**到"该相位的等效位移"（= φ/π × CycleDistance，
//     CycleDistance 从私有属性读，不写死 88.4 —— 换步频参数这条不该悄悄失效）；
//   · 私有字段 `time` 每帧钉回 100 值：它只喂 0.25s 的入场 ramp 与待机呼吸，
//     不钉的话批处理里 `w` 可能停在 0.04，腿的摆幅会被 ramp 吃掉一半，量出来的数是假的。
//     这**不改变任何一步步态数学**，只是把"渐入"跳过去。
//   · 待机（P2）那一组更是把 `time` 钉成常量 ⇒ 位图应当逐字节相同：腿要动就会露。
//
// 跑法（Unity 全局一次只跑一个实例）：
//   WALKFIX_TAG=before "D:/ruangong/unity/Editor/6000.3.25f1/Editor/Unity.exe" -batchmode \
//     -projectPath "D:/qiqi_me/Eternal Jade Scroll/GameClient/TwelveJade" \
//     -executeMethod TwelveJade.Editor.WalkFixShot.Run -quit -logFile "Tools/logs/walk-fix/before.log"
// 判据行：`[WALK-FIX] finished, N problem(s)`。problem 只记**夹具自己坏掉**（缺图/空帧/异常），
// 不改美丑 —— 美丑由数字与 PNG 说话。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    public static class WalkFixShot
    {
        const int Width = 720, Height = 900;
        const float CharHeight = 620f;
        const float BaseX = Width * .5f;
        const float BaseY = 860f;
        const int PhaseCount = 8;
        const int IdleFrames = 4;
        // 立绘在画面里的横向范围（front 427/720、side 426/720，取宽的那一侧再留 6px）
        const int RectX0 = 140, RectX1 = 580;
        const int RectTop = 235, RectBottom = 866;
        // 腿带 / 胯线接缝带（v = 0.76 处是色带模式的接缝）
        const int LegTop = 700, SeamTop = 692, SeamBottom = 730;
        const int ColorTolerance = 8;

        static readonly Color BgColor = new Color(.16f, .18f, .17f, 1f);

        static Camera cam;
        static Canvas canvas;
        static RectTransform canvasRoot;
        static RenderTexture rt;
        static Color32 bg;
        static bool bgKnown;
        static string outDir;
        static string tag;
        static readonly List<string> problems = new List<string>();
        static readonly StringBuilder metrics = new StringBuilder();

        static readonly (string key, PuppetActor.Motion motion, bool split, string view)[] Combos =
        {
            ("walk-front", PuppetActor.Motion.Walk, true, "front"),
            ("walk-side", PuppetActor.Motion.Walk, false, "side"),
            ("run-front", PuppetActor.Motion.Run, true, "front"),
            ("run-side", PuppetActor.Motion.Run, false, "side"),
        };

        static MethodInfo updateMethod;
        static PropertyInfo cycleProp;

        sealed class Frame
        {
            public string file;
            public string md5;
            public Color32[] px;
            public int charPixels;
            public int seamHoles;
            public float legL, legR, deform, phase, stride;
            public int populate, populatedVerts;   // 网格模式：OnPopulateMesh 被调了几次 / 最后塞进去多少顶点
            public GaitStat left, right;
        }

        // 一条腿在一帧里的可量测事实（全部从**渲染出的像素**上量，不信代码里的角度）：
        //   BottomY —— 这条腿最低那一行的 yTop。整周期的基线减它就得到"摆动脚离地多少 px"。
        //   FeetCx - HipCx —— 脚带质心相对髖带的水平偏移。它的峰峰值就是"腿摆了多少 px"，
        //                     全身起伏/摇晃被减掉了，所以纯色带模式侧身档会得到 ≈ 0。
        sealed class GaitStat
        {
            public float bottomY;
            public float feetCx, hipCx, feetCy;
            // WALK-POLISH 加的两个测量量（只输出，不进判据）：
            //   footPx —— 脚带（这条腿最低行往上 FootBandRows 行）里的立绘像素数。
            //             鞋被压成细条/拉长时，这个数会明显掉下去或飙上去；鞋保持自己形状时它基本恒定。
            //   footW  —— 脚带里最宽那一行的宽度（px）。楔形化的时候它先塌。
            public int footPx, footW;
        }

        // 脚带高度（立绘 620px 时静止的草鞋约 34px 高，取 34 行）
        const int FootBandRows = 34;

        // 脚带 / 髖带（v 0.95–1.0 与 0.75–0.81）
        const int HipTop = 706, HipBottom = 748;

        public static void Run()
        {
            tag = Environment.GetEnvironmentVariable("WALKFIX_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "after";
            var project = Directory.GetParent(Application.dataPath).FullName;
            var repoRoot = Directory.GetParent(Directory.GetParent(project).FullName).FullName;
            outDir = Path.Combine(repoRoot, "Tools", "logs", "walk-fix", tag);
            Directory.CreateDirectory(outDir);
            updateMethod = typeof(PuppetActor).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            cycleProp = typeof(PuppetActor).GetProperty("CycleDistance", BindingFlags.Instance | BindingFlags.NonPublic);
            metrics.AppendLine("# " + tag + " 逐帧数字（WalkFixShot 直接写出，未经手工编辑）");
            metrics.AppendLine();
            metrics.AppendLine("相机 720x900 正交 / 1 单位 = 1 像素 / 立绘高 620px / 脚底 y=860（自画面顶算）");
            metrics.AppendLine();
            try
            {
                SetupStage();
                Metrics("Scene", 0, "UnscaledDeltaTime 实测 = " + Time.unscaledDeltaTime.ToString("0.0000") + " s");
                foreach (var mode in new[] { PuppetActor.GaitMode.Slices, PuppetActor.GaitMode.MeshDeform })
                    foreach (var combo in Combos)
                        RunCombo(ModeName(mode), mode, combo);
                foreach (var mode in new[] { PuppetActor.GaitMode.Slices, PuppetActor.GaitMode.MeshDeform })
                    foreach (var view in new[] { true, false })
                        RunIdle(ModeName(mode), mode, view);
            }
            catch (Exception ex)
            {
                problems.Add("夹具异常: " + ex);
            }
            File.WriteAllText(Path.Combine(outDir, "metrics.md"), metrics.ToString(), new UTF8Encoding(false));
            if (problems.Count > 0)
                foreach (var p in problems) Debug.LogError("[WALK-FIX] " + p);
            Debug.Log("[WALK-FIX] finished, " + problems.Count + " problem(s) → " + outDir);
        }

        static string ModeName(PuppetActor.GaitMode mode) =>
            mode == PuppetActor.GaitMode.MeshDeform ? "mesh" : "slices";

        static void SetupStage()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("WalkFix camera", typeof(Camera));
            cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Height * .5f;      // 900px 高 ⇒ 1 单位 = 1 像素
            cam.transform.position = new Vector3(BaseX, Height * .5f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BgColor;
            cam.nearClipPlane = .1f;
            cam.farClipPlane = 100f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;

            var canvasGo = new GameObject("WalkFix canvas", typeof(Canvas), typeof(CanvasScaler));
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 10f;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            canvasRoot = canvasGo.GetComponent<RectTransform>();
            Canvas.ForceUpdateCanvases();
        }

        static Texture2D Art(string view) =>
            Resources.Load<Texture2D>("Art/Characters/Stand/farmer-male-0-" + view);

        static PuppetActor NewPuppet(string view, bool split)
        {
            var art = Art(view);
            if (art == null)
            {
                problems.Add("立绘缺失：Resources/Art/Characters/Stand/farmer-male-0-" + view);
                return null;
            }
            var size = new Vector2(CharHeight * art.width / art.height, CharHeight);
            // 锚点 (0,1) = 画布左上角，anchoredPosition 从那里量且 y 向下为负
            // （社区页 `SyncTraveler` 就是这么喂的：`-worldPos.y * worldScale`）。
            // 第一版写成 +860 直接把人物送到画布外 860px 处，四组图全是一整片背景。
            var p = PuppetActor.Create(canvasRoot, art, new Vector2(BaseX, -BaseY), size);
            p.SetLegSplit(split);
            p.SetMotion(PuppetActor.Motion.Idle);
            return p;
        }

        static void RunCombo(string mode, PuppetActor.GaitMode gait, (string key, PuppetActor.Motion motion, bool split, string view) c)
        {
            var p = NewPuppet(c.view, c.split);
            if (p == null) return;
            p.SetMotion(c.motion);
            p.SetGaitMode(gait);
            var frames = new List<Frame>();
            for (var i = 0; i < PhaseCount; i++)
            {
                SetPhase(p, Mathf.PI * 2f * i / PhaseCount);
                Tick(p);
                frames.Add(Capture(mode + "-" + c.key + "-p" + i.ToString("00"), p, c.split));
            }
            Analyse(mode, c.key, frames, true);
            WriteStrips(mode, c.key, frames);
            UnityEngine.Object.DestroyImmediate(p.Root.gameObject);
            Canvas.ForceUpdateCanvases();
        }

        // P2：待机若干帧。`time` 钉成常量 ⇒ 位图必须逐字节相同；相位也一步都不许推进。
        static void RunIdle(string mode, PuppetActor.GaitMode gait, bool split)
        {
            var view = split ? "front" : "side";
            var key = "idle-" + view;
            var p = NewPuppet(view, split);
            if (p == null) return;
            p.SetMotion(PuppetActor.Motion.Idle);
            p.SetGaitMode(gait);
            var frames = new List<Frame>();
            for (var i = 0; i < IdleFrames; i++)
            {
                Tick(p);       // 不碰 strideDistance：位移为 0
                frames.Add(Capture(mode + "-" + key + "-s" + i.ToString("00"), p, split));
            }
            Analyse(mode, key, frames, false);
            var phaseDrift = Mathf.Abs(frames[frames.Count - 1].phase - frames[0].phase);
            if (phaseDrift > 1e-4f)
                problems.Add(mode + " " + key + "：待机期间相位漂了 " + phaseDrift.ToString("0.0000") + " rad（P2）");
            UnityEngine.Object.DestroyImmediate(p.Root.gameObject);
            Canvas.ForceUpdateCanvases();
        }

        static void SetPhase(PuppetActor p, float phase)
        {
            var cycle = (float)cycleProp.GetValue(p);
            SetField(p, "strideDistance", phase * cycle / Mathf.PI);
        }

        // 每帧：`time` 钉到常量（抵消 Update 里的 +=），跑一次真的 Update，再让 Canvas 重新出网格。
        static void Tick(PuppetActor p)
        {
            SetField(p, "time", 100f - Time.unscaledDeltaTime);
            updateMethod.Invoke(p, null);
            Canvas.ForceUpdateCanvases();
        }

        static void SetField(PuppetActor p, string name, float value)
        {
            var f = typeof(PuppetActor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) { problems.Add("夹具找不到私有字段 " + name + "（代码改过名？）"); return; }
            f.SetValue(p, value);
        }

        static Frame Capture(string label, PuppetActor p, bool split)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            cam.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var bytes = tex.EncodeToPNG();
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            if (!bgKnown)
            {
                bg = px[2 * Width + 2];
                bgKnown = true;
                Metrics("bg", 0, "背景实际字节 = " + bg.r + "," + bg.g + "," + bg.b);
            }
            var file = Path.Combine(outDir, label + ".png");
            File.WriteAllBytes(file, bytes);
            var mesh = p.MeshForCheck;
            var f = new Frame
            {
                file = label + ".png",
                md5 = Hash(bytes),
                px = px,
                charPixels = CountCharacter(px),
                seamHoles = CountBackground(px, RectX0, SeamTop, RectX1, SeamBottom),
                legL = p.JointAngleForCheck(PuppetActor.JointLegLeft),
                legR = p.JointAngleForCheck(PuppetActor.JointLegRight),
                deform = p.DeformMaxDisplacementForCheck,
                populate = mesh != null ? mesh.PopulateCountForCheck : -1,
                populatedVerts = mesh != null ? mesh.LastPopulatedVertexCountForCheck : -1,
                phase = p.GaitPhaseForCheck % (Mathf.PI * 2f),
                stride = p.StrideDistanceForCheck,
            };
            f.left = split ? Measure(px, RectX0, (RectX0 + RectX1) / 2) : Measure(px, RectX0, RectX1);
            f.right = split ? Measure(px, (RectX0 + RectX1) / 2, RectX1) : f.left;
            return f;
        }

        static GaitStat Measure(Color32[] px, int x0, int x1)
        {
            // 两趟：先找这条腿最低的那一行（脚底），再把"脚带"锚在它上方 40px。
            // 不能用固定绝对行 —— 摆动脚抬起来会抬出量带，质心就只剩下杂像素（上一版量出 435px 的假数）。
            var stat = new GaitStat { bottomY = -1f };
            for (var y = RectTop; y < RectBottom; y++)
            {
                var row = (Height - 1 - y) * Width;
                for (var x = x0; x < x1; x++)
                    if (!IsBg(px[row + x]) && y > stat.bottomY) { stat.bottomY = y; break; }
            }
            if (stat.bottomY < 0f) return stat;
            var feetTop = Mathf.Max(RectTop, stat.bottomY - 40f);
            float sx = 0f, sy = 0f, feetN = 0f, hipX = 0f, hipN = 0f;
            for (var y = (int)feetTop; y <= (int)stat.bottomY; y++)
            {
                var row = (Height - 1 - y) * Width;
                for (var x = x0; x < x1; x++)
                {
                    if (IsBg(px[row + x])) continue;
                    sx += x; sy += y; feetN++;
                }
            }
            for (var y = HipTop; y < HipBottom; y++)
            {
                var row = (Height - 1 - y) * Width;
                for (var x = x0; x < x1; x++)
                    if (!IsBg(px[row + x])) { hipX += x; hipN++; }
            }
            if (feetN > 0f) { stat.feetCx = sx / feetN; stat.feetCy = sy / feetN; }
            // 脚带：最低行 + 往上 FootBandRows 行（鞋就住在这一段里）
            var bandTop = Mathf.Max(RectTop, (int)stat.bottomY - (FootBandRows - 1));
            for (var y = bandTop; y <= (int)stat.bottomY; y++)
            {
                var row = (Height - 1 - y) * Width;
                var n = 0;
                for (var x = x0; x < x1; x++) if (!IsBg(px[row + x])) n++;
                stat.footPx += n;
                if (n > stat.footW) stat.footW = n;
            }
            if (hipN > 0f) stat.hipCx = hipX / hipN;
            return stat;
        }

        static void Analyse(string mode, string key, List<Frame> frames, bool gait)
        {
            var md5s = new HashSet<string>();
            var legDiffs = new List<int>();
            var prevDiffs = new List<int>();
            foreach (var f in frames) md5s.Add(f.md5);
            for (var i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                legDiffs.Add(Diff(frames[0].px, f.px, RectX0, LegTop, RectX1, RectBottom));
                prevDiffs.Add(i == 0 ? 0 : Diff(frames[i - 1].px, f.px, RectX0, LegTop, RectX1, RectBottom));
                Metrics(mode + " " + key, i,
                    "file=" + f.file + " md5=" + f.md5.Substring(0, 8) + " charPixels=" + f.charPixels +
                    " legDiff=" + legDiffs[i] + " legDpPrev=" + prevDiffs[i] +
                    " seamHoles=" + f.seamHoles + " phase=" + f.phase.ToString("0.000") +
                    " legL=" + Angle(f.legL) + " legR=" + Angle(f.legR) +
                    " deform=" + f.deform.ToString("0.0000") +
                    (f.populate >= 0 ? " populate=" + f.populate + " verts=" + f.populatedVerts : ""));
                if (f.charPixels <= 0) problems.Add(mode + " " + key + " " + f.file + "：立绘像素为 0（整块消失）");
            }
            var legMax = 0f;
            var prevMin = float.MaxValue;
            foreach (var n in legDiffs) if (n > legMax) legMax = n;
            for (var i = 1; i < prevDiffs.Count; i++) if (prevDiffs[i] < prevMin) prevMin = prevDiffs[i];
            if (prevMin == float.MaxValue) prevMin = 0f;
            var seamBase = frames.Count > 0 ? frames[0].seamHoles : 0;
            var seamMax = 0;
            foreach (var f in frames) if (f.seamHoles > seamMax) seamMax = f.seamHoles;
            var deformMax = 0f;
            foreach (var f in frames) deformMax = Mathf.Max(deformMax, f.deform);
            var footPxL = FootBandRange(frames, true, out var footWidL);
            var footPxR = FootBandRange(frames, false, out var footWidR);
            var liftL = PeakLift(frames, true);
            var liftR = PeakLift(frames, false);
            var swingL = PeakSwing(frames, true);
            var swingR = PeakSwing(frames, false);
            metrics.AppendLine();
            metrics.AppendLine("## " + mode + " / " + key + " 汇总");
            metrics.AppendLine("- 唯一帧数 " + md5s.Count + " / " + frames.Count);
            metrics.AppendLine("- 腿带相对第 0 帧最大像素差 " + legMax + "（腿带面积 " + (RectX1 - RectX0) * (RectBottom - LegTop) + " px）");
            metrics.AppendLine("- 相邻帧腿带最小像素差 " + prevMin);
            metrics.AppendLine("- 胯线接缝带背景像素 " + seamBase + "（φ=0）→ 最大 " + seamMax);
            metrics.AppendLine("- 顶点最大位移 " + deformMax.ToString("0.0000") + " 图高");
            metrics.AppendLine("- 摆动脚峰值离地 " + liftL.ToString("0.0") + " / " + liftR.ToString("0.0") +
                " px（立绘高 620px 的 " + (liftL / CharHeight * 100f).ToString("0.0") + "% / " +
                (liftR / CharHeight * 100f).ToString("0.0") + "%）");
            metrics.AppendLine("- 脚相对髖的摆幅峰峰 " + swingL.ToString("0.0") + " / " + swingR.ToString("0.0") + " px");
            metrics.AppendLine("- 脚带像素 " + footPxL + " / " + footPxR + "（min~max，立绘 620px 时静止草鞋 ≈1900 px）");
            metrics.AppendLine("- 脚带最宽行 " + footWidL + " / " + footWidR + "（px，min~max）");
            metrics.AppendLine();
            Debug.Log("[WALK-FIX] " + mode + "/" + key + (gait ? "（走/跑）" : "（待机）") +
                " 唯一帧 " + md5s.Count + "/" + frames.Count +
                " 腿带最大差 " + legMax + "px 相邻最小差 " + prevMin +
                " 接缝背景 " + seamBase + "→" + seamMax +
                " 抬脚 " + liftL.ToString("0.0") + "/" + liftR.ToString("0.0") +
                "px 摆幅 " + swingL.ToString("0.0") + "/" + swingR.ToString("0.0") +
                "px 顶点位移 " + deformMax.ToString("0.0000") +
                " 脚带px " + footPxL + "/" + footPxR + " 脚带宽 " + footWidL + "/" + footWidR);
        }

        // 基线 = 整周期里这条腿最低的那一帧（= 踩地那帧）；峰值离地 = 基线 - 最高那帧。
        static float PeakLift(List<Frame> frames, bool left)
        {
            var baseline = float.MinValue;
            foreach (var f in frames)
            {
                var s = left ? f.left : f.right;
                if (s.bottomY > baseline) baseline = s.bottomY;
            }
            var peak = 0f;
            foreach (var f in frames)
            {
                var s = left ? f.left : f.right;
                var lift = baseline - s.bottomY;
                if (lift > peak) peak = lift;
            }
            return peak;
        }

        // 脚带像素 / 最宽行在整个周期里的 min~max。鞋保持自己形状 ⇒ 两个数都基本不动；
        // 被压成楔形或者被拉长 ⇒ 明显跳。
        static string FootBandRange(List<Frame> frames, bool left, out string widths)
        {
            var minPx = int.MaxValue; var maxPx = int.MinValue;
            var minW = int.MaxValue; var maxW = int.MinValue;
            foreach (var f in frames)
            {
                var s = left ? f.left : f.right;
                if (s.footPx < minPx) minPx = s.footPx;
                if (s.footPx > maxPx) maxPx = s.footPx;
                if (s.footW < minW) minW = s.footW;
                if (s.footW > maxW) maxW = s.footW;
            }
            widths = (minW == int.MaxValue ? 0 : minW) + "~" + (maxW == int.MinValue ? 0 : maxW);
            return (minPx == int.MaxValue ? 0 : minPx) + "~" + (maxPx == int.MinValue ? 0 : maxPx);
        }

        static float PeakSwing(List<Frame> frames, bool left)
        {
            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var f in frames)
            {
                var s = left ? f.left : f.right;
                var v = s.feetCx - s.hipCx;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            return max > min ? max - min : 0f;
        }

        // 色带模式有真关节（读得到角度）；网格模式没有关节对象，`JointAngleForCheck` 返回 NaN，
        // 这里必须把它显式写成 "—" —— 写成 0° 会把"没有这个关节"伪装成"关节在 0 度"。
        static string Angle(float deg) => float.IsNaN(deg) ? "—" : deg.ToString("0.00") + "°";

        static void WriteStrips(string mode, string key, List<Frame> frames)
        {
            // 腿带 1:1（440 × 175），八帧横排 —— 腿到底动没动，一眼的事。
            var legW = RectX1 - RectX0;
            var legH = RectBottom - 660;
            var legStrip = new Color32[legW * frames.Count * legH];
            for (var i = 0; i < frames.Count; i++)
                for (var y = 0; y < legH; y++)
                    for (var x = 0; x < legW; x++)
                    {
                        var src = (Height - 1 - (660 + y)) * Width + (RectX0 + x);
                        var dst = y * (legW * frames.Count) + i * legW + x;
                        legStrip[dst] = frames[i].px[src];
                    }
            WriteTexture(legStrip, legW * frames.Count, legH, mode + "-" + key + "-legs.png");

            // 全身 1/2 缩放（220 × 315），八帧横排 —— 起伏与躯干倾角在这一张里读。
            var bw = (RectX1 - RectX0) / 2;
            var bh = (RectBottom - RectTop) / 2;
            var bodyStrip = new Color32[bw * frames.Count * bh];
            for (var i = 0; i < frames.Count; i++)
                for (var y = 0; y < bh; y++)
                    for (var x = 0; x < bw; x++)
                    {
                        var sum = 0;
                        int r = 0, g = 0, b = 0;
                        for (var dy = 0; dy < 2; dy++)
                            for (var dx = 0; dx < 2; dx++)
                            {
                                var sx = RectX0 + x * 2 + dx;
                                var sy = RectTop + y * 2 + dy;
                                var c = frames[i].px[(Height - 1 - sy) * Width + sx];
                                r += c.r; g += c.g; b += c.b; sum++;
                            }
                        bodyStrip[y * (bw * frames.Count) + i * bw + x] =
                            new Color32((byte)(r / sum), (byte)(g / sum), (byte)(b / sum), 255);
                    }
            WriteTexture(bodyStrip, bw * frames.Count, bh, mode + "-" + key + "-body.png");
        }

        static void WriteTexture(Color32[] px, int w, int h, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, name), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static int CountCharacter(Color32[] px)
        {
            var n = 0;
            for (var y = RectTop; y < RectBottom; y++)
            {
                var row = (Height - 1 - y) * Width;
                for (var x = RectX0; x < RectX1; x++) if (!IsBg(px[row + x])) n++;
            }
            return n;
        }

        static bool IsBg(Color32 c) =>
            Math.Abs(c.r - bg.r) <= ColorTolerance &&
            Math.Abs(c.g - bg.g) <= ColorTolerance &&
            Math.Abs(c.b - bg.b) <= ColorTolerance;

        static int CountBackground(Color32[] px, int x0, int yTop0, int x1, int yTop1)
        {
            var n = 0;
            for (var y = yTop0; y < yTop1; y++)
            {
                var row = (Height - 1 - y) * Width;
                for (var x = x0; x < x1; x++) if (IsBg(px[row + x])) n++;
            }
            return n;
        }

        static int Diff(Color32[] a, Color32[] b, int x0, int yTop0, int x1, int yTop1)
        {
            var n = 0;
            for (var y = yTop0; y < yTop1; y++)
            {
                var row = (Height - 1 - y) * Width;
                for (var x = x0; x < x1; x++)
                {
                    var i = row + x;
                    if (Math.Abs(a[i].r - b[i].r) > ColorTolerance ||
                        Math.Abs(a[i].g - b[i].g) > ColorTolerance ||
                        Math.Abs(a[i].b - b[i].b) > ColorTolerance) n++;
                }
            }
            return n;
        }

        static string Hash(byte[] bytes)
        {
            using (var md5 = MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        static void Metrics(string key, int index, string line) =>
            metrics.AppendLine("`" + key + "` #" + index + " " + line);
    }
}
