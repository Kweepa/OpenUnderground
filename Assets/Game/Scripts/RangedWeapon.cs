using UnityEngine;

public class RangedWeapon : WeaponBase
{
    public AudioClip drawBack;
    public AudioClip failedShot;
    public AudioClip successfulShot;

    public float spawnForward;
    public float spawnOffset;
    public float spawnVelocity;

    public Transform bowRoot;
    public GameObject projectileRoot;

    private int swooshNum;

    protected override Transform GetLoweredHideExemptChild()
    {
        return projectileRoot != null ? projectileRoot.transform : null;
    }

    protected override void ChangeState(EState newState)
    {
        base.ChangeState(newState);
        switch (newState)
        {
        case EState.Prepare:
            Utils.PlayClip2d(drawBack);
            swooshNum = 0;
            if (projectileRoot != null && Inventory.sInv.FindObjectInInventory(GetAmmoType()) != null)
            {
                projectileRoot.SetActive(true);
            }
            break;
        default:
            if (projectileRoot != null)
            {
                projectileRoot.SetActive(false);
            }
            break;
        }
    }

    public override void Update()
    {
        base.Update();

        if (type is EObjectType.Bow or EObjectType.JeweledBow or EObjectType.Crossbow)
        {
            // Only modify bowRoot scale, not root transform scale (which is handled by WeaponBase)
            if (bowRoot != null)
            {
                Transform t = bowRoot.transform;
                switch (state)
                {
                case EState.Prepare:
                    t.localScale = new Vector3(1.0f, 1.0f, 1.0f + Mathf.Clamp01(prepareTime));
                    break;
                default:
                    t.localScale = Vector3.one;
                    break;
                }
            }
        }
        else if (type is EObjectType.Sling)
        {
            switch (state)
            {
            case EState.Prepare:
                {
                    int swooshCount = (int)prepareTime;
                    if (swooshCount > swooshNum)
                    {
                        swooshNum = swooshCount;
                        Vector3 pos = PlayerObject.Player.mainCamera.transform.position + Vector3.up;
                        Utils.PlayClip(swingSound, pos, Mathf.Min(prepareTime, 1.0f),Random.Range(0.9f, 1.1f));
                    }
                }
                break;
            }
        }
    }

    private EObjectType GetAmmoType()
    {
        EObjectType ammoType = EObjectType.Arrow;
        switch (type)
        {
        case EObjectType.Bow:
        case EObjectType.JeweledBow:
            ammoType = EObjectType.Arrow;
            break;
        case EObjectType.Crossbow:
            ammoType = EObjectType.CrossbowBolt;
            break;
        case EObjectType.Sling:
            ammoType = EObjectType.SlingStone;
            break;
        }

        return ammoType;
    }

    protected override bool Attack()
    {
        // check we still have ammo
        EObjectType ammoType = GetAmmoType();
        bool useGravity = type is EObjectType.Sling;

        UUObject ammo = Inventory.sInv.FindObjectInInventory(ammoType);
        if (ammo != null)
        {
            // peel one off and shoot it
            if (ammo.quantity > 1)
            {
                UUObject stack = ammo;
                ammo.quantity--;
                ammo = LevelLoader.CreateObjectOfType(ammoType);
                Inventory.CopyPeeledStackProperties(stack, ammo);
                // Initialize name properties so projectile has proper name
                ammo.PostLoadInitialize();
            }
            else
            {
                Inventory.sInv.RemoveItemFromInventory(ammo);
            }
            
            Vector3 horizontalOffset = PlayerData.sData.leftHanded 
                ? -PlayerObject.Player.transform.right 
                : PlayerObject.Player.transform.right;
            Vector3 start = PlayerObject.Player.transform.position + 0.1f * horizontalOffset + spawnOffset * Vector3.up + spawnForward * PlayerObject.Player.mainCamera.transform.forward;
            Vector3 dir = PlayerObject.Player.mainCamera.transform.forward;
            Projectile proj = ammo as Projectile;
            if (proj != null)
            {
                if (Physics.Raycast(PlayerObject.Player.mainCamera.transform.forward, dir, out RaycastHit hit, 15.0f, 1 << LayerMask.NameToLayer("Characters")))
                {
                    dir = hit.point - start;
                }
                
                proj.projectileOwner = PlayerObject.Player.gameObject;
                proj.createdByCursedEntity = Magic.sMagic.IsSpellActive(Magic.ESpell.Cursed);
                proj.gameObject.transform.SetPositionAndRotation(start, Quaternion.LookRotation(dir, Vector3.up));
                proj.oldPos = start;
                LevelLoader.AddToWorld(proj);
                proj.doDamage = true;
                Rigidbody rb = proj.gameObject.GetComponent<Rigidbody>();
                if (proj != null)
                {
                    if (useGravity)
                    {
                        // add a bit of up
                        rb.linearVelocity = spawnVelocity * (dir + 0.1f * Vector3.up);
                    }
                    else
                    {
                        // set velocity
                        rb.linearVelocity = spawnVelocity * dir;
                        rb.useGravity = false;
                    }
                }
                Utils.PlayClip2d(successfulShot);
            }
        }
        else
        {
            Utils.PlayClip2d(failedShot);
        }

        return false;
    }
}
