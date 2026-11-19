using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    [DisallowMultipleComponent]
    public sealed class MenuCelestialButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        Selectable owner; RawImage artwork, radiance; TMP_Text caption; Func<bool> reduced;
        bool hovered, focused, pressed; float emphasis;
        public void Configure(Selectable button, RawImage art, RawImage glow, TMP_Text label, Func<bool> low)
        { owner=button;artwork=art;radiance=glow;caption=label;reduced=low;Refresh(true); }
        void LateUpdate()=>Refresh(false);
        void Refresh(bool immediate)
        {
            if(owner==null||artwork==null)return;
            bool enabled=owner.IsActive()&&owner.IsInteractable(),low=reduced?.Invoke()??true;
            float target=!enabled?0:pressed?1:focused? .78f:hovered? .6f:0;
            emphasis=immediate||low?target:Mathf.MoveTowards(emphasis,target,Time.unscaledDeltaTime*5);
            float breath=low?1:.88f+.12f*Mathf.Sin(Time.unscaledTime*2.1f);
            artwork.color=pressed?new Color(.83f,.87f,.84f,1):Color.Lerp(Color.white,new Color(1,.97f,.89f,1),emphasis*.26f);
            if(!enabled)artwork.color=new Color(.72f,.74f,.72f,.55f);
            if(radiance!=null)radiance.color=new Color(1,.82f,.43f,enabled?(.12f+.45f*emphasis)*breath:0);
            if(caption!=null)caption.color=enabled?(pressed?UiKit.Hex("6A4722"):UiKit.Hex("253F3B")):new Color(.30f,.38f,.35f,.5f);
        }
        public void OnPointerEnter(PointerEventData data){hovered=true;Refresh(false);}
        public void OnPointerExit(PointerEventData data){hovered=pressed=false;Refresh(false);}
        public void OnPointerDown(PointerEventData data){if(data.button==PointerEventData.InputButton.Left){pressed=true;Refresh(true);}}
        public void OnPointerUp(PointerEventData data){pressed=false;Refresh(true);}
        public void OnSelect(BaseEventData data){focused=true;Refresh(true);}
        public void OnDeselect(BaseEventData data){focused=pressed=false;Refresh(true);}
        void OnDisable(){hovered=focused=pressed=false;emphasis=0;if(radiance!=null)radiance.color=Color.clear;}
        void OnDestroy(){owner=null;artwork=radiance=null;caption=null;reduced=null;}
    }
}
