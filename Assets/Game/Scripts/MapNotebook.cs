using System.Collections.Generic;
using UnityEngine;
using Screen = UnityEngine.Device.Screen;

/// <summary>
/// The notebook, on the map's page 0, before Level 1: a list of things to do. A line is written in the TO DO section
/// beside an empty box; ticking the box moves it down to DONE, where it can no longer be changed, and taking the tick
/// away moves it back to the end of TO DO. TO DO grows with its lines and DONE follows with every line ticked, the
/// newest first; the page scrolls when they are more than it holds. A line is one line of the page at most: the
/// on-screen keyboard takes no character that would not fit.
/// </summary>
/// <remarks>
/// The original has no notebook - players kept theirs on paper - so this is a convenience of the remake, kept in the
/// save with the map's notes. Its controls are the map's: the quill, the left click or the pad's Select on a box to
/// tick it and on a line to write it, the right click or Erase note to take a line away, the d-pad's Next note from
/// line to line. The mouse wheel scrolls, and the pad's quill pushed past the top or the bottom of the list. Written in
/// the map notes' font size, so that the page holds as many lines as it can.
/// </remarks>
public class MapNotebook
{
    /// <summary>The map page it is on: page 0, which no level uses.</summary>
    public const int Page = 0;

    /// <summary>The map's font, which the lines are written and measured in; set by <see cref="MapScreen"/>.</summary>
    public Font font;

    private readonly List<string> todo = new List<string>();
    // newest first
    private readonly List<string> done = new List<string>();

    // Where the list is on the page, in map pixels (320 x 200, y downwards): inside the torn edge, and left of the
    // compass, the eraser and the Close button in the page's right column (BLNKMAP.BYT).
    private const float ViewLeft = 14f;
    private const float ViewTop = 14f;
    private const float ViewRight = 260f;
    private const float ViewBottom = 188f;
    private const float ViewHeight = ViewBottom - ViewTop;

    // A line: its box, then its text, which may run to the right edge of the list.
    private const float BoxLeft = 17f;
    private const float TextLeft = 25f;
    private const float TextRight = ViewRight - 2f;

    // The font's size in map pixels, the map's notes' (the user's, 22 November 2026), and the heights of a line and a
    // heading.
    private const float FontHeight = 5f;
    private const float HeadingFontHeight = 8f;
    private const float RowHeight = 7.5f;
    private const float HeadingHeight = 12f;
    // between the last line of TO DO and the heading of DONE, with a rule across its middle
    private const float SectionGap = 10f;

    // The box is drawn in half map pixels, as small squares in the ink, the way the map draws its walls: 8 a side, its
    // line 1 thick.
    private const float Unit = 0.5f;
    private const int BoxUnits = 8;

    // The tick is a stroke of a pen, as a hand would make it (the user's, 22 November 2026): a short stroke down,
    // pressing harder, and a long one up and out past the box's top right corner, lifting off. Two quadratic curves in
    // a unit square (y down) with the pen's width along them, drawn once into a texture - white, the ink's coverage in
    // its alpha - and tinted when drawn. The square is TickSize map pixels a side, put so that the stroke's lowest
    // point falls inside the box near its bottom.
    private static readonly Vector2[] TickDown = { new Vector2(0.06f, 0.52f), new Vector2(0.20f, 0.62f), new Vector2(0.34f, 0.90f) };
    private static readonly Vector2[] TickUp = { new Vector2(0.34f, 0.90f), new Vector2(0.52f, 0.42f), new Vector2(0.96f, 0.04f) };
    private const float TickSize = 6.5f;
    private static readonly Vector2 TickOffset = new Vector2(-0.6f, -2.55f);
    private const int TickTextureSize = 64;
    private static Texture2D tickTexture;

    // What is done, and the empty line to write a new one, in faded ink.
    private const float FadedAlpha = 0.55f;

    // a notch of the wheel, and the speed of the pad's quill held past the list's edge, in map pixels
    private const float WheelStep = 2 * RowHeight;
    private const float EdgeScrollSpeed = 60f;

    // The size the lines are measured at, and the most characters a line takes, whatever their width.
    private const int MeasureFontSize = 100;
    private const int MaxLength = 120;

