using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

[System.Serializable]
public class DebrisSaveData : UUObjectSaveData
{
    public int searchResult;
    public int searchResultSearchLevel;
    public int searchDifficulty;
    public bool hasLoot;
    public List<int> lootItems; // serialized as object type IDs
    public bool looted;
}

public class Debris : UUObject
{
    // Search skill tracking (similar to lore system)
    public Skills.ESkillTestResult searchResult = Skills.ESkillTestResult.Failure;
    public int searchResultSearchLevel = -1; // the search skill level at which you obtained the searchResult
    
    // Loot system
    public int searchDifficulty;
    public bool hasLoot;
    public List<EObjectType> lootItems;
    public bool looted;
    
    // Constants
    private const float LOOT_CHANCE = 0.4f; // 40% chance to have loot
    private const int MIN_LOOT_COUNT = 1;
    private const int MAX_LOOT_COUNT = 3;
    private const int MIN_SEARCH_DIFFICULTY = 5;
    private const int MAX_SEARCH_DIFFICULTY = 15;
    
    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);
        
        // Initialize loot if this is a new debris pile (not restored from save)
        if (!restoredFromSave)
        {
            InitializeLoot();
        }
    }
    
    public override string GetUseText()
    {
        return null;
    }
    
    /// <summary>
    /// Initialize loot for a new debris pile
    /// </summary>
    private void InitializeLoot()
    {
        // Only initialize if not already initialized
        if (hasLoot || (lootItems != null && lootItems.Count > 0))
        {
            return;
        }
        
        // Random chance to have loot
        if (Random.value < LOOT_CHANCE)
        {
            int level = LevelLoader.sLevelLoader.loadedLevel;
            int lootCount = Random.Range(MIN_LOOT_COUNT, MAX_LOOT_COUNT + 1);
            
            lootItems = DebrisLootTables.GetRandomLoot(level, lootCount);
            searchDifficulty = Random.Range(MIN_SEARCH_DIFFICULTY, MAX_SEARCH_DIFFICULTY + 1);
            hasLoot = true;
        }
        else
        {
            hasLoot = false;
            lootItems = null;
        }
    }
    
    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        bool foundItems = false;

        // Only handle Use action (when player interacts with debris)
        if (sender == null && action == EAction.Use)
        {
            // Check if already looted
            if (looted)
            {
            }
            // Check if there's no loot
            else if (!hasLoot || lootItems == null || lootItems.Count == 0)
            {
                int searchSkill = Skills.GetSkill(ESkill.Search);
                
                // Perform search check if skill has changed since last check
                if (searchResultSearchLevel < searchSkill)
                {
                    searchResultSearchLevel = searchSkill;
                    searchResult = Skills.GetResult(searchSkill, searchDifficulty);
                }
            }
            else
            {
                // Perform search check if skill has changed since last check
                int currentSearchSkill = Skills.GetSkill(ESkill.Search);
                if (searchResultSearchLevel < currentSearchSkill)
                {
                    searchResultSearchLevel = currentSearchSkill;
                    searchResult = Skills.GetResult(currentSearchSkill, searchDifficulty);
                }

                // Check if search was successful
                if (searchResult >= Skills.ESkillTestResult.Success)
                {
                    // Place loot items on ground
                    PlaceLootItems();
                    looted = true;
                    foundItems = true;
                }
            }

            if (foundItems)
            {
                Messages.Add("You search the debris and find some items!");
            }
            else
            {
                Messages.Add("The search is fruitless.");
            }
        }
        else
        {
            // Call base implementation for other actions
            base.TryInteract(originator, sender, action);
        }
    }
    
    /// <summary>
    /// Place loot items on the ground near the debris
    /// </summary>
    private void PlaceLootItems()
    {
        if (lootItems == null || lootItems.Count == 0)
        {
            return;
        }
        
        Vector3 debrisPos = transform.position;
        Tile debrisTile = LevelLoader.GetTile(GetTileX(), GetTileY());
        
        foreach (EObjectType lootType in lootItems)
        {
            // Create the loot object
            UUObject lootObj = LevelLoader.CreateObjectOfType(lootType);
            if (lootObj == null)
            {
                continue;
            }
            
            // Set potion enchantment if it's a potion
            if (lootType == EObjectType.RedPotion || lootType == EObjectType.GreenPotion)
            {
                lootObj.enchantmentName = DebrisLootTables.GetRandomPotionEnchantment();
                lootObj.isEnchanted = true;
            }
            
            // Position near debris with random offset
            Vector2 randomOffset = Random.insideUnitCircle * 0.5f;
            Vector3 lootPos = debrisPos + new Vector3(randomOffset.x, 0.0f, randomOffset.y);
            
            // Ensure position is valid (on ground)
            int tx = Tile.GetTileX(lootPos.x);
            int ty = Tile.GetTileY(lootPos.z);
            Tile t = LevelLoader.GetTile(tx, ty);
            if (t != null)
            {
                lootPos.y = t.GetFloorY(lootPos.x, lootPos.z) + 0.25f;
            }
            
            // Initialize and add to world
            lootObj.transform.position = lootPos;
            lootObj.PostLoadInitialize();
            lootObj.gameObject.SetActive(true);
            LevelLoader.AddToWorld(lootObj);
        }
        
        // Clear loot items list after placing
        lootItems.Clear();
    }
    
    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);
        
        if (data is DebrisSaveData debrisData)
        {
            debrisData.searchResult = (int)searchResult;
            debrisData.searchResultSearchLevel = searchResultSearchLevel;
            debrisData.searchDifficulty = searchDifficulty;
            debrisData.hasLoot = hasLoot;
            debrisData.looted = looted;
            
            if (lootItems != null && lootItems.Count > 0)
            {
                debrisData.lootItems = new List<int>();
                foreach (EObjectType itemType in lootItems)
                {
                    debrisData.lootItems.Add((int)itemType);
                }
            }
        }
    }
    
    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        
        if (data is DebrisSaveData debrisData)
        {
            searchResult = (Skills.ESkillTestResult)debrisData.searchResult;
            searchResultSearchLevel = debrisData.searchResultSearchLevel;
            searchDifficulty = debrisData.searchDifficulty;
            hasLoot = debrisData.hasLoot;
            looted = debrisData.looted;
            
            if (debrisData.lootItems != null && debrisData.lootItems.Count > 0)
            {
                lootItems = new List<EObjectType>();
                foreach (int itemTypeId in debrisData.lootItems)
                {
                    lootItems.Add((EObjectType)itemTypeId);
                }
            }
            else
            {
                lootItems = null;
            }
        }
    }
    
    public override ObjectSaveData SaveToData()
    {
        DebrisSaveData data = new DebrisSaveData();
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
        
        DebrisSaveData data = JsonUtility.FromJson<DebrisSaveData>(objData.jsonData);
        if (data == null)
        {
            Debug.LogError($"Failed to deserialize JSON for debris objectIndex={objData.objectIndex}, jsonData={objData.jsonData}");
            return;
        }
        RestoreFromSaveData(data);
    }
}
