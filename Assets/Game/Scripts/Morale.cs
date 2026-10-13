/// <summary>
/// When a creature struck by the player runs, as UW.EXE 0x1c033 decides it.
/// </summary>
/// <remarks>
/// The hit points are weighed against the base of the creature's type, critterStats byte 4, and not
/// against what this one started with, which the level data sets anywhere from a fifth of the base to
/// several times it. Above three quarters of the base it never runs, and below an eighth it never
/// does either: it fights to the end. In between, a blow of more than half the base sends it running
/// whatever its courage; otherwise it runs when <c>15 - courage &gt;= hp * 16 / base + roll</c>, the
/// roll 0 to 3. So a courage of 14 or 15 runs only from the big blow, and a courage of 1 almost
/// always does. Critter.Retaliate() decides when the rule is asked.
/// </remarks>
public static class Morale
{
    /// <summary>The roll is <c>rand() % 4</c>: values from 0 to RollRange - 1.</summary>
    public const int RollRange = 4;

    /// <param name="baseHp">critterStats byte 4 of the creature's type.</param>
    /// <param name="hp">Its hit points after the blow.</param>
    /// <param name="courage">The low nibble of critterStats byte 0x1c, 0 to 15.</param>
    /// <param name="damage">The damage the creature took since it last answered a blow (UW.EXE 0x1c2d2).</param>
    /// <param name="roll">0 to RollRange - 1.</param>
    public static bool Flees(int baseHp, int hp, int courage, int damage, int roll)
    {
        if (hp > (baseHp * 3 >> 2))
        {
            return false;
        }

        if (hp < (baseHp >> 3))
        {
            return false;
        }

        if (damage > (baseHp >> 1))
        {
            return true;
        }

        if (baseHp == 0)
        {
            return false;
        }

        return 15 - courage >= hp * 16 / baseHp + roll;
    }
}
