using System;
using UnityEngine;
using System.Collections.Generic;
using System.Text;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

[System.Serializable]
public class UUObjectSaveData : LevelObjectSaveData
{
    public int objectType;
    public int typeNum;
    public int option;
    public int flags;
    public bool isEnchanted;
    public bool isTurned;
    public bool isInvisible;
    public bool isLinked;
    public int z;
    public int angle;
    public int x;
    public int y;
    public int quality;
    public int qualityClass;
    public int chainIndex;
    public int ownerIndex;
    public int special;
    public bool used;
    public bool isStatic;
    public bool alignToFloor;
    public int loreResult;
    public int loreResultLoreLevel;
    public string enchantmentName;
    public int quantity;
    public List<ObjectSaveData> contents;
    
    // Rigidbody state
    public bool hasRigidbody;
    public Vector3 velocity;
    public Vector3 angularVelocity;
    public bool useGravity;
}

public enum EAction
{
    Look,
    Use,
    Pickup,
    Open,
    Unlock,
    Trigger
}

public enum EEquipAction
{
    Nothing,
    Equip,
    Use,
    Consume
}

[SelectionBase]
public class UUObject : LevelObject
{
    [FilterableEnumList]
    public EObjectType type;
    public int typeNum;
    public int option;
    public int flags;
    public bool isEnchanted;
    public bool isTurned;
    public bool isInvisible;
    public bool isLinked;
    public int z;
    public int angle;
    public int x;
    public int y;
    public int quality;
    public int qualityClass;
    public int chainIndex;
    public int ownerIndex;
    public int special;

    public Renderer cachedRenderer;
    public List<UUObject> contents;

    [Tooltip("If true, this object is considered incidental (e.g. grass, rubble) and will be lower priority than non-incidental objects when selecting what is under the cursor.")]
    public bool isIncidental;

    public bool used;
    public bool isStatic;
    public bool alignToFloor;

    public Skills.ESkillTestResult loreResult; // success = magical?, critical success = identified
    public int loreResultLoreLevel = -1; // the lore skill level at which you obtained the loreResult
    
    private readonly Collider[] cachedColliders = new Collider[16];
    
    // Precached StringBuilder for building lookat names and object descriptions
    private static readonly StringBuilder lookNameBuilder = new StringBuilder(128);

    public string enchantmentName;

    public string singularArticle;
    public string singularName;
    public string pluralArticle;
    public string pluralName;

    public Texture2D inventoryTex;

    public enum EMajorClass
    {
        WeaponsAndArmour,
        Critters,
        ContainersAndFood,
        SceneryAndRunes,
        KeysAndBooks,
        Environment,
        TrapsAndTriggers,
        Misc
    }

    public enum EClass
    {
        Weapons, // 0
        Missiles,
        Armour,
        Armour2,

        CrittersA, // 64
        CrittersB,
        CrittersC,
        CrittersD,

        Containers, // 128
        LampsAndWands,
        Treasure,
        Comestibles,

        SceneryA, // 192
        SceneryB,
        RunesA,
        RunesB,

        Keys, // 256
        QuestItems,
        MiscA,
        Books,

        Doors, // 320
        Furniture,
        Decor,
        Switches,

        TrapsA, // 384
        TrapsB,
        TriggersA, // 416
        TriggersB,

        MiscB,
        UnusedA,
        UnusedB,
        UnusedC
    }

    // looking at the three buttons in level 3, they're evenly spaced, using 1, 3, 5.
    // then decals and doors are centered when x or y is 3 or 4, and set against walls at 0 and 7.
    // so I think it's supposed to look like this (these are divided by 8).
    // it doesn't work for signs though, so I think it might be object dependent :|
    // also doesn't quite work for buttons - see the emerald/vas rune puzzle on lv6
    private static readonly float[] skPartialDoor = { 0.05f * 8.0f / 3.0f, 2, 3, 4, 4, 6, 7, 8 - 0.05f * 8.0f / 3.0f };
    private static readonly float[] skPartial = { 1 / 3.0f, 2, 3, 4, 4, 6, 7, 8 - 1.0f / 3.0f };
    private static readonly float[] skPartialPortcullis = { 0.1f * 8.0f / 3.0f, 2, 3, 4, 4, 6, 7, 8 - 0.1f * 8.0f / 3.0f };
    private static readonly float[] skPartialDecal = { 1 / 3.0f, 1, 2, 4, 4, 6, 7, 8 - 1.0f / 3.0f };
    protected const float xzScale = 3.0f;
    protected const float yScale = 3.0f / 32.0f;

