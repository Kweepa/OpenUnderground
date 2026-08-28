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
    public Color keyColor = new Color(0.2f, 0.2f, 0.2f);
    public Color selectedKeyColor = new Color(0.4f, 0.4f, 0.4f);
    public Color textColor = Color.white;
    public Color selectedTextColor = Color.yellow;
    public Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
    
    // State
    private bool visible;
    private string currentText;
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
    private float previousTimeScale = 1.0f;
    private float textEntryCursorBlinkAnchorUnscaled;
    
    // Public property to get current text
    public string CurrentText => currentText;
    
    // Key layout data - 4 rows x 10 columns
    private readonly string[,] keys = new string[4, 10]
    {
        { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" },
        { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" },
        { "a", "s", "d", "f", "g", "h", "j", "k", "l", "\u2019" },
        { "Shift", "Space", "z", "x", "c", "v", "b", "n", "m", "Del" }
    };
    private readonly string[,] keysShifted = new string[4, 10]
    {
        { "!", "@", "#", "$", "?", "-", "+", "*", "(", ")" },
        { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" },
        { "A", "S", "D", "F", "G", "H", "J", "K", "L", "\"" },
        { "Shift", "Space", "Z", "X", "C", "V", "B", "N", "M", "Del" }
    };
    
    // Rendering helpers
    private Texture2D keyBackgroundTex;
    private Texture2D panelBackgroundTex;
    
    private const int KEY_WIDTH = 40;
    private const int KEY_HEIGHT = 40;
    private const int KEY_SPACING = 5;
    private const int PANEL_PADDING = 20;
    private const int TEXT_FIELD_HEIGHT = 40;
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
    
    public void Show(string initialText, System.Action<string> _onComplete, System.Action _onCancel = null, Vector2? position = null, string _contextLabel = "", bool _isMapScreenKeyboard = false, bool _allowCancel = false)
    {
        currentText = initialText ?? "";
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
        
        // Calculate panel dimensions
        float panelWidth = (KEY_WIDTH + KEY_SPACING) * 10 - KEY_SPACING + PANEL_PADDING * 2;
        float buttonAreaHeight = 30;
        float panelHeight = TEXT_FIELD_HEIGHT + PANEL_PADDING * 2 + (KEY_HEIGHT + KEY_SPACING) * 4 - KEY_SPACING + buttonAreaHeight + 10; // Extra 10 pixels
        
        if (position.HasValue)
        {
            keyboardPosition = position.Value;
        }
        else
        {
            // Default: center of screen
            keyboardPosition = new Vector2((Screen.width - panelWidth) / 2, (Screen.height - panelHeight) / 2);
        }
        
        // Clamp keyboard position to stay on screen with 10 pixel buffer
        float buffer = 10.0f;
        keyboardPosition.x = Mathf.Clamp(keyboardPosition.x, buffer, Screen.width - panelWidth - buffer);
        keyboardPosition.y = Mathf.Clamp(keyboardPosition.y, buffer, Screen.height - panelHeight - buffer);
        
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
                float moveSpeed = 0.3f * Screen.width * Time.unscaledDeltaTime; // Use unscaled time since game is paused
                float panelWidth = (KEY_WIDTH + KEY_SPACING) * 10 - KEY_SPACING + PANEL_PADDING * 2;
                float buttonAreaHeight = 30;
                float panelHeight = TEXT_FIELD_HEIGHT + PANEL_PADDING * 2 + (KEY_HEIGHT + KEY_SPACING) * 4 - KEY_SPACING + buttonAreaHeight + 10;
                
                keyboardPosition.x += rightStick.x * moveSpeed;
                keyboardPosition.y -= rightStick.y * moveSpeed; // Invert Y for screen coordinates
                
                // Clamp to screen bounds with 10 pixel buffer
                float buffer = 10.0f;
                keyboardPosition.x = Mathf.Clamp(keyboardPosition.x, buffer, Screen.width - panelWidth - buffer);
                keyboardPosition.y = Mathf.Clamp(keyboardPosition.y, buffer, Screen.height - panelHeight - buffer);
            }
            
            // Navigation - d-pad only (left stick is used for conversation chat scroll elsewhere)
            if (GameInput.CurrentGamepad?.dpad.left.wasPressedThisFrame ?? false)
            {
                cursorX = (cursorX - 1 + 10) % 10;
            }
            else if (GameInput.CurrentGamepad.dpad.right.wasPressedThisFrame)
            {
                cursorX = (cursorX + 1) % 10;
            }
            else if (GameInput.CurrentGamepad.dpad.up.wasPressedThisFrame)
            {
                cursorY = (cursorY - 1 + 4) % 4;
            }
            else if (GameInput.CurrentGamepad.dpad.down.wasPressedThisFrame)
            {
                cursorY = (cursorY + 1) % 4;
            }
            
            // Key selection
            if (GameInput.CurrentGamepad.aButton.wasPressedThisFrame)
            {
                HandleKeyPress();
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
    
    private void HandleKeyPress()
    {
        string key = shiftActive ? keysShifted[cursorY, cursorX] : keys[cursorY, cursorX];
        
        // Handle Shift key
        if (key == "Shift")
        {
            shiftActive = !shiftActive;
        }
        else if (key == "Space")
        {
            currentText += " ";
            ResetTextEntryCursorBlink();
        }
        else if (key == "Del")
        {
            DeleteLastCharacter();
        }
        else
        {
            // Regular character key - use the key from the appropriate array
            currentText += key;
            ResetTextEntryCursorBlink();
            if (shiftActive && key != "Shift" && key != "Space" && key != "Del")
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
                if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                {
                    // Confirm (handles empty string cancellation internally)
                    Confirm();
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.Backspace || Event.current.keyCode == KeyCode.Delete)
                {
                    DeleteLastCharacter();
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.Escape && allowCancel)
                {
                    // Allow Escape to cancel when cancel is allowed
                    Cancel();
                    Event.current.Use();
                }
                else if (Event.current.character != 0 && !char.IsControl(Event.current.character))
                {
                    currentText += Event.current.character;
                    ResetTextEntryCursorBlink();
                    Event.current.Use();
                }
            }
        
        GUI.depth = (int)EGUIDepth.Keyboard;
        
        float panelWidth = (KEY_WIDTH + KEY_SPACING) * 10 - KEY_SPACING + PANEL_PADDING * 2;
        float buttonAreaHeight = 30;
        float panelHeight = TEXT_FIELD_HEIGHT + PANEL_PADDING * 2 + (KEY_HEIGHT + KEY_SPACING) * 4 - KEY_SPACING + buttonAreaHeight + 10; // Extra 10 pixels
        Rect panelRect = new Rect(keyboardPosition.x, keyboardPosition.y, panelWidth, panelHeight);
        GuiInput.RegisterBlockingRect(panelRect);

        // Draw background panel (extended to include button area)
        GUI.DrawTexture(panelRect, panelBackgroundTex);
        
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
            GUI.Label(new Rect(keyboardPosition.x + PANEL_PADDING, currentY, labelStyle.CalcSize(new GUIContent(contextLabel)).x, TEXT_FIELD_HEIGHT), contextLabel, labelStyle);
        }

        GUI.Label(new Rect(textFieldX, currentY, textFieldWidth, TEXT_FIELD_HEIGHT), currentText, textFieldStyle);
        float enteredTextWidth = textFieldStyle.CalcSize(new GUIContent(currentText)).x;
        if (IsTextEntryCursorVisible())
        {
            GUI.Label(new Rect(textFieldX + enteredTextWidth, currentY, textFieldWidth - enteredTextWidth, TEXT_FIELD_HEIGHT), TextEntryCursor, textFieldStyle);
        }
        currentY += TEXT_FIELD_HEIGHT + PANEL_PADDING;
        
        // Draw keys
        for (int row = 0; row < 4; row++)
        {
            float currentX = keyboardPosition.x + PANEL_PADDING;
            
            for (int col = 0; col < 10; col++)
            {
                bool isSelected = (row == cursorY && col == cursorX);
                bool isShiftKey = (keys[row, col] == "Shift");
                bool isShiftActive = isShiftKey && shiftActive && !allowCancel;
                
                // Determine colors
                Color bgColor = isSelected ? selectedKeyColor : keyColor;
                if (isShiftActive)
                {
                    bgColor = selectedKeyColor; // Highlight shift when active
                }
                
                Color txtColor = isSelected ? selectedTextColor : textColor;
                
                // Draw key background
                GUI.color = bgColor;
                GUI.DrawTexture(new Rect(currentX, currentY, KEY_WIDTH, KEY_HEIGHT), keyBackgroundTex);
                GUI.color = Color.white;
                
                // Draw cursor if selected
                if (isSelected && cursor != null)
                {
                    GUI.DrawTexture(new Rect(currentX, currentY, KEY_WIDTH, KEY_HEIGHT), cursor);
                }
                
                // Draw key label - use shifted array when shift is active
                string displayKey = shiftActive ? keysShifted[row, col] : keys[row, col];
                
                // Larger font for everything except Shift, Space, and Del
                int keyFontSize = (displayKey == "Shift" || displayKey == "Space" || displayKey == "Del") ? 14 : 24;
                GUIStyle keyStyle = new GUIStyle { font = font, normal = { textColor = txtColor }, fontSize = keyFontSize, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(currentX, currentY, KEY_WIDTH, KEY_HEIGHT), displayKey, keyStyle);
                
                currentX += KEY_WIDTH + KEY_SPACING;
            }
            
            currentY += KEY_HEIGHT + KEY_SPACING;
        }
        
        // Draw button prompts at bottom (within extended background)
        currentY += KEY_SPACING;
        float buttonY = currentY;
        
        // Cancel affordance (bottom left, only when cancel is allowed): gamepad X + label; mouse/kb = right-mouse texture + label (same layout as X)
        if (allowCancel)
        {
            float buttonSize = 24;
            float buttonX = keyboardPosition.x + PANEL_PADDING;
            GUIStyle buttonLabelStyle = new GUIStyle { font = font, normal = { textColor = textColor }, fontSize = 14 };
            if (UseMouseKeyboardUi())
            {
                if (mouseRightButtonHint != null)
                {
                    GUI.DrawTexture(new Rect(buttonX, buttonY, buttonSize, buttonSize), mouseRightButtonHint);
                    GUI.Label(new Rect(buttonX + buttonSize + 5, buttonY + 2, 100, buttonSize), "Cancel", buttonLabelStyle);
                }
            }
            else if (xButton != null)
            {
                GUI.DrawTexture(new Rect(buttonX, buttonY, buttonSize, buttonSize), xButton);
                GUI.Label(new Rect(buttonX + buttonSize + 5, buttonY + 2, 100, buttonSize), "Cancel", buttonLabelStyle);
            }
        }
        
        // B button + Clear label (center bottom) — gamepad hint only; mouse/KB users still have LMB hitbox below
        if (!UseMouseKeyboardUi() && bButton != null && !string.IsNullOrEmpty(currentText?.Trim()))
        {
            float buttonSize = 24;
            float buttonX = keyboardPosition.x + (panelWidth - buttonSize) / 2.0f;
            GUI.DrawTexture(new Rect(buttonX, buttonY, buttonSize, buttonSize), bButton);
            GUIStyle buttonLabelStyle = new GUIStyle { font = font, normal = { textColor = textColor }, fontSize = 14 };
            GUI.Label(new Rect(buttonX + buttonSize + 5, buttonY + 2, 100, buttonSize), "Clear", buttonLabelStyle);
        }
        
        // Y button + Enter label (bottom right) — gamepad hint only
        if (!UseMouseKeyboardUi() && yButton != null && !string.IsNullOrEmpty(currentText?.Trim()))
        {
            float buttonSize = 24;
            float buttonX = keyboardPosition.x + panelWidth - PANEL_PADDING - buttonSize - 50;
            GUI.DrawTexture(new Rect(buttonX, buttonY, buttonSize, buttonSize), yButton);
            GUIStyle buttonLabelStyle = new GUIStyle { font = font, normal = { textColor = textColor }, fontSize = 14 };
            GUI.Label(new Rect(buttonX + buttonSize + 5, buttonY + 2, 100, buttonSize), "Enter", buttonLabelStyle);
        }

        if (GuiInput.IsLeftMouseDownGui)
        {
            string trimmedText = currentText?.Trim() ?? "";
            if (!panelRect.Contains(Event.current.mousePosition))
            {
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

            float cy = keyboardPosition.y + PANEL_PADDING + TEXT_FIELD_HEIGHT + PANEL_PADDING;
            for (int row = 0; row < 4; row++)
            {
                float cx = keyboardPosition.x + PANEL_PADDING;
                for (int col = 0; col < 10; col++)
                {
                    Rect keyRect = new Rect(cx, cy, KEY_WIDTH, KEY_HEIGHT);
                    if (keyRect.Contains(Event.current.mousePosition))
                    {
                        cursorX = col;
                        cursorY = row;
                        HandleKeyPress();
                        MarkMapPrimaryClickHandled();
                        Event.current.Use();
                        return;
                    }
                    cx += KEY_WIDTH + KEY_SPACING;
                }
                cy += KEY_HEIGHT + KEY_SPACING;
            }

            float buttonSize = 24;
            float buttonAreaY = keyboardPosition.y + PANEL_PADDING + TEXT_FIELD_HEIGHT + PANEL_PADDING
                + (KEY_HEIGHT + KEY_SPACING) * 4 + KEY_SPACING;

            if (allowCancel)
            {
                float cancelHitW;
                if (UseMouseKeyboardUi())
                    cancelHitW = mouseRightButtonHint != null ? buttonSize + 90f : 0f;
                else
                    cancelHitW = xButton != null ? buttonSize + 90f : 0f;

                if (cancelHitW > 0f
                    && GuiInput.TryConsumeClickInRect(new Rect(keyboardPosition.x + PANEL_PADDING, buttonAreaY, cancelHitW, buttonSize + 8)))
                {
                    MarkMapPrimaryClickHandled();
                    Cancel();
                    return;
                }
            }

            if (bButton != null && !string.IsNullOrEmpty(currentText?.Trim()))
            {
                float bx = keyboardPosition.x + (panelWidth - buttonSize) / 2.0f;
                if (GuiInput.TryConsumeClickInRect(new Rect(bx, buttonAreaY, 120, buttonSize + 8)))
                {
                    MarkMapPrimaryClickHandled();
                    currentText = "";
                    ResetTextEntryCursorBlink();
                    return;
                }
            }

            if (yButton != null && !string.IsNullOrEmpty(currentText?.Trim()))
            {
                float yx = keyboardPosition.x + panelWidth - PANEL_PADDING - buttonSize - 50;
                if (GuiInput.TryConsumeClickInRect(new Rect(yx, buttonAreaY, 130, buttonSize + 8)))
                {
                    MarkMapPrimaryClickHandled();
                    Confirm();
                }
            }

            GuiInput.TryConsumeClickInPanel(panelRect);
        }
    }
}
