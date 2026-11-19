using System.IO;
using System.Linq;

namespace TwelveJade.Core
{
    // 大世界底座验收（第一阶段）：只验"世界"，不验内容。
    // 核心断言是 World Size 与 Camera View 的比例——世界必须远大于一屏，
    // 否则小地图失去意义、"跑图"无从谈起。
    /// <summary>世界尺寸验收参数（来自 town-map.json 的 acceptance 节点，缺省值见此）。</summary>
    public sealed class WorldAcceptance
    {
        public WorldAcceptance(float viewportWidth = 1920f, float viewportHeight = 1080f,
            float minWorldWidthToViewportRatio = 4.0f, float minWorldHeightToViewportRatio = 4.0f)
        {
            ViewportWidth = viewportWidth;
            ViewportHeight = viewportHeight;
            MinWorldWidthToViewportRatio = minWorldWidthToViewportRatio;
            MinWorldHeightToViewportRatio = minWorldHeightToViewportRatio;
        }

        public float ViewportWidth { get; }
        public float ViewportHeight { get; }
        public float MinWorldWidthToViewportRatio { get; }
        public float MinWorldHeightToViewportRatio { get; }
    }

    public static class WorldChecks
    {

        public static void Run(TownMap town) => Run(town, new WorldAcceptance());

        public static void Run(TownMap town, WorldAcceptance acceptance)
        {
            Check("世界是分块拼成的（Chunk 可配置）", town.Chunks.Count > 0,
                town.Chunks.Count + " 块，每块 " + town.ChunkSize + " 世界单位");

            // 分块必须无缝铺满世界：数量对、位置不重叠、边缘相接。
            Check("分块铺满世界且不重叠", ChunksTile(town),
                string.Format("{0} 块覆盖 {1}x{2}", town.Chunks.Count, town.WorldWidth, town.WorldHeight));

            // 关键比例：视野必须远小于世界。
            var viewW = acceptance.ViewportWidth / town.WorldScale;
            var viewH = acceptance.ViewportHeight / town.WorldScale;
            var coverage = (viewW * viewH) / (town.WorldWidth * town.WorldHeight);
            Check("一屏只覆盖世界的一小部分（<25%）", coverage < .25f,
                string.Format("视野 {0:0}x{1:0} / 世界 {2:0}x{3:0} = {4:P1}",
                    viewW, viewH, town.WorldWidth, town.WorldHeight, coverage));
            // 阈值配置化（Gate C0-C）：不再硬编码 5 倍；当前批准的世界 8192 对横向约 4.27 倍。
            Check(string.Format("世界远大于一屏（横向 ≥{0:0.##} 倍）", acceptance.MinWorldWidthToViewportRatio),
                viewW * acceptance.MinWorldWidthToViewportRatio <= town.WorldWidth,
                string.Format("视野宽 {0:0} × {1:0.##} = {2:0} vs 世界宽 {3:0}",
                    viewW, acceptance.MinWorldWidthToViewportRatio,
                    viewW * acceptance.MinWorldWidthToViewportRatio, town.WorldWidth));

            // 摄像机钳制：贴边时视口仍在世界内 → 不露黑边。
            var halfW = viewW * .5f;
            var halfH = viewH * .5f;
            var clampedX = town.ClampCameraX(0f, halfW);
            var clampedEdgeX = town.ClampCameraX(town.WorldWidth, halfW);
            Check("摄像机横向不越界（左/右）",
                clampedX >= halfW - .01f && clampedEdgeX <= town.WorldWidth - halfW + .01f,
                string.Format("x {0:0} / {1:0}", clampedX, clampedEdgeX));
            var clampedY = town.ClampCameraY(0f, halfH);
            var clampedEdgeY = town.ClampCameraY(town.WorldHeight, halfH);
            Check("摄像机纵向不越界（上/下）",
                clampedY >= halfH - .01f && clampedEdgeY <= town.WorldHeight - halfH + .01f,
                string.Format("y {0:0} / {1:0}", clampedY, clampedEdgeY));

            // 不可通行：山体/河不可走，桥可走，世界外不可走。
            // 坐标从 blocked 分区反推，不写死数值——世界尺寸变了断言依然成立。
            var mountain = town.Blocked.Where(r => r.Contains(r.MinX + 1f, r.MinY + 1f) && r.MinY < 200f).ToList();
            Check("山体不可通行", mountain.Count > 0 && mountain.All(r => !town.CanStand(r.MinX + 8f, r.MinY + 8f)),
                mountain.Count + " 块山体采样点");
            // 河岸分片高度可不足16：采样实际矩形中点，不能借另一横带的Y再加8。
            var riverBlock = town.Blocked.FirstOrDefault(r => r.MinY > 2000f &&
                r.MinY < town.WorldHeight * .6f && r.MinX > town.WorldWidth * .2f && r.MinX < town.WorldWidth * .8f);
            var riverX = riverBlock == null ? 0f : (riverBlock.MinX + riverBlock.MaxX) * .5f;
            var riverY = riverBlock == null ? 0f : (riverBlock.MinY + riverBlock.MaxY) * .5f;
            Check("河面不可通行（桥外）", riverBlock != null && !town.CanStand(riverX, riverY),
                riverBlock != null ? $"({riverX:0.####},{riverY:0.####})" : "找不到河");
            // 桥：在 blocked 的缺口处（桥面不在 blocked 里 → 可走）
            var bridge = town.Landmarks.FirstOrDefault(m => m.Id.Contains("bridge") || m.Id.Contains("river"));
            Check("主桥可通行", bridge != null && town.CanStand(bridge.X, bridge.Y),
                bridge != null ? $"({bridge.X:0},{bridge.Y:0})" : "缺少桥地标");
            Check("世界边界外不可通行",
                !town.CanStand(-1f, 2560f) && !town.CanStand(town.WorldWidth + 1f, 2560f) &&
                !town.CanStand(2560f, -1f) && !town.CanStand(2560f, town.WorldHeight + 1f));
            Check("出生点可站立", town.CanStand(town.SpawnX, town.SpawnY));

            // 跑图距离：从出生点到世界最北的可走地标（后山本体不可站，用山脚/北街代表），
            // 按移动速度换算成时间——"跑多久才能穿越世界"才是玩家体感的度量。
            // 注意不能拿"最南地标"当起点：出生点往往就在南缘，与地标重合会算出 0。
            var standable = town.Landmarks.Where(m => town.CanStand(m.X, m.Y)).ToList();
            // Y 轴向下增大，所以"最北"是 Y 最小（曾误用 Max，拿到最南端点导致 span=0）。
            var northMost = standable.Count > 0 ? standable.Min(m => m.Y) : 0f;
            var span = Math.Abs(town.SpawnY - northMost);
            var walkSeconds = span / 260f;   // 与 Presentation 里的 WalkSpeedWorld 一致
            var runSeconds = span / 470f;
            Check("跑图距离充足（步行穿越南北 > 25 秒）", walkSeconds > 25f,
                string.Format("南北 {0:0} 单位，步行约 {1:0} 秒 / 奔跑约 {2:0} 秒", span, walkSeconds, runSeconds));
            // 出生点不在世界最南缘 → 说明留有可继续向南扩张的余地
            Check("出生点不贴世界边缘（留扩张余地）", town.SpawnY < town.WorldHeight * .98f,
                string.Format("出生 y={0:0} / 世界高 {1:0}", town.SpawnY, town.WorldHeight));

            // 区域结构：地标覆盖镇内各处（南门/主街/集市/河边/山脚/后山/东西街）。
            Check("地标覆盖多个区域（≥8）", town.Landmarks.Count >= 8, town.Landmarks.Count + " 处");
        }

