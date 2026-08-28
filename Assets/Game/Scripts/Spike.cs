using UnityEngine;

public class Spike : UUObject
{
    private static bool TryGetClosedDoorInAim(out Door door)
    {
        door = null;
        if (Interaction.sInt == null)
        {
            return false;
        }

        float maxDist = Interaction.sInt.GetInteractionDistance();
        int layerMask = LayerMasks.EnvironmentAndCeiling;
        if (!TryRaycastFromPlayerAim(out RaycastHit hit, maxDist, layerMask))
        {
            return false;
        }

        door = hit.collider.transform.root.GetComponent<Door>();
        return door != null && !door.isOpen;
    }

    public override string GetUseText()
    {
        return TryGetClosedDoorInAim(out _) ? "Spike" : null;
    }

    public override bool SupportsStuffGridMouseSecondaryUse => false;

    public override EEquipAction Equip()
    {
        if (Interaction.sInt == null)
        {
            return EEquipAction.Nothing;
        }

        float maxDist = Interaction.sInt.GetInteractionDistance();
        int layerMask = LayerMasks.EnvironmentAndCeiling;
        if (!TryRaycastFromPlayerAim(out RaycastHit hit, maxDist, layerMask))
        {
            return EEquipAction.Nothing;
        }

        Door door = hit.collider.transform.root.GetComponent<Door>();
        if (door == null)
        {
            return EEquipAction.Nothing;
        }

        if (door.isOpen)
        {
            Messages.Add(1, 128);
            return EEquipAction.Nothing;
        }

        door.Spike();
        Messages.Add(1, 129);
        return EEquipAction.Consume;
    }

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
