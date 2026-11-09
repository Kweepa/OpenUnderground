using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(50)]
public class KeyboardGUI : MonoBehaviour
{
    public static KeyboardGUI sKeyboard;
    
    // Public properties for Unity Inspector
    public Font font;
    public Texture cursor;
    public Texture2D bButton;
    public Texture2D yButton;
    public Texture2D xButton;
    public Texture2D mouseRightButtonHint;
    public Texture2D mouseLeftButtonHint;
    /// <summary>The game's blank keyboard key, on which the Del and Enter hints write their names.</summary>
    public Texture2D blankKey;
    public Color keyColor = new Color(0.2f, 0.2f, 0.2f);
    public Color selectedKeyColor = new Color(0.4f, 0.4f, 0.4f);
    public Color textColor = Color.white;
    public Color selectedTextColor = Color.yellow;
    public Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
    
    // State
    private bool visible;
    private string currentText;

    /// <summary>Most characters this field will take, or 0 for no limit. Set by Show.</summary>
    /// <remarks>
    /// A limit the player can see working as he types, rather than a name cut behind his back
    /// once he has confirmed it.
    /// </remarks>
    private int maxLength;

    private bool CanAppend()
    {
        return maxLength <= 0 || currentText == null || currentText.Length < maxLength;
    }
    private string contextLabel;
    private int cursorX, cursorY;
    private bool shiftActive;
    private System.Action<string> onComplete;
    private System.Action onCancel;
    private Vector2 keyboardPosition;
    private bool waitingForYRelease;
    private bool waitingOneFrame;
    private bool isMapScreenKeyboard;
    private bool allowCancel;
    private bool waitingForXRelease;
    private bool waitingOneFrameForX;
    private bool waitingForAReleaseToConfirm;
    private bool waitingOneFrameForA;
    private float previousTimeScale = 1.0f;
    private float textEntryCursorBlinkAnchorUnscaled;
    
    // Public property to get current text
    public string CurrentText => currentText;
    
