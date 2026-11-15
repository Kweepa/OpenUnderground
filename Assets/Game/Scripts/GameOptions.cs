using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>The sections of the game menu's OPTIONS and CONTROLS tabs, in the order they are shown.</summary>
public enum EOptionSection
{
    Gameplay,
    Display,
    Audio,
    General,
    Input,
    /// <summary>The keyboard's map: no rows, drawn by ControlsMap, where its keys are rebound (GameBindings).</summary>
    Keyboard,
    /// <summary>The pad's map, one page per context: no rows, drawn by ControlsMap.</summary>
    Gamepad,
}

/// <summary>What a row of the game menu holds, and so how it is drawn and changed.</summary>
public enum EOptionKind
{
    /// <summary>On or off, a box filled when on.</summary>
    Toggle,
    /// <summary>A number between two ends, in steps, its value written beside it.</summary>
    Slider,
    /// <summary>
    /// One of a list of names, stepped through with left and right: the soundfont, found on disk and kept by its
    /// name, or the names a row lists itself (<see cref="GameOption.Choices"/>), kept by their place in the list.
    /// </summary>
    Choice,
    /// <summary>A page of its own, opened with A, Enter or a click: the music tracks. Holds no value.</summary>
    Page,
}

/// <summary>Where a row's value is kept.</summary>
public enum EOptionStore
{
    /// <summary>A number in the PlayerPrefs key of the row, which the rest of the game reads.</summary>
    Prefs,
    /// <summary>The screen's own state, read and set by <see cref="ScreenOptions"/>: the monitor, the display mode and the resolution.</summary>
    Screen,
}

/// <summary>One setting of the game menu: where it is shown, what it says, and how it is kept.</summary>
public sealed class GameOption
{
    public readonly EOptionSection Section;
    /// <summary>The PlayerPrefs key it is kept under; the rest of the game reads the same key.</summary>
    public readonly string Key;
    public readonly string Label;
    public readonly string Description;
    /// <summary>What the original game does, written after "Classic:" in the detail panel; null where there is nothing to say.</summary>
    public readonly string Original;
    public readonly EOptionKind Kind;
    public readonly float Default;
    public readonly float Min;
    public readonly float Max;
    public readonly float Step;
    /// <summary>Kept with SetFloat rather than SetInt, as the volumes and the mouse look rate always were.</summary>
    public readonly bool StoredAsFloat;
    /// <summary>The value the original's rules give it, or NaN when the row has none.</summary>
    public readonly float OriginalValue;
    /// <summary>The names of a Choice row, kept as their place in this list; null for the soundfont and the screen's rows.</summary>
    public readonly string[] Choices;
    public readonly EOptionStore Store;
    /// <summary>A web page the detail panel offers under the description, opened with a click; null for none.</summary>
    public readonly string Link;
    /// <summary>What the detail panel writes for <see cref="Link"/>.</summary>
    public readonly string LinkText;
    private readonly Func<float, string> format;

    public GameOption(EOptionSection section, string key, string label, string description, EOptionKind kind,
        float defaultValue, float min = 0f, float max = 1f, float step = 1f, bool storedAsFloat = false,
        Func<float, string> format = null, string original = null, float originalValue = float.NaN, string[] choices = null,
        EOptionStore store = EOptionStore.Prefs, string link = null, string linkText = null)
    {
        Link = link;
        LinkText = linkText ?? link;
        Section = section;
        Key = key;
        Label = label;
        Description = description;
        Original = original;
        Kind = kind;
        Default = defaultValue;
        Min = min;
        Max = max;
        Step = step;
        StoredAsFloat = storedAsFloat;
        OriginalValue = originalValue;
        Choices = choices;
        Store = store;
        this.format = format;
    }

    /// <summary>The value written beside a slider, and after "Default:" in the detail panel.</summary>
    public string Format(float value)
    {
        if (format != null)
        {
            return format(value);
        }
        return Kind == EOptionKind.Toggle ? (value != 0f ? "On" : "Off") : value.ToString("0.##");
    }
}

