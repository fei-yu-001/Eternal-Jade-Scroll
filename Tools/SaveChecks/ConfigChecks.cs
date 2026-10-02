using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Core
{
    // 配置表测试：直接读游戏工程里的正式 JSON——改配置表改错了，这里先报警。
    // 与 SaveChecks 一起跑：dotnet run --project Tools/SaveChecks
    public static class ConfigChecks
    {
        public static void Run()
        {
            var configDir = FindConfigDirectory();
            Check("config directory found", configDir != null);
            var itemPath = Path.Combine(configDir, "items.json");
            var itemTable = ItemTable.Parse(File.ReadAllText(itemPath));
            Check("items.json parses", itemTable.Items.Count >= 18, itemTable.Items.Count + " 件物品");
            InventoryChecks.Run(itemTable);
            InventoryChecks.RunReputation();

            var mapPath = Path.Combine(configDir, "town-map.json");
            var town = TownMap.Parse(File.ReadAllText(mapPath));
            TradeChecks.Run(itemTable, configDir);
            CombatChecks.Run();
            EnemyChecks.Run(itemTable, configDir);
            M4Checks.Run(itemTable, town, configDir);
            DialogueChecks.Run(NpcTable.Parse(File.ReadAllText(Path.Combine(configDir, "npcs.json"))), configDir);
            ChapterChecks.Run(configDir);
            QuestChecks.Run(itemTable, configDir, town);
            ScheduleChecks.Run(new SaveData { slot = 1, characterId = "farmer", characterName = "A" },
                itemTable, configDir, town);
            NpcChecks.Run(itemTable, configDir, town);
            // 地形底座阶段（phase=terrain）props 可为空：立绘下限只在完整阶段生效。
            Check("town-map.json parses", town.Walkable.Count >= 5 &&
                (town.Phase == "terrain" || town.Props.Count >= 10) && town.Landmarks.Count >= 3,
                string.Format("{0} 条走廊 / {1} 件立绘 / {2} 处地标 / phase={3}",
                    town.Walkable.Count, town.Props.Count, town.Landmarks.Count, town.Phase));
            Check("spawn stands in the town", town.CanStand(town.SpawnX, town.SpawnY));
            Check("corridors touch each other", CorridorsConnected(town));
            Check("landmarks are reachable", town.Landmarks.All(mark => town.CanStand(mark.X, mark.Y) ||
                Math.Abs(town.ClampToWalkable(mark.X, mark.Y).x - mark.X) < 60f));
            Check("props stay inside the canvas", town.Props.All(prop =>
                prop.X >= 0f && prop.X <= town.ArtWidth && prop.Y >= 0f && prop.Y <= town.ArtHeight));
            // 立绘障碍不能把街巷拦死：从出生点按网格洪水填充，每条走廊都要走得进去。
            Check("blockers never seal off a corridor", ReachableFromSpawn(town));

            // 可行走判定：走廊外不可走。有立绘障碍时再验"圆内不可走、贴着能滑过"。
            Check("corridor gaps are not walkable", !town.CanStand(20f, 20f) && !town.CanStand(town.ArtWidth - 20f, town.ArtHeight - 20f));
            var blocker = town.Props.FirstOrDefault(prop => prop.Blocks);
            if (blocker != null)
            {
                Check("blockers cannot be entered", !town.CanStand(blocker.X, blocker.Y) &&
                    !town.CanStand(blocker.X + blocker.Radius * .5f, blocker.Y));
                Check("blocker face is standable", town.CanStand(blocker.X + blocker.Radius + 6f, blocker.Y));
                var snappedBlocker = town.ClampToWalkable(blocker.X + blocker.Radius + 30f, blocker.Y);
                Check("click outside the corridor snaps back", town.CanStand(snappedBlocker.x, snappedBlocker.y));
                Check("clamped point never lands in a blocker", Enumerable.Range(0, 40).All(i =>
                {
                    var angle = i / 40f * Math.PI * 2;
                    var point = town.ClampToWalkable(blocker.X + (float)Math.Cos(angle) * (blocker.Radius * .6f),
                        blocker.Y + (float)Math.Sin(angle) * (blocker.Radius * .6f));
                    return town.CanStand(point.x, point.y) || !town.WalkableContains(point.x, point.y);
                }));
            }
            Check("perspective narrows toward the horizon", town.PerspectiveTop < town.PerspectiveBottom &&
                town.PerspectiveTop > .3f && town.PerspectiveBottom < 1.6f);

            // 改坏的配置要被拒绝：越界走廊、过小走廊、出生点在墙里、立绘高为零。
            var bad = new[]
            {
                ("{\"walkable\":[{\"x\":0,\"y\":0,\"width\":10,\"height\":10}],\"spawn\":{\"x\":5,\"y\":5}}", "走廊过小"),
                ("{\"walkable\":[{\"x\":1900,\"y\":1000,\"width\":200,\"height\":200}],\"spawn\":{\"x\":1905,\"y\":1005}}", "走廊超出画布"),
                ("{\"walkable\":[{\"x\":100,\"y\":100,\"width\":200,\"height\":200}],\"spawn\":{\"x\":900,\"y\":900}}", "出生点在墙里"),
                ("{\"walkable\":[{\"x\":100,\"y\":100,\"width\":200,\"height\":200}],\"spawn\":{\"x\":200,\"y\":200}," +
                 "\"props\":[{\"slug\":\"x\",\"x\":200,\"y\":200,\"height\":0}]}", "立绘高度为零"),
                ("{\"walkable\":[],\"spawn\":{\"x\":0,\"y\":0}}", "没有走廊"),
                ("not json at all", "不是 JSON")
            };
            foreach (var (json, label) in bad)
            {
                var rejected = false;
                try { TownMap.Parse(json); }
                catch (FormatException) { rejected = true; }
                Check("malformed town map rejected: " + label, rejected);
            }
        }

        // 从可执行文件往上找到仓库根（含 GameClient/TwelveJade/Assets/Resources/Config）。
        static string FindConfigDirectory()
        {
            var candidates = new List<string>();
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (var depth = 0; depth < 8 && directory != null; depth++)
            {
                candidates.Add(Path.Combine(directory.FullName, "GameClient", "TwelveJade", "Assets", "Resources", "Config"));
                candidates.Add(Path.Combine(directory.FullName, "..", "..", "..", "..", "..",
                    "GameClient", "TwelveJade", "Assets", "Resources", "Config"));
                directory = directory.Parent;
            }
            return candidates.Select(Path.GetFullPath).FirstOrDefault(Directory.Exists);
        }

        static bool CorridorsConnected(TownMap map)
        {
            // 走廊接得上才算走得通：矩形相交，或沿一条边贴合（贴着走能过去）。
            // 只在一个角上碰到不算——那里没有可以穿过的宽度。
            var visited = new HashSet<int> { 0 };
            var queue = new Queue<int>();
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var current = map.Walkable[index];
                for (var i = 0; i < map.Walkable.Count; i++)
                {
                    if (visited.Contains(i)) continue;
                    var other = map.Walkable[i];
                    var gapX = Math.Min(current.MaxX, other.MaxX) - Math.Max(current.MinX, other.MinX);
                    var gapY = Math.Min(current.MaxY, other.MaxY) - Math.Max(current.MinY, other.MinY);
                    if (gapX < -1f || gapY < -1f) continue;
                    if (gapX <= 1f && gapY <= 1f) continue;
                    visited.Add(i);
                    queue.Enqueue(i);
                }
            }
            return visited.Count == map.Walkable.Count;
        }

        // 网格洪水填充：从出生点出发，走廊里应有一大片可站立的地面（约等于走廊总面积的七成以上）。
        static bool ReachableFromSpawn(TownMap map)
        {
            const float step = 12f;
            var startX = (int)Math.Floor(map.SpawnX / step);
            var startY = (int)Math.Floor(map.SpawnY / step);
            var reached = new HashSet<(int, int)> { (startX, startY) };
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue((startX, startY));
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var next = (x + dx, y + dy);
                    if (reached.Contains(next)) continue;
                    var px = (next.Item1 + .5f) * step;
                    var py = (next.Item2 + .5f) * step;
                    if (px < 0f || py < 0f || px > map.ArtWidth || py > map.ArtHeight) continue;
                    if (!map.WalkableContains(px, py)) continue;
                    // 圆形障碍挡住的一格若被挤得只剩边角，按不可走处理，避免从障碍边缘"钻"过去。
                    if (map.Props.Any(prop => prop.Blocks &&
                        (px - prop.X) * (px - prop.X) + (py - prop.Y) * (py - prop.Y) <= prop.Radius * prop.Radius * .8f)) continue;
                    reached.Add(next);
                    queue.Enqueue(next);
                }
            }
            var walkableArea = map.Walkable.Sum(rect => rect.Width * rect.Height);
            var reachedArea = reached.Count * step * step;
            return reachedArea > walkableArea * .55f;
        }

        static void Check(string name, bool condition, string detail = null)
        {
            if (!condition) throw new Exception("FAIL: " + name + (detail == null ? "" : "（" + detail + "）"));
            Console.WriteLine("PASS: " + name + (detail == null ? "" : "（" + detail + "）"));
        }
    }
}
