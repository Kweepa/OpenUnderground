using UnityEngine;

/// <summary>
/// Contextual control tutorials shown as a top-center IMGUI overlay; progress persists per save slot.
/// </summary>
[DefaultExecutionOrder(-150)]
public class TutorialManager : MonoBehaviour
{
    private const float MovementDelaySeconds = 5f;
    private const float MovementFirstDisplaySeconds = 10f;
    private const float MovementReshowDisplaySeconds = 5f;
    private const float DefaultStepDisplaySeconds = 30f;
    private const float WeaponChargeDelaySeconds = 1f;

    [SerializeField] private float inventoryTutorialDelay = 20f;
    [SerializeField] private float statsTutorialDelay = 120f;
    public GUIStyle textStyle;

    private static TutorialManager sInstance;

    private ETutorialStep activeStep = ETutorialStep.Count;
    private float activeSessionDisplayTime;
    private float[] stepSessionDisplayTimes = new float[(int)ETutorialStep.Count];

    private float weaponEquipRealTime = -1f;
    private bool weaponEquippedPending;
    private bool attackPerformedThisSession;
    private bool interactableUsedThisSession;
    private bool pickupOccurredThisSession;
    private bool castAttemptedThisSession;
    private bool mouseInventoryHintArmed;
    private bool movementHintReshowPending;

    private static Texture2D backgroundTex;

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

    private TutorialSaveData SaveData
    {
        get
        {
            if (PlayerData.sData == null)
            {
                return null;
            }

            if (PlayerData.sData.tutorialData == null)
            {
                PlayerData.sData.tutorialData = new TutorialSaveData();
            }

            TutorialSaveData data = PlayerData.sData.tutorialData;
            if (data.version < TutorialSaveData.CurrentVersion && data.completedMask == 0)
            {
                data.version = TutorialSaveData.CurrentVersion;
            }

            return data;
        }
    }

    /// <summary>Pre-tutorial saves: skip all hints and persist as fully complete.</summary>
    public static void MarkAllStepsCompleteForLegacySave(TutorialSaveData data)
    {
        if (data == null)
        {
            return;
        }

        data.completedMask = (1 << (int)ETutorialStep.Count) - 1;
        data.version = TutorialSaveData.CurrentVersion;
    }

    public static void NotifyPickup()
    {
        if (sInstance == null)
        {
            return;
        }

        sInstance.HandlePickup();
    }

    public static void NotifyRunestoneStowed()
    {
        if (sInstance == null)
        {
            return;
        }

        sInstance.HandleRunestoneStowed();
    }

    public static void NotifyWeaponEquipped(WeaponBase weapon)
    {
        if (sInstance == null || weapon == null || weapon.type == EObjectType.Fist)
        {
            return;
        }

        sInstance.HandleWeaponEquipped();
    }

    public static void NotifyAttack()
    {
        if (sInstance == null)
        {
            return;
        }

        sInstance.attackPerformedThisSession = true;
        sInstance.TryDismissActiveStep(ETutorialStep.WeaponCharge);
    }

    public static void NotifyPanelChanged(EPlayerPanel previous, EPlayerPanel panel, bool fromUserToggle)
    {
        if (sInstance == null)
        {
            return;
        }

        sInstance.HandlePanelChanged(previous, panel, fromUserToggle);
    }

    public static void NotifyMouseInventoryItemPlaced()
    {
        if (sInstance == null)
        {
            return;
        }

        sInstance.HandleMouseInventoryItemPlaced();
    }

    public static void NotifyCastAttempt()
    {
        if (sInstance == null)
        {
            return;
        }

        sInstance.castAttemptedThisSession = true;
        sInstance.TryDismissActiveStep(ETutorialStep.MagicCast);
    }

    public static void NotifyStatsToggled(bool opening)
    {
        if (sInstance == null || !opening)
        {
            return;
        }

        sInstance.TryDismissActiveStep(ETutorialStep.StatsPanel);
    }

