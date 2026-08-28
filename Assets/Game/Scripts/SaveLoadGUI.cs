using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Runs after <see cref="PlayerPanelInput"/> so opening from Esc runs before we evaluate Esc-to-close here.
/// </summary>
[DefaultExecutionOrder(50)]
public class SaveLoadGUI : MonoBehaviour
{
    public Texture2D background;
    public GUIStyle title;
    public GUIStyle text;
    public GUIStyle optionsDescriptionText;
    public GUIStyle optionsFooterText;
    public Texture2D aButton;
    public Texture2D bButton;
    public Texture2D xButton;
    public Texture2D escapeKey;
    public Texture2D buttonOutline;
    public Texture2D dPadLeftRight;
    public Texture2D xButtonChargeRing;
    public Material xButtonChargeRingMaterial;

    public AudioClip diskSound;

    private bool visible;
    private int index;
    private Material chargeRingMatInstance;

    private enum ETab
    {
        Save,
        Load,
        Options
    }

    private ETab tab;

    private bool quitConfirmOpen;
    private bool quitConfirmInitialButtonsReleased;
    private float quitHoldXTime;
    private const float QuitHoldXDurationSeconds = 0.85f;

    private enum DeferredQuitMouseAction
    {
        None,
        Cancel,
        Confirm
    }

    private DeferredQuitMouseAction deferredQuitMouseAction;
    private int executeDeferredQuitMouseOnFrame = -1;
    private Rect quitConfirmCancelHitRect;
    private Rect quitConfirmConfirmHitRect;
    
    // Options settings
    private float musicVolume = 0.7f;
    private string soundFont = "MT32";
    private float effectsVolume = 1.0f;
    private float mouseLookSpeed = 1.0f;
    private bool useKeyAutomatically = true;
    private bool autoJump = false;
    private bool invertLook = false;
    
    // Options tab navigation
    private int optionsIndex = 0;
    private const int OPTIONS_ITEM_COUNT = 7; // music vol, soundfont, effects vol, mouse look speed, 3 checkboxes
    private const float MinMouseLookSpeed = 0.8f;
    private const float MaxMouseLookSpeed = 2.2f;
    private static readonly string[] OptionsDescriptions =
    {
        "Adjusts the volume of in-game music.",
        "Chooses the synthesizer sound font used for music. Different fonts emulate classic soundcards such as the MT-32.",
        "Adjusts the volume of sound effects.",
        "Controls how quickly the camera turns when using the mouse. Does not affect gamepad look.",
        "Reverses up and down when looking with the mouse or gamepad.",
        "Enabled: a locked door unlocks automatically if you have the correct key and use the door. Disabled: you must manually unlock a door by finding the correct key in your inventory and using the key.",
        "Enabled: the player jumps automatically as close to the edge as possible when running toward a gap and holding down the jump button or key.",
    };
    private const string OptionsApplyFooter = "Changes are only saved when you select Apply All.";
    private string[] availableSoundFonts = new string[0];
    private int currentSoundFontIndex = 0;

    private const int MAX_SAVE_SLOTS = 10;
    private SaveGameManager.SaveSlotInfo[] saves = new SaveGameManager.SaveSlotInfo[MAX_SAVE_SLOTS];
    private SaveGameManager.SaveSlotInfo[] actualSaves = new SaveGameManager.SaveSlotInfo[0];
    private Texture2D[] slotScreenshots = new Texture2D[MAX_SAVE_SLOTS];
    
    private string saveName;

