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
            int durability = originator.GetDurability();
            // The original refuses by type alone - 0x7473f returns -1 for the missile row - and
            // has no quality gate at all: searched every 48/47/49 immediate across the six
            // functions from the anvil's use handler down (UW.EXE 0x36ef9, 0x36b9a, 0x36eaf,
            // 0x748c4, 0x747ac, 0x7473f), and the only hits are a type mask and lcall segment
            // fields. An item in good condition can be brought to the anvil, which calls it trivial.
            // The one guard kept is at full quality, and it is deliberately not the original's:
            // there the anvil takes a pristine item too, and the attempt can only waste time or
            // destroy it. Offering a repair with no upside and a real risk reads as a bug.
            if (originator.quality < 63 && durability != 255)
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

        // What the job costs in game time, worked out before the roll from the quality the item
        // still has (UW.EXE 0x747da). The original adds 60 * (minutes << 8) to the clock at
        // P1[0xce] (0x749ed), and an hour of that clock is 921600 units - the constant the sleep
        // code multiplies by (0x81f8f) - so 256 units to the second makes the cost exactly this
        // many minutes, never fewer than fifteen. It is charged whatever the outcome.
        int minutes = Mathf.Max(15, durability * 3 - repairSkill - itemToRepair.quality / 2);

        Skills.ESkillTestResult result = Skills.GetResult(repairSkill, durability);
        int message = 0;
        switch (result)
        {
        case Skills.ESkillTestResult.CriticalFailure:
            // A saving throw, and failing it costs the item. The original survives with damage on
            // `(rand() & 63) <= quality + Repair` and otherwise returns -2, which the caller turns
            // into message 140 and destroys the object (UW.EXE 0x74856, 0x74a12). Two departures
            // were here: the bound was `<` where the original has `<=`, and the losing branch did
            // nothing at all instead of destroying. Measured in the original with a fabricated
            // save - Repair 0, a long sword at quality 10 - eight of ten attempts destroyed it.
            if (Random.Range(0, 64) <= repairSkill + itemToRepair.quality)
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
                message = 140;
                Utils.DestroyItem(itemToRepair);
            }
            break;
        case Skills.ESkillTestResult.Failure:
            message = 143;
            break;
        case Skills.ESkillTestResult.Success:
            // The original returns 3 - "You have fully repaired the" - whenever the new quality
            // would pass 63, and it reaches that from an ordinary success too, not just a critical
            // one (UW.EXE 0x7486f). Topping an item out said "partially repaired" here.
            itemToRepair.quality += repairSkill / 5 + 3;
            message = itemToRepair.quality > 63 ? 145 : 144;
            itemToRepair.quality = Mathf.Min(itemToRepair.quality, 63);
            ++PlayerData.sData.numRepairs;
            break;
        case Skills.ESkillTestResult.CriticalSuccess:
            itemToRepair.quality = 63;
            message = 145;
            ++PlayerData.sData.numRepairs;
            break;
        }
        // The clock exists here and Bedroll, Food and Incense already jump it, but hunger and
        // fatigue do not follow it - they run on their own accumulators in PlayerObject.Update() -
        // so they are moved by hand. Hunger goes at that code's own rate of one a minute; fatigue
        // is at a twelfth, settled by playing it: at the full rate two repairs were enough to
        // leave a rested character fatigued and a third one bleeding. Neither mapping is the
        // original's - what is the original's is the clock.
        PlayerData.sData.gameTime += 60.0 * minutes;
        PlayerData.sData.hunger = Mathf.Min(PlayerData.sData.hunger + minutes, 255);
        PlayerData.sData.fatigue = Mathf.Min(PlayerData.sData.fatigue + minutes / 12, 30);

        Messages.Add($"{StringLoader.GetString(1, message)}{itemToRepair.singularName}.");

        while (PlayerObject.Player.fade > 0.0f)
        {
            PlayerObject.Player.fade -= Time.unscaledDeltaTime;
            yield return null;
        }
        PlayerObject.Player.fade = 0.0f;
    }
}
