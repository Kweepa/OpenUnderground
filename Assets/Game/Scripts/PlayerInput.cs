using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Merges gamepad and keyboard/mouse input for player movement and actions.
/// </summary>
public static class PlayerInput
{
    public const float DefaultMouseLookSpeed = 1f;
    public const float MinMouseLookSpeed = 0.8f;
    public const float MaxMouseLookSpeed = 2.2f;

    public static float MouseLookSpeed =>
        Mathf.Clamp(PlayerPrefs.GetFloat("Options_MouseLookSpeed", DefaultMouseLookSpeed),
                    MinMouseLookSpeed, MaxMouseLookSpeed);

    public const float DefaultEffectsVolume = 1f;

    public static float EffectsVolume =>
        PlayerPrefs.GetFloat("Options_EffectsVolume", DefaultEffectsVolume);

    public static bool UseKeyAutomatically => PlayerPrefs.GetInt("Options_UseKeyAutomatically", 1) == 1;

    public static bool AutoJump => PlayerPrefs.GetInt("Options_AutoJump", 0) == 1;

    /// <summary>
    /// Whether opening the automap leaves the music playing instead of starting the map's own
    /// track. On unless the player turns it off, the same as the silence below.
    /// </summary>
    public static bool KeepMusicOnMap => PlayerPrefs.GetInt("Options_KeepMusicOnMap", 1) == 1;

    /// <summary>
    /// The longest stretch of quiet left between two ambient tracks, in seconds. Zero means none,
    /// and the tracks run back to back the way they used to.
    /// </summary>
    /// <remarks>
    /// A key that has never been written reads as its default, so a player carrying preferences
    /// over from an older build gets the quiet without having to go and find the control.
    /// The name says Max because the option is a length rather than a switch, and because the
    /// switch it replaced held 0 or 1 under a shorter name: read as seconds, a stale 1 would mean
    /// one second of quiet, which looks like the feature is broken rather than turned off.
    /// </remarks>
    public static float AmbientSilenceMax => PlayerPrefs.GetInt("Options_AmbientSilenceMax", 300);

    /// <summary>Movement in local stick space: x = strafe, y = forward (matches gamepad left stick).</summary>
    public static Vector2 ReadMoveVector()
    {
        Vector2 v = GameInput.CurrentGamepad != null ? GameInput.CurrentGamepad.leftStick.ReadValue() : Vector2.zero;

        Keyboard kb = GameInput.CurrentKeyboard;
        if (kb != null)
        {
            float x = 0f, y = 0f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;
            if (x != 0f || y != 0f)
            {
                v = new Vector2(x, y);
                if (v.sqrMagnitude > 1f)
                    v.Normalize();
            }
        }

        return v;
    }

    /// <summary>True when mouse look should apply (FPS mode).</summary>
    public static bool ShouldApplyMouseLook()
    {
        return GameInput.CurrentMouse != null && Cursor.lockState == CursorLockMode.Locked;
    }

    public static bool JumpReleasedThisFrame()
    {
        bool g = GameInput.CurrentGamepad?.aButton.wasReleasedThisFrame ?? false;
        bool k = GameInput.CurrentKeyboard?.spaceKey.wasReleasedThisFrame ?? false;
        return g || k;
    }

    public static bool JumpHeld()
    {
        bool g = GameInput.CurrentGamepad?.aButton.isPressed ?? false;
        bool k = GameInput.CurrentKeyboard?.spaceKey.isPressed ?? false;
        return g || k;
    }

    public static bool SprintModifierHeld()
    {
        return GameInput.CurrentGamepad?.leftStickButton.isPressed ?? false
               || (GameInput.CurrentKeyboard?.leftShiftKey.isPressed ?? false);
    }

    public static bool GamepadAttackTriggerPressedThisFrame()
    {
        var gp = GameInput.CurrentGamepad;
        if (gp == null || PlayerData.sData == null)
        {
            return false;
        }

        var t = PlayerData.sData.leftHanded ? gp.leftTrigger : gp.rightTrigger;
        return t.wasPressedThisFrame;
    }

    public static bool AttackTriggerPressed()
    {
        if (GamepadAttackTriggerPressedThisFrame())
        {
            return true;
        }

        return GameInput.CurrentMouse?.rightButton.wasPressedThisFrame ?? false;
    }

    public static bool AttackTriggerHeld()
    {
        var gp = GameInput.CurrentGamepad;
        if (gp != null && PlayerData.sData != null)
        {
            var t = PlayerData.sData.leftHanded ? gp.leftTrigger : gp.rightTrigger;
            if (t.isPressed)
                return true;
        }
        return GameInput.CurrentMouse?.rightButton.isPressed ?? false;
    }

    public static bool CancelOrBlockPressed()
    {
        return (GameInput.CurrentGamepad?.bButton.wasPressedThisFrame ?? false)
               || (GameInput.CurrentKeyboard?.bKey.wasPressedThisFrame ?? false);
    }

    public static bool CancelOrBlockHeld()
    {
        return (GameInput.CurrentGamepad?.bButton.isPressed ?? false)
               || (GameInput.CurrentKeyboard?.bKey.isPressed ?? false);
    }

    /// <summary>Gamepad X or keyboard F — look (mouse: short LMB release / F).</summary>
    public static bool InteractLookPressed()
    {
        return (GameInput.CurrentGamepad?.xButton.wasPressedThisFrame ?? false)
               || (GameInput.CurrentKeyboard?.fKey.wasPressedThisFrame ?? false);
    }

    /// <summary>Keyboard F5 — write a quicksave, at once.</summary>
    /// <remarks>
    /// F5 is the one convention there is, and it is a keyboard one: it is the quicksave key in most
    /// PC games that have quicksaving at all.
    /// </remarks>
    public static bool QuickSaveKeyPressed()
    {
        return GameInput.CurrentKeyboard?.f5Key.wasPressedThisFrame ?? false;
    }

    /// <summary>Gamepad right stick click (R3) held down — the quicksave button, which wants a hold.</summary>
    /// <remarks>
    /// On a gamepad there is no convention to follow - one game puts quicksave on a face button
    /// inside the pause menu, another on a shoulder and Select together, and plenty have none - so
    /// the button here is chosen by what is free. Every face button, both shoulders, both triggers,
    /// Start, Select, the D-pad and the left stick click already do something in this game; the
    /// right stick click does not.
    ///
    /// It answers to a hold rather than a click, because the thumb rests on that stick the whole
    /// time the player is looking around and a click is far too easy to make by accident. The
    /// caller times the hold; this only says whether the button is down.
    /// </remarks>
    public static bool QuickSaveButtonHeld()
    {
        return GameInput.CurrentGamepad?.rightStickButton.isPressed ?? false;
    }

    /// <summary>Gamepad Y — use (mouse uses LMB hold / drag). Keyboard has no use binding.</summary>
    public static bool InteractUsePressed()
    {
        return GameInput.CurrentGamepad?.yButton.wasPressedThisFrame ?? false;
    }
}
