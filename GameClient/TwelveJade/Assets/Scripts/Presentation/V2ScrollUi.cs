using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Only the V2 quest/settings controls use this factory; global UiKit recipes stay untouched.
    public static class V2ScrollUi
    {
        public static V2ScrollOrnament Art(UiKit ui,Transform parent,string name,V2ScrollOrnament.Shape shape,float x,float y,float w,float h,bool hit=false)
        {var art=ui.Rect(parent,name,x,y,w,h).gameObject.AddComponent<V2ScrollOrnament>();art.Configure(shape,hit);return art;}
        public static TMP_Text Text(UiKit ui,Transform parent,string name,string text,float x,float y,float w,float h,float size=26,bool brush=false,TextAlignmentOptions align=TextAlignmentOptions.TopLeft,Color? color=null)
        {
            var label=ui.Label(parent,text,x,y,w,h,size,color??UiKit.Ink,align,brush);
            label.name=name;label.richText=false;label.extraPadding=false;label.textWrappingMode=TextWrappingModes.Normal;
            if(!brush)SdfInkWeight.Apply(label);return label;
        }
        public static Button Button(UiKit ui,Transform parent,string name,string text,float x,float y,float w,float h,UnityAction action,UnityAction click=null)
        {
            var art=Art(ui,parent,name,V2ScrollOrnament.Shape.WoodPill,x,y,w,h,true);
            var button=art.gameObject.AddComponent<Button>();button.targetGraphic=art;button.transition=Selectable.Transition.None;
            if(click!=null)button.onClick.AddListener(click);if(action!=null)button.onClick.AddListener(action);
            art.gameObject.AddComponent<V2ControlFeedback>().Configure(art);
            Text(ui,art.transform,name+" caption",text,26,4,w-52,h-8,24,false,TextAlignmentOptions.Center,UiKit.Paper);
            return button;
        }
        public static Slider Slider(UiKit ui,Transform parent,string name,string title,float value,float y,V2ScrollOrnament.Shape emblem,UnityAction<float> changed)
        {
            Art(ui,parent,name+" sound emblem",emblem,82,y+1,70,70);
            Text(ui,parent,name+" caption",title,174,y+13,188,46,27);
            var percentage=Text(ui,parent,name+" percentage",Mathf.RoundToInt(value*100)+"%",667,y+16,84,42,24,false,TextAlignmentOptions.Center);
            var track=Art(ui,parent,name+" slider",V2ScrollOrnament.Shape.Groove,368,y+13,280,44,true);
            var area=ui.Rect(track.transform,"Pearl travel",16,5,248,34);
            var handle=Art(ui,area,name+" white jade pearl",V2ScrollOrnament.Shape.Pearl,0,0,34,0,true);
            handle.rectTransform.anchorMin=handle.rectTransform.anchorMax=new Vector2(0,.5f);handle.rectTransform.pivot=new Vector2(.5f,.5f);handle.rectTransform.anchoredPosition=Vector2.zero;
            // Slider owns the handle anchors. Zero cross-axis sizeDelta avoids doubling the pearl height.
            var slider=track.gameObject.AddComponent<Slider>();slider.minValue=0;slider.maxValue=1;slider.wholeNumbers=false;
            slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;slider.transition=Selectable.Transition.None;slider.SetValueWithoutNotify(value);
            track.gameObject.AddComponent<V2ControlFeedback>().Configure(handle);
            slider.onValueChanged.AddListener(v=>{percentage.text=Mathf.RoundToInt(v*100)+"%";changed(v);});return slider;
        }
    }
}
