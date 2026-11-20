using System;
using System.IO;
using System.Text.Json;

namespace TwelveJade.Core
{
    public static class CultivationChecks
    {
        static void Check(string name,bool yes)
        {
            if(!yes)throw new Exception("FAIL: 修炼 "+name);
            Console.WriteLine("PASS: 修炼 "+name);
        }
        // 正式流程不授予入门标记；这里只构造后期能力夹具。
        static SaveData Initiated()=>new SaveData{bag=InventoryRules.NewBag(),
            oneTimeFlags=new[]{"chapter-1-left-qingshi","cultivation-initiation"}};
        static string Snapshot(SaveData save)=>JsonSerializer.Serialize(save,new JsonSerializerOptions{IncludeFields=true});

        public static void Run()
        {
            var cultivate=Initiated();cultivate.bag[0]=new ItemStack(CultivationRules.ProtectionPill,2);
            try{CultivationRules.AttemptAndCommit(cultivate,0,true,.1,_=>throw new IOException("forced"));throw new Exception("expected failure");}catch(IOException){}
            Check("冲穴写失败恢复穴位与丹药",cultivate.cultivation==null&&InventoryRules.Count(cultivate.bag,CultivationRules.ProtectionPill)==2);
            int commits=0;var good=CultivationRules.AttemptAndCommit(cultivate,0,true,.1,_=>commits++);
            Check("成功冲穴仅结算一次",good.Succeeded&&commits==1&&CultivationRules.Opened(cultivate)==1&&InventoryRules.Count(cultivate.bag,CultivationRules.ProtectionPill)==1);
            var repeat=CultivationRules.AttemptAndCommit(cultivate,0,true,.1,_=>commits++);
            Check("重复旧穴不耗丹不写档",!repeat.Attempted&&commits==1&&InventoryRules.Count(cultivate.bag,CultivationRules.ProtectionPill)==1);
            var fail=CultivationRules.AttemptAndCommit(cultivate,1,false,.99,_=>commits++);
            Check("失败冲穴保留真实失败与保底",fail.Attempted&&!fail.Succeeded&&CultivationRules.Opened(cultivate)==1&&cultivate.cultivation.failures==1&&commits==2);

            var pity=Initiated();
            for(int i=0;i<CultivationRules.PityFailures;i++)
                CultivationRules.Attempt(pity,0,false,.999);
            Check("连续失败到达保底且不丢穴位",pity.cultivation.failures==3&&CultivationRules.Chance(pity,false)==1);
            var guaranteed=CultivationRules.Attempt(pity,0,false,.999);
            Check("保底真成功并清失败次数",guaranteed.Succeeded&&pity.cultivation.opened==1&&pity.cultivation.failures==0);

            var complete=Initiated();
            for(int point=0;point<CultivationRules.Points.Count;point++)
                Check("按序冲穴 "+point,CultivationRules.Attempt(complete,point,false,0).Succeeded);
            Check("五穴有效收益仍为气血8精力7",CultivationRules.HpBonus(complete)==8&&CultivationRules.StaminaBonus(complete)==7);
            var before=Snapshot(complete);
            Check("圆满后拒绝重复冲穴",!CultivationRules.Attempt(complete,5,false,0).Attempted&&CultivationRules.Chance(complete,false)==0&&Snapshot(complete)==before);

            var invalid=Initiated();
            foreach(double roll in new[]{double.NaN,-.01,1,double.PositiveInfinity})
            {
                before=Snapshot(invalid);
                Check("非法随机值不改账 "+roll,!CultivationRules.Attempt(invalid,0,false,roll).Attempted&&Snapshot(invalid)==before);
            }
            before=Snapshot(invalid);
            Check("无丹不能凭空护持",!CultivationRules.Attempt(invalid,0,true,0).Attempted&&Snapshot(invalid)==before);
            invalid.cultivation=new CultivationState{attempts=CultivationRules.MaxAttempts};
            before=Snapshot(invalid);
            Check("满账拒绝且不改记录",!CultivationRules.Attempt(invalid,0,false,0).Attempted&&Snapshot(invalid)==before);
        }
    }
}
