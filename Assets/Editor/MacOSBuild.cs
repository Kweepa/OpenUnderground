using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Builds a macOS Universal player (Apple silicon arm64 and Intel x86_64) with the Mono backend
/// the project already uses. Batch: Unity -batchmode -quit -projectPath . -executeMethod MacOSBuild.BuildUniversal
/// </summary>
public static class MacOSBuild
{
    private const string AppIconPath = "Assets/Game/UI/AppIcon.png";

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
        ApplyStandaloneAppIcon();

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
            EmbedMacOsIcns(location);
            Debug.Log("macOS Universal build: " + location);
        }

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }

    /// <summary>
    /// Standalone expects every icon size from <see cref="PlayerSettings.GetIconSizes"/> (eight on
    /// current Unity). Passing a single texture is rejected and the Dock keeps the default icon.
    /// </summary>
    private static void ApplyStandaloneAppIcon()
    {
        if (!File.Exists(AppIconPath))
        {
            Debug.LogWarning("macOS build: " + AppIconPath + " not found; using existing PlayerSettings icons.");
            return;
        }

        TextureImporter importer = AssetImporter.GetAtPath(AppIconPath) as TextureImporter;
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconPath);
        if (source == null)
        {
            Debug.LogWarning("macOS build: failed to load " + AppIconPath);
            return;
        }

        int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Application);
        if (sizes == null || sizes.Length == 0)
        {
            Debug.LogWarning("macOS build: Standalone reported no application icon sizes.");
            return;
        }

        Texture2D[] icons = new Texture2D[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            icons[i] = ResizeNearest(source, sizes[i]);
        }

        PlayerSettings.SetIcons(NamedBuildTarget.Standalone, icons, IconKind.Application);
        Debug.Log("macOS build: assigned " + icons.Length + " Standalone application icons from " + AppIconPath);
    }

    private static Texture2D ResizeNearest(Texture2D source, int size)
    {
        Texture2D dst = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "AppIcon_" + size
        };

        Color32[] src = source.GetPixels32();
        int sw = source.width;
        int sh = source.height;
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            int sy = Mathf.Clamp(y * sh / size, 0, sh - 1);
            for (int x = 0; x < size; x++)
            {
                int sx = Mathf.Clamp(x * sw / size, 0, sw - 1);
                pixels[y * size + x] = src[sy * sw + sx];
            }
        }

        dst.SetPixels32(pixels);
        dst.Apply(false, false);
        return dst;
    }

    /// <summary>
    /// Replace Unity's PlayerIcon.icns with a full iconutil set so Finder/Dock pick it up reliably.
    /// </summary>
    private static void EmbedMacOsIcns(string appPath)
    {
        string pngPath = Path.GetFullPath(AppIconPath);
        if (!File.Exists(pngPath) || !Directory.Exists(appPath))
        {
            return;
        }

        string tempRoot = Path.GetFullPath(Path.Combine("Temp", "MacAppIcon"));
        string iconset = Path.Combine(tempRoot, "AppIcon.iconset");
        string icnsPath = Path.Combine(tempRoot, "PlayerIcon.icns");
        try
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
            Directory.CreateDirectory(iconset);

            // Standard macOS iconset names. iconutil accepts these PNG sizes.
            WriteIconsetPng(pngPath, Path.Combine(iconset, "icon_16x16.png"), 16);
            WriteIconsetPng(pngPath, Path.Combine(iconset, "icon_16x16@2x.png"), 32);
            WriteIconsetPng(pngPath, Path.Combine(iconset, "icon_32x32.png"), 32);
            WriteIconsetPng(pngPath, Path.Combine(iconset, "icon_32x32@2x.png"), 64);
            WriteIconsetPng(pngPath, Path.Combine(iconset, "icon_128x128.png"), 128);
            WriteIconsetPng(pngPath, Path.Combine(iconset, "icon_128x128@2x.png"), 256);
            WriteIconsetPng(pngPath, Path.Combine(iconset, "icon_256x256.png"), 256);
            WriteIconsetPng(pngPath, Path.Combine(iconset, "icon_256x256@2x.png"), 512);
            WriteIconsetPng(pngPath, Path.Combine(iconset, "icon_512x512.png"), 512);
            WriteIconsetPng(pngPath, Path.Combine(iconset, "icon_512x512@2x.png"), 1024);

            if (!RunTool("/usr/bin/iconutil", "-c", "icns", iconset, "-o", icnsPath))
            {
                Debug.LogWarning("macOS build: iconutil failed; leaving Unity PlayerIcon.icns as built.");
                return;
            }

            string dest = Path.Combine(appPath, "Contents", "Resources", "PlayerIcon.icns");
            File.Copy(icnsPath, dest, true);
            // Unity CLI may drop provenance next to Contents; that breaks the bundle seal.
            string provenance = Path.Combine(appPath, "unity-build.provenance.json");
            if (File.Exists(provenance))
            {
                File.Delete(provenance);
            }
            // Replacing a sealed resource invalidates the signature; ad-hoc resign so
            // right-click / Option-Open Gatekeeper bypass works on other Macs.
            RunTool("/usr/bin/codesign", "--force", "--deep", "--sign", "-", appPath);
            RunTool("/usr/bin/touch", appPath);
            Debug.Log("macOS build: embedded PlayerIcon.icns from " + AppIconPath);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("macOS build: failed to embed icns: " + ex.Message);
        }
    }

    private static void WriteIconsetPng(string sourcePng, string destPng, int size)
    {
        string sizeArg = size.ToString();
        RunTool("/usr/bin/sips", "-z", sizeArg, sizeArg, sourcePng, "--out", destPng);
    }

    private static bool RunTool(string fileName, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using (Process process = Process.Start(psi))
        {
            if (process == null)
            {
                return false;
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                Debug.LogWarning(fileName + " exited " + process.ExitCode + ": " + process.StandardError.ReadToEnd());
                return false;
            }
            return true;
        }
    }
}
