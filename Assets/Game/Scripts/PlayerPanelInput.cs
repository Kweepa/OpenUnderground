using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keyboard Q/E/R magic/inventory/stats; gamepad shoulders; map on Tab.
/// Runs before Inventory/Magic (execution order).
/// </summary>
[DefaultExecutionOrder(-200)]
public class PlayerPanelInput : MonoBehaviour
{
    private const float WalkDismissDuration = 3f;
    private float continuousWalkDismissTime;

    private static PlayerPanelInput sInstance;

    private void Awake()
    {
        sInstance = this;
    }

    private void OnDestroy()
    {
        if (sInstance == this)
        {
            sInstance = null;
        }
    }

    public static void ResetWalkDismissTimer()
    {
        if (sInstance != null)
        {
            sInstance.continuousWalkDismissTime = 0f;
        }
    }

    private void Update()
    {
        if (PlayerObject.Player == null || PlayerData.sData == null)
        {
            return;
        }
        if (LevelLoader.sLevelLoader == null || LevelLoader.sLevelLoader.loadedLevel == 0)
        {
            return;
        }
        if (PlayerData.sData.dead)
        {
            return;
        }

        if ((PlayerObject.Player.controlsDisabled & EControlMask.SaveLoad) != 0)
        {
            continuousWalkDismissTime = 0f;
            GameplayCursorPolicy.Apply(PlayerObject.Player);
            return;
        }

        if ((PlayerObject.Player.controlsDisabled & EControlMask.Keyboard) != 0)
        {
            continuousWalkDismissTime = 0f;
            GameplayCursorPolicy.Apply(PlayerObject.Player);
            return;
        }

        if (MapScreen.IsMapScreenVisible())
        {
            continuousWalkDismissTime = 0f;
            if (GameInput.EscapePressedThisFrame())
            {
                // MapScreen handles close via select/B; Esc closes map — trigger same path
                MapScreen.RequestCloseFromKeyboard();
            }

            GameplayCursorPolicy.Apply(PlayerObject.Player);
            return;
        }

        if (GameInput.EscapePressedThisFrame() && DismissActivePlayerPanels())
        {
            GameplayCursorPolicy.Apply(PlayerObject.Player);
            return;
        }

        if (GameInput.EscapePressedThisFrame()
            && (KeyboardGUI.sKeyboard == null || !KeyboardGUI.sKeyboard.IsVisible())
            && !IsModalInventoryUiBlocking())
        {
            SaveLoadGUI sl = SaveLoadGUI.InstanceOrFind();
            if (sl != null && sl.TryOpenFromGame())
            {
                GameplayCursorPolicy.Apply(PlayerObject.Player);
                return;
            }
        }

        if ((Gamepad.current?.startButton.wasPressedThisFrame ?? false)
            && (KeyboardGUI.sKeyboard == null || !KeyboardGUI.sKeyboard.IsVisible())
            && !IsModalInventoryUiBlocking())
        {
            if (AnyPlayerPanelVisible())
            {
                DismissActivePlayerPanels();
            }

            SaveLoadGUI sl = SaveLoadGUI.InstanceOrFind();
            if (sl != null && sl.TryOpenFromGame())
            {
                GameplayCursorPolicy.Apply(PlayerObject.Player);
                return;
            }
        }

        if (TryCloseActivePanelFromOutsideClick())
        {
            GameplayCursorPolicy.Apply(PlayerObject.Player);
            return;
        }

        if (PlayerInput.GamepadAttackTriggerPressedThisFrame()
            && !IsModalInventoryUiBlocking()
            && DismissActivePlayerPanels())
        {
            GameplayCursorPolicy.Apply(PlayerObject.Player);
            return;
        }

        if (GameInput.ShoulderLeftPressedThisFrame())
        {
            ToggleMagic();
        }
        if (GameInput.ShoulderRightPressedThisFrame())
        {
            ToggleInventory();
        }

        if (GameInput.QKeyPressedThisFrame())
        {
            ToggleMagic();
        }
        if (GameInput.EKeyPressedThisFrame())
        {
            ToggleInventory();
        }
        if (GameInput.RKeyPressedThisFrame())
        {
            ToggleStatsPanel();
        }

        TryDismissPanelsFromContinuousWalk();

        GameplayCursorPolicy.Apply(PlayerObject.Player);
    }

    private static void ToggleMagic()
    {
        if (!PlayerPanelState.ArePanelsAvailable)
        {
            return;
        }

        if (PlayerPanelState.ActivePanel == EPlayerPanel.Magic)
        {
            PlayerPanelState.SetPanel(EPlayerPanel.None);
        }
        else
        {
            StatsPanel.sStatsPanel?.Hide();
            PlayerPanelState.SetPanel(EPlayerPanel.Magic);
        }
    }

    private static void ToggleInventory()
    {
        if (!PlayerPanelState.ArePanelsAvailable)
        {
            return;
        }

        if (PlayerPanelState.ActivePanel == EPlayerPanel.Inventory)
        {
            Inventory.HidePanel();
        }
        else
        {
            StatsPanel.sStatsPanel?.Hide();
            PlayerPanelState.SetPanel(EPlayerPanel.Inventory);
        }
    }

