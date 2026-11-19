using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace TwelveJade.Core
{
    public static class AtlasExplorationChecks
    {
        static void Check(string name,bool yes){if(!yes)throw new Exception("FAIL: 舆图 "+name);Console.WriteLine("PASS: 舆图 "+name);}
        public static void Run(TownMap map,ItemTable items,string configDir)
        {
            var save=new SaveData();Check("旧档空探索兼容",AtlasExploration.IsValid(null)&&!AtlasExploration.Known(save,100,100,map.WorldWidth,map.WorldHeight));
            Check("真实脚点首次驱散",AtlasExploration.Reveal(save,map.SpawnX,map.SpawnY,map.WorldWidth,map.WorldHeight));
            Check("起点可见远处仍封",AtlasExploration.Known(save,map.SpawnX,map.SpawnY,map.WorldWidth,map.WorldHeight)&&!AtlasExploration.Known(save,0,0,map.WorldWidth,map.WorldHeight));
            Check("Unity旧档空壳仍为未知",AtlasExploration.IsValid(new AtlasExplorationState{version=0,cells=null})&&!AtlasExploration.IsValid(new AtlasExplorationState{version=0,cells=new[]{1}}));
            var cells=(int[])save.atlasExploration.cells.Clone();Check("重复踏足幂等",!AtlasExploration.Reveal(save,map.SpawnX,map.SpawnY,map.WorldWidth,map.WorldHeight)&&cells.SequenceEqual(save.atlasExploration.cells));
            foreach(float bad in new[]{float.NaN,float.PositiveInfinity,-1f,map.WorldWidth+1})Check("非法脚点拒绝 "+bad,!AtlasExploration.Reveal(save,bad,0,map.WorldWidth,map.WorldHeight));
            Check("地图右下边界合法",AtlasExploration.Cell(map.WorldWidth,map.WorldHeight,map.WorldWidth,map.WorldHeight)==AtlasExploration.CellCount-1);
            foreach(var state in new[]{new AtlasExplorationState{version=2},new AtlasExplorationState{cells=null},new AtlasExplorationState{cells=new[]{-1}},new AtlasExplorationState{cells=new[]{AtlasExploration.CellCount}},new AtlasExplorationState{cells=new[]{1,1}}})
                Check("损坏探索结构拒绝",!AtlasExploration.IsValid(state));
            Check("所有区块边界合法",AtlasExploration.IsValid(new AtlasExplorationState{cells=Enumerable.Range(0,AtlasExploration.CellCount).ToArray()}));
            try{AtlasExploration.Commit(save,()=>AtlasExploration.Reveal(save,500,500,map.WorldWidth,map.WorldHeight),_=>throw new IOException("forced"));throw new Exception("expected write failure");}catch(IOException){}
            Check("探索写失败回滚",cells.SequenceEqual(save.atlasExploration.cells));
            int writes=0;AtlasExploration.Commit(save,()=>AtlasExploration.Reveal(save,500,500,map.WorldWidth,map.WorldHeight),_=>writes++);
            Check("探索成功单次写入",writes==1&&AtlasExploration.Known(save,500,500,map.WorldWidth,map.WorldHeight));
            var fresh=new SaveData();var mark=map.Landmarks.First();
            string json="{\"quests\":[{\"id\":\"atlas-test\",\"title\":\"到访\",\"chapter\":1,\"initialStatus\":\"Active\",\"objectives\":[{\"id\":\"reach-test\",\"type\":\"reach\",\"target\":\""+mark.Id+"\",\"count\":1}],\"rewards\":{\"coins\":0}}]}";
            // Use the production parser and ledger progression, with an actual configured landmark ID.
            var quests=QuestTable.Parse(json);Check("未达成事件不揭图",!AtlasExploration.RevealEarnedEvents(fresh,map,quests));
            fresh.quests=new[]{new QuestState{id="atlas-test",status=QuestLedger.StatusActive,progress=new[]{1}}};
            Check("达成抵达事件揭图",AtlasExploration.RevealEarnedEvents(fresh,map,quests)&&AtlasExploration.Known(fresh,mark.X,mark.Y,map.WorldWidth,map.WorldHeight));
            var root=Path.Combine(Path.GetTempPath(),"atlas-save-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                var repo=new SaveRepository(root,new global::JsonCodec());var one=repo.Create(1,"farmer","行者");var two=repo.Create(2,"farmer","行客");
                one.atlasExploration=save.atlasExploration.Copy();repo.Write(one);
                Check("探索存读往返",repo.Read(1).Data.atlasExploration.cells.SequenceEqual(save.atlasExploration.cells));
                Check("换档不泄露探索",repo.Read(2).Data.atlasExploration==null);
                var path=Path.Combine(root,"slot-2.json");var document=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)).AsObject();document.Remove("atlasExploration");File.WriteAllText(path,document.ToJsonString());
                Check("旧档测试确无探索属性",!File.ReadAllText(path).Contains("atlasExploration"));
                Check("旧v11无探索字段仍可载",repo.Read(2).CanLoad);
                one.atlasExploration=new AtlasExplorationState{cells=new[]{0,0}};bool rejected=false;try{repo.Write(one);}catch(ArgumentException){rejected=true;}
                Check("写入损坏探索拒绝且保留原档",rejected&&repo.Read(1).Data.atlasExploration.cells.SequenceEqual(save.atlasExploration.cells));
            }
            finally{Directory.Delete(root,true);}
            var cultivate=new SaveData{bag=InventoryRules.NewBag()};cultivate.bag[0]=new ItemStack(CultivationRules.ProtectionPill,2);
            try{CultivationRules.AttemptAndCommit(cultivate,0,true,.1,_=>throw new IOException("forced"));throw new Exception("expected failure");}catch(IOException){}
            Check("冲穴写失败恢复穴位与丹药",cultivate.cultivation==null&&InventoryRules.Count(cultivate.bag,CultivationRules.ProtectionPill)==2);
            int commits=0;var good=CultivationRules.AttemptAndCommit(cultivate,0,true,.1,_=>commits++);
            Check("成功冲穴仅结算一次",good.Succeeded&&commits==1&&CultivationRules.Opened(cultivate)==1&&InventoryRules.Count(cultivate.bag,CultivationRules.ProtectionPill)==1);
            var repeat=CultivationRules.AttemptAndCommit(cultivate,0,true,.1,_=>commits++);
            Check("重复旧穴不耗丹不写档",!repeat.Attempted&&commits==1&&InventoryRules.Count(cultivate.bag,CultivationRules.ProtectionPill)==1);
            var fail=CultivationRules.AttemptAndCommit(cultivate,1,false,.99,_=>commits++);
            Check("失败冲穴保留真实失败与保底",fail.Attempted&&!fail.Succeeded&&CultivationRules.Opened(cultivate)==1&&cultivate.cultivation.failures==1&&commits==2);
        }
    }
}
