// V 线 W-C：**脚步音效的程序化合成**（纯 C#，不引 UnityEngine，可离线断言）。
//
// 为什么要它：`20_项目状态板` / U-1 的实测是"全仓音频文件 **0** 个 ⇒ 全程静音"，
// 而"走路滑行感"里最刺眼的一条就是**没有脚步声**（V-1 的原话）。
// 本仓已经有正确的做法可抄：`FrontEndSettings.SetupAudio()` 用 `AudioClip.Create` +
// 手写采样**在运行期合成**环境琴音与点击音，**盘上一个音频文件都没有**（零素材、零许可风险）。
// 脚步照这条路办：合成 → `AudioClip.Create` → `PlayOneShot`。
//
// 这一层只做 DSP；它与 Unity 的唯一接口是"给我一个 float[]"。
// 于是采样率、包络、峰值、确定性、四种地面的差异**全都能在没有 Unity 的机器上被断言**。
using System;

namespace TwelveJade.Presentation
{
    /// <summary>地面材质：决定"闷响"与"擦地"的配比。默认取自走路最常见的几种地面。</summary>
    public enum FootstepSurface { Sand, Stone, Wood, Grass }

    [Serializable]
    public struct FootstepSpec
    {
        public int Rate;            // 采样率
        public float Duration;      // 时长（秒）
        public float ThumpHz;       // 低频"闷响"基频：脚踩实的那一下
        public float ThumpGain;
        public float ThumpDecay;    // 闷响衰减（越大越快）
        public float ScuffGain;     // 噪声"擦地/扬尘"
        public float ScuffTone;     // 一阶低通系数 0..1：越大越亮
        public float AttackRate;    // 起振速度
        public float Decay;         // 整体包络衰减
        public float Peak;          // 归一化目标峰值（0..1）
        public int Seed;            // 噪声种子：同种子 ⇒ 逐样本可复现

        public static FootstepSpec For(FootstepSurface surface)
        {
            switch (surface)
            {
                case FootstepSurface.Stone:   // 硬、亮、短
                    return new FootstepSpec { Rate = 22050, Duration = .12f, ThumpHz = 152f, ThumpGain = .62f,
                        ThumpDecay = 42f, ScuffGain = .70f, ScuffTone = .34f, AttackRate = 900f, Decay = 34f,
                        Peak = .85f, Seed = 20261006 };
                case FootstepSurface.Wood:    // 有共鸣、尾音略长
                    return new FootstepSpec { Rate = 22050, Duration = .15f, ThumpHz = 186f, ThumpGain = .74f,
                        ThumpDecay = 30f, ScuffGain = .42f, ScuffTone = .22f, AttackRate = 800f, Decay = 28f,
                        Peak = .85f, Seed = 20261006 };
                case FootstepSurface.Grass:   // 碎、沙沙、闷响弱
                    return new FootstepSpec { Rate = 22050, Duration = .20f, ThumpHz = 72f, ThumpGain = .48f,
                        ThumpDecay = 20f, ScuffGain = .92f, ScuffTone = .30f, AttackRate = 600f, Decay = 26f,
                        Peak = .80f, Seed = 20261006 };
                default:                      // Sand：土路/沙地，闷而短
                    return new FootstepSpec { Rate = 22050, Duration = .18f, ThumpHz = 92f, ThumpGain = .80f,
                        ThumpDecay = 26f, ScuffGain = .52f, ScuffTone = .12f, AttackRate = 700f, Decay = 22f,
                        Peak = .85f, Seed = 20261006 };
            }
        }
    }

    public static class FootstepSynth
    {
        // ---- C-12：步幅（走得有多快）→ 响度 / 音高微升 ----------------------------------
        // 为什么分两半做：**音高**必须改 paperclip samples（合成时就定死，没法在播放时调），
        // 所以预合成"慢/快"两档；**响度**可以用 `PlayOneShot(clip, volumeScale)` 在播放时连续给，
        // 不必再合成 ⇒ 两档 pitch + 连续 loudness，既不抖动也不多占内存。
        // 三个数字都不是拍的：它们由 `Tools/AudioChecks` 实测打印（见 `Tools/logs/c-12-report.md`）。
        public const float StridePitchSpan = .18f;   // 最快时音高最多 +18%
        public const float StrideDecaySpan = .10f;   // 越快尾音收得越紧（同 +10%）
        public const float StrideVolumeFloor = .72f; // 最慢时的响度下限（相对 footstepGain）

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>播放时用：`<paramref name="t01"/>`=0 走得最慢 ⇒ 0.72 倍响；=1 最快 ⇒ 满响。</summary>
        public static float StrideVolumeAt(float t01) =>
            StrideVolumeFloor + (1f - StrideVolumeFloor) * Clamp01(t01);

