using System.Collections.Generic;
using TwelveJade.Combat;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    public sealed class CombatSkillEffects:MonoBehaviour
    {
        readonly Dictionary<string,Texture2D> sheets=new Dictionary<string,Texture2D>();
        RawImage castImage,impactImage;
        CombatSkillInkGraphic ink;
        float center,unit,ground,impactAt=-10;
        string impactId="";
        Vector2 impactFoot;
        public bool HasSheet(string id)=>sheets.TryGetValue(id,out var t)&&t!=null;
        public void Configure(UiKit ui,RectTransform root,float centerX,float pxPerUnit,float groundY)
        {
            center=centerX;unit=pxPerUnit;ground=-groundY;
            foreach(var def in PlayerSkillCombat.Definitions)
            {
                var texture=Resources.Load<Texture2D>("Art/Fx/Nov11/skill-"+def.Id);
                if(texture!=null&&texture.width!=texture.height*4)
                {Debug.LogError("[CombatSkillEffects] Expected four square cells in one row: "+texture.name);texture=null;}
                sheets[def.Id]=texture;
            }
            ink=ui.Rect(root,"Skill trajectory and locked mark",0,0,1920,1080).gameObject.AddComponent<CombatSkillInkGraphic>();
            castImage=Image(ui,root,"Skill authored cast phase");impactImage=Image(ui,root,"Skill authored actual impact");
        }
        static RawImage Image(UiKit ui,Transform parent,string name)
        {
            var rect=ui.Rect(parent,name,0,0,300,300);rect.pivot=new Vector2(.5f,.5f);
            var image=rect.gameObject.AddComponent<RawImage>();image.raycastTarget=false;image.enabled=false;return image;
        }
        public void Impact(PlayerSkillOutcome outcome,float targetX,float playerX,float now)
        {
            if(outcome==null||(!(outcome.Strike?.Hit??false)&&outcome.Healed<=0))return;
            impactId=outcome.Id;impactAt=now;impactFoot=new Vector2(center+(outcome.Healed>0?playerX:targetX)*unit,ground+155);
        }
        public void Sample(PlayerSkillCombat skill,CombatActorState actor,float now,bool low,bool settled)
        {
            bool show=!settled&&skill!=null&&skill.Pending&&skill.OwnsAction(actor)&&!actor.IsDead;
            ink.Sample(show?skill:null,actor,now,low,center,unit,ground);
            castImage.enabled=false;impactImage.enabled=false;
            if(show&&actor.Phase==CombatPhase.Active&&HasSheet(skill.Current.Id))
            {
                float x=skill.Current.Id=="sword"?skill.ProjectileX(now):skill.Current.Id=="thunder"?skill.TargetX:actor.X;
                Show(castImage,sheets[skill.Current.Id],new Vector2(center+x*unit,ground+155),skill.Current.Id=="sword"?245:340,
                    Mathf.Min(3,Mathf.FloorToInt(skill.Progress(now)*4)),skill.Current.Id=="sword"?skill.Facing:1,low ? .65f : .9f);
            }
            float elapsed=now-impactAt;
            if(!settled&&elapsed>=0&&elapsed<.34f&&HasSheet(impactId))
                Show(impactImage,sheets[impactId],impactFoot,310,Mathf.Min(3,Mathf.FloorToInt(elapsed/.34f*4)),1,low ? .55f : .85f);
        }
        static void Show(RawImage image,Texture2D sheet,Vector2 at,float size,int frame,int facing,float alpha)
        {
            image.texture=sheet;image.uvRect=facing<0?new Rect((frame+1)/4f,0,-.25f,1):new Rect(frame/4f,0,.25f,1);
            image.rectTransform.anchoredPosition=at;image.rectTransform.sizeDelta=Vector2.one*size;image.color=new Color(1,1,1,alpha);image.enabled=true;
        }
        void OnDisable(){if(castImage!=null)castImage.enabled=false;if(impactImage!=null)impactImage.enabled=false;impactAt=-10;}
    }
}
