using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public static class DebrisLootTables
{
    // Loot tables per level - items appropriate for each level
    private static readonly EObjectType[][] levelLootTables =
    {
        // Level 0 - doesn't exist
        new EObjectType[] {},
        
        // Level 1
        new []
        {
            EObjectType.Coin, EObjectType.Coin, EObjectType.Coin,
            EObjectType.GoldCoin, EObjectType.GoldCoin,
            EObjectType.PieceOfMeat, EObjectType.LoafOfBreadA, EObjectType.PieceOfCheese, EObjectType.Apple,
            EObjectType.LeatherBoots, EObjectType.LeatherGloves,
            EObjectType.Lockpick, EObjectType.Lockpick,
            EObjectType.Spike,
            EObjectType.BoneA, EObjectType.BoneB,
            EObjectType.RedGem, EObjectType.SmallBlueGem,
            EObjectType.RedPotion, EObjectType.GreenPotion
        },
        
        // Level 2
        new []
        {
            EObjectType.Coin, EObjectType.Coin,
            EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin,
            EObjectType.PieceOfMeat, EObjectType.LoafOfBreadA, EObjectType.PieceOfCheese, EObjectType.Fish,
            EObjectType.LeatherBoots, EObjectType.ChainBoots,
            EObjectType.Lockpick, EObjectType.Lockpick,
            EObjectType.Spike, EObjectType.Spike,
            EObjectType.BoneA, EObjectType.BoneB,
            EObjectType.RedGem, EObjectType.SmallBlueGem, EObjectType.LargeBlueGem,
            EObjectType.RedPotion, EObjectType.GreenPotion
        },
        
        // Level 3
        new []
        {
            EObjectType.Coin,
            EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin,
            EObjectType.PieceOfMeat, EObjectType.LoafOfBreadB, EObjectType.PieceOfCheese, EObjectType.Fish, EObjectType.Mushroom,
            EObjectType.LeatherLeggings, EObjectType.ChainBoots, EObjectType.ChainGauntlets,
            EObjectType.Lockpick, EObjectType.Lockpick, EObjectType.Lockpick,
            EObjectType.Spike, EObjectType.Spike,
            EObjectType.BoneA, EObjectType.BoneB,
            EObjectType.RedGem, EObjectType.SmallBlueGem, EObjectType.LargeBlueGem,
            EObjectType.RedPotion, EObjectType.GreenPotion
        },
        
        // Level 4
        new []
        {
            EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin,
            EObjectType.PieceOfMeat, EObjectType.LoafOfBreadB, EObjectType.Fish, EObjectType.Mushroom,
            EObjectType.ChainBoots, EObjectType.ChainGauntlets, EObjectType.ChainCowl,
            EObjectType.Lockpick, EObjectType.Lockpick, EObjectType.Lockpick,
            EObjectType.Spike, EObjectType.Spike, EObjectType.Spike,
            EObjectType.BoneA, EObjectType.BoneB,
            EObjectType.RedGem, EObjectType.SmallBlueGem, EObjectType.LargeBlueGem,
            EObjectType.RedPotion, EObjectType.GreenPotion
        },
        
        // Level 5
        new []
        {
            EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin,
            EObjectType.PieceOfMeat, EObjectType.LoafOfBreadB, EObjectType.Fish, EObjectType.Mushroom,
            EObjectType.ChainBoots, EObjectType.ChainGauntlets, EObjectType.ChainCowl, EObjectType.Helmet,
            EObjectType.Lockpick, EObjectType.Lockpick, EObjectType.Lockpick,
            EObjectType.Spike, EObjectType.Spike, EObjectType.Spike,
            EObjectType.BoneA, EObjectType.BoneB,
            EObjectType.RedGem, EObjectType.SmallBlueGem, EObjectType.LargeBlueGem,
            EObjectType.RedPotion, EObjectType.GreenPotion
        },
        
        // Level 6
        new []
        {
            EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin,
            EObjectType.PieceOfMeat, EObjectType.LoafOfBreadB, EObjectType.Fish, EObjectType.Mushroom,
            EObjectType.ChainBoots, EObjectType.PlateGauntlets, EObjectType.Helmet,
            EObjectType.Lockpick, EObjectType.Lockpick, EObjectType.Lockpick,
            EObjectType.Spike, EObjectType.Spike, EObjectType.Spike,
            EObjectType.BoneA, EObjectType.BoneB,
            EObjectType.RedGem, EObjectType.SmallBlueGem, EObjectType.LargeBlueGem,
            EObjectType.RedPotion, EObjectType.GreenPotion
        },
        
        // Level 7
        new []
        {
            EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin,
            EObjectType.PieceOfMeat, EObjectType.LoafOfBreadB, EObjectType.Fish, EObjectType.Mushroom,
            EObjectType.PlateBoots, EObjectType.PlateGauntlets, EObjectType.Helmet,
            EObjectType.Lockpick, EObjectType.Lockpick, EObjectType.Lockpick,
            EObjectType.Spike, EObjectType.Spike, EObjectType.Spike,
            EObjectType.BoneA, EObjectType.BoneB,
            EObjectType.RedGem, EObjectType.SmallBlueGem, EObjectType.LargeBlueGem,
            EObjectType.RedPotion, EObjectType.GreenPotion
        },
        
        // Level 8
        new []
        {
            EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin, EObjectType.GoldCoin,
            EObjectType.PieceOfMeat, EObjectType.LoafOfBreadB, EObjectType.Fish, EObjectType.Mushroom,
            EObjectType.PlateBoots, EObjectType.PlateGauntlets, EObjectType.Helmet,
            EObjectType.Lockpick, EObjectType.Lockpick, EObjectType.Lockpick,
            EObjectType.Spike, EObjectType.Spike, EObjectType.Spike,
            EObjectType.BoneA, EObjectType.BoneB,
            EObjectType.RedGem, EObjectType.SmallBlueGem, EObjectType.LargeBlueGem,
            EObjectType.RedPotion, EObjectType.GreenPotion
        },
        
        // Level 9 - no debris piles
        new EObjectType[] {}
    };
    
    // Potion enchantments that can be found in debris (STRINGS.PAK block 6 indices)
    private static readonly int[] potionEnchantmentStringIndices =
    {
        261, // Leap
        264, // Lesser Heal
        275, // Heal
        286, // Greater Heal
        Magic.StringIndexManaBoost,
        Magic.StringIndexRestoreMana,
        256, // Light
        270, // Night Vision
        268, // Speed
        257, // Resist Blows
        273, // Thick Skin
        260, // Stealth
        274, // Water Walk
        278, // Flameproof
        Magic.StringIndexPoisonResistance
    };
    
    /// <summary>
    /// Get the loot table for a specific level
    /// </summary>
    public static EObjectType[] GetLootForLevel(int level)
    {
        if (level < 0 || level >= levelLootTables.Length)
        {
            // Default to level 0 if out of range
            return levelLootTables[0];
        }
        return levelLootTables[level];
    }
    
    /// <summary>
    /// Get random loot items for a level
    /// </summary>
    public static List<EObjectType> GetRandomLoot(int level, int count)
    {
        EObjectType[] lootTable = GetLootForLevel(level);
        List<EObjectType> result = new List<EObjectType>();
        
        for (int i = 0; i < count && lootTable.Length > 0; i++)
        {
            EObjectType item = lootTable[Random.Range(0, lootTable.Length)];
            result.Add(item);
        }
        
        return result;
    }
    
    /// <summary>
    /// Get a random potion enchantment name
    /// </summary>
    public static string GetRandomPotionEnchantment()
    {
        int stringIndex = potionEnchantmentStringIndices[Random.Range(0, potionEnchantmentStringIndices.Length)];
        return StringLoader.GetString(6, stringIndex);
    }
}
