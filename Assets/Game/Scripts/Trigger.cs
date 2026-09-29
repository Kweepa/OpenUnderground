using UnityEngine;

[System.Serializable]
public class TriggerSaveData : UUObjectSaveData
{
    public bool markedOnMap;
}

public class Trigger : UUObject
{
    private bool isNear;
    private bool markedOnMap = false;

    /// <summary>
    /// The Search skill a look trigger rolls with instead of the player's, or -1 for the player's.
    /// </summary>
    /// <remarks>
    /// Reveal sets it for the length of the spell. The original does the same thing in the routine
    /// Reveal calls for each object it covers (UW.EXE 0x34963): it saves the Search skill, sets it
    /// to 45 (0x349da), fires the object's look trigger and puts the skill back (0x34a03). At 45
    /// the roll succeeds for every difficulty up to 29, and a player never meets one above 10.
    /// </remarks>
    public static int searchSkillOverride = -1;

    /// <summary>
    /// True while a use of a decal sets its look chain off, so that the look trigger fires with no
    /// Search roll and no wait (Decal.TryInteract).
    /// </summary>
    /// <remarks>
    /// The remake's long press on a wall has always opened a hidden door at once, and it stays
    /// that way at the user's request: it is for a player who already knows where the door is,
    /// and asking them to click on until a roll passes only costs time. A look, a click, is what
    /// rolls. The original has no gesture of this kind: there a default-mode drag is a look, with
    /// its roll, and then a use (UW.EXE 0x26b8e), and a use sends event 4, which a look trigger
    /// does not answer.
    /// </remarks>
    public static bool skipSearchRoll;

    /// <summary>
    /// Seconds after a look rolls Search on this trigger before another look can roll it again.
    /// </summary>
    /// <remarks>
    /// Ours, not the original's, where a miss costs nothing and the next click rolls at once, so
    /// a few quick clicks found any door. A look inside the wait is a miss, with no message, and
    /// does not restart it. It is kept in memory only: after a reload the first look rolls.
    /// </remarks>
    public const float searchRollCooldown = 1.0f;

    // Time.time before which a look does not roll this trigger's Search.
    private float nextSearchRollTime;

