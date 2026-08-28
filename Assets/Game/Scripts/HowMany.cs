using UnityEngine;

public enum EHowManyReason
{
    MoveStuff,
    Trade,
    Throw,
    Pickup,
    /// <summary>Mouse drag from stuff list — detach stack and follow cursor (<see cref="Inventory.mouseCursorCarriedPortable"/>).</summary>
    MoveStuffMouse,
}

public class HowMany : MonoBehaviour
{
    public GUIStyle howManyStyle;
    public GUIStyle howManyButtonStyle;
    public Texture2D aButton;
    public Texture2D bButton;
    public Texture2D yButton;
    public Texture2D escapeKey;
    public Texture2D rightMouseButton;
    public Texture2D buttonOutline;

    public AudioClip moreItems;
    public AudioClip lessItems;
    public AudioClip dialogOn;
    public AudioClip cancel;
    public AudioClip take;
    public AudioClip takeAll;

    public UUObject howManyItem;
    public int howMany = 1;
    private int howManyHold;

    private int quantityRepeatDirection;
    private float quantityRepeatStartTime;
    private float quantityRepeatLastStepTime;
    private Rect cachedLeftArrowRect;
    private Rect cachedRightArrowRect;
    private bool cachedHasArrowRects;

    private const float QuantityRepeatInitialDelay = 0.25f;
    private const float QuantityRepeatInterval = 1f / 30f;
    
    protected bool initialButtonsReleased;

    public EHowManyReason howManyReason;

    /// <summary>Set when Pickup dialog opened from right-hold/drag on world object; <see cref="UUObject.PickUpSome"/> uses hand-only path.</summary>
    public bool pickupFromWorldDragIntent;

    public static HowMany sHowMany;

    public static void AskHowMany(EHowManyReason reason, UUObject obj)
    {
        sHowMany.howManyReason = reason;
        sHowMany.howMany = 1;
        sHowMany.howManyItem = obj;
        sHowMany.initialButtonsReleased = false;
        sHowMany.quantityRepeatDirection = 0;
        sHowMany.cachedHasArrowRects = false;
        sHowMany.pickupFromWorldDragIntent =
            reason == EHowManyReason.Pickup
            && Interaction.sInt != null
            && (Interaction.sInt.HasWorldPickupIntentForPickup()
                || Interaction.sInt.MouseLeftHoldPortablePickupUse);
        Utils.PlayClip2d(sHowMany.dialogOn);
    }

    public static bool IsActive()
    {
        return sHowMany.howManyHold > 0 || sHowMany.howManyItem != null;
    }

    protected void Start()
    {
        sHowMany = this;
    }

    private void SubmitHowMany(bool takeEntireQuantity)
    {
        if (takeEntireQuantity)
        {
            howMany = howManyItem.quantity;
            Utils.PlayClip2d(this.takeAll);
        }
        else
        {
            Utils.PlayClip2d(take);
        }
        switch (howManyReason)
        {
        case EHowManyReason.MoveStuff:
            Inventory.sInv.usingItem = howManyItem;
            howManyItem = null;
            break;
        case EHowManyReason.MoveStuffMouse:
            Inventory.sInv.CompleteMoveStuffMouseAfterHowMany(howManyItem, howMany);
            howManyItem = null;
            break;
        case EHowManyReason.Throw:
            if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
                Inventory.sInv.PrepareThrowImpulseFromMouseCursor();
            Inventory.sInv.TryThrow(howManyItem);
            howManyItem = null;
            break;
        case EHowManyReason.Trade:
            if (Inventory.sInv.pendingFloatingTradeSlot >= 0)
            {
                int destSlot = Inventory.sInv.pendingFloatingTradeSlot;
                if (!Inventory.sInv.TryPutFloatingIntoTradeTrayAtSlot(destSlot, howManyItem, howMany))
                    Inventory.sInv.mouseCursorCarriedPortable = howManyItem;

                Inventory.sInv.pendingFloatingTradeSlot = -1;
            }
            else
            {
                Inventory.sInv.TryPutSelectionInTradeTray();
            }

            howManyItem = null;
            break;
        case EHowManyReason.Pickup:
            howManyItem.PickUpSome(howMany, pickupFromWorldDragIntent);
            pickupFromWorldDragIntent = false;
            howManyItem = null;
            if (Interaction.sInt != null)
                Interaction.sInt.ClearWorldPickupIntentFlags();
            break;
        }
    }

