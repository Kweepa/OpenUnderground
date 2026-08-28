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
using System.Collections.Generic;
using UnityEditor.SceneManagement;

namespace Kweepa.LeanHull
{
    // The primary EditorWindow interface and batch processing manager for the LeanHull tool.
    // Handles asset discovery, background preview rendering, and the hull generation pipeline
    // for prefabs, models, and loose meshes.
    public partial class LeanHullWindow : EditorWindow
    {
        private const string prefsKey = "Kweepa_LeanHull_Settings";

        [MenuItem("Window/LeanHull")]
        public static void ShowWindow()
        {
            GetWindow<LeanHullWindow>("LeanHull");
        }

        private static string generatedCollidersPath => LeanHullProjectSettings.GeneratedAssetsFolder;

        private const string assetsMenuPath = "Assets/LeanHull";
        private const int assetsMenuPriority = 2000;
        private const int hideVertexCountLabelWidth = 340;
        private const int hideMergeLabelWidth = 260;
        private const int hideCleanupLabelWidth = 260;
        
        [MenuItem(assetsMenuPath, false, assetsMenuPriority)]
        public static void OpenFromAssets()
        {
            ShowWindow();
        }

        [MenuItem(assetsMenuPath, true, assetsMenuPriority)]
        public static bool ValidateOpenFromAssets()
        {
            // Ensure we are looking at assets, not scene instances, and skip LeanHull generated assets
            foreach (var obj in Selection.objects)
            {
                if (obj == null || obj.name.StartsWith("LeanHull_")) continue;
                if (!AssetDatabase.Contains(obj)) continue;
                
                if (obj is GameObject go)
                {
                    var type = PrefabUtility.GetPrefabAssetType(go);
                    if (type is PrefabAssetType.Regular or PrefabAssetType.Variant or PrefabAssetType.Model)
                    {
                        return true;
                    }
                }
                else if (obj is Mesh)
                {
                    return true;
                }
            }
            return false;
        }

        // --- Batch Processing ---
        [SerializeField] private bool _mergeMeshes;
        [SerializeField] private int _batchTargetVerts = 16;
        
        // Scan state
        private bool _isRendering;
        private float _toolbarHeight = 30f;
        private float _bottomBarHeight = 30f;
        private bool _isOptimizing;
        private int _renderIndex;

        private PreviewRenderUtility _pru;
        private readonly List<PreviewItem> _previewItems = new ();
        private Texture2D _vignetteTex;
        private Texture2D _groupBgTex;
        private Material _wireMaterial;
        private Material _wireAAMaterial;
        private Texture2D _iconMerged;
        private Texture2D _iconSeparated;
        private Texture2D _iconSweep;
        private bool _lastProSkin;
        private bool _isRotating;
        private bool _isDraggingSplit;
        private PreviewItem _draggedItem;
        private Vector2 _mouseDownPos;
        private PreviewItem _hoveredItem;
        private PreviewItem _lastHoveredItem;

        [SerializeField] private float _splitPosition = LeanHullConstants.initialSplitPosition;
        [SerializeField] private Vector2 _previewDir = new (-120, 20);
        [SerializeField] private Vector2 _gridScroll;
        [SerializeField] private Vector2 _helpScroll;
        [SerializeField] private float _previewSize = LeanHullConstants.minTileSize;
        private readonly Color _previewBgColor = new (0.18f, 0.18f, 0.18f, 1f);
        private readonly Color _hullLineColor = new (0f, 0.7f, 0f, 1f);
        private Rect _lastScrollViewRect;
        private bool _showHelp;

        private const float hierarchyPanelWidth = 240f;
        [SerializeField] private Vector2 _hierarchyScroll;
        private PreviewItem _hierarchyItem;  // when non-null, show hierarchy panel for this prefab tile
        private int _singlePreviewSizePixels; // when > 0, RenderItem uses this instead of _previewSize (single preview fits area)
        private int _lastSinglePreviewSizePixels; // so we can invalidate when window resizes

        private void OnEnable()
        {
            wantsMouseMove = true;
            LoadSettings();
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.projectChanged += OnSelectionChanged;
            EditorApplication.update += EditorUpdate;
            UpdateBatchSelection(false);

            // Resume rendering if we have dirty items (e.g. after domain reload)
            if (_previewItems.Exists(x => x.isDirty))
            {
                _isRendering = true;
                _renderIndex = 0;
            }

            Repaint();
        }

