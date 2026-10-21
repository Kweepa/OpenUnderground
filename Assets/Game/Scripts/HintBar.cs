using UnityEngine;

/// <summary>A key of a hint: a picture of the game's, or a name on a keyboard key or on a pad key.</summary>
public readonly struct HintKey
{
    public readonly Texture2D Picture;
    public readonly string Name;
    public readonly bool Pad;

    private HintKey(Texture2D picture, string name, bool pad)
    {
        Picture = picture;
        Name = name;
        Pad = pad;
    }

    /// <summary>One of the game's key pictures: the round A, B, X and Y, the Esc key, the mouse buttons.</summary>
    public static HintKey Of(Texture2D picture) => new HintKey(picture, null, false);

    /// <summary>A keyboard key: its name on the game's blank key.</summary>
    public static HintKey Keyboard(string name) => new HintKey(null, name, false);

    /// <summary>A pad button the game has no picture of - LB, RB, Start, Select: its name on a grey key.</summary>
    public static HintKey PadButton(string name) => new HintKey(null, name, true);
}

/// <summary>A hint: the keys to press, more than one for a chord like Shift and R, and what they do.</summary>
public readonly struct Hint
{
    public readonly string What;
    public readonly HintKey[] Keys;

    public Hint(string what, params HintKey[] keys)
    {
        What = what;
        Keys = keys;
    }
}

/// <summary>
/// A row of key hints, drawn as the achievements page draws its own: each key a picture 24 pixels
/// high, what it does beside it, the row centred.
/// </summary>
/// <remarks>
/// The keyboard's keys are the game's blank key with the key's name on it, as the achievements page
/// writes its T: square for one letter, stretched for a word. The pad's LB, RB, Start and Select have
/// no picture in the game, so they are a grey key with their name, in the colours of the round A, B, X
/// and Y. Sizes are reference pixels of a 1920x1080 screen, multiplied by the caller's scale.
/// </remarks>
public static class HintBar
{
    private static Texture2D padKey;

    /// <param name="row">Where the row goes; it is centred in it, across and up and down.</param>
    /// <param name="text">What each key does is written in this; the achievements page's is buttonLabelStyle.</param>
    /// <param name="keyText">A key's name is written in this; the achievements page's is keyLetterStyle.</param>
    /// <param name="blankKey">The game's blank keyboard key.</param>
    public static void Draw(Rect row, float scale, GUIStyle text, GUIStyle keyText, Texture2D blankKey, Hint[] hints)
    {
        float iconSize = 24f * scale;
        float keyGap = 4f * scale;
        float iconGap = 8f * scale;
        float hintGap = 28f * scale;
        GUIStyle label = new GUIStyle(text) { fontSize = Mathf.RoundToInt(text.fontSize * scale), alignment = TextAnchor.MiddleLeft, wordWrap = false };
        GUIStyle name = new GUIStyle(keyText) { fontSize = Mathf.RoundToInt(keyText.fontSize * scale), alignment = TextAnchor.MiddleCenter };

        float KeyWidth(HintKey key) => key.Picture != null ? iconSize
            : Mathf.Max(iconSize, name.CalcSize(new GUIContent(key.Name)).x + (key.Name.Length == 1 ? 0f : 0.7f * iconSize));

        float total = -hintGap;
        foreach (Hint hint in hints)
        {
            foreach (HintKey key in hint.Keys)
            {
                total += KeyWidth(key) + keyGap;
            }
            total += iconGap - keyGap + label.CalcSize(new GUIContent(hint.What)).x + hintGap;
        }

        float x = row.x + (row.width - total) / 2;
        float iconY = row.y + (row.height - iconSize) / 2;
        foreach (Hint hint in hints)
        {
            foreach (HintKey key in hint.Keys)
            {
                Rect place = new Rect(x, iconY, KeyWidth(key), iconSize);
                if (key.Picture != null)
                {
                    GUI.DrawTexture(place, key.Picture);
                }
                else if (key.Pad)
                {
                    GUI.Box(place, key.Name, PadKeyStyle(name));
                }
                else
                {
                    StretchedKey(place, blankKey);
                    GUI.Label(place, key.Name, name);
                }
                x += place.width + keyGap;
            }
            x += iconGap - keyGap;
            float width = label.CalcSize(new GUIContent(hint.What)).x;
            GUI.Label(new Rect(x, row.y, width, row.height), hint.What, label);
            x += width + hintGap;
        }
    }

    /// <summary>
    /// The blank key, its two ends kept square and its middle stretched, so a word fits on it the way
    /// a letter fits on the square one.
    /// </summary>
    private static void StretchedKey(Rect place, Texture2D key)
    {
        if (key == null)
        {
            return;
        }
        if (place.width <= place.height + 0.5f)
        {
            GUI.DrawTexture(place, key);
            return;
        }

        const float capShare = 0.4f;
        float cap = place.height * capShare;
        GUI.DrawTextureWithTexCoords(new Rect(place.x, place.y, cap, place.height), key, new Rect(0f, 0f, capShare, 1f));
        GUI.DrawTextureWithTexCoords(new Rect(place.x + cap, place.y, place.width - 2 * cap, place.height), key,
            new Rect(capShare, 0f, 1f - 2 * capShare, 1f));
        GUI.DrawTextureWithTexCoords(new Rect(place.xMax - cap, place.y, cap, place.height), key, new Rect(1f - capShare, 0f, capShare, 1f));
    }

    /// <summary>The grey pad key, cut into nine so that it stretches to any width.</summary>
    private static GUIStyle PadKeyStyle(GUIStyle text)
    {
        padKey ??= MakePadKey();
        GUIStyle style = new GUIStyle(text) { border = new RectOffset(8, 8, 8, 8) };
        style.normal.background = padKey;
        return style;
    }

    /// <summary>
    /// A grey key with a black edge and round corners, the grey and black of the round A, B, X and Y.
    /// Small, so that its corners still fit a key 16 pixels high, the size of a hint on a Steam Deck.
    /// </summary>
    private static Texture2D MakePadKey()
    {
        const int size = 24;
        const float radius = 8f;
        const float edge = 2f;
        Color fill = new Color32(64, 64, 64, 255);
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
        Color[] pixels = new Color[size * size];
        for (int py = 0; py < size; ++py)
        {
            for (int px = 0; px < size; ++px)
            {
                // Distance from the rounded rectangle's edge, inside negative.
                float dx = Mathf.Max(radius - (px + 0.5f), (px + 0.5f) - (size - radius), 0f);
                float dy = Mathf.Max(radius - (py + 0.5f), (py + 0.5f) - (size - radius), 0f);
                float outside = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                Color color = Color.Lerp(fill, Color.black, Mathf.Clamp01(outside + edge + 0.5f));
                color.a = Mathf.Clamp01(0.5f - outside);
                pixels[py * size + px] = color;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
