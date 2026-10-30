using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

/// <summary>
/// The music page of the Setup screen: for each situation of the game, the tracks it plays, which
/// can be listened to, switched off, removed, or added from anywhere on disk, and a reset to the
/// original's for one situation or for all.
/// </summary>
/// <remarks>
/// It edits MusicConfig, which keeps music.txt, so what is done here and what is written there by
/// hand are the same thing. The situations are on the left, the tracks of the one selected on the
/// right. Up and down move, left and right go between the two lists; the other keys are listed at
/// the bottom of the page, and the mouse has a button for each.
/// No other music plays here, so that a track is heard on its own; the title music comes back when
/// the page is left. The soundfont button changes the game's soundfont, the same setting as the
/// options panel's, so that an XMI can be heard through each.
/// Adding goes through the system's file dialog, which a pad cannot drive: on a Steam Deck it is
/// worked with the trackpad or the touch screen.
/// </remarks>
public class MusicSetupPage
{
    private static readonly string[] situationNames =
    {
        "Atmosphere", "Combat", "Combat, hurt", "Combat, winning", "Alert", "Victory", "Level up",
        "Death", "Map", "Sleep", "Conversation", "Title screen"
    };

    private static readonly string[] situationNotes =
    {
        "While exploring. One track at random each time, with a pause between tracks.",
        "During a fight. One track at random, played over and over until the fight ends.",
        "During a fight, while your health is low.",
        "During a fight, while your enemy is nearly dead.",
        "When a creature has noticed you, or while your weapon is raised.",
        "After the last enemy of a fight dies. Played once.",
        "When you gain a level. Played once.",
        "When you die. Played once.",
        "While the map is open, unless the options keep the music playing on the map.",
        "While you sleep.",
        "While you talk to someone.",
        "On the title screen."
    };

    private static readonly Color selectColor = new Color32(255, 213, 64, 255);
    private static readonly Color unselectColor = new Color32(187, 123, 1, 255);
    private static readonly Color offColor = new Color32(120, 110, 100, 255);
    private static readonly Color missingColor = new Color32(200, 90, 70, 255);

    private const int SituationCount = 12;
    // Laid out on a 1920x1080 screen: the two lists, the buttons under them, a status line, the keys.
    private const float PanelTop = 160f;
    private const float PanelHeight = 740f;
    private const float SituationRowHeight = 60f;
    private const float TrackRowHeight = 56f;
    private const float TracksTop = 250f;
    private const int VisibleTrackRows = 11;
    private const float StatusTop = 984f;
    private const float HintsTop = 1022f;
    private const float HintsHeight = 48f;
    private const float TrackRowWidth = 1070f;
    private const float ScrollBarX = 1788f;
    private const float ScrollBarWidth = 18f;
    private const float DurationWidth = 120f;
    // The buttons under the lists share one width; Play, Stop and Remove on a row are narrower.
    private const float ButtonWidth = 260f;
    // Play, Stop and Remove on a row are icons, in square buttons.
    private const float IconButtonWidth = 52f;
    private const float ButtonHeight = 60f;
    private const float ButtonsY = 920f;
    private const float ButtonsGap = 30f;
    private const float SoundFontRowHeight = 52f;
    private const float StatusSeconds = 6f;

    private readonly FrontEnd frontEnd;
    private readonly string[] soundFonts;
    private int situation;
    private int track;
    private int trackScroll;
    private bool tracksFocused;
    private string status = "";
    private float statusUntil;

    // A reset asks first, in a box with Yes and No: which reset, and which answer is lit. No, to begin
    // with, since a yes throws the player's choices away.
    private EAction confirming = EAction.None;
    private bool confirmYes;

    // The scroll bar's thumb being dragged, and where in it the pointer took hold.
    private bool draggingThumb;
    private float thumbGrab;

    // Clicks are seen in OnGUI and acted on in Update, so that the blocking file dialog never opens in
    // the middle of a GUI event.
    private enum EAction { None, Play, Toggle, Remove, Add, ResetOne, ResetAll, SoundFont, ChooseSoundFont, ConfirmYes, ConfirmNo }
    private EAction pending;

    // The soundfont list, a drop-down opened by its button: whether it is open, the one under the
    // pad's or the keyboard's cursor, and the one clicked.
    private bool soundFontListOpen;
    private int soundFontCursor;
    private int soundFontClicked;

    // Remembered for the session: the next dialog opens where the last pick came from.
    private static string lastFolder;

    /// <summary>Set when the Back button is clicked; SetupScreen reads it and clears it.</summary>
    public bool BackClicked { get; set; }

    public MusicSetupPage(FrontEnd frontEnd)
    {
        this.frontEnd = frontEnd;
        MusicConfig.EnsureLoaded();
        soundFonts = FindSoundFonts();

        // The title music would play over whatever is listened to here.
        MusicPlayer.Instance?.StopMusic();
    }

    private EMusicSituation Situation => (EMusicSituation)situation;

