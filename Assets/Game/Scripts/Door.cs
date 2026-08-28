using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class DoorSaveData : UUObjectSaveData
{
    public bool opening;
    public float openAmount;
}

public class Door : Lockable
{
    public GameObject door;
    public GameObject hinge;
    public float openAngle = 110.0f; // degrees
    public float openDistance = 2.0f; // m

    private float openAmount;
    private bool opening;
    public BoxCollider cachedDoorCollider;

    public AudioClip doorOpenClip;
    public AudioClip doorShutClip;
    public AudioClip hitSound;

    public CutscenePlayer arialCutscene;

    public bool spiked;

    public override void Update()
    {
        base.Update();

        if (Time.deltaTime > 0.0f)
        {
            if (opening)
            {
                openAmount = Utils.DampedApproach(openAmount, 1.0f, 1.0f);
            }
            else
            {
                openAmount = Mathf.MoveTowards(openAmount, 0.0f, 2.5f * Time.deltaTime);
            }

            if (hinge != null)
            {
                // swinging door
                float openDirection = (flags & 2) == 0 ? -1.0f : 1.0f;
                hinge.transform.localRotation = Quaternion.AngleAxis(openAngle * openAmount * openDirection, Vector3.up);
                CheckForPlayerAndReverseDirection();
            }
            else
            {
                door.transform.localPosition = new Vector3(0.0f, openDistance * openAmount, 0.0f);
            }
        }
    }
    
    private void CheckForPlayerAndReverseDirection()
    {
        float checkDirection = 0.0f;
        float openDirection = (flags & 2) == 0 ? -1.0f : 1.0f;
        if (opening && openAmount < 0.9f)
        {
            checkDirection = -openDirection;
        }
        else if (!opening && openAmount > 0.1f)
        {
            checkDirection = openDirection;
        }
        if (checkDirection != 0.0f)
        {
            // check if the player is in front of the door
            BoxCollider c = cachedDoorCollider;
            CharacterController cc = PlayerObject.Player.cachedCharacterController;
            Vector3 p = c.transform.InverseTransformPoint(cc.transform.position);

            if (Mathf.Abs(p.x) < 0.5f * c.size.x && Mathf.Abs(p.y) < 0.5f * c.size.y + cc.height / c.transform.localScale.y)
            {
                float pz = p.z * checkDirection;
                if (pz > 0.0f && pz < 0.5f * c.size.z + (cc.radius + 0.05f) / c.transform.localScale.z)
                {
                    PlayerObject.Rumble(0.2f, 0.2f, 0.2f);
                    opening = !opening;
                }
            }
        }
    }

    private Level GetLevel()
    {
        if (levelIndex >= 1 && levelIndex < LevelLoader.sLevelLoader.levels.Length
            && LevelLoader.sLevelLoader.levels[levelIndex] != null)
        {
            return LevelLoader.sLevelLoader.levels[levelIndex];
        }
        // Fallback to current level if levelIndex is invalid
        return LevelLoader.GetLevel();
    }

    private int GetDoorTypeIndex()
    {
        return (int)type & 7;
    }

    private int GetTextureIndex(Level level, int doorTypeIndex)
    {
        // 6 is portcullis, 7 is secret door
        return level.doors[doorTypeIndex > 5 ? 0 : doorTypeIndex];
    }

