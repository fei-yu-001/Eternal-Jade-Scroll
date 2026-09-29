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
        string draftName = "无名";
        string[] draftTraits = Array.Empty<string>();
        string draftDestiny = "";
        TMP_InputField nameInput;

        public void ShowSlots(bool creating)
        {
            BeginPage(creating ? "new-game" : "slots");
            PageHeading("行 迹  /  三 卷 人 生", creating ? "择一卷，启新程" : "故人行迹",
                creating ? "选择空白档位，写下你的故事。已有行迹会完整保留。" : "每一卷都是独立的人生。可继续旅程，也可整理旧卷。");
            foreach (var slot in repository.ReadAll())
            {
                var x = 100 + (slot.Slot - 1) * 575;
                var card = ui.Panel(content, "Slot " + slot.Slot, x, 342, 545, 540, new Color(.045f, .115f, .10f, .96f), true);
                ui.Label(card.transform, "第 " + new[] { "一", "二", "三" }[slot.Slot - 1] + " 卷", 32, 30, 450, 45, 23, UiKit.Gold);
                string title, description;
                if (slot.CanLoad)
                {
                    var preset = presets.FirstOrDefault(p => p.id == slot.Data.characterId);
                    title = slot.Data.characterName;
                    description = (preset != null ? preset.displayName : "未知外观") + " · " +
                        CharacterPreset.GenderName(slot.Data.gender) + "\n停留于：行旅小憩\n\n" +
                        "上次归来\n" + DateTimeOffset.Parse(slot.Data.updatedUtc).ToLocalTime().ToString("yyyy.MM.dd  HH:mm");
                    if (slot.State == SlotState.Recovered) description += "\n\n主记录损坏，将从备份恢复。";
                }
                else if (slot.State == SlotState.Empty) { title = "尚未落笔"; description = "山河辽阔，前路未定。\n这一卷，等你写下第一笔。"; }
                else if (slot.State == SlotState.FutureVersion) { title = "来自未来的行迹"; description = "该档位由更新版本创建。\n请使用更新的游戏版本打开。"; }
                else { title = "行迹暂不可读"; description = "档位和备份均无法读取。\n可保留等待修复，或删除后重新开始。"; }
                ui.Label(card.transform, title, 32, 120, 475, 65, 40, UiKit.Paper, TextAlignmentOptions.TopLeft, true);
                ui.Label(card.transform, description, 34, 210, 472, 215, 22, UiKit.Muted);
                if (slot.State == SlotState.Empty)
                    ui.Button(card.transform, "写下新的人生", 32, 450, 481, 62, () => ShowCharacterCreation(slot.Slot), true);
                else
                {
                    ui.Button(card.transform, "继续", 32, 450, 292, 62, () => LoadSlot(slot), true, slot.CanLoad);
                    ui.Button(card.transform, "删除", 340, 450, 173, 62, () => Confirm("删除第 " + slot.Slot + " 卷？",
                        "这会移除该档位和它的备份。删除后无法恢复，其他档位不受影响。", "确认删除", () =>
                        { repository.Delete(slot.Slot); ShowSlots(creating); Notify("此卷已清空。"); }));
                }
            }
            ui.Label(content, "记录保存在本机。外观、命格与词条只是出身底色，不影响数值与剧情走向。", 102, 926, 1500, 40, 21, UiKit.Muted);
            FocusFirst();
        }

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
            var texture = Resources.Load<Texture2D>("Art/fate-background");
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

        // 落笔：以当前草稿创建档位并进入预览。
        public SaveData CreateFromDraft(int slot)
        {
            SaveData save = null;
            Guard(() =>
            {
                save = repository.Create(slot, presets[draftOrigin].id, draftName, draftGender,
                    draftFaceStyle, draftTraits, draftDestiny);
                activeSave = save;
                ShowPreview(save);
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
            for (var i = 0; i < presets.Length; i++)
            {
                var index = i;
                ui.Button(content, presets[i].displayName, 102 + i * 269, 337, 247, 60,
                    () => SetCreationOrigin(index), i == draftOrigin);
            }
            ui.Panel(content, "Character portrait", 102, 418, 785, 512, new Color(.78f, .76f, .65f, .97f));
            ShowPortrait(content, preset, 0, draftGender, draftFaceStyle, 102 + 20, 418 + 15, 745, 480);
            ui.Label(content, "外观与命格只是出身底色，不预设数值与结局。", 122, 892, 745, 30, 17, UiKit.Ink);

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
                ShowPreview(activeSave);
                if (slot.State == SlotState.Recovered) Notify("已从上一份有效备份恢复行迹。");
            });
        }

        public void ShowPreview(SaveData data)
        {
            BeginPage("preview"); activeSave = data;
            var preset = presets.First(p => p.id == data.characterId);
            var pronoun = data.gender == "female" ? "她" : "他";
            PageHeading("行 旅 小 憩", data.characterName + "的行装", "一身寻常衣衫，一段尚未展开的人生。试试" + pronoun + "的身手。");
            var art = ui.Panel(content, "Turnaround", 102, 336, 920, 593, new Color(.78f, .76f, .65f, .97f));
            var view = preset.Facing(data.facing, data.gender, data.faceStyle);
            if (view != null)
            {
                currentActor = CharacterActor.Create(art.transform, view, data.facing == 3,
                    new Vector2(460, 160), new Vector2(246, 412));
                currentActor.SetMotion(currentMotion);
            }
            else
            {
                ui.Label(art.transform, preset.displayName, 92, 180, 736, 80, 50, UiKit.Ink, TextAlignmentOptions.Center);
                ui.Label(art.transform, "造型画稿待接入", 92, 280, 736, 45, 24, UiKit.Ink, TextAlignmentOptions.Center);
            }
            string[] directions = { "正面", "右侧", "背面", "左侧" };
            for (var i = 0; i < 4; i++)
            {
                var direction = i;
                ui.Button(art.transform, directions[i], 43 + i * 220, 438, 200, 52, () => Guard(() =>
                {
                    var previous = data.facing;
                    data.facing = direction;
                    try { repository.Write(data); }
                    catch { data.facing = previous; throw; }
                    ShowPreview(data);
                }), i == data.facing);
            }
            ui.Label(art.transform, "动 作", 24, 502, 60, 52, 20, UiKit.Ink, TextAlignmentOptions.MidlineLeft);
            for (var i = 1; i < CharacterActor.MotionNames.Length; i++)
            {
                var motion = (CharacterActor.Motion)i;
                ui.Button(art.transform, CharacterActor.MotionNames[i], 92 + (i - 1) * 140, 502, 128, 52,
                    () => SetMotion((int)motion), currentMotion == motion);
            }
            ui.Label(art.transform, "键盘 1–6 亦可切换动作", 24, 562, 400, 24, 15,
                new Color(.24f, .28f, .26f, .55f));
            ui.Panel(content, "Biography", 1074, 336, 746, 593, new Color(.045f, .115f, .10f, .96f));
            ui.Label(content, "第 " + data.slot + " 卷  /  已落笔", 1117, 373, 650, 40, 19, UiKit.Gold);
            ui.Label(content, preset.displayName + " · " + CharacterPreset.GenderName(data.gender), 1110, 424, 650, 60, 38, UiKit.Paper);
            ui.Label(content, preset.description, 1117, 494, 625, 96, 25, UiKit.Paper);
            ui.Label(content, TraitNames(data.traits), 1117, 602, 625, 44, 24, UiKit.Gold);
            ui.Label(content, FateSummary(data.traits, data.destiny), 1117, 652, 625, 128, 18, UiKit.Muted);
            ui.Label(content, BagLine(data), 1117, 780, 625, 30, 19, UiKit.Gold);
            ui.Button(content, "启程 · 进入青石镇", 1117, 812, 449, 62, () => Guard(() => ShowTown()), true);
            ui.Button(content, "行囊", 1580, 812, 186, 62, ShowInventory);
            ui.Button(content, "返回主菜单", 1117, 884, 310, 56, ShowMenu, true);
            ui.Button(content, "查看行迹", 1456, 884, 310, 56, () => ShowSlots(false));
            FocusFirst();
        }

        // 行囊摘要：铜钱、地方声望与占用格数——买卖让利按声望算（M3 交易所同一套规则）。
        string BagLine(SaveData data)
        {
            var bag = data.bag ?? InventoryRules.NewBag();
            return "行囊 " + InventoryRules.UsedSlots(bag) + " / " + InventoryRules.SlotCount +
                " 格 · 铜钱 " + data.coins + " 文 · 地方声望 " + data.localReputation +
                "（让利 " + Mathf.RoundToInt(Reputation.Discount(data.localReputation) * 100) + "%）";
        }

        CharacterActor currentActor;
        CharacterActor.Motion currentMotion = CharacterActor.Motion.Walk;

        public void SetMotion(int motion)
        {
            if (motion < 1 || motion >= CharacterActor.MotionNames.Length) return;
            currentMotion = (CharacterActor.Motion)motion;
            if (currentActor != null) currentActor.SetMotion(currentMotion);
            else if (activeSave != null && page == "preview") ShowPreview(activeSave);
        }
    }
}
