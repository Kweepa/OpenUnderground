/// <summary>
/// Decodes an enchantment number - which is an index into STRINGS.PAK block 6 - into what
/// the original game does with it.
/// </summary>
/// <remarks>
/// There is no lookup table to build. UW.EXE 0x38b5c splits the number with two shifts:
/// <c>kind = (n &amp; 0x1ff) &gt;&gt; 4</c> and <c>parameter = n &amp; 0xf</c>. The kind picks a case in
/// the effect applicator (UW.EXE 0x7e007, fourteen cases) and the parameter is its argument.
/// Block 6 is the same grid written out: sixteen name slots per kind, which is why names
/// repeat - the sixteen "Cursed" entries at 144-159 are one kind with sixteen parameters,
/// not sixteen enchantments.
///
/// Matching the printed name instead, which is what this code used to do, only works while
/// one strings file is loaded, because block 6 carries the same names in three parallel
/// tables: item enchantments low down, spell names from 256, a third set from 384. An
/// armour's number is <c>special - 512</c>, so a ring of Resist Blows is 34 while the spell
/// is 257, and the name was the only bridge between them.
/// </remarks>
public static class Enchantment
{
    /// <summary>No enchantment.</summary>
    public const int None = -1;

    /// <summary>
    /// An object's <c>special</c> only means "enchantment" from here up. Below this it is the
    /// quantity of a stack, and the two share the same ten bits - <see cref="UUObject.quantity"/>
    /// reads and writes <c>special</c> directly. Writing an enchantment low therefore does not
    /// store an enchantment at all: it mints items. A potion of Mana Boost written as 51 becomes
    /// a stack of fifty-one potions.
    /// </summary>
    public const int FirstEnchantedSpecial = 512;

    /// <summary>Block 6 holds spell names from here; a number in this range is a spell number.</summary>
    public const int FirstSpellName = 256;

    /// <summary>
    /// What a potion of Poison Resistance carries in <c>special</c>. The debris loot offers that
    /// potion, but Poison Resistance is not a spell: a potion names its spell as 256 plus the low
    /// six bits of <c>special</c>, and 55, its number, is outside that range. Written the way a
    /// spell is, it would come out as 311 - below 368, so it would read as the size of a stack
    /// and one potion would show as 311. 368 is the lowest value that is not a stack size, and no
    /// potion from the level data carries it, so here it stands for this potion alone.
    /// </summary>
    public const int PoisonResistancePotionSpecial = 368;

    /// <summary>The <c>special</c> a potion carries for an enchantment number.</summary>
    public static int PotionSpecialFor(int enchantmentNumber)
    {
        return enchantmentNumber == Magic.StringIndexPoisonResistance
            ? PoisonResistancePotionSpecial
            : FirstEnchantedSpecial + enchantmentNumber - FirstSpellName;
    }

    /// <summary>
    /// The enchantment number a potion's <c>special</c> stands for, or <see cref="None"/> when it
    /// holds none: below 512 it is a stack size, as in a save written before the debris recorded
    /// its roll. The inverse of <see cref="PotionSpecialFor"/> for every number the loot can roll.
    /// </summary>
    public static int PotionNumberFrom(int special)
    {
        if (special == PoisonResistancePotionSpecial)
        {
            return Magic.StringIndexPoisonResistance;
        }
        return special >= FirstEnchantedSpecial ? FirstSpellName + (special & 0x3f) : None;
    }

    /// <summary>The effect kind, or -1 when the number is not one of the item enchantments.</summary>
    public static int KindOf(int index)
    {
        return index >= 0 && index < FirstSpellName ? (index & 0x1ff) >> 4 : -1;
    }

    /// <summary>The parameter of the effect: its strength, or which variant of the kind it is.</summary>
    public static int ParameterOf(int index)
    {
        return index >= 0 && index < FirstSpellName ? index & 0xf : -1;
    }

    // The kinds, named after what the applicator's case actually does.
    public const int KindLight = 0;         // eight brightness levels, parameter 0-7
    public const int KindMovement = 1;      // Leap, Slow Fall, Levitate, Water Walk, Fly
    public const int KindSoakDamage = 2;    // Resist Blows 2, Thick Skin 3, Iron Flesh 5
    public const int KindWard = 3;          // Curse, stealth, and the five resistances
    public const int KindCursed = 9;        // sixteen grades, 144-159
    // public const int KindMana = 10;         // Increase / Boost / Regain / Restore, four each (not read)
    public const int KindRegeneration = 11; // parameter 14 health, 15 mana
    // public const int KindItemBonus = 12;    // Accuracy / Damage / Protection / Toughness (not read)
}
