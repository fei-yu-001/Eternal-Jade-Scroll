using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // 商人商品表：每个商人一份，条目、收货品类、黑市独门货与记忆对白全部在 JSON 里。
    // 加一件商品只改表，不碰代码（GDD 第二原则）。
    [Serializable]
    public sealed class MerchantStock
    {
        public string itemId = "";
        public int count = 1;
    }

    // 记忆层（GDD 十·第四层）：买卖次数到某个门槛，商人会主动提起上回的事。
    [Serializable]
    public sealed class MerchantMemoryLine
    {
        public string id = "";
        public int at;
        public string line = "";
    }

    public sealed class MerchantDef
    {
        public MerchantDef(string id, string name, string title, string greeting,
            List<MerchantStock> stock, List<MerchantStock> hidden, List<string> buys,
            List<MerchantMemoryLine> memory)
        {
            Id = id; Name = name; Title = title; Greeting = greeting;
            Stock = stock; Hidden = hidden; Buys = buys; Memory = memory;
        }

        public string Id { get; }
        public string Name { get; }
        public string Title { get; }
        public string Greeting { get; }
        public IReadOnlyList<MerchantStock> Stock { get; }
        // 黑市独门货：常人不一定见得着。
        public IReadOnlyList<MerchantStock> Hidden { get; }
        // 愿意收的类别（对应物品表的 category）。
        public IReadOnlyList<string> Buys { get; }
        public IReadOnlyList<MerchantMemoryLine> Memory { get; }

        public int StockOf(string itemId) => Stock.FirstOrDefault(s => s.itemId == itemId)?.count ?? 0;
        public int HiddenOf(string itemId) => Hidden.FirstOrDefault(s => s.itemId == itemId)?.count ?? 0;

        // 记忆对白：取不超过当前交易次数的最高门槛那一档。
        public string MemoryLine(int trades)
        {
            if (Memory.Count == 0) return "";
            return Memory.Where(m => trades >= m.at).OrderByDescending(m => m.at).First().line;
        }
    }

    public sealed class MerchantTable
    {
        readonly List<MerchantDef> merchants;

        public MerchantTable(List<MerchantDef> merchants) { this.merchants = merchants; }

        public IReadOnlyList<MerchantDef> Merchants => merchants;
        public MerchantDef Find(string id) => id == null ? null : merchants.FirstOrDefault(m => m.Id == id);

        public static MerchantTable Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("商人表读取失败：" + error);
            if (root.kind != JsonValue.Kind.Object) throw new FormatException("商人表应是一个对象。");

            var merchants = new List<MerchantDef>();
            foreach (var entry in Elements(root, "merchants"))
            {
                var id = Required(entry, "id");
                if (merchants.Any(m => m.Id == id)) throw new FormatException("商人 id 重复：" + id);
                var stock = new List<MerchantStock>();
                foreach (var line in Elements(entry, "stock")) stock.Add(Stock(line, id));
                var hidden = new List<MerchantStock>();
                foreach (var line in Elements(entry, "hidden")) hidden.Add(Stock(line, id));
                if (stock.Count == 0 && hidden.Count == 0) throw new FormatException("商人 " + id + " 没有货可卖。");
                foreach (var line in stock.Concat(hidden))
                    if (line.count < 1) throw new FormatException("商人 " + id + " 的 " + line.itemId + " 存货不能为负。");
                // 同一件货只能占一条货架行：否则 UI 出现重复行，退货时还可能凭空造出库存。
                var shelves = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in stock)
                    if (!shelves.Add(line.itemId)) throw new FormatException("商人 " + id + " 的货架上 " + line.itemId + " 重复出现。");
                foreach (var line in hidden)
                    if (!shelves.Add(line.itemId)) throw new FormatException("商人 " + id + " 的 " + line.itemId + " 在两处货架重复出现。");

                var buys = new List<string>();
                foreach (var value in entry["buys"].Items)
                    buys.Add(value.AsString("").Trim());
                if (buys.Any(string.IsNullOrEmpty)) throw new FormatException("商人 " + id + " 的收货品类不能为空串。");

                var memory = new List<MerchantMemoryLine>();
                foreach (var line in Elements(entry, "memory"))
                {
                    var threshold = line["at"].AsInt(0);
                    if (threshold < 0) throw new FormatException("商人 " + id + " 的记忆门槛不能为负。");
                    if (string.IsNullOrWhiteSpace(line["line"].AsString(null)))
                        throw new FormatException("商人 " + id + " 的记忆对白不能为空。");
                    memory.Add(new MerchantMemoryLine { id = id, at = threshold, line = line["line"].AsString("") });
                }
                if (memory.Count > 0 && memory.Select(m => m.at).Distinct().Count() != memory.Count)
                    throw new FormatException("商人 " + id + " 的记忆门槛重复。");

                merchants.Add(new MerchantDef(id, Required(entry, "name"), Required(entry, "title"),
                    Required(entry, "greeting"), stock, hidden, buys, memory));
            }
            if (merchants.Count == 0) throw new FormatException("商人表里没有商人。");
            return new MerchantTable(merchants);
        }

        // 表与物品表对表：货和收货品类都得指向真实存在的物品与类别。
        public void CrossCheck(ItemTable items)
        {
            foreach (var merchant in merchants)
            {
                foreach (var line in merchant.Stock.Concat(merchant.Hidden))
                    if (items.Find(line.itemId) == null)
                        throw new FormatException("商人 " + merchant.Id + " 卖的东西不在物品表里：" + line.itemId);
                foreach (var category in merchant.Buys)
                    if (items.FindCategory(category) == null)
                        throw new FormatException("商人 " + merchant.Id + " 的收货品类未定义：" + category);
            }
        }

        static MerchantStock Stock(JsonValue entry, string merchant)
        {
            var stock = new MerchantStock
            {
                itemId = Required(entry, "itemId"),
                count = entry["count"].AsInt(1),
            };
            return stock;
        }

        static IEnumerable<JsonValue> Elements(JsonValue parent, string key)
        {
            var array = parent[key];
            if (array.IsNull) yield break;
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
