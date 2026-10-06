using UnityEngine;

/// <summary>
/// The original's damage types and its resistance filter.
/// </summary>
/// <remarks>
/// Every damage in the original goes through one gate (UW.EXE 0x25f0a), which first hands the
/// amount, with a mask of the kinds of damage it is, to a filter (0x25eaf). The filter compares
/// the mask with the target's resistances, byte 8 of COMOBJ.DAT for its object type. The player
/// is object type 127, and the game rebuilds that row from the spells in force and the enchanted
/// items worn (0x7e444, case 3 of 0x7e007), so the player's resistances are the same kind of byte.
/// A resistance does not cut the damage: it stops it, or it does not.
///
/// Ours, behind <see cref="PlayerInput.PartialResistances"/>: the player's Flameproof and Poison
/// Resistance cut their damage to a third instead of stopping it, so they leave the player's byte.
/// Flameproof's cut is <see cref="CutForPlayer"/>, Poison Resistance's is in <see cref="PoisonClock"/>.
/// </remarks>
public static class Resistance
{
    // The bits of a mask, and of a resistance byte (UW.EXE 0x25eaf and its nineteen callers).

    /// <summary>Magic, one hit in three stopped. Magic Protection.</summary>
    public const int MagicLesser = 0x01;

    /// <summary>Magic, two hits in three stopped. Greater Magic Protection.</summary>
    public const int MagicGreater = 0x02;

    /// <summary>The two magic bits together: as a resistance it stops every magic hit.</summary>
    public const int Magic = MagicLesser | MagicGreater;

    /// <summary>A physical blow: melee both ways, the traps. No resistance of the player has it.</summary>
    public const int Blow = 0x04;

    /// <summary>Fire: lava, the fireball, Flame Wind. Flameproof.</summary>
    public const int Fire = 0x08;

    /// <summary>Poison: the poison itself, acid, and being poisoned at all. Poison Resistance.</summary>
    public const int Poison = 0x10;

    /// <summary>Lightning: the lightning bolt.</summary>
    public const int Lightning = 0x20;

    /// <summary>Missiles: ammunition and the magic missile. Missile Protection.</summary>
    public const int Missile = 0x40;

    /// <summary>
    /// Not a damage but a marker: the undead have it, and Smite Undead looks for it (0x34b98).
    /// </summary>
    public const int Undead = 0x80;

    /// <summary>
    /// Flame Wind, and a fireball's hit: fire, and magic. The fireball's comes from its row of the
    /// missile table; the blasts' from the table at DS:0x9d3 (UW.EXE 0x35bca).
    /// </summary>
    public const int FlameWind = Fire | Magic;

    /// <summary>Sheet Lightning: magic only (DS:0x9d3, kind 2).</summary>
    public const int SheetLightning = Magic;

    /// <summary>
    /// What is left of <paramref name="amount"/> once the resistances have had their say: all of
    /// it, or nothing. A line-by-line port of UW.EXE 0x25eaf.
    /// </summary>
    /// <remarks>
    /// With no bit in common the damage passes. If the mask has magic in it, the resistance's two
    /// magic bits read as a number, 0 to 3, and a roll of rand() % 3 below it stops the damage;
    /// otherwise the magic bits leave the mask. Any bit still in common then stops it. So a
    /// fireball, fire and magic, is stopped every time by Flameproof, and one time in three by
    /// Magic Protection alone.
    /// </remarks>
    public static int Filter(int amount, int mask, int resistances)
    {
        if ((resistances & mask) == 0)
        {
            return amount;
        }

        if ((mask & Magic) != 0)
        {
            if (Random.Range(0, 3) < (resistances & Magic))
            {
                return 0;
            }

            mask &= ~Magic;
        }

        return (resistances & mask) != 0 ? 0 : amount;
    }

    /// <summary>The player's resistances now. See <see cref="global::Magic.GetResistances"/>.</summary>
    public static int Player => global::Magic.sMagic != null ? global::Magic.sMagic.GetResistances() : 0;

    /// <summary>
    /// Whether the player's Flameproof cuts fire to a third instead of stopping it. Ours.
    /// </summary>
    public static bool FlameproofCuts =>
        PlayerInput.PartialResistances && global::Magic.sMagic != null && global::Magic.sMagic.IsSpellActive(global::Magic.ESpell.Flameproof);

    /// <summary>
    /// Whether the player's Poison Resistance cuts the poison to a third instead of stopping it. Ours.
    /// It leaves being poisoned alone, and acid too: it is not a resistance to acid.
    /// </summary>
    public static bool PoisonResistanceCuts =>
        PlayerInput.PartialResistances && global::Magic.sMagic != null && global::Magic.sMagic.IsSpellActive(global::Magic.ESpell.PoisonResistance);

    /// <summary>
    /// What reaches the player of a damage the filter let through: a third of it when it is fire
    /// and Flameproof is on, all of it otherwise. Ours.
    /// </summary>
    public static int CutForPlayer(int amount, int mask)
    {
        return (mask & Fire) != 0 && FlameproofCuts ? Third(amount, Random.Range(0, 3)) : amount;
    }

    /// <summary>
    /// A third of <paramref name="amount"/>, the remainder rounded at random so that the average is
    /// a third exactly: 7 gives 2, or 3 one time in three. <paramref name="roll"/> is 0, 1 or 2.
    /// </summary>
    public static int Third(int amount, int roll)
    {
        return amount / 3 + (roll < amount % 3 ? 1 : 0);
    }

    /// <summary>
    /// Whether the player escapes a poisoning: the original asks the filter for one point of
    /// poison and poisons only when it comes back (UW.EXE 0x25adf for a bite, 0x73e73 for a trap).
    /// </summary>
    public static bool PlayerResistsPoisoning()
    {
        return Filter(1, Poison, Player) == 0;
    }

    /// <summary>
    /// The mask of a missile, from its row of the missile table in OBJECTS.DAT, which keeps it
    /// negated: the original pushes minus the row's third byte (UW.EXE 0x2b355). So the four
    /// physical missiles, 0xC0, are 0x40; the fireball 0x0B, the lightning bolt 0x23, acid 0x10
    /// and the magic missile 0x43.
    /// </summary>
    public static int OfMissile(EObjectType type)
    {
        if (type == EObjectType.Knife)
        {
            // The mages' thrown knife is this project's: type 27 lands among the launchers,
            // whose rows hold an ammunition index there instead, so it gets the mask of the
            // physical missiles it is.
            return Missile;
        }

        if ((int)type < 16 || (int)type > 23)
        {
            return 0;
        }

        return -DataLoader.sDataLoader.objectsData.missileStats[(int)type & 15].marker & 0xff;
    }
}
