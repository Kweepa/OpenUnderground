// ==============================================================================
//  LeanHull - Size optimized Convex Hull Generator
//  Copyright (c) 2026 Kweepa
//  All rights reserved.
//
//  This script is part of the LeanHull tool suite.
//  Do not distribute or share this code without explicit permission.
// ==============================================================================

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace Kweepa.LeanHull
{
    // Prefab sidecar JSON (exclude list) read/write and cleanup.
    public static class LeanHullPrefabSettings
    {
        private const string prefabSettingsPath = "Assets/Game/LeanHull/PrefabSettings";

        public static string ShortHashOfGuid(string guid)
        {
            uint hash = 0;
            foreach (char c in guid ?? "")
            {
                hash = hash * 31 + c;
            }
            return hash.ToString("X8");
        }

        // Replaces invalid filename characters in any string. Use for prefab name, GO name, etc.
        public static string SanitizeNameForFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static string SanitizePrefabNameForFileName(string prefabPath)
        {
            string name = SanitizeNameForFileName(Path.GetFileNameWithoutExtension(prefabPath ?? ""));
            return string.IsNullOrEmpty(name) ? "Prefab" : name;
        }

        private static string GetFullDir()
        {
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), prefabSettingsPath);
        }

        // Sidecar filename: {prefabname}_{shortHash(guid)}.json
        private static string GetSidecarFileName(string prefabPath, string prefabGuid)
        {
            string sanitizedName = SanitizePrefabNameForFileName(prefabPath);
            string shortHash = ShortHashOfGuid(prefabGuid);
            return $"{sanitizedName}_{shortHash}.json";
        }

        private static string GetSidecarPath(string prefabPath, string prefabGuid)
        {
            return Path.Combine(GetFullDir(), GetSidecarFileName(prefabPath, prefabGuid));
        }

        // Returns the set of persistent IDs (GameObject or MeshFilter fileIDs) that should be excluded from hull generation for this prefab.
        public static HashSet<string> GetExcludeIdsForPrefab(string prefabPath)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) return set;
            string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(prefabGuid)) return set;
            string sidecarPath = GetSidecarPath(prefabPath, prefabGuid);
            if (!File.Exists(sidecarPath)) return set;
            try
            {
                string json = File.ReadAllText(sidecarPath);
                var data = JsonUtility.FromJson<LeanHullPrefabSettingsData>(json);
                if (data?.excludeIds != null)
                    foreach (string id in data.excludeIds)
                        if (!string.IsNullOrEmpty(id)) set.Add(id);
            }
            catch { /* ignore */ }
            return set;
        }

        public static void SetExcludeIdsForPrefab(string prefabPath, HashSet<string> excludeIds)
        {
            if (string.IsNullOrEmpty(prefabPath)) return;
            string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(prefabGuid)) return;
            string fullDir = GetFullDir();
            string newSidecarPath = GetSidecarPath(prefabPath, prefabGuid);
            if (excludeIds == null || excludeIds.Count == 0)
            {
                RemoveAnySidecarForPrefabGuid(prefabGuid);
                AssetDatabase.Refresh();
                return;
            }
            Directory.CreateDirectory(fullDir);
            RemoveAnySidecarForPrefabGuid(prefabGuid);
            var data = new LeanHullPrefabSettingsData
            {
                prefabGuid = prefabGuid,
                excludeIds = new List<string>(excludeIds)
            };
            File.WriteAllText(newSidecarPath, JsonUtility.ToJson(data, true));
            AssetDatabase.Refresh();
        }

        // Deletes any sidecar file whose JSON contains this prefabGuid (handles old guid.json names and renames).
        private static void RemoveAnySidecarForPrefabGuid(string prefabGuid)
        {
            string fullDir = GetFullDir();
            if (!Directory.Exists(fullDir)) return;
            foreach (string fullPath in Directory.GetFiles(fullDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(fullPath);
                    var data = JsonUtility.FromJson<LeanHullPrefabSettingsData>(json);
                    if (data != null && data.prefabGuid == prefabGuid)
                        DeleteSidecarFile(fullPath);
                }
                catch { /* ignore */ }
            }
        }

        // Deletes sidecar JSON files in PrefabSettings whose prefabGuid no longer points to an existing prefab. Returns number deleted.
        public static int CleanupOrphanedPrefabSettings()
        {
            string fullDir = GetFullDir();
            if (!Directory.Exists(fullDir)) return 0;
            string[] files = Directory.GetFiles(fullDir, "*.json");
            int deleteCount = 0;
            foreach (string fullPath in files)
            {
                try
                {
                    string json = File.ReadAllText(fullPath);
                    var data = JsonUtility.FromJson<LeanHullPrefabSettingsData>(json);
                    if (data == null || string.IsNullOrEmpty(data.prefabGuid)) { DeleteSidecarFile(fullPath); deleteCount++; continue; }
                    string assetPath = AssetDatabase.GUIDToAssetPath(data.prefabGuid);
                    if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteSidecarFile(fullPath);
                        deleteCount++;
                        continue;
                    }
                    string projectRoot = Path.GetDirectoryName(Application.dataPath);
                    if (!File.Exists(Path.Combine(projectRoot, assetPath)))
                    {
                        DeleteSidecarFile(fullPath);
                        deleteCount++;
                    }
                }
                catch
                {
                    try { DeleteSidecarFile(fullPath); deleteCount++; } catch { }
                }
            }
            return deleteCount;
        }

        private static void DeleteSidecarFile(string fullPath)
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
            string metaPath = fullPath + ".meta";
            if (File.Exists(metaPath)) File.Delete(metaPath);
        }
    }
}
