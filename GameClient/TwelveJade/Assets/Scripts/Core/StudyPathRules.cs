using System;
using System.Linq;
namespace TwelveJade.Core
{
    public enum StudyApproach { Record, Steady, Deduce, Force, Breathe }
    public sealed class StudySessionResult
    {
        public bool Changed;
        public StudyOutcome Outcome=StudyOutcome.Refused;
        public string Heading="暂不能参悟", Detail="";
        public int Progress, Notes, Strain;
    }
    // Player decisions and persisted knowledge are independent of animation duration or framerate.
    public static class StudyPathRules
    {
        public static int Comprehension(SaveData save)=>10+CultivationRules.Opened(save)*3+
            ManualStudyRules.Skills.Count(s=>!s.Basic&&ManualStudyRules.Learned(save,s.Id))*2;
        public static double Risk(SaveData save,string id,StudyApproach approach)
        {
            if(approach!=StudyApproach.Deduce&&approach!=StudyApproach.Force)return 0;
            return Math.Max(0,Math.Min(.55,(ManualStudyRules.Entry(save,id)?.strain??0)*.006+
                (approach==StudyApproach.Force?.12:0)-Comprehension(save)*.002));
        }
        public static double Epiphany(SaveData save,string id,StudyApproach approach)=>
            Math.Min(.3,.05+Comprehension(save)*.004+(ManualStudyRules.Entry(save,id)?.notes??0)*.005+
                (approach==StudyApproach.Force?.05:0));
        public static int Gain(SaveData save,string id,StudyApproach approach)=>
            (approach==StudyApproach.Steady?14:approach==StudyApproach.Deduce?21:30)+
            Comprehension(save)/5+(ManualStudyRules.Entry(save,id)?.notes??0)/4;
        public static StudySessionResult ActAndCommit(SaveData save,ItemTable items,string id,
            StudyApproach approach,double roll,Action<SaveData> commit)
        {
            if(commit==null)throw new ArgumentNullException(nameof(commit));
            var bag=save?.bag?.Select(x=>x==null?null:new ItemStack(x.id,x.count)).ToArray();
            var entries=save?.manualStudies?.Select(x=>x?.Copy()).ToArray();
            try
            {
                var result=Act(save,items,id,approach,roll);
                if(result.Changed)commit(save);
                return result;
            }
            catch {if(save!=null){save.bag=bag;save.manualStudies=entries;}throw;}
        }
        static StudySessionResult Act(SaveData save,ItemTable items,string id,StudyApproach approach,double roll)
        {
            var result=new StudySessionResult();var skill=ManualStudyRules.Find(id);
            if(save==null||skill==null||!ManualStudyRules.IsValid(save.manualStudies)||
                !CultivationRules.IsValid(save.cultivation)||!Enum.IsDefined(typeof(StudyApproach),approach)||
                double.IsNaN(roll)||roll<0||roll>=1){result.Detail="参悟条件无效，未作结算。";return result;}
            var entry=ManualStudyRules.Entry(save,id);
            if(skill.Basic||ManualStudyRules.Learned(save,id))
            {
                result.Outcome=StudyOutcome.Understood;result.Heading="温故知新";
                result.Detail="此法已会 · 温习不耗卷、不叠属性";result.Progress=100;return result;
            }
            if((entry?.attempts??0)>=ManualStudyRules.MaxAttempts)
            {result.Detail="参悟记录已满，未作结算。";return result;}
            if(approach==StudyApproach.Record)
            {
                if(entry?.recorded==true){result.Detail="道藏已有此篇，无须再耗残卷。";return result;}
                var item=items?.Find(skill.ManualId);
                if(item==null||item.Category!="miji"||InventoryRules.Count(save.bag,skill.ManualId)<1)
                {result.Detail="须有对应残卷 · 可向货郎求购";return result;}
                if(!InventoryRules.Remove(save.bag,skill.ManualId,1))return result;
                if(entry==null)
                {
                    entry=new ManualStudyEntry{id=id};
                    save.manualStudies=(save.manualStudies??Array.Empty<ManualStudyEntry>()).Concat(new[]{entry}).ToArray();
                }
                entry.attempts++;entry.recorded=true;entry.progress=Math.Min(40,entry.insights*10);entry.notes=entry.insights;
                result.Outcome=StudyOutcome.Recorded;result.Heading="残卷已拓入道藏";
                result.Detail="耗卷一册 · 此后参悟不再耗卷";
            }
            else
            {
                if(entry?.recorded!=true){result.Detail="先拓录残卷，再与道童参悟。";return result;}
                if(approach==StudyApproach.Breathe)
                {
                    if(entry.strain==0){result.Detail="心神平和，无须反复调息。";return result;}
                    entry.strain=Math.Max(0,entry.strain-40);result.Outcome=StudyOutcome.Restored;
                    result.Heading="抱元守一";result.Detail="心神渐定 · 领会与心得保留";
                }
                else
                {
                    if(entry.strain>=80){result.Detail="心神过载 · 请先调息";return result;}
                    double risk=Risk(save,id,approach),epiphany=Epiphany(save,id,approach);
                    int gain=Gain(save,id,approach);
                    entry.attempts++;
                    if(roll<risk)
                    {
                        entry.progress=Math.Max(0,entry.progress-6);entry.strain=Math.Min(100,entry.strain+35);
                        result.Outcome=StudyOutcome.Deviation;result.Heading="气机岔行 · 暂缓强参";
                        result.Detail="领会 -6（最低0）· 调息可平复";
                    }
                    else
                    {
                        bool insight=roll>=1-epiphany;
                        entry.progress=Math.Min(100,entry.progress+gain+(insight?18:0));
                        entry.notes=Math.Min(12,entry.notes+(approach==StudyApproach.Steady?1:2)+(insight?2:0));
                        entry.strain=Math.Max(0,Math.Min(100,entry.strain+(approach==StudyApproach.Steady?-8:approach==StudyApproach.Deduce?18:30)));
                        entry.learned=entry.progress==100;
                        result.Outcome=entry.learned?StudyOutcome.Understood:insight?StudyOutcome.Insight:StudyOutcome.Progress;
                        result.Heading=entry.learned?"融会「"+skill.Name+"」":insight?"一念顿悟":"推演有所进";
                        result.Detail=entry.learned?skill.Effect:"领会 "+entry.progress+"/100 · 心得 "+entry.notes+"/12";
                    }
                }
            }
            result.Changed=true;result.Progress=entry.progress;result.Notes=entry.notes;result.Strain=entry.strain;return result;
        }
    }
}
