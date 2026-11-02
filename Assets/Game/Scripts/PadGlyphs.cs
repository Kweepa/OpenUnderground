using UnityEngine;
using UnityEngine.InputSystem.DualShock;

/// <summary>
/// Which pad's buttons the game shows: the letters and names of the Xbox pad it was made with, or the symbols and
/// names of a PlayStation pad. One row of CONTROLS > INPUT, Automatic by default.
/// </summary>
/// <remarks>
/// The game's pictures of the face buttons are four PNGs of the Xbox letters (aButton, bButton, xButton, yButton),
/// set on a dozen components in the Inspector. Rather than give each component four more, every place that draws
/// one passes it through <see cref="Face"/>, which hands back the PlayStation symbol drawn in code in the same style
/// - the black rim, the dark grey disc, the symbol in a flat colour - when that is the style in force. The names of
/// the other buttons go through <see cref="Name"/>. The control map draws the PlayStation pad's outline
/// (<see cref="PadOutline"/>) from the same style.
/// </remarks>
public static class PadGlyphs
{
    public const string PrefsKey = "Options_PadGlyphs";

    /// <summary>The row's names, in the order they are kept: 0 Automatic, 1 Xbox, 2 PlayStation.</summary>
    public static readonly string[] StyleNames = { "Automatic", "Xbox", "PlayStation" };

    private const int Automatic = 0;
    private const int Xbox = 1;
    private const int Ps = 2;

    // worked out once a frame: the hints and the map ask for every picture they draw
    private static int resolvedFrame = -1;
    private static bool resolvedPlayStation;

    /// <summary>True when the PlayStation symbols and names are shown.</summary>
    public static bool PlayStation
    {
        get
        {
            if (resolvedFrame != Time.frameCount)
            {
                resolvedFrame = Time.frameCount;
                int style = PlayerPrefs.GetInt(PrefsKey, Automatic);
                // a DualSense is a DualShockGamepad to the Input System, as the DualShock 3 and 4 are; a PlayStation
                // pad that Steam Input passes on as an Xbox pad is not seen as one, and gets the letters
                resolvedPlayStation = style == Ps
                    || (style != Xbox && GameInput.CurrentGamepad is DualShockGamepad);
            }
            return resolvedPlayStation;
        }
    }

    /// <summary>
    /// The picture of a face button to draw for one of the game's Xbox pictures: the same picture with the Xbox style,
    /// the PlayStation symbol in the button's place otherwise. Anything else - the Esc key, the mouse - comes back as
    /// it was.
    /// </summary>
    public static Texture2D Face(Texture2D xboxPicture)
    {
        Remember(xboxPicture);
        if (xboxPicture == null || !PlayStation)
        {
            return xboxPicture;
        }
        switch (xboxPicture.name)
        {
            case "aButton": return Symbol(EPadControl.South);
            case "bButton": return Symbol(EPadControl.East);
            case "xButton": return Symbol(EPadControl.West);
            case "yButton": return Symbol(EPadControl.North);
            default: return xboxPicture;
        }
    }

    /// <summary>
    /// A pad button's name as the pad in use prints it: LB, RB, LT, RT, Start and Select, or L1, R1, L2, R2, Options
    /// and Share. Other names come back as they were.
    /// </summary>
    public static string Name(string xboxName)
    {
        if (!PlayStation)
        {
            return xboxName;
        }
        switch (xboxName)
        {
            case "LB": return "L1";
            case "RB": return "R1";
            case "LT": return "L2";
            case "RT": return "R2";
            case "Start": return "Options";
            case "Select": return "Share";
            default: return xboxName;
        }
    }

    // ---------------------------------------------------------------- the button an action is on (mod 320)

    // the Xbox pictures of the four face buttons, by their asset names, as the components that have them pass them
    private static readonly System.Collections.Generic.Dictionary<string, Texture2D> xboxFaces =
        new System.Collections.Generic.Dictionary<string, Texture2D>();
    private static GUIStyle chipStyle;

    /// <summary>Keeps the face pictures a component has, so that a hint can draw whichever button an action is on.</summary>
    public static void Remember(params Texture2D[] pictures)
    {
        foreach (Texture2D p in pictures)
        {
            if (p != null && (p.name == "aButton" || p.name == "bButton" || p.name == "xButton" || p.name == "yButton"))
            {
                xboxFaces[p.name] = p;
            }
        }
    }

