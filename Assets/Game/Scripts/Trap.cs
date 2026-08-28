using UnityEngine;
using System.Collections;
using Random = UnityEngine.Random;

[System.Serializable]
public class TrapTextClip
{
    public string contains;
    public AudioClip clip;
    public int tileX;
    public int tileY;
}

public class Trap : UUObject
{
    public TrapTextClip[] textClips;
    public AudioClip teleportSound;
    public AudioClip movingPlatformSound;
    
    private readonly Collider[] cachedColliders = new Collider[16];
    
    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        bool doChain = true;

        switch (type)
        {
        case EObjectType.DamageTrap:
            PlayerObject.Player.Damage(Skills.ESkillTestResult.Success, quality, ownerIndex > 0 ? EDamageType.Poison : EDamageType.Damage);
            break;
        case EObjectType.TeleportTrap:
            {
                int level = z;
                int tileX = quality;
                int tileY = ownerIndex;
                bool isTeleport = (level == 0 || level == LevelLoader.sLevelLoader.loadedLevel) && LevelLoader.sLevelLoader.loadedLevel != 2; 
                if (LevelLoader.sLevelLoader.loadedLevel == 1 && level == 2 && tileX == 47 && tileY == 53)
                {
                    PlayerData.sData.usedGoblinToilet = true;
                    // TODO: play a toilet flush sound
                }
                LevelLoader.sLevelLoader.ChangeLevel(level, tileX, tileY);
                if (isTeleport)
                {
                    Utils.PlayClip2d(teleportSound);
                    GameObject particle = ParticleSpawner.SpawnParticle(EParticleType.MagicTeleport, PlayerObject.Player.transform.position - Vector3.up);
                    if (particle != null)
                    {
                        particle.transform.SetParent(PlayerObject.Player.transform);
                    }
                }
                //Debug.LogFormat( gameObject, "Teleport to {0}: {1},{2}", level, tileX, tileY );
            }
            break;
        case EObjectType.ArrowTrap:
            {
                EObjectType typeToCreate = (EObjectType)((quality << 5) | ownerIndex);
                if (typeToCreate != 0)
                {
                    // arrow can refer to anything really - e.g., a boulder
                    UUObject arrow = LevelLoader.CreateObjectOfType(typeToCreate);
                    Tile t = sender != null ? sender.initialTile : initialTile;
                    if (t != null)
                    {
                        arrow.x = x;
                        arrow.y = y;
                        arrow.z = z;
                        arrow.WorldInitialize(t, t.x, t.y);
                        arrow.PostLoadInitialize();
                        Debug.Log($"Trap spawned {arrow.name} ({typeToCreate})");
                        
                        // Check if spawned object is a rock/boulder and spawn ceiling dust
                        if (typeToCreate is EObjectType.LargeBoulder or EObjectType.MediumBoulder)
                        {
                            // Cast upward to find the ceiling
                            Vector3 spawnPosition = arrow.transform.position;
                            int environmentLayerMask = LayerMasks.EnvironmentAndCeiling;
                            float maxDistance = 50.0f; // Reasonable ceiling height
                            
                            if (Physics.Raycast(spawnPosition, Vector3.up, out RaycastHit ceilingHit, maxDistance, environmentLayerMask))
                            {
                                // Spawn ceiling dust particle at the ceiling hit point
                                ParticleSpawner.SpawnParticle(EParticleType.CeilingDust, ceilingHit.point);
                            }
                        }
                        
                        // fire it at the player. depending on mass, will reach
                        if (arrow is Projectile or Bones)
                        {
                            Rigidbody rb = arrow.GetComponentInChildren<Rigidbody>();
                            if (rb != null)
                            {
                                Vector3 target = PlayerObject.Player.transform.position;
                                target.y += 1.0f;
                                Vector3 off = (target - arrow.transform.position).normalized;
                                arrow.transform.LookAt(target);
                                float force = 10.0f;
                                Projectile projectile = arrow as Projectile;
                                if (projectile != null)
                                {
                                    projectile.doDamage = true;
                                    projectile.projectileOwner = gameObject;
                                    force = 30.0f;
                                }
                                rb.AddForce(force * off, ForceMode.Impulse);
                            }
                        }
                    }
                    else
                    {
                        // maybe there are tile coords somewhere?
                        Debug.Log($"Trap tried to spawn but it and its chain parent were inactive");
                    }

                    // once it's fired, turn it off
                    quality = 0;
                    ownerIndex = 0;
                }
                else
                {
                    // once it's fired, prevent it rechaining
                    doChain = false;
                }
            }
            break;
        case EObjectType.DoTrap:
            {
                switch (quality)
                {
                case 2:
                    // camera trap
                    DoCameraTrap();
                    break;
                case 3:
                    initialTile.movingPlatform.Advance();
                    break;
                case 5:
                    DoAlertTrap();
                    break;
                case 24:
                    DoBullfrogTrap(sender);
                    break;
                case 40:
                    // gems in corners of room puzzle
                    DoEmeraldTrap();
                    break;
                case 42:
                    // chat with door
                    DoTalkingDoorTrap();
                    break;
                case 50:
                    // something to do with lv7 prison trap
                    DoPrisonTrap();
                    break;
                case 63:
                    // end the game
                    PlayerData.sData.enteredGreenMoongate = true;
                    // pass control to the front end
                    Instantiate(PlayerObject.Player.endGame);
                    break;
                }
            }
            break;
        case EObjectType.PitTrap:
            {
                // there is one pit trap in the game, in the sw corner of level 3, by the stairs up.
                // triggered by throwing the right hand button of the three button combination nearby.
                // it is supposed to spawn a skeleton.
                // test with left middle right, repeatedly.
                UUObject templateObject = LevelLoader.GetObj(255); // 255 is the skeleton to spawn
                if (templateObject != null)
                {
                    UUObject newObject = Instantiate(templateObject);
                    newObject.name = templateObject.name;
                    newObject.gameObject.SetActive(true);

                    Tile t = LevelLoader.GetTile(2, 7); // this is a spot behind the player (unless they use telekinesis)
                    newObject.x = 3;
                    newObject.y = 3;
                    newObject.WorldInitialize(t, t.x, t.y);
                    newObject.PostLoadInitialize();
                }
            }
            break;
        case EObjectType.WardTrap:
        case EObjectType.TellTrap:
        case EObjectType.CombinationTrap:
            {
                Debug.LogFormat(gameObject, $"{type}. Supposedly unused!");
            }
            break;
        case EObjectType.ChangeTerrainTrap:
            // changes one or more tiles according to the encoded info
            DoChangeTerrainTrap(originator);
            break;
        case EObjectType.SpellTrap:
            if (!isLinked)
            {
                Magic.sMagic.TryCast(special & 0x3f);
            }
            break;
        case EObjectType.CreateObjectTrap:
            DoCreateObjectTrap(sender);
            doChain = false;
            break;
        case EObjectType.DoorTrap:
            {
                // find the door
                Tile t = sender.GetTargetTile();
                UUObject tileObj = LevelLoader.GetObj(t.firstObject);
                while (tileObj != null)
                {
                    if (tileObj.getClass == EClass.Doors)
                    {
                        Door door = tileObj.GetComponent<Door>();
                        switch (quality)
                        {
                        case 1:
                            door.Open();
                            break;
                        case 2:
                            door.Close();
                            break;
                        case 3:
                            door.Toggle();
                            break;
                        }

                        break;
                    }

                    tileObj = tileObj.chainIndex > 0 ? LevelLoader.GetObj(tileObj.chainIndex) : null;
                }
            }
            break;
        case EObjectType.DeleteObjectTrap:
            {
                UUObject toBeDeleted = LevelLoader.GetObj(link);
                if (toBeDeleted != null)
                {
                    LevelLoader.worldObj.Remove(toBeDeleted);
                    toBeDeleted.gameObject.SetActive(false);
                    
                    toBeDeleted.DeleteFromTrap();

                    Debug.LogFormat(gameObject, "DeleteObject {0}", toBeDeleted.name);

                    // since the link is used for the object to delete and
                    // the object to chain interaction, stop here
                    doChain = false;
                }
            }
            break;
        case EObjectType.InventoryTrap:
            {
                doChain = false;
                if (z > 0)
                {
                    EObjectType typeToFind = (EObjectType)((quality << 5) | ownerIndex);
                    if (Inventory.Contains(typeToFind))
                    {
                        doChain = true;
                    }
                }
            }
            break;
        case EObjectType.SetVariableTrap:
            {
                int value = ((quality << 8) | ((ownerIndex & 31) << 3) | y);
                int var = PlayerData.sData.globalVars[z];
                if (z == 0)
                {
                    switch (angle)
                    {
                    case 0:
                    case 2:
                    case 3:
                    case 4:
                    case 6:
                        // set
                        var |= (1 << value);
                        break;
                    case 1:
                        // clear
                        var &= ~(1 << value);
                        break;
                    case 5:
                        // toggle
                        var ^= (1 << value);
                        break;
                    }
                }
                else
                {
                    switch (angle)
                    {
                    case 0: // add
                        var += value;
                        break;
                    case 1: // sub
                        var -= value;
                        break;
                    case 2: // set
                        var = value;
                        break;
                    case 3: // and
                        var &= value;
                        break;
                    case 4: // or
                        var |= value;
                        break;
                    case 5: // xor
                        var ^= value;
                        break;
                    case 6: // shl
                        var <<= value;
                        break;
                    }

                    var &= 0x3f;
                }

                PlayerData.sData.globalVars[z] = var;
            }
            break;
        case EObjectType.CheckVariableTrap:
            {
                int value = (quality << 8) | ((ownerIndex & 31) << 3) | y;
                int cmp = 0;
                for (int i = z; i <= z + angle; ++i)
                {
                    if (x != 0)
                    {
                        cmp += PlayerData.sData.globalVars[i];
                    }
                    else
                    {
                        cmp <<= 3;
                        cmp |= PlayerData.sData.globalVars[i] & 7;
                    }
                }

                if (cmp != value)
                {
                    doChain = false;
                }
            }
            break;
        case EObjectType.TextStringTrap:
            {
                int index = 64 * (LevelLoader.sLevelLoader.loadedLevel - 1) + ownerIndex;
                string messageText = StringLoader.GetString(9, index);
                float duration = 7.0f;
                foreach (var textClip in textClips)
                {
                    if (messageText.Contains(textClip.contains))
                    {
                        if (textClip.tileX > 0)
                        {
                            Tile t = LevelLoader.GetTile(textClip.tileX, textClip.tileY);
                            Utils.PlayClip(textClip.clip, t.GetCenter(), 96.0f, AudioRolloffMode.Linear);
                        }
                        else
                        {
                            Utils.PlayClip2d(textClip.clip, false);
                        }
                        duration = Mathf.Max(duration, textClip.clip.length);
                    }
                }

                Messages.Add(messageText, duration);
            }
            break;
        }


