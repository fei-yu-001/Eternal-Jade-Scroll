using System;
namespace TwelveJade.Core
{
    // Additive starter-meridian ledger. No invented realm, age, spirit currency or tribulation tier.
    [Serializable]
    public sealed class CultivationState
    {
        public int opened;
        public int failures;
        public int attempts;
        public CultivationState Copy() => new CultivationState { opened = opened, failures = failures, attempts = attempts };
    }
    public sealed class MeridianPoint
    {
        public readonly string Id, Name, Verse;
        public readonly int Hp, Stamina;
        public MeridianPoint(string id,string name,string verse,int hp,int stamina)
        { Id=id;Name=name;Verse=verse;Hp=hp;Stamina=stamina; }
    }
    public sealed class BreakthroughResult
    {
        public bool Attempted, Succeeded, Protected;
        public double Chance;
        public string Message = "";
    }
    public static class CultivationRules
    {
        public const string ProtectionPill = "elixir-warm";
        public const int PityFailures = 3, MaxAttempts = 100000;
        static readonly MeridianPoint[] points = {
            new MeridianPoint("lianquan", "廉泉", "舌抵上腭，收神敛息。", 2, 0),
            new MeridianPoint("xuanji", "璇玑", "气行胸臆，吐故纳新。", 0, 2),
            new MeridianPoint("yutang", "玉堂", "心息相依，神清气定。", 3, 0),
            new MeridianPoint("shenque", "神阙", "意守丹田，静养元气。", 0, 3),
            new MeridianPoint("huiyin", "会阴", "五穴相连，初成周天。", 3, 2)
        };
        public static System.Collections.Generic.IReadOnlyList<MeridianPoint> Points => points;
        public static bool IsValid(CultivationState state) => state == null ||
            (state.opened >= 0 && state.opened <= points.Length && state.failures >= 0 && state.failures <= PityFailures &&
             state.attempts >= state.opened + state.failures && state.attempts <= MaxAttempts &&
             (state.opened < points.Length || state.failures == 0));
        public static int Opened(SaveData save) => IsValid(save?.cultivation) ? save?.cultivation?.opened ?? 0 : 0;
        public static int HpBonus(SaveData save) { if(!PrologueRules.CanCultivate(save))return 0;int value=0;for(int i=0;i<Opened(save);i++)value+=points[i].Hp;return value; }
        public static int StaminaBonus(SaveData save) { if(!PrologueRules.CanCultivate(save))return 0;int value=0;for(int i=0;i<Opened(save);i++)value+=points[i].Stamina;return value; }
        public static double Chance(SaveData save, bool pill)
        {
            var state=save?.cultivation;
            if(!PrologueRules.CanCultivate(save) || !IsValid(state) || Opened(save)>=points.Length) return 0;
            if((state?.failures??0)>=PityFailures)return 1;
            return Math.Min(.95, .85 - Opened(save)*.08 + (pill ? .15 : 0));
        }
        // The visual result may start only after a successful durable write; failed writes restore both ledgers.
        public static BreakthroughResult AttemptAndCommit(SaveData save,int point,bool pill,double roll,Action<SaveData> write)
        {
            if(save==null||write==null)throw new ArgumentNullException();
            var old=save.cultivation?.Copy();var bag=save.bag==null?null:Array.ConvertAll(save.bag,s=>s==null?null:new ItemStack(s.id,s.count));
            try{var result=Attempt(save,point,pill,roll);if(result.Attempted)write(save);return result;}
            catch{save.cultivation=old;save.bag=bag;throw;}
        }
        // Pass a [0,1) roll from the caller for deterministic tests; only a valid attempt mutates save/bag.
        public static BreakthroughResult Attempt(SaveData save, int point, bool pill, double roll)
        {
            var result=new BreakthroughResult();
            if(!PrologueRules.CanCultivate(save)){result.Message=PrologueRules.Refusal;return result;}
            if(save==null || !IsValid(save.cultivation) || double.IsNaN(roll) || roll<0 || roll>=1)
            { result.Message="冲穴条件无效，未作结算。";return result; }
            int opened=Opened(save);
            if(point!=opened || opened>=points.Length) {result.Message="请循序冲开下一穴。";return result;}
            if((save.cultivation?.attempts??0)>=MaxAttempts){result.Message="本卷冲穴记录已满。";return result;}
            if(pill && InventoryRules.Count(save.bag,ProtectionPill)<1) {result.Message="行囊中没有暖阳丹。";return result;}
            result.Chance=Chance(save,pill);
            if(pill && !InventoryRules.Remove(save.bag,ProtectionPill,1)){result.Message="暖阳丹未能取出，本次未冲穴。";return result;}
            save.cultivation ??= new CultivationState();
            var state=save.cultivation;state.attempts++;
            result.Attempted=true;result.Succeeded=roll<result.Chance;result.Protected=pill;
            if(result.Succeeded)
            {
                state.opened++;state.failures=0;
                result.Message=points[opened].Name+" · 冲穴功成\n"+
                    (points[opened].Hp>0?"气血上限 +"+points[opened].Hp:"精力上限 +"+points[opened].Stamina);
                if(points[opened].Hp>0&&points[opened].Stamina>0)result.Message+=" · 精力上限 +"+points[opened].Stamina;
            }
            else
            {
                state.failures=Math.Min(PityFailures,state.failures+1);
                result.Message=(pill?"丹气护持 · 冲穴未成":"气息未贯 · 冲穴受阻")+"\n未损已有穴位 · 再试保底 "+state.failures+"/"+PityFailures;
            }
            return result;
        }
    }
}
