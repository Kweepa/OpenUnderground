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

namespace Kweepa.LeanHull
{
    [System.Serializable]
    internal class LeanHullProjectSettingsData
    {
        public const string DefaultGeneratedAssetsFolder = "Assets/Game/LeanHull";

        // Assets-relative folder where LeanHull stores generated collider meshes.
        public string generatedAssetsFolder = DefaultGeneratedAssetsFolder;
    }

    internal static class LeanHullProjectSettings
    {
        private const string SettingsFilePath = "ProjectSettings/LeanHullSettings.json";
        private static LeanHullProjectSettingsData _cachedSettings;

        internal static LeanHullProjectSettingsData GetOrCreateSettings()
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            if (!File.Exists(SettingsFilePath))
            {
                _cachedSettings = new LeanHullProjectSettingsData();
                return _cachedSettings;
            }

            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                _cachedSettings = JsonUtility.FromJson<LeanHullProjectSettingsData>(json);
            }
            catch
            {
                _cachedSettings = null;
            }

            if (_cachedSettings == null)
                _cachedSettings = new LeanHullProjectSettingsData();

            if (string.IsNullOrEmpty(_cachedSettings.generatedAssetsFolder))
                _cachedSettings.generatedAssetsFolder = LeanHullProjectSettingsData.DefaultGeneratedAssetsFolder;

            _cachedSettings.generatedAssetsFolder = NormalizeFolder(_cachedSettings.generatedAssetsFolder);
            return _cachedSettings;
        }

        internal static string GeneratedAssetsFolder
        {
            get
            {
                return GetOrCreateSettings().generatedAssetsFolder;
            }
            set
            {
                var settings = GetOrCreateSettings();
                string normalized = NormalizeFolder(value);
                if (settings.generatedAssetsFolder == normalized)
                    return;

                settings.generatedAssetsFolder = normalized;
                Save();
            }
        }

        internal static void Save()
        {
            if (_cachedSettings == null)
                _cachedSettings = new LeanHullProjectSettingsData();

            string json = JsonUtility.ToJson(_cachedSettings, true);

            string directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsFilePath, json);
        }

        private static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return LeanHullProjectSettingsData.DefaultGeneratedAssetsFolder;

            folder = folder.Replace('\\', '/').Trim();

            // Require Assets-relative paths. If invalid, fall back to default.
            if (!folder.StartsWith("Assets/") && !string.Equals(folder, "Assets", System.StringComparison.Ordinal))
                return LeanHullProjectSettingsData.DefaultGeneratedAssetsFolder;

            return folder;
        }

        [SettingsProvider]
        public static SettingsProvider CreateLeanHullSettingsProvider()
        {
            var provider = new SettingsProvider("Project/LeanHull", SettingsScope.Project)
            {
                label = "LeanHull",
                guiHandler = searchContext =>
                {
                    var settings = GetOrCreateSettings();

                    EditorGUILayout.LabelField("LeanHull", EditorStyles.boldLabel);
                    EditorGUILayout.Space();

                    EditorGUILayout.LabelField("Generated Assets", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;

                    EditorGUI.BeginChangeCheck();
                    string folder = EditorGUILayout.TextField(
                        new GUIContent("Folder", "Assets-relative folder where LeanHull stores generated collider meshes."),
                        settings.generatedAssetsFolder);
                    if (EditorGUI.EndChangeCheck())
                    {
                        settings.generatedAssetsFolder = NormalizeFolder(folder);
                        Save();
                    }

                    if (GUILayout.Button(new GUIContent("Reset To Default", "Reset generated assets folder to Assets/Game/LeanHull")))
                    {
                        settings.generatedAssetsFolder = LeanHullProjectSettingsData.DefaultGeneratedAssetsFolder;
                        Save();
                    }

                    EditorGUI.indentLevel--;
                }
            };

            return provider;
        }
    }
}

