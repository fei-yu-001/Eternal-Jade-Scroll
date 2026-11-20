using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [DefaultExecutionOrder(-200)]
    public sealed class JadeLotusRotator : MonoBehaviour
    {
        public static readonly string[] Labels = { "乾坤", "纳戒", "舆图", "攻击", "经脉", "万法", "道身", "落卷" };
        public static readonly string[] IconIds = { "qiankun", "najie", "yutu", "xundao", "jingmai", "wanfa", "daoshen", "guijuan" };
        public const string BaseArtKey = "nov7-v2/wheel/base", RotorArtKey = "nov7-v2/wheel/rotor";
        public const float BaseSize = 352f, RotorSize = 352f;
        // Final two COMPLETE lotus layers use the final prototype's1:1 layout, uniformly sized352 for the production HUD and untouched source UV.
        // Official jade-ring fit: source (477.008,417.858), median radial error2.603px. Keep bytes and full UV; validate the visual result with real frames.
        public static readonly Rect OfficialArtworkUv = new Rect(0,0,1,1);
        public static readonly Vector2 SourceJadeCentre = new Vector2(477.008f,417.858f);
        public static readonly Vector2 RotorArtworkOffset = new Vector2((480f-477.008f)*RotorSize/960f,(417.858f-480f)*RotorSize/960f);
        // Measured from each painted jade boundary, in the original 960px artwork.
        // A single source coordinate drives the hit target, icon, caption and gold ring.
        public static readonly Vector2[] SourceSlotCentres={new Vector2(476.5f,139.5f),new Vector2(686.5f,220.5f),
            new Vector2(770.5f,420.5f),new Vector2(680.5f,620.5f),new Vector2(477.5f,701.5f),
            new Vector2(268.5f,618.5f),new Vector2(182.5f,421.5f),new Vector2(264.5f,216.5f)};
        public static Vector2 SlotArtworkPosition(int slot)
        {var p=SourceSlotCentres[slot]-SourceJadeCentre;return new Vector2(p.x,-p.y)*(RotorSize/960f);}
        static readonly Rect[] BakedLabelRects = {
            new Rect(420,153,118,56),new Rect(641,243,123,55),new Rect(728,440,126,60),new Rect(642,638,128,57),
            new Rect(420,720,130,60),new Rect(200,640,132,60),new Rect(108,440,132,60),new Rect(198,239,132,62),
            new Rect(330,814,300,80) }; // Original baked bottom title, covered locally without touching source bytes.
        readonly JadeWheelMotion motion = new JadeWheelMotion();
        readonly Button[] buttons = new Button[8];
        readonly RectTransform[] upright = new RectTransform[8];
        readonly JadeWheelSignalGraphic[] focus = new JadeWheelSignalGraphic[8];
        readonly bool[] hovered = new bool[8], focused = new bool[8];
        readonly Texture2D[] icons = new Texture2D[8];
        RectTransform rotor, fixedBase;
        RawImage baseArt, rotorArt;
        TMP_Text warmupLabel;
        string[] liveLabels = Labels;
        double requestClock, sampledClock, alignedClock = -1;
        JadeWheelSignalGraphic halo;
        Func<bool> reduced, available;
        Action beginInput, releaseInput, consumeEscape;
        Action<int> activate;
        Action<JadeLotusRotator> disposed;
        AudioSource effects;
        AudioClip rotateCue, settleCue;
        int dispatchSlot = -1, dispatchRevision, dispatchFrame, activeSlot;
        int selectedSlot = -1; // No persistent highlight before an accepted user selection.
        bool configured, inputHeld;
        public int ActivationCount { get; private set; }
        public int RotationSoundCount { get; private set; }
        public int ArrivalSoundCount { get; private set; }
        public int LastActivatedSlot { get; private set; } = -1;
        public float LogicalAngle => (float)motion.Angle;
        public float TargetLogicalAngle => (float)motion.TargetAngle;
        public float AngularVelocity => (float)motion.Velocity;
        public double MonotonicRequestClock => requestClock;
        public double MonotonicAlignmentClock => alignedClock;
        public int TargetSlot => motion.TargetSlot;
        public int ActiveSlot => activeSlot;
        public bool Moving => motion.Moving;
        public bool PendingActivation => motion.Armed || dispatchSlot >= 0;
        public bool InputHeld => inputHeld;
        public RectTransform Rotor => rotor;
        public RectTransform FixedBase => fixedBase;
        public Button SlotButton(int slot) => slot >= 0 && slot < buttons.Length ? buttons[slot] : null;
        public bool HasApprovedArtwork => baseArt != null && baseArt.texture != null && rotorArt != null && rotorArt.texture != null && Array.TrueForAll(icons, t => t != null);
        public static int OwnedClipCount { get; private set; }
        public TMP_FontAsset WarmedBrushFontForCheck { get; private set; }
        public string FontWarmupCharactersForCheck { get; private set; } = "";
        public string FontWarmupMissingForCheck { get; private set; } = "";
        public string FontWarmupSubmittedForCheck { get; private set; } = "";
        public bool FontWarmupSucceededForCheck { get; private set; }
        public double FontWarmupMillisecondsForCheck { get; private set; }
        public int FontCharactersBeforeWarmupForCheck { get; private set; }
        public int FontCharactersAfterWarmupForCheck { get; private set; }
        public static JadeLotusRotator Create(UiKit ui, Transform parent, Func<bool> lowMotion, Func<bool> canUse,
            Action grabInput, Action freeInput, Action consume, Action<int> onSettled, AudioSource output, Action<JadeLotusRotator> onDisposed, string[] slotNames = null)
        {
            var root = ui.Rect(parent,"JadeWheelHub",1552,696,BaseSize,BaseSize);
            var hit = root.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
            var baseNode=Center(ui,root,"LotusBase",BaseSize,BaseSize,Vector2.zero);
            var rotorNode=Center(ui,root,"SkillLotusRotor",RotorSize,RotorSize,Vector2.zero);
            var view = rotorNode.gameObject.AddComponent<JadeLotusRotator>();
            view.reduced=lowMotion;view.available=canUse;view.beginInput=grabInput;view.releaseInput=freeInput;view.consumeEscape=consume;view.activate=onSettled;view.effects=output;view.disposed=onDisposed;
            view.liveLabels=slotNames!=null&&slotNames.Length==8?(string[])slotNames.Clone():Labels;
            view.fixedBase = baseNode;
            view.baseArt = Artwork(Center(ui,view.fixedBase,"Wheel official base artwork",BaseSize,BaseSize,Vector2.zero),ApprovedUiArt.Load(BaseArtKey));
            view.baseArt.uvRect=OfficialArtworkUv;((JadeWheelArtGraphic)view.baseArt).Configure(false);
            view.rotor = rotorNode;
            view.rotorArt = Artwork(Center(ui,view.rotor,"Wheel official rotor integral taiji artwork",RotorSize,RotorSize,RotorArtworkOffset),ApprovedUiArt.Load(RotorArtKey));
            view.rotorArt.uvRect=OfficialArtworkUv;((JadeWheelArtGraphic)view.rotorArt).Configure(true);
            for(int i=0;i<BakedLabelRects.Length-1;i++)
            {
                var r=BakedLabelRects[i];float scale=RotorSize/960f;
                var cover=Center(ui,view.rotor,"Wheel baked caption cover "+i,(r.width+16)*scale,(r.height+16)*scale,
                    new Vector2((r.center.x-480)*scale,(480-r.center.y)*scale)+RotorArtworkOffset).gameObject.AddComponent<JadeWheelSignalGraphic>();
                cover.Configure(JadeWheelSignalGraphic.Shape.BakedTextCover,1);
            }
            // Integral central taiji is part of this rotating image, never a fixed sibling texture.
            for(int i=0;i<8;i++)
            {
                int slot=i;var point=SlotArtworkPosition(i);
                var rect=Center(ui,view.rotor,"Jade wheel "+view.liveLabels[i],64,64,point);
                var graphic=rect.gameObject.AddComponent<Image>();graphic.color=Color.clear;
                var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=graphic;button.transition=Selectable.Transition.None;
                button.onClick.AddListener(()=>view.Select(slot));view.buttons[i]=button;
                var outline=Center(ui,rect,"Wheel slot focus "+i,74,74,Vector2.zero).gameObject.AddComponent<JadeWheelSignalGraphic>();outline.Configure(JadeWheelSignalGraphic.Shape.Focus,0);view.focus[i]=outline;
                var readable=Center(ui,rect,"Wheel upright content "+i,64,64,Vector2.zero);view.upright[i]=readable;
                view.icons[i]=ApprovedUiArt.Load("nov7-v2/wheel/icon-"+IconIds[i]);
                var icon=Center(ui,readable,"Wheel icon "+IconIds[i],26,26,new Vector2(0,14)).gameObject.AddComponent<RawImage>();icon.texture=view.icons[i];icon.raycastTarget=false;
                var captionPlate=Center(ui,readable,"Wheel live name plate "+i,62,38,new Vector2(0,-18)).gameObject.AddComponent<JadeWheelSignalGraphic>();
                captionPlate.Configure(JadeWheelSignalGraphic.Shape.Caption,1);
                var label=V2ScrollUi.Text(ui,readable,"Wheel label "+i,view.liveLabels[i],0,0,60,38,23,false,TextAlignmentOptions.Center,UiKit.Paper);
                PlaceCentre(label.rectTransform,new Vector2(0,-18));
                if(i==0)view.warmupLabel=label;
                rect.gameObject.AddComponent<JadeWheelSlotInput>().Configure(view,i);
            }
            for(int i=0;i<8;i++)view.buttons[i].navigation=new Navigation { mode=Navigation.Mode.Explicit,
                selectOnRight=view.buttons[(i+1)%8],selectOnDown=view.buttons[(i+1)%8],selectOnLeft=view.buttons[(i+7)%8],selectOnUp=view.buttons[(i+7)%8] };
            // Slot captions provide the name in place; no detached function bubble above the wheel.
            view.WarmActiveSlotNames(); // Product page creation, before any rotation or input can be accepted.
            view.halo=Center(ui,view.rotor,"Wheel taiji glow",166,166,Vector2.zero).gameObject.AddComponent<JadeWheelSignalGraphic>();
            view.halo.Configure(JadeWheelSignalGraphic.Shape.Halo,.32f);view.halo.transform.SetAsLastSibling();
            view.rotateCue=MakeCue(false);view.settleCue=MakeCue(true);OwnedClipCount+=2;
            view.configured=true;view.ApplyPose();return view;
        }
        void WarmActiveSlotNames()
        {
            double start=Time.realtimeSinceStartupAsDouble;
            WarmedBrushFontForCheck=warmupLabel.font;FontWarmupCharactersForCheck=string.Concat(liveLabels);
            FontCharactersBeforeWarmupForCheck=WarmedBrushFontForCheck.characterLookupTable.Count;
            string missing="";
            foreach(char c in FontWarmupCharactersForCheck)
                if(!WarmedBrushFontForCheck.characterLookupTable.ContainsKey(c)&&missing.IndexOf(c)<0)missing+=c;
            FontWarmupSubmittedForCheck=missing;
            if(missing.Length>0)WarmedBrushFontForCheck.TryAddCharacters(missing,out _);
            // TMP returns false when there are no new glyphs. The actual final table defines success and real missing characters.
            missing="";
            foreach(char c in FontWarmupCharactersForCheck)
                if(!WarmedBrushFontForCheck.characterLookupTable.ContainsKey(c)&&missing.IndexOf(c)<0)missing+=c;
            FontWarmupMissingForCheck=missing;FontWarmupSucceededForCheck=missing.Length==0;
            FontCharactersAfterWarmupForCheck=WarmedBrushFontForCheck.characterLookupTable.Count;
            FontWarmupMillisecondsForCheck=(Time.realtimeSinceStartupAsDouble-start)*1000;
            // Keep initialization cost and any missing glyphs visible to diagnostics; no fake timed interaction or fixture warmup.
        }
        static RectTransform Center(UiKit ui,Transform parent,string name,float w,float h,Vector2 at)
        {var rect=ui.Rect(parent,name,0,0,w,h);PlaceCentre(rect,at);return rect;}
        static void PlaceCentre(RectTransform rect,Vector2 at)
        {rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=at;}
        static RawImage Artwork(RectTransform rect,Texture2D texture)
        {var image=rect.gameObject.AddComponent<JadeWheelArtGraphic>();image.texture=texture;image.raycastTarget=false;image.enabled=texture!=null;return image;}
        static AudioClip MakeCue(bool arrival)
        {var data=JadeWheelTone.Render(arrival);var clip=AudioClip.Create(arrival?"Wheel jade arrival (original synthesis)":"Wheel stone rotation (original synthesis)",data.Length,1,JadeWheelTone.Rate,false);clip.SetData(data,0);return clip;}
        public void Select(int slot)
        {
            if(!configured||!isActiveAndEnabled||!(available?.Invoke()??false))return;
            requestClock=sampledClock=Time.realtimeSinceStartupAsDouble;alignedClock=-1;
            dispatchSlot=-1;motion.Request(slot,reduced?.Invoke()??true);
            selectedSlot=slot;
            if(!inputHeld){inputHeld=true;beginInput?.Invoke();}
            if(motion.Moving&&effects!=null){effects.PlayOneShot(rotateCue);RotationSoundCount++;}
            ApplyPose();if(!motion.Moving)alignedClock=Time.realtimeSinceStartupAsDouble;
        }
        public void PointerDown(int slot)
        {
            if(!configured||!(available?.Invoke()??false))return;
            // A press about to choose another badge must suppress a queued old module, even before mouse-up.
            dispatchSlot=-1;motion.Disarm();RestoreActivatedSelection();
        }
        public void CancelPending()
        {dispatchSlot=-1;motion.Cancel();RestoreActivatedSelection();alignedClock=-1;FreeInput();ApplyPose();}
        public void Focus(int slot,bool value){focused[slot]=value;RefreshFocus();}
        public void Hover(int slot,bool value){hovered[slot]=value;RefreshFocus();}
        void Update()
        {
            if(!configured)return;
            if(!(available?.Invoke()??false)){if(PendingActivation||inputHeld)CancelPending();return;}
            var keyboard=Keyboard.current;
            if(keyboard!=null)
            {
                int shortcut=keyboard.bKey.wasPressedThisFrame?1:keyboard.mKey.wasPressedThisFrame?2:-1;
                if(shortcut>=0){if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(buttons[shortcut].gameObject);Select(shortcut);}
            }
            if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame&&(PendingActivation||motion.Moving))
            {consumeEscape?.Invoke();CancelPending();return;}
            if(keyboard!=null&&keyboard.tabKey.wasPressedThisFrame&&EventSystem.current!=null)
            {
                int at=Array.FindIndex(buttons,b=>b!=null&&b.gameObject==EventSystem.current.currentSelectedGameObject);
                if(at>=0){int step=keyboard.leftShiftKey.isPressed||keyboard.rightShiftKey.isPressed?-1:1;EventSystem.current.SetSelectedGameObject(buttons[(at+step+8)%8].gameObject);}
            }
            // Sample from the accepted Select event, not the previous Unity frame's duration.
            double sample=Time.realtimeSinceStartupAsDouble,seconds=Math.Max(0,sample-sampledClock);sampledClock=sample;
            motion.Advance(seconds,reduced?.Invoke()??true);ApplyPose();
            if(!motion.Moving&&motion.Armed&&alignedClock<0)alignedClock=Time.realtimeSinceStartupAsDouble;
            if(motion.TakeArrival(out int slot))
            {
                activeSlot=slot;RefreshFocus();
                if(effects!=null){effects.PlayOneShot(settleCue);ArrivalSoundCount++;}
                dispatchSlot=slot;dispatchRevision=motion.Revision;dispatchFrame=Time.frameCount+2;FreeInput();
            }
            else if(!motion.Moving&&!motion.Armed&&dispatchSlot<0)FreeInput();
        }
        void LateUpdate()
        {
            if(dispatchSlot<0||Time.frameCount<dispatchFrame)return;
            int slot=dispatchSlot;dispatchSlot=-1;
            if(!configured||dispatchRevision!=motion.Revision||motion.Moving||!(available?.Invoke()??false))
            {RestoreActivatedSelection();return;}
            LastActivatedSlot=slot;ActivationCount++;activate?.Invoke(slot);
        }
        void ApplyPose()
        {
            if(rotor==null)return;float angle=(float)motion.Angle;
            rotor.localRotation=Quaternion.Euler(0,0,-angle);
            foreach(var labelRoot in upright)if(labelRoot!=null)labelRoot.localRotation=Quaternion.Euler(0,0,angle);
            // Only slot labels counter-rotate; transient focus overrides the explicit persistent selection.
            if(halo!=null)halo.SetStrength((reduced?.Invoke()??true)?.32f:.32f+(.10f*(1+Mathf.Sin(Time.unscaledTime*Mathf.PI*2/2.5f))));
            RefreshFocus();
        }
        void RefreshFocus()
        {
            float breath=(reduced?.Invoke()??true)?1:.90f+.10f*Mathf.Sin(Time.unscaledTime*Mathf.PI*2/2.5f);
            int target=Array.FindLastIndex(hovered,value=>value);
            if(target<0)target=Array.FindLastIndex(focused,value=>value);
            if(target<0)target=selectedSlot;
            for(int i=0;i<focus.Length;i++)if(focus[i]!=null)
                focus[i].SetStrength(i==target?.78f*breath:0);
        }
        // Cancelled or rejected pending choices must not outlive the last dispatched activation.
        void RestoreActivatedSelection(){selectedSlot=LastActivatedSlot;RefreshFocus();}
        void FreeInput(){if(!inputHeld)return;inputHeld=false;releaseInput?.Invoke();}
        void OnDisable(){dispatchSlot=-1;motion.Cancel();FreeInput();Array.Clear(hovered,0,hovered.Length);Array.Clear(focused,0,focused.Length);RestoreActivatedSelection();}
        void OnDestroy()
        {
            configured=false;dispatchSlot=-1;FreeInput();
            if(rotateCue!=null){Destroy(rotateCue,.15f);rotateCue=null;OwnedClipCount--;}
            if(settleCue!=null){Destroy(settleCue,.15f);settleCue=null;OwnedClipCount--;}
            // Let the short arrival one-shot finish on the existing effects source after a page switch.
            disposed?.Invoke(this);disposed=null;
            reduced=available=null;activate=null;beginInput=releaseInput=consumeEscape=null;
        }
    }
}
