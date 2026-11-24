using UnityEngine;
using UnityEngine.InputSystem;

public class Standard : UUObject
{
    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);

        // identify standard immediately
        loreResult = Skills.ESkillTestResult.CriticalSuccess;
    }
    
    // Carried on a shoulder or in the shield hand, as in the original (see Inventory.IsHoldingSlot).
    public override EEquipAction Equip()
    {
        return EEquipAction.Equip;
    }

    // Gives the Y button and the right click their Equip, with its hint.
    public override string GetUseText()
    {
        return "Equip";
    }

    private const float PlantRayLength = 6.0f;

    public override bool Throw()
    {
        if (PlayerObject.Player == null || PlayerObject.Player.mainCamera == null)
        {
            return true;
        }

        int layerMask = LayerMasks.EnvironmentAndCeiling;

        Camera cam = PlayerObject.Player.mainCamera;
        Vector3 origin = cam.transform.position;
        Vector3 direction = cam.transform.forward;
        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
                origin = ray.origin;
                Vector3 d = ray.direction;
                direction = d.sqrMagnitude > 1e-10f ? d.normalized : cam.transform.forward;
            }
        }

        if (Physics.Raycast(origin, direction, out RaycastHit hit, PlantRayLength, layerMask))
        {
            if (CanPlantAt(hit))
            {
                Inventory.sInv.RemoveItemFromInventory(this);

                // position the standard & don't add a rigid body
                transform.position = hit.point;
                StandUpright();
                LevelLoader.AddToWorld(this);
                return true;
            }
        }

        // No spot within reach to plant it: it is thrown like anything else. The original has no rule of its own
        // for this item when it leaves the hand; what the ending asks of it is decided where it comes to rest
        // (UW.EXE 0x2be7f, mob_to_static, OBJPHYS.C), as UUObject.OnCollisionEnter() does here.
        return false;
    }

    /// <summary>
    /// A standard thrown once keeps the Rigidbody the throw gave it, and may lie on its side: planted again, it
    /// stands up and loses the body, or it would topple. It has none at load (isStatic), the state it starts in.
    /// </summary>
    private void StandUpright()
    {
        transform.rotation = Quaternion.AngleAxis(transform.eulerAngles.y, Vector3.up);
        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            Destroy(rb);
        }
    }

    /// <summary>For inventory X hint when not using mouse UI — forward plant ray matches <see cref="Throw"/> on gamepad.</summary>
    private static bool IsPlantSpotInGamepadThrowAim()
    {
        if (PlayerObject.Player == null || PlayerObject.Player.mainCamera == null)
        {
            return false;
        }

        int layerMask = LayerMasks.EnvironmentAndCeiling;
        Camera cam = PlayerObject.Player.mainCamera;
        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, PlantRayLength, layerMask))
        {
            return false;
        }

        return CanPlantAt(hit);
    }

    /// <summary>
    /// A near-flat floor, and neither water nor lava, where it stood upright in the pool: there it is thrown
    /// instead, and comes to rest like any object (UUObject.OnCollisionEnter()), which also destroys it in the
    /// lava the ending asks for.
    /// </summary>
    private static bool CanPlantAt(RaycastHit hit)
    {
        ETerrainType terrain = LevelLoader.GetClosestTile(hit.point).GetFloorTerrain();
        return hit.normal.y > 0.5f && terrain != ETerrainType.Water && terrain != ETerrainType.Lava;
    }

    public override string GetGamepadChargeThrowVerbOrNull()
    {
        if (IsPlantSpotInGamepadThrowAim())
        {
            return "Plant";
        }

        return null;
    }

    protected override string GetIdentifiedName(string baseName)
    {
        return StringLoader.GetString(1, 265);
    }
}
