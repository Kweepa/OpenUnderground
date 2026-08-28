using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

public class ObservableLinkedList
{
    public bool locked;
    private LinkedList<LevelObject> list = new();

    private void CheckLock()
    {
        if (locked)
        {
            Debug.Log("break");
        }
    }

    public void AddLast(LevelObject o)
    {
        CheckLock();
        list.AddLast(o);
    }

    public void AddFirst(LevelObject o)
    {
        CheckLock();
        list.AddFirst(o);
    }

    public bool Remove(LevelObject o)
    {
        CheckLock();
        return list.Remove(o);
    }

    public void Remove(LinkedListNode<LevelObject> node)
    {
        list.Remove(node);
    }
    
    public IEnumerator<LevelObject> GetEnumerator()
    {
        locked = true;
        foreach (var item in list)
        {
            yield return item;
        }
        locked = false;
    }

    public LinkedListNode<LevelObject> First => list.First;

    public bool Contains(LevelObject o)
    {
        return list.Contains(o);
    }
}

public class Level
{
    public Tile[,] tiles = new Tile[64, 64];
    public ushort[] walls = new ushort[48];
    public ushort[] floors = new ushort[10];
    public byte[] doors = new byte[6];

    public PVSManager pvs = new();

    // stuff to save
    public UUObject[] objects = new UUObject[1024];

    public ObservableLinkedList worldObj = new();
    //public LinkedList<LevelObject> worldObj = new();

    public LevelGeometry geo;
}

public class LevelLoader : MonoBehaviour
{
    public static LevelLoader sLevelLoader;

    public UUObject sprite;
    public UUObject critter;
    public GameObject lavaLight;
    public Splat splat;
    public MovingPlatform movableTile;
    public UUObject shelf;

    private List<GameObject> lavaLights = new List<GameObject>();

    public const float xzScale = 3.0f;
    public const float yScale = 3.0f / 4.0f;
    public const float texScale = 1.0f / 4.0f;

    public Material[] wallMat;
    public Material[] floorMat;
    public Material[] objMat;
    private Material[] tmObjMat;

    private Dictionary<EObjectType, int> usedObjects = new();

    // 1-indexed
    public Level[] levels = new Level[10];

    public ObjectModelMapping objectModelMap;

    // 1-indexed
    public int loadedLevel;

    public double levelLoadedTime;

    private Stream levStream;
    private int chunkCount;
    public int[] chunkOffsets;

    public static ObservableLinkedList worldObj => GetLevel().worldObj;
    //public static LinkedList<LevelObject> worldObj => GetLevel().worldObj;

    public Material GetObjectMaterial(EObjectType type)
    {
        return objMat[(int)type];
    }

    public Material GetTmObjectMaterial(int index)
    {
        return tmObjMat[index];
    }

    public Material GetFloorMat(int index)
    {
        return floorMat[index];
    }

    private void Awake()
    {
        // Singleton pattern - destroy duplicate LevelLoader instances
        if (sLevelLoader != null && sLevelLoader != this)
        {
            Debug.LogWarning("Duplicate LevelLoader detected and destroyed");
            Destroy(gameObject);
            return;
        }
        
        sLevelLoader = this;
    }

    private int GetObjectType(int o)
    {
        // first 256 are critters with 8+19 data elements
        int offset = (o < 256) ? 27 * o : 27 * 256 + 8 * (o - 256);
        levStream.Seek(chunkOffsets[loadedLevel - 1] + 0x4000 + offset);

        ushort[] objData = levStream.GetUShortArray(4);

        ushort s0 = objData[0];
        return s0 & 511;
    }

    public static UUObject CreateObjectOfType(EObjectType type)
    {
        // unlink it
        ushort[] objData = { (ushort)((int)type | (1 << 15)), 0, 40, 0 };
        
        UUObject obj = sLevelLoader.CreateObjectOfType(objData, null);

        return obj;
    }

    // public solely for Summon Monster
    public UUObject CreateObjectOfType(ushort[] objData, byte[] critterData)
    {
        ushort s0 = objData[0];
        EObjectType type = (EObjectType)(s0 & 511);

        usedObjects.TryAdd(type, 0);
        ++usedObjects[type];

        UUObject.EClass objClass = UUObject.GetClass(type);

        if (objClass is >= UUObject.EClass.CrittersA and <= UUObject.EClass.CrittersD && critterData == null)
        {
            return null;
        }
        
        UUObject obj;
        if (objectModelMap != null && objectModelMap.objects[(int)type] != null)
        {
            GameObject oo = Instantiate(objectModelMap.objects[(int)type]);

            foreach (MeshRenderer r in oo.GetComponentsInChildren<MeshRenderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            oo.transform.rotation = Quaternion.AngleAxis(Random.Range(0.0f, 360.0f), Vector3.up);

            if (critterData != null && (int)type >= 64 && (int)type < 128)
            {
                obj = oo.GetComponent<Critter>();
                if (obj == null)
                {
                    obj = oo.AddComponent<Critter>();
                }
            }
            else
            {
                MeshFilter objMeshFilter = oo.GetComponentInChildren<MeshFilter>();
                if (objMeshFilter != null)
                {
                    Collider existingCollider = oo.GetComponentInChildren<Collider>();
                    if (oo.layer != LayerMask.NameToLayer("Ignore Raycast")
                        && (existingCollider == null || !existingCollider.enabled))
                    {
                        MeshCollider meshCollider = objMeshFilter.gameObject.AddComponent<MeshCollider>();
                        meshCollider.convex = true;
                        meshCollider.cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning
                            | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.CookForFasterSimulation;
                        meshCollider.sharedMesh = objMeshFilter.sharedMesh;
                    }

                    if (!objMeshFilter.mesh.isReadable && oo.GetComponentInChildren<MeshCollider>())
                    {
                        Debug.Log($"Mesh for {oo.name} is not readable. Can't create convex collider!");
                    }
                }

                obj = oo.GetComponent<UUObject>();
                if (obj == null)
                {
                    obj = oo.AddComponent<UUObject>();
                }

                if (!obj.isStatic)
                {
                    Rigidbody rb = oo.GetComponentInChildren<Rigidbody>();
                    if (rb == null)
                    {
                        rb = oo.AddComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                        }
                    }
                }
            }
        }
        else
        {
            switch (objClass)
            {
            case UUObject.EClass.CrittersA:
            case UUObject.EClass.CrittersB:
            case UUObject.EClass.CrittersC:
            case UUObject.EClass.CrittersD:
                obj = Instantiate(critter);
                break;
            default:
                obj = Instantiate(sprite);
                
                //Debug.Log($"Level {loadedLevel}: still using sprite for {DataLoader.GetCleanedObjectName(type)}");

                if ((int)type < objMat.Length && objMat[(int)type] != null)
                {
                    MeshRenderer objMeshRenderer = obj.GetComponentInChildren<MeshRenderer>();
                    objMeshRenderer.material = objMat[(int)type];
                    objMeshRenderer.lightProbeUsage = 0;
                }

                break;
            }
        }

        obj.name = DataLoader.GetCleanedObjectName(type);
        obj.Initialize(objData, critterData);
        obj.originalLevel = 0; // Runtime-created objects have no original level
        
        // Initialize cachedRenderer for instantiated objects
        if (obj.cachedRenderer == null)
        {
            obj.cachedRenderer = obj.GetComponentInChildren<MeshRenderer>();
        }
        
        obj.gameObject.SetActive(false);

#if false
        int topLevelLayer = obj.gameObject.layer;
        Transform[] tts = obj.GetComponentsInChildren<Transform>(); 
        foreach (Transform ttt in tts)
        {
            if (ttt.gameObject.layer != topLevelLayer)
            {
                Debug.Log($"Mismatch in layer tagging for {obj.name}");
            }
        }
#endif

        return obj;
    }