/// <summary>
/// Every setting of the game menu in one table, and what the menu does with them: read, change,
/// apply at once, put back to the defaults or to the original's rules, and undo.
/// </summary>
/// <remarks>
/// Mod 270's table of options (group, label, description) grown into the model of the whole menu:
/// one entry per setting, read from and written to the same PlayerPrefs key the rest of the game
/// already reads, so nothing outside the menu changed. A value is written the moment it changes
/// and takes effect at once, which is what the rest of the game already allowed, since it reads the
/// keys every time; only the music and the interface's growth keep a copy, and they are told here.
/// The keys are saved to disk when the menu closes.
/// The soundfont is a name, not a number, and is the one Choice; its value is its place in the
/// list of soundfonts found on disk. The monitor, the display mode and the resolution are the screen's
/// state, not keys: <see cref="ScreenOptions"/> reads and sets them, and asks before keeping a change.
/// </remarks>
public static class GameOptions
{
    public const string SoundFontKey = "Options_SoundFont";
    public const string DefaultSoundFont = "Soundfonts/MT32.sf2";
    public const string MusicVolumeKey = "Options_MusicVolume";
    /// <summary>The last entry of the soundfont's list: the music sent to a MIDI device (MusicPlayerExternalMidi.cs).</summary>
    public const string ExternalMidiName = "MT-32 EXT MIDI";
    // How long the player has to say the music is heard on the MIDI device before it goes back to the soundfont.
    private const float ExternalMidiConfirmSeconds = 15f;
    /// <summary>The key of the Music tracks row, which opens the music page instead of holding a value.</summary>
    public const string MusicPageKey = "page:music";
    public const float DefaultMusicVolume = 0.5f;

    // A change of soundfont rebuilds the synthesizer, which a player stepping through the list
    // would do once per press: it is put off until the list has been still this long.
    private const float SoundFontSettleSeconds = 0.4f;

    private static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

