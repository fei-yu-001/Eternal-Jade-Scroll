using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    // Editor-only runtime-component checks. Uses the shipped textures, no player's saves or live backend.
    public static class NpcMotionShot
    {
        static readonly MethodInfo Render = typeof(NpcFrameAnimator).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        static int passed;
        static readonly string[] Ids = { "huolang", "npc-shenhuai-an", "npc-shitou", "npc-zhao-da", "npc-yao-lao", "npc-liu-sao",
            "npc-a-xing", "npc-he-er-niang", "npc-hu-san", "npc-a-he", "npc-xiao-man" };

        public static void Run()
        {
            string output = Environment.GetEnvironmentVariable("JADE_DESKTOP_EVIDENCE");
            if (string.IsNullOrEmpty(output) || !Path.IsPathRooted(output)) throw new InvalidOperationException("Set absolute JADE_DESKTOP_EVIDENCE.");
            if (!Path.GetFullPath(Application.dataPath).Contains(Path.DirectorySeparatorChar + ".batch" + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Run in an isolated .batch Unity project.");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            RenderTexture target = null;
            try
            {
                var camera = new GameObject("NPC capture camera", typeof(Camera)).GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.18f, .24f, .23f);
                camera.orthographic = true; camera.orthographicSize = 550; camera.allowHDR = false; camera.allowMSAA = false;
                target = new RenderTexture(1100, 1100, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                var canvas = new GameObject("NPC motion checks", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
                Canvas.ForceUpdateCanvases();
                var roots = new List<GameObject>();
                for (int n = 0; n < Ids.Length; n++)
                {
                    var art = TownCharacterArt.Find(Ids[n]);
                    Check(art != null && art.cycleDistance > 0 && art.cycleDistance / art.frames / 26f <= .3f, Ids[n] + " short distance-driven steps");
                    var rect = Root(canvas.transform, 110, 190, 170);
                    var actor = rect.gameObject.AddComponent<NpcFrameAnimator>(); actor.Configure(art, () => false);
                    Check(actor.HasSheetForCheck && actor.FrameCountForCheck == 4, Ids[n] + " real four-frame resource loaded");
                    actor.SetMovement(new Vector2(art.cycleDistance * .30f, 0)); Render.Invoke(actor, null);
                    Check(actor.FrameForCheck == 1, Ids[n] + " world distance advances the actual rendered frame");
                    float distance = actor.DistanceForCheck;
                    rect.anchoredPosition += new Vector2(120, 40); rect.localScale = Vector3.one * .8f;
                    Render.Invoke(actor, null);
                    Check(actor.FrameForCheck == -1 && actor.DistanceForCheck == distance, Ids[n] + " view reposition/scale does not manufacture footsteps");
                    actor.SetMovement(new Vector2(0, art.cycleDistance * .25f)); Render.Invoke(actor, null);
                    Check(actor.FrameForCheck == 2, Ids[n] + " vertical movement keeps the same gait distance");
                    actor.SetMovement(new Vector2(.1f, 0)); actor.SetMovement(Vector2.zero); Render.Invoke(actor, null);
                    Check(actor.FrameForCheck == -1, Ids[n] + " blocked or dialogue movement returns to stand");
                    actor.SetMovement(new Vector2(art.cycleDistance * .25f, 0)); Render.Invoke(actor, null);
                    actor.gameObject.SetActive(false); actor.gameObject.SetActive(true); Render.Invoke(actor, null);
                    Check(actor.FrameForCheck == -1, Ids[n] + " page disable cannot leave a held walk frame");
                    UnityEngine.Object.DestroyImmediate(rect.gameObject);
                    for (int column = 0; column < 5; column++)
                    {
                        var view = Root(canvas.transform, 110 + column * 220, 185 + (n % 6) * 182, 170);
                        roots.Add(view.gameObject);
                        var visible = view.gameObject.AddComponent<NpcFrameAnimator>(); visible.Configure(art, () => true);
                        if (column > 0) visible.SetMovement(new Vector2(art.cycleDistance * (column - .9f) / art.frames, 0));
                        Render.Invoke(visible, null);
                        Check(column == 0 ? visible.FrameForCheck == -1 : visible.FrameForCheck == column - 1,
                            Ids[n] + " reduced-motion keeps real walking, column=" + column);
                    }
                    if (n == 5 || n == Ids.Length - 1)
                    {
                        Canvas.ForceUpdateCanvases(); camera.Render();
                        var previous = RenderTexture.active; RenderTexture.active = target;
                        var png = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                        png.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); png.Apply(); RenderTexture.active = previous;
                        File.WriteAllBytes(Path.Combine(output, "npc-motion-" + (n == 5 ? "a" : "b") + ".png"), png.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(png);
                        foreach (var go in roots) UnityEngine.Object.DestroyImmediate(go);
                        roots.Clear();
                    }
                }
                Debug.Log("NPC_MOTION_CHECKS_PASS " + passed + " assertions; screenshots are static pose evidence, not gameplay acceptance.");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
            finally { if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } }
        }

        static RectTransform Root(Transform parent, float x, float footY, float height)
        {
            var rect = new GameObject("NPC under test", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(.5f, 0); rect.sizeDelta = new Vector2(height * .75f, height);
            rect.anchoredPosition = new Vector2(x, -footY); return rect;
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("NPC motion check failed: " + label);
            passed++; Debug.Log("NPC_MOTION_PASS " + label);
        }
    }
}
