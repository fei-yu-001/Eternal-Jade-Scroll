using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public sealed class ImmortalControlFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        TMP_Text label; Graphic artwork; Color original, originalArt; bool hovered, focused;
        public void Configure(TMP_Text value, Graphic art = null)
        { label=value;artwork=art;if(label!=null)original=label.color;if(artwork!=null)originalArt=artwork.color; }
        public void OnPointerEnter(PointerEventData e) { hovered = true; Apply(); }
        public void OnPointerExit(PointerEventData e) { hovered = false; Apply(); }
        public void OnSelect(BaseEventData e) { focused = true; Apply(); }
        public void OnDeselect(BaseEventData e) { focused = false; Apply(); }
        void Apply()
        {
            bool active=(hovered||focused)&&GetComponent<Button>().interactable;
            if(label!=null)label.color=active?ImmortalUi.Gold:original;
            if(artwork!=null)artwork.color=active?new Color(1.45f,1.3f,1.05f,1):originalArt;
        }
        void OnDisable() { hovered=focused=false;if(label!=null)label.color=original;if(artwork!=null)artwork.color=originalArt; }
    }
}