    private const string AddLineText = "Add note";

    private enum RowKind { Heading, Todo, Add, Done }

    private struct Row
    {
        public RowKind kind;
        // the line's place in its list
        public int index;
        public string heading;
        // in map pixels from the top of the list, unscrolled
        public float top;
        public float height;
    }

    private readonly List<Row> rows = new List<Row>();
    private float ruleY;
    private float contentHeight;
    private float scroll;

    private int hoverRow = -1;
    private bool hoverBox;

    private bool writing;
    // the TO DO line being written, or -1 for a new one
    private int writingIndex;

    private GUIStyle measureStyle;

    /// <summary>True while a line is being written: the on-screen keyboard is up, or about to be.</summary>
    public bool Writing => writing;

    /// <summary>The lines to do, for the save.</summary>
    public List<string> Todo => new List<string>(todo);

    /// <summary>The lines done, newest first, for the save.</summary>
    public List<string> Done => new List<string>(done);

    /// <summary>The notebook of a save, or an empty one for a save made before it existed.</summary>
    public void Load(List<string> savedTodo, List<string> savedDone)
    {
        todo.Clear();
        done.Clear();
        AddLines(todo, savedTodo);
        AddLines(done, savedDone);
        writing = false;
        scroll = 0f;
        hoverRow = -1;
        Layout();
    }

    private static void AddLines(List<string> into, List<string> lines)
    {
        if (lines == null)
        {
            return;
        }
        foreach (string line in lines)
        {
            if (!string.IsNullOrEmpty(line))
            {
                into.Add(line);
            }
        }
    }

    /// <summary>Whether a line this long fits on the page: measured in the font, as wide as it is written on a screen
    /// of the map's shape, and narrower on any other, where the font is sized by the shorter side.</summary>
    public bool Fits(string text)
    {
        if (text.Length > MaxLength)
        {
            return false;
        }
        if (font == null)
        {
            return true;
        }
        if (measureStyle == null)
        {
            measureStyle = new GUIStyle { font = font, fontSize = MeasureFontSize };
        }
        float width = measureStyle.CalcSize(new GUIContent(text)).x * FontHeight / MeasureFontSize;
        return width <= TextRight - TextLeft;
    }

    // ---------------------------------------------------------------- the frame

    /// <summary>
    /// Once a frame while the page is shown: the scroll, from the wheel or the pad's quill held past the top or the
    /// bottom of the list, and the line under the quill.
    /// </summary>
    /// <param name="quill">The quill's tip, in map pixels.</param>
    /// <param name="wheel">The mouse wheel's movement this frame, 0 with the pad.</param>
    /// <param name="pad">Whether the pad moves the quill.</param>
    public void Update(Vector2 quill, float wheel, bool pad, float deltaTime)
    {
        Layout();
        if (writing)
        {
            hoverRow = -1;
            return;
        }
        if (wheel != 0f)
        {
            scroll -= Mathf.Sign(wheel) * WheelStep;
        }
        else if (pad && quill.x >= ViewLeft && quill.x <= ViewRight)
        {
            float step = EdgeScrollSpeed * Mathf.Min(deltaTime, 0.1f);
            if (quill.y < ViewTop)
            {
                scroll -= step;
            }
            else if (quill.y > ViewBottom)
            {
                scroll += step;
            }
        }
        ClampScroll();
        hoverRow = RowAt(quill, out hoverBox);
    }

    /// <summary>The left click or the pad's Select: a box ticked or unticked, a line to do written again, or a new one.</summary>
    public void PrimaryAction()
    {
        if (writing || hoverRow < 0 || hoverRow >= rows.Count)
        {
            return;
        }
        Row r = rows[hoverRow];
        switch (r.kind)
        {
            case RowKind.Todo:
                if (hoverBox)
                {
                    string line = todo[r.index];
                    todo.RemoveAt(r.index);
                    done.Insert(0, line);
                }
                else
                {
                    StartWriting(r.index);
                }
                break;
            case RowKind.Add:
                StartWriting(-1);
                break;
            case RowKind.Done:
                // a line done is not written again, only unticked
                if (hoverBox)
                {
                    string line = done[r.index];
                    done.RemoveAt(r.index);
                    todo.Add(line);
                }
                break;
        }
        hoverRow = -1;
        Layout();
    }

