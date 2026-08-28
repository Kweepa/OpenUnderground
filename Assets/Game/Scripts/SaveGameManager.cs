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

    // Multi-slot API
    public void SaveGameToSlot(string slotName, string displayName = null)
    {
        if (PlayerObject.Player == null || PlayerData.sData == null || LevelLoader.sLevelLoader == null)
        {
            Debug.LogError("Cannot save: Required components not initialized");
            return;
        }
        if (string.IsNullOrWhiteSpace(slotName))
        {
            Debug.LogError("Cannot save: slotName is empty");
            return;
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
        }
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

    public string GetSlotScreenshotPath(string slotName)
    {
        string safe = SanitizeFileName(slotName);
        return Path.Combine(savesDirectoryPath, safe + ".png");
    }

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
        try
        {
            Texture2D tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            byte[] bytes = tex.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            Destroy(tex);
            Debug.Log($"Screenshot saved to {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save screenshot for slot '{slotName}': {e.Message}");
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
