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
    
    public static bool AdvanceSkill(ESkill skill)
    {
        int attrib = GetGoverningAttribute(skill);
        int curVal = GetSkill(skill);
        if (curVal < 2 * attrib && curVal < 30)
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

    public static bool AdvanceSkills(ESkill min, ESkill max)
    {
        int eligibleCount = 0;
        int[] eligible = new int[(int)ESkill.Swimming + 1];
        for (int i = (int)min; i <= (int)max; i++)
        {
            if (PlayerData.sData.skill[i] < 30)
            {
                eligible[eligibleCount++] = i;
            }
        }

        if (eligibleCount == 0)
        {
            Messages.Add(1, 27);
            return false;
        }

        int pick1 = eligible[Random.Range(0, eligibleCount)];
        int pick2 = eligible[Random.Range(0, eligibleCount)];
        ESkill skill1 = (ESkill)pick1;
        ESkill skill2 = (ESkill)pick2;
        AdvanceSkill(skill1);
        AdvanceSkill(skill2);
        Messages.Add(1, 26);
        if (skill1 == skill2)
        {
            Messages.Add($"{StringLoader.GetString(1, 28)}{skill1}.");
        }
        else
        {
            Messages.Add($"{StringLoader.GetString(1, 29)}{skill1} and {skill2}.");
        }
        --PlayerData.sData.skillPoints;
        return true;
    }

    public static int GetSkill(ESkill skill)
    {
        return PlayerData.sData.skill[(int)skill];
    }
}
