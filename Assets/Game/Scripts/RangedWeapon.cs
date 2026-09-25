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

    /// <summary>The sling as it is held, hidden while the whirl shows the goblins' one.</summary>
    public Transform slingRoot;
    /// <summary>
    /// A goblin model, whose own sling - a cord with a pouch, on a chain of bones - is whirled
    /// over the player's head while a shot is prepared. Only the sling is drawn; the rest of the
    /// goblin is there to carry the animation, which moves the arm as well as the cord.
    /// </summary>
    public GameObject slingWhirlModel;
    /// <summary>The goblins' whirl, flat over the head: Sling_Attack_Loop.</summary>
    public AnimationClip slingWhirlClip;
    /// <summary>The one renderer of <see cref="slingWhirlModel"/> that is drawn.</summary>
    public string slingWhirlMesh = "Sling";
    /// <summary>The goblin's bone that is put where the player's head is.</summary>
    public string slingWhirlHeadBone = "head";
    /// <summary>Where the goblin's head goes, in the camera's space.</summary>
    public Vector3 slingWhirlOffset = Vector3.zero;
    public float slingWhirlScale = 1.0f;
    /// <summary>How fast the whirl plays once the wind-up is over, 1 being the goblins' speed.</summary>
    public float slingWhirlSpeed = 1.0f;
    /// <summary>The share of that speed the whirl already has at the press.</summary>
    public float slingWhirlStartSpeed = 0.25f;
    /// <summary>
    /// The end of the goblins' sling, the bone the whirl starts from: at the press the loop is
    /// entered where this comes into the view, so the sling is seen from the first frame.
    /// </summary>
    public string slingWhirlTipBone = "Sling_7";

    private int swooshNum;
    private float whirlStartTime = -1.0f;

    // One whirl for the whole game, under the camera: only one sling is ever in hand, so every
    // sling shares it, and the goblin is copied once per session rather than once per sling.
    private static GameObject sWhirlRig;
    private static Transform sWhirlHead;
    private static Transform sWhirlTip;
    private static RangedWeapon sWhirlUser;

    protected override Transform GetLoweredHideExemptChild()
    {
        return projectileRoot != null ? projectileRoot.transform : null;
    }

    private void OnDisable()
    {
        // A sling put away or destroyed mid-whirl must not leave the whirl turning under the camera.
        StopSlingWhirl();
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
            StopSlingWhirl();
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
            // Built as soon as a sling is in hand, so the copy is made while the player is busy
            // with the pack rather than on the first press, where it would be a stall.
            if (sWhirlRig == null && PlayerObject.Player != null && PlayerObject.Player.mainCamera != null
                && transform.parent == PlayerObject.Player.mainCamera.transform)
            {
                EnsureWhirlRig();
            }

            switch (state)
            {
            case EState.Prepare:
                {
                    // The cursor stays grey through the wind-up, so the sling itself has to show that
                    // something is happening: it comes into view at the press, gathers speed until
                    // a release would land, and swooshes once a round.
                    float rounds = WhirlSling(prepareTime);

                    int swooshCount = (int)(rounds + 0.5f);
                    if (swooshCount > swooshNum)
                    {
                        swooshNum = swooshCount;
                        Vector3 pos = PlayerObject.Player.mainCamera.transform.position + Vector3.up;
                        float volume = Mathf.Clamp01(prepareTime / WindUpSeconds);
                        Utils.PlayClip(swingSound, pos, volume, Random.Range(0.9f, 1.1f));
                    }
                }
                break;
            }
        }
    }

    /// <summary>
    /// How far into the whirl the animation is after this long a hold, in seconds of the clip.
    /// The speed climbs evenly from <see cref="slingWhirlStartSpeed"/> to full across the wind-up
    /// and stays there, so the time is a parabola and then a straight line - worked out from the
    /// hold rather than added up frame by frame.
    /// </summary>
    private float GetWhirlTime(float held)
    {
        float start = slingWhirlStartSpeed;
        if (held <= WindUpSeconds)
        {
            return slingWhirlSpeed * (start * held + (1.0f - start) * held * held / (2.0f * WindUpSeconds));
        }

        return slingWhirlSpeed * ((1.0f + start) * WindUpSeconds / 2.0f + held - WindUpSeconds);
    }

    /// <summary>Poses the whirl for this long a hold, and returns how many rounds of the clip it has played.</summary>
    private float WhirlSling(float held)
    {
        if (!EnsureWhirlRig())
        {
            return 0.0f;
        }

        sWhirlUser = this;
        sWhirlRig.SetActive(true);
        SetHeldSlingVisible(false);

        float length = Mathf.Max(slingWhirlClip.length, 0.01f);
        if (whirlStartTime < 0.0f)
        {
            whirlStartTime = FindWhirlStart(length);
        }

        // It starts slowly and gathers speed until a release would land, and since it starts
        // where the sling comes into view, the slow part is on screen rather than behind the head.
        float played = GetWhirlTime(held);
        PoseWhirl((whirlStartTime + played) % length);

        return played / length;
    }

    /// <summary>
    /// The point of the loop to start from: where the end of the sling comes into the view at the
    /// side, on its way to the lowest point it reaches. Found by trying twelve points of the clip
    /// against the camera as it is at the press, and stepping back from the lowest one for as
    /// long as the end is still in view.
    /// </summary>
    private float FindWhirlStart(float length)
    {
        Camera cam = PlayerObject.Player.mainCamera;
        const int tries = 12;
        bool[] inView = new bool[tries];
        int lowest = -1;
        float lowestHeight = float.MaxValue;
        for (int i = 0; i < tries; i++)
        {
            PoseWhirl(length * i / tries);
            Vector3 view = cam.WorldToViewportPoint(sWhirlTip.position);
            inView[i] = view.z > cam.nearClipPlane
                        && view.x > 0.0f && view.x < 1.0f
                        && view.y > 0.0f && view.y < 1.0f;
            if (inView[i] && view.y < lowestHeight)
            {
                lowestHeight = view.y;
                lowest = i;
            }
        }

        if (lowest < 0)
        {
            return 0.0f;
        }

        int start = lowest;
        for (int step = 1; step < tries; step++)
        {
            int previous = (lowest - step + tries) % tries;
            if (!inView[previous])
            {
                break;
            }
            start = previous;
        }
        return length * start / tries;
    }

    /// <summary>
    /// The goblin looks where the player looks, and its head is pinned to the player's, so the
    /// sling turns over the player's head and the goblin's body sway does not carry it about. The
    /// goblins sling left-handed, so for a right-handed player the whole rig is mirrored.
    /// </summary>
    /// <remarks>
    /// The mirror is set in the world, not against the parent, so that a flip higher up - as
    /// WeaponBase does to a left-handed player's weapon - cannot cancel it.
    /// </remarks>
    private void PoseWhirl(float clipTime)
    {
        Transform rig = sWhirlRig.transform;
        slingWhirlClip.SampleAnimation(sWhirlRig, clipTime);

        Transform cam = PlayerObject.Player.mainCamera.transform;
        float side = PlayerData.sData.leftHanded ? 1.0f : -1.0f;
        side *= Mathf.Sign(rig.parent.lossyScale.x);
        rig.localScale = slingWhirlScale * new Vector3(side, 1.0f, 1.0f);
        rig.rotation = cam.rotation;
        rig.position += cam.TransformPoint(slingWhirlOffset) - sWhirlHead.position;
    }

    private void StopSlingWhirl()
    {
        whirlStartTime = -1.0f;
        if (sWhirlUser == this)
        {
            sWhirlUser = null;
            if (sWhirlRig != null)
            {
                sWhirlRig.SetActive(false);
            }
        }
        SetHeldSlingVisible(true);
    }

    private void SetHeldSlingVisible(bool visible)
    {
        if (slingRoot != null && slingRoot.TryGetComponent(out Renderer held))
        {
            held.enabled = visible;
        }
    }

    /// <summary>
    /// Builds the whirl once: a copy of the goblin under the camera, cut down to the sling and the
    /// bones that move it. The other meshes are destroyed rather than hidden - a goblin carries a
    /// dozen of them, heads, eyes, armour and a club - and so is anything that could act by
    /// itself: animators, colliders, scripts.
    /// </summary>
    private bool EnsureWhirlRig()
    {
        if (sWhirlRig != null)
        {
            return true;
        }
        if (slingWhirlModel == null || slingWhirlClip == null
            || PlayerObject.Player == null || PlayerObject.Player.mainCamera == null)
        {
            return false;
        }

        GameObject rig = Instantiate(slingWhirlModel, PlayerObject.Player.mainCamera.transform, false);
        rig.name = "Sling whirl";
        rig.SetActive(false);
        int layer = slingRoot != null ? slingRoot.gameObject.layer : gameObject.layer;

        foreach (Animator animator in rig.GetComponentsInChildren<Animator>(true))
        {
            Destroy(animator);
        }
        foreach (Collider col in rig.GetComponentsInChildren<Collider>(true))
        {
            Destroy(col);
        }
        foreach (MonoBehaviour script in rig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            Destroy(script);
        }
        foreach (Renderer r in rig.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name != slingWhirlMesh)
            {
                // A mesh node with nothing under it goes whole; one that other nodes hang from
                // keeps its transform and loses only the renderer.
                if (r.transform.childCount == 0)
                {
                    Destroy(r.gameObject);
                }
                else
                {
                    Destroy(r);
                }
                continue;
            }

            r.gameObject.SetActive(true);
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (r is SkinnedMeshRenderer skinned)
            {
                // The pose comes from sampling the clip, and the bounds must follow it or the
                // sling can be culled while it is plainly in view.
                skinned.updateWhenOffscreen = true;
            }
        }

        sWhirlHead = null;
        sWhirlTip = null;
        foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer = layer;
            if (sWhirlHead == null && t.name == slingWhirlHeadBone)
            {
                sWhirlHead = t;
            }
            if (sWhirlTip == null && t.name == slingWhirlTipBone)
            {
                sWhirlTip = t;
            }
        }
        if (sWhirlHead == null)
        {
            sWhirlHead = rig.transform;
        }
        if (sWhirlTip == null)
        {
            sWhirlTip = rig.transform;
        }

        sWhirlRig = rig;
        return true;
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
