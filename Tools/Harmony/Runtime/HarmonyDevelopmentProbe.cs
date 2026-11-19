#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Linq;
using TwelveJade.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Included only in the isolated development build. No commands, credentials or save contents.
public sealed class HarmonyDevelopmentProbe : MonoBehaviour
{
    FrontEndController controller;
    float next;
    string lastPage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe() => new GameObject("Harmony development evidence").AddComponent<HarmonyDevelopmentProbe>();

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 2;
        if (controller == null) controller = FindAnyObjectByType<FrontEndController>();
        if (controller == null) return;
        var pos = controller.PlayerWorldPosition;
        Debug.Log("JADE_DEVICE_STATE " + JsonUtility.ToJson(new State {
            page = controller.CurrentPage, x = pos.x, y = pos.y,
            width = Screen.width, height = Screen.height,
            touchDevice = Touchscreen.current != null,
            online = !TwelveJade.Network.LocalCredentialManager.IsOfflineLocal,
            graphics = SystemInfo.graphicsDeviceType.ToString(),
            frameMilliseconds = Time.unscaledDeltaTime * 1000,
            allocatedMegabytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)
        }));
        if (lastPage == controller.CurrentPage) return;
        lastPage = controller.CurrentPage;
        foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.isActiveAndEnabled && b.interactable).Take(45))
        {
            var rect = button.transform as RectTransform;
            if (rect == null) continue;
            var canvas = button.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
            Debug.Log("JADE_DEVICE_BUTTON " + JsonUtility.ToJson(new Control { name = button.name, x = point.x, y = Screen.height - point.y }));
        }
    }

    [Serializable] sealed class State
    {
        public string page, graphics;
        public float x, y, frameMilliseconds;
        public int width, height;
        public long allocatedMegabytes;
        public bool touchDevice, online;
    }
    [Serializable] sealed class Control { public string name; public float x, y; }
}
#endif
