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
    /// <summary>The keyboard's map: no rows, drawn by ControlsMap.</summary>
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

/// <summary>One setting of the game menu: where it is shown, what it says, and how it is kept.</summary>
public sealed class GameOption
{
    public readonly EOptionSection Section;
    /// <summary>The PlayerPrefs key it is kept under; the rest of the game reads the same key.</summary>
    public readonly string Key;
    public readonly string Label;
    public readonly string Description;
    /// <summary>What the original game does, for a row that changes how the game plays; null otherwise.</summary>
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
    /// <summary>The names of a Choice row, kept as their place in this list; null for the soundfont.</summary>
    public readonly string[] Choices;
    private readonly Func<float, string> format;

    public GameOption(EOptionSection section, string key, string label, string description, EOptionKind kind,
        float defaultValue, float min = 0f, float max = 1f, float step = 1f, bool storedAsFloat = false,
        Func<float, string> format = null, string original = null, float originalValue = float.NaN, string[] choices = null)
    {
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
/// list of soundfonts found on disk.
/// </remarks>
public static class GameOptions
{
    public const string SoundFontKey = "Options_SoundFont";
    public const string DefaultSoundFont = "Soundfonts/MT32.sf2";
    public const string MusicVolumeKey = "Options_MusicVolume";
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
            "Chooses the soundfont the music is played with. Different soundfonts emulate classic sound cards such as the MT-32.",
            EOptionKind.Choice, 0f),
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
            "How quickly the view turns with the right stick. At 100% half a turn a second across, a quarter up and down.",
            EOptionKind.Slider, PlayerInput.DefaultPadLookSpeed, PlayerInput.MinPadLookSpeed, PlayerInput.MaxPadLookSpeed, 0.1f,
            storedAsFloat: true, format: Percent),
        new(EOptionSection.Input, "Options_InvertLook", "Inverted look up/down",
            "Reverses up and down when looking with the mouse or the gamepad.",
            EOptionKind.Toggle, 0f),
        new(EOptionSection.Input, PadGlyphs.PrefsKey, "Button prompts",
            "How the pad's buttons are shown: A, B, X, Y, LB, RB, LT, RT, Start and Select, or the PlayStation's cross, circle, square, triangle, L1, R1, L2, R2, Options and Share.\n<b>Automatic:</b> the PlayStation's while a PlayStation pad is connected.",
            EOptionKind.Choice, 0f, choices: PadGlyphs.StyleNames),
        new(EOptionSection.Input, "Options_AutoJump", "Automatic jumping",
            "<b>Enabled:</b> you jump by yourself at the edge when running straight at a gap or a deep drop. To step off a ledge instead, hold A or D as well, or push the stick only partway.",
            EOptionKind.Toggle, 0f),
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

    /// <summary>The soundfonts on disk, found again; called when the menu opens.</summary>
    public static void ScanSoundFonts()
    {
        try
        {
            string folder = Path.Combine(Application.streamingAssetsPath, "Soundfonts");
            if (!Directory.Exists(folder))
            {
                soundFonts = new[] { CurrentSoundFont };
                return;
            }
            string[] files = Directory.GetFiles(folder, "*.sf2", SearchOption.TopDirectoryOnly);
            soundFonts = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                soundFonts[i] = "Soundfonts/" + Path.GetFileName(files[i]);
            }
            Array.Sort(soundFonts, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error scanning soundfonts: {e.Message}");
            soundFonts = new[] { CurrentSoundFont };
        }
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
        switch (o.Kind)
        {
            case EOptionKind.Page:
                return 0f;
            case EOptionKind.Choice when o.Choices != null:
                return Mathf.Clamp(PlayerPrefs.GetInt(o.Key, Mathf.RoundToInt(o.Default)), 0, o.Choices.Length - 1);
            case EOptionKind.Choice:
                return Mathf.Max(0, SoundFontIndex(CurrentSoundFont));
            default:
                return o.StoredAsFloat ? PlayerPrefs.GetFloat(o.Key, o.Default) : PlayerPrefs.GetInt(o.Key, Mathf.RoundToInt(o.Default));
        }
    }

    /// <summary>What the row shows as its value: the number formatted, or the soundfont's name.</summary>
    public static string ValueText(GameOption o)
    {
        if (o.Kind == EOptionKind.Page)
        {
            return "";
        }
        if (o.Kind == EOptionKind.Choice && o.Choices != null)
        {
            return o.Choices[Mathf.RoundToInt(Get(o))];
        }
        return IsSoundFont(o) ? Path.GetFileNameWithoutExtension(CurrentSoundFont) : o.Format(Get(o));
    }

    public static string DefaultText(GameOption o)
    {
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
        if (o.Kind == EOptionKind.Page)
        {
            return true;
        }
        return IsSoundFont(o)
            ? SoundFontIndex(CurrentSoundFont) == SoundFontIndex(DefaultSoundFont)
            : Mathf.Abs(Get(o) - o.Default) < 0.001f;
    }

    /// <summary>Writes a value, clamped and snapped to the row's steps, and puts it into effect.</summary>
    public static void Set(GameOption o, float value)
    {
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
                    int i = ((Mathf.RoundToInt(value) % soundFonts.Length) + soundFonts.Length) % soundFonts.Length;
                    SetSoundFont(soundFonts[i]);
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

    /// <summary>The soundfont's row: a Choice whose names are the files found on disk, kept by name.</summary>
    private static bool IsSoundFont(GameOption o) => o.Kind == EOptionKind.Choice && o.Choices == null;

    private static void SetSoundFont(string name)
    {
        PlayerPrefs.SetString(SoundFontKey, name);
        soundFontAppliesAt = Time.unscaledTime + SoundFontSettleSeconds;
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
        if (soundFontAppliesAt >= 0f && Time.unscaledTime >= soundFontAppliesAt)
        {
            soundFontAppliesAt = -1f;
            MusicPlayer.Instance?.ReloadSynthesizerWithSoundFont(CurrentSoundFont, PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume));
        }
    }

    /// <summary>Every row of these sections back to its default.</summary>
    public static void ResetSections(params EOptionSection[] sections)
    {
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

    /// <summary>True when some row of these sections is away from its default.</summary>
    public static bool AnyChangedFromDefault(params EOptionSection[] sections)
    {
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

        public Snapshot()
        {
            foreach (GameOption o in All)
            {
                if (!IsSoundFont(o) && o.Kind != EOptionKind.Page)
                {
                    numbers[o] = Get(o);
                }
            }
            soundFont = CurrentSoundFont;
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
            return CurrentSoundFont != soundFont;
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
            if (CurrentSoundFont != soundFont)
            {
                SetSoundFont(soundFont);
            }
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
            if (o.Kind == EOptionKind.Page)
            {
                continue;
            }
            if (IsSoundFont(o))
            {
                PlayerPrefs.SetString(o.Key, CurrentSoundFont);
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
        PlayerPrefs.Save();
    }
}
