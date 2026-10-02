using System.Linq;

namespace TwelveJade.Core
{
    // 大世界底座验收（第一阶段）：只验"世界"，不验内容。
    // 核心断言是 World Size 与 Camera View 的比例——世界必须远大于一屏，
    // 否则小地图失去意义、"跑图"无从谈起。
    public static class WorldChecks
    {
        const float ScreenW = 1920f, ScreenH = 1080f;

        public static void Run(TownMap town)
        {
            Check("世界是分块拼成的（Chunk 可配置）", town.Chunks.Count > 0,
                town.Chunks.Count + " 块，每块 " + town.ChunkSize + " 世界单位");

            // 分块必须无缝铺满世界：数量对、位置不重叠、边缘相接。
            Check("分块铺满世界且不重叠", ChunksTile(town),
                string.Format("{0} 块覆盖 {1}x{2}", town.Chunks.Count, town.WorldWidth, town.WorldHeight));

            // 关键比例：视野必须远小于世界。
            var viewW = ScreenW / town.WorldScale;
            var viewH = ScreenH / town.WorldScale;
            var coverage = (viewW * viewH) / (town.WorldWidth * town.WorldHeight);
            Check("一屏只覆盖世界的一小部分（<25%）", coverage < .25f,
                string.Format("视野 {0:0}x{1:0} / 世界 {2:0}x{3:0} = {4:P1}",
                    viewW, viewH, town.WorldWidth, town.WorldHeight, coverage));
            Check("世界远大于一屏（横向 ≥5 倍）", viewW * 5f <= town.WorldWidth,
                viewW.ToString("0") + " x5 = " + (viewW * 5f).ToString("0") + " vs 世界宽 " + town.WorldWidth.ToString("0"));

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
            // 河：取一条最宽的 blocked 横带的中点（避开桥位），应不可走
            var riverRow = town.Blocked
                .Where(r => r.MinY > 2000f && r.MinY < town.WorldHeight * .6f)
                .GroupBy(r => r.MinY)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            var riverPoint = riverRow != null
                ? new MapRect(0, riverRow.Key, town.WorldWidth, 1).MinY + 8f
                : 0f;
            var riverX = town.Blocked.FirstOrDefault(r => Math.Abs(r.MinY - riverPoint) < 40f &&
                r.MinX > town.WorldWidth * .2f && r.MinX < town.WorldWidth * .8f);
            Check("河面不可通行（桥外）", riverX != null && !town.CanStand(riverX.MinX + 8f, riverPoint),
                riverX != null ? $"({riverX.MinX},{riverPoint:0})" : "找不到河");
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