    private ushort[] ReadObjectData(int o)
    {
        // first 256 are critters with 8+19 data elements
        int offset = (o < 256) ? 27 * o : 27 * 256 + 8 * (o - 256);
        levStream.Seek(chunkOffsets[loadedLevel - 1] + 0x4000 + offset);

        return levStream.GetUShortArray(4);
    }

    /// <summary>Reads the 4 object words from lev.ark for a specific level and objectIndex (independent of loadedLevel).</summary>
    public ushort[] ReadObjectDataFromLevel(int level, int objectIndex)
    {
        if (levStream == null || chunkOffsets == null
            || level < 1 || level - 1 >= chunkOffsets.Length
            || objectIndex < 0 || objectIndex >= 1024)
        {
            return null;
        }

        // first 256 are critters with 8+19 data elements
        int offset = (objectIndex < 256) ? 27 * objectIndex : 27 * 256 + 8 * (objectIndex - 256);
        levStream.Seek(chunkOffsets[level - 1] + 0x4000 + offset);

        return levStream.GetUShortArray(4);
    }

    /// <summary>
    /// If lev.ark still defines this native slot as the given type with a non-zero owner/lock id, returns that id.
    /// </summary>
    public bool TryGetArkOwnerIndex(int level, int objectIndex, EObjectType expectedType, out int arkOwnerIndex)
    {
        arkOwnerIndex = 0;
        ushort[] objData = ReadObjectDataFromLevel(level, objectIndex);
        if (objData == null || objData.Length < 4)
        {
            return false;
        }

        EObjectType arkType = (EObjectType)(objData[0] & 511);
        if (arkType != expectedType)
        {
            return false;
        }

        arkOwnerIndex = objData[3] & 63;
        return arkOwnerIndex != 0;
    }

    public int GetChainIndexFromObjectData(int objectIndex)
    {
        ushort[] objData = ReadObjectData(objectIndex);
        ushort s2 = objData[2];
        return (s2 >> 6) & 1023;
    }

    /// <summary>
    /// Reads critterData (19 bytes) from a specific level and objectIndex.
    /// Returns null if objectIndex >= 256 (not a critter).
    /// </summary>
    public byte[] ReadCritterDataFromLevel(int level, int objectIndex)
    {
        if (objectIndex >= 256)
        {
            return null; // Not a critter
        }

        // first 256 are critters with 8+19 data elements
        int offset = 27 * objectIndex;
        levStream.Seek(chunkOffsets[level - 1] + 0x4000 + offset + 8); // +8 to skip objData (4 ushorts = 8 bytes)
        
        return levStream.GetByteArray(19);
    }

    public UUObject CreateObject(int objectIndex)
    {
        switch (loadedLevel)
        {
        case 2:
            // Skip doors embedded in walls
            if (objectIndex is 948 or 985)
            {
                return null;
            }
            break;
        case 3:
            // Skip door embedded in wall
            if (objectIndex is 654)
            {
                return null;
            }
            break;
        }

        ushort[] objData = ReadObjectData(objectIndex);
        byte[] critterData = (objectIndex < 256) ? levStream.GetByteArray(19) : null;
        
        switch (loadedLevel)
        {
        case 7:
            if (objectIndex is 567 or 564 or 568 or 563 or 557 or 552 or 918 or 545 or 549 or 546 or 547 or 550 or 551 or 548 or 559)
            {
                objData[0] &= 0xffe0;
                objData[0] |= (ushort)EObjectType.DecalWithCollision;
            }
            break;
        }

        UUObject obj = CreateObjectOfType(objData, critterData);

        if (obj != null)
        {
            obj.objectIndex = objectIndex;
            obj.originalLevel = loadedLevel; // Track where this objectIndex came from
            obj.levelIndex = loadedLevel; // Set levelIndex so it saves correctly
        }

        AssignLevelObjectSlot(loadedLevel, objectIndex, obj);
        return obj;
    }

    /// <summary>
    /// Single choke point for writes to <see cref="Level.objects"/>. Warns (with Unity stack trace on expand) when a non-null
    /// object does not match native slot rules: originalLevel &gt; 0 ⇒ originalLevel == level and objectIndex == slotIndex;
    /// originalLevel == 0 ⇒ objectIndex == slotIndex (runtime slot).
    /// </summary>
    public static void AssignLevelObjectSlot(int level, int slotIndex, UUObject obj)
    {
        if (sLevelLoader == null || level < 0 || level >= sLevelLoader.levels.Length || sLevelLoader.levels[level] == null)
            return;
        UUObject[] arr = sLevelLoader.levels[level].objects;
        if (slotIndex < 0 || slotIndex >= arr.Length)
            return;

        if (obj != null)
        {
            if (obj.originalLevel > 0)
            {
                if (obj.originalLevel != level || obj.objectIndex != slotIndex)
                {
                    Debug.LogWarning(
                        $"AssignLevelObjectSlot: writing level {level}[{slotIndex}] ← '{obj.name}' (type {(int)obj.type}) but originalLevel={obj.originalLevel}, objectIndex={obj.objectIndex}, levelIndex={obj.levelIndex}.",
                        obj);
                }
            }
            else if (obj.objectIndex != slotIndex)
            {
                Debug.LogWarning(
                    $"AssignLevelObjectSlot: runtime object '{obj.name}' objectIndex={obj.objectIndex} vs slot {slotIndex} on level {level}.",
                    obj);
            }
        }

        arr[slotIndex] = obj;
    }

    private void Start()
    {
        // Awake() already handles singleton setup
        // Don't auto-load - let FrontEnd or SaveGameManager control when levels load
    }

