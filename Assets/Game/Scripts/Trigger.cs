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

            if ((flags & 2) == 0) // should it retrigger?
            {
                flags &= ~4; // turn off trigger
            }

            // sender needs to be "this", since the trigger is what is in the same tile as the door
            // I had changed this to pass along "sender" but I think that was a failed experiment
            TryChainInteraction(originator, this, action);
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
