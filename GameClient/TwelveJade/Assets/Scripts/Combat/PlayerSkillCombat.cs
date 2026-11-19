using System;
using System.Collections.Generic;
using TwelveJade.Core;

namespace TwelveJade.Combat
{
    public sealed class PlayerSkillDefinition
    {
        public readonly string Id, Name, Key;
        public readonly float Cooldown;
        public readonly CombatMove Move;
        public PlayerSkillDefinition(string id, string name, string key, float cooldown, CombatMove move)
        { Id=id; Name=name; Key=key; Cooldown=cooldown; Move=move; }
    }
    public sealed class PlayerSkillOutcome
    {
        public string Id;
        public CombatResult Strike;
        public int Healed;
    }
    // Active skills extend the learned manuals, never their existing passive bonuses or study rules.
    public sealed class PlayerSkillCombat
    {
        static readonly PlayerSkillDefinition[] definitions = {
            new PlayerSkillDefinition("sword", "剑气", "U / 4", 5f,
                new CombatMove("learned-sword", "御剑出岫", CombatActionType.Light, 20, .22f, .55f, .24f, 4.4f, 18) { Knockback=.16f }),
            new PlayerSkillDefinition("thunder", "五雷", "I / 5", 9f,
                new CombatMove("learned-thunder", "五雷正法", CombatActionType.Heavy, 32, .62f, .20f, .45f, 6.5f, 30) { Knockback=.20f }),
            new PlayerSkillDefinition("clarity", "清心", "O / 6", 12f,
                new CombatMove("learned-clarity", "清心归元", CombatActionType.Heavy, 0, .45f, .10f, .35f, .1f, 24))
        };
        readonly Dictionary<string,float> readyAt = new Dictionary<string,float>();
        bool pending, attempted;
        float priorProgress;
        public const float SwordRadius=.14f, ThunderRadius=.55f;
        public static IReadOnlyList<PlayerSkillDefinition> Definitions { get; } = Array.AsReadOnly(definitions);
        public static PlayerSkillDefinition Find(string id) { foreach(var d in definitions) if(d.Id==id) return d; return null; }
        public PlayerSkillDefinition Current { get; private set; }
        public bool Pending => pending;
        public float OriginX { get; private set; }
        public float TargetX { get; private set; }
        public int Facing { get; private set; }=1;
        public float StartedAt { get; private set; }
        public int Sequence { get; private set; }
        public int ResolveCount { get; private set; }
        public int LastHeal { get; private set; }
        public float CooldownLeft(string id,float now) => readyAt.TryGetValue(id,out var end) ? Math.Max(0,end-now) : 0;
        public bool Learned(SaveData save,string id) => Find(id)!=null && ManualStudyRules.IsValid(save?.manualStudies) && ManualStudyRules.Learned(save,id);
        public bool OwnsAction(CombatActorState actor) => Current!=null && actor!=null && actor.CurrentMove==Current.Move;
        public float Progress(float now) => Current==null ? 0 : Unit((now-StartedAt-Current.Move.Startup)/Current.Move.Active);
        public float Charge(float now) => Current==null ? 0 : Unit((now-StartedAt)/Current.Move.Startup);
        public float EndX => Current==null ? OriginX : ActionCombatMotor.ClampX(OriginX+Facing*Current.Move.Range);
        public float ProjectileX(float now) => OriginX+(EndX-OriginX)*Progress(now);
        public string Refusal(string id,SaveData save,CombatActorState actor,CombatActorState target,float now,bool allowNoTarget=false)
        {
            var def=Find(id);
            if(def==null)return "未登记此术。";
            if(!Learned(save,id))return "尚未习得"+def.Name+"。";
            if(actor==null||actor.IsDead)return "已倒下，不能施术。";
            if(actor.Phase!=CombatPhase.Idle)return "先收完当前招式。";
            if(CooldownLeft(id,now)>0)return def.Name+"尚在调息。";
            if(actor.Stamina<def.Move.StaminaCost)return "精力不足。";
            if(id=="clarity" && actor.Hp>=actor.MaxHp)return "气血充盈，无需回元。";
            if(!allowNoTarget && id!="clarity" && (target==null||target.IsDead))return "没有可施术的敌人。";
            return "";
        }
        public CombatResult Request(string id,SaveData save,CombatActorState actor,CombatActorState target,float now,int direction,bool allowNoTarget=false)
        {
            string refusal=Refusal(id,save,actor,target,now,allowNoTarget);
            if(refusal.Length>0)return actor==null ? new CombatResult { Reason=refusal } : CombatResult.Refused(actor,refusal);
            var def=Find(id);var result=CombatRules.TryAct(actor,def.Move.Action,now,target,def.Move);
            if(!result.Executed)return result;
            Current=def;OriginX=actor.X;TargetX=target?.X ?? actor.X;Facing=direction<0?-1:1;StartedAt=now;
            readyAt[id]=now+def.Cooldown;pending=true;attempted=false;priorProgress=0;LastHeal=0;Sequence++;
            return result;
        }
        public PlayerSkillOutcome Resolve(CombatActorState actor,CombatActorState target,float now,PartStrike part=null)
        {
            if(!pending||Current==null||actor==null)return null;
            if(actor.IsDead||actor.Phase==CombatPhase.Hitstun||!OwnsAction(actor)) { Cancel(); return null; }
            if(actor.Phase==CombatPhase.Recovery||actor.Phase==CombatPhase.Idle)
            { pending=false;return null; }
            if(actor.Phase!=CombatPhase.Active||attempted)return null;
            if(Current.Id=="clarity")
            {
                attempted=true;ResolveCount++;int before=actor.Hp;
                CombatRules.RestoreHp(actor,actor.Hp+Math.Max(10,(int)Math.Floor(actor.MaxHp*.24f)));
                LastHeal=actor.Hp-before;return new PlayerSkillOutcome{Id=Current.Id,Healed=LastHeal};
            }
            if(target==null||target.IsDead){Cancel();return null;}
            bool inside;
            if(Current.Id=="sword")
            {
                float p=Progress(now),from=OriginX+(EndX-OriginX)*priorProgress,to=OriginX+(EndX-OriginX)*p;priorProgress=p;
                float min=Math.Max(Math.Min(OriginX,EndX),Math.Min(from,to)-SwordRadius);
                float max=Math.Min(Math.Max(OriginX,EndX),Math.Max(from,to)+SwordRadius);
                inside=target.X>=min&&target.X<=max;
                if(!inside)return null;
            }
            else inside=Math.Abs(target.X-TargetX)<=ThunderRadius;
            attempted=true;ResolveCount++;
            CombatResult hit;
            if(inside)hit=CombatRules.ResolvePartStrike(actor,target,now,part??new PartStrike());
            else {hit=CombatResult.Missed(actor,target,"离开了雷印。");hit.Whiffed=true;}
            return new PlayerSkillOutcome{Id=Current.Id,Strike=hit};
        }
        public void Cancel(){pending=false;attempted=true;}
        static float Unit(float x)=>Math.Max(0,Math.Min(1,x));
    }
}
