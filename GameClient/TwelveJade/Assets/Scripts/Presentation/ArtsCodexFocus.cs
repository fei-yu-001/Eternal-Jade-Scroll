using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public sealed class ArtsCodexFocus : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,ISelectHandler,IDeselectHandler
    {
        Button button;ArtsCodexGraphic halo;bool hover,focus,marked;
        public void Configure(Button owner,ArtsCodexGraphic art){button=owner;halo=art;Apply();}
        public void Mark(bool value){marked=value;Apply();}
        public void OnPointerEnter(PointerEventData e){hover=true;Apply();}
        public void OnPointerExit(PointerEventData e){hover=false;Apply();}
        public void OnSelect(BaseEventData e){focus=true;Apply();}
        public void OnDeselect(BaseEventData e){focus=false;Apply();}
        void Apply(){if(halo!=null)halo.Emphasize(button!=null&&button.IsInteractable()&&(marked||hover||focus));}
        void OnDisable(){hover=focus=false;if(halo!=null)halo.Emphasize(false);}
        void OnDestroy(){button=null;halo=null;}
    }
}
