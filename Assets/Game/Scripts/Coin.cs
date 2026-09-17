using System.Collections.Generic;
using UnityEngine;

public class Coin : UUObject
{
    private int cachedQuantity;

    public GameObject singleCoin;
    public GameObject multipleCoins;

    // One silver copy per source texture, made the first time it is asked for and kept for the rest
    // of the session: the two icons, and the one the coin models share.
    private static readonly Dictionary<Texture2D, Texture2D> silverTextures = new Dictionary<Texture2D, Texture2D>();

    // One silver copy per source material, for the models lying in the world.
    private static readonly Dictionary<Material, Material> silverMaterials = new Dictionary<Material, Material>();

    private bool modelSilvered;

    /// <summary>
    /// The icon for this pile: the two coin pictures the original ships, and for the common coin
    /// a silver copy of one of them.
    /// </summary>
    /// <remarks>
    /// The original draws a coin and a gold coin with the same two pictures, one for a few and
    /// one for a heap, so in the pack the two are indistinguishable and a player who wants to
    /// leave the small change behind has no way to tell which pile is which. Silver is the obvious
    /// answer and the original has no picture of it, so it is made here at run time from
    /// the picture already in memory: nothing new is shipped, and a player without the original
    /// data files is no worse off than before.
    /// </remarks>
    public override Texture2D GetInventoryTex()
    {
        Texture2D tex = quantity <= 3
            ? DataLoader.sDataLoader.objTex[161]
            : DataLoader.sDataLoader.objTex[160];

        return type == EObjectType.GoldCoin ? tex : Silvered(tex);
    }

    /// <summary>
    /// A silver copy of a texture: every pixel taken down to its own brightness, alpha untouched.
    /// </summary>
    /// <remarks>
    /// The icons come from GraphicsLoader, which builds them with SetPixels and a plain Apply, so
    /// they read back straight away. The one the models share is an imported asset and does not,
    /// so it goes past the GPU first. If neither works the original is handed back and the two
    /// coins look the same again, which is where this started rather than a crash.
    /// </remarks>
    private static Texture2D Silvered(Texture2D source)
    {
        if (source == null)
        {
            return null;
        }

        if (silverTextures.TryGetValue(source, out Texture2D silver))
        {
            return silver;
        }

        silver = source;
        try
        {
            Color[] pixels = ReadPixels(source);
            for (int i = 0; i < pixels.Length; ++i)
            {
                float luminance = pixels[i].grayscale;
                pixels[i] = new Color(luminance, luminance, luminance, pixels[i].a);
            }

            silver = new Texture2D(source.width, source.height, TextureFormat.RGBA32, mipChain: false)
            {
                name = source.name + " (silver)",
                filterMode = source.filterMode,
                wrapMode = source.wrapMode
            };
            silver.SetPixels(pixels);
            silver.Apply();
        }
        catch (UnityException)
        {
        }

        silverTextures[source] = silver;
        return silver;
    }

    /// <summary>
    /// The pixels of a texture, whether or not the import settings left it readable.
    /// </summary>
    /// <remarks>
    /// A texture imported with Read/Write off keeps no copy in memory, so GetPixels throws. The
    /// way round it that needs nothing new shipped is to draw it once into a render target and
    /// read that back, which is a copy and not a conversion: no shader, no material, no asset.
    /// It happens once per texture in a session. The read back needs no particular moment in the
    /// frame: what has to wait for the end of one is reading the screen, and this reads a render
    /// target of its own, made and made active two lines above.
    /// </remarks>
    private static Color[] ReadPixels(Texture2D source)
    {
        if (source.isReadable)
        {
            return source.GetPixels();
        }

        RenderTexture target = RenderTexture.GetTemporary(
            source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, mipChain: false);
            copy.ReadPixels(new Rect(0.0f, 0.0f, source.width, source.height), 0, 0);
            copy.Apply();
            Color[] pixels = copy.GetPixels();
            Destroy(copy);
            return pixels;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    /// <summary>
    /// Silvers the models lying in the world, so a pile reads as silver from across the
    /// room the same way it does in the pack.
    /// </summary>
    /// <remarks>
    /// Both models - the single coin and the heap - share one material, so one silver copy covers
    /// every pile in the game. The copy is assigned to the renderer, which swaps the reference
    /// and leaves the material asset alone; the gold coin keeps the one it came with.
    /// </remarks>
    private void SilverModel()
    {
        foreach (Renderer rend in GetComponentsInChildren<Renderer>(true))
        {
            Material source = rend.sharedMaterial;
            if (source == null)
            {
                continue;
            }

            if (!silverMaterials.TryGetValue(source, out Material silver))
            {
                silver = new Material(source) { name = source.name + " (silver)" };
                silver.mainTexture = Silvered(source.mainTexture as Texture2D);
                silverMaterials[source] = silver;
            }

            rend.sharedMaterial = silver;
        }
    }

    public override void PostLoadInitialize(bool restoredFromSave = false)
    {
        base.PostLoadInitialize(restoredFromSave);
        // Coins have no meaningful quality variance; normalize so stacks always match.
        quality = 63;
    }

    public override void Update()
    {
        if (!modelSilvered && type != EObjectType.GoldCoin)
        {
            // Here and not in PostLoadInitialize because a pile also arrives from a drop, from a
            // container and from a conversation, and every one of those paths reaches Update.
            modelSilvered = true;
            SilverModel();
        }

        if (quantity != cachedQuantity)
        {
            singleCoin.SetActive(quantity <= 3);
            multipleCoins.SetActive(quantity > 3);
            cachedQuantity = quantity;
        }
        base.Update();
    }
}