    /// <summary>Frame when the panel became visible; Esc opens via <see cref="PlayerPanelInput"/> then this Update sees the same Esc press — ignore Esc-to-close on that frame.</summary>
    private int visibleShownAtFrame = -1;

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
        visible = false;
        visibleShownAtFrame = -1;
        ResetQuitToMainMenuState();
        PlayerObject.DisableControls(EControlMask.SaveLoad, false);
        Time.timeScale = 1.0f;
    }

    private void ResetQuitToMainMenuState()
    {
        quitConfirmOpen = false;
        quitConfirmInitialButtonsReleased = false;
        quitHoldXTime = 0f;
        deferredQuitMouseAction = DeferredQuitMouseAction.None;
        executeDeferredQuitMouseOnFrame = -1;
    }

    private void OpenQuitConfirm()
    {
        quitConfirmOpen = true;
        quitConfirmInitialButtonsReleased = false;
        quitHoldXTime = 0f;
        deferredQuitMouseAction = DeferredQuitMouseAction.None;
        executeDeferredQuitMouseOnFrame = -1;
    }

    private void CancelQuitConfirm()
    {
        quitConfirmOpen = false;
        quitConfirmInitialButtonsReleased = false;
        deferredQuitMouseAction = DeferredQuitMouseAction.None;
        executeDeferredQuitMouseOnFrame = -1;
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
        tab = ETab.Save;
        ResetQuitToMainMenuState();
        PlayerObject.DisableControls(EControlMask.SaveLoad, true);
        Time.timeScale = 0.0f;
        RefreshSaves();
        LoadOptionsSettings();
        return true;
    }

    private bool KeyboardActive()
    {
        return KeyboardGUI.sKeyboard != null && KeyboardGUI.sKeyboard.IsVisible();
    }

    public void Update()
    {
        if (!visible)
        {
            if (Gamepad.current?.startButton.wasPressedThisFrame ?? false)
            {
                TryOpenFromGame();
            }
            return;
        }

        if (quitConfirmOpen)
        {
            UpdateQuitConfirm();
            return;
        }

        UpdateQuitHoldX();

        if (!KeyboardActive())
        {
            bool esc = GameInput.EscapePressedThisFrame();
            // Esc/Start open via PlayerPanelInput; same frame the press is still true here — do not close immediately.
            bool escDismiss = esc && Time.frameCount != visibleShownAtFrame;
            bool startDismiss = (Gamepad.current?.startButton.wasPressedThisFrame ?? false)
                && Time.frameCount != visibleShownAtFrame;
            if (escDismiss
                || startDismiss
                || (Gamepad.current?.bButton.wasPressedThisFrame ?? false))
            {
                Hide();
                return;
            }

            if (Gamepad.current?.leftShoulder.wasPressedThisFrame ?? false)
            {
                // Cycle backwards: Save -> Options -> Load
                tab = tab == ETab.Save ? ETab.Options : (tab == ETab.Options ? ETab.Load : ETab.Save);
            }
            else if (Gamepad.current?.rightShoulder.wasPressedThisFrame ?? false)
            {
                // Cycle forwards: Save -> Load -> Options
                tab = tab == ETab.Save ? ETab.Load : (tab == ETab.Load ? ETab.Options : ETab.Save);
            }
            else if (Gamepad.current?.dpad.down.wasPressedThisFrame ?? false)
            {
                if (tab == ETab.Options)
                {
                    optionsIndex = (optionsIndex + 1) % OPTIONS_ITEM_COUNT;
                }
                else
                {
                    index = (index + 1) % saves.Length;
                }
            }
            else if (Gamepad.current?.dpad.up.wasPressedThisFrame ?? false)
            {
                if (tab == ETab.Options)
                {
                    optionsIndex = (optionsIndex + OPTIONS_ITEM_COUNT - 1) % OPTIONS_ITEM_COUNT;
                }
                else
                {
                    index = (index + saves.Length - 1) % saves.Length;
                }
            }
            else if (tab == ETab.Options && ((Gamepad.current?.dpad.left.wasPressedThisFrame ?? false) || (Gamepad.current?.leftStick.left.wasPressedThisFrame ?? false)))
            {
                // Handle left input on Options tab
                HandleOptionsLeftRight(-1);
            }
            else if (tab == ETab.Options && ((Gamepad.current?.dpad.right.wasPressedThisFrame ?? false) || (Gamepad.current?.leftStick.right.wasPressedThisFrame ?? false)))
            {
                // Handle right input on Options tab
                HandleOptionsLeftRight(1);
            }
            else if (Gamepad.current?.aButton.wasReleasedThisFrame ?? false)
            {
                if (tab == ETab.Options)
                {
                    ApplyOptionsSettings();
                }
                else
                {
                    ActivatePrimarySaveOrLoad();
                }
            }
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
        if (gp == null)
        {
            quitHoldXTime = 0f;
            return;
        }

        if (gp.xButton.isPressed)
        {
            quitHoldXTime += Time.unscaledDeltaTime;
            if (quitHoldXTime >= QuitHoldXDurationSeconds)
            {
                OpenQuitConfirm();
            }
        }
        else
        {
            quitHoldXTime = 0f;
        }
    }

    private void UpdateQuitConfirm()
    {
        if (deferredQuitMouseAction != DeferredQuitMouseAction.None)
        {
            Mouse mouse = GameInput.CurrentMouse;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                executeDeferredQuitMouseOnFrame = -1;
            }
            else
            {
                if (executeDeferredQuitMouseOnFrame < 0)
                {
                    executeDeferredQuitMouseOnFrame = Time.frameCount + 1;
                }
                else if (Time.frameCount >= executeDeferredQuitMouseOnFrame)
                {
                    DeferredQuitMouseAction action = deferredQuitMouseAction;
                    deferredQuitMouseAction = DeferredQuitMouseAction.None;
                    executeDeferredQuitMouseOnFrame = -1;
                    if (action == DeferredQuitMouseAction.Confirm)
                    {
                        ConfirmQuitToMainMenu();
                    }
                    else
                    {
                        CancelQuitConfirm();
                    }
                }
            }
            return;
        }

        if (!quitConfirmInitialButtonsReleased)
        {
            Gamepad gp = GameInput.CurrentGamepad;
            Mouse mouse = GameInput.CurrentMouse;
            if (gp != null)
            {
                if (gp.aButton.isPressed || gp.aButton.wasReleasedThisFrame
                    || gp.bButton.isPressed || gp.bButton.wasReleasedThisFrame
                    || gp.xButton.isPressed || gp.xButton.wasReleasedThisFrame)
                {
                    return;
                }
            }
            // Ignore the mouse-down that opened this dialog (Main Menu click) until release.
            if (mouse != null && mouse.leftButton.isPressed)
            {
                return;
            }
            quitConfirmInitialButtonsReleased = true;
        }

        // Mouse hits use Input System + stored rects so they work even if OnGUI already used the event.
        Mouse m = GameInput.CurrentMouse;
        if (m != null && m.leftButton.wasPressedThisFrame)
        {
            Vector2 guiMouse = GuiInput.MousePositionGuiSpace;
            if (quitConfirmCancelHitRect.Contains(guiMouse))
            {
                deferredQuitMouseAction = DeferredQuitMouseAction.Cancel;
                executeDeferredQuitMouseOnFrame = -1;
                return;
            }
            if (quitConfirmConfirmHitRect.Contains(guiMouse))
            {
                deferredQuitMouseAction = DeferredQuitMouseAction.Confirm;
                executeDeferredQuitMouseOnFrame = -1;
                return;
            }
        }

        if (GameInput.CurrentKeyboard != null
            && (GameInput.CurrentKeyboard.enterKey.wasPressedThisFrame
                || GameInput.CurrentKeyboard.numpadEnterKey.wasPressedThisFrame))
        {
            ConfirmQuitToMainMenu();
            return;
        }
        if (GameInput.EscapePressedThisFrame()
            || (Gamepad.current?.bButton.wasPressedThisFrame ?? false))
        {
            CancelQuitConfirm();
            return;
        }
        if (Gamepad.current?.aButton.wasPressedThisFrame ?? false)
        {
            ConfirmQuitToMainMenu();
        }
    }

    private void ActivatePrimarySaveOrLoad()
    {
        if (tab == ETab.Save)
            BeginSaveFlow();
        else if (tab == ETab.Load)
            PerformLoadFromSelection();
    }

    private void BeginSaveFlow()
    {
        saveName = saves[index].displayName == "Empty" ? "" : saves[index].displayName;
#if UNITY_EDITOR
        EditorApplication.ExecuteMenuItem("Window/General/Game");
#endif
        KeyboardGUI.sKeyboard.Show(saveName, (result) =>
        {
            saveName = result;
            string slotFileName = $"Slot{index}";
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
        }, () => { }, new Vector2(Screen.width / 2, Screen.height / 2), "Save name:", false, true);
    }

    private void PerformLoadFromSelection()
    {
        if (SaveGameManager.sInstance != null && !string.IsNullOrEmpty(saves[index].slotName))
        {
            SaveGameManager.sInstance.LoadGameFromSlot(saves[index].slotName);
            Utils.PlayClip2d(diskSound);
        }
        Hide();
    }

    private void RefreshSaves()
    {
        // Use shared helper to populate save slot info and screenshots, and clamp index
        index = SaveUIHelper.RefreshSaves(saves, ref actualSaves, slotScreenshots, index);
    }
    
    private Color selectColor = new Color32(255, 213, 64, 255);
    private Color unselectColor = new Color32(187, 123, 1, 255);

    private static void GetTabWidths(float w, out float saveWidth, out float loadWidth, out float optionsWidth)
    {
        saveWidth = w * 0.30f;
        loadWidth = w * 0.30f;
        optionsWidth = w * 0.40f;
    }

    private Rect TabSaveHitRect(float x, float y, float w)
    {
        GetTabWidths(w, out float sw, out _, out _);
        return new Rect(x + 5, y + 10, sw, title.fontSize + 10);
    }

    private Rect TabLoadHitRect(float x, float y, float w)
    {
        GetTabWidths(w, out float sw, out float lw, out _);
        return new Rect(x + sw, y + 10, lw, title.fontSize + 10);
    }

    private Rect TabOptionsHitRect(float x, float y, float w)
    {
        GetTabWidths(w, out float sw, out float lw, out float ow);
        return new Rect(x - 5 + sw + lw, y + 10, ow, title.fontSize + 10);
    }

    private Rect SlotRowHitRect(float x, float y, float w, int i)
    {
        float rowStep = text.fontSize + 2;
        float top = y + title.fontSize + 25 + rowStep * i;
        return new Rect(x, top, w, text.fontSize + 4);
    }

    private Rect OptionsRowHitRect(float x, float y, float w, float h, int rowIndex)
    {
        float lineHeight = text.fontSize + 15;
        float contentY = y + title.fontSize + 25 + lineHeight * rowIndex;
        return new Rect(x + 34, contentY - 2, w - 68, lineHeight + 4);
    }

    private Rect SaveLoadPrimaryHitRect(float x, float y, float w, float h)
    {
        int bw = text.fontSize;
        float bottomY = y + h - 22 - bw;
        return new Rect(x + w / 2f - 90f, bottomY - 2, 180f, bw + 14);
    }

    private void GetOptionsBottomLayout(float x, float y, float w, float h, out float leftControlX, out float rightControlX, out int bw, out float bottomY)
    {
        bw = text.fontSize;
        bottomY = y + h - 20 - bw;
        leftControlX = x + w / 4f - 80f;
        rightControlX = x + w / 2f + 20f;
    }

    /// <summary>Matches centered &quot;Apply All&quot; for mouse layout in <see cref="DrawOptionsTab"/>.</summary>
    private Rect OptionsApplyHitRect(float x, float y, float w, float h)
    {
        GetOptionsBottomLayout(x, y, w, h, out _, out _, out int bw, out float bottomY);
        return new Rect(x + w / 2f - 110f, bottomY - 4, 220, bw + 14);
    }

    private void UpdateMouseHover(float x, float y, float w, float h)
    {
        if (KeyboardActive())
            return;
        if (GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
            return;
        Vector2 mp = GuiInput.MousePositionGuiSpace;
        // Save/Load list: selection is click-only (index); hover must not change slot or the right preview panel.
        if (tab == ETab.Options)
        {
            for (int i = 0; i < OPTIONS_ITEM_COUNT; i++)
            {
                if (OptionsRowHitRect(x, y, w, h, i).Contains(mp))
                    optionsIndex = i;
            }
        }
    }

    private void ProcessMouseClicks(float x, float y, float w, float h)
    {
        if (quitConfirmOpen)
        {
            return;
        }
        if (KeyboardActive())
        {
            return;
        }
        if (GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
        {
            return;
        }

        if (GuiInput.TryConsumeClickInRect(TabSaveHitRect(x, y, w)))
        {
            tab = ETab.Save;
            return;
        }
        if (GuiInput.TryConsumeClickInRect(TabLoadHitRect(x, y, w)))
        {
            tab = ETab.Load;
            return;
        }
        if (GuiInput.TryConsumeClickInRect(TabOptionsHitRect(x, y, w)))
        {
            tab = ETab.Options;
            return;
        }

        if (tab == ETab.Save || tab == ETab.Load)
        {
            for (int i = 0; i < saves.Length; i++)
            {
                if (GuiInput.TryConsumeClickInRect(SlotRowHitRect(x, y, w, i)))
                {
                    index = i;
                    return;
                }
            }
            if (GuiInput.TryConsumeClickInRect(SaveLoadPrimaryHitRect(x, y, w, h)))
            {
                ActivatePrimarySaveOrLoad();
                return;
            }
        }
        else if (tab == ETab.Options)
        {
            // Click sound-card row to cycle fonts (ProcessMouseClicks is mouse-only).
            if (GuiInput.TryConsumeClickInRect(OptionsRowHitRect(x, y, w, h, 1)))
            {
                optionsIndex = 1;
                CycleSoundFontWithDirection(1);
                return;
            }
            if (GuiInput.TryConsumeClickInRect(OptionsApplyHitRect(x, y, w, h)))
            {
                ApplyOptionsSettings();
                return;
            }
        }
    }

    private Rect QuitToMainMenuButtonRect()
    {
        float bw = 200f;
        float bh = 36f;
        float pad = 16f;
        Rect safe = Screen.safeArea;
        return new Rect(safe.xMax - bw - pad, safe.yMin + pad, bw, bh);
    }

    private Rect QuitHoldHintRect(float contentWidth, float contentHeight)
    {
        float pad = 16f;
        Rect safe = Screen.safeArea;
        float bw = Mathf.Min(contentWidth, safe.width - pad * 2f);
        return new Rect(safe.xMax - bw - pad, safe.yMin + pad, bw, contentHeight);
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

    private void DrawQuitToMainMenuControls()
    {
        if (quitConfirmOpen)
        {
            return;
        }

        bool mouseUi = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;
        TextAnchor prevAlign = text.alignment;
        Color prevColor = text.normal.textColor;
        if (mouseUi)
        {
            Rect r = QuitToMainMenuButtonRect();
            GuiInput.RegisterBlockingRect(r);
            if (buttonOutline != null)
            {
                GUI.DrawTexture(r, buttonOutline, ScaleMode.StretchToFill, true);
            }
            text.alignment = TextAnchor.MiddleCenter;
            text.normal.textColor = selectColor;
            GUI.Label(r, "Main Menu", text);
            if (GuiInput.TryConsumeClickInRect(r))
            {
                OpenQuitConfirm();
            }
        }
        else
        {
            const float ringSize = 36f;
            const float iconSize = 24f;
            const float gap = 10f;
            text.alignment = TextAnchor.MiddleLeft;
            text.normal.textColor = selectColor;
            Vector2 labelSize = text.CalcSize(new GUIContent("Hold — Main Menu"));
            float contentW = ringSize + gap + labelSize.x + 8f;
            float contentH = Mathf.Max(ringSize, labelSize.y + 8f);
            Rect r = QuitHoldHintRect(contentW, contentH);
            GuiInput.RegisterBlockingRect(r);

            float fill = Mathf.Clamp01(quitHoldXTime / QuitHoldXDurationSeconds);
            Rect ringRect = new Rect(r.x, r.y + (r.height - ringSize) * 0.5f, ringSize, ringSize);
            DrawQuitHoldChargeRing(ringRect, fill);

            if (xButton != null)
            {
                float ix = ringRect.x + (ringSize - iconSize) * 0.5f;
                float iy = ringRect.y + (ringSize - iconSize) * 0.5f;
                GUI.DrawTexture(new Rect(ix, iy, iconSize, iconSize), xButton);
            }

            GUI.Label(
                new Rect(ringRect.xMax + gap, r.y, r.xMax - (ringRect.xMax + gap), r.height),
                "Hold — Main Menu",
                text);
        }
        text.alignment = prevAlign;
        text.normal.textColor = prevColor;
    }

    private void DrawQuitConfirmDialog()
    {
        if (!quitConfirmOpen)
        {
            return;
        }

        GUI.depth = (int)EGUIDepth.RepairDialog;

        Texture2D tex = DataLoader.sDataLoader != null ? DataLoader.sDataLoader.invTex[6] : background;
        if (tex == null)
        {
            tex = background;
        }
        float w = 5.5f * tex.width;
        float h = 4.6f * tex.height;
        Rect r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GuiInput.RegisterBlockingRect(r);
        GUI.DrawTexture(r, tex);

        TextAnchor prevTitleAlign = title.alignment;
        Color prevTitleColor = title.normal.textColor;
        TextAnchor prevTextAlign = text.alignment;
        Color prevTextColor = text.normal.textColor;

        const float padX = 40f;
        const float titleY = 10f;
        const float bodyY = 60f;

        int prevTitleFontSize = title.fontSize;
        title.fontSize = Mathf.Min(title.fontSize, 40);
        title.alignment = TextAnchor.UpperCenter;
        title.normal.textColor = selectColor;
        GUI.Label(new Rect(r.x + padX, r.y + titleY, w - padX * 2f, 32f), "Return to main menu?", title);
        title.fontSize = prevTitleFontSize;

        text.alignment = TextAnchor.UpperCenter;
        text.normal.textColor = selectColor;
        GUI.Label(new Rect(r.x + padX, r.y + bodyY, w - padX * 2f, 28f), "Unsaved progress will be lost.", text);

        // One large pair of hit targets for both mouse and gamepad (icons change with last device).
        float btnH = 44f;
        float cancelW = 180f;
        float quitW = 150f;
        float yy = r.y + r.height - 24f - btnH;
        quitConfirmCancelHitRect = new Rect(r.x + padX, yy, cancelW, btnH);
        quitConfirmConfirmHitRect = new Rect(r.x + r.width - padX - quitW, yy, quitW, btnH);

        bool mouseUi = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;
        if (mouseUi && buttonOutline != null)
        {
            GUI.DrawTexture(quitConfirmCancelHitRect, buttonOutline, ScaleMode.StretchToFill, true);
            GUI.DrawTexture(quitConfirmConfirmHitRect, buttonOutline, ScaleMode.StretchToFill, true);
        }

        const float icon = 24f;
        float iconY = yy + (btnH - icon) * 0.5f;

        text.alignment = TextAnchor.MiddleLeft;
        text.normal.textColor = selectColor;
        if (mouseUi)
        {
            if (escapeKey != null)
            {
                GUI.DrawTexture(new Rect(quitConfirmCancelHitRect.x + 14f, iconY, icon, icon), escapeKey);
            }
            GUI.Label(
                new Rect(quitConfirmCancelHitRect.x + 14f + icon + 8f, yy, cancelW - 50f, btnH),
                "Cancel",
                text);
            text.alignment = TextAnchor.MiddleCenter;
            GUI.Label(quitConfirmConfirmHitRect, "Quit", text);
        }
        else
        {
            if (bButton != null)
            {
                GUI.DrawTexture(new Rect(quitConfirmCancelHitRect.x + 14f, iconY, icon, icon), bButton);
            }
            GUI.Label(
                new Rect(quitConfirmCancelHitRect.x + 14f + icon + 8f, yy, cancelW - 50f, btnH),
                "Cancel",
                text);
            if (aButton != null)
            {
                GUI.DrawTexture(new Rect(quitConfirmConfirmHitRect.x + 14f, iconY, icon, icon), aButton);
            }
            GUI.Label(
                new Rect(quitConfirmConfirmHitRect.x + 14f + icon + 8f, yy, quitW - 50f, btnH),
                "Quit",
                text);
        }

        // OnGUI click path (backup); UpdateQuitConfirm also hit-tests these rects.
        if (deferredQuitMouseAction == DeferredQuitMouseAction.None)
        {
            if (GuiInput.TryConsumeClickInRect(quitConfirmCancelHitRect))
            {
                deferredQuitMouseAction = DeferredQuitMouseAction.Cancel;
                executeDeferredQuitMouseOnFrame = -1;
            }
            else if (GuiInput.TryConsumeClickInRect(quitConfirmConfirmHitRect))
            {
                deferredQuitMouseAction = DeferredQuitMouseAction.Confirm;
                executeDeferredQuitMouseOnFrame = -1;
            }
        }

        title.alignment = prevTitleAlign;
        title.normal.textColor = prevTitleColor;
        text.alignment = prevTextAlign;
        text.normal.textColor = prevTextColor;

        GuiInput.TryConsumeClickInPanel(r);
    }

    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.SaveLoad;

        if (visible)
        {
            // Block world look/use for the whole screen while this overlay is open.
            Rect fullScreenRect = Screen.safeArea;
            GuiInput.RegisterBlockingRect(fullScreenRect);

            float w = 5.0f * background.width;
            float h = 4.0f * background.height;
            float x = Screen.width / 3 - w / 2;
            float y = Screen.height / 2 - h / 2;
            Rect panelRect = new Rect(x, y, w, h);
            GuiInput.RegisterBlockingRect(panelRect);

            Rect sidePanelRect = SaveUIHelper.GetSaveSlotDetailPanelRect(x + w + 10f, y, background);
            if (background != null)
            {
                GUI.DrawTexture(sidePanelRect, background, ScaleMode.StretchToFill, true);
                GuiInput.RegisterBlockingRect(sidePanelRect);
            }

            GUI.DrawTexture(panelRect, background);

            GetTabWidths(w, out float saveWidth, out float loadWidth, out float optionsWidth);

            title.alignment = TextAnchor.MiddleCenter;
            title.normal.textColor = tab == ETab.Save ? Color.white : unselectColor;
            GUI.Label(new Rect(x + 5, y + 15, saveWidth, title.fontSize), "Save", title);
        
            title.normal.textColor = tab == ETab.Load ? Color.white : unselectColor;
            GUI.Label(new Rect(x + saveWidth, y + 15, loadWidth, title.fontSize), "Load", title);
            
            title.normal.textColor = tab == ETab.Options ? Color.white : unselectColor;
            GUI.Label(new Rect(x - 5 + saveWidth + loadWidth, y + 15, optionsWidth, title.fontSize), "Options", title);

            if (!quitConfirmOpen)
            {
                UpdateMouseHover(x, y, w, h);
            }

            if (tab == ETab.Options)
            {
                DrawOptionsTab(x, y, w, h);
                DrawOptionsDetailPanel(sidePanelRect);
            }
            else
            {
                DrawSaveLoadTab(x, y, w, h);
                if (HasSelectedSaveSlotContent())
                {
                    SaveUIHelper.DrawSaveSlotDetailContent(
                        sidePanelRect,
                        text,
                        selectColor,
                        saves,
                        slotScreenshots,
                        index);
                }
            }

            if (quitConfirmOpen)
            {
                // Handle confirm clicks before full-screen consume eats the MouseDown event.
                DrawQuitConfirmDialog();
                GuiInput.TryConsumeClickInPanel(fullScreenRect);
            }
            else
            {
                ProcessMouseClicks(x, y, w, h);
                DrawQuitToMainMenuControls();
                GuiInput.TryConsumeClickInPanel(panelRect);
                if (background != null)
                {
                    GuiInput.TryConsumeClickInPanel(sidePanelRect);
                }
                GuiInput.TryConsumeClickInPanel(fullScreenRect);
            }
        }
    }

    private bool HasSelectedSaveSlotContent()
    {
        return index >= 0
            && index < saves.Length
            && !string.IsNullOrEmpty(saves[index].slotName);
    }

    private void DrawOptionsDetailPanel(Rect panelRect)
    {
        if (panelRect.width <= 0f)
        {
            return;
        }

        int idx = Mathf.Clamp(optionsIndex, 0, OPTIONS_ITEM_COUNT - 1);

        const float inset = 48f;
        const float footerHeight = 56f;
        float contentWidth = panelRect.width - inset * 2f;

        GUI.Label(
            new Rect(panelRect.x + inset, panelRect.y + 64f, contentWidth, panelRect.height - 64f - footerHeight),
            OptionsDescriptions[idx],
            optionsDescriptionText);

        GUI.Label(
            new Rect(panelRect.x + inset, panelRect.yMax - footerHeight, contentWidth, footerHeight - 8f),
            OptionsApplyFooter,
            optionsFooterText);
    }

    private void DrawSaveLoadTab(float x, float y, float w, float h)
    {
        for (int i = 0; i < saves.Length; ++i)
        {
            text.alignment = TextAnchor.UpperCenter;

            bool isEmpty = string.IsNullOrEmpty(saves[i].slotName);
            bool isSelected = index == i;

            // Color logic: selected items are bright, unselected are dim, empty slots are greyed out
            if (isSelected)
            {
                text.normal.textColor = selectColor;
            }
            else if (isEmpty)
            {
                text.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 0.7f); // Grayed out
            }
            else
            {
                text.normal.textColor = unselectColor;
            }

            Rect r = SlotRowHitRect(x, y, w, i);
            r.height = text.fontSize;

            // Show save name or "Empty"
            string line = isEmpty ? "Empty" : saves[i].displayName;
            GUI.Label(r, line, text);
        }
        
        if (!KeyboardActive())
        {
            int bw = text.fontSize;
            text.alignment = TextAnchor.MiddleLeft;
            text.normal.textColor = selectColor;
            float bottomY = y + h - 20 - bw;
            float centerX = x + w / 2 - 50;

            bool showGamepadHints = GameInput.LastActiveDevice == GameInputDevice.Gamepad;
            if (showGamepadHints)
            {
                GUI.DrawTexture(new Rect(centerX, bottomY, bw, bw), aButton);
                GUI.Label(new Rect(centerX + bw + 10, bottomY + (bw - 20) / 2, 100, 20), tab == ETab.Save ? "Save" : "Load", text);
            }
            else
            {
                Rect primaryRect = SaveLoadPrimaryHitRect(x, y, w, h);
                if (buttonOutline != null)
                {
                    GUI.DrawTexture(primaryRect, buttonOutline, ScaleMode.StretchToFill, true);
                }
                text.alignment = TextAnchor.MiddleCenter;
                GUI.Label(primaryRect, tab == ETab.Save ? "Save" : "Load", text);
                text.alignment = TextAnchor.MiddleLeft;
            }
        }
    }

    private void DrawOptionsTab(float x, float y, float w, float h)
    {
        float contentY = y + title.fontSize + 25;
        float lineHeight = text.fontSize + 15;
        const float labelWidth = 200f;
        const float labelToSliderGap = 50f;
        const float sliderRightMargin = 90f;
        float controlX = x + labelWidth + labelToSliderGap;
        float sliderWidth = w - labelWidth - sliderRightMargin;
        
        text.alignment = TextAnchor.MiddleLeft;
        int currentItem = 0;
        
        // Music volume slider
        text.normal.textColor = (optionsIndex == currentItem) ? selectColor : unselectColor;
        GUI.Label(new Rect(x + 40, contentY, labelWidth, lineHeight), "Music volume", text);
        musicVolume = GUI.HorizontalSlider(new Rect(controlX, contentY + lineHeight / 3, sliderWidth, 20), musicVolume, 0.0f, 1.0f);
        contentY += lineHeight;
        currentItem++;
        
        // Sound font field
        text.normal.textColor = (optionsIndex == currentItem) ? selectColor : unselectColor;
        GUI.Label(new Rect(x + 40, contentY, w, lineHeight), "Sound card", text);
        
        // Show just the filename without extension for cleaner display
        string displayName = System.IO.Path.GetFileNameWithoutExtension(soundFont);
        text.normal.textColor = (optionsIndex == currentItem) ? selectColor : unselectColor;
        GUI.Label(new Rect(controlX, contentY, w, lineHeight), displayName, text);
        contentY += lineHeight;
        currentItem++;
        
        // Effects volume slider
        text.normal.textColor = (optionsIndex == currentItem) ? selectColor : unselectColor;
        GUI.Label(new Rect(x + 40, contentY, labelWidth, lineHeight), "Effects volume", text);
        effectsVolume = GUI.HorizontalSlider(new Rect(controlX, contentY + lineHeight / 3, sliderWidth, 20), effectsVolume, 0.0f, 1.0f);
        contentY += lineHeight;
        currentItem++;
        
        // Mouse look rate slider
        text.normal.textColor = (optionsIndex == currentItem) ? selectColor : unselectColor;
        GUI.Label(new Rect(x + 40, contentY, labelWidth, lineHeight), "Mouse look rate", text);
        mouseLookSpeed = GUI.HorizontalSlider(new Rect(controlX, contentY + lineHeight / 3, sliderWidth, 20), mouseLookSpeed, MinMouseLookSpeed, MaxMouseLookSpeed);
        contentY += lineHeight;
        currentItem++;
        
        float checkboxSize = 20;

        // Invert look checkbox
        text.normal.textColor = (optionsIndex == currentItem) ? selectColor : unselectColor;
        invertLook = GUI.Toggle(new Rect(x + 40, contentY + lineHeight / 3, checkboxSize, checkboxSize), invertLook, "");
        GUI.Label(new Rect(x + 60, contentY, w - 60, lineHeight), "Invert look up/down", text);
        contentY += lineHeight;
        currentItem++;
        
        // Use key automatically checkbox
        text.normal.textColor = (optionsIndex == currentItem) ? selectColor : unselectColor;
        useKeyAutomatically = GUI.Toggle(new Rect(x + 40, contentY + lineHeight / 3, checkboxSize, checkboxSize), useKeyAutomatically, "");
        GUI.Label(new Rect(x + 60, contentY, w - 60, lineHeight), "Use key automatically", text);
        contentY += lineHeight;
        currentItem++;
        
        // Auto jump checkbox
        text.normal.textColor = (optionsIndex == currentItem) ? selectColor : unselectColor;
        autoJump = GUI.Toggle(new Rect(x + 40, contentY + lineHeight / 3, checkboxSize, checkboxSize), autoJump, "");
        GUI.Label(new Rect(x + 60, contentY, w - 60, lineHeight), "Auto jump", text);

        text.alignment = TextAnchor.MiddleLeft;
        text.normal.textColor = selectColor;

        GetOptionsBottomLayout(x, y, w, h, out float leftControlX, out float rightControlX, out int bw, out float bottomYLayout);

        bool showGamepadHints = GameInput.LastActiveDevice == GameInputDevice.Gamepad;
        if (showGamepadHints)
        {
            GUI.DrawTexture(new Rect(leftControlX, bottomYLayout, bw, bw), dPadLeftRight);
            GUI.Label(new Rect(leftControlX + bw + 10, y + h - 15 - bw, 100, 20), "Change", text);
            GUI.DrawTexture(new Rect(rightControlX, bottomYLayout, bw, bw), aButton);
            GUI.Label(new Rect(rightControlX + bw + 10, y + h - 15 - bw, 120, 20), "Apply All", text);
        }
        else
        {
            Rect applyRect = OptionsApplyHitRect(x, y, w, h);
            if (buttonOutline != null)
            {
                GUI.DrawTexture(applyRect, buttonOutline, ScaleMode.StretchToFill, true);
            }
            text.alignment = TextAnchor.MiddleCenter;
            GUI.Label(applyRect, "Apply All", text);
            text.alignment = TextAnchor.MiddleLeft;
        }
    }
    
    private void LoadOptionsSettings()
    {
        // Load settings from PlayerPrefs
        musicVolume = PlayerPrefs.GetFloat("Options_MusicVolume", 0.5f);
        soundFont = PlayerPrefs.GetString("Options_SoundFont", "Soundfonts/MT32.sf2");
        effectsVolume = PlayerPrefs.GetFloat("Options_EffectsVolume", PlayerInput.DefaultEffectsVolume);
        mouseLookSpeed = PlayerPrefs.GetFloat("Options_MouseLookSpeed", PlayerInput.DefaultMouseLookSpeed);
        mouseLookSpeed = Mathf.Clamp(mouseLookSpeed, MinMouseLookSpeed, MaxMouseLookSpeed);
        useKeyAutomatically = PlayerPrefs.GetInt("Options_UseKeyAutomatically", 1) == 1;
        autoJump = PlayerPrefs.GetInt("Options_AutoJump", 0) == 1;
        invertLook = PlayerPrefs.GetInt("Options_InvertLook", 0) == 1;
        
        // Scan for available sound fonts
        ScanSoundFonts();
    }
    
    private void ScanSoundFonts()
    {
        try
        {
            // Always use Soundfonts subdirectory
            string soundFontPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Soundfonts");
            
            if (!System.IO.Directory.Exists(soundFontPath))
            {
                Debug.LogError($"Soundfonts directory not found at: {soundFontPath}");
                availableSoundFonts = new string[] { soundFont };
                currentSoundFontIndex = 0;
                return;
            }
            
            // Get all .sf2 files
            string[] sf2Files = System.IO.Directory.GetFiles(soundFontPath, "*.sf2", System.IO.SearchOption.TopDirectoryOnly);
            
            // Extract filenames with Soundfonts/ prefix
            availableSoundFonts = new string[sf2Files.Length];
            for (int i = 0; i < sf2Files.Length; i++)
            {
                string fileName = System.IO.Path.GetFileName(sf2Files[i]);
                availableSoundFonts[i] = "Soundfonts/" + fileName;
            }
            
            // Sort alphabetically
            System.Array.Sort(availableSoundFonts);
            
            // Find current sound font index (try with and without Soundfonts/ prefix)
            currentSoundFontIndex = System.Array.IndexOf(availableSoundFonts, soundFont);
            if (currentSoundFontIndex < 0)
            {
                // Try with Soundfonts/ prefix if not already present
                string withPrefix = soundFont.StartsWith("Soundfonts/") ? soundFont : "Soundfonts/" + soundFont;
                currentSoundFontIndex = System.Array.IndexOf(availableSoundFonts, withPrefix);
            }
            if (currentSoundFontIndex < 0)
            {
                // Try matching just the filename
                string justFileName = System.IO.Path.GetFileName(soundFont);
                for (int i = 0; i < availableSoundFonts.Length; i++)
                {
                    if (System.IO.Path.GetFileName(availableSoundFonts[i]) == justFileName)
                    {
                        currentSoundFontIndex = i;
                        break;
                    }
                }
            }
            if (currentSoundFontIndex < 0) currentSoundFontIndex = 0;
            
            Debug.Log($"Found {availableSoundFonts.Length} sound fonts in {soundFontPath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error scanning sound fonts: {ex.Message}");
            availableSoundFonts = new string[] { soundFont };
            currentSoundFontIndex = 0;
        }
    }
    
    private void CycleSoundFontWithDirection(int direction)
    {
        if (availableSoundFonts.Length <= 0)
            return;
        currentSoundFontIndex = (currentSoundFontIndex + direction + availableSoundFonts.Length) % availableSoundFonts.Length;
        soundFont = availableSoundFonts[currentSoundFontIndex];
    }

    private void HandleOptionsLeftRight(int direction)
    {
        switch (optionsIndex)
        {
            case 0: // Music volume
                musicVolume = Mathf.Clamp01(musicVolume + direction * 0.05f);
                break;
            case 1: // Sound font - cycle
                CycleSoundFontWithDirection(direction);
                break;
            case 2: // Effects volume
                effectsVolume = Mathf.Clamp01(effectsVolume + direction * 0.05f);
                break;
            case 3: // Mouse look speed
                mouseLookSpeed = Mathf.Clamp(mouseLookSpeed + direction * 0.05f, MinMouseLookSpeed, MaxMouseLookSpeed);
                break;
            case 4: // Invert look checkbox - toggle on either direction
                invertLook = !invertLook;
                break;
            case 5: // Use key automatically checkbox - toggle on either direction
                useKeyAutomatically = !useKeyAutomatically;
                break;
            case 6: // Auto jump checkbox - toggle on either direction
                autoJump = !autoJump;
                break;
        }
    }
    
    private void ApplyOptionsSettings()
    {
        // Save to PlayerPrefs
        PlayerPrefs.SetFloat("Options_MusicVolume", musicVolume);
        PlayerPrefs.SetString("Options_SoundFont", soundFont);
        PlayerPrefs.SetFloat("Options_EffectsVolume", effectsVolume);
        PlayerPrefs.SetFloat("Options_MouseLookSpeed", mouseLookSpeed);
        PlayerPrefs.SetInt("Options_UseKeyAutomatically", useKeyAutomatically ? 1 : 0);
        PlayerPrefs.SetInt("Options_AutoJump", autoJump ? 1 : 0);
        PlayerPrefs.SetInt("Options_InvertLook", invertLook ? 1 : 0);
        PlayerPrefs.Save();
        
        // Apply music volume to MusicPlayer
        if (MusicPlayer.Instance != null)
        {
            // Apply sound font - hot swap if changed
            MusicPlayer.Instance.ReloadSynthesizerWithSoundFont(soundFont, musicVolume);
        }
        
        Debug.Log($"Applied and saved options: Music={musicVolume:F2}, SoundFont={soundFont}, Effects={effectsVolume:F2}, MouseLookSpeed={mouseLookSpeed:F2}, UseKeyAuto={useKeyAutomatically}, AutoJump={autoJump}, InvertLook={invertLook}");
    }
}
