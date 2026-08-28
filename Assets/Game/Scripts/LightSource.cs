using UnityEngine;

[System.Serializable]
public class LightSourceSaveData : UUObjectSaveData
{
    public bool lit;
    public int qualityDropCountdown;
    public int extinguishCountdown;
}

public class LightSource : UUObject
{
    public AudioClip ignitionSound;
    public AudioClip extinguishSound;

    private bool lit;

    private Texture2D[] animated;

    private int qualityDropCountdown;
    private int extinguishCountdown;

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);

        qualityDropCountdown = GetDuration();

        // identify immediately (you are the Avatar...?)
        if (type == EObjectType.Taper)
        {
            loreResult = Skills.ESkillTestResult.CriticalSuccess;
        }
    }

    private int GetDuration()
    {
        // going to guess that the duration is multiples of 5 minutes
        // but have to take into account quality too.
        // each quality step is 300 * duration / 64 seconds, or 5 * duration seconds
        // so:
        // lantern duration 10 -> 10 * 64 * 5 = 50 minutes
        // torch duration 3 -> 3 * 40 * 5 = 10 minutes
        
        // but, it's actually multiples of 20 minutes, so 3 = 1hr

        // select the lit versions with (4 +)
        return DataLoader.sDataLoader.objectsData.lightSourceStats[4 + ((int)type & 3)].duration;
    }

    private bool HasNakedFlame()
    {
        switch (type)
        {
        case EObjectType.Candle:
        case EObjectType.Torch:
        case EObjectType.Taper:
            return true;
        default:
            return false;
        }
    }

    protected override int GetQualityOffset()
    {
        return HasNakedFlame() ? 60 : 66;
    }
    
    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);
        
        if (data is LightSourceSaveData lightData)
        {
            lightData.lit = lit;
            lightData.qualityDropCountdown = qualityDropCountdown;
            lightData.extinguishCountdown = extinguishCountdown;
        }
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        
        if (data is LightSourceSaveData lightData)
        {
            lit = lightData.lit;
            qualityDropCountdown = lightData.qualityDropCountdown;
            extinguishCountdown = lightData.extinguishCountdown;
        }
    }

    public override ObjectSaveData SaveToData()
    {
        LightSourceSaveData data = new LightSourceSaveData();
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
        
        LightSourceSaveData data = JsonUtility.FromJson<LightSourceSaveData>(objData.jsonData);
        RestoreFromSaveData(data);
    }

    public override string GetLookText()
    {
        return IsLit() ? "Snuff" : "Light";
    }

    public override string GetUseText()
    {
        return "Equip";
    }

    public override void TryInventoryUse()
    {
        TryPreviewLightInInventory();
    }

    /// <summary>Preview lighting while item is in the grid / paperdoll (not equipped on shoulders).</summary>
    private void TryPreviewLightInInventory()
    {
        if (quality > 0 && quantity == 1)
        {
            SetLit(true);
            if (Inventory.sInv.invSlotContents[(int)EInvSlot.LeftShoulder] != this
                && Inventory.sInv.invSlotContents[(int)EInvSlot.RightShoulder] != this)
            {
                extinguishCountdown = 3; // allow it to burn for 15 seconds so we can cook popcorn
                Inventory.sInv.litLights.Add(this);
            }
        }
        //Messages.Add(1, 123); // can only be lit if equipped
    }

    public override void TryInventorySecondaryUse()
    {
        TryPreviewLightInInventory();
    }

    /// <summary>Mouse LMB on paperdoll: toggle lit; does not unequip (drag to remove).</summary>
    public void ToggleLitPaperdollClick()
    {
        if (IsLit())
        {
            SetLit(false);
            return;
        }

        TryInventorySecondaryUse();
    }

    public override bool Throw()
    {
        if (IsLit())
        {
            SetLit(false);
        }
        return base.Throw();
    }

    public void SetLit(bool _lit)
    {
        lit = _lit;
        if (lit)
        {
            Utils.PlayClip2d(ignitionSound);
        }
        else
        {
            Inventory.sInv.litLights.Remove(this);
            extinguishCountdown = 0;
            Utils.PlayClip2d(extinguishSound);
        }
    }

    public bool IsLit()
    {
        return lit;
    }

    public override void GetLightProps(ref float lightRange, ref float lightIntensity, ref float lightFlicker)
    {
        float range = 0.0f;
        float intensity = 0.0f;
        if (lit)
        {
            switch (type)
            {
            case EObjectType.Lantern:
                range = 25.0f;
                intensity = 3.0f;
                lightFlicker = 0.97f;
                break;
            case EObjectType.Torch:
                range = 20.0f;
                intensity = 2.0f;
                lightFlicker = 0.94f;
                break;
            case EObjectType.Candle:
                range = 15.0f;
                intensity = 2.0f;
                lightFlicker = 0.96f;
                break;
            case EObjectType.Taper:
                range = 20.0f;
                intensity = 2.0f;
                lightFlicker = 0.95f;
                break;
            }
            lightRange = Mathf.Max(range, lightRange);
            lightIntensity = Mathf.Max(intensity, lightIntensity);
        }
    }

    public override Texture2D GetInventoryTex()
    {
        if (lit)
        {
            Texture2D[] texs = null;
            switch (type)
            {
            case EObjectType.Lantern:
                return DataLoader.sDataLoader.objTex[148];
            case EObjectType.Torch:
                texs = DataLoader.sDataLoader.litTorchTex;
                break;
            case EObjectType.Candle:
                texs = DataLoader.sDataLoader.litCandleTex;
                break;
            case EObjectType.Taper:
                texs = DataLoader.sDataLoader.litTaperTex;
                break;
            }

            if (texs != null)
            {
                int cycle = (Time.frameCount / 16) % texs.Length;
                return texs[cycle];
            }
        }

        return base.GetInventoryTex();
    }

    public override EEquipAction Equip()
    {
        extinguishCountdown = 0;
        if (quality > 0)
        {
            SetLit(true);
            Inventory.sInv.litLights.Remove(this);
        }
        return EEquipAction.Equip;
    }

    public override void Unequip()
    {
        SetLit(false);
    }
    
    protected override string GetIdentifiedName(string baseName)
    {
        // don't identify the taper if in conversation with Zak and he has it
        if (type == EObjectType.Taper
            && (Conversations.runningConversation?.conversationIndex ?? 0) != (int)EWhoAmI.Zak
                || Inventory.Contains(EObjectType.Taper))
        {
            return StringLoader.GetString(1, 262);
        }

        return base.GetIdentifiedName(baseName);
    }

    public override int GetCombinationType()
    {
        if (IsLit())
        {
            return (int)type + 4;
        }

        return (int)type;
    }

    public static void DecayLights()
    {
        foreach (EInvSlot slot in new[] { EInvSlot.LeftShoulder, EInvSlot.RightShoulder })
        {
            LightSource lightSource = Inventory.sInv.invSlotContents[(int)slot] as LightSource;
            if (lightSource != null && lightSource.IsLit() && lightSource.GetDuration() > 0)
            {
                if (--lightSource.qualityDropCountdown <= 0)
                {
                    lightSource.qualityDropCountdown = lightSource.GetDuration();
                    if (--lightSource.quality == 0)
                    {
                        lightSource.SetLit(false);
                    }
                }
            }
        }

        for (int i = Inventory.sInv.litLights.Count - 1; i >= 0; --i)
        {
            LightSource lightSource = Inventory.sInv.litLights[i];
            if (--lightSource.extinguishCountdown <= 0)
            {
                lightSource.SetLit(false);
            }
        }
    }
}
