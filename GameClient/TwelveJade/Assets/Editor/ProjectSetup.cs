using System.IO;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TwelveJade.Editor
{
    public static class ProjectSetup
    {
        [MenuItem("Twelve Jade/Prepare project")]
        public static void Prepare()
        {
            var project = Directory.GetParent(Application.dataPath).FullName;
            var uiPackage = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            if (uiPackage != null && !File.Exists(Path.Combine(Application.dataPath, "TextMesh Pro", "Resources", "TMP Settings.asset")))
            {
                var essentials = Path.Combine(uiPackage.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
                if (File.Exists(essentials)) AssetDatabase.ImportPackage(essentials, false);
            }
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Resources", "Characters"));
            PlayerSettings.companyName = "fei-yu-001";
            PlayerSettings.productName = "十二玉楼长生经";
            PlayerSettings.bundleVersion = "0.0.1";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.runInBackground = true;
            QualitySettings.vSyncCount = 1;

            CreatePreset("farmer", "农家子弟", "从田垄与灶火间长大，识得四时，也识得人情。",
                "布衣沾着草木气息。此造型不赋予能力加成。");
            CreatePreset("traveller", "江湖行旅", "背着一只旧包袱，从古道走到村口。来路未必能说清。",
                "一双远行旧鞋。此造型不预设人物命运。");
            CreatePreset("merchant", "市井商家", "听惯街市的吆喝，习惯在喧闹里寻找自己的路。",
                "随身的布袋装着家常小物。此造型不赋予钱财优势。");
            BindDefaultTurnarounds();

            const string scenePath = "Assets/Scenes/FrontEnd.unity";
            if (!File.Exists(Path.Combine(project, scenePath)))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("Front End").AddComponent<FrontEndController>();
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UnityEngine.Debug.Log("Twelve Jade front end prepared: " + scenePath);
        }

        static void CreatePreset(string id, string name, string description, string detail)
        {
            var path = "Assets/Resources/Characters/" + id + ".asset";
            if (AssetDatabase.LoadAssetAtPath<CharacterPreset>(path) != null) return;
            var preset = ScriptableObject.CreateInstance<CharacterPreset>();
            preset.id = id; preset.displayName = name; preset.description = description; preset.detail = detail;
            preset.accent = id == "farmer" ? UiKit.Jade : id == "traveller" ? UiKit.Gold : UiKit.Paper;
            AssetDatabase.CreateAsset(preset, path);
        }

        // 画稿引用断掉时（例如文件按新命名规则归位后）自动补绑到默认男装第一款。
        static void BindDefaultTurnarounds()
        {
            var rebound = false;
            foreach (var guid in AssetDatabase.FindAssets("t:CharacterPreset", new[] { "Assets/Resources/Characters" }))
            {
                var preset = AssetDatabase.LoadAssetAtPath<CharacterPreset>(AssetDatabase.GUIDToAssetPath(guid));
                if (preset == null) continue;
                foreach (var view in new[] { "front", "side", "back" })
                {
                    var field = typeof(CharacterPreset).GetField(view);
                    if (field.GetValue(preset) != null) continue;
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        $"Assets/Resources/Art/{preset.id}-male-0-{view}.png");
                    if (texture == null) continue;
                    field.SetValue(preset, texture);
                    EditorUtility.SetDirty(preset);
                    rebound = true;
                }
            }
            if (rebound) Debug.Log("Twelve Jade: rebound missing character turnaround references.");
        }
    }
}
