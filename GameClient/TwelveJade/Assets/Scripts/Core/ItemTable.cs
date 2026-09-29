using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // 物品表：品阶与类别都是表里定义的（不是枚举），加一条 JSON 就能加物品、加类别——
    // 兑现 00 文档第二原则「所有内容数据化」，也为 M3 交易所的价格表留好基准价字段。
    public sealed class ItemTier
    {
        public ItemTier(string id, string name, string color)
        { Id = id; Name = name; Color = color; }

        public string Id { get; }
        public string Name { get; }
        public string Color { get; }
    }

    public sealed class ItemCategory
    {
        public ItemCategory(string id, string name) { Id = id; Name = name; }
        public string Id { get; }
        public string Name { get; }
    }

    public sealed class ItemDef
    {
        public ItemDef(string id, string name, string tier, string category, string icon,
            int stack, int price, string description)
        {
            Id = id; Name = name; Tier = tier; Category = category; Icon = icon;
            Stack = stack; Price = price; Description = description;
        }

        public string Id { get; }
        public string Name { get; }
        public string Tier { get; }
        public string Category { get; }
        public string Icon { get; }
        public int Stack { get; }
        public int Price { get; }
        public string Description { get; }
    }

    public sealed class ItemTable
    {
        public const int MaxStack = 99;

        readonly List<ItemDef> items;
        readonly List<ItemTier> tiers;
        readonly List<ItemCategory> categories;

        ItemTable(List<ItemDef> items, List<ItemTier> tiers, List<ItemCategory> categories)
        {
            this.items = items; this.tiers = tiers; this.categories = categories;
        }

        public IReadOnlyList<ItemDef> Items => items;
        public IReadOnlyList<ItemTier> Tiers => tiers;
        public IReadOnlyList<ItemCategory> Categories => categories;

        public ItemDef Find(string id) => string.IsNullOrEmpty(id) ? null : items.FirstOrDefault(x => x.Id == id);
        public ItemTier FindTier(string id) => tiers.FirstOrDefault(x => x.Id == id);
        public ItemCategory FindCategory(string id) => categories.FirstOrDefault(x => x.Id == id);

        public static ItemTable Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("物品表读取失败：" + error);
            if (root.kind != JsonValue.Kind.Object)
                throw new FormatException("物品表应是一个对象。");

            var tiers = new List<ItemTier>();
            foreach (var entry in Elements(root, "tiers"))
                tiers.Add(new ItemTier(Required(entry, "id"), Required(entry, "name"), entry["color"].AsString("#8C8C86")));
            if (tiers.Count == 0) throw new FormatException("物品表至少要有一个品阶。");

            var categories = new List<ItemCategory>();
            foreach (var entry in Elements(root, "categories"))
                categories.Add(new ItemCategory(Required(entry, "id"), Required(entry, "name")));
            if (categories.Count == 0) throw new FormatException("物品表至少要有一个类别。");

            var items = new List<ItemDef>();
            foreach (var entry in Elements(root, "items"))
            {
                var id = Required(entry, "id");
                if (items.Any(x => x.Id == id)) throw new FormatException("物品 id 重复：" + id);
                var tier = Required(entry, "tier");
                if (!tiers.Any(x => x.Id == tier)) throw new FormatException("物品 " + id + " 的品阶未定义：" + tier);
                var category = Required(entry, "category");
                if (!categories.Any(x => x.Id == category))
                    throw new FormatException("物品 " + id + " 的类别未定义：" + category);
                var stack = entry["stack"].AsInt(1);
                if (stack < 1 || stack > MaxStack)
                    throw new FormatException("物品 " + id + " 的堆叠上限需在 1–" + MaxStack + " 之间。");
                var price = entry["price"].AsInt(0);
                if (price < 0) throw new FormatException("物品 " + id + " 的基准价不能为负。");
                items.Add(new ItemDef(id, Required(entry, "name"), tier, category,
                    Required(entry, "icon"), stack, price, entry["description"].AsString("")));
            }
            if (items.Count == 0) throw new FormatException("物品表里没有物品。");
            return new ItemTable(items, tiers, categories);
        }

        // 新档的开局行囊：写在这里而不是散在 UI 里，改配置即可调整开局。
        public const int StartingCoins = 300;
        public static readonly (string id, int count)[] StartingKit =
        {
            ("ganliang", 5), ("jinchuangyao", 2), ("cudao", 1), ("cubupao", 1)
        };

        static IEnumerable<JsonValue> Elements(JsonValue parent, string key)
        {
            var array = parent[key];
            if (array == null || array.IsNull) yield break;
            if (array.kind != JsonValue.Kind.Array) throw new FormatException("字段 " + key + " 应是数组。");
            foreach (var item in array.Items)
            {
                if (item == null || item.kind != JsonValue.Kind.Object)
                    throw new FormatException("字段 " + key + " 的元素应是对象。");
                yield return item;
            }
        }

        static string Required(JsonValue entry, string key)
        {
            var value = entry[key].AsString(null);
            if (string.IsNullOrWhiteSpace(value)) throw new FormatException("缺少字段 " + key + "（应为非空字符串）。");
            return value;
        }
    }
}
