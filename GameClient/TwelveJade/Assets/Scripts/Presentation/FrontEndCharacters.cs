using System;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        string draftGender = SaveData.Genders[0];
        int draftFaceStyle;
        int draftOrigin;
        string draftName = "未定名";
        string[] draftTraits = Array.Empty<string>();
        string draftDestiny = "";
        TMP_InputField nameInput;

        int selectedMemoSlot = 1;
        int memoDraftSlot;
        Vector2 memoViewport;
        RectTransform memoPaper;
        readonly MemoScrollOrnament[] memoScrolls = new MemoScrollOrnament[SaveRepository.SlotCount];
        public int SelectedMemoSlot => selectedMemoSlot;
        public const float MemoRailFraction = .22f;
        public static readonly string[] MemoInformationFields = { "name", "identity", "age", "realm", "sect", "roots",
            "traits", "destiny", "coins", "spirit", "location", "clock", "elapsed", "timestamp" };
        static readonly Color MemoInk = UiKit.Hex("2E2117"), MemoHeading = UiKit.Hex("6B512E");

        public void ShowSlots(bool creating)
        {
            var all = repository.ReadAll();
            if (creating)
                selectedMemoSlot = all.FirstOrDefault(s => s.State == SlotState.Empty)?.Slot ?? selectedMemoSlot;
            selectedMemoSlot = Mathf.Clamp(selectedMemoSlot, 1, SaveRepository.SlotCount);
            RenderMemo(all);
        }

        void RefreshSlotLayout()
        {
            if (page != "slots" || modal != null || screenCanvas == null) return;
            var size = screenCanvas.rect.size;
            if ((size - memoViewport).sqrMagnitude > 1f) RenderMemo(repository.ReadAll());
        }

        void RenderMemo(SlotInfo[] all)
        {
            BeginPage("slots");
            memoPaper = null;
            memoViewport = screenCanvas.rect.size;
            MenuMemoArt.Command(ui, content, "Slots back", "回卷", new Rect(40, 32, 168, 64), ShowMenu, () => settings.reduceMotion, PlayClick);
            var rail = ui.Stretch(content, "Slots rail");
            rail.anchorMax = new Vector2(MemoRailFraction, 1);
            foreach (var slot in all)
            {
                int id = slot.Slot;
                var scroll = ui.Rect(rail, "Slot " + id, 0, 0, 126, 514).gameObject.AddComponent<MemoScrollOrnament>();
                scroll.Configure(MemoScrollOrnament.Surface.HangingScroll, id == selectedMemoSlot, () => settings.reduceMotion);
                memoScrolls[id - 1] = scroll;
                scroll.rectTransform.anchorMin = scroll.rectTransform.anchorMax = new Vector2(.185f + (id - 1) * .325f, .5f);
                scroll.rectTransform.pivot = new Vector2(.5f, .5f); scroll.rectTransform.anchoredPosition = Vector2.zero;
                var button = scroll.gameObject.AddComponent<Button>();
                button.targetGraphic = scroll; button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => { PlayClick(); SelectMemoSlot(id); });
                var scrollTitle = ui.Label(scroll.transform, "存\n档\n" + new[] { "一", "二", "三" }[id - 1],
                    20, 86, 86, 180, 43, MemoInk, TextAlignmentOptions.Center, true);
                scrollTitle.name = "Slot " + id + " title"; scrollTitle.richText = false;
                scrollTitle.enableAutoSizing = false; scrollTitle.extraPadding = false;
                scrollTitle.margin = new Vector4(2, 2, 2, 2);
                scrollTitle.overflowMode = TextOverflowModes.Ellipsis;
                SdfInkWeight.Apply(scrollTitle);
                var imprint=MemoSealAction.Record(ui, scroll.transform, "Slot " + id + " cinnabar seal", 41, 278);
                // Keep the paper cinnabar impression; remove the hard bevel slab from these hanging folios only.
                var bevel=imprint.GetComponent<MemoSealGraphic>();if(bevel!=null)bevel.enabled=false;
                MemoLabel(scroll.transform, "Slot " + id + " seal glyph", new[] { "甲", "乙", "丙" }[id - 1],
                    43, 277, 40, 40, 25, TextAlignmentOptions.Center, true).color = UiKit.Hex("9B3B2B");
                string state = slot.State == SlotState.Empty ? "待落笔" : slot.State == SlotState.Recovered ? "备卷" :
                    slot.CanLoad ? "已落笔" : slot.State == SlotState.FutureVersion ? "异世卷" : "待修复";
                MemoLabel(scroll.transform, "Slot " + id + " seal", state, 16, 330, 94, 38, 19, TextAlignmentOptions.Center);
                // The full return time remains in the selected dossier; the folio keeps a quiet silk lower cap.
                scroll.SetVacant(slot.State == SlotState.Empty);
            }
            RefreshMemoDossier(all);
            EventSystem.current.SetSelectedGameObject(memoScrolls[selectedMemoSlot - 1].gameObject);
        }

        void SelectMemoSlot(int slot)
        {
            // Selection is synchronous and reuses all three scrolls. Dossier refresh never restarts the page fade.
            selectedMemoSlot = slot;
            foreach (var scroll in memoScrolls) if (scroll != null) scroll.SetSelected(scroll == memoScrolls[slot - 1]);
            RefreshMemoDossier(repository.ReadAll());
        }

        void RefreshMemoDossier(SlotInfo[] all)
        {
            if (memoPaper != null) { memoPaper.gameObject.SetActive(false); Destroy(memoPaper.gameObject); }
            nameInput = null;
            var chosen = all.First(s => s.Slot == selectedMemoSlot);
            bool empty = chosen.State == SlotState.Empty;
            if (empty && memoDraftSlot != chosen.Slot)
            {
                memoDraftSlot = chosen.Slot; currentDraftSlot = chosen.Slot;
                draftOrigin = 0; draftGender = SaveData.Genders[0]; draftFaceStyle = 0; draftName = "未定名";
                RerollDraftFate();
            }
            var dossier = ui.Rect(content, "Slot dossier", 0, 0, 0, 0).gameObject.AddComponent<MemoScrollOrnament>();
            dossier.Configure(MemoScrollOrnament.Surface.Paper, false, () => settings.reduceMotion);
            var paper = dossier.rectTransform; memoPaper = paper;
            paper.anchorMin = new Vector2(.24f, .08f); paper.anchorMax = new Vector2(.98f, .85f);
            paper.offsetMin = paper.offsetMax = Vector2.zero; paper.pivot = new Vector2(.5f, .5f);
            float width = Mathf.Max(1920, memoViewport.x) * .74f;
            float bodyX = 674, bodyW = width - bodyX - 44;
            var data = chosen.CanLoad ? chosen.Data : null;
            var preset = data != null ? presets.FirstOrDefault(p => p.id == data.characterId) : empty ? presets[draftOrigin] : null;
            var poster = ui.Rect(paper, "Dossier portrait", 38, 88, 600, 650).gameObject.AddComponent<PosterCardView>();
            poster.Configure(preset?.id, data?.gender ?? (empty ? draftGender : "male"),true);
            MenuMemoArt.FramePortrait(poster);
            if (!poster.HasArtwork)
                MemoLabel(poster.transform, "Portrait awaiting record", empty || data != null ? "身世待访" : "旧影待复",
                    22, 270, 556, 64, 28, TextAlignmentOptions.Center, true);
            MemoLabel(paper, "Dossier timestamp", "归来时刻\n" + (data == null ? empty ? "行卷初启" : "旧时难考" :
                MemoTimestamp(data.updatedUtc, "yyyy.MM.dd  HH:mm")), 38, 742, 600, 80, 20, TextAlignmentOptions.Center);
            string title = data != null ? (string.IsNullOrWhiteSpace(data.characterName) ? "未定名" : data.characterName) : empty ?
                (string.IsNullOrWhiteSpace(draftName) ? "未定名" : draftName) : chosen.State == SlotState.FutureVersion ? "异世来卷" : "旧卷待修";
            var titleLabel = MemoLabel(paper, "Dossier name", title, bodyX, 20, bodyW, 84, 48, TextAlignmentOptions.MidlineLeft, true);
            titleLabel.color = UiKit.Hex("1E1E1E"); titleLabel.enableAutoSizing = true;
            titleLabel.fontSizeMin = 28; titleLabel.fontSizeMax = 48; TextWrapCompat.SetWrapping(titleLabel, false);
            if (empty) BuildEmptyMemo(paper, bodyX, bodyW, titleLabel);
            else BuildRecordedMemo(paper, chosen, preset, bodyX, bodyW);
            var enter = MenuMemoArt.Command(ui,paper,"Slot enter",empty?"立传开卷":"承卷入世",new Rect(0,0,276,76),
                () => { if(empty)CreateFromDraft(chosen.Slot);else LoadSlot(repository.Read(chosen.Slot)); },()=>settings.reduceMotion,PlayClick,28,false);
            enter.interactable=empty||chosen.CanLoad;
            var enterRect = (RectTransform)enter.transform;
            enterRect.anchorMin = enterRect.anchorMax = new Vector2(1, 0); enterRect.pivot = new Vector2(1, 0);
            enterRect.anchoredPosition = new Vector2(-40, 20);
            if (!empty)
            {
                var erase = MenuMemoArt.Command(ui,paper,"Slot delete","归尘",new Rect(0,0,154,66),
                    () => Confirm("此卷归尘？", "将移除第" + new[] { "一", "二", "三" }[chosen.Slot - 1] +
                        "卷的行迹与备卷。落笔不易，归尘后无法找回；其余两卷仍会保留。", "归尘", () =>
                        { repository.Delete(chosen.Slot); memoDraftSlot = 0; ShowSlots(false); Notify("此卷已归尘。其余行迹仍在。"); }),
                    () => settings.reduceMotion,PlayClick,26,false);
                var rect = (RectTransform)erase.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(1,0); rect.pivot = new Vector2(1,0);
                rect.anchoredPosition = new Vector2(-340, 26);
            }
        }

        void BuildRecordedMemo(Transform paper, SlotInfo chosen, CharacterPreset preset, float x, float width)
        {
            var data = chosen.CanLoad ? chosen.Data : null;
            string locked = chosen.State == SlotState.FutureVersion ? "待新世解卷" : "卷损待校";
            string identity = data == null ? "此卷暂不可读，原有行迹仍保留。" :
                (preset != null ? preset.displayName : "来路待访") + " · " + CharacterPreset.GenderName(data.gender);
            if (chosen.State == SlotState.Recovered) identity += " · 将从有效备卷恢复";
            if (chosen.State == SlotState.FutureVersion) identity = "此卷来自更新的游戏版本，请保留待归。";
            MemoLabel(paper, "Dossier identity", identity, x, 108, width, 42, 22);
            float col = (width - 24) * .5f;
            MemoValue(paper, "age", "年岁骨龄", data == null ? locked : "年岁待考", x, 170, col);
            MemoValue(paper, "realm", "修真境界", data == null ? locked : "仙途未明", x + col + 24, 170, col);
            // SaveData has no sect membership. Unknown membership must not be asserted as unaffiliated.
            MemoValue(paper, "sect", "门派宗流", data == null ? locked : "宗门未明", x, 248, col);
            MemoValue(paper, "roots", "灵根", data == null ? locked : "天机未明", x + col + 24, 248, col);
            DaoIdentityView.Roots(ui,paper,"Dossier roots",new Rect(x,330,width,80));
            BuildMemoFate(paper, x, 418, width, data?.traits, data?.destiny, data == null ? locked : null, false);
            BuildMemoWealth(paper, x, 522, width, data == null ? locked : data.coins.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " 文",
                data == null ? locked : "未入仙途", 24);
            BuildMemoJourney(paper, x, 584, width, data, data == null ? locked : null, 24);
        }

        void BuildEmptyMemo(Transform paper, float x, float width, TMP_Text title)
        {
            MemoLabel(paper, "Dossier identity", presets[draftOrigin].displayName + " · " + CharacterPreset.GenderName(draftGender) +
                " · 择来路，落笔入世。", x, 108, width, 38, 21);
            nameInput = ui.Input(paper, draftName, x, 144, width);
            nameInput.name = "Draft name";
            MenuMemoArt.StyleNameField(nameInput, () => settings.reduceMotion);
            nameInput.onValueChanged.AddListener(value => { draftName = value; title.text = string.IsNullOrWhiteSpace(value) ? "未定名" : value; });
            float each = (width - 20) / 3;
            for (int i = 0; i < presets.Length; i++)
            {
                int origin = i;
                MemoOriginPlaque.Create(ui, paper, "Draft origin " + presets[i].id, presets[i].displayName,
                    x + i * (each + 10), 216, each, 54,
                    () => { draftOrigin = origin; RefreshMemoDossier(repository.ReadAll()); FocusMemoControl("Draft origin " + presets[origin].id); },
                    () => settings.reduceMotion, draftOrigin == i, PlayClick);
            }
            MenuMemoArt.Choice(ui, paper, "Draft gender male", draftGender == "male" ? "男 · 已择" : "男", new Rect(x, 280, 170, 52),
                () => { draftGender = "male"; RefreshMemoDossier(repository.ReadAll()); FocusMemoControl("Draft gender male"); }, () => settings.reduceMotion, draftGender == "male", PlayClick);
            MenuMemoArt.Choice(ui, paper, "Draft gender female", draftGender == "female" ? "女 · 已择" : "女", new Rect(x + 184, 280, 170, 52),
                () => { draftGender = "female"; RefreshMemoDossier(repository.ReadAll()); FocusMemoControl("Draft gender female"); }, () => settings.reduceMotion, draftGender == "female", PlayClick);
            float col = (width - 24) * .5f;
            MemoLabel(paper, "Dossier age", "年岁骨龄 · 年岁待考", x, 346, col, 34, 22);
            MemoLabel(paper, "Dossier realm", "修真境界 · 仙途未明", x + col + 24, 346, col, 34, 22);
            MemoLabel(paper, "Dossier sect", "门派宗流 · 宗门未明", x, 386, col, 34, 22);
            MemoLabel(paper, "Dossier roots", "灵根 · 天机未明", x + col + 24, 386, col, 34, 22);
            DaoIdentityView.Roots(ui,paper,"Draft roots",new Rect(x,426,width,80));
            BuildMemoFate(paper, x, 508, width, draftTraits, draftDestiny, null, true);
            BuildMemoWealth(paper, x, 588, width, "行装待备", "未入仙途", 21);
            BuildMemoJourney(paper, x, 632, width, null, "行卷初启", 20);
        }

        void FocusMemoControl(string name)
        {
            var control = memoPaper.Find(name);
            if (control != null) EventSystem.current.SetSelectedGameObject(control.gameObject);
        }

        void MemoValue(Transform paper, string name, string caption, string value, float x, float y, float width)
        {
            MemoLabel(paper, "Caption " + name, caption, x, y, width, 32, 20).color = MemoHeading;
            MemoLabel(paper, "Dossier " + name, value, x, y + 34, width, 38, 25);
        }

        void BuildMemoFate(Transform paper, float x, float y, float width, string[] traits, string destiny, string locked, bool compact)
        {
            var group = ui.Rect(paper, "Dossier fate", x, y, width, compact ? 76 : 94);
            string traitText = string.Join(" · ", (traits ?? Array.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => CharacterGen.FindTrait(id)?.Name ?? id + "（未识名）"));
            string destinyText = string.IsNullOrWhiteSpace(destiny) ? "命星待照" : CharacterGen.FindDestiny(destiny)?.Name ?? destiny + "（未识名）";
            MemoLabel(group, "Dossier traits", "先天词条 · " + (locked ?? (traitText.Length == 0 ? "天赋待显" : traitText)),
                0, 0, width, compact ? 36 : 50, compact ? 21 : 24);
            MemoLabel(group, "Dossier destiny", "命格 · " + (locked ?? destinyText), 0, compact ? 38 : 54, width, 36, compact ? 21 : 24);
        }

        void BuildMemoWealth(Transform paper, float x, float y, float width, string coins, string spirit, float size)
        {
            var group = ui.Rect(paper, "Dossier wealth", x, y, width, 42);
            float col = (width - 24) * .5f;
            ui.Art(group, MemoVisualAssets.GetTexture("icon-coins"), 0, 0, 30, 30).name = "Memo coin icon";
            ui.Art(group, MemoVisualAssets.GetTexture("icon-spirit"), col + 24, 0, 30, 30).name = "Memo spirit icon";
            MemoLabel(group, "Dossier coins", "铜钱 · " + coins, 38, 0, col - 38, 38, size);
            MemoLabel(group, "Dossier spirit", "灵石 · " + spirit, col + 62, 0, col - 38, 38, size);
        }

        void BuildMemoJourney(Transform paper, float x, float y, float width, SaveData data, string pending, float size)
        {
            // At 24px the bundled serif needs 34.49px line height in the real canvas.
            // Preserve the font size; give each row its measured height plus spacing.
            float rowHeight = size >= 24 ? 36f : 32f, rowStep = rowHeight + 2f;
            var group = ui.Rect(paper, "Dossier journey", x, y, width, rowStep * 2 + rowHeight);
            string place = data == null ? pending : string.IsNullOrWhiteSpace(data.location) || data.location == "CharacterPreview" ? "行卷初启" : data.location;
            string clock = data == null ? pending : "第 " + data.worldDay + " 日 " + (data.worldMinuteOfDay / 60).ToString("00") + ":" + (data.worldMinuteOfDay % 60).ToString("00");
            long elapsed = data == null ? 0 : Math.Max(0L, ((long)data.worldDay - WorldTime.StartDay) * 1440 + data.worldMinuteOfDay - WorldTime.StartMinute);
            string duration = data == null ? pending : elapsed / 1440 + " 日 " + elapsed % 1440 / 60 + " 时 " + elapsed % 60 + " 分";
            MemoLabel(group, "Dossier location", "驻足地点 · " + place, 0, 0, width, rowHeight, size);
            MemoLabel(group, "Dossier clock", "世间时辰 · " + clock, 0, rowStep, width, rowHeight, size);
            MemoLabel(group, "Dossier elapsed", "世界历时 · " + duration, 0, rowStep * 2, width, rowHeight, size);
        }

        TMP_Text MemoLabel(Transform parent, string name, string value, float x, float y, float w, float h,
            float size, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool brush = false)
        {
            // Keep small serif strokes legible over the translucent paper at a 1280px viewport.
            var label = ui.Label(parent, value, x, y, w, h, brush ? size : Mathf.Max(22, size), MemoInk, align, false);
            SdfInkWeight.Apply(label);label.extraPadding=false;
            label.margin = new Vector4(2, 2, 2, 2);
            label.name = name; label.richText = false;
            label.overflowMode = name.StartsWith("Dossier ", StringComparison.Ordinal)
                ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            return label;
        }

        static string MemoTimestamp(string value, string format) => DateTimeOffset.TryParse(value, out var date)
            ? date.ToLocalTime().ToString(format) : "旧时难考";

        // 从档位管理进入：重置整份草稿并掷一次命格。换外观、改性别等只重绘页面，不重置草稿。
        public void ShowCharacterCreation(int slot)
        {
            draftOrigin = 0;
            draftGender = SaveData.Genders[0];
            draftFaceStyle = 0;
            draftName = "无名";
            RerollDraftFate();
            RedrawCreation(slot);
        }

        public void SetCreationOrigin(int index)
        {
            draftOrigin = Mathf.Clamp(index, 0, presets.Length - 1);
            RedrawCreation(currentDraftSlot);
        }

        public void SetCreationGender(string gender)
        {
            if (!SaveData.IsValidGender(gender)) return;
            draftGender = gender;
            RedrawCreation(currentDraftSlot);
        }

        public void SetCreationFaceStyle(int style)
        {
            if (!SaveData.IsValidFaceStyle(style)) return;
            draftFaceStyle = style;
            RedrawCreation(currentDraftSlot);
        }

        public void RerollCreationFate()
        {
            RerollDraftFate();
            RedrawCreation(currentDraftSlot);
        }

        void RerollDraftFate()
        {
            var rng = new System.Random();
            draftTraits = CharacterGen.RollTraits(rng);
            draftDestiny = CharacterGen.RollDestiny(rng);
        }

        // 问命：序章星夜问命。答案只为命格加权（底数 1，倾向 +3），命盘随答点亮；词条始终纯随机。
        int fateStep;
        readonly System.Collections.Generic.List<string> fateFavored = new();
        readonly System.Collections.Generic.Dictionary<string, RawImage> fateStars =
            new System.Collections.Generic.Dictionary<string, RawImage>();

        public void OpenFateDialogue()
        {
            CloseModal();
            content.GetComponent<CanvasGroup>().interactable = false;
            fateStep = 0;
            fateFavored.Clear();
            modal = OpenFateScene("Fate dialogue");
            ShowFateIntro();
        }

        // 序章共用底景：星夜渡口背景画压暗作底，无图时退回纯色遮罩。
        RectTransform OpenFateScene(string name)
        {
            var root = ui.Panel(canvas, name, 0, 0, 1920, 1080, new Color(0, .035f, .03f, .9f), true).rectTransform;
            var texture = Resources.Load<Texture2D>("Art/Background/fate-background");
            if (texture != null)
            {
                var art = ui.Art(root, texture, 0, 0, 1920, 1080);
                art.color = new Color(.62f, .72f, .7f, .92f);
                ui.Panel(root, "Fate veil", 0, 0, 1920, 1080, new Color(0f, .02f, .018f, .5f));
            }
            return root;
        }

        // 命盘：中央无名（卜算照不出），内环六星外环十二星对应其余命格；答问点亮倾向之星。
        // 星用中心锚点自成坐标（y 向上），其余 UI 元素一律沿用 UiKit 的左上原点、y 向下约定。
        void BuildFateBoard(Transform parent, bool animate)
        {
            fateStars.Clear();
            var board = ui.Panel(parent, "Fate board", 120, 170, 500, 640, new Color(.015f, .05f, .04f, .72f)).rectTransform;
            ui.Label(board, "命  盘", 0, 16, 500, 40, 22, UiKit.Gold, TextAlignmentOptions.Center, true);
            var center = new Vector2(0f, -30f);
            AddStar(board, CharacterGen.Destinies[18].Id, center, 24);
            for (var i = 0; i < 6; i++)
            {
                var angle = i * 60f * Mathf.Deg2Rad;
                AddStar(board, CharacterGen.Destinies[i].Id,
                    center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 84f, 17);
            }
            for (var i = 0; i < 12; i++)
            {
                var angle = (i * 30f + 15f) * Mathf.Deg2Rad;
                AddStar(board, CharacterGen.Destinies[6 + i].Id,
                    center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 150f, 13);
            }
            foreach (var id in fateFavored) LightStar(id);
            if (animate)
            {
                var driver = board.gameObject.AddComponent<FateSendOff>();
                driver.Init(fateStars.Values.Select(s => s.rectTransform).ToArray(), center, AddStar(board, "", center, 26));
            }
        }

        RectTransform FateBox(Transform parent)
        {
            return ui.Panel(parent, "Fate box", 680, 150, 1120, 780, UiKit.Dark).rectTransform;
        }

        RawImage AddStar(Transform parent, string destinyId, Vector2 position, float size)
        {
            var star = new GameObject("FateStar " + destinyId, typeof(RectTransform)).GetComponent<RectTransform>();
            star.SetParent(parent, false);
            star.anchorMin = star.anchorMax = new Vector2(.5f, .5f);
            star.pivot = new Vector2(.5f, .5f);
            star.anchoredPosition = position;
            star.sizeDelta = new Vector2(size, size);
            var image = star.gameObject.AddComponent<RawImage>();
            image.texture = StarTexture();
            image.raycastTarget = false;
            image.color = new Color(.45f, .62f, .55f, .2f);
            if (!string.IsNullOrEmpty(destinyId)) fateStars[destinyId] = image;
            return image;
        }

        void LightStar(string destinyId)
        {
            if (fateStars.TryGetValue(destinyId, out var star))
                star.color = new Color(1f, .86f, .5f, .95f);
        }

        static Texture2D starTexture;
        static Texture2D StarTexture()
        {
            if (starTexture != null) return starTexture;
            const int s = 48;
            starTexture = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (var y = 0; y < s; y++)
            for (var x = 0; x < s; x++)
            {
                var dx = (x + .5f) / s - .5f;
                var dy = (y + .5f) / s - .5f;
                var d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                var a = Mathf.Clamp01(1f - d);
                starTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
            starTexture.Apply(false, true);
            return starTexture;
        }

        // 序章各幕共用一只遮罩，内容面板逐幕重建，避免遮罩堆叠挡住射线。
        RectTransform FatePhasePanel()
        {
            var old = modal.Find("Fate content");
            if (old != null) Destroy(old.gameObject);
            return ui.Panel(modal, "Fate content", 0, 0, 1920, 1080, new Color(0, 0, 0, 0)).rectTransform;
        }

        void ShowFateIntro()
        {
            var panel = FatePhasePanel();
            BuildFateBoard(panel, false);
            var box = FateBox(panel);
            ui.Label(box, "星 夜 问 命", 48, 52, 1024, 84, 50, UiKit.Paper, TextAlignmentOptions.Center, true);
            ui.Label(box, FateDialogue.Opening, 108, 200, 904, 170, 30, UiKit.Gold, TextAlignmentOptions.Center, true);
            ui.Label(box, "答案不为定命，只为命盘添几星光。", 108, 410, 904, 44, 20, UiKit.Muted, TextAlignmentOptions.Center);
            ui.Button(box, "落座听问", 440, 630, 240, 68, ShowFateQuestion, true);
        }

        void ShowFateQuestion()
        {
            var panel = FatePhasePanel();
            BuildFateBoard(panel, false);
            var box = FateBox(panel);
            var question = FateDialogue.Questions[fateStep];
            ui.Label(box, question.Title, 48, 42, 780, 52, 30, UiKit.Gold, TextAlignmentOptions.TopLeft, true);
            ui.Label(box, string.Format("（{0} / {1}）", fateStep + 1, FateDialogue.Questions.Length),
                900, 52, 170, 40, 20, UiKit.Muted, TextAlignmentOptions.TopRight);
            ui.Label(box, question.Text, 48, 118, 1024, 96, 28, UiKit.Paper);
            for (var i = 0; i < question.Options.Length; i++)
            {
                var index = i;
                ui.Button(box, question.Options[i].Text, 48, 244 + i * 102, 1024, 86, () => ChooseFate(index));
            }
        }

        void ChooseFate(int optionIndex)
        {
            var option = FateDialogue.Questions[fateStep].Options[optionIndex];
            fateFavored.AddRange(option.Favor);
            fateStep++;
            ShowFateReply(option.Reply);
        }

        void ShowFateReply(string reply)
        {
            var panel = FatePhasePanel();
            BuildFateBoard(panel, false);
            var box = FateBox(panel);
            ui.Label(box, "老 者 神 态", 48, 42, 780, 52, 26, UiKit.Gold, TextAlignmentOptions.TopLeft, true);
            ui.Label(box, string.Format("（{0} / {1}）", fateStep, FateDialogue.Questions.Length),
                900, 52, 170, 40, 20, UiKit.Muted, TextAlignmentOptions.TopRight);
            ui.Label(box, reply, 48, 130, 1024, 280, 26, UiKit.Paper);
            ui.Label(box, "命盘之上，又亮几星。", 48, 668, 700, 40, 19, UiKit.Muted, TextAlignmentOptions.TopLeft);
            ui.Button(box, "继 续", 852, 646, 220, 64, NextFateStep, true);
        }

        void NextFateStep()
        {
            if (fateStep < FateDialogue.Questions.Length) ShowFateQuestion();
            else FinishFateDialogue();
        }

        void FinishFateDialogue()
        {
            var rng = new System.Random();
            draftTraits = CharacterGen.RollTraits(rng);
            draftDestiny = FateDialogue.RollDestiny(fateFavored.ToArray(), rng);
            var fate = CharacterGen.FindDestiny(draftDestiny);
            if (modal != null) { modal.gameObject.SetActive(false); Destroy(modal.gameObject); modal = null; }
            modal = OpenFateScene("Fate result");
            BuildFateBoard(modal.transform, true);
            var box = FateBox(modal.transform);
            ui.Label(box, "卦 成", 48, 42, 400, 52, 26, UiKit.Gold, TextAlignmentOptions.TopLeft, true);
            ui.Label(box, "「" + (fate?.Name ?? "无名") + "」", 48, 120, 1024, 110, 54, UiKit.Paper, TextAlignmentOptions.TopLeft, true);
            ui.Label(box, fate?.Description ?? "卦象古怪，寻常卜算一概算不出。", 48, 282, 1024, 96, 26, UiKit.Muted);
            ui.Label(box, "另得词条：" + TraitNames(draftTraits) + "。卦象只给个去向，路终究是你自己走。",
                48, 400, 1024, 74, 21, UiKit.Muted);
            var accept = ui.Button(box, "记下此卦", 48, 660, 320, 68, () =>
            {
                CloseModal();
                RedrawCreation(currentDraftSlot);
            }, true);
            EventSystem.current.SetSelectedGameObject(accept.gameObject);
        }

        // 星落收束：问毕诸星向命盘中心收拢，一点金星坠向人间——命格随后揭晓。
        sealed class FateSendOff : MonoBehaviour
        {
            RectTransform[] stars;
            Vector2[] starts;
            float[] alphas;
            Vector2 center;
            RawImage falling;
            float time;

            public void Init(RectTransform[] starRects, Vector2 centerPoint, RawImage fallingStar)
            {
                stars = starRects;
                center = centerPoint;
                falling = fallingStar;
                starts = new Vector2[stars.Length];
                alphas = new float[stars.Length];
                for (var i = 0; i < stars.Length; i++)
                {
                    starts[i] = stars[i].anchoredPosition;
                    alphas[i] = stars[i].GetComponent<RawImage>().color.a;
                }
                if (falling != null) falling.gameObject.SetActive(false);
            }

            void Update()
            {
                if (stars == null || stars.Length == 0) return;
                time += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(time / 1.1f);
                var pull = t * t;
                for (var i = 0; i < stars.Length; i++)
                {
                    if (stars[i] == null) continue;
                    stars[i].anchoredPosition = Vector2.Lerp(starts[i], center, pull);
                    var image = stars[i].GetComponent<RawImage>();
                    var color = image.color;
                    image.color = new Color(color.r, color.g, color.b, alphas[i] * (1f - pull * .9f));
                }
                if (time > 1.05f && falling != null)
                {
                    var u = Mathf.Clamp01((time - 1.05f) / .85f);
                    falling.gameObject.SetActive(true);
                    falling.rectTransform.anchoredPosition = center + new Vector2(0f, -u * 340f);
                    var glow = Mathf.Sin(u * Mathf.PI);
                    falling.color = new Color(1f, .86f, .5f, glow);
                    falling.rectTransform.localScale = Vector3.one * (.7f + glow * .8f);
                }
            }
        }

        // 落笔：沿用真实建档入口，成功后直接进入青石镇。
        public SaveData CreateFromDraft(int slot)
        {
            SaveData save = null;
            Guard(() =>
            {
                save = repository.Create(slot, presets[draftOrigin].id, draftName, draftGender,
                    draftFaceStyle, draftTraits, draftDestiny);
                activeSave = save;
                ShowTown();
            });
            return save;
        }

        int currentDraftSlot;

        void RedrawCreation(int slot)
        {
            currentDraftSlot = slot;
            BeginPage("character-creation");
            PageHeading("众 生  /  初 见", "你从人间来", "每一个普通人，都有自己的来路。择一身行装，赴一程山河。");
            var preset = presets[draftOrigin];
            // 出身立绘卡片：三种来路一眼看全，选中卡描金、未选中压暗；点击卡片即切换。
            // 立绘跟随当前性别/面容重取，选性别也能预览到每一张卡的样子。
            for (var i = 0; i < presets.Length; i++)
            {
                var index = i;
                var selected = index == draftOrigin;
                var card = ui.Panel(content, "Origin card " + presets[i].displayName, 102 + i * 269, 418, 247, 512,
                    selected ? new Color(.82f, .72f, .5f, .97f) : new Color(.5f, .48f, .42f, .92f), true);
                var view = presets[i].Facing(0, draftGender, draftFaceStyle);
                if (view != null)
                {
                    var art = ui.Art(card.transform, view, 8, 8, 231, 438);
                    art.color = selected ? Color.white : new Color(.6f, .58f, .52f, 1f);
                }
                else
                    ui.Label(card.transform, "画稿待接入", 8, 200, 231, 50, 24, UiKit.Ink, TextAlignmentOptions.Center);
                ui.Label(card.transform, presets[i].displayName, 8, 454, 231, 48, 26,
                    selected ? UiKit.Ink : UiKit.Paper, TextAlignmentOptions.Center, true);
                card.gameObject.AddComponent<Button>().onClick.AddListener(() => SetCreationOrigin(index));
            }
            ui.Label(content, "外观与命格只是出身底色，不预设数值与结局。", 122, 946, 745, 30, 17, UiKit.Ink);

            ui.Panel(content, "Character details", 949, 336, 873, 594, new Color(.045f, .115f, .10f, .96f));
            ui.Label(content, "平 民 出 身", 998, 366, 715, 36, 19, UiKit.Gold);
            ui.Label(content, preset.displayName, 991, 408, 724, 64, 42, UiKit.Paper);
            ui.Label(content, preset.description, 998, 482, 748, 66, 23, UiKit.Paper);

            ui.Label(content, "性别", 998, 566, 90, 54, 24, UiKit.Muted, TextAlignmentOptions.MidlineLeft);
            ui.Button(content, "男", 1092, 566, 126, 54, () => SetCreationGender("male"), draftGender == "male");
            ui.Button(content, "女", 1230, 566, 126, 54, () => SetCreationGender("female"), draftGender == "female");
            ui.Label(content, "面容", 1396, 566, 90, 54, 24, UiKit.Muted, TextAlignmentOptions.MidlineLeft);
            ui.Button(content, CharacterPreset.FaceStyleName(draftGender, 0), 1490, 566, 150, 54,
                () => SetCreationFaceStyle(0), draftFaceStyle == 0);
            ui.Button(content, CharacterPreset.FaceStyleName(draftGender, 1), 1652, 566, 150, 54,
                () => SetCreationFaceStyle(1), draftFaceStyle == 1);

            ui.Label(content, "命格", 998, 646, 90, 54, 22, UiKit.Gold, TextAlignmentOptions.MidlineLeft);
            ui.Label(content, TraitNames(draftTraits), 1092, 646, 420, 54, 24, UiKit.Paper, TextAlignmentOptions.MidlineLeft);
            ui.Button(content, "重掷", 1506, 646, 128, 54, RerollCreationFate);
            ui.Button(content, "入梦问命", 1642, 646, 178, 54, OpenFateDialogue, true);
            ui.Label(content, FateSummary(draftTraits, draftDestiny), 998, 714, 824, 140, 19, UiKit.Muted);

            ui.Label(content, "你的名字", 998, 874, 110, 54, 22, UiKit.Gold, TextAlignmentOptions.MidlineLeft);
            nameInput = ui.Input(content, draftName, 1112, 868, 280);
            nameInput.onValueChanged.AddListener(value => draftName = value);
            ui.Button(content, "落笔 · 创建行迹", 1412, 868, 404, 54, () => CreateFromDraft(slot), true);
            FocusFirst();
        }

        string TraitNames(string[] traits) => string.Join("  ·  ",
            traits.Select(id => CharacterGen.FindTrait(id)?.Name ?? "无名").ToArray());

        string FateSummary(string[] traits, string destiny)
        {
            var lines = traits.Select(id =>
            {
                var trait = CharacterGen.FindTrait(id);
                return trait == null ? "" : trait.Name + "：" + trait.Description;
            }).Where(line => line.Length > 0).ToList();
            var fate = CharacterGen.FindDestiny(destiny);
            lines.Add(fate == null ? "命运：尚未显形。" : "命运·" + fate.Name + "：" + fate.Description);
            return string.Join("\n", lines.ToArray());
        }

        void ShowPortrait(Transform parent, CharacterPreset preset, int facing, string gender, int faceStyle,
            float x, float y, float w, float h)
        {
            var texture = preset.Facing(facing, gender, faceStyle);
            if (texture != null) ui.Art(parent, texture, x, y, w, h, facing == 3);
            else
            {
                // The explicit development fallback is removed automatically when final art is imported.
                ui.Label(parent, preset.displayName, x, y + h * .32f, w, 80, 50, UiKit.Ink, TextAlignmentOptions.Center);
                ui.Label(parent, "造型画稿待接入", x, y + h * .55f, w, 45, 24, UiKit.Ink, TextAlignmentOptions.Center);
            }
        }

        public void LoadSlot(SlotInfo slot)
        {
            if (slot == null || !slot.CanLoad) { Notify("暂无可继续的行迹。"); return; }
            Guard(() =>
            {
                if (!presets.Any(p => p.id == slot.Data.characterId))
                    throw new InvalidOperationException("该角色的外观资源暂未找到，档位已保留。");
                activeSave = slot.Data;
                if (slot.State == SlotState.Recovered) repository.Write(activeSave);
                ShowTown();
                if (slot.State == SlotState.Recovered) Notify("已从上一份有效备份恢复行迹。");
            });
        }

        // Public compatibility entry for existing editor tools; the retired Preview page is no longer built.
        public void ShowPreview(SaveData data)
        {
            activeSave = data;
            ShowTown();
        }

        // 行囊摘要：铜钱、地方声望与占用格数——买卖让利按声望算（M3 交易所同一套规则）。
        string BagLine(SaveData data)
        {
            var bag = data.bag ?? InventoryRules.NewBag();
            return "行囊 " + InventoryRules.UsedSlots(bag) + " / " + InventoryRules.SlotCount +
                " 格 · 铜钱 " + data.coins + " 文 · 地方声望 " + data.localReputation +
                "（让利 " + Mathf.RoundToInt(Reputation.Discount(data.localReputation) * 100) + "%）";
        }

        CharacterActor currentActor = null; // Legacy preview handle; intentionally absent from the live UI.
        // 进页面必须站着：默认 Walk 会让人物在原地踏步（上下起伏 + 左右钟摆），
        // 玩家还没碰任何按钮就看见一个在“跳”的人。
        CharacterActor.Motion currentMotion = CharacterActor.Motion.Idle;
        PuppetActor walkPreview = null; // Legacy preview handle; intentionally absent from the live UI.
        int walkPreviewMotion = 1;
        RectTransform walkSamplePanel = null; // Legacy preview handle; intentionally absent from the live UI.   // T128：行走样张面板（显式唤出），隐藏位与右列停靠位之间搬
        // 样张传送带速度 = 城镇页的走路速度（FrontEndWorld.WalkSpeedWorld）。
        // 数值相同不是巧合：预览页写着"镇上那台就是这台纸偶"，节拍也得是同一个节拍。
        const float WalkSampleConveyorSpeed = 260f;

        // 行走样张档位切换（0 静立 / 1 行走 / 2 奔跑），与城镇页同一套 PuppetActor。
        // B-12：入参是 **PuppetActor 自己的档位**（0..2），不是 CharacterActor 的那 7 档；
        // 若哪天要跟着 7 档走，必须过 `PuppetMotionMap.Map()`，不许再拿裸 int 直接强转。
        // 另一处改动：越界原来只是 `return`，**什么都不留**（表现为"点了没反应"）；现在打一条 LogWarning。
        public void SetWalkPreview(int gait)
        {
            if (gait < 0 || gait >= PuppetMotionMap.PuppetMotionCount)
            {
                Debug.LogWarning("[B-12] SetWalkPreview 收到越界档位 " + gait +
                    "（合法范围 0.." + (PuppetMotionMap.PuppetMotionCount - 1) + "），已忽略。");
                return;
            }
            walkPreviewMotion = gait;
            if (walkPreview != null) walkPreview.SetMotion((PuppetActor.Motion)gait);
        }

        // B-12：把 CharacterActor 的 7 档映射到 PuppetActor 的 3 档，喂给"行走样张"预览。
        // 修的是任务书点名的那条：**选潜行/下蹲/跳跃/游泳时预览一动不动，且日志无痕**。
        // 借档（第 4 档起）会打一条 Log，说明"用了谁的档"，不假装等价。
        void ApplyMotionToPreview(int characterMotion)
        {
            var mapped = PuppetMotionMap.Map(characterMotion);
            if (mapped < 0)
            {
                Debug.LogWarning("[B-12] 档位 " + characterMotion +
                    " 不在 CharacterActor.Motion 范围内（0.." + (PuppetMotionMap.CharacterMotionCount - 1) + "），预览未改。");
                return;
            }
            if (PuppetMotionMap.IsDegraded(characterMotion))
                Debug.Log("[B-12] 档位「" + CharacterActor.MotionNames[characterMotion] +
                    "」在纸偶侧暂无专属表现，借用「" + PuppetActor.MotionNames[mapped] + "」。");
            walkPreviewMotion = mapped;
            if (walkPreview != null) walkPreview.SetMotion((PuppetActor.Motion)mapped);
        }

        public PuppetActor WalkPreview => walkPreview;
        // T100：删掉 `WalkPreviewMotion`。档位真正生效在 SetWalkPreview→walkPreview.SetMotion，
        // 按钮高亮读的是同一个私有字段；这个公开 getter 全仓没人调，留着像"外面有人按它同步状态"。

        public void SetMotion(int motion)
        {
            // 0 = 静立，是合法档位（新档默认值）；原来的 `< 1` 让静立既无按钮也切不进去。
            if (motion < 0 || motion >= CharacterActor.MotionNames.Length) return;
            currentMotion = (CharacterActor.Motion)motion;
            if (currentActor != null) currentActor.SetMotion(currentMotion);
            else if (activeSave != null && page == "preview") ShowPreview(activeSave);
            // B-12：动作档同时喂给"行走样张"预览（走显式映射表）。放在重建之后，
            // 免得 ShowPreview 刚重建的 walkPreview 把这一档覆盖掉。
            ApplyMotionToPreview(motion);
        }

        // T128：行走样张改为显式唤出。传记面板需要右列全高，两者纵向摆不下，
        // 样张默认收在屏外隐藏位（x=-3000，不参与布局、不与任何面板重叠），
        // 唤出才摆进右列。传送带保持空转（夹具相位断言依赖它）。
        void ToggleWalkSample()
        {
            if (walkSamplePanel == null) return;
            var shown = walkSamplePanel.anchoredPosition.x > 0f;
            // 隐藏位（-3000, 336）与右列停靠位（1074, 336）之间搬。
            walkSamplePanel.anchoredPosition = shown ? new Vector2(-3000f, -336f) : new Vector2(1074f, -336f);
            // 唤出态必须显式置顶：本面板建在 Biography 之前，同级后建者画在上层，
            // 只搬坐标会被传记面板整块盖住。收回屏外时不用降级，玩家本来就看不见。
            if (!shown) walkSamplePanel.SetAsLastSibling();
        }
    }
}
