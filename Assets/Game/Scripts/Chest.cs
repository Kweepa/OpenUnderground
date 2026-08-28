using UnityEngine;
using System.Collections.Generic;
using Random = UnityEngine.Random;

public class Chest : Lockable
{
    public Transform lid;

    /// <summary>Played when the lock is struck so metal sparks do not replace the usual weapon hit feedback.</summary>
    public AudioClip hitSound;

    public Vector3[] lev1ContentsPos;
    public Vector3[] lev2ContentsPos;
    public Vector3[] lev4ContentsPos;
    
    private float openAmount;
    private bool shouldOpen;

    public override bool IsDamageable()
    {
        return Locked(out _) && quality < 63;
    }

    public override void TryDamage(int damage, Skills.ESkillTestResult result)
    {
        if (!Locked(out _))
        {
            return;
        }

        if (quality < 63)
        {
            int appliedDamage = damage >> qualityClass;

            if (ParticleSpawner.sParticleSpawner != null)
            {
                // Lock / keyhole — same basis as GetKeyholePos()
                Vector3 lockPos = GetKeyholePos() - 0.1f * transform.right;

                int numSparks = Mathf.Min(1 + appliedDamage, 9);
                for (int i = 0; i < numSparks; ++i)
                {
                    Vector3 sparkPosition = lockPos;
                    sparkPosition += transform.up * Random.Range(-0.02f, 0.06f);
                    sparkPosition += transform.right * Random.Range(-0.04f, 0.04f);
                    sparkPosition += transform.forward * Random.Range(-0.02f, 0.02f);

                    GameObject particle = ParticleSpawner.SpawnParticle(EParticleType.SparkSplat, sparkPosition);
                    if (particle == null)
                    {
                        Utils.CreateFallbackSplat(sparkPosition, SplatType.Spark);
                    }
                }

                Utils.PlayClip2d(hitSound);
            }

            quality -= appliedDamage;
            if (quality <= 0)
            {
                quality = 0;
                Unlock();
                LockableOpen();
            }
        }
    }

    protected override int GetQualityOffset()
    {
        return Locked(out _) ? 0 : -1;
    }

    protected override Vector3 GetKeyholePos()
    {
        return transform.position + 0.5f * transform.right + 0.3f * transform.up;
    }

    public override void Update()
    {
        base.Update();

        if (shouldOpen && lid != null)
        {
            openAmount = Utils.DampedApproach(openAmount, 1.0f, 0.5f);
            lid.localRotation = Quaternion.Euler(-80.0f * openAmount, 0.0f, 0.0f);
        }
    }

    private Transform FindNamedChild(Transform t, string name)
    {
        foreach (Transform childTransform in t)
        {
            if (childTransform != null && childTransform.name.StartsWith(name))
            {
                return childTransform;
            }
        }
        return t;
    }

    private void PlaceContentsInChest()
    {
        int child = link;
        
        // find the transform levelRoot with name "level<loadedLevel>" that is a child of the barrel.
        // then for each item in contents, find the subtransform of that with name "index<objectIndex>".

        // Look for a direct child named level<loadedLevel>
        Transform levelRoot = FindNamedChild(transform, $"Level{LevelLoader.sLevelLoader.loadedLevel}");

        string setup = $"Chest on Level{LevelLoader.sLevelLoader.loadedLevel}\n";

        List<UUObject> objectsToPutToSleep = new List<UUObject>();

        if (levelRoot != null)
        {
            while (child != 0)
            {
                UUObject obj = LevelLoader.GetObj(child);
                if (obj == null)
                {
                    break;
                }

                child = obj.chainIndex;

                Transform objTransform = FindNamedChild(levelRoot, $"Index{obj.objectIndex}");

                if (objTransform != null)
                {
                    obj.PostLoadInitialize();
                    if (objTransform != levelRoot)
                    {
                        obj.transform.SetPositionAndRotation(objTransform.position, objTransform.rotation);
                        objectsToPutToSleep.Add(obj);
                    }
                    obj.gameObject.SetActive(true);
                    LevelLoader.AddToWorld(obj);

                    setup += $"  Index{obj.objectIndex} ({obj.type})";
                }
            }

            foreach (UUObject obj in objectsToPutToSleep)
            {
                obj.GetComponent<Rigidbody>().Sleep();
            }
        }
        
        Debug.Log(setup);
    }
    
    public override void LockableOpen()
    {
        shouldOpen = true;

        if (link > 0)
        {
            UUObject obj = LevelLoader.GetObj(link);
            // skip over the lock
            if (obj.type == EObjectType.Lock)
            {
                link = obj.chainIndex;
            }
            PlayerData.sData.openedChest[LevelLoader.sLevelLoader.loadedLevel] = true;
            PlaceContentsInChest();
            link = 0;
        }
    }
}
