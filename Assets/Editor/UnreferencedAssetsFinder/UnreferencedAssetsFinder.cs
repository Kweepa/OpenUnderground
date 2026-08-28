using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// An editor tool to find assets within a specified folder that are not referenced by any other asset in the project.
/// It provides an option to delete the unreferenced assets found.
/// </summary>
public class UnreferencedAssetsFinder : EditorWindow
{
    private string _searchPath = "Assets/";
    private Vector2 _scrollPosition;
    private List<string> _unreferencedAssets = new List<string>();
    private Dictionary<string, bool> _assetSelection = new Dictionary<string, bool>();
    private bool _searchInProgress = false;
    private bool _selectAll = false;
    private HashSet<string> _loggedProblematicPaths = new HashSet<string>();
    private bool _searchWasRun = false;
    private string _lastSearchPath = "";
    private GUIStyle _oddRowStyle; // Style for alternating row background color
    private GUIStyle _selectedRowStyle; // Style for a selected/checked row

    /// <summary>
    /// Creates a menu item to open this editor window.
    /// </summary>
    [MenuItem("Tools/Find Unreferenced Assets")]
    public static void ShowWindow()
    {
        GetWindow<UnreferencedAssetsFinder>("Find Unreferenced Assets");
    }

    /// <summary>
    /// Called when the window is enabled. Sets the initial search path and subscribes to editor updates for polling.
    /// </summary>
    private void OnEnable()
    {
        PollAndUpdateSearchPath(); // Set initial path
        EditorApplication.update += PollAndUpdateSearchPath;
    }

    /// <summary>
    /// Called when the window is disabled. Unsubscribes from editor updates to prevent memory leaks.
    /// </summary>
    private void OnDisable()
    {
        EditorApplication.update -= PollAndUpdateSearchPath;
    }

    /// <summary>
    /// Renders the editor window UI.
    /// </summary>
    private void OnGUI()
    {
        EditorGUILayout.HelpBox("This tool finds assets in a selected folder that are not referenced by other assets. Assets in 'Resources' folders or scenes included in build settings are considered referenced.", MessageType.Info);
        EditorGUILayout.Space();

        // --- Folder Selection ---
        EditorGUILayout.LabelField("Selected Folder to Search", EditorStyles.label);
        
        GUIContent pathFieldLabel = new GUIContent("Path", "This path is determined by your current folder selection in the Project view.");
        EditorGUI.BeginDisabledGroup(true); // Disables interaction, making it read-only.
        EditorGUILayout.TextField(pathFieldLabel, _searchPath);
        EditorGUI.EndDisabledGroup();
        
        EditorGUILayout.Space();

        // --- Action Buttons ---
        if (GUILayout.Button("Find Unreferenced Assets", GUILayout.Height(30)))
        {
            FindUnreferencedAssets();
        }

        EditorGUILayout.Space();

        if (_searchInProgress)
        {
            EditorGUILayout.LabelField("Search in progress...");
            return;
        }

        if (!_searchWasRun)
        {
            EditorGUILayout.LabelField("Press 'Find Unreferenced Assets' to begin.");
        }
        else if (_unreferencedAssets.Count == 0)
        {
            EditorGUILayout.LabelField($"No unreferenced assets found in '{_lastSearchPath}'.");
        }
        else
        {
            string pluralSuffix = _unreferencedAssets.Count == 1 ? "" : "s";
            EditorGUILayout.LabelField($"Found {_unreferencedAssets.Count} unreferenced asset{pluralSuffix} in '{_lastSearchPath}'");
            EditorGUILayout.Space(2);

            // --- Selection Header ---
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUI.BeginChangeCheck();
            _selectAll = EditorGUILayout.Toggle(_selectAll, GUILayout.Width(20));
            if (EditorGUI.EndChangeCheck())
            {
                SetAllSelections(_selectAll);
            }

            // Calculate widths for columns to ensure alignment
            float availableWidth = position.width - 45; // Subtract checkbox and scrollbar/padding
            float pathColumnWidth = availableWidth * 0.4f;
            float typeColumnWidth = availableWidth * 0.2f;
            float assetColumnWidth = availableWidth * 0.4f;

            EditorGUILayout.LabelField("Path", GUILayout.Width(pathColumnWidth));
            EditorGUILayout.LabelField("Type", GUILayout.Width(typeColumnWidth));
            EditorGUILayout.LabelField("Asset", GUILayout.Width(assetColumnWidth));
            EditorGUILayout.EndHorizontal();
            
            // **FIX**: Initialize GUI Styles if they are null OR if their background textures have been lost (e.g., after a recompile or tab switch).
            if (_oddRowStyle == null || _oddRowStyle.normal.background == null)
            {
                _oddRowStyle = new GUIStyle();
                var texture = new Texture2D(1, 1);
                Color color = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.06f)
                    : new Color(0f, 0f, 0f, 0.06f);
                texture.SetPixel(0, 0, color);
                texture.Apply();
                _oddRowStyle.normal.background = texture;
            }
            if (_selectedRowStyle == null || _selectedRowStyle.normal.background == null)
            {
                _selectedRowStyle = new GUIStyle();
                var texture = new Texture2D(1, 1);
                Color color = EditorGUIUtility.isProSkin
                    ? new Color(0.25f, 0.45f, 0.7f, 1f)
                    : new Color(0.35f, 0.6f, 0.9f, 1f);
                texture.SetPixel(0, 0, color);
                texture.Apply();
                _selectedRowStyle.normal.background = texture;
            }

            // --- Results List ---
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _unreferencedAssets.Count; i++)
            {
                var assetPath = _unreferencedAssets[i];
                Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);

