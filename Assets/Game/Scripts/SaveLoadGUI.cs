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

    // The list of saves of SAVE and LOAD, one page that is shown as either.
    private SaveListPage page;
    private SaveListPage Page => page ??= new SaveListPage(() => Menu, text, diskSound);

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

    public bool TryOpenFromGame() => TryOpenFromGame(GameMenu.ETab.Save, null);

    /// <summary>Opens the menu from play on a tab, and on one of its sections if given: F1 opens CONTROLS on a map.</summary>
    public bool TryOpenFromGame(GameMenu.ETab tab, EOptionSection? section)
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
        Page.SelectTop();
        ResetQuitToMainMenuState();
        PlayerObject.DisableControls(EControlMask.SaveLoad, true);
        Time.timeScale = 0.0f;
        if (section.HasValue)
        {
            Menu.Open(tab, section.Value);
        }
        else
        {
            Menu.Open(tab);
        }
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
            else if (GameInput.KeyPressedThisFrame(GameActions.KeysWorld.Controls))
            {
                TryOpenFromGame(GameMenu.ETab.Controls, ControlsMap.ForLastDevice());
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
        if (gp == null || Menu.Asking || Menu.PadHoldsPaused || Menu.ShowsSubPage)
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
        SaveGameManager.SaveSlotInfo selected = Page.Selected;
        if (selected == null)
        {
            return;
        }

        // The first row of the save list makes a new save; any other row overwrites the save on it,
        // and offers its name to be edited. The slot is picked now rather than in the callback:
        // the list can be refreshed while the keyboard is up.
        bool newSave = Page.SelectedIsNewSave;
        string slotFileName = newSave
            ? (SaveGameManager.sInstance != null ? SaveGameManager.sInstance.NextFreeManualSlot() : "Slot0")
            : selected.slotName;
        saveName = newSave ? "" : selected.displayName;
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
                    Page.Refresh();
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
        if (!Page.SelectedIsSave)
        {
            return;
        }

        if (SaveGameManager.sInstance != null)
        {
            SaveGameManager.sInstance.LoadGameFromSlot(Page.Selected.slotName);
            Utils.PlayClip2d(diskSound);
        }
        Hide();
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
    /// SAVE or LOAD has come into view: the list is made again, because the save tab carries one row
    /// the load tab does not, the row that starts a new save. The options tabs leave the list alone,
    /// so it still names the save that was chosen before them.
    /// </summary>
    public void OnTabShown(GameMenu.ETab tab)
    {
        pageTab = tab;
        Page.Show(tab == GameMenu.ETab.Save);
    }

    public void UpdatePage(GameMenu.ETab tab, GameMenu.Nav nav)
    {
        if (Page.Update(nav))
        {
            ActivatePrimarySaveOrLoad();
        }
    }

    /// <summary>The list of saves on the left and the preview of the selected one.</summary>
    public void DrawPage(GameMenu.ETab tab, Rect list, Rect detail)
    {
        Page.Draw(list, detail);
    }

    public Hint[] PageHints(GameMenu.ETab tab, bool pad)
    {
        bool saving = tab == GameMenu.ETab.Save;
        var hints = new System.Collections.Generic.List<Hint>
        {
            pad ? new Hint(saving ? "save" : "load", HintKey.Of(aButton)) : new Hint(saving ? "save" : "load", HintKey.Keyboard("Enter")),
        };
        hints.Add(pad ? new Hint("delete", HintKey.Of(yButton)) : new Hint("delete", HintKey.Keyboard("Del")));
        hints.Add(pad ? new Hint("tab", HintKey.PadButton("LB"), HintKey.PadButton("RB")) : new Hint("tab", HintKey.Keyboard("Tab")));
        hints.Add(pad ? new Hint("close", HintKey.Of(bButton)) : new Hint("close", HintKey.Of(escapeKey)));
        return hints.ToArray();
    }

    // Delete on LOAD too, first in the row (the user's rule of 7 November 2026): the autosaves are listed only there, so they
    // could not be deleted at all
    public string[] PageButtons(GameMenu.ETab tab)
    {
        return new[] { "Delete", tab == GameMenu.ETab.Save ? "Save" : "Load" };
    }

    public bool PageButtonEnabled(GameMenu.ETab tab, int button)
    {
        if (KeyboardActive())
        {
            return false;
        }
        // Save on any row, the new save row included; Load and Delete on a save
        return button == 1 && tab == GameMenu.ETab.Save ? Page.Selected != null : Page.SelectedIsSave;
    }

    public void PressPageButton(GameMenu.ETab tab, int button)
    {
        if (button == 0)
        {
            Page.AskDelete();
        }
        else
        {
            ActivatePrimarySaveOrLoad();
        }
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
            GUI.DrawTexture(UiScale.R(icon.center.x - size / 2f, icon.center.y - size / 2f, size, size), PadGlyphs.Face(xButton));
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
}
