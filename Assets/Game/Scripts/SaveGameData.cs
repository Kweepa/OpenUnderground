using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class LevelWorldSaveData
{
    public List<ObjectSaveData> objects = new List<ObjectSaveData>();
    /// <summary>Objects in this level's objects[] that are not in worldObj (e.g. inactive/picked up slot placeholders).</summary>
    public List<ObjectSaveData> inactiveObjects = new List<ObjectSaveData>();
}

[System.Serializable]
public class SaveGameData
{
    public int version = 1;
    public int currentLevel;
    // Optional metadata for listings
    public string slotName;
    public string displayName;
    public string savedAtIso;
    public PlayerSaveData playerData;
    public InventorySaveData inventoryData;
    public List<ObjectSaveData> worldObjects = new List<ObjectSaveData>();
    public LevelWorldSaveData[] worldObjectsByLevel;
    public MapSaveData mapData;
    
    public SaveGameData()
    {
        playerData = new PlayerSaveData();
        inventoryData = new InventorySaveData();
        mapData = new MapSaveData();
    }
}

// Simplified header for parsing just basic info without circular references
[System.Serializable]
public class SaveGameDataHeader
{
    public int version = 1;
    public int currentLevel;
    public string slotName;
    public string displayName;
    public string savedAtIso;
    public PlayerSaveData playerData;
}

// Minimal header data for fast slot listing (stored in separate .header.json file)
[System.Serializable]
public class SaveSlotHeader
{
    public string slotName;
    public string displayName;
    public string savedAtIso;
    public int currentLevel;
    public string playerName;
    public int xp;
    public int playerClass;
    public string inGameTime;
    public int charLevel;
}

[System.Serializable]
public class PlayerSaveData
{
    public Vector3 position;
    public Quaternion rotation;
    
    // Core stats
    public bool female;
    public bool leftHanded;
    public int portrait;
    public int playerClass;
    public string playerName;
    public int vitality;
    public int hp;
    public int maxMana;
    public int mana;
    public int xp;
    public int charLevel;
    public int skillPoints;
    public int skillPointsXpTier;
    public bool easy;
    public bool dead;
    
    // Attributes
    public int strength;
    public int intellect;
    public int dexterity;
    
    // Status
    public int poison;
    public int hunger;
    public int fatigue;
    public int drunkenness;
    
    // Skills
    public int[] skill;
    
    // Quest and game state
    public int[] questFlags;
    public int[] globalVars;
    
    // Dreams
    public List<int> dreamsRemaining;
    public double timeOfLastDream;
    public int cupDreamIndex;
    
    // Special flags
    public bool cupFound;
    public bool saidFanlo;
    public bool saplingPlanted;
    public Vector3 saplingPlantedPosition;
    public int saplingPlantedLevel;
    public bool moonstoneDropped;
    public Vector3 moonstoneDroppedPosition;
    public int moonstoneDroppedLevel;
    public int talismansDestroyed;
    public int talismansCollected;
    public bool garamonAtRest;
    public bool enteredGreenMoongate;
    
    // Game time
    public double gameTime;
    
    // Player movement state
    public bool wasGrounded;
    public float stamina = 1.0f;
    public float staminaRecoverDelayRemaining;
    
    // Encountered critters for summon spell
    public List<EncounteredCritterEntry> encounteredCritters;
    
    // Magic state
    public MagicSaveData magicData;

    // Achievement-related progress (per save slot)
    public int numRepairs;
    public bool[] openedChest;
    public int numFishCaught;
    public int booksRead;
    public int mapTilesRevealed;
    public float gateTravelDistance;
    public int waterWalkSteps;
    public int lavaWalkSteps;
    public double playTime;
    public int booksBurned;
    public bool pacifistStopped;

    public TutorialSaveData tutorialData;
}

[System.Serializable]
public class EncounteredCritterEntry
{
    public int critterType; // EObjectType as int (64-127)
    public int level;
    public int objectIndex;
    public int originalHp;
}

[System.Serializable]
public class InventorySaveData
{
    public List<ObjectSaveData> mainInventory = new List<ObjectSaveData>();
    public List<ObjectSaveData> equippedItems = new List<ObjectSaveData>();

    /// <summary>Mouse-held portable (not in main list / slots). Missing in older saves: JsonUtility defaults bool to false.</summary>
    public bool hasMouseCursorCarriedPortable;

    public ObjectSaveData mouseCursorCarriedPortableData;

    /// <summary>Gamepad-style floating use item when not on cursor. Omitted when same object as cursor carry.</summary>
    public bool hasUsingItem;

    public ObjectSaveData usingItemData;
    
    public InventorySaveData()
    {
        mainInventory = new List<ObjectSaveData>();
        equippedItems = new List<ObjectSaveData>();
    }
}

[System.Serializable]
public class ObjectSaveData
{
    public string objectName;      // for debugging
    public string objectTypeName;  // For CreateObject routing (class name)
    public int objectType;         // EObjectType as int (for routing)
    public int objectIndex;        // Object index (for CreateObject routing)
    public int level;              // Level index (for routing)
    public int originalLevel;      // Level where objectIndex was originally defined (0 = not set/runtime-created)
    public bool inWorldObj;        // Whether object was in worldObj list when saved
    public string jsonData;        // Serialized type-specific data
}
