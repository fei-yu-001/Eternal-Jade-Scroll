using System;
using System.Collections.Generic;
using System.Linq;
namespace TwelveJade.Core
{
    [Serializable]
    public sealed class ManualStudyEntry
    {
        public string id="";
        public int attempts, insights;
        public bool learned;
        // Additive study notebook; missing fields retain the legacy Phase 2 ledger.
        public bool recorded;
        public int progress, notes, strain;
        public ManualStudyEntry Copy()=>new ManualStudyEntry{id=id,attempts=attempts,insights=insights,learned=learned,recorded=recorded,progress=progress,notes=notes,strain=strain};
    }
    public enum StudyOutcome { Refused, Understood, Insight, Unresolved, Progress, Deviation, Restored, Recorded }
    public sealed class ArtsSkill
    {
        public readonly string Id,Name,ManualId,Verse,Key,Effect;
        public readonly CombatActionType Action;
        public readonly double BaseChance;
        public readonly int Hp,Attack,Stamina;
        public bool Basic=>string.IsNullOrEmpty(ManualId);
        public ArtsSkill(string id,string name,string manual,string verse,string key,CombatActionType action,
            double chance,string effect,int hp=0,int attack=0,int stamina=0)
        {Id=id;Name=name;ManualId=manual;Verse=verse;Key=key;Action=action;BaseChance=chance;Effect=effect;Hp=hp;Attack=attack;Stamina=stamina;}
    }
    public sealed class ManualStudyResult
    {
        public bool Attempted,Consumed,NewlyLearned,Practice;
        public StudyOutcome Outcome;
        public double Chance;
        public string Heading="",Detail="";
    }
    public static class ManualStudyRules
    {
        public const int MaxAttempts=10000,MaxInsight=4;
        static readonly IReadOnlyList<ArtsSkill> skills=Array.AsReadOnly(new[]{
            new ArtsSkill("light","轻击","","顺势出手，轻灵试探。","J / 1",CombatActionType.Light,.65,"精力消耗由战斗规则结算"),
            new ArtsSkill("heavy","重击","","蓄力破势，待隙而进。","K / 2",CombatActionType.Heavy,.65,"精力消耗由战斗规则结算"),
            new ArtsSkill("dodge","闪避","","移步避锋，气定神闲。","L / 空格",CombatActionType.Dodge,.65,"精力消耗由战斗规则结算"),
            new ArtsSkill("sword","御剑","manual-sword-fragment","意随剑行，先修其基。","",CombatActionType.Wait,.65,"习得剑意 · 基础攻击 +2",attack:2),
            new ArtsSkill("thunder","五雷","manual-thunder-fragment","五气朝元，先养其息。","",CombatActionType.Wait,.55,"习得雷息 · 精力上限 +4",stamina:4),
            new ArtsSkill("clarity","清心","manual-clarity-fragment","抱元守一，清心养性。","",CombatActionType.Wait,.75,"习得心诀 · 气血上限 +5",hp:5)
        });
        public static IReadOnlyList<ArtsSkill> Skills=>skills;
        public static ArtsSkill Find(string id)=>skills.FirstOrDefault(s=>s.Id==id);
        public static ManualStudyEntry Entry(SaveData save,string id)=>save?.manualStudies?.FirstOrDefault(e=>e!=null&&e.id==id);
        public static bool Learned(SaveData save,string id)=>Find(id)?.Basic==true||
            PrologueRules.CanCultivate(save)&&Entry(save,id)?.learned==true;
        public static bool IsValid(ManualStudyEntry[] entries)
        {
            if(entries==null)return true;
            if(entries.Length>3)return false;
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in entries)
            {
                var skill=entry==null?null:Find(entry.id);
                if(skill==null||skill.Basic||!seen.Add(entry.id)||entry.attempts<1||entry.attempts>MaxAttempts||
                    entry.insights<0||entry.insights>MaxInsight||entry.insights>entry.attempts-(entry.learned?1:0))return false;
                if(entry.progress<0||entry.progress>100||entry.notes<0||entry.notes>12||entry.strain<0||entry.strain>100)return false;
                if(!entry.recorded&&(entry.progress!=0||entry.notes!=0||entry.strain!=0))return false;
                if(entry.recorded&&entry.learned&&entry.progress!=100)return false;
                if(entry.recorded&&!entry.learned&&entry.progress==100)return false;
            }
            return true;
        }
        public static void CrossCheck(ItemTable table)
        {
            foreach(var skill in skills.Where(s=>!s.Basic))
            {
                var item=table?.Find(skill.ManualId);
                if(item==null||item.Category!="miji"||item.Stack<1||PlotItemRules.IsPlotItem(item))
                    throw new FormatException("道藏残卷未正确登记："+skill.ManualId);
            }
        }
        public static double Chance(SaveData save,string id)
        {
            var skill=Find(id);if(!PrologueRules.CanCultivate(save)||skill==null||!IsValid(save.manualStudies))return 0;
            if(skill.Basic||Learned(save,id))return .65;
            return Math.Min(.95,skill.BaseChance+CultivationRules.Opened(save)*.03+(Entry(save,id)?.insights??0)*.05);
        }
        // Refusal is pure. A new manual attempt consumes exactly one matching item even on failure.
        // The caller commits the bag and ledger together; animation never grants skill state.
        public static ManualStudyResult Study(SaveData save,ItemTable table,string id,double roll)
        {
            var result=new ManualStudyResult();var skill=Find(id);
            if(!PrologueRules.CanCultivate(save))
            {result.Heading="尚未入门";result.Detail=PrologueRules.Refusal;return result;}
            if(save==null||skill==null||!IsValid(save.manualStudies)||!CultivationRules.IsValid(save.cultivation)||
                double.IsNaN(roll)||roll<0||roll>=1)
            {result.Heading="暂不能研读";result.Detail="行卷或研读条件无效。";return result;}
            if(Entry(save,id)?.recorded==true&&!Learned(save,id))
            {result.Heading="请续读已录道藏";result.Detail="此篇已拓录，不再重复耗卷。";return result;}
            bool practice=skill.Basic||Learned(save,id);result.Practice=practice;result.Chance=Chance(save,id);
            if(!practice)
            {
                var item=table?.Find(skill.ManualId);
                if(item==null||item.Category!="miji"||InventoryRules.Count(save.bag,skill.ManualId)<1)
                {result.Heading="古卷待寻";result.Detail="须先取得「"+skill.Name+"残卷」。";return result;}
                if((Entry(save,id)?.attempts??0)>=MaxAttempts)
                {result.Heading="研读记录已满";result.Detail="本次未消耗残卷。";return result;}
                if(!InventoryRules.Remove(save.bag,skill.ManualId,1))
                {result.Heading="未能取出残卷";result.Detail="本次未作研读结算。";return result;}
                result.Consumed=true;
            }
            result.Attempted=true;
            result.Outcome=roll<result.Chance?StudyOutcome.Understood:roll<Math.Min(1,result.Chance+.2)?StudyOutcome.Insight:StudyOutcome.Unresolved;
            if(!practice)
            {
                var entry=Entry(save,id);
                if(entry==null)
                {
                    entry=new ManualStudyEntry{id=id};save.manualStudies=(save.manualStudies??Array.Empty<ManualStudyEntry>()).Concat(new[]{entry}).ToArray();
                }
                entry.attempts++;
                if(result.Outcome==StudyOutcome.Understood){entry.learned=true;result.NewlyLearned=true;}
                else if(result.Outcome==StudyOutcome.Insight)entry.insights=Math.Min(MaxInsight,entry.insights+1);
            }
            if(result.Outcome==StudyOutcome.Understood)
            {result.Heading=practice?"温故知新":"领悟「"+skill.Name+"」";result.Detail=practice?"心神澄明 · 未耗残卷":skill.Effect;}
            else if(result.Outcome==StudyOutcome.Insight)
            {result.Heading="偶有所得";result.Detail=practice?"一念通明 · 可再温习":Chance(save,id)>result.Chance?"残卷已研 · 下次领悟率提升":"残卷已研 · 悟意已至上限";}
            else
            {result.Heading="参悟未透";result.Detail=practice?"暂敛心神 · 未耗残卷":"残卷已研 · 此法尚未习得";}
            return result;
        }
        public static ManualStudyResult StudyAndCommit(SaveData save,ItemTable table,string id,double roll,Action<SaveData> commit)
        {
            if(commit==null)throw new ArgumentNullException(nameof(commit));
            var bag=save?.bag?.Select(s=>s==null?null:new ItemStack(s.id,s.count)).ToArray();
            var studies=save?.manualStudies?.Select(s=>s?.Copy()).ToArray();
            var result=Study(save,table,id,roll);
            if(!result.Consumed)return result;
            try{commit(save);return result;}
            catch{save.bag=bag;save.manualStudies=studies;throw;}
        }
        public static int HpBonus(SaveData save)=>skills.Where(s=>!s.Basic&&Learned(save,s.Id)).Sum(s=>s.Hp);
        public static int AttackBonus(SaveData save)=>skills.Where(s=>!s.Basic&&Learned(save,s.Id)).Sum(s=>s.Attack);
        public static int StaminaBonus(SaveData save)=>skills.Where(s=>!s.Basic&&Learned(save,s.Id)).Sum(s=>s.Stamina);
    }
}