    private IReadOnlyList<MusicEntry> Entries => MusicConfig.Entries(Situation);

    public void Update()
    {
        Gamepad pad = Gamepad.current;
        Keyboard keyboard = Keyboard.current;
        bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

        bool up = (pad?.dpad.up.wasPressedThisFrame ?? false) || (keyboard?.upArrowKey.wasPressedThisFrame ?? false);
        bool down = (pad?.dpad.down.wasPressedThisFrame ?? false) || (keyboard?.downArrowKey.wasPressedThisFrame ?? false);
        bool left = (pad?.dpad.left.wasPressedThisFrame ?? false) || (keyboard?.leftArrowKey.wasPressedThisFrame ?? false);
        bool right = (pad?.dpad.right.wasPressedThisFrame ?? false) || (keyboard?.rightArrowKey.wasPressedThisFrame ?? false);
        bool accept = (pad?.aButton.wasPressedThisFrame ?? false)
            || (keyboard?.enterKey.wasPressedThisFrame ?? false) || (keyboard?.numpadEnterKey.wasPressedThisFrame ?? false);
        bool reset = (pad?.startButton.wasPressedThisFrame ?? false) || (keyboard?.rKey.wasPressedThisFrame ?? false);
        bool soundFontKey = (pad?.rightShoulder.wasPressedThisFrame ?? false) || (keyboard?.sKey.wasPressedThisFrame ?? false);

        if (confirming != EAction.None)
        {
            // While the question is up the keys belong to it.
            if (left || right)
            {
                Sound(frontEnd.moveSelection);
                confirmYes = !confirmYes;
            }
            else if (accept)
            {
                Answer(confirmYes);
            }
            else if ((keyboard?.yKey.wasPressedThisFrame ?? false) || pending == EAction.ConfirmYes)
            {
                Answer(true);
            }
            else if ((keyboard?.nKey.wasPressedThisFrame ?? false) || pending == EAction.ConfirmNo)
            {
                Answer(false);
            }
            pending = EAction.None;
            return;
        }

        if (soundFontListOpen)
        {
            // While the list is open the keys belong to it.
            if ((up || down) && soundFonts.Length > 0)
            {
                Sound(frontEnd.moveSelection);
                soundFontCursor = (soundFontCursor + (up ? -1 : 1) + soundFonts.Length) % soundFonts.Length;
            }
            else if (accept)
            {
                ChooseSoundFont(soundFontCursor);
            }
            else if (soundFontKey)
            {
                soundFontListOpen = false;
            }
            else if (pending == EAction.ChooseSoundFont)
            {
                ChooseSoundFont(soundFontClicked);
            }
            pending = EAction.None;
            return;
        }

        if (up || down)
        {
            Move(up ? -1 : 1);
        }
        else if (right && Entries.Count > 0)
        {
            Focus(true);
        }
        else if (left)
        {
            Focus(false);
        }
        else if (accept)
        {
            if (tracksFocused)
            {
                pending = EAction.Play;
            }
            else if (Entries.Count > 0)
            {
                Focus(true);
            }
        }
        else if ((pad?.xButton.wasPressedThisFrame ?? false) || (keyboard?.spaceKey.wasPressedThisFrame ?? false))
        {
            pending = tracksFocused ? EAction.Toggle : EAction.None;
        }
        else if ((pad?.leftShoulder.wasPressedThisFrame ?? false) || (keyboard?.deleteKey.wasPressedThisFrame ?? false))
        {
            pending = tracksFocused ? EAction.Remove : EAction.None;
        }
        else if ((pad?.yButton.wasPressedThisFrame ?? false) || (keyboard?.insertKey.wasPressedThisFrame ?? false))
        {
            pending = EAction.Add;
        }
        else if (soundFontKey)
        {
            pending = EAction.SoundFont;
        }
        else if ((pad?.selectButton.wasPressedThisFrame ?? false) || (reset && shift))
        {
            pending = EAction.ResetAll;
        }
        else if (reset)
        {
            pending = EAction.ResetOne;
        }

        EAction action = pending;
        pending = EAction.None;
        switch (action)
        {
        case EAction.Play:
            TogglePreview();
            break;
        case EAction.Toggle:
            ToggleTrack();
            break;
        case EAction.Remove:
            RemoveTrack();
            break;
        case EAction.Add:
            AddTracks();
            break;
        case EAction.ResetOne:
        case EAction.ResetAll:
            Reset(action);
            break;
        case EAction.SoundFont:
            OpenSoundFontList();
            break;
        }
    }

    /// <summary>
    /// B or Escape: closes the soundfont list or the question, when one is up. Returns false
    /// otherwise, and the screen closes.
    /// </summary>
    public bool Cancel()
    {
        if (soundFontListOpen)
        {
            soundFontListOpen = false;
            return true;
        }
        if (confirming != EAction.None)
        {
            Answer(false);
            return true;
        }
        // Out of the track list is left on the d-pad or the arrow: back leaves the screen.
        return false;
    }

