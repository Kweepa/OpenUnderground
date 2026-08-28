using UnityEngine;
using System.Collections;

public enum TwilightZoneType
{
    SkullBat,
    Teeth,
    Spiral,
    DevilPanther,
    Lightning,
    Fish,
    Eyeball,
    Skull
}

public class TwilightZone : Critter
{
    private TwilightZoneType GetTwilightZoneType()
    {
        // these rules seem odd, but they work...
        int[] slots = { 32, 40, 80, 128 };
        int slot = slots[(int)whoami & 3];
        if (crit.slots[slot, 0] == null)
        {
            slot = slots[(int)whoami & 2];
        }

        switch (type)
        {
        case EObjectType.TwilightZoneA:
            if (slot == 32) return TwilightZoneType.SkullBat;
            if (slot == 40) return TwilightZoneType.Teeth;
            if (slot == 80) return TwilightZoneType.Spiral;
            if (slot == 128) return TwilightZoneType.DevilPanther;
            break;            
        case EObjectType.TwilightZoneB:
            if (slot == 32) return TwilightZoneType.Lightning;
            if (slot == 80) return TwilightZoneType.Fish;
            break;
        case EObjectType.TwilightZoneC:
            if (slot == 32) return TwilightZoneType.Eyeball;
            if (slot == 80) return TwilightZoneType.Skull;
            break;
        }
        return TwilightZoneType.SkullBat;
    }

    [EnumNamedArray(typeof(TwilightZoneType))]
    public GameObject[] twilightZonePrefabs;

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);

        TwilightZoneType twilightZoneType = GetTwilightZoneType();

        if (twilightZonePrefabs != null && (int)twilightZoneType < twilightZonePrefabs.Length)
        {
            GameObject prefab = twilightZonePrefabs[(int)twilightZoneType];
            if (prefab != null)
            {
                GameObject obj = Instantiate(prefab, transform.position, transform.rotation);
                StartCoroutine(DestroyAndCleanup(obj));
            }
        }
    }

    IEnumerator DestroyAndCleanup(GameObject obj)
    {
        yield return null;
        LevelObject o = obj.GetComponent<LevelObject>();
        if (o != null)
        {
            LevelLoader.worldObj.AddFirst(o);
        }
        else
        {
            Debug.Log($"{name} doesn't have a level object");
        }
        Utils.DestroyCritter(this);
    }
}
