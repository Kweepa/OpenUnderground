using UnityEngine;

[System.Serializable]
public class LevelObjectSaveData
{
    public int levelIndex;
    public int objectIndex;
    public int originalLevel; // Level where objectIndex was originally defined (0 = not set/runtime-created)
    public Vector3 position;
    public Quaternion rotation;
    public bool activeInLevel;
    public bool wasActive;
    public int initialTileX;
    public int initialTileY;
}

public class LevelObject : MonoBehaviour
{
    public Tile initialTile;
    public bool activeInLevel;
    public bool temporary; // if true, just delete this when changing level
    public int levelIndex; // Which dungeon level this object belongs to
    public int objectIndex; // the index into the level ark data; also used for referencing
    public int originalLevel; // Level where objectIndex was originally defined (0 = runtime-created)


    public virtual void PostLoadInitialize(bool restoredFromSave = false)
    {
        
    }
    
    protected virtual void PopulateSaveData(LevelObjectSaveData data)
    {
        data.levelIndex = levelIndex;
        data.objectIndex = objectIndex;
        data.originalLevel = originalLevel;
        data.position = transform.position;
        data.rotation = transform.rotation;
        data.activeInLevel = activeInLevel;
        data.wasActive = gameObject.activeSelf;
        
        if (initialTile != null)
        {
            data.initialTileX = initialTile.x;
            data.initialTileY = initialTile.y;
        }
        else
        {
            data.initialTileX = -1;
            data.initialTileY = -1;
        }
    }

    protected virtual void RestoreFromSaveData(LevelObjectSaveData data)
    {
        transform.position = data.position;
        transform.rotation = data.rotation;
        levelIndex = data.levelIndex;
        originalLevel = data.originalLevel;
        activeInLevel = data.activeInLevel;
        
        if (data.initialTileX >= 0 && data.initialTileY >= 0)
        {
            initialTile = LevelLoader.GetTile(levelIndex, data.initialTileX, data.initialTileY);
        }
        else
        {
            initialTile = LevelLoader.GetClosestTile(transform.position);
        }
        
        if (gameObject != null)
        {
            gameObject.SetActive(data.wasActive);
        }
    }

    public virtual ObjectSaveData SaveToData()
    {
        LevelObjectSaveData data = new LevelObjectSaveData();
        PopulateSaveData(data);
        
        return new ObjectSaveData
        {
            objectTypeName = GetType().Name,
            objectType = 0,  // LevelObject doesn't have objectType
            objectIndex = objectIndex,
            level = levelIndex,
            originalLevel = originalLevel,
            jsonData = JsonUtility.ToJson(data)
        };
    }

    public virtual void LoadFromData(ObjectSaveData objData)
    {
        if (objData == null || string.IsNullOrEmpty(objData.jsonData))
            return;
        
        LevelObjectSaveData data = JsonUtility.FromJson<LevelObjectSaveData>(objData.jsonData);
        RestoreFromSaveData(data);
    }
}
