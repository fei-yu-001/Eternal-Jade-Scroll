using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public static class XuanSettingsUi
    {
        public static readonly Color Ink=UiKit.Hex("D8E3D6"),Muted=UiKit.Hex("A8BBB4");
        public static XuanStoneGraphic Art(UiKit ui,Transform parent,string name,Rect box,XuanStoneGraphic.Form form,bool hit=false)
        {var art=ui.Rect(parent,name,box.x,box.y,box.width,box.height).gameObject.AddComponent<XuanStoneGraphic>();art.Configure(form,hit);return art;}
        public static TMP_Text Text(UiKit ui,Transform parent,string name,string value,Rect box,float size=26,TextAlignmentOptions align=TextAlignmentOptions.TopLeft,bool muted=false)
            =>AestheticUi.Text(ui,parent,name,value,box,size,align,muted?Muted:Ink);
        public static Button Button(UiKit ui,Transform parent,string name,string value,float x,float y,float width,float height,UnityAction action,Func<bool> low,Action click)
            =>AestheticUi.Jade(ui,parent,name,value,new Rect(x,y,width,height),action,low,click,24);
        public static Slider Slider(UiKit ui,Transform parent,string name,string title,float value,float y,UnityAction<float> changed)
        {
            Text(ui,parent,name+" caption",title,new Rect(112,y+4,210,50),28);
            var percentage=Text(ui,parent,name+" percentage",Mathf.RoundToInt(value*100)+"%",new Rect(667,y+8,88,46),24,TextAlignmentOptions.Center);
            var track=Art(ui,parent,name+" slider",new Rect(342,y+1,306,58),XuanStoneGraphic.Form.Groove,true);
            var area=ui.Rect(track.transform,"Ruyi travel",21,9,264,40);
            var handle=Art(ui,area,name+" white jade ruyi",new Rect(0,0,40,0),XuanStoneGraphic.Form.Ruyi,true);
            handle.rectTransform.anchorMin=handle.rectTransform.anchorMax=new Vector2(0,.5f);handle.rectTransform.pivot=new Vector2(.5f,.5f);handle.rectTransform.anchoredPosition=Vector2.zero;
            var slider=track.gameObject.AddComponent<Slider>();slider.minValue=0;slider.maxValue=1;slider.wholeNumbers=false;
            slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;slider.transition=Selectable.Transition.ColorTint;slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(v=>{percentage.text=Mathf.RoundToInt(v*100)+"%";changed(v);});return slider;
        }
    }
}
