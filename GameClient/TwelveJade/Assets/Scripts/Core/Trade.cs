using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // 存档里的商人状态：存货（买一件少一件）与记忆（买卖次数、好感）。
    // 编号靠 itemId 对回商品表——表里加一件旧货，这里自动多一格。
    [Serializable]
    public sealed class StockLine
    {
        public string itemId = "";
        public int count;
    }

    [Serializable]
    public sealed class MerchantState
    {
        public string id = "";
        public int trades;
        public int goodwill;
        public StockLine[] stock = System.Array.Empty<StockLine>();
        public StockLine[] hidden = System.Array.Empty<StockLine>();
    }

    // 买卖规则：价格、声望折扣、黑市加价、收不收、买不买得起。
    // 一律整数文，向上取整，绝不出现负价。
    public static class Trade
    {
        public const int MaxQuantity = 99;
        public const float BlackMarketMultiplier = 1.4f;
        // 一笔买卖涨多少好感；黑市交易多涨一些（他知道你不是干净人）。
        public const int GoodwillPerTrade = 2;
        public const int GoodwillPerBlackTrade = 4;

        public static int BuyPrice(ItemDef item, int reputation) =>
            item == null ? 0 : Reputation.BuyPrice(item.Price, reputation);

        // 收价：基准价的一半，再按声望上浮。
        public static int SellPrice(ItemDef item, int reputation) =>
            item == null ? 0 : Reputation.SellPrice(item.Price, reputation);

        public static int BlackPrice(ItemDef item) =>
            item == null ? 0 : (int)Math.Ceiling(item.Price * BlackMarketMultiplier);

        // 任务物不卖：误卖了会坏剧情（规划"防呆"）。
        public static bool CanSell(ItemDef item, MerchantDef merchant)
        {
            if (item == null || merchant == null) return false;
            if (item.Category == "renwu") return false;
            return merchant.Buys.Contains(item.Category);
        }

        public static int Missing(int coins, int price) => Math.Max(0, price - coins);

        public static bool CanAfford(int coins, int price) => price <= coins;

        // 黑市：杀星入命 / 龙蛇命这类命格才看得见全部独门货。
        public static readonly string[] BlackMarketFavouredDestinies = { "shaxing", "longshe", "chiqing" };

        public static bool SeesBlackMarket(SaveData save) =>
            save != null && save.destiny != null &&
            BlackMarketFavouredDestinies.Contains(save.destiny);
    }

    // 记忆与存货的读写：纯数据操作，UI 只调这里。
    public static class MerchantLedger
    {
        // 取（或补出）某个商人的存档状态；首次见面按商品表铺一份满货。
        public static MerchantState Of(SaveData save, MerchantDef merchant)
        {
            if (save == null || merchant == null) return null;
            save.merchants ??= System.Array.Empty<MerchantState>();
            var state = save.merchants.FirstOrDefault(m => m.id == merchant.Id);
            if (state == null)
            {
                state = new MerchantState
                {
                    id = merchant.Id,
                    trades = 0,
                    goodwill = 0,
                    stock = merchant.Stock.Select(s => new StockLine { itemId = s.itemId, count = s.count }).ToArray(),
                    hidden = merchant.Hidden.Select(s => new StockLine { itemId = s.itemId, count = s.count }).ToArray(),
                };
                save.merchants = save.merchants.Concat(new[] { state }).ToArray();
            }
            state.stock ??= System.Array.Empty<StockLine>();
            state.hidden ??= System.Array.Empty<StockLine>();
            return state;
        }

        public static int Left(MerchantState state, StockLine[] lines, string itemId) =>
            lines?.FirstOrDefault(s => s.itemId == itemId)?.count ?? 0;

        public static int Left(MerchantState state, string itemId) =>
            Left(state, state.stock, itemId) + Left(state, state.hidden, itemId);

        // 从他手里拿货：返回真正拿到的件数（存货不足就少拿，不为负）。
        public static int TakeFrom(MerchantState state, string itemId, int count, bool black)
        {
            if (state == null || count <= 0) return 0;
            var lines = black ? state.hidden : state.stock;
            var taken = 0;
            foreach (var line in lines.Where(l => l.itemId == itemId))
            {
                if (taken >= count) break;
                var moved = Math.Min(line.count, count - taken);
                line.count -= moved;
                taken += moved;
            }
            return taken;
        }

        // 买入：扣钱、进包、减存货、记一笔记忆。返回实际成交件数。
        public static int Buy(SaveData save, MerchantState state, ItemTable items, string itemId,
            int count, bool black)
        {
            var item = items.Find(itemId);
            if (save == null || state == null || item == null || count <= 0) return 0;
            var unit = black ? Trade.BlackPrice(item) : Trade.BuyPrice(item, save.localReputation);
            var affordable = unit <= 0 ? count : Math.Min(count, save.coins / unit);
            affordable = Math.Min(affordable, MerchantLedger.Left(state, black ? state.hidden : state.stock, itemId));
            if (affordable <= 0) return 0;
            var taken = TakeFrom(state, itemId, affordable, black);
            if (taken <= 0) return 0;
            save.coins -= unit * taken;
            // Add 返回的是"没装下的余量"，装进去的件数要自己减出来。
            var leftover = InventoryRules.Add(save.bag, items, itemId, taken);
            var added = taken - leftover;
            // 背包塞不下：多收的钱和货都退回货架，不能让玩家白掉钱。
            if (added < taken)
            {
                var returned = taken - added;
                save.coins += unit * returned;
                GiveBack(state, itemId, returned, black);
            }
            state.trades += added;
            state.goodwill += (black ? Trade.GoodwillPerBlackTrade : Trade.GoodwillPerTrade) * added;
            return added;
        }

        // 卖出：出货、收钱、记一笔记忆。返回实际卖出件数。
        public static int Sell(SaveData save, MerchantState state, ItemTable items, MerchantDef merchant, string itemId, int count)
        {
            var item = items.Find(itemId);
            if (save == null || state == null || item == null || count <= 0) return 0;
            if (!Trade.CanSell(item, merchant)) return 0;
            if (InventoryRules.Count(save.bag, itemId) < count) return 0;
            if (!InventoryRules.Remove(save.bag, itemId, count)) return 0;
            save.coins += Trade.SellPrice(item, save.localReputation) * count;
            state.trades += count;
            state.goodwill += Trade.GoodwillPerTrade * count;
            return count;
        }

        // 买入因包满而退货时，把货放回他那儿：只补第一条来源行。
        // 解析已拒绝同一物品出现在多条货架行上，这里若按行循环加会凭空造出库存。
        static void GiveBack(MerchantState state, string itemId, int count, bool black)
        {
            var line = (black ? state.hidden : state.stock).FirstOrDefault(l => l.itemId == itemId);
            if (line != null) line.count += count;
        }
    }
}
