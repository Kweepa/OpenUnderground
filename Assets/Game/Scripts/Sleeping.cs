using System.Collections;
using UnityEngine;

/// <summary>
/// Sleeping, as UW.EXE 0x81f8f does it.
/// </summary>
/// <remarks>
/// Three ways in: Use on a bedroll; F10, or the D-pad held left, which sleep on a bedroll when there is
/// one anywhere in the pack and otherwise without one (0x82345); and the collapse after a drink, the
/// forced sleep (Food). A sleep lasts 2 to 6 hours, after which the poison is paid and a hostile
/// creature may come and wake the player (Critter.AmbushSleeper()). Otherwise it goes on to 7 to 10
/// hours in all, and heals a share of the hit points and of the mana: twice as much fed on a bedroll,
/// nothing at all when starving. The hunger and fatigue of the remake run on their own scales; the
/// original's food above 64 is hunger below 191 here, and a fatigue term that the original fills in 48
/// minutes awake fills here in 50.
/// </remarks>
public static class Sleeping
{
    // The ways in, as the original's argument to 0x81f8f: a bedroll, none, and the forced sleep.
    private const int OnBedroll = 1;
    private const int WithoutBedroll = 0;
    private const int Forced = -2;

    /// <summary>Sleeps on a bedroll that was used, if the player can sleep here and says yes.</summary>
    public static void TryOnBedroll()
    {
        AskThenSleep(OnBedroll);
    }

    /// <summary>
    /// The sleep key, F10 or the D-pad held left: on a bedroll if there is one anywhere in the pack,
    /// containers included, and otherwise on the bare floor (UW.EXE 0x82345).
    /// </summary>
    public static void TryFromKey()
    {
        AskThenSleep(Inventory.Contains(EObjectType.Bedroll) ? OnBedroll : WithoutBedroll);
    }

    // The original lies down at once. Ours, the user's choice: it asks first, once it knows the player
    // can sleep here, so that a refusal is not preceded by a question.
    private static void AskThenSleep(int way)
    {
        if (Utils.CanSleepHere(notify: true))
        {
            RepairDialog.AskYesNo("Do you wish to go to sleep?", yes =>
            {
                if (yes)
                {
                    PlayerObject.Player.StartCoroutine(Sleep(way));
                }
            });
        }
    }

    /// <summary>
    /// The forced sleep after a drink too many: no refusal, never fed, no dream, and death where
    /// Utils.SafeToSleepOffDrink() says so.
    /// </summary>
    public static IEnumerator Collapse()
    {
        return Sleep(Forced);
    }

