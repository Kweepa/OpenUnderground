using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

/// <summary>
/// The music tracks: for each situation of the game, the tracks it plays, which can be listened to,
/// switched off, removed, or added from anywhere on disk, and a reset to the original's for one
/// situation or for all. A page of the game menu, opened from the Music tracks row of AUDIO, on the
/// title screen and in a game alike.
/// </summary>
/// <remarks>
/// It edits MusicConfig, which keeps music.txt, so what is done here and what is written there by
/// hand are the same thing. The situations are on the left, the tracks of the one selected on the
/// right. Up and down move, left and right go between the two lists; the other keys are listed at
/// the foot of the menu, and the mouse has a button for each.
/// No other music plays here, so that a track is heard on its own; whoever opened the page gives the
/// music back when it is left (the title music, or the game's). The soundfont button changes the
/// game's soundfont, the same setting as the Soundfont row of AUDIO, so that an XMI can be heard through each.
/// Drawn in the menu's look through GameMenu, in reference pixels, so it fits 1366x768 at scale 1:
/// the two lists scroll when the height does not hold them.
/// Adding goes through the system's file dialog, which a pad cannot drive: on a Steam Deck it is
/// worked with the trackpad or the touch screen.
/// </remarks>
public class MusicSetupPage : IGameMenuSubPage
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
    // In reference pixels, inside the area the menu gives the page.
    private const float StatusHeight = 36f;
    private const float ScrollBarWidth = 14f;
    private const float NoteHeight = 70f;
    private const float DurationWidth = 110f;
    private const float IconButtonWidth = 44f;
    private const float BoxSize = 30f;
    private const float SoundFontRowHeight = 46f;
    private const float StatusSeconds = 6f;
    // Select held this long is Reset all, let go sooner Reset: the same as on the menu's other pages.
    private const float SelectHoldSeconds = 0.85f;

    private readonly GameMenu menu;
    private readonly MonoBehaviour host;
    private readonly Action onLeave;
    private readonly string[] soundFonts;
    private int situation;
    private int situationScroll;
    private int track;
    private int trackScroll;
    private bool tracksFocused;
    private string status = "";
    private float statusUntil;
    private float selectHeld;
    private bool selectFired;

    // How many rows each list showed last, for the keys between two draws.
    private int situationRows = SituationCount;
    private int trackRows = 8;

    private bool draggingSituations;
    private bool draggingTracks;
    private float grab;

    // Clicks are seen in the draw and acted on in Update, so that the blocking file dialog never opens
    // in the middle of a GUI event.
    private enum EAction { None, Play, Toggle, Remove, Add, ResetOne, ResetAll, SoundFont, ChooseSoundFont }
    private EAction pending;

    // The soundfont list, a drop-down opened by its button: whether it is open, the one under the
    // pad's or the keyboard's cursor, and the one clicked.
    private bool soundFontListOpen;
    private int soundFontCursor;
    private int soundFontClicked;

    // Remembered for the session: the next dialog opens where the last pick came from.
    private static string lastFolder;

    /// <param name="menu">The menu the page is drawn in, and asks its questions through.</param>
    /// <param name="host">A component to measure the tracks' lengths on, in the background.</param>
    /// <param name="onLeave">Gives the music back when the page goes.</param>
    public MusicSetupPage(GameMenu menu, MonoBehaviour host, Action onLeave)
    {
        this.menu = menu;
        this.host = host;
        this.onLeave = onLeave;
        MusicConfig.EnsureLoaded();
        soundFonts = FindSoundFonts();

        // The music playing would play over whatever is listened to here.
        MusicPlayer.Instance?.StopMusic();
    }

    public string Title => "MUSIC TRACKS";

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
        bool soundFontKey = (pad?.rightShoulder.wasPressedThisFrame ?? false) || (keyboard?.sKey.wasPressedThisFrame ?? false);

        if (soundFontListOpen)
        {
            // While the list is open the keys belong to it.
            if ((up || down) && soundFonts.Length > 0)
            {
                Sound(menu.Skin.MoveSound);
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

        // Select is Reset, Select held Reset all, as on the menu's other pages.
        if (pad != null && pad.selectButton.isPressed)
        {
            selectHeld += Time.unscaledDeltaTime;
            if (!selectFired && selectHeld >= SelectHoldSeconds)
            {
                selectFired = true;
                pending = EAction.ResetAll;
            }
        }
        else
        {
            if (selectHeld > 0f && !selectFired)
            {
                pending = EAction.ResetOne;
            }
            selectHeld = 0f;
            selectFired = false;
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
        else if (keyboard?.rKey.wasPressedThisFrame ?? false)
        {
            pending = shift ? EAction.ResetAll : EAction.ResetOne;
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
            AskReset(action);
            break;
        case EAction.SoundFont:
            OpenSoundFontList();
            break;
        }
    }

    /// <summary>B or Escape: closes the soundfont list when it is open. Returns false otherwise, and the page goes.</summary>
    public bool Cancel()
    {
        if (soundFontListOpen)
        {
            soundFontListOpen = false;
            return true;
        }
        return false;
    }

    /// <summary>The page is going: whatever was being listened to stops, and the music is given back.</summary>
    public void Leave()
    {
        MusicPlayer.Instance?.StopMusic();
        onLeave?.Invoke();
    }

    private void Move(int delta)
    {
        Sound(menu.Skin.MoveSound);
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
        KeepVisible();
    }

    private void Focus(bool tracks)
    {
        if (tracksFocused == tracks)
        {
            return;
        }
        Sound(menu.Skin.MoveSound);
        tracksFocused = tracks;
        track = Mathf.Clamp(track, 0, Mathf.Max(0, Entries.Count - 1));
        KeepVisible();
    }

    private void KeepVisible()
    {
        if (track < trackScroll)
        {
            trackScroll = track;
        }
        else if (track >= trackScroll + trackRows)
        {
            trackScroll = track - trackRows + 1;
        }
        trackScroll = Mathf.Clamp(trackScroll, 0, Mathf.Max(0, Entries.Count - trackRows));

        if (situation < situationScroll)
        {
            situationScroll = situation;
        }
        else if (situation >= situationScroll + situationRows)
        {
            situationScroll = situation - situationRows + 1;
        }
        situationScroll = Mathf.Clamp(situationScroll, 0, Mathf.Max(0, SituationCount - situationRows));
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

        Sound(menu.Skin.MakeSound);
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
        Sound(menu.Skin.MoveSound);
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

        Sound(menu.Skin.BackSound);
        MusicConfig.Remove(Situation, track);
        if (Entries.Count == 0)
        {
            Focus(false);
            SetStatus($"{situationNames[situation]} now plays no music.");
        }
        track = Mathf.Clamp(track, 0, Mathf.Max(0, Entries.Count - 1));
        KeepVisible();
    }

    private void AddTracks()
    {
        if (!NativeFileDialog.IsAvailable)
        {
            SetStatus($"Copy your files into {MusicConfig.Folder} and add a line for each to music.txt.");
            return;
        }

        Sound(menu.Skin.MakeSound);
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
            KeepVisible();
        }
    }

    /// <summary>A reset asks first, in the menu's Yes/No box with No lit: a yes throws the player's choices away.</summary>
    private void AskReset(EAction which)
    {
        string question = which == EAction.ResetAll
            ? "Go back to the original tracks in every situation? Your own tracks leave all the lists."
            : $"Go back to the original tracks of {situationNames[situation]}? Your own tracks leave its list.";
        menu.Ask(question, () => DoReset(which));
    }

    private void DoReset(EAction which)
    {
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
        Sound(menu.Skin.MakeSound);
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

        Sound(menu.Skin.MakeSound);
        string chosen = soundFonts[index];
        PlayerPrefs.SetString("Options_SoundFont", chosen);
        PlayerPrefs.Save();
        // Restarts the XMI being listened to, through the new soundfont; does nothing for the same one.
        MusicPlayer.Instance?.ReloadSynthesizerWithSoundFont(chosen, PlayerPrefs.GetFloat("Options_MusicVolume", 0.5f));
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

    /// <summary>
    /// The page, in the area the menu gives it: the situations on the left, the tracks of the one
    /// selected on the right, a status line, and the row of buttons at the foot.
    /// </summary>
    public void Draw(Rect area)
    {
        float buttonH = GameMenu.PageButtonHeight;
        float rowH = GameMenu.PageRowHeight;
        float buttonsY = area.yMax - buttonH;
        float statusY = buttonsY - 8f - StatusHeight;
        float listsBottom = statusY - 8f;
        Rect situationsPanel = new Rect(area.x, area.y, Mathf.Round(area.width * 0.34f), listsBottom - area.y);
        Rect tracksPanel = new Rect(situationsPanel.xMax + 24f, area.y, area.xMax - situationsPanel.xMax - 24f, situationsPanel.height);

        // The buttons, as wide as the menu's own on its other pages (the user's, 31 October 2026: they
        // ran into each other at 1280x800 when each named its soundfont). The soundfont one opens the
        // list of them above it, where the one in use is marked.
        string[] labels = { "Add files...", "Reset", "Reset all", "Soundfont", "Back" };
        EAction[] actions = { EAction.Add, EAction.ResetOne, EAction.ResetAll, EAction.SoundFont, EAction.None };
        float buttonW = menu.PageButtonWidth;
        foreach (string label in labels)
        {
            buttonW = Mathf.Max(buttonW, menu.MeasureText(label, 28f) + 48f);
        }
        Rect ButtonRect(int b) => b == labels.Length - 1
            ? new Rect(area.xMax - buttonW, buttonsY, buttonW, buttonH)
            : new Rect(area.x + b * (buttonW + 16f), buttonsY, buttonW, buttonH);

        // The soundfont list takes its clicks first, being drawn over everything; any other click
        // while it is open only closes it.
        Rect fontButton = ButtonRect(3);
        Rect SoundFontRow(int i) => new Rect(fontButton.x, fontButton.y - 8f - (soundFonts.Length - i) * SoundFontRowHeight,
            fontButton.width, SoundFontRowHeight);
        if (soundFontListOpen && !menu.Asking)
        {
            for (int i = 0; i < soundFonts.Length; ++i)
            {
                if (GuiInput.TryConsumeClickInRect(UiScale.R(SoundFontRow(i))))
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

        DrawSituations(situationsPanel, rowH);
        DrawTracks(tracksPanel, rowH);

        for (int b = 0; b < labels.Length; ++b)
        {
            bool on = b == 3 && soundFontListOpen;
            if (menu.DrawButton(ButtonRect(b), labels[b], true, on))
            {
                if (actions[b] == EAction.None)
                {
                    menu.LeavePage();
                    return;
                }
                pending = actions[b];
            }
        }

        if (Time.unscaledTime < statusUntil && !string.IsNullOrEmpty(status))
        {
            menu.DrawText(new Rect(area.x, statusY, area.width, StatusHeight), status, GameMenu.PageSmallFont, TextAnchor.MiddleCenter,
                GameMenu.SelectColor);
        }

        if (soundFontListOpen)
        {
            int current = CurrentSoundFontIndex;
            for (int i = 0; i < soundFonts.Length; ++i)
            {
                Rect row = SoundFontRow(i);
                menu.DrawPanel(row);
                menu.DrawPanel(row);
                if (i == soundFontCursor || menu.IsHovered(row))
                {
                    menu.DrawHighlight(row, true);
                }
                menu.DrawFrame(row, GameMenu.UnselectColor);
                string fontName = Path.GetFileNameWithoutExtension(soundFonts[i]) + (i == current ? "  (in use)" : "");
                menu.DrawText(row, fontName, 26f, TextAnchor.MiddleCenter,
                    menu.IsHovered(row) ? Color.white : i == soundFontCursor ? GameMenu.SelectColor : GameMenu.UnselectColor);
            }
        }
    }

    private void DrawSituations(Rect panel, float rowH)
    {
        menu.DrawPanel(panel);
        situationRows = Mathf.Max(1, Mathf.FloorToInt((panel.height - 16f) / rowH));
        bool scrolls = SituationCount > situationRows;
        Rect bar = new Rect(panel.xMax - 8f - ScrollBarWidth, panel.y + 8f, ScrollBarWidth, situationRows * rowH);
        float right = scrolls ? bar.x - 8f : panel.xMax - 8f;
        WheelOver(panel, ref situationScroll, SituationCount - situationRows);
        situationScroll = Mathf.Clamp(situationScroll, 0, Mathf.Max(0, SituationCount - situationRows));

        for (int r = 0; r < situationRows && situationScroll + r < SituationCount; ++r)
        {
            int i = situationScroll + r;
            Rect row = new Rect(panel.x + 8f, panel.y + 8f + r * rowH, right - panel.x - 8f, rowH);
            bool selected = i == situation;
            if (selected)
            {
                menu.DrawHighlight(row, !tracksFocused);
            }

            IReadOnlyList<MusicEntry> entries = MusicConfig.Entries((EMusicSituation)i);
            int on = 0;
            foreach (MusicEntry entry in entries)
            {
                on += entry.Enabled ? 1 : 0;
            }
            string count = entries.Count == 0 ? "none" : on == entries.Count ? on.ToString() : $"{on} of {entries.Count}";
            Color color = selected && !tracksFocused ? Color.white : GameMenu.SelectColor;
            menu.DrawText(new Rect(row.x + 16f, row.y, row.width - 32f, row.height), situationNames[i], GameMenu.PageRowFont,
                TextAnchor.MiddleLeft, color);
            menu.DrawText(new Rect(row.x, row.y, row.width - 16f, row.height), count, GameMenu.PageRowFont, TextAnchor.MiddleRight, color);
            if (!menu.Asking && GuiInput.TryConsumeClickInRect(UiScale.R(row)))
            {
                if (i != situation)
                {
                    situation = i;
                    track = 0;
                    trackScroll = 0;
                    Sound(menu.Skin.MoveSound);
                }
                tracksFocused = false;
            }
        }

        if (scrolls)
        {
            situationScroll = menu.DrawScrollBar(bar, situationScroll, situationRows, SituationCount, ref draggingSituations, ref grab);
        }
    }

    private void DrawTracks(Rect panel, float rowH)
    {
        menu.DrawPanel(panel);
        menu.DrawText(new Rect(panel.x + 20f, panel.y + 8f, panel.width - 40f, NoteHeight), situationNotes[situation], GameMenu.PageSmallFont,
            TextAnchor.UpperLeft, GameMenu.UnselectColor, true);

        IReadOnlyList<MusicEntry> tracks = Entries;
        float top = panel.y + 8f + NoteHeight;
        trackRows = Mathf.Max(1, Mathf.FloorToInt((panel.yMax - 8f - top) / rowH));
        bool scrolls = tracks.Count > trackRows;
        Rect bar = new Rect(panel.xMax - 8f - ScrollBarWidth, top, ScrollBarWidth, trackRows * rowH);
        float right = scrolls ? bar.x - 8f : panel.xMax - 8f;
        WheelOver(panel, ref trackScroll, tracks.Count - trackRows);
        trackScroll = Mathf.Clamp(trackScroll, 0, Mathf.Max(0, tracks.Count - trackRows));

        if (tracks.Count == 0)
        {
            menu.DrawText(new Rect(panel.x + 20f, top, panel.width - 40f, rowH), "No music. Add files to give this situation some.",
                GameMenu.PageRowFont, TextAnchor.MiddleLeft, GameMenu.UnselectColor);
        }
        for (int r = 0; r < trackRows; ++r)
        {
            int i = trackScroll + r;
            if (i >= tracks.Count)
            {
                break;
            }

            MusicEntry entry = tracks[i];
            Rect rect = new Rect(panel.x + 8f, top + r * rowH, right - panel.x - 8f, rowH);
            bool selected = tracksFocused && i == track;
            if (selected)
            {
                menu.DrawHighlight(rect, true);
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
            Color color = !present ? missingColor : !entry.Enabled ? offColor : selected ? Color.white : GameMenu.SelectColor;

            Rect toggle = new Rect(rect.x + 12f, rect.center.y - BoxSize / 2, BoxSize, BoxSize);
            Rect remove = new Rect(rect.xMax - IconButtonWidth - 6f, rect.y + 5f, IconButtonWidth, rect.height - 10f);
            Rect play = new Rect(remove.x - IconButtonWidth - 8f, remove.y, IconButtonWidth, remove.height);
            Rect duration = new Rect(play.x - DurationWidth - 12f, rect.y, DurationWidth, rect.height);
            Rect nameRect = new Rect(toggle.xMax + 16f, rect.y, duration.x - toggle.xMax - 28f, rect.height);

            // A long name is cut short with an ellipsis rather than run under the length.
            menu.DrawText(nameRect, menu.FitText(name, GameMenu.PageRowFont, nameRect.width), GameMenu.PageRowFont, TextAnchor.MiddleLeft, color);
            if (present)
            {
                menu.DrawText(duration, TrackLengths.Text(path, host), GameMenu.PageRowFont, TextAnchor.MiddleRight, color);
            }

            if (menu.DrawCheckBox(toggle, entry.Enabled, selected))
            {
                SelectTrack(i);
                pending = EAction.Toggle;
            }
            else if (present && menu.DrawIconButton(play, IsPreviewing(path) ? SetupIcons.Stop : SetupIcons.Play))
            {
                SelectTrack(i);
                pending = EAction.Play;
            }
            else if (menu.DrawIconButton(remove, SetupIcons.Remove))
            {
                SelectTrack(i);
                pending = EAction.Remove;
            }
            else if (!menu.Asking && GuiInput.TryConsumeClickInRect(UiScale.R(rect)))
            {
                SelectTrack(i);
            }
        }

        if (scrolls)
        {
            trackScroll = menu.DrawScrollBar(bar, trackScroll, trackRows, tracks.Count, ref draggingTracks, ref grab);
        }
    }

    /// <summary>The wheel over a list scrolls it, a row a notch.</summary>
    private void WheelOver(Rect panel, ref int scroll, int maxScroll)
    {
        Event e = Event.current;
        if (e.type == EventType.ScrollWheel && UiScale.R(panel).Contains(e.mousePosition))
        {
            scroll = Mathf.Clamp(scroll + (e.delta.y > 0 ? 1 : -1), 0, Mathf.Max(0, maxScroll));
            e.Use();
        }
    }

    private void SelectTrack(int index)
    {
        tracksFocused = true;
        track = index;
        KeepVisible();
    }

    public Hint[] Hints(bool pad)
    {
        GameMenuSkin skin = menu.Skin;
        return pad
            ? new[]
            {
                new Hint("on/off", HintKey.Of(skin.XButton)),
                new Hint("play/stop", HintKey.Of(skin.AButton)),
                new Hint("remove", HintKey.PadButton("LB")),
                new Hint("add files", HintKey.Of(skin.YButton)),
                new Hint("soundfont", HintKey.PadButton("RB")),
                new Hint("reset", HintKey.PadButton("Select")),
                new Hint("hold: reset all", HintKey.PadButton("Select")),
                new Hint("back", HintKey.Of(skin.BButton)),
            }
            : new[]
            {
                new Hint("on/off", HintKey.Keyboard("Space")),
                new Hint("play/stop", HintKey.Keyboard("Enter")),
                new Hint("remove", HintKey.Keyboard("Delete")),
                new Hint("add files", HintKey.Keyboard("Insert")),
                new Hint("soundfont", HintKey.Keyboard("S")),
                new Hint("reset", HintKey.Keyboard("R")),
                new Hint("reset all", HintKey.Keyboard("Shift"), HintKey.Keyboard("R")),
                new Hint("back", HintKey.Of(skin.EscapeKey)),
            };
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
