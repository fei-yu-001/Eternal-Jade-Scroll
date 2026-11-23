using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using InputKey = UnityEngine.InputSystem.Key;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    public static partial class PrologueTownShot
    {
        static IEnumerator LocomotionChecks()
        {
            foreach (string path in new[] { "Assets/Editor/QingshiLocomotionShot.cs", "Assets/Editor/ArtImportGuard.cs", "Assets/Scripts/Presentation/PuppetActor.cs",
                "Assets/Scripts/Presentation/MenuMemoArt.cs", "Assets/Scripts/Presentation/MenuOrnament.cs", "Assets/Resources/Config/town-character-art.json" })
            { Bind(path); Bind(path + ".meta"); }
            // The desktop stays available to the player during this isolated editor test.
            // A cloned in-memory setting lets the owned virtual keyboard survive focus changes.
            var originalInputSettings = InputSystem.settings;
            var verificationInputSettings = UnityEngine.Object.Instantiate(originalInputSettings);
            verificationInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            verificationInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            bool wasRunningInBackground = Application.runInBackground;
            Application.runInBackground = true;
            InputSystem.settings = verificationInputSettings;
            var disabled = InputSystem.devices.Where(d => d.enabled && (d is Keyboard || d is Mouse)).ToArray();
            foreach (var device in disabled) InputSystem.DisableDevice(device);
            var keys = InputSystem.AddDevice<Keyboard>("LocomotionVerificationKeyboard");
            try
            {
                yield return ResizeDossierView(1600, 1000);
                foreach (int slot in new[] { 1, 2 })
                {
                    string gender = slot == 1 ? "male" : "female";
                    host.LoadSlot(host.Repository.Read(slot));
                    yield return Until(() => host.CurrentPage == "town" && host.TravelerPuppetForShot != null, gender + " locomotion town ready", 20);
                    var actor = host.TravelerPuppetForShot;
                    foreach (var view in new[] { "front", "side", "back" })
                    {
                        var entry = TownCharacterArt.Find("hero-" + gender + "-" + view);
                        Check(gender + view + " eight-frame locomotion cycle", entry.frames == 8 && entry.cycleDistance == 140);
                        var stand = TownCharacterArt.Hero(gender, view, false); var walk = TownCharacterArt.Hero(gender, view, true);
                        Check(gender + view + " full resolution frame strip", walk != null && stand != null &&
                            walk.width == stand.width * 8 && walk.height == stand.height && walk.width == 3072);
                        BindAsset(stand); BindAsset(walk);
                    }
                    foreach (bool run in new[] { false, true })
                    foreach (var direction in new[] { (InputKey.D, Vector2.right, 1), (InputKey.A, Vector2.left, 3),
                        (InputKey.S, Vector2.up, 0), (InputKey.W, Vector2.down, 2) })
                    {
                        string label = gender + "-" + (run ? "run-" : "walk-") + direction.Item1;
                        Invoke(host, "CancelTravel");
                        var start = OpenLocomotionLane(direction.Item2);
                        Require(label + " safe lane", host.SnapTravelerTo(start.x, start.y));
                        yield return Frames(3);
                        var seen = new HashSet<int>();
                        var drawn = (RawImage)Field(actor, "frameImage");
                        int renderer = drawn.GetInstanceID();
                        float strideBefore = actor.StrideDistanceForCheck;
                        InputSystem.QueueStateEvent(keys, run ? new KeyboardState(direction.Item1, InputKey.LeftShift) : new KeyboardState(direction.Item1));
                        keys.MakeCurrent();
                        float until = Time.realtimeSinceStartup + 1.15f;
                        while (Time.realtimeSinceStartup < until)
                        {
                            yield return new WaitForEndOfFrame();
                            int frame = actor.FrameIndexForCheck;
                            if (actor.CurrentMotion != PuppetActor.Motion.Idle && seen.Add(frame) && !run)
                                CaptureLocomotionCrop(label + "-" + frame, actor);
                        }
                        float moved = Vector2.Distance(start, host.PlayerWorldPosition);
                        Check(label + " real keyboard moved through world", moved > 140, "moved=" + moved + "; focused=" + Application.isFocused + "; virtualEnabled=" + keys.enabled + "; pressed=" + keys[direction.Item1].isPressed);
                        Check(label + " cycles through distinct steps", seen.Count >= 7, "frames=" + string.Join(",", seen));
                        Check(label + " correct four-way facing", host.ActiveSave.facing == direction.Item3);
                        Check(label + " renderer survives turns", ((RawImage)Field(actor, "frameImage")).GetInstanceID() == renderer);
                        Check(label + " recorded actual displacement", Mathf.Abs(actor.StrideDistanceForCheck - strideBefore - moved) < 1f);
                        Check(label + " no turn squashing", Vector3.Distance(actor.Root.localScale, actor.BaseScale) < .0001f);
                        if (!run && direction.Item1 == InputKey.D) yield return Capture("locomotion-" + gender + "-town", "keyboard-walking");
                        InputSystem.QueueStateEvent(keys, new KeyboardState());
                        yield return new WaitForSecondsRealtime(.35f);
                        Check(label + " stops in planted idle", actor.CurrentMotion == PuppetActor.Motion.Idle && actor.StandingFrameForCheck);
                        float restStride = actor.StrideDistanceForCheck; var restPosition = host.PlayerWorldPosition;
                        yield return new WaitForSecondsRealtime(.15f);
                        Check(label + " no treadmill when stopped", restStride == actor.StrideDistanceForCheck && Vector2.Distance(restPosition, host.PlayerWorldPosition) < .001f);
                    }
                    // Near-diagonal input keeps its axis; an unambiguous cardinal turn still changes immediately.
                    Invoke(host, "UpdateTravelerFacing", Vector2.right);
                    for (int i = 0; i < 12; i++) Invoke(host, "UpdateTravelerFacing", new Vector2(1, i % 2 == 0 ? .98f : 1.02f));
                    Check(gender + " diagonal steering does not chatter", host.TravelerViewForShot == 1);
                    Invoke(host, "UpdateTravelerFacing", Vector2.up);
                    Check(gender + " cardinal turn remains immediate", host.TravelerViewForShot == 0);
                }
                host.ShowMenu(); yield return Frames(15);
                InputSystem.QueueStateEvent(keys, new KeyboardState(InputKey.F2)); keys.MakeCurrent(); yield return Frames(3);
                Check("F2 no longer opens cloud window", !TwelveJade.Network.UI.CloudSaveDialog.IsOpen);
                InputSystem.QueueStateEvent(keys, new KeyboardState()); yield return Frames(2);
                InputSystem.QueueStateEvent(keys, new KeyboardState(InputKey.F1)); yield return Frames(3);
                Check("F1 still opens account on title", TwelveJade.Network.UI.AuthDialog.IsOpen);
                TwelveJade.Network.UI.AuthDialog.Close();
            }
            finally
            {
                InputSystem.RemoveDevice(keys);
                foreach (var device in disabled) if (device.added) InputSystem.EnableDevice(device);
                InputSystem.settings = originalInputSettings;
                Application.runInBackground = wasRunningInBackground;
                UnityEngine.Object.Destroy(verificationInputSettings);
            }
        }

        static Vector2 OpenLocomotionLane(Vector2 direction)
        {
            var origin = host.PlayerWorldPosition;
            var candidates = new List<Vector2> { origin, new Vector2(4096, 2100), new Vector2(4260, 3720) };
            for (int y = -800; y <= 800; y += 100)
            for (int x = -800; x <= 800; x += 100) candidates.Add(origin + new Vector2(x, y));
            foreach (var p in candidates)
            {
                if (!host.TerrainStandable(p.x, p.y)) continue;
                if ((bool)Invoke(host, "CanTraverseWorldPlayer", p, p + direction * 620f)) return p;
            }
            throw new InvalidOperationException("No open locomotion lane for " + direction);
        }

        static void CaptureLocomotionCrop(string name, PuppetActor actor)
        {
            var image = (RawImage)Field(actor, "frameImage");
            var rect = DossierScreenRect(image.rectTransform);
            int x = Mathf.Clamp(Mathf.FloorToInt(rect.xMin) - 6, 0, Screen.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(rect.yMin) - 6, 0, Screen.height - 1);
            int width = Mathf.Min(Mathf.CeilToInt(rect.width) + 12, Screen.width - x);
            int height = Mathf.Min(Mathf.CeilToInt(rect.height) + 12, Screen.height - y);
            var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            var crop = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                crop.SetPixels(screenshot.GetPixels(x, y, width, height)); crop.Apply();
                File.WriteAllBytes(Path.Combine(state.output, name + ".png"), crop.EncodeToPNG());
            }
            finally { UnityEngine.Object.Destroy(screenshot); UnityEngine.Object.Destroy(crop); }
        }
    }
}
