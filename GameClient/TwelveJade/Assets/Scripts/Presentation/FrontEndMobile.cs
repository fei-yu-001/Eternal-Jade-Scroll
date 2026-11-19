using UnityEngine;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        RectTransform mobileControls;
        MobileMovePad mobileMovePad;

        void BuildMobileControls()
        {
            if (!MobileControls.Enabled) return;
            mobileControls = ui.Stretch(content, "Mobile controls");
            mobileMovePad = MobileMovePad.Create(ui, mobileControls);
            AestheticUi.Jade(ui, mobileControls, "Touch dodge", "闪身", new Rect(1278, 908, 172, 94),
                () => ActivateWorldCombatSlot(2), () => settings.reduceMotion, PlayClick, 30);
            AestheticUi.Jade(ui, mobileControls, "Touch combat", "出招", new Rect(1278, 800, 172, 84),
                OpenWorldCombatWheel, () => settings.reduceMotion, PlayClick, 28);
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused || !MobileControls.Enabled) return;
            mobileMovePad?.ResetInput();
            CancelTravel();
            if (activeSave != null && repository != null) Guard(() => repository.Write(activeSave));
        }

        void RefreshMobileControls()
        {
            if (mobileControls == null) return;
            bool usable = page == "town" && !WorldDialogueInputBlocked && modal == null && bagOverlay == null;
            if (mobileControls.gameObject.activeSelf != usable) mobileControls.gameObject.SetActive(usable);
        }
    }
}
