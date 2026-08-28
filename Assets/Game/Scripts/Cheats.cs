using UnityEngine;
using System.Collections.Generic;

public class Cheats : MonoBehaviour
{
    public int level = 1;
    public bool giveAllKeys;
    public bool showConversationHeads;
    public bool showOldHeads;
    public bool showSpellIcons;
    public bool showFloorMats;
    public bool showWallMats;
    public bool showDoorMats;
    public bool showSwitches;
    public bool labelCenteredObject;
    public bool addIndexToObjectName;
    public string giveRunestones;
    public bool castWithoutMana;
    public bool boostCasting;
    public bool invincible;
    public bool boostDamageFromPlayer;
    public bool spawnObjects;
    public List<EObjectTypeEntry> objectsToSpawn;
    public bool spawnStacks;
    public List<EObjectTypeEntry> stacksToSpawn;
    public bool mapWithoutMap;
    public bool revealMap;
    public bool advanceSkillsWithoutPoints;

    
    public static Cheats sCheats;
    
    protected void Start()
    {
        sCheats = this;
    }

    public void Update()
    {
        if (spawnObjects)
        {
            foreach (var type in objectsToSpawn)
            {
                UUObject obj = Utils.SpawnSingleObject(type);
                if (obj != null)
                {
                    obj.quality = 30;
                }
            }
            spawnObjects = false;
        }

        if (spawnStacks)
        {
            foreach (var type in stacksToSpawn)
            {
                UUObject obj = Utils.SpawnSingleObject(type);
                if (obj.stackable)
                {
                    obj.quantity = 20;
                }
            }
            spawnStacks = false;
        }
    }
}
