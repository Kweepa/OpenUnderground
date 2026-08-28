using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// An Editor tool to find 3D models in a specified folder that are not marked as Read/Write enabled.
/// It provides an option to automatically fix them or to list them for manual, one-by-one fixing.
/// </summary>
public class ModelReadWriteScanner : EditorWindow
{
    // A simple data structure to hold information about a model asset.
    private class ModelInfo
    {
        public string Path { get; set; }
        public ModelImporter Importer { get; set; }
        public GameObject AssetObject { get; set; }
    }

    private string scanPath = "Assets/";
    private bool autoFixEnabled = false;
    private Vector2 scrollPosition;
    
    // A list to hold models found during the scan (for manual fixing mode).
    private readonly List<ModelInfo> nonReadableModels = new List<ModelInfo>();
    
    // A string to log actions when in auto-fix mode.
    private string reportLog = "Scan results will be shown here.";

    /// <summary>
    /// Creates a menu item in the Unity Editor to open this tool window.
    /// </summary>
    [MenuItem("Tools/Model Read-Write Scanner")]
    public static void ShowWindow()
    {
        // Get existing open window or if none, make a new one.
        GetWindow<ModelReadWriteScanner>("Model Read/Write Scanner");
    }

    /// <summary>
    /// Renders the GUI for the editor window.
    /// </summary>
    private void OnGUI()
    {
        GUILayout.Label("Scan Models for Read/Write Access", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("This tool finds models that don't have 'Read/Write Enabled' checked in their import settings. This is often required for procedural mesh manipulation at runtime.", MessageType.Info);
        EditorGUILayout.Space();

        // --- FOLDER SELECTION ---
        EditorGUILayout.LabelField("1. Select a Folder to Scan", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        scanPath = EditorGUILayout.TextField("Folder Path", scanPath);
        if (GUILayout.Button("Select Folder", GUILayout.Width(100)))
        {
            SelectScanFolder();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();

        // --- OPTIONS ---
        EditorGUILayout.LabelField("2. Choose Scan Mode", EditorStyles.boldLabel);
        autoFixEnabled = EditorGUILayout.ToggleLeft("Automatically Fix Found Models", autoFixEnabled);
        EditorGUILayout.Space();
        
        // --- ACTION BUTTON ---
        EditorGUILayout.LabelField("3. Run Scan", EditorStyles.boldLabel);
        if (GUILayout.Button("Scan For Non Read/Write Models", GUILayout.Height(40)))
        {
            ScanModels();
        }
        EditorGUILayout.Space();

        // --- RESULTS AREA ---
        EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, EditorStyles.helpBox);

        if (autoFixEnabled)
        {
            // Display a log of actions for automatic mode
            EditorGUILayout.TextArea(reportLog, GUILayout.ExpandHeight(true));
        }
        else
        {
            // Display an interactive list for manual mode
            RenderManualFixList();
        }

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// Opens a folder panel and updates the scan path.
    /// </summary>
    private void SelectScanFolder()
    {
        string selectedPath = EditorUtility.OpenFolderPanel("Select Folder", "Assets", "");
        if (!string.IsNullOrEmpty(selectedPath))
        {
            // Unity's AssetDatabase requires paths relative to the project root (e.g., "Assets/Models").
            if (selectedPath.StartsWith(Application.dataPath))
            {
                scanPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
            }
            else
            {
                EditorUtility.DisplayDialog("Invalid Path", "Please select a folder inside your project's Assets directory.", "OK");
            }
        }
    }
    
    /// <summary>
    /// Renders the list of non-readable models for manual review and fixing.
    /// </summary>
    private void RenderManualFixList()
    {
        if (nonReadableModels.Count == 0)
        {
            EditorGUILayout.LabelField("No non-readable models found in the selected folder.");
        }
        else
        {
            EditorGUILayout.HelpBox($"Found {nonReadableModels.Count} models to fix.", MessageType.Warning);
            if (GUILayout.Button("Fix All Displayed Models", GUILayout.Height(30)))
            {
                FixAllListedModels();
            }
            EditorGUILayout.Space();

            // Iterate backwards as the list can be modified when an item is fixed.
            for (int i = nonReadableModels.Count - 1; i >= 0; i--)
            {
                ModelInfo modelInfo = nonReadableModels[i];
                EditorGUILayout.BeginHorizontal();
                // Display the asset object field (not editable).
                EditorGUILayout.ObjectField(modelInfo.AssetObject, typeof(GameObject), false);
                if (GUILayout.Button("Enable Read/Write", GUILayout.Width(150)))
                {
                    FixModel(modelInfo);
                    nonReadableModels.RemoveAt(i); // Remove from list after fixing.
                }
                EditorGUILayout.EndHorizontal();
            }
        }
    }

    /// <summary>
    /// Main logic to find all model assets and check their import settings.
    /// </summary>
    private void ScanModels()
    {
        nonReadableModels.Clear();
        reportLog = "";
        
        // Use AssetDatabase.FindAssets to efficiently find all assets of type "Model"
        // in the specified folder and its subfolders.
        string[] searchInFolders = { scanPath };
        string[] guids = AssetDatabase.FindAssets("t:Model", searchInFolders);

        if (guids.Length == 0)
        {
            reportLog = $"Scan complete. No models found in: {scanPath}";
            Debug.Log(reportLog);
            return;
        }

        EditorUtility.DisplayProgressBar("Scanning Models", "Checking model import settings...", 0f);

        int modelsFixedCount = 0;
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                EditorUtility.DisplayProgressBar("Scanning Models", $"Checking: {Path.GetFileName(assetPath)}", (float)i / guids.Length);

                // Get the importer for the asset
                ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;

                if (importer != null && !importer.isReadable)
                {
                    if (autoFixEnabled)
                    {
                        // Automatic Mode: Fix it immediately
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                        reportLog += $"FIXED: {assetPath}\n";
                        modelsFixedCount++;
                    }
                    else
                    {
                        // Manual Mode: Add to a list to be displayed in the UI
                        nonReadableModels.Add(new ModelInfo
                        {
                            Path = assetPath,
                            Importer = importer,
                            AssetObject = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath)
                        });
                    }
                }
            }
        }
        finally
        {
            // Ensure the progress bar is always cleared, even if an error occurs.
            EditorUtility.ClearProgressBar();
        }

        if (autoFixEnabled)
        {
            if (modelsFixedCount == 0)
            {
                reportLog = $"Scan complete. All models in '{scanPath}' are already Read/Write enabled.";
            } else
            {
                reportLog = $"Scan complete. Automatically fixed {modelsFixedCount} models.\n\n" + reportLog;
            }
        }
        else
        {
            Debug.Log($"Scan complete. Found {nonReadableModels.Count} non-readable models for manual review.");
        }
    }

    /// <summary>
    /// Sets the isReadable flag to true on a specific ModelImporter and re-imports the asset.
    /// </summary>
    private void FixModel(ModelInfo modelInfo)
    {
        if (modelInfo?.Importer != null)
        {
            modelInfo.Importer.isReadable = true;
            modelInfo.Importer.SaveAndReimport();
            Debug.Log($"Enabled Read/Write on: {modelInfo.Path}");
        }
    }

    /// <summary>
    /// Iterates through all models in the list and fixes them.
    /// </summary>
    private void FixAllListedModels()
    {
        EditorUtility.DisplayProgressBar("Fixing Models", "Enabling Read/Write and re-importing...", 0f);
        try
        {
            for (int i = 0; i < nonReadableModels.Count; i++)
            {
                ModelInfo modelInfo = nonReadableModels[i];
                EditorUtility.DisplayProgressBar("Fixing Models", $"Fixing: {Path.GetFileName(modelInfo.Path)}", (float)i / nonReadableModels.Count);
                FixModel(modelInfo);
            }
            nonReadableModels.Clear();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}

