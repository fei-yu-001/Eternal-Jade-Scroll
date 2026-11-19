// V 线 W-C：脚步音效的 **Unity 侧**（与 `FrontEndSettings.cs` 是同一个 partial 类）。
//
// 与既有音频同一套做法：**运行期合成 + `AudioClip.Create`，盘上零音频文件**。
// 触发口径与 P2 完全一致：**脚步声由真实位移驱动** —— 站着不动时相位不前进，
// `PuppetGait.FootfallsCrossed` 恒返回 0 ⇒ 一声都不响（老问题"原地踏步"的音频版）。
using UnityEngine;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        [Header("脚步（W-C：程序化合成，零素材文件；默认关）")]
        // 默认关的理由与 `travelerGroundShadow` 同一范式：**NPC 没有脚步**，
        // 只给主角加会让两者用不同的"着地语言"，所以它是一档开关，不是新默认值。
        [SerializeField] bool travelerFootsteps;
        [SerializeField] FootstepSurface travelerFootstepSurface = FootstepSurface.Sand;
        [SerializeField, Range(0f, 1f)] float footstepGain = .5f;
        [SerializeField] int footstepMaxPerFrame = 2;
        // C-12：走得快 ⇒ 响度/音高微升。**音高**只能在合成时定死 ⇒ 预合成"慢/快"两档；
        // **响度**在播放时按 `FootstepSynth.StrideVolumeAt(k)` 连续给（0.72~1.00 倍）。
        // 跨过 `footstepFastTierAt` 就换成快档 ⇒ 理论上会跳一次档；但因为同一时刻只有一只脚响，
        // 实际听感是"越走越亮"而不是"啪一下换音色"（**待听过 wav 的人复核**，我听不了）。
        [SerializeField, Range(0f, 1f)] float footstepFastTierAt = .5f;
        [SerializeField] float footstepSpeedLerp = 6f;   // 速度 01 的平滑（每秒收敛比例）

        AudioClip[] footstepClips;        // [0]=左 [1]=右：**慢档**（k < footstepFastTierAt 用这套）
        AudioClip[] footstepClipsFast;    // [0]=左 [1]=右：**快档**（`FootstepSynth.WithStride(spec, 1)`）
        float footstepPhase = -1f;
        float footstepLastStride = -1f;
        float footstepSpeed01;
        int footstepPlayed;

        /// <summary>合成某一档（t01=0 慢 / =1 快）的左右两条。合成本身零素材、零许可风险。</summary>
        AudioClip[] BuildFootstepTier(float t01)
        {
            var spec = FootstepSpec.For(travelerFootstepSurface);
            var tier = new AudioClip[2];
            for (var side = 0; side < 2; side++)
            {
                var one = FootstepSynth.WithStride(spec, t01);
                one.Seed = spec.Seed + side * 7919;          // 素数错开：左右不完全相同
                if (side == 1) one.ThumpHz *= 1.06f;         // 再给一点点音高差，听感不机械
                var samples = FootstepSynth.Render(one);
                var clip = AudioClip.Create(
                    "Footstep " + travelerFootstepSurface + (side == 0 ? " L" : " R") +
                    (t01 > 0f ? " fast" : ""),
                    samples.Length, 1, one.Rate, false);
                clip.SetData(samples, 0);
                tier[side] = clip;
            }
            return tier;
        }

        /// <summary>合成慢/快两档。合成本身零素材、零许可风险（见 `FootstepSynth`）。</summary>
        void SetupFootsteps()
        {
            footstepClips = BuildFootstepTier(0f);
            footstepClipsFast = BuildFootstepTier(1f);
            ResetFootsteps();
        }

        /// <summary>换人/换档后重来：把相位基准清掉，免得下一帧按"从 0 跳到现在"补出一串脚步。</summary>
        void ResetFootsteps()
        {
            footstepPhase = -1f;
            footstepLastStride = -1f;
            footstepSpeed01 = 0f;
            footstepPlayed = 0;
        }

        /// <summary>每帧喂一次**真实位移驱动**的步态相位。站立不动 ⇒ 零脚步（P2 的音频版）。</summary>
        void TickFootsteps()
        {
            if (travelerPuppet == null) return;
            if (footstepClips == null) SetupFootsteps();   // 懒建：不依赖启动路径有没有走到 SetupAudio

            // C-12：先量"这一步有多快"。数据源与步态相位同源 —— **累计位移**（`StrideDistanceForCheck`），
            // 不是 Time.time ⇒ 与 P2「位移驱动，不许改成时间驱动」同一口径。
            var stride = travelerPuppet.StrideDistanceForCheck;
            if (footstepLastStride < 0f) footstepLastStride = stride;
            var dt = Mathf.Max(1e-4f, Time.deltaTime);
            var speed = Mathf.Max(0f, (stride - footstepLastStride) / dt);
            footstepLastStride = stride;
            var reference = Mathf.Max(1f, travelerPuppet.GaitSpeedForCycle);
            var target = Mathf.Clamp01(speed / reference);
            // 一阶平滑：单帧的位移抖动不该让音量抽动
            footstepSpeed01 += (target - footstepSpeed01) * Mathf.Clamp01(dt * footstepSpeedLerp);

            var phase = travelerPuppet.GaitPhaseForCheck;
            if (footstepPhase < 0f) { footstepPhase = phase; return; }
            bool leftFirst;
            var count = PuppetGait.FootfallsCrossed(footstepPhase, phase, Mathf.Max(0, footstepMaxPerFrame), out leftFirst);
            footstepPhase = phase;
            for (var i = 0; i < count; i++) PlayFootstep((i % 2 == 0) == leftFirst);
        }

        void PlayFootstep(bool left)
        {
            footstepPlayed++;                                  // 计数与开关无关：夹具要能证明"站着确实没响"
            if (!travelerFootsteps || effects == null || footstepClips == null) return;
            // C-12：快档换切片（音高已在里面），响度按速度连续给。
            var k = Mathf.Clamp01(footstepSpeed01);
            var tier = (footstepClipsFast != null && k >= footstepFastTierAt) ? footstepClipsFast : footstepClips;
            var clip = tier[left ? 0 : 1];
            if (clip != null) effects.PlayOneShot(clip, footstepGain * FootstepSynth.StrideVolumeAt(k));
        }

        /// <summary>夹具口径：合成了几条脚步（0 = 没接上）。</summary>
        public int FootstepClipCountForCheck => footstepClips == null ? 0 : footstepClips.Length;
        /// <summary>夹具口径：这一次运行里播过几次脚步（含被开关挡掉的尝试）。</summary>
        public int FootstepsTriggeredForCheck => footstepPlayed;
        public bool FootstepsEnabledForCheck => travelerFootsteps;
        /// <summary>夹具口径：当前"走得多快"（0..1，已平滑；C-12 的耦合输入）。</summary>
        public float FootstepSpeed01ForCheck => footstepSpeed01;
        /// <summary>夹具口径：快档切片是否已合成（0 = 没接上，C-12 未生效）。</summary>
        public int FootstepFastClipCountForCheck => footstepClipsFast == null ? 0 : footstepClipsFast.Length;

        /// <summary>夹具用：打开/关闭脚步（同 `SetTravelerGroundShadow` 的范式，不改默认值）。
        /// 打开时**顺带确保 clip 已合成** —— 否则夹具可能在第一帧 Update 之前就来读数，
        /// 读到 0 条而误判"没接上"。</summary>
        public void SetTravelerFootsteps(bool on)
        {
            travelerFootsteps = on;
            if (on && footstepClips == null) SetupFootsteps();
        }
        /// <summary>夹具用：换地面材质并**立刻重新合成**。</summary>
        public void SetFootstepSurface(FootstepSurface surface)
        {
            if (travelerFootstepSurface == surface && footstepClips != null) return;
            travelerFootstepSurface = surface;
            SetupFootsteps();
        }
    }
}
