using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// The game menu in a game: opens it, pauses the game under it, and keeps its two pages of saves.
/// </summary>
/// <remarks>
/// The menu itself - the frame, the tabs, the OPTIONS and CONTROLS pages - is GameMenu; this is its
/// host in a game, holding what only a game has: the lists of saves to save over or load, the way
/// back to the title screen, the quicksave and the pause when the window loses focus.
/// Runs after <see cref="PlayerPanelInput"/> so opening from Esc runs before we evaluate Esc-to-close here.
/// </remarks>
[DefaultExecutionOrder(50)]
public class SaveLoadGUI : MonoBehaviour, IGameMenuHost
{
    public Texture2D background;
    [Tooltip("The picture behind the game menu: the wood of the signs.")]
    public Texture2D menuBackground;
    public GUIStyle title;
    public GUIStyle text;
    public Texture2D aButton;
    public Texture2D bButton;
    public Texture2D xButton;
    public Texture2D yButton;
    public Texture2D escapeKey;
    public Texture2D blankKey;
    public Texture2D buttonOutline;
    public Texture2D dPadLeftRight;
    public Texture2D xButtonChargeRing;
    public Material xButtonChargeRingMaterial;

    [Tooltip("Open the save/load screen, and so pause the game, when the window loses focus.")]
    public bool pauseOnFocusLoss = true;

    public AudioClip diskSound;
    public AudioClip moveSound;
    public AudioClip makeSound;
    public AudioClip backSound;

    private bool visible;
    private int index;
    private Material chargeRingMatInstance;

    // The menu, made when first needed; not serialized, so a script reload in the Editor makes it again.
    private GameMenu menu;

    // The tab of saves in view, SAVE or LOAD: the save list carries one row the load list does not.
    private GameMenu.ETab pageTab = GameMenu.ETab.Save;

    // Yes to going back to the title, waiting for the mouse button to come up: the release must not
    // land on the title screen's menu.
    private bool quitPending;
    private float quitHoldXTime;
    private const float QuitHoldXDurationSeconds = 0.85f;

    // The quicksave button is held, not clicked. Same duration as the hold above, so a player only
    // has to learn one: long enough that the thumb resting on the right stick cannot write a save
    // by accident while looking around.
    private const float QuickSaveHoldSeconds = 0.85f;
    private float quickSaveHoldTime;
    private bool quickSaveDoneThisHold;

    // The list is whatever is on disk, newest first, so it has no fixed length any more and the
    // rows scroll. slotScroll is the first row drawn.
    private SaveGameManager.SaveSlotInfo[] saves = new SaveGameManager.SaveSlotInfo[0];
    private Texture2D[] slotScreenshots = new Texture2D[0];
    private int slotScroll;

    // The window follows the selection when the selection is what moved, and stays where the
    // player put it when he scrolled it himself: otherwise the wheel would be undone on the very
    // next frame, because the selection is still where it was.
    private bool slotScrollFollowsSelection = true;

    private string saveName;

    /// <summary>Most characters a save made by hand will take.</summary>
    /// <remarks>
    /// As wide as the rows the game writes for itself, which run to twenty six characters at
    /// their longest - "Autosave - Bartolomeo - Lvl 8" - so a name typed by hand cannot make a
    /// row the list has no room for.
    /// </remarks>
    private const int MaxSaveNameLength = 26;

    /// <summary>Frame when the panel became visible; Esc opens via <see cref="PlayerPanelInput"/> then this Update sees the same Esc press — ignore Esc-to-close on that frame.</summary>
    private int visibleShownAtFrame = -1;

    // The lists of saves, in reference pixels (UiScale).
    private const float SlotRowHeight = 52f;
    private const float SlotFont = 32f;
    private const float SlotTextInset = 20f;
    private const float ScrollBarWidth = 14f;
    private const float PreviewFont = 26f;
    // The screenshots are taken at the shape the old preview panel showed them, 570 x 332.
    private const float ScreenshotAspect = 570f / 332f;

    // The scroll bar of the list of saves, while its thumb is dragged.
    private bool draggingThumb;
    private float thumbGrab;
    // a row double clicked in the list, acted on in the next Update as Enter
    private bool rowDoubleClicked;