        if (doChain)
        {
            // chain the interaction
            TryChainInteraction(EAction.Trigger);
        }
    }

    void DoCameraTrap()
    {
        StartCoroutine(CameraTrap(3.0f));
    }

    private IEnumerator CameraTrap(float time)
    {
        PlayerObject.DisableControls(EControlMask.Cutscene, true);

        Camera c = gameObject.AddComponent<Camera>();
        Light l = c.gameObject.AddComponent<Light>();
        l.range = 30.0f;
        l.intensity = 1.3f;
        Camera old = PlayerObject.Player.mainCamera;
        old.tag = "Untagged";
        tag = "MainCamera";
        yield return new WaitForSeconds(time);
        tag = "Untagged";
        old.tag = "MainCamera";
        Destroy(c);
        Destroy(l);
        PlayerObject.DisableControls(EControlMask.Cutscene, false);
    }

    void DoBullfrogTrap(UUObject sender)
    {
        // bullfrog puzzle. pretty hardcoded.
        // there are two rotary switches, for x and y, and two buttons, for up and down
        // sender should be the type, dial or button.
        // for the dial do nothing, for the buttons go find the dials and check their settings

        // from up and down button (dials are 2 and 3)
        if (ownerIndex == 0 || ownerIndex == 1)
        {
            // check which button it is,
            // so we know whether to raise or lower the ground
            int dir = ownerIndex == 0 ? 1 : -1;

            int cx = 48;
            int cy = 48;

            Tile switchTile = LevelLoader.GetTile(46, 47);
            int objIndex = switchTile.firstObject;
            while (objIndex != 0)
            {
                UUObject obj = LevelLoader.GetObj(objIndex);
                if (obj != null)
                {
                    switch (obj.type)
                    {
                    case EObjectType.RotaryLever:
                        {
                            // determine which based on the rotation angle
                            switch (obj.angle)
                            {
                            case 4:
                                cx += obj.flags & 7;
                                break;
                            case 6:
                                cy += obj.flags & 7;
                                break;
                            }
                        }
                        break;
                    }
                }

                objIndex = LevelLoader.sLevelLoader.GetChainIndexFromObjectData(objIndex);
            }

            for (int tx = cx - 1; tx <= cx + 1; ++tx)
            {
                for (int ty = cy - 1; ty <= cy + 1; ++ty)
                {
                    Tile t = LevelLoader.GetTile(tx, ty);
                    if (t.movingPlatform != null)
                    {
                        t.movingPlatform.Move(tx == cx && ty == cy ? 2 * dir : dir);
                    }
                }
            }
        }
    }

    void DoChangeTerrainTrap(UUObject originator)
    {
        int floorTex = quality >> 1;
        int wallTex = ownerIndex;
        int tileType = quality & 1;

        // tile type 0 - move up to the ceiling
        int newHeight = tileType == 0 ? 128 : z;

        // if revealing a secret door behind a decal, place the floor at the decal height
        // x == 0 and y == 0 means a single tile of terrain to change
        if (originator as Decal != null && x == 0 && y == 0)
        {
            newHeight = originator.z;
        }

        newHeight >>= 3;

        if (initialTile.movingPlatform != null && initialTile.movingPlatform.h != newHeight)
        {
            Vector3 center = Vector3.zero;
            for (int xx = initialTile.x; xx <= initialTile.x + x; ++xx)
            {
                for (int yy = initialTile.y; yy <= initialTile.y + y; ++yy)
                {
                    Tile t = LevelLoader.GetTile(xx, yy);
                    if (t != null && t.movingPlatform != null)
                    {
                        t.movingPlatform.SetHeight(newHeight);
                        t.movingPlatform.SetMaterials(t, wallTex, floorTex);
                        MapScreen.UpdateTile(t);

                        Vector3 tileCenter = t.GetCenter();
                        tileCenter.y = t.movingPlatform.transform.position.y;
                        center += tileCenter;
                    }
                }
            }
            Utils.PlayClipOccluded(movingPlatformSound, center);
        }
    }

    void DoEmeraldTrap()
    {
        // all hard coded :(
        Vector3[] pillars =
        {
            new(148, 10, 67),
            new(172, 10, 43),
            new(148, 10, 43),
            new(172, 10, 67)
        };
        int count = 0;
        foreach (var pillar in pillars)
        {
            int colCount = Physics.OverlapSphereNonAlloc(pillar, 5.0f, cachedColliders, 1 << LayerMask.NameToLayer("Objects"));
            for (int i = 0; i < colCount; i++)
            {
                UUObject obj = cachedColliders[i].gameObject.transform.root.GetComponent<UUObject>();
                if (obj != null && obj.type == EObjectType.Emerald)
                {
                    ++count;
                    break;
                }
            }
        }

        if (count == 4)
        {
            // spawn the Vas rune
            UUObject rune = LevelLoader.CreateObjectOfType(EObjectType.RunestoneVas);
            rune.x = 3;
            rune.y = 3;
            rune.WorldInitialize(initialTile, initialTile.x, initialTile.y);
            rune.PostLoadInitialize();
        }
    }

    void DoPrisonTrap()
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        StartCoroutine(PrisonTrap());
    }

    private IEnumerator PrisonTrap()
    {
        PlayerObject.DisableControls(EControlMask.Conversation, true);
        
        Door door = LevelLoader.GetObj(1000) as Door;
        if (door != null)
        {
            door.Close();
        }

        yield return new WaitForSeconds(1.0f);

        PlayerObject.DisableControls(EControlMask.Conversation, false);

        Critter jailor = LevelLoader.GetObj(251) as Critter;
        if (jailor != null && jailor.whoami == EWhoAmI.Jailor && jailor.hp > 0)
        {
            // start conversation
            jailor.TryInteract(null, null, EAction.Use);
        }
    }

    private Critter doorCritter; 

    void DoTalkingDoorTrap()
    {
        if (doorCritter == null)
        {
            doorCritter = gameObject.AddComponent<Critter>();
            doorCritter.whoami = EWhoAmI.Door;
            doorCritter.name = StringLoader.GetString(7, 16 + (int) doorCritter.whoami);
        }
        Conversations.StartConversation(doorCritter);
    }

    void DoAlertTrap()
    {
        TryStolen();
    }

    public override Tile GetTargetTile()
    {
        return initialTile;
    }

    public UUObject DoCreateObjectTrap(UUObject sender)
    {
        // spawns an object from a template
        if (Random.Range(0, 64) >= quality)
        {
            int templateIndex = link;
            if (templateIndex != 0)
            {
                UUObject templateObject = LevelLoader.GetObj(templateIndex);
                if (templateObject != null)
                {
                    UUObject newObject = Instantiate(templateObject);
                    newObject.name = templateObject.name;
                    newObject.gameObject.SetActive(true);

                    if (newObject is Critter spawnedFromTemplate)
                    {
                        spawnedFromTemplate.PrepareVisibilityAfterTemplateClone();
                    }

                    Tile t = sender.GetTargetTile();
                    newObject.x = 3;
                    newObject.y = 3;
                    newObject.WorldInitialize(t, t.x, t.y);
                    newObject.PostLoadInitialize();

                    // clear the trap so it can't spawn again
                    gameObject.SetActive(false);

                    Debug.Log($"Trap creating object {name}", this);
                    Debug.Log($"-> New object {newObject.name}.", newObject);

                    return newObject;
                }
            }
        }
        return null;
    }
}
