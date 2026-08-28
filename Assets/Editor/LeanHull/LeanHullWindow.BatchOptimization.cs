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
using UnityEditor.SceneManagement;

namespace Kweepa.LeanHull
{
    /// Apply pipeline: load prefab, generate hulls, save mesh assets, attach MeshColliders.
    public partial class LeanHullWindow
    {
        private void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string parent = Path.GetDirectoryName(folderPath).Replace("\\", "/");
            string folderName = Path.GetFileName(folderPath);

            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolderExists(parent);
            }
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private string GetObjectPersistentId(GameObject go, GameObject root, string prefabPath)
        {
            if (go == null || root == null) return "Unknown";

            string relPath = LeanHullMeshQueries.GetRelativePathStatic(go.transform, root.transform);

            GameObject actualAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (actualAsset != null)
            {
                GameObject targetObj = actualAsset;
                if (!string.IsNullOrEmpty(relPath))
                {
                    Transform t = LeanHullMeshQueries.FindTransformByPathIncludingInactive(actualAsset.transform, relPath);
                    if (t != null) targetObj = t.gameObject;
                    else targetObj = null;
                }

                if (targetObj != null)
                {
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(targetObj, out string _, out long localId))
                    {
                        if (localId != 0) return localId.ToString();
                    }
                }
            }

