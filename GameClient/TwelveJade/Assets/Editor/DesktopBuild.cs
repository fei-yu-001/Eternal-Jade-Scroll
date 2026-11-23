using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TwelveJade.Editor
{
    public static class DesktopBuild
    {
        public static void Run()
        {
            string directory = Environment.GetEnvironmentVariable("TWELVEJADE_DESKTOP_OUTPUT");
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory))
                throw new InvalidOperationException("Set TWELVEJADE_DESKTOP_OUTPUT to the Windows build directory.");
            Directory.CreateDirectory(directory);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Scenes/FrontEnd.unity" },
                locationPathName = Path.Combine(directory, "TwelveJade.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            string stamp = File.ReadAllText(Path.Combine(Application.dataPath, "Resources/Config/build-stamp.txt")).Trim();
            File.WriteAllText(Path.Combine(directory, "build-verification.txt"),
                "Stamp=" + stamp + "\nUnity=" + Application.unityVersion + "\nResult=" + report.summary.result +
                "\nErrors=" + report.summary.totalErrors + "\nWarnings=" + report.summary.totalWarnings +
                "\nSize=" + report.summary.totalSize + "\n");
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Desktop build failed: " + report.summary.result);
            Debug.Log("Current desktop build ready: " + stamp);
        }
    }
}
