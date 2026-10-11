using UnityEngine;

/// <summary>
/// What the Cheats component puts into the world besides objects: a creature of any type at a chosen
/// distance in front of the player, hostile or not, built as Summon Monster builds one but from the
/// type's own stats, so it needs no creature of that type met before.
/// </summary>
public static class CheatsKit
{
    public static void SpawnCritter(EObjectType type, float distance, bool hostile)
    {
        if ((int)type < 64 || (int)type >= 128)
        {
            Debug.LogWarning($"[CHEAT] {type} is not a creature");
            return;
        }

        Critter.EAttitude attitude = hostile ? Critter.EAttitude.Hostile : Critter.EAttitude.Mellow;
        ObjectsData.CritterData stats = Critter.getStats(type);
        byte[] critterData = new byte[19];
        critterData[0] = (byte)Mathf.Clamp(stats.AvgHit, 1, 255);   // hit points
        critterData[3] = (byte)Critter.EGoal.Wander2;
        critterData[5] = 1;                                          // level
        critterData[6] = (byte)((int)attitude << 6);
        ushort[] objData = { (ushort)((int)type | (1 << 15)), 0, 40, 0 };
        Critter critter = LevelLoader.sLevelLoader.CreateObjectOfType(objData, critterData) as Critter;
        if (critter == null)
        {
            Debug.LogWarning($"[CHEAT] {type} could not be made");
            return;
        }

        Vector3 pos = Place(critter, distance);
        critter.x = Tile.GetSubTileX(pos.x);
        critter.y = Tile.GetSubTileY(pos.z);
        LevelLoader.AddToWorld(critter);
        critter.WorldInitialize();
        critter.PostLoadInitialize();
        if (critter.movementType == Critter.EMovementType.Flying)
        {
            pos.y += 2.0f;
        }
        critter.xhome = Tile.GetTileX(pos.x);
        critter.yhome = Tile.GetTileY(pos.z);
        critter.transform.position = pos;
        critter.attitude = attitude;
        Debug.Log($"[CHEAT] {type} at tile {critter.xhome},{critter.yhome}, {(hostile ? "hostile" : "mellow")}");
    }

    private static Vector3 Place(UUObject obj, float distance)
    {
        // that far ahead, on the floor there, as Summon Monster finds it
        Transform player = PlayerObject.Player.transform;
        Vector3 pos = player.position + Vector3.up + Mathf.Max(0.5f, distance) * player.forward;
        if (Physics.SphereCast(pos, 0.2f, Vector3.down, out RaycastHit hit, 4.0f, LayerMasks.EnvironmentOnly))
        {
            pos += hit.distance * Vector3.down;
        }
        obj.transform.position = pos;
        return pos;
    }
}