        private void OnDisable()
        {
            _isRotating = false;
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.projectChanged -= OnSelectionChanged;
            EditorApplication.update -= EditorUpdate;
            CleanupPreviews();
            if (_wireMaterial != null) DestroyImmediate(_wireMaterial);
            if (_wireAAMaterial != null) DestroyImmediate(_wireAAMaterial);
            if (_groupBgTex != null) { DestroyImmediate(_groupBgTex); _groupBgTex = null; }
            SaveSettings();
        }

        private void OnDestroy()
        {
            SaveSettings();
        }

        private void CleanupPreviews(bool clearItems = true)
        {
            if (_pru != null)
            {
                _pru.Cleanup();
                _pru = null;
            }
            foreach (var item in _previewItems)
            {
                item.Cleanup(clearItems);
            }
            if (clearItems) _previewItems.Clear();
        }

        private void InvalidateAllPreviewTextures()
        {
            foreach (var item in _previewItems)
            {
                if (item.tex != null) DestroyImmediate(item.tex);
                item.tex = null;
                item.previewTextureDirty = true;
            }
        }

        private static bool GetEffectiveMerge(PreviewItem item, bool globalMerge) =>
            item.mergeOverride == MergeOverride.Merged || (item.mergeOverride == MergeOverride.None && globalMerge);
        
        private void OnSelectionChanged()
        {
            UpdateBatchSelection(false);
            Repaint();
        }

