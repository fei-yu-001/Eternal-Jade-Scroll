// 员工C（手感与听觉）的**离线取证 / 验证工程**。
//
// 为什么要有它：任务书 Y-3 的"总禁止"里写明 **一切手感改动都要有可复算的数字**
// （时长/曲线/触点数），而且本仓**一律运行期程序合成**、盘上零音频文件 ——
// 于是"听感"这种主观量必须换成可打印、可复算、可回归的数字，否则无从判对错。
//
// 做法照抄既有 `PuppetRigChecks`：**DSP 层（`FootstepSynth.cs`）不引 UnityEngine**，
// 所以用 `<Compile Include>` 把它链进来，`dotnet run` 就能在**没有 Unity 的机器**上跑。
// 本机确实没有 Unity，因此这是 C 线唯一能自证的通道。
//
// 输出纪律：**机器读的那半必须 ASCII**（键名 + 数字），中文只作叙述；
// 这是 D-57 / T6 那一族的口径 —— 读不出的判据行等于没有判据行。
using System;
using System.Globalization;
using TwelveJade.Presentation;

namespace TwelveJade.AudioChecks
{
    public static class Program
    {
        static int failures;

        static void Check(bool ok, string what, string detail)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what + " :: " + detail);
            if (!ok) failures++;
        }

        static string Num(float v, int digits) => v.ToString("0." + new string('0', digits), CultureInfo.InvariantCulture);

        /// <summary>把一个 float[] 量成"音频学上说得清"的几个数。</summary>
        struct Metrics
        {
            public int N;
            public float DurMs;
            public float Peak;
            public float AttackMs;    // 起振：从 0 到峰值用了多久（毫秒）
            public float Decay20Ms;   // 衰减：峰值之后掉到 -20dB（0.1 倍）用了多久
            public float ZcrHz;       // 过零率（次/秒）：粗粒度的"亮度"代理，越大越亮
            public float HeadRms;
            public float TailRms;
            public float RmsRatio;    // 前段/尾段能量比：>1 说明有衰减包络，不是一段平噪声
            public float CentroidHz;  // 频谱质心（Goertzel）：比"过零率"干净得多的亮度/音高代理
        }

        // 对数分布的探测频率（约 1.35 倍一档）。为什么不用 FFT：只要质心一个数，
        // Goertzel 在十几个频点上直接算功率就够，代码短、无依赖、结果可手算复核。
        static readonly float[] ProbeHz = {
            40f, 54f, 73f, 99f, 133f, 180f, 243f, 328f, 443f, 598f,
            807f, 1090f, 1470f, 1990f, 2680f, 3620f, 4890f, 6600f
        };

        /// <summary>Goertzel 单点功率。</summary>
        static float Power(float[] buf, int rate, float hz)
        {
            var w = 2.0 * Math.PI * hz / rate;
            var coeff = 2.0 * Math.Cos(w);
            var s1 = 0.0; var s2 = 0.0;
            for (var i = 0; i < buf.Length; i++)
            {
                var s0 = buf[i] + coeff * s1 - s2;
                s2 = s1; s1 = s0;
            }
            var p = s1 * s1 + s2 * s2 - coeff * s1 * s2;
            return p > 0 ? (float)p : 0f;
        }

        /// <summary>频谱质心：能量按频率加权的重心。听着"更亮" ⇒ 重心更高。</summary>
        static float Centroid(float[] buf, int rate)
        {
            var num = 0.0; var den = 0.0;
            foreach (var hz in ProbeHz)
            {
                var p = Power(buf, rate, hz);
                num += hz * p;
                den += p;
            }
            return den <= 1e-12 ? 0f : (float)(num / den);
        }

        static Metrics Measure(float[] buf, int rate)
        {
            var m = new Metrics { N = buf.Length };
            m.DurMs = buf.Length * 1000f / rate;

            var peakIdx = 0;
            var max = 0f;
            for (var i = 0; i < buf.Length; i++)
            {
                var a = Math.Abs(buf[i]);
                if (a > max) { max = a; peakIdx = i; }
            }
            m.Peak = max;
            m.AttackMs = peakIdx * 1000f / rate;

            // -20dB = 峰值的 0.1 倍；取"最后一个还在阈值之上的位置"
            var last = peakIdx;
            for (var i = peakIdx; i < buf.Length; i++) if (Math.Abs(buf[i]) >= max * .1f) last = i;
            m.Decay20Ms = (last - peakIdx) * 1000f / rate;

            var crossings = 0;
            for (var i = 1; i < buf.Length; i++)
                if ((buf[i - 1] < 0f) != (buf[i] < 0f)) crossings++;
            m.ZcrHz = crossings * 1000f / Math.Max(1f, m.DurMs);

            m.HeadRms = FootstepSynth.Rms(buf, 0, buf.Length / 4);
            m.TailRms = FootstepSynth.Rms(buf, buf.Length * 3 / 4, buf.Length);
            m.RmsRatio = m.TailRms > 1e-9f ? m.HeadRms / m.TailRms : float.PositiveInfinity;
            m.CentroidHz = Centroid(buf, rate);
            return m;
        }

        /// <summary>左右两条**逐样本**差异率（%）：100 只说明"噪声种子不同"，近乎必然成立，
        /// 所以单看它不够 —— 它能量到"差多少"，得配合下面的 DiffEnergyPct。</summary>
        static float DiffRate(float[] a, float[] b)
        {
            var n = Math.Min(a.Length, b.Length);
            if (n == 0) return 0f;
            var diff = 0;
            for (var i = 0; i < n; i++) if (a[i] != b[i]) diff++;
            return diff * 100f / n;
        }

        /// <summary>左右两条的**差异能量占比**（%）：把 (a-b) 的 RMS 与 a 的 RMS 相比。
        /// 这才是"听上去差多少"的粗代理 —— 逐样本差异率几乎必然 100%，没有分辨力。</summary>
        static float DiffEnergyPct(float[] a, float[] b)
        {
            var n = Math.Min(a.Length, b.Length);
            if (n == 0) return 0f;
            var sum = 0.0;
            for (var i = 0; i < n; i++)
            {
                var d = (double)a[i] - b[i];
                sum += d * d;
            }
            var diffRms = Math.Sqrt(sum / n);
            var baseRms = FootstepSynth.Rms(a, 0, n);
            return baseRms <= 1e-9f ? 0f : (float)(diffRms / baseRms) * 100f;
        }

        /// <summary>导出 16bit PCM wav（给**人耳朵**用；默认不导出，跑不带 --wav 就零文件产生）。
        /// 为什么默认不导出：本仓"盘上零音频文件"是硬口径，产物必须能被指向仓外目录。</summary>
        static void WriteWav(string path, float[] buf, int rate)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            using (var fs = System.IO.File.Open(path, System.IO.FileMode.Create))
            using (var bw = new System.IO.BinaryWriter(fs))
            {
                var dataLen = buf.Length * 2;
                void Str(string s) { foreach (var c in s) bw.Write((byte)c); }
                bw.Write((byte)'R'); bw.Write((byte)'I'); bw.Write((byte)'F'); bw.Write((byte)'F');
                bw.Write(36 + dataLen);
                Str("WAVE");
                Str("fmt "); bw.Write(16); bw.Write((ushort)1); bw.Write((ushort)1);
                bw.Write(rate); bw.Write(rate * 2); bw.Write((ushort)2); bw.Write((ushort)16);
                Str("data"); bw.Write(dataLen);
                foreach (var v in buf)
                {
                    var s = v < -1f ? -1f : (v > 1f ? 1f : v);
                    var iv = (int)Math.Round(s * 32767.0);
                    bw.Write((short)(iv < short.MinValue ? short.MinValue : (iv > short.MaxValue ? short.MaxValue : iv)));
                }
            }
        }

        /// <summary>C-19/C-29：量"合成一次要多久 / 占多大"，并跟一个预算比。
        /// 为什么要它：底噪一条 26 万采样，**合成是在主线程上做的**，
        /// 若某天有人把它挪到每帧调用，帧率会直接掉 ⇒ 用一个数把它钉住。</summary>
        static void Cost(string name, Func<float[]> render, float budgetMs)
        {
            render();                                  // 预热（JIT / 首次分配不计）
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var buf = render();
            sw.Stop();
            var ms = (float)sw.Elapsed.TotalMilliseconds;
            var bytes = buf.Length * 4;
            Console.WriteLine(
                "COST name=" + name +
                " samples=" + buf.Length +
                " bytes=" + bytes +
                " render_ms=" + Num(ms, 2) +
                " budget_ms=" + Num(budgetMs, 0));
            Check(ms < budgetMs, name + "：单次合成耗时在预算内（超了就说明它不该在主线程每帧调）",
                  Num(ms, 2) + "ms / 预算 " + Num(budgetMs, 0) + "ms");
        }

        public static int Main(string[] args)
        {
            // --wav <目录>：把四种地面 × 左右导出成 16bit PCM wav，**目录由调用方给**
            // （本仓"盘上零音频文件"是硬口径，所以默认一个字节都不产）。
            var wavDir = (string)null;
            for (var i = 0; i < args.Length; i++)
                if (args[i] == "--wav" && i + 1 < args.Length) { wavDir = args[i + 1]; i++; }

            Console.WriteLine("AudioChecks —— 员工C（手感与听觉）离线取证");
            Console.WriteLine("（纯 C# DSP，无 Unity；数字可复算，供每轮回归对比）");
            Console.WriteLine(wavDir == null
                ? "模式：只量数（未给 --wav ⇒ 不落任何音频文件）"
                : "模式：量数 + 导出 wav 到 " + wavDir);
            Console.WriteLine("");

            var surfaces = new[]
            {
                FootstepSurface.Sand, FootstepSurface.Stone,
                FootstepSurface.Wood, FootstepSurface.Grass
            };

            var lens = new System.Collections.Generic.List<int>();
            var rows = 0;

            Console.WriteLine("== C-01 脚步定档：四种地面的实测量 ==");
            foreach (var s in surfaces)
            {
                var spec = FootstepSpec.For(s);
                var left = FootstepSynth.Render(spec);
                var rightSpec = spec;
                rightSpec.Seed = spec.Seed + 7919;      // 与 FrontEndFootsteps.SetupFootsteps 同一套错开方式
                rightSpec.ThumpHz *= 1.06f;
                var right = FootstepSynth.Render(rightSpec);
                var m = Measure(left, spec.Rate);

                Console.WriteLine(
                    "FOOTSTEP surface=" + s +
                    " n=" + m.N +
                    " dur_ms=" + Num(m.DurMs, 1) +
                    " peak=" + Num(m.Peak, 3) +
                    " attack_ms=" + Num(m.AttackMs, 2) +
                    " decay20_ms=" + Num(m.Decay20Ms, 1) +
                    " zcr_hz=" + Num(m.ZcrHz, 0) +
                    " head_rms=" + Num(m.HeadRms, 4) +
                    " tail_rms=" + Num(m.TailRms, 4) +
                    " rms_ratio=" + Num(m.RmsRatio, 1) +
                    " centroid_hz=" + Num(m.CentroidHz, 0) +
                    " lr_sample_diff_pct=" + Num(DiffRate(left, right), 1) +
                    " lr_energy_diff_pct=" + Num(DiffEnergyPct(left, right), 1));
                rows++;
                lens.Add(m.N);

                if (wavDir != null)
                {
                    WriteWav(System.IO.Path.Combine(wavDir, "footstep-" + s + "-L.wav"), left, spec.Rate);
                    WriteWav(System.IO.Path.Combine(wavDir, "footstep-" + s + "-R.wav"), right, spec.Rate);
                }

                // ---- 每条都要过的不变量（不许为"让验收过"放宽）----
                Check(Math.Abs(m.Peak - spec.Peak) < .01f, s + "：峰值恰好等于目标（不削顶也不偏轻）",
                      "峰值 " + Num(m.Peak, 3) + " / 目标 " + Num(spec.Peak, 2));
                Check(Math.Abs(left[0]) < 1e-6f, s + "：起振为 0（不爆头）", "首样本 " + Num(left[0], 6));
                Check(Math.Abs(left[left.Length - 1]) < .1f * m.Peak, s + "：结尾已衰减（不留尾巴）",
                      "末样本 " + Num(left[left.Length - 1], 4));
                Check(m.RmsRatio > 3f, s + "：能量集中在前段（有衰减包络）",
                      "前/尾 RMS = " + Num(m.RmsRatio, 1));

                var finite = true; var outOfRange = 0;
                foreach (var v in left)
                {
                    if (float.IsNaN(v) || float.IsInfinity(v)) finite = false;
                    if (Math.Abs(v) > 1.0001f) outOfRange++;
                }
                Check(finite && outOfRange == 0, s + "：无 NaN/Inf、全部 |x| <= 1",
                      "异常 " + (finite ? 0 : 1) + " / 越界 " + outOfRange);

                var again = FootstepSynth.Render(spec);
                var same = true;
                for (var i = 0; i < left.Length; i++) if (left[i] != again[i]) { same = false; break; }
                Check(same, s + "：确定性（同 spec+Seed 逐样本一致）", "重算一致=" + same);

                Check(DiffRate(left, right) > 25f, s + "：左右两步不完全相同（避免机械重复）",
                      "逐样本差异 " + Num(DiffRate(left, right), 1) + "%");
                // 逐样本差异率几乎必然 100%（噪声种子不同），分辨力为零 ⇒ 再用能量差异补一刀。
                Check(DiffEnergyPct(left, right) > 25f, s + "：左右两步的差异要听得出来（能量占比）",
                      "差异能量 " + Num(DiffEnergyPct(left, right), 1) + "%");
            }

            // 四种地面必须真的能听出区别：用"长度两两不同"作最粗的一条证据
            var allDistinct = true;
            for (var i = 0; i < lens.Count; i++)
                for (var j = i + 1; j < lens.Count; j++)
                    if (lens[i] == lens[j]) allDistinct = false;
            Check(allDistinct, "四种地面互不相同（时长两两不同为证）", string.Join(" / ", lens) + " 采样");

            // ---- C-12：步幅（走得有多快）→ 响度 / 音高微升 ------------------------------
            Console.WriteLine("");
            Console.WriteLine("== C-12 步幅耦合：走得快 ⇒ 音高/响度微升 ==");
            foreach (var s in surfaces)
            {
                var baseSpec = FootstepSpec.For(s);
                var prevThump = -1f;
                var prevCentroid = -1f;
                float slowCentroid = -1f, fastCentroid = -1f;
                foreach (var k in new[] { 0f, .25f, .5f, .75f, 1f })
                {
                    var spec = FootstepSynth.WithStride(baseSpec, k);
                    var buf = FootstepSynth.Render(spec);
                    var m = Measure(buf, spec.Rate);
                    Console.WriteLine(
                        "STRIDE surface=" + s +
                        " k=" + Num(k, 2) +
                        " thump_hz=" + Num(spec.ThumpHz, 1) +
                        " centroid_hz=" + Num(m.CentroidHz, 0) +
                        " zcr_hz=" + Num(m.ZcrHz, 0) +
                        " peak=" + Num(m.Peak, 3) +
                        " decay20_ms=" + Num(m.Decay20Ms, 1) +
                        " volume_x=" + Num(FootstepSynth.StrideVolumeAt(k), 3));
                    if (k <= 0f) slowCentroid = m.CentroidHz;
                    fastCentroid = m.CentroidHz;
                    rows++;
                    Check(spec.ThumpHz > prevThump, s + " k=" + Num(k, 2) + "：基频随步幅单调上升",
                          Num(spec.ThumpHz, 1) + "Hz（上一个 " + Num(prevThump, 1) + "）");
                    // 为什么用质心而不是过零率：过零率被噪声成分污染，实测**不单调**
                    // （Sand k=0.25 是 2706Hz、k=0.50 反而掉到 2506Hz）⇒ 它不是合格的判据。
                    Check(m.CentroidHz > prevCentroid, s + " k=" + Num(k, 2) + "：亮度（频谱质心）随步幅单调上升",
                          Num(m.CentroidHz, 0) + "Hz（上一个 " + Num(prevCentroid, 0) + "）");
                    prevThump = spec.ThumpHz;
                    prevCentroid = m.CentroidHz;
                }

                // k=0 必须与原档**逐样本一致**：不能悄悄把默认听感改掉
                var slow = FootstepSynth.Render(FootstepSynth.WithStride(baseSpec, 0f));
                var orig = FootstepSynth.Render(baseSpec);
                Check(DiffEnergyPct(orig, slow) == 0f, s + "：k=0 与原档完全一致（默认听感没被动过）",
                      "差异能量 " + Num(DiffEnergyPct(orig, slow), 3) + "%");
                // k=1 必须真的听得出不是同一条
                var top = FootstepSynth.Render(FootstepSynth.WithStride(baseSpec, 1f));
                Check(DiffEnergyPct(orig, top) > 10f, s + "：最快档与原档听得出来不同",
                      "差异能量 " + Num(DiffEnergyPct(orig, top), 1) + "%");
                Check(fastCentroid > slowCentroid, s + "：最快档比最慢档更亮（频谱质心）",
                      Num(slowCentroid, 0) + "Hz → " + Num(fastCentroid, 0) + "Hz");
            }

            Check(Math.Abs(FootstepSynth.StrideVolumeAt(0f) - FootstepSynth.StrideVolumeFloor) < 1e-6f,
                  "C-12：最慢档响度等于设定下限", FootstepSynth.StrideVolumeAt(0f).ToString(CultureInfo.InvariantCulture));
            Check(Math.Abs(FootstepSynth.StrideVolumeAt(1f) - 1f) < 1e-6f,
                  "C-12：最快档响度恰好等于 footstepGain（不额外加成）",
                  FootstepSynth.StrideVolumeAt(1f).ToString(CultureInfo.InvariantCulture));
            var monoVol = true; var prev = -1f;
            foreach (var k in new[] { 0f, .25f, .5f, .75f, 1f })
            {
                var v = FootstepSynth.StrideVolumeAt(k);
                if (v <= prev) monoVol = false;
                prev = v;
            }
            Check(monoVol, "C-12：响度随步幅单调上升", "0.72 → 1.00");

            // ---- C-13 / C-23：环境底噪（风/鸟/水/虫，程序合成、可循环）--------------------
            Console.WriteLine("");
            Console.WriteLine("== C-13/C-23 环境底噪：风/鸟/水/虫（零素材、可循环）==");
            var kinds = new[] { AmbienceKind.Wind, AmbienceKind.Bird, AmbienceKind.Water, AmbienceKind.Insect };
            var centroids = new System.Collections.Generic.List<float>();
            foreach (var k in kinds)
            {
                var spec = AmbienceSpec.For(k);
                var buf = AmbienceSynth.Render(spec);
                var peak = FootstepSynth.PeakOf(buf);
                var rms = FootstepSynth.Rms(buf, 0, buf.Length);
                var centroid = Centroid(buf, spec.Rate);
                centroids.Add(centroid);

                // 循环接缝：把"首样本与末样本的落差"和"普通相邻样本的落差"比 ——
                // 比值小 ⇒ 首尾接得上；事件型（鸟/虫）接缝处是静音，比值应该接近 0。
                var seam = Math.Abs(buf[0] - buf[buf.Length - 1]);
                var step = 0.0;
                for (var i = 1; i < buf.Length; i++) step += Math.Abs(buf[i] - buf[i - 1]);
                step /= Math.Max(1, buf.Length - 1);
                var seamRatio = step > 1e-9 ? (float)(seam / step) : 0f;

                Console.WriteLine(
                    "AMBIENCE kind=" + k +
                    " n=" + buf.Length +
                    " dur_s=" + Num(buf.Length / (float)spec.Rate, 2) +
                    " peak=" + Num(peak, 3) +
                    " rms=" + Num(rms, 4) +
                    " centroid_hz=" + Num(centroid, 0) +
                    " seam=" + Num(seam, 5) +
                    " mean_step=" + Num((float)step, 5) +
                    " seam_ratio=" + Num(seamRatio, 2));
                rows++;

                Check(Math.Abs(peak - spec.Level) < .01f, k + "：峰值恰好等于目标",
                      Num(peak, 3) + " / 目标 " + Num(spec.Level, 2));
                var ok = true; var over = 0;
                foreach (var v in buf)
                {
                    if (float.IsNaN(v) || float.IsInfinity(v)) ok = false;
                    if (Math.Abs(v) > 1.0001f) over++;
                }
                Check(ok && over == 0, k + "：无 NaN/Inf、全部 |x| <= 1", "越界 " + over);
                var twice = AmbienceSynth.Render(spec);
                var sameA = true;
                for (var i = 0; i < buf.Length; i++) if (buf[i] != twice[i]) { sameA = false; break; }
                Check(sameA, k + "：确定性（同 spec ⇒ 逐样本一致）", "重算一致=" + sameA);
                Check(rms > 1e-4f, k + "：不是一条静音（真有内容）", "RMS " + Num(rms, 4));
                Check(seamRatio < 10f, k + "：循环接缝平滑（落差不超过普通相邻跳变的 10 倍）",
                      "比值 " + Num(seamRatio, 2));
            }

            var distinct = true;
            for (var i = 0; i < centroids.Count; i++)
                for (var j = i + 1; j < centroids.Count; j++)
                {
                    var bigger = Math.Max(centroids[i], centroids[j]);
                    if (bigger <= 1e-6f || Math.Abs(centroids[i] - centroids[j]) / bigger < .1f) distinct = false;
                }
            Check(distinct, "四种底噪互不相同（频谱质心两两相差 >10%）",
                  string.Join(" / ", centroids.ConvertAll(c => Num(c, 0) + "Hz")));

            // ---- C-17 / C-27 / TC-31~TC-33：交互音四类（点击 / 翻页 / 获得 / 失败）------
            Console.WriteLine("");
            Console.WriteLine("== C-17/C-27 交互音四类（全部 <=120ms，零素材）==");
            var tones = new[] { UiToneKind.Click, UiToneKind.Page, UiToneKind.Acquire, UiToneKind.Fail };
            var toneCentroids = new System.Collections.Generic.List<float>();
            foreach (var kind in tones)
            {
                var spec = UiToneSpec.For(kind);
                var buf = UiToneSynth.Render(spec);
                var durMs = buf.Length * 1000f / spec.Rate;
                var peak = FootstepSynth.PeakOf(buf);
                var centroid = Centroid(buf, spec.Rate);
                toneCentroids.Add(centroid);
                Console.WriteLine(
                    "UITONE kind=" + kind +
                    " n=" + buf.Length +
                    " dur_ms=" + Num(durMs, 1) +
                    " peak=" + Num(peak, 3) +
                    " centroid_hz=" + Num(centroid, 0) +
                    " freq_hz=" + Num(spec.FreqA, 0) + "->" + Num(spec.FreqB, 0));
                rows++;

                Check(durMs <= UiToneSynth.MaxSeconds * 1000f + 1f, kind + "：时长 <= 120ms（任务书硬要求）",
                      Num(durMs, 1) + "ms / 上限 120ms");
                Check(Math.Abs(buf[0]) < 1e-6f, kind + "：起振为 0（不爆头）", "首样本 " + Num(buf[0], 6));
                Check(Math.Abs(buf[buf.Length - 1]) < .35f * peak, kind + "：结尾已衰减（不留尾巴）",
                      "末样本 " + Num(buf[buf.Length - 1], 4));
                var okT = true; var overT = 0;
                foreach (var v in buf)
                {
                    if (float.IsNaN(v) || float.IsInfinity(v)) okT = false;
                    if (Math.Abs(v) > 1.0001f) overT++;
                }
                Check(okT && overT == 0, kind + "：无 NaN/Inf、全部 |x| <= 1", "越界 " + overT);
                var againT = UiToneSynth.Render(spec);
                var sameT = true;
                for (var i = 0; i < buf.Length; i++) if (buf[i] != againT[i]) { sameT = false; break; }
                Check(sameT, kind + "：确定性（同 spec 逐样本一致）", "重算一致=" + sameT);
            }

            var toneDistinct = true;
            for (var i = 0; i < toneCentroids.Count; i++)
                for (var j = i + 1; j < toneCentroids.Count; j++)
                {
                    var bigger = Math.Max(toneCentroids[i], toneCentroids[j]);
                    if (bigger <= 1e-6f || Math.Abs(toneCentroids[i] - toneCentroids[j]) / bigger < .1f) toneDistinct = false;
                }
            Check(toneDistinct, "四种交互音互不相同（频谱质心两两相差 >10%）",
                  string.Join(" / ", toneCentroids.ConvertAll(c => Num(c, 0) + "Hz")));

            // ---- C-19 / C-29：合成开销（给"要不要池化 / 会不会卡主线程"一个可复算的依据）----
            Console.WriteLine("");
            Console.WriteLine("== C-19/C-29 合成开销：单次渲染耗时 / 采样数 / 内存 ==");
            Cost("footstep-Sand", () => FootstepSynth.Render(FootstepSpec.For(FootstepSurface.Sand)), 20f);
            Cost("ambience-Wind", () => AmbienceSynth.Render(AmbienceSpec.For(AmbienceKind.Wind)), 400f);
            Cost("uitone-Click", () => UiToneSynth.Render(UiToneSpec.For(UiToneKind.Click)), 20f);

            Console.WriteLine("");
            Console.WriteLine("RESULT: " + (failures == 0 ? "PASS" : "FAIL") + " rows=" + rows + " fail=" + failures);
            return failures == 0 ? 0 : 1;
        }
    }
}
