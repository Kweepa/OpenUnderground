using UnityEngine;

public class OilFlask : UUObject
{
    private void DeleteFlask()
    {
        if (quantity > 1)
        {
            --quantity;
        }
        else
        {
            Utils.DestroyItem(this);
        }
    }

    /// <summary>
    /// Returns true when the interaction was handled (success or refused with a message).
    /// Returns false only for oil+oil so inventory can stack.
    /// </summary>
    public override bool TryCombine(UUObject obj)
    {
        switch (obj.type)
        {
        case EObjectType.OilFlask:
            return false; // allow stacking
        case EObjectType.PieceOfWoodA:
        case EObjectType.PieceOfWoodB:
            {
                // if wood is stacked, remove one
                if (obj.quantity > 1)
                {
                    obj = Inventory.sInv.Split(obj);
                }

                // delete oil, replace wood with torch
                DeleteFlask();

                UUObject torch = LevelLoader.CreateObjectOfType(EObjectType.Torch);
                torch.quality = 63;
                // Copy flags from wood
                torch.flags = obj.flags;
                // Initialize name properties so torch has proper name
                torch.PostLoadInitialize();
                Inventory.sInv.ReplaceItemInInventory(obj, torch);
                Utils.DestroyItem(obj);
                Messages.Add(1, 181); // dousing etc
                return true;
            }
        case EObjectType.Torch:
        case EObjectType.LitTorch:
        case EObjectType.Lantern:
        case EObjectType.LitLantern:
            {
                LightSource lightSource = obj as LightSource;
                bool isTorch = obj.type is EObjectType.Torch or EObjectType.LitTorch;
                bool lit = lightSource != null && lightSource.IsLit()
                    || obj.type is EObjectType.LitTorch or EObjectType.LitLantern;

                if (lit)
                {
                    // you think it is a bad idea to add oil to the lit torch/lantern
                    Messages.Add(1, isTorch ? 182 : 178);
                    return true;
                }

                if (obj.quality < 63)
                {
                    DeleteFlask();

                    if (obj.quantity > 1)
                    {
                        obj = Inventory.sInv.Split(obj);
                    }
                    if (isTorch)
                    {
                        // full refresh
                        obj.quality = 63;
                        Messages.Add(1, 183);
                    }
                    else
                    {
                        // partial refuel
                        obj.quality = Mathf.Min(obj.quality + 32, 63);
                        Messages.Add(1, 179);
                    }
                    return true;
                }

                Messages.Add(1, isTorch ? 184 : 180);
                return true;
            }
        default:
            Messages.Add(1, 177); // not on that
            return true;
        }
    }
}
