using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class DoorSaveData : UUObjectSaveData
{
    public bool opening;
    public float openAmount;

    // The spike, which the original keeps in the door's own record (owner set to 63 by UW.EXE
    // 0x36c42) and so in every save, with what the blows have worn of it. The side, the seed, the count
    // and the first nail's place draw the same nails again on a load.
    public bool spiked;
    public int spikeSide;
    public int spikeSeed;
    public int spikeNailCount;
    public bool spikeLockFirst;
    public int spikeWear;
}

public class Door : Lockable
{
    /// <summary>How high a door's opening is above its base: the frame's top starts there.</summary>
    public const float OpeningHeight = 52.0f / 64.0f * 3.0f;

    /// <summary>
    /// The height of the underside of the frame's top. It is on the Ceiling layer, which the
    /// walkers' capsules leave out and a flyer's does not, so a flyer has to pass under it.
    /// </summary>
    public float OpeningTop => transform.position.y + OpeningHeight;

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

    // Which face the door was spiked from, +1 the collider's forward face and -1 the other (0 in a save
    // written before the nails, read as +1), the seed of the nails' places and angles, how many nails
    // it holds - one for each iron spike, ten for each cast of the spell, up to SpikeMaxNails - and
    // whether the first was an iron spike's, which goes by the lock.
    private int spikeSide;
    private int spikeSeed;
    private int spikeNailCount;
    private bool spikeLockFirst;

    // How much of the spike's strength the blows have worn away since the door was last spiked.
    private int spikeWear;

    private const int SpikeNailsBySpell = 10;
    private const int SpikeMaxNails = 20;
    private readonly List<GameObject> spikeNails = new();
    private bool spikeNailsBuilt;

