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
        public InnateFateView InnateFateForCheck => modal == null ? null : modal.GetComponent<InnateFateView>();

        public bool ShowInnateFate(SaveData data, bool returnToIdentity = false)
        {
            if (data == null || content == null || (page != "slots" && page != "town")) return false;
            if (page == "town" && (!ReferenceEquals(data, activeSave) || WorldDialogueInputBlocked || WorldCombatActive)) return false;
            if (modal != null && (!returnToIdentity || modal.name != "Dao identity modal")) return false;
            var ownerContent = content; var ownerSave = activeSave; int ownerSlot = selectedMemoSlot; string ownerPage = page;
            if (page == "town") { CloseInventory(); CloseChapterPanel(); CloseWorldCombatWheel(); StopDialogueMovement(); }
            CloseModal();
            var group = content.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            modal = ui.Stretch(screenCanvas, "Innate fate modal");
            var veil = modal.gameObject.AddComponent<Image>(); veil.color = UiKit.Hex("102320"); veil.raycastTarget = true;
            var host = modal;
            var view = host.gameObject.AddComponent<InnateFateView>();
            view.Configure(ui, host, data, () => settings.reduceMotion, () =>
            {
                if (modal != host || content != ownerContent || page != ownerPage || !ReferenceEquals(ownerSave, activeSave) ||
                    (page == "slots" && selectedMemoSlot != ownerSlot)) return;
                CloseModal();
                if (returnToIdentity && page == "town") ShowDaoIdentityLedger();
                dialogueConsumeThroughFrame = Math.Max(dialogueConsumeThroughFrame, Time.frameCount + 1);
                if (page == "slots" && memoPaper != null)
                {
                    var entry = memoPaper.Find("Dossier fate inspect");
                    if (entry != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(entry.gameObject);
                }
            }, PlayClick);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            return true;
        }

        void BuildMemoInnate(Transform paper, float x, float y, float width, SaveData data, string locked, bool compact = false)
        {
            var rect = ui.Rect(paper, "Dossier fate inspect", x, y, width, compact ? 158 : 180);
            var surface = rect.gameObject.AddComponent<QingshiComicPaperGraphic>();
            surface.Kind = QingshiComicPaperGraphic.Surface.Note; surface.color = UiKit.Hex("E7DABD");
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = surface; button.interactable = data != null;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = UiKit.Hex("FFF3D3");
            colors.pressedColor = UiKit.Hex("C7B690"); colors.selectedColor = UiKit.Hex("F7E9C7"); colors.disabledColor = UiKit.Hex("CBC6B3"); button.colors = colors;
            button.onClick.AddListener(() => { if (button != null && button.isActiveAndEnabled && button.interactable && memoPaper == paper && modal == null) { PlayClick(); ShowInnateFate(data); } });
            var image = ui.Rect(rect, "Dossier sleeping jade compass", 15, compact ? 9 : 19, 140, 140).gameObject.AddComponent<WuxingCompassGraphic>(); image.Configure();
            bool revealed = InnateFateView.RootsRevealed(data);
            string title = data == null ? locked : revealed ? "水火双极 · 灵根已显" : "五行未测 · 灵盘静候";
            MemoLabel(rect, "Dossier innate roots", title, 174, 16, width - 290, 42, 25);
            MemoLabel(rect, "Dossier innate inspect caption", data == null ? "待修" : "观照 ›", width - 110, 20, 85, 36, 22, TextAlignmentOptions.Center);
            string traitNames = string.Join(" · ", (data?.traits ?? Array.Empty<string>()).Select(id => CharacterGen.FindTrait(id)?.Name ?? id));
            MemoLabel(rect, "Dossier traits", "先天词条 · " + (locked ?? (traitNames.Length > 0 ? traitNames : "天赋待显")), 174, compact ? 59 : 67, width - 202, compact ? 38 : 44, compact ? 22 : 24);
            MemoLabel(rect, "Dossier destiny", "命格 · " + (locked ?? (CharacterGen.FindDestiny(data?.destiny)?.Name ?? "命星待照")), 174, compact ? 104 : 121, width - 202, 38, 23);
        }

        SaveData DraftInnatePreview() => new SaveData {
            slot = selectedMemoSlot, characterId = presets[draftOrigin].id, characterName = draftName,
            gender = draftGender, faceStyle = draftFaceStyle, traits = (string[])draftTraits.Clone(), destiny = draftDestiny
        };
    }
}
