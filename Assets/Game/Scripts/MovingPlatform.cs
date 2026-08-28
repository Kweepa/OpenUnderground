using UnityEngine;

[System.Serializable]
public class MovingPlatformSaveData : LevelObjectSaveData
{
    public int currentHeight;
    public int originalHeight;
    public int floorTextureIndex;
    public int[] wallTextureIndices = new int[4]; // N, E, S, W
}

public class MovingPlatform : LevelObject
{
    public float speed = 1.0f;
    public int h;
    private int originalHeight;
    private bool set;
    private bool moving;
    
    private int floorTextureIndex;
    private int[] wallTextureIndices = new int[4];
    
    // Static mesh data - shared across all moving platforms
    private static readonly Vector3[] vertices =
    {
        // floor
        new(0, 0, 0),
        new(3, 0, 0),
        new(0, 0, 3),
        new(3, 0, 3),

        // sides (N, E, S, W)
        new(0, 0, 0),
        new(0, -12, 0),
        new(3, 0, 0),
        new(3, -12, 0),

        new(3, 0, 0),
        new(3, -12, 0),
        new(3, 0, 3),
        new(3, -12, 3),

        new(3, 0, 3),
        new(3, -12, 3),
        new(0, 0, 3),
        new(0, -12, 3),

        new(0, 0, 3),
        new(0, -12, 3),
        new(0, 0, 0),
        new(0, -12, 0),
    };
    
    private static readonly Vector2[] uvs =
    {
        new Vector2(0, 1),
        new Vector2(1, 1),
        new Vector2(0, 0),
        new Vector2(1, 0),

        new Vector2(0, 4),
        new Vector2(0, 0),
        new Vector2(1, 4),
        new Vector2(1, 0),

        new Vector2(1, 4),
        new Vector2(1, 0),
        new Vector2(2, 4),
        new Vector2(2, 0),

        new Vector2(2, 4),
        new Vector2(2, 0),
        new Vector2(3, 4),
        new Vector2(3, 0),

        new Vector2(3, 4),
        new Vector2(3, 0),
        new Vector2(4, 4),
        new Vector2(4, 0),
    };
    
    private static readonly int[] floorTris = { 0, 2, 1, 1, 2, 3 };
    private static readonly int[][] wallTris =
    {
        new[] { 4, 7, 5, 7, 4, 6 },
        new[] { 8, 11, 9, 11, 8, 10 },
        new[] { 12, 15, 13, 15, 12, 14 },
        new[] { 16, 19, 17, 19, 16, 18 }
    };

    private void CheckSet()
    {
        if (!set)
        {
            Vector3 pos = transform.position;
            h = (int)(pos.y / LevelLoader.yScale + 0.5f);
            originalHeight = h;
            set = true;
        }
    }

