using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The spells the player knows, on a sheet of parchment that comes in on the right while the magic
/// panel is open: by circle, the first four in the left column and the rest in the right, each spell
/// as its name and then its runes, as the printed manual lists them. The original has no such list -
/// players keep their own notes - so this is an addition; which spells it holds is up to
/// <see cref="Magic"/>.
/// </summary>
/// <remarks>
/// The parchment is cut from the original's blank map, BLNKMAP.BYT, the player's own copy, so nothing
/// of the original art is shipped. Its right side has the folded corners that turn the map's pages and
/// the map's buttons, so the sheet takes the map's left edge and corners and turns them round for its
/// right side; the top and bottom are the map's own, and the middle is the map's parchment. Then it is
/// turned upside down, so the shallower of the two torn edges runs over the headings. It is as large
/// as the stats panel and drawn at its scale, which is the original's pixel shape.
/// </remarks>
public class SpellListPanel
{
    /// <summary>
    /// One of the 48 spells that can be cast. Only the known ones are written; the others count in
    /// the layout, so a spell learned later fits where the list keeps a place for it.
    /// </summary>
    public struct Entry
    {
        public int circle;
        public string runes;
        public string name;
        public bool known;
    }

    // The sheet in the original's pixels, and the scale it is drawn at: those of the stats panel as it
    // is drawn, 82 x 112 at 5 x 6 (StatsPanel), so the two panels are the same size on screen.
    private const int SheetWidth = 82;
    private const int SheetHeight = 112;
    private const float ScaleX = 5;
    private const float ScaleY = 6;

    // How much of the map's edge the sheet takes: the torn border and the darker band inside it are
    // about ten pixels deep all round; twelve keeps the plain parchment out of the corners.
    private const int EdgeDepth = 12;

    // In BLNKMAP.BYT, colour 3 is the black round the torn edge, and only there.
    private const int OutsideColour = 3;
    private const int MapWidth = 320;
    private const int MapHeight = 200;
    private const int MapPalette = 1;

    // The margins round the text, in screen pixels, from the edges of the sheet as drawn, set by eye in
    // play: the left column starts at the left margin and the right one ends at the right margin, and
    // the gap between them is what is left.
    private const float TopMargin = 19;
    private const float LeftMargin = 38;
    private const float RightMargin = 26;

    // The rune glyphs of Britannian Runes I are 0.588 of its em tall, the capitals of the Avatar font
    // 0.733 of its own (the fonts' tables): at 0.733 / 0.588 the runes would be as tall as the
    // capitals, and they are drawn 30% larger than that, and bold, to stand out.
    private const float NameCapHeight = 0.733f;
    private const float RuneScale = 1.3f * NameCapHeight / 0.588f;

    // The runes are in the orange of the menus, #BB7B01: the yellow of the runes carved on the
    // runestones (OBJECTS.GR 232-255, colour 161 of palette 0, #F2AC0C) is too bright on the parchment.
    private static readonly Color RuneInk = new(0xbb / 255f, 0x7b / 255f, 0x01 / 255f);

    // How tall a rune glyph is, in ems of its font: 61 to 2470 of 4096 above the baseline (the glyphs'
    // bounding boxes), so its middle is at 1265.5.
    private const float RuneGlyphHeight = (2470f - 61) / 4096;
    private const float RuneGlyphMiddle = (2470f + 61) / 2 / 4096;

    // Baselines from the top of a line, in ems, from the fonts' ascenders: Avatar 1948 of 2048, the
    // runes 2863 of 4096. Unity lays a dynamic font's line from its ascender (ascentCalculationMode 1
    // on both fonts), so a rune line moves by the difference to put the runes' middle on the middle
    // of the names' capitals.
    private const float NameAscent = 1948f / 2048;
    private const float RuneAscent = 2863f / 4096;

    // The size of the names and headings, and the smallest a long one is made to fit its place, chosen
    // in play: below it the Avatar font is hard to read. A name too long even at the smallest size is
    // cut short at the runes; the cure is a shorter form for the list (Magic.spellListShortNames).
    private const int FontSize = 16;
    private const int MinFontSize = 11;

    // The columns are fixed, so a longer name - another language's - is written smaller, alone, and
    // nothing moves. Their widths, in screen pixels, are the English list's at FontSize as Unity
    // measures it: the longest names, Strengthen Door on the left (111.14) and Missile Protection on the
    // right (118.78), and the widest runes, three in bold (40.5). A column is an indent, its names, a
    // gap and the runes.
    private const float LeftNamesWidth = 111.5f;
    private const float RightNamesWidth = 119;
    private const float RunesWidth = 40.5f;

    // The ink of the names and headings, #190E06, chosen in play: a very dark brown, near the map's own
    // writing (MapScreen, 0.2, 0.1, 0.1).
    private static readonly Color Ink = new(0x19 / 255f, 0x0e / 255f, 0x06 / 255f);

    // The rows are 10% taller than the font's own line, to space the list out.
    private const float LineSpacing = 1.1f;

