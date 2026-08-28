using UnityEngine;

public class Bones : UUObject
{
    public GameObject defaultMesh;
    public GameObject lizardmanSkullMesh;

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);

        // Swap to LizardmanSkull if originalLevel is 3 and objectIndex matches specific values
        if (originalLevel == 3 && lizardmanSkullMesh != null && defaultMesh != null)
        {
            // TODO: Replace placeholder values with actual objectIndex values
            if (objectIndex is 935 or 716 or 718)
            {
                defaultMesh.SetActive(false);
                lizardmanSkullMesh.SetActive(true);
            }
        }
    }

    protected override bool TryUseAtAim()
    {
        if (Interaction.sInt == null)
        {
            return false;
        }

        float maxDist = Interaction.sInt.GetInteractionDistance();
        int layerMask = 1 << LayerMask.NameToLayer("Objects");
        if (!TryRaycastFromPlayerAim(out Gravestone grave, maxDist, layerMask))
        {
            return false;
        }

        grave.TryInteract(this, this, EAction.Use);
        return true;
    }

    public override EEquipAction Equip()
    {
        return TryUseAtAim() ? EEquipAction.Use : EEquipAction.Nothing;
    }

    public override string GetUseText()
    {
        return IsGravestoneInAim() ? "Bury" : null;
    }

    public override bool SupportsStuffGridMouseSecondaryUse => false;

    /// <summary>Cursor release uses <see cref="UUObject.Throw"/> before physics — bury at aim first (same ray as <see cref="Equip"/>).</summary>
    public override bool Throw()
    {
        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
        {
            return TryUseAtAim();
        }

        return false;
    }

    /// <summary>For inventory X hint when not using mouse UI — forward cast matches <see cref="Equip"/> on gamepad.</summary>
    private static bool IsGravestoneInAim()
    {
        if (Interaction.sInt == null)
        {
            return false;
        }

        float maxDist = Interaction.sInt.GetInteractionDistance();
        int layerMask = 1 << LayerMask.NameToLayer("Objects");
        return TryRaycastFromPlayerAim(out Gravestone _, maxDist, layerMask);
    }

    public override string GetGamepadChargeThrowVerbOrNull()
    {
        if (IsGravestoneInAim())
        {
            return "Bury";
        }

        return null;
    }

    public override void NameEnchantment(bool spawnParticleEffect = true)
    {
        base.NameEnchantment(spawnParticleEffect); // You see blah
        if (ownerIndex != 0)
        {
            EObjectType ownerType = EObjectType.Rotworm + ownerIndex;
            bool plural = type is EObjectType.PileOfBonesA or EObjectType.PileOfBonesB;
            Messages.Add($"{StringLoader.GetString(1, plural ? 23 : 22)}{DataLoader.GetCleanedObjectName(ownerType)}.");
        }
    }
}
