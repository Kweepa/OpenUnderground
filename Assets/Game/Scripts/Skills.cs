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
        int roll = hitChance + Random.Range(0, 30) - target;

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

    private static int GetRandomRange(ESkill skill)
    {
        int i = (int)skill;
        if (i < 7) return 25; // strength
        if (i < 10) return 10; // intellect
        return 40; // dexterity
    }

    public static void ManaAdvanced()
    {
        // https://wiki.ultimacodex.com/wiki/Character_attributes#Ultima_Underworld_and_Ultima_Underworld_II
        int manaSkill = PlayerData.sData.skill[(int)ESkill.Mana];
        int intellect = PlayerData.sData.intellect;
        PlayerData.sData.maxMana = ((3 * manaSkill + 2) * intellect + 12) / 24;
        PlayerData.sData.mana = PlayerData.sData.maxMana;
    }
    
    public static bool AdvanceSkill(ESkill skill)
    {
        int attrib = GetGoverningAttribute(skill);
        int curVal = GetSkill(skill);
        if (curVal < 2 * attrib && curVal < 30)
        {
            ++curVal;
            if (curVal < attrib / 2)
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
                ManaAdvanced();
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