        /// <summary>Gate C1-C：正式贴图接入 Resources 的验收。
        /// 断言的是"运行期真能加载到"，不只是"配置里写了个路径"——
        /// Resources.Load 失败在批处理里表现为品红格，配置对了但文件没拷进来照样是空世界。</summary>
        public static void RunChunkArt(TownMap town, string resourcesDir)
        {
            Check("世界贴图走模板加载（chunkArtTemplate 已配置）",
                !string.IsNullOrEmpty(town.ChunkArtTemplate), town.ChunkArtTemplate ?? "(未配置)");

            Check("模板网格与 chunks 数量一致",
                town.WorldColumns * town.WorldRows == town.Chunks.Count,
                string.Format("{0}x{1} = {2} 格 / chunks {3} 条",
                    town.WorldColumns, town.WorldRows, town.WorldColumns * town.WorldRows, town.Chunks.Count));

            var missing = new List<string>();
            for (var row = 0; row < town.WorldRows; row++)
            {
                for (var col = 0; col < town.WorldColumns; col++)
                {
                    var path = town.ChunkArtAt(col, row);
                    if (string.IsNullOrEmpty(path) ||
                        !File.Exists(Path.Combine(resourcesDir, path + ".png"))) missing.Add(path ?? "(空)");
                }
            }
            Check("全部分块贴图都在 Resources 下", missing.Count == 0,
                missing.Count == 0 ? town.WorldColumns * town.WorldRows + " 张就位"
                                   : "缺 " + missing.Count + " 张：" + string.Join(", ", missing.Take(3)));

            Check("小地图底图在 Resources 下",
                !string.IsNullOrEmpty(town.OverviewArt) &&
                File.Exists(Path.Combine(resourcesDir, town.OverviewArt + ".png")), town.OverviewArt ?? "(未配置)");

            // 兼容回落不能被删：没配模板的老配置仍要能靠 chunks[].art 跑起来。
            Check("chunks[].art 灰盒回落仍完整", town.Chunks.All(c => !string.IsNullOrEmpty(c.Art)),
                town.Chunks.Count + " 条");
        }

        static bool ChunksTile(TownMap town)
        {
            var area = town.Chunks.Sum(c => c.Width * c.Height);
            if (Mathf.Approximately(area, town.WorldWidth * town.WorldHeight) == false)
                return false;
            foreach (var a in town.Chunks)
            foreach (var b in town.Chunks)
            {
                if (ReferenceEquals(a, b)) continue;
                var overlapX = System.Math.Min(a.X + a.Width, b.X + b.Width) - System.Math.Max(a.X, b.X);
                var overlapY = System.Math.Min(a.Y + a.Height, b.Y + b.Height) - System.Math.Max(a.Y, b.Y);
                if (overlapX > .5f && overlapY > .5f) return false;   // 有重叠
            }
            return true;
        }

        static class Mathf
        {
            public static bool Approximately(float a, float b) => System.Math.Abs(a - b) < .5f;
        }

        static void Check(string name, bool condition, string detail = null)
        {
            if (!condition) throw new System.Exception("FAIL: " + name + (detail != null ? " · " + detail : ""));
            System.Console.WriteLine("PASS: " + name + (detail != null ? " · " + detail : ""));
        }
    }
}