    private Texture2D sheet;

    private List<Entry> laidOutEntries;
    private float lineStep;
    private float headingGap;
    // the size each entry's name is written at, in the entries' order, and each circle's heading
    private readonly List<int> nameSizes = new();
    private readonly int[] headingSizes = new int[2 * LastCircleOnTheLeft + 1];
    // Made on the first draw: Unity refuses a GUIStyle's settings from a MonoBehaviour's constructor,
    // and this panel belongs to one, Magic.
    private GUIStyle nameStyle;
    private GUIStyle headingStyle;
    private GUIStyle runeStyle;

    /// <summary>The first four circles go in the left column, the others in the right.</summary>
    private const int LastCircleOnTheLeft = 4;

    /// <summary>
    /// Every circle has six spells and keeps six rows, empty at the bottom until the spell it lacks is
    /// learned, so the circles never move.
    /// </summary>
    private const int RowsPerCircle = 6;

    /// <summary>
    /// The sheet as drawn, coming in from the right edge as the magic panel comes in from the left, in
    /// reference pixels (UiScale). The layout is measured with the styles at their reference size and
    /// drawn with them scaled.
    /// </summary>
    public static Rect GetRect(float lerpIn)
    {
        float w = ScaleX * SheetWidth;
        float h = ScaleY * SheetHeight;
        float x = UiScale.Width - lerpIn * (w + PlayerPanelState.PanelMargin);
        return new Rect(x, PlayerPanelState.PanelMargin, w, h);
    }

