using UnityEngine;

/// <summary>
/// Gameplay hides the OS cursor (<see cref="SoftwareCursorOverlay"/> draws the in-game pointer) and toggles
/// <see cref="Cursor.lockState"/> (free mouse vs mouselook). The title menu uses <see cref="ApplyMenu"/> instead.
/// </summary>
public static class GameplayCursorPolicy
{
    /// <summary>
    /// Restores the hardware pointer for title-menu navigation (death / win return to <c>World</c>, startup, etc.).
    /// </summary>
    public static void ApplyMenu()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    /// <summary>
    /// <see cref="PlayerObject.controlsDisabled"/> bits that mean mouse-driven UI is active. They are excluded from
    /// the legacy &quot;blocked&quot; early-out (which preserved locked cursor for cutscenes etc.) and force the
    /// unlocked-cursor branch. Add bits here when introducing new IMGUI modes that need a free pointer.
    /// </summary>
    public const EControlMask CursorFreeUiMask =
        EControlMask.Conversation
        | EControlMask.HowMany
        | EControlMask.RepairDialog
        | EControlMask.Map
        | EControlMask.SaveLoad
        | EControlMask.Warning;

    /// <summary>
    /// Applies cursor lock state; keeps hardware cursor invisible so <see cref="SoftwareCursorOverlay"/> is the pointer.
    /// </summary>
    public static void Apply(PlayerObject player)
    {
        if (player == null)
        {
            return;
        }

        Cursor.visible = false;

        if (MapScreen.IsMapScreenVisible())
        {
            Cursor.lockState = CursorLockMode.None;
            return;
        }

        if ((player.controlsDisabled & EControlMask.SaveLoad) != 0)
        {
            Cursor.lockState = CursorLockMode.None;
            return;
        }

        if ((player.controlsDisabled & EControlMask.Keyboard) != 0)
        {
            Cursor.lockState = CursorLockMode.None;
            return;
        }

        // Roaming Sight uses FPS-style mouselook; ignore that mask here so we still set Locked (see RoamingSightControls).
        EControlMask ignoredForBlocked =
            EControlMask.Magic | EControlMask.Inventory | EControlMask.RoamingSight | CursorFreeUiMask;
        bool blocked = !player.controlsActive && (player.controlsDisabled & ~ignoredForBlocked) != 0;
        if (blocked)
        {
            return;
        }

        bool mouseKeyboardUi = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;
        bool unlockForMagicInventoryPanels =
            (mouseKeyboardUi && PlayerPanelState.IsEffectivelyExploringMagic)
            || (mouseKeyboardUi && PlayerPanelState.IsEffectivelyExploringInventory)
            || (StatsPanel.sStatsPanel != null && StatsPanel.sStatsPanel.ShouldDismissWithEscape())
            || (Magic.sMagic != null && Magic.sMagic.HasMousePrimedSpellAwaitingAim);
        EControlMask cursorFreeMask = CursorFreeUiMask;
        if (!mouseKeyboardUi)
        {
            cursorFreeMask &= ~EControlMask.HowMany;
        }

        bool unlockFromUiMask = (player.controlsDisabled & cursorFreeMask) != 0;

        // Left Shift held over the panels or a primed spell gives the mouse back to the view, so the player
        // can turn while aiming; a click then aims at the centre crosshair. Released, the pointer comes back
        // on the crosshair.
        bool wasPanelMouseLook = panelMouseLook;
        panelMouseLook = unlockForMagicInventoryPanels && !unlockFromUiMask && PlayerInput.PanelMouseLookHeld();
        if (panelMouseLook)
        {
            unlockForMagicInventoryPanels = false;
        }

        if (unlockForMagicInventoryPanels || unlockFromUiMask)
        {
            Cursor.lockState = CursorLockMode.None;
            // Where the unlock leaves the pointer is not quite the centre, and the mouse position may not catch
            // up until the mouse moves: it is put on the crosshair, and again on the next frame in case the
            // unlock lands after the first warp.
            if (wasPanelMouseLook || warpPointerAgain)
            {
                SoftwareCursorOverlay.WarpPointerOntoCrosshair();
                warpPointerAgain = wasPanelMouseLook;
            }
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            warpPointerAgain = false;
        }
    }

    /// <summary>True while left Shift holds the cursor locked over the panels or a primed spell.</summary>
    private static bool panelMouseLook;

    /// <summary>The pointer was put on the crosshair last frame, when the Shift lock ended: once more.</summary>
    private static bool warpPointerAgain;
}
