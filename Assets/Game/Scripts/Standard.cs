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
            if (hit.normal.y > 0.5f)
            {
                Inventory.sInv.RemoveItemFromInventory(this);

                transform.position = hit.point;
                if (TryDestroyTalisman(LevelLoader.GetClosestTile(hit.point)))
                {
                    SpawnSplash(DataLoader.sDataLoader.lavaSplash, DataLoader.sDataLoader.lavaSplashParticle, 10.0f * Vector3.up);
                }
                else
                {
                    // position the standard & don't add a rigid body
                    LevelLoader.AddToWorld(this);
                }
            }
        }

        // don't allow the player to drop this just anywhere...
        return true;
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

        return hit.normal.y > 0.5f;
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