            string pathKey = string.IsNullOrEmpty(relPath) ? "Root" : relPath;
            return "Path_" + pathKey.GetHashCode().ToString("X8");
        }

        private (int targetVertOverride, MergeOverride mergeOverride) GetExistingColliderSettingsForPrefab(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return (0, MergeOverride.None);
            string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(prefabGuid)) return (0, MergeOverride.None);
            var all = GetAllLeanHullMeshMetadata(generatedCollidersPath);
            int targetVertices = 0;
            int mergedCount = 0;
            int separatedCount = 0;
            foreach (var (_, meta, _) in all)
            {
                if (meta.prefabGuid != prefabGuid) continue;
                int tv = meta.targetVertices > 0 ? meta.targetVertices : 16;
                tv = Mathf.Clamp(tv, 4, 128);
                targetVertices = tv;
                if (meta.mergeMeshes) mergedCount++; else separatedCount++;
            }
            if (mergedCount == 0 && separatedCount == 0) return (0, MergeOverride.None);
            bool useMerged = separatedCount == 0 && mergedCount > 0;
            MergeOverride mergeOverride = useMerged ? MergeOverride.Merged : MergeOverride.Separated;
            return (targetVertices, mergeOverride);
        }

        private void SaveAndAttachCollider(GameObject go, GameObject prefabRoot, string prefabPath, Mesh hullMesh, bool isMock, string sourceContentHash, string sourceMeshOnlyHash, string sourceStructureHash, bool mergeMeshes, int targetVertices, ref bool modified, ref int optimizedCount)
        {
            Mesh savedMesh;

            EnsureFolderExists(generatedCollidersPath);

            string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            string localId = GetObjectPersistentId(go, prefabRoot, prefabPath);

            string sanitizedPrefabName = LeanHullPrefabSettings.SanitizeNameForFileName(prefabRoot.name);
            string sanitizedGoName = LeanHullPrefabSettings.SanitizeNameForFileName(go.name);

            string identityKey = $"{prefabGuid}_{localId}";
            string shortHash = LeanHullPrefabSettings.ShortHashOfGuid(identityKey);
            string baseName = $"LeanHull_{sanitizedPrefabName}_{sanitizedGoName}_{shortHash}";
            string extension = ".asset";
            string targetPath = Path.Combine(generatedCollidersPath, baseName + extension);

            bool isPrefab = prefabPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase);
            ColliderMetadata metadata = new ColliderMetadata
            {
                prefabGuid = prefabGuid,
                localId = localId,
                isPrefab = isPrefab,
                sourceContentHash = sourceContentHash,
                sourceMeshOnlyHash = sourceMeshOnlyHash,
                sourceStructureHash = sourceStructureHash ?? "",
                mergeMeshes = mergeMeshes,
                targetVertices = targetVertices
            };
            string metadataJson = JsonUtility.ToJson(metadata);

            hullMesh.name = baseName;

            string finalPath = targetPath;
            foreach (var (path, existingMeta, _) in GetAllLeanHullMeshMetadata(generatedCollidersPath))
            {
                if (existingMeta.prefabGuid != prefabGuid || existingMeta.localId != localId) continue;
                Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (m != null && !IsAssetUsedElsewhere(m, prefabPath))
                {
                    finalPath = path;
                    break;
                }
            }

            Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(finalPath);
            if (existingMesh != null)
            {
                EditorUtility.CopySerialized(hullMesh, existingMesh);
                savedMesh = existingMesh;
                EditorUtility.SetDirty(existingMesh);
            }
            else
            {
                AssetDatabase.CreateAsset(Instantiate(hullMesh), finalPath);
                savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(finalPath);
            }

            AssetImporter importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(savedMesh));
            if (importer != null)
            {
                importer.userData = metadataJson;
                importer.SaveAndReimport();
            }

            if (hullMesh != null) DestroyImmediate(hullMesh);

            if (savedMesh == null)
            {
                UnityEngine.Debug.LogWarning($"LeanHull: Failed to save or load generated collider at {prefabPath}. Check folder permissions.");
                return;
            }

            if (!isMock)
            {
                Collider sourceCollider = go.GetComponent<Collider>();

                if (go == prefabRoot)
                {
                    MeshCollider[] existingMcs = prefabRoot.GetComponentsInChildren<MeshCollider>(true);
                    foreach (var emc in existingMcs)
                    {
                        if (emc.gameObject != prefabRoot) DestroyImmediate(emc, true);
                    }
                }

                Collider[] allColliders = go.GetComponents<Collider>();
                foreach (var c in allColliders)
                {
                    if (!(c is MeshCollider)) DestroyImmediate(c, true);
                }

                MeshCollider mc = go.GetComponent<MeshCollider>();
                if (mc == null)
                {
                    mc = go.AddComponent<MeshCollider>();
                    if (sourceCollider != null) CopyColliderProperties(sourceCollider, mc);
                }

                mc.sharedMesh = savedMesh;
                mc.convex = true;
                modified = true;
            }
            else
            {
                optimizedCount++;
            }
        }

        private GameObject LoadPrefabForEdit(string assetPath, out PrefabStage stage)
        {
            stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == assetPath)
            {
                return stage.prefabContentsRoot;
            }
            stage = null;
            return PrefabUtility.LoadPrefabContents(assetPath);
        }

        private void UnloadPrefabForEdit(GameObject prefabRoot, string assetPath, PrefabStage stage, bool save = false)
        {
            if (stage != null)
            {
                if (save)
                {
                    EditorUtility.SetDirty(prefabRoot);
                    EditorSceneManager.MarkSceneDirty(stage.scene);
                }
            }
            else
            {
                if (save)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
                }
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private GameObject LoadAssetForData(string assetPath, out PrefabStage stage, out bool isMock)
        {
            isMock = false;
            stage = null;

            if (string.IsNullOrEmpty(assetPath)) return null;

            if (assetPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            {
                return LoadPrefabForEdit(assetPath, out stage);
            }

            if (assetPath.EndsWith(".mesh", System.StringComparison.OrdinalIgnoreCase))
            {
                Mesh looseMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                if (looseMesh == null) return null;

                isMock = true;
                GameObject mockObj = new GameObject(looseMesh.name);
                mockObj.hideFlags = HideFlags.HideAndDontSave;
                MeshFilter mf = mockObj.AddComponent<MeshFilter>();
                mf.sharedMesh = looseMesh;
                MeshRenderer mr = mockObj.AddComponent<MeshRenderer>();
                mr.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                return mockObj;
            }

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (modelAsset != null)
            {
                isMock = true;
                GameObject inst = Instantiate(modelAsset);
                inst.name = modelAsset.name;
                inst.hideFlags = HideFlags.HideAndDontSave;
                return inst;
            }

            return null;
        }

        private void UnloadAssetForData(GameObject root, string path, PrefabStage stage, bool isMock)
        {
            if (isMock)
            {
                DestroyImmediate(root);
            }
            else
            {
                UnloadPrefabForEdit(root, path, stage);
            }
        }

        private void CopyColliderProperties(Collider source, Collider dest)
        {
            if (source == null || dest == null) return;

            SerializedObject soSource = new SerializedObject(source);
            SerializedObject soDest = new SerializedObject(dest);

            SerializedProperty prop = soSource.GetIterator();
            if (prop.NextVisible(true))
            {
                do
                {
                    if (prop.name == "m_IsConvex" ||
                        prop.name == "m_Mesh" ||
                        prop.name == "m_CookingOptions")
                    {
                        continue;
                    }

                    if (prop.name == "m_Script" || prop.name == "m_ObjectHideFlags" || prop.name == "m_CorrespondingSourceObject")
                    {
                        continue;
                    }

                    SerializedProperty targetProp = soDest.FindProperty(prop.name);
                    if (targetProp != null)
                    {
                        soDest.CopyFromSerializedPropertyIfDifferent(prop);
                    }
                }
                while (prop.NextVisible(false));
            }

            soDest.ApplyModifiedPropertiesWithoutUndo();
        }

        private void RunBatchOptimization()
        {
            if (_previewItems.Count == 0) return;

            _isOptimizing = true;

            HashSet<string> pathsToOptimize = new HashSet<string>();
            foreach (var item in _previewItems)
            {
                pathsToOptimize.Add(item.prefabPath);
            }

            CleanupPreviews(false);
            Repaint();

            int optimizedCount = 0;
            List<string> processedPaths = new List<string>(pathsToOptimize);
            List<string> orphanHullPathsToCheck = new List<string>();

            for (int i = 0; i < processedPaths.Count; ++i)
            {
                string pPath = processedPaths[i];
                float progress = (float)i / processedPaths.Count;
                EditorUtility.DisplayProgressBar("LeanHull Collision Optimizer", $"Processing {Path.GetFileName(pPath)}...", progress);

                var previewItem = _previewItems.Find(x => x.prefabPath == pPath);
                int targetVertOverride = previewItem != null ? previewItem.targetVertOverride : 0;
                bool merge = previewItem != null ? GetEffectiveMerge(previewItem, _mergeMeshes) : _mergeMeshes;

                GameObject prefabRoot = LoadAssetForData(pPath, out PrefabStage stage, out bool isMock);
                if (prefabRoot == null) continue;

                bool modified = false;
                RemoveOrphanCollidersForPrefab(pPath, prefabRoot, ref modified, orphanHullPathsToCheck);

                List<MeshFilter> mfsStrict = LeanHullMeshQueries.GetMeshFiltersForItem(prefabRoot, previewItem, pPath);
                int validMeshCount = mfsStrict.Count;

                if (merge && validMeshCount > 1)
                {
                    List<MeshFilter> mfsMerge = LeanHullMeshQueries.GetMeshFiltersForItem(prefabRoot, previewItem, pPath, false);
                    List<Vector3> allVertices = LeanHullMeshQueries.GetMergedVerticesInPrefabSpace(prefabRoot, mfsMerge);

                    if (allVertices.Count == 0)
                    {
                        MeshCollider[] mcs = prefabRoot.GetComponentsInChildren<MeshCollider>(true);
                        foreach (var mc in mcs)
                        {
                            if (mc.sharedMesh != null)
                            {
                                Vector3[] verts = mc.sharedMesh.vertices;
                                Transform t = mc.transform;
                                foreach (Vector3 vert in verts)
                                {
                                    Vector3 worldPos = t.TransformPoint(vert);
                                    Vector3 localPos = prefabRoot.transform.InverseTransformPoint(worldPos);
                                    allVertices.Add(localPos);
                                }
                            }
                        }
                    }

                    if (allVertices.Count >= 4)
                    {
                        int targetVerts = targetVertOverride > 0 ? targetVertOverride : _batchTargetVerts;
                        Mesh finalMesh = GenerateOptimizedHull(allVertices.ToArray(), targetVerts, out int _, out Mesh originalHull, $"Merge Optimization '{prefabRoot.name}'");
                        if (originalHull != null) DestroyImmediate(originalHull);

                        if (finalMesh != null)
                        {
                            string sourceHash = LeanHullHashing.ComputeSourceContentHashMerged(prefabRoot, pPath, previewItem);
                            string meshOnlyHash = LeanHullHashing.ComputeSourceContentHashMeshOnlyMerged(prefabRoot, pPath, previewItem);
                            string structureHash = LeanHullHashing.ComputeSourceStructureHashMerged(prefabRoot, pPath, previewItem);
                            SaveAndAttachCollider(prefabRoot, prefabRoot, pPath, finalMesh, isMock, sourceHash, meshOnlyHash, structureHash, true, targetVerts, ref modified, ref optimizedCount);
                        }
                    }
                }
                else
                {
                    if (!isMock)
                    {
                        MeshCollider rootMc = prefabRoot.GetComponent<MeshCollider>();
                        if (rootMc != null && rootMc.sharedMesh != null && rootMc.sharedMesh.name.StartsWith("LeanHull_"))
                            DestroyImmediate(rootMc, true);
                    }
                    foreach (MeshFilter mf in mfsStrict)
                    {
                        int targetVerts = targetVertOverride > 0 ? targetVertOverride : _batchTargetVerts;
                        Mesh finalMesh = GenerateOptimizedHull(mf.sharedMesh.vertices, targetVerts, out int _, out Mesh originalHull, $"Indiv Optimization '{mf.gameObject.name}'");
                        if (originalHull != null && originalHull != finalMesh)
                        {
                            DestroyImmediate(originalHull);
                        }

                        if (finalMesh != null)
                        {
                            string sourceHash = LeanHullHashing.ComputeSourceContentHashSingle(mf.sharedMesh);
                            SaveAndAttachCollider(mf.gameObject, prefabRoot, pPath, finalMesh, isMock, sourceHash, sourceHash, "", false, targetVerts, ref modified, ref optimizedCount);
                        }
                    }
                }

                if (modified)
                {
                    optimizedCount++;
                    UnloadPrefabForEdit(prefabRoot, pPath, stage, true);
                }
                else
                {
                    UnloadAssetForData(prefabRoot, pPath, stage, isMock);
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            foreach (string path in orphanHullPathsToCheck)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh != null && !IsAssetUsedElsewhere(mesh, ""))
                    AssetDatabase.DeleteAsset(path);
            }

            _isOptimizing = false;
            UpdateBatchSelection(true);

            foreach (string pPath in processedPaths)
            {
                var item = _previewItems.Find(x => x.prefabPath == pPath);
                if (item != null)
                    item.staleChangeDescription = null;
            }
        }
    }
}
