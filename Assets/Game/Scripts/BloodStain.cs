using UnityEngine;

public class BloodStain : UUObject
{
    private const float FloorAlignRayDistance = 3.0f;

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);

        transform.rotation = Quaternion.AngleAxis(360.0f * Random.value, Vector3.up) * transform.rotation;
    }
    
    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);

        if (cachedRenderer != null)
        {
            cachedRenderer.material = LevelLoader.sLevelLoader.GetObjectMaterial(type);
        }
    }

    private static void AlignTransformToFloorBelow(Transform t)
    {
        if (Physics.Raycast(t.position + Vector3.up, Vector3.down, out RaycastHit r, FloorAlignRayDistance,
                LayerMasks.EnvironmentOnly))
        {
            t.position = r.point;
            t.rotation = Quaternion.FromToRotation(t.up, r.normal) * t.rotation;
        }
    }

    /// <summary>
    /// Raycast down from above worldPosition; returns false if nothing solid within range (avoids mid-air stains).
    /// </summary>
    private static bool TryProbeFloorBelow(Vector3 worldPosition, out RaycastHit hit)
    {
        return Physics.Raycast(worldPosition + Vector3.up, Vector3.down, out hit, FloorAlignRayDistance,
            LayerMasks.EnvironmentOnly);
    }

    /// <summary>
    /// Uses the tile under the hit point (floor texture / terrain mapping) — no blood on water or lava.
    /// </summary>
    private static bool IsFloorTerrainOkForBloodstain(RaycastHit floorHit)
    {
        Tile tile = LevelLoader.GetTile(floorHit.point);
        if (tile == null)
        {
            return false;
        }

        ETerrainType terrain = tile.GetFloorTerrain();
        return terrain != ETerrainType.Water && terrain != ETerrainType.Lava;
    }

    /// <summary>
    /// Raycast down from above the current position and snap to the floor with up aligned to hit normal.
    /// </summary>
    public void AlignDecalToFloorBelow()
    {
        AlignTransformToFloorBelow(transform);
    }

    public override void WorldInitialize(Tile t, int tx, int ty)
    {
        base.WorldInitialize(t, tx, ty);
        AlignDecalToFloorBelow();
    }

    /// <summary>
    /// Spawns a bloodstain for a dead critter when its splat type is red or green blood.
    /// </summary>
    public static void TrySpawnDeathStain(Vector3 worldPosition, int remains, int blood)
    {
        if (LevelLoader.sLevelLoader == null)
        {
            return;
        }

        EParticleType pt = Utils.MapRemainsToParticleType(remains, blood);
        EObjectType? stainType = pt switch
        {
            EParticleType.BloodSplat => EObjectType.BloodStainB,
            EParticleType.PoisonSplat => EObjectType.BloodStainA,
            _ => null
        };

        if (stainType == null)
        {
            return;
        }

        if (!TryProbeFloorBelow(worldPosition, out RaycastHit floorProbe)
            || !IsFloorTerrainOkForBloodstain(floorProbe))
        {
            return;
        }

        UUObject stain = LevelLoader.CreateObjectOfType(stainType.Value);
        if (stain == null)
        {
            return;
        }

        int loadedLevel = LevelLoader.sLevelLoader.loadedLevel;
        // Decals are worldObj-only — not lev.ark objects[] entries (objectIndex 0; CreateObjectOfType sets originalLevel 0).
        stain.objectIndex = 0;
        stain.levelIndex = loadedLevel;
        stain.used = true;
        stain.transform.position = worldPosition;
        stain.PostLoadInitialize();
        stain.transform.position = floorProbe.point;
        stain.transform.rotation = Quaternion.FromToRotation(stain.transform.up, floorProbe.normal) * stain.transform.rotation;
        LevelLoader.AddToWorld(stain);
    }

    public override string GetUnderCursorName()
    {
        return null;
    }
}
