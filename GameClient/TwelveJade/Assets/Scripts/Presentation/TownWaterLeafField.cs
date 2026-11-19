using System;

namespace TwelveJade.Presentation
{
    /// <summary>
    /// 落叶漂流的纯几何层：没有 UnityEngine 依赖，运行期与离线检查共用同一份判据。
    /// 河流形状、水面半宽、桥/码头排除区全部来自既有 <see cref="TownWaterGeometry"/>，不另算一份。
    /// 叶心沿 CenterY 曲线走，横向只做小幅摆动；整片叶子按外接半径检查，绝不上岸、不穿桥。
    /// </summary>
    public sealed class TownWaterLeafField
    {
        public const int MaxLeaves = 24;
        public const float MaxSubstep = .05f;
        public const float BobAmplitude = 1.2f;
        const float FadeRate = 3.5f;
        // 开始淡出的提前量：走到桥边之前就该透明，不能在桥面上半透明地穿过去。
        const float FadeLead = 6f;

        public readonly struct LeafState
        {
            public readonly float X, Across, Angle, Size, Visibility;
            public LeafState(float x, float across, float angle, float size, float visibility)
            { X = x; Across = across; Angle = angle; Size = size; Visibility = visibility; }
            /// <summary>实际叶尖 + 全幅世界单位浮沉的保守外接半径。</summary>
            public float Radius => Size * .5f + BobAmplitude;
        }

        struct Leaf
        {
            public float X, BaseAcross, SwayAmp, SwayPhase, SwayRate, Spin, SpinRate, Size, Speed, Visibility;
            public int Palette;
            public bool Leaving, Alive;
        }

        readonly TownWaterGeometry.Definition definition;
        readonly TownWaterGeometry.Exclusion[] cutouts;
        readonly Leaf[] leaves;
        readonly Random random;
        readonly float half, direction, maxSlope, chordError;

        public int Count => leaves.Length;
        public float HalfWidth => half;
        public float Direction => direction;
        public float Elapsed { get; private set; }

