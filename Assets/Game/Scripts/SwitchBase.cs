using UnityEngine;
using UnityEngine.Rendering;

[System.Serializable]
public class SwitchBaseSaveData : UUObjectSaveData
{
    public int textureIndex;
}

public class SwitchBase : UUObject
{
    public AudioClip switchClip;
    public Transform switchChild;

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);

        textureIndex = (int)type - 0x170;
        if (switchChild == null)
        {
            Shader spriteShader = Shader.Find("Transparent/Diffuse");
            Material objMaterial = new Material(spriteShader) { mainTexture = DataLoader.sDataLoader.tmFlatTex[textureIndex] };
            cachedRenderer.material = objMaterial;
            cachedRenderer.lightProbeUsage = LightProbeUsage.Off;
        }
        else
        {
            SetSwitchChildPosition();
        }
    }

    protected virtual void SetSwitchChildPosition()
    {
        Vector3 pos = switchChild.localPosition;
        pos.z = (textureIndex & 8) > 0 ? 0.1f : 0.0f;
        switchChild.SetLocalPositionAndRotation(pos, Quaternion.identity);
    }

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        // Call base to load names and other UUObject initialization
        base.PostLoadInitialize(restoredFromSave);
        
        // Only apply position adjustments when created from ark data, not when loaded from save
        if (!restoredFromSave)
        {
            Vector3 offset = Vector3.zero;

            // move first pull chain over so when you activate it, you can see the door opening
            if (LevelLoader.sLevelLoader.loadedLevel == 1 && initialTile.x == 32 && initialTile.y == 7)
            {
                offset = Vector3.forward;
            }
            else
            {
                // keep away from wall edges (eg emerald puzzle, level 6)
                switch (angle)
                {
                case 0:
                case 4:
                    if (x == 0)
                    {
                        offset = Vector3.right;
                    }
                    else if (x == 7)
                    {
                        offset = Vector3.left;
                    }
                    break;
                case 2:
                case 6:
                    if (y == 0)
                    {
                        offset = Vector3.forward;
                    }
                    else if (y == 7)
                    {
                        offset = Vector3.back;
                    }
                    break;
                }
                offset *= 0.2f;
            }
            transform.SetPositionAndRotation(transform.position + offset, transform.rotation);
        }
        else
        {
            // When restoring from save, update the visual state based on saved textureIndex
            if (switchChild == null)
            {
                if (cachedRenderer != null && cachedRenderer.material != null)
                {
                    cachedRenderer.material.mainTexture = DataLoader.sDataLoader.tmFlatTex[textureIndex];
                }
            }
            else
            {
                SetSwitchChildPosition();
            }
        }

        SnapToWallUsingTileBoundaries();
    }

    protected int textureIndex;

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        if (sender != null && sender.type == EObjectType.Pole)
        {
            // using the pole you trigger the switch
            Messages.Add(1, 157);
        }

        if (sender == null || sender.type == EObjectType.Pole)
        {
            UUObject obj = LevelLoader.GetObj(link);
            if (obj != null)
            {
                obj.TryInteract(originator, this, EAction.Use);
            }
            else if (link != 0)
            {
                Debug.Log($"Switch '{name}' on level {LevelLoader.sLevelLoader.loadedLevel} refers to non-existent object {link}");
            }

            textureIndex ^= 8;
            if (switchChild == null)
            {
                cachedRenderer.material.mainTexture = DataLoader.sDataLoader.tmFlatTex[textureIndex];
            }
            else
            {
                SetSwitchChildPosition();
            }

            Utils.PlayClip(switchClip, transform.position);
        }
    }
    
    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);
        
        if (data is SwitchBaseSaveData switchData)
        {
            switchData.textureIndex = textureIndex;
        }
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);
        
        if (data is SwitchBaseSaveData switchData)
        {
            textureIndex = switchData.textureIndex;
        }
    }

    public override ObjectSaveData SaveToData()
    {
        SwitchBaseSaveData data = new SwitchBaseSaveData();
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
        
        SwitchBaseSaveData data = JsonUtility.FromJson<SwitchBaseSaveData>(objData.jsonData);
        RestoreFromSaveData(data);
    }
}
