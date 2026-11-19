using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public sealed class QuestEntryView : MonoBehaviour
    {
        TMP_Text caption,body,fold;RectTransform root;Button button;Action changed;bool expanded;
        public float Height { get; private set; }=78;
        public void Build(UiKit ui,RectTransform host,string seal,string title,string detail,Func<bool> reduced,Action sound,Action reflow)
        {
            root=host;changed=reflow;float width=root.rect.width;
            button=AestheticUi.Jade(ui,host,host.name+" entry","",new Rect(0,0,width,74),Toggle,reduced,sound,24);
            var empty=button.GetComponentInChildren<TMP_Text>();empty.gameObject.SetActive(false);
            var stamp=ui.Rect(button.transform,"Quest cinnabar impression",12,14,42,42);
            ImmortalUi.Texture(ui,stamp,"Authored cinnabar seal",ApprovedUiArt.Load("memo/seal-cinnabar"),0,0,42,42,new Color(.70f,.58f,.52f,.9f));
            AestheticUi.Text(ui,stamp,"Quest class",seal,new Rect(2,2,38,38),22,TextAlignmentOptions.Center,new Color(.96f,.94f,.86f,1));
            caption=AestheticUi.Text(ui,button.transform,"Quest entry title",title,new Rect(65,12,width-112,50),24,TextAlignmentOptions.Left);
            fold=AestheticUi.Text(ui,button.transform,"Quest fold state","展",new Rect(width-41,15,30,44),21,TextAlignmentOptions.Center);
            body=AestheticUi.Text(ui,host,"Quest expanded detail",detail,new Rect(16,86,width-32,120),23);
            body.textWrappingMode=TextWrappingModes.Normal;Set(title,detail);Apply();
        }
        public void Set(string title,string detail){caption.text=title;body.text=detail;Apply();}
        void Toggle(){expanded=!expanded;Apply();changed?.Invoke();}
        void Apply()
        {
            if(body==null)return;body.gameObject.SetActive(expanded);fold.text=expanded?"收":"展";
            float heading=Mathf.Max(74,caption.GetPreferredValues(caption.text,root.rect.width-116,float.PositiveInfinity).y+24);
            ((RectTransform)button.transform).sizeDelta=new Vector2(root.rect.width,heading);
            var plate=button.GetComponentInChildren<JadeCommandPlateGraphic>();if(plate!=null)plate.rectTransform.sizeDelta=new Vector2(root.rect.width,heading);
            var border=button.GetComponentInChildren<ArtsCodexGraphic>();if(border!=null)border.rectTransform.sizeDelta=new Vector2(root.rect.width-16,heading-10);
            caption.rectTransform.sizeDelta=new Vector2(root.rect.width-112,heading-24);
            fold.rectTransform.anchoredPosition=new Vector2(root.rect.width-41,-(heading-44)*.5f);
            body.rectTransform.anchoredPosition=new Vector2(16,-(heading+12));
            float needed=body.GetPreferredValues(body.text,Mathf.Max(1,root.rect.width-36),float.PositiveInfinity).y+12;
            body.rectTransform.sizeDelta=new Vector2(root.rect.width-32,needed);
            Height=expanded?heading+16+needed:heading+4;root.sizeDelta=new Vector2(root.sizeDelta.x,Height);
        }
        void OnDestroy(){changed=null;}
    }
}