    private void CancelHowMany()
    {
        if (howManyReason == EHowManyReason.Throw && Inventory.sInv != null)
            Inventory.sInv.CancelPendingThrowCloseInventory();
        if (howManyReason == EHowManyReason.Trade && Inventory.sInv != null && Inventory.sInv.pendingFloatingTradeSlot >= 0
            && howManyItem != null)
        {
            Inventory.sInv.RestoreFloatingTradeHowManyCancel(howManyItem);
        }

        if (howManyReason == EHowManyReason.MoveStuffMouse && Inventory.sInv != null)
            Inventory.sInv.pendingTradeTrayDragSlot = -1;

        pickupFromWorldDragIntent = false;
        howManyItem = null;
        quantityRepeatDirection = 0;
        cachedHasArrowRects = false;
        if (Interaction.sInt != null)
            Interaction.sInt.ClearWorldPickupIntentFlags();
        Utils.PlayClip2d(cancel);
    }

    private void ApplyQuantityStep(int direction)
    {
        if (direction < 0 && howMany > 1)
        {
            --howMany;
            Utils.PlayClip2d(lessItems);
        }
        else if (direction > 0 && howMany < howManyItem.quantity)
        {
            ++howMany;
            Utils.PlayClip2d(moreItems);
        }
    }

    private void UpdateQuantityRepeat(int direction)
    {
        if (direction > 0 && howMany >= howManyItem.quantity)
        {
            direction = 0;
        }
        if (direction < 0 && howMany <= 1)
        {
            direction = 0;
        }
        if (direction == 0)
        {
            quantityRepeatDirection = 0;
            return;
        }

        float now = Time.unscaledTime;
        if (direction != quantityRepeatDirection)
        {
            quantityRepeatDirection = direction;
            quantityRepeatStartTime = now;
            quantityRepeatLastStepTime = now;
            ApplyQuantityStep(direction);
            return;
        }

        if (now - quantityRepeatStartTime < QuantityRepeatInitialDelay)
        {
            return;
        }

        if (now - quantityRepeatLastStepTime >= QuantityRepeatInterval)
        {
            quantityRepeatLastStepTime = now;
            ApplyQuantityStep(direction);
        }
    }

    private int ReadQuantityRepeatDirection()
    {
        if (GameInput.CurrentGamepad?.dpad.left.isPressed ?? false)
        {
            return -1;
        }
        if (GameInput.CurrentGamepad?.dpad.right.isPressed ?? false)
        {
            return 1;
        }

        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard
            && cachedHasArrowRects
            && (GameInput.CurrentMouse?.leftButton.isPressed ?? false))
        {
            Vector2 mouse = GuiInput.MousePositionGuiSpace;
            if (cachedLeftArrowRect.Contains(mouse))
            {
                return -1;
            }
            if (cachedRightArrowRect.Contains(mouse))
            {
                return 1;
            }
        }