    public static readonly GameOption[] All =
    {
        new(EOptionSection.General, "Options_AutoSave", "Autosave on a new level",
            "<b>Enabled:</b> the game saves by itself the first time you set foot on a level.\n<b>Disabled:</b> only the quicksave and the saves you make yourself are written.",
            EOptionKind.Toggle, 1f),

        new(EOptionSection.Display, ScreenOptions.MonitorKey, "Monitor",
            "Which monitor the game is shown on. Choosing another moves the game there, at that monitor's own size in Fullscreen. A, Enter or a click opens the list.\nAfter a change the game asks whether to keep it, and goes back by itself after 10 seconds.",
            EOptionKind.Choice, 0f, store: EOptionStore.Screen),
        new(EOptionSection.Display, ScreenOptions.ModeKey, "Display mode",
            "<b>Fullscreen (borderless):</b> the game covers the desktop at Windows' own size; Alt-Tab is instant.\n<b>Fullscreen (exclusive):</b> the monitor itself switches to the size chosen, up to its largest; another shape shows black bands, is zoomed or stretched as the monitor's settings say. Alt-Tab takes a moment.\n<b>Windowed:</b> the game in a window, which can be moved, and resized by its edges.\nAlt-Enter switches between Windowed and the last Fullscreen; A, Enter or a click opens the list. After a change the game asks whether to keep it, and goes back by itself after 10 seconds.",
            EOptionKind.Choice, 0f, choices: ScreenOptions.ModeNames, store: EOptionStore.Screen),
        new(EOptionSection.Display, ScreenOptions.ResolutionKey, "Resolution",
            "The size the game is drawn at, and its shape. <b>desktop</b>: Windows' own size, the one of Fullscreen (borderless). <b>native</b>: the monitor's largest size, the sharpest; a smaller one is softer and lighter on the graphics card. <b>scaled</b>: the interface is drawn smaller to fit. <b>last</b>: the size you last dragged the window to. A, Enter or a click opens the list.\nEach mode remembers its own size. After a change the game asks whether to keep it, and goes back by itself after 10 seconds.",
            EOptionKind.Choice, 0f, store: EOptionStore.Screen),
        new(EOptionSection.Display, PlayerInput.FieldOfViewKey, "Field of view",
            "How wide the view is, in degrees across the screen. A screen wider than 16:9 shows more at the sides, and a 16:10 or 4:3 screen more above and below. A wide view shows more around you and stretches the edges. The weapon in your hand keeps its size and place.",
            EOptionKind.Slider, PlayerInput.DefaultFieldOfView, PlayerInput.MinFieldOfView, PlayerInput.MaxFieldOfView, 1f,
            format: v => Mathf.RoundToInt(v) + "\u00b0"),
        new(EOptionSection.Display, PlayerInput.CameraBobKey, "Camera bob",
            "How deep the view dips at each step as you walk. At 100% the usual bob; at Off the view stays level. Swimming and flying keep their own motion.",
            EOptionKind.Slider, PlayerInput.DefaultCameraBob, 0f, PlayerInput.MaxCameraBob, 10f,
            format: v => v <= 0f ? "Off" : Mathf.RoundToInt(v) + "%",
            // no originalValue: GAMEPLAY's Classic button would set it, and this is a matter of taste
            original: "about 200%, the view dipping twice as deep at each step."),
        new(EOptionSection.Display, UiScale.GrowthPrefsKey, "Interface scale above 1080p",
            "How much the panels and the text grow on a screen larger than 1920x1080. At 0% they keep the size they have at 1920x1080; at 100% they grow with the screen, up to twice that size. Smaller screens are not affected.",
            EOptionKind.Slider, UiScale.DefaultGrowthPercent, 0f, 100f, 10f, format: v => Mathf.RoundToInt(v) + "%"),

        new(EOptionSection.Audio, MusicVolumeKey, "Music volume",
            "Adjusts the volume of the music.",
            EOptionKind.Slider, DefaultMusicVolume, 0f, 1f, 0.05f, storedAsFloat: true, format: Percent),
        new(EOptionSection.Audio, "Options_EffectsVolume", "Effects volume",
            "Adjusts the volume of the sound effects.",
            EOptionKind.Slider, PlayerInput.DefaultEffectsVolume, 0f, 1f, 0.05f, storedAsFloat: true, format: Percent),
        new(EOptionSection.Audio, SoundFontKey, "Soundfont",
            "Chooses the soundfont the music is played with. Different soundfonts emulate classic sound cards such as the MT-32.\n"
            + "<b>" + ExternalMidiName + ":</b> the music goes to a MIDI device instead, such as the Munt MT-32 emulator with its ROMs, which plays it as the original did on a Roland MT-32. Without one, there is no music.",
            EOptionKind.Choice, 0f, link: "https://sourceforge.net/projects/munt/", linkText: "Munt, the MT-32 emulator: sourceforge.net/projects/munt"),
        new(EOptionSection.Audio, MusicPageKey, "Music tracks...",
            "Which tracks play in each situation of the game - exploring, a fight, the map, a conversation - with your own music files added from disk, listened to, switched off or removed, and the soundfont the original's music is played through.",
            EOptionKind.Page, 0f),
        new(EOptionSection.Audio, "Options_KeepMusicOnMap", "Music over the map",
            "<b>Enabled:</b> the exploring music carries on while the map is open.\n<b>Disabled:</b> the map plays its own track, and the exploring music starts again from the beginning when you close it.",
            EOptionKind.Toggle, 1f),
        new(EOptionSection.Audio, "Options_AmbientSilenceMax", "Max silence between tracks",
            "After an exploring track ends, and after a fight, the music stops for a while before the next track. Each pause lasts a random time, up to the length set here.\nAt none, one track follows another with no pause.",
            EOptionKind.Slider, PlayerInput.DefaultAmbientSilenceMax, 0f, PlayerInput.MaxAmbientSilence, 10f,
            format: v => v <= 0f ? "none" : Mathf.RoundToInt(v) + " s"),

        // In alphabetical order, the user's rule of 31 October 2026, and every label a noun or a gerund.
        new(EOptionSection.Gameplay, "Options_AutomaticSearch", "Automatic search",
            "<b>Enabled:</b> every few seconds, away from a fight, your Search, Traps and Track skills roll by themselves and tell you of a hidden door, a trapped object or the creatures nearby.",
            EOptionKind.Toggle, 1f,
            original: "Search and Traps roll only when you look at something, and Track tells you nothing by itself.", originalValue: 0f),
        new(EOptionSection.Gameplay, "Options_UseKeyAutomatically", "Automatic unlocking",
            "<b>Enabled:</b> a locked door unlocks by itself when you use it with the right key in your pack.",
            EOptionKind.Toggle, 1f,
            original: "you take the key from your pack and use it on the door.", originalValue: 0f),
        new(EOptionSection.Gameplay, "Options_RewardOnlyFirstTrapCopy", "Grinding without rewards",
            "<b>Enabled:</b> when you set off the same trap again and again, the creatures it spawns after the first one give no experience and no loot.",
            EOptionKind.Toggle, 1f,
            original: "every creature a trap spawns gives experience and loot.", originalValue: 0f),
        new(EOptionSection.Gameplay, "Options_PartialResistances", "Partial resistances",
            "<b>Enabled:</b> Resist Fire and Poison Resistance let a third of the damage through, and poison hurts a point at a time over the minute.",
            EOptionKind.Toggle, 1f,
            original: "the two stop their damage outright, and poison damage is applied every minute.", originalValue: 0f),
        new(EOptionSection.Gameplay, "Options_LootInDebris", "Searching debris piles",
            "<b>Enabled:</b> a pile of debris may hide a few items, which a Search roll finds when you use it.",
            EOptionKind.Toggle, 1f,
            original: "debris is only scenery: it holds nothing and cannot be searched.", originalValue: 0f),
        new(EOptionSection.Gameplay, "Options_ShieldSpellsSoak", "Shield spells absorbing blows",
            "<b>Enabled:</b> Resist Blows, Thick Skin and Iron Flesh take 1, 2 and 3 points off every blow, cast or worn.",
            EOptionKind.Toggle, 1f,
            original: "the three spells do nothing.", originalValue: 0f),
        new(EOptionSection.Gameplay, "Options_Sprint", "Sprinting",
            "<b>Enabled:</b> you can sprint for a few seconds, longer with more Acrobat, and a gauge beside the rune shelf shows how much is left.\nBe warned: dashing through the level can make you miss secret details.",
            EOptionKind.Toggle, 1f,
            original: "there is no sprint.", originalValue: 0f),

        new(EOptionSection.Input, "Options_MouseLookSpeed", "Mouse look rate",
            "How quickly the view turns with the mouse. Does not affect the gamepad.",
            EOptionKind.Slider, PlayerInput.DefaultMouseLookSpeed, PlayerInput.MinMouseLookSpeed, PlayerInput.MaxMouseLookSpeed, 0.05f,
            storedAsFloat: true, format: v => v.ToString("0.00")),
        new(EOptionSection.Input, PlayerInput.PadLookSpeedKey, "Pad look speed",
            "How quickly the view turns with the look stick. At 100% half a turn a second across, a quarter up and down.",
            EOptionKind.Slider, PlayerInput.DefaultPadLookSpeed, PlayerInput.MinPadLookSpeed, PlayerInput.MaxPadLookSpeed, 0.1f,
            storedAsFloat: true, format: Percent),
        new(EOptionSection.Input, "Options_InvertLook", "Inverted look up/down",
            "Reverses up and down when looking with the mouse or the gamepad.",
            EOptionKind.Toggle, 0f),
        new(EOptionSection.Input, PadGlyphs.PrefsKey, "Button prompts",
            "How the pad's buttons are shown: A, B, X, Y, LB, RB, LT, RT, Start and Select, or the PlayStation's cross, circle, square, triangle, L1, R1, L2, R2, Options and Share.\n<b>Automatic:</b> the PlayStation's while a PlayStation pad is connected.",
            EOptionKind.Choice, 0f, choices: PadGlyphs.StyleNames),
        new(EOptionSection.Input, "Options_AutoJump", "Automatic jumping",
            "<b>Enabled:</b> you jump by yourself at the edge when running straight at a gap or a deep drop. To step off a ledge instead, hold a sideways key as well, or push the stick only partway.",
            EOptionKind.Toggle, 0f),
        new(EOptionSection.Input, "Options_LedgeRumble", "Ledge rumble",
            "<b>Enabled:</b> the pad gives a short rumble when you run at a ledge, a moment before the edge: jump, or stop. The pad's other rumbles, for blows, doors and shrines, stay on either way.",
            EOptionKind.Toggle, 1f),
    };

