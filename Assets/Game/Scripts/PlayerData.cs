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
    // What exploring has earned below a whole unit, paid once it adds up to one (PlayerObject.AddExplorationXP()).
    public float explorationXpFraction;
    public int charLevel = 1;
    // One, not three: character creation writes a single point (UW.EXE 0x6c84e,
    // movb $0x1, 0x52(%bx), right beside the level being set to 1 and the experience
    // tier high-water to 0). World.unity holds 3, which only a Play in the Editor without
    // making a character sees: CreateCharacter sets 1.
    public int skillPoints = 1;
    public int skillPointsXpTier; // high-water of displayXp/300 tiers already awarded
    public bool easy;

    public bool dead;
    
    public int strength;
    public int intellect;
    public int dexterity;

    public int poison;
    public int hunger = 64;
    public int fatigue;
    public int drunkenness;

    [EnumNamedArray(typeof(ESkill))]
    public int[] skill = new int[Enum.GetNames(typeof(ESkill)).Length];

    [EnumNamedArray(typeof(EQuestFlag))]
    public int[] questFlags = new int[64];

    public int[] globalVars = new int[64];

    /// <summary>
    /// One bit per level, set when that level has written its automatic save. It lives on the
    /// character rather than on the save manager so it travels in the save file: walking back into
    /// a level does not write a second one, and a new character starts with a clean set.
    /// An older save has no such field and reads as zero, so the first visit to each level after
    /// loading it writes one automatic save. That is the safe way round and it settles by itself.
    /// </summary>
    public int autoSavedLevels;

    /// <summary>
    /// For each level, the game time up to which its repopulation checks have been made (Repopulation).
    /// 0, or no array at all in an older save, is a level whose clock has not started.
    /// </summary>
    public double[] repopulationClock;

    /// <summary>
    /// Garamon's dreams, a bit each as the original keeps them (player->dreams): dreams 0, 1 and 4-9
    /// set once seen, dreams 2 and 3 set while due and cleared once seen.
    /// </summary>
    public int dreamBits;
    /// <summary>How many times one of dreams 4-9 has been shown, repeats included: it sets the wait before the next.</summary>
    public int dreamsShown;
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
    // Sseetharee's talk freed the prisoner, which the player can reach only in the lizardmen's
    // tongue; opening his cell by hand frees him too, but proves nothing.
    public bool freedMurgoByTrade;
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

    public void ResetDreams()
    {
        dreamBits = 0;
        dreamsShown = 0;
        timeOfLastDream = 0;
    }

    /// <summary>
    /// The dreams of a save made before dreamBits: the list of those still to come, which Tyball's death
    /// replaced with dreams 2 and 3. Which of 4-9 were seen before that is lost, so they start again.
    /// </summary>
    public void DreamsFromOldSave(List<int> remaining)
    {
        ResetDreams();
        if (remaining.Count == 0 || remaining.Contains(2) || remaining.Contains(3))
        {
            dreamBits = 3;
            if (remaining.Contains(2))
            {
                dreamBits |= 4;
            }
            if (remaining.Count == 0)
            {
                dreamsShown = 6;
            }
            return;
        }
        foreach (int dream in new[] { 0, 1, 4, 5, 6, 7, 8, 9 })
        {
            if (!remaining.Contains(dream))
            {
                dreamBits |= 1 << dream;
                if (dream >= 4)
                {
                    ++dreamsShown;
                }
            }
        }
    }

    /// <summary>Dream 2 is due: Tyball is dead (UW.EXE TyballDeath_ovr107_13D1, WORLDEV.C).</summary>
    public void SetTyballDead()
    {
        dreamBits |= 1 << 2;
    }

    /// <summary>
    /// Dream 3 is due: a talisman came to rest in the lava at the heart of level 8 before Garamon was
    /// buried (UW.EXE 0x2be7f, mob_to_static, OBJPHYS.C).
    /// </summary>
    public void SetTalismanInLavaTooEarly()
    {
        dreamBits |= 1 << 3;
    }

    /// <summary>
    /// The dream at the end of a full sleep the player chose, or -1 (Sleeping). Dreams 0-3 as the
    /// original picks them (UW.EXE 0x81de3, dream, SKILLS.C), at the first such sleep after their
    /// event: dream 0 at once, dream 1 off level 1, 2 and 3 while due. None at all once Garamon is
    /// buried.
    /// </summary>
    /// <remarks>
    /// Dreams 4-9 are a middle way of ours. In the original one of the six is drawn one full sleep in
    /// four (one in eight fed on a bedroll), and nothing if it was seen already: about sixty full
    /// sleeps to see them all. Kweepa's showed one at every sleep at least 10 hours after the last
    /// dream, each once. Here one of the six is drawn, repeats included as in the original, at a full
    /// sleep at least 10 hours after the last dream, and each one shown makes the next wait two hours
    /// longer, up to 20.
    /// </remarks>
    private int PickDreamInternal()
    {
        if (garamonAtRest)
        {
            return -1;
        }

        int dream = -1;
        if ((dreamBits & 1) == 0)
        {
            dream = 0;
        }
        else if (LevelLoader.sLevelLoader.loadedLevel > 1 && (dreamBits & 2) == 0)
        {
            dream = 1;
        }
        else if ((dreamBits & 4) != 0)
        {
            dream = 2;
        }
        else if ((dreamBits & 8) != 0)
        {
            dream = 3;
        }

        if (dream >= 0)
        {
            // seen for 0 and 1, no longer due for 2 and 3
            dreamBits ^= 1 << dream;
        }
        else
        {
            double wait = (10 + 2 * Mathf.Min(dreamsShown, 5)) * 60 * 60;
            if (gameTime - timeOfLastDream < wait)
            {
                return -1;
            }
            dream = 4 + UnityEngine.Random.Range(0, 6);
            dreamBits |= 1 << dream;
            ++dreamsShown;
        }

        timeOfLastDream = gameTime;
        return dream;
    }

    public static int PickDream()
    {
        return sData.PickDreamInternal();
    }

    public void Start()
    {
        sData = this;

        // in case we skip the character creation
        ResetDreams();

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

            for (int i = 0; i < questFlags.Length; ++i)
            {
                GUI.Label(new Rect(20 * (i % 16), 20 * (i / 16), 20, 20), $"{questFlags[i]}");
            }
        }
    }
}
