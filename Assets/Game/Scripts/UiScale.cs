using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// One scale for every panel drawn in raw pixels.
/// </summary>
/// <remarks>
/// The interface is laid out in the pixels of a 1920x1080 screen and drawn at one uniform scale,
/// so it keeps its shape on any monitor instead of stretching with the aspect ratio. 1920x1080 is
/// the size this project's interface is already drawn at: it is the default window size in the
/// project settings, and the in game panels are sized in raw pixels to suit it.
///
/// The scale has three ranges. From 1366x768 to 1920x1080 it is 1: the panels keep their size and
/// only move, anchored to the edges they hang from, so a small screen - a handheld's - does not get
/// small panels. Below 1366x768 everything shrinks together by the smaller of the two ratios, so
/// a Steam Deck (1280x800) and 1280x720 both get about 0.94. Above 1920x1080 everything grows
/// together until Max, past which the border grows instead of the interface, so a very large
/// display does not get a billboard. The scale is 1 at both ends of the middle range, so it never
/// jumps. Every layout therefore has to fit 1366x768 reference pixels.
///
/// A panel is written in reference pixels, measuring the screen with <see cref="Width"/> and
/// <see cref="Height"/> where it used Screen.width and Screen.height, and converts at the draw
/// call: <see cref="R(float, float, float, float)"/> for a rect, <see cref="Font"/> or
/// <see cref="Style"/> for text, <see cref="ToRef(Vector2)"/> for the pointer. The scale is
/// applied to each rect and each font size rather than through GUI.matrix, because IMGUI draws a
/// glyph at its font size and lets the matrix stretch it, which turns the text soft.
/// </remarks>
public static class UiScale
{
    public const float RefWidth = 1920f;
    public const float RefHeight = 1080f;
    public const float MinWidth = 1366f;
    public const float MinHeight = 768f;
    public const float Max = 2f;