    private void CreateWallAndFloorMaterials()
    {
        Shader shader = Shader.Find("Standard");

        if (wallMat == null || wallMat.Length == 0)
        {
            List<List<Texture2D>> wallTex = DataLoader.sDataLoader.wallTex;
            wallMat = new Material[wallTex.Count];
            for (int c = 0; c < wallTex.Count; ++c)
            {
                wallMat[c] = new Material(shader) { mainTexture = wallTex[c][0] };
                wallMat[c].SetFloat("_Glossiness", 0.0f);
            }
            // pure black, for the ethereal void
            wallMat[64] = new Material(Shader.Find("Unlit/Color"));
            wallMat[64].SetColor("_Color", Color.black);
        }

        if (floorMat == null || floorMat.Length == 0)
        {
            List<List<Texture2D>> floorTex = DataLoader.sDataLoader.floorTex;
            floorMat = new Material[floorTex.Count];
            for (int c = 0; c < floorTex.Count; ++c)
            {
                floorMat[c] = new Material(shader) { mainTexture = floorTex[c][0] };
                floorMat[c].SetFloat("_Glossiness", 0.2f);
            }
            // pure black, for the ethereal void
            floorMat[26] = new Material(Shader.Find("Unlit/Color"));
            floorMat[26].SetColor("_Color", Color.black);
        }
    }

    private List<Tile> movableTiles;
    
    public void LoadLevelGeometry(int level)
    {
        loadedLevel = level;
        Cheats.sCheats.level = level;

        // Disable geometry for all other levels
        for (int i = 1; i < levels.Length; ++i)
        {
            if (i != loadedLevel && levels[i] != null && levels[i].geo != null)
            {
                levels[i].geo.gameObject.SetActive(false);
            }
        }

        if (levels[loadedLevel] != null)
        {
            // wake up objects
            foreach (LevelObject obj in levels[loadedLevel].worldObj)
            {
                if (obj.activeInLevel)
                {
                    obj.gameObject.SetActive(true);
                }
            }
            
            // Level geometry should always be enabled when its level is active
            if (levels[loadedLevel].geo != null)
            {
                levels[loadedLevel].geo.gameObject.SetActive(true);
            }

            return;
        }

        sentPostLoadInitialize = false;

        float startT = Time.realtimeSinceStartup;

        levStream = new Stream("../Data/lev.ark");
        // header
        chunkCount = levStream.GetUShort();
        chunkOffsets = levStream.GetIntArray(chunkCount);

        levels[loadedLevel] = new Level();

        ReadTiles();

        movableTiles = FindMovableTiles();

        CarveOutHiddenTiles();

        CreateWallAndFloorMaterials();

        levels[loadedLevel].geo = LevelGeometry.CreateLevelGeometry(levStream, movableTiles);

        // Ensure geometry is enabled for the newly loaded level
        levels[loadedLevel].geo.gameObject.SetActive(true);

        CreateObjectMaterials();
        
        levels[loadedLevel].pvs.EnsurePVSIsReady(loadedLevel);

        Debug.Log($"Time to load level geometry: {Time.realtimeSinceStartup - startT}.");

        levelLoadedTime = Time.realtimeSinceStartupAsDouble;
    }

    public void LoadLevel(int level)
    {
        // Check if level was already loaded before calling LoadLevelGeometry
        bool wasAlreadyLoaded = (levels[level] != null);
        
        // Load geometry/environment first
        LoadLevelGeometry(level);
        
        // If level was already loaded, LoadLevelGeometry woke up objects and returned early
        // Don't create objects again, but still recreate lava lights
        if (wasAlreadyLoaded)
        {
            CreateLavaLights();
            return;
        }
        
        // Now create objects from lev.ark (moving platforms, lava lights, and all objects)
        CreateMovingPlatforms(movableTiles);

        // objects need the geo to prevent them falling through the floor
        CreateObjects();

        CreateLavaLights();
    }

    /// <summary>
    /// Clears all per-level state and loader caches so a subsequent save load matches a fresh LevelLoader
    /// (avoids LoadLevel skipping CreateObjects because levels[level] was left non-null after a prior visit).
    /// Call after <see cref="SaveGameManager.DeleteNonPersistentObjects"/> so worldObj lists still exist for cleanup.
    /// </summary>
    public void PrepareForSaveLoad()
    {
        for (int i = 0; i < levels.Length; i++)
        {
            if (levels[i] == null)
                continue;
            if (levels[i].geo != null && levels[i].geo.gameObject != null)
            {
                DestroyImmediate(levels[i].geo.gameObject);
            }
            levels[i] = null;
        }

        usedObjects.Clear();
        movableTiles = null;

        foreach (GameObject lightObj in lavaLights)
        {
            if (lightObj != null)
            {
                DestroyImmediate(lightObj);
            }
        }
        lavaLights.Clear();

        levStream = null;
        chunkOffsets = null;
        chunkCount = 0;

        sentPostLoadInitialize = false;
    }

    public void DeactivateCurrentLevel()
    {
        // Disable level geometry
        if (levels[loadedLevel].geo != null)
        {
            levels[loadedLevel].geo.gameObject.SetActive(false);
        }
        
        LinkedListNode<LevelObject> node = levels[loadedLevel].worldObj.First;
        while (node != null)
        {
            LinkedListNode<LevelObject> next = node.Next;
            LevelObject obj = node.Value;
            if (obj == null || obj.gameObject == null)
            {
                Debug.Log("Deactivate current level: Removing dead object from worldObj. Likely a bug.");
                levels[loadedLevel].worldObj.Remove(node);
            }
            else if (obj.temporary)
            {
                levels[loadedLevel].worldObj.Remove(node);
                Destroy(obj.gameObject);
            }
            else
            {
                obj.activeInLevel = obj.gameObject.activeSelf;
                obj.gameObject.SetActive(false);
            }
            node = next;
        }
    }

    private static void DismissPlayerPanelsForVoidLevel()
    {
        Inventory.HidePanel();
        Magic.HidePanel();
        StatsPanel.sStatsPanel?.Hide();
    }

    public void ChangeLevel(int level, Vector3 pos)
    {
        if (level != 0 && level != loadedLevel)
        {
            DeactivateCurrentLevel();
            LoadLevel(level);
            if (level == 9)
            {
                DismissPlayerPanelsForVoidLevel();
            }
        }

        PlayerObject.Player.fade = 1.0f;
        PlayerObject.Player.fadeIn = true;

        PlayerObject.Player.TeleportTo(pos, PlayerObject.Player.gameObject.transform.rotation);
    }

