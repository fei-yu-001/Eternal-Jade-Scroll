using System;
using System.Collections.Generic;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public sealed class WideInkAtlasView : MonoBehaviour
    {
        // Presentation coordinates only: this world canvas can receive more real regions later.
        public static readonly Vector2 WorldCanvasSize=new Vector2(3200,1800);
        public static readonly Rect QingshiTile=new Rect(1150,500,900,900);
        public static Vector2 RegionUv(Vector2 local)=>new Vector2((QingshiTile.x+local.x*QingshiTile.width)/WorldCanvasSize.x,
            (QingshiTile.y+local.y*QingshiTile.height)/WorldCanvasSize.y);
        public void Build(UiKit ui,RectTransform host,TownMap map,string json,Vector2 foot,SaveData save,
            Func<bool> low,Action close,Action click)
        {
            var viewport=ui.Rect(host,"Atlas painted viewport",0,0,1920,1080);viewport.gameObject.AddComponent<RectMask2D>();
            var hit=viewport.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
            var sheet=ui.Rect(viewport,"Atlas expandable world canvas",0,0,WorldCanvasSize.x,WorldCanvasSize.y);
            var cloud=ui.Stretch(sheet,"Atlas unopened world cloud veil").gameObject.AddComponent<RawImage>();
            cloud.texture=ApprovedUiArt.Load("nov10/world-unopened-clouds");cloud.raycastTarget=false;
            var tile=ui.Rect(sheet,"Atlas open Qingshi region",QingshiTile.x,QingshiTile.y,QingshiTile.width,QingshiTile.height);
            var texture=ApprovedUiArt.Load("nov8/qingshi-town-atlas");tile.gameObject.AddComponent<AtlasRegionGraphic>().Configure(texture);
            var plotSource=Resources.Load<TextAsset>("Config/building-plots");
            var places=TownAtlasRegistry.Places(map,plotSource!=null?plotSource.text:"{}");
            var marks=ui.Stretch(tile,"Atlas actual foot markers").gameObject.AddComponent<InkAtlasGraphic>();
            Func<MapLandmark,bool> known=m=>AtlasExploration.Known(save,m.X,m.Y,map.WorldWidth,map.WorldHeight);
            marks.Configure(map,json,foot,texture==null,known,places);
            var here=RegionUv(new Vector2(foot.x/map.WorldWidth,foot.y/map.WorldHeight));
            var pan=viewport.gameObject.AddComponent<AtlasPanZoom>();pan.Configure(viewport,sheet,here,low);
            // Map-local display controls stay fixed over the world painting while its canvas pans underneath.
            var heading=PaintedScrollArt.Note(ui,viewport,"Atlas title silk",new Rect(28,22,320,100));
            ImmortalUi.Text(ui,heading,"Atlas world title","山 河 舆 图",42,22,236,54,38,ImmortalUi.Ink,true,TextAlignmentOptions.Center);
            var chosen=ImmortalUi.Text(ui,viewport,"Atlas chosen ground location","青石镇 · 已开放",38,136,400,54,25,ImmortalUi.Ink,false);
            var sealRect=ui.Rect(viewport,"Atlas true player portrait marker",0,0,90,114);sealRect.pivot=new Vector2(.5f,0);
            var seal=sealRect.gameObject.AddComponent<AtlasPlayerSealGraphic>();seal.Configure(sheet,here,low);
            var portrait=ui.PortraitBadge(sealRect,DialoguePortraitCatalog.Hero(save?.gender),
                new UiKit.PortraitFrame{X=.22f,Y=.60f,W=.56f,H=.373333f},14,8,62);
            var round=(RectTransform)portrait.transform.parent;round.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            GuofengHudArt.Create(ui,sealRect,"Atlas jade portrait bezel","hud-avatar",6,0,78,78);
            var caption=ImmortalUi.Text(ui,sealRect,"Atlas player seal caption","此身",20,76,50,26,21,UiKit.Hex("244D36"),true,TextAlignmentOptions.Center);
            caption.raycastTarget=false;
            heading.SetAsLastSibling();chosen.transform.SetAsLastSibling();
            Vector2 focusUv=here;string chosenId=null;var occupied=new List<Rect>();var labels=new List<CanvasGroup>();
            foreach(var mark in places)
            {
                if(!known(mark))continue;var point=marks.Project(mark.X,mark.Y);
                var box=new Rect(Mathf.Clamp(point.x-110,8,672),Mathf.Clamp(point.y+10,8,846),220,46);
                if(occupied.Exists(other=>other.Overlaps(box)))continue;occupied.Add(box);var target=mark;
                var label=ImmortalUi.Hotspot(ui,tile,"Atlas ground label "+mark.Id,mark.Name,box.x,box.y,box.width,box.height,()=>
                {click?.Invoke();chosenId=target.Id;focusUv=RegionUv(new Vector2(target.X/map.WorldWidth,target.Y/map.WorldHeight));
                    marks.Select(chosenId);marks.HighlightDetails(true);pan.Focus(focusUv);chosen.text=target.Name;},27,UiKit.Hex("2D372B"));
                var group=label.gameObject.AddComponent<CanvasGroup>();labels.Add(group);
            }
            ImmortalUi.Text(ui,tile,"Atlas open region name","青 石 镇",270,-70,360,60,44,UiKit.Hex("384F41"),true,TextAlignmentOptions.Center);
            PaintedScrollArt.Command(ui,viewport,"Atlas close","收卷",new Rect(1630,28,180,66),()=>close?.Invoke(),click,25);
            PaintedScrollArt.Command(ui,viewport,"Atlas locate self","此身所在",new Rect(32,890,210,70),()=>
            {chosenId=null;focusUv=here;marks.Select(null);pan.CloseView(here);seal.Pulse();chosen.text="此身所在 · 青石镇";},click,26);
            PaintedScrollArt.Command(ui,viewport,"Atlas near town","近观",new Rect(258,890,160,70),()=>
            {marks.HighlightDetails(true);marks.Select(chosenId);pan.CloseView(focusUv);seal.Pulse();},click,26);
            PaintedScrollArt.Command(ui,viewport,"Atlas world overview","天下",new Rect(434,890,160,70),()=>
            {chosenId=null;focusUv=here;marks.Select(null);marks.HighlightDetails(false);pan.FitWorld();chosen.text="青石镇 · 已开放";},click,26);
            var plus=PaintedScrollArt.Command(ui,viewport,"Atlas zoom in","",new Rect(1622,868,84,70),()=>pan.Scale(1.25f),click);
            ui.Rect(plus.transform,"Magnifying glass plus",23,15,38,38).gameObject.AddComponent<AtlasZoomGlyph>().Configure(true);
            var minus=PaintedScrollArt.Command(ui,viewport,"Atlas zoom out","",new Rect(1720,868,84,70),()=>pan.Scale(1/1.25f),click);
            ui.Rect(minus.transform,"Magnifying glass minus",23,15,38,38).gameObject.AddComponent<AtlasZoomGlyph>().Configure(false);
            ImmortalUi.Text(ui,viewport,"Atlas control hint","拖动览图 · 滚轮缩放",1400,956,408,34,22,ImmortalUi.Ink,false,TextAlignmentOptions.Right);
            ImmortalUi.Text(ui,viewport,"Atlas unopened scope","云深处 · 尚未开放",620,940,610,42,25,ImmortalUi.Ink,true,TextAlignmentOptions.Center);
            gameObject.AddComponent<AtlasLabelLod>().Configure(pan,labels);
        }
    }
}