    public override void Update()
    {
        base.Update();

        SyncSpikeNails();

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

            KeepTheTombRoomOpen();
        }
    }
    
    /// <summary>
    /// Level 4: the door of the room at 13,44 will not shut with the player inside it.
    /// </summary>
    /// <remarks>
    /// It is the one door in the game that can wall somebody in. Both switches that work it are
    /// out in the corridor, on the east side of the threshold, and the door is locked - closing
    /// it does not unlock it - so a player inside with the door shut has nothing to open it with
    /// and nothing to walk out through. The original will not close a door on you at all: it
    /// moves a little towards shut and opens again by itself. This is that answer, kept to the
    /// one place where being shut in has no way out rather than changed for every door in the
    /// game, which is where a change like this could do harm.
    /// The room is the floor west of the door, x 10 to 12, y 43 to 45, plus the doorway itself;
    /// the corridor and its two switches are east of it, so operating them still closes the door
    /// behind you when you are out there.
    /// </remarks>
    private void KeepTheTombRoomOpen()
    {
        if (originalLevel != 4 || objectIndex != 748 || opening || openAmount <= 0.1f
            || PlayerObject.Player == null)
        {
            return;
        }

        Tile playerTile = LevelLoader.GetTile(PlayerObject.Player.transform.position);
        if (playerTile == null
            || playerTile.x < 10 || playerTile.x > 13
            || playerTile.y < 43 || playerTile.y > 45)
        {
            return;
        }

        PlayerObject.Rumble(0.2f, 0.2f, 0.2f);
        opening = true;
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
                float d = OpeningHeight;

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

    /// <summary>
    /// Spikes the door shut, with an iron spike or the Strengthen Door spell: no creature opens it or
    /// paths through it, the spike takes the blows before the door does (TryDamage), and the player's
    /// next use, or anything else that opens it, pulls the spike (Open). The nails show, a sight of
    /// this remake's own, between the door and the frame on the side the player stands: an iron
    /// spike drives one, the first by the lock at mid height and the next ones anywhere along the
    /// jambs and the lintel, and the spell ten; the blows knock them out as they wear the spike, the
    /// last one with its last point. A look calls the door spiked (AppendLookAdjectives).
    /// An open door cannot be spiked, as the iron spike already said (message 128), nor a portcullis
    /// or a secret door (CanBeSpiked).
    /// </summary>
    /// <param name="bySpell">The spell, not an iron spike.</param>
    /// <returns>False for a door that cannot be spiked.</returns>
    public bool Spike(bool bySpell)
    {
        if (!CanBeSpiked)
        {
            return false;
        }

        if (!spiked)
        {
            spikeSide = PlayerObject.Player != null ? SideOf(PlayerObject.Player.mainCamera.transform.position) : 1;
            spikeSeed = Random.Range(1, int.MaxValue);
            spikeLockFirst = !bySpell;
        }

        // the nails still standing, and the new ones
        int before = spiked ? StandingSpikeNails : 0;
        spikeNailCount = Mathf.Min(before + (bySpell ? SpikeNailsBySpell : 1), SpikeMaxNails);

        // Every spiking makes the spike whole again, as UW.EXE 0x36c42 (SpikeDoor_seg040_662,
        // USEITEMS.C) sets the door's owner to 63 whether it was spiked or not.
        spikeWear = 0;
        spiked = true;

        // drawn again with the new ones; each nail has its own seed, so the old ones stay put
        DestroySpikeNails();
        return true;
    }

    /// <summary>
    /// A closed door, but not a portcullis or a secret door: the user's choice for this remake. The
    /// original spikes all eight closed doors, 320 to 327 (UW.EXE 0x36c42, SpikeDoor_seg040_662,
    /// USEITEMS.C), the portcullis and the secret door among them.
    /// </summary>
    public bool CanBeSpiked => !opening && type is not (EObjectType.Portcullis or EObjectType.SecretDoor);

    /// <summary>The nails the door holds; a save of the first test build has no count, and held ten.</summary>
    private int SpikeNails => spikeNailCount > 0 ? spikeNailCount : SpikeNailsBySpell;

    /// <summary>
    /// The spike's strength, the damage it takes before the door does: 15 and 2 for each nail, 17
    /// for one iron spike and 35 for the spell. The original gives every spike 31, the five bits above
    /// the spike's own in the door's owner field (63 = 31 &lt;&lt; 1 | 1, UW.EXE 0x36c42); here the
    /// nails count, by design.
    /// </summary>
    private int SpikeStrength => 15 + 2 * SpikeNails;

    /// <summary>
    /// The nails still in the door: as many of them as the strength left is of the whole, rounded
    /// up, so the last one goes only when nothing is left. The last driven go first.
    /// </summary>
    private int StandingSpikeNails => spiked
        ? (SpikeNails * (SpikeStrength - spikeWear) + SpikeStrength - 1) / SpikeStrength
        : 0;

    protected override void AppendLookAdjectives(System.Text.StringBuilder sb)
    {
        // "a sturdy spiked door", in the look and under the cursor. The original says it on a line of
        // its own after the description, "The door is spiked." (block 1, string 131, from UW.EXE
        // 0x7c621, SpecialLook, LOOK.C, for a closed door with the spike's bit set).
        if (spiked && !opening)
        {
            sb.Append("spiked ");
        }
    }

    /// <summary>
    /// The side of the leaf its lock is on, in the leaf's own x: the edge away from the hinge, which
    /// is the edge that swings. A door with no hinge (the portcullis) gets +1.
    /// </summary>
    private float LockEdgeSide()
    {
        if (hinge == null || cachedDoorCollider == null)
        {
            return 1.0f;
        }

        Vector3 h = cachedDoorCollider.transform.InverseTransformPoint(hinge.transform.position) - cachedDoorCollider.center;
        return h.x > 0.0f ? -1.0f : 1.0f;
    }

    /// <summary>+1 when <paramref name="point"/> is on the forward face of the door's leaf, -1 when on the other.</summary>
    private int SideOf(Vector3 point)
    {
        if (cachedDoorCollider == null)
        {
            return 1;
        }

        Transform leaf = cachedDoorCollider.transform;
        Vector3 local = leaf.InverseTransformPoint(point) - cachedDoorCollider.center;
        return local.z >= 0.0f ? 1 : -1;
    }

    /// <summary>
    /// Keeps the nails in step with <see cref="spiked"/>, whatever set or cleared it: built once the
    /// door is spiked and shut (so they are placed on the leaf where it rests), removed when the
    /// spike is pulled.
    /// </summary>
    private void SyncSpikeNails()
    {
        if (spiked && cachedDoorCollider != null && openAmount <= 0.001f
            && (!spikeNailsBuilt || spikeNails.Count != StandingSpikeNails))
        {
            // the blows have knocked some out: the rest are drawn again, each from its own seed
            DestroySpikeNails();
            CreateSpikeNails();
        }
        else if (!spiked && spikeNailsBuilt)
        {
            DestroySpikeNails();
        }
    }

    private void DestroySpikeNails()
    {
        foreach (GameObject nail in spikeNails)
        {
            if (nail != null)
            {
                Destroy(nail);
            }
        }

        spikeNails.Clear();
        spikeNailsBuilt = false;
    }

    /// <summary>
    /// Copies of the iron spike's own model, without its object, on the face the door was spiked
    /// from: the first by the lock at mid height when an iron spike drove it, every other one at
    /// random along the two jambs and the lintel. Each enters the leaf one to three centimetres from
    /// its edge, point first, tilted up to 30 degrees from straight in, in any direction, with most
    /// of it out. Only the nails still standing are drawn (StandingSpikeNails), and each from a seed
    /// of its own, so adding nails or losing them to blows leaves the others put.
    /// </summary>
    private void CreateSpikeNails()
    {
        spikeNailsBuilt = true;

        GameObject prefab = LevelLoader.sLevelLoader != null && LevelLoader.sLevelLoader.objectModelMap != null
            ? LevelLoader.sLevelLoader.objectModelMap.objects[(int)EObjectType.Spike]
            : null;
        MeshFilter source = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
        MeshRenderer sourceRenderer = source != null ? source.GetComponent<MeshRenderer>() : null;
        if (source == null || source.sharedMesh == null || sourceRenderer == null)
        {
            return;
        }

        Mesh mesh = source.sharedMesh;
        SpikeAxis(mesh, out Vector3 tipAxis, out float meshLength);
        Vector3 nailScale = source.transform.lossyScale;
        float length = meshLength * Mathf.Abs(Vector3.Dot(nailScale, Abs(tipAxis)));

        Transform leaf = cachedDoorCollider.transform;
        Vector3 centre = cachedDoorCollider.center;
        Vector3 size = cachedDoorCollider.size;
        float side = spikeSide < 0 ? -1.0f : 1.0f;
        Vector3 inward = leaf.TransformDirection(new Vector3(0.0f, 0.0f, -side)).normalized;
        Vector3 across = Vector3.Cross(inward, leaf.up).normalized;

        for (int i = 0; i < StandingSpikeNails; ++i)
        {
            var rng = new System.Random(unchecked(spikeSeed + 7919 * i));

            // the edge, in the leaf's own space, and the way out of the leaf across it
            Vector3 local;
            Vector3 outLocal;
            if (i == 0 && spikeLockFirst)
            {
                // the lock's jamb, about half way up
                float lockSide = LockEdgeSide();
                float up = (float)(0.04 * (2.0 * rng.NextDouble() - 1.0));
                local = new Vector3(lockSide * 0.5f * size.x, up * size.y, 0.0f);
                outLocal = new Vector3(lockSide, 0.0f, 0.0f);
            }
            else
            {
                // an edge, weighted by its length: the left jamb, the right jamb, the lintel
                double pick = rng.NextDouble() * (2.0 * size.y + size.x);
                float along = (float)(0.1 + 0.8 * rng.NextDouble());
                if (pick < 2.0 * size.y)
                {
                    float x = pick < size.y ? -1.0f : 1.0f;
                    local = new Vector3(0.5f * x * size.x, (along - 0.5f) * size.y, 0.0f);
                    outLocal = new Vector3(x, 0.0f, 0.0f);
                }
                else
                {
                    local = new Vector3((along - 0.5f) * size.x, 0.5f * size.y, 0.0f);
                    outLocal = Vector3.up;
                }
            }

            local.z = side * 0.5f * size.z;
            local += centre;
            Vector3 outward = leaf.TransformDirection(outLocal).normalized;

            // in through the face one to three centimetres inside the leaf's edge
            float inset = (float)(0.01 + 0.02 * rng.NextDouble());
            Vector3 entry = leaf.TransformPoint(local) - outward * inset;

            // the point tilted 0 to 30 degrees from straight in, towards any direction, and the nail
            // turned at random about its own length
            Vector3 tiltAxis = Quaternion.AngleAxis((float)(rng.NextDouble() * 360.0), inward) * across;
            Vector3 tipDirection = Quaternion.AngleAxis((float)(rng.NextDouble() * 30.0), tiltAxis) * inward;
            Quaternion rotation = Quaternion.AngleAxis((float)(rng.NextDouble() * 360.0), tipDirection)
                                  * Quaternion.FromToRotation(tipAxis, tipDirection);

            // three fifths of it outside the face: the centre a tenth of a length out
            Vector3 position = entry - tipDirection * (0.1f * length);

            var nail = new GameObject("SpikeNail");
            nail.transform.SetParent(transform, false);
            nail.transform.SetPositionAndRotation(position, rotation);
            Vector3 parentScale = transform.lossyScale;
            nail.transform.localScale = new Vector3(nailScale.x / parentScale.x, nailScale.y / parentScale.y,
                nailScale.z / parentScale.z);
            nail.AddComponent<MeshFilter>().sharedMesh = mesh;
            nail.AddComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
            spikeNails.Add(nail);
        }
    }

    private static Vector3 Abs(Vector3 v)
    {
        return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    private static Mesh spikeAxisMesh;
    private static Vector3 spikeAxisTip;
    private static float spikeAxisLength;

    /// <summary>
    /// The spike model's length and which way its point faces, in the mesh's own space: the length
    /// is the longest side of its bounds, and the point is the end whose vertices stay closer to the
    /// axis. Read from the mesh (its import keeps it readable) rather than assumed from the export.
    /// </summary>
    private static void SpikeAxis(Mesh mesh, out Vector3 tip, out float length)
    {
        if (spikeAxisMesh == mesh)
        {
            tip = spikeAxisTip;
            length = spikeAxisLength;
            return;
        }

        Bounds bounds = mesh.bounds;
        Vector3 extent = bounds.size;
        int axis = extent.x >= extent.y && extent.x >= extent.z ? 0 : (extent.y >= extent.z ? 1 : 2);
        length = extent[axis];
        float band = 0.2f * length;
        double spreadLow = 0.0, spreadHigh = 0.0;
        int countLow = 0, countHigh = 0;
        foreach (Vector3 v in mesh.vertices)
        {
            Vector3 off = v - bounds.center;
            off[axis] = 0.0f;
            if (v[axis] <= bounds.min[axis] + band)
            {
                spreadLow += off.magnitude;
                ++countLow;
            }
            else if (v[axis] >= bounds.max[axis] - band)
            {
                spreadHigh += off.magnitude;
                ++countHigh;
            }
        }

        bool pointHigh = countLow == 0 || countHigh == 0 || spreadHigh / countHigh <= spreadLow / countLow;
        tip = Vector3.zero;
        tip[axis] = pointHigh ? 1.0f : -1.0f;

        spikeAxisMesh = mesh;
        spikeAxisTip = tip;
        spikeAxisLength = length;
    }

    /// <param name="byPlayer">The player opens it, by a use or by breaking it: only then does the
    /// door's open trigger fire.</param>
    /// <remarks>
    /// Whoever opens the door pulls its spike - the player, a lever or trigger, a conversation, a
    /// blow that breaks it - as UW.EXE 0x387c6 (OpenDoor, USEITEMS.C) clears the spike's bit for every
    /// caller. Only a creature's own use is refused a spiked door (UW.EXE 0x38115, UseRect), which
    /// here is Critter's test of spiked before it calls this.
    /// </remarks>
    public void Open(bool byPlayer = false)
    {
        if (!opening)
        {
            spiked = false;
            opening = true;
            clipToPlay = doorOpenClip;
            if (byPlayer)
            {
                FireOpenTrigger();
            }

            // portcullis caging Arial
            if (arialCutscene != null && LevelLoader.sLevelLoader.loadedLevel == 7 && objectIndex == 944)
            {
                PlayerObject.Player.StartCoroutine(ArialFree());
            }

            if (initialTile.hidden)
            {
                MapScreen.RevealTile(initialTile);
            }

            if (type is EObjectType.Portcullis)
            {
                ParticleSpawner.SpawnParticle(EParticleType.PortcullisRise, transform.position);
            }
        }
    }

    /// <summary>
    /// The opening sends event 7 down the door's list, to its first trap or trigger, past the lock
    /// (UW.EXE 0x387c6 -> 0x385d6), and only an open trigger answers it. The trigger takes the player
    /// alone (w0 bit 11; a creature is refused at 0x83c56), so a door a creature or a lever opens does
    /// not fire it and it stays armed. The game has five, each right behind a lock: two goblin doors
    /// of level 1, the prisoner's cell of level 3 and two portcullises of level 7. Unlocking sends
    /// event 6, which they ignore.
    /// </summary>
    private void FireOpenTrigger()
    {
        UUObject obj = link != 0 ? LevelLoader.GetObj(link) : null;
        while (obj != null && obj is not (Trigger or Trap))
        {
            obj = obj.chainIndex != 0 ? LevelLoader.GetObj(obj.chainIndex) : null;
        }

        if (obj is Trigger trigger)
        {
            trigger.TryInteract(this, this, EAction.Open);
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
        Open(true);
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

            // The spike takes the blow first, and while it has strength left the door loses nothing
            // and cannot break: UW.EXE 0x25f97 (damage_object, DAMAGE.C), after the shift by the
            // quality class, and what is left of a blow over the spike's strength is lost. So a
            // strength of 35 is 35 points of a weapon's damage on a door of class 0, 70 on one of
            // class 1, 140 on one of class 2.
            if (spiked && spikeWear < SpikeStrength)
            {
                spikeWear = Mathf.Min(SpikeStrength, spikeWear + appliedDamage);

                // The last nail knocked out, the spike is gone: the door is no longer spiked and
                // the next blow is its own. In the original the spike's bit stays set at 0, so a
                // creature still cannot open the door and a look still calls it spiked.
                if (spikeWear >= SpikeStrength)
                {
                    spiked = false;
                }

                return;
            }

            quality -= appliedDamage;
            if (quality <= 0)
            {
                quality = 0;
                Unlock();
                // Only the player breaks doors here; in the original his blow, missile or spell
                // opens a broken door as he would (UW.EXE 0x25c86 -> 0x387c6).
                Open(true);
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

    /// <summary>
    /// Closes some of the open doors while the player sleeps (UW.EXE 0x850c4, called by the sleep with 0):
    /// each one with a chance of 3 in 10, at any distance, except those on a tile with bit 15 set, the high
    /// bit of doorFrob, which the original leaves as the player left them.
    /// </summary>
    public static void CloseDoorsInSleep()
    {
        foreach (Door d in FindObjectsByType<Door>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (d.isOpen
                && (d.initialTile == null || (d.initialTile.doorFrob & 2) == 0)
                && Random.Range(0, 10) < 3)
            {
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
            doorData.spiked = spiked;
            doorData.spikeSide = spikeSide;
            doorData.spikeSeed = spikeSeed;
            doorData.spikeNailCount = spikeNailCount;
            doorData.spikeLockFirst = spikeLockFirst;
            doorData.spikeWear = spikeWear;
        }
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        
        if (data is DoorSaveData doorData)
        {
            opening = doorData.opening;
            openAmount = doorData.openAmount;
            spiked = doorData.spiked;
            spikeSide = doorData.spikeSide;
            spikeSeed = doorData.spikeSeed;
            spikeNailCount = doorData.spikeNailCount;
            spikeLockFirst = doorData.spikeLockFirst;
            spikeWear = doorData.spikeWear;
            if (spikeNailsBuilt)
            {
                DestroySpikeNails();      // drawn again by Update() from the restored seed
            }

            // a secret door saved open was found: the level's loading hid its tile again
            if (opening && initialTile != null && initialTile.hidden)
            {
                MapScreen.RevealTile(initialTile);
            }
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
