using UnityEngine;

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
    /// It ships at zero, so the music plays the way it always has until the player asks for the
    /// quiet.
    /// The name says Max because the option is a length rather than a switch, and because the
    /// switch it replaced held 0 or 1 under a shorter name: read as seconds, a stale 1 would mean
    /// one second of quiet, which looks like the feature is broken rather than turned off.
    /// </remarks>
    public static float AmbientSilenceMax => PlayerPrefs.GetInt("Options_AmbientSilenceMax", 0);

    /// <summary>
    /// Whether Resist Blows, Thick Skin and Iron Flesh soak 1, 2 and 3 points of every blow, cast
    /// or worn. Off, they soak nothing, which is what the original does. On unless the player
    /// turns it off.
    /// </summary>
    public static bool ShieldSpellsSoak => PlayerPrefs.GetInt("Options_ShieldSpellsSoak", 1) == 1;

    /// <summary>
    /// Whether Flameproof and Poison Resistance let a third of their damage through, and the
    /// poison hurts one point at a time over the minute. Off, the two stop their damage outright
    /// and the poison hurts once a minute, which is what the original does. On unless the player
    /// turns it off.
    /// </summary>
    public static bool PartialResistances => PlayerPrefs.GetInt("Options_PartialResistances", 1) == 1;

    /// <summary>
    /// Whether a creature that a trap makes again, from its second copy on, dies with no experience
    /// and no loot. Only traps the player sets off count, through a button, a lever or a step; the
    /// creatures the dungeon brings back by itself are worth fighting as before. Off, every copy
    /// pays, which is what the original does. On unless the player turns it off.
    /// </summary>
    public static bool RewardOnlyFirstTrapCopy => PlayerPrefs.GetInt("Options_RewardOnlyFirstTrapCopy", 1) == 1;

    /// <summary>
    /// Whether the game saves by itself the first time the character sets foot on a level. On
    /// unless the player turns it off. Off, only the quicksave and the saves made by hand are written.
    /// </summary>
    public static bool AutoSave => PlayerPrefs.GetInt("Options_AutoSave", 1) == 1;

    /// <summary>Movement in local stick space: x = strafe, y = forward (matches gamepad left stick).</summary>
    /// <remarks>The keyboard wins whenever one of its movement keys is down.</remarks>
    public static Vector2 ReadMoveVector()
    {
        Vector2 keys = ReadKeyboardMove(withArrows: true, out bool keysHeld);
        return keysHeld ? keys : GameActions.PadWorld.Move.ReadValue<Vector2>();
    }

    /// <summary>
    /// The keyboard's movement keys as one vector, each axis -1, 0 or 1 and normalised on the diagonal: W, S, A and
    /// D, plus the arrows unless a panel has them for its cursor.
    /// </summary>
    public static Vector2 ReadKeyboardMove(bool withArrows, out bool hasInput)
    {
        Vector2 v = GameActions.KeysWorld.Move.ReadValue<Vector2>();
        if (withArrows)
        {
            Vector2 arrows = GameActions.KeysWorld.MoveAlternate.ReadValue<Vector2>();
            v = new Vector2(Mathf.Clamp(v.x + arrows.x, -1f, 1f), Mathf.Clamp(v.y + arrows.y, -1f, 1f));
        }

        hasInput = v.x != 0f || v.y != 0f;
        if (v.sqrMagnitude > 1f)
        {
            v.Normalize();
        }

        return v;
    }

    /// <summary>True when mouse look should apply (FPS mode).</summary>
    public static bool ShouldApplyMouseLook()
    {
        return GameInput.CurrentMouse != null && Cursor.lockState == CursorLockMode.Locked;
    }

    /// <summary>
    /// Left Shift held while the magic or inventory panel is open or a spell waits for its target: the mouse
    /// turns the view instead of moving the pointer (<see cref="GameplayCursorPolicy.Apply"/>).
    /// </summary>
    public static bool PanelMouseLookHeld()
    {
        return GameActions.KeysWorld.MouseLook.IsPressed();
    }

    /// <summary>Keyboard F5 — write a quicksave, at once.</summary>
    /// <remarks>
    /// F5 is the one convention there is, and it is a keyboard one: it is the quicksave key in most
    /// PC games that have quicksaving at all.
    /// </remarks>
    public static bool QuickSaveKeyPressed()
    {
        return GameActions.KeysWorld.Quicksave.WasPressedThisFrame();
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
        return GameActions.PadWorld.Quicksave.IsPressed();
    }

    /// <summary>Keyboard F10 — sleep, the original's key for it (UW.EXE 0x82345).</summary>
    public static bool SleepKeyPressed()
    {
        return GameActions.KeysWorld.Sleep.WasPressedThisFrame();
    }

    /// <summary>Gamepad D-pad left held down — sleep, which wants a hold.</summary>
    /// <remarks>
    /// During play the d-pad's left and right are the only inputs nothing else reads; up and down
    /// scroll the message log. It answers to a hold, as the quicksave does, because sleeping is not
    /// something to do by accident. The caller times the hold; this only says whether it is down.
    /// </remarks>
    public static bool SleepButtonHeld()
    {
        return GameActions.PadWorld.Sleep.IsPressed();
    }
}
