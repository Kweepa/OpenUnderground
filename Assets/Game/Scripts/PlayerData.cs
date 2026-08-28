using UnityEngine;
using System;
using System.Collections.Generic;

public enum EPlayerClass
{
    Fighter,
    Mage,
    Bard,
    Tinker,
    Druid,
    Paladin,
    Ranger,
    Shepherd
}

public enum EQuestFlag
{
    MurgoFreed,
    TalkedToHagbard,
    MetDrOwl,
    CanSpeakToKetcheval,
    GazerKilled,
    ShouldFindTalismans,
    BefriendedLizardmen,
    ConvoWithMurgo,
    BronusBookGoBoom,
    FindGurstang,
    WhereIsZak,
    RodrickKilled,
    
    // 1 - seek, 2 - search for writ, 3 - found writ, 4 - opened armoury
    KnightOfCrux = 32,
    TalismansLeft = 36,
    Dreams = 37
}

public enum ETalisman
{
    Book,
    Bottle,
    Cup,
    Ring,
    Shield,
    Standard,
    Sword,
    Taper
}

public class PlayerData : MonoBehaviour
{
    public bool female;
    public bool leftHanded;
    public int portrait;
    public EPlayerClass playerClass;
    public string playerName;
    public int vitality;
    public int hp;
    public int maxMana;
    public int mana;
    public int xp;
    public int charLevel = 1;
    public int skillPoints = 3; // start with 3, I think
    public int skillPointsXpTier; // high-water of displayXp/300 tiers already awarded
    public bool easy;

    public bool dead;
    
    public int strength;
    public int intellect;
    public int dexterity;

    public int poison;
    public int hunger = 10;
    public int fatigue;
    public int drunkenness;

    [EnumNamedArray(typeof(ESkill))]
    public int[] skill = new int[Enum.GetNames(typeof(ESkill)).Length];

    [EnumNamedArray(typeof(EQuestFlag))]
    public int[] questFlags = new int[64];

    public int[] globalVars = new int[64];

    public List<int> dreamsRemaining;
    public double timeOfLastDream;
    public int cupDreamIndex;

    public bool cupFound;
    public bool saidFanlo;
    
    public bool saplingPlanted;
    public Vector3 saplingPlantedPosition;
    public int saplingPlantedLevel;

    public bool moonstoneDropped;
    public Vector3 moonstoneDroppedPosition;
    public int moonstoneDroppedLevel;

    public int talismansCollected;

    public int talismansDestroyed;
    public bool garamonAtRest;

    public bool enteredGreenMoongate;

    public double gameTime = 19 * 60 * 60; // 19:00 or 7pm

    // data for achievements:
    public int numRepairs;
    public bool navreyKilled;
    public bool releasedArial;
    public bool[] hadConversation = new bool[320];
    public bool[] openedChest = new bool[5];
    public int numFishCaught;
    // Defeated critters stored as bit flags (64-bit integer) for efficient storage and persistence
    // Bit 0 = critter type 0, Bit 1 = critter type 1, etc.
    public ulong defeatedCritterFlags = 0;
    public int booksRead;
    public bool[] dreamDreamt = new bool[10];
    public int mapTilesRevealed;
    public bool atePopcorn;
    public bool usedGoblinToilet;
    public bool releasedFish;
    public bool playedMardinOnMandolin;
    public float gateTravelDistance;
    public bool passedOutDrunk;
    public bool tripped;
    public int waterWalkSteps;
    public int lavaWalkSteps;
    public bool raidedTreasury;
    public double playTime;
    public int booksBurned;
    public bool pacifistStopped;

    public TutorialSaveData tutorialData = new TutorialSaveData();
    
    public static PlayerData sData;

    private const string DEFEATED_CRITTERS_PREFS_KEY = "DefeatedCritters";
    private const string HAD_CONVERSATION_PREFS_KEY = "HadConversation";
    private const string DREAM_DREAMT_PREFS_KEY = "DreamDreamt";

    public static void SetQuestFlag(EQuestFlag flag, int value)
    {
        sData.questFlags[(int)flag] = value;
    }

    public static int GetQuestFlag(EQuestFlag flag)
    {
        return sData?.questFlags[(int)flag] ?? 0;
    }

