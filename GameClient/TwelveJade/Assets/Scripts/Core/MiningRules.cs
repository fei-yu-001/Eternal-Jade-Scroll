using System;
using System.Linq;
namespace TwelveJade.Core
{
    [Serializable]
    public sealed class MiningEntry
    {
        public string nodeId;
        public int day;
    }
    public static class MiningRules
    {
        public const int MaxNodes=128;
        public static bool IsValid(MiningEntry[] entries)
        {
            if(entries==null)return true;
            return entries.Length<=MaxNodes && entries.All(e=>e!=null&&!string.IsNullOrWhiteSpace(e.nodeId)&&e.nodeId.Length<=48&&e.day>=WorldTime.StartDay&&e.day<=WorldTime.MaxDays)
                &&entries.Select(e=>e.nodeId).Distinct(StringComparer.Ordinal).Count()==entries.Length;
        }
        public static bool Available(SaveData save,string id)=>save!=null && !(save.mining??Array.Empty<MiningEntry>()).Any(e=>e.nodeId==id&&e.day>=save.worldDay);
        // A complete deposit is granted once per game day. Full bags and disk failures consume nothing.
        public static bool Harvest(SaveData save,ItemTable items,MineNode node,Action<SaveData> write,out string message)
        {
            if(save==null||items==null||node==null||write==null)throw new ArgumentNullException();
            if(!IsValid(save.mining))throw new ArgumentException("Invalid mining ledger");
            if(!Available(save,node.Id)){message="这处矿脉今日已经采尽，明日再来。";return false;}
            if(items.Find(node.Item)==null||node.Count<1||node.Count>10)throw new ArgumentException("Unknown mining yield");
            var previous=save.mining; var oldBag=save.bag;
            var nextBag=InventoryRules.Normalize(save.bag,items);
            if(InventoryRules.Add(nextBag,items,node.Item,node.Count)>0){message="行囊空间不足，矿脉仍然保留。";return false;}
            var next=(previous??Array.Empty<MiningEntry>()).Where(e=>e.nodeId!=node.Id).Select(e=>new MiningEntry{nodeId=e.nodeId,day=e.day}).ToList();
            if(next.Count>=MaxNodes){message="采集记录已满。";return false;}
            next.Add(new MiningEntry{nodeId=node.Id,day=save.worldDay});
            save.bag=nextBag;save.mining=next.ToArray();
            try{write(save);}catch{save.bag=oldBag;save.mining=previous;throw;}
            message="采得 "+items.Find(node.Item).Name+" × "+node.Count;return true;
        }
    }
}
