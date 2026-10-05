using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The icons of the spells and of the effects of worn items, built once from the player's own data.
/// </summary>
/// <remarks>
/// SPELLS.GR has 21 icons, 0 to 20, and every one of them is taken. The effects that had none - Poison
/// Resistance, the two Magic Protections, the two Regenerations and the dragon skin boots - get one
/// here, numbered from 21 on. The ones made from an icon of the original are made from the player's
/// copy of it, with a colour swap or a mask, so nothing of the original art is shipped; the textures
/// on the Magic prefab hold only masks and pixels drawn for this.
///
/// Some icons move: a band of their colours steps round, as the original does with fire and water.
/// All of them, the Light icon and the lit lights in the inventory step together on one clock,
/// <see cref="Utils.ColourCycleStep"/>.
/// </remarks>
public static class SpellIcons
{
    public const int PoisonResistance = 21;
    public const int MagicProtection = 22;
    public const int Regeneration = 23;
    public const int ManaRegeneration = 24;
    public const int LavaResistance = 25;

    private const int WaterWalk = 3;
    private const int Flameproof = 10;
    private const int Light = 17;
    private const int Daylight = 20;

    private static Texture2D[][] icons;

    /// <summary>The icon as it is now, the current step of its animation if it has one.</summary>
    public static Texture2D Get(int icon)
    {
        if (icons == null || icon < 0 || icon >= icons.Length || icons[icon] == null)
        {
            return null;
        }

        Texture2D[] frames = icons[icon];
        return frames[Utils.ColourCycleStep() % frames.Length];
    }

    public static void Build(Texture2D sphereMask, Texture2D sphereBorder, Texture2D poisonResistanceOverlay,
                             Texture2D magicProtectionSpark, Texture2D regenerationFrames)
    {
        if (icons != null)
        {
            return;
        }

        Texture2D[] original = DataLoader.sDataLoader.spellsTex;
        icons = new Texture2D[LavaResistance + 1][];
        for (int i = 0; i < original.Length && i < icons.Length; ++i)
        {
            icons[i] = new[] { original[i] };
        }

        icons[Light] = DataLoader.sDataLoader.lightSpellTex;

        Image waterWalk = Image.FromGr("../Data/spells.gr", WaterWalk);
        Image flameproof = Image.FromGr("../Data/spells.gr", Flameproof);

        icons[WaterWalk] = waterWalk.Cycle(Band(197, 7));
        icons[Daylight] = Image.FromGr("../Data/spells.gr", Daylight).Cycle(new[] { 21, 22, 23, 16 });

        // The fire toned down: yellow 5 and orange 6 for darker ones, the red kept.
        Image fire = flameproof.Swapped(new[] { 5, 6 }, new[] { 162, 17 });
        icons[Flameproof] = fire.Cycle(new[] { 162, 17, 7 });

        // Both protections start from Flameproof's sphere without the flames, its edge closed by a
        // few pixels drawn for this.
        Image sphere = flameproof.Masked(sphereMask);
        sphere.Paint(Image.FromTexture(sphereBorder), 0, 0);

        // Poison Resistance: splashes of poison round the sphere.
        Image poison = Image.FromTexture(poisonResistanceOverlay);
        poison.Paint(sphere, 0, 0);
        icons[PoisonResistance] = poison.Cycle(new[] { 83, 85, 88 });

        // Magic Protection: the sphere in front of a flame, a spark and a flash of lightning from ANIMO.GR,
        // each with a second frame: the next frame of its own animation for the flame and the lightning,
        // and for the spark one drawn for this, ANIMO's next being far bigger. Four steps: the three at
        // rest, then the spark, the lightning and the flame in turn at their second frame. The two flames
        // differ in size and are centred on the same spot; the two lightnings share a canvas.
        Image[] flame = { Image.FromGr("../Data/animo.gr", 32), Image.FromGr("../Data/animo.gr", 31) };
        Image[] lightning = { Image.FromGr("../Data/animo.gr", 50), Image.FromGr("../Data/animo.gr", 51) };
        Image sparkAtRest = Image.FromGr("../Data/animo.gr", 45);
        Image sparkLit = Image.FromTexture(magicProtectionSpark);
        int[,] steps = { { 0, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 }, { 1, 0, 0 } };
        icons[MagicProtection] = new Texture2D[steps.GetLength(0)];
        for (int i = 0; i < steps.GetLength(0); ++i)
        {
            Image magic = Image.Empty(flameproof.width, flameproof.height);
            Image f = flame[steps[i, 0]];
            magic.Paint(f, 5 - f.width / 2, 13 - f.height / 2);
            if (steps[i, 1] == 0)
            {
                magic.Paint(sparkAtRest, -8, -6);
            }
            else
            {
                magic.Paint(sparkLit, -2, 1);
            }

            magic.Paint(lightning[steps[i, 2]], 1, -6);
            magic.Paint(sphere, 0, 0);
            icons[MagicProtection][i] = magic.ToTexture();
        }

        // A flask filling, drawn for this; the mana one is the same with the reds of the health flask
        // turned into the blues of the mana flask. The original pairs them sixteen apart: in FLASKS.GR
        // every red 177-188 of the health flask is blue 193-204 at the same spot of the mana flask.
        Image flasks = Image.FromTexture(regenerationFrames);
        int frameCount = flasks.width / 16;
        icons[Regeneration] = new Texture2D[frameCount];
        icons[ManaRegeneration] = new Texture2D[frameCount];
        int[] reds = Band(177, 15);
        int[] blues = Band(193, 15);
        for (int f = 0; f < frameCount; ++f)
        {
            Image frame = flasks.Cut(16 * f, 0, 16, flasks.height);
            icons[Regeneration][f] = frame.ToTexture();
            icons[ManaRegeneration][f] = frame.Swapped(reds, blues).ToTexture();
        }

        // The dragon skin boots: the water of Water Walk turned into lava, cycling like it, without the
        // step where most of it is the brightest yellow.
        icons[LavaResistance] = waterWalk.Swapped(new[] { 197, 199, 200, 201, 203 }, new[] { 16, 19, 184, 187, 187 })
                                         .Cycle(Band(16, 8), skip: 2);
    }

