# LeanHull

**LeanHull** is a low poly convex hull generator. It creates ultra-low poly collision meshes from high poly visual models.

---

## Contents

- [1. Workflow](#1-workflow)
- [2. Asset storage](#2-asset-storage)
- [3. Find Stale and Cleanup](#3-find-stale-and-cleanup)
- [4. Algorithms used](#4-algorithms-used)
- [5. Where things are stored](#5-where-things-are-stored)

---

## 1. Workflow

1. **Set output folder (optional):** In **Edit → Project Settings → LeanHull**, set where generated hull `.asset` files are saved. The default is **Assets/Game/LeanHull**. Change it first if you want assets to go somewhere else, so they don’t appear in an unexpected location.
2. **Select Assets:** Choose prefabs, imported models, and raw meshes in the Project view.
3. **Global Settings:** Adjust the **Vertex Count** (target count per hull) and **Merge** (combine sub-meshes) in the top toolbar. Hold **Shift** while clicking arrows to step by 8 vertices instead of 1.
4. **Individual Overrides:** Use the ◀ ▶ arrows on any tile to tweak its specific vertex count (also supports **Shift-click**). Use the **Merge**/**Separate** icon to toggle merging for that item only.
5. **Which meshes get colliders:** Click the **Pencil icon** on a prefab tile to open the **Hull contributors** panel. Click the icon next to a GameObject or mesh to **exclude** it from hull generation (or click again to include it). Excluding a GameObject excludes it and all its children. Your choices are saved per-prefab and survive reimport.
6. **Clearing Overrides:** When an item has custom settings, a **Padlock icon** appears on its tile. Click the padlock to reset that object to the global defaults.
7. **Preview:** Rotate thumbnails with the left mouse button. Move the vertical divider to compare the original hull (right) with the optimized result (left). Hold **Ctrl + Scroll** to quickly resize the preview grid.
8. **Apply:** Click **Apply** to generate and attach the colliders. This process is automatic and handles object renames. **Note:** Undo is not supported for asset modification; please ensure you have a backup or commit your changes to source control before applying.

---

## 2. Asset storage

- **External Assets:** All generated hulls are saved as `.asset` files in a project-configurable folder. The default is **Assets/Game/LeanHull**. You can change it in **Edit → Project Settings → LeanHull** (or **Project Settings → LeanHull**); the value is stored in **ProjectSettings/LeanHullSettings.json** and is shared with the team via version control.
- **Why not subassets?** LeanHull uses external assets to ensure stable visualization in the Unity Editor. This prevents the common Unity bug where collision wireframes disappear during prefab autosaves.

---

## 3. Find Stale and Cleanup

- **Find Stale:** Click **Find stale** in the bottom bar to select prefabs, models, or meshes that use generated colliders but whose source geometry has changed (e.g. after reimport). They appear in the grid with their previous settings restored; click **Apply** to regenerate the colliders.
- **Manual Cleanup:** Click the **Sweep button** on the toolbar to automatically find and delete orphaned colliders. Unreferenced prefab colliders are scrubbed; model colliders are only removed if both unreferenced and the source file is missing. Orphaned prefab-setting sidecar files (whose prefab no longer exists) are also removed.

---

## 4. Algorithms used

- **QuickHull** (Barber, Dobkin, Huhdanpaa, 1996): Builds an initial tetrahedron from extreme points, then iteratively adds the furthest outside point by extending the hull. Points very close to the hull are treated as coplanar and not added, so flat facets stay low-poly.
- **Grid decimation:** Before QuickHull, vertices are reduced by snapping to a 3D grid (binary search for cell size to hit roughly the target count). Keeps QuickHull fast; quality is fine from a few hundred samples.
- **QEM mesh simplification** (Garland & Heckbert, 1997): Quadric Error Metrics edge collapse. Each vertex gets a quadric (sum of squared distances to adjacent planes); edges are collapsed in order of lowest error. Used to reduce the hull down to the target vertex count while keeping shape and volume stable. For hulls, border detection is skipped and QuickHull is only run once at the start.
- **Hashing:** Content and structure hashes (mesh geometry, transforms, vertex samples) are used to detect when a source has changed so the tool can mark items stale or avoid redundant work.
- **Negative scale:** Meshes with negative scale are reflected (vertices/normals flipped on the flipped axes) and drawn with an adjusted matrix so the preview and lighting match the convex hull.

---

## 5. Where things are stored

- **Project settings (LeanHull):** **ProjectSettings/LeanHullSettings.json** — stores the **generated assets folder** (Assets-relative path where hull meshes are saved). Editable via **Edit → Project Settings → LeanHull**. Version-controlled; shared across the team.
- **User preferences (EditorPrefs):** Vertex count, Merge toggle, and preview zoom are stored in Unity's user preferences (EditorPrefs) so they persist per machine but are not in the project.
- **Generated hull meshes:** One `.asset` per generated collider mesh, in the folder set in project settings (default **Assets/Game/LeanHull**). Each mesh stores metadata (prefab link, source hashes, target vertex count, merge flag) for change detection and cleanup.
- **Prefab exclude list:** **{generatedAssetsFolder}/PrefabSettings** — sidecar JSON per prefab, named `{prefabname}_{shortHash(guid)}.json`. Holds the list of persistent IDs (GameObjects or MeshFilters) excluded from hull generation for that prefab. Exclude choices survive reimport because they’re keyed by prefab GUID and local IDs.

