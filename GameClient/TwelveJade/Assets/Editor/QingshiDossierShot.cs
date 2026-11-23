using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TwelveJade.Core;
using TwelveJade.Network;
using TwelveJade.Network.UI;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    public static partial class PrologueTownShot
    {
        static DossierTransport dossierTransport;

        // Install before NetworkRuntime.Start: UI checks never use player credentials or the live service.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void IsolateDossierNetwork()
        {
            if (state == null || !state.dossier || state.phase != "play") return;
            LocalCredentialManager.SetStoreForTests(new DossierMemoryStore());
            LocalCredentialManager.StoreCredentials("dossier-ui-fixture", 1, "验收行者");
            dossierTransport = new DossierTransport();
            var client = new NetworkClient(NetworkConfig.Load(), dossierTransport) { SkipBackoffWaitsForTests = true };
            client.BindCredentials(() => LocalCredentialManager.Token, _ => { });
            var go = new GameObject("Isolated dossier network");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var runtime = go.AddComponent<NetworkRuntime>();
            typeof(NetworkRuntime).GetField("<Client>k__BackingField", Flags).SetValue(runtime, client);
            typeof(NetworkRuntime).GetField("<Auth>k__BackingField", Flags).SetValue(runtime, new AuthService(client));
            typeof(NetworkRuntime).GetField("<Cloud>k__BackingField", Flags).SetValue(runtime,
                new CloudSaveService(client, NetworkRuntime.CreateLocalRepository));
        }

        static string DossierCredentialsFingerprint()
        {
            var content = string.Join("\n", LocalCredentialManager.KnownKeys.Select(k =>
                k + ":" + PlayerPrefs.HasKey(k) + ":" + PlayerPrefs.GetString(k, "")));
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(content)));
        }

        static IEnumerator DossierChecks()
        {
            foreach (var path in new[] { "Assets/Editor/QingshiDossierShot.cs", "Assets/Scripts/UI/Auth/NetworkDialogEntry.cs" })
            { Bind(path); Bind(path + ".meta"); }
            Require("dossier network replaced before startup", dossierTransport != null &&
                LocalCredentialManager.Username == "验收行者");
            Require("scratch dossier repository", SamePath(Field(host.Repository, "root") as string, state.scratch));
            canvasRoot = Field(host, "canvasObject") as GameObject;
            Require("dossier real overlay canvas", canvasRoot != null && canvasRoot.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay);
            var preset = Resources.LoadAll<CharacterPreset>("Characters").OrderBy(p => p.id).First();
            host.Repository.Create(1, preset.id, "青石行客", "male", 0, null, "shaxing");
            host.Repository.Create(2, preset.id, "归溪行者", "female", 0, null, "shaxing");
            var savedFiles = DirectoryEntries(state.scratch);
            yield return ResizeDossierView(1920, 1080);
            host.ShowMenu();
            yield return Frames(30);
            var entry = UnityEngine.Object.FindAnyObjectByType<NetworkDialogEntry>();
            Require("entry bootstrap exists", entry != null);
            Check("title has no account or cloud entry", !Live("Slots network tools") && !Live("Menu account") && !Live("Menu cloud saves"));
            Invoke(entry, "OpenDialog", false, false);
            Check("title network shortcut ignored", !AuthDialog.IsOpen);
            yield return Capture("dossier-01-menu", "menu-no-network-entry");
            ClickDossier("Menu primary");
            yield return Until(() => host.CurrentPage == "slots" && Live("Slots network tools") && host.CanOpenNetworkDialog,
                "menu primary reaches save slots");
            CheckDossierLayout("male", "wide");
            yield return Capture("dossier-02-male", "wide-male-cutout");
            ClickDossier("Slots account");
            yield return Frames(3);
            Check("real account click opens dialog", AuthDialog.IsOpen && !Live("Slots network tools"));
            Invoke(entry, "OpenDialog", true, false);
            Check("existing dialog blocks other shortcut", !CloudSaveDialog.IsOpen);
            yield return Capture("dossier-03-account", "account-open");
            ClickDossierLabel("告 辞");
            yield return Until(() => !AuthDialog.IsOpen && Live("Slots network tools"), "account close restores slot entry");
            ClickDossier("Slots cloud saves");
            yield return Frames(8);
            Check("real cloud click opens dialog", CloudSaveDialog.IsOpen && !Live("Slots network tools"));
            Check("cloud list uses isolated read-only request", dossierTransport.ListReads > 0 && dossierTransport.Writes == 0);
            yield return Capture("dossier-04-cloud", "cloud-open-fake-read-only-service");
            ClickDossierLabel("告 辞");
            yield return Until(() => !CloudSaveDialog.IsOpen && Live("Slots network tools"), "cloud close restores slot entry");
            ClickDossier("Slot 2");
            yield return Frames(5);
            CheckDossierLayout("female", "wide");
            yield return Capture("dossier-05-female", "wide-female-cutout");
            yield return ResizeDossierView(1280, 960);
            yield return Until(() => Live("Slots network tools") && host.CanOpenNetworkDialog, "resized slot page ready");
            CheckDossierLayout("female", "narrow");
            yield return Capture("dossier-06-narrow", "narrow-female-cutout");
            ClickDossier("Slots account");
            yield return Frames(3);
            Check("resized account button remains clickable", AuthDialog.IsOpen);
            ClickDossierLabel("告 辞");
            yield return Frames(4);
            ClickDossier("Slot 3");
            yield return Frames(5);
            Check("empty slot remains empty during preview", host.Repository.Read(3).State == SlotState.Empty);
            ClickDossier("Draft gender female");
            yield return Frames(5);
            CheckDossierLayout("female", "draft");
            yield return Capture("dossier-07-draft", "empty-slot-female-preview");
            Check("preview, switches, and dialogs preserve local saves", savedFiles.SequenceEqual(DirectoryEntries(state.scratch)));
            ClickDossier("Slot enter");
            yield return Until(() => host.CurrentPage == "town" && host.TravelerPuppetForShot != null, "draft entry creates and enters town", 20);
            Check("created female save readable", host.Repository.Read(3).CanLoad && host.Repository.Read(3).Data.gender == "female");
            Check("town has no slot buttons", !Live("Slots network tools"));
            Invoke(entry, "OpenDialog", false, false);
            yield return Frames(3);
            Check("town account shortcut remains supported", AuthDialog.IsOpen);
            ClickDossierLabel("告 辞");
            yield return Frames(3);
            host.ShowSettings();
            Invoke(entry, "OpenDialog", false, false);
            Check("settings modal blocks network shortcut", !AuthDialog.IsOpen);
            Invoke(host, "CloseSettings");
            host.ShowSlots(false);
            yield return Until(() => Live("Slots network tools") && host.CanOpenNetworkDialog, "return to slots restores entry");
            ClickDossier("Slot 1");
            yield return Frames(4);
            ClickDossier("Slot enter");
            yield return Until(() => host.CurrentPage == "town" && host.ActiveSave?.slot == 1, "existing save still enters town", 20);
            host.ShowSlots(false);
            yield return Until(() => Live("Slots network tools") && host.CanOpenNetworkDialog, "slot redraw ready");
            Check("redraw has one entry pair", canvasRoot.GetComponentsInChildren<Button>(true).Count(b => b.name == "Slots account") == 1 &&
                canvasRoot.GetComponentsInChildren<Button>(true).Count(b => b.name == "Slots cloud saves") == 1);
            ClickDossier("Slots back");
            yield return Until(() => host.CurrentPage == "menu", "back button returns to menu");
            yield return Frames(4);
            Check("leaving slots removes entry", !Live("Slots network tools"));
            Check("UI test issued no remote writes", dossierTransport.Writes == 0);
        }

        static IEnumerator ResizeDossierView(int width, int height)
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
            var view = EditorWindow.GetWindow(type);
            view.position = new Rect(0, 0, width, height);
            view.Show(); view.Focus(); view.Repaint();
            yield return Frames(20);
            Check("live GameView resized " + width + "x" + height, Screen.width > 0 && Screen.height > 0,
                "actual=" + Screen.width + "x" + Screen.height);
        }

        static void CheckDossierLayout(string gender, string layout)
        {
            Canvas.ForceUpdateCanvases();
            var poster = Find("Dossier portrait")?.GetComponent<PosterCardView>();
            Require(layout + " " + gender + " cutout exists", poster != null && poster.HasArtwork && poster.IsCutout);
            Check(layout + " matching gender resource", poster.ResourcePath.EndsWith("qingshi-" + gender + "-figure", StringComparison.Ordinal));
            BindAsset(poster.Artwork.texture);
            Check(layout + " no portrait picture frame", poster.Frame == null);
            Check(layout + " full figure UV", poster.Crop == new Rect(0, 0, 1, 1));
            var image = poster.Artwork.rectTransform.rect;
            var bounds = ((RectTransform)poster.transform).rect;
            float sourceRatio = (float)poster.Artwork.texture.width / poster.Artwork.texture.height;
            Check(layout + " undistorted full-height figure", Mathf.Abs(image.width / image.height - sourceRatio) < .001f &&
                image.width <= bounds.width && image.height <= bounds.height && image.height >= bounds.height - 20);
            Check(layout + " illustration does not intercept clicks", !poster.Artwork.raycastTarget);
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(poster.Artwork.texture));
            Check(layout + " transparency imported", importer.alphaIsTransparency && importer.DoesSourceTextureHaveAlpha());
            var tools = (RectTransform)Find("Slots network tools").transform;
            Rect toolsRect = DossierScreenRect(tools);
            Check(layout + " entry inside viewport", toolsRect.xMin >= 0 && toolsRect.yMin >= 0 && toolsRect.xMax <= Screen.width + 1 && toolsRect.yMax <= Screen.height + 1);
            Check(layout + " entry above paper", toolsRect.yMin > DossierScreenRect((RectTransform)poster.transform.parent).yMax);
            Check(layout + " entry clear of back", !toolsRect.Overlaps(DossierScreenRect((RectTransform)Find("Slots back").transform)));
            Check(layout + " entry clear of enter", !toolsRect.Overlaps(DossierScreenRect((RectTransform)Find("Slot enter").transform)));
            foreach (string name in new[] { "Slots account", "Slots cloud saves", "Slot enter", "Slots back" }) DossierButton(name, false);
        }

        static Rect DossierScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
        static void ClickDossier(string name) => DossierButton(name, true);
        static void ClickDossierLabel(string text)
        {
            var button = canvasRoot.GetComponentsInChildren<Button>(false).FirstOrDefault(b =>
                b.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => Compact(t.text) == Compact(text)));
            Require("dialog close button exists", button != null);
            DossierButton(button, true);
        }
        static void DossierButton(string name, bool click)
        {
            var button = Find(name)?.GetComponent<Button>();
            Require("button exists " + name, button != null); DossierButton(button, click);
        }
        static void DossierButton(Button button, bool click)
        {
            Require("button interactable " + button.name, button.IsActive() && button.IsInteractable());
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current) {
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)),
                button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Require("real raycast reaches " + button.name, hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
                "top=" + (hits.Count == 0 ? "none" : hits[0].gameObject.name));
            if (click) ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        sealed class DossierMemoryStore : IKeyValueStore
        {
            readonly Dictionary<string, string> data = new Dictionary<string, string>();
            public string GetString(string key, string fallback) => data.TryGetValue(key, out var value) ? value : fallback;
            public void SetString(string key, string value) => data[key] = value;
            public void DeleteKey(string key) => data.Remove(key);
            public bool HasKey(string key) => data.ContainsKey(key);
        }
        sealed class DossierTransport : NetworkClient.ITransport
        {
            public int ListReads, Writes;
            public IEnumerator Send(string method, string url, string jsonBody, float timeoutSeconds,
                string authToken, Action<NetworkClient.TransportResult> done)
            {
                if (method != "GET") Writes++;
                if (url.EndsWith("/api/save/list", StringComparison.Ordinal)) ListReads++;
                done(new NetworkClient.TransportResult { HttpStatus = 200,
                    Body = "{\"code\":0,\"message\":\"fixture\",\"data\":{\"slots\":[]}}" });
                yield break;
            }
        }
    }
}
