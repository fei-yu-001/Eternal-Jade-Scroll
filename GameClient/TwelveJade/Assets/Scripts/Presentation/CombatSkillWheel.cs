using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    public struct CombatSkillSlotState
    {
        public bool Learned, Available;
        public string Status;
        public float Cooldown, Duration;
    }
    // Combat page owns this hub. Town's original hub is rebuilt naturally when leaving the page.
    // 版式是自绘深青玉盘加金线分隔的独立圆形玉槽：八槽位置固定，出手只换高亮，
    // 不跟随出手转动，连招时手指记得住位置。
    public sealed class CombatSkillWheel : MonoBehaviour
    {
        public const float Size=352;
        public static readonly string[] Ids={"light","heavy","dodge","pill","sword","thunder","clarity","target"};
        static readonly string[] Labels={"轻击","重击","闪身","丹药","剑气","五雷","清心","察隙"};
        static readonly string[] Keys={"J / 1","K / 2","空格 / L","Q / 3","U / 4","I / 5","O / 6","Tab"};
        static readonly string[] Names={"Combat light chain","Combat heavy strike","Combat true dodge","Combat use medicine","Combat learned sword","Combat learned thunder","Combat learned clarity","Combat cycle weak point"};
        // 走 UI 美术目录：nov7 图标与 nov11 图标都在目录里声明,路径大小写由目录给定。
        static readonly string[] IconIds={"nov8/arts-sigil-light-vr","nov8/arts-sigil-heavy-vr","nov8/arts-sigil-dodge-vr","nov7-v2/wheel/icon-najie",
            "nov8/arts-sigil-sword-vr","nov8/arts-sigil-thunder-vr","nov8/arts-sigil-clarity-vr","nov7-v2/wheel/icon-jingmai"};
        // radius 116 的相邻弦长 88.8,槽径 74 留 14.8 空隙,八槽互不压边。
        const float SlotRadius=116f, SlotSize=74f, SlotIconSize=30f;
        readonly Button[] buttons=new Button[8];
        readonly TMP_Text[] captions=new TMP_Text[8], footers=new TMP_Text[8];
        readonly Image[] icons=new Image[8];
        readonly JadeWheelSignalGraphic[] focus=new JadeWheelSignalGraphic[8];
        readonly CombatSkillCooldownGraphic[] cooldowns=new CombatSkillCooldownGraphic[8];
        readonly WheelSlotDiscGraphic[] discs=new WheelSlotDiscGraphic[8];
        Func<int,CombatSkillSlotState> read;
        Action<int> activate;
        Action click, close;
        Func<bool> reduced;
        Button closeButton;
        int selected=-1;
        public int Selected=>selected;
        public bool ArtworkReady {get;private set;}
        public Button SlotButton(int slot)=>slot>=0&&slot<buttons.Length?buttons[slot]:null;
        public Button CloseButton=>closeButton;
        public static CombatSkillWheel Create(UiKit ui,Transform parent,Func<int,CombatSkillSlotState> states,Action<int> action,Func<bool> low,Action sound)
        {
            var root=ui.Rect(parent,"Combat jade skill wheel",1552,696,Size,Size);
            var view=root.gameObject.AddComponent<CombatSkillWheel>();view.read=states;view.activate=action;view.reduced=low;view.click=sound;
            var hit=root.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
            Center(ui,root,"Combat jade wheel face",Size,Size,Vector2.zero).gameObject.AddComponent<WheelFaceGraphic>().Configure();
            bool complete=true;
            for(int i=0;i<8;i++)
            {
                int slot=i;
                var rect=Center(ui,root,Names[i],SlotSize+14,SlotSize+14,SlotPoint(i));
                var graphic=rect.gameObject.AddComponent<Image>();graphic.color=Color.clear;graphic.raycastTarget=true;
                var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=graphic;button.transition=Selectable.Transition.None;
                button.onClick.AddListener(()=>{view.click?.Invoke();view.activate?.Invoke(slot);});view.buttons[i]=button;
                view.focus[i]=Center(ui,rect,"Combat chosen skill gold "+i,SlotSize+14,SlotSize+14,Vector2.zero).gameObject.AddComponent<JadeWheelSignalGraphic>();
                view.focus[i].Configure(JadeWheelSignalGraphic.Shape.Focus,0);
                view.discs[i]=Center(ui,rect,"Combat slot jade disc "+i,SlotSize,SlotSize,Vector2.zero).gameObject.AddComponent<WheelSlotDiscGraphic>();
                view.cooldowns[i]=Center(ui,rect,"Combat actual cooldown arc "+i,SlotSize+8,SlotSize+8,Vector2.zero).gameObject.AddComponent<CombatSkillCooldownGraphic>();
                var icon=Center(ui,rect,"Combat skill icon "+Ids[i],SlotIconSize,SlotIconSize,new Vector2(0,17)).gameObject.AddComponent<Image>();
                icon.raycastTarget=false;icon.preserveAspect=true;
                var art=ApprovedUiArt.Load(IconIds[i]);
                if(art==null)complete=false;icon.sprite=SpriteOf(art);icon.enabled=art!=null;view.icons[i]=icon;
                view.captions[i]=Text(ui,rect,Labels[i],SlotSize+4,22,18,new Vector2(0,-8));
                view.footers[i]=Text(ui,rect,"",SlotSize+4,16,11,new Vector2(0,-26));
            }
            if(!complete)Debug.LogError("[CombatSkillWheel] Missing existing wheel skill icons.");
            view.ArtworkReady=complete;
            view.BuildClose(ui,root);
            for(int i=0;i<8;i++)
            {
                // 位置固定,键位环也按画面顺序走,不会因为出手换位。
                view.buttons[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,
                    selectOnRight=view.buttons[(i+1)%8],selectOnDown=view.buttons[(i+1)%8],
                    selectOnLeft=view.buttons[(i+7)%8],selectOnUp=view.buttons[(i+7)%8]};
            }
            view.Sample(0,0);return view;
        }
        internal static Vector2 SlotPoint(int slot)
        {
            float radians=(90-slot*45)*Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians),Mathf.Sin(radians))*SlotRadius;
        }
        void BuildClose(UiKit ui,RectTransform root)
        {
            var rect=Center(ui,root,"Combat sheathe blade",88,88,Vector2.zero);
            Center(ui,rect,"Combat close jade disc",78,78,Vector2.zero).gameObject.AddComponent<WheelSlotDiscGraphic>().Configure(true);
            var hit=Center(ui,rect,"Combat close hit",88,88,Vector2.zero).gameObject.AddComponent<Image>();
            hit.color=Color.clear;hit.raycastTarget=true;
            closeButton=rect.gameObject.AddComponent<Button>();closeButton.targetGraphic=hit;
            closeButton.transition=Selectable.Transition.None;closeButton.navigation=new Navigation{mode=Navigation.Mode.None};
            closeButton.onClick.AddListener(()=>{click?.Invoke();close?.Invoke();});
            var label=Text(ui,rect,"收剑",78,28,21,new Vector2(0,-2));label.color=UiKit.Hex("F1E3C2");
        }
        /// <summary>收剑回调：可以晚于 Create 设置，页面重启用同一实例也能重设。</summary>
        public void SetCloseAction(Action close)
        {
            this.close=close;
            if(closeButton!=null)closeButton.interactable=close!=null;
        }
        public void MarkUsed(int slot){if(slot>=0&&slot<8)selected=slot;}
        public void Sample(float now,float dt)
        {
            bool low=reduced!=null&&reduced();
            for(int i=0;i<8;i++)
            {
                var state=read==null?new CombatSkillSlotState():read(i);
                bool on=state.Available&&state.Learned;
                buttons[i].interactable=on;
                string footer=state.Cooldown>0?Mathf.CeilToInt(state.Cooldown)+"秒"
                    :!state.Learned?"未习得":!state.Available&&!string.IsNullOrEmpty(state.Status)?state.Status
                    :Keys[i].Replace(" ","");
                if(footers[i].text!=footer)footers[i].text=footer;
                captions[i].color=on?UiKit.Hex("EFE4C8"):UiKit.Hex("7E8A7C");
                footers[i].color=state.Cooldown>0?UiKit.Hex("8FD0C6"):on?UiKit.Hex("B9C9B4"):UiKit.Hex("6C7A6B");
                discs[i].SetState(on,state.Cooldown>0);
                icons[i].color=on?Color.white:new Color(1,1,1,.42f);
                focus[i].SetStrength(selected==i?(low ? .7f : .68f+.12f*Mathf.Sin(now*2.5f)):0);
                cooldowns[i].SetRemaining(state.Cooldown,state.Duration);
            }
        }
        static readonly System.Collections.Generic.Dictionary<Texture2D,Sprite> iconSprites=new System.Collections.Generic.Dictionary<Texture2D,Sprite>();
        static Sprite SpriteOf(Texture2D texture)
        {
            if(texture==null)return null;
            if(iconSprites.TryGetValue(texture,out var known)&&known!=null)return known;
            var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
            iconSprites[texture]=sprite;return sprite;
        }
        static TMP_Text Text(UiKit ui,Transform parent,string value,float w,float h,float size,Vector2 at)
        {
            var text=ui.Label(parent,value,0,0,w,h,size,UiKit.Hex("EFE4C8"),TextAlignmentOptions.Center);
            text.name="Combat skill "+value;text.extraPadding=false;text.margin=new Vector4(1,1,1,1);text.raycastTarget=false;text.richText=false;
            Place(text.rectTransform,at);return text;
        }
        static RectTransform Center(UiKit ui,Transform parent,string name,float w,float h,Vector2 at){var r=ui.Rect(parent,name,0,0,w,h);Place(r,at);return r;}
        static void Place(RectTransform r,Vector2 at){r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=at;}
        // 回调全部留在字段里：OnDisable 不清,页面重启用同一实例无需重新接线。
        void OnDisable(){}
        /// <summary>深青玉盘：内外金线、八条短隔线。</summary>
        [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
        sealed class WheelFaceGraphic:MaskableGraphic
        {
            protected override void Awake(){base.Awake();raycastTarget=false;}
            public void Configure(){SetVerticesDirty();}
            protected override void OnPopulateMesh(VertexHelper v)
            {
                v.Clear();var r=rectTransform.rect;var c=r.center;float radius=Mathf.Min(r.width,r.height)*.5f;
                Fill(v,c,radius);Ring(v,c,radius-1.6f,3.2f,new Color(.83f,.69f,.38f,.52f));
                Ring(v,c,radius-9f,1f,new Color(.86f,.73f,.42f,.20f));
                Ring(v,c,radius*.33f,1.4f,new Color(.83f,.69f,.38f,.30f));
                for(int i=0;i<8;i++)
                {
                    float a=(90-22.5f-i*45f)*Mathf.Deg2Rad;var d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                    Quad(v,c+d*(radius*.40f),c+d*(radius*.84f),1.3f,new Color(.84f,.70f,.40f,.22f));
                }
            }
            static void Fill(VertexHelper v,Vector2 c,float radius)
            {
                var outer=new Color(.043f,.132f,.120f,.96f);var inner=new Color(.075f,.212f,.190f,.93f);
                const int steps=56;
                for(int i=0;i<steps;i++)
                {
                    float a=i*Mathf.PI*2/steps,b=(i+1)*Mathf.PI*2/steps;
                    var p=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var q=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                    Triangle(v,c,c+p*radius,c+q*radius,outer);
                    Triangle(v,c,c+p*(radius*.62f),c+q*(radius*.62f),inner);
                }
            }
            static void Quad(VertexHelper v,Vector2 a,Vector2 b,float width,Color color)
            {
                var d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*width*.5f;int i=v.currentVertCount;
                v.AddVert(a-n,color,Vector2.zero);v.AddVert(a+n,color,Vector2.zero);v.AddVert(b+n,color,Vector2.zero);v.AddVert(b-n,color,Vector2.zero);
                v.AddTriangle(i,i+1,i+2);v.AddTriangle(i,i+2,i+3);
            }
            static void Ring(VertexHelper v,Vector2 c,float radius,float width,Color color)
            {
                const int steps=64;
                for(int i=0;i<steps;i++)
                {
                    float a=i*Mathf.PI*2/steps,b=(i+1)*Mathf.PI*2/steps;
                    var p=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var q=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                    Triangle(v,c+p*(radius-width*.5f),c+p*(radius+width*.5f),c+q*(radius+width*.5f),color);
                    Triangle(v,c+p*(radius-width*.5f),c+q*(radius+width*.5f),c+q*(radius-width*.5f),color);
                }
            }
            static void Triangle(VertexHelper v,Vector2 a,Vector2 b,Vector2 c,Color color)
            {int i=v.currentVertCount;v.AddVert(a,color,Vector2.zero);v.AddVert(b,color,Vector2.zero);v.AddVert(c,color,Vector2.zero);v.AddTriangle(i,i+1,i+2);}
        }
        /// <summary>独立圆形玉槽：深青玉底加金环,禁用时整槽压暗,冷却时偏冷。</summary>
        [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
        sealed class WheelSlotDiscGraphic:MaskableGraphic
        {
            bool on=true, cooling, centre;
            protected override void Awake(){base.Awake();raycastTarget=false;}
            public void Configure(bool isCentre){centre=isCentre;SetVerticesDirty();}
            public void SetState(bool available,bool isCooling)
            {
                if(on==available&&cooling==isCooling)return;on=available;cooling=isCooling;SetVerticesDirty();
            }
            protected override void OnPopulateMesh(VertexHelper v)
            {
                v.Clear();var r=rectTransform.rect;var c=r.center;float radius=Mathf.Min(r.width,r.height)*.5f;
                if(radius<=1)return;
                var fill=centre?new Color(.055f,.148f,.132f,.97f)
                    :on?new Color(.062f,.196f,.172f,.97f):new Color(.050f,.116f,.106f,.94f);
                if(cooling&&!centre)fill=new Color(.052f,.128f,.146f,.96f);
                Disc(v,c,radius,fill);
                Ring(v,c,radius-1.2f,1.9f,on||centre?new Color(.84f,.70f,.40f,.62f):new Color(.60f,.58f,.44f,.40f));
                Ring(v,c,radius-4.4f,.8f,on||centre?new Color(1,.95f,.76f,.20f):new Color(.80f,.82f,.70f,.10f));
            }
            static void Disc(VertexHelper v,Vector2 c,float radius,Color color)
            {
                const int steps=40;
                for(int i=0;i<steps;i++)
                {
                    float a=i*Mathf.PI*2/steps,b=(i+1)*Mathf.PI*2/steps;
                    Triangle(v,c,c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,c+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,color);
                }
            }
            static void Ring(VertexHelper v,Vector2 c,float radius,float width,Color color)
            {
                const int steps=40;
                for(int i=0;i<steps;i++)
                {
                    float a=i*Mathf.PI*2/steps,b=(i+1)*Mathf.PI*2/steps;
                    var p=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var q=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                    Triangle(v,c+p*(radius-width*.5f),c+p*(radius+width*.5f),c+q*(radius+width*.5f),color);
                    Triangle(v,c+p*(radius-width*.5f),c+q*(radius+width*.5f),c+q*(radius-width*.5f),color);
                }
            }
            static void Triangle(VertexHelper v,Vector2 a,Vector2 b,Vector2 c,Color color)
            {int i=v.currentVertCount;v.AddVert(a,color,Vector2.zero);v.AddVert(b,color,Vector2.zero);v.AddVert(c,color,Vector2.zero);v.AddTriangle(i,i+1,i+2);}
        }
    }
}
