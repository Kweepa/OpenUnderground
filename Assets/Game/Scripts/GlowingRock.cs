using UnityEngine;

// zanium
public class GlowingRock : UUObject
{
    public AudioClip pickupClip;
    public GameObject rockA;
    public GameObject rockB;
    public GameObject rockC;

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);

        int which = Random.Range(0, 3); 
        rockA.SetActive(which == 0);
        rockB.SetActive(which == 1);
        rockC.SetActive(which == 2);
    }

    public override void Update()
    {
        base.Update();

        Vector3 playerOff = PlayerObject.Player.transform.position + Vector3.down - transform.position;
        if (playerOff.sqrMagnitude < 1.0f)
        {
            if (Inventory.sInv.FindObjectInInventory(type))
            {
                Utils.PlayClip(pickupClip, this.transform.position);
                gameObject.SetActive(false);
                LevelLoader.worldObj.Remove(this);
                Inventory.Add(this);
            }
        }
    }
}
