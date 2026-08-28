using UnityEngine;

public class Leeches : UUObject
{
    public AudioClip fleshRipClip;

    public override EEquipAction Equip()
    {
        if (PlayerData.sData.poison > 0)
        {
            PlayerObject.Player.Damage(Skills.ESkillTestResult.Success, Random.Range(1, Mathf.Max(2, PlayerData.sData.poison)), EDamageType.Direct);
            PlayerData.sData.poison = 0;
            Messages.Add(1, 224);
            Utils.PlayClip2d(fleshRipClip);
            return EEquipAction.Consume;
        }

        return EEquipAction.Nothing;
    }

    public override string GetUseText()
    {
        return PlayerData.sData.poison > 0 ? "Use" : null;
    }
}