    // Key layout data - 4 rows x 11 columns. The eleventh is Back, the dash and Enter, which is one
    // key two rows high, so that the pad's cursor and the mouse have a key that confirms.
    private readonly string[,] keys = new string[4, COLUMNS]
    {
        { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "Back" },
        { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p", "-" },
        { "a", "s", "d", "f", "g", "h", "j", "k", "l", "\u2019", "Enter" },
        { "Shift", "Space", "z", "x", "c", "v", "b", "n", "m", ".", "Enter" }
    };
    private readonly string[,] keysShifted = new string[4, COLUMNS]
    {
        { "!", "@", "#", "$", "?", "%", "+", "*", "(", ")", "Back" },
        { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "_" },
        { "A", "S", "D", "F", "G", "H", "J", "K", "L", "\"", "Enter" },
        { "Shift", "Space", "Z", "X", "C", "V", "B", "N", "M", ",", "Enter" }
    };
    
    // Rendering helpers
    private Texture2D keyBackgroundTex;
    private Texture2D panelBackgroundTex;
    
    private const int KEY_WIDTH = 40;
    private const int KEY_HEIGHT = 40;
    private const int KEY_SPACING = 5;
    private const int PANEL_PADDING = 20;
    private const int TEXT_FIELD_HEIGHT = 40;
    private const int COLUMNS = 11;
    private const int HINT_SIZE = 24;
    private const int HINT_GAP = 16; // above and below the row of hints

    /// <summary>The panel's size in reference pixels (UiScale), for whoever places it.</summary>
    public const float PanelWidth = PANEL_PADDING * 2 + (KEY_WIDTH + KEY_SPACING) * COLUMNS - KEY_SPACING;
    public const float PanelHeight = PANEL_PADDING * 2 + TEXT_FIELD_HEIGHT + (KEY_HEIGHT + KEY_SPACING) * 4 - KEY_SPACING
        + HINT_GAP + HINT_SIZE + HINT_GAP;
    private const string TextEntryCursor = "\u2020"; // dagger †
    private const float TextEntryCursorBlinkHz = 2f; // 0.5s on / 0.5s off
    private const float TextEntryCursorIdleBeforeBlinkSeconds = 0.5f;

    /// <summary>Set when OnGUI consumes LMB so <see cref="MapScreen"/> can ignore the same click on the next Update (IMGUI vs Input System ordering).</summary>
    private static bool s_suppressMapPrimaryClickOnce;

    /// <summary>Returns true once after keyboard handled primary mouse; clears the latch.</summary>
    public static bool ConsumeMapPrimaryClickSuppression()
    {
        if (!s_suppressMapPrimaryClickOnce)
            return false;
        s_suppressMapPrimaryClickOnce = false;
        return true;
    }

    private static void MarkMapPrimaryClickHandled()
    {
        s_suppressMapPrimaryClickOnce = true;
    }

    private static bool UseMouseKeyboardUi()
    {
        return GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;
    }

    private void ResetTextEntryCursorBlink()
    {
        textEntryCursorBlinkAnchorUnscaled = Time.unscaledTime;
    }

    private bool IsTextEntryCursorVisible()
    {
        float elapsed = Time.unscaledTime - textEntryCursorBlinkAnchorUnscaled;
        if (elapsed < TextEntryCursorIdleBeforeBlinkSeconds)
        {
            return true;
        }
        float blinkElapsed = elapsed - TextEntryCursorIdleBeforeBlinkSeconds;
        return (Mathf.FloorToInt(blinkElapsed * TextEntryCursorBlinkHz) % 2) == 0;
    }

    void Awake()
    {
        sKeyboard = this;
        CreateBackgroundTextures();
        DontDestroyOnLoad(gameObject);
    }
    
    private void CreateBackgroundTextures()
    {
        // Create key background texture
        keyBackgroundTex = new Texture2D(1, 1);
        keyBackgroundTex.SetPixel(0, 0, keyColor);
        keyBackgroundTex.Apply();
        
        // Create panel background texture
        panelBackgroundTex = new Texture2D(1, 1);
        panelBackgroundTex.SetPixel(0, 0, backgroundColor);
        panelBackgroundTex.Apply();
    }
    
    /// <param name="_maxLength">Most characters the field will take, or 0 for no limit.</param>
    public void Show(string initialText, System.Action<string> _onComplete, System.Action _onCancel = null, Vector2? position = null, string _contextLabel = "", bool _isMapScreenKeyboard = false, bool _allowCancel = false, int _maxLength = 0)
    {
        maxLength = _maxLength;
        currentText = initialText ?? "";
        if (maxLength > 0 && currentText.Length > maxLength)
        {
            currentText = currentText.Substring(0, maxLength);
        }

        ResetTextEntryCursorBlink();
        contextLabel = _contextLabel ?? "";
        onComplete = _onComplete;
        onCancel = _onCancel;
        isMapScreenKeyboard = _isMapScreenKeyboard;
        allowCancel = _allowCancel;
        cursorX = 4; // Start on 'O' (row 2, column 4)
        cursorY = 2;
        shiftActive = false;
        visible = true;
        waitingForYRelease = false;
        waitingOneFrame = false;
        waitingForXRelease = false;
        waitingOneFrameForX = false;
        waitingForAReleaseToConfirm = false;
        waitingOneFrameForA = false;
        
        // Calculate panel dimensions
        float panelWidth = PanelWidth;
        float panelHeight = PanelHeight;
        
        // The panel is laid out in reference pixels (UiScale); the position comes in screen pixels.
        if (position.HasValue)
        {
            keyboardPosition = UiScale.ToRef(position.Value);
        }
        else
        {
            // Default: center of screen
            keyboardPosition = new Vector2((UiScale.Width - panelWidth) / 2, (UiScale.Height - panelHeight) / 2);
        }
        
        // Clamp keyboard position to stay on screen with 10 pixel buffer
        float buffer = 10.0f;
        keyboardPosition.x = Mathf.Clamp(keyboardPosition.x, buffer, UiScale.Width - panelWidth - buffer);
        keyboardPosition.y = Mathf.Clamp(keyboardPosition.y, buffer, UiScale.Height - panelHeight - buffer);
        
        // Pause game and disable controls, remembering previous time scale
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0.0f;
        PlayerObject.DisableControls(EControlMask.Keyboard, true);
    }
    
    private void Hide()
    {
        visible = false;
        waitingForYRelease = false;
        waitingOneFrame = false;
        waitingForXRelease = false;
        waitingOneFrameForX = false;
        waitingForAReleaseToConfirm = false;
        waitingOneFrameForA = false;
        Time.timeScale = previousTimeScale;
        PlayerObject.DisableControls(EControlMask.Keyboard, false);
    }
    
    public bool IsVisible()
    {
        return visible;
    }
    
    void Update()
    {
        if (!visible) return;

        if (allowCancel && GameInput.EscapePressedThisFrame())
        {
            // the press is the keyboard's: what is behind it must not take it as well, now or next frame
            GameInput.ConsumeEscapeThroughNextFrame();
            Cancel();
            return;
        }

        if (allowCancel && UseMouseKeyboardUi())
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                Cancel();
                return;
            }
        }
        
