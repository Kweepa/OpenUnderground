using System.Collections;
using UnityEngine;

public class Bedroll : UUObject
{
    public CutscenePlayer[] dreams;

    public override EEquipAction Equip()
    {
        if (Utils.CanSleepHere(notify: true))
        {
            EPoisonRating poisonRating = Utils.GetPoisonRating();
            switch (poisonRating)
            {
            case EPoisonRating.Barely:
            case EPoisonRating.Mildly:
            case EPoisonRating.Badly:
                PlayerObject.Player.StartCoroutine(BedrollUpdate());
                break;
            case EPoisonRating.Seriously:
            case EPoisonRating.Egregiously:
                Messages.Add($"You can't sleep while {poisonRating} poisoned.");
                break;
            }

        }
        return EEquipAction.Nothing;
    }

    public override string GetUseText()
    {
        return "Sleep";
    }

    private IEnumerator BedrollUpdate()
    {
        PlayerObject.DisableControls(EControlMask.Resting, true);
        
        Messages.Add(1, 15); // you make camp

        // turn out the lights
        foreach (EInvSlot slot in new[] { EInvSlot.LeftShoulder, EInvSlot.RightShoulder })
        {
            LightSource lightSource = Inventory.sInv.invSlotContents[(int) slot] as LightSource;
            if (lightSource != null && lightSource.IsLit())
            {
                lightSource.SetLit(false);
                Messages.Add($"You extinguish your {lightSource.singularName}.");
            }
        }

        EPoisonRating poisonRating = (EPoisonRating) Mathf.Clamp(PlayerData.sData.poison / 6, 0, 5);

        PlayerObject.Player.fadeIn = false;

        while (PlayerObject.Player.fade < 0.5f)
        {
            PlayerObject.Player.fade += Time.unscaledDeltaTime;
            yield return null;
        }

        Messages.Add(1, 16); // you go to sleep

        while (PlayerObject.Player.fade < 1.0f)
        {
            PlayerObject.Player.fade += Time.unscaledDeltaTime;
            yield return null;
        }

        PlayerObject.Player.fade = 1.0f;

        // determine whether to play a dream

        int dreamIndex = PlayerData.GetDreamIndex(); 
        if (dreamIndex >= 0)
        {
            PlayerData.sData.MarkDreamDreamt(dreamIndex);
            CutscenePlayer dream = Instantiate(dreams[dreamIndex]);
            while (dream != null)
            {
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(2.0f);
        }

        PlayerData.sData.gameTime += 8 * 60 * 60; // 8 hours

        // play appropriate message (depends on hunger and maybe more?)
        if (PlayerData.sData.hunger >= 224 || poisonRating >= EPoisonRating.Badly)
        {
            Messages.Add(1, 19); // sleep uneasily
            PlayerData.sData.fatigue = 10;
        }
        else
        {
            Messages.Add(1, 18); // you feel rested
            PlayerData.sData.fatigue = 0;
            PlayerObject.Player.RestoreHealth(PlayerData.sData.vitality / 2);
        }
        PlayerData.sData.poison = 0;
        
        // add to hunger
        PlayerData.sData.hunger = Mathf.Min(PlayerData.sData.hunger + 80, 255);

        // stop spells
        Magic.sMagic.StopAllSpells();
        
        // close some doors
        Door.CloseRandomDoors();

        while (PlayerObject.Player.fade > 0.0f)
        {
            PlayerObject.Player.fade -= Time.unscaledDeltaTime;
            yield return null;
        }
        PlayerObject.Player.fade = 0.0f;
        
        PlayerObject.DisableControls(EControlMask.Resting, false);
    }
}