    /// <summary>
    /// Draws, at a rectangle of the screen, the pad button an action is bound to now: a face button's picture in the
    /// style in force, or for the others a dark key with the button's name. The panels' hints, which drew A, B, X
    /// and Y before the buttons could be rebound.
    /// </summary>
    public static void DrawBound(Rect screen, UnityEngine.InputSystem.InputAction action)
    {
        string path = GameBindings.PadPath(action);
        EPadControl face;
        string picture;
        switch (path)
        {
            case "<Gamepad>/buttonSouth": face = EPadControl.South; picture = "aButton"; break;
            case "<Gamepad>/buttonEast": face = EPadControl.East; picture = "bButton"; break;
            case "<Gamepad>/buttonWest": face = EPadControl.West; picture = "xButton"; break;
            case "<Gamepad>/buttonNorth": face = EPadControl.North; picture = "yButton"; break;
            default: face = EPadControl.Start; picture = null; break;
        }
        if (picture != null && PlayStation)
        {
            UiScale.DrawTexture(screen, Symbol(face));
            return;
        }
        if (picture != null && xboxFaces.TryGetValue(picture, out Texture2D xbox))
        {
            UiScale.DrawTexture(screen, xbox);
            return;
        }

        string name = GameBindings.PadName(action);
        if (chipStyle == null)
        {
            chipStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
            chipStyle.normal.textColor = Color.white;
        }
        chipStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(screen.height * 0.5f));
        float width = Mathf.Max(screen.width, chipStyle.CalcSize(new GUIContent(name)).x + screen.height * 0.4f);
        Rect key = new Rect(screen.center.x - width / 2, screen.y, width, screen.height);
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(key, Texture2D.whiteTexture);
        GUI.color = previous;
        GUI.Label(key, name, chipStyle);
    }

    // ---------------------------------------------------------------- the PlayStation symbols

    private const int Size = 92;
    private const float Rim = 46f;
    private const float RimInner = 41.5f;
    // half the width of a symbol's stroke: the Xbox letters' strokes are about ten texels across
    private const float Stroke = 5f;

    private static readonly Color Black = new Color32(0, 0, 0, 255);
    private static readonly Color Disc = new Color32(64, 64, 64, 255);

    private static readonly Texture2D[] symbols = new Texture2D[4];

    private static Texture2D Symbol(EPadControl face)
    {
        int i = face == EPadControl.South ? 0 : face == EPadControl.East ? 1 : face == EPadControl.West ? 2 : 3;
        if (symbols[i] == null)
        {
            symbols[i] = MakeSymbol(face);
        }
        return symbols[i];
    }

    /// <summary>
    /// The disc of the Xbox pictures with a PlayStation symbol on it, drawn as a stroke about as wide as the letters'.
    /// </summary>
    /// <remarks>
    /// The colours are the PlayStation pad's own, since its players know the buttons by colour as much as by shape:
    /// a green triangle, a red circle, a blue cross and a pink square. The green, red and blue are the exact values of
    /// the Xbox pictures' A, B and X; the pink is new.
    /// </remarks>
    private static Texture2D MakeSymbol(EPadControl face)
    {
        Color ink;
        System.Func<Vector2, float> distance;
        Vector2 c = new Vector2(Size / 2f, Size / 2f);
        switch (face)
        {
            case EPadControl.South:
                // the cross: two strokes on the diagonals
                ink = new Color32(64, 144, 240, 255);
                distance = p => Mathf.Min(LineArt.Capsule(p, c + new Vector2(-17f, -17f), c + new Vector2(17f, 17f), 0f),
                    LineArt.Capsule(p, c + new Vector2(-17f, 17f), c + new Vector2(17f, -17f), 0f));
                break;
            case EPadControl.East:
                ink = new Color32(240, 80, 80, 255);
                distance = p => Mathf.Abs(LineArt.Circle(p, c, 20f));
                break;
            case EPadControl.West:
                // the square, its corners sharp as the PlayStation's are
                ink = new Color32(240, 128, 200, 255);
                distance = p =>
                {
                    Vector2 d = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y));
                    float outside = new Vector2(Mathf.Max(d.x - 18f, 0f), Mathf.Max(d.y - 18f, 0f)).magnitude;
                    float inside = Mathf.Min(18f - d.x, 18f - d.y);
                    return outside > 0f ? outside : Mathf.Max(0f, inside);
                };
                break;
            default:
                // the triangle, pointing up, its middle a little below the disc's so it looks centred
                ink = new Color32(128, 224, 96, 255);
                Vector2 t = c + new Vector2(0f, 3f);
                Vector2 top = t + new Vector2(0f, -22f);
                Vector2 left = t + new Vector2(-19f, 11f);
                Vector2 right = t + new Vector2(19f, 11f);
                distance = p => Mathf.Min(LineArt.Capsule(p, top, left, 0f),
                    Mathf.Min(LineArt.Capsule(p, left, right, 0f), LineArt.Capsule(p, right, top, 0f)));
                break;
        }

        // four by four samples a texel, so the edges are as soft as the Xbox pictures' rim
        Color[] pixels = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Color sum = Color.clear;
                for (int sy = 0; sy < 4; sy++)
                {
                    for (int sx = 0; sx < 4; sx++)
                    {
                        // y counts down from the top here, as the shapes above are given; the texture's rows go up
                        Vector2 p = new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f);
                        float r = (p - c).magnitude;
                        Color s = r > Rim ? Color.clear : r > RimInner ? Black : distance(p) <= Stroke ? ink : Disc;
                        sum += s;
                    }
                }
                Color mean = sum / 16f;
                // colours averaged with their alpha, so a half-covered rim texel stays black and not grey
                pixels[(Size - 1 - y) * Size + x] = mean.a > 0f ? new Color(mean.r / mean.a, mean.g / mean.a, mean.b / mean.a, mean.a) : Color.clear;
            }
        }
        Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "PlayStation " + face,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return tex;
    }
}
