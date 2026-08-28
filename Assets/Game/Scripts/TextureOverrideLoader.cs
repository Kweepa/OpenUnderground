using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class TextureOverrideLoader
{
    private const int FloorCount = 52;
    private const int WallCount = 210;
    private const int DoorCount = 13;

    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

    public static void Apply(List<List<Texture2D>> wallTex, List<List<Texture2D>> floorTex, Texture2D[] doorTex)
    {
        string folder = Path.Combine(Application.streamingAssetsPath, "Textures");
        if (!Directory.Exists(folder))
        {
            return;
        }

        ApplyCyclingOverrides(folder, "floor", 2, FloorCount, floorTex, TextureWrapMode.Repeat);
        ApplyCyclingOverrides(folder, "wall", 3, WallCount, wallTex, TextureWrapMode.Repeat);
        ApplyDoorOverrides(folder, doorTex);
    }

    private static void ApplyCyclingOverrides(
        string folder,
        string prefix,
        int digitCount,
        int expectedCount,
        List<List<Texture2D>> texLists,
        TextureWrapMode wrapMode)
    {
        if (texLists == null)
        {
            return;
        }

        int count = Mathf.Min(expectedCount, texLists.Count);
        for (int i = 0; i < count; ++i)
        {
            string baseName = prefix + i.ToString($"D{digitCount}");
            List<Texture2D> frames = TryLoadLetteredFrames(folder, baseName, wrapMode);
            if (frames == null)
            {
                Texture2D staticTex = TryLoadImage(ResolveOverridePath(folder, baseName), wrapMode);
                if (staticTex == null)
                {
                    continue;
                }

                frames = new List<Texture2D> { staticTex };
            }

            texLists[i] = frames;
        }

        for (int i = count; i < expectedCount; ++i)
        {
            string baseName = prefix + i.ToString($"D{digitCount}");
            if (ResolveOverridePath(folder, baseName + "a") != null ||
                ResolveOverridePath(folder, baseName) != null)
            {
                Debug.LogWarning($"Texture override {baseName} ignored: index {i} is outside loaded archive length ({texLists.Count}).");
            }
        }
    }

    private static void ApplyDoorOverrides(string folder, Texture2D[] doorTex)
    {
        if (doorTex == null)
        {
            return;
        }

        int count = Mathf.Min(DoorCount, doorTex.Length);
        for (int i = 0; i < count; ++i)
        {
            string baseName = "door" + i.ToString("D2");
            Texture2D tex = TryLoadImage(ResolveOverridePath(folder, baseName), TextureWrapMode.Clamp);
            if (tex != null)
            {
                doorTex[i] = tex;
            }
        }

        for (int i = count; i < DoorCount; ++i)
        {
            string baseName = "door" + i.ToString("D2");
            if (ResolveOverridePath(folder, baseName) != null)
            {
                Debug.LogWarning($"Texture override door{i:D2} ignored: index {i} is outside loaded archive length ({doorTex.Length}).");
            }
        }
    }

    private static List<Texture2D> TryLoadLetteredFrames(string folder, string baseName, TextureWrapMode wrapMode)
    {
        string firstPath = ResolveOverridePath(folder, baseName + "a");
        if (firstPath == null)
        {
            return null;
        }

        string extension = Path.GetExtension(firstPath);
        List<Texture2D> frames = new List<Texture2D>();
        for (char letter = 'a'; letter <= 'z'; ++letter)
        {
            string path = Path.Combine(folder, baseName + letter + extension);
            if (!File.Exists(path))
            {
                break;
            }

            Texture2D tex = TryLoadImage(path, wrapMode);
            if (tex == null)
            {
                break;
            }

            frames.Add(tex);
        }

        return frames.Count > 0 ? frames : null;
    }

    private static string ResolveOverridePath(string folder, string baseName)
    {
        foreach (string ext in Extensions)
        {
            string path = Path.Combine(folder, baseName + ext);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static Texture2D TryLoadImage(string path, TextureWrapMode wrapMode)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        byte[] bytes = File.ReadAllBytes(path);
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
        if (!tex.LoadImage(bytes))
        {
            Object.Destroy(tex);
            Debug.LogWarning($"Failed to load texture override: {path}");
            return null;
        }

        tex.filterMode = FilterMode.Point;
        tex.wrapMode = wrapMode;
        tex.Apply(true, true);
        return tex;
    }
}
