using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using System.Collections.Generic;

public class SaveGameManager : MonoBehaviour
{
    public static SaveGameManager sInstance;
    
    private string savesDirectoryPath;

    [System.Serializable]
    public class SaveSlotInfo
    {
        public string slotName;
        public string displayName; // same as slotName, but could map differently later
        public string savedAtIso;
        public int level;
        public string playerName;
        public int xp;
        public int playerClass;
        public string inGameTime;
        public int charLevel;
    }
    
    private void Awake()
    {
        if (sInstance != null && sInstance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        sInstance = this;
        DontDestroyOnLoad(gameObject);
        
        savesDirectoryPath = Application.persistentDataPath + "/Saves";
        try
        {
            if (!Directory.Exists(savesDirectoryPath))
            {
                Directory.CreateDirectory(savesDirectoryPath);
            }
        }
        catch { }
    }

    // --- Save slot kinds -----------------------------------------------------------------------
    //
    // A slot name is the file name, and it says where the save came from: "Slot" in front of one
    // the player made by hand, "Quick" in front of the rolling quicksaves, and the character's own
    // name for the one written on first setting foot on a level. Only the two prefixes the game
    // writes are ever read back - they are what lets the five quicksaves roll over each other and
    // a level's autosave replace the one before, while leaving every save made by hand alone -
    // and the lists are sorted by date and show the name the player sees.
    //
    // A quicksave's file carries the date down to the second and no number, because the number the
    // player sees is its place in the list, newest first, and that changes every time one is
    // written. A number in the file name would disagree with the screen as soon as the oldest was
    // rolled over.

    /// <summary>
    /// How many quicksaves are kept. The sixth use of the quicksave key overwrites the oldest of
    /// them, and nothing else: a manual save and a level's automatic save are never rolled over.
    /// </summary>
    public const int QuickSaveSlotCount = 5;

    private const string ManualSlotPrefix = "Slot";
    private const string QuickSlotPrefix = "Quick";

    /// <summary>What a level's own save calls itself on its row. Eight characters, like "Quick 01".</summary>
    public const string AutoSaveRowKind = "Autosave";

    /// <summary>
    /// How many characters of the player's name a generated name keeps, on screen and on disk
    /// alike: a long name makes a row nobody can read, and a file name nobody wants to look at.
    /// </summary>
    private const int GeneratedNameLength = 8;

    /// <summary>
    /// Is this the save the game wrote on first setting foot on a level. The save list hides these
    /// as it hides the quicksaves: a save made by hand on one would keep its file, and with it the
    /// name that marks it as the game's to replace.
    /// </summary>
    public static bool IsAutoSlot(string slotName)
    {
        return !string.IsNullOrEmpty(slotName) && slotName.StartsWith(AutoSaveRowKind + " - ");
    }

    /// <summary>
    /// Is this one of the rolling quicksaves. The save list hides them: they are the game's to
    /// write over, so offering one as a place to put a save by hand would be a trap.
    /// </summary>
    public static bool IsQuickSlot(string slotName)
    {
        return !string.IsNullOrEmpty(slotName) && slotName.StartsWith(QuickSlotPrefix);
    }

    /// <summary>
    /// When a save was written, in ticks, or 0 when the header carries no readable date.
    /// </summary>
    public static long SavedAtTicks(SaveSlotInfo info)
    {
        if (info != null && !string.IsNullOrEmpty(info.savedAtIso)
            && System.DateTime.TryParse(info.savedAtIso, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime when))
        {
            return when.ToUniversalTime().Ticks;
        }

        return 0;
    }

    /// <summary>
    /// Every save on disk, newest first. This is the order both save lists show, so the game a
    /// player is most likely to want is always the top row.
    /// </summary>
    public SaveSlotInfo[] ListSaveSlotsNewestFirst()
    {
        SaveSlotInfo[] slots = ListSaveSlots();
        // Sort is not stable, so two saves written inside the same second would otherwise swap
        // places from one refresh to the next: the slot name settles it.
        System.Array.Sort(slots, (a, b) =>
        {
            int byDate = SavedAtTicks(b).CompareTo(SavedAtTicks(a));
            return byDate != 0 ? byDate : string.CompareOrdinal(a?.slotName, b?.slotName);
        });
        return slots;
    }

    private static SaveSlotInfo FindSlot(SaveSlotInfo[] slots, string slotName)
    {
        foreach (SaveSlotInfo info in slots)
        {
            if (info != null && info.slotName == slotName)
            {
                return info;
            }
        }

        return null;
    }

    /// <summary>
    /// The player's name cut down to what can safely be part of a file name: letters, digits, -, _
    /// and . only, and no more than eight characters, to leave room for the level and the date.
    /// </summary>
    /// <remarks>
    /// Only the name on disk needs this. The name shown in the lists is the player's own, spaces,
    /// apostrophes and all: there is nothing to protect there.
    /// </remarks>
    public static string SanitizeForFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "";
        }

