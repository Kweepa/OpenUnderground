// ==============================================================================
//  LeanHull - Size optimized Convex Hull Generator
//  Copyright (c) 2026 Kweepa
//  All rights reserved.
//
//  This script is part of the LeanHull tool suite.
//  Do not distribute or share this code without explicit permission.
// ==============================================================================

using UnityEngine;
using System.Collections.Generic;

namespace Kweepa.LeanHull
{
    // Content/structure hashing for change detection and batch selection.
    public static class LeanHullHashing
    {
        private const int vertexHashSampleDivisor = 100;

        private static long HashTransform(long h, Transform t)
        {
            h = h * 31 + t.localPosition.GetHashCode();
            h = h * 31 + t.localRotation.GetHashCode();
            h = h * 31 + t.localScale.GetHashCode();
            return h;
        }

        private static long HashMeshCounts(long h, int vertexCount, int triangleLength)
        {
            h = h * 31 + vertexCount;
            h = h * 31 + triangleLength;
            return h;
        }

        private static long HashVerts(long h, Vector3[] verts, int step)
        {
            for (int i = 0; i < verts.Length; i += step)
            {
                Vector3 v = verts[i];
                h = h * 31 + v.x.GetHashCode();
                h = h * 31 + v.y.GetHashCode();
                h = h * 31 + v.z.GetHashCode();
            }
            return h;
        }

        private static long HashVertsInRootSpace(long h, Transform root, MeshFilter mf)
        {
            Vector3[] verts = mf.sharedMesh.vertices;
            int step = Mathf.Max(1, verts.Length / vertexHashSampleDivisor);
            Transform t = mf.transform;
            for (int i = 0; i < verts.Length; i += step)
            {
                Vector3 v = root.InverseTransformPoint(t.TransformPoint(verts[i]));
                h = h * 31 + v.x.GetHashCode();
                h = h * 31 + v.y.GetHashCode();
                h = h * 31 + v.z.GetHashCode();
            }
            return h;
        }

        public static long CalculateMeshHash(GameObject root, bool includeVertices, Mesh filterMesh = null)
        {
            long hash = 17;
            MeshFilter[] all = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in all)
            {
                if (mf.sharedMesh == null) continue;
                if (filterMesh != null && mf.sharedMesh != filterMesh) continue;
                hash = HashMeshCounts(hash, mf.sharedMesh.vertexCount, mf.sharedMesh.triangles.Length);
                hash = HashTransform(hash, mf.transform);
                if (includeVertices)
                {
                    Vector3[] verts = mf.sharedMesh.vertices;
                    int step = Mathf.Max(1, verts.Length / vertexHashSampleDivisor);
                    hash = HashVerts(hash, verts, step);
                }
            }
            return hash;
        }

        private static List<MeshFilter> getMeshFiltersForHash(GameObject root, string prefabPath, PreviewItem item)
        {
            List<MeshFilter> mfs = (item != null && item.filterMesh != null)
                ? LeanHullMeshQueries.GetMeshFiltersForItem(root, item, prefabPath ?? "", false)
                : new List<MeshFilter>(root.GetComponentsInChildren<MeshFilter>(true));

            return mfs;
        }

        // Content hash for merged hull source (all meshes under root, or only item.filterMesh when item has mesh scope). Used to detect when source has changed.
        public static string ComputeSourceContentHashMerged(GameObject root, string prefabPath = null, PreviewItem item = null)
        {
            if (root == null) return "";
            long h = 17;
            List<MeshFilter> mfs = getMeshFiltersForHash(root, prefabPath, item);
            h = h * 31 + mfs.Count;
            Transform rootT = root.transform;
            foreach (var mf in mfs)
            {
                if ((item == null || item.filterMesh == null) && !LeanHullMeshQueries.IsMeshEligibleForHull(mf, prefabPath, root)) continue;
                h = HashMeshCounts(h, mf.sharedMesh.vertexCount, mf.sharedMesh.triangles.Length);
                h = HashTransform(h, mf.transform);
                h = HashVertsInRootSpace(h, rootT, mf);
            }
            return h.ToString("X16");
        }

        // Content hash for a single mesh (separated hull source). Used to detect when source has changed. Also used as mesh-only hash (no transforms).
        public static string ComputeSourceContentHashSingle(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount < 4) return "";
            long h = 17;
            h = HashMeshCounts(h, mesh.vertexCount, mesh.triangles.Length);
            Vector3[] verts = mesh.vertices;
            int step = Mathf.Max(1, verts.Length / vertexHashSampleDivisor);
            h = HashVerts(h, verts, step);
            return h.ToString("X16");
        }

        // Structure hash for merged hull (mesh count + transforms only). Used to detect prefab structure changes vs mesh geometry changes. When item has filterMesh, only that mesh is included.
        public static string ComputeSourceStructureHashMerged(GameObject root, string prefabPath = null, PreviewItem item = null)
        {
            if (root == null) return "";
            long h = 17;
            List<MeshFilter> mfs = getMeshFiltersForHash(root, prefabPath, item);
            h = h * 31 + mfs.Count;
            foreach (var mf in mfs)
            {
                if ((item == null || item.filterMesh == null) && !LeanHullMeshQueries.IsMeshEligibleForHull(mf, prefabPath, root)) continue;
                h = HashTransform(h, mf.transform);
            }
            return h.ToString("X16");
        }

        // Mesh-only hash for merged hull source (geometry in local space, no transforms). Used to distinguish transform vs mesh changes. When item has filterMesh, only that mesh is included.
        public static string ComputeSourceContentHashMeshOnlyMerged(GameObject root, string prefabPath = null, PreviewItem item = null)
        {
            if (root == null) return "";
            long h = 17;
            List<MeshFilter> mfs = getMeshFiltersForHash(root, prefabPath, item);
            h = h * 31 + mfs.Count;
            foreach (var mf in mfs)
            {
                if ((item == null || item.filterMesh == null) && !LeanHullMeshQueries.IsMeshEligibleForHull(mf, prefabPath, root)) continue;
                h = HashMeshCounts(h, mf.sharedMesh.vertexCount, mf.sharedMesh.triangles.Length);
                Vector3[] verts = mf.sharedMesh.vertices;
                int step = Mathf.Max(1, verts.Length / vertexHashSampleDivisor);
                h = HashVerts(h, verts, step);
            }
            return h.ToString("X16");
        }
    }
}
