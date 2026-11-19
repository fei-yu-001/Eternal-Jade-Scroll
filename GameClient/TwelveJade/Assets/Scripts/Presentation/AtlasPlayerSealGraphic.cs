using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
    public sealed class AtlasPlayerSealGraphic : MaskableGraphic
    {
        RectTransform sheet; Vector2 worldUv; Func<bool> reduced; Texture2D jade;
        float phase, pulseUntil, nextPaint; bool pulseActive;
        public override Texture mainTexture => jade!=null?jade:Texture2D.whiteTexture;
        public Vector2 NormalizedFoot => worldUv;
        public void Configure(RectTransform worldSheet,Vector2 foot,Func<bool> low)
        {
            sheet=worldSheet;worldUv=foot;reduced=low;raycastTarget=false;
            jade=ApprovedUiArt.Load("nov10/dialogue-control-active");SetAllDirty();Follow();
        }
        public void Pulse(){pulseUntil=Time.unscaledTime+1.8f;SetVerticesDirty();}
        void Follow()
        {
            if(sheet==null)return;
            float scale=sheet.localScale.x;
            rectTransform.anchoredPosition=sheet.anchoredPosition+new Vector2(worldUv.x*sheet.rect.width*scale,-worldUv.y*sheet.rect.height*scale);
        }
        void LateUpdate()
        {
            Follow();float now=Time.unscaledTime;
            if(now<nextPaint)return;nextPaint=now+.0333f;
            float next=(reduced?.Invoke()??false)?0:now;
            bool active=now<pulseUntil;
            if(!Mathf.Approximately(next,phase)||active||active!=pulseActive){phase=next;pulseActive=active;SetVerticesDirty();}
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var r=rectTransform.rect;
            bool low=reduced?.Invoke()??false;float pulse=low?0:Mathf.Sin(phase*2.2f)*.035f;
            float alpha=Time.unscaledTime<pulseUntil?1:.94f+pulse;
            // Bottom-center of this graphic is the exact foot anchor. The plaque floats above it.
            float left=r.center.x-34,right=r.center.x+34,bottom=r.yMin+12,top=bottom+30;
            mesh.AddVert(new Vector2(left,bottom),new Color(.88f,.98f,.88f,alpha),new Vector2(0,0));
            mesh.AddVert(new Vector2(left,top),new Color(.88f,.98f,.88f,alpha),new Vector2(0,1));
            mesh.AddVert(new Vector2(right,top),new Color(.88f,.98f,.88f,alpha),new Vector2(1,1));
            mesh.AddVert(new Vector2(right,bottom),new Color(.88f,.98f,.88f,alpha),new Vector2(1,0));
            mesh.AddTriangle(0,1,2);mesh.AddTriangle(0,2,3);
            // A tiny pointer uses the same painted jade texel; the geographic position never moves with the pulse.
            int n=mesh.currentVertCount;var tint=new Color(.38f,.65f,.46f,.95f);
            mesh.AddVert(new Vector2(r.center.x-6,r.yMin+12),tint,new Vector2(.45f,.5f));
            mesh.AddVert(new Vector2(r.center.x+6,r.yMin+12),tint,new Vector2(.55f,.5f));
            mesh.AddVert(new Vector2(r.center.x,r.yMin+1),tint,new Vector2(.5f,.5f));mesh.AddTriangle(n,n+1,n+2);
        }
        protected override void OnDestroy(){sheet=null;jade=null;reduced=null;base.OnDestroy();}
    }
}
