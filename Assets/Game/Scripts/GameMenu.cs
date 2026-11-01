using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>What the game menu needs from whoever shows it: the pictures, the font and the sounds.</summary>
public sealed class GameMenuSkin
{
    /// <summary>A style whose font every text of the menu is written in; sizes and colours are the menu's.</summary>
    public GUIStyle Font;
    /// <summary>The picture behind the menu, drawn to cover the panel; without it, a wall of the game's tiled.</summary>
    public Texture2D Background;
    /// <summary>The wood panel, the last fallback when neither the picture nor the original's walls are there.</summary>
    public Texture2D Wood;
    /// <summary>The frame round the panel, a broad dark line and a thin lighter one inside it, in the background's tones.</summary>
    public Color FrameDark = new Color(0.10f, 0.05f, 0.02f, 1f);
    public Color FrameLight = new Color(0.55f, 0.36f, 0.18f, 1f);
    public Texture2D AButton;
    public Texture2D BButton;
    public Texture2D XButton;
    public Texture2D YButton;
    public Texture2D EscapeKey;
    public Texture2D BlankKey;
    public Texture2D DPadLeftRight;
    public AudioClip MoveSound;
    public AudioClip MakeSound;
    public AudioClip BackSound;
}

/// <summary>What the game menu asks of the screen that shows it, for the pages that are its own: SAVE and LOAD.</summary>
public interface IGameMenuHost
{
    /// <summary>A tab of the host's has come into view.</summary>
    void OnTabShown(GameMenu.ETab tab);

    /// <summary>The keys of the frame, for a tab of the host's.</summary>
    void UpdatePage(GameMenu.ETab tab, GameMenu.Nav nav);

    /// <summary>Draws a tab of the host's: its list and its detail, in reference pixels.</summary>
    void DrawPage(GameMenu.ETab tab, Rect list, Rect detail);

    /// <summary>The hints of a tab of the host's, for the pad or for the keyboard.</summary>
    Hint[] PageHints(GameMenu.ETab tab, bool pad);

    /// <summary>The buttons of a tab of the host's, in the row under the page: SAVE has Save.</summary>
    string[] PageButtons(GameMenu.ETab tab);

    bool PageButtonEnabled(GameMenu.ETab tab, int button);

    void PressPageButton(GameMenu.ETab tab, int button);

    /// <summary>The button at the right end of the row on every tab, or null: in a game, Main Menu.</summary>
    string EndButton { get; }

    void PressEndButton();

    /// <summary>
    /// Draws, inside the end button at its left, what the pad does instead of a click: in a game, X
    /// with the ring that fills while it is held. In reference pixels.
    /// </summary>
    void DrawEndButtonPadIcon(Rect icon);

    /// <summary>The page a Page row opens (GameOptions.MusicPageKey: the music tracks), or null when this host has none.</summary>
    IGameMenuSubPage OpenPage(string key);
}

/// <summary>
/// A page opened from a row of OPTIONS - the music tracks - drawn inside the menu's frame in place of
/// the list and the detail, with its own buttons and hints. It reads its own keys; B or Escape go
/// back to the list, through <see cref="Cancel"/> first.
/// </summary>
public interface IGameMenuSubPage
{
    /// <summary>Shown where the sections are.</summary>
    string Title { get; }

    void Update();

    /// <summary>B or Escape: true when the page used it to close something of its own.</summary>
    bool Cancel();

    /// <summary>The page goes, back to the list or with the whole menu.</summary>
    void Leave();

    /// <summary>Draws the page in this area, in reference pixels.</summary>
    void Draw(Rect area);

    Hint[] Hints(bool pad);
}

/// <summary>
/// The game menu: Save, Load, Options and Controls on one full-screen panel, each tab with its list
/// on the left and the detail of the selected row on the right, and the hints of the device used
/// last along the bottom.
/// </summary>
/// <remarks>
/// A plain class, so that the screen that opens it - SaveLoadGUI in a game - owns the opening, the
/// pause and the closing, and keeps the pages that are its own (the lists of saves) while the
/// menu draws the frame, the tabs and the OPTIONS and CONTROLS pages from GameOptions.
/// Laid out in reference pixels and drawn at UiScale, so it fits 1366x768 at scale 1: the list
/// holds about eight rows of 52 there and more on a taller screen.
/// Values apply as they change and are saved when the menu closes; Reset, Reset all and Undo
/// changes each ask first, with No lit. Tabs change with LB and RB or Tab and Shift+Tab, sections
/// with LT and RT or Page Up and Page Down. The menu reads the devices directly, as the rest of
/// the menus do: mod 1000's actions cover the game, not its menus.
/// </remarks>
public sealed class GameMenu
{
    public enum ETab
    {
        Save,
        Load,
        Options,
        Controls,
    }

    /// <summary>The keys the frame has read this frame, handed to a page of the host's.</summary>
    public struct Nav
    {
        public bool Up;
        public bool Down;
        public bool Left;
        public bool Right;
        /// <summary>A on the pad (released, as the old panel read it), Enter on the keyboard.</summary>
        public bool Accept;
        /// <summary>-1 a notch up, 1 a notch down, 0 none.</summary>
        public int Wheel;
    }

    private enum EConfirm
    {
        None,
        Reset,
        ResetAll,
        Undo,
        Original,
        /// <summary>A question of the host's, asked through <see cref="Ask"/>.</summary>
        Host,
    }

    private enum EButton
    {
        Reset,
        ResetAll,
        Undo,
        Original,
        End,
    }

    /// <summary>
    /// The word for the 1992 game's rules, in the GAMEPLAY rows' detail and on their button. One
    /// place, so that changing it changes every use.
    /// </summary>
    public const string OriginalWord = "Classic";

    public static readonly Color SelectColor = new Color32(255, 213, 64, 255);
    public static readonly Color UnselectColor = new Color32(187, 123, 1, 255);
    // a little lighter than the music page's off tracks, the user's of 31 October 2026, so a disabled button still reads
    private static readonly Color DimColor = new Color32(150, 142, 132, 255);
    /// <summary>A question about what cannot be undone, as deleting a save, is written in red (the user's, 8 November 2026).</summary>
    public static readonly Color WarningColor = new Color32(235, 72, 56, 255);
    private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.55f);

    // Laid out in reference pixels (UiScale): what fits 1366x768.
    private const float Margin = 16f;
    private const float Padding = 28f;
    private const float FrameLine = 6f;
    private const float TabsHeight = 60f;
    private const float TabFont = 40f;
    private const float SectionsHeight = 46f;
    private const float SectionFont = 30f;
    private const float RowHeight = 52f;
    private const float RowFont = 32f;
    private const float DetailTitleFont = 32f;
    private const float DetailFont = 28f;
    private const float SmallFont = 26f;
    private const float HintsHeight = 44f;
    private const float HintLabelFont = 24f;
    private const float HintKeyFont = 18f;
    private const float ButtonHeight = 52f;
    private const float ButtonFont = 28f;
    private const float ValueWidth = 300f;
    private const float SliderWidth = 180f;
    private const float BoxSize = 32f;
    // A wall of the game's own, from W64.TR of the original's data, tiled at BackgroundTexel
    // reference pixels a texel: a picture made to repeat, where the wood panel of the other panels
    // could only be stretched. 49 is "an irregular stone wall" in tan (STRINGS.PAK block 10), the
    // user's choice to try after 108, "a finished oak wall", and 12, the same wall in grey; it also
    // comes in blue grey (145) and red (163).
    private const int BackgroundWallTexture = 49;
    // Six reference pixels a texel, twice the three of the other panels: the user's zoom of 200%.
    private const float BackgroundTexel = 6f;
    private const float BackgroundDarkening = 0.5f;
    private const float ConfirmWidth = 820f;
    private const float ConfirmHeight = 250f;

    // Select held this long is Reset all; let go sooner, Reset. The same time as the X held to quit.
    private const float SelectHoldSeconds = 0.85f;

    private readonly GameMenuSkin skin;
    private readonly IGameMenuHost host;
    private readonly ETab[] tabs;
    private readonly bool dimBehind;
    private readonly ControlsMap controlsMap;

    private int tabIndex;
    private readonly Dictionary<ETab, int> sectionOf = new Dictionary<ETab, int>();
    private int row;
    private int rowScroll;
    private bool onButtons;
    private int button;

    private EConfirm confirm;
    private bool confirmYes;
    private string hostQuestion;
    private Action hostYes;
    private bool hostWarning;

    private float selectHeld;
    private bool selectFired;

    private GameOptions.Snapshot snapshot;
    private IGameMenuSubPage subPage;
    private int openedFrame = -1;

    private Vector2 lastMouse;
    private GameOption dragging;

    private readonly Dictionary<long, GUIStyle> styles = new Dictionary<long, GUIStyle>();
    private float stylesFactor;

#if UNITY_EDITOR
    private readonly HashSet<string> labelsWarned = new HashSet<string>();
