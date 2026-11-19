using System.Collections.Generic;
using UnityEngine;
namespace TwelveJade.Presentation
{
    public sealed class AtlasLabelLod : MonoBehaviour
    {
        AtlasPanZoom pan;List<CanvasGroup> labels;bool shown;
        public void Configure(AtlasPanZoom owner,List<CanvasGroup> groups){pan=owner;labels=groups;Apply(false);}
        void LateUpdate(){if(pan!=null){bool value=pan.RelativeZoom>=1.65f;if(value!=shown)Apply(value);}}
        void Apply(bool value){shown=value;if(labels==null)return;foreach(var label in labels)if(label!=null){label.alpha=value?1:0;label.interactable=label.blocksRaycasts=value;}}
        void OnDestroy(){pan=null;labels=null;}
    }
}
