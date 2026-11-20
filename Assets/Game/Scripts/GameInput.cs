using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Central access to keyboard, gamepad, and mouse, plus common combined checks (dpad + arrows, panel keys, etc.).
/// </summary>
public static class GameInput
{
    /// <summary>Last discrete input scheme (updated each frame by <see cref="RefreshLastActiveDevice"/>).</summary>
    public static GameInputDevice LastActiveDevice { get; private set; } = GameInputDevice.MouseKeyboard;

    public static Keyboard CurrentKeyboard => Keyboard.current;

    /// <summary>
    /// Alt held: Alt-Enter is the fullscreen switch (Unity's own), never an Enter for the game. Also true
    /// for half a second after the window changes mode or focus: the switch freezes the game, and the
    /// keys of the Alt-Enter come in together afterwards, Alt already up when Enter reads as pressed,
    /// so the title menu took the Enter as a choice.
    /// </summary>
    public static bool AltHeld =>
        (Keyboard.current is { } k && (k.leftAltKey.isPressed || k.rightAltKey.isPressed))
        || Time.realtimeSinceStartup - windowChangedAt < WindowChangeGrace;

    private const float WindowChangeGrace = 0.5f;

    /// <summary>How far a stick must be pushed to make the pad the device in use.</summary>
    private const float StickTakesOver = 0.5f;
    private static bool sticksWereOut;
    private static float windowChangedAt = float.NegativeInfinity;
    private static bool wasFullScreen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void WatchWindow()
    {
        windowChangedAt = float.NegativeInfinity;
        wasFullScreen = Screen.fullScreen;
        Application.focusChanged -= OnFocusChanged;
        Application.focusChanged += OnFocusChanged;
        InputSystem.onBeforeUpdate -= CheckFullScreen;
        InputSystem.onBeforeUpdate += CheckFullScreen;
    }

    private static void OnFocusChanged(bool focused)
    {
        windowChangedAt = Time.realtimeSinceStartup;
    }

    // Before the input of the frame is read, so the Enter that comes in with the switch already sees it.
    private static void CheckFullScreen()
    {
        bool fullScreen = Screen.fullScreen;
        if (fullScreen != wasFullScreen)
        {
            wasFullScreen = fullScreen;
            windowChangedAt = Time.realtimeSinceStartup;
        }
    }

