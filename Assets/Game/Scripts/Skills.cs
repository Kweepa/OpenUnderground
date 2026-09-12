using UnityEngine;

public enum ESkill
{
    Attack,
    Defense,
    Unarmed,
    Sword,
    Axe,
    Mace,
    Missile,
    Mana,
    Lore,
    Casting,
    Traps,
    Search,
    Track,
    Sneak,
    Repair,
    Charm,
    Picklock,
    Acrobat,
    Appraise,
    Swimming
}

public class Skills
{
    public enum ESkillTestResult
    {
        CriticalFailure,
        Failure,
        Success,
        CriticalSuccess
    }

    public static ESkillTestResult GetResult(int hitChance, int target)
    {
        // The original draws rand() % 31, so 0 to 30 inclusive - thirty-one values, not
        // thirty. Random.Range with an int upper bound is exclusive, so it needs 31 to match.
        // UW.EXE 0x3419c, read whole: mov $0x1f,%bx; cwtd; idiv %bx; add %dx,%si. rand() is
        // 0xec5:0x0de7, file 0x12c37, and returns 0 to 32767, so the remainder spans the
        // full 0 to 30. The four thresholds below already match the original exactly.
        int roll = hitChance + Random.Range(0, 31) - target;

        if (roll <= 2)
        {
            return ESkillTestResult.CriticalFailure;
        }
        else if (roll < 16)
        {
            return ESkillTestResult.Failure;
        }
        else if (roll < 29)
        {
            return ESkillTestResult.Success;
        }
        else
        {
            return ESkillTestResult.CriticalSuccess;
        }
    }

    private static int GetGoverningAttribute(ESkill skill)
    {
        int i = (int)skill;
        if (i < 7) return PlayerData.sData.strength;
        if (i < 10) return PlayerData.sData.intellect;
        return PlayerData.sData.dexterity;
    }

    /// <summary>True for the seven skills the original governs with Strength (Attack..Missile).</summary>
    private static bool IsStrengthSkill(ESkill skill)
    {
        return (int)skill < 7;
    }

    private static int GetRandomRange(ESkill skill)
    {
        int i = (int)skill;
        if (i < 7) return 25; // strength
        if (i < 10) return 10; // intellect
        return 40; // dexterity
    }

    /// <summary>
    /// Recomputes the mana ceiling from the Mana skill and Intelligence, and optionally refills the
    /// pool. The original computes (Mana + 1) * Intelligence / 8 and refills only when its caller
    /// asks: character creation passes 1, the shrine passes 0 (UW.EXE 0x81305, called from 0x6ca4c
    /// and 0x819ab).
    /// </summary>
    public static void ManaAdvanced(bool refill)
    {
        int manaSkill = PlayerData.sData.skill[(int)ESkill.Mana];
        int intellect = PlayerData.sData.intellect;
        PlayerData.sData.maxMana = (manaSkill + 1) * intellect / 8;
        if (refill)
        {
            PlayerData.sData.mana = PlayerData.sData.maxMana;
        }
    }
    
    /// <summary>
    /// Whether this skill could still take an advance, without taking one.
    /// </summary>
    /// <remarks>
    /// The ceiling is twice the governing attribute. The original is a shade looser - it fails
    /// only once twice the attribute has fallen below the skill (UW.EXE 0x81521, a cmp followed
    /// by jl), so a skill resting exactly on the ceiling takes one more step and ends a point
    /// above it. That is left alone deliberately: the ceiling a player is told about is twice
    /// the attribute, and a skill standing one past it reads as a bug rather than as fidelity.
    /// </remarks>
    public static bool CanAdvanceSkill(ESkill skill)
    {
        int curVal = GetSkill(skill);
        return curVal < 2 * GetGoverningAttribute(skill) && curVal < 30;
    }

