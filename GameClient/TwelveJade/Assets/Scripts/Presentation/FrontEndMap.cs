using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // 青石镇：以密集主街鸟瞰图为可行走镇子。走廊、立绘、地标、出生点与透视全部来自
    // Resources/Config/town-map.json（配置表校验在 Tools/SaveChecks，改坏了那里先报警）。
    // 表现层只负责把画布坐标翻译成 UI 局部坐标，并按基座 y 做纵深排序。
    public sealed partial class FrontEndController
    {
        PuppetActor traveler;
        RectTransform nameTag;
        RectTransform townMap, miniDot, miniHalo, ripple, dustLayer;
        RawImage rippleImage;
        Texture2D townMapArt;
        TownMap town;
        Vector2 localPos, walkTarget;
        bool hasTarget, facingLeft;
        float townScale = 1f, rippleStart;
        int seenFootfalls;
        // 深度条目：立绘与 NPC 放在同一张表里按基座 y 排；分开两张表会各排各的、互相插队。
        readonly List<TownLayer> townLayers = new();
        readonly List<(RectTransform ring, RectTransform label)> encounterMarkers = new();
        string pendingNpcId;
        string pendingEncounterId;
        public event System.Action<string> EncounterArrived;
        public sealed class TownLayer
        {
            public RectTransform Rect;
            public float BaseY;
        }
        public event System.Action<string> NpcArrived;
        readonly List<RawImage> dustPool = new();
        readonly List<float> dustStart = new();
        readonly List<float> dustFade = new();
        readonly List<Vector2> dustVelocity = new();
        int dustCursor;

        const float WalkSpeed = 215f, RunSpeed = 390f;
        const int DustCount = 8;
        // 近景镜头倍率：整镇图放大后超出视口，镜头跟随主角滚动（UpdateCamera）。
        // 2 倍时主角约屏高 28%，替代原先 1:1 缩在画布里的鸟瞰构图。
        const float TownZoom = 2f;

        // 可行走区域来自配置：读不到就不放行，避免用代码里的副本悄悄跑偏。
        bool LoadTownMap()
        {
            if (town != null) return true;
            var asset = Resources.Load<TextAsset>("Config/town-map");
            if (asset == null) { Notify("城镇地图配置缺失：Resources/Config/town-map.json。"); return false; }
            try { town = TownMap.Parse(asset.text); }
            catch (System.FormatException ex) { Notify("城镇地图配置有误：" + ex.Message); return false; }
            return true;
        }

        public void ShowTown()
        {
            if (activeSave == null) { Notify("先落笔创建一位行旅人，再启程入镇。"); return; }
            if (!LoadTownMap()) return;
            CloseInventory();
            BeginPage("town");
            ui.Panel(content, "Town veil", 0, 0, 1920, 1080, new Color(.91f, .88f, .79f, .98f));
            BuildTownHud();
            // 提示行垫一条半透明墨底：镜头拉近后底下滚过深浅不一的屋顶，纯墨字会没进屋檐里。
            var townHint = ui.Panel(content, "Town hint", 96, 158, 668, 46, new Color(.07f, .11f, .09f, .55f));
            ui.Label(townHint.transform, "点击街面或 WASD 移动 · 按住 Shift 奔跑 · B 开行囊", 12, 4, 644, 38, 20,
                new Color(.96f, .93f, .85f, .92f));
            ui.Button(content, "行囊", 1408, 44, 200, 54, ShowInventory);
            ui.Button(content, "返回主菜单", 1620, 44, 200, 54, ShowMenu, true);

            townMapArt = Resources.Load<Texture2D>("Art/town-map");
            float mapW = town.ArtWidth * TownZoom, mapH = mapW * town.ArtHeight / town.ArtWidth;
            townScale = mapW / town.ArtWidth;
            townMap = ui.Art(content, townMapArt != null ? townMapArt : Texture2D.whiteTexture, 0, 0, mapW, mapH).rectTransform;
            // 近景下整图铺满视口，会压住先建的标题与按钮——压到背景纱之后、标题之前。
            townMap.SetSiblingIndex(1);

            townLayers.Clear();
            pendingNpcId = null;
            pendingEncounterId = null;
            encounterMarkers.Clear();
            foreach (var prop in town.Props) PlaceProp(prop);
            foreach (var npc in town.Npcs) PlaceNpc(npc);
            foreach (var encounter in town.Encounters) PlaceEncounter(encounter);

            dustLayer = ui.Rect(townMap, "Footfall dust", 0, 0, 10, 10);
            ArrangeTownLayers();
            BuildDust();

            var preset = System.Array.Find(presets, p => p.id == activeSave.characterId) ?? presets[0];
            var view = preset.Facing(0, activeSave.gender, activeSave.faceStyle)
                       ?? presets[0].Facing(0, activeSave.gender, activeSave.faceStyle);
            var bodyHeight = 165f * townScale;
            traveler = PuppetActor.Create(townMap, view, Vector2.zero, new Vector2(bodyHeight * .593f, bodyHeight));
            BuildVersionStamp(view);
            localPos = new Vector2(town.SpawnX, town.SpawnY);
            walkTarget = localPos;
            hasTarget = false;
            facingLeft = town.SpawnFacingLeft;
            seenFootfalls = traveler.Footfalls;
            traveler.SetPosition(ToLocal(localPos));
            ApplyPerspective();
            UpdateCamera();
            traveler.SetMotion(PuppetActor.Motion.Idle);
            BuildTravelerTag();

            BuildMiniMap();
            // 眼前事/镇民/线索面板（M5-05 S2）：城镇页右侧，与行囊、交易互不打断。
            ShowTownChapterPanel();
            FocusFirst();
        }

        void PlaceProp(MapProp prop)
        {
            var texture = Resources.Load<Texture2D>("Art/Props/" + prop.Slug);
            if (texture == null) return;
            var rect = new GameObject("Prop " + prop.Slug, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(townMap, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            // 枢轴取底座中心（pivot.y = 0）：配置里的 x/y 就是立绘站的那条地平线，
            // 立绘从该点向上长；用顶端做枢轴的话整幅画会往下挂一个身位，纵深排序也跟着错。
            rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = ToLocal(new Vector2(prop.X, prop.Y));
            rect.sizeDelta = new Vector2(prop.Height, prop.Height) * townScale;
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            townLayers.Add(new TownLayer { Rect = rect, BaseY = prop.Y });
        }

        // 子节点顺序决定 UI 立绘的遮挡关系：尘点在最底（index 0），
        // 其余按基座 y 由远到近排。立绘与 NPC 共用这一张表，插队才不会各排各的。
        void ArrangeTownLayers()
        {
            dustLayer.SetAsFirstSibling();
            townLayers.Sort((left, right) => left.BaseY.CompareTo(right.BaseY));
            for (var i = 0; i < townLayers.Count; i++)
                if (townLayers[i].Rect != null) townLayers[i].Rect.SetSiblingIndex(i + 1);
        }

        // 镇上的活人：与立绘同样按地平线摆放，但可点、可搭话。
        void PlaceNpc(MapNpc npc)
        {
            var texture = Resources.Load<Texture2D>(npc.Art);
            if (texture == null)
            {
                // 缺贴图就没有这个画面对象，但搭话仍然按配置里的 id 走，不受缺图影响。
                Debug.LogWarning("NPC 贴图缺失，" + npc.Id + "：" + npc.Art);
                return;
            }
            var rect = new GameObject("Npc " + npc.Id, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(townMap, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = ToLocal(new Vector2(npc.X, npc.Y));
            rect.sizeDelta = new Vector2(npc.Height, npc.Height) * townScale;
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.gameObject.name = "Npc " + npc.Id;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => GreetNpc(npc));
            townLayers.Add(new TownLayer { Rect = rect, BaseY = npc.Y });
        }

        // 走过去再搭话：点人先挪到他面前，脚下出现涟漪，到了才开口。
        public void GreetNpc(MapNpc npc)
        {
            if (npc == null) return;
            var spot = ClampToWalkable(new Vector2(npc.X, npc.Y + npc.Radius + 16f));
            var distance = (spot - localPos).magnitude;
            // 存 id 而不是下标：缺贴图的 NPC 不会进深度表，下标会错位。
            pendingNpcId = npc.Id;
            if (distance < 6f) { pendingNpcId = null; NpcArrived?.Invoke(npc.Id); return; }
            walkTarget = spot;
            hasTarget = true;
            ShowRipple(ToLocal(spot));
        }

        // 验收钩子：点完 NPC 后人物是否已经在走过去。
        public bool TownWalkingToNpc => pendingNpcId != null;

        // 验收钩子：把人物直接放到 NPC 跟前。
        // 批处理里播放循环的节拍不稳，等不到真实走完的那一段，所以"走过去"与
        // "到达后搭话"分开验：前者断言已设定目标，后者由这里触发到达回调。
        public bool SnapTravelerNextToNpc(string npcId)
        {
            var npc = town?.Npcs.FirstOrDefault(n => n.Id == npcId);
            if (npc == null || pendingNpcId != npcId) return false;
            localPos = ClampToWalkable(new Vector2(npc.X, npc.Y + npc.Radius + 16f));
            hasTarget = false;
            return true;
        }

        // 遭遇点：脉动金环 + 竖排名，走进去触发战斗（M4-04：encounter-boar-01 后山兽吼）。
        void PlaceEncounter(MapEncounter encounter)
        {
            var rect = new GameObject("Encounter " + encounter.Id, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(townMap, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = ToLocal(new Vector2(encounter.X, encounter.Y));
            rect.sizeDelta = new Vector2(44f, 44f) * townScale;
            var ring = rect.gameObject.AddComponent<RawImage>();
            ring.texture = TownRingTexture(.35f);
            ring.color = new Color(1f, .82f, .35f, .95f);
            ring.raycastTarget = false;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = ring;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SeekEncounter(encounter));
            var label = ui.Label(rect, encounter.Name, -10, -2, 64, 30, 17, new Color(.4f, .3f, .18f, .95f),
                TextAlignmentOptions.Top);
            encounterMarkers.Add((ring.rectTransform, label.rectTransform));
            townLayers.Add(new TownLayer { Rect = rect, BaseY = encounter.Y });
        }

        // 走到遭遇点跟前再触发：脚下涟漪，到位才开战。
        public void SeekEncounter(MapEncounter encounter)
        {
            if (encounter == null) return;
            var spot = ClampToWalkable(new Vector2(encounter.X, encounter.Y));
            pendingEncounterId = encounter.Id;
            if ((spot - localPos).magnitude < 6f)
            {
                pendingEncounterId = null;
                EncounterArrived?.Invoke(encounter.Id);
                return;
            }
            walkTarget = spot;
            hasTarget = true;
            ShowRipple(ToLocal(spot));
        }

        // 宣纸舆图：主图缩绘，玩家金点描墨边，地标朱红点；点舆图展开全图浮层。
        void BuildMiniMap()
        {
            var mini = ui.Panel(content, "Mini map", 1594, 178, 246, 156, UiKit.Dark, true);
            ui.Label(mini.transform, town.Name + " · 舆图", 14, 8, 200, 26, 15, UiKit.Gold);
            var miniArt = ui.Art(mini.transform, townMapArt != null ? townMapArt : Texture2D.whiteTexture, 13, 36, 220, 106);
            foreach (var mark in town.Landmarks)
            {
                var dot = new GameObject("Mini mark " + mark.Id, typeof(RectTransform)).GetComponent<RectTransform>();
                dot.SetParent(miniArt.rectTransform, false);
                dot.anchorMin = dot.anchorMax = new Vector2(0, 1);
                dot.pivot = new Vector2(.5f, .5f);
                dot.anchoredPosition = new Vector2(mark.X / town.ArtWidth * miniArt.rectTransform.sizeDelta.x,
                    -mark.Y / town.ArtHeight * miniArt.rectTransform.sizeDelta.y);
                dot.sizeDelta = new Vector2(9f, 9f);
                var dotImage = dot.gameObject.AddComponent<RawImage>();
                dotImage.texture = DotTexture();
                dotImage.color = new Color(.66f, .2f, .16f, .95f);
                dotImage.raycastTarget = false;
            }
            MiniStar(miniArt.rectTransform);
            var open = mini.gameObject.AddComponent<Button>();
            open.targetGraphic = mini;
            open.transition = Selectable.Transition.None;
            open.onClick.AddListener(() => { PlayClick(); ShowMapOverlay(); });
        }

        void MiniStar(RectTransform parent)
        {
            var rect = new GameObject("Mini star", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(22f, 22f);
            var image = rect.gameObject.AddComponent<RawImage>();
            // 金点带墨边：小图上也要一眼找得到自己。
            image.texture = DotTexture();
            image.raycastTarget = false;
            image.color = new Color(1f, .82f, .35f, 1f);
            var halo = new GameObject("Halo", typeof(RectTransform)).GetComponent<RectTransform>();
            halo.SetParent(parent, false);
            halo.anchorMin = halo.anchorMax = new Vector2(0, 1);
            halo.pivot = new Vector2(.5f, .5f);
            halo.sizeDelta = new Vector2(30f, 30f);
            var haloImage = halo.gameObject.AddComponent<RawImage>();
            haloImage.texture = TownRingTexture(.45f);
            haloImage.color = new Color(.1f, .16f, .14f, .85f);
            haloImage.raycastTarget = false;
            halo.SetSiblingIndex(rect.GetSiblingIndex());
            miniDot = rect;
            miniHalo = halo;
        }

        static Texture2D dotTexture;

        // 实心圆点：外圈稍暗，用作舆图上的金点与朱红地标。
        static Texture2D DotTexture()
        {
            if (dotTexture != null) return dotTexture;
            const int s = 48;
            dotTexture = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (var y = 0; y < s; y++)
            for (var x = 0; x < s; x++)
            {
                var dx = (x + .5f) / s - .5f;
                var dy = (y + .5f) / s - .5f;
                var d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                var alpha = Mathf.Clamp01((1f - d) / .12f);
                var shade = Mathf.Lerp(1f, .45f, Mathf.Clamp01((d - .78f) / .22f));
                dotTexture.SetPixel(x, y, new Color(shade, shade, shade, alpha * alpha * (3f - 2f * alpha)));
            }
            dotTexture.Apply(false, true);
            return dotTexture;
        }

        // 全图浮层：本卷全图 + 图例，不做跨图传送（第一章无传送）。
        void ShowMapOverlay()
        {
            CloseModal();
            content.GetComponent<CanvasGroup>().interactable = false;
            modal = ui.Panel(canvas, "Town map overlay", 0, 0, 1920, 1080, new Color(0f, .035f, .03f, .88f), true).rectTransform;
            var box = ui.Panel(modal, "Map box", 160, 96, 1600, 900, UiKit.Dark, true).rectTransform;
            ui.Label(box, town.Name + " · 舆图", 48, 32, 800, 52, 32, UiKit.Paper, TextAlignmentOptions.TopLeft, true);
            ui.Label(box, "点击任意处收起", 1180, 42, 372, 34, 20, UiKit.Muted, TextAlignmentOptions.TopRight);
            var art = ui.Art(box, townMapArt != null ? townMapArt : Texture2D.whiteTexture, 48, 100, 1504, 700);
            foreach (var mark in town.Landmarks)
            {
                var dot = new GameObject("Mark " + mark.Id, typeof(RectTransform)).GetComponent<RectTransform>();
                dot.SetParent(art.rectTransform, false);
                dot.anchorMin = dot.anchorMax = new Vector2(0, 1);
                dot.pivot = new Vector2(.5f, .5f);
                dot.anchoredPosition = new Vector2(mark.X / town.ArtWidth * 1504f, -mark.Y / town.ArtHeight * 700f);
                dot.sizeDelta = new Vector2(16f, 16f);
                var dotImage = dot.gameObject.AddComponent<RawImage>();
                dotImage.texture = DotTexture();
                dotImage.color = new Color(.66f, .2f, .16f, .95f);
                dotImage.raycastTarget = false;
                ui.Label(dot, mark.Name, -60, 10, 140, 26, 20, UiKit.Paper, TextAlignmentOptions.Top);
            }
            ui.Label(box, "玩家所在以下方朱点为准 · 地标：" + string.Join(" · ", town.Landmarks.Select(m => m.Name)),
                48, 818, 1504, 40, 20, UiKit.Muted);
            var close = modal.gameObject.AddComponent<Button>();
            close.targetGraphic = modal.GetComponent<Image>();
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(CloseModal);
        }

        // 脚步尘点：每落一步在脚下撒一枚淡墨点，0.4s 淡出；八枚一循环，不额外建对象。
        void BuildDust()
        {
            dustPool.Clear();
            dustStart.Clear();
            dustFade.Clear();
            dustVelocity.Clear();
            dustCursor = 0;
            for (var i = 0; i < DustCount; i++)
            {
                var rect = new GameObject("Dust " + i, typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(dustLayer, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(14f, 14f);
                var image = rect.gameObject.AddComponent<RawImage>();
                image.texture = StarTexture();
                image.raycastTarget = false;
                image.color = new Color(.22f, .26f, .24f, 0f);
                dustPool.Add(image);
                dustStart.Add(-99f);
                dustFade.Add(0f);
                dustVelocity.Add(Vector2.zero);
            }
        }

        void SpawnDust(bool running)
        {
            if (dustPool.Count == 0) return;
            var image = dustPool[dustCursor];
            var size = (running ? 22f : 15f) * townScale * Mathf.Lerp(town.PerspectiveTop, town.PerspectiveBottom,
                Mathf.Clamp01((localPos.y - 90f) / 1040f));
            image.rectTransform.sizeDelta = new Vector2(size, size);
            image.rectTransform.localScale = Vector3.one * .7f;
            image.rectTransform.anchoredPosition = ToLocal(localPos) - new Vector2(0f, size * .1f);
            dustFade[dustCursor] = running ? .42f : .3f;
            image.color = new Color(.2f, .24f, .22f, dustFade[dustCursor]);
            dustStart[dustCursor] = Time.unscaledTime;
            // 尘点往后（远离前进方向）飘一点，跑动时更明显。
            dustVelocity[dustCursor] = new Vector2(facingLeft ? 12f : -12f, 6f) * (running ? 1.5f : 1f);
            dustCursor = (dustCursor + 1) % DustCount;
        }

        void UpdateDust()
        {
            for (var i = 0; i < dustPool.Count; i++)
            {
                var image = dustPool[i];
                if (image == null || dustFade[i] <= 0f) continue;
                var t = (Time.unscaledTime - dustStart[i]) / .4f;
                if (t >= 1f) { image.color = new Color(.2f, .24f, .22f, 0f); dustFade[i] = 0f; continue; }
                image.rectTransform.anchoredPosition += dustVelocity[i] * Time.unscaledDeltaTime * townScale;
                image.rectTransform.localScale = Vector3.one * (.7f + t * .8f);
                image.color = new Color(.2f, .24f, .22f, dustFade[i] * (1f - t));
            }
        }

        void UpdateTown()
        {
            if (page != "town" || traveler == null || townMap == null || town == null) return;
            var keyboard = Keyboard.current;
            var move = Vector2.zero;
            var run = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y -= 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y += 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
            }
            if (move != Vector2.zero) { hasTarget = false; move.Normalize(); }
            else if (hasTarget)
            {
                var delta = walkTarget - localPos;
                if (delta.magnitude < 7f) hasTarget = false;
                else move = delta.normalized;
            }
            if (move != Vector2.zero)
            {
                var speed = run ? RunSpeed : WalkSpeed;
                TryMove(move * speed * Time.unscaledDeltaTime);
                if (Mathf.Abs(move.x) > .05f) facingLeft = move.x < 0f;
                traveler.SetMirror(facingLeft);
                traveler.SetMotion(run ? PuppetActor.Motion.Run : PuppetActor.Motion.Walk);
                // 步频与折返：斜向位移比直线短，步频按实际步幅折算，免得斜走时脚下打滑。
                traveler.SetStepScale(speed / WalkSpeed);
            }
            else
            {
                traveler.SetMotion(PuppetActor.Motion.Idle);
                // 走到位、停下脚，这时才搭话。
                if (pendingNpcId != null && !hasTarget)
                {
                    var arrived = pendingNpcId;
                    pendingNpcId = null;
                    NpcArrived?.Invoke(arrived);
                }
                if (pendingEncounterId != null && !hasTarget)
                {
                    var arrivedEncounter = pendingEncounterId;
                    pendingEncounterId = null;
                    EncounterArrived?.Invoke(arrivedEncounter);
                }
            }
            traveler.SetPosition(ToLocal(localPos));
            ApplyPerspective();
            UpdateCamera();
            if (nameTag != null) nameTag.anchoredPosition = ToLocal(localPos) + new Vector2(0f, -10f);
            SortTravelerDepth();
            if (traveler.Footfalls != seenFootfalls)
            {
                seenFootfalls = traveler.Footfalls;
                if (move != Vector2.zero) SpawnDust(run);
            }
            UpdateDust();
            var pulse = 1f + Mathf.Sin(Time.unscaledTime * 4f) * .12f;
            foreach (var (ring, _) in encounterMarkers)
                if (ring != null) ring.localScale = Vector3.one * pulse;

            if (miniDot != null)
            {
                var miniArt = miniDot.parent as RectTransform;
                var size = miniArt.sizeDelta;
                // 点是舆图子节点，锚在舆图左上——只按比例位移，不叠加父级自身的偏移。
                miniDot.anchoredPosition = new Vector2(localPos.x / town.ArtWidth * size.x, -localPos.y / town.ArtHeight * size.y);
                // 光晕直接跟着金点走：持引用而不是按兄弟顺序去找，找不到就当没有。
                if (miniHalo != null) miniHalo.anchoredPosition = miniDot.anchoredPosition;
            }

            if (ripple != null)
            {
                var t = Mathf.Clamp01((Time.unscaledTime - rippleStart) / .5f);
                ripple.localScale = Vector3.one * (.5f + t * 1.2f);
                rippleImage.color = new Color(1f, .86f, .5f, .9f * (1f - t));
                if (t >= 1f) { Destroy(ripple.gameObject); ripple = null; }
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame
                && !EventSystem.current.IsPointerOverGameObject())
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        townMap, mouse.position.ReadValue(), null, out var local))
                {
                    var point = new Vector2(local.x / townScale, -local.y / townScale);
                    if (point.x >= 0f && point.x <= town.ArtWidth && point.y >= 0f && point.y <= town.ArtHeight)
                    {
                        walkTarget = ClampToWalkable(point);
                        hasTarget = true;
                        ShowRipple(ToLocal(walkTarget));
                    }
                }
            }
        }

        // 鸟瞰透视：画的上端是远方，角色按 y 缩小。
        void ApplyPerspective()
        {
            var depth = Mathf.Clamp01((localPos.y - 90f) / (1050f - 90f));
            traveler.SetScale(Mathf.Lerp(town.PerspectiveTop, town.PerspectiveBottom, depth));
        }

        static readonly string[] HourBranches = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };

        // 构建版本戳：左下角显示当前提交的短哈希（Tools/write-build-stamp.py 写入）。
        // "改了没变化"这类反馈没法自证，屏幕上看得到版本号，对不上就是没跑到最新代码。
        // 后面跟一段贴图指纹：运行时直接读当前角色贴图的像素特征，和 Tools 侧从文件算出的
        // 期望值一比，就能判定"是贴图没更新"还是"是切块/姿态的问题"——两者修法完全不同。
        void BuildVersionStamp(Texture2D travelerView)
        {
            var asset = Resources.Load<TextAsset>("Config/build-stamp");
            var text = "build " + (asset == null ? "?" : asset.text.Trim());
            if (travelerView != null)
            {
                text += $"　{travelerView.name} {travelerView.width}x{travelerView.height}";
                try
                {
                    // GetPixels 自下而上排列：from 之后的 70% 对应画面上方的七成。
                    // 贴图没开 Read/Write 时这里会抛，诊断信息不该把游戏带崩。
                    var pixels = travelerView.GetPixels();
                    long sum = 0;
                    for (var i = (int)(pixels.Length * .30f); i < pixels.Length; i++)
                        sum += (long)(pixels[i].a * 255f);
                    text += $" #{sum % 1000000}";
                }
                catch (System.Exception)
                {
                    text += " #unreadable";
                }
            }
            ui.Label(content, text, 24, 1024, 640, 34, 20, new Color(.32f, .35f, .31f, .85f),
                TextAlignmentOptions.BottomLeft);
        }

        // 左上 HUD：头像窗 + 生命/精力条 + 铜钱与行历，替换原先孤零零的镇名大字。
        // 生命/精力取战斗同源的 PlayerStatsFor（T017 养成系统接入后这里自动跟上）。
        void BuildTownHud()
        {
            var preset = System.Array.Find(presets, p => p.id == activeSave.characterId) ?? presets[0];
            var stats = PlayerStatsFor(activeSave.characterId);
            var hud = ui.Panel(content, "Town hud", 88, 26, 700, 130, new Color(.06f, .1f, .08f, .66f));
            var portrait = ui.Rect(hud.transform, "Hud portrait", 14, 12, 94, 94);
            var portraitImage = portrait.gameObject.AddComponent<RawImage>();
            var face = preset.Facing(0, activeSave.gender, activeSave.faceStyle);
            portraitImage.texture = face != null ? face : Texture2D.whiteTexture;
            // 正面立绘裁头肩段：UV 的 v 向上，取图的上中部。
            portraitImage.uvRect = new Rect(.32f, .5f, .36f, .44f);
            ui.Label(hud.transform, activeSave.characterName, 126, 12, 264, 36, 24, UiKit.Paper,
                TextAlignmentOptions.TopLeft, true);
            ui.Label(hud.transform, preset.displayName, 126, 52, 264, 28, 17, UiKit.Gold);
            var hpBack = ui.Panel(hud.transform, "Hp back", 126, 86, 270, 16, new Color(.2f, .12f, .1f, .95f));
            ui.Panel(hpBack.transform, "Hp fill", 2, 2, 266, 12, new Color(.7f, .22f, .16f, .96f));
            ui.Label(hud.transform, stats.hp + "/" + stats.hp, 402, 84, 90, 20, 14,
                new Color(.95f, .8f, .75f, .95f), TextAlignmentOptions.MidlineLeft);
            var spBack = ui.Panel(hud.transform, "Sp back", 126, 108, 270, 8, new Color(.16f, .17f, .12f, .95f));
            ui.Panel(spBack.transform, "Sp fill", 2, 2, 266, 4, new Color(.85f, .68f, .3f, .96f));
            var now = activeSave.WorldTimeNow();
            var branch = HourBranches[(now.minuteOfDay / 60 + 1) / 2 % 12];
            ui.Label(hud.transform, "铜钱 " + activeSave.coins + " 文", 500, 12, 184, 30, 18, UiKit.Gold,
                TextAlignmentOptions.TopLeft);
            ui.Label(hud.transform, "第 " + now.day + " 日 · " + branch + "时", 500, 46, 184, 26, 15, UiKit.Paper,
                TextAlignmentOptions.TopLeft);
            ui.Label(hud.transform, town.Name, 500, 76, 184, 32, 20, UiKit.Paper, TextAlignmentOptions.TopLeft, true);
        }

        // 主角脚下名牌：每帧跟底座点同步；不进深度表（浮层语义，走到道具后也不会被挡）。
        void BuildTravelerTag()
        {
            nameTag = ui.Panel(townMap, "Traveler tag", 0, -80, 220, 34, new Color(.06f, .1f, .08f, .72f)).rectTransform;
            nameTag.pivot = new Vector2(.5f, 1f);
            nameTag.anchorMin = nameTag.anchorMax = new Vector2(0, 1);
            ui.Label(nameTag, activeSave.characterName, 8, 3, 204, 28, 20, new Color(.98f, .95f, .86f, .97f),
                TextAlignmentOptions.Center);
            nameTag.SetAsLastSibling();
        }

        // 镜头跟随：主角恒居视口中心，钳在图边内（贴边时主角偏离中心、图不再滚动）。
        // townMap 锚在父左上、枢轴自身左上：x 向右为正，合法区间 [1920-W, 0]；
        // y 向上为正、图向下延伸，合法区间 [0, H-1080]——两个轴的钳制方向相反。
        void UpdateCamera()
        {
            if (townMap == null) return;
            var focus = ToLocal(localPos);
            var x = Mathf.Clamp(960f - focus.x, 1920f - townMap.rect.width, 0f);
            var y = Mathf.Clamp(-540f - focus.y, 0f, townMap.rect.height - 1080f);
            townMap.anchoredPosition = new Vector2(x, y);
        }

        // 验收钩子：镜头跟随是否把主角留在 1920×1080 视口内。
        public bool TownTravelerOnScreen
        {
            get
            {
                if (townMap == null) return false;
                var view = townMap.anchoredPosition + ToLocal(localPos);
                return view.x >= 0f && view.x <= 1920f && view.y <= 0f && view.y >= -1080f;
            }
        }

        // 纵深排序：人物站在谁身后就被谁挡住——基座 y 比人物小（更远）的条目在人物之下。
        // 与 ArrangeTownLayers 用同一张深度表，插队位置和实际绘制顺序必然一致。
        void SortTravelerDepth()
        {
            var slot = townLayers.Count(layer => layer.Rect != null && layer.BaseY <= localPos.y);
            var currentIndex = traveler.transform.GetSiblingIndex();
            // 尘点层永远排在第一个子节点之下：排序时补一位，免得它压到人物身上。
            var targetIndex = Mathf.Clamp(slot + 1, 0, townMap.childCount - 1);
            if (currentIndex != targetIndex) traveler.transform.SetSiblingIndex(targetIndex);
        }

        void TryMove(Vector2 delta)
        {
            var next = localPos + delta;
            if (WalkablePoint(next)) { localPos = next; return; }
            if (WalkablePoint(new Vector2(next.x, localPos.y))) { localPos = new Vector2(next.x, localPos.y); return; }
            if (WalkablePoint(new Vector2(localPos.x, next.y))) localPos = new Vector2(localPos.x, next.y);
        }

        bool WalkablePoint(Vector2 point) => town.CanStand(point.x, point.y);
        Vector2 ClampToWalkable(Vector2 point)
        {
            var clamped = town.ClampToWalkable(point.x, point.y);
            return new Vector2(clamped.x, clamped.y);
        }

        Vector2 ToLocal(Vector2 artPoint) => new(artPoint.x * townScale, -artPoint.y * townScale);

        void ShowRipple(Vector2 local)
        {
            if (ripple != null) Destroy(ripple.gameObject);
            ripple = new GameObject("Walk ripple", typeof(RectTransform)).GetComponent<RectTransform>();
            ripple.SetParent(townMap, false);
            ripple.anchorMin = ripple.anchorMax = new Vector2(0, 1);
            ripple.pivot = new Vector2(.5f, .5f);
            ripple.anchoredPosition = local;
            ripple.sizeDelta = new Vector2(54f, 54f);
            rippleImage = ripple.gameObject.AddComponent<RawImage>();
            rippleImage.texture = TownRingTexture(.62f);
            rippleImage.raycastTarget = false;
            rippleImage.color = new Color(1f, .86f, .5f, .9f);
            rippleStart = Time.unscaledTime;
        }

        static Texture2D ringTexture;

        // 细描金环：点击落点标记；inner 控制环的粗细。
        static Texture2D TownRingTexture(float inner)
        {
            if (ringTexture != null) return ringTexture;
            const int s = 64;
            ringTexture = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (var y = 0; y < s; y++)
            for (var x = 0; x < s; x++)
            {
                var dx = (x + .5f) / s - .5f;
                var dy = (y + .5f) / s - .5f;
                var d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                var ring = Mathf.Clamp01(1f - Mathf.Abs(d - .8f) / inner);
                ringTexture.SetPixel(x, y, new Color(1f, 1f, 1f, ring * ring));
            }
            ringTexture.Apply(false, true);
            return ringTexture;
        }
    }
}
