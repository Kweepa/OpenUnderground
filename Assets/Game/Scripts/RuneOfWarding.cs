using UnityEngine;
using Random = UnityEngine.Random;

public class RuneOfWarding : UUObject
{
    // TODO: shouldn't use a static
    private static RuneOfWarding existingRune;

    private float timeBetweenChecks;
    
    private readonly Collider[] cachedColliders = new Collider[8];

    public Transform runeTransform;

    private void Start()
    {
        // get rid of existing rune of warding
        if (existingRune != null)
        {
            Utils.DestroyItem(existingRune);
        }

        existingRune = this;
    }

    public override void Update()
    {
        timeBetweenChecks -= Time.deltaTime;
        if (timeBetweenChecks < 0.0f)
        {
            timeBetweenChecks += Random.Range(0.3f, 0.5f);

            int count = Physics.OverlapSphereNonAlloc(transform.position, 2.0f, cachedColliders, 1 << LayerMask.NameToLayer("Characters"));
            for (int i = 0; i < count; ++i)
            {
                Collider col = cachedColliders[i];
                Critter critter = col.transform.root.gameObject.GetComponent<Critter>();
                if (critter != null && critter.hp > 0)
                {
                    string setOffMessage = StringLoader.GetString(1, 245); // set off
                    Vector3 off = transform.position - PlayerObject.Player.transform.position;
                    int octant = Utils.OffsetToOctant(off);
                    setOffMessage += StringLoader.GetString(1, 36 + octant);
                    Messages.Add(setOffMessage);
                    Utils.DestroyItem(this);
                    existingRune = null;
                    return;
                }
            }
        }

        if (runeTransform != null)
        {
            runeTransform.SetLocalPositionAndRotation(0.2f * Mathf.Sin(Time.time) * Vector3.up, Quaternion.identity);
        }
    }
}