    /// <summary>The screen is closing: whatever was being listened to stops, and FrontEnd starts the title music.</summary>
    public void Leave()
    {
        MusicPlayer.Instance?.StopMusic();
    }

    private void Move(int delta)
    {
        Sound(frontEnd.moveSelection);
        if (tracksFocused)
        {
            int count = Entries.Count;
            if (count > 0)
            {
                track = (track + delta + count) % count;
            }
        }
        else
        {
            situation = (situation + delta + SituationCount) % SituationCount;
            track = 0;
            trackScroll = 0;
        }
        KeepTrackVisible();
    }

    private void Focus(bool tracks)
    {
        if (tracksFocused == tracks)
        {
            return;
        }
        Sound(frontEnd.moveSelection);
        tracksFocused = tracks;
        track = Mathf.Clamp(track, 0, Mathf.Max(0, Entries.Count - 1));
        KeepTrackVisible();
    }

    private void KeepTrackVisible()
    {
        if (track < trackScroll)
        {
            trackScroll = track;
        }
        else if (track >= trackScroll + VisibleTrackRows)
        {
            trackScroll = track - VisibleTrackRows + 1;
        }
        trackScroll = Mathf.Clamp(trackScroll, 0, Mathf.Max(0, Entries.Count - VisibleTrackRows));
    }

    private static bool IsPreviewing(string path)
    {
        MusicPlayer player = MusicPlayer.Instance;
        return player != null && player.IsPlaying && MusicConfig.SamePath(player.currentTrack, path);
    }

    private void TogglePreview()
    {
        MusicPlayer player = MusicPlayer.Instance;
        if (player == null || track >= Entries.Count)
        {
            return;
        }

        string path = MusicConfig.Resolve(Entries[track].Track);
        if (IsPreviewing(path))
        {
            player.StopMusic();
            return;
        }
        if (path == null || !File.Exists(path))
        {
            SetStatus($"{Entries[track].Track} is not on disk.");
            return;
        }

        Sound(frontEnd.makeSelection);
        // From the start even when it is the track that played last: SwitchTrack() does nothing for
        // the track it last started.
        player.StopMusic();
        player.SwitchTrack(path, false);
    }

    private void ToggleTrack()
    {
        if (track >= Entries.Count)
        {
            return;
        }
        Sound(frontEnd.moveSelection);
        MusicConfig.SetEnabled(Situation, track, !Entries[track].Enabled);
    }

    private void RemoveTrack()
    {
        if (track >= Entries.Count)
        {
            return;
        }

        if (IsPreviewing(MusicConfig.Resolve(Entries[track].Track)))
        {
            MusicPlayer.Instance.StopMusic();
        }

        Sound(frontEnd.backSelection);
        MusicConfig.Remove(Situation, track);
        if (Entries.Count == 0)
        {
            Focus(false);
            SetStatus($"{situationNames[situation]} now plays no music.");
        }
        track = Mathf.Clamp(track, 0, Mathf.Max(0, Entries.Count - 1));
        KeepTrackVisible();
    }

    private void AddTracks()
    {
        if (!NativeFileDialog.IsAvailable)
        {
            SetStatus($"Copy your files into {MusicConfig.Folder} and add a line for each to music.txt.");
            return;
        }

        Sound(frontEnd.makeSelection);
        string start = lastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        string[] files = NativeFileDialog.OpenFiles($"Add music: {situationNames[situation]}", "Music",
            MusicConfig.Extensions, start, out string error);
        if (error != null)
        {
            SetStatus($"Cannot add music: {error}");
            return;
        }
        if (files.Length == 0)
        {
            return;
        }

        lastFolder = Path.GetDirectoryName(files[0]);
        int before = Entries.Count;
        List<string> problems = new();
        foreach (string file in files)
        {
            string problem = MusicConfig.Add(Situation, file);
            if (problem != null)
            {
                problems.Add(problem);
            }
        }

        int added = Entries.Count - before;
        if (problems.Count > 0)
        {
            SetStatus(string.Join("; ", problems));
        }
        else if (added == 0)
        {
            SetStatus("Already in the list.");
        }
        else
        {
            SetStatus(added == 1 ? "1 track added." : $"{added} tracks added.");
        }

        if (added > 0)
        {
            tracksFocused = true;
            track = Entries.Count - 1;
            KeepTrackVisible();
        }
    }

    private void Reset(EAction which)
    {
        Sound(frontEnd.moveSelection);
        confirming = which;
        confirmYes = false;
    }

    private string ConfirmQuestion => confirming == EAction.ResetAll
        ? "Go back to the original tracks in every situation? Your own tracks leave all the lists."
        : $"Go back to the original tracks of {situationNames[situation]}? Your own tracks leave its list.";

