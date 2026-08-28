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

namespace Kweepa.LeanHull
{
    // Stale detection, cleanup orphan colliders/sidecars, find-stale UI behavior.
    public partial class LeanHullWindow
    {
        // Returns all LeanHull mesh asset paths in the folder with their parsed ColliderMetadata and raw userData.
        // Skips unparseable userData.
        private static List<(string path, ColliderMetadata meta, string userData)> GetAllLeanHullMeshMetadata(string folderPath)
        {
            var list = new List<(string path, ColliderMetadata meta, string userData)>();
            if (!AssetDatabase.IsValidFolder(folderPath)) return list;
            string[] guids = AssetDatabase.FindAssets("LeanHull_ t:Mesh", new[] { folderPath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AssetImporter imp = AssetImporter.GetAtPath(path);
                if (imp == null || string.IsNullOrEmpty(imp.userData)) continue;
                try
                {
                    ColliderMetadata meta = JsonUtility.FromJson<ColliderMetadata>(imp.userData);
                    if (meta != null)
                        list.Add((path, meta, imp.userData));
                }
                catch { /* ignore */ }
            }
            return list;
        }

        private static HashSet<string> GetReferencedAssetGuids()
        {
            var referenced = new HashSet<string>();
            string[] referencerGuids = AssetDatabase.FindAssets("t:Prefab t:Scene t:ScriptableObject");
            int total = referencerGuids?.Length ?? 0;
            try
            {
                for (int i = 0; i < total; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("LeanHull Cleanup", $"Scanning references... {i + 1}/{total}", (float)(i + 1) / total))
                        break;
                    string refPath = AssetDatabase.GUIDToAssetPath(referencerGuids[i]);
                    if (string.IsNullOrEmpty(refPath)) continue;
                    string[] dependencies = AssetDatabase.GetDependencies(refPath, false);
                    foreach (string depPath in dependencies)
                    {
                        string depGuid = AssetDatabase.AssetPathToGUID(depPath);
                        if (!string.IsNullOrEmpty(depGuid)) referenced.Add(depGuid);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return referenced;
        }

        private bool IsAssetUsedElsewhere(Mesh colliderMesh, string currentPrefabPath)
        {
            if (colliderMesh == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(colliderMesh);
            if (string.IsNullOrEmpty(assetPath)) return false;
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) return false;
            string[] referencerGuids = AssetDatabase.FindAssets("t:Prefab t:Scene t:ScriptableObject");
            foreach (string refGuid in referencerGuids)
            {
                string refPath = AssetDatabase.GUIDToAssetPath(refGuid);
                if (refPath == currentPrefabPath) continue;
                string[] dependencies = AssetDatabase.GetDependencies(refPath, false);
                foreach (string depPath in dependencies)
                {
                    if (AssetDatabase.AssetPathToGUID(depPath) == guid)
                        return true;
                }
            }
            return false;
        }

        private bool IsPersistentIdValid(string userData)
        {
            if (string.IsNullOrEmpty(userData)) return false;
            try
            {
                ColliderMetadata metadata = JsonUtility.FromJson<ColliderMetadata>(userData);
                if (metadata == null || string.IsNullOrEmpty(metadata.prefabGuid)) return false;
                string path = AssetDatabase.GUIDToAssetPath(metadata.prefabGuid);
                if (string.IsNullOrEmpty(path)) return false;
                if (long.TryParse(metadata.localId, out long localId))
                {
                    Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
                    foreach (var o in all)
                    {
                        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string g, out long l))
                        {
                            if (g == metadata.prefabGuid && l == localId) return true;
                        }
                    }
                    return false;
                }
                return true;
            }
            catch { return false; }
        }

        private bool HasGeneratedColliders()
        {
            if (!AssetDatabase.IsValidFolder(generatedCollidersPath)) return false;
            string[] guids = AssetDatabase.FindAssets("LeanHull_ t:Mesh", new[] { generatedCollidersPath });
            return guids != null && guids.Length > 0;
        }

        private void CleanupOrphanedColliders()
        {
            if (!AssetDatabase.IsValidFolder(generatedCollidersPath))
            {
                Debug.Log("No generated colliders found.");
                return;
            }
            var allMetadata = GetAllLeanHullMeshMetadata(generatedCollidersPath);
            HashSet<string> referencedGuids = GetReferencedAssetGuids();
            int deleteCount = 0;
            foreach (var (path, metadata, userData) in allMetadata)
            {
                if (metadata == null || string.IsNullOrEmpty(metadata.prefabGuid)) continue;
                string guid = AssetDatabase.AssetPathToGUID(path);
                bool sourceExists = IsPersistentIdValid(userData);
                bool beingUsed = referencedGuids.Contains(guid);
                bool shouldDelete = metadata.isPrefab ? !beingUsed : (!beingUsed && !sourceExists);
                if (shouldDelete)
                {
                    AssetDatabase.DeleteAsset(path);
                    deleteCount++;
                }
            }
            int prefabSettingsDeleted = LeanHullPrefabSettings.CleanupOrphanedPrefabSettings();
            if (deleteCount > 0 || prefabSettingsDeleted > 0)
            {
                AssetDatabase.Refresh();
                if (deleteCount > 0) Debug.Log($"LeanHull: Deleted {deleteCount} orphaned collider assets.");
                if (prefabSettingsDeleted > 0) Debug.Log($"LeanHull: Deleted {prefabSettingsDeleted} orphaned prefab settings.");
            }
            else
            {
                Debug.Log("LeanHull: No orphaned colliders found.");
            }
        }

        private void FindStaleColliders()
        {
            if (!AssetDatabase.IsValidFolder(generatedCollidersPath))
            {
                Debug.Log("LeanHull: No generated colliders folder.");
                return;
            }
            var all = GetAllLeanHullMeshMetadata(generatedCollidersPath);
            HashSet<string> staleSourcePaths = new HashSet<string>();
            foreach (var (_, meta, _) in all)
            {
                if (meta == null || string.IsNullOrEmpty(meta.prefabGuid)) continue;
                string sourcePath = AssetDatabase.GUIDToAssetPath(meta.prefabGuid);
                string fullSourcePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), sourcePath);
                if (string.IsNullOrEmpty(sourcePath) || !System.IO.File.Exists(fullSourcePath)) continue;
                string currentHash = ComputeCurrentSourceHash(meta, sourcePath);
                if (currentHash == null) continue;
                string storedHash = meta.sourceContentHash ?? "";
                if (string.IsNullOrEmpty(storedHash) || storedHash != currentHash)
                    staleSourcePaths.Add(sourcePath);
            }
            if (staleSourcePaths.Count == 0)
            {
                Debug.Log("LeanHull: No colliders with changed sources found.");
                return;
            }
            List<Object> sourceAssets = new List<Object>();
            foreach (string sourcePath in staleSourcePaths)
            {
                Object main = AssetDatabase.LoadMainAssetAtPath(sourcePath);
                if (main != null) sourceAssets.Add(main);
            }
            if (sourceAssets.Count == 0)
            {
                Debug.Log("LeanHull: No colliders with changed sources found.");
                return;
            }
            Selection.objects = sourceAssets.ToArray();
            EditorGUIUtility.PingObject(sourceAssets[0]);
            if (EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType().Name.Contains("Project"))
                EditorWindow.focusedWindow.Repaint();
            else
                EditorApplication.ExecuteMenuItem("Window/General/Project");
            Debug.Log($"LeanHull: Selected {sourceAssets.Count} source prefab(s)/model(s) that have changed. Re-apply from LeanHull to update their colliders.");
        }