    private void CreateDoorGeometry()
    {
        Shader shader = Shader.Find("Standard");

        // Read from this door's own level, not the currently loaded level
        // This ensures doors initialize correctly even when loaded from save with a different level active
        Level level = GetLevel();

        int surroundingWallTexture = initialTile.wallTexture;

        MeshRenderer frameRenderer = gameObject.AddComponent<MeshRenderer>();
        frameRenderer.material = new Material(shader);
        frameRenderer.material.SetFloat("_Glossiness", 0.2f);
        frameRenderer.material.mainTexture = DataLoader.sDataLoader.wallTex[level.walls[surroundingWallTexture]][0];
        frameRenderer.lightProbeUsage = 0;

        {
            MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
            if (meshFilter)
            {
                float h = (16 - z / 8) * LevelLoader.yScale;
                float t = type == EObjectType.Portcullis ? 0.1f : 0.05f; // portcullis needs thicker frame
                float w = 0.5f * LevelLoader.xzScale;
                float d = 52.0f / 64.0f * 3.0f;

                // create a mesh and texture it
                Vector3[] vp =
                {
                    new Vector3(-w, 0, t),
                    new Vector3(-w, h, t),
                    new Vector3(w, h, t),
                    new Vector3(w, 0, t),
                    new Vector3(w / 2, 0, t),
                    new Vector3(w / 2, d, t),
                    new Vector3(-w / 2, d, t),
                    new Vector3(-w / 2, 0, t),

                    new Vector3(-w, 0, -t),
                    new Vector3(-w, h, -t),
                    new Vector3(w, h, -t),
                    new Vector3(w, 0, -t),
                    new Vector3(w / 2, 0, -t),
                    new Vector3(w / 2, d, -t),
                    new Vector3(-w / 2, d, -t),
                    new Vector3(-w / 2, 0, -t),

                    // top, for roaming sight
                    new(-w, h, -t),
                    new(w, h, -t),
                    new(-w, h, t),
                    new(w, h, t),
                };
                List<Vector3> verts = new List<Vector3>();
                Vector3[] normals = new Vector3[32];
                Vector2[] uv = new Vector2[32];
                List<int> triangles = new List<int>(); // triangle indices

                // front
                verts.AddRange(new[] { vp[0], vp[1], vp[2], vp[3], vp[4], vp[5], vp[6], vp[7] });
                for (int i = 0; i < 8; ++i) normals[i] = Vector3.forward;
                uv[0] = new Vector2(0, 0);
                uv[1] = new Vector2(0, h / 3);
                uv[2] = new Vector2(1, h / 3);
                uv[3] = new Vector2(1, 0);
                uv[4] = new Vector2(0.75f, 0);
                uv[5] = new Vector2(0.75f, d / 3);
                uv[6] = new Vector2(0.25f, d / 3);
                uv[7] = new Vector2(0.25f, 0);
                triangles.AddRange(new[] { 7, 6, 0, 6, 1, 0, 6, 2, 1, 2, 6, 5, 5, 3, 2, 5, 4, 3 });

                // back
                verts.AddRange(new[] { vp[11], vp[10], vp[9], vp[8], vp[15], vp[14], vp[13], vp[12] });
                for (int i = 0; i < 8; ++i) normals[8 + i] = Vector3.back;
                for (int i = 0; i < 8; ++i) uv[8 + i] = uv[i];
                for (int i = 0; i < 18; ++i) triangles.Add(triangles[i] + 8);

                // inner left
                verts.AddRange(new[] { vp[6], vp[7], vp[14], vp[15] });
                for (int i = 0; i < 4; ++i) normals[16 + i] = Vector3.right;
                uv[16] = new Vector2(0, 1 - d / 3);
                uv[17] = new Vector2(0, 1);
                uv[18] = new Vector2(t / 4, 1 - d / 3);
                uv[19] = new Vector2(t / 4, 1);
                triangles.AddRange(new[] { 16, 17, 18, 18, 17, 19 });

                // inner right
                verts.AddRange(new[] { vp[4], vp[5], vp[12], vp[13] });
                for (int i = 0; i < 4; ++i) normals[20 + i] = Vector3.left;
                for (int i = 0; i < 4; ++i) uv[20 + i] = uv[16 + i];
                triangles.AddRange(new[] { 20, 21, 22, 22, 21, 23 });

                // inner upper
                verts.AddRange(new[] { vp[6], vp[5], vp[14], vp[13] });
                for (int i = 0; i < 4; ++i) normals[24 + i] = Vector3.down;
                uv[24] = new Vector2(0, 1);
                uv[25] = new Vector2(0.5f, 1);
                uv[26] = new Vector2(0, 1 - t / 4);
                uv[27] = new Vector2(0.5f, 1 - t / 4);
                triangles.AddRange(new[] { 24, 26, 25, 26, 27, 25 });

                // top
                verts.AddRange(new[] { vp[16], vp[17], vp[18], vp[19] });
                for (int i = 0; i < 4; ++i) normals[28 + i] = Vector3.up;
                uv[28] = new(0, 1);
                uv[29] = new(1, 1);
                uv[30] = new(0, 1 - t / 4);
                uv[31] = new(1, 1 - t / 4);
                triangles.AddRange(new[] { 28, 30, 29, 29, 30, 31 });

                meshFilter.mesh = new Mesh();
                meshFilter.mesh.SetVertices(verts);
                meshFilter.mesh.SetNormals(new List<Vector3>(normals));
                meshFilter.mesh.SetUVs(0, new List<Vector2>(uv));
                meshFilter.mesh.SetTriangles(triangles, 0);
                meshFilter.mesh.UploadMeshData(false);

                // Use box colliders: left/right sides on Environment, top on Ceiling (walk/crawl/creep exclude it)
                // Left side: full height, left half of frame
                BoxCollider leftCollider = gameObject.AddComponent<BoxCollider>();
                leftCollider.center = new Vector3(-w * 0.75f, h * 0.5f, 0);
                leftCollider.size = new Vector3(w * 0.5f, h, 2 * t);
                
                // Right side: full height, right half of frame
                BoxCollider rightCollider = gameObject.AddComponent<BoxCollider>();
                rightCollider.center = new Vector3(w * 0.75f, h * 0.5f, 0);
                rightCollider.size = new Vector3(w * 0.5f, h, 2 * t);
                
                // Top: horizontal panel at top of frame
                GameObject topGo = new GameObject("DoorFrameTopCollider");
                topGo.transform.SetParent(transform, false);
                topGo.layer = LayerMask.NameToLayer("Ceiling");
                BoxCollider topCollider = topGo.AddComponent<BoxCollider>();
                topCollider.center = new Vector3(0, d + (h - d) / 2, 0);
                topCollider.size = new Vector3(w, h - d, 2 * t);
            }
        }

        // portcullis
        if (GetDoorTypeIndex() == 6)
        {
            return;
        }

        MeshRenderer doorRenderer = door.GetComponent<MeshRenderer>();
        if (doorRenderer != null)
        {
            int doorTypeIndex = GetDoorTypeIndex();
            // 6 is portcullis, 7 is secret door
            int textureIndex = GetTextureIndex(level, doorTypeIndex);

            Material doorMaterial = new Material(shader);
            doorMaterial.SetFloat("_Glossiness", 0.0f);
            if (doorTypeIndex == 7)
            {
                doorMaterial.mainTexture = DataLoader.sDataLoader.wallTex[level.walls[surroundingWallTexture]][0];
            }
            else
            {
                doorMaterial.mainTexture = DataLoader.sDataLoader.doorTex[textureIndex];
            }

            doorRenderer.material = doorMaterial;
            doorRenderer.lightProbeUsage = 0;

            MeshFilter meshFilter = door.GetComponent<MeshFilter>();

            if (meshFilter)
            {
                // create a cube mesh and texture it
                Vector3[] verts = new Vector3[24];
                Vector3[] normals = new Vector3[24];
                Vector2[] uv = new Vector2[24];
                int[] triangles = new int[36];

                normals[0] = Vector3.back;
                normals[4] = Vector3.forward;
                normals[8] = Vector3.left;
                normals[12] = Vector3.right;
                normals[16] = Vector3.up;
                normals[20] = Vector3.down;

                // the door is 52 pixels high
                // to keep the pixel density the same, so that secret doors blend in
                float blankPixels = 12.0f / 64.0f;

                float border = doorTypeIndex == 7 ? 0.25f : 0.0f;
                float voff = doorTypeIndex == 7 ? 1.0f / 64.0f : 0.0f;

                for (int face = 0; face < 6; ++face)
                {
                    int t = 6 * face;
                    int v = 4 * face;
                    triangles[t + 0] = v;
                    triangles[t + 1] = v + 1;
                    triangles[t + 2] = v + 2;
                    triangles[t + 3] = v + 2;
                    triangles[t + 4] = v + 1;
                    triangles[t + 5] = v + 3;
                    normals[v + 1] = normals[v];
                    normals[v + 2] = normals[v];
                    normals[v + 3] = normals[v];

                    Vector3 a = Vector3.zero;
                    Vector3 b = Vector3.zero;
                    Vector3 c = Vector3.zero;

                    switch (face)
                    {
                    case 0:
                        a = Vector3.back;
                        b = Vector3.left;
                        c = Vector3.up;
                        break;
                    case 1:
                        a = Vector3.forward;
                        b = Vector3.right;
                        c = Vector3.up;
                        break;
                    case 2:
                        a = Vector3.left;
                        b = Vector3.up;
                        c = Vector3.back;
                        break;
                    case 3:
                        a = Vector3.right;
                        b = Vector3.up;
                        c = Vector3.forward;
                        break;
                    case 4:
                        a = Vector3.up;
                        b = Vector3.right;
                        c = Vector3.back;
                        break;
                    case 5:
                        a = Vector3.down;
                        b = Vector3.left;
                        c = Vector3.back;
                        break;
                    }

                    normals[v] = a;
                    normals[v + 1] = a;
                    normals[v + 2] = a;
                    normals[v + 3] = a;

                    verts[v] = 0.5f * (a + b + c);
                    verts[v + 1] = 0.5f * (a - b + c);
                    verts[v + 2] = 0.5f * (a + b - c);
                    verts[v + 3] = 0.5f * (a - b - c);

                    int uvSwitch = doorTypeIndex == 7 ? 1 : 0;

                    switch (face)
                    {
                    case 0: // back
                        uv[v] = new Vector2(1 - border, 1 - blankPixels);
                        uv[v + 1] = new Vector2(border, 1 - blankPixels);
                        uv[v + 2] = new Vector2(1 - border, 0);
                        uv[v + 3] = new Vector2(border, 0);
                        break;
                    case 1: // forward
                        uv[v + uvSwitch] = new Vector2(border, 1 - blankPixels);
                        uv[v + 1 - uvSwitch] = new Vector2(1 - border, 1 - blankPixels);
                        uv[v + 2 + uvSwitch] = new Vector2(border, 0);
                        uv[v + 3 - uvSwitch] = new Vector2(1 - border, 0);
                        break;
                    case 2: // left
                    case 3: // right
                    case 4: // up
                    case 5: // down
                        uv[v] = new Vector2(0, 1);
                        uv[v + 1] = new Vector2(1, 1);
                        uv[v + 2] = new Vector2(0, 1 - blankPixels);
                        uv[v + 3] = new Vector2(1, 1 - blankPixels);
                        break;
                    }

                    if (doorTypeIndex == 7)
                    {
                        for (int i = 0; i < 4; ++i)
                        {
                            uv[v + i].y += 1.0f / 64.0f;
                        }
                    }
                }

                meshFilter.mesh = new Mesh();
                meshFilter.mesh.SetVertices(new List<Vector3>(verts));
                meshFilter.mesh.SetNormals(new List<Vector3>(normals));
                meshFilter.mesh.SetUVs(0, new List<Vector2>(uv));
                meshFilter.mesh.SetTriangles(new List<int>(triangles), 0);
                meshFilter.mesh.UploadMeshData(false);
            }
        }
    }

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        // Call base to load names and other UUObject initialization
        base.PostLoadInitialize(restoredFromSave);
        