    public void ChangeLevel(int level, int tileX, int tileY)
    {
        bool changedLevel = false;
        if (level != 0 && level != loadedLevel)
        {
            DeactivateCurrentLevel();
            LoadLevel(level);
            changedLevel = true;
            if (level == 9)
            {
                DismissPlayerPanelsForVoidLevel();
            }
        }

        PlayerObject.Player.fade = 1.0f;
        PlayerObject.Player.fadeIn = true;

        Vector3 pos = GetTile(tileX, tileY).GetCenter() + Vector3.up;
        Quaternion rot = PlayerObject.Player.gameObject.transform.rotation;

        if (!changedLevel)
        {
            // probe for longest view
            Quaternion furthestQ = Quaternion.identity;
            float furthestDist = 0.0f;
            for (int i = 0; i < 4; ++i)
            {
                Quaternion q = Quaternion.AngleAxis(i * 90.0f, Vector3.up);
                int layerMask = LayerMasks.EnvironmentAndCeiling;
                if (Physics.Raycast(pos, q * Vector3.forward, out RaycastHit hit, 20.0f, LayerMasks.EnvironmentOnly))
                {
                    if (hit.distance > furthestDist)
                    {
                        furthestDist = hit.distance;
                        furthestQ = q;
                    }
                }
                else
                {
                    furthestDist = 20.0f;
                    furthestQ = q;
                }
            }

            rot = furthestQ;
        }
        else if (level == 9)
        {
            // face the slasher
            rot = Quaternion.AngleAxis(90.0f, Vector3.up);
        }
        else
        {
            // check for a decal
            for (int i = 0; i < 4; ++i)
            {
                Quaternion q = Quaternion.AngleAxis(i * 90.0f, Vector3.up);
                int layerMask = 1 << LayerMask.NameToLayer("Objects");
                // look behind
                if (Physics.Raycast(pos, q * Vector3.back, 3.0f, layerMask))
                {
                    rot = q;
                    break;
                }
            }
        }
        
        PlayerObject.Player.TeleportTo(pos, rot);
    }