    /// <summary>Enter or the keypad's Enter pressed this frame, Alt-Enter not counting (mod 740).</summary>
    public static bool EnterPressedThisFrame() =>
        Keyboard.current is { } k && !AltHeld && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame);
    public static Gamepad CurrentGamepad => Gamepad.current;
    public static Mouse CurrentMouse => Mouse.current;

    /// <summary>True while gameplay keyboard shortcuts are disabled (<see cref="EControlMask.Keyboard"/>).</summary>
    public static bool IsGameplayKeyboardInputBlocked()
    {
        PlayerObject p = PlayerObject.Player;
        return p != null && (p.controlsDisabled & EControlMask.Keyboard) != 0;
    }

    /// <summary>
    /// Call once per frame before gameplay reads input. Gamepad wins over mouse/keyboard if both fire the same frame.
    /// Stick drift does not change the scheme: a stick counts only on the frame it is pushed past half
    /// its travel, and not while it is held, so walking with a stick does not take the mouse's place.
    /// </summary>
    public static void RefreshLastActiveDevice()
    {
        Gamepad gp = CurrentGamepad;
        if (gp != null)
        {
            if (gp.aButton.wasPressedThisFrame || gp.bButton.wasPressedThisFrame
                || gp.xButton.wasPressedThisFrame || gp.yButton.wasPressedThisFrame)
            {
                LastActiveDevice = GameInputDevice.Gamepad;
                return;
            }

            if (gp.leftShoulder.wasPressedThisFrame || gp.rightShoulder.wasPressedThisFrame)
            {
                LastActiveDevice = GameInputDevice.Gamepad;
                return;
            }

            if (gp.leftStickButton.wasPressedThisFrame || gp.rightStickButton.wasPressedThisFrame)
            {
                LastActiveDevice = GameInputDevice.Gamepad;
                return;
            }

            if (gp.selectButton.wasPressedThisFrame || gp.startButton.wasPressedThisFrame)
            {
                LastActiveDevice = GameInputDevice.Gamepad;
                return;
            }

            var dpad = gp.dpad;
            if (dpad.up.wasPressedThisFrame || dpad.down.wasPressedThisFrame
                || dpad.left.wasPressedThisFrame || dpad.right.wasPressedThisFrame)
            {
                LastActiveDevice = GameInputDevice.Gamepad;
                return;
            }

            // The triggers and the sticks too: after a click the attack trigger did not attack, and the
            // right stick did not turn the view, until another of the pad's buttons was pressed.
            bool sticksOut = gp.leftStick.ReadValue().sqrMagnitude > StickTakesOver * StickTakesOver
                || gp.rightStick.ReadValue().sqrMagnitude > StickTakesOver * StickTakesOver;
            bool stickPushed = sticksOut && !sticksWereOut;
            sticksWereOut = sticksOut;
            if (gp.leftTrigger.wasPressedThisFrame || gp.rightTrigger.wasPressedThisFrame || stickPushed)
            {
                LastActiveDevice = GameInputDevice.Gamepad;
                return;
            }
        }

        Mouse m = CurrentMouse;
        if (m != null)
        {
            if (m.leftButton.wasPressedThisFrame || m.leftButton.wasReleasedThisFrame
                || m.rightButton.wasPressedThisFrame || m.rightButton.wasReleasedThisFrame
                || m.middleButton.wasPressedThisFrame || m.middleButton.wasReleasedThisFrame
                // the two side buttons, which a player can bind (mod 320)
                || m.backButton.wasPressedThisFrame || m.forwardButton.wasPressedThisFrame)
            {
                LastActiveDevice = GameInputDevice.MouseKeyboard;
                return;
            }

            Vector2 scroll = m.scroll.ReadValue();
            if (Mathf.Abs(scroll.x) > 0.01f || Mathf.Abs(scroll.y) > 0.01f)
            {
                LastActiveDevice = GameInputDevice.MouseKeyboard;
                return;
            }
        }

        Keyboard kb = CurrentKeyboard;
        // F1 opens the map of the device used before it, so it does not count as using the keyboard
        if (kb != null && kb.anyKey.wasPressedThisFrame && !GameActions.KeysWorld.Controls.WasPressedThisFrame())
        {
            LastActiveDevice = GameInputDevice.MouseKeyboard;
        }
    }

    /// <summary>
    /// A keyboard action pressed this frame, unless the keyboard is writing - typing runes or on the on-screen
    /// keyboard (<see cref="EControlMask.Keyboard"/>).
    /// </summary>
    public static bool KeyPressedThisFrame(InputAction action)
    {
        return !IsGameplayKeyboardInputBlocked() && action.WasPressedThisFrame();
    }

    private static int escapeConsumedFrame = -1;
    private static int escapeAlsoConsumedFrame = -1;

    /// <summary>
    /// Called by a dialog that has just closed itself on Esc, so that the readers after it in the same
    /// frame do not take the same press as their own: closing the "how many?" box opened the save
    /// panel, or closed the inventory behind it.
    /// </summary>
    public static void ConsumeEscape()
    {
        escapeConsumedFrame = Time.frameCount;
    }

    /// <summary>
    /// As <see cref="ConsumeEscape"/>, and for the next frame too: the on-screen keyboard can take Esc from IMGUI's
    /// key event, which may come a frame before the Input System reports the press; then the press, reported once
    /// the keyboard has gone, closed the map behind a note being written.
    /// </summary>
    public static void ConsumeEscapeThroughNextFrame()
    {
        escapeConsumedFrame = Time.frameCount;
        escapeAlsoConsumedFrame = Time.frameCount + 1;
    }

    public static bool EscapePressedThisFrame()
    {
        if (escapeConsumedFrame == Time.frameCount || escapeAlsoConsumedFrame == Time.frameCount)
            return false;

        foreach (InputDevice d in InputSystem.devices)
        {
            if (d is Keyboard k && k.escapeKey.wasPressedThisFrame)
                return true;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        return UnityEngine.Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }
}

public enum GameInputDevice
{
    Gamepad,
    MouseKeyboard
}