    private void OnEnable()
    {
        // prevent being triggered if we start inside (e.g., teleporting to new level)
        isNear = true;
    }

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        if (type.ToString().Contains(action.ToString()) || action == EAction.Trigger)
        {
            // HACK for bad save data
            // turn back on the trigger that deletes the bridge hiding
            // (this can only be interacted if the bridge is not hidden)
            if (levelIndex == 6 && objectIndex == 822)
            {
                flags |= 4;
            }
            
            if ((flags & 4) == 0) // should it trigger?
            {
                return;
            }

            // Before the trigger turns itself off, so that a failed roll leaves it armed.
            if (!PassesSearchRoll(action))
            {
                return;
            }

            if ((flags & 2) == 0) // should it retrigger?
            {
                flags &= ~4; // turn off trigger
                if (type == EObjectType.PickupTrigger)
                {
                    SwitchOffTriggersSharingTrap();
                }
            }

            // sender needs to be "this", since the trigger is what is in the same tile as the door
            // I had changed this to pass along "sender" but I think that was a failed experiment
            TryChainInteraction(originator, this, action);
        }
    }

    /// <summary>
    /// Whether a look at the object this trigger hangs from passes the trigger's Search roll.
    /// </summary>
    /// <remarks>
    /// In the original, looking at an object whose look trigger has a nonzero z rolls
    /// GetResult(Search, z), and the trigger fires only on a success (UW.EXE 0x83c21-0x83c3d,
    /// inside the trigger chain 0x83b3b). The z field is the object's height, which means
    /// nothing for an invisible trigger, and the level designers used it on look triggers as the
    /// difficulty of that one hidden door: 3 to 10 on the seven that have one. Zero means no roll.
    ///
    /// A failure writes nothing and prints nothing, so the next look simply rolls again. A trigger
    /// reached through another trigger's chain arrives here as EAction.Trigger and does not roll,
    /// as in the original, where the trap dispatcher fires the rest of a chain with event -1 and
    /// the negative event skips the roll (0x84869).
    ///
    /// Two things are ours: after a roll the same trigger waits searchRollCooldown seconds before a
    /// look rolls it again, and Reveal does not wait and does not make anyone wait; and a use of the
    /// wall, the long press, fires with no roll at all (skipSearchRoll).
    /// </remarks>
    private bool PassesSearchRoll(EAction action)
    {
        if (type != EObjectType.LookTrigger || action != EAction.Look || z == 0 || skipSearchRoll)
        {
            return true;
        }

        if (searchSkillOverride >= 0)
        {
            return Skills.GetResult(searchSkillOverride, z) >= Skills.ESkillTestResult.Success;
        }

        if (Time.time < nextSearchRollTime)
        {
            return false;
        }

        nextSearchRollTime = Time.time + searchRollCooldown;
        return Skills.GetResult(Skills.GetSkill(ESkill.Search), z) >= Skills.ESkillTestResult.Success;
    }

    /// <summary>
    /// Switches off every other trigger of the level that sets off the same trap as this one.
    /// </summary>
    /// <remarks>
    /// In the original a one-shot trigger that fires hands its trap to 0x84983 (UW.EXE 0x83d3a),
    /// which walks the lists of every tile and of every object (0x848c2), unhooks each trigger
    /// that links to that trap, as many as the count the trap keeps in its flags, and then deletes
    /// the trap. So a trap that several one-shot triggers share fires once: three objects that
    /// each carry a pick-up trigger to the same create object trap make one creature between them,
    /// at the first one taken. The remake keeps a spent trigger and switches it off instead, so the
    /// others are switched off too.
    ///
    /// The original does this for every one-shot trigger; here it is done for pick-up triggers
    /// only, the user's decision of 28 September 2026, since the other shared traps of the game
    /// work today and changing them is still to be tried in play.
    /// </remarks>
    private void SwitchOffTriggersSharingTrap()
    {
        if (link == 0)
        {
            return;
        }

        foreach (UUObject obj in LevelLoader.GetLevel().objects)
        {
            if (obj is Trigger other && other != this && other.link == link)
            {
                other.flags &= ~4;
            }
        }
    }

    public override void Update()
    {
        if (type == EObjectType.MoveTrigger)
        {
            Vector3 off = PlayerObject.Player.GetFootPos() - transform.position;
            // half the size of the tile
            if (Mathf.Abs(off.x) < 1.5f && Mathf.Abs(off.z) < 1.5f && Mathf.Abs(off.y) < 2.0f)
            {
                if (!isNear)
                {
                    if ((flags & 4) == 0) // should it trigger?
                    {
                        return;
                    }

                    if ((flags & 2) == 0) // should it retrigger?
                    {
                        flags &= ~4; // turn off trigger
                    }

                    isNear = true;
                    TryChainInteraction(EAction.Trigger);
                    // also find change terrain trap in the same tile and trigger that
                    foreach (Trap trap in FindObjectsByType<Trap>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    {
                        if (trap.type == EObjectType.ChangeTerrainTrap
                            && trap.initialTile == this.initialTile
                            && trap.isLinked
                            && trap.link == 0)
                        {
                            trap.TryInteract(this, this, EAction.Trigger);
                        }
                    }
                }
            }
            else
            {
                isNear = false;
            }

            if (!markedOnMap)
            {
                MarkAsStairIfNeeded();
                markedOnMap = true;
            }
        }
    }
    
    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);
        
        if (data is TriggerSaveData triggerData)
        {
            triggerData.markedOnMap = markedOnMap;
        }
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        
        if (data is TriggerSaveData triggerData)
        {
            markedOnMap = triggerData.markedOnMap;
        }
    }

    public override ObjectSaveData SaveToData()
    {
        TriggerSaveData data = new TriggerSaveData();
        PopulateSaveData(data);
        
        return new ObjectSaveData
        {
            objectTypeName = GetType().Name,
            objectType = (int)type,
            objectIndex = objectIndex,
            level = levelIndex,
            jsonData = JsonUtility.ToJson(data)
        };
    }

    public override void LoadFromData(ObjectSaveData objData)
    {
        if (objData == null || string.IsNullOrEmpty(objData.jsonData))
            return;
        
        TriggerSaveData data = JsonUtility.FromJson<TriggerSaveData>(objData.jsonData);
        RestoreFromSaveData(data);
    }
    
    /// <summary>
    /// Marks the trigger's tile as a stairway if this is a MoveTrigger linked to a TeleportTrap on a different level.
    /// This is called both during normal gameplay (Update) and when restoring map data from save.
    /// </summary>
    public void MarkAsStairIfNeeded()
    {
        if (type == EObjectType.MoveTrigger && initialTile != null)
        {
            UUObject trap = LevelLoader.GetObj(link, levelIndex);
            if (trap != null && trap.type == EObjectType.TeleportTrap && trap.z != levelIndex)
            {
                initialTile.isStair = true;
            }
        }
    }
}
