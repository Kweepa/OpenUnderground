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
    // Mesh eligibility, hierarchy paths, and exclude resolution for prefabs.
    public static class LeanHullMeshQueries
    {
        // Returns true if the GameObject has a TextMeshPro text component.
        // (TMP uses a MeshFilter for the text mesh; we skip those from hull generation.)
        public static bool IsTextMeshProObject(GameObject go)
        {
            if (go == null) return false;
            Component[] comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                string name = comps[i].GetType().Name;
                if (name.Contains("TextMeshPro") || name == "TMP_Text") return true;
            }
            return false;
        }

        // Returns true if this MeshFilter could contribute to a hull (valid mesh, not TMP, not LeanHull_, >=4 verts).
        // Does not consider exclude list.
        public static bool IsMeshPotentialContributor(MeshFilter mf)
        {
            if (mf == null || mf.sharedMesh == null) return false;
            if (mf.sharedMesh.name.StartsWith("LeanHull_")) return false;
            if (mf.sharedMesh.vertexCount < 4) return false;
            if (IsTextMeshProObject(mf.gameObject)) return false;
            return true;
        }

        // Returns true if this MeshFilter should be used for hull generation (valid mesh, not excluded, not TMP).
        // When requireMinVertices is true, also requires vertexCount >= 4 and not a LeanHull-generated mesh.
        public static bool IsMeshEligibleForHull(MeshFilter mf, string prefabPath, GameObject prefabRoot, bool requireMinVertices = true)
        {
            if (mf == null || mf.sharedMesh == null) return false;
            if (mf.sharedMesh.name.StartsWith("LeanHull_")) return false;
            if (requireMinVertices && mf.sharedMesh.vertexCount < 4) return false;
            if (!string.IsNullOrEmpty(prefabPath) && prefabRoot != null && IsMeshExcluded(mf, prefabPath, prefabRoot)) return false;
            if (IsTextMeshProObject(mf.gameObject)) return false;
            return true;
        }

        // Returns all MeshFilters that should be drawn in the preview (includes disabled/excluded).
        // Used so visual meshes are always shown regardless of hull exclude state.
        public static void GetMeshFiltersForPreviewDisplay(GameObject prefabRoot, PreviewItem item, HashSet<MeshFilter> outSet)
        {
            outSet.Clear();
            MeshFilter[] all = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter mf in all)
            {
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name.StartsWith("LeanHull_")) continue;
                if (item != null && item.filterMesh != null && mf.sharedMesh != item.filterMesh) continue;
                outSet.Add(mf);
            }
        }

        // Returns the MeshFilters to use for hull generation for this item.
        // When item is not null and item.filterMesh is set (mesh-scoped selection from FBX/model),
        // only MeshFilters with that sharedMesh are returned; otherwise all eligible MeshFilters under the root.
        public static List<MeshFilter> GetMeshFiltersForItem(GameObject prefabRoot, PreviewItem item, string prefabPath, bool requireMinVertices = true)
        {
            MeshFilter[] all = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
            var result = new List<MeshFilter>();
            foreach (MeshFilter mf in all)
            {
                if (item != null && item.filterMesh != null && mf.sharedMesh != item.filterMesh) continue;
                if (IsMeshEligibleForHull(mf, prefabPath, prefabRoot, requireMinVertices))
                    result.Add(mf);
            }
            return result;
        }

        // Collects all vertices from the given MeshFilters into prefab root local space. Used for merged hull generation.
        public static List<Vector3> GetMergedVerticesInPrefabSpace(GameObject prefabRoot, List<MeshFilter> mfs)
        {
            var allVertices = new List<Vector3>();
            Transform rootT = prefabRoot.transform;
            foreach (MeshFilter mf in mfs)
            {
                Vector3[] verts = mf.sharedMesh.vertices;
                Transform t = mf.transform;
                foreach (Vector3 vert in verts)
                    allVertices.Add(rootT.InverseTransformPoint(t.TransformPoint(vert)));
            }
            return allVertices;
        }

        // Returns true if this MeshFilter (or its GameObject, or any ancestor GameObject) is marked as excluded from hull in the prefab's metadata.
        // Resolves IDs from the prefab asset so they match the IDs stored by the context menu.
        // Excluded GameObjects implicitly exclude their children.
        public static bool IsMeshExcluded(MeshFilter mf, string prefabPath, GameObject prefabRoot)
        {
            if (mf == null || string.IsNullOrEmpty(prefabPath) || prefabRoot == null) return false;
            HashSet<string> exclude = LeanHullPrefabSettings.GetExcludeIdsForPrefab(prefabPath);
            if (exclude.Count == 0) return false;
            if (!prefabPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) return false;
            string relPath = GetRelativePathStatic(mf.transform, prefabRoot.transform);
            GameObject assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (assetRoot == null) return false;
            Transform assetT = string.IsNullOrEmpty(relPath) ? assetRoot.transform : FindTransformByPathIncludingInactive(assetRoot.transform, relPath);
            if (assetT == null) return false;
            for (Transform t = assetT; t != null; t = t.parent)
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(t.gameObject, out _, out long goId) && exclude.Contains(goId.ToString())) return true;
                if (t == assetRoot.transform) break;
            }
            MeshFilter[] currentMfs = mf.gameObject.GetComponents<MeshFilter>();
            MeshFilter[] assetMfs = assetT.gameObject.GetComponents<MeshFilter>();
            int matchIndex = -1;
            for (int i = 0; i < currentMfs.Length; i++)
            {
                if (currentMfs[i] == mf) { matchIndex = i; break; }
            }
            if (matchIndex >= 0 && matchIndex < assetMfs.Length && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(assetMfs[matchIndex], out _, out long mfId) && exclude.Contains(mfId.ToString())) return true;
            return false;
        }

        public static string GetRelativePathStatic(Transform t, Transform root)
        {
            if (t == root) return "";
            string path = t.name;
            Transform current = t;
            while (current.parent != null && current.parent != root)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }
            return path;
        }

        // Find descendant by path (e.g. "Parent/Child"); uses GetChild so inactive GameObjects are found (Transform.Find skips them).
        public static Transform FindTransformByPathIncludingInactive(Transform root, string relPath)
        {
            if (root == null || string.IsNullOrEmpty(relPath)) return root;
            string[] parts = relPath.Split('/');
            Transform current = root;
            for (int i = 0; i < parts.Length && current != null; i++)
            {
                string name = parts[i];
                Transform next = null;
                for (int c = 0; c < current.childCount; c++)
                {
                    Transform ch = current.GetChild(c);
                    if (ch.name == name) { next = ch; break; }
                }
                current = next;
            }
            return current;
        }

        public static int GetTransformDepth(Transform t, Transform root)
        {
            int depth = 0;
            for (Transform p = t.parent; p != null && p != root; p = p.parent)
                depth++;
            return depth;
        }

        // Gets the persistent ID for exclude list.
        // Prefer instance ID when we have the loaded prefab (works for disabled GOs); fall back to asset path lookup.
        public static bool TryGetExcludeIdForMenu(Object target, string prefabPath, GameObject stageRoot, out string id)
        {
            id = null;
            if (target == null || string.IsNullOrEmpty(prefabPath)) return false;
            GameObject go = target is MeshFilter mf ? mf.gameObject : target as GameObject;
            if (go == null) return false;
            if (stageRoot != null && (go == stageRoot || go.transform.IsChildOf(stageRoot.transform)))
            {
                // Use instance's persistent ID directly so disabled GameObjects work (asset path lookup can fail for them)
                if (target is MeshFilter meshFilter)
                {
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(meshFilter, out _, out long lid))
                    {
                        id = lid.ToString();
                        return true;
                    }
                }
                else if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(go, out _, out long lid))
                {
                    id = lid.ToString();
                    return true;
                }
            }
            if (stageRoot != null)
            {
                string relPath = GetRelativePathStatic(go.transform, stageRoot.transform);
                GameObject assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (assetRoot == null) return false;
                Transform assetT = string.IsNullOrEmpty(relPath) ? assetRoot.transform : FindTransformByPathIncludingInactive(assetRoot.transform, relPath);
                if (assetT == null) return false;
                if (target is MeshFilter)
                {
                    MeshFilter[] stageMfs = go.GetComponents<MeshFilter>();
                    MeshFilter[] assetMfs = assetT.gameObject.GetComponents<MeshFilter>();
                    int matchIndex = -1;
                    for (int i = 0; i < stageMfs.Length; i++)
                    {
                        if (stageMfs[i] == target) { matchIndex = i; break; }
                    }
                    if (matchIndex >= 0 && matchIndex < assetMfs.Length && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(assetMfs[matchIndex], out _, out long lid))
                    {
                        id = lid.ToString();
                        return true;
                    }
                }
                else if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(assetT.gameObject, out _, out long lid))
                {
                    id = lid.ToString();
                    return true;
                }
                return false;
            }
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out _, out long localId)) { id = localId.ToString(); return true; }
            
            return false;
        }
    }
}
