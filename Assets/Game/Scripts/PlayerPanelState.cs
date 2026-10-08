/// <summary>
/// Which full-screen player panel is active (magic vs inventory). Mutually exclusive.
/// </summary>
public enum EPlayerPanel
{
    None,
    Magic,
    Inventory
}

/// <summary>
/// Single source of truth for magic/inventory panel toggles (keyboard + gamepad).
/// </summary>
public static class PlayerPanelState
{
    public static EPlayerPanel ActivePanel { get; private set; }

    /// <summary>
    /// Gap between a panel (inventory, magic, stats) and the screen's top and side edges: the
    /// same as between the compass and the bottom edge, so the frame is even all round.
    /// </summary>
    public const int PanelMargin = 12;

    public static bool IsExploringMagic => ActivePanel == EPlayerPanel.Magic;
    public static bool IsExploringInventory => ActivePanel == EPlayerPanel.Inventory;

    /// <summary>Magic/inventory/stats panels are disabled on level 9 (ethereal void).</summary>
    public static bool ArePanelsAvailable =>
        LevelLoader.sLevelLoader != null && LevelLoader.sLevelLoader.loadedLevel != 9;

    public static bool IsEffectivelyExploringMagic =>
        ArePanelsAvailable && IsExploringMagic;

    public static bool IsEffectivelyExploringInventory =>
        ArePanelsAvailable && IsExploringInventory;

    public static void SetPanel(EPlayerPanel panel, bool fromUserToggle = true)
    {
        EPlayerPanel previous = ActivePanel;
        if (panel != EPlayerPanel.None && panel != ActivePanel)
        {
            PlayerPanelInput.ResetWalkDismissTimer();
        }

        ActivePanel = panel;
        TutorialManager.NotifyPanelChanged(previous, panel, fromUserToggle);
    }
}