        private void UpdateBatchSelection(bool invalidateExisting, bool vertCountChanged = true, bool mergeChanged = true)
        {
            if (_pru == null)
            {
                _pru = new PreviewRenderUtility();
            }

            // Compile current valid selection
            List<Object> validObjects = new();
            
            foreach (Object obj in Selection.objects)
            {
                if (obj == null || obj.name.StartsWith("LeanHull_")) continue;
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase) || 
                    path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) || 
                    path.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase) || 
                    path.EndsWith(".blend", System.StringComparison.OrdinalIgnoreCase) || 
                    path.EndsWith(".mesh", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (!validObjects.Contains(obj)) validObjects.Add(obj);
                }
            }

            // Step 1: Remove items that are no longer selected
            for (int i = _previewItems.Count - 1; i >= 0; i--)
            {
                if (!validObjects.Exists(x => x.name == _previewItems[i].name && AssetDatabase.GetAssetPath(x) == _previewItems[i].prefabPath))
                {
                    if (_hierarchyItem == _previewItems[i]) _hierarchyItem = null;
                    _previewItems[i].Cleanup();
                    _previewItems.RemoveAt(i);
                }
                else if (invalidateExisting)
                {
                    var item = _previewItems[i];
                    bool needsRefresh = (vertCountChanged && item.targetVertOverride == 0)
                        || (mergeChanged && item.mergeOverride == MergeOverride.None && item.validMeshCount > 1);
                    
                    if (needsRefresh) item.isDirty = true;
                }
            }

            if (validObjects.Count > 0)
            {
                bool hasNewItems = false;
                // Step 2: Add new items that aren't already in the list
                foreach (Object obj in validObjects)
                {
                    string pPath = AssetDatabase.GetAssetPath(obj);
                    
                    GameObject prefabRoot = LoadAssetForData(pPath, out PrefabStage stage, out bool isMock);
                    if (prefabRoot == null) continue;

                    bool isModelPath = pPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) || pPath.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase) || pPath.EndsWith(".blend", System.StringComparison.OrdinalIgnoreCase);
                    Mesh filterMeshForHash = (obj is Mesh meshObj && isModelPath) ? meshObj : null;
                    long currentMeshHash = LeanHullHashing.CalculateMeshHash(prefabRoot, false, filterMeshForHash);

                    // Skip if we already have it AND the mesh hasn't changed
                    var existingItem = _previewItems.Find(x => x.prefabPath == pPath && x.name == obj.name);
                    if (existingItem != null)
                    {
                        // Don't overwrite targetVertOverride/mergeOverride from saved here â€” preserves in-UI vertex count when exclude/sidecar changes trigger projectChanged
                        existingItem.staleChangeDescription = _isOptimizing ? null : GetStaleChangeDescription(pPath);
                        if (existingItem.meshHash == currentMeshHash)
                        {
                            // Double check with deep vertex hash if shallow pass matches
                            long deepHash = LeanHullHashing.CalculateMeshHash(prefabRoot, true, existingItem.filterMesh);
                            if (existingItem.deepMeshHash == deepHash)
                            {
                                UnloadAssetForData(prefabRoot, pPath, stage, isMock);
                                continue;
                            }
                            
                            // Shallow match, deep mismatch - update deep hash and proceed to refresh
                            existingItem.deepMeshHash = deepHash;
                        }
                        else
                        {
                            existingItem.meshHash = currentMeshHash;
                            existingItem.deepMeshHash = LeanHullHashing.CalculateMeshHash(prefabRoot, true, existingItem.filterMesh);
                        }
                        
                        // Hash mismatch - update existing item
                        existingItem.isDirty = true;
                        existingItem.previewTextureDirty = true;
                        if (existingItem.tex != null) DestroyImmediate(existingItem.tex);
                        existingItem.tex = null;
                        
                        // Recache render data: always include all drawable meshes (including disabled/excluded)
                        existingItem.renderMeshes.Clear();
                        existingItem.renderMatrices.Clear();
                        existingItem.renderScales.Clear();
                        existingItem.renderMaterials.Clear();
                        var existingMfDisplaySet = new HashSet<MeshFilter>();
                        LeanHullMeshQueries.GetMeshFiltersForPreviewDisplay(prefabRoot, existingItem, existingMfDisplaySet);
                        Renderer[] rends = prefabRoot.GetComponentsInChildren<Renderer>(true);
                        foreach (var r in rends)
                        {
                            MeshFilter mf = r.GetComponent<MeshFilter>();
                            if (mf != null && mf.sharedMesh != null && existingMfDisplaySet.Contains(mf))
                            {
                                existingItem.renderMeshes.Add(mf.sharedMesh);
                                existingItem.renderMatrices.Add(r.transform.localToWorldMatrix);
                                existingItem.renderScales.Add(r.transform.lossyScale);
                                existingItem.renderMaterials.Add(r.sharedMaterials);
                            }
                        }
                        
                        hasNewItems = true;
                        UnloadAssetForData(prefabRoot, pPath, stage, isMock);
                        continue;
                    }

                    hasNewItems = true;
                    // Let's get the icon first
                    Texture icon = AssetPreview.GetMiniThumbnail(obj);
                    if (icon == null)
                    {
                        GUIContent content = EditorGUIUtility.ObjectContent(obj, obj.GetType());
                        if (content != null) icon = content.image;
                    }

                    if (prefabRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0)
                    {
                        UnloadAssetForData(prefabRoot, pPath, stage, isMock);
                        continue;
                    }

                    bool hasVisualMeshes = false;
                    var (existingTargetVerts, existingMergeOverride) = GetExistingColliderSettingsForPrefab(pPath);
                    Mesh itemFilterMesh = (obj is Mesh mesh && isModelPath) ? mesh : null;
                    PreviewItem item = new()
                    {
                        prefabPath = pPath,
                        name = obj.name,
                        filterMesh = itemFilterMesh,
                        icon = icon,
                        meshHash = currentMeshHash,
                        deepMeshHash = LeanHullHashing.CalculateMeshHash(prefabRoot, true, itemFilterMesh),
                        splitPosition = _splitPosition,
                        targetVertOverride = existingTargetVerts,
                        mergeOverride = existingMergeOverride,
                        staleChangeDescription = _isOptimizing ? null : GetStaleChangeDescription(pPath)
                    };
                    
                    Renderer[] renderers = prefabRoot.GetComponentsInChildren<Renderer>(true);
                    Bounds bounds = new();
                    bool hasBounds = false;
                    foreach (var r in renderers)
                    {
                        if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                        else bounds.Encapsulate(r.bounds);
                    }
                    if (!hasBounds) bounds = new(prefabRoot.transform.position, Vector3.one);
                    item.bounds = bounds;

                    List<MeshFilter> mfsStrict = LeanHullMeshQueries.GetMeshFiltersForItem(prefabRoot, item, pPath);
                    int validMeshCount = mfsStrict.Count;

                    if (_mergeMeshes && validMeshCount > 1)
                    {
                        List<MeshFilter> mfsMerge = LeanHullMeshQueries.GetMeshFiltersForItem(prefabRoot, item, pPath, false);
                        List<Vector3> allVertices = LeanHullMeshQueries.GetMergedVerticesInPrefabSpace(prefabRoot, mfsMerge);

                        if (allVertices.Count >= 4)
                        {
                            hasVisualMeshes = true;
                        }
                    }
                    else if (validMeshCount > 0)
                    {
                        hasVisualMeshes = true;
                    }

                    // When root (or all) is excluded, validMeshCount can be 0; still show prefab so user can open and un-exclude
                    bool hasPotentialContributors = false;
                    if (!hasVisualMeshes && validMeshCount == 0 && pPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                    {
                        MeshFilter[] allMfs = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
                        foreach (MeshFilter mf in allMfs)
                        {
                            if (LeanHullMeshQueries.IsMeshPotentialContributor(mf)) { hasPotentialContributors = true; break; }
                        }
                    }

                    if (hasVisualMeshes || hasPotentialContributors)
                    {
                        item.validMeshCount = hasPotentialContributors && !hasVisualMeshes ? 0 : validMeshCount;
                        item.currentVerts = 0; // Will be accurately set by the render pass
                        item.optimizedVerts = 0;

                        // Cache visuals for preview: always include all drawable meshes (including disabled/excluded) so they are always drawn
                        var mfDisplaySet = new HashSet<MeshFilter>();
                        LeanHullMeshQueries.GetMeshFiltersForPreviewDisplay(prefabRoot, item, mfDisplaySet);
                        foreach (var r in renderers)
                        {
                            MeshFilter mf = r.GetComponent<MeshFilter>();
                            if (mf != null && mf.sharedMesh != null && mfDisplaySet.Contains(mf))
                            {
                                item.renderMeshes.Add(mf.sharedMesh);
                                item.renderMatrices.Add(r.transform.localToWorldMatrix);
                                item.renderScales.Add(r.transform.lossyScale);
                                item.renderMaterials.Add(r.sharedMaterials);
                            }
                        }

                        // Instantly queue visual thumbnail for generation
                        item.isDirty = true; // Needs background physics generation
                        item.tex = null;
                        item.previewTextureDirty = true;
                        _previewItems.Add(item);
                    }
                    else
                    {
                        item.Cleanup();
                    }

                    UnloadAssetForData(prefabRoot, pPath, stage, isMock);
                }

                if (hasNewItems || invalidateExisting)
                {
                    _isRendering = true;
                    _renderIndex = 0;
                }
                else
                {
                    UpdateBatchStats();
                }

                // Sort items by full asset path for a consistent grid order
                _previewItems.Sort((a, b) => string.Compare(a.prefabPath, b.prefabPath, System.StringComparison.OrdinalIgnoreCase));
            }
        }

        private Mesh GenerateOptimizedHull(Vector3[] rawVerts, int targetVerts, out int preSimVertCount, out Mesh originalHull, string debugName = "")
        {
            return LeanHullHullGenerator.Generate(rawVerts, targetVerts, out preSimVertCount, out originalHull, debugName);
        }

        //Generates hull mesh(es) for this item and fills item.hullMeshes, item.originalHullMeshes, item.hullMatrices.
        //Caller must clear those lists first. Returns total pre-simplification vertex count.
        private int GenerateHullsForItem(GameObject prefabRoot, PreviewItem item, int targetVerts, bool merge)
        {
            List<MeshFilter> mfsStrict = LeanHullMeshQueries.GetMeshFiltersForItem(prefabRoot, item, item.prefabPath);
            int validMeshCount = mfsStrict.Count;
            int generatedHullVertCount = 0;

            if (merge && validMeshCount > 1)
            {
                List<MeshFilter> mfsMerge = LeanHullMeshQueries.GetMeshFiltersForItem(prefabRoot, item, item.prefabPath, false);
                List<Vector3> allVertices = LeanHullMeshQueries.GetMergedVerticesInPrefabSpace(prefabRoot, mfsMerge);
                if (allVertices.Count >= 4)
                {
                    Mesh finalSim = GenerateOptimizedHull(allVertices.ToArray(), targetVerts, out int preSim, out Mesh originalHull);
                    if (finalSim != null)
                    {
                        generatedHullVertCount += preSim;
                        item.hullMeshes.Add(finalSim);
                        item.originalHullMeshes.Add(originalHull);
                        item.hullMatrices.Add(prefabRoot.transform.localToWorldMatrix);
                    }
                }
            }
            else
            {
                foreach (MeshFilter mf in mfsStrict)
                {
                    Mesh finalSim = GenerateOptimizedHull(mf.sharedMesh.vertices, targetVerts, out int preSim, out Mesh originalHull);
                    if (finalSim != null)
                    {
                        generatedHullVertCount += preSim;
                        item.hullMeshes.Add(finalSim);
                        item.originalHullMeshes.Add(originalHull);
                        item.hullMatrices.Add(mf.transform.localToWorldMatrix);
                    }
                }
            }
            return generatedHullVertCount;
        }

        private void EditorUpdateRender()
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            long maxMsPerFrame = 30; // ~33fps target, allowing more heavy processing per frame
            
            while (_renderIndex < _previewItems.Count && sw.ElapsedMilliseconds < maxMsPerFrame)
            {
                PreviewItem item = _previewItems[_renderIndex];
                
                if (!item.isDirty && item.hullMeshes.Count > 0)
                {
                    _renderIndex++;
                    continue; // Already processed!
                }

                int targetVertsForItem = item.targetVertOverride > 0 ? item.targetVertOverride : _batchTargetVerts;
                // When reducing (target < current), simplify from current mesh(es) so debug "next merge" matches what actually happens
                bool canReduceFromCurrent = item.hullMeshes.Count >= 1 && targetVertsForItem >= 4;
                if (canReduceFromCurrent)
                {
                    foreach (Mesh h in item.hullMeshes)
                    {
                        if (h.vertexCount <= targetVertsForItem) { canReduceFromCurrent = false; break; }
                    }
                }
                if (canReduceFromCurrent)
                {
                    bool allSucceeded = true;
                    for (int h = 0; h < item.hullMeshes.Count; h++)
                    {
                        Mesh mesh = item.hullMeshes[h];
                        while (mesh.vertexCount > targetVertsForItem)
                        {
                            Mesh next = LeanHullMeshSimplifier.Simplify(mesh, mesh.vertexCount - 1);
                            if (next == null) { allSucceeded = false; break; }
                            DestroyImmediate(mesh);
                            mesh = next;
                        }
                        item.hullMeshes[h] = mesh;
                        if (!allSucceeded) break;
                    }
                    if (allSucceeded)
                    {
                        item.optimizedVerts = 0;
                        foreach (var hm in item.hullMeshes) item.optimizedVerts += hm.vertexCount;
                        item.isDirty = false;
                        item.previewTextureDirty = true;
                        _renderIndex++;
                        continue;
                    }
                }

                // Clear any old data
                foreach (var m in item.hullMeshes) if (m != null) DestroyImmediate(m);
                item.hullMeshes.Clear();
                foreach (var m in item.originalHullMeshes) if (m != null) DestroyImmediate(m);
                item.originalHullMeshes.Clear();
                item.hullMatrices.Clear();

                GameObject prefabRoot = LoadAssetForData(item.prefabPath, out PrefabStage stage, out bool isMock);
                if (prefabRoot != null)
                {
                    int targetVerts = item.targetVertOverride > 0 ? item.targetVertOverride : _batchTargetVerts;
                    bool merge = GetEffectiveMerge(item, _mergeMeshes);
                    int generatedHullVertCount = GenerateHullsForItem(prefabRoot, item, targetVerts, merge);

                    if (item.hullMeshes.Count > 0)
                    {
                        item.currentVerts = generatedHullVertCount;
                        item.optimizedVerts = 0;
                        foreach (var hm in item.hullMeshes) item.optimizedVerts += hm.vertexCount;
                    }

                    // Build hull contributor tree for prefabs (GameObject hierarchy down to MeshFilters, sidecar toggles)
                    item.hullContributors.Clear();
                    if (item.prefabPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase) && !isMock)
                    {
                        List<MeshFilter> mfsStrict = LeanHullMeshQueries.GetMeshFiltersForItem(prefabRoot, item, item.prefabPath);
                        int validMeshCount = mfsStrict.Count;
                        List<MeshFilter> mfsForTree = (merge && validMeshCount > 1) ? LeanHullMeshQueries.GetMeshFiltersForItem(prefabRoot, item, item.prefabPath, false) : mfsStrict;
                        HashSet<string> excludeSet = LeanHullPrefabSettings.GetExcludeIdsForPrefab(item.prefabPath);
                        Transform root = prefabRoot.transform;

                        // Map: MeshFilter -> hullIndex (same order as hull building: merge=0, separated=0,1,2,...)
                        Dictionary<MeshFilter, int> mfToHullIndex = new Dictionary<MeshFilter, int>();
                        int separatedHullIndex = 0;
                        foreach (MeshFilter mf in mfsForTree)
                        {
                            if (!LeanHullMeshQueries.IsMeshPotentialContributor(mf)) continue;
                            if (LeanHullMeshQueries.IsMeshExcluded(mf, item.prefabPath, prefabRoot)) continue;
                            int hi = merge && validMeshCount > 1 ? 0 : separatedHullIndex++;
                            mfToHullIndex[mf] = hi;
                        }

                        // For prefabs show full hierarchy: all GameObjects and MeshFilters (including disabled)
                        Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
                        HashSet<Transform> showTransforms = new HashSet<Transform>(allTransforms);

                        void AddTreeRecursive(Transform t)
                        {
                            if (!showTransforms.Contains(t)) return;
                            int depth = LeanHullMeshQueries.GetTransformDepth(t, root);

                            // GameObject row
                            if (LeanHullMeshQueries.TryGetExcludeIdForMenu(t.gameObject, item.prefabPath, prefabRoot, out string goExcludeId))
                            {
                                item.hullContributors.Add(new HullContributorEntry
                                {
                                    isGameObject = true,
                                    displayName = t.gameObject.name,
                                    depth = depth,
                                    excludeId = goExcludeId,
                                    isExcluded = excludeSet.Contains(goExcludeId),
                                    hullIndex = -1
                                });
                            }

                            // Mesh rows for this GameObject (show all MeshFilters including on disabled objects)
                            MeshFilter[] goMfs = t.GetComponents<MeshFilter>();
                            foreach (MeshFilter mf in goMfs)
                            {
                                if (!LeanHullMeshQueries.TryGetExcludeIdForMenu(mf, item.prefabPath, prefabRoot, out string mfExcludeId)) continue;
                                bool meshExcluded = LeanHullMeshQueries.IsMeshExcluded(mf, item.prefabPath, prefabRoot);
                                mfToHullIndex.TryGetValue(mf, out int hullIdx);
                                item.hullContributors.Add(new HullContributorEntry
                                {
                                    isGameObject = false,
                                    displayName = mf.sharedMesh != null ? mf.sharedMesh.name : "Mesh",
                                    depth = depth + 1,
                                    excludeId = mfExcludeId,
                                    isExcluded = meshExcluded,
                                    hullIndex = meshExcluded ? -1 : hullIdx
                                });
                            }

                            for (int i = 0; i < t.childCount; i++)
                                AddTreeRecursive(t.GetChild(i));
                        }
                        AddTreeRecursive(root);
                    }

                    UnloadAssetForData(prefabRoot, item.prefabPath, stage, isMock);
                }

                item.isDirty = false;
                item.previewTextureDirty = true;
                if (item.tex != null) DestroyImmediate(item.tex);
                item.tex = null;
                _renderIndex++;
            }

            if (_renderIndex >= _previewItems.Count)
            {
                _isRendering = false;
                UpdateBatchStats();
            }
            
            Repaint();
        }

        private void UpdateBatchStats()
        {
            int totalCurrentVerts = 0;
            int totalOptimizedVerts = 0;

            foreach (var item in _previewItems)
            {
                totalCurrentVerts += item.currentVerts;
                totalOptimizedVerts += item.optimizedVerts;
            }
        }

        private void EditorUpdate()
        {
            if (_isOptimizing) return;
            
            if (_isRotating)
            {
                Repaint();
            }

            // Pause background rendering while rotating to keep the UI smooth
            if (_isRendering && !_isRotating)
            {
                EditorUpdateRender();
            }
        }

    }
}