    private GameMenu Menu
    {
        get
        {
            if (menu == null)
            {
                GameMenuSkin skin = new GameMenuSkin
                {
                    Font = text,
                    Background = menuBackground,
                    Wood = background,
                    AButton = aButton,
                    BButton = bButton,
                    XButton = xButton,
                    YButton = yButton,
                    EscapeKey = escapeKey,
                    BlankKey = blankKey,
                    DPadLeftRight = dPadLeftRight,
                    MoveSound = moveSound,
                    MakeSound = makeSound,
                    BackSound = backSound,
                };
                menu = new GameMenu(skin, this,
                    new[] { GameMenu.ETab.Save, GameMenu.ETab.Load, GameMenu.ETab.Options, GameMenu.ETab.Controls }, true);
            }
            return menu;
        }
    }

    private void Start()
    {
        if (xButtonChargeRingMaterial != null)
        {
            chargeRingMatInstance = new Material(xButtonChargeRingMaterial);
        }
    }

    private void OnDestroy()
    {
        if (chargeRingMatInstance != null)
        {
            Destroy(chargeRingMatInstance);
            chargeRingMatInstance = null;
        }
    }

    private void Hide()
    {
        // the options are saved as the menu goes, and a line says so when any changed
        if (visible && Menu.Close())
        {
            Messages.Add("Options saved.");
        }
        visible = false;
        visibleShownAtFrame = -1;
        ResetQuitToMainMenuState();
        PlayerObject.DisableControls(EControlMask.SaveLoad, false);
        Time.timeScale = 1.0f;
    }

    private void ResetQuitToMainMenuState()
    {
        quitHoldXTime = 0f;
        quitPending = false;
    }

    /// <summary>Main Menu clicked or chosen: asked in the menu's own Yes/No box, No lit first. X held goes without asking.</summary>
    private void AskQuitToMainMenu()
    {
        quitHoldXTime = 0f;
        Menu.Ask("Return to the title screen? Progress not saved is lost.", () => quitPending = true);
    }

    private void ConfirmQuitToMainMenu()
    {
        Hide();
        SceneManager.LoadScene("World");
    }

