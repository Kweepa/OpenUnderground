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
    /// Get a random potion enchantment number (a block 6 index)
    /// </summary>
    public static int GetRandomPotionEnchantmentNumber()
    {
        return potionEnchantmentStringIndices[Random.Range(0, potionEnchantmentStringIndices.Length)];
    }

    // The English names STRINGS.PAK gives the potions above, written out so that they do not
    // depend on the strings file. They serve one case only: a potion from a save written before
    // its roll was recorded in special has nothing but its name, and if the strings file has been
    // swapped for a translated one since, that English name matches nothing read from the file.
    private static readonly Dictionary<int, string> englishPotionEnchantmentNames = new()
    {
        { 261, "Leap" },
        { 264, "Lesser Heal" },
        { 275, "Heal" },
        { 286, "Greater Heal" },
        { Magic.StringIndexManaBoost, "Mana Boost" },
        { Magic.StringIndexRestoreMana, "Restore Mana" },
        { 256, "Light" },
        { 270, "Night Vision" },
        { 268, "Speed" },
        { 257, "Resist Blows" },
        { 273, "Thick Skin" },
        { 260, "Stealth" },
        { 274, "Water Walk" },
        { 278, "Flameproof" },
        { Magic.StringIndexPoisonResistance, "Poison Resistance" }
    };

    /// <summary>
    /// The potion enchantment whose name is <paramref name="savedName"/>, or
    /// <see cref="Enchantment.None"/>. This is for a potion from a save written before the roll
    /// was recorded in special, where the name is all that is left of it. The names from the
    /// strings file in use are tried first, then the fixed English ones, for a save written in
    /// English and loaded with a translation. The fifteen names are all different, so the match
    /// cannot be ambiguous.
    /// </summary>
    public static int FindPotionEnchantmentNumber(string savedName)
    {
        if (string.IsNullOrEmpty(savedName))
        {
            return Enchantment.None;
        }

        foreach (int number in potionEnchantmentStringIndices)
        {
            if (StringLoader.GetString(6, number) == savedName)
            {
                return number;
            }
        }

        foreach (int number in potionEnchantmentStringIndices)
        {
            if (englishPotionEnchantmentNames.TryGetValue(number, out string english) && english == savedName)
            {
                return number;
            }
        }

        return Enchantment.None;
    }
}