    /// <summary>Screen pixels per reference pixel. See the remarks.</summary>
    public static float Factor
    {
        get
        {
            float below = Mathf.Min(Screen.width / MinWidth, Screen.height / MinHeight);
            if (below < 1f)
            {
                return below;
            }

            return Mathf.Clamp(Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight), 1f, Max);
        }
    }

    /// <summary>
    /// The scale that fits a whole 1920x1080 page on the screen, for the one page still laid out
    /// that way (the music page of the Setup screen); everything else uses <see cref="Factor"/>.
    /// </summary>
    public static float Fit => Mathf.Min(Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight), Max);

    /// <summary>The screen's width in reference pixels: what Screen.width is to a panel laid out at 1920x1080.</summary>
    public static float Width => Screen.width / Factor;

    /// <summary>The screen's height in reference pixels.</summary>
    public static float Height => Screen.height / Factor;

    public static float S(float referencePixels)
    {
        return referencePixels * Factor;
    }

    public static Rect R(float x, float y, float width, float height)
    {
        float f = Factor;
        return new Rect(x * f, y * f, width * f, height * f);
    }

    public static Rect R(Rect reference)
    {
        return R(reference.x, reference.y, reference.width, reference.height);
    }

    /// <summary>A rect on screen back in reference pixels.</summary>
    public static Rect ToRef(Rect screen)
    {
        float f = Factor;
        return new Rect(screen.x / f, screen.y / f, screen.width / f, screen.height / f);
    }

    /// <summary>A point on screen, such as the pointer in GUI space, in reference pixels.</summary>
    public static Vector2 ToRef(Vector2 screen)
    {
        return screen / Factor;
    }

    /// <summary>A font size in reference pixels as it is drawn; never below 1, which IMGUI reads as the font's own size.</summary>
    public static int Font(float referenceSize)
    {
        return Mathf.Max(1, Mathf.RoundToInt(referenceSize * Factor));
    }

    private struct ScaledStyle
    {
        public GUIStyle Style;
        public float Factor;
    }

    // By source, then by size, since a panel often draws one style at two or three sizes in a frame.
    // Weak on the source: some panels make their styles anew every frame, and those copies must go
    // with them.
    private static readonly ConditionalWeakTable<GUIStyle, Dictionary<int, ScaledStyle>> scaledStyles =
        new ConditionalWeakTable<GUIStyle, Dictionary<int, ScaledStyle>>();

    /// <summary>
    /// A copy of <paramref name="style"/> with its font and padding scaled, for styles set in the
    /// inspector or shared between panels. Made once for each source and size, and again when the scale changes;
    /// the settings callers change from frame to frame - colour, alignment, wrapping, font style,
    /// padding - are copied on every call.
    /// </summary>
    public static GUIStyle Style(GUIStyle style)
    {
        int size = style.fontSize != 0 ? style.fontSize : style.font != null ? style.font.fontSize : 0;
        float f = Factor;
        Dictionary<int, ScaledStyle> bySize = scaledStyles.GetOrCreateValue(style);
        if (!bySize.TryGetValue(size, out ScaledStyle scaled) || scaled.Factor != f)
        {
            GUIStyle copy = new GUIStyle(style) { fontSize = size > 0 ? Font(size) : 0 };
            scaled = new ScaledStyle { Style = copy, Factor = f };
            bySize[size] = scaled;
        }

        GUIStyle s = scaled.Style;
        s.font = style.font;
        s.normal.textColor = style.normal.textColor;
        s.alignment = style.alignment;
        s.wordWrap = style.wordWrap;
        s.fontStyle = style.fontStyle;
        s.richText = style.richText;
        s.clipping = style.clipping;
        s.padding = Scaled(style.padding);
        s.contentOffset = style.contentOffset * f;
        return s;
    }

    // --- Pixel art at a scale that is not whole -------------------------------------------------
    //
    // The panels are the original's pixel art, point filtered: at 1920x1080 a texel is 3 x 3.6 screen
    // pixels, as it always was. Above that the texels come out of uneven sizes and their edges step,
    // which at 1440p and up reads as jagged. So above scale 1 a texture is drawn through a copy made
    // larger by repeating its texels, a whole number of times on each axis, with bilinear filtering:
    // the copy is stretched by EdgeSoftness or a little more, so each edge is softened over that many
    // screen pixels while the texels stay flat ("sharp bilinear"). At scale 1 and below - a handheld,
    // where the steps are a pixel and the user found the point filtering right - nothing changes.

    /// <summary>
    /// Screen pixels each edge of the pixel art is softened over, above scale 1, at the least: the copy's
    /// texels are repeated as often as leaves at least this much stretch. 1 was too little (30 October 2026).
    /// </summary>
    private const float EdgeSoftness = 1.5f;

    private const int MaxPrescale = 8;
    private const int MaxPrescaledSize = 2048;

    private struct Prescaled
    {
        public Texture2D Texture;
        public uint UpdateCount;
    }

    private static readonly ConditionalWeakTable<Texture2D, Dictionary<int, Prescaled>> prescaled =
        new ConditionalWeakTable<Texture2D, Dictionary<int, Prescaled>>();

    /// <summary>GUI.DrawTexture through <see cref="Sharp"/>.</summary>
    public static void DrawTexture(Rect screen, Texture texture, ScaleMode scaleMode = ScaleMode.StretchToFill, bool alphaBlend = true)
    {
        GUI.DrawTexture(screen, Sharp(texture, screen.width, screen.height), scaleMode, alphaBlend);
    }

    /// <summary>GUI.DrawTextureWithTexCoords through <see cref="Sharp"/>; the coordinates are the same on the copy.</summary>
    public static void DrawTextureWithTexCoords(Rect screen, Texture texture, Rect texCoords)
    {
        GUI.DrawTextureWithTexCoords(screen, Sharp(texture, screen.width / texCoords.width, screen.height / texCoords.height), texCoords);
    }

    /// <summary>
    /// The texture to draw a point filtered texture with when it is drawn width x height screen pixels whole: itself
    /// at scale 1 and below, above it a copy with its texels repeated (RepeatCount) and bilinear filtered. Textures that
    /// cannot be read, or are not point filtered, are drawn as they are.
    /// </summary>
    public static Texture Sharp(Texture texture, float width, float height)
    {
        if (!(texture is Texture2D source) || Factor <= 1f || source.filterMode != FilterMode.Point || !source.isReadable)
        {
            return texture;
        }

        int kx = RepeatCount(width / source.width, source.width);
        int ky = RepeatCount(height / source.height, source.height);
        int key = kx * (MaxPrescale + 1) + ky;

        Dictionary<int, Prescaled> byFactor = prescaled.GetOrCreateValue(source);
        if (byFactor.TryGetValue(key, out Prescaled copy) && copy.Texture != null && copy.UpdateCount == source.updateCount)
        {
            return copy.Texture;
        }

        if (copy.Texture != null)
        {
            Object.Destroy(copy.Texture);
        }
        copy = new Prescaled { Texture = Repeat(source, kx, ky), UpdateCount = source.updateCount };
        byFactor[key] = copy;
        return copy.Texture;
    }

    /// <summary>How often to repeat a texel on one axis: as often as leaves the copy stretched by EdgeSoftness at the least.</summary>
    private static int RepeatCount(float magnification, int size)
    {
        int k = Mathf.Clamp(Mathf.FloorToInt(magnification / EdgeSoftness), 1, MaxPrescale);
        while (k > 1 && size * k > MaxPrescaledSize)
        {
            --k;
        }
        return k;
    }

    /// <summary>
    /// The texture with each texel repeated kx times across and ky down, bilinear filtered. A clear texel takes the colour of an
    /// opaque neighbour first, still clear, so the filtering does not draw a dark fringe round the art.
    /// </summary>
    private static Texture2D Repeat(Texture2D source, int kx, int ky)
    {
        int w = source.width;
        int h = source.height;
        Color[] pixels = source.GetPixels();
        Color[] bled = (Color[])pixels.Clone();
        for (int y = 0; y < h; ++y)
        {
            for (int x = 0; x < w; ++x)
            {
                int i = y * w + x;
                if (pixels[i].a > 0f)
                {
                    continue;
                }
                int n = x > 0 && pixels[i - 1].a > 0f ? i - 1
                    : x < w - 1 && pixels[i + 1].a > 0f ? i + 1
                    : y > 0 && pixels[i - w].a > 0f ? i - w
                    : y < h - 1 && pixels[i + w].a > 0f ? i + w
                    : -1;
                if (n >= 0)
                {
                    bled[i] = new Color(pixels[n].r, pixels[n].g, pixels[n].b, 0f);
                }
            }
        }

        Color[] big = new Color[w * kx * h * ky];
        for (int y = 0; y < h * ky; ++y)
        {
            int row = (y / ky) * w;
            for (int x = 0; x < w * kx; ++x)
            {
                big[y * w * kx + x] = bled[row + x / kx];
            }
        }

        Texture2D copy = new Texture2D(w * kx, h * ky, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = source.wrapMode
        };
        copy.SetPixels(big);
        copy.Apply();
        return copy;
    }

    private static RectOffset Scaled(RectOffset o)
    {
        float f = Factor;
        return new RectOffset(Mathf.RoundToInt(o.left * f), Mathf.RoundToInt(o.right * f),
            Mathf.RoundToInt(o.top * f), Mathf.RoundToInt(o.bottom * f));
    }
}