    private void Answer(bool yes)
    {
        EAction which = confirming;
        confirming = EAction.None;
        if (!yes)
        {
            Sound(frontEnd.backSelection);
            return;
        }

        Sound(frontEnd.makeSelection);
        MusicPlayer.Instance?.StopMusic();
        if (which == EAction.ResetAll)
        {
            MusicConfig.Reset();
            SetStatus("The original tracks are back in every situation.");
        }
        else
        {
            MusicConfig.Reset(Situation);
            SetStatus($"The original tracks of {situationNames[situation]} are back.");
        }
        track = 0;
        trackScroll = 0;
        Focus(false);
    }

    private static string[] FindSoundFonts()
    {
        try
        {
            string folder = Path.Combine(Application.streamingAssetsPath, "Soundfonts");
            string[] files = Directory.GetFiles(folder, "*.sf2");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            // Named the way the options panel stores them.
            return Array.ConvertAll(files, f => "Soundfonts/" + Path.GetFileName(f));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Music: no soundfonts found: {e.Message}");
            return Array.Empty<string>();
        }
    }

    private static string CurrentSoundFont => PlayerPrefs.GetString("Options_SoundFont", "Soundfonts/MT32.sf2");

    private int CurrentSoundFontIndex => Array.FindIndex(soundFonts, f => string.Equals(Path.GetFileName(f),
        Path.GetFileName(CurrentSoundFont), StringComparison.OrdinalIgnoreCase));

    private void OpenSoundFontList()
    {
        if (soundFonts.Length == 0)
        {
            SetStatus("No soundfonts found.");
            return;
        }
        Sound(frontEnd.makeSelection);
        soundFontListOpen = true;
        soundFontCursor = Mathf.Max(0, CurrentSoundFontIndex);
    }

    private void ChooseSoundFont(int index)
    {
        soundFontListOpen = false;
        if (index < 0 || index >= soundFonts.Length)
        {
            return;
        }

        Sound(frontEnd.makeSelection);
        string chosen = soundFonts[index];
        PlayerPrefs.SetString("Options_SoundFont", chosen);
        PlayerPrefs.Save();
        // Restarts the XMI being listened to, through the new soundfont; does nothing for the same one.
        MusicPlayer.Instance?.ReloadSynthesizerWithSoundFont(chosen, PlayerPrefs.GetFloat("Options_MusicVolume", 0.5f));
    }

    /// <summary>Where the soundfont button is, among the buttons under the lists.</summary>
    private const int SoundFontButton = 3;

    private static float ButtonX(int index, int count) =>
        (1920f - count * ButtonWidth - (count - 1) * ButtonsGap) / 2 + index * (ButtonWidth + ButtonsGap);

    /// <summary>The rows of the open soundfont list, in reference pixels, stacked up from its button.</summary>
    private Rect SoundFontRow(int index, int buttonCount)
    {
        float top = ButtonsY - 8 - (soundFonts.Length - index) * SoundFontRowHeight;
        return new Rect(ButtonX(SoundFontButton, buttonCount), top, ButtonWidth, SoundFontRowHeight);
    }

    private void SetStatus(string text, float seconds = StatusSeconds)
    {
        status = text;
        statusUntil = Time.unscaledTime + seconds;
    }

    private static void Sound(AudioClip clip)
    {
        if (clip != null)
        {
            Utils.PlayClip2d(clip);
        }
    }

