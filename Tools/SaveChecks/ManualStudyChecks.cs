using System;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace TwelveJade.Core
{
    public static class ManualStudyChecks
    {
        static void Check(string name,bool okay){if(!okay)throw new Exception("FAIL: 道藏 "+name);Console.WriteLine("PASS: 道藏 "+name);}
        // 后期研读能力夹具：明确已离镇且正式入门，不替青石镇新档授予资格。
        static SaveData Fresh()=>new SaveData{bag=InventoryRules.NewBag(),manualStudies=null,
            oneTimeFlags=new[]{"chapter-1-left-qingshi","cultivation-initiation"}};
        static void Give(SaveData save,string id,int count=3){save.bag[0]=new ItemStack(id,count);}
        public static void Run(ItemTable items,string configDir)
        {
            ManualStudyRules.CrossCheck(items);Check("三残卷与真物品表交叉核验",true);
            var plain=Fresh();Check("旧档空账本无额外属性",ManualStudyRules.IsValid(null)&&ManualStudyRules.AttackBonus(plain)==0&&ManualStudyRules.HpBonus(plain)==0&&ManualStudyRules.StaminaBonus(plain)==0);
            foreach(string id in new[]{"light","heavy","dodge"})
            {
                foreach(double roll in new[]{.1,.75,.99})
                {
                    var before=JsonSerializer.Serialize(plain,new JsonSerializerOptions{IncludeFields=true});
                    var result=ManualStudyRules.Study(plain,items,id,roll);
                    Check(id+"温习"+roll+"有反馈且不耗物不造进度",result.Attempted&&result.Practice&&!result.Consumed&&!result.NewlyLearned&&
                        result.Outcome==(roll<.65?StudyOutcome.Understood:roll<.85?StudyOutcome.Insight:StudyOutcome.Unresolved)&&
                        before==JsonSerializer.Serialize(plain,new JsonSerializerOptions{IncludeFields=true}));
                }
            }
            foreach(var skill in ManualStudyRules.Skills.Where(s=>!s.Basic))
            {
                var save=Fresh();
                Check(skill.Name+"缺卷拒绝且不建账",!ManualStudyRules.Study(save,items,skill.Id,0).Attempted&&save.manualStudies==null);
                Give(save,"ganliang");Check(skill.Name+"不得消耗不对应物品",!ManualStudyRules.Study(save,items,skill.Id,0).Attempted&&InventoryRules.Count(save.bag,"ganliang")==3);
                Give(save,skill.ManualId);double chance=ManualStudyRules.Chance(save,skill.Id);
                var fail=ManualStudyRules.Study(save,items,skill.Id,.9999);
                Check(skill.Name+"未透扣一卷而不授法",fail.Attempted&&fail.Outcome==StudyOutcome.Unresolved&&fail.Consumed&&!ManualStudyRules.Learned(save,skill.Id)&&InventoryRules.Count(save.bag,skill.ManualId)==2);
                var insight=ManualStudyRules.Study(save,items,skill.Id,chance+.01);
                Check(skill.Name+"偶得落账并增下次概率",insight.Outcome==StudyOutcome.Insight&&ManualStudyRules.Entry(save,skill.Id).insights==1&&Math.Abs(ManualStudyRules.Chance(save,skill.Id)-(chance+.05))<.000001&&InventoryRules.Count(save.bag,skill.ManualId)==1);
                var success=ManualStudyRules.Study(save,items,skill.Id,0);
                Check(skill.Name+"领悟扣最后一卷并真落技能",success.NewlyLearned&&success.Outcome==StudyOutcome.Understood&&ManualStudyRules.Learned(save,skill.Id)&&InventoryRules.Count(save.bag,skill.ManualId)==0&&ManualStudyRules.IsValid(save.manualStudies));
                var practice=ManualStudyRules.Study(save,items,skill.Id,0);
                Check(skill.Name+"已学再温习不重复奖励",practice.Practice&&!practice.Consumed&&!practice.NewlyLearned&&ManualStudyRules.Entry(save,skill.Id).attempts==3);
            }
            var invalid=Fresh();Give(invalid,"manual-sword-fragment");
            foreach(double roll in new[]{double.NaN,-.01,1,double.PositiveInfinity})
                Check("非法随机值拒绝 "+roll,!ManualStudyRules.Study(invalid,items,"sword",roll).Attempted&&InventoryRules.Count(invalid.bag,"manual-sword-fragment")==3&&invalid.manualStudies==null);
            Check("未知技能拒绝",!ManualStudyRules.Study(invalid,items,"made-up",0).Attempted);
            var learned=Fresh();
            foreach(var skill in ManualStudyRules.Skills.Where(s=>!s.Basic)){Give(learned,skill.ManualId);ManualStudyRules.Study(learned,items,skill.Id,0);}
            Check("三篇真实收益为攻击2气血5精力4",ManualStudyRules.AttackBonus(learned)==2&&ManualStudyRules.HpBonus(learned)==5&&ManualStudyRules.StaminaBonus(learned)==4);
            var capped=Fresh();capped.cultivation=new CultivationState{opened=5,attempts=5};
            capped.manualStudies=new[]{new ManualStudyEntry{id="clarity",attempts=4,insights=4}};
            Check("经脉偶得加成不越95%",Math.Abs(ManualStudyRules.Chance(capped,"clarity")-.95)<.000001);
            Give(capped,"manual-clarity-fragment",1);var cappedInsight=ManualStudyRules.Study(capped,items,"clarity",.99);
            Check("上限偶得不冒称再次涨概率",cappedInsight.Outcome==StudyOutcome.Insight&&cappedInsight.Detail.Contains("上限")&&ManualStudyRules.Entry(capped,"clarity").insights==4);
            Check("重复账行拒绝",!ManualStudyRules.IsValid(new[]{new ManualStudyEntry{id="sword",attempts=1},new ManualStudyEntry{id="sword",attempts=1}}));
            Check("不接受基础招式伪进度",!ManualStudyRules.IsValid(new[]{new ManualStudyEntry{id="light",attempts=1,learned=true}}));
            Check("不接受无成功次数的习得",!ManualStudyRules.IsValid(new[]{new ManualStudyEntry{id="sword",attempts=0,learned=true}}));
            Check("不接受多于尝试次数的偶得",!ManualStudyRules.IsValid(new[]{new ManualStudyEntry{id="sword",attempts=1,insights=1,learned=true}}));
            Check("不接受负数或空行",!ManualStudyRules.IsValid(new[]{new ManualStudyEntry{id="sword",attempts=1,insights=-1}})&&!ManualStudyRules.IsValid(new ManualStudyEntry[]{null}));
            invalid.manualStudies=new[]{new ManualStudyEntry{id="sword",attempts=ManualStudyRules.MaxAttempts}};
            Check("满账拒绝不扣残卷",!ManualStudyRules.Study(invalid,items,"sword",0).Attempted&&InventoryRules.Count(invalid.bag,"manual-sword-fragment")==3);
            var rollback=Fresh();Give(rollback,"manual-sword-fragment",1);bool threw=false;
            try{ManualStudyRules.StudyAndCommit(rollback,items,"sword",0,_=>throw new IOException("test disk unavailable"));}catch(IOException){threw=true;}
            Check("写盘失败原子恢复残卷和学习记录",threw&&InventoryRules.Count(rollback.bag,"manual-sword-fragment")==1&&rollback.manualStudies==null);
            int writes=0;ManualStudyRules.StudyAndCommit(rollback,items,"light",0,_=>writes++);
            Check("无变更温习不写档",writes==0);
            ManualStudyRules.StudyAndCommit(rollback,items,"sword",0,_=>writes++);
            Check("成功研读只提交一次",writes==1&&ManualStudyRules.Learned(rollback,"sword"));
            // 残卷交易能力使用独立后期商人，青石镇正式货郎不再出售功法。
            var fixture=MerchantTable.Parse("{\"merchants\":[{\"id\":\"study-fixture\",\"name\":\"研读夹具\",\"title\":\"后期货架\",\"greeting\":\"试卷\","+
                "\"buys\":[\"chishi\",\"miji\"],\"stock\":[{\"itemId\":\"ganliang\",\"count\":12},{\"itemId\":\"manual-sword-fragment\",\"count\":4}],"+
                "\"hidden\":[],\"memory\":[{\"at\":0,\"line\":\"试卷\"}]}]}");
            fixture.CrossCheck(items);var merchant=fixture.Find("study-fixture");
            var traded=Fresh();traded.coins=1000;
            traded.merchants=new[]{new MerchantState{id="study-fixture",stock=new[]{new StockLine{itemId="ganliang",count=0}},hidden=Array.Empty<StockLine>()}};
            var state=MerchantLedger.Of(traded,merchant);
            Check("旧档货架补新增残卷但不补已售空干粮",MerchantLedger.Left(state,state.stock,"manual-sword-fragment")==4&&MerchantLedger.Left(state,state.stock,"ganliang")==0);
            int paid=Trade.BuyPrice(items.Find("manual-sword-fragment"),0);
            int bought=MerchantLedger.Buy(traded,state,items,"manual-sword-fragment",1,false);
            Check("真实买残卷扣钱入包且减货架",bought==1&&traded.coins==1000-paid&&InventoryRules.Count(traded.bag,"manual-sword-fragment")==1&&MerchantLedger.Left(state,state.stock,"manual-sword-fragment")==3);
            MerchantLedger.Of(traded,merchant);Check("重开货架不刷回残卷",MerchantLedger.Left(state,state.stock,"manual-sword-fragment")==3);
            var full=Fresh();full.coins=1000;for(int i=0;i<full.bag.Length;i++)full.bag[i]=new ItemStack("cudao",1);
            var fullStock=MerchantLedger.Of(full,merchant);int oldStock=MerchantLedger.Left(fullStock,fullStock.stock,"manual-sword-fragment");
            Check("满包买卷不丢钱不丢货",MerchantLedger.Buy(full,fullStock,items,"manual-sword-fragment",1,false)==0&&full.coins==1000&&MerchantLedger.Left(fullStock,fullStock.stock,"manual-sword-fragment")==oldStock);
            var path=Path.Combine(Path.GetTempPath(),"twelve-jade-manual-checks-"+Guid.NewGuid().ToString("N"));
            try
            {
                var repo=new SaveRepository(path,new Codec()){Items=items};var disk=repo.Create(1,"farmer","悟道客");
                Check("缺新字段旧档正常读",repo.Read(1).CanLoad&&repo.Read(1).Data.manualStudies==null);
                disk.MarkFlag("chapter-1-left-qingshi");disk.MarkFlag("cultivation-initiation");
                Give(disk,"manual-sword-fragment",2);repo.Write(disk);
                ManualStudyRules.StudyAndCommit(disk,items,"sword",.7,repo.Write);
                var read=repo.Read(1).Data;Check("偶得与消耗真实读写往返",ManualStudyRules.Entry(read,"sword").insights==1&&InventoryRules.Count(read.bag,"manual-sword-fragment")==1);
                ManualStudyRules.StudyAndCommit(read,items,"sword",0,repo.Write);read=repo.Read(1).Data;
                Check("习得状态真实读写往返",ManualStudyRules.Learned(read,"sword")&&ManualStudyRules.AttackBonus(read)==2&&InventoryRules.Count(read.bag,"manual-sword-fragment")==0);
                read.manualStudies[0].attempts=-1;bool rejected=false;try{repo.Write(read);}catch(ArgumentException){rejected=true;}
                Check("脏学习账拒写且旧盘完整",rejected&&ManualStudyRules.Learned(repo.Read(1).Data,"sword"));
            }
            finally{if(Directory.Exists(path))Directory.Delete(path,true);}
        }
        sealed class Codec:IJsonCodec
        {
            static readonly JsonSerializerOptions options=new(){IncludeFields=true};
            public string Serialize<T>(T value)=>JsonSerializer.Serialize(value,options);
            public T Deserialize<T>(string json) where T:class=>JsonSerializer.Deserialize<T>(json,options);
        }
    }
}
