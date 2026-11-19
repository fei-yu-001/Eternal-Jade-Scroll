using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public sealed class V2ControlFeedback : MonoBehaviour,ISelectHandler,IDeselectHandler,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler
    {
        V2ScrollOrnament art;bool selected,hovered,pressed;
        public void Configure(V2ScrollOrnament value){art=value;Refresh();}
        void Refresh(){if(art!=null)art.Emphasize(pressed?1f:selected?.85f:hovered?.5f:0);}
        public void OnSelect(BaseEventData e)
        {
            selected=true;Refresh();var scroll=GetComponentInParent<ScrollRect>();
            if(scroll==null||scroll.content==null||scroll.viewport==null)return;
            Canvas.ForceUpdateCanvases();var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,transform);
            float shift=bounds.min.y<scroll.viewport.rect.yMin?scroll.viewport.rect.yMin-bounds.min.y:
                bounds.max.y>scroll.viewport.rect.yMax?scroll.viewport.rect.yMax-bounds.max.y:0;
            var p=scroll.content.anchoredPosition;p.y=Mathf.Clamp(p.y+shift,0,Mathf.Max(0,scroll.content.rect.height-scroll.viewport.rect.height));scroll.content.anchoredPosition=p;
        }
        public void OnDeselect(BaseEventData e){selected=false;Refresh();}
        public void OnPointerEnter(PointerEventData e){hovered=true;Refresh();}
        public void OnPointerExit(PointerEventData e){hovered=false;pressed=false;Refresh();}
        public void OnPointerDown(PointerEventData e){pressed=true;Refresh();}
        public void OnPointerUp(PointerEventData e){pressed=false;Refresh();}
        void OnDisable(){selected=hovered=pressed=false;Refresh();}
    }
}