                // If the asset is null, it's a problematic path. Log it once and skip rendering it.
                if (asset == null)
                {
                    if (!_loggedProblematicPaths.Contains(assetPath))
                    {
                        Debug.LogWarning($"UnreferencedAssetsFinder: Could not load asset at path: '{assetPath}'. It might be an empty folder reference or a corrupted asset. It will not be shown in the list.");
                        _loggedProblematicPaths.Add(assetPath);
                    }
                    continue; // Skip to the next asset
                }

                if (!_assetSelection.ContainsKey(assetPath))
                {
                    _assetSelection[assetPath] = false;
                }

                // Determine which style to use. Selection highlight takes precedence over zebra striping.
                GUIStyle rowStyle;
                bool isSelected = _assetSelection[assetPath];
                if (isSelected)
                {
                    rowStyle = _selectedRowStyle;
                }
                else
                {
                    rowStyle = (i % 2 != 0) ? _oddRowStyle : GUIStyle.none;
                }
                
                EditorGUILayout.BeginHorizontal(rowStyle);

                _assetSelection[assetPath] = EditorGUILayout.Toggle(_assetSelection[assetPath], GUILayout.Width(20));
                
                string fullDirectoryPath = Path.GetDirectoryName(assetPath).Replace("\\", "/");
                string relativePath;

                // Handle cases where search path might have a trailing slash
                string cleanSearchPath = _lastSearchPath.TrimEnd('/');

                if (fullDirectoryPath.Equals(cleanSearchPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = ".";
                }
                // Check if the directory path starts with the search path plus a slash
                else if (fullDirectoryPath.StartsWith(cleanSearchPath + "/", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Get the substring after the search path and its trailing slash
                    relativePath = fullDirectoryPath.Substring(cleanSearchPath.Length + 1);
                }
                else
                {
                    // Fallback to the full path if it's not directly inside the search path
                    relativePath = fullDirectoryPath;
                }
                
                // Use a GUIContent to add a tooltip showing the full path, while displaying the relative one
                EditorGUILayout.LabelField(new GUIContent(relativePath, fullDirectoryPath), GUILayout.Width(pathColumnWidth));
                
                string assetType = asset.GetType().Name;
                if (asset is GameObject)
                {
                    // For GameObjects, let's try to be more specific (Prefab, Model, etc.)
                    switch (PrefabUtility.GetPrefabAssetType(asset))
                    {
                        case PrefabAssetType.Regular:
                            assetType = "Prefab";
                            break;
                        case PrefabAssetType.Variant:
                            assetType = "Prefab Variant";
                            break;
                        case PrefabAssetType.Model:
                            assetType = "Model";
                            break;
                    }
                }
                EditorGUILayout.LabelField(assetType, GUILayout.Width(typeColumnWidth));
                
                EditorGUILayout.ObjectField(asset, typeof(Object), false, GUILayout.Width(assetColumnWidth));

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            // --- Deletion Button at the bottom ---
            EditorGUILayout.Space();

            // Check if any asset is selected for deletion.
            bool anyAssetSelected = _assetSelection.Any(pair => pair.Value);
            
            // Disable the button if no assets are selected.
            EditorGUI.BeginDisabledGroup(!anyAssetSelected);
            if (GUILayout.Button("Delete Selected", GUILayout.Height(25)))
            {
                DeleteSelectedAssets();
            }
            EditorGUI.EndDisabledGroup();
        }
    }

    /// <summary>
    /// Sets the selection state for all found assets.
    /// </summary>
    private void SetAllSelections(bool selected)
    {
        _selectAll = selected;
        var keys = _assetSelection.Keys.ToList();
        foreach (var key in keys)
        {
            _assetSelection[key] = selected;
        }
    }