    private static void ToggleStatsPanel()
    {
        if (!PlayerPanelState.ArePanelsAvailable)
        {
            return;
        }

        StatsPanel.sStatsPanel?.TogglePinnedOpen();
    }

    /// <summary>Mouse: right-click outside the magic/inventory/stats panel closes it (same as Esc); left does not dismiss. Gamepad: left- or right-click outside still closes.</summary>
    private static bool TryCloseActivePanelFromOutsideClick()
    {
        bool statsVisible = PlayerPanelState.ArePanelsAvailable
            && StatsPanel.sStatsPanel != null
            && StatsPanel.sStatsPanel.ShouldDismissWithEscape();
        if (!PlayerPanelState.IsEffectivelyExploringMagic
            && !PlayerPanelState.IsEffectivelyExploringInventory
            && !statsVisible)
        {
            return false;
        }
        if (IsModalInventoryUiBlocking())
        {
            return false;
        }
        if (KeyboardGUI.sKeyboard != null && KeyboardGUI.sKeyboard.IsVisible())
        {
            return false;
        }
        if (MapScreen.IsMapScreenVisible())
        {
            return false;
        }

        Mouse m = GameInput.CurrentMouse;
        bool outsideClick = false;
        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
        {
            outsideClick = m?.rightButton.wasPressedThisFrame ?? false;
        }
        if (!outsideClick)
        {
            return false;
        }

        Vector2 gui = GuiInput.ScreenToGuiMouse(m.position.ReadValue());

        if (PlayerPanelState.IsEffectivelyExploringInventory && Inventory.sInv != null)
        {
            if (Conversations.runningConversation != null)
            {
                return false;
            }

            Rect r = Inventory.sInv.GetPanelGuiRectForOutsideClick();
            if (r.width <= 0f || r.height <= 0f)
            {
                return false;
            }
            if (r.Contains(gui))
            {
                return false;
            }
            Inventory.HidePanel();
            return true;
        }

        if (PlayerPanelState.IsEffectivelyExploringMagic && Magic.sMagic != null)
        {
            Rect r = Magic.sMagic.GetPanelGuiRectForOutsideClick();
            if (r.width <= 0f || r.height <= 0f)
            {
                return false;
            }
            if (r.Contains(gui))
            {
                return false;
            }
            Magic.HidePanel();
            return true;
        }

        if (statsVisible && StatsPanel.sStatsPanel != null)
        {
            Rect sr = StatsPanel.sStatsPanel.GetPanelGuiRectForOutsideClick();
            if (sr.width > 0f && sr.height > 0f && !sr.Contains(gui))
            {
                StatsPanel.sStatsPanel.Hide();
                return true;
            }
        }

        return false;
    }

    private static bool IsModalInventoryUiBlocking()
    {
        PlayerObject player = PlayerObject.Player;
        return player != null
            && (player.controlsDisabled & (EControlMask.HowMany | EControlMask.RepairDialog)) != 0;
    }

    private static bool AnyPlayerPanelVisible()
    {
        return PlayerPanelState.IsEffectivelyExploringInventory
            || PlayerPanelState.IsEffectivelyExploringMagic
            || (PlayerPanelState.ArePanelsAvailable
                && StatsPanel.sStatsPanel != null
                && StatsPanel.sStatsPanel.ShouldDismissWithEscape());
    }

    private void TryDismissPanelsFromContinuousWalk()
    {
        if (!AnyPlayerPanelVisible())
        {
            continuousWalkDismissTime = 0f;
            return;
        }
        if (IsModalInventoryUiBlocking())
        {
            continuousWalkDismissTime = 0f;
            return;
        }
        if (KeyboardGUI.sKeyboard != null && KeyboardGUI.sKeyboard.IsVisible())
        {
            continuousWalkDismissTime = 0f;
            return;
        }
        if (PlayerPanelState.IsEffectivelyExploringInventory && Conversations.runningConversation != null)
        {
            continuousWalkDismissTime = 0f;
            return;
        }

        PlayerObject player = PlayerObject.Player;
        if (player == null || !player.IsPanelWalkDismissMovement(out _))
        {
            continuousWalkDismissTime = 0f;
            return;
        }

        continuousWalkDismissTime += Time.deltaTime;
        if (continuousWalkDismissTime >= WalkDismissDuration)
        {
            continuousWalkDismissTime = 0f;
            DismissActivePlayerPanels();
        }
    }

    /// <summary>Close magic/inventory/stats (same paths as Esc). Returns true if anything was dismissed.</summary>
    private static bool DismissActivePlayerPanels()
    {
        if (!AnyPlayerPanelVisible())
        {
            return false;
        }

        if (PlayerPanelState.ActivePanel == EPlayerPanel.Inventory)
        {
            Inventory.HidePanel(fromUserToggle: false);
        }
        else if (PlayerPanelState.ActivePanel == EPlayerPanel.Magic)
        {
            Magic.HidePanel();
        }
        else if (PlayerPanelState.ActivePanel != EPlayerPanel.None)
        {
            PlayerPanelState.SetPanel(EPlayerPanel.None, fromUserToggle: false);
        }

        if (StatsPanel.sStatsPanel != null && StatsPanel.sStatsPanel.ShouldDismissWithEscape())
        {
            StatsPanel.sStatsPanel.Hide();
        }

        return true;
    }
}
