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
    // Shared constants for default values used by persisted and preview data.
    public static class LeanHullConstants
    {
        public const float initialSplitPosition = 0.666f;
        public const float minTileSize = 160.0f;
        public const float maxTileSize = 512.0f;
    }

    // Whether to merge meshes or keep separated for hull generation (per-item override).
    public enum MergeOverride
    {
        None,
        Merged,
        Separated
    }

    [System.Serializable]
    public class PersistedData
    {
        public int batchTargetVerts = 16;
        public bool mergeMeshes;
        public float previewSize = LeanHullConstants.minTileSize;
    }

    [System.Serializable]
    public class ColliderMetadata
    {
        public string prefabGuid;
        public string localId;
        public bool isPrefab;
        public string sourceContentHash;
        public string sourceMeshOnlyHash;
        public string sourceStructureHash;
        public bool mergeMeshes;
        public int targetVertices = 16;
    }

    // Sidecar JSON stored in PrefabSettings/{prefabname}_{shortHash(guid)}.json. Uses prefabGuid in JSON to identify the prefab.
    [System.Serializable]
    public class LeanHullPrefabSettingsData
    {
        public string prefabGuid;
        public List<string> excludeIds = new ();
    }

    //One row in the hull-contributor hierarchy: either a GameObject (exclude self and children) or a MeshFilter (exclude this mesh).
    public class HullContributorEntry
    {
        public bool isGameObject;   // true = GO row, false = mesh row
        public string displayName; // GO name or mesh name
        public int depth;          // indent level (0 = root)
        public string excludeId;   // persistent ID for sidecar (GO id or MeshFilter id)
        public bool isExcluded;    // from prefab sidecar
        public int hullIndex;      // only for mesh rows: which hull; -1 if excluded or N/A
    }

    // One item in the batch preview grid (prefab/model/mesh asset).
    public class PreviewItem
    {
        public string prefabPath;
        public string name;
        // When non-null, only MeshFilters with sharedMesh == this mesh are used (mesh-scoped selection from FBX/model).
        public Mesh filterMesh;
        public Bounds bounds;
        public Texture icon;

        public readonly List<Mesh> renderMeshes = new ();
        public readonly List<Matrix4x4> renderMatrices = new ();
        public readonly List<Vector3> renderScales = new ();
        public readonly List<Material[]> renderMaterials = new ();

        public readonly List<Mesh> hullMeshes = new ();
        public readonly List<Mesh> originalHullMeshes = new ();
        public readonly List<Matrix4x4> hullMatrices = new ();

        // For prefabs: objects that contribute to hull(s). Populated during render pass. Used by hierarchy panel.
        public readonly List<HullContributorEntry> hullContributors = new ();

        public Texture2D tex;
        public int currentVerts;
        public int optimizedVerts;
        public int targetVertOverride;
        public int validMeshCount;
        public MergeOverride mergeOverride = MergeOverride.None;
        public long meshHash;
        public long deepMeshHash;
        public bool isDirty = true;
        public bool previewTextureDirty = true;
        public float splitPosition = LeanHullConstants.initialSplitPosition;
        public string staleChangeDescription;
        // Which hull index is highlighted in yellow in the preview (-1 = none).
        public int highlightedHullIndex = -1;

        public void Cleanup(bool destroyMeshes = true)
        {
            if (tex != null) Object.DestroyImmediate(tex);
            tex = null;
            previewTextureDirty = true;
            if (destroyMeshes)
            {
                foreach (var m in hullMeshes) if (m != null) Object.DestroyImmediate(m);
                hullMeshes.Clear();
                foreach (var m in originalHullMeshes) if (m != null) Object.DestroyImmediate(m);
                originalHullMeshes.Clear();
                hullMatrices.Clear();
            }
        }
    }
}