    /// <summary>
    /// Draws the known spells. The entries come sorted by circle, then by name; each circle's known
    /// spells fill its rows from the top, so a spell learned takes its place in the order and the
    /// empty row goes to the bottom.
    /// </summary>
    public void Draw(float lerpIn, List<Entry> entries, Font nameFont, Font runeFont)
    {
        Rect rect = GetRect(lerpIn);
        GuiInput.RegisterBlockingRect(UiScale.R(rect));

        if (sheet == null)
        {
            sheet = BuildSheet();
        }

        if (sheet != null)
        {
            UiScale.DrawTexture(UiScale.R(rect), sheet);
        }

        if (nameStyle == null)
        {
            nameStyle = new GUIStyle { fontStyle = FontStyle.Normal, wordWrap = false, clipping = TextClipping.Clip };
            headingStyle = new GUIStyle { fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
            runeStyle = new GUIStyle { fontStyle = FontStyle.Bold, wordWrap = false };
        }

        nameStyle.font = nameFont;
        headingStyle.font = nameFont;
        runeStyle.font = runeFont;
        nameStyle.normal.textColor = Ink;
        headingStyle.normal.textColor = Ink;
        runeStyle.normal.textColor = RuneInk;

        float innerX = rect.x + LeftMargin;
        float innerY = rect.y + TopMargin;
        float innerWidth = rect.width - LeftMargin - RightMargin;

        if (!ReferenceEquals(entries, laidOutEntries))
        {
            LayOut(entries);
            laidOutEntries = entries;
        }

        runeStyle.fontSize = RuneFontSize;

        // the left column from the left margin, the right one up to the right margin
        float leftX = innerX;
        float rightX = innerX + innerWidth - ColumnWidth(true);

        float runeDrop = Mathf.Round(TextMiddle(FontSize) - (RuneAscent - RuneGlyphMiddle) * runeStyle.fontSize);

        int circle = 0;
        int row = 0;
        for (int i = 0; i < entries.Count; ++i)
        {
            Entry entry = entries[i];
            if (entry.circle != circle)
            {
                circle = entry.circle;
                row = 0;
                bool right = circle > LastCircleOnTheLeft;
                headingStyle.fontSize = headingSizes[circle];
                GUI.Label(UiScale.R(right ? rightX : leftX, CircleTop(circle, innerY) + TextDrop(headingSizes[circle]), ColumnWidth(right), lineStep),
                    CircleHeading(circle), UiScale.Style(headingStyle));
            }

            if (!entry.known)
            {
                continue;
            }

            bool onTheRight = circle > LastCircleOnTheLeft;
            float nameX = (onTheRight ? rightX : leftX) + NameIndent;
            float namesWidth = NamesWidth(onTheRight);
            float y = CircleTop(circle, innerY) + lineStep * (1 + row) + headingGap;
            nameStyle.fontSize = nameSizes[i];
            GUI.Label(UiScale.R(nameX, y + TextDrop(nameSizes[i]), namesWidth, lineStep), entry.name, UiScale.Style(nameStyle));
            GUI.Label(UiScale.R(nameX + namesWidth + RuneGap, y + runeDrop, RunesWidth, lineStep), entry.runes, UiScale.Style(runeStyle));
            ++row;
        }
    }

    /// <summary>
    /// Where a circle's heading goes: the circles of a column one under the other, each a heading, a
    /// gap under it and six rows.
    /// </summary>
    private float CircleTop(int circle, float innerY)
    {
        int place = (circle - 1) % LastCircleOnTheLeft;
        return innerY + place * (lineStep * (1 + RowsPerCircle) + headingGap + GroupGap);
    }

    /// <summary>"First Circle" and so on; the ordinal is the strings file's, block 1 from 411.</summary>
    private static string CircleHeading(int circle)
    {
        string ordinal = StringLoader.GetString(1, 410 + circle);
        if (ordinal.Length > 0)
        {
            ordinal = char.ToUpperInvariant(ordinal[0]) + ordinal.Substring(1);
        }

        return ordinal + " Circle";
    }

    // The gap under a heading: half the space between the runes of two rows.
    private const float HeadingGapShare = 0.5f;

    // between the longest name of a column and the runes: about a third of the font size
    private const float RuneGap = 0.65f * 0.5f * FontSize;
    // the names start a little in from the heading: one pixel of the sheet
    private const float NameIndent = ScaleX;
    // between the last row of a circle and the next circle's heading
    private const float GroupGap = 5;

    // The runes are not translated and keep their size.
    private static int RuneFontSize => Mathf.RoundToInt(FontSize * RuneScale);

    private static float NamesWidth(bool right) => right ? RightNamesWidth : LeftNamesWidth;

    private static float ColumnWidth(bool right) => NameIndent + NamesWidth(right) + RuneGap + RunesWidth;

    /// <summary>How far below the top of a line the middle of the capitals is, at a size.</summary>
    private static float TextMiddle(int size) => (NameAscent - NameCapHeight / 2) * size;

    /// <summary>
    /// How far down a smaller name or heading is moved to keep its middle where the full size has it:
    /// the rows are those of the full size, whatever the text's size.
    /// </summary>
    private static float TextDrop(int size) => Mathf.Round(TextMiddle(FontSize) - TextMiddle(size));

    /// <summary>
    /// Measures the rows at the full size, and gives each name and each heading its own size: the full
    /// one, or the largest at which it fits its place, so only a long name is written smaller. Every
    /// spell is measured, known or not, so nothing changes when one is learned. Under each heading goes
    /// a gap half as tall as the space between the runes of two rows.
    /// </summary>
    private void LayOut(List<Entry> entries)
    {
        nameStyle.fontSize = FontSize;
        runeStyle.fontSize = RuneFontSize;
        lineStep = Mathf.Ceil(LineSpacing * nameStyle.CalcSize(new GUIContent("Ag")).y);
        headingGap = Mathf.Max(0, Mathf.Round(HeadingGapShare * (lineStep - RuneGlyphHeight * runeStyle.fontSize)));

        nameSizes.Clear();
        foreach (Entry entry in entries)
        {
            nameSizes.Add(FittingSize(nameStyle, entry.name, NamesWidth(entry.circle > LastCircleOnTheLeft)));
        }

        for (int circle = 1; circle <= 2 * LastCircleOnTheLeft; ++circle)
        {
            headingSizes[circle] = FittingSize(headingStyle, CircleHeading(circle), ColumnWidth(circle > LastCircleOnTheLeft));
        }
    }

    /// <summary>The largest size, from the full one down to the smallest, at which a text fits a width.</summary>
    private static int FittingSize(GUIStyle style, string text, float width)
    {
        int size = FontSize;
        style.fontSize = size;
        while (size > MinFontSize && style.CalcSize(new GUIContent(text)).x > width)
        {
            --size;
            style.fontSize = size;
        }

        return size;
    }

    /// <summary>
    /// Cuts the sheet out of the blank map: its top left, the top and left edges and the parchment
    /// from the map's top left; its bottom from the map's bottom edge; its right side from its own left
    /// side, turned round. Colour 3, outside the torn edge, is left clear. The whole is then turned
    /// upside down: the map's top edge is torn much deeper than its bottom, and the shallow one makes a
    /// better top over the headings.
    /// </summary>
    private static Texture2D BuildSheet()
    {
        Stream map = new("../Data/blnkmap.byt");
        byte[] indices = new byte[MapWidth * MapHeight];
        for (int i = 0; i < indices.Length; ++i)
        {
            indices[i] = map.GetByte();
        }

        Color[] palette = DataLoader.sDataLoader.palettes[MapPalette].colors;
        Color[] pixels = new Color[SheetWidth * SheetHeight];
        for (int y = 0; y < SheetHeight; ++y)
        {
            int mapY = y < SheetHeight - EdgeDepth ? y : MapHeight - (SheetHeight - y);
            for (int x = 0; x < SheetWidth; ++x)
            {
                int mapX = x < SheetWidth - EdgeDepth ? x : SheetWidth - 1 - x;
                int index = indices[mapY * MapWidth + mapX];
                Color colour = palette[index];
                colour.a = index == OutsideColour ? 0 : 1;
                // Unity's rows run from the bottom, so the sheet's top row y going to row y counted from
                // the bottom turns the sheet upside down
                pixels[y * SheetWidth + x] = colour;
            }
        }

        Texture2D tex = new(SheetWidth, SheetHeight, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