    private static string[] soundFonts = Array.Empty<string>();
    private static float soundFontAppliesAt = -1f;

    /// <summary>The rows of one section, in the table's order.</summary>
    public static List<GameOption> InSection(EOptionSection section)
    {
        List<GameOption> rows = new List<GameOption>();
        foreach (GameOption o in All)
        {
            if (o.Section == section)
            {
                rows.Add(o);
            }
        }
        return rows;
    }

    /// <summary>
    /// The soundfonts on disk, found again; called when the menu opens. A MIDI device chosen and not
    /// found before is looked for again here, quietly.
    /// </summary>
    public static void ScanSoundFonts()
    {
        MusicPlayer music = MusicPlayer.Instance;
        if (MusicPlayer.ExternalMidiChosen && music != null && music.ExternalMidiPort == null)
        {
            music.StartExternalMidi(out _);
        }
        try
        {
            string folder = Path.Combine(Application.streamingAssetsPath, "Soundfonts");
            if (!Directory.Exists(folder))
            {
                soundFonts = new[] { CurrentSoundFont, ExternalMidiName };
                return;
            }
            soundFonts = MusicOutputs(Directory.GetFiles(folder, "*.sf2", SearchOption.TopDirectoryOnly));
        }
        catch (Exception e)
        {
            Debug.LogError($"Error scanning soundfonts: {e.Message}");
            soundFonts = new[] { CurrentSoundFont, ExternalMidiName };
        }
    }

    // The order of the list, the user's of 15 November 2026: the MT-32 soundfont, the MIDI device that
    // plays the MT-32 itself, then the other two cards; any other soundfont after them, by name.
    private static readonly string[] MusicOutputOrder = { "MT32.sf2", ExternalMidiName, "OPL2.sf2", "GUS.sf2" };

