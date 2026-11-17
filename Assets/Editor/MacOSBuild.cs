using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds a macOS Universal player (Apple silicon arm64 and Intel x86_64) with the Mono backend
/// the project already uses. Batch: Unity -batchmode -quit -projectPath . -executeMethod MacOSBuild.BuildUniversal
/// </summary>
public static class MacOSBuild
{
    public static void BuildUniversal()
    {
        string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("macOS build: EditorBuildSettings has no enabled scenes.");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
            return;
        }

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, (int)OSArchitecture.x64ARM64);

        Texture2D appIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/UI/AppIcon.png");
        if (appIcon != null)
        {
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, new[] { appIcon }, IconKind.Application);
        }
        else
        {
            Debug.LogWarning("macOS build: Assets/Game/UI/AppIcon.png not found; using existing PlayerSettings icons.");
        }

        string location = Path.GetFullPath(Path.Combine("Builds", "macOS", "UnityUnderground.app"));
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-buildOutput" && !string.IsNullOrEmpty(args[i + 1]))
            {
                location = Path.GetFullPath(args[i + 1]);
                if (!location.EndsWith(".app", System.StringComparison.OrdinalIgnoreCase))
                {
                    location = Path.Combine(location, "UnityUnderground.app");
                }
                break;
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(location));

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = location,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.CompressWithLz4HC
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        bool ok = report.summary.result == BuildResult.Succeeded;
        if (!ok)
        {
            Debug.LogError("macOS build failed: " + report.summary.result + " errors=" + report.summary.totalErrors);
        }
        else
        {
            Debug.Log("macOS Universal build: " + location);
        }

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
