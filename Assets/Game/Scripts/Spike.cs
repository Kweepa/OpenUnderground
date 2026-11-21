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
        return door != null && door.CanBeSpiked;
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

        // A refused spike is kept, Use rather than Nothing: with the mouse, Nothing would throw it
        // (Inventory.TryThrow()), and Use puts it back as it does a key used on a door.
        // A door that holds all the spikes it can (a remake's limit, Door.SpikesFull).
        if (door.CanBeSpiked && door.SpikesFull)
        {
            Messages.Add("The door can hold no more spikes.");
            return EEquipAction.Use;
        }

        // an open door, a portcullis or a secret door: "You can only spike closed doors."
        if (!door.Spike(bySpell: false))
        {
            Messages.Add(1, 128);
            return EEquipAction.Use;
        }

        Messages.Add(1, 129);
        return EEquipAction.Consume;
    }

    public override bool Throw()
    {
        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
        {
            EEquipAction r = Equip();
            if (r == EEquipAction.Consume)
            {
                // The spike is used up, as in the original (using_punt from UW.EXE 0x36c42,
                // SpikeDoor_seg040_662, USEITEMS.C). The cursor's use reaches here, not the
                // Consume of Inventory.TryEquipSelection(), and the spike went back to the pack.
                Inventory.sInv.UseUpOneAfterThrow(this);
                return true;
            }

            return r == EEquipAction.Use;
        }

        return false;
    }
}