        // Handle controller input
        if (GameInput.CurrentGamepad != null)
        {
            // Move keyboard with right stick
            Vector2 rightStick = GameInput.CurrentGamepad.rightStick.ReadValue();
            if (rightStick.magnitude > 0.1f) // Dead zone
            {
                float moveSpeed = 0.3f * UiScale.Width * Time.unscaledDeltaTime; // Use unscaled time since game is paused
                float panelWidth = PanelWidth;
                float panelHeight = PanelHeight;
                
                keyboardPosition.x += rightStick.x * moveSpeed;
                keyboardPosition.y -= rightStick.y * moveSpeed; // Invert Y for screen coordinates
                
                // Clamp to screen bounds with 10 pixel buffer
                float buffer = 10.0f;
                keyboardPosition.x = Mathf.Clamp(keyboardPosition.x, buffer, UiScale.Width - panelWidth - buffer);
                keyboardPosition.y = Mathf.Clamp(keyboardPosition.y, buffer, UiScale.Height - panelHeight - buffer);
            }
            
            // Navigation - d-pad only (left stick is used for conversation chat scroll elsewhere)
            if (GameInput.CurrentGamepad?.dpad.left.wasPressedThisFrame ?? false)
            {
                cursorX = (cursorX - 1 + COLUMNS) % COLUMNS;
            }
            else if (GameInput.CurrentGamepad.dpad.right.wasPressedThisFrame)
            {
                cursorX = (cursorX + 1) % COLUMNS;
            }
            else if (GameInput.CurrentGamepad.dpad.up.wasPressedThisFrame)
            {
                cursorY = StepRow(-1);
            }
            else if (GameInput.CurrentGamepad.dpad.down.wasPressedThisFrame)
            {
                cursorY = StepRow(1);
            }
            
            // Key selection
            if (GameInput.CurrentGamepad.aButton.wasPressedThisFrame)
            {
                HandleKeyPress(true);
            }

            // A on Enter confirms once released, and a frame later, as Y does, so that the press
            // does not reach the screen the keyboard returns to.
            if (waitingForAReleaseToConfirm && !GameInput.CurrentGamepad.aButton.isPressed)
            {
                waitingForAReleaseToConfirm = false;
                waitingOneFrameForA = true;
            }
            else if (waitingOneFrameForA)
            {
                waitingOneFrameForA = false;
                if (HasText())
                {
                    Confirm();
                    return;
                }
            }
            
            // X button: Cancel when cancel is allowed (otherwise does nothing)
            if (allowCancel)
            {
                // X button cancels when cancel is allowed
                if (GameInput.CurrentGamepad.xButton.wasPressedThisFrame)
                {
                    waitingForXRelease = true;
                }
                else if (waitingForXRelease && GameInput.CurrentGamepad.xButton.wasReleasedThisFrame)
                {
                    waitingForXRelease = false;
                    waitingOneFrameForX = true;
                }
                else if (waitingOneFrameForX)
                {
                    waitingOneFrameForX = false;
                    Cancel();
                }
                else if (waitingForXRelease && !GameInput.CurrentGamepad.xButton.isPressed)
                {
                    waitingForXRelease = false;
                    waitingOneFrameForX = true;
                }
            }
            
            // B button: Clear text on any keyboard (no longer cancels on map screen)
            if (GameInput.CurrentGamepad.bButton.wasPressedThisFrame)
            {
                currentText = "";
                ResetTextEntryCursorBlink();
            }
            
            // Enter (Y button) - only active when there is non-empty text
            bool hasText = !string.IsNullOrEmpty(currentText?.Trim());
            if (!hasText)
            {
                // Ensure we don't accidentally confirm when there's no text
                waitingForYRelease = false;
                waitingOneFrame = false;
            }
            else
            {
                // Wait for release then one frame before confirming
                if (GameInput.CurrentGamepad?.yButton.wasPressedThisFrame ?? false)
                {
                    waitingForYRelease = true;
                }
                else if (waitingForYRelease && (GameInput.CurrentGamepad?.yButton.wasReleasedThisFrame ?? false))
                {
                    waitingForYRelease = false;
                    waitingOneFrame = true;
                }
                else if (waitingOneFrame)
                {
                    // Wait one frame after release before confirming
                    waitingOneFrame = false;
                    Confirm();
                }
            }
        }
    }
    
    /// <summary>
    /// Enter is one key two rows high: stepping up or down off it skips its other half, so it is
    /// left in one press, like any key.
    /// </summary>
    private int StepRow(int step)
    {
        int row = cursorY;
        do
        {
            row = (row + step + 4) % 4;
        }
        while (IsEnterCell(cursorY, cursorX) && IsEnterCell(row, cursorX));
        return row;
    }

    private bool IsEnterCell(int row, int col)
    {
        return keys[row, col] == "Enter";
    }

    private bool HasText()
    {
        return !string.IsNullOrEmpty(currentText?.Trim());
    }

    /// <summary>A key's place in reference pixels; Enter's two cells give the same, two rows high.</summary>
    private Rect KeyRect(int row, int col)
    {
        float keysTop = keyboardPosition.y + PANEL_PADDING + TEXT_FIELD_HEIGHT + PANEL_PADDING;
        float x = keyboardPosition.x + PANEL_PADDING + (KEY_WIDTH + KEY_SPACING) * col;
        if (IsEnterCell(row, col))
        {
            return new Rect(x, keysTop + (KEY_HEIGHT + KEY_SPACING) * 2, KEY_WIDTH, KEY_HEIGHT * 2 + KEY_SPACING);
        }
        return new Rect(x, keysTop + (KEY_HEIGHT + KEY_SPACING) * row, KEY_WIDTH, KEY_HEIGHT);
    }

    /// <param name="fromPad">A pressed on the pad, rather than a click: Enter then waits for A's release.</param>
    private void HandleKeyPress(bool fromPad)
    {
        string key = shiftActive ? keysShifted[cursorY, cursorX] : keys[cursorY, cursorX];
        
        // Handle Shift key
        if (key == "Shift")
        {
            shiftActive = !shiftActive;
        }
        else if (key == "Space")
        {
            if (CanAppend())
            {
                currentText += " ";
            }

            ResetTextEntryCursorBlink();
        }
        else if (key == "Back")
        {
            DeleteLastCharacter();
        }
        else if (key == "Enter")
        {
            // greyed while there is nothing to confirm
            if (HasText())
            {
                if (fromPad)
                {
                    waitingForAReleaseToConfirm = true;
                }
                else
                {
                    Confirm();
                }
            }
        }
        else
        {
            // Regular character key - use the key from the appropriate array
            if (CanAppend())
            {
                currentText += key;
            }

            ResetTextEntryCursorBlink();
            if (shiftActive && key != "Shift" && key != "Space" && key != "Back")
            {
                shiftActive = false; // Shift deactivates after one use
            }
        }
    }
    
    private void DeleteLastCharacter()
    {
        if (currentText.Length > 0)
        {
            currentText = currentText.Substring(0, currentText.Length - 1);
            ResetTextEntryCursorBlink();
        }
    }
    
    private void Confirm()
    {
        // Trim spaces from both ends
        string trimmedText = currentText.Trim();
        
        // Handle empty string based on allowCancel
        if (string.IsNullOrEmpty(trimmedText))
        {
            if (allowCancel)
            {
                // Empty string cancels when cancel is allowed
                Cancel();
            }
            // Otherwise, don't confirm if empty or all spaces
            return;
        }
        
        Hide();
        if (onComplete != null)
        {
            onComplete(trimmedText);
        }
    }
    
    private void Cancel()
    {
        Hide();
        if (onCancel != null)
        {
            onCancel();
        }
    }
    
    void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Keyboard;

        if (!visible) return;
        
            // Handle physical keyboard input (Event.current is only valid in OnGUI)
            if (Event.current.isKey && Event.current.type == EventType.KeyDown)
            {
                // Alt-Enter is the fullscreen switch, not an Enter (mod 740)
                if ((Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && !Event.current.alt)
                {
                    // Confirm (handles empty string cancellation internally)
                    Confirm();
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.Backspace)
                {
                    DeleteLastCharacter();
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.Delete)
                {
                    // Delete clears the whole text, as the pad's B does
                    currentText = "";
                    ResetTextEntryCursorBlink();
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.Escape && allowCancel)
                {
                    // Allow Escape to cancel when cancel is allowed
                    GameInput.ConsumeEscapeThroughNextFrame();
                    Cancel();
                    Event.current.Use();
                }
                else if (Event.current.character != 0 && !char.IsControl(Event.current.character))
                {
                    if (CanAppend())
                    {
                        currentText += Event.current.character;
                    }

                    ResetTextEntryCursorBlink();
                    Event.current.Use();
                }
            }
        
        GUI.depth = (int)EGUIDepth.Keyboard;
        
        float panelWidth = PanelWidth;
        float panelHeight = PanelHeight;
        Rect panelRect = new Rect(keyboardPosition.x, keyboardPosition.y, panelWidth, panelHeight);
        GuiInput.RegisterBlockingRect(UiScale.R(panelRect));

        // Draw background panel (extended to include button area)
        UiScale.DrawTexture(UiScale.R(panelRect), panelBackgroundTex);
        
        float currentY = keyboardPosition.y + PANEL_PADDING;
        
        // Draw context label and text field on same line
        GUIStyle labelStyle = new GUIStyle { font = font, normal = { textColor = textColor }, fontSize = 40 };
        labelStyle.alignment = TextAnchor.MiddleLeft;
        
        // Calculate text field width
        float textFieldWidth;
        float textFieldX;
        if (!string.IsNullOrEmpty(contextLabel))
        {
            float labelWidth = labelStyle.CalcSize(new GUIContent(contextLabel)).x;
            textFieldX = keyboardPosition.x + PANEL_PADDING + labelWidth + 10;
            textFieldWidth = panelWidth - PANEL_PADDING * 2 - labelWidth - 10;
        }
        else
        {
            textFieldX = keyboardPosition.x + PANEL_PADDING;
            textFieldWidth = panelWidth - PANEL_PADDING * 2;
        }
        
        // Calculate font size to fit text + cursor in available width
        int baseFontSize = 40;
        string textWithCursor = currentText + TextEntryCursor;
        GUIStyle testStyle = new GUIStyle { font = font };
        testStyle.fontSize = baseFontSize;
        float textWidth = testStyle.CalcSize(new GUIContent(textWithCursor)).x;
        
        int fontSize = baseFontSize;
        if (textWidth > textFieldWidth)
        {
            // Shrink font to fit
            fontSize = Mathf.Max(10, (int)(baseFontSize * (textFieldWidth / textWidth)));
            testStyle.fontSize = fontSize;
            // Recalculate with new font size to ensure it fits
            textWidth = testStyle.CalcSize(new GUIContent(textWithCursor)).x;
            if (textWidth > textFieldWidth)
            {
                fontSize = Mathf.Max(10, (int)(fontSize * (textFieldWidth / textWidth)));
            }
        }
        
        GUIStyle textFieldStyle = new GUIStyle { font = font, normal = { textColor = selectedTextColor }, fontSize = fontSize };
        textFieldStyle.alignment = TextAnchor.MiddleLeft;
        
        if (!string.IsNullOrEmpty(contextLabel))
        {
            // Label on left side
            GUI.Label(UiScale.R(keyboardPosition.x + PANEL_PADDING, currentY, labelStyle.CalcSize(new GUIContent(contextLabel)).x, TEXT_FIELD_HEIGHT), contextLabel, UiScale.Style(labelStyle));
        }

        GUI.Label(UiScale.R(textFieldX, currentY, textFieldWidth, TEXT_FIELD_HEIGHT), currentText, UiScale.Style(textFieldStyle));
        float enteredTextWidth = textFieldStyle.CalcSize(new GUIContent(currentText)).x;
        if (IsTextEntryCursorVisible())
        {
            GUI.Label(UiScale.R(textFieldX + enteredTextWidth, currentY, textFieldWidth - enteredTextWidth, TEXT_FIELD_HEIGHT), TextEntryCursor, UiScale.Style(textFieldStyle));
        }
        currentY += TEXT_FIELD_HEIGHT + PANEL_PADDING;
        
        // Draw keys
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < COLUMNS; col++)
            {
                // Enter is drawn once, from its upper cell, and lit from either
                if (row > 0 && IsEnterCell(row, col) && IsEnterCell(row - 1, col))
                {
                    continue;
                }
                Rect keyRect = KeyRect(row, col);
                bool isSelected = col == cursorX && (row == cursorY || (IsEnterCell(row, col) && IsEnterCell(cursorY, col)));
                bool isShiftKey = (keys[row, col] == "Shift");
                bool isShiftActive = isShiftKey && shiftActive && !allowCancel;
                
                // Determine colors
                Color bgColor = isSelected ? selectedKeyColor : keyColor;
                if (isShiftActive)
                {
                    bgColor = selectedKeyColor; // Highlight shift when active
                }
                
                Color txtColor = isSelected ? selectedTextColor : textColor;
                if (IsEnterCell(row, col) && !HasText())
                {
                    txtColor.a *= 0.4f; // greyed: nothing to confirm
                }
                
                // Draw key background
                GUI.color = bgColor;
                UiScale.DrawTexture(UiScale.R(keyRect), keyBackgroundTex);
                GUI.color = Color.white;
                
                // Draw cursor if selected
                if (isSelected && cursor != null)
                {
                    UiScale.DrawTexture(UiScale.R(keyRect), cursor);
                }
                
                // Draw key label - use shifted array when shift is active
                string displayKey = shiftActive ? keysShifted[row, col] : keys[row, col];
                
                // Larger font for everything except the keys named by a word
                int keyFontSize = (displayKey == "Shift" || displayKey == "Space" || displayKey == "Back" || displayKey == "Enter") ? 14 : 24;
                GUIStyle keyStyle = new GUIStyle { font = font, normal = { textColor = txtColor }, fontSize = keyFontSize, alignment = TextAnchor.MiddleCenter };
                GUI.Label(UiScale.R(keyRect), displayKey, UiScale.Style(keyStyle));
            }
        }
        
        // The hints under the keys, in three places: Cancel, Clear, Enter. The pad's buttons, or the
        // mouse's and the keyboard's keys. Only pictures: the keys and the buttons do the work, so no
        // hint is a place to click.
        float hintY = KeyRect(3, 0).yMax + HINT_GAP;
        float hintWidth = (panelWidth - PANEL_PADDING * 2) / 3f;
        bool mouseHints = UseMouseKeyboardUi();
        if (allowCancel)
        {
            DrawHint(0, hintY, hintWidth, "Cancel", mouseHints ? mouseRightButtonHint : xButton, null);
        }
        if (HasText())
        {
            DrawHint(1, hintY, hintWidth, "Clear", mouseHints ? null : bButton, mouseHints ? "Del" : null);
            // On the map a click outside the keyboard confirms the note where the quill is
            Texture2D enterPicture = !mouseHints ? yButton : isMapScreenKeyboard ? mouseLeftButtonHint : null;
            DrawHint(2, hintY, hintWidth, "Enter", enterPicture, mouseHints && !isMapScreenKeyboard ? "Enter" : null);
        }

        if (GuiInput.IsLeftMouseDownGui)
        {
            string trimmedText = currentText?.Trim() ?? "";
            if (!panelRect.Contains(UiScale.ToRef(Event.current.mousePosition)))
            {
                // A click outside the panel: on the map it confirms the note where the quill is, or
                // cancels an empty one; anywhere else it does nothing, as outside a dialog, and goes
                // no further.
                if (!isMapScreenKeyboard)
                {
                    Event.current.Use();
                    GuiInput.MarkPrimaryClickConsumed();
                    return;
                }

                if (!string.IsNullOrEmpty(trimmedText))
                {
                    MarkMapPrimaryClickHandled();
                    Confirm();
                    Event.current.Use();
                    return;
                }

                if (allowCancel)
                {
                    MarkMapPrimaryClickHandled();
                    Cancel();
                    Event.current.Use();
                    return;
                }
            }

            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < COLUMNS; col++)
                {
                    if (KeyRect(row, col).Contains(UiScale.ToRef(Event.current.mousePosition)))
                    {
                        cursorX = col;
                        cursorY = row;
                        HandleKeyPress(false);
                        MarkMapPrimaryClickHandled();
                        Event.current.Use();
                        return;
                    }
                }
            }

            GuiInput.TryConsumeClickInPanel(UiScale.R(panelRect));
        }
    }

    /// <summary>
    /// One hint in the third of the hint row given by place (0 left, 1 middle, 2 right): a picture,
    /// or a keyboard key's name on the blank key, and what it does. Nothing if the picture is missing.
    /// </summary>
    private void DrawHint(int place, float y, float width, string what, Texture2D picture, string keyName)
    {
        HintKey key;
        if (picture != null)
        {
            key = HintKey.Of(picture);
        }
        else if (keyName != null && blankKey != null)
        {
            key = HintKey.Keyboard(keyName);
        }
        else
        {
            return;
        }
        GUIStyle text = new GUIStyle { font = font, fontSize = 14 };
        text.normal.textColor = textColor;
        GUIStyle keyText = new GUIStyle { font = font, fontSize = 12 };
        keyText.normal.textColor = Color.white;
        Rect row = new Rect(keyboardPosition.x + PANEL_PADDING + width * place, y, width, HINT_SIZE);
        HintBar.Draw(UiScale.R(row), UiScale.Factor, text, keyText, blankKey, new[] { new Hint(what, key) });
    }
}