    public static bool AdvanceSkill(ESkill skill)
    {
        int attrib = GetGoverningAttribute(skill);
        int curVal = GetSkill(skill);
        if (CanAdvanceSkill(skill))
        {
            ++curVal;
            // The original grants this second point only when the governing attribute is not
            // Strength: the test is on the attribute index and skips it when that index is 0, so
            // the seven Strength skills (Attack..Missile) never get it (UW.EXE 0x8155a).
            if (!IsStrengthSkill(skill) && curVal < attrib / 2)
            {
                ++curVal;
            }

            int diff = attrib - curVal;
            if (diff > 0 && Random.Range(0, GetRandomRange(skill)) < diff)
            {
                ++curVal;
            }
            int i = (int)skill;
            PlayerData.sData.skill[i] = curVal;

            switch (skill)
            {
            case ESkill.Mana:
                // Advancing Mana raises the ceiling; the original does not refill the pool here.
                ManaAdvanced(refill: false);
                break;
            }
            
            return true;
        }
        return false;
    }

    /// <summary>
    /// A group mantra: SUMM RA, MU AHM or OM CAH. How many skills it advances belongs to the
    /// mantra and is not the same for all three (UW.EXE 0x816fd, the three cases of the jump
    /// table at 0x819d3 set a base, a width and a count: 0/7/3, 7/3/2 and 10/10/4).
    /// </summary>
    /// <remarks>
    /// The original draws blind over the whole group and tries each draw as it comes, so a draw
    /// that lands on a skill which cannot rise is spent rather than re-rolled (the loop at
    /// 0x81922: the count comes down on every pass, and only a successful advance is recorded).
    /// Filtering the group first would never waste one, which is a kinder rule than the game's.
    /// The point is spent whatever happens, and there is a message for the case where nothing
    /// improved - the original prints it (0x81676) and reaches it the same way.
    /// </remarks>
    public static bool AdvanceSkills(ESkill min, ESkill max, int count)
    {
        // Deliberately kinder than the original, which spends the point whatever happens: if
        // nothing in the group can move at all, say so and let the player keep the point. Kept
        // as a modern convenience rather than restored to the original's behaviour.
        bool anyCanAdvance = false;
        for (int i = (int)min; i <= (int)max && !anyCanAdvance; i++)
        {
            anyCanAdvance = CanAdvanceSkill((ESkill)i);
        }

        if (!anyCanAdvance)
        {
            Messages.Add(1, 30); // none of your skills improved
            return false;
        }

        int width = max - min + 1;
        // The original's safety counter is the group's width. It never binds on the three real
        // mantras, where the count is always the smaller of the two, but it is what stops the
        // loop in the original and it costs nothing to keep.
        int attempts = width;
        ESkill[] advanced = new ESkill[4];
        int advancedCount = 0;

        while (count > 0 && attempts-- > 0)
        {
            ESkill skill;
            // MU AHM leans on Mana while Mana is still low: one draw in two is forced to it
            // instead of being rolled. The original tests the group's base, the skill against 8,
            // and a single bit of rand().
            if (min == ESkill.Mana && GetSkill(ESkill.Mana) < 8 && Random.Range(0, 2) == 0)
            {
                skill = ESkill.Mana;
            }
            else
            {
                skill = (ESkill)((int)min + Random.Range(0, width));
            }

            if (AdvanceSkill(skill) && advancedCount < advanced.Length)
            {
                advanced[advancedCount++] = skill;
            }

            count--;
        }

        if (advancedCount == 0)
        {
            Messages.Add(1, 30); // none of your skills improved
        }
        else
        {
            // When every advance landed on the same skill, and there was more than one, say it
            // went up greatly - the wording the single-skill mantras use for their two attempts.
            bool allTheSame = true;
            for (int i = 1; i < advancedCount; i++)
            {
                allTheSame &= advanced[i] == advanced[0];
            }

            if (allTheSame && advancedCount > 1)
            {
                Messages.Add($"{StringLoader.GetString(1, 28)}{advanced[0]}.");
            }
            else
            {
                string list = "";
                for (int i = 0; i < advancedCount; i++)
                {
                    if (i > 0)
                    {
                        list += (i == advancedCount - 1) ? " and " : ", ";
                    }
                    list += advanced[i];
                }
                Messages.Add($"{StringLoader.GetString(1, 29)}{list}.");
            }
        }

        --PlayerData.sData.skillPoints;
        return true;
    }

    public static int GetSkill(ESkill skill)
    {
        return PlayerData.sData.skill[(int)skill];
    }
}
