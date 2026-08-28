using UnityEngine;

public class Coin : UUObject
{
    private int cachedQuantity;

    public GameObject singleCoin;
    public GameObject multipleCoins;

    public override Texture2D GetInventoryTex()
    {
        if (quantity <= 3)
        {
            return DataLoader.sDataLoader.objTex[161]; 
        }
        return DataLoader.sDataLoader.objTex[160];
    }

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);
        // Coins have no meaningful quality variance; normalize so stacks always match.
        quality = 63;
    }

    public override void Update()
    {
        if (quantity != cachedQuantity)
        {
            singleCoin.SetActive(quantity <= 3);
            multipleCoins.SetActive(quantity > 3);
            cachedQuantity = quantity;
        }
        base.Update();
    }
}