        StringBuilder sb = new StringBuilder(GeneratedNameLength);
        foreach (char c in name)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                || c == '-' || c == '_' || c == '.')
            {
                sb.Append(c);
                if (sb.Length == GeneratedNameLength)
                {
                    break;
                }
            }
        }

        return sb.ToString();
    }

    private static string PlayerNameOrAvatar()
    {
        return NormalizedPlayerName(PlayerData.sData != null ? PlayerData.sData.playerName : "");
    }

    /// <summary>
    /// A character's name as the saves compare it: trimmed, and "Avatar" when there is none.
    /// </summary>
    /// <remarks>
    /// A header keeps the name exactly as it was typed, trailing space and all, so two names are
    /// only compared after both have been through here. Without it "Bob " never matched "Bob",
    /// and that character's automatic saves were never replaced.
    /// </remarks>
    private static string NormalizedPlayerName(string who)
    {
        return string.IsNullOrWhiteSpace(who) ? "Avatar" : who.Trim();
    }

    /// <summary>
    /// The player's name as it goes into a file name, falling back to "Avatar" when nothing in it
    /// survives the sanitiser - a name written only in letters outside A to Z, say.
    /// </summary>
    private static string FileNamePlayerName()
    {
        string who = SanitizeForFileName(PlayerNameOrAvatar());
        return string.IsNullOrEmpty(who) ? "Avatar" : who;
    }

    /// <summary>
    /// The player's name cut to eight characters, keeping whatever he typed inside them.
    /// </summary>
    /// <remarks>
    /// The cut is for the eye, not for the file system, so it applies to the name in the list as
    /// well: a character called after half a dynasty would otherwise push the level off the end of
    /// a row that is only so wide.
    /// </remarks>
    private static string ShortPlayerName()
    {
        return ShortName(PlayerNameOrAvatar());
    }

    /// <summary>
    /// Any name cut to the same eight characters, for a row that has to fit beside a level and a
    /// kind. Public because the lists build a quicksave's row from the name in its header rather
    /// than from the live player, who may be someone else entirely.
    /// </summary>
    public static string ShortName(string who)
    {
        if (string.IsNullOrWhiteSpace(who))
        {
            return "Avatar";
        }

        who = who.Trim();
        return who.Length > GeneratedNameLength ? who.Substring(0, GeneratedNameLength).TrimEnd() : who;
    }

    /// <summary>
    /// The name shown in the lists for a save the game wrote by itself: what kind of save it is,
    /// who it belongs to, and how deep it was taken - "Autosave - Cabirus - Lvl 3".
    /// </summary>
    /// <remarks>
    /// The kind comes first so the rows line up in a column, and "Quick 01" is eight characters
    /// wide for the same reason "Autosave" is: the two names the game writes are the same length,
    /// so the player's name starts on the same column in every row the game made itself.
    /// No date here, although the file name carries one. The panel already prints the date of the
    /// selected save beside its screenshot, and a second copy of it on the row was long enough to
    /// run off the end of the list.
    /// </remarks>
    public static string BuildGeneratedSaveName(string kind, int level)
    {
        return $"{kind} - {ShortPlayerName()} - {DungeonLevelLabel(level)}";
    }

    /// <summary>
    /// How a dungeon level is written on a save row and in a save's file name.
    /// </summary>
    /// <remarks>
    /// It said "Abyss Lvl 3" until it was seen in the game on 23 September 2026, and the row was
    /// too long for the window it has to fit. What tells this number apart from the character's
    /// own is the rest of the row: a save the game wrote names its kind first, and the character's
    /// level is not on it at all.
    /// </remarks>
    public static string DungeonLevelLabel(int level)
    {
        return $"Lvl {level}";
    }

    /// <summary>
    /// The file name for a save the game wrote by itself: the same three things, but tamed.
    /// </summary>
    /// <remarks>
    /// The date cannot be the local one here. A short date is full of slashes and a short time of
    /// colons, and neither can go in a file name: the sanitiser would turn every one of them into
    /// an underscore, which is both ugly and ambiguous. So the file gets the international order,
    /// yyyy-MM-dd HH.mm, which sorts by date on its own in any file browser, and uses only - and .
    /// The list shows the player his own format either way.
    /// </remarks>
    public static string BuildGeneratedSlotName(int level)
    {
        string who = FileNamePlayerName();
        string when = System.DateTime.Now.ToString("yyyy-MM-dd HH.mm",
            System.Globalization.CultureInfo.InvariantCulture);
        // The same shape as the row it makes, with the date on the end: the folder reads like the
        // list does. "Autosave - Cabirus - Lvl 3 2026-09-22 14.05".
        return $"{AutoSaveRowKind} - {who} - {DungeonLevelLabel(level)} {when}";
    }

    /// <summary>
    /// The Ethereal Void. The save panel refuses to open there
    /// (SaveLoadGUI.CanOpenSaveLoadFromGameplay), so the game does not save there by itself either.
    /// </summary>
    private const int VoidLevel = 9;

    /// <summary>Can the game be saved at all right now.</summary>
    /// <remarks>
    /// Not while the player is dead: there is a moment between the killing blow and the death
    /// cutscene, and a save taken in it would restore a dead character that nothing ever buries.
    /// </remarks>
    public bool CanSaveNow()
    {
        return PlayerObject.Player != null && PlayerData.sData != null && LevelLoader.sLevelLoader != null
            && LevelLoader.sLevelLoader.loadedLevel > 0 && LevelLoader.sLevelLoader.loadedLevel != VoidLevel
            && !PlayerData.sData.dead;
    }

    /// <summary>How long the quicksave key is dead after it has written one.</summary>
    /// <remarks>
    /// Two reasons, and the second is the one that matters. It stops a key held down from filling
    /// the five slots with the same moment; and it is what makes the file name unique, since that
    /// name is the clock read to the second.
    /// </remarks>
    private const float QuickSaveCooldownSeconds = 1.0f;

    private float lastQuickSaveTime = -QuickSaveCooldownSeconds;

    /// <summary>
    /// Writes one of the rolling quicksaves. Returns the slot written, or null when the game
    /// cannot be saved at this moment, or when one was written a moment ago.
    /// </summary>
    public string QuickSave()
    {
        if (!CanSaveNow())
        {
            return null;
        }

        float now = Time.unscaledTime;
        if (now - lastQuickSaveTime < QuickSaveCooldownSeconds)
        {
            return null;
        }

        lastQuickSaveTime = now;

        int level = LevelLoader.sLevelLoader.loadedLevel;
        // The file reads like the row it will make, minus the number, which is the row's place in
        // the list and not a property of the file: "Quick - Cabirus - Lvl 4 2026-09-22 14.05.33".
        string slotName = $"{QuickSlotPrefix} - {FileNamePlayerName()} - "
            + $"{DungeonLevelLabel(level)} "
            + System.DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", System.Globalization.CultureInfo.InvariantCulture);
        // No number in the stored name either: the lists put the player's own name, the place in
        // the list and the level together when they draw the row.
        // Nothing is pruned unless the new one is on disk: with a full disk the oldest would go and
        // nothing would take its place.
        if (!SaveGameToSlot(slotName, BuildGeneratedSaveName("Quick", level)))
        {
            return null;
        }

        RequestScreenshotForSlot(slotName);
        PruneQuickSaves();
        return slotName;
    }

    /// <summary>How recent a level's previous automatic save has to be to be replaced.</summary>
    private const double AutoSaveReplaceHours = 24.0;

    /// <summary>
    /// Keeps one automatic save per level per character, as long as the one it replaces is from
    /// the same day's play.
    /// </summary>
    /// <remarks>
    /// Without this, loading an older save and walking the same stairs again leaves a row for
    /// every visit, since the file name carries the time and never collides with the one before.
    /// Three things have to agree before anything is deleted: the level, the character - matched
    /// on the name in each save's own header, not the shortened one in the file name, so two who
    /// begin alike cannot delete each other's - and the age. Only a save from the last
    /// <see cref="AutoSaveReplaceHours"/> hours is replaced: a year-old game that happens to share
    /// a character name is another playthrough, not the one being played, and losing it to a
    /// coincidence of names would be the worst thing this feature could do. A save whose date
    /// cannot be read is left alone for the same reason.
    /// </remarks>
    private void PruneOldAutoSaves(int level, string keepSlotName)
    {
        string who = PlayerNameOrAvatar();
        long now = System.DateTime.UtcNow.Ticks;
        long window = System.TimeSpan.FromHours(AutoSaveReplaceHours).Ticks;

        foreach (SaveSlotInfo info in ListSaveSlots())
        {
            if (info == null || info.slotName == keepSlotName || !IsAutoSlot(info.slotName))
            {
                continue;
            }

            if (info.level != level || NormalizedPlayerName(info.playerName) != who)
            {
                continue;
            }

            // A save made by hand over an autosave, before the save list stopped offering them,
            // kept the autosave's file but carries the name the player typed. It is his now.
            if (info.displayName == null || !info.displayName.StartsWith(AutoSaveRowKind + " - "))
            {
                continue;
            }

            long when = SavedAtTicks(info);
            if (when == 0 || now - when >= window)
            {
                continue;
            }

            DeleteSaveSlot(info.slotName);
        }
    }

    /// <summary>Keeps the newest <see cref="QuickSaveSlotCount"/> quicksaves and deletes the rest.</summary>
    private void PruneQuickSaves()
    {
        SaveSlotInfo[] slots = ListSaveSlotsNewestFirst();
        int kept = 0;
        foreach (SaveSlotInfo info in slots)
        {
            if (info == null || !IsQuickSlot(info.slotName))
            {
                continue;
            }

            ++kept;
            if (kept > QuickSaveSlotCount)
            {
                DeleteSaveSlot(info.slotName);
            }
        }
    }

    /// <summary>
    /// The first free Slot0, Slot1, ... name, for a save the player is making by hand.
    /// </summary>
    public string NextFreeManualSlot()
    {
        SaveSlotInfo[] slots = ListSaveSlots();
        for (int i = 0; i < 1000; ++i)
        {
            string slotName = ManualSlotPrefix + i;
            if (FindSlot(slots, slotName) == null)
            {
                return slotName;
            }
        }

        // A thousand manual saves is not a case worth a nicer answer, but it is worth not
        // overwriting Slot0 in silence.
        return ManualSlotPrefix + System.DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    }

    /// <summary>
    /// Asks for the automatic save of a level, the first time this character sets foot on it.
    /// </summary>
    /// <remarks>
    /// The levels already saved are remembered in the character, not in this component, so the
    /// record travels with the save file: coming back to a level does not write a second one, and
    /// a second character gets its own set.
    ///
    /// The save itself is put off until the screen has faded back in. Writing it here would catch
    /// the player mid transition, before the teleport that follows the level load has settled him,
    /// and the thumbnail would be a black frame.
    /// </remarks>
    public void RequestAutoSaveForLevel(int level)
    {
        if (level <= 0 || level >= 32 || level == VoidLevel || PlayerData.sData == null)
        {
            return;
        }

        int bit = 1 << level;
        if ((PlayerData.sData.autoSavedLevels & bit) != 0 || pendingAutoSaveLevel == level)
        {
            return;
        }

        // The bit is set when the save is actually written, not here: if the save cannot happen -
        // the level was left again while the screen was still fading, say - the level should get
        // another chance rather than be marked done for a save that never existed.
        pendingAutoSaveLevel = level;
        StartCoroutine(AutoSaveWhenSettled(level));
    }

    /// <summary>The level whose automatic save is waiting for the fade, or 0.</summary>
    private int pendingAutoSaveLevel;

    /// <summary>Is the level built and populated, rather than merely named.</summary>
    /// <remarks>
    /// The one condition that actually matters to a save, and the one that was missing. A level
    /// with geometry and no objects is a level that has not finished loading, and a save taken
    /// there is an empty world that the loader cannot repair afterwards.
    /// </remarks>
    private static bool LevelHasObjects(int level)
    {
        Level[] levels = LevelLoader.sLevelLoader != null ? LevelLoader.sLevelLoader.levels : null;
        // First, not Count: worldObj is an ObservableLinkedList, which wraps a LinkedList rather
        // than extending one, and the only members it exposes are AddFirst, AddLast, Remove,
        // Contains, GetEnumerator and First. Count would compile here under -nostdlib and fail
        // in Unity as a CS1061, which is one of the codes that check throws away.
        return levels != null && level >= 0 && level < levels.Length
            && levels[level] != null && levels[level].worldObj.First != null;
    }

    private IEnumerator AutoSaveWhenSettled(int level)
    {
        // Give the frame away before looking at anything. A coroutine runs inside StartCoroutine
        // until its first yield, so a wait whose condition is already true never yields at all -
        // and this one is started from the top of LevelLoader.LoadLevel, which means the save
        // would be written before the level is built. That is exactly what happened to the first
        // level of a new character, whose fade is already clear when the level is asked for: the
        // file came out 1.3 KB against the 500 KB of a real one, and the world it restored was
        // empty. Measured on three characters in a row on 22 September 2026.
        yield return null;

        // Then wait for the fade AND for the level to have its objects. Five seconds, then give
        // up: an automatic save that does not happen is a nuisance, but one that writes an empty
        // world is data loss, because nothing repopulates a level that a save says is empty.
        //
        // The five seconds are of play, not of the clock, and nothing is saved while the game is
        // paused. The fade runs on unscaled time, so without this the save went ahead with the
        // save panel open, took the panel into its screenshot, and could even be written after
        // the player had loaded another game from that panel. The save panel, the map, a
        // conversation, a cutscene and the on screen keyboard all stop time.
        float waited = 0.0f;
        while (waited < 5.0f)
        {
            if (Time.timeScale > 0.0f)
            {
                if (PlayerObject.Player != null && PlayerObject.Player.fade <= 0.05f
                    && LevelLoader.sLevelLoader != null && LevelLoader.sLevelLoader.loadedLevel == level
                    && LevelHasObjects(level))
                {
                    break;
                }

                waited += Time.unscaledDeltaTime;
            }

            yield return null;
        }

        // Only if it is still this level's: a request for another level may have taken its place.
        if (pendingAutoSaveLevel == level)
        {
            pendingAutoSaveLevel = 0;
        }

        // Asked again now, not only when the request was made: a game loaded while this waited
        // brings its own record of which levels are done.
        if (!CanSaveNow() || Time.timeScale <= 0.0f || LevelLoader.sLevelLoader.loadedLevel != level
            || (PlayerData.sData.autoSavedLevels & (1 << level)) != 0 || !LevelHasObjects(level))
        {
            // The bit stays clear on purpose: the level gets another chance next time rather than
            // being marked done for a save that never happened.
            yield break;
        }

        string slotName = BuildGeneratedSlotName(level);
        // The bit is set before writing, because it has to be inside the file; if the write fails
        // it is taken back, and the level's previous save is only replaced by one that exists.
        PlayerData.sData.autoSavedLevels |= 1 << level;
        if (!SaveGameToSlot(slotName, BuildGeneratedSaveName(AutoSaveRowKind, level)))
        {
            PlayerData.sData.autoSavedLevels &= ~(1 << level);
            yield break;
        }

        RequestScreenshotForSlot(slotName);
        PruneOldAutoSaves(level, slotName);
    }

    // Multi-slot API
    /// <summary>Writes the game to a slot. Returns false when nothing, or not all of it, was written.</summary>
    public bool SaveGameToSlot(string slotName, string displayName = null)
    {
        if (PlayerObject.Player == null || PlayerData.sData == null || LevelLoader.sLevelLoader == null)
        {
            Debug.LogError("Cannot save: Required components not initialized");
            return false;
        }
        if (string.IsNullOrWhiteSpace(slotName))
        {
            Debug.LogError("Cannot save: slotName is empty");
            return false;
        }

        SaveGameData saveData = new SaveGameData();
        saveData.currentLevel = LevelLoader.sLevelLoader.loadedLevel;
        saveData.slotName = slotName;
        saveData.displayName = displayName ?? slotName;
        saveData.savedAtIso = System.DateTime.UtcNow.ToString("o");
        SavePlayerData(saveData.playerData);
        SaveInventoryData(saveData.inventoryData);
        SaveWorldObjects(saveData);
        SaveMapData(saveData);

        string json = JsonUtility.ToJson(saveData, prettyPrint: true);
        string filePath = GetSlotFilePath(slotName);
        string headerFilePath = GetSlotHeaderFilePath(slotName);
        try
        {
            // Write compressed full save file
            byte[] compressed = CompressString(json);
            File.WriteAllBytes(filePath, compressed);
            Debug.Log($"Saved slot '{slotName}' to {filePath} (compressed {json.Length} -> {compressed.Length} bytes)");
            
            // Write header file for fast listing
            SaveSlotHeader header = new SaveSlotHeader
            {
                slotName = saveData.slotName,
                displayName = saveData.displayName,
                savedAtIso = saveData.savedAtIso,
                currentLevel = saveData.currentLevel,
                playerName = saveData.playerData?.playerName,
                xp = saveData.playerData?.xp ?? 0,
                playerClass = saveData.playerData?.playerClass ?? 0,
                inGameTime = FormatInGameTime(saveData.playerData?.gameTime ?? 0.0),
                charLevel = saveData.playerData?.charLevel ?? 1
            };
            string headerJson = JsonUtility.ToJson(header, prettyPrint: false);
            File.WriteAllText(headerFilePath, headerJson);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save slot '{slotName}': {e.Message}");
            return false;
        }

        return true;
    }

    public void LoadGameFromSlot(string slotName)
    {
        if (string.IsNullOrWhiteSpace(slotName))
        {
            Debug.LogError("Cannot load: slotName is empty");
            return;
        }
        string filePath = GetSlotFilePath(slotName);
        if (!File.Exists(filePath))
        {
            Debug.LogError($"No save file found for slot '{slotName}' at path: {filePath}");
            return;
        }
        {
            byte[] compressed = File.ReadAllBytes(filePath);
            string json = DecompressString(compressed);
            Debug.Log($"Loaded slot '{slotName}' (decompressed {compressed.Length} -> {json.Length} bytes)");
            SaveGameData saveData;
            {
                saveData = JsonUtility.FromJson<SaveGameData>(json);
                if (saveData == null)
                {
                    Debug.LogError("Failed to parse save file - JsonUtility returned null");
                    return;
                }
            }
            
            // Check if we need to change levels
            if (LevelLoader.sLevelLoader != null && LevelLoader.sLevelLoader.loadedLevel != saveData.currentLevel)
            {
                // Deactivate current level if needed
                if (LevelLoader.sLevelLoader.loadedLevel > 0)
                {
                    LevelLoader.sLevelLoader.DeactivateCurrentLevel();
                }
                // Just set the loaded level - EnsureLevelsInitialized will load geometry
                // Don't call LoadLevel() since that would create objects from ark that we'll delete anyway
                LevelLoader.sLevelLoader.loadedLevel = saveData.currentLevel;
                Cheats.sCheats.level = saveData.currentLevel;
            }
            
            float startTime = Time.realtimeSinceStartup;
            
            // Strip dynamic objects while Level structs still exist, then drop all level slots so
            // EnsureLevelsInitialized matches a fresh loader (mid-game load fix).
            DeleteNonPersistentObjects();
            LevelLoader.sLevelLoader.PrepareForSaveLoad();
            
            // Ensure all levels with saved objects are initialized from lev.ark
            // (Geometry deactivation is handled in EnsureLevelInitialized to prevent physics interactions)
            EnsureLevelsInitialized(saveData);
            
            // Ensure loadedLevel is set correctly after initializing all levels
            // (LoadLevelGeometry changes loadedLevel, so we need to restore it)
            LevelLoader.sLevelLoader.loadedLevel = saveData.currentLevel;
            Cheats.sCheats.level = saveData.currentLevel;
            
            // Restore player data
            LoadPlayerData(saveData.playerData);
            
            // Save character sex to PlayerPrefs for frontend background display
            PlayerPrefs.SetString("LastCharacterSex", PlayerData.sData.female ? "female" : "male");
            PlayerPrefs.Save();
            
            // Restore world objects first so linked spells exist in objects[] before inventory
            // PostLoadInitialize (e.g. wands/sceptres recovering charges from a spell link).
            LoadWorldObjects(saveData);
            
            // Restore inventory
            LoadInventoryData(saveData.inventoryData);
            
            if (LevelLoader.sLevelLoader != null)
                LevelLoader.sLevelLoader.TryRepairLevelObjects();
            
            // Call PostLoadInitialize on all loaded objects
            PostLoadInitializeAllLoadedObjects();
            PostLoadInitializeCritterLootIfUnnamed();
            FinishWandChargeRecoveryAfterLoad();

            // Link objects to tiles (bridges, stairs, moving platforms)
            // WorldInitialize isn't called during save/load, so we need to manually link them
            LinkObjectsToTiles();
            
            // Restore map data
            LoadMapData(saveData.mapData);

            // Back-compat: old saves didn't store mapTilesRevealed; reconstruct from map data when possible
            if (PlayerData.sData != null && PlayerData.sData.mapTilesRevealed == 0 && saveData.mapData != null)
            {
                PlayerData.sData.mapTilesRevealed = ComputeMapTilesRevealedFromSave(saveData.mapData);
            }
            
            // Create lava lights for the loaded level
            LevelLoader.sLevelLoader.CreateLavaLights();

            PlayerObject.Player?.GetComponent<PlayerEffectsController>()?.ResetMushroomTripAfterLoad();
            
            Debug.Log($"Game loaded successfully in {Time.realtimeSinceStartup - startTime:F3}s");
        }
    }

    public SaveSlotInfo[] ListSaveSlots()
    {
        try
        {
            if (!Directory.Exists(savesDirectoryPath))
            {
                return new SaveSlotInfo[0];
            }

            // read header files for fast listing
            string[] headerFiles = Directory.GetFiles(savesDirectoryPath, "*.header.json");
            Dictionary<string, SaveSlotInfo> slotMap = new Dictionary<string, SaveSlotInfo>();

            // Process header files (fast path)
            foreach (string headerFile in headerFiles)
            {
                try
                {
                    string headerJson = File.ReadAllText(headerFile);
                    SaveSlotHeader header = JsonUtility.FromJson<SaveSlotHeader>(headerJson);
                    if (header != null && !string.IsNullOrEmpty(header.slotName))
                    {
                        SaveSlotInfo info = new SaveSlotInfo
                        {
                            slotName = header.slotName,
                            displayName = !string.IsNullOrEmpty(header.displayName) ? header.displayName : header.slotName,
                            savedAtIso = !string.IsNullOrEmpty(header.savedAtIso) ? header.savedAtIso : File.GetLastWriteTimeUtc(headerFile).ToString("o"),
                            level = header.currentLevel,
                            playerName = header.playerName,
                            xp = header.xp,
                            playerClass = header.playerClass,
                            inGameTime = header.inGameTime,
                            charLevel = header.charLevel > 0 ? header.charLevel : 1
                        };
                        slotMap[header.slotName] = info;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Could not parse header file {headerFile}: {e.Message}");
                }
            }
            
            List<SaveSlotInfo> result = new List<SaveSlotInfo>(slotMap.Values);
            return result.ToArray();
        }
        catch
        {
            return new SaveSlotInfo[0];
        }
    }

    public void DeleteSaveSlot(string slotName)
    {
        string filePath = GetSlotFilePath(slotName);
        string headerFilePath = GetSlotHeaderFilePath(slotName);
        string screenshotPath = GetSlotScreenshotPath(slotName);
        // The PNG of a save made before the screenshots became JPEGs, so deleting a slot does not
        // leave its picture behind.
        string oldScreenshotPath = Path.Combine(savesDirectoryPath, SanitizeFileName(slotName) + ".png");
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            if (File.Exists(headerFilePath))
            {
                File.Delete(headerFilePath);
            }
            if (File.Exists(screenshotPath))
            {
                File.Delete(screenshotPath);
            }
            if (File.Exists(oldScreenshotPath))
            {
                File.Delete(oldScreenshotPath);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to delete slot '{slotName}': {e.Message}");
        }
    }

    private string GetSlotFilePath(string slotName)
    {
        string safe = SanitizeFileName(slotName);
        return Path.Combine(savesDirectoryPath, safe + ".json.gz");
    }

    private string GetSlotHeaderFilePath(string slotName)
    {
        string safe = SanitizeFileName(slotName);
        return Path.Combine(savesDirectoryPath, safe + ".header.json");
    }

    /// <summary>Where a screenshot for this slot is written. JPEG since 23 September 2026.</summary>
    public string GetSlotScreenshotPath(string slotName)
    {
        string safe = SanitizeFileName(slotName);
        return Path.Combine(savesDirectoryPath, safe + ".jpg");
    }

    /// <summary>
    /// The screenshot a slot actually has on disk: the JPEG it writes now, or the PNG it used to
    /// write, so saves made before the change still show their picture.
    /// </summary>
    public string FindSlotScreenshotPath(string slotName)
    {
        string jpg = GetSlotScreenshotPath(slotName);
        if (File.Exists(jpg))
        {
            return jpg;
        }

        string png = Path.Combine(savesDirectoryPath, SanitizeFileName(slotName) + ".png");
        return File.Exists(png) ? png : jpg;
    }

    /// <summary>
    /// How large a screenshot is kept, in pixels.
    /// </summary>
    /// <remarks>
    /// It is only ever shown in the preview panel, which is 570 by 332 in the reference pixels the
    /// rest of the layout uses, and the largest it is ever drawn is that times the scale cap - so
    /// 1140 by 664, which is what is kept. What it replaces: a full screen grab at whatever the
    /// monitor happens to be - 2560 by 1440 on the machine this was measured on - kept as a PNG,
    /// which came to 1.2 MB for every save.
    /// </remarks>
    public const int ScreenshotWidth = 1140;
    public const int ScreenshotHeight = 664;

    /// <summary>JPEG quality, for a picture nobody looks at closely.</summary>
    private const int ScreenshotQuality = 75;

    /// <summary>
    /// Takes a screenshot after the next frame and saves it for the given slot.
    /// Call after dismissing the save/load UI so the game view is visible.
    /// </summary>
    public void RequestScreenshotForSlot(string slotName)
    {
        if (string.IsNullOrWhiteSpace(slotName)) return;
        StartCoroutine(CaptureScreenshotForSlotRoutine(slotName));
    }

    private IEnumerator CaptureScreenshotForSlotRoutine(string slotName)
    {
        // Wait until UI is dismissed and game is rendered
        yield return null;
        yield return new WaitForEndOfFrame();

        string path = GetSlotScreenshotPath(slotName);
        Texture2D full = null;
        Texture2D small = null;
        RenderTexture scaled = null;
        RenderTexture wasActive = RenderTexture.active;
        try
        {
            full = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            full.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            full.Apply();

            // Down to the size it will be looked at, through a render texture so the card does the
            // filtering, and out as a JPEG: the picture of a dungeon has no flat colour for PNG to
            // exploit, so it was paying for lossless on the one kind of image that gains nothing.
            scaled = RenderTexture.GetTemporary(ScreenshotWidth, ScreenshotHeight, 0);
            Graphics.Blit(full, scaled);
            RenderTexture.active = scaled;

            small = new Texture2D(ScreenshotWidth, ScreenshotHeight, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, ScreenshotWidth, ScreenshotHeight), 0, 0);
            small.Apply();

            byte[] bytes = small.EncodeToJPG(ScreenshotQuality);
            File.WriteAllBytes(path, bytes);
            Debug.Log($"Screenshot saved to {path} ({bytes.Length} bytes, {ScreenshotWidth}x{ScreenshotHeight})");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save screenshot for slot '{slotName}': {e.Message}");
        }
        finally
        {
            RenderTexture.active = wasActive;
            if (scaled != null)
            {
                RenderTexture.ReleaseTemporary(scaled);
            }

            if (full != null)
            {
                Destroy(full);
            }

            if (small != null)
            {
                Destroy(small);
            }
        }
    }

    private static byte[] CompressString(string text)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(text);
        using (MemoryStream memoryStream = new MemoryStream())
        {
            using (GZipStream gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, true))
            {
                gzipStream.Write(buffer, 0, buffer.Length);
            }
            return memoryStream.ToArray();
        }
    }
    
    private static string DecompressString(byte[] compressedData)
    {
        using (MemoryStream memoryStream = new MemoryStream(compressedData))
        {
            using (GZipStream gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
            {
                using (MemoryStream decompressedStream = new MemoryStream())
                {
                    gzipStream.CopyTo(decompressedStream);
                    return Encoding.UTF8.GetString(decompressedStream.ToArray());
                }
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }

    private static string FormatInGameTime(double gameTimeSeconds)
    {
        // Mirror the day/time phrasing used in StatsPanel (string indices from uw.str)
        int day = 1 + (int)(gameTimeSeconds / (24 * 60 * 60));
        int thour = (int)(gameTimeSeconds % (24 * 60 * 60)) / (2 * 60 * 60);

        return $"day {day}, {StringLoader.GetString(1, 71 + thour)}";
    }
    
    
    private void SavePlayerData(PlayerSaveData data)
    {
        PlayerObject player = PlayerObject.Player;
        PlayerData playerData = PlayerData.sData;
        
        // Position and rotation
        data.position = player.transform.position;
        data.rotation = player.transform.rotation;
        
        // Basic stats
        data.female = playerData.female;
        data.leftHanded = playerData.leftHanded;
        data.portrait = playerData.portrait;
        data.playerClass = (int)playerData.playerClass;
        data.playerName = playerData.playerName;
        data.vitality = playerData.vitality;
        data.hp = playerData.hp;
        data.maxMana = playerData.maxMana;
        data.mana = playerData.mana;
        data.xp = playerData.xp;
        data.charLevel = playerData.charLevel;
        data.skillPoints = playerData.skillPoints;
        data.skillPointsXpTier = playerData.skillPointsXpTier;
        data.easy = playerData.easy;
        data.dead = playerData.dead;
        
        // Attributes
        data.strength = playerData.strength;
        data.intellect = playerData.intellect;
        data.dexterity = playerData.dexterity;
        
        // Status
        data.poison = playerData.poison;
        data.hunger = playerData.hunger;
        data.fatigue = playerData.fatigue;
        data.drunkenness = playerData.drunkenness;
        
        // Skills
        data.skill = new int[playerData.skill.Length];
        System.Array.Copy(playerData.skill, data.skill, playerData.skill.Length);
        
        // Quest flags and global vars
        data.questFlags = new int[playerData.questFlags.Length];
        System.Array.Copy(playerData.questFlags, data.questFlags, playerData.questFlags.Length);
        
        data.globalVars = new int[playerData.globalVars.Length];
        System.Array.Copy(playerData.globalVars, data.globalVars, playerData.globalVars.Length);
        
        // Dreams
        data.dreamsRemaining = new List<int>(playerData.dreamsRemaining);
        data.timeOfLastDream = playerData.timeOfLastDream;
        data.cupDreamIndex = playerData.cupDreamIndex;
        
        // Special flags
        data.cupFound = playerData.cupFound;
        data.saidFanlo = playerData.saidFanlo;
        data.saplingPlanted = playerData.saplingPlanted;
        data.saplingPlantedPosition = playerData.saplingPlantedPosition;
        data.saplingPlantedLevel = playerData.saplingPlantedLevel;
        data.moonstoneDropped = playerData.moonstoneDropped;
        data.moonstoneDroppedPosition = playerData.moonstoneDroppedPosition;
        data.moonstoneDroppedLevel = playerData.moonstoneDroppedLevel;
        data.talismansCollected = playerData.talismansCollected; 
        data.talismansDestroyed = playerData.talismansDestroyed;
        data.garamonAtRest = playerData.garamonAtRest;
        data.enteredGreenMoongate = playerData.enteredGreenMoongate;
        
        // Game time
        data.gameTime = playerData.gameTime;
        
        // Player movement state
        data.wasGrounded = player.wasGrounded;
        data.stamina = player.stamina;
        data.staminaRecoverDelayRemaining = player.staminaRecoverDelayRemaining;
        
        // Encountered critters for summon spell
        data.encounteredCritters = new List<EncounteredCritterEntry>();
        for (int i = 0; i < player.encounteredCritterData.Length; i++)
        {
            if (player.encounteredCritterData[i] != null)
            {
                CritterEncounterData critterData = player.encounteredCritterData[i].Value;
                data.encounteredCritters.Add(new EncounteredCritterEntry
                {
                    critterType = i + 64, // Convert array index to EObjectType (64-127)
                    level = critterData.level,
                    objectIndex = critterData.objectIndex,
                    originalHp = critterData.originalHp
                });
            }
        }
        
        // Magic state
        if (Magic.sMagic != null)
        {
            data.magicData = Magic.sMagic.SaveToData();
        }

        // Achievement-related progress (per save slot)
        data.numRepairs = playerData.numRepairs;
        data.numFishCaught = playerData.numFishCaught;
        data.booksRead = playerData.booksRead;
        data.mapTilesRevealed = playerData.mapTilesRevealed;
        data.gateTravelDistance = playerData.gateTravelDistance;
        data.waterWalkSteps = playerData.waterWalkSteps;
        data.lavaWalkSteps = playerData.lavaWalkSteps;
        data.playTime = playerData.playTime;
        data.booksBurned = playerData.booksBurned;
        data.pacifistStopped = playerData.pacifistStopped;
        // Away from the block of core stats above on purpose: that is where every mod that adds
        // a saved field puts it, and two of them land on the same line.
        data.autoSavedLevels = playerData.autoSavedLevels;

        if (playerData.openedChest != null)
        {
            data.openedChest = new bool[playerData.openedChest.Length];
            System.Array.Copy(playerData.openedChest, data.openedChest, playerData.openedChest.Length);
        }

        if (playerData.tutorialData != null)
        {
            data.tutorialData = CopyTutorialSaveData(playerData.tutorialData);
        }
        else
        {
            data.tutorialData = new TutorialSaveData();
        }
    }

    private static TutorialSaveData CopyTutorialSaveData(TutorialSaveData source)
    {
        if (source == null)
        {
            return new TutorialSaveData();
        }

        return new TutorialSaveData
        {
            version = source.version,
            completedMask = source.completedMask,
            gameplayStartTime = source.gameplayStartTime,
            firstPickupTime = source.firstPickupTime,
            hasOpenedInventory = source.hasOpenedInventory,
            runesStowedCount = source.runesStowedCount,
            movementAccumulatedDisplayTime = source.movementAccumulatedDisplayTime
        };
    }
    
    private void SaveMapData(SaveGameData saveData)
    {
        MapScreen mapScreen = FindAnyObjectByType<MapScreen>();
        if (mapScreen != null)
        {
            saveData.mapData = mapScreen.SaveToData();
        }
    }
    
    private void LoadPlayerData(PlayerSaveData data)
    {
        PlayerObject player = PlayerObject.Player;
        PlayerData playerData = PlayerData.sData;
        
        // Restore position and rotation
        player.TeleportTo(data.position, data.rotation);
        
        // Restore player movement state
        // Note: For old save files without this field, JsonUtility will default to false,
        // but it will be corrected on the next frame update in NormalMovement()
        if (player.cachedCharacterController != null)
        {
            player.wasGrounded = data.wasGrounded;
        }
        player.stamina = Mathf.Clamp01(data.stamina);
        player.staminaRecoverDelayRemaining = Mathf.Max(0f, data.staminaRecoverDelayRemaining);
        
        // Restore basic stats
        playerData.female = data.female;
        playerData.leftHanded = data.leftHanded;
        playerData.portrait = data.portrait;
        playerData.playerClass = (EPlayerClass)data.playerClass;
        playerData.playerName = data.playerName;
        playerData.vitality = data.vitality;
        playerData.hp = data.hp;
        playerData.maxMana = data.maxMana;
        playerData.mana = data.mana;
        playerData.xp = data.xp;
        playerData.charLevel = data.charLevel;
        // max with current display XP so older saves missing this field do not backdate awards
        playerData.skillPointsXpTier = Mathf.Max(data.skillPointsXpTier, data.xp / 20 / 300);
        playerData.skillPoints = data.skillPoints;
        playerData.easy = data.easy;
        playerData.dead = data.dead;
        
        // Restore attributes
        playerData.strength = data.strength;
        playerData.intellect = data.intellect;
        playerData.dexterity = data.dexterity;
        
        // Restore status
        playerData.poison = data.poison;
        playerData.hunger = data.hunger;
        playerData.fatigue = data.fatigue;
        playerData.drunkenness = data.drunkenness;
        
        // Restore skills
        System.Array.Copy(data.skill, playerData.skill, Mathf.Min(data.skill.Length, playerData.skill.Length));
        
        // Restore quest flags and global vars
        System.Array.Copy(data.questFlags, playerData.questFlags, Mathf.Min(data.questFlags.Length, playerData.questFlags.Length));
        System.Array.Copy(data.globalVars, playerData.globalVars, Mathf.Min(data.globalVars.Length, playerData.globalVars.Length));
        
        // Restore dreams
        if (data.dreamsRemaining != null)
        {
            playerData.dreamsRemaining = new List<int>(data.dreamsRemaining);
        }
        else
        {
            playerData.ShuffleDreams();
        }
        playerData.timeOfLastDream = data.timeOfLastDream;
        playerData.cupDreamIndex = data.cupDreamIndex;
        
        // Restore special flags
        playerData.cupFound = data.cupFound;
        playerData.saidFanlo = data.saidFanlo;
        playerData.saplingPlanted = data.saplingPlanted;
        playerData.saplingPlantedPosition = data.saplingPlantedPosition;
        playerData.saplingPlantedLevel = data.saplingPlantedLevel;
        playerData.moonstoneDropped = data.moonstoneDropped;
        playerData.moonstoneDroppedPosition = data.moonstoneDroppedPosition;
        playerData.moonstoneDroppedLevel = data.moonstoneDroppedLevel;
        playerData.talismansDestroyed = data.talismansDestroyed;
        playerData.talismansCollected = data.talismansCollected;
        playerData.garamonAtRest = data.garamonAtRest;
        playerData.enteredGreenMoongate = data.enteredGreenMoongate;
        
        // Restore game time
        playerData.gameTime = data.gameTime;
        
        // Restore encountered critters for summon spell
        // Clear the array first
        for (int i = 0; i < player.encounteredCritterData.Length; i++)
        {
            player.encounteredCritterData[i] = null;
        }
        
        // Restore from save data
        if (data.encounteredCritters != null)
        {
            foreach (EncounteredCritterEntry entry in data.encounteredCritters)
            {
                int typeIndex = entry.critterType - 64; // Convert EObjectType to array index (0-63)
                if (typeIndex >= 0 && typeIndex < player.encounteredCritterData.Length)
                {
                    player.encounteredCritterData[typeIndex] = new CritterEncounterData
                    {
                        level = entry.level,
                        objectIndex = entry.objectIndex,
                        originalHp = entry.originalHp
                    };
                }
            }
        }
        
        // Restore magic state
        if (Magic.sMagic != null && data.magicData != null)
        {
            Magic.sMagic.LoadFromData(data.magicData);
        }

        // Restore achievement-related progress (per save slot)
        playerData.numRepairs = data.numRepairs;
        playerData.numFishCaught = data.numFishCaught;
        playerData.booksRead = data.booksRead;
        playerData.mapTilesRevealed = data.mapTilesRevealed;
        playerData.gateTravelDistance = data.gateTravelDistance;
        playerData.waterWalkSteps = data.waterWalkSteps;
        playerData.lavaWalkSteps = data.lavaWalkSteps;
        playerData.playTime = data.playTime;
        playerData.booksBurned = data.booksBurned;
        playerData.pacifistStopped = data.pacifistStopped;
        playerData.autoSavedLevels = data.autoSavedLevels;

        if (data.openedChest != null)
        {
            if (playerData.openedChest == null || playerData.openedChest.Length != data.openedChest.Length)
            {
                playerData.openedChest = new bool[data.openedChest.Length];
            }
            System.Array.Copy(data.openedChest, playerData.openedChest, data.openedChest.Length);
        }
        else if (playerData.openedChest == null)
        {
            // Back-compat: old saves didn't store this
            playerData.openedChest = new bool[5];
        }

        if (data.tutorialData != null && data.tutorialData.version >= TutorialSaveData.CurrentVersion)
        {
            playerData.tutorialData = CopyTutorialSaveData(data.tutorialData);
        }
        else
        {
            playerData.tutorialData = new TutorialSaveData();
            TutorialManager.MarkAllStepsCompleteForLegacySave(playerData.tutorialData);
        }

        // Cross-save persistence (PlayerPrefs): ensure arrays exist and merge in persisted values.
        playerData.EnsureCrossSaveAchievementArrays();
        playerData.MergeCrossSaveAchievementArraysFromPrefs();
    }
    
    private void LoadMapData(MapSaveData data)
    {
        MapScreen mapScreen = FindAnyObjectByType<MapScreen>();
        if (mapScreen != null && data != null)
        {
            mapScreen.LoadFromData(data);
        }
    }

    private static int ComputeMapTilesRevealedFromSave(MapSaveData mapData)
    {
        if (mapData?.pages == null) return 0;

        int total = 0;
        int maxPages = Mathf.Min(8, mapData.pages.Count);

        for (int level = 1; level <= maxPages; level++)
        {
            MapPageSaveData page = mapData.pages[level - 1];
            if (page == null || string.IsNullOrEmpty(page.mappedRLE)) continue;

            bool[] mapped = MapSaveData.DecodeMappedFromRLE(page.mappedRLE, 64 * 64);

            // If we have tiles for this level, apply the same rule as MapPage.Update()
            // (increment only for non-closed tiles, t.type != 0). Otherwise fall back to counting mapped tiles.
            Level lvl = null;
            if (LevelLoader.sLevelLoader != null
                && LevelLoader.sLevelLoader.levels != null
                && level >= 0
                && level < LevelLoader.sLevelLoader.levels.Length)
            {
                lvl = LevelLoader.sLevelLoader.levels[level];
            }

            if (lvl != null && lvl.tiles != null)
            {
                for (int i = 0; i < mapped.Length; i++)
                {
                    if (!mapped[i]) continue;
                    int x = i % 64;
                    int y = i / 64;
                    Tile t = lvl.tiles[x, y];
                    if (t != null && t.type != 0)
                    {
                        total++;
                    }
                }
            }
            else
            {
                for (int i = 0; i < mapped.Length; i++)
                {
                    if (mapped[i]) total++;
                }
            }
        }

        return total;
    }

    private static bool IsEmptyInventoryObjectSaveData(ObjectSaveData itemData)
    {
        return itemData == null
               || (itemData.objectIndex == 0 && itemData.objectType == 0 && string.IsNullOrEmpty(itemData.objectTypeName));
    }
    
    private void SaveInventoryData(InventorySaveData data)
    {
        if (Inventory.sInv == null)
        {
            Debug.LogWarning("Inventory.sInv is null, cannot save inventory data");
            return;
        }
        
        // Save main inventory - only top-level items
        // (Nested items in containers will be saved recursively by their container's SaveToData)
        foreach (UUObject item in Inventory.sInv.inventory)
        {
            if (item == null) continue;
            
            // Check if this item is in the equipped slots
            bool isEquipped = false;
            for (int i = 0; i < Inventory.sInv.invSlotContents.Length; i++)
            {
                if (Inventory.sInv.invSlotContents[i] == item)
                {
                    isEquipped = true;
                    break;
                }
            }
            
            // Only save if not equipped (equipped items are saved separately)
            if (!isEquipped)
            {
                ObjectSaveData objData = item.SaveToData();
                if (objData != null)
                {
                    data.mainInventory.Add(objData);
                }
            }
        }
        
        // Save equipped items (all slots including nulls to preserve array structure)
        for (int i = 0; i < Inventory.sInv.invSlotContents.Length; i++)
        {
            UUObject item = Inventory.sInv.invSlotContents[i];
            if (item != null)
            {
                ObjectSaveData objData = item.SaveToData();
                // Always add something to maintain index correspondence
                // If SaveToData() returns null, add null (same as null item)
                data.equippedItems.Add(objData);
            }
            else
            {
                data.equippedItems.Add(null);
            }
        }

        data.hasMouseCursorCarriedPortable = false;
        data.mouseCursorCarriedPortableData = null;
        data.hasUsingItem = false;
        data.usingItemData = null;

        UUObject carried = Inventory.sInv.mouseCursorCarriedPortable;
        if (carried != null)
        {
            ObjectSaveData cursorSave = carried.SaveToData();
            if (!IsEmptyInventoryObjectSaveData(cursorSave))
            {
                data.hasMouseCursorCarriedPortable = true;
                data.mouseCursorCarriedPortableData = cursorSave;
            }
        }

        UUObject usingObj = Inventory.sInv.usingItem;
        if (usingObj != null && usingObj != carried)
        {
            ObjectSaveData usingSave = usingObj.SaveToData();
            if (!IsEmptyInventoryObjectSaveData(usingSave))
            {
                data.hasUsingItem = true;
                data.usingItemData = usingSave;
            }
        }
    }
    
    private void LoadInventoryData(InventorySaveData data)
    {
        if (Inventory.sInv == null)
        {
            return;
        }

        UUObject oldCarried = Inventory.sInv.mouseCursorCarriedPortable;
        UUObject oldUsing = Inventory.sInv.usingItem;
        Inventory.sInv.mouseCursorCarriedPortable = null;
        Inventory.sInv.usingItem = null;
        if (oldCarried != null)
        {
            Destroy(oldCarried.gameObject);
        }

        if (oldUsing != null && oldUsing != oldCarried)
        {
            Destroy(oldUsing.gameObject);
        }
        
        // Clear current inventory by removing all items
        // Note: This is a simplified approach - may need refinement
        List<UUObject> allItems = Inventory.GetAllItems();
        foreach (UUObject item in allItems)
        {
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }
        
        // Clear the inventory list itself to remove destroyed references
        Inventory.sInv.inventory.Clear();
        
        // Clear the stack to remove destroyed Container references
        Inventory.sInv.ClearStack();
        
        // Clear equipped slots
        for (int i = 0; i < Inventory.sInv.invSlotContents.Length; i++)
        {
            Inventory.sInv.invSlotContents[i] = null;
        }

        // Enchanted-item effects live only in Magic.permanentSpells; destroyed items never unequip,
        // so clear before restoring so Equip() does not stack duplicate icons on each load.
        if (Magic.sMagic != null)
        {
            Magic.sMagic.ClearPermanentSpells();
        }
        
        // Restore main inventory
        foreach (ObjectSaveData objData in data.mainInventory)
        {
            LevelObject levelObj = CreateObjectFromSaveData(objData);
            if (levelObj is UUObject item)
            {
                // Initialize the item and its contents (sets up renderer, loads names, etc.)
                item.PostLoadInitialize(restoredFromSave: true);
                // Add to inventory using the public Add method
                Inventory.Add(item);
            }
        }
        
        // Restore equipped items
        for (int i = 0; i < data.equippedItems.Count && i < Inventory.sInv.invSlotContents.Length; i++)
        {
            ObjectSaveData itemData = data.equippedItems[i];
            
            // Check for null or empty objects (JsonUtility may serialize nulls as empty objects with default values)
            if (itemData == null || (itemData.objectIndex == 0 && itemData.objectType == 0 && string.IsNullOrEmpty(itemData.objectTypeName)))
            {
                Inventory.sInv.invSlotContents[i] = null;
                continue;
            }
            
            LevelObject levelObj = CreateObjectFromSaveData(itemData);
            if (levelObj is UUObject item)
            {
                // Initialize the item and its contents (sets up renderer, loads names, etc.)
                item.PostLoadInitialize(restoredFromSave: true);
                Inventory.sInv.invSlotContents[i] = item;
                
                // Equip the item to properly parent it to the camera and disable colliders
                item.Equip();
            }
        }

        if (data.hasMouseCursorCarriedPortable && !IsEmptyInventoryObjectSaveData(data.mouseCursorCarriedPortableData))
        {
            LevelObject levelObj = CreateObjectFromSaveData(data.mouseCursorCarriedPortableData);
            if (levelObj is UUObject item)
            {
                item.PostLoadInitialize(restoredFromSave: true);
                Inventory.sInv.mouseCursorCarriedPortable = item;
            }
        }

        if (data.hasUsingItem && !IsEmptyInventoryObjectSaveData(data.usingItemData))
        {
            LevelObject levelObj = CreateObjectFromSaveData(data.usingItemData);
            if (levelObj is UUObject item)
            {
                item.PostLoadInitialize(restoredFromSave: true);
                Inventory.sInv.usingItem = item;
            }
        }
    }
    
    private void SaveWorldObjects(SaveGameData saveData)
    {
        Level[] levels = LevelLoader.sLevelLoader.levels;
        saveData.worldObjectsByLevel = new LevelWorldSaveData[levels.Length];
        for (int i = 0; i < saveData.worldObjectsByLevel.Length; i++)
            saveData.worldObjectsByLevel[i] = new LevelWorldSaveData();
        
        for (int levelIndex = 0; levelIndex < levels.Length; levelIndex++)
        {
            Level level = levels[levelIndex];
            if (level == null)
                continue;
            
            LinkedListNode<LevelObject> node = level.worldObj.First;
            while (node != null)
            {
                LevelObject levelObj = node.Value;
                node = node.Next;
                
                if (levelObj == null)
                    continue;
                if (levelObj.temporary)
                    continue;
                
                ObjectSaveData objData = levelObj.SaveToData();
                if (objData != null)
                {
                    // Skip objects with objectIndex == 0 and objectType == 0 (likely uninitialized)
                    // UNLESS they have a special objectTypeName (like CascadingSpellEffect, MovingPlatform, RuneOfWarding)
                    if (objData.objectIndex == 0 && objData.objectType == 0
                        && string.IsNullOrEmpty(objData.objectTypeName))
                    {
                        Debug.LogWarning($"Skipping save of object '{levelObj.name}' with objectIndex=0 and objectType=0 (likely uninitialized)");
                        continue;
                    }
                    
                    objData.inWorldObj = true;
                    saveData.worldObjectsByLevel[levelIndex].objects.Add(objData);
                }
            }
            
            // Save objects in this level's objects[] that are not in worldObj (inactive / not currently in world)
            UUObject[] levelObjects = level.objects;
            if (levelObjects != null)
            {
                for (int i = 0; i < levelObjects.Length; i++)
                {
                    UUObject uu = levelObjects[i];
                    if (uu == null)
                        continue;
                    if (uu.temporary)
                        continue;
                    if (level.worldObj.Contains(uu))
                        continue;
                    ObjectSaveData objData = uu.SaveToData();
                    if (objData != null)
                    {
                        if (objData.objectIndex == 0 && objData.objectType == 0
                            && string.IsNullOrEmpty(objData.objectTypeName))
                        {
                            Debug.LogWarning($"Skipping save of inactive object '{uu.name}' with objectIndex=0 and objectType=0");
                            continue;
                        }
                        objData.inWorldObj = false;
                        saveData.worldObjectsByLevel[levelIndex].inactiveObjects.Add(objData);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// objectIndex in lev.ark is only meaningful on originalLevel. Objects in another level's worldObj
    /// (e.g. dropped portables) must not be written into that level's objects[] or they steal script slots.
    /// </summary>
    private static bool ShouldPlaceObjectInLevelObjectsArray(UUObject uu, int levelBeingLoaded)
    {
        if (uu == null || uu.objectIndex <= 0)
            return false;
        Level[] levels = LevelLoader.sLevelLoader.levels;
        if (levelBeingLoaded < 0 || levelBeingLoaded >= levels.Length)
            return false;
        Level level = levels[levelBeingLoaded];
        if (level == null || uu.objectIndex >= level.objects.Length)
            return false;
        return uu.originalLevel == levelBeingLoaded;
    }
    
    private void LoadWorldObjects(SaveGameData saveData)
    {
        Level[] levels = LevelLoader.sLevelLoader.levels;
        if (saveData.worldObjectsByLevel != null && saveData.worldObjectsByLevel.Length > 0)
        {
            int maxLevel = Mathf.Min(saveData.worldObjectsByLevel.Length, levels.Length);
            for (int levelIndex = 0; levelIndex < maxLevel; levelIndex++)
            {
                Level level = levels[levelIndex];
                if (level == null)
                    continue;
                LevelWorldSaveData levelData = saveData.worldObjectsByLevel[levelIndex];
                if (levelData == null)
                    continue;
                
                // Restore inactive objects first (in objects[] but not in worldObj)
                if (levelData.inactiveObjects != null)
                {
                    foreach (ObjectSaveData objData in levelData.inactiveObjects)
                    {
                        if (objData == null) continue;
                        try
                        {
                            LevelObject levelObj = CreateObjectFromSaveData(objData);
                            if (levelObj != null && levelObj.gameObject != null
                                && levelObj is UUObject uuObj && ShouldPlaceObjectInLevelObjectsArray(uuObj, levelIndex))
                            {
                                uuObj.levelIndex = levelIndex;
                                LevelLoader.AssignLevelObjectSlot(levelIndex, uuObj.objectIndex, uuObj);
                            }
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"Exception loading inactive object {objData.objectTypeName}: {e.Message}");
                        }
                    }
                }
                
                // Restore world objects (in worldObj)
                if (levelData.objects != null)
                {
                    foreach (ObjectSaveData objData in levelData.objects)
                    {
                        if (objData == null) continue;
                        try
                        {
                            LevelObject levelObj = CreateObjectFromSaveData(objData);
                            if (levelObj != null && levelObj.gameObject != null)
                            {
                                if (levelObj is UUObject uuObj && ShouldPlaceObjectInLevelObjectsArray(uuObj, levelIndex))
                                {
                                    uuObj.levelIndex = levelIndex;
                                    LevelLoader.AssignLevelObjectSlot(levelIndex, uuObj.objectIndex, uuObj);
                                }
                                level.worldObj.AddLast(levelObj);
                            }
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"Exception loading world object {objData.objectTypeName}: {e.Message}");
                        }
                    }
                }
            }
        }
        else
        {
            List<ObjectSaveData> objects = saveData.worldObjects;
            if (objects == null) return;
            for (int i = 0; i < objects.Count; i++)
            {
                ObjectSaveData objData = objects[i];
                if (objData == null) continue;
                try
                {
                    LevelObject levelObj = CreateObjectFromSaveData(objData);
                    if (levelObj != null && levelObj.gameObject != null)
                    {
                        int levelIdx = objData.level >= 0 && objData.level < levels.Length ? objData.level : levelObj.levelIndex;
                        levelObj.levelIndex = levelIdx;
                        if (levelIdx >= 0 && levelIdx < levels.Length && levels[levelIdx] != null
                            && levelObj is UUObject uuObj && ShouldPlaceObjectInLevelObjectsArray(uuObj, levelIdx))
                        {
                            LevelLoader.AssignLevelObjectSlot(levelIdx, uuObj.objectIndex, uuObj);
                        }
                        if (objData.inWorldObj && levelIdx >= 0 && levelIdx < levels.Length && levels[levelIdx] != null)
                        {
                            levels[levelIdx].worldObj.AddLast(levelObj);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Exception loading world object {objData.objectTypeName}: {e.Message}");
                }
            }
        }
    }
    
    // Public method to create and restore an object from save data
    // Used by SaveGameManager and also by UUObject.LoadFromData for container contents
    public LevelObject CreateObjectFromSaveData(ObjectSaveData data)
    {
        if (data == null)
            return null;
        
        // Handle special generated objects like MovingPlatform
        if (data.objectTypeName == "MovingPlatform")
        {
            // Deserialize data
            MovingPlatformSaveData mpData = JsonUtility.FromJson<MovingPlatformSaveData>(data.jsonData);
            
            // Get the tile
            Tile tile = LevelLoader.GetTile(mpData.initialTileX, mpData.initialTileY);
            if (tile == null)
            {
                Debug.LogError($"Failed to find tile at ({mpData.initialTileX}, {mpData.initialTileY}) for MovingPlatform");
                return null;
            }
            
            // Create platform
            MovingPlatform plat = Instantiate(LevelLoader.sLevelLoader.movableTile);
            plat.levelIndex = data.level;
            plat.initialTile = tile;
            plat.gameObject.layer = LayerMask.NameToLayer("Environment");
            
            // Create mesh with saved material indices
            plat.CreateMesh(mpData.floorTextureIndex, mpData.wallTextureIndices);
            
            // Restore state from save data
            plat.LoadFromSaveData(mpData);
            
            // Link tile
            tile.movingPlatform = plat;
            
            return plat;
        }
        
        // Handle CascadingSpellEffect
        if (data.objectTypeName == "CascadingSpellEffect")
        {
            // Create the effect object
            GameObject effectObj = new GameObject("CascadingSpellEffect");
            CascadingSpellEffect effect = effectObj.AddComponent<CascadingSpellEffect>();
            effect.levelIndex = data.level;
            
            // Restore state from save data
            effect.LoadFromData(data);
            
            // Make sure the GameObject is active so Update() runs
            effectObj.SetActive(true);
            
            return effect;
        }

        // Handle RuneOfWarding (spell-placed; prefab type is 0 / HandAxe, so must not use CreateObjectOfType)
        if (data.objectTypeName == "RuneOfWarding")
        {
            if (Magic.sMagic == null || Magic.sMagic.runeOfWarding == null)
            {
                return null;
            }

            UUObject rune = Instantiate(Magic.sMagic.runeOfWarding);
            rune.levelIndex = data.level;
            rune.LoadFromData(data);
            return rune;
        }

        // Ephemeral debris; never recreate (would become HandAxe via objectType 0)
        if (data.objectTypeName == "DestructiblePiece")
        {
            return null;
        }
            
        EObjectType objType = (EObjectType)data.objectType;
        
        // Always use CreateObjectOfType - all state will be restored from JSON anyway
        // For critters, we need to provide dummy critterData to pass the null check
        // (real values will be restored from JSON)
        LevelObject obj;
        UUObject.EClass objClass = UUObject.GetClass(objType);
        if (objClass is >= UUObject.EClass.CrittersA and <= UUObject.EClass.CrittersD)
        {
            // Critters require critterData, but we're loading from save so provide dummy data
            // All real values will be restored from CritterSaveData in LoadFromData()
            ushort[] objData = { (ushort)((int)objType | (1 << 15)), 0, 40, 0 };
            byte[] dummyCritterData = new byte[19]; // 19 bytes of zeros - real values restored from JSON
            obj = LevelLoader.sLevelLoader.CreateObjectOfType(objData, dummyCritterData);
        }
        else
        {
            obj = LevelLoader.CreateObjectOfType(objType);
        }
        
        if (obj == null)
        {
            Debug.LogError($"Failed to create object of type {objType} (objectIndex={data.objectIndex})");
            return null;
        }
        
        // Check if the GameObject is still valid
        if (obj.gameObject == null)
        {
            Debug.LogError($"Created object of type {objType} but GameObject is null");
            return null;
        }
        
        // Set objectIndex and originalLevel from save data (for reference, before restoring from JSON)
        if (data.objectIndex > 0)
        {
            obj.objectIndex = data.objectIndex;
            obj.originalLevel = data.originalLevel; // Preserve originalLevel for reference/debugging
        }
        
        // Call the virtual LoadFromData method to restore object state from JSON
        obj.LoadFromData(data);
        
        return obj;
    }
    
    
    private void DeleteNonPersistentObjects()
    {
        // Remove all non-geometry objects from worldObj lists before destroying them
        // This prevents null references from accumulating in worldObj
        for (int level = 0; level < LevelLoader.sLevelLoader.levels.Length; level++)
        {
            if (LevelLoader.sLevelLoader.levels[level] != null)
            {
                var worldObj = LevelLoader.sLevelLoader.levels[level].worldObj;
                var node = worldObj.First;
                while (node != null)
                {
                    var next = node.Next;
                    var obj = node.Value;
                    // Remove non-geometry objects (geometry will be preserved and not destroyed)
                    if (obj != null && !(obj is LevelGeometry))
                    {
                        worldObj.Remove(node);
                    }
                    node = next;
                }
            }
        }
        
        LevelObject[] levelObjects = FindObjectsByType<LevelObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        
        foreach (LevelObject obj in levelObjects)
        {
            if (obj == null || obj.gameObject == null) continue;
            
            // Preserve only static level geometry; everything else is dynamic and should be restored from save
            if (obj is LevelGeometry)
            {
                continue;
            }

            // Don't delete the fist - it's a player system object, not a world object
            if (obj is Fist)
            {
                continue;
            }

            DestroyImmediate(obj.gameObject);
        }
        
        // Clear all objects[] arrays
        for (int level = 0; level < LevelLoader.sLevelLoader.levels.Length; level++)
        {
            if (LevelLoader.sLevelLoader.levels[level] != null)
            {
                for (int i = 0; i < LevelLoader.sLevelLoader.levels[level].objects.Length; i++)
                {
                    LevelLoader.AssignLevelObjectSlot(level, i, null);
                }
            }
        }
    }
    
    private void PostLoadInitializeAllLoadedObjects()
    {
        // Find all LevelObjects in the scene (including inactive ones)
        LevelObject[] levelObjects = FindObjectsByType<LevelObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        
        foreach (LevelObject obj in levelObjects)
        {
            if (obj == null || obj is LevelGeometry)
                continue;
            
            // Check if object is in worldObj
            bool inWorldObj = false;
            if (obj.levelIndex >= 0 && obj.levelIndex < LevelLoader.sLevelLoader.levels.Length
                && LevelLoader.sLevelLoader.levels[obj.levelIndex] != null)
            {
                inWorldObj = LevelLoader.sLevelLoader.levels[obj.levelIndex].worldObj.Contains(obj);
            }
            
            // Only initialize if object is in worldObj OR is enabled
            if (inWorldObj || obj.gameObject.activeSelf)
            {
                obj.PostLoadInitialize(restoredFromSave: true);
            }
        }
        
        // Prevent the deferred PostLoadInitialize in LevelLoader.Update from running
        LevelLoader.sLevelLoader.sentPostLoadInitialize = true;
    }

    /// <summary>
    /// Inventory/container wands may PostLoadInitialize before their linked spell is in objects[];
    /// finish legacy charge recovery once the world is fully restored.
    /// </summary>
    private void FinishWandChargeRecoveryAfterLoad()
    {
        void RecoverInObject(UUObject obj)
        {
            if (obj == null)
            {
                return;
            }

            if (obj is Wand wand)
            {
                wand.TryFinishLegacyChargeRecovery();
            }

            if (obj.contents == null)
            {
                return;
            }

            foreach (UUObject content in obj.contents)
            {
                RecoverInObject(content);
            }
        }

        if (Inventory.sInv != null)
        {
            foreach (UUObject item in Inventory.sInv.inventory)
            {
                RecoverInObject(item);
            }

            if (Inventory.sInv.invSlotContents != null)
            {
                foreach (UUObject item in Inventory.sInv.invSlotContents)
                {
                    RecoverInObject(item);
                }
            }

            RecoverInObject(Inventory.sInv.mouseCursorCarriedPortable);
            RecoverInObject(Inventory.sInv.usingItem);
        }

        // World / inactive objects not reached via inventory lists
        Wand[] wands = FindObjectsByType<Wand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Wand wand in wands)
        {
            if (wand != null)
            {
                wand.TryFinishLegacyChargeRecovery();
            }
        }
    }

    /// <summary>
    /// Critter trade loot is inactive and not in worldObj, so PostLoadInitializeAllLoadedObjects skips it.
    /// Pre-fix saves may have loot that never ran PLI (e.g. chain-from-ark inventory). Repair any that
    /// still have no display name.
    /// </summary>
    private void PostLoadInitializeCritterLootIfUnnamed()
    {
        Critter[] critters = FindObjectsByType<Critter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Critter critter in critters)
        {
            if (critter.loot == null)
            {
                continue;
            }

            foreach (UUObject uu in critter.loot)
            {
                if (uu != null && string.IsNullOrEmpty(uu.singularName))
                {
                    uu.PostLoadInitialize(restoredFromSave: true);
                }
            }
        }
    }
    
    /// <summary>
    /// Links objects to tiles after loading from save. WorldInitialize isn't called during save/load,
    /// so we need to manually link bridges, stairs, and moving platforms to their tiles.
    /// This ensures these objects are properly associated with tiles regardless of map state.
    /// </summary>
    private void LinkObjectsToTiles()
    {
        if (LevelLoader.sLevelLoader == null)
            return;
        
        // Iterate through all levels
        for (int levelIdx = 0; levelIdx < LevelLoader.sLevelLoader.levels.Length; levelIdx++)
        {
            Level level = LevelLoader.sLevelLoader.levels[levelIdx];
            if (level == null || level.worldObj == null)
                continue;
            
            foreach (LevelObject obj in level.worldObj)
            {
                if (obj == null) continue;
                
                if (obj is Bridge bridge && bridge.initialTile != null)
                {
                    bridge.LinkToTile(bridge.initialTile);
                }
                else if (obj is Trigger trigger)
                {
                    trigger.MarkAsStairIfNeeded();
                }
                else if (obj is MovingPlatform plat && plat.initialTile != null)
                {
                    plat.initialTile.movingPlatform = plat;
                }
            }
        }
    }

    private void EnsureLevelsInitialized(SaveGameData saveData)
    {
        HashSet<int> levelsWithObjects = new HashSet<int>();
        
        if (saveData.worldObjectsByLevel != null && saveData.worldObjectsByLevel.Length > 0)
        {
            int maxLevel = Mathf.Min(saveData.worldObjectsByLevel.Length, LevelLoader.sLevelLoader.levels.Length);
            for (int i = 0; i < maxLevel; i++)
            {
                LevelWorldSaveData ld = saveData.worldObjectsByLevel[i];
                bool hasWorld = ld?.objects != null && ld.objects.Count > 0;
                bool hasInactive = ld?.inactiveObjects != null && ld.inactiveObjects.Count > 0;
                if (hasWorld || hasInactive)
                    levelsWithObjects.Add(i);
            }
        }
        else if (saveData.worldObjects != null)
        {
            foreach (ObjectSaveData objData in saveData.worldObjects)
            {
                if (objData.level >= 1 && objData.level < LevelLoader.sLevelLoader.levels.Length)
                {
                    levelsWithObjects.Add(objData.level);
                }
            }
        }
        
        if (saveData.currentLevel >= 1 && saveData.currentLevel < LevelLoader.sLevelLoader.levels.Length)
        {
            levelsWithObjects.Add(saveData.currentLevel);
        }
        
        foreach (int lvl in levelsWithObjects)
        {
            if (lvl != saveData.currentLevel)
            {
                EnsureLevelInitialized(lvl, saveData.currentLevel);
            }
        }
        EnsureLevelInitialized(saveData.currentLevel, saveData.currentLevel);
    }

    private void EnsureLevelInitialized(int level, int currentLevel)
    {
        Level levelObj = LevelLoader.sLevelLoader.levels[level]; 
        if (levelObj == null || levelObj.geo == null)
        {
            // Only load geometry; objects will be restored from save data
            LevelLoader.sLevelLoader.LoadLevelGeometry(level);
            // Immediately deactivate if not current level (prevents physics interactions)
            levelObj = LevelLoader.sLevelLoader.levels[level];
        }
        
        if (levelObj != null && levelObj.geo != null)
        {
            levelObj.geo.gameObject.SetActive(level == currentLevel);
        }
    }
}