#endif

    /// <param name="tabs">The tabs shown, in order: all four in a game, OPTIONS and CONTROLS on Setup, LOAD alone on Journey Onward.</param>
    /// <param name="dimBehind">Darken what is behind the panel: the paused game.</param>
    public GameMenu(GameMenuSkin skin, IGameMenuHost host, ETab[] tabs, bool dimBehind)
    {
        this.skin = skin;
        this.host = host;
        this.tabs = tabs;
        this.dimBehind = dimBehind;
        controlsMap = new ControlsMap(this);
    }

    // A is read on its release, and a menu opened with A pressed - Setup and Journey Onward on the
    // title screen, which act on the press - would take the release of that same press for a choice:
    // the first row of OPTIONS toggled, or the newest save loaded. Until A has been up once since the
    // menu opened, its release is not a choice.
    private bool padAcceptArmed = true;

    // Start held on after it opened the menu takes it to the pad's map, the way to the controls for a player with
    // only a pad (a Steam Deck), as F1 is on the keyboard; the user's choice of 1 November 2026. How long it has been
    // held since the menu opened, or -1 when it was not down then or has been let go.
    private const float StartHoldSeconds = 0.5f;
    private float startHeld = -1f;

    public ETab Tab => tabs[tabIndex];

    private bool IsHostTab => Tab == ETab.Save || Tab == ETab.Load;

    /// <summary>The menu comes up: every value is recorded for Undo changes, and the soundfonts are looked for.</summary>
    public void Open(ETab tab)
    {
        GameOptions.ScanSoundFonts();
        snapshot = new GameOptions.Snapshot();
        openedFrame = Time.frameCount;
        confirm = EConfirm.None;
        dragging = null;
        selectHeld = 0f;
        selectFired = false;
        lastMouse = GuiInput.MousePositionGuiSpace;
        padAcceptArmed = !(Gamepad.current?.aButton.isPressed ?? false);
        startHeld = (Gamepad.current?.startButton.isPressed ?? false) ? 0f : -1f;
        int i = Array.IndexOf(tabs, tab);
        ShowTab(i >= 0 ? i : 0);
    }

    private void UpdateStartHeld()
    {
        if (startHeld < 0f)
        {
            return;
        }
        if (!(Gamepad.current?.startButton.isPressed ?? false))
        {
            startHeld = -1f;
            return;
        }
        startHeld += Time.unscaledDeltaTime;
        if (startHeld >= StartHoldSeconds)
        {
            startHeld = -1f;
            if (confirm == EConfirm.None)
            {
                ShowControlsMap(EOptionSection.Gamepad);
            }
        }
    }

    /// <summary>Goes to a map of CONTROLS, when the menu has that tab.</summary>
    private void ShowControlsMap(EOptionSection map)
    {
        int i = Array.IndexOf(tabs, ETab.Controls);
        if (i < 0)
        {
            return;
        }
        ShowTab(i);
        ShowSection(map);
        Sound(skin.MoveSound);
    }

    /// <summary>The menu comes up on a tab and one of its sections: F1 opens it on a control map.</summary>
    public void Open(ETab tab, EOptionSection section)
    {
        Open(tab);
        ShowSection(section);
    }

    private void ShowSection(EOptionSection section)
    {
        int s = Sections(Tab).IndexOf(section);
        if (s >= 0)
        {
            sectionOf[Tab] = s;
            row = 0;
            rowScroll = 0;
            onButtons = false;
            button = 0;
        }
    }

    /// <summary>
    /// The menu goes: the options are saved. True when anything differs from when it opened, which
    /// the host says with a line, the sign that the changes were kept.
    /// </summary>
    public bool Close()
    {
        CloseSubPage();
        confirm = EConfirm.None;
        dragging = null;
        GameOptions.Save();
        return snapshot != null && snapshot.Differs();
    }

    /// <summary>
    /// Asks the host's own question in the menu's Yes/No box, No lit first, and calls
    /// <paramref name="onYes"/> on a yes: in a game, going back to the title screen.
    /// </summary>
    /// <param name="warning">Written in red: what a yes does cannot be undone.</param>
    public void Ask(string question, Action onYes, bool warning = false)
    {
        hostQuestion = question;
        hostYes = onYes;
        hostWarning = warning;
        confirmYes = false;
        confirm = EConfirm.Host;
        Sound(skin.MakeSound);
    }

    /// <summary>True while the Yes/No box is up.</summary>
    public bool Asking => confirm != EConfirm.None;

    /// <summary>True on the frame the menu opened, when the key that opened it must not also close it.</summary>
    public bool JustOpened => Time.frameCount == openedFrame;

    /// <summary>True while a page of the host's is open in place of the tab: the music tracks.</summary>
    public bool ShowsSubPage => subPage != null;

    /// <summary>
    /// A box over the menu has taken a press: A's release is not a choice until A has been up once, as when the
    /// menu opens with A down.
    /// </summary>
    public void DisarmAccept()
    {
        padAcceptArmed = false;
    }

    private void OpenSubPage(string key)
    {
        subPage = host.OpenPage(key);
        if (subPage != null)
        {
            Sound(skin.MakeSound);
        }
    }

    private void CloseSubPage()
    {
        if (subPage != null)
        {
            IGameMenuSubPage leaving = subPage;
            subPage = null;
            leaving.Leave();
        }
    }

    private void ShowTab(int index)
    {
        CloseSubPage();
        tabIndex = (index + tabs.Length) % tabs.Length;
        row = 0;
        rowScroll = 0;
        onButtons = false;
        button = 0;
        if (IsHostTab)
        {
            host.OnTabShown(Tab);
        }
    }

    // ---------------------------------------------------------------- sections and rows

    private List<EOptionSection> Sections(ETab tab)
    {
        List<EOptionSection> list = new List<EOptionSection>();
        EOptionSection[] candidates = tab == ETab.Options
            ? new[] { EOptionSection.Gameplay, EOptionSection.Display, EOptionSection.Audio, EOptionSection.General }
            : tab == ETab.Controls ? new[] { EOptionSection.Input, EOptionSection.Keyboard, EOptionSection.Gamepad }
            : Array.Empty<EOptionSection>();
        foreach (EOptionSection s in candidates)
        {
            // a section with no rows is not shown, but for the maps, which have none
            if (GameOptions.InSection(s).Count > 0 || ControlsMap.Shows(s))
            {
                list.Add(s);
            }
        }
        return list;
    }

    private EOptionSection Section
    {
        get
        {
            List<EOptionSection> list = Sections(Tab);
            sectionOf.TryGetValue(Tab, out int i);
            return list[Mathf.Clamp(i, 0, list.Count - 1)];
        }
    }

    private void StepSection(int direction)
    {
        List<EOptionSection> list = Sections(Tab);
        if (list.Count < 2)
        {
            return;
        }
        sectionOf.TryGetValue(Tab, out int i);
        sectionOf[Tab] = (i + direction + list.Count) % list.Count;
        row = 0;
        rowScroll = 0;
        onButtons = false;
        button = 0;
        Sound(skin.MoveSound);
    }

    private List<GameOption> Rows => GameOptions.InSection(Section);

    private static string SectionName(EOptionSection s)
    {
        switch (s)
        {
            case EOptionSection.General: return "GENERAL";
            case EOptionSection.Display: return "DISPLAY";
            case EOptionSection.Audio: return "AUDIO";
            case EOptionSection.Gameplay: return "GAMEPLAY";
            case EOptionSection.Keyboard: return "KEYBOARD & MOUSE";
            case EOptionSection.Gamepad: return "GAMEPAD";
            default: return "INPUT";
        }
    }

    private static string TabName(ETab t)
    {
        switch (t)
        {
            case ETab.Save: return "SAVE";
            case ETab.Load: return "LOAD";
            case ETab.Options: return "OPTIONS";
            default: return "CONTROLS";
        }
    }

    private EOptionSection[] TabSections() => Sections(Tab).ToArray();

    private List<EButton> Buttons()
    {
        List<EButton> list = new List<EButton> { EButton.Reset, EButton.ResetAll, EButton.Undo };
        if (Section == EOptionSection.Gameplay)
        {
            list.Add(EButton.Original);
        }
        if (host.EndButton != null)
        {
            list.Add(EButton.End);
        }
        return list;
    }

    private bool Enabled(EButton b)
    {
        switch (b)
        {
            case EButton.Reset: return GameOptions.AnyChangedFromDefault(Section);
            case EButton.ResetAll: return GameOptions.AnyChangedFromDefault(TabSections());
            case EButton.Undo: return snapshot != null && snapshot.Differs();
            case EButton.End: return true;
            default: return GameOptions.DiffersFromOriginal(EOptionSection.Gameplay);
        }
    }

    private string ButtonLabel(EButton b)
    {
        switch (b)
        {
            case EButton.Reset: return "Reset";
            case EButton.ResetAll: return "Reset all";
            case EButton.Undo: return "Undo changes";
            case EButton.End: return host.EndButton;
            default: return OriginalWord + " rules";
        }
    }

    private void Press(EButton b)
    {
        if (!Enabled(b))
        {
            return;
        }
        if (b == EButton.End)
        {
            host.PressEndButton();
            return;
        }
        Sound(skin.MakeSound);
        confirmYes = false;
        confirm = b == EButton.Reset ? EConfirm.Reset
            : b == EButton.ResetAll ? EConfirm.ResetAll
            : b == EButton.Undo ? EConfirm.Undo
            : EConfirm.Original;
    }

    private string ConfirmQuestion()
    {
        switch (confirm)
        {
            case EConfirm.Reset: return $"Put every setting of {SectionName(Section)} back to its default?";
            case EConfirm.ResetAll: return $"Put every setting of {TabName(Tab)} back to its default?";
            case EConfirm.Undo: return "Put every setting back as it was when you opened the menu?";
            case EConfirm.Host: return hostQuestion;
            default: return $"Set every GAMEPLAY setting to its {OriginalWord.ToLowerInvariant()} rule?";
        }
    }

    private void Answer(bool yes)
    {
        EConfirm what = confirm;
        confirm = EConfirm.None;
        if (!yes)
        {
            Sound(skin.BackSound);
            return;
        }
        Sound(skin.MakeSound);
        switch (what)
        {
            case EConfirm.Reset:
                GameOptions.ResetSections(Section);
                break;
            case EConfirm.ResetAll:
                GameOptions.ResetSections(TabSections());
                break;
            case EConfirm.Undo:
                snapshot.Restore();
                break;
            case EConfirm.Original:
                GameOptions.SetOriginalRules();
                break;
            case EConfirm.Host:
                hostYes?.Invoke();
                break;
        }
    }

    private static void Sound(AudioClip clip)
    {
        if (clip != null)
        {
            Utils.PlayClip2d(clip);
        }
    }

    // ---------------------------------------------------------------- input

    /// <summary>The keys, every frame the menu is open. True when the player asked to close it.</summary>
    public bool Update()
    {
        GameInput.RefreshLastActiveDevice();
        GameOptions.Tick();
        // the Interface scale, clicked or dragged, grows the interface once the button is let go
        GameOptions.HoldGrowth(dragging != null && dragging.Key == UiScale.GrowthPrefsKey);
        UpdateStartHeld();

        Gamepad pad = Gamepad.current;
        Keyboard keys = Keyboard.current;
        bool shift = keys != null && (keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed);
        bool ctrl = keys != null && (keys.leftCtrlKey.isPressed || keys.rightCtrlKey.isPressed);

        Nav nav = new Nav
        {
            Up = (pad?.dpad.up.wasPressedThisFrame ?? false) || (pad?.leftStick.up.wasPressedThisFrame ?? false)
                || (keys?.upArrowKey.wasPressedThisFrame ?? false),
            Down = (pad?.dpad.down.wasPressedThisFrame ?? false) || (pad?.leftStick.down.wasPressedThisFrame ?? false)
                || (keys?.downArrowKey.wasPressedThisFrame ?? false),
            Left = (pad?.dpad.left.wasPressedThisFrame ?? false) || (pad?.leftStick.left.wasPressedThisFrame ?? false)
                || (keys?.leftArrowKey.wasPressedThisFrame ?? false),
            Right = (pad?.dpad.right.wasPressedThisFrame ?? false) || (pad?.leftStick.right.wasPressedThisFrame ?? false)
                || (keys?.rightArrowKey.wasPressedThisFrame ?? false),
            Accept = (padAcceptArmed && (pad?.aButton.wasReleasedThisFrame ?? false))
                || (keys?.enterKey.wasPressedThisFrame ?? false) || (keys?.numpadEnterKey.wasPressedThisFrame ?? false),
        };
        float wheel = Mouse.current?.scroll.ReadValue().y ?? 0f;
        nav.Wheel = wheel > 0f ? -1 : wheel < 0f ? 1 : 0;
        padAcceptArmed |= !(pad?.aButton.isPressed ?? false);

        // a direction held repeats, as the pack's cursor does (fix 477: Field of view was 45 presses); not in a question,
        // where it would flip Yes and No
        bool upRepeat = Repeats(0, (pad?.dpad.up.isPressed ?? false) || (pad?.leftStick.up.isPressed ?? false)
            || (keys?.upArrowKey.isPressed ?? false));
        bool downRepeat = Repeats(1, (pad?.dpad.down.isPressed ?? false) || (pad?.leftStick.down.isPressed ?? false)
            || (keys?.downArrowKey.isPressed ?? false));
        bool leftRepeat = Repeats(2, (pad?.dpad.left.isPressed ?? false) || (pad?.leftStick.left.isPressed ?? false)
            || (keys?.leftArrowKey.isPressed ?? false));
        bool rightRepeat = Repeats(3, (pad?.dpad.right.isPressed ?? false) || (pad?.leftStick.right.isPressed ?? false)
            || (keys?.rightArrowKey.isPressed ?? false));
        if (confirm == EConfirm.None)
        {
            nav.Up |= upRepeat;
            nav.Down |= downRepeat;
            // left and right only on a slider: a switch would flip twenty times a second, a list run round and round
            if (SidewaysRepeats())
            {
                nav.Left |= leftRepeat;
                nav.Right |= rightRepeat;
            }
        }

        bool back = (pad?.bButton.wasPressedThisFrame ?? false) || GameInput.EscapePressedThisFrame();

        if (confirm != EConfirm.None)
        {
            if (nav.Left || nav.Right)
            {
                confirmYes = !confirmYes;
                Sound(skin.MoveSound);
            }
            else if (back)
            {
                Answer(false);
            }
            else if (nav.Accept)
            {
                Answer(confirmYes);
            }
            return false;
        }

        if (subPage != null)
        {
            if (back)
            {
                if (!subPage.Cancel())
                {
                    CloseSubPage();
                    Sound(skin.BackSound);
                }
                return false;
            }
            if (!JustOpened && (pad?.startButton.wasPressedThisFrame ?? false))
            {
                return true;
            }
            subPage.Update();
            return false;
        }

        if (!JustOpened && (back || (pad?.startButton.wasPressedThisFrame ?? false)))
        {
            return true;
        }

        // F1: on a control map it closes the menu, as it opened it; anywhere else it goes to the map of the
        // device used last
        if (!JustOpened && GameActions.KeysWorld.Controls.WasPressedThisFrame() && Array.IndexOf(tabs, ETab.Controls) >= 0)
        {
            if (Tab == ETab.Controls && ControlsMap.Shows(Section))
            {
                return true;
            }
            ShowControlsMap(ControlsMap.ForLastDevice());
            return false;
        }

        // with one tab, Journey Onward's, the tab keys do nothing: no sound, and the list not made again (fix 465)
        bool tabKey = tabs.Length > 1 && (keys?.tabKey.wasPressedThisFrame ?? false);
        bool tabPad = tabs.Length > 1 && pad != null;
        if ((tabPad && pad.leftShoulder.wasPressedThisFrame) || (tabKey && shift))
        {
            ShowTab(tabIndex - 1);
            Sound(skin.MoveSound);
            return false;
        }
        if ((tabPad && pad.rightShoulder.wasPressedThisFrame) || (tabKey && !shift))
        {
            ShowTab(tabIndex + 1);
            Sound(skin.MoveSound);
            return false;
        }

        if (IsHostTab)
        {
            host.UpdatePage(Tab, nav);
            return false;
        }

        if ((pad?.leftTrigger.wasPressedThisFrame ?? false) || (keys?.pageUpKey.wasPressedThisFrame ?? false))
        {
            StepSection(-1);
            return false;
        }
        if ((pad?.rightTrigger.wasPressedThisFrame ?? false) || (keys?.pageDownKey.wasPressedThisFrame ?? false))
        {
            StepSection(1);
            return false;
        }

        UpdateResetKeys(pad, keys, shift, ctrl);
        if (confirm != EConfirm.None)
        {
            return false;
        }

        List<GameOption> rows = Rows;
        // a button the keys rest on that a reset has just disabled hands them to the next one
        if (onButtons && !Enabled(Buttons()[Mathf.Clamp(button, 0, Buttons().Count - 1)]))
        {
            EnterButtons(1, button);
        }
        if (ControlsMap.Shows(Section))
        {
            UpdateMap(nav);
            return false;
        }
        if (nav.Down)
        {
            if (!onButtons && row < rows.Count - 1)
            {
                row++;
            }
            else if (onButtons || !EnterButtons(1, 0))
            {
                // from the buttons, or past the last row when every button is disabled, round to the first row
                onButtons = false;
                row = 0;
            }
            Sound(skin.MoveSound);
        }
        else if (nav.Up)
        {
            if (onButtons)
            {
                onButtons = false;
                row = rows.Count - 1;
            }
            else if (row > 0)
            {
                row--;
            }
            else if (!EnterButtons(1, 0))
            {
                row = rows.Count - 1;
            }
            Sound(skin.MoveSound);
        }
        else if (nav.Left || nav.Right)
        {
            int direction = nav.Left ? -1 : 1;
            if (onButtons)
            {
                EnterButtons(direction, button + direction);
                Sound(skin.MoveSound);
            }
            else if (row < rows.Count)
            {
                GameOptions.Step(rows[row], direction);
                Sound(skin.MoveSound);
            }
        }
        else if (nav.Accept)
        {
            if (onButtons)
            {
                List<EButton> buttons = Buttons();
                Press(buttons[Mathf.Clamp(button, 0, buttons.Count - 1)]);
            }
            else if (row < rows.Count && rows[row].Kind == EOptionKind.Page)
            {
                OpenSubPage(rows[row].Key);
            }
            else if (row < rows.Count && rows[row].Kind != EOptionKind.Slider)
            {
                GameOptions.Step(rows[row], 1);
                Sound(skin.MoveSound);
            }
        }
        else if (nav.Wheel != 0)
        {
            rowScroll += nav.Wheel;
        }
        return false;
    }

    /// <summary>
    /// The keys on a map of CONTROLS: the map walks its own list, and past either end of it they go to
    /// the row of buttons, as past the rows of a section; from the buttons up or down goes back to it.
    /// </summary>
    private void UpdateMap(Nav nav)
    {
        if (onButtons)
        {
            if (nav.Up || nav.Down)
            {
                onButtons = false;
                controlsMap.EnterFromButtons(Section, nav.Up);
                Sound(skin.MoveSound);
            }
            else if (nav.Left || nav.Right)
            {
                int direction = nav.Left ? -1 : 1;
                EnterButtons(direction, button + direction);
                Sound(skin.MoveSound);
            }
            else if (nav.Accept)
            {
                List<EButton> buttons = Buttons();
                Press(buttons[Mathf.Clamp(button, 0, buttons.Count - 1)]);
            }
            return;
        }
        if (controlsMap.Update(Section, nav) && EnterButtons(1, 0))
        {
            Sound(skin.MoveSound);
        }
    }

    /// <summary>
    /// Puts the keys on the first button that is not disabled, looking from <paramref name="from"/>
    /// in <paramref name="direction"/> and round the row. False, and the keys stay on the rows, when
    /// every button is disabled - on the title screen, with nothing changed, there is no Main Menu to
    /// fall on.
    /// </summary>
    private bool EnterButtons(int direction, int from)
    {
        List<EButton> buttons = Buttons();
        for (int k = 0; k < buttons.Count; k++)
        {
            int i = ((from + direction * k) % buttons.Count + buttons.Count) % buttons.Count;
            if (Enabled(buttons[i]))
            {
                onButtons = true;
                button = i;
                return true;
            }
        }
        onButtons = false;
        return false;
    }

    /// <summary>Select is Reset and Select held Reset all; on the keyboard R, Shift+R and Ctrl+Z.</summary>
    private void UpdateResetKeys(Gamepad pad, Keyboard keys, bool shift, bool ctrl)
    {
        if (pad != null && pad.selectButton.isPressed)
        {
            selectHeld += Time.unscaledDeltaTime;
            if (!selectFired && selectHeld >= SelectHoldSeconds)
            {
                selectFired = true;
                Press(EButton.ResetAll);
            }
        }
        else
        {
            if (selectHeld > 0f && !selectFired)
            {
                Press(EButton.Reset);
            }
            selectHeld = 0f;
            selectFired = false;
        }

        if (keys == null)
        {
            return;
        }
        if (keys.rKey.wasPressedThisFrame && !ctrl)
        {
            Press(shift ? EButton.ResetAll : EButton.Reset);
        }
        else if (keys.zKey.wasPressedThisFrame && ctrl)
        {
            Press(EButton.Undo);
        }
    }

    // ---------------------------------------------------------------- styles

    private GUIStyle Style(float refSize, TextAnchor anchor, bool wrap = false)
    {
        float f = UiScale.Factor;
        if (f != stylesFactor)
        {
            styles.Clear();
            stylesFactor = f;
        }
        long key = ((long)Mathf.RoundToInt(refSize * 10f) << 8) | ((long)anchor << 1) | (wrap ? 1L : 0L);
        if (!styles.TryGetValue(key, out GUIStyle s))
        {
            s = new GUIStyle(skin.Font)
            {
                fontSize = UiScale.Font(refSize),
                alignment = anchor,
                wordWrap = wrap,
                // the detail writes Enabled:, Disabled:, Original: and Default: in bold
                richText = true,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                contentOffset = Vector2.zero,
            };
            s.normal.background = null;
            styles[key] = s;
        }
        return s;
    }

    private void Text(Rect refRect, string content, float refSize, TextAnchor anchor, Color color, bool wrap = false)
    {
        GUIStyle s = Style(refSize, anchor, wrap);
        s.normal.textColor = color;
        GUI.Label(UiScale.R(refRect), content, s);
    }

    /// <summary>Width of a text in reference pixels, measured at its reference size.</summary>
    private float Measure(string content, float refSize)
    {
        GUIStyle s = Style(refSize, TextAnchor.MiddleLeft);
        return s.CalcSize(new GUIContent(content)).x / UiScale.Factor;
    }

    private float MeasureHeight(string content, float refSize, float refWidth)
    {
        GUIStyle s = Style(refSize, TextAnchor.UpperLeft, true);
        return s.CalcHeight(new GUIContent(content), refWidth * UiScale.Factor) / UiScale.Factor;
    }

    private static void Fill(Rect refRect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(UiScale.R(refRect), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static void Frame(Rect refRect, Color color)
    {
        Rect r = UiScale.R(refRect);
        float line = Mathf.Max(1f, UiScale.S(2f));
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, line), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - line, r.width, line), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, line, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - line, r.y, line, r.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static bool Hover(Rect refRect) => UiScale.R(refRect).Contains(GuiInput.MousePositionGuiSpace);

    private static bool Clicked(Rect refRect) => GuiInput.TryConsumeClickInRect(UiScale.R(refRect));

    // ---------------------------------------------------------------- for the pages

    // A page draws in the menu's own look through these, in reference pixels.
    public GameMenuSkin Skin => skin;
    public bool PadInUse => GameInput.LastActiveDevice == GameInputDevice.Gamepad;
    public static Color Dim => DimColor;
    public const float PageRowHeight = RowHeight;
    public const float PageRowFont = RowFont;
    public const float PageSmallFont = SmallFont;
    public const float PageButtonHeight = ButtonHeight;

    /// <summary>The width of every button of the menu's rows, for a page's own buttons to match.</summary>
    public float PageButtonWidth => ButtonWidth();

    public void PlaySound(AudioClip clip) => Sound(clip);

    /// <summary>The page's own Back button: back to the list it was opened from.</summary>
    public void LeavePage()
    {
        CloseSubPage();
        Sound(skin.BackSound);
    }

    public void DrawPanel(Rect r) => Fill(r, PanelColor);

    public void DrawHighlight(Rect r, bool focused) => Fill(r, focused ? LitFill : new Color(1f, 0.84f, 0.25f, 0.10f));

    public void DrawFrame(Rect r, Color color) => Frame(r, color);

    public void DrawRect(Rect r, Color color) => Fill(r, color);

    /// <summary>True when the pointer clicked in the rect, and no question is up.</summary>
    public bool WasClicked(Rect r) => confirm == EConfirm.None && Clicked(r);

    public void DrawText(Rect r, string content, float size, TextAnchor anchor, Color color, bool wrap = false) =>
        Text(r, content, size, anchor, color, wrap);

    public float MeasureText(string content, float size) => Measure(content, size);

    /// <summary>A text cut short with an ellipsis to fit a width in reference pixels.</summary>
    public string FitText(string content, float size, float refWidth) =>
        Utils.Ellipsize(content, Style(size, TextAnchor.MiddleLeft), refWidth * UiScale.Factor);

    public bool IsHovered(Rect r) => !PadInUse && Hover(r);

    /// <summary>A button in the menu's one style. True when clicked.</summary>
    public bool DrawButton(Rect r, string label, bool enabled, bool on) => StyledButton(r, label, ButtonFont, enabled, on, PadInUse);

    /// <summary>The on and off box of a toggle row. True when clicked.</summary>
    public bool DrawCheckBox(Rect r, bool on, bool selected)
    {
        Frame(r, selected || Hover(r) ? Color.white : UnselectColor);
        if (on)
        {
            float inset = Mathf.Round(r.width * 0.22f);
            Fill(new Rect(r.x + inset, r.y + inset, r.width - 2 * inset, r.height - 2 * inset), selected || Hover(r) ? Color.white : SelectColor);
        }
        return confirm == EConfirm.None && Clicked(r);
    }

    /// <summary>A square button holding a picture, in the one style. True when clicked.</summary>
    public bool DrawIconButton(Rect r, Texture2D icon)
    {
        bool clicked = StyledButton(r, "", ButtonFont, true, false, PadInUse);
        if (icon != null)
        {
            float size = Mathf.Min(r.width, r.height) * 0.6f;
            Color previous = GUI.color;
            GUI.color = Hover(r) && !PadInUse ? Color.white : SelectColor;
            GUI.DrawTexture(UiScale.R(r.center.x - size / 2, r.center.y - size / 2, size, size), icon);
            GUI.color = previous;
        }
        return clicked;
    }

    /// <summary>
    /// A scroll bar for a list longer than its rows: a small arrow at each end for a row, the thumb
    /// dragged, the track clicked above or below it for a page. Returns the first row to show.
    /// </summary>
    // the frame a thumb was taken in: one drag at a time, whichever list it is
    private static int scrollDragFrame;

    public int DrawScrollBar(Rect whole, int scroll, int rows, int total, ref bool dragging, ref float grab)
    {
        int maxScroll = Mathf.Max(1, total - rows);
        Rect up = new Rect(whole.x, whole.y, whole.width, whole.width);
        Rect down = new Rect(whole.x, whole.yMax - whole.width, whole.width, whole.width);
        Rect bar = new Rect(whole.x, up.yMax + 2f, whole.width, whole.height - 2f * (whole.width + 2f));
        float thumbH = Mathf.Max(30f, bar.height * rows / Mathf.Max(1, total));
        float travel = bar.height - thumbH;
        Rect thumb = new Rect(bar.x, bar.y + travel * scroll / maxScroll, bar.width, thumbH);
        Rect barScreen = UiScale.R(bar);
        Rect thumbScreen = UiScale.R(thumb);

        Text(up, "\u25b2", 11f, TextAnchor.MiddleCenter, SelectColor);
        Text(down, "\u25bc", 11f, TextAnchor.MiddleCenter, SelectColor);

        Event e = Event.current;
        if (confirm == EConfirm.None)
        {
            if (Clicked(up))
            {
                scroll--;
            }
            else if (Clicked(down))
            {
                scroll++;
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && barScreen.Contains(e.mousePosition))
            {
                if (thumbScreen.Contains(e.mousePosition))
                {
                    dragging = true;
                    grab = UiScale.ToRef(e.mousePosition).y - thumb.y;
                    scrollDragFrame = Time.frameCount;
                }
                else
                {
                    scroll += UiScale.ToRef(e.mousePosition).y < thumb.y ? -rows : rows;
                }
                e.Use();
                GuiInput.MarkPrimaryClickConsumed();
            }
            else if (dragging && (e.type == EventType.MouseDrag || e.type == EventType.Repaint))
            {
                // the thumb follows the pointer at every repaint as well as at the drag events, which do not always
                // come (the user's report of 2 November 2026: the thumb of the controls map's list could not be dragged)
                float y = UiScale.ToRef(e.mousePosition).y - grab - bar.y;
                scroll = Mathf.RoundToInt(travel > 0f ? y / travel * maxScroll : 0f);
                if (e.type == EventType.MouseDrag)
                {
                    e.Use();
                }
            }
        }
        // let go when IMGUI sees the button come up, or the Input System sees it up at a repaint of a later frame: in the
        // frame of the press it may not have seen the press yet, and the drag ended as it began
        if (dragging && (e.rawType == EventType.MouseUp
            || (e.type == EventType.Repaint && Time.frameCount > scrollDragFrame && !(Mouse.current?.leftButton.isPressed ?? false))))
        {
            dragging = false;
        }

        Fill(bar, new Color(1f, 0.84f, 0.25f, 0.15f));
        Fill(thumb, dragging || (!PadInUse && Hover(thumb)) ? Color.white : UnselectColor);
        return Mathf.Clamp(scroll, 0, Mathf.Max(0, total - rows));
    }

    // ---------------------------------------------------------------- the frame

    /// <summary>
    /// The panel: the background picture covering it, or else a wall of the game's tiled across
    /// it, darkened, and a dark frame round it. Without either, the wood panel stretched.
    /// </summary>
    private void DrawWood(Rect refRect)
    {
        Texture2D wall = WallTexture();
        if (skin.Background != null)
        {
            // one picture, not a tile - in a game the signs' wood (woodwritingbackground.png), on the
            // title screen its rock (creditsbackground-transformed.png), both 1280x800 - so it covers
            // the panel and is cropped to its shape
            // - drawn as it is, not darkened, the user's choice of 31 October 2026
            GUI.DrawTexture(UiScale.R(refRect), skin.Background, ScaleMode.ScaleAndCrop);
        }
        else if (wall != null)
        {
            float tile = wall.width * BackgroundTexel;
            Rect screen = UiScale.R(refRect);
            // the texture repeats by itself (it is a wall), so one draw with coordinates past 1 tiles it
            GUI.DrawTextureWithTexCoords(screen, wall, new Rect(0f, 0f, refRect.width / tile, refRect.height / tile));
            // darkened by half, the user's of 31 October 2026, so the text over it reads
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, BackgroundDarkening);
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            GUI.color = previous;
        }
        else if (skin.Wood != null)
        {
            GUI.DrawTexture(UiScale.R(refRect), skin.Wood);
        }
        else
        {
            Fill(refRect, new Color(0.18f, 0.11f, 0.06f, 1f));
        }

        Color dark = skin.FrameDark;
        Fill(new Rect(refRect.x, refRect.y, refRect.width, FrameLine), dark);
        Fill(new Rect(refRect.x, refRect.yMax - FrameLine, refRect.width, FrameLine), dark);
        Fill(new Rect(refRect.x, refRect.y, FrameLine, refRect.height), dark);
        Fill(new Rect(refRect.xMax - FrameLine, refRect.y, FrameLine, refRect.height), dark);
        Frame(new Rect(refRect.x + FrameLine, refRect.y + FrameLine, refRect.width - 2 * FrameLine, refRect.height - 2 * FrameLine),
            skin.FrameLight);
    }

    private static Texture2D WallTexture()
    {
        List<List<Texture2D>> walls = DataLoader.sDataLoader != null ? DataLoader.sDataLoader.wallTex : null;
        if (walls == null || walls.Count <= BackgroundWallTexture || walls[BackgroundWallTexture] == null
            || walls[BackgroundWallTexture].Count == 0)
        {
            return null;
        }
        return walls[BackgroundWallTexture][0];
    }

    /// <summary>A pad button alone, beside the tabs or the sections it changes.</summary>
    private void Glyph(Rect refRect, string padButton)
    {
        Hints(refRect, new[] { new Hint("", HintKey.PadButton(padButton)) });
    }

    // ---------------------------------------------------------------- drawing

    public void OnGUI()
    {
        float W = UiScale.Width;
        float H = UiScale.Height;
        Rect screen = new Rect(0, 0, Screen.width, Screen.height);
        GuiInput.RegisterBlockingRect(screen);

        if (dimBehind)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        Rect panel = new Rect(Margin, Margin, W - 2 * Margin, H - 2 * Margin);
        DrawWood(panel);

        bool pad = GameInput.LastActiveDevice == GameInputDevice.Gamepad;
        bool mouse = !pad;
        Vector2 mousePos = GuiInput.MousePositionGuiSpace;
        bool mouseMoved = (mousePos - lastMouse).sqrMagnitude > 0.25f;
        lastMouse = mousePos;

        float innerX = Margin + Padding;
        float innerW = W - 2 * innerX;
        float tabsY = Margin + 14f;

        // A question takes the clicks first, being drawn over everything else.
        Rect confirmBox = new Rect((W - ConfirmWidth) / 2, (H - ConfirmHeight) / 2, ConfirmWidth, ConfirmHeight);
        float choiceW = 220f;
        Rect yes = new Rect(confirmBox.center.x - 20 - choiceW, confirmBox.yMax - 40 - ButtonHeight, choiceW, ButtonHeight);
        Rect no = new Rect(confirmBox.center.x + 20, yes.y, choiceW, ButtonHeight);
        if (confirm != EConfirm.None)
        {
            if (Clicked(yes))
            {
                Answer(true);
            }
            else if (Clicked(no))
            {
                Answer(false);
            }
            else if (GuiInput.IsLeftMouseDownGui)
            {
                Event.current.Use();
            }
        }

        DrawTabs(innerX, innerW, tabsY, pad);

        float hintsY = H - Margin - 12f - HintsHeight;
        Rect hintsRow = new Rect(innerX, hintsY, innerW, HintsHeight);
        float buttonsY = hintsY - 8f - ButtonHeight;
        bool hoverSelects = mouse && mouseMoved && confirm == EConfirm.None;

        if (subPage != null)
        {
            float sectionsY = tabsY + TabsHeight + 4f;
            float chipW = Measure(subPage.Title, SectionFont) + 60f;
            StyledButton(new Rect(innerX + (innerW - chipW) / 2, sectionsY, chipW, SectionsHeight), subPage.Title, SectionFont, true, true, true);
            float top = sectionsY + SectionsHeight + 12f;
            // the tabs drawn before may have closed the page with a click
            subPage?.Draw(new Rect(innerX, top, innerW, hintsY - 12f - top));
            if (subPage != null)
            {
                Hints(hintsRow, HintsNow(subPage.Hints(pad), pad));
            }
        }
        else if (IsHostTab)
        {
            float top = tabsY + TabsHeight + 12f;
            float bottom = buttonsY - 12f;
            float listW = Mathf.Min(640f, innerW * 0.45f);
            Rect list = new Rect(innerX, top, listW, bottom - top);
            Rect detail = new Rect(innerX + listW + 24f, top, innerW - listW - 24f, bottom - top);
            Fill(list, PanelColor);
            Fill(detail, PanelColor);
            host.DrawPage(Tab, list, detail);
            DrawHostButtons(innerX, innerW, buttonsY, pad);
            Hints(hintsRow, HintsNow(host.PageHints(Tab, pad), pad));
        }
        else if (ControlsMap.Shows(Section))
        {
            float sectionsY = tabsY + TabsHeight + 4f;
            DrawSections(innerX, innerW, sectionsY, pad);
            float top = sectionsY + SectionsHeight + 12f;
            if (controlsMap.Draw(Section, new Rect(innerX, top, innerW, buttonsY - 12f - top), hoverSelects))
            {
                onButtons = false;
            }
            DrawButtons(innerX, innerW, buttonsY, pad, hoverSelects);
            Hints(hintsRow, HintsNow(controlsMap.Hints(Section, pad), pad));
        }
        else
        {
            float sectionsY = tabsY + TabsHeight + 4f;
            DrawSections(innerX, innerW, sectionsY, pad);
            float top = sectionsY + SectionsHeight + 12f;
            float bottom = buttonsY - 12f;
            float listW = Mathf.Round(innerW * 0.6f);
            Rect list = new Rect(innerX, top, listW, bottom - top);
            Rect detail = new Rect(innerX + listW + 24f, top, innerW - listW - 24f, bottom - top);
            Fill(list, PanelColor);
            Fill(detail, PanelColor);
            DrawRows(list, pad, hoverSelects);
            DrawDetail(detail);
            DrawButtons(innerX, innerW, buttonsY, pad, hoverSelects);
            Hints(hintsRow, HintsNow(OptionHints(pad), pad));
        }

        if (confirm != EConfirm.None)
        {
            DrawConfirm(confirmBox, yes, no);
        }

        GuiInput.TryConsumeClickInPanel(screen);
    }

    private void DrawTabs(float innerX, float innerW, float y, bool pad)
    {
        float gap = 12f;
        float width = 0f;
        foreach (ETab t in tabs)
        {
            width = Mathf.Max(width, Measure(TabName(t), TabFont) + 48f);
        }
        float total = tabs.Length * (width + gap) - gap;
        float x = innerX + (innerW - total) / 2;
        // not over a page of the host's, where LB and RB are the page's own: the music tracks remove a track on LB (fix 473)
        if (pad && tabs.Length > 1 && subPage == null)
        {
            Glyph(new Rect(x - 80f, y, 60f, TabsHeight), "LB");
            Glyph(new Rect(x + total + 20f, y, 60f, TabsHeight), "RB");
        }
        for (int i = 0; i < tabs.Length; i++)
        {
            Rect r = new Rect(x + i * (width + gap), y, width, TabsHeight);
            bool selected = i == tabIndex;
            if (StyledButton(r, TabName(tabs[i]), TabFont, true, selected, pad) && !selected)
            {
                ShowTab(i);
                Sound(skin.MoveSound);
            }
        }
    }

    /// <summary>The sections of OPTIONS or CONTROLS, all as wide as the widest, which is GAMEPLAY.</summary>
    private void DrawSections(float innerX, float innerW, float y, bool pad)
    {
        List<EOptionSection> list = Sections(Tab);
        sectionOf.TryGetValue(Tab, out int current);
        current = Mathf.Clamp(current, 0, list.Count - 1);
        float gap = 10f;
        float width = 0f;
        foreach (EOptionSection section in list)
        {
            width = Mathf.Max(width, Measure(SectionName(section), SectionFont) + 40f);
        }
        float total = list.Count * (width + gap) - gap;
        float x = innerX + (innerW - total) / 2;
        if (pad && list.Count > 1)
        {
            Glyph(new Rect(x - 74f, y, 56f, SectionsHeight), "LT");
            Glyph(new Rect(x + total + 18f, y, 56f, SectionsHeight), "RT");
        }
        for (int i = 0; i < list.Count; i++)
        {
            Rect r = new Rect(x + i * (width + gap), y, width, SectionsHeight);
            bool selected = i == current;
            if (StyledButton(r, SectionName(list[i]), SectionFont, true, selected, pad) && !selected)
            {
                sectionOf[Tab] = i;
                row = 0;
                rowScroll = 0;
                onButtons = false;
                Sound(skin.MoveSound);
            }
        }
    }

    private static readonly Color LitFill = new Color(1f, 0.84f, 0.25f, 0.22f);

    /// <summary>
    /// Every button of the menu in one style, the user's of 30 October 2026: disabled, grey text and
    /// outline, no fill, deaf to the pointer; off, yellow text and orange outline; under the pointer,
    /// white text and outline on a see-through yellow; on (the tab, the section, the answer or the
    /// button the keys have reached), yellow text and orange outline on the see-through yellow.
    /// True when clicked.
    /// </summary>
    private bool StyledButton(Rect r, string label, float font, bool enabled, bool on, bool pad, Rect? textRect = null)
    {
        bool hover = enabled && !pad && Hover(r);
        on = on && enabled;
        if (hover || on)
        {
            Fill(r, LitFill);
        }
        Color line = !enabled ? DimColor : hover ? Color.white : UnselectColor;
        Color ink = !enabled ? DimColor : hover ? Color.white : SelectColor;
        Frame(r, line);
        Text(textRect ?? r, label, font, TextAnchor.MiddleCenter, ink);
        return enabled && confirm == EConfirm.None && Clicked(r);
    }

    private void DrawRows(Rect list, bool pad, bool hoverSelects)
    {
        List<GameOption> rows = Rows;
        int visible = Mathf.Max(1, Mathf.FloorToInt((list.height - 16f) / RowHeight));
        int maxScroll = Mathf.Max(0, rows.Count - visible);
        if (!onButtons)
        {
            row = Mathf.Clamp(row, 0, rows.Count - 1);
            if (row < rowScroll)
            {
                rowScroll = row;
            }
            else if (row >= rowScroll + visible)
            {
                rowScroll = row - visible + 1;
            }
        }
        rowScroll = Mathf.Clamp(rowScroll, 0, maxScroll);

        float valueX = list.xMax - 20f - ValueWidth;
        float labelX = list.x + 40f;
        float labelW = valueX - 16f - labelX;

        for (int r = 0; r < visible && rowScroll + r < rows.Count; r++)
        {
            int i = rowScroll + r;
            GameOption o = rows[i];
            Rect rect = new Rect(list.x + 8f, list.y + 8f + r * RowHeight, list.width - 16f, RowHeight);
            bool selected = !onButtons && i == row;

            if (hoverSelects && Hover(rect) && (i != row || onButtons))
            {
                row = i;
                onButtons = false;
                selected = true;
            }
            if (selected)
            {
                Fill(rect, new Color(1f, 0.84f, 0.25f, 0.22f));
            }

            // the dot of a value away from its default
            if (!GameOptions.IsDefault(o))
            {
                Fill(new Rect(list.x + 20f, rect.center.y - 5f, 10f, 10f), SelectColor);
            }

            // a toggle's box is narrow, so its label may run on up to it
            float rowLabelW = o.Kind == EOptionKind.Toggle ? valueX + ValueWidth - BoxSize - 16f - labelX : labelW;
            Color labelColor = selected ? Color.white : SelectColor;
            Text(new Rect(labelX, rect.y, rowLabelW, rect.height), o.Label, RowFont, TextAnchor.MiddleLeft, labelColor);
            WarnIfTooWide(o.Label, rowLabelW);

            Rect value = new Rect(valueX, rect.y, ValueWidth, rect.height);
            switch (o.Kind)
            {
                case EOptionKind.Toggle:
                    DrawToggleRow(o, rect, value, selected);
                    break;
                case EOptionKind.Slider:
                    DrawSliderRow(o, rect, value, selected);
                    break;
                case EOptionKind.Page:
                    DrawPageRow(o, rect, value, selected);
                    break;
                case EOptionKind.Choice:
                    DrawChoiceRow(o, rect, value, selected, pad);
                    break;
            }
        }

        if (rowScroll > 0)
        {
            Text(new Rect(list.xMax - 40f, list.y + 4f, 30f, 30f), "▲", 22f, TextAnchor.MiddleCenter, SelectColor);
        }
        if (rowScroll < maxScroll)
        {
            Text(new Rect(list.xMax - 40f, list.yMax - 34f, 30f, 30f), "▼", 22f, TextAnchor.MiddleCenter, SelectColor);
        }
    }

    /// <summary>On and off as the track list of the music page has it: a box, filled when on. A click anywhere on the row flips it.</summary>
    private void DrawToggleRow(GameOption o, Rect rect, Rect value, bool selected)
    {
        bool on = GameOptions.Get(o) != 0f;
        Rect box = new Rect(value.xMax - BoxSize, value.center.y - BoxSize / 2, BoxSize, BoxSize);
        Frame(box, selected ? Color.white : UnselectColor);
        if (on)
        {
            float inset = 7f;
            Fill(new Rect(box.x + inset, box.y + inset, box.width - 2 * inset, box.height - 2 * inset), selected ? Color.white : SelectColor);
        }
        if (confirm == EConfirm.None && Clicked(rect))
        {
            Select(o);
            GameOptions.Step(o, 1);
            Sound(skin.MoveSound);
        }
    }

    /// <summary>A track with the default as a notch and the value written at its end; clicked or dragged.</summary>
    private void DrawSliderRow(GameOption o, Rect rect, Rect value, bool selected)
    {
        float v = GameOptions.Get(o);
        float share = Mathf.InverseLerp(o.Min, o.Max, v);
        float defaultShare = Mathf.InverseLerp(o.Min, o.Max, o.Default);
        Rect track = new Rect(value.x, value.center.y - 2f, SliderWidth, 4f);
        Fill(track, UnselectColor);
        Fill(new Rect(track.x, track.y, track.width * share, track.height), SelectColor);
        Fill(new Rect(track.x + track.width * defaultShare - 1f, track.center.y - 9f, 2f, 18f), DimColor);
        Fill(new Rect(track.x + track.width * share - 6f, track.center.y - 14f, 12f, 28f), selected ? Color.white : SelectColor);
        Text(new Rect(track.xMax + 10f, rect.y, value.xMax - track.xMax - 10f, rect.height), o.Format(v), RowFont,
            TextAnchor.MiddleRight, selected ? Color.white : SelectColor);

        // the track takes the pointer over the whole height of the row, which is easier to hit
        Rect grab = new Rect(track.x - 10f, rect.y, track.width + 20f, rect.height);
        Event e = Event.current;
        Rect grabScreen = UiScale.R(grab);
        if (confirm == EConfirm.None && e.type == EventType.MouseDown && e.button == 0 && grabScreen.Contains(e.mousePosition))
        {
            dragging = o;
            Select(o);
            SetFromPointer(o, track, e.mousePosition);
            e.Use();
            GuiInput.MarkPrimaryClickConsumed();
        }
        else if (dragging == o)
        {
            if (e.type == EventType.MouseDrag)
            {
                SetFromPointer(o, track, e.mousePosition);
                e.Use();
            }
            else if (e.type == EventType.MouseUp || !(Mouse.current?.leftButton.isPressed ?? false))
            {
                dragging = null;
            }
        }
        else if (confirm == EConfirm.None && Clicked(rect))
        {
            Select(o);
        }
    }

    private static void SetFromPointer(GameOption o, Rect refTrack, Vector2 guiPointer)
    {
        float x = UiScale.ToRef(guiPointer).x;
        GameOptions.HoldGrowth(o.Key == UiScale.GrowthPrefsKey);
        GameOptions.Set(o, Mathf.Lerp(o.Min, o.Max, Mathf.InverseLerp(refTrack.x, refTrack.xMax, x)));
    }

    /// <summary>A name stepped through with left and right, the arrows shown on the selected row and clickable.</summary>
    /// <summary>A row that opens a page: an arrow at the right, and a click anywhere on it opens.</summary>
    private void DrawPageRow(GameOption o, Rect rect, Rect value, bool selected)
    {
        Text(new Rect(value.xMax - 36f, rect.y, 36f, rect.height), "\u25b6", 24f, TextAnchor.MiddleCenter, selected ? Color.white : SelectColor);
        if (confirm == EConfirm.None && Clicked(rect))
        {
            Select(o);
            OpenSubPage(o.Key);
        }
    }

    private void DrawChoiceRow(GameOption o, Rect rect, Rect value, bool selected, bool pad)
    {
        string name = GameOptions.ValueText(o);
        Color color = selected ? Color.white : SelectColor;
        if (selected)
        {
            Rect left = new Rect(value.x, rect.y, 36f, rect.height);
            Rect right = new Rect(value.xMax - 36f, rect.y, 36f, rect.height);
            Text(left, "◀", 24f, TextAnchor.MiddleCenter, Hover(left) ? Color.white : SelectColor);
            Text(right, "▶", 24f, TextAnchor.MiddleCenter, Hover(right) ? Color.white : SelectColor);
            Text(new Rect(left.xMax, rect.y, right.x - left.xMax, rect.height), name, RowFont, TextAnchor.MiddleCenter, color);
            if (confirm == EConfirm.None)
            {
                if (Clicked(left))
                {
                    GameOptions.Step(o, -1);
                    Sound(skin.MoveSound);
                    return;
                }
                if (Clicked(right))
                {
                    GameOptions.Step(o, 1);
                    Sound(skin.MoveSound);
                    return;
                }
            }
        }
        else
        {
            Text(value, name, RowFont, TextAnchor.MiddleRight, color);
        }
        if (confirm == EConfirm.None && Clicked(rect))
        {
            Select(o);
        }
    }

    private void Select(GameOption o)
    {
        int i = Rows.IndexOf(o);
        if (i >= 0)
        {
            row = i;
            onButtons = false;
        }
    }

    private void DrawDetail(Rect detail)
    {
        List<GameOption> rows = Rows;
        if (onButtons || rows.Count == 0)
        {
            List<EButton> buttons = Buttons();
            if (onButtons)
            {
                EButton b = buttons[Mathf.Clamp(button, 0, buttons.Count - 1)];
                float x = detail.x + 24f;
                float w = detail.width - 48f;
                Text(new Rect(x, detail.y + 20f, w, 44f), ButtonLabel(b), DetailTitleFont, TextAnchor.MiddleLeft, Color.white);
                Text(new Rect(x, detail.y + 76f, w, detail.height - 96f), ButtonHelp(b), DetailFont, TextAnchor.UpperLeft, SelectColor, true);
            }
            return;
        }

        GameOption o = rows[Mathf.Clamp(row, 0, rows.Count - 1)];
        float left = detail.x + 24f;
        float width = detail.width - 48f;
        float y = detail.y + 20f;
        Text(new Rect(left, y, width, 44f), o.Label, DetailTitleFont, TextAnchor.MiddleLeft, Color.white);
        y += 56f;
        // one paragraph per line of the description - Enabled, Disabled - as far apart as the Default line below
        foreach (string paragraph in o.Description.Split('\n'))
        {
            float h = MeasureHeight(paragraph, DetailFont, width);
            Text(new Rect(left, y, width, h), paragraph, DetailFont, TextAnchor.UpperLeft, SelectColor, true);
            y += h + 20f;
        }
        if (o.Original != null)
        {
            string original = $"<b>{OriginalWord}:</b> " + o.Original;
            float oh = MeasureHeight(original, SmallFont, width);
            Text(new Rect(left, y, width, oh), original, SmallFont, TextAnchor.UpperLeft, UnselectColor, true);
            y += oh + 12f;
        }
        if (o.Kind != EOptionKind.Page)
        {
            Text(new Rect(left, y, width, 36f), "<b>Default:</b> " + GameOptions.DefaultText(o), SmallFont, TextAnchor.UpperLeft, UnselectColor);
        }
    }

    private static string ButtonHelp(EButton b)
    {
        switch (b)
        {
            case EButton.Reset: return "Puts every setting of this section back to its default.";
            case EButton.ResetAll: return "Puts every setting of every section of this tab back to its default.";
            case EButton.Undo: return "Puts every setting back as it was when you opened the menu.";
            case EButton.End: return "Leaves the game for the title screen. Progress not saved is lost.";
            default: return $"Turns every setting of this section to its {OriginalWord.ToLowerInvariant()} rule, the one the original game plays by. Each can still be changed afterwards.";
        }
    }

    /// <summary>
    /// One width for every button of the row, the widest label's: the row reads as one set, and a
    /// translation that lengthens a label widens them all instead of one.
    /// </summary>
    private float ButtonWidth()
    {
        float widest = Measure("Undo changes", ButtonFont);
        foreach (EButton b in new[] { EButton.Reset, EButton.ResetAll, EButton.Original })
        {
            widest = Mathf.Max(widest, Measure(ButtonLabel(b), ButtonFont));
        }
        if (host.EndButton != null)
        {
            widest = Mathf.Max(widest, Measure(host.EndButton, ButtonFont) + 36f);
        }
        foreach (ETab t in tabs)
        {
            if (t == ETab.Save || t == ETab.Load)
            {
                foreach (string label in host.PageButtons(t))
                {
                    widest = Mathf.Max(widest, Measure(label, ButtonFont));
                }
            }
        }
        return widest + 48f;
    }

    /// <summary>A button of the row: framed, lit when chosen, dimmed when it would do nothing. True when clicked.</summary>
    private bool Button(Rect r, string label, bool enabled, bool lit, bool pad, bool padIcon)
    {
        Rect? textRect = null;
        if (padIcon)
        {
            // what the pad does instead of a click, at the left inside the button
            host.DrawEndButtonPadIcon(new Rect(r.x + 8f, r.center.y - 18f, 36f, 36f));
            textRect = new Rect(r.x + 44f, r.y, r.width - 48f, r.height);
        }
        return StyledButton(r, label, ButtonFont, enabled, lit, pad, textRect);
    }

    /// <summary>The row under OPTIONS and CONTROLS: the resets, Undo changes, and the host's end button at the right.</summary>
    private void DrawButtons(float x, float width, float y, bool pad, bool hoverSelects)
    {
        List<EButton> buttons = Buttons();
        float bw = ButtonWidth();
        float left = x;
        for (int i = 0; i < buttons.Count; i++)
        {
            EButton b = buttons[i];
            bool end = b == EButton.End;
            Rect r = new Rect(end ? x + width - bw : left, y, bw, ButtonHeight);
            // Only the keys light a button for good; the pointer's light goes with it (StyledButton).
            bool lit = onButtons && button == i;
            if (Button(r, ButtonLabel(b), Enabled(b), lit, pad, end && pad))
            {
                onButtons = true;
                button = i;
                Press(b);
            }
            left = r.xMax + 16f;
        }
    }

    /// <summary>The row under SAVE and LOAD: the host's buttons, and its end button at the right.</summary>
    private void DrawHostButtons(float x, float width, float y, bool pad)
    {
        float bw = ButtonWidth();
        string[] labels = host.PageButtons(Tab);
        for (int i = 0; i < labels.Length; i++)
        {
            Rect r = new Rect(x + i * (bw + 16f), y, bw, ButtonHeight);
            if (Button(r, labels[i], host.PageButtonEnabled(Tab, i), false, pad, false))
            {
                Sound(skin.MakeSound);
                host.PressPageButton(Tab, i);
            }
        }
        if (host.EndButton != null)
        {
            Rect r = new Rect(x + width - bw, y, bw, ButtonHeight);
            if (Button(r, host.EndButton, true, false, pad, pad))
            {
                host.PressEndButton();
            }
        }
    }

    private void DrawConfirm(Rect box, Rect yes, Rect no)
    {
        Fill(box, new Color(0f, 0f, 0f, 0.92f));
        Frame(box, SelectColor);
        Text(new Rect(box.x + 40f, box.y + 30f, box.width - 80f, box.height - 120f - ButtonHeight), ConfirmQuestion(), DetailTitleFont,
            TextAnchor.MiddleCenter, confirm == EConfirm.Host && hostWarning ? WarningColor : Color.white, true);
        DrawChoice(yes, "Yes", confirmYes);
        DrawChoice(no, "No", !confirmYes);
    }

    private void DrawChoice(Rect r, string label, bool lit)
    {
        // drawn in the one style; its click is taken before anything else is drawn (OnGUI)
        bool hover = Hover(r);
        if (hover || lit)
        {
            Fill(r, LitFill);
        }
        Frame(r, hover ? Color.white : UnselectColor);
        Text(r, label, ButtonFont, TextAnchor.MiddleCenter, hover ? Color.white : SelectColor);
    }

    // the repeat of a held direction: the pack's cursor's times (Inventory.ShouldInventoryMoveRepeat())
    private const float RepeatDelay = 0.25f;
    private const float RepeatInterval = 0.05f;
    private readonly float[] heldFor = { -1f, -1f, -1f, -1f };

    /// <summary>Whether a held left or right repeats: on a slider row only.</summary>
    private bool SidewaysRepeats()
    {
        if (IsHostTab || onButtons)
        {
            return false;
        }
        List<GameOption> rows = Rows;
        // row is -1 on a section with no rows, the control maps
        return row >= 0 && row < rows.Count && rows[row].Kind == EOptionKind.Slider;
    }

    /// <summary>A direction held down: true on the frames its repeat comes round, after the press's own move and a delay.</summary>
    private bool Repeats(int direction, bool held)
    {
        if (!held)
        {
            heldFor[direction] = -1f;
            return false;
        }
        if (heldFor[direction] < 0f)
        {
            // the frame of the press, which moves by itself
            heldFor[direction] = 0f;
            return false;
        }
        heldFor[direction] += Time.unscaledDeltaTime;
        if (heldFor[direction] < RepeatDelay)
        {
            return false;
        }
        heldFor[direction] -= RepeatInterval;
        return true;
    }

    /// <summary>What the keys do while the Yes/No box is up, in place of the page's (fix 473).</summary>
    private Hint[] ConfirmHints(bool pad)
    {
        return pad
            ? new[]
            {
                new Hint("yes or no", HintKey.Of(skin.DPadLeftRight)),
                new Hint("answer", HintKey.Of(skin.AButton)),
                new Hint("no", HintKey.Of(skin.BButton)),
            }
            : new[]
            {
                new Hint("yes or no", HintKey.Keyboard("←"), HintKey.Keyboard("→")),
                new Hint("answer", HintKey.Keyboard("Enter")),
                new Hint("no", HintKey.Of(skin.EscapeKey)),
            };
    }

    /// <summary>The hints of the page, or the question's while one is asked.</summary>
    private Hint[] HintsNow(Hint[] page, bool pad) => confirm != EConfirm.None ? ConfirmHints(pad) : page;

    private Hint[] OptionHints(bool pad)
    {
        bool sections = Sections(Tab).Count > 1;
        List<Hint> hints = new List<Hint>();
        if (pad)
        {
            hints.Add(new Hint("change", HintKey.Of(skin.DPadLeftRight)));
            hints.Add(new Hint("tab", HintKey.PadButton("LB"), HintKey.PadButton("RB")));
            if (sections)
            {
                hints.Add(new Hint("section", HintKey.PadButton("LT"), HintKey.PadButton("RT")));
            }
            hints.Add(new Hint("reset", HintKey.PadButton("Select")));
            hints.Add(new Hint("hold: reset all", HintKey.PadButton("Select")));
            hints.Add(new Hint("close", HintKey.Of(skin.BButton)));
        }
        else
        {
            hints.Add(new Hint("change", HintKey.Keyboard("←"), HintKey.Keyboard("→")));
            hints.Add(new Hint("tab", HintKey.Keyboard("Tab")));
            if (sections)
            {
                hints.Add(new Hint("section", HintKey.Keyboard("PgUp"), HintKey.Keyboard("PgDn")));
            }
            hints.Add(new Hint("reset", HintKey.Keyboard("R")));
            hints.Add(new Hint("reset all", HintKey.Keyboard("Shift"), HintKey.Keyboard("R")));
            hints.Add(new Hint("undo", HintKey.Keyboard("Ctrl"), HintKey.Keyboard("Z")));
            hints.Add(new Hint("close", HintKey.Of(skin.EscapeKey)));
        }
        return hints.ToArray();
    }

    private void Hints(Rect refRow, Hint[] hints)
    {
        if (hints == null || hints.Length == 0)
        {
            return;
        }
        GUIStyle label = new GUIStyle(skin.Font) { fontSize = Mathf.RoundToInt(HintLabelFont), alignment = TextAnchor.MiddleLeft };
        label.normal.textColor = SelectColor;
        GUIStyle key = new GUIStyle(skin.Font) { fontSize = Mathf.RoundToInt(HintKeyFont), alignment = TextAnchor.MiddleCenter };
        // white on the grey key, as the Esc picture is
        key.normal.textColor = Color.white;
        HintBar.Draw(UiScale.R(refRow), UiScale.Factor, label, key, skin.BlankKey, hints);
    }

    /// <summary>
    /// In the Editor, a label wider than its column is said once in the log: the widths are measured,
    /// not counted in characters, so that a translation is checked the same way.
    /// </summary>
    private void WarnIfTooWide(string label, float refWidth)
    {
#if UNITY_EDITOR
        if (Measure(label, RowFont) > refWidth && labelsWarned.Add(label))
        {
            Debug.LogWarning($"GameMenu: the label \"{label}\" is {Measure(label, RowFont):0} reference pixels wide, its column {refWidth:0}.");
        }
#endif
    }
}