    public static SaveLoadGUI InstanceOrFind()
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<SaveLoadGUI>();
#else
        return Object.FindObjectOfType<SaveLoadGUI>();
#endif
    }

    private const EControlMask PanelOnlyControlMask = EControlMask.Inventory | EControlMask.Magic;

    private static bool IsModalInventoryUiBlocking()
    {
        PlayerObject player = PlayerObject.Player;
        return player != null
            && (player.controlsDisabled & (EControlMask.HowMany | EControlMask.RepairDialog)) != 0;
    }

    private bool CanOpenSaveLoadFromGameplay()
    {
        if (visible)
        {
            return false;
        }
        if (PlayerObject.Player == null || LevelLoader.sLevelLoader == null)
        {
            return false;
        }
        if (LevelLoader.sLevelLoader.loadedLevel == 9)
        {
            return false;
        }
        if (KeyboardActive())
        {
            return false;
        }
        if (IsModalInventoryUiBlocking())
        {
            return false;
        }
        EControlMask blocked = PlayerObject.Player.controlsDisabled & ~PanelOnlyControlMask;
        return blocked == 0;
    }

    private static void DismissPlayerPanelsBeforeOpen()
    {
        if (PlayerPanelState.ActivePanel == EPlayerPanel.Inventory)
        {
            Inventory.HidePanel();
        }
        else if (PlayerPanelState.ActivePanel == EPlayerPanel.Magic)
        {
            Magic.HidePanel();
        }
        else if (PlayerPanelState.ActivePanel != EPlayerPanel.None)
        {
            PlayerPanelState.SetPanel(EPlayerPanel.None);
        }

        StatsPanel.sStatsPanel?.Hide();
    }

    public bool TryOpenFromGame()
    {
        if (!CanOpenSaveLoadFromGameplay())
        {
            return false;
        }

        DismissPlayerPanelsBeforeOpen();
        PlayerObject.DisableControls(EControlMask.Inventory, false);
        PlayerObject.DisableControls(EControlMask.Magic, false);

        visible = true;
        visibleShownAtFrame = Time.frameCount;
        index = 0;
        slotScroll = 0;
        slotScrollFollowsSelection = true;
        ResetQuitToMainMenuState();
        PlayerObject.DisableControls(EControlMask.SaveLoad, true);
        Time.timeScale = 0.0f;
        Menu.Open(GameMenu.ETab.Save);
        return true;
    }

    private bool KeyboardActive()
    {
        return KeyboardGUI.sKeyboard != null && KeyboardGUI.sKeyboard.IsVisible();
    }

    /// <summary>
    /// Pauses the game when the window loses focus, by opening the save/load screen.
    /// </summary>
    /// <remarks>
    /// Nothing else in the project handles focus, so whether alt-tabbing freezes the game depends
    /// entirely on the Run In Background player setting. With that on, hunger, fatigue, poison,
    /// torch decay and every Critter.Update keep running while you are in another window.
    /// Opening this screen is exactly what Escape does from gameplay, and it sets Time.timeScale
    /// to 0. Simulating an Escape press would not do the same thing: Escape is context sensitive,
    /// and with a panel open it only closes the panel.
    /// TryOpenFromGame needs no guard here. It is idempotent, and CanOpenSaveLoadFromGameplay
    /// already refuses when the menu is up, when the virtual keyboard is showing, when a modal
    /// blocks, on level 9, or when controls are disabled for a conversation or cutscene; it
    /// cannot fire on the main menu either, since it requires PlayerObject.Player and
    /// LevelLoader.sLevelLoader to exist.
    /// Unity delivers OnApplicationFocus(false) at the moment focus is lost, before it suspends
    /// updates, so this works whether or not Run In Background is set.
    /// On coming back you find the save/load screen open and time stopped; Escape resumes.
    /// Not in the editor, where losing focus is how you reach the rest of the editor while the
    /// game runs: pausing there would stop the clock every time you clicked on the inspector.
    /// </remarks>
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && pauseOnFocusLoss && !Application.isEditor)
        {
            TryOpenFromGame();
        }
    }

    public void Update()
    {
        if (!visible)
        {
            if (Gamepad.current?.startButton.wasPressedThisFrame ?? false)
            {
                TryOpenFromGame();
            }
            else
            {
                UpdateQuickSaveFromGameplay();
            }
            return;
        }

        if (quitPending)
        {
            if (!(GameInput.CurrentMouse?.leftButton.isPressed ?? false))
            {
                ConfirmQuitToMainMenu();
            }
            return;
        }

        // The small-window warning over the menu takes its keys, and the press that closes it too: B or Start
        // closed the menu as well, and A's release chose the row behind it (fix 460).
        if (SmallWindowWarning.BlocksInput)
        {
            quitHoldXTime = 0f;
            Menu.DisarmAccept();
            return;
        }

        UpdateQuitHoldX();

        // Esc/Start open via PlayerPanelInput; the same frame the press is still true here, and
        // the menu must not close on it.
        if (!KeyboardActive() && Time.frameCount != visibleShownAtFrame && Menu.Update())
        {
            Hide();
        }
    }

    private void UpdateQuitHoldX()
    {
        if (KeyboardActive())
        {
            quitHoldXTime = 0f;
            return;
        }

        Gamepad gp = Gamepad.current;
        // a page of the menu's own - the music tracks - has X for itself: it toggles a track there (fix 455)
        if (gp == null || Menu.Asking || Menu.ShowsSubPage)
        {
            quitHoldXTime = 0f;
            return;
        }

        if (gp.xButton.isPressed)
        {
            quitHoldXTime += Time.unscaledDeltaTime;
            if (quitHoldXTime >= QuitHoldXDurationSeconds)
            {
                // no question: holding the button this long already says it (the user, 31 October 2026)
                quitHoldXTime = 0f;
                quitPending = true;
            }
        }
        else
        {
            quitHoldXTime = 0f;
        }
    }

    private void ActivatePrimarySaveOrLoad()
    {
        if (pageTab == GameMenu.ETab.Save)
            BeginSaveFlow();
        else if (pageTab == GameMenu.ETab.Load)
            PerformLoadFromSelection();
    }

    private void BeginSaveFlow()
    {
        if (index < 0 || index >= saves.Length)
        {
            return;
        }

        // The first row of the save list makes a new save; any other row overwrites the save on it,
        // and offers its name to be edited. The slot is picked now rather than in the callback:
        // the list can be refreshed while the keyboard is up.
        bool newSave = SaveUIHelper.IsNewSaveRow(saves, index);
        string slotFileName = newSave
            ? (SaveGameManager.sInstance != null ? SaveGameManager.sInstance.NextFreeManualSlot() : "Slot0")
            : saves[index].slotName;
        saveName = newSave ? "" : saves[index].displayName;
#if UNITY_EDITOR
        EditorApplication.ExecuteMenuItem("Window/General/Game");
#endif
        KeyboardGUI.sKeyboard.Show(saveName, (result) =>
        {
            saveName = result;
            if (saveName.Length > 0)
            {
                if (SaveGameManager.sInstance != null)
                {
                    SaveGameManager.sInstance.SaveGameToSlot(slotFileName, saveName);
                    Utils.PlayClip2d(diskSound);
                    RefreshSaves();
                }
            }
            Hide();
            if (saveName.Length > 0 && SaveGameManager.sInstance != null)
                SaveGameManager.sInstance.RequestScreenshotForSlot(slotFileName);
        }, () => { }, new Vector2(Screen.width / 2, Screen.height / 2), "Save name:", false, true,
            MaxSaveNameLength);
    }

    private void PerformLoadFromSelection()
    {
        // The list is whatever is on disk and can be empty, so the row is checked before it is read.
        if (index < 0 || index >= saves.Length || saves[index] == null)
        {
            return;
        }

        if (SaveGameManager.sInstance != null && !string.IsNullOrEmpty(saves[index].slotName))
        {
            SaveGameManager.sInstance.LoadGameFromSlot(saves[index].slotName);
            Utils.PlayClip2d(diskSound);
        }
        Hide();
    }

    private void RefreshSaves()
    {
        // Every save on disk, newest first, plus the new save row when this is the save tab.
        index = SaveUIHelper.RefreshSaves(ref saves, ref slotScreenshots, index, pageTab == GameMenu.ETab.Save);
    }
    
    /// <summary>Moves the selection by <paramref name="delta"/> rows, wrapping as the panel always did.</summary>
    private void MoveSlotSelection(int delta)
    {
        if (saves.Length == 0)
        {
            index = 0;
            return;
        }

        index = ((index + delta) % saves.Length + saves.Length) % saves.Length;
        slotScrollFollowsSelection = true;
    }

    /// <summary>
    /// The quicksave input during play: F5 writes one straight away, the gamepad button has to be
    /// held down for <see cref="QuickSaveHoldSeconds"/>.
    /// </summary>
    /// <remarks>
    /// The hold is timed in unscaled seconds, so it still runs while the game is paused, and it
    /// only fires once per hold: keeping the button down does not write a second save, and the
    /// timer only starts again after it is released.
    /// </remarks>
    private void UpdateQuickSaveFromGameplay()
    {
        if (PlayerInput.QuickSaveKeyPressed())
        {
            TryQuickSaveFromGameplay();
        }

        if (!PlayerInput.QuickSaveButtonHeld())
        {
            quickSaveHoldTime = 0.0f;
            quickSaveDoneThisHold = false;
            return;
        }

        if (quickSaveDoneThisHold)
        {
            return;
        }

        quickSaveHoldTime += Time.unscaledDeltaTime;
        if (quickSaveHoldTime >= QuickSaveHoldSeconds)
        {
            quickSaveDoneThisHold = true;
            TryQuickSaveFromGameplay();
        }
    }

    /// <summary>
    /// Writes a quicksave from gameplay, with a line in the message area so the player knows it
    /// happened: nothing else on screen changes.
    /// </summary>
    private void TryQuickSaveFromGameplay()
    {
        if (!CanOpenSaveLoadFromGameplay() || SaveGameManager.sInstance == null)
        {
            return;
        }

        string slotName = SaveGameManager.sInstance.QuickSave();
        if (!string.IsNullOrEmpty(slotName))
        {
            Utils.PlayClip2d(diskSound);
            Messages.Add("Quicksaved.");
        }
    }

    private Color selectColor = new Color32(255, 213, 64, 255);
    private Color unselectColor = new Color32(187, 123, 1, 255);

    private void DrawQuitHoldChargeRing(Rect ringRect, float fill01)
    {
        if (xButtonChargeRing == null || chargeRingMatInstance == null || Event.current.type != EventType.Repaint)
        {
            return;
        }

        chargeRingMatInstance.SetFloat("_Fill", Mathf.Clamp01(fill01));
        Graphics.DrawTexture(ringRect, xButtonChargeRing, chargeRingMatInstance);
    }

    /// <summary>
    /// Keeps the window inside the list, and the selected row inside the window whenever it was
    /// the selection that moved.
    /// </summary>
    private void ClampSlotScroll(int rows)
    {
        int maxScroll = Mathf.Max(0, saves.Length - rows);
        slotScroll = Mathf.Clamp(slotScroll, 0, maxScroll);

        if (slotScrollFollowsSelection)
        {
            if (index < slotScroll)
            {
                slotScroll = index;
            }
            else if (index >= slotScroll + rows)
            {
                slotScroll = index - rows + 1;
            }

            slotScroll = Mathf.Clamp(slotScroll, 0, maxScroll);
        }
    }

    /// <summary>
    /// SAVE or LOAD has come into view: the list is made again, because the save tab carries one row
    /// the load tab does not, the row that starts a new save.
    /// </summary>
    public void OnTabShown(GameMenu.ETab tab)
    {
        // What was selected stays selected across the move, which is what a player who went to
        // the save tab and then realised he meant to load expects. It is carried by slot name and
        // not by row number: the save list hides the saves the game writes and carries the new save
        // row at the top, so the same number is a different save on the other side. A save that
        // has no row on the other side - a quicksave or an autosave, going from load to save -
        // falls back to the top. The options tabs leave this list and this index alone, so they
        // still name the save that was chosen before them.
        string wasSelected = index >= 0 && index < saves.Length && saves[index] != null
            ? saves[index].slotName : "";

        pageTab = tab;
        index = 0;
        slotScroll = 0;
        slotScrollFollowsSelection = true;
        RefreshSaves();
        index = SaveUIHelper.IndexOfSlot(saves, wasSelected, 0);
    }

    public void UpdatePage(GameMenu.ETab tab, GameMenu.Nav nav)
    {
        if (nav.Down)
        {
            MoveSlotSelection(1);
        }
        else if (nav.Up)
        {
            MoveSlotSelection(-1);
        }
        else if (nav.Wheel != 0)
        {
            // The wheel scrolls the list without moving the selection, the way a list is expected
            // to behave once it is longer than the panel. One row per notch.
            slotScroll = Mathf.Max(0, slotScroll + nav.Wheel);
            slotScrollFollowsSelection = false;
        }
        else if (nav.Accept || rowDoubleClicked)
        {
            rowDoubleClicked = false;
            ActivatePrimarySaveOrLoad();
        }
    }

    /// <summary>The list of saves on the left, with a scroll bar when it is longer than the panel, and the preview of the selected one.</summary>
    public void DrawPage(GameMenu.ETab tab, Rect list, Rect detail)
    {
        float rowsTop = list.y + 8f;
        int rows = Mathf.Max(1, Mathf.FloorToInt((list.height - 16f) / SlotRowHeight));
        ClampSlotScroll(rows);
        bool scrolls = saves.Length > rows;
        Rect bar = new Rect(list.xMax - 8f - ScrollBarWidth, rowsTop, ScrollBarWidth, rows * SlotRowHeight);
        // the rows stop short of the bar, so the selection never lies under it
        float rowsRight = scrolls ? bar.x - 8f : list.xMax - 8f;

        // plain, whatever the source style carries: a name is not markup, and the user found the rows looking bold
        GUIStyle rowStyle = new GUIStyle(text)
        {
            fontSize = Mathf.RoundToInt(SlotFont), alignment = TextAnchor.MiddleLeft, wordWrap = false,
            fontStyle = FontStyle.Normal, richText = false,
        };

        if (saves.Length == 0)
        {
            rowStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            GUI.Label(UiScale.R(list.x + 8f + SlotTextInset, rowsTop, list.width - 16f, SlotRowHeight), "No saved games", UiScale.Style(rowStyle));
        }

        // Rows are clicked to be chosen; hovering does not move the selection, nor the preview.
        for (int row = 0; row < rows; ++row)
        {
            int i = slotScroll + row;
            if (i >= saves.Length)
            {
                break;
            }

            Rect r = new Rect(list.x + 8f, rowsTop + row * SlotRowHeight, rowsRight - list.x - 8f, SlotRowHeight);
            bool isNewSaveRow = SaveUIHelper.IsNewSaveRow(saves, i);
            bool isSelected = index == i;
            if (isSelected)
            {
                Color previous = GUI.color;
                GUI.color = new Color(1f, 0.84f, 0.25f, 0.22f);
                GUI.DrawTexture(UiScale.R(r), Texture2D.whiteTexture);
                GUI.color = previous;
            }

            // Selected rows are bright, the rest dim, and the row that makes a new save is dimmer
            // still: it is an action rather than a save. Written from the left, as on the title screen.
            rowStyle.normal.textColor = isSelected ? Color.white
                : isNewSaveRow ? new Color(0.5f, 0.5f, 0.5f, 0.7f)
                : selectColor;
            Rect textRect = UiScale.R(r.x + SlotTextInset, r.y, r.width - 2f * SlotTextInset, r.height);
            GUIStyle scaled = UiScale.Style(rowStyle);
            int size = scaled.fontSize;
            scaled.fontSize = SaveUIHelper.FitFontSize(scaled, saves[i].displayName, textRect.width, size, Mathf.Max(10, (size * 3) / 4));
            GUI.Label(textRect, Utils.Ellipsize(saves[i].displayName, scaled, textRect.width), scaled);
            scaled.fontSize = size;

            // read before the click is taken: taking it marks the event used
            int clicks = Event.current != null ? Event.current.clickCount : 0;
            if (!Menu.Asking && !draggingThumb && GuiInput.TryConsumeClickInRect(UiScale.R(r)))
            {
                index = i;
                // a double click saves or loads, as Enter does (the user's, 2 November 2026)
                if (clicks >= 2)
                {
                    rowDoubleClicked = true;
                }
            }
        }

        if (scrolls)
        {
            // the menu's scroll bar, one for every list (the user's, 2 November 2026)
            int before = slotScroll;
            slotScroll = Menu.DrawScrollBar(bar, slotScroll, rows, saves.Length, ref draggingThumb, ref thumbGrab);
            if (slotScroll != before || draggingThumb)
            {
                slotScrollFollowsSelection = false;
            }
        }
        else
        {
            draggingThumb = false;
        }

        DrawPreview(detail);
    }

    /// <summary>
    /// The preview of the selected save, centred in its column: the character above, the screenshot
    /// framed, and when it was saved below.
    /// </summary>
    private void DrawPreview(Rect detail)
    {
        if (!SaveUIHelper.PreviewLines(saves, index, out string character, out string when))
        {
            return;
        }
        SaveUIHelper.EnsureScreenshotLoaded(slotScreenshots, saves, index);

        const float pad = 24f;
        const float gap = 14f;
        float lineH = PreviewFont + 10f;
        float shotW = detail.width - 2f * pad;
        float shotH = shotW / ScreenshotAspect;
        float maxShotH = detail.height - 2f * pad - 2f * (lineH + gap);
        if (shotH > maxShotH)
        {
            shotH = maxShotH;
            shotW = shotH * ScreenshotAspect;
        }
        float blockH = lineH + gap + shotH + gap + lineH;
        float top = detail.y + (detail.height - blockH) / 2f;
        float left = detail.x + (detail.width - shotW) / 2f;

        GUIStyle line = new GUIStyle(text) { fontSize = Mathf.RoundToInt(PreviewFont), alignment = TextAnchor.MiddleLeft, wordWrap = false };
        line.normal.textColor = selectColor;
        GUIStyle scaled = UiScale.Style(line);
        Rect characterRect = UiScale.R(left, top, shotW, lineH);
        int size = scaled.fontSize;
        scaled.fontSize = SaveUIHelper.FitFontSize(scaled, character, characterRect.width, size, Mathf.Max(10, size / 2));
        GUI.Label(characterRect, character, scaled);
        scaled.fontSize = size;

        Rect shot = new Rect(left, top + lineH + gap, shotW, shotH);
        Texture2D picture = index < slotScreenshots.Length ? slotScreenshots[index] : null;
        if (picture != null)
        {
            GUI.DrawTexture(UiScale.R(shot), picture, ScaleMode.StretchToFill, false);
        }
        else
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(UiScale.R(shot), Texture2D.whiteTexture);
            GUI.color = previous;
        }
        DrawFrame(new Rect(shot.x - 2f, shot.y - 2f, shot.width + 4f, shot.height + 4f), unselectColor);

        GUI.Label(UiScale.R(left, shot.yMax + gap, shotW, lineH), when, scaled);
    }

    private static void DrawFrame(Rect refRect, Color color)
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

    public Hint[] PageHints(GameMenu.ETab tab, bool pad)
    {
        string what = tab == GameMenu.ETab.Save ? "save" : "load";
        return pad
            ? new[]
            {
                new Hint(what, HintKey.Of(aButton)),
                new Hint("tab", HintKey.PadButton("LB"), HintKey.PadButton("RB")),
                new Hint("close", HintKey.Of(bButton)),
            }
            : new[]
            {
                new Hint(what, HintKey.Keyboard("Enter")),
                new Hint("tab", HintKey.Keyboard("Tab")),
                new Hint("close", HintKey.Of(escapeKey)),
            };
    }

    public string[] PageButtons(GameMenu.ETab tab)
    {
        return new[] { tab == GameMenu.ETab.Save ? "Save" : "Load" };
    }

    public bool PageButtonEnabled(GameMenu.ETab tab, int button)
    {
        return !KeyboardActive() && index >= 0 && index < saves.Length
            && (tab == GameMenu.ETab.Save || HasSelectedSaveSlotContent());
    }

    public void PressPageButton(GameMenu.ETab tab, int button)
    {
        ActivatePrimarySaveOrLoad();
    }

    /// <summary>The way back to the title screen, at the right end of the row of buttons on every tab.</summary>
    public string EndButton => "Main Menu";

    public void PressEndButton()
    {
        AskQuitToMainMenu();
    }

    /// <summary>
    /// The music tracks, from the row of AUDIO: the game's music is held while the page is open, so
    /// that a track listened to is not cut by the next, and given back when it closes.
    /// </summary>
    public IGameMenuSubPage OpenPage(string key)
    {
        if (key != GameOptions.MusicPageKey)
        {
            return null;
        }
        Music.Listening(true);
        return new MusicSetupPage(Menu, this, () => Music.Listening(false));
    }

    /// <summary>On the pad the button is X held: its picture, in the ring that fills while it is held.</summary>
    public void DrawEndButtonPadIcon(Rect icon)
    {
        const float iconShare = 24f / 36f;
        DrawQuitHoldChargeRing(UiScale.R(icon), Mathf.Clamp01(quitHoldXTime / QuitHoldXDurationSeconds));
        if (xButton != null)
        {
            float size = icon.width * iconShare;
            GUI.DrawTexture(UiScale.R(icon.center.x - size / 2f, icon.center.y - size / 2f, size, size), xButton);
        }
    }

    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.SaveLoad;
        if (!visible)
        {
            return;
        }

        Menu.OnGUI();
    }

    private bool HasSelectedSaveSlotContent()
    {
        return index >= 0
            && index < saves.Length
            && !string.IsNullOrEmpty(saves[index].slotName);
    }
}
