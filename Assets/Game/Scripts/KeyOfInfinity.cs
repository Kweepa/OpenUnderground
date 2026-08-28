using UnityEngine;
using UnityEngine.InputSystem;

public class KeyOfInfinity : UUObject
{
    private const float CrosshairRayLength = 10.0f;
    private const float FatRayRadius = 0.1f;

    public override EEquipAction Equip()
    {
        if (LevelLoader.sLevelLoader == null || LevelLoader.sLevelLoader.loadedLevel != 8)
        {
            return EEquipAction.Nothing;
        }

        if (PlayerObject.Player == null || PlayerObject.Player.mainCamera == null || Interaction.sInt == null)
        {
            return EEquipAction.Nothing;
        }

        float maxDist = Interaction.sInt.GetInteractionDistance();

        Decal targetDecal = null;
        bool usedMouseRay = false;
        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                usedMouseRay = true;
                Camera cam = PlayerObject.Player.mainCamera;
                Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
                Vector3 origin = ray.origin;
                Vector3 direction = ray.direction.sqrMagnitude > 1e-10f ? ray.direction.normalized : cam.transform.forward;

                // Build at runtime — LayerMask.NameToLayer must not run in static field init (Unity script lifecycle).
                int fatRayMask =
                    (1 << LayerMask.NameToLayer("Objects")) |
                    (1 << LayerMask.NameToLayer("NonBlockingObject")) |
                    (1 << LayerMask.NameToLayer("PhysicsDebris")) |
                    (1 << LayerMask.NameToLayer("Characters"));

                RaycastHit[] envRayHits = Physics.RaycastAll(origin, direction, CrosshairRayLength, LayerMasks.EnvironmentAndCeiling);
                RaycastHit[] fatHits = Physics.SphereCastAll(origin, FatRayRadius, direction, CrosshairRayLength, fatRayMask);

                Decal bestNonIncidental = null;
                float bestNonIncidentalDist = float.MaxValue;
                Decal bestIncidental = null;
                float bestIncidentalDist = float.MaxValue;

                void ConsiderHit(RaycastHit hit)
                {
                    UUObject obj = hit.collider.transform.root.GetComponent<UUObject>();
                    if (obj is not Decal decal || decal.ownerIndex != 14) // door to Slasher of Veils
                    {
                        return;
                    }

                    float dist = hit.distance;
                    if (obj.isIncidental)
                    {
                        if (dist < bestIncidentalDist)
                        {
                            bestIncidental = decal;
                            bestIncidentalDist = dist;
                        }
                    }
                    else if (dist < bestNonIncidentalDist)
                    {
                        bestNonIncidental = decal;
                        bestNonIncidentalDist = dist;
                    }
                }

                for (int i = 0; i < envRayHits.Length; i++)
                {
                    ConsiderHit(envRayHits[i]);
                }

                for (int i = 0; i < fatHits.Length; i++)
                {
                    ConsiderHit(fatHits[i]);
                }

                targetDecal = bestNonIncidental != null ? bestNonIncidental : bestIncidental;
            }
        }

        if (!usedMouseRay && targetDecal == null)
        {
            UUObject centeredObj = Interaction.sInt.centeredObject;
            if (centeredObj != null)
            {
                targetDecal = centeredObj as Decal;
                if (targetDecal != null && targetDecal.ownerIndex != 14)
                {
                    targetDecal = null;
                }
            }
        }

        if (targetDecal != null)
        {
            Vector3 playerPos = PlayerObject.Player.mainCamera.transform.position;
            Vector3 objPos = targetDecal.cachedRenderer != null ? targetDecal.cachedRenderer.bounds.center : targetDecal.transform.position;
            float distance = Vector3.Distance(playerPos, objPos);

            if (distance <= maxDist)
            {
                targetDecal.TryChainInteraction(EAction.Trigger);
                return EEquipAction.Consume;
            }
        }

        return EEquipAction.Nothing;
    }

    /// <summary>Level 8 + centered Slasher decal in interaction range (gamepad X hint; matches non-mouse <see cref="Equip"/>).</summary>
    private static bool IsGamepadInfinityUseTargetInRange()
    {
        if (LevelLoader.sLevelLoader == null || LevelLoader.sLevelLoader.loadedLevel != 8)
        {
            return false;
        }

        if (PlayerObject.Player == null || PlayerObject.Player.mainCamera == null || Interaction.sInt == null)
        {
            return false;
        }

        UUObject centeredObj = Interaction.sInt.centeredObject;
        if (centeredObj is not Decal targetDecal || targetDecal.ownerIndex != 14) // door to Slasher of Veils
        {
            return false;
        }

        float maxDist = Interaction.sInt.GetInteractionDistance();
        Vector3 playerPos = PlayerObject.Player.mainCamera.transform.position;
        Vector3 objPos = targetDecal.cachedRenderer != null ? targetDecal.cachedRenderer.bounds.center : targetDecal.transform.position;
        return Vector3.Distance(playerPos, objPos) <= maxDist;
    }

    public override string GetUseText()
    {
        return IsGamepadInfinityUseTargetInRange() ? "Open" : null;
    }

    public override bool SupportsStuffGridMouseSecondaryUse => false;

    public override string GetGamepadChargeThrowVerbOrNull()
    {
        return null;
    }

    /// <summary>Same as <see cref="Key.Throw"/> — cursor release must try <see cref="Equip"/> before <see cref="Inventory.TryThrow"/> hurls the key.</summary>
    public override bool Throw()
    {
        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
        {
            EEquipAction r = Equip();
            return r == EEquipAction.Use || r == EEquipAction.Consume;
        }

        return false;
    }
}
