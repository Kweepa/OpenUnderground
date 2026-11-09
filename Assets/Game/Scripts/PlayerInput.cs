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

    public const string PadLookSpeedKey = "Options_PadLookSpeed";
    public const float DefaultPadLookSpeed = 1f;
    public const float MinPadLookSpeed = 0.5f;
    public const float MaxPadLookSpeed = 2f;

    /// <summary>
    /// How quickly the right stick turns the view, as a share of the 180 degrees a second across and 90 up and down
    /// it has always turned at: one number, so the two keep their ratio.
    /// </summary>
    public static float PadLookSpeed =>
        Mathf.Clamp(PlayerPrefs.GetFloat(PadLookSpeedKey, DefaultPadLookSpeed), MinPadLookSpeed, MaxPadLookSpeed);

    public const string FieldOfViewKey = "Options_FieldOfView";
    public const int DefaultFieldOfView = 92;
    public const int MinFieldOfView = 75;
    public const int MaxFieldOfView = 120;

    /// <summary>The field of view the player chose, in degrees across the screen, the way it is usually quoted.</summary>
    public static float FieldOfView =>
        Mathf.Clamp(PlayerPrefs.GetInt(FieldOfViewKey, DefaultFieldOfView), MinFieldOfView, MaxFieldOfView);

    /// <summary>
    /// The camera's vertical field for FieldOfView on a screen of this width over height. From 16:9 to 4:3 the width is
    /// the one chosen and a narrower screen sees more height: at 92, 60.4 degrees vertical on 16:9, 65.8 on 16:10, 75.7
    /// on 4:3. A screen wider than 16:9 keeps 16:9's height and sees more at the sides; one narrower than 4:3 keeps
    /// 4:3's height and sees less across, so a window squeezed tall never turns into a fisheye.
    /// </summary>
    /// <remarks>
    /// Before this setting the vertical field was 60 on every screen, so a 16:10 screen saw 85 degrees across and a 4:3
    /// one 75, against 91 on 16:9.
    /// </remarks>
    public static float VerticalFieldOfView(float aspect)
    {
        float kept = Mathf.Clamp(aspect, 4f / 3f, 16f / 9f);
        return 2f * Mathf.Rad2Deg * Mathf.Atan(Mathf.Tan(0.5f * FieldOfView * Mathf.Deg2Rad) / kept);
    }

    public const string CameraBobKey = "Options_CameraBob";
    public const int DefaultCameraBob = 100;
    public const int MaxCameraBob = 200;

    /// <summary>
    /// How deep the view dips at each step when walking, as a share of PlayerObject.cameraBobAmplitude:
    /// 1 the remake's usual bob, 0 none, 2 at most. The water's and the flight's bobs do not depend on it.
    /// </summary>
    public static float CameraBob =>
        Mathf.Clamp(PlayerPrefs.GetInt(CameraBobKey, DefaultCameraBob), 0, MaxCameraBob) / 100f;

    public const float DefaultEffectsVolume = 1f;

    public static float EffectsVolume =>
        PlayerPrefs.GetFloat("Options_EffectsVolume", DefaultEffectsVolume);

    public static bool UseKeyAutomatically => PlayerPrefs.GetInt("Options_UseKeyAutomatically", 1) == 1;

    public static bool AutoJump => PlayerPrefs.GetInt("Options_AutoJump", 0) == 1;

    /// <summary>
    /// Whether the pad gives its short rumble when the player runs at a ledge (PlayerObject.UpdateJumpIndicator()).
    /// On unless turned off; the pad's other rumbles - blows, doors, shrines - do not depend on it.
    /// </summary>
    public static bool LedgeRumble => PlayerPrefs.GetInt("Options_LedgeRumble", 1) == 1;

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
    /// It ships at ten seconds, the user's choice of 31 October 2026 (it shipped at zero before, the
    /// tracks back to back), and goes up to five minutes; a larger number kept from the old slider,
    /// which went to 400, is read as the top.
    /// The name says Max because the option is a length rather than a switch, and because the
    /// switch it replaced held 0 or 1 under a shorter name: read as seconds, a stale 1 would mean
    /// one second of quiet, which looks like the feature is broken rather than turned off.
    /// </remarks>
    public static float AmbientSilenceMax => Mathf.Min(PlayerPrefs.GetInt("Options_AmbientSilenceMax", DefaultAmbientSilenceMax), MaxAmbientSilence);

    public const int DefaultAmbientSilenceMax = 10;
    public const int MaxAmbientSilence = 300;

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

    /// <summary>
    /// Whether the Search, Traps and Track skills roll by themselves every few seconds and report a
    /// hidden door, a trapped object or the creatures nearby. On unless the player turns it off. Off,
    /// Search and Traps roll only when the player looks, which is what the original does.
    /// </summary>
    public static bool AutomaticSearch => PlayerPrefs.GetInt("Options_AutomaticSearch", 1) == 1;

    /// <summary>
    /// Whether the player can sprint, with the stamina gauge under the worn spells. On unless the
    /// player turns it off. Off, there is no sprint and no gauge, as in the original.
    /// </summary>
    public static bool Sprint => PlayerPrefs.GetInt("Options_Sprint", 1) == 1;

    /// <summary>
    /// Whether a pile of debris may hide a few items that a Search roll finds. On unless the player
    /// turns it off. Off, debris holds nothing, as in the original.
    /// </summary>
    public static bool LootInDebris => PlayerPrefs.GetInt("Options_LootInDebris", 1) == 1;

    /// <summary>Movement in local stick space: x = strafe, y = forward (matches gamepad left stick).</summary>
    /// <remarks>The keyboard wins whenever one of its movement keys is down.</remarks>
    public static Vector2 ReadMoveVector()
    {
        Vector2 keys = ReadKeyboardMove(out bool keysHeld);
        return keysHeld ? keys : GameActions.PadWorld.Move.ReadValue<Vector2>();
    }

    /// <summary>
    /// The keyboard's movement keys as one vector, each axis -1, 0 or 1 and normalised on the diagonal: W, S, A and
    /// D, plus the arrows.
    /// </summary>
    public static Vector2 ReadKeyboardMove(out bool hasInput)
    {
        Vector2 v = GameActions.KeysWorld.Move.ReadValue<Vector2>();
        // Move forward quietly is forward, and PlayerObject slows it as the Walk quietly modifier does (mod 1080)
        if (WalkSlowlyHeld())
        {
            v.y = 1f;
        }
        Vector2 arrows = GameActions.KeysWorld.MoveAlternate.ReadValue<Vector2>();
        v = new Vector2(Mathf.Clamp(v.x + arrows.x, -1f, 1f), Mathf.Clamp(v.y + arrows.y, -1f, 1f));

        hasInput = v.x != 0f || v.y != 0f;
        if (v.sqrMagnitude > 1f)
        {
            v.Normalize();
        }

        return v;
    }

    /// <summary>The original's S held: forward at the slow walk (mod 1080; no key unless the player gives it one).</summary>
    public static bool WalkSlowlyHeld()
    {
        return GameActions.KeysWorld.WalkSlowly.IsPressed();
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

    /// <summary>Keyboard F9 by default — use the Track skill, the original's key for it (UW.EXE 0x7ebb6).</summary>
    public static bool TrackKeyPressed()
    {
        return GameActions.KeysWorld.Track.WasPressedThisFrame();
    }

    /// <summary>Gamepad D-pad right by default — use the Track skill.</summary>
    /// <remarks>
    /// A press, not a hold as for sleeping: the skill only reports, so using it by accident costs
    /// nothing.
    /// </remarks>
    public static bool TrackButtonPressed()
    {
        return GameActions.PadWorld.Track.WasPressedThisFrame();
    }

    /// <summary>Gamepad D-pad left held down — sleep, which wants a hold.</summary>
    /// <remarks>
    /// During play the d-pad's up and down scroll the message log, and right uses Track. It answers
    /// to a hold, as the quicksave does, because sleeping is not something to do by accident. The
    /// caller times the hold; this only says whether it is down.
    /// </remarks>
    public static bool SleepButtonHeld()
    {
        return GameActions.PadWorld.Sleep.IsPressed();
    }
}
