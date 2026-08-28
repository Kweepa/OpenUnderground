using UnityEngine;
using System.Collections.Generic;

public class Barrel : Lockable
{
    public GameObject barrelLid;
    public GameObject openBarrelLid;

    protected override Vector3 GetKeyholePos()
    {
        // not important as we can't lock barrels
        return transform.position;
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

    private void PlaceContentsInBarrel()
    {
        int child = link;
        
        // find the transform levelRoot with name "level<loadedLevel>" that is a child of the barrel.
        // then for each item in contents, find the subtransform of that with name "index<objectIndex>".

        // Look for a direct child named level<loadedLevel>
        Transform levelRoot = FindNamedChild(transform, $"Level{LevelLoader.sLevelLoader.loadedLevel}");
        Transform barrelRoot = FindNamedChild(levelRoot, $"Barrel{objectIndex}");

        string setup = $"Level{LevelLoader.sLevelLoader.loadedLevel}/Barrel{objectIndex}\n";

        List<UUObject> objectsToPutToSleep = new List<UUObject>();

        if (barrelRoot != null)
        {
            while (child != 0)
            {
                UUObject obj = LevelLoader.GetObj(child);
                if (obj == null)
                {
                    break;
                }

                child = obj.chainIndex;

                Transform objTransform = FindNamedChild(barrelRoot, $"Index{obj.objectIndex}");

                if (objTransform != null)
                {
                    obj.PostLoadInitialize();
                    if (objTransform != barrelRoot)
                    {
                        obj.transform.SetPositionAndRotation(objTransform.position, objTransform.rotation);
                        objectsToPutToSleep.Add(obj);
                    }
                    else
                    {
                        // just stick the object in the center of the barrel, rotated so it doesn't intersect
                        obj.transform.SetPositionAndRotation(transform.position + 0.5f * Vector3.up, Quaternion.AngleAxis(90.0f, Vector3.right));
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
        if (barrelLid != null)
        {
            barrelLid.SetActive(false);
        }
        if (openBarrelLid != null)
        {
            openBarrelLid.SetActive(true);
        }
        if (link > 0)
        {
            PlaceContentsInBarrel();
            link = 0;
        }
    }
}