    public void OnGUI(Rect area, float scale)
    {
        Rect R(float x, float y, float w, float h) => new Rect(area.x + x * scale, area.y + y * scale, w * scale, h * scale);

        GUIStyle text = new GUIStyle(frontEnd.buttonLabelStyle)
        {
            fontSize = Mathf.RoundToInt(30 * scale),
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
            clipping = TextClipping.Clip
        };
        GUIStyle small = new GUIStyle(text) { fontSize = Mathf.RoundToInt(24 * scale), wordWrap = true };
        GUIStyle centered = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
        GUIStyle buttonText = new GUIStyle(text) { fontSize = Mathf.RoundToInt(26 * scale), alignment = TextAnchor.MiddleCenter };
        GUIStyle durationText = new GUIStyle(text) { alignment = TextAnchor.MiddleRight };
        string[] labels = { "Add files...", "Reset", "Reset all", "", "Back" };
        EAction[] actions = { EAction.Add, EAction.ResetOne, EAction.ResetAll, EAction.SoundFont, EAction.None };

        // The question and the soundfont list are drawn last, over everything, but take their clicks
        // first: a click is taken by whatever asks for it first, and what lies under them is drawn
        // before them. Any other click, while one of them is up, only closes the list.
        Rect dialog = R(560, 400, 800, 240);
        Rect yes = R(560 + 400 - 20 - ButtonWidth, 400 + 150, ButtonWidth, 60);
        Rect no = R(560 + 400 + 20, 400 + 150, ButtonWidth, 60);
        if (confirming != EAction.None)
        {
            if (GuiInput.TryConsumeClickInRect(yes))
            {
                pending = EAction.ConfirmYes;
            }
            else if (GuiInput.TryConsumeClickInRect(no))
            {
                pending = EAction.ConfirmNo;
            }
            else if (GuiInput.IsLeftMouseDownGui || Event.current.type == EventType.ScrollWheel)
            {
                Event.current.Use();
            }
        }
        else if (soundFontListOpen)
        {
            for (int i = 0; i < soundFonts.Length; ++i)
            {
                Rect row = SoundFontRow(i, labels.Length);
                if (GuiInput.TryConsumeClickInRect(R(row.x, row.y, row.width, row.height)))
                {
                    soundFontClicked = i;
                    pending = EAction.ChooseSoundFont;
                }
            }
            if (GuiInput.IsLeftMouseDownGui)
            {
                Event.current.Use();
                soundFontListOpen = false;
            }
        }

        // The situations.
        Panel(R(100, PanelTop, 560, PanelHeight));
        for (int i = 0; i < SituationCount; ++i)
        {
            Rect row = R(110, PanelTop + 10 + i * SituationRowHeight, 540, SituationRowHeight);
            bool selected = i == situation;
            if (selected)
            {
                Highlight(row, !tracksFocused);
            }

            IReadOnlyList<MusicEntry> entries = MusicConfig.Entries((EMusicSituation)i);
            int on = 0;
            foreach (MusicEntry entry in entries)
            {
                on += entry.Enabled ? 1 : 0;
            }
            string count = entries.Count == 0 ? "none" : on == entries.Count ? on.ToString() : $"{on} of {entries.Count}";
            Color color = selected ? selectColor : unselectColor;
            Label(row, "  " + situationNames[i], text, color);
            Label(new Rect(row.x, row.y, row.width - 16 * scale, row.height), count,
                new GUIStyle(text) { alignment = TextAnchor.MiddleRight }, color);
            if (GuiInput.TryConsumeClickInRect(row))
            {
                if (i != situation)
                {
                    situation = i;
                    track = 0;
                    trackScroll = 0;
                    Sound(frontEnd.moveSelection);
                }
                tracksFocused = false;
            }
        }

        // The tracks of the selected situation.
        Rect tracksPanel = R(700, PanelTop, 1120, PanelHeight);
        Panel(tracksPanel);
        Label(R(720, PanelTop + 10, 1080, 70), situationNotes[situation], small, unselectColor);

        IReadOnlyList<MusicEntry> tracks = Entries;
        ScrollTracks(tracksPanel, tracks.Count, R(ScrollBarX, TracksTop, ScrollBarWidth, VisibleTrackRows * TrackRowHeight));
        if (tracks.Count == 0)
        {
            Label(R(720, TracksTop, 1060, TrackRowHeight), "No music. Add files to give this situation some.", text, unselectColor);
        }
        for (int row = 0; row < VisibleTrackRows; ++row)
        {
            int i = trackScroll + row;
            if (i >= tracks.Count)
            {
                break;
            }

            MusicEntry entry = tracks[i];
            Rect rect = R(710, TracksTop + row * TrackRowHeight, TrackRowWidth, TrackRowHeight);
            bool selected = tracksFocused && i == track;
            if (selected)
            {
                Highlight(rect, true);
            }

            string path = MusicConfig.Resolve(entry.Track);
            bool present = path != null && File.Exists(path);
            string name = MusicConfig.IsOriginal(entry.Track)
                ? entry.Track.Substring(MusicConfig.SoundFolderPrefix.Length) + "   (original)"
                : Path.GetFileName(entry.Track);
            if (!present)
            {
                name += "   (missing)";
            }
            Color color = !present ? missingColor : !entry.Enabled ? offColor : selected ? selectColor : unselectColor;

            float buttonW = IconButtonWidth * scale;
            Rect toggle = new Rect(rect.x + 12 * scale, rect.y + 11 * scale, 34 * scale, 34 * scale);
            Rect play = new Rect(rect.xMax - 2 * buttonW - 20 * scale, rect.y + 6 * scale, buttonW, rect.height - 12 * scale);
            Rect remove = new Rect(rect.xMax - buttonW - 10 * scale, rect.y + 6 * scale, buttonW, rect.height - 12 * scale);
            Rect duration = new Rect(play.x - (DurationWidth + 12) * scale, rect.y, DurationWidth * scale, rect.height);
            Rect nameRect = new Rect(rect.x + 60 * scale, rect.y, duration.x - rect.x - 72 * scale, rect.height);

            // A long name is cut short with an ellipsis rather than run under the length.
            Label(nameRect, Utils.Ellipsize(name, text, nameRect.width), text, color);
            if (present)
            {
                Label(duration, TrackLengths.Text(path, frontEnd), durationText, color);
            }

            if (CheckBox(toggle, entry.Enabled, scale))
            {
                SelectTrack(i);
                pending = EAction.Toggle;
            }
            else if (present && IconButton(play, IsPreviewing(path) ? SetupIcons.Stop : SetupIcons.Play, scale))
            {
                SelectTrack(i);
                pending = EAction.Play;
            }
            else if (IconButton(remove, SetupIcons.Remove, scale))
            {
                SelectTrack(i);
                pending = EAction.Remove;
            }
            else if (GuiInput.TryConsumeClickInRect(rect))
            {
                SelectTrack(i);
            }
        }

        // The buttons, one width for all of them. The soundfont one is a drop-down: it names the
        // soundfont in use, and opens the list of them above it.
        labels[SoundFontButton] = "Soundfont: " + Path.GetFileNameWithoutExtension(CurrentSoundFont);
        for (int b = 0; b < labels.Length; ++b)
        {
            if (Button(R(ButtonX(b, labels.Length), ButtonsY, ButtonWidth, ButtonHeight), labels[b], buttonText, scale))
            {
                if (actions[b] == EAction.None)
                {
                    BackClicked = true;
                }
                else
                {
                    pending = actions[b];
                }
            }
        }

        // What just happened, and what the keys do.
        if (Time.unscaledTime < statusUntil && !string.IsNullOrEmpty(status))
        {
            Label(R(100, StatusTop, 1720, 34), status, centered, selectColor);
        }

        Panel(R(100, HintsTop, 1720, HintsHeight));
        Hints(R(100, HintsTop, 1720, HintsHeight), scale, GameInput.LastActiveDevice == GameInputDevice.Gamepad);

        if (soundFontListOpen)
        {
            int current = CurrentSoundFontIndex;
            for (int i = 0; i < soundFonts.Length; ++i)
            {
                Rect place = SoundFontRow(i, labels.Length);
                Rect row = R(place.x, place.y, place.width, place.height);
                Panel(row);
                Panel(row);
                bool hover = row.Contains(GuiInput.MousePositionGuiSpace);
                if (i == soundFontCursor || hover)
                {
                    Highlight(row, true);
                }
                Frame(row, unselectColor, scale);
                string fontName = Path.GetFileNameWithoutExtension(soundFonts[i]) + (i == current ? "  (in use)" : "");
                Label(row, fontName, buttonText, hover ? Color.white : i == soundFontCursor ? selectColor : unselectColor);
            }
        }

        if (confirming != EAction.None)
        {
            // A dark veil over the page, then the box.
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
            Panel(dialog);
            Panel(dialog);
            Frame(dialog, selectColor, scale);
            Label(R(600, 420, 720, 110), ConfirmQuestion, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(28 * scale) },
                selectColor);
            ChoiceButton(yes, "Yes", confirmYes, buttonText, scale);
            ChoiceButton(no, "No", !confirmYes, buttonText, scale);
        }
    }

    /// <summary>
    /// The mouse wheel over the track list, and its scroll bar: drawn at its right while there are
    /// more tracks than rows, its thumb dragged, or the bar clicked above or below it for a page.
    /// </summary>
    private void ScrollTracks(Rect panel, int count, Rect bar)
    {
        int maxScroll = Mathf.Max(0, count - VisibleTrackRows);
        Event e = Event.current;
        if (e.type == EventType.ScrollWheel && panel.Contains(e.mousePosition))
        {
            trackScroll = Mathf.Clamp(trackScroll + (e.delta.y > 0 ? 1 : -1), 0, maxScroll);
            e.Use();
        }

        if (maxScroll == 0)
        {
            draggingThumb = false;
            return;
        }

        float thumbHeight = Mathf.Max(bar.height * VisibleTrackRows / count, bar.width * 2);
        float travel = bar.height - thumbHeight;
        Rect thumb = new Rect(bar.x, bar.y + travel * trackScroll / maxScroll, bar.width, thumbHeight);

        if (e.type == EventType.MouseDown && e.button == 0 && bar.Contains(e.mousePosition))
        {
            if (thumb.Contains(e.mousePosition))
            {
                draggingThumb = true;
                thumbGrab = e.mousePosition.y - thumb.y;
            }
            else
            {
                int page = VisibleTrackRows - 1;
                trackScroll = Mathf.Clamp(trackScroll + (e.mousePosition.y < thumb.y ? -page : page), 0, maxScroll);
            }
            e.Use();
            GuiInput.MarkPrimaryClickConsumed();
        }
        else if (e.type == EventType.MouseDrag && draggingThumb)
        {
            float y = e.mousePosition.y - thumbGrab - bar.y;
            trackScroll = Mathf.Clamp(Mathf.RoundToInt(travel > 0 ? y / travel * maxScroll : 0), 0, maxScroll);
            e.Use();
        }
        else if (e.type == EventType.MouseUp)
        {
            draggingThumb = false;
        }

        Color previous = GUI.color;
        GUI.color = new Color(1f, 0.84f, 0.25f, 0.15f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = draggingThumb || thumb.Contains(GuiInput.MousePositionGuiSpace) ? Color.white : unselectColor;
        GUI.DrawTexture(thumb, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    /// <summary>A Yes or No of the question: lit when it is the answer the keys would give.</summary>
    private static void ChoiceButton(Rect rect, string label, bool lit, GUIStyle style, float scale)
    {
        bool hover = rect.Contains(GuiInput.MousePositionGuiSpace);
        if (lit)
        {
            Highlight(rect, true);
        }
        Frame(rect, hover || lit ? Color.white : unselectColor, scale);
        Label(rect, label, style, hover || lit ? Color.white : selectColor);
    }

    private void SelectTrack(int index)
    {
        tracksFocused = true;
        track = index;
        KeepTrackVisible();
    }

    private static void Panel(Rect rect)
    {
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static void Highlight(Rect rect, bool focused)
    {
        Color previous = GUI.color;
        GUI.color = focused ? new Color(1f, 0.84f, 0.25f, 0.22f) : new Color(1f, 0.84f, 0.25f, 0.10f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static void Label(Rect rect, string content, GUIStyle style, Color color)
    {
        style.normal.textColor = color;
        GUI.Label(rect, content, style);
    }

    private static void Frame(Rect rect, Color color, float scale)
    {
        float line = Mathf.Max(1f, 2f * scale);
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, line), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - line, rect.width, line), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, line, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - line, rect.y, line, rect.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    /// <summary>The hints under the page, as the achievements page draws its own (HintBar).</summary>
    private void Hints(Rect row, float scale, bool pad)
    {
        Hint[] hints = pad
            ? new[]
            {
                new Hint("on/off", HintKey.Of(frontEnd.xButton)),
                new Hint("play/stop", HintKey.Of(frontEnd.aButton)),
                new Hint("remove", HintKey.PadButton("LB")),
                new Hint("add files", HintKey.Of(frontEnd.yButton)),
                new Hint("reset", HintKey.PadButton("Start")),
                new Hint("reset all", HintKey.PadButton("Select")),
                new Hint("soundfont", HintKey.PadButton("RB")),
                new Hint("back", HintKey.Of(frontEnd.bButton)),
            }
            : new[]
            {
                new Hint("on/off", HintKey.Keyboard("Space")),
                new Hint("play/stop", HintKey.Keyboard("Enter")),
                new Hint("remove", HintKey.Keyboard("Delete")),
                new Hint("add files", HintKey.Keyboard("Insert")),
                new Hint("reset", HintKey.Keyboard("R")),
                new Hint("reset all", HintKey.Keyboard("Shift"), HintKey.Keyboard("R")),
                new Hint("soundfont", HintKey.Keyboard("S")),
                new Hint("back", HintKey.Of(frontEnd.escapeKeyTexture)),
            };
        HintBar.Draw(row, scale, frontEnd.buttonLabelStyle, frontEnd.keyLetterStyle, frontEnd.blankKeyTexture, hints);
    }

    /// <summary>A square button holding an icon, framed like the others, lit under the pointer. True when clicked.</summary>
    private static bool IconButton(Rect rect, Texture2D icon, float scale)
    {
        bool hover = rect.Contains(GuiInput.MousePositionGuiSpace);
        Frame(rect, hover ? Color.white : unselectColor, scale);
        float size = Mathf.Min(rect.width, rect.height) * 0.6f;
        Color previous = GUI.color;
        GUI.color = hover ? Color.white : selectColor;
        GUI.DrawTexture(new Rect(rect.center.x - size / 2, rect.center.y - size / 2, size, size), icon);
        GUI.color = previous;
        return GuiInput.TryConsumeClickInRect(rect);
    }

    /// <summary>A text button with a thin frame, lit under the pointer. True when clicked.</summary>
    private static bool Button(Rect rect, string label, GUIStyle style, float scale)
    {
        bool hover = rect.Contains(GuiInput.MousePositionGuiSpace);
        Frame(rect, hover ? Color.white : unselectColor, scale);
        Label(rect, label, style, hover ? Color.white : selectColor);
        return GuiInput.TryConsumeClickInRect(rect);
    }

    /// <summary>The on and off switch of a track: a box, filled when on. True when clicked.</summary>
    private static bool CheckBox(Rect rect, bool on, float scale)
    {
        bool hover = rect.Contains(GuiInput.MousePositionGuiSpace);
        Frame(rect, hover ? Color.white : unselectColor, scale);
        if (on)
        {
            float inset = 7f * scale;
            Color previous = GUI.color;
            GUI.color = hover ? Color.white : selectColor;
            GUI.DrawTexture(new Rect(rect.x + inset, rect.y + inset, rect.width - 2 * inset, rect.height - 2 * inset), Texture2D.whiteTexture);
            GUI.color = previous;
        }
        return GuiInput.TryConsumeClickInRect(rect);
    }
}

/// <summary>
/// How long each track lasts, for the Setup page: measured once per file, one at a time in the
/// background, and remembered for the session.
/// </summary>
/// <remarks>
/// An XMI is played through a sequencer of its own, silently and as fast as it will go: its clock
/// is fixed, so the time it ends at is the track's length. A recorded track is asked of Unity's
/// decoder in streaming mode, which gave the right length at once on the probe (mod 900, October
/// 2026) where a full decode took a quarter of a second.
/// </remarks>
internal static class TrackLengths
{
    // NaN while waiting to be measured, negative when it could not be.
    private static readonly Dictionary<string, float> seconds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> waiting = new();
    private static MonoBehaviour runner;

    /// <summary>"3:12", or nothing while the file is still to be measured, or "?".</summary>
    public static string Text(string path, MonoBehaviour host)
    {
        if (seconds.TryGetValue(path, out float length))
        {
            return float.IsNaN(length) ? "" : length < 0f ? "?" : Format(length);
        }

        seconds[path] = float.NaN;
        waiting.Enqueue(path);
        // The one that measured may be gone - the title screen is rebuilt after a game - and with it
        // its coroutine.
        if (runner == null && host != null)
        {
            runner = host;
            host.StartCoroutine(MeasureWaiting());
        }
        return "";
    }

    private static string Format(float length)
    {
        int whole = Mathf.RoundToInt(length);
        return $"{whole / 60}:{whole % 60:D2}";
    }

    private static IEnumerator MeasureWaiting()
    {
        while (waiting.Count > 0)
        {
            string path = waiting.Dequeue();
            float length = -1f;
            if (Path.GetExtension(path).Equals(".xmi", StringComparison.OrdinalIgnoreCase))
            {
                length = MeasureXmi(path);
            }
            else
            {
                using UnityWebRequest web = UnityWebRequestMultimedia.GetAudioClip(MusicPlayer.FileUri(path), MusicPlayer.AudioTypeOf(path));
                ((DownloadHandlerAudioClip)web.downloadHandler).streamAudio = true;
                yield return web.SendWebRequest();
                if (web.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        AudioClip clip = DownloadHandlerAudioClip.GetContent(web);
                        if (clip != null)
                        {
                            length = clip.length > 0f ? clip.length : -1f;
                            UnityEngine.Object.Destroy(clip);
                        }
                    }
                    catch (Exception)
                    {
                        length = -1f;
                    }
                }
            }
            seconds[path] = length;
            yield return null;
        }
        runner = null;
    }

    private static float MeasureXmi(string path)
    {
        var commands = new ConcurrentQueue<MusicCommand>();
        var sequencer = new XMISequencer(path, commands, 1000, false);
        if (!sequencer.IsLoaded)
        {
            return -1f;
        }
        // A tenth of a second a step, an hour at most.
        for (int step = 0; sequencer.IsLoaded && step < 36000; ++step)
        {
            sequencer.ProcessSamples(100);
            while (commands.TryDequeue(out _))
            {
            }
        }
        return sequencer.IsLoaded ? -1f : (float)sequencer.CurrentTime;
    }
}

/// <summary>
/// The play, stop and remove signs of a track's row on the Setup screen, white so that GUI.color
/// tints them. Made once, the first time they are asked for, so there is no asset to ship.
/// </summary>
internal static class SetupIcons
{
    private const int Size = 64;
    private const int Samples = 4;

    private static Texture2D play;
    private static Texture2D stop;
    private static Texture2D remove;

    public static Texture2D Play => play ??= Make((x, y) => x >= 18 && Mathf.Abs(y - 32) <= (52 - x) * 20f / 34f);

    public static Texture2D Stop => stop ??= Make((x, y) => x >= 16 && x <= 48 && y >= 16 && y <= 48);

    public static Texture2D Remove => remove ??= Make((x, y) =>
        x >= 12 && x <= 52 && y >= 12 && y <= 52 && (Mathf.Abs(x - y) <= 5.5f || Mathf.Abs(x + y - 64) <= 5.5f));

    /// <summary>A white shape on clear, its edge smoothed by sampling each pixel Samples x Samples times.</summary>
    private static Texture2D Make(Func<float, float, bool> inside)
    {
        Texture2D texture = NewTexture(Size);
        Color[] pixels = new Color[Size * Size];
        for (int py = 0; py < Size; ++py)
        {
            for (int px = 0; px < Size; ++px)
            {
                int hits = 0;
                for (int sy = 0; sy < Samples; ++sy)
                {
                    for (int sx = 0; sx < Samples; ++sx)
                    {
                        // Counted from the top, as the shapes are written; GetPixels() starts at the bottom.
                        float x = px + (sx + 0.5f) / Samples;
                        float y = Size - (py + (sy + 0.5f) / Samples);
                        hits += inside(x, y) ? 1 : 0;
                    }
                }
                pixels[py * Size + px] = new Color(1f, 1f, 1f, hits / (float)(Samples * Samples));
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private static Texture2D NewTexture(int size)
    {
        return new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
    }
}
