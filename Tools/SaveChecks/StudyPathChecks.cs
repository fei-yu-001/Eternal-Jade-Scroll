using System;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace TwelveJade.Core
{
    public static class StudyPathChecks
    {
        static void Check(string label,bool okay){if(!okay)throw new Exception("FAIL: 参悟 "+label);Console.WriteLine("PASS: 参悟 "+label);}
        static SaveData Fresh(string skill="sword")=>new SaveData{bag=new[]{new ItemStack("manual-"+skill+"-fragment",3)},manualStudies=null};
        static string Snapshot(SaveData save)=>JsonSerializer.Serialize(save,new JsonSerializerOptions{IncludeFields=true});
        public static void Run(ItemTable items,string configDir)
        {
            var save=Fresh();int writes=0;
            var before=Snapshot(save);
            var no=StudyPathRules.ActAndCommit(save,items,"sword",StudyApproach.Steady,.5,_=>writes++);
            Check("未拓录不推进不写盘",!no.Changed&&before==Snapshot(save)&&writes==0);
            var record=StudyPathRules.ActAndCommit(save,items,"sword",StudyApproach.Record,.5,_=>writes++);
            Check("首录恰耗一卷且真实记录",record.Changed&&writes==1&&InventoryRules.Count(save.bag,"manual-sword-fragment")==2&&ManualStudyRules.Entry(save,"sword").recorded);
            before=Snapshot(save);StudyPathRules.ActAndCommit(save,items,"sword",StudyApproach.Record,.5,_=>writes++);
            Check("重复拓录拒扣卷",before==Snapshot(save)&&writes==1);
            Check("旧接口不能绕过新道藏重复抽取",!ManualStudyRules.Study(save,items,"sword",0).Attempted&&Snapshot(save)==before);
            var invalids=new[]{double.NaN,-.1,1,double.PositiveInfinity};
            foreach(double roll in invalids){StudyPathRules.ActAndCommit(save,items,"sword",StudyApproach.Force,roll,_=>writes++);Check("非法随机值不动账 "+roll,Snapshot(save)==before);}
            StudyPathRules.ActAndCommit(save,items,"sword",(StudyApproach)99,.5,_=>writes++);Check("未知选择不动账",before==Snapshot(save));
            var missing=Fresh("clarity");before=Snapshot(missing);StudyPathRules.ActAndCommit(missing,items,"sword",StudyApproach.Record,.5,_=>writes++);Check("错种残卷不能拓录",before==Snapshot(missing));
            int steps=0;while(!ManualStudyRules.Learned(save,"sword")&&steps++<10)StudyPathRules.ActAndCommit(save,items,"sword",StudyApproach.Steady,.5,_=>{});
            Check("循章有限步融会不靠随机成功",ManualStudyRules.Learned(save,"sword")&&steps<=7&&ManualStudyRules.Entry(save,"sword").progress==100);
            Check("后续参悟不耗卷且道藏合法",InventoryRules.Count(save.bag,"manual-sword-fragment")==2&&ManualStudyRules.IsValid(save.manualStudies));
            Check("习得真实战斗增益只加一次",ManualStudyRules.AttackBonus(save)==2);
            before=Snapshot(save);StudyPathRules.ActAndCommit(save,items,"sword",StudyApproach.Force,0,_=>throw new Exception("practice must not commit"));Check("已习得复习不写盘不叠属性",before==Snapshot(save));
            var learner=Fresh();StudyPathRules.ActAndCommit(learner,items,"sword",StudyApproach.Record,.5,_=>{});
            var learned=ManualStudyRules.Entry(learner,"sword");
            int steady=StudyPathRules.Gain(learner,"sword",StudyApproach.Steady),deduce=StudyPathRules.Gain(learner,"sword",StudyApproach.Deduce),force=StudyPathRules.Gain(learner,"sword",StudyApproach.Force);
            Check("三种推演真实推进差异",steady<deduce&&deduce<force);
            Check("循章零风险而强参有风险",StudyPathRules.Risk(learner,"sword",StudyApproach.Steady)==0&&StudyPathRules.Risk(learner,"sword",StudyApproach.Force)>0);
            StudyPathRules.ActAndCommit(learner,items,"sword",StudyApproach.Deduce,.5,_=>{});
            Check("推演增加心得与负担",learned.notes==2&&learned.strain==18&&learned.progress==deduce);
            int progress=learned.progress;var deviation=StudyPathRules.ActAndCommit(learner,items,"sword",StudyApproach.Force,0,_=>{});
            Check("走火真损领会且不毁卷",deviation.Outcome==StudyOutcome.Deviation&&learned.progress==progress-6&&learned.strain==53&&InventoryRules.Count(learner.bag,"manual-sword-fragment")==2);
            StudyPathRules.ActAndCommit(learner,items,"sword",StudyApproach.Force,0,_=>{});before=Snapshot(learner);
            StudyPathRules.ActAndCommit(learner,items,"sword",StudyApproach.Force,.5,_=>{});Check("高负担强参被拒且不写账",before==Snapshot(learner));
            int notes=learned.notes;progress=learned.progress;
            StudyPathRules.ActAndCommit(learner,items,"sword",StudyApproach.Breathe,.5,_=>{});
            Check("调息降负担且保留所得",learned.strain==48&&learned.notes==notes&&learned.progress==progress);
            for(int i=0;i<2;i++)StudyPathRules.ActAndCommit(learner,items,"sword",StudyApproach.Breathe,.5,_=>{});
            before=Snapshot(learner);StudyPathRules.ActAndCommit(learner,items,"sword",StudyApproach.Breathe,.5,_=>throw new Exception("empty breath commit"));Check("平静不空刷心得或写盘",before==Snapshot(learner));
            progress=learned.progress;int gain=StudyPathRules.Gain(learner,"sword",StudyApproach.Steady);
            var insight=StudyPathRules.ActAndCommit(learner,items,"sword",StudyApproach.Steady,.999,_=>{});
            Check("顿悟增加真实进度",insight.Outcome==StudyOutcome.Insight&&learned.progress==progress+gain+18);
            var old=Fresh();old.manualStudies=new[]{new ManualStudyEntry{id="sword",attempts=2,insights=2}};
            StudyPathRules.ActAndCommit(old,items,"sword",StudyApproach.Record,.5,_=>{});
            Check("旧偶得带入新领会且不改历史",old.manualStudies[0].insights==2&&old.manualStudies[0].progress==20&&old.manualStudies[0].notes==2&&ManualStudyRules.IsValid(old.manualStudies));
            var corrupt=old.manualStudies[0].Copy();corrupt.progress=101;Check("进度越界拒绝",!ManualStudyRules.IsValid(new[]{corrupt}));
            corrupt=old.manualStudies[0].Copy();corrupt.learned=true;Check("未满进度伪习得拒绝",!ManualStudyRules.IsValid(new[]{corrupt}));
            corrupt=old.manualStudies[0].Copy();corrupt.recorded=false;Check("未拓录伪心得拒绝",!ManualStudyRules.IsValid(new[]{corrupt}));
            foreach(var action in new[]{StudyApproach.Record,StudyApproach.Deduce,StudyApproach.Breathe})
            {
                var failed=Fresh();if(action!=StudyApproach.Record){StudyPathRules.ActAndCommit(failed,items,"sword",StudyApproach.Record,.5,_=>{});StudyPathRules.ActAndCommit(failed,items,"sword",StudyApproach.Deduce,.5,_=>{});}
                before=Snapshot(failed);bool thrown=false;try{StudyPathRules.ActAndCommit(failed,items,"sword",action,.5,_=>throw new IOException("disk test"));}catch(IOException){thrown=true;}
                Check(action+"写盘异常整账回滚",thrown&&before==Snapshot(failed));
            }
            var path=Path.Combine(Path.GetTempPath(),"study-path-"+Guid.NewGuid().ToString("N"));
            try
            {
                var repo=new SaveRepository(path,new Codec()){Items=items};var disk=repo.Create(1,"farmer","参悟客");disk.bag=InventoryRules.NewBag();disk.bag[0]=new ItemStack("manual-thunder-fragment",2);repo.Write(disk);
                StudyPathRules.ActAndCommit(disk,items,"thunder",StudyApproach.Record,.5,repo.Write);
                StudyPathRules.ActAndCommit(disk,items,"thunder",StudyApproach.Deduce,.5,repo.Write);var load=repo.Read(1).Data;
                Check("真存档续读领会心得负担",load.manualStudies[0].recorded&&load.manualStudies[0].progress>0&&load.manualStudies[0].notes==2&&load.manualStudies[0].strain==18&&InventoryRules.Count(load.bag,"manual-thunder-fragment")==1);
                while(!ManualStudyRules.Learned(load,"thunder"))StudyPathRules.ActAndCommit(load,items,"thunder",StudyApproach.Steady,.5,repo.Write);
                Check("续读成功与精力加成持久化",ManualStudyRules.StaminaBonus(repo.Read(1).Data)==4);
            }
            finally{if(Directory.Exists(path))Directory.Delete(path,true);}
            var map=TownMap.Parse(File.ReadAllText(Path.Combine(configDir,"town-map.json")));var fit=new TownAtlasProjection(map.WorldWidth,map.WorldHeight,770,770);
            foreach(var mark in map.Landmarks)
            {
                var p=fit.Project(mark.X,mark.Y);var back=fit.World(p.x,p.y);
                Check("舆图真实坐标往返 "+mark.Id,Math.Abs(back.x-mark.X)<.01&&Math.Abs(back.y-mark.Y)<.01&&p.x>=0&&p.x<=770&&p.y>=0&&p.y<=770);
            }
            var wide=new TownAtlasProjection(8000,4000,770,770);Check("长宽异比等比不拉伸",wide.OffsetY>0&&wide.OffsetX==0&&Math.Abs(wide.Scale-.09625f)<.00001f);
            bool bad=false;try{new TownAtlasProjection(0,1,1,1);}catch(ArgumentOutOfRangeException){bad=true;}Check("非法地图尺寸拒绝",bad);
        }
        sealed class Codec:IJsonCodec
        {
            static readonly JsonSerializerOptions options=new(){IncludeFields=true};
            public string Serialize<T>(T v)=>JsonSerializer.Serialize(v,options);
            public T Deserialize<T>(string json) where T:class=>JsonSerializer.Deserialize<T>(json,options);
        }
    }
}
