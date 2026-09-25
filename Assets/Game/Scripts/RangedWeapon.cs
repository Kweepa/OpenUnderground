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

    /// <summary>
    /// A shot is not a swing. It is rolled on Missile alone, against a fixed difficulty, when the
    /// arrow is loosed (see <see cref="Projectile"/>) - none of Attack, Dexterity or the weapon's
    /// own enchantment comes into it, so the panel must not add them either.
    /// </summary>
    public override int GetAttackScore()
    {
        return Skills.GetSkill(ESkill.Missile);
    }

    public override int GetKnownAttackScore()
    {
        return GetAttackScore();
    }

    /// <summary>
    /// Missile weapons never wear out. The missile table carries no durability byte at all, and
    /// the original neither damages them in combat nor lets the anvil repair them.
    /// </summary>
    public override int GetDurability()
    {
        return 255;
    }

    /// <summary>
    /// A launcher does not charge. Once it is up the original sets the gem straight to its last
    /// frame, the one a melee charge reaches at a hundred, and never runs the charge loop for it
    /// (UW.EXE 0x256a4; the loop at 0x2574b is reached only on the melee side of the flag
    /// 0x2674). So there is no draw speed to look up, and the melee row this used to borrow -
    /// a mace's for a bow, the shiny sword's for a crossbow - made the gem climb at a rate that
    /// meant nothing, since the shot does not depend on it.
    /// </summary>
    protected override int GetChargePercent()
    {
        return prepareTime > WindUpSeconds ? 100 : 0;
    }

    /// <summary>
    /// A launcher does the damage of what it fires, scaled by Missile the way the shot scales it.
    /// The melee row this used to read belongs to another weapon. An enchantment on the launcher
    /// is not counted, because the shot does not count it either.
    /// </summary>
    protected override int GetMaxDamage(bool asKnown)
    {
        int maxDamage = Projectile.GetPlayerMaxDamage(GetAmmoType());

        bool cursed = asKnown
            ? Magic.sMagic.IsSpellKnownActive(Magic.ESpell.Cursed)
            : Magic.sMagic.IsSpellActive(Magic.ESpell.Cursed);
        if (cursed)
        {
            maxDamage /= 2;
        }

        return maxDamage;
    }

    /// <summary>
    /// A launcher with nothing to launch refuses the attack before it starts, and says what is
    /// missing. The original checks at the press: for a launcher UW.EXE 0x25326 calls 0x25288,
    /// which looks for the ammunition in the pack and, finding none, prints the message and
    /// returns -1, so the attack never begins. The words are the original's, kept in the
    /// executable's data segment rather than in strings.pak: "Sorry, you have no " at DS:0x24b,
    /// the plural of the ammunition without its article (0x3605e), and "." at DS:0x267.
    /// </summary>
    protected override bool CanStartAttack(bool announce)
    {
        EObjectType ammoType = GetAmmoType();
        if (Inventory.sInv.FindObjectInInventory(ammoType) != null)
        {
            return true;
        }

        if (announce)
        {
            Messages.Add($"Sorry, you have no {DataLoader.GetPlural((int)ammoType)}.");
            Utils.PlayClip2d(failedShot);
        }

        return false;
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
