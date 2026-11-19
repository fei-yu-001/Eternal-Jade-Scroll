using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

// Copied only into the disposable Tuanjie project by prepare-project.py.
public static class HarmonyClientBuild
{
    public static void Export()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!File.Exists(Path.Combine(project, ".harmony-source.json")))
            throw new InvalidOperationException("Build only the prepared, isolated Harmony project.");
        string tools = Required("HARMONY_TOOLS_ROOT");
        string output = Required("HARMONY_EXPORT_ROOT");
        string bundle = Required("HARMONY_BUNDLE_ID");
        int versionCode = int.Parse(Required("HARMONY_VERSION_CODE"));
        EditorPrefs.SetBool("OpenHarmony SdkUseEmbedded", false);
        EditorPrefs.SetBool("OpenHarmony JdkUseEmbedded", false);
        EditorPrefs.SetBool("OpenHarmony NodejsUseEmbedded", false);
        EditorPrefs.SetString("OpenHarmonySdkRoot", Path.Combine(tools, "DevEcoStudio/sdk/default/openharmony"));
        EditorPrefs.SetString("OpenHarmony Jdk17Path", Path.Combine(tools, "DevEcoStudio/jbr"));
        EditorPrefs.SetString("OpenHarmony NodejsPath", Path.Combine(tools, "DevEcoStudio/tools/node"));
        var target = NamedBuildTarget.OpenHarmony;
        PlayerSettings.companyName = "fei-yu-001";
        PlayerSettings.productName = "十二玉楼长生经";
        PlayerSettings.bundleVersion = "0.0.1";
        PlayerSettings.SetApplicationIdentifier(target, bundle);
        PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Low);
        PlayerSettings.OpenHarmony.targetArchitectures = OpenHarmonyArchitecture.ARM64;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.OpenHarmony, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.OpenHarmony, new[] { GraphicsDeviceType.OpenGLES3 });
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Branding/eternal-jade-scroll-app-icon-v1.png");
        if (icon == null) throw new InvalidOperationException("Missing game icon.");
        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
        ConfigureMobileTextures();
        string scene = "Assets/Scenes/FrontEnd.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
            throw new InvalidOperationException("Missing actual game entry scene.");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scene, true) };
        EditorUserBuildSettings.exportAsOpenHarmonyProject = true;
        AssetDatabase.SaveAssets();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { scene }, target = BuildTarget.OpenHarmony, locationPathName = output,
            options = BuildOptions.Development | BuildOptions.CompressWithLz4
        });
        string status = "result=" + report.summary.result + "\nerrors=" + report.summary.totalErrors +
            "\nwarnings=" + report.summary.totalWarnings + "\nbytes=" + report.summary.totalSize;
        File.WriteAllText(Path.Combine(project, "harmony-build-result.txt"), status);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Game export failed: " + status);
        string appPath = Path.Combine(output, "AppScope/app.json5");
        File.WriteAllText(appPath, Regex.Replace(File.ReadAllText(appPath), "\"versionCode\"\\s*:\\s*\\d+", "\"versionCode\": " + versionCode));
        string profilePath = Path.Combine(output, "build-profile.json5");
        string profile = File.ReadAllText(profilePath);
        profile = Regex.Replace(profile, "\"compatibleSdkVersion\"\\s*:\\s*(?:\"[^\"]*\"|\\d+)", "\"compatibleSdkVersion\": \"6.1.1(24)\"");
        if (!profile.Contains("\"targetSdkVersion\""))
            profile = profile.Replace("\"compatibleSdkVersion\"", "\"targetSdkVersion\": \"26.0.1\",\n        \"compatibleSdkVersion\"");
        File.WriteAllText(profilePath, profile);
        string nativeHttp = Path.GetFullPath(Path.Combine(project, "../../Tools/Harmony/JadeHttp.ets"));
        File.Copy(nativeHttp, Path.Combine(output, "tuanjieLib/src/main/ets/JadeHttp.ets"), true);
        string registerPath = Path.Combine(output, "tuanjieLib/src/main/ets/gen/TuanjieJSScriptRegister.ets");
        string register = File.ReadAllText(registerPath);
        if (!register.Contains("import { JadeHttp }"))
        {
            register = "import { JadeHttp } from '../JadeHttp';\n" + register;
            register = register.Replace("  SetToGlobalThis('tuanjieJSScript', tuanjieJSClasses);",
                "  tuanjieJSClasses['JadeHttp'] = JadeHttp;\n  SetToGlobalThis('tuanjieJSScript', tuanjieJSClasses);");
        }
        File.WriteAllText(registerPath, register);
        Debug.Log("ETERNAL_JADE_GAME_EXPORT_SUCCESS " + status);
    }

    static void ConfigureMobileTextures()
    {
        int changed = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Art" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                var settings = importer.GetPlatformTextureSettings("OpenHarmony");
                if (settings.overridden && settings.format == TextureImporterFormat.ASTC_6x6 && settings.maxTextureSize == 2048) continue;
                settings.name = "OpenHarmony";
                settings.overridden = true;
                settings.maxTextureSize = 2048;
                settings.format = TextureImporterFormat.ASTC_6x6;
                settings.textureCompression = TextureImporterCompression.Compressed;
                settings.compressionQuality = 50;
                importer.SetPlatformTextureSettings(settings);
                AssetDatabase.WriteImportSettingsIfDirty(path);
                AssetDatabase.ImportAsset(path);
                changed++;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        Debug.Log("ETERNAL_JADE_HARMONY_TEXTURE_OVERRIDES " + changed);
    }

    static string Required(string name)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Missing build environment: " + name);
        return value;
    }
}
