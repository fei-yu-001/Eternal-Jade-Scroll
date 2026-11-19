using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Local assembly of approved artwork. Shared UiKit recipes and save rules stay intact.
    public static class AestheticUi
    {
        public static readonly Color Ink=UiKit.Hex("283F3C"),ColdGold=UiKit.Hex("AA915D");
        public static TMP_Text Text(UiKit ui,Transform parent,string name,string value,Rect box,float size=26,
            TextAlignmentOptions align=TextAlignmentOptions.TopLeft,Color? ink=null)
        {
            var label=ImmortalUi.Text(ui,parent,name,value,box.x,box.y,box.width,box.height,size,ink??Ink,false,align);
            label.margin=new Vector4(2,2,2,2);label.extraPadding=false;return label;
        }
        public static Button Jade(UiKit ui,Transform parent,string name,string caption,Rect box,UnityAction action,
            Func<bool> lowMotion,Action sound=null,float fontSize=26)
        {
            var host=ui.Rect(parent,name,box.x,box.y,box.width,box.height);
            CommandPlate(ui,host,name+" white jade",new Rect(0,0,box.width,box.height));
            var hit=host.gameObject.AddComponent<Image>();hit.color=Color.clear;
            var button=host.gameObject.AddComponent<Button>();button.targetGraphic=hit;button.transition=Selectable.Transition.None;
            var halo=ArtsCodexArt.Graphic(ui,host,name+" cool gold light",new Rect(8,5,box.width-16,box.height-10),ArtsCodexGraphic.Form.CommandEdge,ColdGold,lowMotion);
            host.gameObject.AddComponent<ArtsCodexFocus>().Configure(button,halo);
            Text(ui,host,name+" caption",caption,new Rect(box.width*.10f,6,box.width*.80f,box.height-12),fontSize,TextAlignmentOptions.Center);
            button.onClick.AddListener(()=>sound?.Invoke());if(action!=null)button.onClick.AddListener(action);return button;
        }
        public static Button Talisman(UiKit ui,Transform parent,string name,string caption,Rect box,UnityAction action,
            Func<bool> lowMotion,Action sound=null,float fontSize=24)
        {
            var button=Jade(ui,parent,name,caption,box,action,lowMotion,sound,fontSize);
            var plate=button.GetComponentInChildren<JadeCommandPlateGraphic>();plate.texture=ApprovedUiArt.Load("nov8/arts-choice-vr");
            button.GetComponentInChildren<TMP_Text>().color=UiKit.Hex("EAE4D7");return button;
        }
        public static RawImage CommandPlate(UiKit ui,Transform parent,string name,Rect box,string id=null)
        {
            var plate=ui.Rect(parent,name,box.x,box.y,box.width,box.height).gameObject.AddComponent<JadeCommandPlateGraphic>();
            if(id==null&&ApprovedUiArt.Has("nov8/ancient-jade-command-plate"))plate.texture=ApprovedUiArt.Load("nov8/ancient-jade-command-plate");
            if(plate.texture==null)plate.texture=ApprovedUiArt.Load(id??"nov8/moon-jade-command");
            plate.raycastTarget=false;plate.color=Color.white;return plate;
        }
        public static RawImage Paper(UiKit ui,Transform parent,string name,Rect box)
            =>ScholarScrollArt.WritingSilk(ui,parent,name,box.x,box.y,box.width,box.height);
    }
}
