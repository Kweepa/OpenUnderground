using UnityEngine;
using System.Collections;

public class Anvil : UUObject
{
    public CutscenePlayer repairCutscene;
    private static Anvil currentAnvil;

    private void Awake()
    {
        // Subscribe to repair dialog callback (only subscribe once globally)
        if (currentAnvil == null)
        {
            RepairDialog.OnRepairConfirmed += HandleRepairConfirmed;
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe if this was the last anvil
        RepairDialog.OnRepairConfirmed -= HandleRepairConfirmed;
    }

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        if (originator != null && originator.IsRepairable())
        {
            int durability = DataLoader.sDataLoader.objectsData.armourStats[(int)originator.type & 31].durability;
            if (originator.quality < 48 && durability != 255)
            {
                currentAnvil = this;
                RepairDialog.AskRepair(originator, durability);
            }
            else
            {
                Messages.Add(1, 142);
            }
            
        }
        else
        {
            base.TryInteract(originator, sender, action);
        }
    }

    private static void HandleRepairConfirmed(UUObject itemToRepair, int durability)
    {
        // Only process if an anvil initiated the repair
        if (currentAnvil != null)
        {
            currentAnvil.StartCoroutine(currentAnvil.Repair(itemToRepair, durability));
            currentAnvil = null;
        }
    }

    private IEnumerator Repair(UUObject itemToRepair, int durability)
    {
        PlayerObject.Player.fadeIn = false;

        while (PlayerObject.Player.fade < 1.0f)
        {
            PlayerObject.Player.fade += Time.unscaledDeltaTime;
            yield return null;
        }
        PlayerObject.Player.fade = 1.0f;
        
        CutscenePlayer cut = Instantiate(repairCutscene);
        while (cut != null)
        {
            yield return null;
        }
        
        int repairSkill = Skills.GetSkill(ESkill.Repair);
        
        Skills.ESkillTestResult result = Skills.GetResult(repairSkill, durability);
        int message = 0;
        switch (result)
        {
        case Skills.ESkillTestResult.CriticalFailure:
            if (Random.Range(0, 64) < repairSkill + itemToRepair.quality)
            {
                // damage the item, potentially destroying it
                itemToRepair.quality -= Random.Range(4, 12);
                if (itemToRepair.quality <= 0)
                {
                    message = 140;
                    Utils.DestroyItem(itemToRepair);
                }
                else
                {
                    message = 141;
                }
            }
            else
            {
                message = 143;
            }
            break;
        case Skills.ESkillTestResult.Failure:
            message = 143;
            break;
        case Skills.ESkillTestResult.Success:
            itemToRepair.quality += repairSkill / 5 + 3;
            itemToRepair.quality = Mathf.Min(itemToRepair.quality, 63);
            message = 144;
            ++PlayerData.sData.numRepairs;
            break;
        case Skills.ESkillTestResult.CriticalSuccess:
            itemToRepair.quality = 63;
            message = 145;
            ++PlayerData.sData.numRepairs;
            break;
        }
        Messages.Add($"{StringLoader.GetString(1, message)}{itemToRepair.singularName}.");

        while (PlayerObject.Player.fade > 0.0f)
        {
            PlayerObject.Player.fade -= Time.unscaledDeltaTime;
            yield return null;
        }
        PlayerObject.Player.fade = 0.0f;
    }
}
