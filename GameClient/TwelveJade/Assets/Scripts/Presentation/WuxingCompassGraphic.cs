using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WuxingCompassGraphic : RawImage
    {
        // Current saves have no root-affinity field. Authored flames are subdued, never presented as earned roots.
        public const bool AffinityKnown=false;
        public void Configure(){texture=ApprovedUiArt.Load("nov8/wuxing-lingpan-compass");raycastTarget=false;color=new Color(.51f,.59f,.56f,.86f);}
    }
}