    public void PrepareForLoad()
    {
        foreach (GameObject o in FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!o.CompareTag("SurvivesLoad"))
            {
                Destroy(o);
            }
        }
    }

    List<Tile> FindMovableTiles()
    {
        List<Tile> tiles = new List<Tile>();
        for (int y = 0; y < 64; ++y)
        {
            for (int x = 0; x < 64; ++x)
            {
                Tile t = levels[loadedLevel].tiles[x, y];
                int o = t.firstObject;
                while (o > 0)
                {
                    ushort[] objData = ReadObjectData(o);
                    int type = objData[0] & 511;
                    if (type == 387) // a do trap
                    {
                        // quality
                        if ((objData[2] & 63) == 3) // platform
                        {
                            if (!tiles.Contains(t))
                            {
                                tiles.Add(t);
                                t.originalFloorHeight = t.floorHeight;
                            }
                        }
                    }
                    else if (type == 389) // change terrain
                    {
                        int ty = (objData[1] >> 10) & 7;
                        int tx = (objData[1] >> 13) & 7;
                        for (int xx = t.x; xx <= t.x + tx; ++xx)
                        {
                            for (int yy = t.y; yy <= t.y + ty; ++yy)
                            {
                                Tile tt = levels[loadedLevel].tiles[xx, yy];
                                if (tt != null)
                                {
                                    if (!tiles.Contains(tt))
                                    {
                                        tiles.Add(tt);
                                        if (tt.type == 0)
                                        {
                                            tt.type = 1;
                                            tt.originalFloorHeight = 16; // closed off
                                        }
                                        else
                                        {
                                            tt.originalFloorHeight = tt.floorHeight;
                                        }
                                        tt.floorHeight = 0;
                                    }
                                }
                            }
                        }
                    }

                    // chain index
                    o = (objData[2] >> 6) & 1023;
                }
                
                // bullfrog puzzle
                if (loadedLevel == 4 && x is >= 48 and <= 55 && y is >= 48 and <= 55)
                {
                    if (!tiles.Contains(t))
                    {
                        tiles.Add(t);
                        // allow the tiles to be lowered
                        t.originalFloorHeight = t.floorHeight;
                        t.floorHeight = 0;
                    }
                }
            }
        }

        return tiles;
    }

    void CarveOutHiddenTiles()
    {
        for (int y = 0; y < 64; ++y)
        {
            for (int x = 0; x < 64; ++x)
            {
                Tile t = levels[loadedLevel].tiles[x, y];
                int o = t.firstObject;
                while (o > 0)
                {
                    ushort[] objData = ReadObjectData(o);
                    EObjectType type = (EObjectType)(objData[0] & 511);
                    if (type == EObjectType.SecretDoor)
                    {
                        int z = objData[1] & 127;
                        t.floorHeight = z / 8;
                        t.type = 1; // flat
                        t.hidden = true; // for map & reveal spell
                    }
                    else if (type == EObjectType.Decal)
                    {
                        t.decal = o;
                        t.decalType = objData[3] & 63;
                        t.decalAngle = (objData[1] >> 7) & 7;
                        t.decalHeight = (objData[1] & 127) / 8;
                    }

                    // chain index
                    o = (objData[2] >> 6) & 1023;
                }
            }
        }
    }

    private void CreateMovingPlatforms(List<Tile> movableTiles)
    {
        foreach (Tile t in movableTiles)
        {
            MovingPlatform obj = Instantiate(movableTile);
            obj.levelIndex = loadedLevel;
            obj.initialTile = t;
            obj.gameObject.layer = LayerMask.NameToLayer("Environment");

            // Calculate material indices
            int floorTexIndex = levels[loadedLevel].floors[t.floorTexture];
            int[] wallTexIndices = new int[4];
            wallTexIndices[0] = levels[loadedLevel].walls[levels[loadedLevel].tiles[t.x, t.y - 1].wallTexture]; // N
            wallTexIndices[1] = levels[loadedLevel].walls[levels[loadedLevel].tiles[t.x + 1, t.y].wallTexture]; // E  
            wallTexIndices[2] = levels[loadedLevel].walls[levels[loadedLevel].tiles[t.x, t.y + 1].wallTexture]; // S
            wallTexIndices[3] = levels[loadedLevel].walls[levels[loadedLevel].tiles[t.x - 1, t.y].wallTexture]; // W

            obj.CreateMesh(floorTexIndex, wallTexIndices);
            obj.transform.position = new Vector3(xzScale * t.x, yScale * t.originalFloorHeight, xzScale * t.y);

            t.movingPlatform = obj;

            levels[loadedLevel].worldObj.AddFirst(obj);
        }
    }

    public void SetMaterialsOnMovingPlatform(Tile t, MovingPlatform plat, int wallTex, int floorTex)
    {
        MeshRenderer r = plat.GetComponent<MeshRenderer>();
        Material[] mats = r.materials;
        
        int wallMatIndex = -1;
        int floorMatIndex = -1;
        
        // Use the platform's own level, not the currently loaded level
        Level level;
        if (plat.levelIndex >= 1 && plat.levelIndex < levels.Length 
            && levels[plat.levelIndex] != null)
        {
            level = levels[plat.levelIndex];
        }
        else
        {
            // Fallback to current level if levelIndex is invalid (shouldn't happen normally)
            level = levels[loadedLevel];
        }
        
        if (wallTex < level.walls.Length)
        {
            wallMatIndex = level.walls[wallTex];
            for (int i = 1; i < 4; ++i)
            {
                mats[i] = wallMat[wallMatIndex];
            }
        }

        if (floorTex < level.floors.Length)
        {
            floorMatIndex = level.floors[floorTex];
            mats[0] = floorMat[floorMatIndex];

            t.floorTexture = floorTex;
        }

        r.materials = mats;
        
        // Update material indices for save/load
        plat.UpdateMaterialIndices(floorMatIndex, wallMatIndex);
    }

    public static void FillContainer(UUObject obj)
    {
        if (sLevelLoader.loadedLevel == 9) // containers borked on level 9
        {
            return;
        }

        // If container already has contents, skip (might have been filled during level loading)
        if (obj.contents != null && obj.contents.Count > 0)
        {
            return;
        }

        obj.contents = new List<UUObject>();
        int child = obj.link;
        while (child != 0)
        {
            // Stop the content chain as soon as it reaches a non-portable, non-container object
            // (lock, trigger, door, decal, etc.). Container contents are only portable loot or
            // nested containers; a script object here means the chain has run off the real
            // contents list, so don't pull it (or anything after it) into contents.
            EObjectType childType = (EObjectType)sLevelLoader.GetObjectType(child);
            if (UUObject.GetClass(childType) != UUObject.EClass.Containers && !IsPortableObjectType(childType))
            {
                break;
            }

            // Try to get the object from the level's object array
            UUObject childObj = GetObj(child);
            
            if (childObj == null)
            {
                // Object doesn't exist yet, try to create it
                // This handles the case where the container references objects that weren't created yet
                childObj = sLevelLoader.CreateObject(child);
                if (childObj != null)
                {
                    childObj.used = true;
                }
            }

            if (childObj != null)
            {
                obj.contents.Add(childObj);

                // If the child object is itself a container, fill its contents recursively
                if (childObj.getClass == UUObject.EClass.Containers && (childObj.contents == null || childObj.contents.Count == 0))
                {
                    LevelLoader.FillContainer(childObj);
                }

                child = childObj.chainIndex;
            }
            else
            {
                // Can't find or create the child object, break the chain
                break;
            }
        }
    }

    private void CreateObjects()
    {
        for (int y = 0; y < 64; ++y)
        {
            for (int x = 0; x < 64; ++x)
            {
                Tile t = levels[loadedLevel].tiles[x, y];
                int o = t.firstObject;
                UUObject prevObj = null;
                while (o > 0)
                {
                    UUObject obj = CreateObject(o);
                    
                    // Skip if object creation was skipped (e.g., door embedded in wall)
                    if (obj == null)
                    {
                        // Read chainIndex from objData to continue the chain
                        o = GetChainIndexFromObjectData(o);
                        continue;
                    }

                    if (prevObj != null)
                    {
                        // check for duplicated object
                        if (obj.type == prevObj.type
                            && obj.x == prevObj.x
                            && obj.y == prevObj.y
                            && obj.z == prevObj.z)
                        {
                            // Do NOT Destroy here: CreateObject already wrote objects[o] = obj, and Destroy is
                            // deferred to end of frame, so the template loop below still sees the slot as occupied
                            // and skips it. The slot then ends up referencing a destroyed object, vanishes from the
                            // save, and only TryRepairLevelObjects restores it on reload. Keep the instance inactive
                            // (we skip WorldInitialize) so the slot stays valid for scripting (e.g. a door's lock).
                            o = obj.chainIndex;
                            continue;
                        }
                    }

                    prevObj = obj;

                    obj.WorldInitialize(t, x, y);

                    obj.used = true;

                    if (obj.isCritter)
                    {
                        int child = obj.link;
                        while (child != 0)
                        {
                            UUObject childObj = CreateObject(child);
                            if (childObj == null)
                            {
                                break; // Break if child creation was skipped
                            }
                            child = childObj.chainIndex;
                        }
                    }

                    o = obj.chainIndex;
                }
            }
        }
        
        // also create all template objects, traps, triggers, locks, etc. 
        for (int o = 0; o < 1024; ++o)
        {
            int type = GetObjectType(o);
            if (type > 0
                && levels[loadedLevel].objects[o] == null
                && StringLoader.GetString(4, type) != "")
            {
                CreateObject(o);
            }

            if (Cheats.sCheats.addIndexToObjectName)
            {
                if (levels[loadedLevel].objects[o] != null)
                {
                    levels[loadedLevel].objects[o].name += $"_{o}";
                }
            }
        }
        
        // fill containers (only for used objects, not disabled/template objects)
        for (int o = 0; o < 1024; ++o)
        {
            UUObject obj = levels[loadedLevel].objects[o];
            if (obj != null && obj.used && obj.getClass == UUObject.EClass.Containers && (obj.contents == null || obj.contents.Count == 0))
            {
                LevelLoader.FillContainer(obj); 
            }
        }
        
        Shelf.PutBooksOnShelves(levels[loadedLevel].objects, shelf);
    }

    public static Level GetLevel()
    {
        return sLevelLoader.levels[sLevelLoader.loadedLevel];
    }

    private void CreateObjectMaterials()
    {
        Shader spriteShader = Shader.Find("Transparent/Diffuse");

        Texture2D[] objTex = DataLoader.sDataLoader.objTex;
        objMat = new Material[objTex.Length];
        for (int i = 0; i < objTex.Length; ++i)
        {
            if (objTex[i] != null)
            {
                objMat[i] = new Material(spriteShader) { mainTexture = objTex[i] };
                objMat[i].SetFloat("_Glossiness", 0.2f);
            }
        }

        Texture2D[] tmObjTex = DataLoader.sDataLoader.tmObjTex;
        tmObjMat = new Material[tmObjTex.Length];
        for (int i = 0; i < tmObjTex.Length; ++i)
        {
            if (tmObjTex[i] != null)
            {
                tmObjMat[i] = new Material(spriteShader) { mainTexture = tmObjTex[i] };
                tmObjMat[i].SetFloat("_Glossiness", 0.2f);
            }
        }
    }

    private void ReadTiles()
    {
        // read the tiles from the chunk
        levStream.Seek(chunkOffsets[loadedLevel - 1]);
        uint[] tileDatas = levStream.GetUIntArray(64 * 64);
        for (int y = 0; y < 64; ++y)
        {
            for (int x = 0; x < 64; ++x)
            {
                Tile t = levels[loadedLevel].tiles[x, y] = new Tile();
                uint tileData = tileDatas[64 * y + x];
                t.type = (int)tileData & 15;
                t.floorHeight = (int)(tileData >> 4) & 15;
                t.floorTexture = (int)(tileData >> 10) & 15;
                t.doorFrob = (int)(tileData >> 14) & 3;
                t.wallTexture = (int)(tileData >> 16) & 63;
                t.firstObject = (int)(tileData >> 22) & 1023;
                t.x = x;
                t.y = y;
            }
        }
    }

    public void CreateLavaLights()
    {
        // Delete existing lava lights
        foreach (GameObject lightObj in lavaLights)
        {
            if (lightObj != null)
            {
                Destroy(lightObj);
            }
        }
        lavaLights.Clear();

        // Create new lava lights
        for (int y = 0; y < 64; ++y)
        {
            for (int x = 0; x < 64; ++x)
            {
                Tile t = levels[loadedLevel].tiles[x, y];
                if (t.type != 0)
                {
                    bool floorIsLava = t.GetFloorTerrain() == ETerrainType.Lava;
                    bool wallIsLava = t.GetWallTerrain() == ETerrainType.Lavafall; 
                    if (floorIsLava || wallIsLava)
                    {
                        Vector2 rnd2 = (wallIsLava ? 0.0f : 0.8f) * Random.insideUnitCircle;
                        Vector3 rnd3 = new Vector3(rnd2.x, 0.0f, rnd2.y);
                        GameObject lightObj = Instantiate(lavaLight, t.GetCenter() + rnd3, Quaternion.identity);
                        lavaLights.Add(lightObj);
                    }
                }
            }
        }
    }

    public Material mazePathMaterial;

    public void GetFloorHeights(int[] h, int x, int y, int dx, int dy, List<Tile> movableTiles)
    {
        if (x < 0 || x > 63 || y < 0 || y > 63)
        {
            h[0] = 16;
            h[1] = 16;
            h[2] = 16;
            h[3] = 16;
            return;
        }

        Tile t = levels[loadedLevel].tiles[x, y];
        if (movableTiles != null && movableTiles.Contains(t))
        {
            h[0] = 0;
            h[1] = 0;
            h[2] = 0;
            h[3] = 0;
            return;
        }

        int fh = t.floorHeight;
        h[0] = fh;
        h[1] = fh;
        h[2] = fh;
        h[3] = fh;
        switch (t.type)
        {
        case 0:
            h[0] = 16;
            h[1] = 16;
            h[2] = 16;
            h[3] = 16;
            break;
        case 2:
            // open to SE
            h[2] = 16;
            // if approaching from back side
            if (dx > 0 || dy < 0)
            {
                h[0] = 16;
                h[3] = 16;
            }
            break;
        case 3:
            // open to SW
            h[3] = 16;
            // if approaching from back side
            if (dx < 0 || dy < 0)
            {
                h[1] = 16;
                h[2] = 16;
            }
            break;
        case 4:
            // open to NE
            h[0] = 16;
            // if approaching from back side
            if (dx > 0 || dy > 0)
            {
                h[1] = 16;
                h[2] = 16;
            }
            break;
        case 5:
            // open to NW
            h[1] = 16;
            // if approaching from back side
            if (dx < 0 || dy > 0)
            {
                h[0] = 16;
                h[3] = 16;
            }
            break;
        case 6:
            // slope n
            h[2] += 1;
            h[3] += 1;
            break;
        case 7:
            // slope s
            h[0] += 1;
            h[1] += 1;
            break;
        case 8:
            // slope e
            h[1] += 1;
            h[3] += 1;
            break;
        case 9:
            // slope w
            h[0] += 1;
            h[2] += 1;
            break;
        }
    }

    public static UUObject GetObj(int index)
    {
        return GetObj(index, sLevelLoader.loadedLevel);
    }

    /// <summary>
    /// Gets an object by index from a specific level (e.g. the level a trigger is on, not necessarily the loaded level).
    /// </summary>
    public static UUObject GetObj(int index, int level)
    {
        if (level > 0
            && level < sLevelLoader.levels.Length
            && sLevelLoader.levels[level] != null
            && index >= 0 && index < sLevelLoader.levels[level].objects.Length)
        {
            return sLevelLoader.levels[level].objects[index];
        }
        return null;
    }

    public static Tile GetTile(int x, int y)
    {
        if (x is >= 0 and < 64 && y is >= 0 and < 64 
            && sLevelLoader != null
            && sLevelLoader.loadedLevel > 0
            && GetLevel() != null)
        {
            return GetLevel().tiles[x, y];
        }
        return null;
    }

    public static Tile GetTile(int level, int x, int y)
    {
        if (x is >= 0 and < 64 && y is >= 0 and < 64 
            && sLevelLoader != null
            && level > 0
            && level < sLevelLoader.levels.Length
            && sLevelLoader.levels[level] != null)
        {
            return sLevelLoader.levels[level].tiles[x, y];
        }
        return null;
    }

    public static Tile GetTile(Vector3 pos)
    {
        return GetTile((int)(pos.x / Tile.xzScale), (int)(pos.z / Tile.xzScale));
    }

    public static Tile GetClosestTile(Vector3 pos)
    {
        int x = Math.Clamp((int)(pos.x / xzScale), 0, 63);
        int y = Math.Clamp((int)(pos.z / xzScale), 0, 63);
        
        // extend out until we find a non-zero tile
        List<Tile> candidates = new();
        List<Tile> done = new();
        candidates.Add(GetTile(x, y));
        while (candidates.Count > 0)
        {
            Tile t = candidates[0];
            candidates.RemoveAt(0);
            if (t.type != 0)
            {
                return t;
            }
            done.Add(t);
            int[] xo = { -1, 0, 1, 0 };
            int[] yo = { 0, -1, 0, 1 };
            for (int i = 0; i < 4; ++i)
            {
                x = Math.Clamp(t.x + xo[i], 0, 63);
                y = Math.Clamp(t.y + yo[i], 0, 63);
                Tile n = GetTile(x, y);
                if (!done.Contains(n))
                {
                    candidates.Add(n);
                }
            }
        }
        
        return null;
    }

    public static Material GetWallMat(int index)
    {
        return sLevelLoader.wallMat[GetLevel().walls[index]];
    }

    public void TryInteract(RaycastHit hit)
    {
        Level level = GetLevel();
        if (hit.transform == level.geo.transform)
        {
            Mesh mesh = level.geo.GetComponent<MeshFilter>().mesh;
            for (int i = 0; i < mesh.subMeshCount; ++i)
            {
                SubMeshDescriptor desc = mesh.GetSubMesh(i);
                if (desc.indexStart <= 3 * hit.triangleIndex &&
                    desc.indexStart + desc.indexCount > 3 * hit.triangleIndex)
                {
                    int stringIndex;
                    if (i < 48)
                    {
                        // walls indexed from the bottom of the array
                        stringIndex = levels[loadedLevel].walls[i];
                    }
                    else
                    {
                        if (hit.normal.y > 0.0f)
                        {
                            // floors indexed from the top of the array
                            stringIndex = 510 - levels[loadedLevel].floors[i - 48];
                        }
                        else
                        {
                            stringIndex = 511;
                        }
                    }

                    Messages.Add($"{StringLoader.GetString(1, 260)}{StringLoader.GetString(10, stringIndex)}.");
                    return;
                }
            }
        }
        else if (level.geo != null
            && hit.transform.parent == level.geo.transform
            && hit.collider != null
            && hit.collider.gameObject.name == "Ceiling")
        {
            // Dedicated ceiling mesh (LevelGeometry); same string index as main mesh floor submeshes for ceiling-facing.
            int stringIndex = 511;
            Messages.Add($"{StringLoader.GetString(1, 260)}{StringLoader.GetString(10, stringIndex)}.");
            return;
        }
        else if (hit.transform != null)
        {
            MovingPlatform plat = hit.transform.GetComponent<MovingPlatform>(); 
            if (plat != null)
            {
                plat.LookedAt(hit.normal);
                return;
            }
            
            // check for Arial
            if (loadedLevel == 7)
            {
                Arial arial = hit.transform.GetComponent<Arial>();
                if (arial != null)
                {
                    Messages.Add("You see a princess chained to a wall.");
                }
            }
        }
    }

    public static void AddToWorld(UUObject obj)
    {
        obj.gameObject.SetActive(true);
        if (!worldObj.Contains(obj))
        {
            obj.levelIndex = sLevelLoader.loadedLevel;
            worldObj.AddFirst(obj);
        }
    }

    /// <summary>
    /// Ark indices that are not "template-only": reachable from tile object stacks plus critter possession lists
    /// and container content lists (link + chainIndex), matching CreateObjects / FillContainer. Used so save repair
    /// does not CreateObject() indices that were placed or nested—only template slots may be recreated from lev.ark.
    /// </summary>
    private HashSet<int> BuildArkNonTemplateIndexSet(int level)
    {
        HashSet<int> reachable = new HashSet<int>();
        if (level < 1 || level >= levels.Length || levels[level] == null || chunkOffsets == null)
        {
            return reachable;
        }

        int savedLoaded = loadedLevel;
        loadedLevel = level;
        try
        {
            // Phase A: tile firstObject chains (sibling chainIndex only)
            for (int y = 0; y < 64; ++y)
            {
                for (int x = 0; x < 64; ++x)
                {
                    int o = levels[level].tiles[x, y].firstObject;
                    while (o > 0)
                    {
                        if (!reachable.Add(o))
                        {
                            break;
                        }

                        o = GetChainIndexFromObjectData(o);
                    }
                }
            }

            // Phase B: for critters and containers only, follow link (special high bits) then chainIndex siblings.
            // Do not follow link for other classes—stackables use special for quantity, etc.
            Queue<int> expand = new Queue<int>(reachable);
            while (expand.Count > 0)
            {
                int i = expand.Dequeue();
                ushort[] data = ReadObjectData(i);
                ushort s0 = data[0];
                ushort s3 = data[3];
                EObjectType type = (EObjectType)(s0 & 511);
                UUObject.EClass cls = UUObject.GetClass(type);
                bool followLink = cls is >= UUObject.EClass.CrittersA and <= UUObject.EClass.CrittersD
                    || cls == UUObject.EClass.Containers;
                if (!followLink)
                {
                    continue;
                }

                int link = (s3 >> 6) & 1023;
                HashSet<int> linkWalkGuard = new HashSet<int>();
                int c = link;
                while (c > 0 && linkWalkGuard.Add(c))
                {
                    if (reachable.Add(c))
                    {
                        expand.Enqueue(c);
                    }

                    c = GetChainIndexFromObjectData(c);
                }
            }
        }
        finally
        {
            loadedLevel = savedLoaded;
        }

        return reachable;
    }

    /// <summary>
    /// Old saves could assign a foreign object to another level's objects[] slot (wrong originalLevel / objectIndex).
    /// For each bad slot, prefer an existing instance with matching originalLevel, objectIndex, and levelIndex;
    /// otherwise recreate from lev.ark via CreateObject only for template indices (not tile-placed / nested).
    /// </summary>
    public void TryRepairLevelObjects()
    {
        float repairStartT = Time.realtimeSinceStartup;
        int reassigned = 0;
        int recreated = 0;
        int placedSkipNoScene = 0;
        int templateSkipInvalidType = 0;

        // unfortunately this is super expensive
        UUObject[] all = FindObjectsByType<UUObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int rememberLoaded = loadedLevel;
        try
        {
            for (int level = 1; level < levels.Length; level++)
            {
                if (levels[level] == null)
                {
                    continue;
                }
                loadedLevel = level;

                HashSet<int> arkReachable = BuildArkNonTemplateIndexSet(level);
                UUObject[] objs = levels[level].objects;
                for (int i = 1; i < objs.Length; i++)
                {
                    UUObject at = objs[i];
                    if (IsCorrectNativeSlotObject(at, level, i))
                    {
                        continue;
                    }

                    // Only recreate template objects that the normal template-loader would create.
                    // (Many ark indices have type==0 or no string entry and should be skipped.)
                    int type = GetObjectType(i);
                    if (type <= 0 || StringLoader.GetString(4, type) == "")
                    {
                        templateSkipInvalidType++;
                        continue;
                    }

                    UUObject found = FindNativeObjectInstance(all, level, i);
                    if (found != null)
                    {
                        if (IsPortableObjectType(found.type))
                        {
                            AssignLevelObjectSlot(level, i, null);
                            continue;
                        }

                        found.levelIndex = level;
                        AssignLevelObjectSlot(level, i, found);
                        reassigned++;
                        string prior = FormatWrongSlotPriorOccupant(at, level, i);
                        Debug.LogWarning(
                            $"Save repair: assigned '{found.name}' (type {found.type}) to level {level} objects[{i}]. Prior occupant: {prior}.", found);
                        continue;
                    }

                    if (arkReachable.Contains(i))
                    {
                        placedSkipNoScene++;
                        continue;
                    }

                    // Repair only exists to fill objects[] with lev.ark natives; never instantiate portables for that.
                    if (IsPortableObjectType((EObjectType)type))
                    {
                        AssignLevelObjectSlot(level, i, null);
                        continue;
                    }

                    UUObject created = CreateObject(i);
                    if (created != null)
                    {
                        created.used = true;
                        if (rememberLoaded != level)
                        {
                            created.gameObject.SetActive(false);
                        }

                        recreated++;
                        string priorRec = FormatWrongSlotPriorOccupant(at, level, i);
                        Debug.LogWarning(
                            $"Save repair: recreated '{created.name}' (type {created.type}) at level {level} objects[{i}] from lev.ark. Prior occupant: {priorRec}.", created);
                    }
                }
            }
        }
        finally
        {
            loadedLevel = rememberLoaded;
            float elapsed = Time.realtimeSinceStartup - repairStartT;
            Debug.Log($"Save repair: TryRepairLevelObjects finished in {elapsed:F3}s (reassigned={reassigned}, recreated={recreated}, placedSkipNoScene={placedSkipNoScene}, templateSkipInvalidType={templateSkipInvalidType}).");
        }
    }

    private static bool IsPortableObjectType(EObjectType type)
    {
        if (DataLoader.sDataLoader == null)
            return false;
        int ti = (int)type;
        if (ti < 0 || ti >= DataLoader.sDataLoader.comObjProps.Length)
            return false;
        return DataLoader.sDataLoader.comObjProps[ti].isPortable;
    }

    private static bool IsCorrectNativeSlotObject(UUObject at, int level, int slotIndex)
    {
        return at != null
            && at.originalLevel > 0
            && at.originalLevel == level
            && at.objectIndex == slotIndex;
    }

    /// <summary>Human-readable description of what was in objects[slot] before save repair (for logging).</summary>
    private static string FormatWrongSlotPriorOccupant(UUObject at, int level, int slotIndex)
    {
        if (at == null)
            return "null (empty slot)";
        return $"'{at.name}' (type {at.type}, originalLevel={at.originalLevel}, objectIndex={at.objectIndex}, levelIndex={at.levelIndex}; slot was [{level}][{slotIndex}])";
    }

    /// <summary>Prefer levelIndex == level, then any match on originalLevel + objectIndex.</summary>
    private static UUObject FindNativeObjectInstance(UUObject[] all, int level, int objectIndex)
    {
        UUObject loose = null;
        foreach (UUObject u in all)
        {
            if (u == null || u.originalLevel != level || u.objectIndex != objectIndex)
                continue;
            if (u.levelIndex == level)
                return u;
            if (loose == null)
                loose = u;
        }
        return loose;
    }

    public bool sentPostLoadInitialize;

    private int floorAnimIndex;
    private int wallAnimIndex;
    private float floorAnimTime;
    private float wallAnimTime = 0.125f;

    void Update()
    {
        // Guard against no level loaded yet
        if (levels[loadedLevel] == null)
        {
            return;
        }
        
        if (!sentPostLoadInitialize)
        {
            sentPostLoadInitialize = true;

            foreach (LevelObject obj in levels[loadedLevel].worldObj)
            {
                obj.PostLoadInitialize();
            }
        }

        if (!Magic.sMagic.IsSpellActive(Magic.ESpell.FreezeTime))
        {
            Level lev = levels[loadedLevel];
            // palette cycles for wall and floor textures (and decals I guess)
            floorAnimTime += Time.deltaTime;
            if (floorAnimTime > 0.25f)
            {
                floorAnimTime -= 0.25f;
                ++floorAnimIndex;
                foreach (ushort f in lev.floors)
                {
                    List<Texture2D> texs = DataLoader.sDataLoader.floorTex[f];
                    if (texs.Count > 1)
                    {
                        int index = floorAnimIndex % texs.Count;
                        floorMat[f].SetTexture("_MainTex", texs[index]);
                    }
                }
            }

            wallAnimTime += Time.deltaTime;
            if (wallAnimTime > 0.25f)
            {
                wallAnimTime -= 0.25f;
                ++wallAnimIndex;
                foreach (ushort w in lev.walls)
                {
                    List<Texture2D> texs = DataLoader.sDataLoader.wallTex[w];
                    if (texs.Count > 1)
                    {
                        int index = wallAnimIndex % texs.Count;
                        wallMat[w].SetTexture("_MainTex", texs[index]);
                    }
                }
            }
        }

        if (Cheats.sCheats.level != loadedLevel && Cheats.sCheats.level > 0 && Cheats.sCheats.level < 10)
        {
            DeactivateCurrentLevel();
            LoadLevel(Cheats.sCheats.level);
            PlayerObject.Player.DebugJumpToLevelStart();
        }
    }

    public void ShowMazePath(bool show)
    {
        if (mazePathMaterial != null && loadedLevel == 7)
        {
            mazePathMaterial.SetTexture("_MainTex", DataLoader.sDataLoader.floorTex[show ? 12 : 14][0]);
        }
    }

    void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.ShowObjectsDebug;

        if (Cheats.sCheats.showFloorMats)
        {
            for (int i = 0; i < floorMat.Length; ++i)
            {
                if (i != 26) // this is the pure black for the ethereal void
                {
                    Texture2D tex = floorMat[i].mainTexture as Texture2D;
                    if (tex != null)
                    {
                        GUI.DrawTexture(new Rect(100 * (i % 16), 100 * (i / 16), 96, 96), tex);
                    }
                }
            }

            for (int i = 0; i < levels[loadedLevel].floors.Length; ++i)
            {
                if (levels[loadedLevel].floors[i] != 26) // pure black for ethereal void
                {
                    Texture2D tex = floorMat[levels[loadedLevel].floors[i]].mainTexture as Texture2D;
                    if (tex != null)
                    {
                        GUI.DrawTexture(new Rect(100 * (i % 16), 400 + 100 * (i / 16), 96, 96), tex);
                    }
                }
            }
        }

        if (Cheats.sCheats.showWallMats)
        {
            for (int i = 0; i < wallMat.Length; ++i)
            {
                if (i != 64)
                {
                    Texture2D tex = wallMat[i].mainTexture as Texture2D;
                    if (tex != null)
                    {
                        GUI.DrawTexture(new Rect(66 * (i % 32), 66 * (i / 32), 64, 64), tex);
                    }
                }
            }

            for (int i = 0; i < levels[loadedLevel].walls.Length; ++i)
            {
                if (levels[loadedLevel].walls[i] != 64)
                {
                    Texture2D tex = wallMat[levels[loadedLevel].walls[i]].mainTexture as Texture2D;
                    if (tex != null)
                    {
                        GUI.DrawTexture(new Rect(66 * (i % 32), 600 + 66 * (i / 32), 64, 64), tex);
                    }
                }
            }
        }

        if (Cheats.sCheats.showDoorMats)
        {
            for (int i = 0; i < DataLoader.sDataLoader.doorTex.Length; ++i)
            {
                Texture2D tex = DataLoader.sDataLoader.doorTex[i];
                if (tex != null)
                {
                    GUI.DrawTexture(new Rect(132 * (i % 16), 198 * (i / 16), 128, 192), tex);
                }
            }

            for (int i = 0; i < levels[loadedLevel].doors.Length; ++i)
            {
                int textureIndex = levels[loadedLevel].doors[i];
                if (textureIndex < DataLoader.sDataLoader.doorTex.Length)
                {
                    Texture2D tex = DataLoader.sDataLoader.doorTex[textureIndex];
                    if (tex != null)
                    {
                        GUI.DrawTexture(new Rect(132 * (i % 16), 600 + 198 * (i / 16), 128, 192), tex);
                    }
                }
            }
        }

        if (Cheats.sCheats.showSwitches)
        {
            for (int i = 0; i < DataLoader.sDataLoader.tmFlatTex.Length; ++i)
            {
                Texture2D tex = DataLoader.sDataLoader.tmFlatTex[i];
                GUI.DrawTexture(new Rect(66 * (i & 7), 66 * (i / 8), 64, 64), tex);
            }

            for (int i = 4; i < 12; ++i)
            {
                Texture2D tex = DataLoader.sDataLoader.tmObjTex[i];
                GUI.DrawTexture(new Rect(66 * (i - 4), 66 * 3, 64, 64), tex);
            }
        }
    }

    private void OnDestroy()
    {
        // Clean up lava lights on LevelLoader destruction
        foreach (GameObject lightObj in lavaLights)
        {
            if (lightObj != null)
            {
                Destroy(lightObj);
            }
        }
        lavaLights.Clear();
    }
}
