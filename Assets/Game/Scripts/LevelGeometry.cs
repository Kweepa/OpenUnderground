using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class LevelGeometry : LevelObject
{
    /// <summary>
    /// Wall Frob - Defines wall rendering parameters for a specific wall direction/orientation.
    /// 
    /// Used during level geometry generation to determine which walls should be rendered and how.
    /// Each WallFrob represents one possible wall face (cardinal directions: N/S/E/W, or diagonal walls).
    /// 
    /// The system works by checking each tile against all WallFrobs to see if a wall should be rendered
    /// on that tile's edge. A wall is rendered if:
    /// 1. The tile type matches the typeMask (bitwise AND check)
    /// 2. The neighboring tile (at xoff, yoff) has a higher floor height than the current tile
    /// 
    /// Corner indexing convention (used by h[], nh[], v[] arrays):
    /// - Index 0 = SW (South-West) corner
    /// - Index 1 = SE (South-East) corner
    /// - Index 2 = NW (North-West) corner
    /// - Index 3 = NE (North-East) corner
    /// </summary>
    private class WallFrob
    {
        /// <param name="m">typeMask - Bitmask that filters which tile types this wall applies to</param>
        /// <param name="x">xoff - X offset to check the neighboring tile (-1, 0, or 1)</param>
        /// <param name="y">yoff - Y offset to check the neighboring tile (-1, 0, or 1)</param>
        /// <param name="_ln">ln - Left neighbor corner index: index into neighbor's height array for the left side of the wall</param>
        /// <param name="_rn">rn - Right neighbor corner index: index into neighbor's height array for the right side of the wall</param>
        /// <param name="_lt">lt - Left tile corner index: index into current tile's height array for the left side of the wall</param>
        /// <param name="_rt">rt - Right tile corner index: index into current tile's height array for the right side of the wall</param>
        public WallFrob(int m, int x, int y, int _ln, int _rn, int _lt, int _rt)
        {
            typeMask = m;
            xoff = x;
            yoff = y;
            ln = _ln;
            rn = _rn;
            lt = _lt;
            rt = _rt;
        }

        /// <summary>
        /// Bitmask that determines which tile types this wall configuration applies to.
        /// The check is: ((1 << tile.type) & typeMask) != 0
        /// 
        /// Common patterns:
        /// - 1023 = all bits set (0b1111111111) = matches all tile types 0-9
        /// - 1023 - (1 << n) = matches all types except type n
        /// - (1 << n) = matches only type n
        /// 
        /// Tile types:
        /// - 0 = solid/void (no walls rendered)
        /// - 1 = open (full square)
        /// - 2 = diagonal open to SE
        /// - 3 = diagonal open to SW
        /// - 4 = diagonal open to NW
        /// - 5 = diagonal open to NE
        /// - 6-9 = slopes
        /// </summary>
        public readonly int typeMask;

        /// <summary>
        /// X offset to the neighboring tile to check for height differences.
        /// -1 = check tile to the west, 0 = no X offset, 1 = check tile to the east
        /// Used with yoff to determine which adjacent tile to compare heights against.
        /// </summary>
        public readonly int xoff;

        /// <summary>
        /// Y offset to the neighboring tile to check for height differences.
        /// 1 = check tile to the north, 0 = no Y offset, -1 = check tile to the south
        /// Used with xoff to determine which adjacent tile to compare heights against.
        /// </summary>
        public readonly int yoff;

        /// <summary>
        /// Left neighbor corner index - Index into the neighbor tile's height array (nh[]) 
        /// for the left side of the wall (from the wall's perspective).
        /// Used to get the floor height at the left edge of the neighboring tile.
        /// </summary>
        public readonly int ln;

        /// <summary>
        /// Right neighbor corner index - Index into the neighbor tile's height array (nh[])
        /// for the right side of the wall (from the wall's perspective).
        /// Used to get the floor height at the right edge of the neighboring tile.
        /// </summary>
        public readonly int rn;

        /// <summary>
        /// Left tile corner index - Index into the current tile's height array (h[])
        /// for the left side of the wall (from the wall's perspective).
        /// Used to get the floor height at the left edge of the current tile.
        /// </summary>
        public readonly int lt;

        /// <summary>
        /// Right tile corner index - Index into the current tile's height array (h[])
        /// for the right side of the wall (from the wall's perspective).
        /// Used to get the floor height at the right edge of the current tile.
        /// </summary>
        public readonly int rt;
    }

    /// <summary>
    /// Array of WallFrob configurations for all possible wall orientations.
    /// 
    /// Index mapping:
    /// - 0: West wall (facing west, checking east neighbor)
    /// - 1: East wall (facing east, checking west neighbor)
    /// - 2: North wall (facing north, checking south neighbor)
    /// - 3: South wall (facing south, checking north neighbor)
    /// - 4: Diagonal SW wall (for diagonal tiles)
    /// - 5: Diagonal SE wall (for diagonal tiles)
    /// - 6: Diagonal NW wall (for diagonal tiles)
    /// - 7: Diagonal NE wall (for diagonal tiles)
    /// 
    /// Note: Diagonal walls use xoff=-64 as a special marker (not a valid tile offset).
    /// This is likely used to distinguish diagonal walls from regular walls in the rendering logic.
    /// </summary>
    private static readonly WallFrob[] frobs =
    {
        // West wall: matches all types except 2 (diagonal open to SE) and 4 (diagonal open to NE)
        // Checks neighbor to the west (x-1), uses corners NW(3) and SW(1) from neighbor, NW(2) and SW(0) from current
        new (1023 - (1 << 2) - (1 << 4), -1, 0, 3, 1, 2, 0), // west wall
        
        // East wall: matches all types except 3 (diagonal open to SW) and 5 (diagonal open to NW)
        // Checks neighbor to the east (x+1), uses corners SW(0) and NW(2) from neighbor, SE(1) and NE(3) from current
        new (1023 - (1 << 3) - (1 << 5), 1, 0, 0, 2, 1, 3), // east wall
        
        // South wall: matches all types except 4 (diagonal open to NE) and 5 (diagonal open to NW)
        // Checks neighbor to the south (y-1), uses corners NW(2) and NE(3) from neighbor, SW(0) and SE(1) from current
        new (1023 - (1 << 4) - (1 << 5), 0, -1, 2, 3, 0, 1), // north wall
        
        // North wall: matches all types except 2 (diagonal open to SE) and 3 (diagonal open to SW)
        // Checks neighbor to the north (y+1), uses corners SE(1) and SW(0) from neighbor, NE(3) and NW(2) from current
        new (1023 - (1 << 2) - (1 << 3), 0, 1, 1, 0, 3, 2), // south wall
        
        // Diagonal NW wall: only matches type 2 (diagonal open to SE tile)
        // Uses xoff=-64 as special marker, corners NE(3) and SW(0) from both neighbor and current
        new ((1 << 2), -64, 0, 3, 0, 3, 0), // diagonal sw
        
        // Diagonal NE wall: only matches type 3 (diagonal open to SW tile)
        // Uses xoff=-64 as special marker, corners SE(1) and NW(2) from both neighbor and current
        new ((1 << 3), -64, 0, 1, 2, 1, 2), // diagonal se
        
        // Diagonal SW wall: only matches type 4 (diagonal open to NE tile)
        // Uses xoff=-64 as special marker, corners NW(2) and SE(1) from both neighbor and current
        new ((1 << 4), -64, 0, 2, 1, 2, 1), // diagonal nw
        
        // Diagonal SE wall: only matches type 5 (diagonal open to NW tile)
        // Uses xoff=-64 as special marker, corners SW(0) and NE(3) from both neighbor and current
        new ((1 << 5), -64, 0, 0, 3, 0, 3), // diagonal ne
    };

    public static LevelGeometry CreateLevelGeometry(Stream levStream, List<Tile> movableTiles)
    {
        Level level = LevelLoader.GetLevel();
        LevelLoader loader = LevelLoader.sLevelLoader;
        int loadedLevel = loader.loadedLevel;
        
        GameObject go = new GameObject($"Level{loadedLevel}Geo");
        LevelGeometry geo = go.AddComponent<LevelGeometry>();

        // texture list
        levStream.Seek(loader.chunkOffsets[18 + loadedLevel - 1]);
        level.walls = levStream.GetUShortArray(48);
        level.floors = levStream.GetUShortArray(10);
        level.doors = levStream.GetByteArray(6);

        go.layer = LayerMask.NameToLayer("Environment");
        MeshFilter meshFilter = go.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = go.AddComponent<MeshCollider>();

        Mesh mesh = meshFilter.mesh = new Mesh();

        mesh.subMeshCount = 48 + 10; // unique wall + floor materials

        // create the level mesh

        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<List<int>> tris = new List<List<int>>(mesh.subMeshCount);
        for (int c = 0; c < tris.Capacity; ++c)
        {
            tris.Add(new List<int>());
        }
        
        // Separate list for ceiling-only triangles (ceiling geometry uses same material as floor texture 9)
        List<int> ceilingOnlyTris = new List<int>();

        Material[] mats = new Material[58];
        for (int c = 0; c < 48; ++c)
        {
            mats[c] = loader.wallMat[level.walls[c]];
        }

        for (int c = 0; c < 10; ++c)
        {
            mats[48 + c] = loader.floorMat[level.floors[c]];
        }

        if (loadedLevel == 7)
        {
            Shader shader = Shader.Find("Standard");

            Material mazePathMaterial = new Material(shader);
            mazePathMaterial.mainTexture = loader.floorMat[14].mainTexture; // rough grey
            mazePathMaterial.SetFloat("_Glossiness", 0.2f);
            mats[48 + 4] = LevelLoader.sLevelLoader.mazePathMaterial = mazePathMaterial;
        }

        meshRenderer.materials = mats;
        meshRenderer.lightProbeUsage = 0;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        Vector2 uv0 = new Vector2(0, 1);
        Vector2 uv1 = new Vector2(1, 1);
        Vector2 uv2 = new Vector2(0, 0);
        Vector2 uv3 = new Vector2(1, 0);

        int[] h = new int[4];
        int[] nh = new int[4];

        for (int y = 0; y < 64; ++y)
        {
            for (int x = 0; x < 64; ++x)
            {
                Tile t = level.tiles[x, y];

                int floorTexture = t.floorTexture;
                {
                    Vector3 v0 = LevelLoader.xzScale * (new Vector3(x, 0, y));
                    Vector3 v1 = LevelLoader.xzScale * (new Vector3(x + 1, 0, y));
                    Vector3 v2 = LevelLoader.xzScale * (new Vector3(x, 0, y + 1));
                    Vector3 v3 = LevelLoader.xzScale * (new Vector3(x + 1, 0, y + 1));

                    if (movableTiles.Contains(t))
                    {
                        h[0] = 0;
                        h[1] = 0;
                        h[2] = 0;
                        h[3] = 0;
                    }
                    else
                    {
                        loader.GetFloorHeights(h, x, y, 0, 0, movableTiles);
                    }

                    v0.y = LevelLoader.yScale * h[0];
                    v1.y = LevelLoader.yScale * h[1];
                    v2.y = LevelLoader.yScale * h[2];
                    v3.y = LevelLoader.yScale * h[3];

                    Vector3 v4 = v0;
                    Vector3 v5 = v1;
                    Vector3 v6 = v2;
                    Vector3 v7 = v3;
                    v4.y = LevelLoader.yScale * 16;
                    v5.y = LevelLoader.yScale * 16;
                    v6.y = LevelLoader.yScale * 16;
                    v7.y = LevelLoader.yScale * 16;

                    int vi = verts.Count;
                    
                    // floor
                    if (t.floorHeight != 15)
                    {
                        verts.Add(v0);
                        verts.Add(v1);
                        verts.Add(v2);
                        verts.Add(v3);
                        uvs.Add(uv0);
                        uvs.Add(uv1);
                        uvs.Add(uv2);
                        uvs.Add(uv3);
                    }

                    int vc = verts.Count;
                    int vcf = verts.Count;
                    if (loadedLevel != 9)
                    {
                        // ceiling
                        if (t.floorHeight != 15)
                        {
                            verts.Add(v4);
                            verts.Add(v5);
                            verts.Add(v6);
                            verts.Add(v7);
                            uvs.Add(uv0);
                            uvs.Add(uv1);
                            uvs.Add(uv2);
                            uvs.Add(uv3);
                        }

                        // for the upward facing ceiling tiles (in the blank spaces, for roaming sight)
                        vcf = verts.Count;
                        verts.Add(v4);
                        verts.Add(v5);
                        verts.Add(v6);
                        verts.Add(v7);
                        uvs.Add(uv0);
                        uvs.Add(uv1);
                        uvs.Add(uv2);
                        uvs.Add(uv3);
                    }

                    List<int> subMeshTris = tris[48 + floorTexture];
                    // Note: tris[48 + 9] is for floor triangles with floorTexture == 9
                    // ceilingOnlyTris is for actual ceiling geometry (separate from floor)

                    if (t.floorHeight != 15)
                    {
                        switch (t.type)
                        {
                        case 1: // open
                        case 6:
                        case 7:
                        case 8:
                        case 9:
                            subMeshTris.Add(vi);
                            subMeshTris.Add(vi + 2);
                            subMeshTris.Add(vi + 1);
                            subMeshTris.Add(vi + 1);
                            subMeshTris.Add(vi + 2);
                            subMeshTris.Add(vi + 3);
                            break;
                        case 2:
                            // diagonal se
                            subMeshTris.Add(vi);
                            subMeshTris.Add(vi + 3);
                            subMeshTris.Add(vi + 1);
                            break;
                        case 3:
                            // diagonal sw
                            subMeshTris.Add(vi);
                            subMeshTris.Add(vi + 2);
                            subMeshTris.Add(vi + 1);
                            break;
                        case 4:
                            // diagonal ne
                            subMeshTris.Add(vi + 1);
                            subMeshTris.Add(vi + 2);
                            subMeshTris.Add(vi + 3);
                            break;
                        case 5:
                            // diagonal nw
                            subMeshTris.Add(vi);
                            subMeshTris.Add(vi + 2);
                            subMeshTris.Add(vi + 3);
                            break;
                        }
                    }

                    if (t.floorHeight != 15 && loadedLevel != 9)
                    {
                        switch (t.type)
                        {
                        case 1: // open
                        case 6:
                        case 7:
                        case 8:
                        case 9:
                            ceilingOnlyTris.Add(vc);
                            ceilingOnlyTris.Add(vc + 1);
                            ceilingOnlyTris.Add(vc + 2);
                            ceilingOnlyTris.Add(vc + 2);
                            ceilingOnlyTris.Add(vc + 1);
                            ceilingOnlyTris.Add(vc + 3);
                            break;
                        case 2:
                            // diagonal se
                            ceilingOnlyTris.Add(vc);
                            ceilingOnlyTris.Add(vc + 1);
                            ceilingOnlyTris.Add(vc + 3);
                            // above the world
                            ceilingOnlyTris.Add(vcf + 0);
                            ceilingOnlyTris.Add(vcf + 2);
                            ceilingOnlyTris.Add(vcf + 3);
                            break;
                        case 3:
                            // diagonal sw
                            ceilingOnlyTris.Add(vc);
                            ceilingOnlyTris.Add(vc + 1);
                            ceilingOnlyTris.Add(vc + 2);
                            // above the world
                            ceilingOnlyTris.Add(vcf + 1);
                            ceilingOnlyTris.Add(vcf + 2);
                            ceilingOnlyTris.Add(vcf + 3);
                            break;
                        case 4:
                            // diagonal ne
                            ceilingOnlyTris.Add(vc + 1);
                            ceilingOnlyTris.Add(vc + 3);
                            ceilingOnlyTris.Add(vc + 2);
                            // above the world
                            ceilingOnlyTris.Add(vcf + 0);
                            ceilingOnlyTris.Add(vcf + 2);
                            ceilingOnlyTris.Add(vcf + 1);
                            break;
                        case 5:
                            // diagonal nw
                            ceilingOnlyTris.Add(vc);
                            ceilingOnlyTris.Add(vc + 3);
                            ceilingOnlyTris.Add(vc + 2);
                            // above the world
                            ceilingOnlyTris.Add(vcf + 0);
                            ceilingOnlyTris.Add(vcf + 3);
                            ceilingOnlyTris.Add(vcf + 1);
                            break;
                        }
                    }
                    if (loadedLevel != 9)
                    {
                        if (t.type == 0 || t.floorHeight == 15)
                        {
                            ceilingOnlyTris.Add(vcf + 0);
                            ceilingOnlyTris.Add(vcf + 2);
                            ceilingOnlyTris.Add(vcf + 1);
                            ceilingOnlyTris.Add(vcf + 1);
                            ceilingOnlyTris.Add(vcf + 2);
                            ceilingOnlyTris.Add(vcf + 3);
                        }
                    }

                    // now add walls
                    if (t.type != 0 && t.floorHeight != 15)
                    {
                        Vector3[] v = new Vector3[4];
                        v[0] = v0;
                        v[1] = v1;
                        v[2] = v2;
                        v[3] = v3;
                        foreach (WallFrob f in frobs)
                        {
                            LevelLoader.sLevelLoader.GetFloorHeights(nh, x + f.xoff, y + f.yoff, f.xoff, f.yoff, movableTiles);

                            if (((1 << t.type) & f.typeMask) != 0 && (nh[f.ln] > h[f.lt] || nh[f.rn] > h[f.rt]))
                            {
                                Vector2 uvw0 = new Vector2(1, 1.0f - LevelLoader.texScale * (16 - nh[f.ln]));
                                Vector2 uvw1 = new Vector2(0, 1.0f - LevelLoader.texScale * (16 - nh[f.rn]));
                                Vector2 uvw2 = new Vector2(1, 1.0f - LevelLoader.texScale * (16 - h[f.lt]));
                                Vector2 uvw3 = new Vector2(0, 1.0f - LevelLoader.texScale * (16 - h[f.rt]));
                                Vector3 wv0 = v[f.lt], wv1 = v[f.rt], wv2 = v[f.lt], wv3 = v[f.rt];
                                wv0.y = LevelLoader.yScale * nh[f.ln];
                                wv1.y = LevelLoader.yScale * nh[f.rn];
                                wv2.y = LevelLoader.yScale * h[f.lt];
                                wv3.y = LevelLoader.yScale * h[f.rt];
                                
                                // Check for a decal on this wall and adjust geometry to make space for it.
                                if (t.decal != 0)
                                {
                                    Decal.EDecal decalType = Decal.GetDecalType(level.walls[t.decalType]);
                                    if (decalType != Decal.EDecal.Wall)
                                    {
                                        bool isDecalWall = (f.xoff == 1 && t.decalAngle == 2) || // East
                                            (f.yoff == -1 && t.decalAngle == 4) || // North
                                            (f.xoff == -1 && t.decalAngle == 6) || // West
                                            (f.yoff == 1 && t.decalAngle == 0);   // South

                                        if (isDecalWall)
                                        {
                                            float floorHeight = t.decalHeight * LevelLoader.yScale;
                                            
                                            if (decalType == Decal.EDecal.Drain)
                                            {
                                                // find the water group
                                                int waterGroup = 48;
                                                for (int i = 0; i < 10; ++i)
                                                {
                                                    if (level.floors[i] == 16)
                                                    {
                                                        waterGroup = 48 + i;
                                                        break;
                                                    }
                                                }
                                                // Create a new floor quad in the neighboring tile to continue the water flow visually.
                                                List<int> drainFloorTris = tris[waterGroup];
                                                int drain_vi = verts.Count;
                                                
                                                // Define a tile-sized offset vector pointing into the neighboring tile.
                                                Vector3 tileOffset = Vector3.zero;
                                                switch (t.decalAngle)
                                                {
                                                    case 2: tileOffset = new Vector3(LevelLoader.xzScale, 0, 0); break;  // East
                                                    case 4: tileOffset = new Vector3(0, 0, -LevelLoader.xzScale); break; // North
                                                    case 6: tileOffset = new Vector3(-LevelLoader.xzScale, 0, 0); break; // West
                                                    case 0: tileOffset = new Vector3(0, 0, LevelLoader.xzScale); break;  // South
                                                }

                                                // Create four new vertices for the drain floor by offsetting the current tile's base vertices.
                                                // Ensure the new floor's Y-position matches the current tile's floor.
                                                verts.Add(new Vector3(v0.x + tileOffset.x, floorHeight, v0.z + tileOffset.z));
                                                verts.Add(new Vector3(v1.x + tileOffset.x, floorHeight, v1.z + tileOffset.z));
                                                verts.Add(new Vector3(v2.x + tileOffset.x, floorHeight, v2.z + tileOffset.z));
                                                verts.Add(new Vector3(v3.x + tileOffset.x, floorHeight, v3.z + tileOffset.z));
                                                
                                                // Add standard floor UVs for the new quad.
                                                uvs.AddRange(new []{uv0, uv1, uv2, uv3});
                                                
                                                // Add triangles for the new floor quad.
                                                drainFloorTris.Add(drain_vi + 0); // v0
                                                drainFloorTris.Add(drain_vi + 2); // v2
                                                drainFloorTris.Add(drain_vi + 1); // v1
                                                drainFloorTris.Add(drain_vi + 1); // v1
                                                drainFloorTris.Add(drain_vi + 2); // v2
                                                drainFloorTris.Add(drain_vi + 3); // v3
                                            }
                                            
                                            // Raise the bottom of the wall segment by 4 units to fit the decal.
                                            wv2.y = floorHeight + LevelLoader.yScale * 4.0f;
                                            wv3.y = floorHeight + LevelLoader.yScale * 4.0f;
                                            
                                            // Adjust the bottom V coordinates to match the new geometry height,
                                            // effectively starting the texture above the cutout.
                                            // A height of 4 units corresponds to a full texture height (4 * texScale = 1.0f).
                                            uvw2.y += 4.0f * LevelLoader.texScale;
                                            uvw3.y += 4.0f * LevelLoader.texScale;
                                        }
                                    }
                                }

                                vi = verts.Count;

                                int wallTexture = t.wallTexture;
                                // because I map the walls a little differently, under the cup of wonder's plinth
                                // can see a partial grating. replace that with some vine covered wall.
                                if (loadedLevel == 3 && t.x == 26 && t.y == 44 && f.ln == 2) // north facing wall only
                                {
                                    ++wallTexture;
                                }
                                subMeshTris = tris[wallTexture];
                                verts.Add(wv0);
                                verts.Add(wv1);
                                verts.Add(wv2);
                                verts.Add(wv3);
                                uvs.Add(uvw0);
                                uvs.Add(uvw1);
                                uvs.Add(uvw2);
                                uvs.Add(uvw3);
                                subMeshTris.Add(vi);
                                subMeshTris.Add(vi + 2);
                                subMeshTris.Add(vi + 1);
                                subMeshTris.Add(vi + 1);
                                subMeshTris.Add(vi + 2);
                                subMeshTris.Add(vi + 3);
                            }
                        }
                    }
                }
            }
        }

        // lower 48 are walls, upper 10 are floors/ceiling 
        int[] submeshCount = new int[58];
        
        Vector3[] vertices = verts.ToArray();
        Vector2[] uvArray = uvs.ToArray();
        
        // Build main mesh with all submeshes (including floor submesh 48+9 for floor geometry)
        mesh.vertices = vertices;
        mesh.uv = uvArray;
        for (int c = 0; c < tris.Count; ++c)
        {
            submeshCount[c] = tris[c].Count;
            mesh.SetTriangles(tris[c].ToArray(), c);
        }

        meshRenderer.materials = mats;

        mesh.RecalculateNormals();
        mesh.Optimize();
        
        // Set collision for main mesh (all floor+walls, including floor triangles with texture 9)
        List<int> floorWallsTris = new List<int>();
        for (int c = 0; c < tris.Count; ++c)
        {
            floorWallsTris.AddRange(tris[c]);
        }
        Mesh meshFloorWalls = new Mesh();
        meshFloorWalls.vertices = vertices;
        meshFloorWalls.SetTriangles(floorWallsTris, 0);
        meshFloorWalls.RecalculateBounds();
        meshCollider.sharedMesh = meshFloorWalls;

        // Create separate ceiling GameObject for rendering and collision (walk/crawl/creep critters exclude it)
        // Uses ceilingOnlyTris (actual ceiling geometry), not tris[48+9] (which includes floor triangles)
        if (loadedLevel != 9 && ceilingOnlyTris.Count > 0)
        {
            GameObject ceilingGo = new GameObject("Ceiling");
            ceilingGo.transform.SetParent(go.transform, false);
            int ceilingLayer = LayerMask.NameToLayer("Ceiling");
            if (ceilingLayer >= 0 && ceilingLayer <= 31)
                ceilingGo.layer = ceilingLayer;
            
            // Ceiling rendering - uses same material as floor texture 9
            MeshFilter ceilingMeshFilter = ceilingGo.AddComponent<MeshFilter>();
            MeshRenderer ceilingMeshRenderer = ceilingGo.AddComponent<MeshRenderer>();
            Mesh meshCeiling = new Mesh();
            meshCeiling.vertices = vertices;
            meshCeiling.uv = uvArray;
            meshCeiling.SetTriangles(ceilingOnlyTris, 0);
            meshCeiling.RecalculateNormals();
            meshCeiling.RecalculateBounds();
            ceilingMeshFilter.mesh = meshCeiling;
            ceilingMeshRenderer.material = mats[48 + 9]; // Same material as floor texture 9
            ceilingMeshRenderer.lightProbeUsage = 0;
            ceilingMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ceilingMeshRenderer.receiveShadows = false;
            
            // Ceiling collision
            MeshCollider ceilingCollider = ceilingGo.AddComponent<MeshCollider>();
            ceilingCollider.sharedMesh = meshCeiling;
        }

        // Validate submesh counts
        for (int i = 0; i < mesh.subMeshCount; ++i)
        {
            if (mesh.GetSubMesh(i).indexCount != submeshCount[i])
            {
                Debug.Log($"Error: Submesh {i} count mismatch");
            }
        }

        return geo;
    }
}
