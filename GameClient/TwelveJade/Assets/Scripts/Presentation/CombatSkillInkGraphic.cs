using TwelveJade.Combat;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Always-available trajectories and targeting geometry. Authored sheets complement these true gates.
    [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
    public sealed class CombatSkillInkGraphic:MaskableGraphic
    {
        PlayerSkillCombat skill;CombatPhase phase;float now,center,unit,ground,playerX;bool low;
        protected override void Awake(){base.Awake();raycastTarget=false;}
        public void Sample(PlayerSkillCombat value,CombatActorState actor,float clock,bool reduced,float cx,float scale,float y)
        {skill=value;phase=actor?.Phase??CombatPhase.Dead;playerX=actor?.X??0;now=clock;low=reduced;center=cx;unit=scale;ground=y;SetVerticesDirty();}
        Vector2 P(float x,float y)=>new Vector2(center+x*unit,ground+y);
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();if(skill?.Current==null)return;
            bool active=phase==CombatPhase.Active,charge=phase==CombatPhase.Startup;if(!active&&!charge)return;
            var jade=new Color(.44f,.94f,.88f,.85f);var gold=new Color(1,.91f,.58f,.9f);
            if(skill.Current.Id=="sword")
            {
                float x=charge?skill.OriginX:skill.ProjectileX(now);
                var at=P(x,160);var tail=at+Vector2.left*skill.Facing*(low?60:100);
                Line(v,tail,at,low?7:14,new Color(.3f,.88f,.91f,.28f));Line(v,tail,at,3,jade);
                Line(v,at-Vector2.right*skill.Facing*15-Vector2.up*10,at+Vector2.right*skill.Facing*16,3,gold);
                Line(v,at-Vector2.right*skill.Facing*15+Vector2.up*10,at+Vector2.right*skill.Facing*16,3,gold);
            }
            else if(skill.Current.Id=="thunder")
            {
                var foot=P(skill.TargetX,2);Ring(v,foot,PlayerSkillCombat.ThunderRadius*unit,19,3,jade);
                if(charge){Line(v,foot+Vector2.left*10,foot+Vector2.right*10,3,gold);Line(v,foot+Vector2.down*10,foot+Vector2.up*10,3,gold);}
                if(active)for(int bolt=0;bolt<5;bolt++)
                {
                    float offset=(bolt-2)*19;
                    Vector2 a=foot+new Vector2(offset,330),b=foot+new Vector2(offset-14,235),c=foot+new Vector2(offset+12,225),d=foot+new Vector2(offset-4,90);
                    Line(v,a,b,low?3:6,jade);Line(v,b,c,3,gold);Line(v,c,d,low?3:6,jade);Line(v,d,foot+new Vector2(offset*.3f,8),3,gold);
                }
            }
            else
            {
                float a=charge ? .35f+.5f*skill.Charge(now) : .9f;
                Ring(v,P(playerX,7),105,20,4,new Color(.5f,.94f,.68f,a));Ring(v,P(playerX,150),105,145,3,new Color(.59f,.96f,.74f,a*.7f));
                if(active){Line(v,P(playerX,125),P(playerX,179),6,jade);Line(v,P(playerX-.12f,152),P(playerX+.12f,152),6,jade);}
            }
        }
        void Ring(VertexHelper v,Vector2 at,float x,float y,float width,Color tint)
        {for(int i=0;i<48;i++){float a=i*Mathf.PI/24,b=(i+1)*Mathf.PI/24;Line(v,at+new Vector2(Mathf.Cos(a)*x,Mathf.Sin(a)*y),at+new Vector2(Mathf.Cos(b)*x,Mathf.Sin(b)*y),width,tint);}}
        void Line(VertexHelper v,Vector2 a,Vector2 b,float w,Color c)
        {var d=b-a;if(d.sqrMagnitude<.0001f)return;var n=new Vector2(-d.y,d.x).normalized*w*.5f;int k=v.currentVertCount;v.AddVert(a-n,c*color,Vector2.zero);v.AddVert(a+n,c*color,Vector2.zero);v.AddVert(b+n,c*color,Vector2.zero);v.AddVert(b-n,c*color,Vector2.zero);v.AddTriangle(k,k+1,k+2);v.AddTriangle(k,k+2,k+3);}
        protected override void OnDisable(){skill=null;base.OnDisable();}
    }
}