    private static IEnumerator Sleep(int way)
    {
        PlayerObject.DisableControls(EControlMask.Resting, true);

        if (way != Forced)
        {
            Messages.Add(1, 15); // you make camp

            // turn out the lights
            foreach (EInvSlot slot in LightSource.WornSlots)
            {
                LightSource lightSource = Inventory.sInv.invSlotContents[(int)slot] as LightSource;
                if (lightSource != null && lightSource.IsLit())
                {
                    lightSource.SetLit(false);
                    Messages.Add($"You extinguish your {lightSource.singularName}.");
                }
            }
        }

        PlayerObject.Player.fadeIn = false;

        while (PlayerObject.Player.fade < 0.5f)
        {
            PlayerObject.Player.fade += Time.unscaledDeltaTime;
            yield return null;
        }

        if (way != Forced)
        {
            Messages.Add(1, 16); // you go to sleep
        }

        // The original plays its own track while you sleep.
        Music.Sleep();

        while (PlayerObject.Player.fade < 1.0f)
        {
            PlayerObject.Player.fade += Time.unscaledDeltaTime;
            yield return null;
        }

        PlayerObject.Player.fade = 1.0f;

        if (way != Forced)
        {
            // determine whether to play a dream
            int dreamIndex = PlayerData.GetDreamIndex();
            if (dreamIndex >= 0)
            {
                PlayerData.sData.MarkDreamDreamt(dreamIndex);
                CutscenePlayer dream = Object.Instantiate(GetDreams()[dreamIndex]);
                while (dream != null)
                {
                    yield return null;
                }
            }
            else
            {
                yield return new WaitForSeconds(2.0f);
            }
        }
        else
        {
            PlayerData.sData.passedOutDrunk = true;
            yield return new WaitForSeconds(1.0f);

            if (!Utils.SafeToSleepOffDrink() && !Cheats.sCheats.invincible)
            {
                // Passed out where something kills him in his sleep: go into the death routine, sober,
                // as Kweepa's had it, or the drunken blur outlives the death and the rebirth.
                PlayerData.sData.drunkenness = 0;
                PlayerObject.DisableControls(EControlMask.Resting, false);
                PlayerObject.Player.Damage(Skills.ESkillTestResult.Success, PlayerData.sData.hp + 1, EDamageType.Direct);
                yield break;
            }
        }

        // Every sleep, interrupted or not: the doors, the spells, the hallucination, the first hours and
        // the poison.
        Door.CloseDoorsInSleep();
        Magic.sMagic.StopAllSpells();
        PlayerObject.Player.GetComponent<PlayerEffectsController>()?.EndMushroomTrip();

        int hours = Random.Range(2, 7);
        PlayerData.sData.gameTime += hours * 60 * 60;
        // Ours: the repopulation makes up the hours as if the player had stayed awake where he lies,
        // before a creature may come and wake him.
        Repopulation.Update();

        PlayerObject.Player.PayPoisonInSleep();
        if (way == Forced)
        {
            ForcedSleepHazards();
        }

        if (PlayerData.sData.dead)
        {
            // the death takes over from here
            PlayerObject.DisableControls(EControlMask.Resting, false);
            yield break;
        }

        string waking;
        if (Critter.AmbushSleeper())
        {
            // Woken by a creature: some rest, no healing. The original takes 32 of its 255 fatigue,
            // a level and a half of its 23-point levels, which is 7 of the remake's 5-point ones.
            PlayerData.sData.fatigue = Mathf.Max(PlayerData.sData.fatigue - 7, 0);
            PlayerData.sData.hunger = Mathf.Min(PlayerData.sData.hunger + Random.Range(12, 28), 255);
            PlayerData.sData.drunkenness = Mathf.Max(PlayerData.sData.drunkenness - 16, 0);
            waking = StringLoader.GetString(1, 21); // your sleep is interrupted
        }
        else
        {
            // A full sleep. The original also puts every creature of the level back here, as at a
            // level exit (fix 126, not yet in the remake), deletes the creatures the repopulation
            // made and fires every respawn trap at any distance; here the repopulation makes up the
            // hours instead (Repopulation).
            int moreHours = Random.Range(7, 11) - hours;
            if (PlayerData.sData.hp < 10)
            {
                moreHours += Random.Range(1, 3);
            }
            PlayerData.sData.gameTime += moreHours * 60 * 60;
            hours += moreHours;
            Repopulation.Update();

            bool fed = PlayerData.sData.hunger < 191 && way == OnBedroll;
            int full = fed ? 1 : 0;

            // The original's fatigue goes up one every 8 minutes and this term is full after six
            // steps; the remake's goes up one every 5 minutes, so it is full after ten.
            int cure = Mathf.Min(PlayerData.sData.fatigue * 5 / 16 + 2, 5);
            PlayerData.sData.fatigue = 0;

            if (PlayerData.sData.hunger < 255)
            {
                PlayerObject.Player.RestoreHealth(Sixteenths(cure * full + cure - 1, PlayerData.sData.vitality));
                RestoreMana(6);
                RestoreMana(Sixteenths((cure + 1) * full + cure - 1, PlayerData.sData.maxMana));
            }
            else
            {
                Messages.Add(1, 17); // you are starving
                PlayerObject.Player.Damage(Skills.ESkillTestResult.Success, 2, EDamageType.Direct);
            }

            PlayerData.sData.hunger = Mathf.Min(PlayerData.sData.hunger + Random.Range(24, 56), 255);
            PlayerData.sData.drunkenness = Mathf.Max(PlayerData.sData.drunkenness - 32, 0);
            waking = StringLoader.GetString(1, fed ? 18 : 19); // you feel rested, your sleep is uneasy
        }

        if (PlayerData.sData.dead)
        {
            PlayerObject.DisableControls(EControlMask.Resting, false);
            yield break;
        }

        // The original says only how the sleep went. Ours: how long it was, and how the player is now.
        Messages.Add($"{waking.TrimEnd()} You slept {hours} hours; you are {FatigueWord()} and {HungerWord()}.");

        if (way == Forced)
        {
            Messages.Add(1, 243);
        }

        while (PlayerObject.Player.fade > 0.0f)
        {
            PlayerObject.Player.fade -= Time.unscaledDeltaTime;
            yield return null;
        }
        PlayerObject.Player.fade = 0.0f;

        PlayerObject.DisableControls(EControlMask.Resting, false);

        // The sleep music runs on a little rather than stopping the instant the eyes open. With
        // no dream to play the whole sleep is two seconds, so cutting it on the last frame left
        // barely a bar of it. Control is already back, so this waits without holding anything up.
        yield return new WaitForSeconds(2.0f);
        Music.ResumeExploringMusic();
    }

    /// <summary>
    /// What the original's restores give for a positive argument (UW.EXE 0x346c2, 0x34656): one point,
    /// plus n to n + 3 sixteenths of the maximum.
    /// </summary>
    private static int Sixteenths(int n, int max)
    {
        return 1 + (((n + Random.Range(0, 4)) * max) >> 4);
    }

    private static void RestoreMana(int mana)
    {
        PlayerData.sData.mana = Mathf.Min(PlayerData.sData.mana + mana, PlayerData.sData.maxMana);
    }

    /// <summary>
    /// The forced sleep's own dangers (UW.EXE 0x81eea): lava kills, and water, unless the player is
    /// held above it, does 12 to 62.
    /// </summary>
    private static void ForcedSleepHazards()
    {
        PlayerObject player = PlayerObject.Player;
        if (player.TouchingLava)
        {
            player.Damage(Skills.ESkillTestResult.Success, 255, EDamageType.Lava);
            return;
        }

        if (player.isInWater
            && !Magic.sMagic.IsSpellActive(Magic.ESpell.SlowFall)
            && !Magic.sMagic.IsSpellActive(Magic.ESpell.Levitate)
            && !Magic.sMagic.IsSpellActive(Magic.ESpell.Fly))
        {
            player.Damage(Skills.ESkillTestResult.Success, Random.Range(0, 6) * 10 + 12, EDamageType.Drowning, Resistance.Poison);
        }
    }

    // The dream cutscenes hang on the bedroll's prefab, which F10 may have to do without.
    private static CutscenePlayer[] GetDreams()
    {
        return LevelLoader.sLevelLoader.objectModelMap.objects[(int)EObjectType.Bedroll].GetComponent<Bedroll>().dreams;
    }

    // The status words of the stats panel, block 1: 113 fatigued to 118 wide awake (StatsPanel).
    private static string FatigueWord()
    {
        return StringLoader.GetString(1, 113 + Mathf.Min((30 - PlayerData.sData.fatigue) / 5, 5));
    }

    // And 104 starving to 112 satiated.
    private static string HungerWord()
    {
        return StringLoader.GetString(1, 104 + (255 - PlayerData.sData.hunger) / 30);
    }
}