        return 0;
    }

    private void RefreshCachedArrowRects()
    {
        cachedHasArrowRects = false;
        if (howManyItem == null || GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
        {
            return;
        }

        DataLoader dl = DataLoader.sDataLoader;
        if (dl?.invTex == null || dl.invTex.Length <= 6 || dl.cursorTex == null || dl.cursorTex.Length <= 4)
        {
            return;
        }

        Texture2D leftArrow = dl.cursorTex[3];
        Texture2D rightArrow = dl.cursorTex[4];
        if (leftArrow == null || rightArrow == null)
        {
            return;
        }

        Texture2D panelTex = dl.invTex[6];
        Rect r = new Rect(
            (Screen.width - 3 * panelTex.width) / 2,
            (Screen.height - 3.6f * panelTex.height) / 2,
            3 * panelTex.width,
            3.6f * panelTex.height);
        Texture2D itemTex = howManyItem.GetInventoryTex();
        float aw = 1.5f * leftArrow.width;
        float ah = 4 * leftArrow.height;
        float itemFootprint = 20 + 3 * itemTex.width;
        float bandLeft = r.x + itemFootprint + 4;
        float bandRight = r.xMax - 20;
        float countCenterY = r.y + r.height * 0.5f;
        cachedLeftArrowRect = new Rect(bandLeft, countCenterY - ah * 0.5f, aw, ah);
        cachedRightArrowRect = new Rect(bandRight - aw, countCenterY - ah * 0.5f, aw, ah);
        cachedHasArrowRects = true;
    }
    
    protected void Update()
    {
        PlayerObject.DisableControls(EControlMask.HowMany, IsActive());

        if (howManyItem != null)
        {
            if (!initialButtonsReleased)
            {
                if ((GameInput.CurrentGamepad?.aButton.isPressed ?? false) || (GameInput.CurrentGamepad?.aButton.wasReleasedThisFrame ?? false)
                    || (GameInput.CurrentGamepad?.yButton.isPressed ?? false) || (GameInput.CurrentGamepad?.yButton.wasReleasedThisFrame ?? false))
                {
                    return;
                }
                initialButtonsReleased = true;
            }
            howManyHold = 2;
            RefreshCachedArrowRects();
            float x = Utils.DeadZone(GameInput.CurrentGamepad?.leftStick.ReadValue().x ?? 0.0f);
            int repeatDirection = ReadQuantityRepeatDirection();
            UpdateQuantityRepeat(repeatDirection);
            if (repeatDirection == 0)
            {
                if (howMany < howManyItem.quantity && x > 0)
                {
                    int speed = (int)(30 * x);
                    if (Time.frameCount % (31 - speed) == 0)
                    {
                        ++howMany;
                        Utils.PlayClip2d(moreItems);
                    }
                }
                if (howMany > 1 && x < 0)
                {
                    int speed = (int)(30 * -x);
                    if (Time.frameCount % (31 - speed) == 0)
                    {
                        --howMany;
                        Utils.PlayClip2d(lessItems);
                    }
                }
            }
            if ((GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false) || (GameInput.CurrentGamepad?.yButton.wasPressedThisFrame ?? false))
            {
                SubmitHowMany(GameInput.CurrentGamepad?.yButton.wasPressedThisFrame ?? false);
            }
            else if (GameInput.CurrentGamepad?.bButton.wasPressedThisFrame ?? false)
            {
                CancelHowMany();
            }
            else if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
            {
                if (GameInput.EscapePressedThisFrame())
                    CancelHowMany();
                else if (GameInput.CurrentMouse?.rightButton.wasPressedThisFrame ?? false)
                    SubmitHowMany(true);
            }
        }

        bool aButtonInAction = (GameInput.CurrentGamepad?.aButton.isPressed ?? false) || (GameInput.CurrentGamepad?.aButton.wasReleasedThisFrame ?? false);
        bool bButtonInAction = (GameInput.CurrentGamepad?.bButton.isPressed ?? false) || (GameInput.CurrentGamepad?.bButton.wasReleasedThisFrame ?? false);
        bool yButtonInAction = (GameInput.CurrentGamepad?.yButton.isPressed ?? false) || (GameInput.CurrentGamepad?.yButton.wasReleasedThisFrame ?? false);

        if (howManyItem == null && !aButtonInAction && !bButtonInAction && !yButtonInAction && howManyHold > 0)
        {
            --howManyHold;
        }
    }

    private static string[] verb = { "Move", "Move", "Hurl", "Take", "Move" };

    private int CalcCountFontSize(string countText, float maxTextWidth)
    {
        const int baseFontSize = 40;
        const int minFontSize = 24;
        howManyStyle.fontSize = baseFontSize;
        if (maxTextWidth <= 0)
        {
            return baseFontSize;
        }

        float textWidth = howManyStyle.CalcSize(new GUIContent(countText)).x;
        if (textWidth <= maxTextWidth)
        {
            return baseFontSize;
        }

        int fontSize = Mathf.Max(minFontSize, (int)(baseFontSize * (maxTextWidth / textWidth)));
        howManyStyle.fontSize = fontSize;
        textWidth = howManyStyle.CalcSize(new GUIContent(countText)).x;
        if (textWidth > maxTextWidth)
        {
            fontSize = Mathf.Max(minFontSize, (int)(fontSize * (maxTextWidth / textWidth)));
        }

        return fontSize;
    }

    protected void OnGUI()
    {
        if (howManyItem != null)
        {
            GUI.depth = (int)EGUIDepth.HowMany; 
            
            Texture2D tex = DataLoader.sDataLoader.invTex[6];
            Rect r = new Rect((Screen.width - 3 * tex.width) / 2, (Screen.height - 3.6f * tex.height) / 2, 3 * tex.width, 3.6f * tex.height);
            GuiInput.RegisterBlockingRect(r);
            GUI.DrawTexture(r, tex);
            Texture2D itemTex = howManyItem.GetInventoryTex();
            GUI.DrawTexture(new Rect(r.x + 20, r.y + 20, 3 * itemTex.width, 3.6f * itemTex.height), itemTex);
            howManyStyle.alignment = TextAnchor.UpperCenter;
            howManyStyle.fontSize = 20;
            GUI.Label(new Rect(r.x, r.y + 20, r.width, r.height), "How many?", howManyStyle);

            string countText = $"{howMany}/{howManyItem.quantity}";
            bool mouseUi = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;
            howManyStyle.alignment = TextAnchor.MiddleCenter;

            if (mouseUi)
            {
                Texture2D leftArrow = DataLoader.sDataLoader.cursorTex[3];
                Texture2D rightArrow = DataLoader.sDataLoader.cursorTex[4];
                const float arrowGap = 4f;
                float aw = 1.5f * leftArrow.width;
                float itemFootprint = 20 + 3 * itemTex.width;
                float bandLeft = r.x + itemFootprint + 4;
                float bandRight = r.xMax - 20;
                float bandWidth = bandRight - bandLeft;

                float maxTextWidth = bandWidth - 2 * aw - 2 * arrowGap;
                howManyStyle.fontSize = CalcCountFontSize(countText, maxTextWidth);
                Vector2 countSize = howManyStyle.CalcSize(new GUIContent(countText));

                float countCenterY = r.y + r.height * 0.5f;
                float countLeft = bandLeft + aw + arrowGap;
                Rect countRect = new Rect(countLeft, countCenterY - countSize.y * 0.5f, maxTextWidth, countSize.y);

                GUI.Label(countRect, countText, howManyStyle);

                RefreshCachedArrowRects();
                if (cachedHasArrowRects)
                {
                    GUI.DrawTexture(cachedLeftArrowRect, leftArrow);
                    GUI.DrawTexture(cachedRightArrowRect, rightArrow);
                }
            }
            else
            {
                howManyStyle.fontSize = CalcCountFontSize(countText, r.width);
                GUI.Label(r, countText, howManyStyle);
            }

            Rect hintCancelRect = new Rect(r.x + 16, r.y + r.height - 42, 72, 29);
            Rect hintCenterRect = new Rect(r.x + r.width / 2f - 36f, r.y + r.height - 42, 72, 29);
            Rect hintRightRect = new Rect(r.x + r.width - 88, r.y + r.height - 42, 72, 29);
            if (mouseUi && buttonOutline != null)
            {
                GUI.DrawTexture(hintCancelRect, buttonOutline, ScaleMode.StretchToFill, true);
                GUI.DrawTexture(hintCenterRect, buttonOutline, ScaleMode.StretchToFill, true);
                GUI.DrawTexture(hintRightRect, buttonOutline, ScaleMode.StretchToFill, true);
            }

            Texture2D leftHintIcon = mouseUi && escapeKey != null ? escapeKey : bButton;
            GUI.DrawTexture(new Rect(hintCancelRect.x + 5, hintCancelRect.y + 5, 16, 16), leftHintIcon);
            GUI.Label(new Rect(hintCancelRect.x + 21, hintCancelRect.y, 51, 29), "Cancel", howManyButtonStyle);
            if (!mouseUi)
            {
                GUI.DrawTexture(new Rect(hintCenterRect.x + 5, hintCenterRect.y + 5, 16, 16), aButton);
                GUI.Label(new Rect(hintCenterRect.x + 21, hintCenterRect.y, 51, 29), verb[(int)howManyReason], howManyButtonStyle);
            }
            else
            {
                howManyButtonStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(hintCenterRect, verb[(int)howManyReason], howManyButtonStyle);
                howManyButtonStyle.alignment = TextAnchor.MiddleLeft;
            }
            Texture2D rightHintIcon = mouseUi && rightMouseButton != null ? rightMouseButton : yButton;
            GUI.DrawTexture(new Rect(hintRightRect.x + 5, hintRightRect.y + 5, 16, 16), rightHintIcon);
            GUI.Label(new Rect(hintRightRect.x + 21, hintRightRect.y, 51, 29), "All", howManyButtonStyle);

            if (GuiInput.TryConsumeClickInRect(hintCancelRect))
            {
                CancelHowMany();
                return;
            }
            if (GuiInput.TryConsumeClickInRect(hintCenterRect))
            {
                SubmitHowMany(false);
                return;
            }
            if (GuiInput.TryConsumeClickInRect(hintRightRect))
            {
                SubmitHowMany(true);
                return;
            }

            GuiInput.TryConsumeClickInPanel(r);
        }
    }
}