        // Cache the door's BoxCollider before creating geometry
        cachedDoorCollider = door.GetComponent<BoxCollider>();
        
        // Create door geometry for all doors
        CreateDoorGeometry();
        
        // Set the tile's door reference
        if (initialTile != null)
        {
            initialTile.door = this;
        }

        if (!restoredFromSave)
        {
            // open these doors the other way so they are less likely to trap critters
            // (they currently open up into the neighbouring tile)
            switch (LevelLoader.sLevelLoader.loadedLevel)
            {
            case 3:
                if (objectIndex is 884 or 1011)
                {
                    flags ^= 2;
                }
                break;
            case 4:
                if (objectIndex is 979 or 962 or 951 or 973)
                {
                    flags ^= 2;
                }
                break;
            }
        }
    }

    public override void WorldInitialize(Tile t, int tileX, int tileY)
    {
        if (type >= EObjectType.OpenDoorA)
        {
            // open doors/portcullises are higher than expected
            z -= 24;
            // starts open
            opening = true;
            openAmount = 1.0f;
            type -= 8;
            typeNum -= 8;
        
            // remove the "open" from the description
            if (type == EObjectType.Portcullis)
            {
                singularArticle = "a ";
                singularName = "portcullis";
            }
        }

        base.WorldInitialize(t, tileX, tileY);

        // make sure these are reported as massive so you know they are unbreakable
        if (qualityClass == 3)
        {
            quality = 63;
        }
    }

    public void Spike()
    {
        spiked = true;
    }

    public void Open()
    {
        if (!opening && !spiked)
        {
            opening = true;
            clipToPlay = doorOpenClip;
            TryChainInteraction(EAction.Open);

            // portcullis caging Arial
            if (arialCutscene != null && LevelLoader.sLevelLoader.loadedLevel == 7 && objectIndex == 944)
            {
                PlayerObject.Player.StartCoroutine(ArialFree());
            }

            if (initialTile.hidden)
            {
                initialTile.hidden = false;
                MapScreen.UpdateTile(initialTile);
            }

            if (type is EObjectType.Portcullis)
            {
                ParticleSpawner.SpawnParticle(EParticleType.PortcullisRise, transform.position);
            }
        }
    }

    public void Close()
    {
        if (opening)
        {
            clipToPlay = doorShutClip;
        }

        opening = false;
    }

    public void Toggle()
    {
        if (!opening)
        {
            Open();
        }
        else
        {
            Close();
        }
    }

    public bool isOpen => opening;

    public float OpenAmount => openAmount;

    protected override Vector3 GetKeyholePos()
    {
        return door.transform.position + 0.5f * door.transform.right;
    }

    public override void LockableOpen()
    {
        // only comes from the player, so can be unspiked
        spiked = false;
        Open();
    }

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        if (opening)
        {
            Close();
        }
        else
        {
            // the only door with a link on lv6 is the talking door
            if (isLinked && link != 0 && LevelLoader.sLevelLoader.loadedLevel == 6)
            {
                // chat to the door?
                TryChainInteraction(EAction.Use);
            }
            else
            {
                base.TryInteract(originator, sender, action);
            }
        }
    }

    private string GetBaseLookName()
    {
        if (Physics.Raycast(PlayerObject.Player.mainCamera.transform.position, PlayerObject.Player.mainCamera.transform.forward,
                out RaycastHit objectHit, 10.0f, LayerMasks.EnvironmentAndCeiling))
        {
            if (objectHit.transform.gameObject.tag == "Door")
            {
                return base.GetLookName();
            }
        }
        return null;
    }

    public override string GetLookName()
    {
        string baseName = GetBaseLookName();
        if (baseName != null)
        {
            return baseName;
        }
        // otherwise return the wall type
        // Read from this door's own level, not the currently loaded level
        int stringIndex = 0;
        if (levelIndex >= 1 && levelIndex < LevelLoader.sLevelLoader.levels.Length 
            && LevelLoader.sLevelLoader.levels[levelIndex] != null)
        {
            stringIndex = LevelLoader.sLevelLoader.levels[levelIndex].walls[initialTile.wallTexture];
        }
        return StringLoader.GetString(10, stringIndex);
    }

    public override string GetUnderCursorName()
    {
        // Don't display name for door frames
        return GetBaseLookName();
    }

    public override bool IsDamageable()
    {
        return true; // want to provide sparks when hit
    }

    public override void TryDamage(int damage, Skills.ESkillTestResult result)
    {
        if (quality != 63 && qualityClass < 3) // otherwise unbreakable
        {
            int appliedDamage = damage >> qualityClass;

            // Spawn sparks on the door face closest to the player (front or back only)
            if (cachedDoorCollider != null && ParticleSpawner.sParticleSpawner != null)
            {
                Vector3 playerPosition = PlayerObject.Player.mainCamera.transform.position;
                Vector3 doorCenter = cachedDoorCollider.transform.TransformPoint(cachedDoorCollider.center);
                Vector3 doorSize = Vector3.Scale(cachedDoorCollider.size, cachedDoorCollider.transform.lossyScale);
                
                // Calculate which front/back face is closest to player
                Vector3 toPlayer = playerPosition - doorCenter;
                Vector3 localToPlayer = cachedDoorCollider.transform.InverseTransformDirection(toPlayer);
                
                // Use front or back face based on Z component
                Vector3 faceNormal = localToPlayer.z > 0 ? cachedDoorCollider.transform.forward : -cachedDoorCollider.transform.forward;
                Vector3 faceBase = doorCenter + faceNormal * (doorSize.z * 0.5f);
                
                // Determine if door is wooden based on texture index
                EParticleType particleType = EParticleType.SparkSplat;
                
                int doorTypeIndex = GetDoorTypeIndex();
                // 6 is portcullis, 7 is secret door - always use spark for these
                if (doorTypeIndex != 6 && doorTypeIndex != 7)
                {
                    Level level = GetLevel();
                    if (level != null)
                    {
                        int textureIndex = GetTextureIndex(level, doorTypeIndex);
                        
                        // hardcoded values to identify wooden doors
                        bool isWoodenDoor = textureIndex is 0 or 1 or 2 or 7 or 8;
                        if (isWoodenDoor)
                        {
                            particleType = EParticleType.WoodSplat;
                        }
                    }
                }
                
                // Spawn each particle with randomized position
                int numSparks = Mathf.Min(1 + appliedDamage, 9);
                for (int i = 0; i < numSparks; ++i)
                {
                    // 60-80% up, 25-75% across (X axis) - randomized for each particle
                    float upPercent = Random.Range(0.6f, 0.8f);
                    float acrossPercent = Random.Range(0.25f, 0.75f);
                    Vector3 sparkPosition = faceBase;
                    sparkPosition += cachedDoorCollider.transform.up * (doorSize.y * (upPercent - 0.5f));
                    sparkPosition += cachedDoorCollider.transform.right * (doorSize.x * (acrossPercent - 0.5f));
                    
                    // Push out 0.1m from the face
                    sparkPosition += faceNormal * 0.1f;
                    
                    GameObject particle = ParticleSpawner.SpawnParticle(particleType, sparkPosition);
                    if (particle == null)
                    {
                        Utils.CreateFallbackSplat(sparkPosition, SplatType.Spark);
                    }
                }

                Utils.PlayClip2d(hitSound);
            }

            quality -= appliedDamage;
            if (quality <= 0)
            {
                quality = 0;
                Unlock();
                Open();
            }
        }
    }

    protected override int GetQualityOffset()
    {
        return 0;
    }

    private IEnumerator ArialFree()
    {
        PlayerObject.Player.fadeIn = false;

        while (PlayerObject.Player.fade < 1.0f)
        {
            PlayerObject.Player.fade += Time.unscaledDeltaTime;
            yield return null;
        }

        PlayerObject.Player.fade = 1.0f;

        CutscenePlayer soliloquy = Instantiate(arialCutscene);
        while (soliloquy != null)
        {
            yield return null;
        }
        
        // hide the princess
        (LevelLoader.GetObj(945) as Decal)?.gameObject.SetActive(false);

        // find the arial model and hide that too
        if (Arial.sArial != null)
        {
            Arial.sArial.gameObject.SetActive(false);
        }

        while (PlayerObject.Player.fade > 0.0f)
        {
            PlayerObject.Player.fade -= Time.unscaledDeltaTime;
            yield return null;
        }

        PlayerObject.Player.fade = 0.0f;

        PlayerData.sData.releasedArial = true;

        arialCutscene = null;
    }

    public static void CloseRandomDoors()
    {
        foreach (Door d in FindObjectsByType<Door>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (d.isOpen
                && !LevelLoader.GetLevel().pvs.IsVisible(d.initialTile)
                && Random.value < 0.1)
            {
                if (d.type != EObjectType.Portcullis)
                {
                    d.ClearLockedFlag();
                }
                d.opening = false;
            }
        }
    }
    
    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);
        
        if (data is DoorSaveData doorData)
        {
            doorData.opening = opening;
            doorData.openAmount = openAmount;
        }
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        
        if (data is DoorSaveData doorData)
        {
            opening = doorData.opening;
            openAmount = doorData.openAmount;
        }
    }

    public override ObjectSaveData SaveToData()
    {
        DoorSaveData data = new DoorSaveData();
        PopulateSaveData(data);
        
        return new ObjectSaveData
        {
            objectTypeName = GetType().Name,
            objectType = (int)type,
            objectIndex = objectIndex,
            level = levelIndex,
            jsonData = JsonUtility.ToJson(data)
        };
    }

    public override void LoadFromData(ObjectSaveData objData)
    {
        if (objData == null || string.IsNullOrEmpty(objData.jsonData))
            return;
        
        DoorSaveData data = JsonUtility.FromJson<DoorSaveData>(objData.jsonData);
        RestoreFromSaveData(data);
    }
}
