using System;
using System.Collections.Generic;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Economy
{
    // 手艺配方表（recipes.json）：烹饪与炼药。加一道方子只改 JSON。
    public sealed class RecipeIngredient
    {
        public RecipeIngredient(string itemId, int count) { ItemId = itemId; Count = count; }
        public string ItemId { get; }
        public int Count { get; }
    }

    public sealed class RecipeDef
    {
        public RecipeDef(string id, string name, string craftType, string station,
            List<RecipeIngredient> ingredients, string resultItemId, int resultCount, double doubleChance)
        {
            Id = id; Name = name; CraftType = craftType; Station = station;
            Ingredients = ingredients; ResultItemId = resultItemId;
            ResultCount = resultCount; DoubleChance = doubleChance;
        }

        public string Id { get; }
        public string Name { get; }
        /// <summary>cooking（灶台）或 alchemy（药炉）。</summary>
        public string CraftType { get; }
        public string Station { get; }
        public IReadOnlyList<RecipeIngredient> Ingredients { get; }
        public string ResultItemId { get; }
        public int ResultCount { get; }
        /// <summary>双倍产出的基础几率（0–1），手巧/早慧再加成。</summary>
        public double DoubleChance { get; }
    }

    public sealed class RecipeTable
    {
        /// <summary>运行期的加载路径（Resources 下相对路径，不带扩展名）。
        /// 界面按它 Resources.Load、断言按它核对文件与 .meta 是否真在包里——
        /// 只留一个字符串，就不出现"配置在盘上却没人去那儿找"这种事。</summary>
        public const string ResourcePath = "Config/recipes";
        public const string FileName = "recipes.json";

        readonly List<RecipeDef> recipes;

        RecipeTable(List<RecipeDef> recipes) { this.recipes = recipes; }

        public IReadOnlyList<RecipeDef> Recipes => recipes;
        public RecipeDef Find(string id) => string.IsNullOrEmpty(id) ? null : recipes.FirstOrDefault(r => r.Id == id);
        public IEnumerable<RecipeDef> OfType(string craftType) =>
            recipes.Where(r => string.Equals(r.CraftType, craftType, StringComparison.Ordinal));

        public static RecipeTable Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("配方表读取失败：" + error);
            // 兼容两种写法：裸数组（任务书给的样子）与 {"recipes":[...]}。
            var array = root.kind == JsonValue.Kind.Array ? root : root["recipes"];
            if (array.IsNull)
                throw new FormatException("配方表应是数组，或含 recipes 数组。");
            if (array.kind != JsonValue.Kind.Array)
                throw new FormatException("配方表的 recipes 应是数组。");

            var recipes = new List<RecipeDef>();
            foreach (var entry in array.Items)
            {
                if (entry == null || entry.kind != JsonValue.Kind.Object)
                    throw new FormatException("配方表的元素应是对象。");
                var id = Required(entry, "id", "配方");
                if (recipes.Any(r => r.Id == id)) throw new FormatException("配方 id 重复：" + id);
                var craftType = Required(entry, "craftType", "配方 " + id);
                if (craftType != Crafting.Cooking && craftType != Crafting.Alchemy && craftType != Crafting.Forging)
                    throw new FormatException("配方 " + id + " 的手艺类型未定义：" + craftType +
                        "（可选 " + Crafting.Cooking + " / " + Crafting.Alchemy + "）");

                var ingredients = new List<RecipeIngredient>();
                foreach (var line in Elements(entry, "ingredients"))
                {
                    var itemId = Required(line, "itemId", "配方 " + id + " 的材料");
                    var count = line["count"].AsInt(1);
                    if (count < 1) throw new FormatException("配方 " + id + " 的材料 " + itemId + " 数量需至少为 1。");
                    ingredients.Add(new RecipeIngredient(itemId, count));
                }
                if (ingredients.Count == 0) throw new FormatException("配方 " + id + " 没有材料。");

                var result = entry["result"];
                if (result.IsNull || result.kind != JsonValue.Kind.Object)
                    throw new FormatException("配方 " + id + " 缺少 result。");
                var resultItemId = Required(result, "itemId", "配方 " + id + " 的产物");
                var resultCount = result["count"].AsInt(1);
                if (resultCount < 1) throw new FormatException("配方 " + id + " 的产出数量需至少为 1。");

                var chance = entry["doubleChance"].AsFloat(0f);
                if (chance < 0f || chance > 1f)
                    throw new FormatException("配方 " + id + " 的双倍几率需落在 0–1 之间。");

                recipes.Add(new RecipeDef(id, Required(entry, "name", "配方 " + id), craftType,
                    entry["station"].AsString(""), ingredients, resultItemId, resultCount, chance));
            }
            if (recipes.Count == 0) throw new FormatException("配方表里没有配方。");
            return new RecipeTable(recipes);
        }

        // 与物品表对表：材料与产物都得是真实存在的东西，否则做出来的会是空气。
        public void CrossCheck(ItemTable items)
        {
            foreach (var recipe in recipes)
            {
                foreach (var line in recipe.Ingredients)
                    if (items.Find(line.ItemId) == null)
                        throw new FormatException("配方 " + recipe.Id + " 的材料不在物品表里：" + line.ItemId);
                if (items.Find(recipe.ResultItemId) == null)
                    throw new FormatException("配方 " + recipe.Id + " 的产物不在物品表里：" + recipe.ResultItemId);
            }
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

        static string Required(JsonValue entry, string key, string owner)
        {
            var value = entry[key].AsString(null);
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException(owner + " 缺少字段 " + key + "（应为非空字符串）。");
            return value;
        }
    }
}
