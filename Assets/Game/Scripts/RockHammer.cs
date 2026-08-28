using UnityEngine;

public class RockHammer : UUObject
{
    // large boulders are environment so they block sight and movement; smaller bouleder are objects.
    private static int BoulderLayerMask =>
        LayerMasks.EnvironmentAndCeiling | (1 << LayerMask.NameToLayer("Objects"));

    protected override bool TryUseAtAim()
    {
        float maxDist = Interaction.sInt != null ? Interaction.sInt.GetInteractionDistance() : 4f;
        if (!TryRaycastFromPlayerAim(out Boulder boulder, maxDist, BoulderLayerMask))
        {
            return false;
        }

        boulder.Break();
        return true;
    }

    public override EEquipAction Equip()
    {
        return TryUseAtAim() ? EEquipAction.Use : EEquipAction.Nothing;
    }

    public override string GetUseText()
    {
        float maxDist = Interaction.sInt != null ? Interaction.sInt.GetInteractionDistance() : 4f;
        return TryRaycastFromPlayerAim(out Boulder _, maxDist, BoulderLayerMask) ? "Break" : null;
    }

    public override bool SupportsStuffGridMouseSecondaryUse => false;

    /// <summary>Cursor release uses <see cref="UUObject.Throw"/> before physics — break boulder at aim first (same ray as <see cref="Equip"/>).</summary>
    public override bool Throw()
    {
        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
        {
            return TryUseAtAim();
        }

        return false;
    }
}