    public virtual void Initialize(ushort[] objData, byte[] critterData)
    {
        ushort s0 = objData[0];
        ushort s1 = objData[1];
        ushort s2 = objData[2];
        ushort s3 = objData[3];

        typeNum = s0 & 511;
        type = (EObjectType)typeNum;
        option = (s0 >> 9) & 7;
        flags = (s0 >> 9) & 15;
        isEnchanted = (s0 & (1 << 12)) > 0;
        isTurned = (s0 & (1 << 13)) > 0;
        isInvisible = (s0 & (1 << 14)) > 0;
        isLinked = (s0 & (1 << 15)) == 0;

        z = s1 & 127;
        angle = (s1 >> 7) & 7;
        y = (s1 >> 10) & 7;
        x = (s1 >> 13) & 7;

        quality = s2 & 63;
        chainIndex = (s2 >> 6) & 1023;

        ownerIndex = s3 & 63;
        special = (s3 >> 6) & 1023;

        qualityClass = (DataLoader.sDataLoader.comObjProps[(int)type].qualityClass >> 2) & 3;
    }
    
    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);

        cachedRenderer = GetComponentInChildren<MeshRenderer>();

        string s = StringLoader.GetString(4, (int)type);
        string[] sp = s.Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (sp.Length == 0)
        {
            singularArticle = "";
            singularName = s;
        }
        else if (sp[0].Length > 0)
        {
            string[] an = sp[0].Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (an.Length == 2)
            {
                singularArticle = an[0] + " ";
                singularName = an[1];
            }
            else
            {
                singularArticle = "";
                singularName = an[0];
            }
        }

        if (sp.Length > 1)
        {
            string[] an = sp[1].Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (an.Length == 2)
            {
                pluralArticle = an[0] + " ";
                pluralName = an[1];
            }
            else
            {
                pluralArticle = "";
                pluralName = an[0];
            }
        }
        else
        {
            pluralArticle = "";
            pluralName = singularName + "s";
        }

        inventoryTex = GetInventoryTex();
        
        // Recursively initialize container contents
        if (contents != null && contents.Count > 0)
        {
            foreach (UUObject contentObj in contents)
            {
                if (contentObj != null)
                {
                    contentObj.PostLoadInitialize(restoredFromSave: restoredFromSave);
                }
            }
        }
    }

    public virtual void WorldInitialize()
    {
        int tx = Tile.GetTileX(transform.position.x);
        int ty = Tile.GetTileY(transform.position.z);
        Tile t = LevelLoader.GetTile(tx, ty);
        WorldInitialize(t, tx, ty);
    }

    public virtual void WorldInitialize(Vector3 pos)
    {
        int tx = Tile.GetTileX(pos.x);
        int ty = Tile.GetTileY(pos.z);
        Tile t = LevelLoader.GetTile(tx, ty);

        x = Tile.GetSubTileX(pos.x);
        y = Tile.GetSubTileY(pos.z);
        z = (int)(pos.y / yScale);

        WorldInitialize(t, tx, ty);
    }

    private void positionInWorldUsingSpawnLocation(float radiusMultiplier, bool castToFloor)
    {
        if (initialTile.type == 0)
        {
            Rigidbody rb = GetComponentInChildren<Rigidbody>();
            if (rb != null)
            {
                Debug.Log($"Level {LevelLoader.sLevelLoader.loadedLevel}: {name} outside world. Removing rigid body.");
                Destroy(rb);
            }
        }

        float[] partial = skPartial;
        if (getClass == EClass.Doors)
        {
            partial = type is EObjectType.Portcullis or EObjectType.OpenPortcullis ? skPartialPortcullis : skPartialDoor;
        }
        else if (type == EObjectType.DecalWithCollision)
        {
            partial = skPartialDecal;
        }

        float ox = xzScale * (initialTile.x + partial[x] / 8.0f);
        float oz = xzScale * (initialTile.y + partial[y] / 8.0f);
        float floorHeight = initialTile.GetFloorY(ox, oz);
        float oy = yScale * z;
        if (type == EObjectType.Decal || type == EObjectType.DecalWithCollision)
        {
            // place these flush. the model is bowed out
            if (x == 0 || x == 7)
            {
                ox = xzScale * (initialTile.x + (x == 0 ? 0 : 1));
            }

            if (y == 0 || y == 7)
            {
                oz = xzScale * (initialTile.y + (y == 0 ? 0 : 1));
            }
            // and don't modify y
        }
        else
        {
            if (z > 0)
            {
                oy = Mathf.Max(yScale * z, floorHeight);
            }
        }

        transform.position = new Vector3(ox, oy, oz);

        if (radiusMultiplier > 1.0f)
        {
            Debug.DrawLine(transform.position - 0.2f * Vector3.left, transform.position + 0.2f * Vector3.left,
                Color.red, 30.0f);
            Debug.DrawLine(transform.position - 0.2f * Vector3.back, transform.position + 0.2f * Vector3.back,
                Color.red, 30.0f);
        }

        transform.rotation = Quaternion.AngleAxis(45.0f * angle, Vector3.up);
        gameObject.SetActive(true);

        if (!isStatic && 8 * (initialTile.floorHeight + 1) >= z && castToFloor) // close to the floor
        {
            // first check for actual floor height from the tile (could be a slope)
            oy = floorHeight;
            transform.position = new Vector3(ox, oy, oz);
            // move up a bit if we have a rigidbody
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                Collider childCollider = GetComponentInChildren<Collider>();
                if (childCollider != null)
                {
                    transform.position += radiusMultiplier * childCollider.bounds.extents.magnitude * Vector3.up;
                }

                // find normal and move away from diagonal wall
                if (initialTile.type is >= 2 and <= 5)
                {
                    Vector3 normal = Vector3.up;
                    switch (initialTile.type)
                    {
                    case 2: // open to SE
                        normal = Vector3.right + Vector3.back;
                        break;
                    case 3: // open to SW
                        normal = Vector3.left + Vector3.back;
                        break;
                    case 4: // open to NE
                        normal = Vector3.right + Vector3.forward;
                        break;
                    case 5: // open to NW
                        normal = Vector3.left + Vector3.forward;
                        break;
                    }

                    normal *= 0.3f;

                    if (Physics.Raycast(transform.position + normal, -normal.normalized, out RaycastHit hit, 0.8f,
                            LayerMasks.EnvironmentAndCeiling))
                    {
                        transform.position = hit.point + normal;
                    }
                }

                if (childCollider != null)
                {
                    if (rb.SweepTest(Vector3.down, out RaycastHit hit, 2.0f * radiusMultiplier * childCollider.bounds.extents.magnitude,
                            QueryTriggerInteraction.Ignore))
                    {
                        transform.position += hit.distance * Vector3.down;
                    }
                }
            }
            else if (gameObject.layer != LayerMask.NameToLayer("Environment"))
            {
                int layerMask = LayerMasks.EnvironmentAndCeiling;
                if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit r, 2.0f,
                        LayerMasks.EnvironmentOnly))
                {
                    transform.position = r.point;
                }
            }
        }
        else if (isStatic && alignToFloor)
        {
            // ignore self
            MeshCollider meshCollider = gameObject.GetComponentInChildren<MeshCollider>();
            if (meshCollider != null)
            {
                meshCollider.gameObject.SetActive(false);
            }
            int layerMask = LayerMasks.EnvironmentAndCeiling;
            if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit r, 2.0f,
                    layerMask))
            {
                transform.up = r.normal;
            }

            if (meshCollider != null)
            {
                meshCollider.gameObject.SetActive(true);
            }
        }
    }

    public virtual void WorldInitialize(Tile t, int tileX, int tileY)
    {
        initialTile = t;

        bool castToFloor = true;
        
        // patch position of certain objects
        switch (LevelLoader.sLevelLoader.loadedLevel)
        {
        case 1:
            switch (objectIndex)
            {
            case 885: // move drain so it's centered in a tile (necessary for 3d decals cut into wall)
                y = 4;
                break;
            case 665: // move plaque out from behind fountain so it's easier to read
                x = 7;
                z = 56;
                break;
            }
            break;
        case 2:
            switch (objectIndex)
            {
            case 823: // move chest away from the wall so its lid can open
                y = 5;
                break;
            }
            break;
        case 3:
            switch (objectIndex)
            {
            case 926: // excess waterfall decal
                z = 0;
                break;
            case 534: // waterfall decal not positioned correctly over the gap (was 5)
                y = 3;
                break;
            case 538: // the move trigger should also be centered on the gap (was 0)
                y = 3;
                break;
            case 539: // change terrain trap too low (was 88), so move it up
                z = 96;
                break;
            case 729: // stone wall
                z = 64;
                break;
            case 678: // stone wall
                z = 32;
                break;
            case 679: // axe at useless switch
                x = 6;
                y = 5;
                break;
            }
            break;
        case 4:
            switch (objectIndex)
            {
            case 867: // chest is facing the wall, so rotate 180
                angle = (angle + 4) % 7;
                break;
            case 914: // move ring out from under rock
                x = 0;
                break;
            case 685: // gold chain on same tile as static debris — raycast snaps to floor under pile; spawn above and drop
                castToFloor = false;
                z = 98;
                break;
            }
            break;
        case 6:
            switch (objectIndex)
            {
            case 586: // a wand hidden in a pile of debris
                x = 5;
                break;
            case 1011: // stairway leading down
                y = 7;
                break;
            }
            break;
        case 7:
            switch (objectIndex)
            {
            case 976: // caved in passageway
                // so it doesn't stick up above the ceiling when casting roaming sight
                z = 96;
                break;
            }
            break;
        case 8:
            switch (objectIndex)
            {
            case 761: // ring buried in debris
                z = 2;
                castToFloor = false;
                break;
            }
            break;
        }

        positionInWorldUsingSpawnLocation(1.0f, castToFloor);

        LevelLoader.AddToWorld(this);
        levelIndex = LevelLoader.sLevelLoader.loadedLevel;
    }

    public virtual void Update()
    {
        if (transform.position.y < -1.0f)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
            }

            if (initialTile != null)
            {
                positionInWorldUsingSpawnLocation(2.0f, true);
                Vector3 offset = Random.insideUnitCircle;
                transform.position += new Vector3(offset.x, 0.0f, offset.y);
                Debug.LogFormat(gameObject, $"Level {LevelLoader.sLevelLoader.loadedLevel}: {name} falling out of world. Respawning.");
            }
            else if (rb != null)
            {
                // stop it from falling
                Vector3 pos = transform.position;
                pos.y = 0.0f;
                transform.SetPositionAndRotation(pos, transform.rotation);
                Destroy(rb);
                Debug.LogFormat(gameObject, $"Level {LevelLoader.sLevelLoader.loadedLevel}: {name} fell out of world with no initial tile. Removing rigid body.");
            }
        }
    }

    /// <summary>
    /// Snaps a wall-mounted object to the tile boundary indicated by its <see cref="angle"/> and <see cref="initialTile"/>.
    /// When the tile has a door on the same wall axis, snaps to the door frame face instead so signs
    /// above doorways are not pushed through to the far tile edge.
    /// This avoids physics casts (raycasts) and matches the optimized behavior used by decals.
    /// </summary>
    protected void SnapToWallUsingTileBoundaries()
    {
        if (initialTile == null)
        {
            return;
        }

        Vector3 pos = transform.position;

        bool isDiagonal = (angle & 1) == 1;
        if (isDiagonal)
        {
            // Project current position onto the diagonal line perpendicular to the forward vector
            float offset = Vector3.Dot(transform.forward, pos - initialTile.GetGridCenter());
            transform.position = pos - offset * transform.forward;
            return;
        }

        Door door = FindDoorOnTile(initialTile);
        // Sharing an axis is not enough: north and south are the same axis but opposite edges of
        // the tile, and a control on one of them has nothing to do with a door on the other. The
        // sub tile coordinate says which edge each of them is actually on - 0 to 3 is the low
        // side, 4 to 7 the high side - so require them to agree before treating the door's face
        // as the surface to mount on.
        // Without this, the lever in the secret door niche on level 3 (tile 52,13: door at y = 0
        // facing south, lever at y = 7 facing north) is dragged three metres across the tile onto
        // the outside of the door, and ends up hanging in the corridor once the niche opens.
        bool doorSharesWall = door != null
            && ((angle == 0 || angle == 4) && (door.angle == 0 || door.angle == 4)
                    && SameTileEdge(y, door.y)
                || (angle == 2 || angle == 6) && (door.angle == 2 || door.angle == 6)
                    && SameTileEdge(x, door.x));

        if (doorSharesWall)
        {
            // Match Door.CreateDoorGeometry frame half-thickness. Forward points into the wall, so the
            // readable face is on the opposite side of the door center (away along -forward).
            float t = door.type is EObjectType.Portcullis or EObjectType.OpenPortcullis ? 0.1f : 0.05f;
            Vector3 doorFace = door.transform.position - transform.forward * t;
            switch (angle)
            {
            case 0: // North
            case 4: // South
                pos.z = doorFace.z;
                break;
            case 2: // East
            case 6: // West
                pos.x = doorFace.x;
                break;
            }
        }
        else
        {
            // For non-diagonal walls, snap to the tile edge in the direction of the wall normal.
            // Forward vector points into the wall, so we snap to the edge it's pointing toward.
            switch (angle)
            {
            case 0: // North -> snap to north edge (maxZ)
                pos.z = (initialTile.y + 1) * Tile.xzScale;
                break;
            case 2: // East -> snap to east edge (maxX)
                pos.x = (initialTile.x + 1) * Tile.xzScale;
                break;
            case 4: // South -> snap to south edge (minZ)
                pos.z = initialTile.y * Tile.xzScale;
                break;
            case 6: // West -> snap to west edge (minX)
                pos.x = initialTile.x * Tile.xzScale;
                break;
            }
        }

        transform.position = pos;
    }

    /// <summary>
    /// Finds a door on the tile. Prefers <see cref="Tile.door"/> when already set; otherwise walks the
    /// object chain (needed when this runs before Door.PostLoadInitialize assigns the tile reference).
    /// </summary>
    /// <summary>
    /// Whether two sub tile coordinates fall on the same half of the tile, and so on the same
    /// wall of the pair that share an axis.
    /// </summary>
    private static bool SameTileEdge(int a, int b)
    {
        return (a < 4) == (b < 4);
    }

    private static Door FindDoorOnTile(Tile tile)
    {
        if (tile == null)
        {
            return null;
        }

        if (tile.door != null)
        {
            return tile.door;
        }

        UUObject tileObj = LevelLoader.GetObj(tile.firstObject);
        while (tileObj != null)
        {
            if (tileObj.getClass == EClass.Doors)
            {
                return tileObj.GetComponent<Door>();
            }

            tileObj = tileObj.chainIndex > 0 ? LevelLoader.GetObj(tileObj.chainIndex) : null;
        }

        return null;
    }

    protected void TryStolen()
    {
        // seems the ownership is pretty broad - just a race
        if (DataLoader.sDataLoader.comObjProps[(int)type].canBeOwned || type == EObjectType.DoTrap)
        {
            int race = ownerIndex & 31;
            if (race != 0)
            {
                // max distance is < 4 tiles
                int count = Physics.OverlapSphereNonAlloc(transform.position, 4.0f * xzScale,
                             cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
                for (int i = 0; i < count; i++)
                {
                    Collider col = cachedColliders[i];
                    if (col is CharacterController)
                    {
                        UUObject obj = col.transform.root.gameObject.GetComponent<UUObject>();
                        obj.StolenFrom(this, race);
                    }
                }
            }
        }

        if (LevelLoader.sLevelLoader.loadedLevel == 2)
        {
            Tile t = LevelLoader.GetClosestTile(PlayerObject.Player.mainCamera.transform.position);
            if (t.x is >= 4 and <= 10 && t.y is >= 20 and <= 26)
            {
                if (type is EObjectType.Coin or EObjectType.GoldCoin or EObjectType.GoldChain or EObjectType.GoldPlate or EObjectType.Sceptre)
                {
                    PlayerData.sData.raidedTreasury = true;
                }
            }
        }
    }

    protected void FirePickupTriggers(UUObject originator)
    {
        // find pickup triggers in the contents
        if (contents != null)
        {
            for (int i = 0; i < contents.Count; ++i)
            {
                Trigger trig = contents[i] as Trigger;
                if (trig != null)
                {
                    trig.TryInteract(originator, this, EAction.Pickup);
                    contents.RemoveAt(i);
                    trig.gameObject.SetActive(false);
                    break;
                }
            }
        }
    }

    public virtual void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        if (sender == null && action == EAction.Use) // change to pickup...
        {
            if (isPortable)
            {
                if (quantity > 1)
                {
                    // pop up the how many dialog 
                    HowMany.AskHowMany(EHowManyReason.Pickup, this);
                    return;
                }
                else
                {
                    if (Interaction.sInt != null
                        && (Interaction.sInt.MouseLeftHoldPortablePickupUse
                            || Interaction.sInt.TryConsumeWorldPickupIntentForImmediatePickup()))
                    {
                        TryStolen();
                        FirePickupTriggers(originator);
                        gameObject.SetActive(false);
                        LevelLoader.worldObj.Remove(this);
                        Utils.PlayClip2d(PlayerObject.Player.pickupClip);
                        RemoveFromLevelObjectsRecursive();
                        Inventory.sInv.BeginWorldPickupFromGround(this);
                        Interaction.sInt.ClearWorldPickupIntentFlags();
                        return;
                    }

                    TryStolen();
                    FirePickupTriggers(originator);
            
                    gameObject.SetActive(false);
                    Inventory.Add(this);
                    Utils.PlayClip2d(PlayerObject.Player.pickupClip);
                    LevelLoader.worldObj.Remove(this);
                    
                    // Remove from level.objects[] array recursively (handles containers and their contents)
                    RemoveFromLevelObjectsRecursive();
                    if (GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
                    {
                        TutorialManager.NotifyPickup();
                    }
                }
            }
            else if (!isLinked || link == 0 || stackable)
            {
                Messages.Add(1, 96); // can't pick up
            }
        }

        // only check for linked when interacting - not for example entering a move trigger
        if (isLinked)
        {
            TryChainInteraction(action);
        }
    }

    public void PickUpSome(int howMany)
    {
        PickUpSome(howMany, false);
    }

    /// <param name="worldDragToHandOnly">From right-hold/drag pickup: hold in cursor only; do not merge or add to inventory until placed.</param>
    public void PickUpSome(int howMany, bool worldDragToHandOnly)
    {
        if (howMany == 0)
            return;

        TryStolen();
        FirePickupTriggers(null);
        Utils.PlayClip2d(PlayerObject.Player.pickupClip);

        if (worldDragToHandOnly)
        {
            if (howMany == quantity)
            {
                gameObject.SetActive(false);
                LevelLoader.worldObj.Remove(this);
                RemoveFromLevelObjectsRecursive();
                Inventory.sInv.BeginWorldPickupFromGround(this);
            }
            else
            {
                UUObject obj = LevelLoader.CreateObjectOfType(type);
                obj.quality = quality;
                obj.flags = flags;
                obj.ownerIndex = ownerIndex;
                obj.quantity = howMany;
                obj.PostLoadInitialize();
                quantity -= howMany;
                Inventory.sInv.BeginWorldPickupFromGround(obj);
            }
            TutorialManager.NotifyPickup();
            return;
        }

        if (howMany == quantity)
        {
            gameObject.SetActive(false);
            LevelLoader.worldObj.Remove(this);
            Inventory.Add(this);
            RemoveFromLevelObjectsRecursive();
        }
        else
        {
            UUObject obj = LevelLoader.CreateObjectOfType(type);
            obj.quality = quality;
            obj.flags = flags;
            obj.ownerIndex = ownerIndex;
            obj.quantity = howMany;
            obj.PostLoadInitialize();
            Inventory.Add(obj);
            quantity -= howMany;
        }

        TutorialManager.NotifyPickup();
    }

    public void TryChainInteraction(EAction action)
    {
        if (link != 0 && !stackable)
        {
            UUObject chained = LevelLoader.GetObj(link);
            if (chained != null)
            {
                chained.TryInteract(this, this, action);
            }
        }
    }
    
    protected void TryChainInteraction(UUObject originator, UUObject sender, EAction action)
    {
        if (link != 0 && !stackable)
        {
            UUObject chained = LevelLoader.GetObj(link);
            if (chained != null)
            {
                chained.TryInteract(originator, this, action);
            }
        }
    }

    protected void TryChainLinkedInteraction(EAction action)
    {
        if (isLinked && link != 0)
        {
            UUObject chained = LevelLoader.GetObj(link);
            if (chained != null)
            {
                chained.TryInteract(this, this, action);
            }
        }
    }

    /// <summary>Inventory / paperdoll primary action — same as gamepad A on an item (inspect by default).</summary>
    public virtual void TryInventoryUse()
    {
        Messages.Add($"You see {GetLookName()}.");
    }

    /// <summary>Inventory / paperdoll secondary action — mouse RMB; default none.</summary>
    public virtual void TryInventorySecondaryUse()
    {
    }

    public bool ContainsRecursive(EObjectType typeId)
    {
        bool found = false;
        if (type == typeId)
        {
            found = true;
        }
        else if (contents != null)
        {
            foreach (UUObject obj in contents)
            {
                if (obj.ContainsRecursive(typeId))
                {
                    found = true;
                    break;
                }
            }
        }

        return found;
    }

    public void GetAllContents(List<UUObject> allContents)
    {
        if (contents != null && contents.Count > 0)
        {
            allContents.AddRange(contents);
            foreach (UUObject item in contents)
            {
                item.GetAllContents(allContents);
            }
        }
    }
    
    /// <summary>
    /// Removes this object from the level's objects[] array.
    /// Once picked up, an object can't be referenced by scripting.
    /// </summary>
    private void RemoveFromLevelObjects()
    {
        if (objectIndex > 0 && levelIndex == LevelLoader.sLevelLoader.loadedLevel)
        {
            if (LevelLoader.sLevelLoader.levels[levelIndex] != null)
            {
                // Only clear the slot for a portable object that actually owns it. Non-portable
                // script objects (locks, triggers, doors) are never picked up and must never be
                // blanked, even if a foreign object happens to share this objectIndex.
                if (isPortable && LevelLoader.GetObj(objectIndex, levelIndex) == this)
                {
                    LevelLoader.AssignLevelObjectSlot(levelIndex, objectIndex, null);
                }
            }
        }
    }
    
    /// <summary>
    /// Recursively removes this object and all contained items from the level's objects[] array.
    /// Used when picking up containers to ensure all nested items are removed.
    /// </summary>
    private void RemoveFromLevelObjectsRecursive()
    {
        // Remove this object
        RemoveFromLevelObjects();
        
        // Recursively remove all contained items
        if (contents != null && contents.Count > 0)
        {
            foreach (UUObject contentObj in contents)
            {
                if (contentObj != null)
                {
                    contentObj.RemoveFromLevelObjectsRecursive();
                }
            }
        }
    }

    /// <summary>Undo a world pickup that only set <see cref="Inventory.usingItem"/> (item was never added to a list).</summary>
    public void RestoreToWorldAfterCancelledHandPickup()
    {
        if (this == null)
            return;
        gameObject.SetActive(true);
        LevelLoader.worldObj.AddLast(this);
        if (objectIndex > 0 && levelIndex == LevelLoader.sLevelLoader.loadedLevel)
        {
            Level level = LevelLoader.sLevelLoader.levels[levelIndex];
            if (level != null)
                level.objects[objectIndex] = this;
        }
    }

    protected int GetTileX()
    {
        return (int)(transform.position.x / xzScale);
    }

    protected int GetTileY()
    {
        return (int)(transform.position.z / xzScale);
    }

    public virtual Tile GetTargetTile()
    {
        return LevelLoader.GetTile(quality, ownerIndex);
    }

    public EClass getClass => GetClass(type);

    public static EClass GetClass(EObjectType type)
    {
        if (type is EObjectType.Sling or EObjectType.Bow or EObjectType.JeweledBow or EObjectType.Crossbow)
        {
            return EClass.Weapons;
        }
        return (EClass)((int)type >> 4);
    }

    public int link
    {
        get => special;
        set => special = value;
    }

    public int weight => DataLoader.sDataLoader.comObjProps[(int)type].massStuff >> 4;

    public bool isCritter => getClass is >= EClass.CrittersA and <= EClass.CrittersD;

    public bool stackable => DataLoader.sDataLoader.comObjProps[(int)type].stackable && special < 368;

    public int quantity
    {
        get
        {
            if (stackable)
            {
                return special > 0 ? special : 1;
            }

            return 1;
        }
        set
        {
            if (stackable)
            {
                special = value;
            }
        }
    }

    public int totalWeight => weight * quantity;

    protected void PositionPossessedObject(UUObject obj, float maxDistance = 1.0f)
    {
        obj.PostLoadInitialize();

        Vector3 castStart = transform.position + Vector3.up;
        Vector2 randomCircle = Random.insideUnitCircle;
        randomCircle /= randomCircle.magnitude;
        Vector3 castDir = new Vector3(randomCircle.x, 0.0f, randomCircle.y);
        int layerMask = LayerMasks.EnvironmentAndCeiling;
        if (Physics.Raycast(castStart, castDir, out RaycastHit hit, maxDistance + 0.2f, layerMask))
        {
            obj.WorldInitialize(castStart + Mathf.Max(0, hit.distance - 0.2f) * castDir);
        }
        else
        {
            obj.WorldInitialize(castStart + maxDistance * castDir);
        }

        //Debug.DrawLine(castStart, obj.transform.position, Color.red, 100.0f);
    }

    protected void SpawnInventory()
    {
        int child = link;
        while (child != 0)
        {
            UUObject obj = LevelLoader.GetObj(child);
            child = obj.chainIndex;

            PositionPossessedObject(obj);
        }
    }

    public bool isPortable => DataLoader.sDataLoader.comObjProps[(int)type].isPortable;

    public virtual bool IsDamageable()
    {
        return false;
    }

    public virtual int GetDefence()
    {
        return 0;
    }

    /// <summary>
    /// What the player is entitled to see of this item's protection. The same as
    /// <see cref="GetDefence"/> unless the piece carries an enchantment he has not identified: that
    /// still protects him in combat, it just must not announce itself through a number in the panel.
    /// </summary>
    public virtual int GetKnownDefence()
    {
        return GetDefence();
    }

    public virtual int GetToughness()
    {
        return 0;
    }

    public virtual void TryDamage(int damage, Skills.ESkillTestResult result)
    {
    }

    protected virtual void StolenFrom(UUObject item, int race)
    {
    }

    public virtual void GetLightProps(ref float lightRange, ref float lightIntensity, ref float lightFlicker)
    {
    }

    public virtual bool IsRepairable()
    {
        return false;
    }

    /// <summary>Held-item use at cursor/camera aim (pole, rock hammer, etc.). Default false.</summary>
    protected virtual bool TryUseAtAim()
    {
        return false;
    }

    /// <summary>
    /// Ray for cursor-held throw, mouse aim, or camera forward (gamepad).
    /// Priority: pending mouse throw ray, live screen ray, then camera forward.
    /// </summary>
    public static bool TryGetPlayerAimRay(out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = default;

        if (PlayerObject.Player == null || PlayerObject.Player.mainCamera == null)
        {
            return false;
        }

        Camera cam = PlayerObject.Player.mainCamera;

        if (Inventory.sInv != null && Inventory.sInv.TryPeekPendingMouseScreenRayWorldQuery(out Ray mouseRay))
        {
            origin = mouseRay.origin;
            direction = mouseRay.direction.sqrMagnitude > 1e-10f ? mouseRay.direction.normalized : cam.transform.forward;
            return true;
        }

        origin = cam.transform.position;
        direction = cam.transform.forward;

        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
                origin = ray.origin;
                Vector3 d = ray.direction;
                direction = d.sqrMagnitude > 1e-10f ? d.normalized : cam.transform.forward;
            }
        }

        return true;
    }

    public static bool TryRaycastFromPlayerAim(out RaycastHit hit, float maxDistance, int layerMask)
    {
        hit = default;
        if (!TryGetPlayerAimRay(out Vector3 origin, out Vector3 direction))
        {
            return false;
        }

        return Physics.Raycast(origin, direction, out hit, maxDistance, layerMask);
    }

    public static bool TryRaycastFromPlayerAim<T>(out T component, float maxDistance, int layerMask) where T : Component
    {
        component = null;
        if (!TryRaycastFromPlayerAim(out RaycastHit hit, maxDistance, layerMask))
        {
            return false;
        }

        component = hit.collider.transform.root.GetComponent<T>();
        return component != null;
    }

    public static Anvil FindAnvil()
    {
        int layerMask = 1 << LayerMask.NameToLayer("Objects");
        if (TryRaycastFromPlayerAim(out Anvil anvil, 4.0f, layerMask))
        {
            return anvil;
        }

        return null;
    }

    /// <summary>
    /// Gamepad inventory X-hold row: label beside the charge ring when not using mouse UI.
    /// Non-null replaces the default &quot;Throw&quot; label; <see cref="Inventory"/> still draws &quot;Repair&quot; first when <see cref="IsRepairable"/> and an anvil are in range.
    /// </summary>
    public virtual string GetGamepadChargeThrowVerbOrNull()
    {
        return null;
    }

    public virtual bool Throw()
    {
        if (IsRepairable())
        {
            Anvil anvil = FindAnvil();
            if (anvil)
            {
                anvil.TryInteract(this, this, EAction.Use);
                return true;
            }
        }

        return false;
    }

    public virtual Texture2D GetInventoryTex()
    {
        if (typeNum < DataLoader.sDataLoader.objTex.Length)
        {
            return DataLoader.sDataLoader.objTex[typeNum];
        }
        return null;
    }

    public virtual void NameEnchantment(bool spawnParticleEffect = true)
    {
        if (!string.IsNullOrEmpty(enchantmentName))
        {
            loreResult = Skills.ESkillTestResult.CriticalSuccess;
            if (spawnParticleEffect)
            {
                ParticleSpawner.SpawnParticle(EParticleType.MagicNameEnchantment, transform.position);
            }
        }
        Messages.Add($"You see {GetLookName()}.");
    }

    protected virtual void GetQualityString(StringBuilder sb)
    {
        int qualityOffset = GetQualityOffset();
        if (qualityOffset != -1)
        {
            sb.Append(StringLoader.GetString(5, qualityOffset + GetQualityIndex()));
            sb.Append(" ");
        }
    }

    /// <summary>
    /// When Lore has increased since the last evaluation, roll once (same rules as the old GetLookName side effect).
    /// Invoked at the start of GetLookName and from PopulateSaveData before serializing.
    /// </summary>
    public void ApplyLoreSkillCatchUpIfNeeded()
    {
        if (!String.IsNullOrEmpty(enchantmentName))
        {
            int loreSkill = Skills.GetSkill(ESkill.Lore);
            if (loreResultLoreLevel < loreSkill)
            {
                loreResultLoreLevel = loreSkill;
                Skills.ESkillTestResult newLoreResult = Skills.GetResult(loreSkill, 8);
                if (newLoreResult > loreResult)
                {
                    loreResult = newLoreResult;
                }
            }
        }
    }

    private void AddArticle(StringBuilder sb)
    {
        string article = quantity > 1 ? pluralArticle : singularArticle;
        if (article == "a " || article == "an ")
        {
            // Check first character of the current StringBuilder content
            if (sb.Length > 0)
            {
                article = Utils.IsVowel(sb[0]) ? "an " : "a ";
            }
        }

        if (quantity > 1 && article == "")
        {
            sb.Insert(0, " ");
            sb.Insert(0, quantity);
        }
        else
        {
            sb.Insert(0, article);
        }
    }

    public virtual string GetLookName()
    {
        ApplyLoreSkillCatchUpIfNeeded();
        lookNameBuilder.Clear();
        
        GetQualityString(lookNameBuilder);

        if (!String.IsNullOrEmpty(enchantmentName) && loreResult == Skills.ESkillTestResult.Success)
        {
            lookNameBuilder.Append("magical ");
        }

        lookNameBuilder.Append(quantity > 1 ? pluralName : singularName);

        AddArticle(lookNameBuilder);

        if (loreResult != Skills.ESkillTestResult.CriticalSuccess)
        {
            return lookNameBuilder.ToString();
        }

        return GetIdentifiedName(lookNameBuilder.ToString());
    }

    public virtual string GetUnderCursorName()
    {
        return GetLookName();
    }

    protected virtual string GetIdentifiedName(string baseName)
    {
        if (loreResult == Skills.ESkillTestResult.CriticalSuccess && !String.IsNullOrEmpty(enchantmentName))
        {
            lookNameBuilder.Clear();
            lookNameBuilder.Append(baseName);
            if (enchantmentName == StringLoader.GetString(6, Magic.StringIndexCursed))
            {
                lookNameBuilder.Append(" (Cursed}");
            }
            else
            {
                lookNameBuilder.Append(" of ");
                lookNameBuilder.Append(enchantmentName);
            }
            return lookNameBuilder.ToString();
        }

        return baseName;
    }

    public string GetNonMagicalName()
    {
        lookNameBuilder.Clear();
        GetQualityString(lookNameBuilder);
        lookNameBuilder.Append(quantity > 1 ? pluralName : singularName);
        AddArticle(lookNameBuilder);
        return lookNameBuilder.ToString();
    }

    protected virtual int GetQualityOffset()
    {
        return -1;
    }

    public virtual int GetQualityIndex()
    {
        return quality > 0 ? (1 + quality / 16) : 0;
    }

    public virtual float GetInteractionDistance()
    {
        return 3.0f;
    }

    protected bool UseEnchantedScrollOrPotion(bool anonymous = false)
    {
        if (!String.IsNullOrEmpty(enchantmentName))
        {
            // all potions and wands accounted for

            if (enchantmentName == StringLoader.GetString(6, Magic.StringIndexManaBoost))
            {
                // the original is too random here. modern audiences expect some predictability
                Magic.sMagic.RestoreMana(PlayerData.sData.maxMana / 2);
                if (Magic.sMagic.defaultCastSound != null)
                {
                    Utils.PlayClip2d(Magic.sMagic.defaultCastSound);
                }
                return true;
            }

            if (enchantmentName == StringLoader.GetString(6, Magic.StringIndexRestoreMana))
            {
                Magic.sMagic.RestoreMana(PlayerData.sData.maxMana);
                if (Magic.sMagic.defaultCastSound != null)
                {
                    Utils.PlayClip2d(Magic.sMagic.defaultCastSound);
                }
                return true;
            }

            return Magic.sMagic.TryCast(enchantmentName, anonymous);
        }

        // find linked 
        // try and find a linked spell
        TryChainInteraction(EAction.Use);
        return true;
    }

    protected void EquipEnchantedItem()
    {
        if (isEnchanted && !isLinked)
        {
            Magic.sMagic.EquipEnchantedItem(this);
        }
    }

    protected void UnequipEnchantedItem()
    {
        if (isEnchanted && !isLinked)
        {
            Magic.sMagic.UnequipEnchantedItem(this);
        }
    }

    protected bool TryDestroyTalisman(Tile t)
    {
        bool destroyed = false;
        if (LevelLoader.sLevelLoader.loadedLevel == 8
            && PlayerData.sData.garamonAtRest
            && t.x is >= 30 and <= 34 && t.y is >= 31 and <= 34)
        {
            Talisman talisman = GetComponent<Talisman>();
            if (talisman != null)
            {
                gameObject.SetActive(false);
                Utils.PlayClipOccluded(DataLoader.sDataLoader.lavaBurn, transform.position);
                Magic.sMagic.CastTremor();
                ++PlayerData.sData.talismansDestroyed;
                if (PlayerData.sData.talismansDestroyed == 8)
                {
                    // Stop all MagicContainment effects spawned by SlasherOfVeils instances
                    Game.Scripts.CritterVariants.SlasherOfVeils.StopAllMagicContainmentEffects();
                    PlayerObject.Player.GoToEtherealVoid();
                }

                destroyed = true;
            }
        }
        return destroyed;
    }

    public static bool CloseToFloor(Vector3 point, Tile t)
    {
        float y = t.GetFloorY(point.x, point.z);
        return point.y < y + 0.1f;
    }

    private static bool CloseToFloor(ContactPoint c, Tile t)
    {
        return CloseToFloor(c.point, t);
    }

    public virtual void OnCollisionEnter(Collision collision)
    {
        bool floating = false;

        ContactPoint c = collision.GetContact(0);
        Tile t = LevelLoader.GetClosestTile(c.point);
        ETerrainType tt = CloseToFloor(c, t) ? t.GetFloorTerrain() : ETerrainType.Normal;
        switch (tt)
        {
        case ETerrainType.Lava:
            {
                // Tyball's key (marked with quality = 255) is invulnerable to lava
                bool isTyballKey = this is Key && quality == 255;
                
                if (!TryDestroyTalisman(t) && !DataLoader.sDataLoader.comObjProps[(int)type].floats && !isTyballKey)
                {
                    Utils.PlayClipOccluded(DataLoader.sDataLoader.lavaBurn, transform.position);
                    if (getClass == EClass.Books || type == EObjectType.ExplodingBook)
                    {
                        ++PlayerData.sData.booksBurned;
                    }
                    Utils.DestroyItem(this);
                }
                else
                {
                    floating = true;
                }
                SpawnSplash(DataLoader.sDataLoader.lavaSplash, DataLoader.sDataLoader.lavaSplashParticle, collision.relativeVelocity);
            }
            break;
        case ETerrainType.Water:
            {
                if (!DataLoader.sDataLoader.comObjProps[(int)type].floats)
                {
                    Utils.DestroyItem(this);
                }
                else
                {
                    floating = true;
                }
                SpawnSplash(DataLoader.sDataLoader.splash, DataLoader.sDataLoader.waterSplashParticle, collision.relativeVelocity);

                if (type == EObjectType.Fish)
                {
                    PlayerData.sData.releasedFish = true;
                }
            }
            break;
        default:
            if (DataLoader.sDataLoader.impact != null && Time.realtimeSinceStartupAsDouble - LevelLoader.sLevelLoader.levelLoadedTime > 1.0)
            {
                if (Time.realtimeSinceStartupAsDouble - timeSinceLastImpactSound > 0.15f)
                {
                    float vol = collision.relativeVelocity.magnitude / 10.0f;
                    if (vol > 0.05f)
                    {
                        Utils.PlayClipOccluded(DataLoader.sDataLoader.impact, transform.position, vol);
                    }
                    timeSinceLastImpactSound = Time.realtimeSinceStartupAsDouble;
                }
            }
            break;            
        }

        if (floating)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity *= 1.0f - Mathf.Pow(0.08f, 60.0f * Time.deltaTime);
            }
        }
    }

    private double timeSinceLastImpactSound;

    protected void SpawnSplash(AudioClip splashClip, GameObject splashParticle, Vector3 relativeVelocity)
    {
        float vol = relativeVelocity.magnitude / 10.0f;

        if (vol > 0.05f)
        {
            Utils.PlayClipOccluded(splashClip, transform.position, vol);

            // Cast down from object position to find ground
            Vector3 splashPos = transform.position;
            Vector3 splashNormal = Vector3.up;
            int layerMask = LayerMasks.EnvironmentAndCeiling;
            RaycastHit hit;
            if (Physics.Raycast(transform.position, Vector3.down, out hit, 2.0f, LayerMasks.EnvironmentOnly))
            {
                splashPos = hit.point;
                splashNormal = hit.normal;
            }

            GameObject splash = Instantiate(splashParticle, splashPos, Quaternion.identity);
            splash.transform.up = splashNormal;

            float boundsScale = 0.2f;
            if (cachedRenderer != null)
            {
                boundsScale = cachedRenderer.bounds.size.magnitude;
            }
            else
            {
                SkinnedMeshRenderer renderer = GetComponentInChildren<SkinnedMeshRenderer>();
                if (renderer != null)
                {
                    boundsScale = renderer.bounds.size.magnitude;
                }
            }

            float scale = Mathf.Max(0.3f, boundsScale * vol);
            splash.transform.localScale = new Vector3(scale, scale, scale);
        }
    }

    public virtual EEquipAction Equip()
    {
        return EEquipAction.Nothing;
    }

    public virtual void Unequip()
    {
    }

    public virtual int GetCombinationType()
    {
        return (int)type;
    }

    public virtual string GetUseText()
    {
        return null;
    }

    /// <summary>Stuff-grid RMB secondary use (gamepad Y is unaffected).</summary>
    public virtual bool SupportsStuffGridMouseSecondaryUse => true;

    public virtual string GetLookText()
    {
        return "Look";
    }

    public virtual bool TryCombine(UUObject obj)
    {
        return false;
    }
    
    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);
        
        if (data is UUObjectSaveData uuData)
        {
            ApplyLoreSkillCatchUpIfNeeded();
            uuData.objectType = (int)type;
            uuData.typeNum = typeNum;
            uuData.option = option;
            uuData.flags = flags;
            uuData.isEnchanted = isEnchanted;
            uuData.isTurned = isTurned;
            uuData.isInvisible = isInvisible;
            uuData.isLinked = isLinked;
            uuData.z = z;
            uuData.angle = angle;
            uuData.x = x;
            uuData.y = y;
            uuData.quality = quality;
            uuData.qualityClass = qualityClass;
            uuData.chainIndex = chainIndex;
            uuData.ownerIndex = ownerIndex;
            uuData.special = special;
            uuData.used = used;
            uuData.isStatic = isStatic;
            uuData.alignToFloor = alignToFloor;
            uuData.loreResult = (int)loreResult;
            uuData.loreResultLoreLevel = loreResultLoreLevel;
            uuData.enchantmentName = enchantmentName;
            uuData.quantity = quantity;
            
            if (contents != null && contents.Count > 0)
            {
                uuData.contents = new List<ObjectSaveData>();
                foreach (UUObject contentObj in contents)
                {
                    if (contentObj != null)
                    {
                        uuData.contents.Add(contentObj.SaveToData());
                    }
                }
            }
            
            // Save rigidbody state if present
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                uuData.hasRigidbody = true;
                uuData.velocity = rb.linearVelocity;
                uuData.angularVelocity = rb.angularVelocity;
                uuData.useGravity = rb.useGravity;
            }
        }
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        
        if (data is UUObjectSaveData uuData)
        {
            typeNum = uuData.typeNum;
            option = uuData.option;
            flags = uuData.flags;
            isEnchanted = uuData.isEnchanted;
            isTurned = uuData.isTurned;
            isInvisible = uuData.isInvisible;
            isLinked = uuData.isLinked;
            z = uuData.z;
            angle = uuData.angle;
            x = uuData.x;
            y = uuData.y;
            quality = uuData.quality;
            qualityClass = uuData.qualityClass;
            chainIndex = uuData.chainIndex;
            ownerIndex = uuData.ownerIndex;
            special = uuData.special;
            used = uuData.used;
            isStatic = uuData.isStatic;
            alignToFloor = uuData.alignToFloor;
            loreResult = (Skills.ESkillTestResult)uuData.loreResult;
            loreResultLoreLevel = uuData.loreResultLoreLevel;
            enchantmentName = uuData.enchantmentName;
            quantity = uuData.quantity;
            
            if (uuData.contents != null && uuData.contents.Count > 0)
            {
                contents = new List<UUObject>();
                foreach (ObjectSaveData contentData in uuData.contents)
                {
                    LevelObject contentObj = SaveGameManager.sInstance.CreateObjectFromSaveData(contentData);
                    if (contentObj is UUObject uuContentObj)
                    {
                        contents.Add(uuContentObj);
                    }
                }
            }
            
            // Restore rigidbody state if it was saved
            if (uuData.hasRigidbody)
            {
                Rigidbody rb = GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = uuData.velocity;
                    rb.angularVelocity = uuData.angularVelocity;
                    rb.useGravity = uuData.useGravity;
                }
            }
        }
    }

    public override ObjectSaveData SaveToData()
    {
        UUObjectSaveData data = new UUObjectSaveData();
        PopulateSaveData(data);
        
        return new ObjectSaveData
        {
            objectName = name,
            objectTypeName = GetType().Name,
            objectType = (int)type,
            objectIndex = objectIndex,
            level = levelIndex,
            originalLevel = originalLevel,
            jsonData = JsonUtility.ToJson(data)
        };
    }

    public override void LoadFromData(ObjectSaveData objData)
    {
        if (objData == null || string.IsNullOrEmpty(objData.jsonData))
            return;
        
        UUObjectSaveData data = JsonUtility.FromJson<UUObjectSaveData>(objData.jsonData);
        if (data == null)
        {
            Debug.LogError($"Failed to deserialize JSON for objectIndex={objData.objectIndex}, jsonData={objData.jsonData}");
            return;
        }
        RestoreFromSaveData(data);
    }

    public virtual void DeleteFromTrap()
    {
        
    }
}