    public static void NotifyInteractableUsed(UUObject target)
    {
        if (sInstance == null || target == null)
        {
            return;
        }

        if (target is Door || target is SwitchBase || target is Decal)
        {
            sInstance.interactableUsedThisSession = true;
            sInstance.TryDismissActiveStep(ETutorialStep.Interactable);
        }
    }

    private void HandlePickup()
    {
        TutorialSaveData data = SaveData;
        if (data == null)
        {
            return;
        }

        pickupOccurredThisSession = true;
        if (data.firstPickupTime < 0.0 && PlayerData.sData != null)
        {
            data.firstPickupTime = PlayerData.sData.gameTime;
        }

        TryDismissActiveStep(ETutorialStep.PortablePickup);
    }

    private void HandleRunestoneStowed()
    {
        TutorialSaveData data = SaveData;
        if (data == null)
        {
            return;
        }

        data.runesStowedCount++;
    }

    private void HandleWeaponEquipped()
    {
        TutorialSaveData data = SaveData;
        if (data == null || IsStepComplete(data, ETutorialStep.WeaponCharge))
        {
            return;
        }

        weaponEquipRealTime = Time.time;
        weaponEquippedPending = true;
    }

    private void HandleMouseInventoryItemPlaced()
    {
        TutorialSaveData data = SaveData;
        if (data == null)
        {
            return;
        }

        if (GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
        {
            return;
        }

        if (!PlayerPanelState.IsEffectivelyExploringInventory)
        {
            return;
        }

        TryDismissActiveStep(ETutorialStep.PortableDropInInventory);
        if (IsStepComplete(data, ETutorialStep.Inventory))
        {
            return;
        }

        mouseInventoryHintArmed = true;
    }

    private void HandlePanelChanged(EPlayerPanel previous, EPlayerPanel panel, bool fromUserToggle)
    {
        TutorialSaveData data = SaveData;
        if (data == null)
        {
            return;
        }

        if (!fromUserToggle)
        {
            return;
        }

        if (panel == EPlayerPanel.Inventory)
        {
            data.hasOpenedInventory = true;
            mouseInventoryHintArmed = false;
            TryDismissActiveStep(ETutorialStep.Inventory);
        }
        else if (previous == EPlayerPanel.Inventory && panel == EPlayerPanel.None)
        {
            data.hasOpenedInventory = true;
            mouseInventoryHintArmed = false;
            TryDismissActiveStep(ETutorialStep.Inventory);
        }
        else if (panel == EPlayerPanel.Magic)
        {
            TryDismissActiveStep(ETutorialStep.MagicPanelOpen);
        }
    }

    private void Update()
    {
        TutorialSaveData data = SaveData;
        if (data == null || PlayerObject.Player == null)
        {
            return;
        }

        if (LevelLoader.sLevelLoader == null || LevelLoader.sLevelLoader.loadedLevel == 0)
        {
            return;
        }

        if (data.gameplayStartTime <= 0.0)
        {
            data.gameplayStartTime = PlayerData.sData.gameTime;
        }

        TryAutoCompleteTimedSteps(data);

        bool suppressed = IsSuppressed();
        ETutorialStep desiredStep = EvaluateDesiredStep(data);

        if (desiredStep != ETutorialStep.Count)
        {
            if (activeStep != desiredStep)
            {
                BeginShowingStep(data, desiredStep);
            }
        }
        else if (activeStep != ETutorialStep.Count && !StepStillWantsShow(data, activeStep))
        {
            EndShowingStep(data, activeStep);
            activeStep = ETutorialStep.Count;
            activeSessionDisplayTime = 0f;
        }

        if (activeStep == ETutorialStep.Count || suppressed)
        {
            return;
        }

        activeSessionDisplayTime += Time.deltaTime;
        stepSessionDisplayTimes[(int)activeStep] = activeSessionDisplayTime;

        if (ShouldDismissStep(data, activeStep))
        {
            CompleteStep(data, activeStep);
            activeStep = ETutorialStep.Count;
            activeSessionDisplayTime = 0f;
        }
    }

    private void BeginShowingStep(TutorialSaveData data, ETutorialStep step)
    {
        if (activeStep != ETutorialStep.Count)
        {
            EndShowingStep(data, activeStep);
        }

        activeStep = step;
        activeSessionDisplayTime = 0f;
        stepSessionDisplayTimes[(int)step] = 0f;

        if (step == ETutorialStep.Movement)
        {
            data.movementAccumulatedDisplayTime = 0f;
        }
    }

    private void EndShowingStep(TutorialSaveData data, ETutorialStep step)
    {
        stepSessionDisplayTimes[(int)step] = 0f;

        if (step == ETutorialStep.Movement)
        {
            data.movementAccumulatedDisplayTime = 0f;
            if (!IsStepComplete(data, ETutorialStep.Movement))
            {
                movementHintReshowPending = true;
            }
        }
    }

    private ETutorialStep EvaluateDesiredStep(TutorialSaveData data)
    {
        ETutorialStep best = ETutorialStep.Count;

        for (int i = 0; i < (int)ETutorialStep.Count; i++)
        {
            ETutorialStep step = (ETutorialStep)i;
            if (!StepStillWantsShow(data, step))
            {
                continue;
            }

            if (best == ETutorialStep.Count || GetStepShowPriority(step) > GetStepShowPriority(best))
            {
                best = step;
            }
        }

        return best;
    }

    private static int GetStepShowPriority(ETutorialStep step)
    {
        switch (step)
        {
        case ETutorialStep.StatsPanel:
            return 0;
        case ETutorialStep.Movement:
            return 10;
        case ETutorialStep.PortablePickup:
            return 20;
        case ETutorialStep.Interactable:
            return 25;
        case ETutorialStep.Inventory:
            return 30;
        case ETutorialStep.MagicPanelOpen:
            return 35;
        case ETutorialStep.WeaponCharge:
            return 40;
        case ETutorialStep.PortableDropInInventory:
            return 50;
        case ETutorialStep.MagicCast:
            return 55;
        default:
            return 0;
        }
    }

    private bool StepStillWantsShow(TutorialSaveData data, ETutorialStep step)
    {
        if (IsStepComplete(data, step))
        {
            return false;
        }

        switch (step)
        {
        case ETutorialStep.Movement:
            return WantsMovementTutorial(data);
        case ETutorialStep.PortablePickup:
            return IsPortableHovered();
        case ETutorialStep.PortableDropInInventory:
            return WantsPortableDropInInventoryTutorial(data);
        case ETutorialStep.Inventory:
            return WantsInventoryTutorial(data);
        case ETutorialStep.WeaponCharge:
            return WantsWeaponChargeTutorial(data);
        case ETutorialStep.MagicPanelOpen:
            return WantsMagicPanelOpenTutorial(data);
        case ETutorialStep.MagicCast:
            return WantsMagicCastTutorial(data);
        case ETutorialStep.Interactable:
            return IsInteractableHovered();
        case ETutorialStep.StatsPanel:
            return WantsStatsTutorial(data);
        default:
            return false;
        }
    }

    private bool WantsMovementTutorial(TutorialSaveData data)
    {
        if (PlayerData.sData == null)
        {
            return false;
        }

        if (!IsMovementTutorialContext())
        {
            return false;
        }

        if (GetTotalDisplayTime(data, ETutorialStep.Movement) >= GetMovementDisplaySeconds())
        {
            return false;
        }

        return PlayerData.sData.gameTime >= data.gameplayStartTime + MovementDelaySeconds;
    }

    private float GetMovementDisplaySeconds()
    {
        return movementHintReshowPending ? MovementReshowDisplaySeconds : MovementFirstDisplaySeconds;
    }

    private static bool IsMovementTutorialContext()
    {
        if (IsAnyPlayerPanelOpen())
        {
            return false;
        }

        if (GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
        {
            return true;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            return true;
        }

        if (Inventory.sInv != null && Inventory.sInv.mouseCursorCarriedPortable != null)
        {
            return false;
        }

        if (SoftwareCursorOverlay.DefaultTextureOverride != null)
        {
            return false;
        }

        if (Magic.sMagic != null && Magic.sMagic.HasMousePrimedSpellAwaitingAim)
        {
            return false;
        }

        PlayerObject player = PlayerObject.Player;
        if (player != null && (player.controlsDisabled & GameplayCursorPolicy.CursorFreeUiMask) != 0)
        {
            return false;
        }

        return true;
    }

    private static bool IsAnyPlayerPanelOpen()
    {
        if (PlayerPanelState.IsEffectivelyExploringInventory)
        {
            return true;
        }

        if (PlayerPanelState.IsEffectivelyExploringMagic)
        {
            return true;
        }

        if (StatsPanel.sStatsPanel != null && StatsPanel.sStatsPanel.ShouldDismissWithEscape())
        {
            return true;
        }

        if (MapScreen.IsMapScreenVisible())
        {
            return true;
        }

        if (Conversations.runningConversation != null)
        {
            return true;
        }

        return false;
    }

    private bool WantsPortableDropInInventoryTutorial(TutorialSaveData data)
    {
        if (GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
        {
            return false;
        }

        if (!IsStepComplete(data, ETutorialStep.PortablePickup))
        {
            return false;
        }

        if (!PlayerPanelState.IsEffectivelyExploringInventory)
        {
            return false;
        }

        return Inventory.sInv != null && Inventory.sInv.mouseCursorCarriedPortable != null;
    }

    private bool WantsInventoryTutorial(TutorialSaveData data)
    {
        if (!IsStepComplete(data, ETutorialStep.PortablePickup))
        {
            return false;
        }

        if (!pickupOccurredThisSession && data.firstPickupTime < 0.0)
        {
            return false;
        }

        if (data.hasOpenedInventory)
        {
            return false;
        }

        if (mouseInventoryHintArmed)
        {
            return true;
        }

        if (PlayerData.sData == null || data.firstPickupTime < 0.0)
        {
            return false;
        }

        return PlayerData.sData.gameTime >= data.firstPickupTime + inventoryTutorialDelay;
    }

    private bool WantsWeaponChargeTutorial(TutorialSaveData data)
    {
        if (!weaponEquippedPending)
        {
            return false;
        }

        return Time.time >= weaponEquipRealTime + WeaponChargeDelaySeconds;
    }

    private bool WantsMagicPanelOpenTutorial(TutorialSaveData data)
    {
        if (data.runesStowedCount < 2)
        {
            return false;
        }

        if (PlayerPanelState.IsEffectivelyExploringMagic)
        {
            return false;
        }

        return true;
    }

    private bool WantsMagicCastTutorial(TutorialSaveData data)
    {
        return PlayerPanelState.IsEffectivelyExploringMagic;
    }

    private bool WantsStatsTutorial(TutorialSaveData data)
    {
        if (PlayerData.sData == null)
        {
            return false;
        }

        return PlayerData.sData.playTime >= statsTutorialDelay;
    }

    private bool ShouldDismissStep(TutorialSaveData data, ETutorialStep step)
    {
        float totalDisplay = GetTotalDisplayTime(data, step);

        switch (step)
        {
        case ETutorialStep.Movement:
            return totalDisplay >= GetMovementDisplaySeconds();
        case ETutorialStep.PortablePickup:
            return totalDisplay >= DefaultStepDisplaySeconds || pickupOccurredThisSession;
        case ETutorialStep.PortableDropInInventory:
            return totalDisplay >= DefaultStepDisplaySeconds;
        case ETutorialStep.Inventory:
            return totalDisplay >= DefaultStepDisplaySeconds || data.hasOpenedInventory;
        case ETutorialStep.WeaponCharge:
            return totalDisplay >= DefaultStepDisplaySeconds || attackPerformedThisSession;
        case ETutorialStep.MagicPanelOpen:
            return totalDisplay >= DefaultStepDisplaySeconds;
        case ETutorialStep.MagicCast:
            return totalDisplay >= DefaultStepDisplaySeconds || castAttemptedThisSession;
        case ETutorialStep.Interactable:
            return totalDisplay >= DefaultStepDisplaySeconds || interactableUsedThisSession;
        case ETutorialStep.StatsPanel:
            return totalDisplay >= DefaultStepDisplaySeconds || IsStatsPanelPinnedOpen();
        default:
            return false;
        }
    }

    private float GetTotalDisplayTime(TutorialSaveData data, ETutorialStep step)
    {
        if (activeStep == step)
        {
            return activeSessionDisplayTime;
        }

        return stepSessionDisplayTimes[(int)step];
    }

    private void TryDismissActiveStep(ETutorialStep step)
    {
        TutorialSaveData data = SaveData;
        if (data == null || activeStep != step)
        {
            if (data != null && (step == ETutorialStep.PortablePickup || step == ETutorialStep.PortableDropInInventory))
            {
                SetStepComplete(data, step);
            }

            return;
        }

        CompleteStep(data, step);
        activeStep = ETutorialStep.Count;
        activeSessionDisplayTime = 0f;
    }

    private void CompleteStep(TutorialSaveData data, ETutorialStep step)
    {
        SetStepComplete(data, step);
        EndShowingStep(data, step);

        if (step == ETutorialStep.WeaponCharge)
        {
            weaponEquippedPending = false;
        }

        if (step == ETutorialStep.Inventory)
        {
            mouseInventoryHintArmed = false;
        }
    }

    private void TryAutoCompleteTimedSteps(TutorialSaveData data)
    {
        if (!IsStepComplete(data, ETutorialStep.Movement)
            && activeStep == ETutorialStep.Movement
            && GetTotalDisplayTime(data, ETutorialStep.Movement) >= GetMovementDisplaySeconds())
        {
            CompleteStep(data, ETutorialStep.Movement);
            activeStep = ETutorialStep.Count;
            activeSessionDisplayTime = 0f;
        }
    }

    private static bool IsStepComplete(TutorialSaveData data, ETutorialStep step)
    {
        return (data.completedMask & (1 << (int)step)) != 0;
    }

    private static void SetStepComplete(TutorialSaveData data, ETutorialStep step)
    {
        data.completedMask |= 1 << (int)step;
    }

    private static bool IsPortableHovered()
    {
        UUObject centered = Interaction.sInt?.centeredObject;
        return centered != null && centered.isPortable;
    }

    private static bool IsInteractableHovered()
    {
        UUObject centered = Interaction.sInt?.centeredObject;
        if (centered == null)
        {
            return false;
        }

        return centered is Door || centered is SwitchBase || centered is Decal;
    }

    private static bool IsStatsPanelPinnedOpen()
    {
        return StatsPanel.sStatsPanel != null && StatsPanel.sStatsPanel.IsPinnedOpen();
    }

    private static bool IsSuppressed()
    {
        if (PlayerData.sData == null || PlayerData.sData.dead)
        {
            return true;
        }

        if (!PlayerPanelState.ArePanelsAvailable && LevelLoader.sLevelLoader != null
            && LevelLoader.sLevelLoader.loadedLevel == 9)
        {
            return true;
        }

        if (Conversations.runningConversation != null)
        {
            return true;
        }

        if (MapScreen.IsMapScreenVisible())
        {
            return true;
        }

        if (HowMany.IsActive())
        {
            return true;
        }

        if (RepairDialog.IsActive())
        {
            return true;
        }

        PlayerObject player = PlayerObject.Player;
        if (player == null)
        {
            return true;
        }

        EControlMask blocking = EControlMask.Cutscene | EControlMask.SaveLoad | EControlMask.Conversation;
        if ((player.controlsDisabled & blocking) != 0)
        {
            return true;
        }

        return false;
    }

    private void OnGUI()
    {
        if (activeStep == ETutorialStep.Count || IsSuppressed())
        {
            return;
        }

        string text = BuildStepText(activeStep);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        GUI.depth = (int)EGUIDepth.Tutorial;

        const float maxTextWidthFraction = 0.55f;
        float textWidth = Screen.width * maxTextWidthFraction;
        GUIContent content = new GUIContent(text);
        float textHeight = textStyle.CalcHeight(content, textWidth);

        const float padX = 16f;
        const float padY = 10f;
        float boxW = textWidth + padX * 2f;
        float boxH = textHeight + padY * 2f;
        float boxX = (Screen.width - boxW) * 0.5f;
        float boxY = Screen.height * 0.1f;

        if (backgroundTex == null)
        {
            backgroundTex = new Texture2D(1, 1);
            backgroundTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
            backgroundTex.Apply();
        }

        GUI.DrawTexture(new Rect(boxX, boxY, boxW, boxH), backgroundTex);
        Utils.DropShadowText(text, boxX + padX, boxY + padY, textWidth, textHeight, textStyle);
    }

    private string BuildStepText(ETutorialStep step)
    {
        bool mouseUi = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;
        bool leftHanded = PlayerData.sData != null && PlayerData.sData.leftHanded;

        switch (step)
        {
        case ETutorialStep.Movement:
            if (mouseUi)
            {
                return "Use WASD or the arrow keys to move. Press Space to jump.";
            }

            return "Use the left stick to move. Press A to jump.";
        case ETutorialStep.PortablePickup:
            if (mouseUi)
            {
                return "Click the left mouse button to examine things. "
                       + "Move (WASD) close enough that an item name turns yellow, then hold or drag the left mouse button to pick it up.";
            }

            return "Press X to examine things. "
                   + "Move close enough that an item name turns yellow, then press Y to pick it up. Open the inventory with the right shoulder button.";
        case ETutorialStep.PortableDropInInventory:
            return "Click an inventory slot to drop the item from your cursor into your inventory.";
        case ETutorialStep.Inventory:
            if (mouseUi)
            {
                if (PlayerPanelState.IsEffectivelyExploringInventory)
                {
                    return "Press E or Esc to close your inventory.";
                }

                return "Press E to open and close your inventory.";
            }

            return "Press the right shoulder button to open and close your inventory.";
        case ETutorialStep.WeaponCharge:
            if (mouseUi)
            {
                return "Hold the right mouse button to charge an attack, then release to swing. Press X to cancel a charge.";
            }

            if (leftHanded)
            {
                return "Hold the left trigger to charge an attack, then release to swing. Press B to cancel a charge.";
            }

            return "Hold the right trigger to charge an attack, then release to swing. Press B to cancel a charge.";
        case ETutorialStep.MagicPanelOpen:
            if (mouseUi)
            {
                return "Press Q to open the magic panel.";
            }

            return "Press the left shoulder button to open the magic panel.";
        case ETutorialStep.MagicCast:
            {
                bool canBuildFirstCircleSpell = Magic.sMagic != null && Magic.sMagic.CanBuildFirstCircleSpell();
                string spellExamplesText = "Cast In Lor for Light, Ort Jux for Magic Arrow, or Bet In Sanct for Resist Blows. ";
                if (mouseUi)
                {
                    if (canBuildFirstCircleSpell)
                    {
                        return "Click runes in the magic panel to build a spell, then click the spell runes to cast. " + spellExamplesText + "Press Q to close the magic panel.";
                    }

                    return "Press Q to close the magic panel.";
                }

                if (canBuildFirstCircleSpell)
                {
                    return "Use A to select runes, X to cast, and B to clear. " + spellExamplesText + "Press the left shoulder button to close the magic panel.";
                }

                return "Press the left shoulder button to close the magic panel.";
            }
        case ETutorialStep.Interactable:
            if (mouseUi)
            {
                return "Hold the left mouse button to use switches, doors, and other interactables when you are close enough.";
            }

            return "Press Y to use switches, doors, and other interactables when you are close enough.";
        case ETutorialStep.StatsPanel:
            if (mouseUi)
            {
                return "Press R to show and hide your character stats.";
            }

            if (leftHanded)
            {
                return "Press the right trigger to show and hide your character stats.";
            }

            return "Press the left trigger to show and hide your character stats.";
        default:
            return "";
        }
    }
}
