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
        | EControlMask.SaveLoad;

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

        if (unlockForMagicInventoryPanels || unlockFromUiMask)
        {
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
        }
    }
}
