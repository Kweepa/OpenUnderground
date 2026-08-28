using UnityEngine;

public class Pole : UUObject
{
    public float thresholdDist = 5.0f;

    protected override bool TryUseAtAim()
    {
        int layerMask = 1 << LayerMask.NameToLayer("Objects");
        if (!TryRaycastFromPlayerAim(out SwitchBase switchObj, thresholdDist, layerMask))
        {
            return false;
        }

        switchObj.TryInteract(this, this, EAction.Use);
        return true;
    }

    public override EEquipAction Equip()
    {
        return TryUseAtAim() ? EEquipAction.Use : EEquipAction.Nothing;
    }

    public override string GetUseText()
    {
        return IsSwitchInAim(thresholdDist) ? "Poke" : null;
    }

    public override bool SupportsStuffGridMouseSecondaryUse => false;

    /// <summary>Cursor release uses <see cref="UUObject.Throw"/> before physics — poke switch at aim first (same ray as <see cref="Equip"/>).</summary>
    public override bool Throw()
    {
        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
        {
            return TryUseAtAim();
        }

        return false;
    }

    private static bool IsSwitchInAim(float maxDist)
    {
        int layerMask = 1 << LayerMask.NameToLayer("Objects");
        return TryRaycastFromPlayerAim(out SwitchBase _, maxDist, layerMask);
    }

    public override string GetGamepadChargeThrowVerbOrNull()
    {
        return null;
    }
}
