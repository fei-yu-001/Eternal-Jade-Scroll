using UnityEngine;
using UnityEngine.EventSystems;
namespace TwelveJade.Presentation
{
    public sealed class JadeWheelSlotInput : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, ICancelHandler
    {
        JadeLotusRotator wheel;int slot;
        public void Configure(JadeLotusRotator value,int index){wheel=value;slot=index;}
        public void OnSelect(BaseEventData e){if(wheel!=null)wheel.Focus(slot,true);}
        public void OnDeselect(BaseEventData e){if(wheel!=null)wheel.Focus(slot,false);}
        public void OnPointerEnter(PointerEventData e){if(wheel!=null)wheel.Hover(slot,true);}
        public void OnPointerExit(PointerEventData e){if(wheel!=null)wheel.Hover(slot,false);}
        public void OnPointerDown(PointerEventData e){if(wheel!=null)wheel.PointerDown(slot);}
        public void OnCancel(BaseEventData e){if(wheel!=null)wheel.CancelPending();}
        void OnDisable(){if(wheel!=null){wheel.Focus(slot,false);wheel.Hover(slot,false);}}
    }
}
