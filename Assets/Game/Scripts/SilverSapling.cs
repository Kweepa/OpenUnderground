using UnityEngine;

public class SilverSapling : UUObject
{
    public AudioClip saplingShrink;
    
    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);

        growTime = 0.001f; // avoid divide by zero
        transform.root.localScale = new Vector3(0.1f, 0.1f, 0.1f);
    }

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        if (action == EAction.Use)
        {
            // pretend to pick it up but instead put silver seed in inventory
            UUObject seed = LevelLoader.CreateObjectOfType(EObjectType.SilverSeed);
            if (seed != null)
            {
                seed.PostLoadInitialize();
                Inventory.GrantNewItemToMouseOrInventory(seed);
                Utils.PlayClip(saplingShrink, transform.position);
                ParticleSpawner.SpawnParticle(EParticleType.SilverSaplingUproot, transform.position);
                Utils.DestroyItem(this);

                Messages.Add(1, 9);

                PlayerData.sData.saplingPlanted = false;
            }
        }
    }
    
    private float growTime;

    public override void Update()
    {
        base.Update();

        // grow
        growTime += Time.deltaTime;
        growTime = Mathf.Min(growTime, 1.0f);
        float xScale = 0.1f + Mathf.Sin(1.8f * growTime);
        float yScale = 1.0f - 0.9f * Mathf.Sin(4.0f * Mathf.PI * growTime) / (4.0f * Mathf.PI * growTime);
        transform.root.localScale = new Vector3(xScale, yScale, xScale);
    }
}