    /// <summary>The right click or the pad's Erase note: the line under the quill taken away, to do or done.</summary>
    public void EraseHovered()
    {
        if (writing || hoverRow < 0 || hoverRow >= rows.Count)
        {
            return;
        }
        Row r = rows[hoverRow];
        if (r.kind == RowKind.Todo)
        {
            todo.RemoveAt(r.index);
        }
        else if (r.kind == RowKind.Done)
        {
            done.RemoveAt(r.index);
        }
        hoverRow = -1;
        Layout();
    }

    /// <summary>
    /// The pad's Next note and Previous note: where the quill goes, in map pixels, on the next or the previous line
    /// from the one it is on, or from where it is; it stops at the first and the last, closing and opening the
    /// notebook goes back to the top (the user's, 22 November 2026). The list scrolls to show it.
    /// </summary>
    public Vector2 Jump(int direction, Vector2 quill)
    {
        Layout();
        List<int> lines = new List<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].kind != RowKind.Heading)
            {
                lines.Add(i);
            }
        }
        int k = lines.IndexOf(RowAt(quill, out _));
        if (k >= 0)
        {
            k = Mathf.Clamp(k + direction, 0, lines.Count - 1);
        }
        else
        {
            float y = quill.y - ViewTop + scroll;
            k = direction > 0
                ? lines.FindIndex(i => rows[i].top > y)
                : lines.FindLastIndex(i => rows[i].top + rows[i].height < y);
            if (k < 0)
            {
                // nothing further that way: the last line, or the first
                k = direction > 0 ? lines.Count - 1 : 0;
            }
        }
        return PointAt(lines[k]);
    }

    /// <summary>Where the quill goes when the notebook is opened: on the empty line, ready to write a new one.</summary>
    public Vector2 AddLinePoint()
    {
        Layout();
        return PointAt(rows.FindIndex(r => r.kind == RowKind.Add));
    }

    /// <summary>The map closed, or forced closed, while a line was written: what the keyboard had is not kept.</summary>
    public void StopWriting()
    {
        writing = false;
    }

    private void StartWriting(int index)
    {
        writing = true;
        writingIndex = index;
    }

    /// <summary>
    /// The on-screen keyboard for the line being written, as the map's notes show it, from OnGUI: Enter keeps the
    /// line, Esc leaves it as it was, and a line left empty is not kept (Erase takes a line away).
    /// </summary>
    public void ShowKeyboard(Vector2 position)
    {
        if (!writing)
        {
            return;
        }
        if (KeyboardGUI.sKeyboard == null)
        {
            writing = false;
            return;
        }
        if (KeyboardGUI.sKeyboard.IsVisible())
        {
            return;
        }
        string initial = writingIndex >= 0 && writingIndex < todo.Count ? todo[writingIndex] : "";
        KeyboardGUI.sKeyboard.Show(initial, result =>
        {
            string line = (result ?? "").Trim();
            if (line.Length > 0)
            {
                if (writingIndex >= 0 && writingIndex < todo.Count)
                {
                    todo[writingIndex] = line;
                }
                else
                {
                    todo.Add(line);
                }
            }
            writing = false;
        }, () => writing = false, position, "To do:", true, true, MaxLength, Fits);
    }

    // ---------------------------------------------------------------- the layout

    private void Layout()
    {
        rows.Clear();
        float y = 0f;
        AddRow(RowKind.Heading, 0, "To do", ref y, HeadingHeight);
        for (int i = 0; i < todo.Count; i++)
        {
            AddRow(RowKind.Todo, i, null, ref y, RowHeight);
        }
        AddRow(RowKind.Add, todo.Count, null, ref y, RowHeight);
        ruleY = y + SectionGap / 2;
        y += SectionGap;
        AddRow(RowKind.Heading, 0, "Done", ref y, HeadingHeight);
        for (int i = 0; i < done.Count; i++)
        {
            AddRow(RowKind.Done, i, null, ref y, RowHeight);
        }
        contentHeight = y;
        ClampScroll();
    }

    private void AddRow(RowKind kind, int index, string heading, ref float y, float height)
    {
        rows.Add(new Row { kind = kind, index = index, heading = heading, top = y, height = height });
        y += height;
    }

    private void ClampScroll()
    {
        scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, contentHeight - ViewHeight));
    }

    /// <summary>The line under a point of the page, in map pixels, and whether the point is on its box; -1 for none.</summary>
    private int RowAt(Vector2 point, out bool onBox)
    {
        onBox = false;
        if (point.x < ViewLeft || point.x > ViewRight || point.y < ViewTop || point.y > ViewBottom)
        {
            return -1;
        }
        float y = point.y - ViewTop + scroll;
        for (int i = 0; i < rows.Count; i++)
        {
            Row r = rows[i];
            if (r.kind != RowKind.Heading && y >= r.top && y < r.top + r.height)
            {
                // the empty line has no box to tick: all of it writes
                onBox = r.kind != RowKind.Add && point.x < TextLeft - 1f;
                return i;
            }
        }
        return -1;
    }

    /// <summary>A point on a line's text, in map pixels, after scrolling the list so that the line shows, and its
    /// heading too when it is the first under one.</summary>
    private Vector2 PointAt(int rowIndex)
    {
        Row r = rows[rowIndex];
        float top = rowIndex > 0 && rows[rowIndex - 1].kind == RowKind.Heading ? rows[rowIndex - 1].top : r.top;
        if (top < scroll)
        {
            scroll = top;
        }
        else if (r.top + r.height > scroll + ViewHeight)
        {
            scroll = r.top + r.height - ViewHeight;
        }
        ClampScroll();
        return new Vector2(TextLeft + 4f, ViewTop + r.top - scroll + r.height / 2);
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>The page's list, from OnGUI, in the map's ink and its highlight for what the quill is on.</summary>
    public void Draw(Color ink, Color highlight)
    {
        float sx = Screen.width / 320.0f;
        float sy = Screen.height / 200.0f;
        // sized by the shorter side, so that a line that fits on a screen of the map's shape fits on any
        float scale = Mathf.Min(sx, sy);
        GUIStyle text = new GUIStyle
        {
            font = font,
            fontSize = Mathf.Max(1, Mathf.FloorToInt(FontHeight * scale)),
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
        };
        GUIStyle heading = new GUIStyle(text)
        {
            fontSize = Mathf.Max(1, Mathf.FloorToInt(HeadingFontHeight * scale)),
            fontStyle = FontStyle.Italic,
        };
        Color faded = new Color(ink.r, ink.g, ink.b, ink.a * FadedAlpha);

        Rect view = new Rect(ViewLeft * sx, ViewTop * sy, (ViewRight - ViewLeft) * sx, ViewHeight * sy);
        GUI.BeginGroup(view);
        for (int i = 0; i < rows.Count; i++)
        {
            Row r = rows[i];
            float top = (r.top - scroll) * sy;
            float height = r.height * sy;
            if (top > view.height || top + height < 0f)
            {
                continue;
            }
            Rect textRect = new Rect((TextLeft - ViewLeft) * sx, top, (TextRight - TextLeft) * sx, height);
            bool hovered = i == hoverRow;
            switch (r.kind)
            {
                case RowKind.Heading:
                    heading.normal.textColor = ink;
                    GUI.Label(new Rect((BoxLeft - ViewLeft) * sx, top, (TextRight - BoxLeft) * sx, height), r.heading, heading);
                    break;
                case RowKind.Todo:
                {
                    bool written = writing && writingIndex == r.index;
                    DrawBox(top, height, hovered && hoverBox || written ? highlight : ink, false, sx, sy);
                    text.normal.textColor = hovered && !hoverBox || written ? highlight : ink;
                    GUI.Label(textRect, written ? KeyboardText(todo[r.index]) : todo[r.index], text);
                    break;
                }
                case RowKind.Add:
                {
                    bool written = writing && writingIndex < 0;
                    Color color = hovered || written ? highlight : faded;
                    DrawBox(top, height, color, false, sx, sy);
                    text.normal.textColor = color;
                    text.fontStyle = written ? FontStyle.Normal : FontStyle.Italic;
                    GUI.Label(textRect, written ? KeyboardText("") : AddLineText, text);
                    text.fontStyle = FontStyle.Normal;
                    break;
                }
                case RowKind.Done:
                {
                    DrawBox(top, height, hovered && hoverBox ? highlight : ink, true, sx, sy);
                    Color color = hovered && !hoverBox ? highlight : faded;
                    text.normal.textColor = color;
                    GUI.Label(textRect, done[r.index], text);
                    // struck through, across the text's width
                    float width = Mathf.Min(text.CalcSize(new GUIContent(done[r.index])).x, textRect.width);
                    Fill(new Rect(textRect.x, top + 0.55f * height, width, Mathf.Max(1f, 0.5f * sy)), color);
                    break;
                }
            }
        }
        float rule = (ruleY - scroll) * sy;
        Fill(new Rect((BoxLeft - ViewLeft) * sx, rule, (TextRight - BoxLeft) * sx, Mathf.Max(1f, 0.5f * sy)), faded);
        GUI.EndGroup();
    }

    /// <summary>The line as the on-screen keyboard has it while it is written.</summary>
    private static string KeyboardText(string fallback)
    {
        return KeyboardGUI.sKeyboard != null && KeyboardGUI.sKeyboard.IsVisible() ? KeyboardGUI.sKeyboard.CurrentText : fallback;
    }

    /// <summary>A line's box, at the middle of its row, and its tick if it is done; in the list's group.</summary>
    private static void DrawBox(float rowTop, float rowHeight, Color color, bool ticked, float sx, float sy)
    {
        float ux = Unit * sx;
        float uy = Unit * sy;
        float left = (BoxLeft - ViewLeft) * sx;
        float top = rowTop + 0.5f * rowHeight - 0.5f * BoxUnits * uy;
        Fill(new Rect(left, top, BoxUnits * ux, uy), color);
        Fill(new Rect(left, top + (BoxUnits - 1) * uy, BoxUnits * ux, uy), color);
        Fill(new Rect(left, top, ux, BoxUnits * uy), color);
        Fill(new Rect(left + (BoxUnits - 1) * ux, top, ux, BoxUnits * uy), color);
        if (ticked)
        {
            Color before = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(left + TickOffset.x * sx, top + TickOffset.y * sy, TickSize * sx, TickSize * sy), TickTexture());
            GUI.color = before;
        }
    }

    /// <summary>The tick's texture, made the first time it is drawn.</summary>
    internal static Texture2D TickTexture()
    {
        if (tickTexture != null)
        {
            return tickTexture;
        }
        // the stroke as points along it, each with the pen's half-width there
        List<Vector3> pen = new List<Vector3>();
        const int downPoints = 80;
        const int upPoints = 160;
        for (int i = 0; i < downPoints; i++)
        {
            float t = i / (downPoints - 1f);
            Vector2 p = Quadratic(TickDown, t);
            pen.Add(new Vector3(p.x, p.y, 0.045f + 0.04f * t));
        }
        for (int i = 1; i < upPoints; i++)
        {
            float t = i / (upPoints - 1f);
            Vector2 p = Quadratic(TickUp, t);
            pen.Add(new Vector3(p.x, p.y, 0.085f * Mathf.Pow(1f - t, 1.2f) + 0.012f));
        }

        int n = TickTextureSize;
        Color32[] pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float px = (x + 0.5f) / n;
                float py = (y + 0.5f) / n;
                float cover = 0f;
                foreach (Vector3 point in pen)
                {
                    float dx = px - point.x;
                    float dy = py - point.y;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    cover = Mathf.Max(cover, Mathf.Clamp01((point.z - distance) * n + 0.5f));
                }
                // a texture's rows run upwards
                pixels[(n - 1 - y) * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(cover * 255f));
            }
        }
        tickTexture = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        tickTexture.SetPixels32(pixels);
        tickTexture.Apply();
        return tickTexture;
    }

    private static Vector2 Quadratic(Vector2[] p, float t)
    {
        float u = 1f - t;
        return new Vector2(u * u * p[0].x + 2f * u * t * p[1].x + t * t * p[2].x, u * u * p[0].y + 2f * u * t * p[1].y + t * t * p[2].y);
    }

    private static void Fill(Rect rect, Color color)
    {
        Color before = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = before;
    }
}
