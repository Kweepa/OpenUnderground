using UnityEngine;

[System.Serializable]
public class SilverSeedSaveData : UUObjectSaveData
{
    public bool wasSleeping;
    public bool waitingForRoom;
}

public class SilverSeed : UUObject
{
    public AudioClip saplingGrow;

    private bool wasSleeping;

    // Settled on good ground, but the player stands where the tree would grow: it grows once he
    // steps away, instead of inside him.
    private bool waitingForRoom;

    // How far from the player's side the trunk needs, in metres.
    private const float treeClearance = 0.5f;

    public override string GetUseText()
    {
        return "Plant";
    }

    public override EEquipAction Equip()
    {
        Inventory.sInv.TryThrow(this);
        return EEquipAction.Consume;
    }

    private bool ShouldPlantSeed(Vector3 pos)
    {
        Tile t = LevelLoader.GetTile(pos);
        if (t.GetFloorTerrain() == ETerrainType.Dirt && t.type == 1) // flat dirt floor
        {
            // check we're close to the ground
            float floorY = t.GetFloorY(pos.x, pos.z);
            if (pos.y - 0.1f < floorY)
            {
                return true;
            }
        }
        Messages.Add(1, 10); // no space for roots

        return false;
    }
    
    private void PlantSeed(Vector3 pos)
    {
        // plant the seed
        UUObject sap = LevelLoader.CreateObjectOfType(EObjectType.SilverTree);
        sap.transform.position = pos;
        sap.PostLoadInitialize();
        sap.transform.root.gameObject.SetActive(true);
        
        Utils.PlayClipOccluded(saplingGrow, pos);

        ParticleSpawner.SpawnParticle(EParticleType.SilverSaplingPlant, pos);
        
        LevelLoader.worldObj.AddLast(sap);

        Utils.DestroyItem(this);

        Messages.Add(1, 12);

        PlayerData.sData.saplingPlanted = true;
        PlayerData.sData.saplingPlantedLevel = LevelLoader.sLevelLoader.loadedLevel;
        PlayerData.sData.saplingPlantedPosition = pos;
    }

    public override void Update()
    {
        base.Update();

        Rigidbody rb = gameObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            bool sleeping = rb.IsSleeping();
            if (!wasSleeping && sleeping)
            {
                // cast down to ground
                // TODO: check for being close to the floor (not on a bridge or another object)
                waitingForRoom = ShouldPlantSeed(rb.position);
            }
            else if (!sleeping)
            {
                waitingForRoom = false;
            }
            wasSleeping = sleeping;

            if (waitingForRoom && !IsPlayerInTheWay(rb.position))
            {
                waitingForRoom = false;
                PlantSeed(rb.position);
            }
        }
    }

    private static bool IsPlayerInTheWay(Vector3 pos)
    {
        PlayerObject player = PlayerObject.Player;
        CharacterController body = player != null ? player.cachedCharacterController : null;
        if (body == null)
        {
            return false;
        }

        Vector3 offset = pos - player.transform.position;
        float reach = body.radius + treeClearance;
        return Mathf.Abs(offset.y) < body.height && new Vector2(offset.x, offset.z).sqrMagnitude < reach * reach;
    }

    protected override void PopulateSaveData(LevelObjectSaveData data)
    {
        base.PopulateSaveData(data);

        if (data is SilverSeedSaveData seedData)
        {
            seedData.wasSleeping = wasSleeping;
            seedData.waitingForRoom = waitingForRoom;
        }
    }

    protected override void RestoreFromSaveData(LevelObjectSaveData data)
    {
        base.RestoreFromSaveData(data);

        if (data is SilverSeedSaveData seedData)
        {
            wasSleeping = seedData.wasSleeping;
            waitingForRoom = seedData.waitingForRoom;
        }
    }

    public override ObjectSaveData SaveToData()
    {
        SilverSeedSaveData data = new SilverSeedSaveData();
        PopulateSaveData(data);

        return new ObjectSaveData
        {
            objectName = name,
            objectTypeName = GetType().Name,
            objectType = (int)type,
            objectIndex = objectIndex,
            level = levelIndex,
            originalLevel = originalLevel,
            jsonData = JsonUtility.ToJson(data)
        };
    }

    public override void LoadFromData(ObjectSaveData objData)
    {
        if (objData == null || string.IsNullOrEmpty(objData.jsonData))
            return;

        SilverSeedSaveData data = JsonUtility.FromJson<SilverSeedSaveData>(objData.jsonData);
        if (data == null)
        {
            Debug.LogError($"Failed to deserialize JSON for silver seed objectIndex={objData.objectIndex}, jsonData={objData.jsonData}");
            return;
        }
        RestoreFromSaveData(data);
    }
}