    private static int[] Band(int start, int length)
    {
        int[] band = new int[length];
        for (int i = 0; i < length; ++i)
        {
            band[i] = start + i;
        }

        return band;
    }

    /// <summary>An image as palette indices, top row first; index 0 is transparent, as in the original.</summary>
    private sealed class Image
    {
        public readonly int width;
        public readonly int height;
        private readonly int[] pixels;

        private Image(int width, int height)
        {
            this.width = width;
            this.height = height;
            pixels = new int[width * height];
        }

        private Image Copy()
        {
            Image copy = new(width, height);
            pixels.CopyTo(copy.pixels, 0);
            return copy;
        }

        public static Image Empty(int width, int height)
        {
            return new Image(width, height);
        }

        public static Image FromGr(string relativePath, int index)
        {
            byte[] indices = GraphicsLoader.GetImageIndices(relativePath, index, out int w, out int h);
            Image image = new(w, h);
            for (int y = 0; y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    // the loader keeps the bottom row first, as a texture does
                    image.pixels[y * w + x] = indices[(h - 1 - y) * w + x];
                }
            }

            return image;
        }

        /// <summary>
        /// A texture drawn in the game's palette, read back as indices: a pixel at least half opaque
        /// takes the nearest colour of palette 0 other than 0, so a black drawn with colour 0 stays black.
        /// </summary>
        public static Image FromTexture(Texture2D texture)
        {
            Color32[] colours = texture.GetPixels32();
            Color[] palette = DataLoader.sDataLoader.palettes[0].colors;
            Image image = new(texture.width, texture.height);
            for (int y = 0; y < image.height; ++y)
            {
                for (int x = 0; x < image.width; ++x)
                {
                    Color32 c = colours[(image.height - 1 - y) * image.width + x];
                    image.pixels[y * image.width + x] = c.a < 128 ? 0 : Nearest(palette, c);
                }
            }

            return image;
        }

        private static int Nearest(Color[] palette, Color32 c)
        {
            int best = 1;
            int bestDistance = int.MaxValue;
            for (int i = 1; i < palette.Length; ++i)
            {
                Color32 p = palette[i];
                int dr = p.r - c.r;
                int dg = p.g - c.g;
                int db = p.b - c.b;
                int distance = dr * dr + dg * dg + db * db;
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>A copy keeping only the pixels where the mask is at least half opaque.</summary>
        public Image Masked(Texture2D mask)
        {
            Color32[] alpha = mask.GetPixels32();
            Image copy = Copy();
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    if (alpha[(height - 1 - y) * width + x].a < 128)
                    {
                        copy.pixels[y * width + x] = 0;
                    }
                }
            }

            return copy;
        }

        /// <summary>Draws the opaque pixels of another image over this one, its top left corner at (x, y).</summary>
        public void Paint(Image other, int left, int top)
        {
            for (int y = 0; y < other.height; ++y)
            {
                for (int x = 0; x < other.width; ++x)
                {
                    int v = other.pixels[y * other.width + x];
                    int tx = left + x;
                    int ty = top + y;
                    if (v != 0 && tx >= 0 && tx < width && ty >= 0 && ty < height)
                    {
                        pixels[ty * width + tx] = v;
                    }
                }
            }
        }

        public Image Cut(int left, int top, int w, int h)
        {
            Image cut = new(w, h);
            cut.Paint(this, -left, -top);
            return cut;
        }

        public Image Swapped(int[] from, int[] to)
        {
            Image copy = Copy();
            for (int p = 0; p < pixels.Length; ++p)
            {
                int k = System.Array.IndexOf(from, pixels[p]);
                if (k >= 0)
                {
                    copy.pixels[p] = to[k];
                }
            }

            return copy;
        }

        /// <summary>
        /// One frame per colour of the cycle, but the one skipped: in frame i each of those colours moves
        /// i places on.
        /// </summary>
        public Texture2D[] Cycle(int[] colours, int skip = -1)
        {
            List<Texture2D> frames = new();
            for (int i = 0; i < colours.Length; ++i)
            {
                if (i == skip)
                {
                    continue;
                }

                int[] next = new int[colours.Length];
                for (int k = 0; k < colours.Length; ++k)
                {
                    next[k] = colours[(k + i) % colours.Length];
                }

                frames.Add(Swapped(colours, next).ToTexture());
            }

            return frames.ToArray();
        }

        public Texture2D ToTexture()
        {
            Color[] palette = DataLoader.sDataLoader.palettes[0].colors;
            Color[] colours = new Color[pixels.Length];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    int v = pixels[y * width + x];
                    Color c = palette[v];
                    if (v == 0)
                    {
                        c.a = 0.0f;
                    }

                    colours[(height - 1 - y) * width + x] = c;
                }
            }

            Texture2D texture = new(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(colours);
            texture.Apply();
            return texture;
        }
    }
}
