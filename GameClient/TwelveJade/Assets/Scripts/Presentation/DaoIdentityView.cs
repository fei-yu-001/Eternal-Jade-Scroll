using System;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
namespace TwelveJade.Presentation
{
    public static class DaoIdentityView
    {
        public static void Roots(UiKit ui,Transform parent,string name,Rect box)
        {
            float size=Mathf.Min(box.height,box.width);
            var image=ui.Rect(parent,name+" sleeping jade compass",box.x,box.y,size,size).gameObject.AddComponent<WuxingCompassGraphic>();
            image.Configure();
            if(box.width>size+140)
                AestheticUi.Text(ui,parent,name+" unknown affinity","五行未测 · 灵盘静候",new Rect(box.x+size+18,box.y+(size-44)*.5f,box.width-size-18,44),23,TextAlignmentOptions.Left);
        }
        public static void Build(UiKit ui,RectTransform host,SaveData data,string presetId,string origin,int hp,int stamina,int attack,int defense,
            Func<bool> lowMotion,Action close,Action sound)
        {
            bool initiated=PrologueRules.CanCultivate(data);
            var page=ui.Rect(host,"Dao identity scroll",80,70,1760,940);
            ScholarScrollArt.Horizontal(ui,page,"Dao identity woven mount",0,0,1760,940);
            AestheticUi.Paper(ui,page,"Dao identity writing silk",new Rect(42,42,1676,856));
            var portrait=ui.Rect(page,"Dao identity large portrait",62,94,686,728).gameObject.AddComponent<PosterCardView>();
            portrait.Configure(presetId,data.gender,useFigure:true);
            AestheticUi.Text(ui,page,"Dao identity portrait caption",initiated?"道 身 · 本 命 行 牒":"身 世 · 乱 世 行 牒",new Rect(68,836,674,54),28,TextAlignmentOptions.Center);
            AestheticUi.Text(ui,page,"Dao identity name",data.characterName,new Rect(824,76,814,70),36);
            AestheticUi.Text(ui,page,"Dao identity origin",origin+" · "+CharacterPreset.GenderName(data.gender),new Rect(824,157,814,46),26);
            AestheticUi.Text(ui,page,"Dao identity capacities","气血上限  "+hp+"     精力上限  "+stamina,new Rect(824,227,814,46),26);
            AestheticUi.Text(ui,page,"Dao identity combat","基础力道  "+attack+"     基础防御  "+defense,new Rect(824,281,814,46),26);
            AestheticUi.Text(ui,page,"Dao identity roots title",initiated?"五 行 灵 根 · 天机未明":"凡 人 之 身 · 前 路 未 知",new Rect(824,352,814,46),25);
            if(initiated)Roots(ui,page,"Dao roots",new Rect(814,416,334,334));
            else AestheticUi.Text(ui,page,"Mortal journey","十八加冠\n青石农家\n\n父亲修河伤了腿\n阿娘体弱仍持家",new Rect(824,440,300,280),28,TextAlignmentOptions.Center);
            string traits=string.Join(" · ",(data.traits??Array.Empty<string>()).Select(id=>CharacterGen.FindTrait(id)?.Name??id));
            AestheticUi.Text(ui,page,"Dao identity destiny","命格  "+(CharacterGen.FindDestiny(data.destiny)?.Name??"命星待照"),new Rect(1172,420,466,56),28);
            AestheticUi.Text(ui,page,"Dao identity traits","先天词条  "+(traits.Length>0?traits:"尚未记载"),new Rect(1172,496,466,104),25);
            AestheticUi.Text(ui,page,"Dao identity wealth","铜钱  "+data.coins+" 文     镇中声望  "+data.localReputation,new Rect(1172,619,466,88),25);
            AestheticUi.Text(ui,page,"Dao identity unknowns",initiated?"年岁待考 · 仙途未明 · 宗门未明":"乱世初行 · 故里青石 · 去路未定",new Rect(824,766,814,48),24);
            AestheticUi.Jade(ui,page,"Dao identity close","收起",new Rect(1438,826,230,76),()=>close?.Invoke(),lowMotion,sound);
        }
    }
}