        /// <summary>合成时用：只改**音高与包络**（响度留给播放时的 `StrideVolumeAt`，避免两头各乘一次）。
        /// 同参数 ⇒ 同结果，所以仍能被离线逐样本复算。</summary>
        public static FootstepSpec WithStride(in FootstepSpec spec, float t01)
        {
            var k = Clamp01(t01);
            var m = spec;
            m.ThumpHz = spec.ThumpHz * (1f + StridePitchSpan * k);
            m.ScuffTone = Math.Min(1f, spec.ScuffTone * (1f + StridePitchSpan * .5f * k));
            m.ThumpDecay = spec.ThumpDecay * (1f + StrideDecaySpan * k);
            return m;
        }

        public static int SampleCount(in FootstepSpec spec) =>
            Math.Max(1, (int)(spec.Rate * spec.Duration));

        /// <summary>渲染一次脚步。**确定性**：同 spec（含 Seed）⇒ 逐样本一致。
        /// 输出已归一化到 `spec.Peak`，并做一次软限幅，保证 |x| ≤ 1（不会削顶爆音）。</summary>
        public static float[] Render(in FootstepSpec spec)
        {
            var rate = Math.Max(1, spec.Rate);
            var n = SampleCount(spec);
            var buffer = new float[n];
            var state = (uint)(spec.Seed | 1);
            var lowpass = 0f;
            var tone = Math.Max(.01f, spec.ScuffTone);
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)rate;
                var envelope = Math.Min(t * spec.AttackRate, 1f) * (float)Math.Exp(-t * spec.Decay);
                // 线性同余噪声：不用 UnityEngine.Random，才能在离线门禁里逐样本复现
                state = state * 1664525u + 1013904223u;
                var white = ((state >> 9) & 0x7FFFu) / 16384f - 1f;   // [-1, 1)
                lowpass += tone * (white - lowpass);
                var thump = (float)Math.Sin(2 * Math.PI * spec.ThumpHz * t) * (float)Math.Exp(-t * spec.ThumpDecay);
                var raw = (thump * spec.ThumpGain + lowpass * spec.ScuffGain) * envelope;
                // 先软限幅、**再**归一化。顺序反了的话 tanh 会把已经归一化好的峰值再压一次
                // （实测：目标 0.85 会变成 0.691），听感偏轻而且断言量不到"目标音量"。
                buffer[i] = (float)Math.Tanh(raw);
            }
            Normalize(buffer, spec.Peak);
            return buffer;
        }

        /// <summary>归一化到目标峰值，并做一次软限幅保证 |x| ≤ 1（不会削顶爆音）。
        /// 调用方应**先**软限幅再一次调用它，这样最终峰值恰好等于目标。</summary>
        public static void Normalize(float[] buffer, float peak)
        {
            if (buffer == null || buffer.Length == 0) return;
            var max = 0f;
            for (var i = 0; i < buffer.Length; i++)
            {
                var a = Math.Abs(buffer[i]);
                if (a > max) max = a;
            }
            if (max <= 1e-9f) return;
            var gain = Math.Abs(peak) / max;
            for (var i = 0; i < buffer.Length; i++)
            {
                var v = buffer[i] * gain;
                // 保险丝：即便上面算错增益也不出现越界样本
                buffer[i] = v > 1f ? 1f : (v < -1f ? -1f : v);
            }
        }

        /// <summary>均方根（夹具量包络形状用）。</summary>
        public static float Rms(float[] buffer, int from, int to)
        {
            if (buffer == null || buffer.Length == 0) return 0f;
            from = Math.Max(0, from);
            to = Math.Min(buffer.Length, to);
            if (to <= from) return 0f;
            var sum = 0.0;
            for (var i = from; i < to; i++) sum += (double)buffer[i] * buffer[i];
            return (float)Math.Sqrt(sum / (to - from));
        }

        public static float PeakOf(float[] buffer)
        {
            if (buffer == null) return 0f;
            var max = 0f;
            for (var i = 0; i < buffer.Length; i++)
            {
                var a = Math.Abs(buffer[i]);
                if (a > max) max = a;
            }
            return max;
        }
    }

    /// <summary>C-17 / C-27 / TC-31~TC-33：交互音。**四类的口径**：点击 / 翻页 / 获得 / 失败，
    /// 全部**程序合成**（零素材），**每条 ≤120ms**（任务书 C-17 的硬要求：动效时长 ≤120ms）。
    /// 与 `AmbienceSynth` 同理由放在本文件：C 线主责、且要被 `Tools/AudioChecks` 离线断言。</summary>
    public enum UiToneKind { Click, Page, Acquire, Fail }

    [Serializable]
    public struct UiToneSpec
    {
        public UiToneKind Kind;
        public int Rate;
        public float Seconds;    // **必须 ≤ .12f**（120ms）
        public float FreqA;      // 主频
        public float FreqB;      // 第二频（获得=上行、失败=下行；单击给同一个值）
        public float Gain;
        public float Decay;      // 指数衰减系数

        public static UiToneSpec For(UiToneKind kind)
        {
            switch (kind)
            {
                case UiToneKind.Page:    return new UiToneSpec { Kind = kind, Rate = 22050, Seconds = .10f, FreqA = 520f, FreqB = 520f, Gain = .18f, Decay = 30f };
                case UiToneKind.Acquire: return new UiToneSpec { Kind = kind, Rate = 22050, Seconds = .12f, FreqA = 880f, FreqB = 1320f, Gain = .22f, Decay = 22f };
                case UiToneKind.Fail:    return new UiToneSpec { Kind = kind, Rate = 22050, Seconds = .12f, FreqA = 190f, FreqB = 140f, Gain = .20f, Decay = 26f };
                default:                 return new UiToneSpec { Kind = kind, Rate = 22050, Seconds = .12f, FreqA = 660f, FreqB = 660f, Gain = .20f, Decay = 26f }; // Click
            }
        }
    }

    public static class UiToneSynth
    {
        /// <summary>任务书硬要求：**每条交互音 ≤120ms**。这条常量就是那个上限，拿它做断言。</summary>
        public const float MaxSeconds = .12f;

        public static int SampleCount(in UiToneSpec spec) =>
            Math.Max(1, (int)(Math.Max(1, spec.Rate) * Math.Max(.01f, spec.Seconds)));

        /// <summary>渲染一条交互音。同 spec ⇒ 逐样本一致（可离线复算）。</summary>
        public static float[] Render(in UiToneSpec spec)
        {
            var rate = Math.Max(1, spec.Rate);
            var n = SampleCount(spec);
            var buffer = new float[n];
            var half = Math.Max(1, n / 2);
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)rate;
                // 前半用 FreqA、后半滑到 FreqB ⇒ 获得音"上行"、失败音"下行"，同一种实现两类听感
                var f = i < half ? spec.FreqA : spec.FreqB;
                var envelope = Math.Min(1f, t * 400f) * (float)Math.Exp(-t * spec.Decay);
                buffer[i] = (float)Math.Tanh(spec.Gain * envelope *
                    (float)Math.Sin(2 * Math.PI * f * t));
            }
            FootstepSynth.Normalize(buffer, Math.Min(1f, spec.Gain));
            return buffer;
        }
    }

    /// <summary>C-13 / C-23 环境底噪。**为什么放在这个文件里**：本文件是 C 线**主责**的
    /// 纯 C# 音频 DSP（不引 UnityEngine），`Tools/AudioChecks` 已经把它链进去做离线断言；
    /// 另开一个文件的收益（文件名更贴切）小于它带来的白名单扩张成本，故就近放，
    /// 若主架构师要拆，是一次纯移动、无行为变化。</summary>
    public enum AmbienceKind { Wind, Bird, Water, Insect }

    [Serializable]
    public struct AmbienceSpec
    {
        public AmbienceKind Kind;
        public int Rate;
        public float Seconds;   // 一条循环的时长（各档统一 12s，循环接缝才好对齐）
        public float Level;     // 归一化目标峰值
        public int Seed;

        public static AmbienceSpec For(AmbienceKind kind)
        {
            switch (kind)
            {
                case AmbienceKind.Bird:   return new AmbienceSpec { Kind = kind, Rate = 22050, Seconds = 12f, Level = .55f, Seed = 20261007 };
                case AmbienceKind.Water:  return new AmbienceSpec { Kind = kind, Rate = 22050, Seconds = 12f, Level = .45f, Seed = 20261008 };
                case AmbienceKind.Insect: return new AmbienceSpec { Kind = kind, Rate = 22050, Seconds = 12f, Level = .34f, Seed = 20261009 };
                default:                  return new AmbienceSpec { Kind = kind, Rate = 22050, Seconds = 12f, Level = .30f, Seed = 20261006 }; // Wind
            }
        }
    }

    public static class AmbienceSynth
    {
        public static int SampleCount(in AmbienceSpec spec) =>
            Math.Max(1, (int)(Math.Max(1, spec.Rate) * Math.Max(.1f, spec.Seconds)));

        /// <summary>渲染一条**可循环**的底噪。确定性：同 spec ⇒ 逐样本一致。
        /// 循环接缝的处理：连续型（风/水）用"整数个周期的 LFO"保证首尾同值；
        /// 事件型（鸟/虫）把每次发声**完整放在缓冲内部**，不许跨过首尾 ⇒ 接缝处是静音，天然平滑。</summary>
        public static float[] Render(in AmbienceSpec spec)
        {
            var rate = Math.Max(1, spec.Rate);
            var n = SampleCount(spec);
            var buffer = new float[n];
            var state = (uint)(spec.Seed | 1);
            float Next()
            {
                state = state * 1664525u + 1013904223u;
                return ((state >> 9) & 0x7FFFu) / 16384f - 1f;
            }

            var kind = spec.Kind;
            var lp = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)rate;
                var v = 0f;
                switch (kind)
                {
                    case AmbienceKind.Wind:
                        {
                            lp += .055f * (Next() - lp);
                            // 2 个完整周期 ⇒ 首尾的 LFO 取值相同，接缝不会"啪"一下
                            var lfo = 1f + .45f * (float)Math.Sin(2 * Math.PI * 2f * t / spec.Seconds);
                            v = lp * lfo;
                            break;
                        }
                    case AmbienceKind.Water:
                        {
                            lp += .30f * (Next() - lp);
                            v = lp * .8f;
                            for (var b = 0; b < 12; b++)                       // 12 颗"咕嘟"，位置写死在缓冲内
                            {
                                var at = (.04f + b * .078f) * n;
                                var d = i - at;
                                if (d < 0 || d > rate * .07f) continue;
                                var tt = d / rate;
                                var f = 900f - 320f * (float)(tt / .07f);       // 下滑音 ⇒ 像气泡上浮
                                v += .5f * (float)Math.Sin(2 * Math.PI * f * tt) * (float)Math.Exp(-tt * 46f);
                            }
                            break;
                        }
                    case AmbienceKind.Bird:
                        {
                            for (var c = 0; c < 6; c++)                         // 6 声鸟叫，全部落在缓冲内
                            {
                                var at = (.05f + c * .155f) * n;
                                var d = i - at;
                                if (d < 0 || d > rate * .18f) continue;
                                var tt = d / rate;
                                var f0 = 2200f + 260f * c;                      // 每声略不同，免得机器味
                                var f = f0 * (1f + .35f * (float)(tt / .18f));  // 上滑
                                var env = Math.Min(tt * 260f, 1f) * (float)Math.Exp(-tt * 15f);
                                v += .85f * env * (float)Math.Sin(2 * Math.PI * f * tt);
                            }
                            break;
                        }
                    default:                                                    // Insect
                        {
                            for (var c = 0; c < 4; c++)                         // 4 段虫鸣（夜里）
                            {
                                var at = (.06f + c * .23f) * n;
                                var d = i - at;
                                if (d < 0 || d > rate * .9f) continue;
                                var tt = d / rate;
                                var env = Math.Min(tt * 60f, 1f) * Math.Min((float)((.9f - tt) * 40f), 1f);
                                var trill = .5f + .5f * (float)Math.Sin(2 * Math.PI * 42f * tt);  // 颤音
                                v += .7f * env * trill * (float)Math.Sin(2 * Math.PI * 4300f * tt);
                            }
                            break;
                        }
                }
                buffer[i] = (float)Math.Tanh(v);
            }
            FootstepSynth.Normalize(buffer, spec.Level);
            return buffer;
        }

    }
}
