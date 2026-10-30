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
    /// Stick drift does not change the scheme (only buttons, d-pad, mouse, keys, wheel).
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
        }

        Mouse m = CurrentMouse;
        if (m != null)
        {
            if (m.leftButton.wasPressedThisFrame || m.leftButton.wasReleasedThisFrame
                || m.rightButton.wasPressedThisFrame || m.rightButton.wasReleasedThisFrame
                || m.middleButton.wasPressedThisFrame || m.middleButton.wasReleasedThisFrame)
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
        if (kb != null && kb.anyKey.wasPressedThisFrame)
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

    /// <summary>
    /// Called by a dialog that has just closed itself on Esc, so that the readers after it in the same
    /// frame do not take the same press as their own: closing the "how many?" box opened the save
    /// panel, or closed the inventory behind it.
    /// </summary>
    public static void ConsumeEscape()
    {
        escapeConsumedFrame = Time.frameCount;
    }

    public static bool EscapePressedThisFrame()
    {
        if (escapeConsumedFrame == Time.frameCount)
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
