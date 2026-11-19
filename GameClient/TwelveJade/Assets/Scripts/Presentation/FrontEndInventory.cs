using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using TwelveJade.Combat;
using TwelveJade.Economy;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // 纳戒：七七定版贴图上的24个真格位、八个装备热区、类别页签与真实行囊操作。
    // 规则全在 Core 的 InventoryRules 里，这里只管画面与输入；格子尺寸与位置由常量算出，
    // 空档按空格补满 24 格——存档里也始终是 24 格。
    public sealed partial class FrontEndController
    {
        const int BagColumns = 5, BagRows = 5;
        const float BagCell = 120f, BagGap = 9f;

        const string BagRestoredOverlayArt = "nov9/inventory-live-overlay-restored";
        bool bagArtRegistrationWarning;

        ItemTable itemTable;
        RecipeTable recipeTable;
        GameObject bagOverlay;
        GameObject craftOverlay;
        TMP_Text craftNotice;
        RectTransform bagGrid;
        readonly List<RectTransform> bagCells = new();
        readonly List<RawImage> bagCellIcons = new();
        readonly List<TMP_Text> bagCellCounts = new();
        readonly List<Button> bagCellButtons = new();
        TMP_Text bagHint, bagSummary, bagBagLine;
        RectTransform bagTip;
        TMP_Text bagTipTitle, bagTipBody;
        Button bagEquipButton,bagCraftButton;
        ScrollRect bagDetailScroll;
        int bagTipSlot = -1;
        string bagCategory = "";
        int bagSelected = -1;
        int bagDragFrom = -1;
        // 丢弃防手滑（ADR-029）：状态在 Economy 的纯逻辑层，界面只负责把 notice 念出来。
        readonly DiscardGuard discardGuard = new();
        // 名字要写全：UnityEngine.Random 也在作用域里，裸写 Random 是不明确的引用。
        readonly System.Random craftRng = new System.Random();

        // 五档页签：id 是筛选键（不是类别 id），name 是按钮上的字。
        static readonly (string id, string name)[] BagTabs =
        {
            ("", "全部"), ("equipment", "装备"), ("consumable", "丹药"), ("material", "材料"), ("relic", "信物")
        };

        static BagFilterKind FilterKindOf(string tab)
        {
            switch (tab)
            {
                case "equipment": return BagFilterKind.Equipment;
                case "consumable": return BagFilterKind.Consumable;
                case "material": return BagFilterKind.Material;
                case "relic": return BagFilterKind.Relic;
                default: return BagFilterKind.All;
            }
        }

        ItemTable Table
        {
            get
            {
                if (itemTable != null) return itemTable;
                var asset = Resources.Load<TextAsset>("Config/items");
                if (asset == null)
                {
                    Debug.LogWarning("Missing Resources/Config/items.json; the bag falls back to an empty table.");
                    // 兜底表也要装标记表：命途门控在"没装表"时会当场响（T098），
                    // 而缺配置是可恢复的降级，不该以一次抛错收场。
                    ItemFlags.Load(EmptyTableJson);
                    return itemTable = ItemTable.Parse(EmptyTableJson);
                }
                try
                {
                    itemTable = ItemTable.Parse(asset.text);
                    // 顺手把"煞气门控"这类附加标记装上：同一张表，不另开一份真相。
                    ItemFlags.Load(asset.text);
                }
                catch (FormatException ex)
                {
                    Debug.LogWarning("items.json is malformed: " + ex.Message);
                    ItemFlags.Load(EmptyTableJson);
                    itemTable = ItemTable.Parse(EmptyTableJson);
                }
                return itemTable;
            }
        }

        // 手艺配方表（recipes.json）：与物品表同一套加载与兜底口径。
        // 早前这张表在运行期根本没人加载——方子写在盘上，玩家却没有一处能做，
        // 判据全绿也只绿在服务层。这里把它接进 Resources 的加载路径上。
        RecipeTable Recipes
        {
            get
            {
                if (recipeTable != null) return recipeTable;
                var asset = Resources.Load<TextAsset>(RecipeTable.ResourcePath);
                if (asset == null)
                {
                    Debug.LogWarning("Missing Resources/" + RecipeTable.ResourcePath +
                        "; the kitchen and the furnace have no recipes to work from.");
                    return null;
                }
                try
                {
                    recipeTable = RecipeTable.Parse(asset.text);
                    recipeTable.CrossCheck(Table);
                }
                catch (FormatException ex)
                {
                    Debug.LogWarning("recipes.json is malformed: " + ex.Message);
                    recipeTable = null;
                }
                return recipeTable;
            }
        }

        // 兜底表：物品表缺失时行囊仍能打开，已有物品按 id 显示为"未登记之物"，不丢东西。
        const string EmptyTableJson =
            "{\"tiers\":[{\"id\":\"fanpin\",\"name\":\"凡品\",\"color\":\"#8C8C86\"}]," +
            "\"categories\":[{\"id\":\"qita\",\"name\":\"杂物\"}]," +
            "\"items\":[{\"id\":\"unknown-item\",\"name\":\"未登记之物\",\"tier\":\"fanpin\"," +
            "\"category\":\"qita\",\"icon\":\"missing-item\",\"stack\":99,\"price\":0," +
            "\"description\":\"这一件来历不明，像是从别处流过来的。\"}]}";

        public void ShowInventory()
        {
            if (bagOverlay != null) { bagOverlay.SetActive(true); userBag = activeSave?.bag; RedrawBag(); return; }
            if (activeSave == null) { Notify("先落笔创建一位行旅人，再打开行囊。"); return; }
            if (WorldDialogueInputBlocked || modal != null) return;
            CancelTravel();
            userBag = activeSave.bag ?? InventoryRules.NewBag();
            activeSave.bag = userBag;
            bagSelected = -1;
            bagCategory = "";
            if (content != null)
            { var group = content.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false; }
            bagOverlay = ui.Rect(canvas, "Inventory", 0, 0, 1920, 1080).gameObject;
            var veil = bagOverlay.AddComponent<Image>();
            veil.color = new Color(0f, 0f, 0f, .72f);
            veil.raycastTarget = true;
            // 点格外的暗底才收行囊：面板自己的图片会吃掉射线，不会误触。
            var outside = bagOverlay.AddComponent<Button>();
            outside.targetGraphic = veil;
            outside.transition = Selectable.Transition.None;
            outside.onClick.AddListener(CloseInventory);
            bagOverlay.AddComponent<V2FocusNavigation>();
            RedrawBag();
        }

        ItemStack[] userBag;

        // 验收脚本用的最小接口：不打开面板也能查当前格数与提示格。
        public ItemStack[] CurrentBag => userBag;
        public int BagCellCount => bagCells.Count;
        public int BagTipSlot => bagTipSlot;
        public string BagCategory => bagCategory;
        // 验收用：上一次点「使用」被拒时那句诚实的话——回血效果在镇上没接入，光看数量不变
        // 分不清"拒绝了"和"偷偷吃了没生效"，所以把话本身做成可断言的输出。
        public string LastBagUseRefusal { get; private set; } = "";

        public void CloseInventory()
        {
            if (bagOverlay == null) return;
            CloseCrafting();
            CancelInvoke(nameof(ShowTipNow));
            discardGuard.Reset();
            bagOverlay.SetActive(false);
            Destroy(bagOverlay);
            bagOverlay = null;
            bagGrid = null;
            bagCells.Clear();
            bagCellIcons.Clear();
            bagCellCounts.Clear();
            bagCellButtons.Clear();
            bagTip = null;
            bagTipSlot = -1;
            bagDragFrom = -1;
            bagEquipButton = bagCraftButton = null; bagDetailScroll = null;
            if (content != null)
            { var group = content.GetComponent<CanvasGroup>(); group.interactable = true; group.blocksRaycasts = modal == null; }
        }

        // 页签：全部 + 物品表里定义的每一类。类别是数据，不是枚举——加一类 JSON 即可。
        void RedrawBag()
        {
            if (bagOverlay == null) return;
            CancelInvoke(nameof(ShowTipNow)); bagTipSlot = -1;
            foreach (Transform old in bagOverlay.transform) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
            bagCells.Clear(); bagCellIcons.Clear(); bagCellCounts.Clear(); bagCellButtons.Clear();
            var box = BuildBagBackdrop();
            // Remove the painted legacy tabs and duplicate close symbols as well as their hit targets.
            // Each cover samples real empty wood from this same approved painting; no flat-color patch.
            BagWoodCover(box,"Bag quiet header",new Rect(0,0,1920,141));
            BagWoodCover(box,"Bag inner close restoration",new Rect(1730,129,128,70));
            BagWoodCover(box,"Bag quiet footer",new Rect(34,1001,1852,79));
            AestheticUi.Text(ui,box,"Bag page title","须 弥 · 储 物 囊",new Rect(72,34,820,68),38,TextAlignmentOptions.MidlineLeft,ImmortalUi.Pale);
            AestheticUi.Jade(ui,box,"Bag close","收起",new Rect(1658,30,204,76),CloseInventory,()=>settings.reduceMotion,PlayClick);
            AestheticUi.Jade(ui,box,"Bag artisan codex","百工",new Rect(1396,30,226,76),ShowCrafting,()=>settings.reduceMotion,PlayClick);
            ImmortalUi.Text(ui,box,"Bag character heading","真 容",75,153,138,53,28,ImmortalUi.Gold,false,TextAlignmentOptions.Center);
            ImmortalUi.Text(ui,box,"Bag equipment heading","行 装",571,153,144,53,28,ImmortalUi.Gold,false,TextAlignmentOptions.Center);
            // Keep the approved RGBA actor, horizontal crop and size. Its visible sole differs
            // by five source rows between genders; seat both near y797 on the restored oval.
            float footOffset = activeSave.gender == "female" ? -3f : 3f;
            ImmortalUi.Hero(ui,box,activeSave.gender,224,205 + footOffset,340,595);
            BuildBagEquipment(box);
            ImmortalUi.Text(ui,box,"Bag storage heading","须 弥 纳 戒",1400,141,300,53,31,ImmortalUi.Gold,false,TextAlignmentOptions.Center);
            for (int i=0;i<BagTabs.Length;i++)
            {
                var entry=BagTabs[i];
                ImmortalUi.Hotspot(ui,box,"Bag filter "+entry.id,entry.name,1205+i*129,210,120,45,
                    ()=>{bagCategory=entry.id;bagSelected=-1;RedrawBag();},23,bagCategory==entry.id?ImmortalUi.Gold:ImmortalUi.Pale);
            }
            // 24 real slots, registered to the master's five-column carved bays. The 25th bay is an emblem.
            bagGrid=ui.Rect(box,"Bag grid",1204.5f,270,636,620);
            for(int i=0;i<InventoryRules.SlotCount;i++)
            {
                int index=i;int col=i%BagColumns,row=i/BagColumns;
                var cell=ui.Rect(bagGrid,"Cell "+i,col*129,row*124.5f,120,120);
                var skin=ImmortalUi.Art(ui,cell,"Native empty carved bay","nov8/ledger-slot",0,0,120,120,true);
                var button=cell.gameObject.AddComponent<Button>();button.targetGraphic=skin;button.transition=Selectable.Transition.None;
                button.onClick.AddListener(()=>BagClick(index));
                cell.gameObject.AddComponent<ImmortalControlFeedback>().Configure(null,skin);
                var trigger=cell.gameObject.AddComponent<EventTrigger>();
                var enter=new EventTrigger.Entry{eventID=EventTriggerType.PointerEnter};enter.callback.AddListener(_=>BagHover(index));trigger.triggers.Add(enter);
                var exit=new EventTrigger.Entry{eventID=EventTriggerType.PointerExit};
                exit.callback.AddListener(_=>{CancelInvoke(nameof(ShowTipNow));if(bagSelected<0)HideTip();else ShowTip(bagSelected);});trigger.triggers.Add(exit);
                cell.gameObject.AddComponent<BagDragHandler>().Init(this,index);
                ImmortalUi.Text(ui,cell,"Bag index "+i,(i+1).ToString("00"),9,7,44,27,16,ImmortalUi.Ash);
                var icon=ui.Rect(cell,"Icon",17,21,86,76).gameObject.AddComponent<RawImage>();icon.raycastTarget=false;icon.color=Color.clear;
                var count=ImmortalUi.Text(ui,cell,"Bag count "+i,"",67,78,43,32,22,ImmortalUi.Pale,false,TextAlignmentOptions.BottomRight);
                bagCells.Add(cell);bagCellIcons.Add(icon);bagCellCounts.Add(count);bagCellButtons.Add(button);
            }
            ImmortalUi.Texture(ui,bagGrid,"Twenty-four-slot bag seal",ApprovedUiArt.Load("memo/seal-cinnabar"),537,520,74,74);
            bagSummary=ImmortalUi.Text(ui,box,"Bag real coins","",78,1020,640,48,26,ImmortalUi.Pale);
            bagBagLine=ImmortalUi.Text(ui,box,"Bag real capacity","",1206,901,630,40,24,ImmortalUi.Gold);
            bagHint=ImmortalUi.Text(ui,box,"Bag interaction hint","点选详阅 · 拖拽换位 · 双击使用",1206,946,630,46,22,ImmortalUi.Pale);
            AestheticUi.Jade(ui,box,"Bag sort","整理",new Rect(1610,1007,226,66),BagSort,()=>settings.reduceMotion,PlayClick);
            BuildTip(box);RefreshBag();
            if(bagSelected>=0)ShowTip(bagSelected);
            if(EventSystem.current!=null && bagCellButtons.Count>0)
                EventSystem.current.SetSelectedGameObject(bagCellButtons[Mathf.Clamp(bagSelected,0,bagCellButtons.Count-1)].gameObject);
        }

        RectTransform BuildBagBackdrop()
        {
            var backdrop = ImmortalUi.Art(ui,bagOverlay.transform,"Inventory Masterpiece",
                "nov8/inventory-bg",0,0,1920,1080,true);
            var box = backdrop != null ? backdrop.rectTransform
                : ui.Rect(bagOverlay.transform,"Inventory Masterpiece",0,0,1920,1080);
            // The old overlay stretches a wood rectangle across the actor and the back half
            // of the taiji. The page-specific repair removes only the painted concept actor,
            // preserving the original wood, complete oval and all other live-data patches.
            var restoration = ApprovedUiArt.Has(BagRestoredOverlayArt)
                ? ImmortalUi.Art(ui,box,"Inventory Masterpiece live-data restoration",BagRestoredOverlayArt,0,0,1920,1080)
                : null;
            if (restoration == null)
            {
                const string legacyOverlay = "nov8/inventory-live-overlay";
                if (ApprovedUiArt.Has(legacyOverlay))
                    ImmortalUi.Art(ui,box,"Inventory Masterpiece live-data restoration",legacyOverlay,0,0,1920,1080);
                if (!bagArtRegistrationWarning)
                {
                    Debug.LogWarning("Inventory backdrop repair is missing its registered texture: " + BagRestoredOverlayArt);
                    bagArtRegistrationWarning = true;
                }
            }
            // Restoration stays below the transparent saved hero and equipment hit targets.
            return box;
        }

        void BagWoodCover(RectTransform parent,string name,Rect box)
        {
            var patch=ImmortalUi.Art(ui,parent,name,"nov8/inventory-bg",box.x,box.y,box.width,box.height,true);
            if(patch!=null)patch.uvRect=new Rect(250f/1920f,1f-192f/1080f,130f/1920f,22f/1080f);
        }

        void BuildBagEquipment(RectTransform parent)
        {
            var names=new[]{"道冠","法衣","腰带","步履","兵刃","宝镜","纳戒","佩饰"};
            var slots=new[]{"",GearSlots.Armor,"","",GearSlots.Weapon,"","",GearSlots.Accessory};
            var ys=new[]{231f,412.5f,651f,834f};
            for(int i=0;i<8;i++)
            {
                int index=i;float x=i%2==0?72:568.5f,y=ys[i/2];string slot=slots[i];
                var line=string.IsNullOrEmpty(slot)?null:GearLedger.Of(activeSave,slot);
                var def=line==null?null:Table.Find(line.itemId);
                if(def!=null && InventoryRules.Count(userBag,def.Id)>0)
                    ImmortalUi.Texture(ui,parent,"Equipped "+slot,Resources.Load<Texture2D>("Art/Items/"+def.Icon),x+22,y+14,102,98);
                else ImmortalUi.Text(ui,parent,"Gear empty "+i,string.IsNullOrEmpty(slot)?"未开放":"未佩",x+8,y+36,131,45,24,ImmortalUi.Ash,false,TextAlignmentOptions.Center);
                ImmortalUi.Text(ui,parent,"Gear name "+i,names[i],x+6,y+103,133,38,22,ImmortalUi.Gold,false,TextAlignmentOptions.Center);
                if(string.IsNullOrEmpty(slot))continue; // Unimplemented equipment positions are labels, never fake buttons.
                ImmortalUi.Hotspot(ui,parent,"Gear slot "+i,"",x,y,147,147,()=>
                {
                    if(string.IsNullOrEmpty(slots[index])){bagHint.text=names[index]+"尚无可装备条目。";return;}
                    bagCategory="equipment";bagSelected=-1;RedrawBag();
                    if(line!=null) {int owned=Array.FindIndex(userBag,b=>b!=null&&!b.IsEmpty&&b.id==line.itemId);if(owned>=0)BagClick(owned);}
                });
            }
        }

        void BuildTip(RectTransform parent)
        {
            bagTip=ui.Rect(parent,"Item tip",813,230,312,661);
            bagTipTitle=ImmortalUi.Text(ui,bagTip,"Bag detail title","",9,8,294,76,29,ImmortalUi.Ink,true,TextAlignmentOptions.Center);
            var view=ui.Rect(bagTip,"Bag detail viewport",10,98,292,416);view.gameObject.AddComponent<RectMask2D>();
            var hit=view.gameObject.AddComponent<Image>();hit.color=Color.clear;
            bagDetailScroll=view.gameObject.AddComponent<ScrollRect>();bagDetailScroll.viewport=view;
            bagDetailScroll.horizontal=false;bagDetailScroll.vertical=true;bagDetailScroll.movementType=ScrollRect.MovementType.Clamped;
            bagDetailScroll.inertia=!settings.reduceMotion;bagDetailScroll.scrollSensitivity=30;
            bagTipBody=ImmortalUi.Text(ui,view,"Bag live item or attributes","",4,0,284,416,24,ImmortalUi.Ink);
            bagDetailScroll.content=bagTipBody.rectTransform;
            ImmortalUi.Text(ui,bagTip,"Tip tier","",10,525,292,62,22,ImmortalUi.Ink);
            bagEquipButton=AestheticUi.Jade(ui,bagTip,"Bag equip action","穿戴所选",new Rect(25,591,262,66),BagEquipSelected,()=>settings.reduceMotion,PlayClick);
            bagEquipButton.gameObject.SetActive(false);
            bagCraftButton=AestheticUi.Jade(ui,bagTip,"Bag recipe action","查看配方",new Rect(25,591,262,66),ShowCrafting,()=>settings.reduceMotion,PlayClick);
            bagCraftButton.gameObject.SetActive(false);ResetTipText();
        }
        void FitBagDetails()
        {
            if(bagTipBody==null)return;
            bagTipBody.rectTransform.sizeDelta=new Vector2(284,Mathf.Max(416,bagTipBody.GetPreferredValues(bagTipBody.text,284,float.PositiveInfinity).y+12));
            if(bagDetailScroll!=null){bagDetailScroll.StopMovement();bagDetailScroll.verticalNormalizedPosition=1;}
        }
        void ResetTipText()
        {
            if(bagTipTitle==null)return;
            var stats=PlayerStatsFor(activeSave.characterId);
            bagTipTitle.text="本 命 根 基";bagTipTitle.color=ImmortalUi.Ink;
            bagTipBody.text=activeSave.characterName+"\n\n气血上限  "+(stats.hp+CultivationRules.HpBonus(activeSave)+ManualStudyRules.HpBonus(activeSave))+
                "\n精力上限  "+(stats.stamina+CultivationRules.StaminaBonus(activeSave)+ManualStudyRules.StaminaBonus(activeSave))+"\n基础力道  "+(stats.attack+ManualStudyRules.AttackBonus(activeSave))+"\n基础防御  "+stats.defense+
                "\n\n命格\n"+(CharacterGen.FindDestiny(activeSave.destiny)?.Name??"命星待照")+"\n\n经脉已通  "+CultivationRules.Opened(activeSave)+" / 5";
            var tier=bagTip.Find("Tip tier")?.GetComponent<TMP_Text>();if(tier!=null){tier.text="点选物品阅其来历\n滚轮可展开长文";tier.color=ImmortalUi.Ink;}
            if(bagEquipButton!=null)bagEquipButton.gameObject.SetActive(false);
            if(bagCraftButton!=null)bagCraftButton.gameObject.SetActive(false);FitBagDetails();
        }
        void BagEquipSelected()
        {
            if(activeSave==null||bagSelected<0||bagSelected>=userBag.Length)return;
            var stack=userBag[bagSelected];if(stack==null||stack.IsEmpty)return;
            var entry=CombatGearCatalog?.Find(stack.id);if(entry==null)return;
            string slot=entry.Slot==GearSlot.Weapon?GearSlots.Weapon:entry.Slot==GearSlot.Armor?GearSlots.Armor:GearSlots.Accessory;
            var old=(activeSave.gear??Array.Empty<GearLine>()).Select(g=>new GearLine{slot=g.slot,itemId=g.itemId,durability=g.durability}).ToArray();
            var worn=GearLedger.Of(activeSave,slot);
            if(worn!=null&&worn.itemId==entry.ItemId)GearLedger.Clear(activeSave,slot);
            else GearLedger.Record(activeSave,slot,entry.ItemId,entry.MaxDurability);
            try{repository.Write(activeSave);PlayClick();RedrawBag();}
            catch(Exception ex){activeSave.gear=old;Debug.LogError("Inventory gear save failed: "+ex.Message);Notify("穿戴未能落卷，已恢复原装备。");}
        }

        public void ShowTip(int index)
        {
            if (bagTip == null || userBag == null) return;
            var stack = index >= 0 && index < userBag.Length ? userBag[index] : null;
            if (stack == null || stack.IsEmpty) { HideTip(); return; }
            var def = Table.Find(stack.id);
            if(!BagFilterAllows(def)){HideTip();return;}
            bagTipSlot = index;
            var reputation = activeSave?.localReputation ?? 0;
            bagTipTitle.text = def?.Name ?? ItemInspector.UnknownName;
            // 品阶色与正文一并交给 Economy 的 ItemInspector 生成：界面上报的买卖价
            // 必须就是货郎真正收付的那个数（Core 的 Trade），不是另一套模型的。
            var tierColor = ItemInspector.TierColor(Table, def);
            bagTipTitle.color = ImmortalUi.Ink;
            bagTipBody.text = ItemInspector.Body(Table, def, stack.count, reputation);
            var tierText = bagTip.Find("Tip tier")?.GetComponent<TMP_Text>();
            if (tierText != null)
            {
                tierText.text = "品阶 · " + ItemInspector.TierName(Table, def);
                tierText.color = ImmortalUi.Ink;
            }
            var gear=def==null?null:CombatGearCatalog?.Find(def.Id);
            // Crafting is a real operation, so retain it contextually for relevant materials/results.
            // It no longer occupies a permanent footer button or appears on unrelated empty states.
            if(bagCraftButton!=null)bagCraftButton.gameObject.SetActive(gear==null&&def!=null&&bagSelected==index&&
                Recipes!=null&&Recipes.Recipes.Any(r=>r.ResultItemId==def.Id||r.Ingredients.Any(i=>i.ItemId==def.Id)));
            if(bagEquipButton!=null)
            {
                bagEquipButton.gameObject.SetActive(gear!=null && bagSelected==index);
                if(gear!=null)
                {
                    string slot=gear.Slot==GearSlot.Weapon?GearSlots.Weapon:gear.Slot==GearSlot.Armor?GearSlots.Armor:GearSlots.Accessory;
                    bagEquipButton.GetComponentInChildren<TMP_Text>().text=GearLedger.Of(activeSave,slot)?.itemId==gear.ItemId?"卸下此物":"穿戴此物";
                }
            }
            FitBagDetails();
        }

        public void HideTip()
        {
            bagTipSlot = -1;
            if (bagTip != null) ResetTipText();
        }

        bool BagFilterAllows(ItemDef def)
        {
            // 未登记之物只在【全部】里露面：它连类别都没有，不该被哪一档认领。
            if (def == null) return string.IsNullOrEmpty(bagCategory);
            return BagService.Allows(FilterKindOf(bagCategory), def.Category);
        }

        void RefreshBag()
        {
            if (userBag == null) return;
            for (var i = 0; i < bagCells.Count; i++)
            {
                var stack = userBag[i];
                var def = stack == null || stack.IsEmpty ? null : Table.Find(stack.id);
                var visible = stack != null && !stack.IsEmpty && BagFilterAllows(def);
                var icon = bagCellIcons[i];
                var texture = def == null || !visible ? null : Resources.Load<Texture2D>("Art/Items/" + def.Icon);
                icon.texture = texture;
                icon.color = new Color(1, 1, 1, texture != null ? 1f : 0f);
                bagCellCounts[i].text = visible && stack.count > 1 ? stack.count.ToString() : "";
                var background = bagCellButtons[i].targetGraphic;
                var selected = bagSelected == i;
                background.color = selected ? new Color(1.55f, 1.35f, 1.05f, 1) : Color.white;
                if(texture!=null)
                {
                    float scale=Mathf.Min(86f/texture.width,76f/texture.height);
                    icon.rectTransform.sizeDelta=new Vector2(texture.width*scale,texture.height*scale);
                    icon.rectTransform.anchoredPosition=new Vector2(17+(86-texture.width*scale)*.5f,-21-(76-texture.height*scale)*.5f);
                }
            }
            var used = InventoryRules.UsedSlots(userBag);
            var total = userBag.Sum(stack => stack == null ? 0 : stack.count);
            if(bagSummary!=null)bagSummary.text=activeSave.coins+" 文";
            if(bagBagLine!=null)bagBagLine.text="行囊 "+used+" / "+InventoryRules.SlotCount+" 格 · 共 "+total+" 件";
            if(bagHint!=null)bagHint.text=bagSelected>=0&&bagSelected<userBag.Length&&!userBag[bagSelected].IsEmpty?
                "已选「"+(Table.Find(userBag[bagSelected].id)?.Name??"未登记之物")+"」 · 拖拽换位":"点选详阅 · 拖拽换位 · 双击使用";
        }

        void BagHover(int index)
        {
            // 浮签延迟 150ms 弹出，避免鼠标扫过时一路刮蹭。
            CancelInvoke(nameof(ShowTipNow));
            bagTipSlot = index;
            Invoke(nameof(ShowTipNow), .15f);
        }

        void ShowTipNow()
        {
            if (bagTipSlot < 0 || bagTipSlot >= bagCells.Count) return;
            if (!bagCells[bagTipSlot].gameObject.activeInHierarchy) return;
            ShowTip(bagTipSlot);
        }

        public void BagClick(int index)
        {
            if (userBag == null || index < 0 || index >= userBag.Length) return;
            CancelInvoke(nameof(ShowTipNow));
            if(userBag[index]!=null&&!userBag[index].IsEmpty&&!BagFilterAllows(Table.Find(userBag[index].id)))return;
            // 换了选中格就等于改主意：上一格攒下的"已警告一次"不作数。
            if (bagSelected != index) discardGuard.Reset();
            bagSelected = bagSelected == index ? -1 : index;
            if (bagSelected >= 0 && userBag[index] != null && !userBag[index].IsEmpty) ShowTip(index);
            else HideTip();
            RefreshBag();
        }

        // 双击 = 使用。本轮（D-09 / O6）行囊**不使用任何入口物**：气血与精力只存在于战斗内
        // （ADR-034），丹药由战斗页"服药"结算（员工 N 域），吃食的体力 buff 尚未做。
        // 所以这里一句"用了"都不许编——既不扣数量，也不写任何效果。判据在 BagUseRules 里。
        public void BagUse(int index)
        {
            if (userBag == null || activeSave == null) return;
            if (index < 0 || index >= userBag.Length) return;
            var stack = userBag[index];
            if (stack == null || stack.IsEmpty) return;
            var refusal = BagUseRules.RefusalFor(Table.Find(stack.id));
            LastBagUseRefusal = refusal;
            Notify(refusal);
            if (BagUseRules.Consumes(Table.Find(stack.id)))
            {
                activeSave.bag = userBag;
                PersistBag();
            }
            RefreshBag();
        }

        // 智能理包：走 Economy 的 BagService（同类合并 → 品阶降序 → 空槽归尾），
        // 不再用 Core 的 InventoryRules.Sort——那个按表内声明顺序排，且已被
        // SaveChecks/InventoryChecks 钉住（"sort is tidy and lossless" 断言 ganliang 在首位）。
        public void BagSort()
        {
            if (userBag == null) return;
            var before = BagService.TotalCount(userBag);
            BagService.SortAndCompactBag(userBag, Table);
            activeSave.bag = userBag;
            PersistBag();
            bagSelected = -1;
            HideTip(); RefreshBag();
            // 理包丢件是最难自查的一类 bug：这里当场对一次账，对不上立刻喊出来。
            Notify(BagService.TotalCount(userBag) == before
                ? "行囊已整理：同类归堆，品阶在前的靠前。"
                : "整理前后件数对不上（" + before + " → " + BagService.TotalCount(userBag) + "），请报给管事的。");
        }

        /// <summary>丢弃当前选中的一件：同一格连点两次才真撒手（ADR-029 防手滑），
        /// 剧情信物在第一次点击就被拒掉——丢了它，后面的戏就没了。</summary>
        public void BagDiscardSelected()
        {
            if (userBag == null || activeSave == null) return;
            string removedId=bagSelected>=0&&bagSelected<userBag.Length?userBag[bagSelected]?.id:null;
            var dropped = discardGuard.TryDiscard(userBag, Table, bagSelected, out var notice);
            if (dropped)
            {
                activeSave.bag = userBag;
                if(!string.IsNullOrEmpty(removedId)&&InventoryRules.Count(userBag,removedId)==0)
                    foreach(var line in (activeSave.gear??Array.Empty<GearLine>()).Where(g=>g.itemId==removedId).ToArray())
                        GearLedger.Clear(activeSave,line.slot);
                PersistBag();RedrawBag();
            }
            Notify(notice);
            RefreshBag();
            // 丢掉的是这一格最后一件：浮签继续留着，就是在展示一件已经不在手里的东西。
            if (bagSelected >= 0 && bagSelected < userBag.Length && userBag[bagSelected].IsEmpty) HideTip();
        }

        // ---------------- 手艺：灶台与药炉 ----------------

        string craftCategory="forging";
        string lastCraftNotice="";
        static string StationName(string station)
        {
            switch(station){case "kitchen-stove":return "灶台";case "herbal-furnace":return "丹炉";case "smith-anvil":return "锻炉";default:return "手作";}
        }
        public void ShowCrafting()
        {
            if(activeSave==null)return;
            var recipes=Recipes;if(recipes==null){Notify("百工谱尚未备齐。 ");return;}
            userBag=activeSave.bag;
            CloseCrafting();
            craftOverlay=ui.Rect(canvas,"Crafting",0,0,1920,1080).gameObject;
            var veil=craftOverlay.AddComponent<Image>();veil.color=new Color(.035f,.055f,.05f,.76f);veil.raycastTarget=true;
            var outside=craftOverlay.AddComponent<Button>();outside.targetGraphic=veil;outside.transition=Selectable.Transition.None;outside.onClick.AddListener(CloseCrafting);
            var box=ui.Rect(craftOverlay.transform,"Artisan scroll",145,110,1630,860);
            var backdrop=ui.Rect(box,"Silk scroll ground",0,0,1630,860).gameObject.AddComponent<RawImage>();backdrop.texture=ApprovedUiArt.Load("nov11/artisan-silk-page");backdrop.raycastTarget=true;
            var picture=Resources.Load<Texture2D>(craftCategory=="forging"?"Art/UI/Nov11/forge-workshop":"Art/UI/Nov11/alchemy-workshop");
            if(picture!=null){var image=ui.Rect(box,"Painted artisan scene",1005,120,570,570).gameObject.AddComponent<RawImage>();image.texture=picture;image.uvRect=new Rect(.47f,0,.53f,1);image.raycastTarget=false;}
            ui.Label(box,"百工仙谱",55,34,700,60,39,UiKit.Ink,TextAlignmentOptions.Left,false);
            MenuMemoArt.Command(ui,box,"Close craft","收卷",new Rect(1374,25,200,74),CloseCrafting,()=>settings.reduceMotion,PlayClick,25,false);
            string[] kinds={"forging","alchemy","cooking"};string[] names={"炼器","炼药","膳食"};
            for(int i=0;i<kinds.Length;i++)
            {var kind=kinds[i];MenuMemoArt.Command(ui,box,"Craft category "+kind,(craftCategory==kind?"· ":"")+names[i],new Rect(55+i*294,112,275,72),()=>{craftCategory=kind;ShowCrafting();},()=>settings.reduceMotion,PlayClick,26,false);}
            var viewport=ui.Rect(box,"Recipe viewport",48,210,936,548);viewport.gameObject.AddComponent<Image>().color=Color.clear;viewport.gameObject.AddComponent<RectMask2D>();
            var list=ui.Rect(viewport,"Recipe content",0,0,936,1);var entries=recipes.OfType(craftCategory).ToList();list.sizeDelta=new Vector2(936,Mathf.Max(548,entries.Count*178));
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=list;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=46;scroll.inertia=false;
            for(int i=0;i<entries.Count;i++)DrawRecipeRow(list,entries[i],i*178);
            craftNotice=ui.Label(box,string.IsNullOrEmpty(lastCraftNotice)?"采集矿材，温养药性。材料与产物均记入行囊。":lastCraftNotice,58,785,1500,45,24,UiKit.Ink,TextAlignmentOptions.Left,false);
            ui.Label(box,craftCategory=="forging"?"炉火淬青锋\n玄铁 · 玉脉 · 妖兽材料":"丹砂养灵息\n草药 · 兽胆 · 青灵草",1050,698,460,76,25,UiKit.Ink,TextAlignmentOptions.Center,false);
        }
        void DrawRecipeRow(RectTransform parent,RecipeDef recipe,float y)
        {
            var row=ui.Rect(parent,"Recipe "+recipe.Id,0,y,936,164);
            var panel=ui.Rect(row,"Faint silk ink",0,0,936,160).gameObject.AddComponent<RawImage>();panel.texture=ApprovedUiArt.Load("nov11/artisan-silk-page");panel.color=new Color(1,1,1,.18f);panel.raycastTarget=false;
            var result=Table.Find(recipe.ResultItemId);var icon=result==null?null:Resources.Load<Texture2D>("Art/Items/"+result.Icon);
            if(icon!=null)ui.Art(row,icon,12,22,108,108).raycastTarget=false;
            ui.Label(row,result?.Name??recipe.Name,138,15,535,40,27,UiKit.Ink,TextAlignmentOptions.Left,false);
            var bag=userBag??activeSave.bag;var materials=recipe.Ingredients.Select(x=>(Table.Find(x.ItemId)?.Name??x.ItemId)+" "+InventoryRules.Count(bag,x.ItemId)+"/"+x.Count);
            ui.Label(row,string.Join("  ·  ",materials),138,61,554,56,20,UiKit.Hex("596353"),TextAlignmentOptions.Left,false);
            ui.Label(row,StationName(recipe.Station)+" · 已制 "+CraftLedger.TimesOf(activeSave, recipe.Id)+" 次 · 双出 "+Mathf.RoundToInt(Crafting.DoubleChanceFor(recipe, activeSave)*100)+"%",138,121,480,29,19,UiKit.Hex("806D49"),TextAlignmentOptions.Left,false);
            bool can=Crafting.HasIngredients(bag,recipe)&&Crafting.HasRoomFor(bag,Table,recipe.ResultItemId,recipe.ResultCount);
            var button=MenuMemoArt.Command(ui,row,"Craft "+recipe.Id,can?"开炉":"材料未齐",new Rect(700,46,210,77),()=>CraftOne(recipe.Id),()=>settings.reduceMotion,PlayClick,22,false);button.interactable=can;
        }

        void CraftOne(string recipeId)
        {
            if (activeSave == null) return;
            var recipe = Recipes?.Find(recipeId);
            if (recipe == null) return;
            if (userBag == null) userBag = activeSave.bag ?? InventoryRules.NewBag();
            activeSave.bag = userBag;
            var priorBag=InventoryRules.Normalize(activeSave.bag,Table);
            var priorCrafts=(activeSave.crafts??Array.Empty<CraftLine>()).Select(c=>new CraftLine{recipeId=c.recipeId,times=c.times}).ToArray();
            var made = Crafting.Craft(activeSave, Table, recipe, craftRng, out var notice);
            if(made>0)
            {
                try{repository.Write(activeSave);}
                catch(Exception ex){activeSave.bag=priorBag;userBag=priorBag;activeSave.crafts=priorCrafts;made=0;notice="开炉未能记档，材料已保留。";Debug.LogError(ex.Message);}
                if(made>0){DriveQuests(QuestSignal.None());Guard(()=>repository.Write(activeSave));}
            }
            lastCraftNotice = notice;
            if (craftNotice != null) craftNotice.text = notice;
            else Notify(notice);
            RedrawBag();
            if (craftOverlay != null) ShowCrafting();
        }

        void CloseCrafting()
        {
            if (craftOverlay == null) return;
            Destroy(craftOverlay);
            craftOverlay = null;
            craftNotice = null;
        }

        public void BagDrop(int from, int to)
        {
            if (userBag == null || from < 0 || to < 0 || from == to) return;
            if (InventoryRules.TryMove(userBag, Table, from, to))
            {
                activeSave.bag = userBag;
                PersistBag();
                bagSelected = to;
            }
            RefreshBag();
        }

        void PersistBag()
        {
            // 行囊每一次真实变化都是一次对账机会（T083 的第二个运行期调用点）：
            // 做出来的"粗布袍"能顶上一条 collect 目标，结完算的发奖也进同一个 Write。
            DriveQuests(QuestSignal.None());
            Guard(() => repository.Write(activeSave));
        }

        // 拖拽：用 IDragHandler 记录来源，用 IDropHandler 在目标格落位。
        sealed class BagDragHandler : MonoBehaviour, IBeginDragHandler, IDropHandler, IPointerClickHandler
        {
            FrontEndController owner;
            int index;
            public void Init(FrontEndController controller, int slot) { owner = controller; index = slot; }

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (owner.userBag == null || index >= owner.userBag.Length) return;
                owner.bagDragFrom = owner.userBag[index].IsEmpty ? -1 : index;
            }

            public void OnDrop(PointerEventData eventData)
            {
                if (owner.bagDragFrom < 0) return;
                owner.BagDrop(owner.bagDragFrom, index);
                owner.bagDragFrom = -1;
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.clickCount >= 2) owner.BagUse(index);
            }
        }
    }
}
