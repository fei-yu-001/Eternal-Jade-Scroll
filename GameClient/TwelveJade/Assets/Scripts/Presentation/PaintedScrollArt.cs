using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Local composition of the Nov10 painted controls. No change to shared UiKit or game transactions.
    public static class PaintedScrollArt
    {
        public static RectTransform Note(UiKit ui,Transform parent,string name,Rect bounds)
        {
            var root=ui.Rect(parent,name,bounds.x,bounds.y,bounds.width,bounds.height);
            var image=root.gameObject.AddComponent<Image>();image.raycastTarget=false;
            root.gameObject.AddComponent<DialogueBorrowedArt>().InitializeMount(image,ApprovedUiArt.Load("nov10/dialogue-scroll-mount"),3.2f);
            return root;
        }
        public static Button Command(UiKit ui,Transform parent,string name,string title,Rect bounds,UnityAction action,Action sound,float size=25)
        {
            var root=ui.Rect(parent,name,bounds.x,bounds.y,bounds.width,bounds.height);
            var hit=root.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
            var button=root.gameObject.AddComponent<Button>();button.targetGraphic=hit;button.transition=Selectable.Transition.None;
            var surface=ui.Stretch(root,"Painted jade command").gameObject.AddComponent<DialogueSurface>();surface.ConfigurePainted(ui,false);
            var text=ui.Label(root,title,22,4,bounds.width-44,bounds.height-8,size,new Color(.16f,.23f,.20f,1),TextAlignmentOptions.Center);
            text.name=name+" caption";text.richText=false;text.enableAutoSizing=false;text.textWrappingMode=TextWrappingModes.NoWrap;text.extraPadding=false;text.margin=new Vector4(2,2,2,2);
            root.gameObject.AddComponent<DialogueSlipFeedback>().Configure(button,surface,text);
            button.onClick.AddListener(()=>sound?.Invoke());if(action!=null)button.onClick.AddListener(action);return button;
        }
    }
}
