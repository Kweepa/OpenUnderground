using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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

    public static bool SelectPressedThisFrame() => CurrentGamepad?.selectButton.wasPressedThisFrame ?? false;

    public static bool DpadOrArrowLeftPressedThisFrame()
    {
        return (CurrentGamepad?.dpad.left.wasPressedThisFrame ?? false)
               || (!IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.leftArrowKey.wasPressedThisFrame ?? false));
    }

    public static bool DpadOrArrowRightPressedThisFrame()
    {
        return (CurrentGamepad?.dpad.right.wasPressedThisFrame ?? false)
               || (!IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.rightArrowKey.wasPressedThisFrame ?? false));
    }

    public static bool DpadOrArrowUpPressedThisFrame()
    {
        return (CurrentGamepad?.dpad.up.wasPressedThisFrame ?? false)
               || (!IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.upArrowKey.wasPressedThisFrame ?? false));
    }

    public static bool DpadOrArrowDownPressedThisFrame()
    {
        return (CurrentGamepad?.dpad.down.wasPressedThisFrame ?? false)
               || (!IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.downArrowKey.wasPressedThisFrame ?? false));
    }

    public static bool MagicRuneSelectPressedThisFrame()
    {
        return (CurrentGamepad?.aButton.wasPressedThisFrame ?? false)
               || (!IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.enterKey.wasPressedThisFrame ?? false));
    }

    public static bool MagicClearRunesPressedThisFrame()
    {
        return (CurrentGamepad?.bButton.wasPressedThisFrame ?? false)
               || (!IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.bKey.wasPressedThisFrame ?? false));
    }

    public static bool MagicCastKeyboardConfirmPressedThisFrame()
    {
        return !IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.cKey.wasPressedThisFrame ?? false);
    }

    public static bool EscapePressedThisFrame()
    {
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

    public static bool TabPressedThisFrame()
    {
        return !IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.tabKey.wasPressedThisFrame ?? false);
    }

    public static bool Digit1PressedThisFrame() =>
        !IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.digit1Key.wasPressedThisFrame ?? false);
    public static bool Digit2PressedThisFrame() =>
        !IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.digit2Key.wasPressedThisFrame ?? false);
    public static bool Digit3PressedThisFrame() =>
        !IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.digit3Key.wasPressedThisFrame ?? false);

    public static bool QKeyPressedThisFrame() =>
        !IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.qKey.wasPressedThisFrame ?? false);
    public static bool EKeyPressedThisFrame() =>
        !IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.eKey.wasPressedThisFrame ?? false);
    public static bool RKeyPressedThisFrame() =>
        !IsGameplayKeyboardInputBlocked() && (CurrentKeyboard?.rKey.wasPressedThisFrame ?? false);

    public static bool ShoulderLeftPressedThisFrame() => CurrentGamepad?.leftShoulder.wasPressedThisFrame ?? false;
    public static bool ShoulderRightPressedThisFrame() => CurrentGamepad?.rightShoulder.wasPressedThisFrame ?? false;

    /// <summary>Stats panel: primary trigger for held-to-show (handedness from <see cref="PlayerData"/>).</summary>
    public static AxisControl StatsTrigger()
    {
        if (CurrentGamepad == null || PlayerData.sData == null)
            return null;
        return PlayerData.sData.leftHanded ? CurrentGamepad.rightTrigger : CurrentGamepad.leftTrigger;
    }
}

public enum GameInputDevice
{
    Gamepad,
    MouseKeyboard
}