    /// <summary>
    /// The soundfonts found, named the way the options store them ("Soundfonts/x.sf2"), in the list's
    /// order and with <see cref="ExternalMidiName"/> among them: what the Soundfont row and the music
    /// page's list step through.
    /// </summary>
    public static string[] MusicOutputs(string[] files)
    {
        List<string> names = new List<string>();
        foreach (string f in files)
        {
            names.Add(Path.GetFileName(f));
        }
        names.Add(ExternalMidiName);
        names.Sort((a, b) =>
        {
            int ia = Array.FindIndex(MusicOutputOrder, n => string.Equals(n, a, StringComparison.OrdinalIgnoreCase));
            int ib = Array.FindIndex(MusicOutputOrder, n => string.Equals(n, b, StringComparison.OrdinalIgnoreCase));
            if (ia < 0)
            {
                ia = MusicOutputOrder.Length;
            }
            if (ib < 0)
            {
                ib = MusicOutputOrder.Length;
            }
            return ia != ib ? ia.CompareTo(ib) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });
        string[] list = new string[names.Count];
        for (int i = 0; i < names.Count; i++)
        {
            list[i] = names[i] == ExternalMidiName ? ExternalMidiName : "Soundfonts/" + names[i];
        }
        return list;
    }

    public static string CurrentSoundFont => PlayerPrefs.GetString(SoundFontKey, DefaultSoundFont);