    public void Update()
    {
        CheckSet();

        if (moving)
        {
            // platform
            Vector3 pos = transform.position;
            float targetY = h * LevelLoader.yScale;
// There are a lot of places where you can see the platform moving and it doesn't look good :(
// TODO: mark the spots where it makes sense to have the platform move rather than snap.
// For example, the bullfrog puzzle, the room with the four switches on level 4, the stairway to Korianous's
// grave.
#if false
            pos.y = Mathf.MoveTowards(pos.y, targetY, Time.deltaTime * speed);
#else
            pos.y = targetY;
#endif
            transform.position = pos;
            if (pos.y == targetY)
            {
                moving = false;
            }
        }

        if (initialTile.door != null && initialTile.door.type == EObjectType.SecretDoor)
        {
            // check which side of the door the player is and if the floor height is greater than the door height
            // if we're on the door side and the floor is high, hide us, otherwise show us
            bool hidden = false;
            if (transform.position.y > initialTile.door.transform.position.y + 0.1f)
            {
                Vector3 doorRelativePos = initialTile.door.transform.position - initialTile.GetCenter();
                Vector3 playerRelativePos = PlayerObject.Player.mainCamera.transform.position - initialTile.GetCenter();
                if (Vector3.Dot(doorRelativePos, playerRelativePos) > 0.0f)
                {
                    hidden = true;
                }
            }

            MeshRenderer r = GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.enabled = !hidden;
            }
        }
    }

    // To get to garamon's gravestone on level 1
    public void Advance()
    {
        CheckSet();

        if (++h > 8)
        {
            h = 1;
        }

        moving = true;
    }

    // For the bullfrog puzzle on level 4
    public void Move(int dir)
    {
        CheckSet();

        h = Mathf.Clamp(h + dir, 1, 12);
        moving = true;
    }

    // for some random stuff
    public void SetHeight(int targetHeight)
    {
        CheckSet();

        h = targetHeight;
        moving = true;
        gameObject.SetActive(true);
    }

    public void Reset()
    {
        CheckSet();

        h = originalHeight;
        moving = true;
        gameObject.SetActive(true);
    }

    public void SetMaterials(Tile t, int wallMat, int floorMat)
    {
        LevelLoader.sLevelLoader.SetMaterialsOnMovingPlatform(t, this, wallMat, floorMat);
    }
    
    // Update material indices for save/load
    // Pass -1 for indices that shouldn't be updated
    public void UpdateMaterialIndices(int floorMatIndex, int wallMatIndex)
    {
        if (floorMatIndex >= 0)
        {
            floorTextureIndex = floorMatIndex;
        }
        if (wallMatIndex >= 0)
        {
            // All 4 wall sides use the same material index
            for (int i = 0; i < 4; i++)
            {
                wallTextureIndices[i] = wallMatIndex;
            }
        }
    }

    public void LookedAt(Vector3 normal)
    {
        // depending on normal, check texture of this initialTile or one of the neighbours
        Tile t = LevelLoader.GetTile(initialTile.GetCenter() + LevelLoader.xzScale * normal);
        if (t != null)
        {
            // Read from this moving platform's own level, not the currently loaded level
            Level level;
            if (levelIndex >= 1 && levelIndex < LevelLoader.sLevelLoader.levels.Length 
                && LevelLoader.sLevelLoader.levels[levelIndex] != null)
            {
                level = LevelLoader.sLevelLoader.levels[levelIndex];
            }
            else
            {
                // Fallback to current level if levelIndex is invalid (shouldn't happen normally)
                level = LevelLoader.GetLevel();
            }
            
            if (t == initialTile)
            {
                // identify the top
                int floorIndex = level.floors[t.floorTexture];
                Messages.Add($"You see {StringLoader.GetString(10, 510 - floorIndex)}.");
            }
            else
            {
                // identify the side
                int wallIndex = level.walls[t.wallTexture];
                Messages.Add($"You see {StringLoader.GetString(10, wallIndex)}.");
            }
        }
    }
    
    public void CreateMesh(int floorTexIndex, int[] wallTexIndices)
    {
        // Store material indices for save/load
        this.floorTextureIndex = floorTexIndex;
        this.wallTextureIndices = wallTexIndices;
        
        // Add components
        MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = gameObject.AddComponent<MeshCollider>();
        
        // Create mesh using static arrays
        Mesh mesh = new Mesh { vertices = vertices, uv = uvs, subMeshCount = 5 };
        mesh.SetTriangles(floorTris, 0);
        mesh.SetTriangles(wallTris[0], 1);
        mesh.SetTriangles(wallTris[1], 2);
        mesh.SetTriangles(wallTris[2], 3);
        mesh.SetTriangles(wallTris[3], 4);
        mesh.RecalculateNormals();
        mesh.Optimize();
        
        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;
        meshCollider.convex = true;
        meshRenderer.lightProbeUsage = 0;
        
        // Apply materials
        Material[] mats = new Material[5];
        mats[0] = LevelLoader.sLevelLoader.GetFloorMat(floorTexIndex);
        for (int i = 0; i < 4; i++)
        {
            mats[i + 1] = LevelLoader.sLevelLoader.wallMat[wallTexIndices[i]];
        }
        meshRenderer.materials = mats;
    }
    
    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);
        
        if (data is MovingPlatformSaveData mpData)
        {
            CheckSet(); // Ensure h/originalHeight are initialized
            mpData.currentHeight = h;
            mpData.originalHeight = originalHeight;
            mpData.floorTextureIndex = floorTextureIndex;
            mpData.wallTextureIndices = wallTextureIndices;
        }
    }

    private Level GetPlatformLevel()
    {
        if (levelIndex >= 1 && levelIndex < LevelLoader.sLevelLoader.levels.Length
            && LevelLoader.sLevelLoader.levels[levelIndex] != null)
        {
            return LevelLoader.sLevelLoader.levels[levelIndex];
        }

        return LevelLoader.GetLevel();
    }

    private void SyncInitialTileFloorTexture()
    {
        if (initialTile == null)
        {
            return;
        }

        Level level = GetPlatformLevel();
        if (level == null || level.floors == null)
        {
            return;
        }

        if (initialTile.floorTexture < level.floors.Length
            && level.floors[initialTile.floorTexture] == floorTextureIndex)
        {
            return;
        }

        for (int i = 0; i < level.floors.Length; i++)
        {
            if (level.floors[i] == floorTextureIndex)
            {
                initialTile.floorTexture = i;
                return;
            }
        }
    }
    
    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        
        if (data is MovingPlatformSaveData mpData)
        {
            h = mpData.currentHeight;
            originalHeight = mpData.originalHeight;
            floorTextureIndex = mpData.floorTextureIndex;
            wallTextureIndices = mpData.wallTextureIndices;
            set = true;
            
            // Update position to match the saved height
            // The base class sets position from data.position, but we need to ensure
            // it matches the saved h value, which is the source of truth for moving platforms
            Vector3 pos = transform.position;
            pos.y = h * LevelLoader.yScale;
            transform.position = pos;
            
            // Clear moving flag since we're snapping to position
            moving = false;

            SyncInitialTileFloorTexture();
        }
    }
    
    public void LoadFromSaveData(MovingPlatformSaveData data)
    {
        RestoreFromSaveData(data);
    }
    
    public override ObjectSaveData SaveToData()
    {
        MovingPlatformSaveData data = new MovingPlatformSaveData();
        PopulateSaveData(data);
        
        return new ObjectSaveData
        {
            objectTypeName = "MovingPlatform",
            objectType = -1,
            objectIndex = 0,
            level = levelIndex,
            inWorldObj = false,
            jsonData = JsonUtility.ToJson(data)
        };
    }
}