        private static string ComputeCurrentSourceHash(ColliderMetadata meta, string sourcePath)
        {
            bool isMeshAsset = sourcePath.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase) &&
                              AssetDatabase.GetMainAssetTypeAtPath(sourcePath) == typeof(Mesh);
            if (isMeshAsset)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(sourcePath);
                return mesh != null && mesh.vertexCount >= 4 ? LeanHullHashing.ComputeSourceContentHashSingle(mesh) : null;
            }
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (root == null) return null;
            if (meta.mergeMeshes)
                return LeanHullHashing.ComputeSourceContentHashMerged(root, sourcePath);
            GameObject go = FindGameObjectByLocalId(root, meta.localId);
            if (go == null) return null;
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount < 4) return null;
            return LeanHullHashing.ComputeSourceContentHashSingle(mf.sharedMesh);
        }

        private static string ComputeCurrentSourceHashMeshOnly(ColliderMetadata meta, string sourcePath)
        {
            bool isMeshAsset = sourcePath.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase) &&
                              AssetDatabase.GetMainAssetTypeAtPath(sourcePath) == typeof(Mesh);
            if (isMeshAsset)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(sourcePath);
                return mesh != null && mesh.vertexCount >= 4 ? LeanHullHashing.ComputeSourceContentHashSingle(mesh) : null;
            }
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (root == null) return null;
            if (meta.mergeMeshes)
                return LeanHullHashing.ComputeSourceContentHashMeshOnlyMerged(root, sourcePath);
            GameObject go = FindGameObjectByLocalId(root, meta.localId);
            if (go == null) return null;
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount < 4) return null;
            return LeanHullHashing.ComputeSourceContentHashSingle(mf.sharedMesh);
        }

        private static string ComputeCurrentSourceStructureHash(ColliderMetadata meta, string sourcePath)
        {
            if (!meta.mergeMeshes) return null;
            if (sourcePath.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase) && AssetDatabase.GetMainAssetTypeAtPath(sourcePath) == typeof(Mesh))
                return null;
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            return root != null ? LeanHullHashing.ComputeSourceStructureHashMerged(root, sourcePath) : null;
        }

        private string GetStaleChangeDescription(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath) || !AssetDatabase.IsValidFolder(generatedCollidersPath))
                return null;
            string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(prefabGuid)) return null;
            string fullSourcePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), prefabPath);
            if (!System.IO.File.Exists(fullSourcePath)) return null;
            var all = GetAllLeanHullMeshMetadata(generatedCollidersPath);
            int separatedCountTotal = 0;
            foreach (var (_, m, _) in all)
            {
                if (m != null && m.prefabGuid == prefabGuid) { if (!m.mergeMeshes) separatedCountTotal++; }
            }
            bool effectiveSeparated = separatedCountTotal > 0;
            if (effectiveSeparated && prefabPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (root != null)
                {
                    int includedCount = 0;
                    foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (LeanHullMeshQueries.IsMeshEligibleForHull(mf, prefabPath, root))
                            includedCount++;
                    }
                    int separatedCountInUse = 0;
                    foreach (var (_, m, _) in all)
                    {
                        if (m == null || m.prefabGuid != prefabGuid || m.mergeMeshes) continue;
                        GameObject go = FindGameObjectByLocalId(root, m.localId);
                        if (go == null) continue;
                        bool hasEligible = false;
                        foreach (MeshFilter mf in go.GetComponents<MeshFilter>())
                        {
                            if (LeanHullMeshQueries.IsMeshEligibleForHull(mf, prefabPath, root)) { hasEligible = true; break; }
                        }
                        if (hasEligible) separatedCountInUse++;
                    }
                    if (includedCount != separatedCountInUse)
                        return "Prefab changed";
                }
            }
            bool meshChanged = false;
            bool prefabChanged = false;
            bool genericStale = false;
            foreach (var (_, meta, _) in all)
            {
                if (meta == null || meta.prefabGuid != prefabGuid) continue;
                if (effectiveSeparated && meta.mergeMeshes) continue;
                string currentFull = ComputeCurrentSourceHash(meta, prefabPath);
                if (currentFull == null) continue;
                string storedFull = meta.sourceContentHash ?? "";
                if (string.IsNullOrEmpty(storedFull) || storedFull == currentFull) continue;
                string storedStructure = meta.sourceStructureHash ?? "";
                if (!string.IsNullOrEmpty(storedStructure) && meta.mergeMeshes)
                {
                    string currentStructure = ComputeCurrentSourceStructureHash(meta, prefabPath);
                    if (currentStructure != null && storedStructure != currentStructure)
                    {
                        prefabChanged = true;
                        continue;
                    }
                }
                string storedMeshOnly = meta.sourceMeshOnlyHash ?? "";
                if (string.IsNullOrEmpty(storedMeshOnly))
                {
                    genericStale = true;
                    continue;
                }
                string currentMeshOnly = ComputeCurrentSourceHashMeshOnly(meta, prefabPath);
                if (currentMeshOnly == null) continue;
                if (storedMeshOnly != currentMeshOnly)
                    meshChanged = true;
                else
                    prefabChanged = true;
            }
            if (prefabChanged)
                return "Prefab changed";
            if (meshChanged)
                return "Source mesh changed";
            if (genericStale)
                return "Prefab changed";
            return null;
        }

        private static GameObject FindGameObjectByLocalId(GameObject root, string localId)
        {
            if (root == null || string.IsNullOrEmpty(localId)) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(t.gameObject, out string _, out long lid))
                {
                    if (lid.ToString() == localId) return t.gameObject;
                }
            }
            return null;
        }

        private bool RemoveOrphanCollidersForPrefab(string prefabPath, GameObject prefabRoot, ref bool modified, List<string> orphanHullPathsToCheck)
        {
            if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase) || prefabRoot == null)
                return false;
            if (orphanHullPathsToCheck == null) return false;
            if (!AssetDatabase.IsValidFolder(generatedCollidersPath)) return false;
            string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(prefabGuid)) return false;
            var all = GetAllLeanHullMeshMetadata(generatedCollidersPath);
            List<string> thisPrefabOrphanPaths = new List<string>();
            foreach (var (path, meta, _) in all)
            {
                if (meta == null || meta.prefabGuid != prefabGuid || meta.mergeMeshes) continue;
                GameObject go = FindGameObjectByLocalId(prefabRoot, meta.localId);
                bool isOrphan = (go == null);
                if (go != null)
                {
                    bool hasEligibleMesh = false;
                    foreach (MeshFilter mf in go.GetComponents<MeshFilter>())
                    {
                        if (LeanHullMeshQueries.IsMeshEligibleForHull(mf, prefabPath, prefabRoot)) { hasEligibleMesh = true; break; }
                    }
                    isOrphan = !hasEligibleMesh;
                }
                if (!isOrphan) continue;
                if (!orphanHullPathsToCheck.Contains(path))
                    orphanHullPathsToCheck.Add(path);
                thisPrefabOrphanPaths.Add(path);
            }
            if (thisPrefabOrphanPaths.Count > 0)
            {
                List<Mesh> orphanMeshes = new List<Mesh>();
                foreach (string path in thisPrefabOrphanPaths)
                {
                    Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (m != null) orphanMeshes.Add(m);
                }
                foreach (MeshCollider mc in prefabRoot.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (mc.sharedMesh != null && orphanMeshes.Contains(mc.sharedMesh))
                    {
                        DestroyImmediate(mc, true);
                        modified = true;
                    }
                }
            }
            return thisPrefabOrphanPaths.Count > 0;
        }
    }
}