    public static void SetQuestFlagFromConversation(int conversation, int flag, int value)
    {
        EQuestFlag eflag = (EQuestFlag)flag;
        Debug.Log($"From convo {conversation}, setting quest flag {eflag} ({flag}) to {value}.");
        sData.questFlags[flag] = value;
    }

    public static int GetQuestFlagFromConversation(int conversation, int flag)
    {
        int value = sData.questFlags[flag];
        EQuestFlag eflag = (EQuestFlag)flag;
        Debug.Log($"From convo {conversation}, getting quest flag {eflag} ({flag}) {value}.");
        return value;
    }

    public void ShuffleDreams()
    {
        // play dream 0 first, then a random one from this list
        dreamsRemaining = new List<int>();
        List<int> dreams = new List<int>(new[] { 1, 4, 5, 6, 7, 8, 9 });
        while (dreams.Count > 0)
        {
            int pick = UnityEngine.Random.Range(0, dreams.Count);
            dreamsRemaining.Add(dreams[pick]);
            dreams.RemoveAt(pick);
        }
        // dream 0 first
        dreamsRemaining.Insert(0, 0);
    }

    public void SetTyballDead()
    {
        dreamsRemaining = new List<int>(new[] { 2, 3 });
        // make sure we see the next dream immediately
        timeOfLastDream = 0;
    }

    private int GetDreamIndexInternal()
    {
        // need at least 10 hours between dreams
        // sleeping lasts 8 hours so it's really only 2 hours of gameplay
        if (gameTime - timeOfLastDream < 10 * 60 * 60
            || dreamsRemaining.Count == 0)
        {
            return -1;
        }

        int index = dreamsRemaining[0];
        dreamsRemaining.RemoveAt(0);

        timeOfLastDream = gameTime;
        return index;
    }

    public static int GetDreamIndex()
    {
        return sData.GetDreamIndexInternal();
    }

    public void Start()
    {
        sData = this;

        // in case we skip the character creation
        ShuffleDreams();

        // Global persistence across saves (legacy behavior)
        MergeDefeatedCrittersFromPrefs();

        EnsureCrossSaveAchievementArrays();
        MergeCrossSaveAchievementArraysFromPrefs();
    }
    
    // Defeated critters helper methods
    
    /// <summary>
    /// Sets whether a critter type has been defeated.
    /// </summary>
    /// <param name="index">Critter type index (0-63, where 0 = EObjectType index 64)</param>
    /// <param name="defeated">True if defeated, false to clear</param>
    public void SetDefeatedCritter(int index, bool defeated)
    {
        if (index < 0 || index >= 64)
        {
            Debug.LogWarning($"SetDefeatedCritter: Invalid index {index}, must be 0-63");
            return;
        }
        
        if (defeated)
        {
            defeatedCritterFlags |= (1UL << index);
        }
        else
        {
            defeatedCritterFlags &= ~(1UL << index);
        }

        // Global persistence across saves (legacy behavior)
        SaveDefeatedCrittersToPrefs();
    }

    public void MergeDefeatedCrittersFromPrefs()
    {
        if (!PlayerPrefs.HasKey(DEFEATED_CRITTERS_PREFS_KEY))
        {
            return;
        }

        string savedValue = PlayerPrefs.GetString(DEFEATED_CRITTERS_PREFS_KEY, "0");
        if (ulong.TryParse(savedValue, out ulong savedFlags))
        {
            defeatedCritterFlags |= savedFlags;
        }
        else
        {
            Debug.LogWarning($"Failed to parse DefeatedCritters from PlayerPrefs: {savedValue}");
        }
    }

    private void SaveDefeatedCrittersToPrefs()
    {
        PlayerPrefs.SetString(DEFEATED_CRITTERS_PREFS_KEY, defeatedCritterFlags.ToString());
        PlayerPrefs.Save();
    }

    public void EnsureCrossSaveAchievementArrays()
    {
        hadConversation ??= new bool[320];
        if (hadConversation.Length != 320) hadConversation = new bool[320];

        dreamDreamt ??= new bool[10];
        if (dreamDreamt.Length != 10) dreamDreamt = new bool[10];
    }

