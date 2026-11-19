using System;
using System.Collections.Generic;
using TwelveJade.Core;

namespace TwelveJade.Economy
{
    // items.json 里 ItemTable 读不进去的附加标记（目前只有"煞气门控"）。
    // ItemTable 只认 tier/category/stack/price 那几个字段，新加的 "fateGated" 它直接忽略。
    // 与其为了一个开关去改 Core 的 ItemDef（那会牵动交易、任务、存档三处），
    // 不如在这里按需再读一遍同一张表——读的是同一个文件，不存在第二份真相。
    public sealed class ItemFlags
    {
        readonly HashSet<string> gated;

        ItemFlags(HashSet<string> gated) { this.gated = gated; }

        public static ItemFlags Empty { get; } = new ItemFlags(new HashSet<string>(StringComparer.Ordinal));

        /// <summary>当前生效的标记表。只能由 Load 装上（T098：不给外部一个"忘了装也没事"的口子）。</summary>
        public static ItemFlags Current { get; private set; } = Empty;

        /// <summary>装过表没有。没装 = 一个煞气标记都没读到，此时任何"门控判定"都会把
        /// 煞气货当成寻常货放行——这种降级必须当场响，不能静悄悄（审计第 5 条）。</summary>
        public static bool IsLoaded { get; private set; }

        public bool IsFateGated(string itemId) => itemId != null && gated.Contains(itemId);

        public static ItemFlags Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("物品标记表读取失败：" + error);
            var gated = new HashSet<string>(StringComparer.Ordinal);
            var items = root.kind == JsonValue.Kind.Array ? root : root["items"];
            if (items.IsNull) return new ItemFlags(gated);
            foreach (var entry in items.Items)
            {
                if (entry == null || entry.kind != JsonValue.Kind.Object) continue;
                var id = entry["id"].AsString("");
                if (id.Length == 0) continue;
                if (entry["fateGated"].AsBool(false)) gated.Add(id);
            }
            return new ItemFlags(gated);
        }

        public static ItemFlags Load(string json)
        {
            var flags = Parse(json);
            Current = flags;
            IsLoaded = true;
            return flags;
        }
    }
}
