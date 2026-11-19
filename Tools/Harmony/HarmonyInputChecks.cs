using System;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

public static class HarmonyInputChecks
{
    const string Pending = "EternalJade.HarmonyInputChecks.Pending";

    public static void Run()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    static void ResumeAfterDomainReload()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        EditorApplication.update += RunWhenPlaying;
    }

    static void RunWhenPlaying()
    {
        if (!EditorApplication.isPlaying || !Application.isPlaying || Time.timeSinceLevelLoad < .2f || EditorApplication.isCompiling) return;
        EditorApplication.update -= RunWhenPlaying;
        SessionState.SetBool(Pending, false);
        try { CheckInputs(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void CheckInputs()
    {
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var touch = InputSystem.AddDevice<Touchscreen>();
        GameObject host = null, events = null;
        try
        {
            InputSystem.QueueStateEvent(touch, new TouchState { touchId = 1, phase = TouchPhase.Began, position = new Vector2(321, 456) });
            InputSystem.Update();
            Debug.Log("HARMONY_INPUT_TEST_STATE playing=" + Application.isPlaying + " enabled=" + touch.enabled + " press=" + touch.primaryTouch.press.isPressed + " began=" + touch.primaryTouch.press.wasPressedThisFrame + " position=" + touch.primaryTouch.position.ReadValue());
            Check(MobileControls.TryGroundPress(out var point) && Vector2.Distance(point, new Vector2(321, 456)) < .1f, "touch begins a ground press");
            InputSystem.QueueStateEvent(touch, new TouchState { touchId = 1, phase = TouchPhase.Moved, position = new Vector2(340, 456) });
            InputSystem.Update();
            Check(!MobileControls.TryGroundPress(out _), "touch drag does not repeatedly choose destinations");
            InputSystem.QueueStateEvent(touch, new TouchState { touchId = 1, phase = TouchPhase.Ended, position = new Vector2(340, 456) });
            InputSystem.Update();
            events = new GameObject("Input checks events", typeof(EventSystem));
            host = new GameObject("Input checks canvas", typeof(RectTransform), typeof(Canvas));
            host.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var kit = TwelveJade.Network.UI.NetworkUi.Kit;
            var pad = MobileMovePad.Create(kit, host.transform);
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)pad.transform;
            Vector2 ScreenAt(Vector2 offset) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center + offset));
            var first = new PointerEventData(events.GetComponent<EventSystem>()) { pointerId = 11, position = ScreenAt(new Vector2(84, 0)) };
            pad.OnPointerDown(first);
            Check(pad.Direction.x > .99f && pad.Running, "full pad deflection runs");
            var second = new PointerEventData(events.GetComponent<EventSystem>()) { pointerId = 12, position = ScreenAt(new Vector2(-84, 0)) };
            pad.OnPointerDown(second); pad.OnDrag(second); pad.OnPointerUp(second);
            Check(pad.Direction.x > .99f, "second finger cannot steal or release movement");
            first.position = ScreenAt(new Vector2(30, 0)); pad.OnDrag(first);
            Check(pad.Direction.x > .3f && !pad.Running, "partial deflection walks");
            pad.OnPointerUp(first);
            Check(pad.Direction == Vector2.zero, "owner release stops movement");
            pad.OnPointerDown(first); pad.gameObject.SetActive(false);
            Check(pad.Direction == Vector2.zero, "modal/page disable clears held movement");
            Debug.Log("ETERNAL_JADE_HARMONY_INPUT_CHECKS_PASS 7 assertions");
        }
        finally
        {
            InputSystem.RemoveDevice(touch);
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            if (events != null) UnityEngine.Object.DestroyImmediate(events);
        }
    }

    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Mobile input check failed: " + label);
        Debug.Log("HARMONY_INPUT_PASS " + label);
    }
}