    public void MergeCrossSaveAchievementArraysFromPrefs()
    {
        EnsureCrossSaveAchievementArrays();

        // hadConversation
        if (PlayerPrefs.HasKey(HAD_CONVERSATION_PREFS_KEY))
        {
            string b64 = PlayerPrefs.GetString(HAD_CONVERSATION_PREFS_KEY, "");
            bool[] loaded = DecodeBoolArrayFromBase64(b64, 320);
            for (int i = 0; i < 320; i++)
            {
                hadConversation[i] |= loaded[i];
            }
        }

        // dreamDreamt
        if (PlayerPrefs.HasKey(DREAM_DREAMT_PREFS_KEY))
        {
            string b64 = PlayerPrefs.GetString(DREAM_DREAMT_PREFS_KEY, "");
            bool[] loaded = DecodeBoolArrayFromBase64(b64, 10);
            for (int i = 0; i < 10; i++)
            {
                dreamDreamt[i] |= loaded[i];
            }
        }
    }

    public void MarkHadConversation(int conversationIndex)
    {
        EnsureCrossSaveAchievementArrays();
        if (conversationIndex < 0 || conversationIndex >= hadConversation.Length) return;
        if (hadConversation[conversationIndex]) return;

        hadConversation[conversationIndex] = true;
        SaveHadConversationToPrefs();
    }

    public void MarkDreamDreamt(int dreamIndex)
    {
        EnsureCrossSaveAchievementArrays();
        if (dreamIndex < 0 || dreamIndex >= dreamDreamt.Length) return;
        if (dreamDreamt[dreamIndex]) return;

        dreamDreamt[dreamIndex] = true;
        SaveDreamDreamtToPrefs();
    }

    private void SaveHadConversationToPrefs()
    {
        PlayerPrefs.SetString(HAD_CONVERSATION_PREFS_KEY, EncodeBoolArrayToBase64(hadConversation));
        PlayerPrefs.Save();
    }

    private void SaveDreamDreamtToPrefs()
    {
        PlayerPrefs.SetString(DREAM_DREAMT_PREFS_KEY, EncodeBoolArrayToBase64(dreamDreamt));
        PlayerPrefs.Save();
    }

    private static string EncodeBoolArrayToBase64(bool[] arr)
    {
        if (arr == null || arr.Length == 0) return "";
        int numBytes = (arr.Length + 7) / 8;
        byte[] bytes = new byte[numBytes];

        for (int i = 0; i < arr.Length; i++)
        {
            if (!arr[i]) continue;
            int byteIndex = i >> 3;
            int bitIndex = i & 7;
            bytes[byteIndex] |= (byte)(1 << bitIndex);
        }

        return System.Convert.ToBase64String(bytes);
    }

    private static bool[] DecodeBoolArrayFromBase64(string b64, int expectedLength)
    {
        bool[] result = new bool[expectedLength];
        if (string.IsNullOrEmpty(b64)) return result;

        byte[] bytes;
        try
        {
            bytes = System.Convert.FromBase64String(b64);
        }
        catch
        {
            return result;
        }

        int maxBits = Mathf.Min(expectedLength, bytes.Length * 8);
        for (int i = 0; i < maxBits; i++)
        {
            int byteIndex = i >> 3;
            int bitIndex = i & 7;
            result[i] = (bytes[byteIndex] & (1 << bitIndex)) != 0;
        }

        return result;
    }
    
    /// <summary>
    /// Gets whether a critter type has been defeated.
    /// </summary>
    /// <param name="index">Critter type index (0-63, where 0 = EObjectType index 64)</param>
    /// <returns>True if defeated, false otherwise</returns>
    public bool GetDefeatedCritter(int index)
    {
        if (index < 0 || index >= 64)
        {
            return false;
        }
        
        return (defeatedCritterFlags & (1UL << index)) != 0;
    }
    
    public bool DebugShowQuestFlags;

    public void OnGUI()
    {
        if (DebugShowQuestFlags)
        {
            GUI.depth = (int)EGUIDepth.PlayerDataDebug;

            for (int i = 0; i < 256; ++i)
            {
                GUI.Label(new Rect(20 * (i % 16), 20 * (i / 16), 20, 20), $"{questFlags[i]}");
            }
        }
    }
}