    /// <summary>
    /// Called on editor update to poll the current project selection and update the search path if it has changed.
    /// </summary>
    private void PollAndUpdateSearchPath()
    {
        string path = null;

        // Using assetGUIDs is the most reliable way to get the selection from either pane
        // of the Project window (folder tree or asset list).
        if (Selection.assetGUIDs.Length > 0)
        {
            path = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
        }
        
        if (!string.IsNullOrEmpty(path))
        {
            // If the selection is a file, get its containing directory.
            if (!AssetDatabase.IsValidFolder(path))
            {
                path = Path.GetDirectoryName(path);
            }
        }
        else
        {
            // Fallback for cases where assetGUIDs might be empty (rare, but possible).
            if (Selection.activeObject != null)
            {
               path = AssetDatabase.GetAssetPath(Selection.activeObject);
               if (!string.IsNullOrEmpty(path) && !AssetDatabase.IsValidFolder(path))
               {
                   path = Path.GetDirectoryName(path);
               }
            }
        }
        
        // If a path was successfully found, update the search path variable if it has changed.
        if (!string.IsNullOrEmpty(path))
        {
            string newPath = path.Replace("\\", "/"); // Normalize path separators
            if (_searchPath != newPath)
            {
                _searchPath = newPath;
                Repaint(); // Redraw the UI with the new path
            }
        }
    }

    /// <summary>
    /// The core logic to find assets that are not referenced anywhere in the project.
    /// This version correctly identifies chains of unreferenced assets.
    /// </summary>
    private void FindUnreferencedAssets()
    {
        if (!Directory.Exists(_searchPath))
        {
            EditorUtility.DisplayDialog("Error", $"The search path '{_searchPath}' does not exist.", "OK");
            return;
        }

        _searchInProgress = true;
        _searchWasRun = true;
        _lastSearchPath = _searchPath;
        _unreferencedAssets.Clear();
        _assetSelection.Clear();
        _selectAll = false;
        _loggedProblematicPaths.Clear(); // Reset for the new search
        Repaint(); // Redraw UI to show "in progress"

        try
        {
            // Step 1: Get all asset paths and identify root assets (entry points).
            var allAssetPaths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/")).ToArray();
            var rootAssets = new HashSet<string>();
            
            // Scenes in build settings are root assets.
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && File.Exists(scene.path))
                {
                    rootAssets.Add(scene.path);
                }
            }
            
            // Assets in any "Resources" folder are root assets.
            foreach (var path in allAssetPaths)
            {
                if (path.Contains("/Resources/"))
                {
                    rootAssets.Add(path);
                }
            }

            // Step 2: Build a complete dependency graph for all assets.
            var allDependencies = new Dictionary<string, string[]>();
            for(int i = 0; i < allAssetPaths.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Building Dependency Graph", $"Processing asset {i+1}/{allAssetPaths.Length}", (float)i/allAssetPaths.Length);
                allDependencies[allAssetPaths[i]] = AssetDatabase.GetDependencies(allAssetPaths[i], false);
            }

            // Step 3: Traverse the dependency graph from the root assets to find all referenced assets.
            var referencedAssets = new HashSet<string>();
            var queue = new Queue<string>(rootAssets);

            while (queue.Count > 0)
            {
                var currentAsset = queue.Dequeue();
                if (referencedAssets.Contains(currentAsset)) continue;

                referencedAssets.Add(currentAsset);

                if (allDependencies.TryGetValue(currentAsset, out var dependencies))
                {
                    foreach (var dep in dependencies)
                    {
                        if (!referencedAssets.Contains(dep))
                        {
                            queue.Enqueue(dep);
                        }
                    }
                }
            }
            
            EditorUtility.DisplayProgressBar("Finding Unreferenced Assets", "Comparing results...", 0.9f);

            // Step 4: Get all assets within the specified search path.
            string[] searchFolder = { _searchPath };
            var candidateGuids = AssetDatabase.FindAssets("", searchFolder);
            
            // Step 5: Compare the candidate assets against the set of all referenced assets.
            foreach (var guid in candidateGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // Ignore folders, script files, and version control files.
                if (AssetDatabase.IsValidFolder(path) || path.EndsWith(".cs") || path.Contains("/.") )
                {
                    continue;
                }

                if (!referencedAssets.Contains(path))
                {
                    _unreferencedAssets.Add(path);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            _searchInProgress = false;
        }
    }
    
    /// <summary>
    /// Deletes the assets that have been selected in the UI.
    /// </summary>
    private void DeleteSelectedAssets()
    {
        var assetsToDelete = _assetSelection
            .Where(pair => pair.Value)
            .Select(pair => pair.Key)
            .ToList();

        if (assetsToDelete.Count == 0)
        {
            // This case should no longer be reachable since the button is disabled.
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Delete Selected Assets?",
            $"Are you sure you want to move {assetsToDelete.Count} assets to the Trash? This action cannot be undone from this tool.",
            "Yes, delete",
            "Cancel"
        );

        if (confirmed)
        {
            foreach (var assetPath in assetsToDelete)
            {
                AssetDatabase.MoveAssetToTrash(assetPath);
            }
            
            // Refresh the list after deletion
            FindUnreferencedAssets();
        }
    }
}
