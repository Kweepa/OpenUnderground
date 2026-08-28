using UnityEngine;

public abstract class Lockable : UUObject
{
    public AudioClip unlockClip;
    public AudioClip tryClip;

    public bool Locked(out UUObject lockObj)
    {
        bool locked = false;
        lockObj = null;
        if (link > 0)
        {
            lockObj = LevelLoader.GetObj(link);
            if (lockObj != null)
            {
                if ((lockObj.flags & (1u << 0)) != 0)
                {
                    locked = true;
                }
            }
        }

        return locked;
    }

    public void Unlock()
    {
        if (Locked(out UUObject lockObj))
        {
            lockObj.flags &= ~(1 << 0);
            lockObj.TryChainInteraction(EAction.Unlock);
            Utils.PlayClip(unlockClip, transform.position);
            
            // find the chained trigger (it looks different, but this is how locks work)
            Trigger trigger = LevelLoader.GetObj(lockObj.chainIndex) as Trigger;
            if (trigger != null)
            {
                trigger.TryInteract(null, null, EAction.Trigger);
            }
        }
    }

    /// <summary>Clears the locked flag without unlock audio or chain reactions.</summary>
    public void ClearLockedFlag()
    {
        if (Locked(out UUObject lockObj))
        {
            lockObj.flags &= ~(1 << 0);
        }
    }
    
    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        bool isLocked = Locked(out UUObject lockObj);
        if (isLocked)
        {
            bool unlocked = false;
            Key key = originator as Key;
            if (key != null)
            {
                if (key.type == EObjectType.Lockpick)
                {
                    TryUseLockpick(key, lockObj);
                }
                else if (TryUnlock(key, GetKeyholePos()))
                {
                    clipToPlay = unlockClip;
                }
                else
                {
                    clipToPlay = tryClip;
                    Messages.Add(1, 2);
                }
            }
            else
            {
                if (PlayerInput.UseKeyAutomatically)
                {
                    foreach (UUObject invItem in Inventory.GetAllItems())
                    {
                        if (TryUnlock(invItem, GetKeyholePos()))
                        {
                            clipToPlay = unlockClip;
                            unlocked = true;
                            break;
                        }
                    }
                }
                if (!unlocked)
                {
                    if (Cheats.sCheats.giveAllKeys)
                    {
                        clipToPlay = unlockClip;
                        Unlock();
                        Messages.Add("You finagle the lock open.");
                    }
                    else
                    {
                        clipToPlay = tryClip;
                        Messages.Add($"The {singularName} is locked.");
                    }
                }
            }
        }
        else
        {
            if (originator is Key key)
            {
                if (key.type == EObjectType.Lockpick)
                {
                    if (lockObj != null)
                    {
                        Messages.Add(1, 6); // that is already open
                    }
                    else
                    {
                        Messages.Add(1, 3); // there is no lock on that
                    }
                    return;
                }

                if (TryLock(key, GetKeyholePos()))
                {
                    return;
                }

                if (lockObj != null && key.getClass == EClass.Keys)
                {
                    if (!KeyMatchesLock(key, lockObj))
                    {
                        clipToPlay = tryClip;
                        Messages.Add(1, 2);
                    }
                    else
                    {
                        Messages.Add(1, 6); // that is already open
                    }
                }
                else
                {
                    Messages.Add(1, 3); // there is no lock on that
                }
            }
            else
            {
                LockableOpen();
            }
        }
    }

    public abstract void LockableOpen();
    protected abstract Vector3 GetKeyholePos();

    private static bool CanLockWithKey(Door door)
    {
        return door != null && !door.isOpen;
    }

    public static bool KeyMatchesLock(UUObject key, UUObject lockObj)
    {
        if (key == null || lockObj == null || key.getClass != EClass.Keys)
        {
            return false;
        }

        int lockId = lockObj.special & 63;
        if (lockId == 0)
        {
            return false;
        }

        int keyId = key.ownerIndex & 63;
        return keyId == lockId;
    }

    /// <summary>Inventory Y hint: Pick / Unlock / Lock when aiming at a lockable (attempt allowed even if key does not fit or there is no lock).</summary>
    public bool TryGetKeyUseHint(UUObject key, out string hint)
    {
        hint = null;
        if (key == null || key.getClass != EClass.Keys)
        {
            return false;
        }

        if (key.type == EObjectType.Lockpick)
        {
            hint = "Pick";
            return true;
        }

        if (Locked(out _))
        {
            hint = "Unlock";
            return true;
        }

        if (this is Door door && CanLockWithKey(door))
        {
            hint = "Lock";
            return true;
        }

        return false;
    }

    protected bool TryLock(UUObject key, Vector3 keyholePos)
    {
        if (this is not Door door || !CanLockWithKey(door))
        {
            return false;
        }

        if (Locked(out _))
        {
            return false;
        }

        Locked(out UUObject lockObj);
        if (!KeyMatchesLock(key, lockObj))
        {
            return false;
        }

        lockObj.flags |= 1 << 0;
        Utils.PlayClip(unlockClip, transform.position);
        Messages.Add(1, 4); // the key locks the lock
        unlocker = key;
        unlockTime = 0.0f;
        this.keyholePos = keyholePos;
        return true;
    }

    protected bool TryUnlock(UUObject key, Vector3 _keyholePos)
    {
        bool unlocked = false;

        if (key.getClass == EClass.Keys)
        {
            if (Locked(out UUObject lockObj) && KeyMatchesLock(key, lockObj))
            {
                Unlock();
                unlocked = true;
                Messages.Add(1, 5); // the key unlocks the lock
                unlocker = key;
                unlockTime = 0.0f;
                keyholePos = _keyholePos;
            }
        }

        return unlocked;
    }

    protected AudioClip clipToPlay;

    protected UUObject unlocker;
    protected float unlockTime;
    private Vector3 keyholePos;

    protected void TryUseLockpick(Key lockpick, UUObject lockObj)
    {
        // try to pick lock. borrowed logic from Hank once again :)
        Skills.ESkillTestResult result = Skills.GetResult(1 + Skills.GetSkill(ESkill.Picklock), 3 * lockObj.z);

        switch (result)
        {
        case Skills.ESkillTestResult.CriticalFailure:
            if (Skills.GetResult(PlayerData.sData.dexterity, 20) < Skills.ESkillTestResult.Success)
            {
                Messages.Add("The pick broke.");
                Utils.PlayClip(lockpick.lockpickBreak, transform.position);
                if (lockpick.quantity > 1)
                {
                    --lockpick.quantity;
                }
                else
                {
                    Utils.DestroyItem(lockpick);
                }
            }
            else
            {
                Messages.Add(1, 120);
                Utils.PlayClip(lockpick.lockpickFail, transform.position);
            }
            break;
        case Skills.ESkillTestResult.Failure:
            Messages.Add(1, 120);
            Utils.PlayClip(lockpick.lockpickFail, transform.position);
            break;
        case Skills.ESkillTestResult.Success:
        case Skills.ESkillTestResult.CriticalSuccess:
            Unlock();
            Messages.Add(1, 121);
            break;
        }
    }
    
    public override void Update()
    {
        base.Update();

        if (Time.deltaTime > 0.0f)
        {
            if (clipToPlay != null)
            {
                Utils.PlayClip(clipToPlay, transform.position, 2.0f);
                clipToPlay = null;
            }
            
            if (unlocker)
            {
                unlockTime += Time.deltaTime;
                if (unlockTime > 2.0f)
                {
                    unlocker = null;
                }
            }
        }
    }

    

    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.KeyFloater;

        if (unlocker != null)
        {
            Camera guiCam = PlayerObject.Player != null ? PlayerObject.Player.mainCamera : null;
            if (guiCam == null)
            {
                return;
            }

            DataLoader dl = DataLoader.sDataLoader;
            Texture2D tex = null;
            if (dl != null && dl.objTex != null)
            {
                int ti = (int)unlocker.type;
                if (ti >= 0 && ti < dl.objTex.Length)
                {
                    tex = dl.objTex[ti];
                }
            }

            if (tex == null)
            {
                return;
            }

            Vector3 screenPoint =
                guiCam.WorldToScreenPoint(keyholePos + 0.05f * unlockTime * Vector3.up);
            if (screenPoint.z > 0.0f)
            {
                float w = 64;
                float h = 64 * 1.2f;
                GUI.DrawTexture(new Rect(screenPoint.x - w / 2, guiCam.pixelHeight - screenPoint.y - h / 2, w, h), tex);
            }
        }
    }
}