        public TownWaterLeafField(TownWaterGeometry.Definition definition, int seed, int count = MaxLeaves)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.HalfWidth <= definition.BankInset)
                throw new ArgumentException("water leaves: river has no usable width", nameof(definition));
            this.definition = definition;
            half = definition.HalfWidth - definition.BankInset;
            direction = definition.FlowDirection < 0 ? -1f : 1f;
            double slope = 0, curvature = 0;
            foreach (var wave in definition.Waves) {
                slope += Math.Abs((double)wave.Amplitude / wave.Period);
                curvature += Math.Abs((double)wave.Amplitude) / wave.Period / wave.Period;
            }
            maxSlope = (float)slope;
            // Build 的弦长不超过 SampleStep；整叶也必须留在实际分段水 mesh 内。
            chordError = (float)(curvature * definition.SampleStep * definition.SampleStep / 8);
            // 与 TownWaterGeometry.Build 的 Cutouts 逐字相同：同一条 DeckClearance 扩张规则。
            cutouts = new TownWaterGeometry.Exclusion[definition.Exclusions.Length];
            for (int i = 0; i < cutouts.Length; i++)
                cutouts[i] = definition.Exclusions[i].Expanded(definition.DeckClearance);
            leaves = new Leaf[Math.Max(0, Math.Min(count, MaxLeaves))];
            random = new Random(seed);
            for (int i = 0; i < leaves.Length; i++)
            {
                var leaf = default(Leaf);
                Spawn(ref leaf);
                leaves[i] = leaf;
            }
        }

        /// <summary>每个可见帧最多推进 0.05s，丢弃停顿积压；一次调用绝不跨过淡出/重生周期。</summary>
        public void Advance(float deltaSeconds, bool frozen)
        {
            if (frozen || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds <= 0f) return;
            Step(Math.Min(deltaSeconds, MaxSubstep));
        }

        /// <summary>整片叶子此刻是否完整落在水带内且不碰任何桥/码头/渡口排除区。</summary>
        public bool IsFootprintLegal(float x, float across, float radius)
        {
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(across) || float.IsInfinity(across) ||
                float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f) return false;
            if (x - radius < 0f || x + radius > definition.WorldWidth) return false;
            if (Math.Abs(across) + radius * (1f + maxSlope) + chordError > half) return false;
            float y = definition.CenterY(x) + across;
            if (y - radius < 0f || y + radius > definition.WorldHeight) return false;
            for (int i = 0; i < cutouts.Length; i++)
            {
                var hole = cutouts[i];
                if (x + radius < hole.MinX || x - radius > hole.MaxX) continue;
                if (y + radius < hole.MinY || y - radius > hole.MaxY) continue;
                return false;
            }
            return true;
        }

        /// <summary>河道中心线（世界 Y）——与水面同一份 CenterY。</summary>
        public float CenterYAt(float x) => definition.CenterY(x);

        public bool TryGetLeaf(int index, out LeafState state)
        {
            state = default(LeafState);
            if (index < 0 || index >= leaves.Length || !leaves[index].Alive) return false;
            var leaf = leaves[index];
            float across = leaf.BaseAcross + (float)Math.Sin(leaf.SwayPhase) * leaf.SwayAmp;
            float angle = (direction > 0 ? 0f : (float)Math.PI) +
                (float)Math.Sin(leaf.SwayPhase) * .22f + (float)Math.Sin(leaf.Spin) * .12f;
            state = new LeafState(leaf.X, across, angle, leaf.Size, leaf.Visibility);
            return true;
        }

        void Step(float step)
        {
            Elapsed += step;
            for (int i = 0; i < leaves.Length; i++)
            {
                var leaf = leaves[i];
                if (!leaf.Alive) continue;
                if (leaf.Leaving)
                {
                    leaf.Visibility -= FadeRate * step;
                    if (leaf.Visibility <= 0f)
                    {
                        leaf.Visibility = 0f;
                        Spawn(ref leaf);
                    }
                }
                else
                {
                    float nextX = leaf.X + direction * leaf.Speed * step;
                    float nextPhase = leaf.SwayPhase + leaf.SwayRate * step;
                    float nextAcross = leaf.BaseAcross + (float)Math.Sin(nextPhase) * leaf.SwayAmp;
                    if (IsFootprintLegal(nextX, nextAcross, leaf.Size * .5f + BobAmplitude + FadeLead)) {
                        leaf.X = nextX;
                        leaf.SwayPhase = nextPhase;
                        leaf.Spin += leaf.SpinRate * step;
                    }
                    else
                        leaf.Leaving = true;   // 原地淡出，绝不带着半透明穿桥
                    leaf.Visibility = Math.Min(1f, leaf.Visibility + FadeRate * step);
                }
                leaves[i] = leaf;
            }
        }

        void Spawn(ref Leaf leaf)
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                float size = Range(14f, 22f);
                float radius = size * .5f + BobAmplitude;
                float amp = Range(2f, 5f);
                float maxAcross = Math.Max(0f, half - (radius + FadeLead + amp) * (1f + maxSlope) - chordError);
                float baseAcross = Range(-1f, 1f) * maxAcross;
                float x = Range(radius + 1f, definition.WorldWidth - radius - 1f);
                if (!IsFootprintLegal(x, baseAcross, radius + FadeLead + amp)) continue;
                leaf = new Leaf
                {
                    X = x,
                    BaseAcross = baseAcross,
                    SwayAmp = amp,
                    SwayPhase = Range(0f, (float)Math.PI * 2f),
                    SwayRate = Range(.5f, 1.1f),
                    Spin = Range(0f, (float)Math.PI * 2f),
                    SpinRate = Range(.3f, .7f),
                    Size = size,
                    Speed = Range(9f, 15f),
                    Visibility = 0f,
                    Palette = random.Next(3),
                    Leaving = false,
                    Alive = true
                };
                return;
            }
            leaf.Alive = false;
            leaf.Visibility = 0f;
        }

        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
    }
}