    private static int SoundFontIndex(string name)
    {
        for (int i = 0; i < soundFonts.Length; i++)
        {
            if (string.Equals(Path.GetFileName(soundFonts[i]), Path.GetFileName(name), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>The value of a row: a number, or for the soundfont its place in the list.</summary>
    public static float Get(GameOption o)
    {
        if (o.Store == EOptionStore.Screen)
        {
            return ScreenOptions.Get(o);
        }
        switch (o.Kind)
        {
            case EOptionKind.Page:
                return 0f;
            case EOptionKind.Choice when o.Choices != null:
                return Mathf.Clamp(PlayerPrefs.GetInt(o.Key, Mathf.RoundToInt(o.Default)), 0, o.Choices.Length - 1);
            case EOptionKind.Choice:
                return MusicPlayer.ExternalMidiChosen
                    ? Mathf.Max(0, Array.IndexOf(soundFonts, ExternalMidiName))
                    : Mathf.Max(0, SoundFontIndex(CurrentSoundFont));
            default:
                return o.StoredAsFloat ? PlayerPrefs.GetFloat(o.Key, o.Default) : PlayerPrefs.GetInt(o.Key, Mathf.RoundToInt(o.Default));
        }
    }

    /// <summary>What the row shows as its value: the number formatted, or the soundfont's name.</summary>
    public static string ValueText(GameOption o)
    {
        if (o.Store == EOptionStore.Screen)
        {
            return ScreenOptions.ValueText(o);
        }
        if (o.Kind == EOptionKind.Page)
        {
            return "";
        }
        if (o.Kind == EOptionKind.Choice && o.Choices != null)
        {
            return o.Choices[Mathf.RoundToInt(Get(o))];
        }
        return IsSoundFont(o) ? CurrentMusicOutputName : o.Format(Get(o));
    }

    public static string DefaultText(GameOption o)
    {
        if (o.Store == EOptionStore.Screen)
        {
            return ScreenOptions.DefaultText(o);
        }
        if (o.Kind == EOptionKind.Page)
        {
            return "";
        }
        if (o.Kind == EOptionKind.Choice && o.Choices != null)
        {
            return o.Choices[Mathf.RoundToInt(o.Default)];
        }
        return IsSoundFont(o) ? Path.GetFileNameWithoutExtension(DefaultSoundFont) : o.Format(o.Default);
    }

    public static bool IsDefault(GameOption o)
    {
        if (o.Store == EOptionStore.Screen)
        {
            return ScreenOptions.IsDefault(o);
        }
        if (o.Kind == EOptionKind.Page)
        {
            return true;
        }
        return IsSoundFont(o)
            ? !MusicPlayer.ExternalMidiChosen && SoundFontIndex(CurrentSoundFont) == SoundFontIndex(DefaultSoundFont)
            : Mathf.Abs(Get(o) - o.Default) < 0.001f;
    }

    /// <summary>Writes a value, clamped and snapped to the row's steps, and puts it into effect.</summary>
    public static void Set(GameOption o, float value)
    {
        if (o.Store == EOptionStore.Screen)
        {
            ScreenOptions.Set(o, value);
            return;
        }
        switch (o.Kind)
        {
            case EOptionKind.Page:
                return;
            case EOptionKind.Toggle:
                PlayerPrefs.SetInt(o.Key, value != 0f ? 1 : 0);
                break;
            case EOptionKind.Choice when o.Choices != null:
                // round the list, as the soundfonts go
                PlayerPrefs.SetInt(o.Key, ((Mathf.RoundToInt(value) % o.Choices.Length) + o.Choices.Length) % o.Choices.Length);
                break;
            case EOptionKind.Choice:
                if (soundFonts.Length > 0)
                {
                    // the soundfonts and, among them, the MIDI device
                    int i = ((Mathf.RoundToInt(value) % soundFonts.Length) + soundFonts.Length) % soundFonts.Length;
                    if (soundFonts[i] == ExternalMidiName)
                    {
                        ChooseExternalMidi();
                    }
                    else
                    {
                        SetSoundFont(soundFonts[i]);
                    }
                }
                return;
            default:
                float snapped = Mathf.Clamp(o.Min + Mathf.Round((value - o.Min) / o.Step) * o.Step, o.Min, o.Max);
                if (o.StoredAsFloat)
                {
                    PlayerPrefs.SetFloat(o.Key, snapped);
                }
                else
                {
                    PlayerPrefs.SetInt(o.Key, Mathf.RoundToInt(snapped));
                }
                break;
        }
        Apply(o);
    }

    /// <summary>Left or right on a row: a step of a slider, the next soundfont, or a toggle flipped.</summary>
    public static void Step(GameOption o, int direction)
    {
        if (o.Store == EOptionStore.Screen)
        {
            ScreenOptions.Step(o, direction);
            return;
        }
        switch (o.Kind)
        {
            case EOptionKind.Page:
                return;
            case EOptionKind.Toggle:
                Set(o, Get(o) != 0f ? 0f : 1f);
                break;
            case EOptionKind.Choice:
                Set(o, Get(o) + direction);
                break;
            default:
                Set(o, Get(o) + direction * o.Step);
                break;
        }
    }

    /// <summary>False where left or right would do nothing: the ends of the monitor and resolution lists, which do not wrap round.</summary>
    public static bool CanStep(GameOption o, int direction) =>
        o.Kind != EOptionKind.Page && (o.Store != EOptionStore.Screen || ScreenOptions.CanStep(o, direction));

    /// <summary>A row whose values open as a drop-down list on A, Enter or a click: the monitor and the resolution.</summary>
    public static bool HasList(GameOption o) => o.Store == EOptionStore.Screen && ScreenOptions.HasList(o);

    /// <summary>The drop-down's entries, in the row's order; the row's value is the place of the one in force.</summary>
    public static List<string> ListTexts(GameOption o) => HasList(o) ? ScreenOptions.ListTexts(o) : new List<string>();

    /// <summary>An entry chosen in the drop-down, put into effect at once.</summary>
    public static void Choose(GameOption o, int index)
    {
        if (HasList(o))
        {
            ScreenOptions.Choose(o, index);
        }
    }

    /// <summary>A row whose value is long (the screen's: "1920x1080 16:9 (native)"), drawn in a wider column.</summary>
    public static bool WideValue(GameOption o) => o.Store == EOptionStore.Screen;

    /// <summary>The soundfont's row: a Choice whose names are the files found on disk, kept by name.</summary>
    private static bool IsSoundFont(GameOption o) => o.Kind == EOptionKind.Choice && o.Choices == null && o.Store == EOptionStore.Prefs;

    private static void SetSoundFont(string name)
    {
        PlayerPrefs.SetString(SoundFontKey, name);
        PlayerPrefs.SetInt(MusicPlayer.ExternalMidiKey, 0);
        soundFontAppliesAt = Time.unscaledTime + SoundFontSettleSeconds;
    }

    /// <summary>A soundfont chosen elsewhere, the music page's list: put into effect as the row would.</summary>
    public static void ChooseSoundFont(string name)
    {
        SetSoundFont(name);
    }

    /// <summary>
    /// The MIDI device chosen, looked for once the list is still. Without one the music is silent (the
    /// user's rule, 15 November 2026); the MT-32 soundfont is kept behind it only for a way back.
    /// </summary>
    /// <param name="ask">
    /// False from the music page, where the player is already listening and steps from one soundfont to
    /// the next: no question there (the user's rule, 15 November 2026).
    /// </param>
    public static void ChooseExternalMidi(bool ask = true)
    {
        PlayerPrefs.SetString(SoundFontKey, DefaultSoundFont);
        PlayerPrefs.SetInt(MusicPlayer.ExternalMidiKey, 1);
        soundFontAppliesAt = Time.unscaledTime + SoundFontSettleSeconds;
        askWhenApplied = ask;
    }

    // Whether putting the MIDI device into effect asks its question; reset after each time.
    private static bool askWhenApplied = true;

    /// <summary>What the soundfont row shows: the soundfont's name, or the MIDI device.</summary>
    public static string CurrentMusicOutputName =>
        MusicPlayer.ExternalMidiChosen ? ExternalMidiName : Path.GetFileNameWithoutExtension(CurrentSoundFont);

    /// <summary>A question for the player, asked by the menu in its Yes/No box: No lit, and with a countdown that answers No.</summary>
    public sealed class Question
    {
        public string Text;
        public Action OnYes;
        public Action OnNo;
        public float Seconds;
    }

    private static Question pendingQuestion;

    /// <summary>The question waiting to be asked, taken by the menu that asks it.</summary>
    public static Question TakeQuestion()
    {
        Question q = pendingQuestion;
        pendingQuestion = null;
        return q;
    }

    /// <summary>
    /// The music to the soundfont or the MIDI device, as chosen. When the device is new to this
    /// choice, the player is asked: with none found, whether to keep the choice; with one, whether
    /// the music is heard, since a device that plays nothing - Munt without its ROMs - answers like
    /// any other. Quietly, when the menu is closing and nothing could be asked.
    /// </summary>
    private static void ApplyMusicOutput(bool ask)
    {
        MusicPlayer music = MusicPlayer.Instance;
        if (music == null)
        {
            return;
        }
        float volume = PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume);
        if (!MusicPlayer.ExternalMidiChosen)
        {
            music.StopExternalMidi();
            music.ReloadSynthesizerWithSoundFont(CurrentSoundFont, volume);
            return;
        }

        music.ReloadSynthesizerWithSoundFont(CurrentSoundFont, volume);
        bool wasPlaying = music.ExternalMidiPort != null;
        if (music.StartExternalMidi(out string port))
        {
            if (ask && !wasPlaying)
            {
                pendingQuestion = new Question
                {
                    Text = $"The music now plays on {port}. Do you hear it?",
                    OnNo = BackToSoundFont,
                    Seconds = ExternalMidiConfirmSeconds,
                };
            }
        }
        else if (ask)
        {
            pendingQuestion = new Question
            {
                Text = "No MIDI device found. Keep " + ExternalMidiName + "? There is no music until one is found.",
                OnNo = BackToSoundFont,
            };
        }
    }

    private static void BackToSoundFont()
    {
        SetSoundFont(DefaultSoundFont);
        soundFontAppliesAt = -1f;
        ApplyMusicOutput(false);
    }

    /// <summary>A number written back exactly as it was kept, not snapped to the row's steps, and put into effect.</summary>
    private static void Put(GameOption o, float value)
    {
        if (o.StoredAsFloat)
        {
            PlayerPrefs.SetFloat(o.Key, value);
        }
        else
        {
            PlayerPrefs.SetInt(o.Key, Mathf.RoundToInt(value));
        }
        Apply(o);
    }

    // the Interface scale slider while the pointer has it (HoldGrowth)
    private static bool growthHeld;

    /// <summary>
    /// While the pointer clicks or drags the Interface scale slider its value is written but the interface does not
    /// grow yet: the track is laid out, and the pointer read, at the scale the slider is changing, so the thumb would
    /// run from under the pointer. The new scale comes in when the button is let go.
    /// </summary>
    public static void HoldGrowth(bool hold)
    {
        if (growthHeld == hold)
        {
            return;
        }
        growthHeld = hold;
        if (!hold)
        {
            foreach (GameOption o in All)
            {
                if (o.Key == UiScale.GrowthPrefsKey)
                {
                    Apply(o);
                }
            }
        }
    }

    /// <summary>The settings some part of the game keeps a copy of, told of the change.</summary>
    private static void Apply(GameOption o)
    {
        if (o.Key == MusicVolumeKey)
        {
            // the soundfont now playing, so a volume change never rebuilds the synthesizer
            MusicPlayer music = MusicPlayer.Instance;
            music?.ReloadSynthesizerWithSoundFont(music.SoundFontFileName, Get(o));
        }
        else if (o.Key == UiScale.GrowthPrefsKey && !growthHeld)
        {
            UiScale.GrowthPercent = Mathf.RoundToInt(Get(o));
        }
    }

    /// <summary>Called every frame the menu is open: puts a new soundfont into effect once the list is still.</summary>
    public static void Tick()
    {
        ScreenOptions.Tick();
        if (soundFontAppliesAt >= 0f && Time.unscaledTime >= soundFontAppliesAt)
        {
            // set to 0 by Save(), when the menu is closing and cannot ask
            bool ask = soundFontAppliesAt > 0f && askWhenApplied;
            soundFontAppliesAt = -1f;
            askWhenApplied = true;
            ApplyMusicOutput(ask);
        }
    }

    /// <summary>Every row of these sections back to its default, and the bindings of the two maps among them.</summary>
    public static void ResetSections(params EOptionSection[] sections)
    {
        GameBindings.Reset(sections);
        foreach (GameOption o in All)
        {
            if (Array.IndexOf(sections, o.Section) >= 0)
            {
                ResetOne(o);
            }
        }
    }

    private static void ResetOne(GameOption o)
    {
        if (o.Kind == EOptionKind.Page)
        {
            return;
        }
        if (o.Store == EOptionStore.Screen)
        {
            ScreenOptions.Reset(o);
            return;
        }
        if (IsSoundFont(o))
        {
            SetSoundFont(DefaultSoundFont);
        }
        else
        {
            Set(o, o.Default);
        }
    }

    /// <summary>Every row that has one to the original's rule, each free to be changed again afterwards.</summary>
    public static void SetOriginalRules()
    {
        foreach (GameOption o in All)
        {
            if (!float.IsNaN(o.OriginalValue))
            {
                Set(o, o.OriginalValue);
            }
        }
    }

    /// <summary>True when some row of these sections differs from what the original's rules give it.</summary>
    public static bool DiffersFromOriginal(EOptionSection section)
    {
        foreach (GameOption o in All)
        {
            if (o.Section == section && !float.IsNaN(o.OriginalValue) && Mathf.Abs(Get(o) - o.OriginalValue) > 0.001f)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True when some row of these sections is away from its default, or a binding of the two maps among them.</summary>
    public static bool AnyChangedFromDefault(params EOptionSection[] sections)
    {
        if (GameBindings.AnyChanged(sections))
        {
            return true;
        }
        foreach (GameOption o in All)
        {
            if (Array.IndexOf(sections, o.Section) >= 0 && !IsDefault(o))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Every value as it stands, kept when the menu opens so that Undo changes can go back to it.</summary>
    public sealed class Snapshot
    {
        private readonly Dictionary<GameOption, float> numbers = new Dictionary<GameOption, float>();
        private readonly string soundFont;
        private readonly bool externalMidi;
        private readonly GameBindings.Snapshot bindings = new GameBindings.Snapshot();
        private readonly ScreenOptions.Saved screen = ScreenOptions.Capture();

        public Snapshot()
        {
            foreach (GameOption o in All)
            {
                if (!IsSoundFont(o) && o.Kind != EOptionKind.Page && o.Store == EOptionStore.Prefs)
                {
                    numbers[o] = Get(o);
                }
            }
            soundFont = CurrentSoundFont;
            externalMidi = MusicPlayer.ExternalMidiChosen;
        }

        public bool Differs()
        {
            foreach (KeyValuePair<GameOption, float> kv in numbers)
            {
                if (Mathf.Abs(Get(kv.Key) - kv.Value) > 0.0001f)
                {
                    return true;
                }
            }
            return CurrentSoundFont != soundFont || MusicPlayer.ExternalMidiChosen != externalMidi || bindings.Differs()
                || !screen.Equals(ScreenOptions.Capture());
        }

        public void Restore()
        {
            foreach (KeyValuePair<GameOption, float> kv in numbers)
            {
                // only what changed, and as it was: through Set() a value an earlier build's free slider wrote
                // (the look rate 1.3742) would be snapped to the row's steps, and Differs() stay true
                if (Mathf.Abs(Get(kv.Key) - kv.Value) > 0.0001f)
                {
                    Put(kv.Key, kv.Value);
                }
            }
            if (externalMidi && !MusicPlayer.ExternalMidiChosen)
            {
                ChooseExternalMidi();
            }
            else if (CurrentSoundFont != soundFont || (!externalMidi && MusicPlayer.ExternalMidiChosen))
            {
                SetSoundFont(soundFont);
            }
            bindings.Restore();
            // at once, and not asked about: the screen as it was when the menu opened
            ScreenOptions.Restore(screen);
        }
    }

    /// <summary>
    /// Writes every row, at the value in force, so that each shows in the registry, and saves the
    /// keys to disk. Called when the menu closes.
    /// </summary>
    public static void Save()
    {
        HoldGrowth(false);
        foreach (GameOption o in All)
        {
            // the screen's rows are no keys: Unity keeps the mode and size, ScreenOptions the sizes remembered
            if (o.Kind == EOptionKind.Page || o.Store == EOptionStore.Screen)
            {
                continue;
            }
            if (IsSoundFont(o))
            {
                PlayerPrefs.SetString(o.Key, CurrentSoundFont);
                PlayerPrefs.SetInt(MusicPlayer.ExternalMidiKey, MusicPlayer.ExternalMidiChosen ? 1 : 0);
            }
            else if (o.StoredAsFloat)
            {
                PlayerPrefs.SetFloat(o.Key, Get(o));
            }
            else
            {
                PlayerPrefs.SetInt(o.Key, Mathf.RoundToInt(Get(o)));
            }
        }
        // a soundfont still waiting for the list to settle is put into effect now
        if (soundFontAppliesAt >= 0f)
        {
            soundFontAppliesAt = 0f;
            Tick();
        }
        GameBindings.Save();
        PlayerPrefs.Save();
    }
}
