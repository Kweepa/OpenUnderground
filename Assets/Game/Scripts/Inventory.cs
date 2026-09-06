using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public enum EInvSlot
{
    RightShoulder,
    Head,
    LeftShoulder,
    RightHand,
    Torso,
    LeftHand,
    Hands,
    RightFinger,
    Legs,
    LeftFinger,
    Feet,
    None
}

public class Inventory : MonoBehaviour
{
    public Texture cursor;
    public Texture tradingLight;
    public Texture2D aButton;
    public Texture2D bButton;
    public Texture2D xButton;
    public Texture2D yButton;
    public Texture2D xButtonChargeRing;
    public Material xButtonChargeRingMaterial;

    public Texture2D inventoryMouseLeftHint;
    public Texture2D inventoryMouseRightHint;

    [Tooltip("I-beam for insertion gaps (assign on prefab). Drawn at 3× / 3.6× source pixels like other inventory icons.")]
    public Texture2D inventoryInsertCursor;

    public WeaponBase fistToSpawn;
    public WeaponBase fist;

    // the style for the carry weight number
    public GUIStyle carryStyle;

    // the style for the item labels
    public GUIStyle descriptionStyle;

    public AudioClip moveCursor;
    public AudioClip open;
    public AudioClip backOut;
    public AudioClip equip;
    public AudioClip unequip;
    public AudioClip place;
    public AudioClip slideIn;
    public AudioClip slideOut;
    public AudioClip combine;
    public AudioClip fail;
    public AudioClip throwClip;

    [System.Serializable]
    public struct CombinationSoundEntry
    {
        public EObjectTypeEntry object1;
        public EObjectTypeEntry object2;
        public AudioClip soundClip;
    }

    [SerializeField]
    public List<CombinationSoundEntry> combinationSounds = new ();

    [SerializeField, Min(0f)]
    [Tooltip("Seconds after holding a direction before the first repeated inventory cursor step.")]
    private float inventoryMoveRepeatInitialDelay = 0.25f;

    [SerializeField, Min(0f)]
    [Tooltip("Seconds between repeated inventory cursor steps while holding a direction.")]
    private float inventoryMoveRepeatInterval = 0.05f;

    public List<UUObject> inventory = new List<UUObject>();
    private float lerpIn;
    private float holdTime;
    /// <summary>True last frame if slide was held open (inventory or trade HowMany); used to zero holdTime immediately on dismiss instead of ~2s countdown.</summary>
    private bool inventorySlideWasOpenLastFrame;
    private List<UUObject> stack = new List<UUObject>();
    private int index;
    private int viewIndex;
    private int equipSlot;
    private int tradeSlot;

    public UUObject[] tradeSlots = new UUObject[8];
    public bool[] tradeSlotSelected = new bool[8];

    public List<LightSource> litLights = new();

    public static Inventory sInv;

    private int ix = 15;
    private int iy = 300;
    private int dx = 57;
    private int dy = 66;

    private const EControlMask InventoryModalUiMask = EControlMask.HowMany | EControlMask.RepairDialog;

    private static bool IsInventoryModalUiBlocking(PlayerObject player) =>
        player != null && (player.controlsDisabled & InventoryModalUiMask) != 0;

    // Mouse UI support (used by PlayerPanelInput outside-click logic).
    public bool IsDraggingItemWithMouse =>
        mouseCursorCarriedPortable != null && GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard;

    /// <summary>
    /// Portable following the mouse pointer (ground pickup or inventory drag-off). Separate from gamepad <see cref="usingItem"/>.
    /// </summary>
    public UUObject mouseCursorCarriedPortable;

    /// <summary>Legacy name — true when a portable is held for cursor placement (not in any list).</summary>
    public bool IsWorldPickupUsingItem()
    {
        return mouseCursorCarriedPortable != null;
    }

    public void GetInventoryPanelLayout(out float panelLeft, out float invSlidePixels)
    {
        const float kExtendedInvPosition = 16 + 4 * 64;
        invSlidePixels = lerpIn * kExtendedInvPosition;
        panelLeft = Screen.width - invSlidePixels;
    }

    private void RestoreBridgedCursorPortable(bool bridgedPortableFromCursor)
    {
        if (!bridgedPortableFromCursor || usingItem == null)
            return;
        mouseCursorCarriedPortable = usingItem;
        usingItem = null;
    }

    /// <summary>If the float was bridged into <see cref="usingItem"/> and code left an orphaned floated object, put it back on the mouse cursor.</summary>
    private void SealBridgedFloatIfUsingItemOrphaned(bool bridgedPortableFromCursor)
    {
        if (!bridgedPortableFromCursor || usingItem == null || FindObjectInInventory(usingItem) != null)
        {
            return;
        }

        mouseCursorCarriedPortable = usingItem;
        usingItem = null;
    }

    private static void NotifyTutorialMouseWorldPickupPlaced(bool fromCursorPortable)
    {
        if (fromCursorPortable)
        {
            TutorialManager.NotifyMouseInventoryItemPlaced();
        }
    }

    /// <summary>Click placement for <see cref="mouseCursorCarriedPortable"/> (world pickup / drag-off). Uses <see cref="usingItem"/> only internally for shared combine/equip paths.</summary>
    public bool TryHandleFloatingUsingItemClick(Vector2 guiMouse, float invPosition)
    {
        bool bridgedPortableFromCursor = false;
        if (mouseCursorCarriedPortable != null)
        {
            usingItem = mouseCursorCarriedPortable;
            mouseCursorCarriedPortable = null;
            bridgedPortableFromCursor = true;
        }

        if (usingItem == null || FindObjectInInventory(usingItem) != null)
        {
            RestoreBridgedCursorPortable(bridgedPortableFromCursor);
            return false;
        }

        if (IsInventoryModalUiBlocking(PlayerObject.Player))
        {
            RestoreBridgedCursorPortable(bridgedPortableFromCursor);
            return false;
        }

        if (lerpIn < 0.001f)
        {
            RestoreBridgedCursorPortable(bridgedPortableFromCursor);
            return false;
        }

        if (Conversations.runningConversation != null)
        {
            if (TryGetTradeSlotUnderMouse(guiMouse, out int tradeSlotHit))
            {
                if (!IsPlayerTradeSlotIndex(tradeSlotHit))
                {
                    Utils.PlayClip2d(fail);
                    RestoreBridgedCursorPortable(bridgedPortableFromCursor);
                    return false;
                }

                UUObject fo = usingItem;
                if (fo != null && fo.quantity > 1 && fo.stackable && !skipTradeHowManyOnNextFloatingDrop)
                {
                    pendingFloatingTradeSlot = tradeSlotHit;
                    HowMany.AskHowMany(EHowManyReason.Trade, fo);
                    return true;
                }

                if (TryPutFloatingIntoTradeTrayAtSlot(tradeSlotHit, fo, fo != null ? fo.quantity : 1))
                {
                    SealBridgedFloatIfUsingItemOrphaned(bridgedPortableFromCursor);
                    NotifyTutorialMouseWorldPickupPlaced(bridgedPortableFromCursor);
                    return true;
                }

                RestoreBridgedCursorPortable(bridgedPortableFromCursor);
                return false;
            }

            if (!IsGuiMouseOverInventoryPanel(guiMouse, invPosition))
            {
                RestoreBridgedCursorPortable(bridgedPortableFromCursor);
                return false;
            }

            GetInventoryPanelLayout(out float panelLeftConv, out _);

            if (TryGetPaperdollSlotUnderMouse(guiMouse, panelLeftConv, out int slotIdxConv, out _))
            {
                if (TryPaperdollPlaceWorldPickup(slotIdxConv))
                {
                    SealBridgedFloatIfUsingItemOrphaned(bridgedPortableFromCursor);
                    NotifyTutorialMouseWorldPickupPlaced(bridgedPortableFromCursor);
                    return true;
                }

                RestoreBridgedCursorPortable(bridgedPortableFromCursor);
                return bridgedPortableFromCursor;
            }

            if (TryGetStuffListInsertIndexFromMouse(guiMouse, panelLeftConv, out int insertIdxConv))
            {
                if (TryInsertFloatingUsingItemAt(insertIdxConv))
                {
                    SealBridgedFloatIfUsingItemOrphaned(bridgedPortableFromCursor);
                    NotifyTutorialMouseWorldPickupPlaced(bridgedPortableFromCursor);
                    return true;
                }

                RestoreBridgedCursorPortable(bridgedPortableFromCursor);
                return bridgedPortableFromCursor;
            }

            if (TryHitStuffGridForWorldPickup(guiMouse, panelLeftConv, out int listIndexConv))
            {
                if (TryStuffPlaceFloatingItemAtListIndex(listIndexConv))
                {
                    SealBridgedFloatIfUsingItemOrphaned(bridgedPortableFromCursor);
                    NotifyTutorialMouseWorldPickupPlaced(bridgedPortableFromCursor);
                    return true;
                }

                RestoreBridgedCursorPortable(bridgedPortableFromCursor);
                return bridgedPortableFromCursor;
            }

            RestoreBridgedCursorPortable(bridgedPortableFromCursor);
            return false;
        }

        if (!IsGuiMouseOverInventoryPanel(guiMouse, invPosition))
        {
            RestoreBridgedCursorPortable(bridgedPortableFromCursor);
            return false;
        }

        GetInventoryPanelLayout(out float panelLeft, out _);

        if (TryGetPaperdollSlotUnderMouse(guiMouse, panelLeft, out int slotIdx, out _))
        {
            if (TryPaperdollPlaceWorldPickup(slotIdx))
            {
                SealBridgedFloatIfUsingItemOrphaned(bridgedPortableFromCursor);
                NotifyTutorialMouseWorldPickupPlaced(bridgedPortableFromCursor);
                return true;
            }

            RestoreBridgedCursorPortable(bridgedPortableFromCursor);
            return bridgedPortableFromCursor;
        }

        if (TryGetStuffListInsertIndexFromMouse(guiMouse, panelLeft, out int insertIdx))
        {
            if (TryInsertFloatingUsingItemAt(insertIdx))
            {
                SealBridgedFloatIfUsingItemOrphaned(bridgedPortableFromCursor);
                NotifyTutorialMouseWorldPickupPlaced(bridgedPortableFromCursor);
                return true;
            }

            RestoreBridgedCursorPortable(bridgedPortableFromCursor);
            return bridgedPortableFromCursor;
        }

        if (TryHitStuffGridForWorldPickup(guiMouse, panelLeft, out int listIndex))
        {
            if (TryStuffPlaceFloatingItemAtListIndex(listIndex))
            {
                SealBridgedFloatIfUsingItemOrphaned(bridgedPortableFromCursor);
                NotifyTutorialMouseWorldPickupPlaced(bridgedPortableFromCursor);
                return true;
            }

            RestoreBridgedCursorPortable(bridgedPortableFromCursor);
            return bridgedPortableFromCursor;
        }

        RestoreBridgedCursorPortable(bridgedPortableFromCursor);
        return false;
    }

    private static Rect PaperdollSlotHitRect(float panelLeft, int slotIndex)
    {
        float x = panelLeft + 3 * invSlots[slotIndex].cx;
        float y = 30 + 3.6f * invSlots[slotIndex].cy;
        return new Rect(x, y, 48, 58);
    }

    private bool PaperdollSlotHitTest(int slotIndex, Vector2 guiMouse, float panelLeft)
    {
        if (slotIndex == (int)EInvSlot.Hands)
        {
            Rect r1 = PaperdollSlotHitRect(panelLeft, slotIndex);
            Rect r2 = new Rect(r1.x + 72, r1.y, 48, 58);
            return r1.Contains(guiMouse) || r2.Contains(guiMouse);
        }

        return PaperdollSlotHitRect(panelLeft, slotIndex).Contains(guiMouse);
    }

    /// <summary>Top-most paperdoll slot under the mouse; <paramref name="secondHandsRect"/> if over the auxiliary gloves rect.</summary>
    private bool TryGetPaperdollSlotUnderMouse(Vector2 guiMouse, float panelLeft, out int slotIdx, out bool secondHandsRect)
    {
        secondHandsRect = false;
        slotIdx = -1;
        for (int ord = slotDrawOrder.Length - 1; ord >= 0; ord--)
        {
            int i = (int)slotDrawOrder[ord];
            if (i == (int)EInvSlot.Hands)
            {
                Rect r1 = PaperdollSlotHitRect(panelLeft, i);
                Rect r2 = new Rect(r1.x + 72, r1.y, 48, 58);
                if (r2.Contains(guiMouse))
                {
                    slotIdx = i;
                    secondHandsRect = true;
                    return true;
                }

                if (r1.Contains(guiMouse))
                {
                    slotIdx = i;
                    return true;
                }

                continue;
            }

            if (PaperdollSlotHitRect(panelLeft, i).Contains(guiMouse))
            {
                slotIdx = i;
                return true;
            }
        }

        return false;
    }

    private EInvSlot GetDesiredPaperdollSlotForItem(UUObject obj)
    {
        if (obj == null)
            return EInvSlot.None;
        EInvSlot invSlot = EInvSlot.None;
        switch (obj.getClass)
        {
        case UUObject.EClass.Armour:
        case UUObject.EClass.Armour2:
            invSlot = slotMap[(int)obj.type & 31];
            if (invSlot == EInvSlot.LeftHand && PlayerData.sData.leftHanded)
                invSlot = EInvSlot.RightHand;
            if (invSlot == EInvSlot.RightFinger
                && invSlotContents[(int)EInvSlot.RightFinger] != null
                && invSlotContents[(int)EInvSlot.LeftFinger] == null)
                invSlot = EInvSlot.LeftFinger;
            break;
        case UUObject.EClass.Weapons:
            invSlot = PlayerData.sData.leftHanded ? EInvSlot.LeftHand : EInvSlot.RightHand;
            break;
        case UUObject.EClass.LampsAndWands:
            invSlot = EInvSlot.RightShoulder;
            if (invSlotContents[(int)EInvSlot.RightShoulder] != null
                && invSlotContents[(int)EInvSlot.LeftShoulder] == null)
                invSlot = EInvSlot.LeftShoulder;
            break;
        }

        return invSlot;
    }

    /// <summary>Food / potions: paperdoll eat-or-quaff only when dropped on head or torso slots.</summary>
    private static bool IsEatOrQuaffPaperdollSlot(int slotIndex)
    {
        return slotIndex == (int)EInvSlot.Head || slotIndex == (int)EInvSlot.Torso;
    }

    private static bool IsShoulderPaperdollSlot(int slotIndex)
    {
        return slotIndex == (int)EInvSlot.RightShoulder || slotIndex == (int)EInvSlot.LeftShoulder;
    }

    /// <summary>Mouse floating drop / cursor: whether this paperdoll slot accepts the carried item.</summary>
    private bool PaperdollSlotAcceptsFloatingEquip(UUObject obj, int slotIndex)
    {
        if (obj == null)
            return false;

        EInvSlot desired = GetDesiredPaperdollSlotForItem(obj);
        if (desired != EInvSlot.None && (int)desired == slotIndex)
            return true;

        if (obj.getClass == UUObject.EClass.LampsAndWands && IsShoulderPaperdollSlot(slotIndex))
            return true;

        if ((obj is Food || obj is Potion) && IsEatOrQuaffPaperdollSlot(slotIndex))
            return true;

        return false;
    }

    private static bool TryGetIncenseBurnPair(UUObject a, UUObject b, out Incense incense)
    {
        incense = null;
        if (a == null || b == null)
        {
            return false;
        }

        if (a.type == EObjectType.BlockOfIncense && b is LightSource ls && ls.IsLit())
        {
            incense = a as Incense;
            return incense != null;
        }

        if (b.type == EObjectType.BlockOfIncense && a is LightSource ls2 && ls2.IsLit())
        {
            incense = b as Incense;
            return incense != null;
        }

        return false;
    }

    /// <summary>Light, snuff, incense burn, and item combines are disabled while the trade conversation UI is open.</summary>
    private static bool CanCraftInInventory()
    {
        return Conversations.runningConversation == null;
    }

    private bool TryIncenseBurnInteraction(UUObject held, UUObject target)
    {
        if (!TryGetIncenseBurnPair(held, target, out Incense incense))
        {
            return false;
        }

        if (!CanCraftInInventory())
        {
            NormalizeUnusedCombineHeldItem(true);
            return true;
        }

        if (held != null && held.type == EObjectType.BlockOfIncense && held.quantity > 1)
        {
            UUObject peeled = LevelLoader.CreateObjectOfType(held.type);
            CopyPeeledStackProperties(held, peeled);
            peeled.PostLoadInitialize();
            --held.quantity;
            List<UUObject> heldList = FindObjectInInventory(held);
            if (heldList != null)
            {
                heldList.Add(held);
            }
            else
            {
                GetCurrentListInternal().Add(held);
            }

            incense = peeled as Incense;
        }
        else if (target != null && target.type == EObjectType.BlockOfIncense && target.quantity > 1)
        {
            incense = Split(target) as Incense;
        }

        incense.TryBurn();
        if (Utils.CanSleepHere())
        {
            usingItem = null;
            mouseCursorCarriedPortable = null;
        }
        else
        {
            NormalizeUnusedCombineHeldItem(true);
        }

        return true;
    }

    private bool TryGetMousePrimaryHint(UUObject carried, UUObject target, int paperdollSlot, bool overPaperdoll,
        out string hint)
    {
        hint = null;
        if (carried != null)
        {
            if (TryGetIncenseBurnPair(carried, target, out _))
            {
                if (!CanCraftInInventory())
                {
                    return false;
                }

                hint = "Burn";
                return true;
            }

            if (overPaperdoll && paperdollSlot >= 0 && PaperdollSlotAcceptsFloatingEquip(carried, paperdollSlot))
            {
                hint = "Swap";
                return true;
            }

            return false;
        }

        if (target != null)
        {
            hint = $"{target.GetLookText()}/Move";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Held oil flask on an equipped torch/lantern (paperdoll). Handles success, lit/full refusal, and craft lockout.
    /// Returns false when the pair is not oil + torch/lantern (caller should continue).
    /// </summary>
    private bool TryCombineHeldOilWithPaperdollLight(UUObject slotObj)
    {
        if (usingItem == null || slotObj == null || usingItem.type != EObjectType.OilFlask)
        {
            return false;
        }

        if (slotObj.type != EObjectType.Torch && slotObj.type != EObjectType.Lantern)
        {
            return false;
        }

        if (!CanCraftInInventory())
        {
            NormalizeUnusedCombineHeldItem(true);
            return true;
        }

        if (usingItem.TryCombine(slotObj))
        {
            if (usingItem != null && FindObjectInInventory(usingItem) == null)
            {
                mouseCursorCarriedPortable = usingItem;
            }
            usingItem = null;
            return true;
        }

        // Lit / already full — message already shown; restore held oil without fail beep.
        NormalizeUnusedCombineHeldItem(false);
        return true;
    }

    private bool TryPaperdollPlaceWorldPickup(int slotIndex)
    {
        if (usingItem == null)
            return false;

        UUObject slotObj = invSlotContents[slotIndex];
        if (slotObj != null && TryIncenseBurnInteraction(usingItem, slotObj))
        {
            return true;
        }

        if (slotObj != null && TryCombineHeldOilWithPaperdollLight(slotObj))
        {
            return true;
        }

        bool slotMatchesEquip = PaperdollSlotAcceptsFloatingEquip(usingItem, slotIndex);
        // Food / potions have no equip slot; dropping on head/torso runs Equip() (eat / quaff).
        bool tryConsumeOnFigure = !slotMatchesEquip
            && (usingItem is Food || usingItem is Potion)
            && IsEatOrQuaffPaperdollSlot(slotIndex);

        if (!slotMatchesEquip && !tryConsumeOnFigure)
        {
            Utils.PlayClip2d(fail);
            mouseCursorCarriedPortable = usingItem;
            usingItem = null;
            return true;
        }

        UUObject obj = usingItem;
        switch (obj.Equip())
        {
        case EEquipAction.Consume:
            if (obj.quantity > 1)
            {
                obj.quantity--;
            }
            else
            {
                Utils.DestroyItem(obj);
                usingItem = null;
                mouseCursorCarriedPortable = null;
            }
            return true;
        case EEquipAction.Equip:
            if (!slotMatchesEquip)
            {
                Utils.PlayClip2d(fail);
                mouseCursorCarriedPortable = usingItem;
                usingItem = null;
                return true;
            }

            DoEquipFromWorldPickup(obj, slotIndex);
            return true;
        default:
            if (tryConsumeOnFigure)
            {
                mouseCursorCarriedPortable = usingItem;
                usingItem = null;
                return true;
            }

            Utils.PlayClip2d(fail);
            mouseCursorCarriedPortable = usingItem;
            usingItem = null;
            return true;
        }
    }

    private void DoEquipFromWorldPickup(UUObject obj, int targetSlotIndex)
    {
        List<UUObject> currentList = GetCurrentListInternal();

        if (obj.quantity > 1)
        {
            int quantityRemaining = obj.quantity - 1;
            obj.quantity = 1;
            UUObject newItem = LevelLoader.CreateObjectOfType(obj.type);
            newItem.quantity = quantityRemaining;
            CopyPeeledStackProperties(obj, newItem);
            newItem.PostLoadInitialize();
            currentList.Add(newItem);
        }

        if (obj.getClass == UUObject.EClass.Weapons && fist.isActiveAndEnabled)
            fist.Unequip();

        if (invSlotContents[targetSlotIndex] != null)
        {
            UUObject oldObj = invSlotContents[targetSlotIndex];
            oldObj.Unequip();
            mouseCursorCarriedPortable = oldObj;
        }
        else
        {
            mouseCursorCarriedPortable = null;
        }

        usingItem = null;
        invSlotContents[targetSlotIndex] = obj;
        // Stay on stuff list so mouse drag/pickup keeps working (floating equip is mouse-driven).
        currentArea = EInventoryArea.Stuff;
        equipSlot = targetSlotIndex;
        Utils.PlayClip2d(PlayerObject.Player.pickupClip);
    }

    private bool TryInsertFloatingUsingItemAt(int insertIndex)
    {
        List<UUObject> currentList = GetCurrentListInternal();
        if (!TryValidateFloatingPlaceIntoOpenContainer(usingItem, currentList))
        {
            return false;
        }

        UUObject u = usingItem;
        usingItem = null;
        mouseCursorCarriedPortable = null;
        int prevIdx = currentList.IndexOf(u);
        if (prevIdx >= 0)
        {
            currentList.RemoveAt(prevIdx);
            if (prevIdx < insertIndex)
                insertIndex--;
        }
        else
        {
            List<UUObject> prevList = FindListContainingItem(inventory, u);
            if (prevList != null)
                prevList.Remove(u);
        }

        insertIndex = Mathf.Clamp(insertIndex, 0, currentList.Count);
        currentList.Insert(insertIndex, u);
        index = insertIndex;
        currentArea = EInventoryArea.Stuff;
        Utils.PlayClip2d(place);
        return true;
    }

    /// <summary>
    /// Gaps between stuff cells (I-beam insertion targets). <paramref name="rawInsertIndex"/> is unclamped list index.
    /// </summary>
    private bool TryGetStuffListInsertGapFromMouse(Vector2 guiMouse, float panelLeft, out int rawInsertIndex,
        out Vector2 centerGui)
    {
        rawInsertIndex = 0;
        centerGui = default;
        float relX = guiMouse.x - (panelLeft + ix);
        float relY = guiMouse.y - iy;
        const float kSlotW = 48f;
        const float kBracketH = 58f;

        if (relY < -12f || relY > dy * 4f + kBracketH + 8f)
            return false;

        int row = Mathf.Clamp((int)(relY / dy), 0, 3);
        float yInRow = relY - row * dy;

        // Vertical gaps between rows (below the 58px-tall selection bracket row)
        if (yInRow >= kBracketH && yInRow < dy)
        {
            int col = Mathf.Clamp((int)((relX + dx * 0.5f) / dx), 0, 3);
            rawInsertIndex = viewIndex + (row + 1) * 4 + col;
            centerGui = new Vector2(
                panelLeft + ix + col * dx + dx * 0.5f,
                iy + row * dy + kBracketH + (dy - kBracketH) * 0.5f);
            return true;
        }

        if (yInRow < 0f || yInRow >= kBracketH)
            return false;

        // Horizontal gap before column 0
        if (relX >= -12f && relX < 0f)
        {
            rawInsertIndex = viewIndex + row * 4;
            centerGui = new Vector2(panelLeft + ix - 4f, iy + row * dy + kBracketH * 0.5f);
            return true;
        }

        // Horizontal gaps between columns
        for (int c = 1; c <= 3; c++)
        {
            float gapL = (c - 1) * dx + kSlotW;
            float gapR = c * dx;
            if (relX > gapL && relX < gapR)
            {
                rawInsertIndex = viewIndex + row * 4 + c;
                centerGui = new Vector2(
                    panelLeft + ix + gapL + (gapR - gapL) * 0.5f,
                    iy + row * dy + kBracketH * 0.5f);
                return true;
            }
        }

        // Gap after last column in row (append within row / before next row)
        if (relX > 3 * dx + kSlotW && relX < 4 * dx + 20f)
        {
            rawInsertIndex = viewIndex + row * 4 + 4;
            centerGui = new Vector2(
                panelLeft + ix + 3 * dx + kSlotW + (dx - kSlotW) * 0.5f,
                iy + row * dy + kBracketH * 0.5f);
            return true;
        }

        return false;
    }

    /// <summary>Gaps between stuff cells: horizontal gutters and vertical row spacing (insertion point in list).</summary>
    private bool TryGetStuffListInsertIndexFromMouse(Vector2 guiMouse, float panelLeft, out int insertIndex)
    {
        insertIndex = 0;
        if (!TryGetStuffListInsertGapFromMouse(guiMouse, panelLeft, out int raw, out _))
            return false;
        List<UUObject> currentList = GetCurrentListInternal();
        // At/past the append position (after last item / among blanks): grid hit + slot cursor, not I-beam.
        if (raw >= currentList.Count)
            return false;
        insertIndex = raw;
        return true;
    }

    private bool TryGetStuffListInsertPreviewCenter(Vector2 guiMouse, float panelLeft, out Vector2 centerGui)
    {
        centerGui = default;
        if (!TryGetStuffListInsertGapFromMouse(guiMouse, panelLeft, out int raw, out Vector2 ctr))
        {
            return false;
        }
        List<UUObject> currentList = GetCurrentListInternal();
        if (raw >= currentList.Count)
        {
            return false;
        }
        centerGui = ctr;
        return true;
    }

    private Rect GetStuffGridHitRect(float panelLeft)
    {
        return new Rect(panelLeft + ix, iy, dx * 4, dy * 4);
    }

    private bool TryApplyStuffViewScroll(int rowDelta, int itemsToDisplayCount)
    {
        if ((rowDelta < 0 && viewIndex > 0)
            || (rowDelta > 0 && viewIndex <= itemsToDisplayCount - 16))
        {
            viewIndex = Mathf.Max(0, viewIndex + 4 * rowDelta);
            Utils.PlayClip2d(moveCursor);
            return true;
        }

        return false;
    }

    private bool TryHandleInventoryScrollClick(Vector2 guiMouse, float invPosition, int itemsToDisplayCount)
    {
        if (DataLoader.sDataLoader?.buttonsTex == null || DataLoader.sDataLoader.buttonsTex.Length <= 28)
        {
            return false;
        }

        float scrollIndicatorX = Screen.width - invPosition + 174;

        if (viewIndex > 0)
        {
            Texture2D upArrow = DataLoader.sDataLoader.buttonsTex[27];
            if (upArrow != null)
            {
                Rect r = new Rect(scrollIndicatorX, 256, 3 * upArrow.width, 3.6f * upArrow.height);
                if (r.Contains(guiMouse))
                {
                    return TryApplyStuffViewScroll(-1, itemsToDisplayCount);
                }
            }
        }

        // Same condition as down-arrow visibility (>=).
        if (itemsToDisplayCount >= viewIndex + 16)
        {
            Texture2D downArrow = DataLoader.sDataLoader.buttonsTex[28];
            if (downArrow != null)
            {
                Rect r = new Rect(scrollIndicatorX + downArrow.width * 3 + 6, 256, 3 * downArrow.width, 3.6f * downArrow.height);
                if (r.Contains(guiMouse))
                {
                    return TryApplyStuffViewScroll(1, itemsToDisplayCount);
                }
            }
        }

        return false;
    }

    private bool TryHandleOpenContainerCloseClick(Vector2 guiMouse, float invPosition)
    {
        if (stack.Count == 0)
            return false;
        float panelLeft = Screen.width - invPosition;
        Rect r = new Rect(panelLeft + ix, iy - 64, 48, 58);
        if (!r.Contains(guiMouse))
            return false;
        BackOutOfContainer();
        return true;
    }

    private bool TryHitStuffGridForWorldPickup(Vector2 guiMouse, float panelLeft, out int listIndex)
    {
        listIndex = 0;
        float relX = guiMouse.x - (panelLeft + ix);
        float relY = guiMouse.y - iy;
        if (relX < 0 || relY < 0)
            return false;
        int col = (int)(relX / dx);
        int row = (int)(relY / dy);
        if (col < 0 || col > 3 || row < 0 || row >= 4)
            return false;

        List<UUObject> currentList = GetCurrentListInternal();
        listIndex = viewIndex + row * 4 + col;
        if (listIndex > currentList.Count)
            listIndex = currentList.Count;
        return true;
    }

    /// <summary>Unclamped list index for the stuff grid cell under the mouse (4×4 visible window).</summary>
    private bool TryGetStuffGridCellListIndex(Vector2 guiMouse, float panelLeft, out int listIndex)
    {
        listIndex = 0;
        float relX = guiMouse.x - (panelLeft + ix);
        float relY = guiMouse.y - iy;
        if (relX < 0 || relY < 0)
            return false;
        int col = (int)(relX / dx);
        int row = (int)(relY / dy);
        if (col < 0 || col > 3 || row < 0 || row >= 4)
            return false;
        listIndex = viewIndex + row * 4 + col;
        return true;
    }

    private bool TryStuffPlaceFloatingItemAtListIndex(int listIndex)
    {
        List<UUObject> currentList = GetCurrentListInternal();
        index = Mathf.Clamp(listIndex, 0, currentList.Count);
        currentArea = EInventoryArea.Stuff;

        if (index < currentList.Count && currentList[index] != null
            && currentList[index].getClass == UUObject.EClass.Containers)
        {
            UUObject container = currentList[index];
            if (usingItem == container)
            {
                Utils.PlayClip2d(fail);
                if (usingItem != null && FindObjectInInventory(usingItem) == null)
                {
                    mouseCursorCarriedPortable = usingItem;
                }
                usingItem = null;
                return true;
            }

            if (container.contents == null)
            {
                container.contents = new List<UUObject>();
            }

            if (usingItem != null)
            {
                if (!ValidatePutUsingItemInContainer(container, usingItem))
                {
                    if (FindObjectInInventory(usingItem) == null)
                    {
                        mouseCursorCarriedPortable = usingItem;
                    }
                    usingItem = null;
                    return true;
                }

                // Cursor-held drop: if the item *could* be stored, open the container and keep holding it (same as click-to-open with empty hand).
                bool openToStow = container.type is EObjectType.Sack or EObjectType.Pack or EObjectType.Box;
                if (openToStow)
                {
                    stack.Add(container);
                    index = 0;
                    viewIndex = 0;
                    Utils.PlayClip2d(open);
                    mouseCursorCarriedPortable = usingItem;
                    usingItem = null;
                    return true;
                }

                TryPutUsingItemInContainer(skipValidation: true);
            }
            return true;
        }

        if (index < currentList.Count && currentList[index] != null && usingItem != null)
        {
            TryCombineObjects();
            return true;
        }

        if (usingItem != null && index == currentList.Count)
        {
            if (!TryValidateFloatingPlaceIntoOpenContainer(usingItem, currentList))
            {
                if (FindObjectInInventory(usingItem) == null)
                {
                    mouseCursorCarriedPortable = usingItem;
                }
                usingItem = null;
                return true;
            }

            FinishDropIntoOpenContainer(usingItem, currentList);
            return true;
        }

        Utils.PlayClip2d(fail);
        if (usingItem != null && FindObjectInInventory(usingItem) == null)
            mouseCursorCarriedPortable = usingItem;
        usingItem = null;
        return true;
    }

    private void FinishDropIntoOpenContainer(UUObject movingItem, List<UUObject> currentList)
    {
        RemoveItemFromInventory(movingItem);
        currentList.Add(movingItem);
        if (GetCurrentContainerInternal()?.type == EObjectType.RuneBag && movingItem is RuneStone)
        {
            Magic.AddRunestone(movingItem.type);
            RuneStone.PlayStowSound(movingItem);
        }
        else
        {
            Utils.PlayClip2d(place);
        }

        usingItem = null;
    }

    public Rect GetPanelGuiRectForOutsideClick()
    {
        const float kExtendedInvPosition = 16 + 4 * 64;
        return GetInventoryPanelScreenRect(lerpIn * kExtendedInvPosition);
    }

    /// <summary>Same horizontal slide distance as <see cref="GetPanelGuiRectForOutsideClick"/>.</summary>
    public float GetCurrentInventoryPanelInvPosition()
    {
        const float kExtendedInvPosition = 16 + 4 * 64;
        return lerpIn * kExtendedInvPosition;
    }

    public bool IsGuiMouseOverInventoryPanelGui(Vector2 guiMouse)
    {
        return IsGuiMouseOverInventoryPanel(guiMouse, GetCurrentInventoryPanelInvPosition());
    }

    /// <summary>Name Enchantment from magic priming: paperdoll slot or stuff cell under the mouse when the inventory panel is open.</summary>
    public bool TryGetNameEnchantmentInventoryTarget(Vector2 guiMouse, out UUObject target)
    {
        target = null;
        if (!PlayerPanelState.IsEffectivelyExploringInventory)
        {
            return false;
        }

        float invPos = GetCurrentInventoryPanelInvPosition();
        if (!IsGuiMouseOverInventoryPanel(guiMouse, invPos))
        {
            return false;
        }

        float panelLeft = Screen.width - invPos;
        if (TryGetPaperdollSlotUnderMouse(guiMouse, panelLeft, out int pdSlot, out _)
            && pdSlot >= 0 && pdSlot < invSlotContents.Length
            && invSlotContents[pdSlot] != null)
        {
            target = invSlotContents[pdSlot];
            return true;
        }

        if (TryGetStuffGridCellListIndex(guiMouse, panelLeft, out int cellIdx))
        {
            List<UUObject> list = GetCurrentListInternal();
            if (cellIdx >= 0 && cellIdx < list.Count && list[cellIdx] != null)
            {
                target = list[cellIdx];
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Primed Name Enchantment uses LMB down to cast; inventory arms stuff/paperdoll pending on the same press and would
    /// run <see cref="UUObject.TryInventoryUse"/> on release — suppress that once so the item is not described twice.
    /// </summary>
    public void SuppressInventoryMousePrimaryShortReleaseOnce()
    {
        suppressStuffMousePrimaryOnNextLeftRelease = true;
        suppressPaperdollMousePrimaryOnNextLeftRelease = true;
    }

    /// <summary>
    /// After cursor charge-throw, <see cref="Interaction"/> LateUpdate must not treat the same LMB release as a short-click look
    /// (portable is cleared in Update before Interaction runs, often centering the thrown object).
    /// </summary>
    public void SuppressMouseLookOnNextLeftRelease()
    {
        suppressMouseLookOnNextLeftRelease = true;
    }

    public bool ConsumeSuppressMouseLookOnNextLeftRelease()
    {
        if (!suppressMouseLookOnNextLeftRelease)
        {
            return false;
        }

        suppressMouseLookOnNextLeftRelease = false;
        return true;
    }

    private bool IsGuiMouseOverInventoryPanel(Vector2 guiMouse, float invPosition)
    {
        Rect r = GetInventoryPanelScreenRect(invPosition);
        return r.width > 0f && r.height > 0f && r.Contains(guiMouse);
    }

    private Rect GetInventoryPanelScreenRect(float invPosition)
    {
        Texture2D tex = DataLoader.sDataLoader != null ? DataLoader.sDataLoader.panelsTex[0] : null;
        if (tex == null)
            return default;
        float panelLeft = Screen.width - invPosition;
        float w = 3f * tex.width;
        float h = Mathf.Min(Screen.height - 30f, 3.6f * tex.height + 3.6f * 50f);
        return new Rect(panelLeft, 30f, w, h);
    }

    private static bool IsPlayerTradeSlotIndex(int slotIndex)
    {
        return (slotIndex & 3) >= 2;
    }

    private void GetTradeTrayColumnXs(out int tx1, out int tx2)
    {
        tx1 = (Screen.width - 882) / 2 + 167;
        tx2 = (Screen.width - 882) / 2 + 602;
    }

    private Rect GetTradeSlotCellRect(int i)
    {
        GetTradeTrayColumnXs(out int tx1, out int tx2);
        float x = tx1 + ddx * (i & 1) + (tx2 - tx1) * (i & 2) / 2;
        float y = y1 + ddy * (i / 4);
        return new Rect(x, y, 48, 58);
    }

    private Rect GetTradeSelectionDotRect(int i)
    {
        GetTradeTrayColumnXs(out int tx1, out int tx2);
        float x = tx1 + ddx * (i & 1) + (tx2 - tx1) * (i & 2) / 2;
        float y = y1 + ddy * (i / 4);
        int xoff = (i & 1) > 0 ? xo2 : xo1;
        return new Rect(x + xoff, y + yo1, 9, 11);
    }

    /// <summary>Axis-aligned union of all eight trade slot hit rects (conversation UI).</summary>
    public Rect GetTradeTraysBoundingRect()
    {
        if (Conversations.runningConversation == null)
            return default;

        Rect u = GetTradeSlotCellRect(0);
        for (int i = 1; i < 8; ++i)
        {
            u = RectUnion(u, GetTradeSlotCellRect(i));
        }

        return u;
    }

    private static Rect RectUnion(Rect a, Rect b)
    {
        float xMin = Mathf.Min(a.xMin, b.xMin);
        float yMin = Mathf.Min(a.yMin, b.yMin);
        float xMax = Mathf.Max(a.xMax, b.xMax);
        float yMax = Mathf.Max(a.yMax, b.yMax);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private bool TryGetTradeSlotUnderMouse(Vector2 guiMouse, out int slotIndex)
    {
        slotIndex = -1;
        if (Conversations.runningConversation == null)
            return false;

        for (int i = 7; i >= 0; --i)
        {
            if (GetTradeSlotCellRect(i).Contains(guiMouse))
            {
                slotIndex = i;
                return true;
            }
        }

        return false;
    }

    private bool TryGetTradeSelectionDotUnderMouse(Vector2 guiMouse, out int slotIndex)
    {
        slotIndex = -1;
        if (Conversations.runningConversation == null)
            return false;

        for (int i = 7; i >= 0; --i)
        {
            if (GetTradeSelectionDotRect(i).Contains(guiMouse))
            {
                slotIndex = i;
                return true;
            }
        }

        return false;
    }

    public bool IsGuiMouseOverTradeTraysBounding(Vector2 guiMouse)
    {
        Rect r = GetTradeTraysBoundingRect();
        return r.width > 0f && r.height > 0f && r.Contains(guiMouse);
    }

    private class InvSlot
    {
        // cx,cy is for cursor x,y (and also for items that use their inventory graphic for the paperdoll)
        // ax,ay is for item x,y
        public InvSlot(int _cx, int _cy, int _ax, int _ay, int _goLeft, int _goRight, int _goUp, int _goDown)
        {
            cx = _cx;
            cy = _cy;
            ax = _ax;
            ay = _ay;
            goLeft = _goLeft;
            goRight = _goRight;
            goUp = _goUp;
            goDown = _goDown;
        }

        public readonly int cx;
        public readonly int cy;
        public readonly int ax;
        public readonly int ay;
        public readonly int goLeft;
        public readonly int goRight;
        public readonly int goUp;
        public readonly int goDown;
    }

    private static InvSlot[] invSlots =
    {
        new InvSlot(9, 6, 9, 6, -1, 1, -1, 3), // 0
        new InvSlot(34, 3, 31, 3, 0, 2, -1, 4), // 1
        new InvSlot(58, 6, 58, 6, 1, -1, -1, 5), // 2
        new InvSlot(6, 28, 6, 28, -1, 4, 0, 7), // 3
        new InvSlot(34, 23, 26, 15, 3, 5, 1, 6), // 4
        new InvSlot(60, 28, 60, 28, 4, -1, 2, 9), // 5
        new InvSlot(20, 37, 25, 35, 3, 5, 4, 8), // 6 - hands
        new InvSlot(18, 43, 18, 43, 3, 8, 6, 10), // 7
        new InvSlot(34, 44, 32, 17, 7, 9, 6, 10), // 8
        new InvSlot(49, 43, 49, 43, 8, 5, 6, 10), // 9
        new InvSlot(34, 60, 30, 59, 7, 9, 8, -1), // 10
    };

    public UUObject[] invSlotContents = new UUObject[11];

    // map of (object type & 31) to paperdoll position
    private static EInvSlot[] slotMap =
    {
        EInvSlot.Torso, EInvSlot.Torso, EInvSlot.Torso,
        EInvSlot.Legs, EInvSlot.Legs, EInvSlot.Legs,
        EInvSlot.Hands, EInvSlot.Hands, EInvSlot.Hands,
        EInvSlot.Feet, EInvSlot.Feet, EInvSlot.Feet,
        EInvSlot.Head, EInvSlot.Head, EInvSlot.Head,
        EInvSlot.Feet, // dragonskin boots
        EInvSlot.Head, EInvSlot.Head, EInvSlot.Head, // crowns
        EInvSlot.None, EInvSlot.None, EInvSlot.None,
        EInvSlot.RightFinger,
        EInvSlot.LeftHand,
        EInvSlot.RightFinger, EInvSlot.RightFinger, EInvSlot.RightFinger,
        EInvSlot.LeftHand, EInvSlot.LeftHand, EInvSlot.LeftHand, EInvSlot.LeftHand,
        EInvSlot.LeftHand
    };

    private static EInvSlot[] slotDrawOrder =
    {
        EInvSlot.Legs,
        EInvSlot.Torso,
        EInvSlot.Head,
        EInvSlot.Feet,
        EInvSlot.Hands,
        EInvSlot.RightShoulder,
        EInvSlot.LeftShoulder,
        EInvSlot.RightHand,
        EInvSlot.LeftHand,
        EInvSlot.RightFinger,
        EInvSlot.LeftFinger,
    };

    // A map of all object types to a conceptual "size" value.
    // Size Scale: 1=Tiny, 2=Small, 3=Medium, 4=Large, 5=Huge
    private static readonly Dictionary<EObjectType, int> objectSizes = new()
    {
        // === TINY (SIZE 1) ===
        // --- Rings, Keys, & Quest Items ---
        { EObjectType.IronRing, 1 }, { EObjectType.GoldRing, 1 }, { EObjectType.SilverRing, 1 }, { EObjectType.RedRing, 1 },
        { EObjectType.KeyOfTruth, 1 }, { EObjectType.KeyOfLove, 1 }, { EObjectType.KeyOfCourage, 1 }, { EObjectType.TwoPartKeyA, 1 },
        { EObjectType.TwoPartKeyB, 1 }, { EObjectType.TwoPartKeyC, 1 }, { EObjectType.KeyOfInfinity, 1 }, { EObjectType.KeyA, 1 },
        { EObjectType.KeyB, 1 }, { EObjectType.KeyC, 1 }, { EObjectType.KeyD, 1 }, { EObjectType.KeyE, 1 },
        { EObjectType.KeyF, 1 }, { EObjectType.KeyG, 1 }, { EObjectType.KeyH, 1 }, { EObjectType.KeyI, 1 },
        { EObjectType.KeyJ, 1 }, { EObjectType.KeyK, 1 }, { EObjectType.KeyL, 1 }, { EObjectType.KeyM, 1 }, { EObjectType.KeyN, 1 },
        { EObjectType.Lockpick, 1 }, { EObjectType.CrystalSplinter, 1 }, { EObjectType.OrbRock, 1 }, { EObjectType.BlockOfIncense, 1 },
        { EObjectType.StrongThread, 1 }, { EObjectType.SilverSeed, 1 }, { EObjectType.Leeches, 1 }, { EObjectType.Moonstone, 1 },
        { EObjectType.Spike, 1 }, { EObjectType.OilFlask, 1 }, { EObjectType.Amulet, 1 }, { EObjectType.AnkhPendant, 1 },
        { EObjectType.Medallion, 1 }, { EObjectType.Figurine, 1 }, { EObjectType.DragonScales, 1 }, { EObjectType.BlockOfBurningIncense, 1 },
        // --- Runes & Ammo ---
        { EObjectType.Runestone, 1 },
        { EObjectType.RunestoneAn, 1 }, { EObjectType.RunestoneBet, 1 }, { EObjectType.RunestoneCorp, 1 }, { EObjectType.RunestoneDes, 1 },
        { EObjectType.RunestoneEx, 1 }, { EObjectType.RunestoneFlam, 1 }, { EObjectType.RunestoneGrav, 1 }, { EObjectType.RunestoneHur, 1 },
        { EObjectType.RunestoneIn, 1 }, { EObjectType.RunestoneJux, 1 }, { EObjectType.RunestoneKal, 1 }, { EObjectType.RunestoneLor, 1 },
        { EObjectType.RunestoneMani, 1 }, { EObjectType.RunestoneNox, 1 }, { EObjectType.RunestoneOrt, 1 }, { EObjectType.RunestonePor, 1 },
        { EObjectType.RunestoneQuas, 1 }, { EObjectType.RunestoneRel, 1 }, { EObjectType.RunestoneSanct, 1 }, { EObjectType.RunestoneTym, 1 },
        { EObjectType.RunestoneUus, 1 }, { EObjectType.RunestoneVas, 1 }, { EObjectType.RunestoneWis, 1 }, { EObjectType.RunestoneYlem, 1 },
        { EObjectType.SlingStone, 1 }, { EObjectType.CrossbowBolt, 1 }, { EObjectType.Arrow, 1 }, { EObjectType.Stone, 1 },
        // --- Treasure ---
        { EObjectType.Coin, 1 }, { EObjectType.GoldCoin, 1 }, { EObjectType.Ruby, 1 }, { EObjectType.RedGem, 1 },
        { EObjectType.SmallBlueGem, 1 }, { EObjectType.LargeBlueGem, 1 }, { EObjectType.Sapphire, 1 }, { EObjectType.Emerald, 1 },
        { EObjectType.LargeGoldNugget, 1 }, { EObjectType.GoldChain, 1 }, { EObjectType.GoldPlate, 1 },
        // --- Food & Potions ---
        { EObjectType.PieceOfMeat, 1 }, { EObjectType.LoafOfBreadA, 1 }, { EObjectType.PieceOfCheese, 1 }, { EObjectType.Apple, 1 },
        { EObjectType.EarOfCorn, 1 }, { EObjectType.LoafOfBreadB, 1 }, { EObjectType.Fish, 1 }, { EObjectType.Popcorn, 1 },
        { EObjectType.Mushroom, 1 }, { EObjectType.Toadstool, 1 }, { EObjectType.BottleOfAle, 1 }, { EObjectType.RedPotion, 1 },
        { EObjectType.GreenPotion, 1 }, { EObjectType.BottleOfWater, 1 }, { EObjectType.FlaskOfPort, 1 }, { EObjectType.BottleOfWine, 1 },
        { EObjectType.RotwormStew, 1 },
        // --- Small Containers (as items) ---
        { EObjectType.Pouch, 1 }, { EObjectType.MapCase, 1 }, { EObjectType.RuneBag, 1 }, { EObjectType.OpenPouch, 1 }, { EObjectType.OpenMapCase, 1 },

        // === SMALL (SIZE 2) ===
        { EObjectType.Dagger, 2 }, // Special case weapon
        { EObjectType.WandA, 2 }, { EObjectType.WandB, 2 }, { EObjectType.WandC, 2 }, { EObjectType.WandD, 2 },
        { EObjectType.BrokenWandA, 2 }, { EObjectType.BrokenWandB, 2 }, { EObjectType.BrokenWandC, 2 }, { EObjectType.BrokenWandD, 2 },
        { EObjectType.Candle, 2 }, { EObjectType.Taper, 2 }, { EObjectType.Torch, 2 }, { EObjectType.LitCandle, 2 }, { EObjectType.LitTaper, 2 },
        { EObjectType.BookA, 2 }, { EObjectType.BookB, 2 }, { EObjectType.BookC, 2 }, { EObjectType.BookD, 2 }, { EObjectType.BookE, 2 },
        { EObjectType.BookF, 2 }, { EObjectType.BookOfHonesty, 2 }, { EObjectType.BookH, 2 }, { EObjectType.ExplodingBook, 2 },
        { EObjectType.ScrollA, 2 }, { EObjectType.ScrollB, 2 }, { EObjectType.ScrollC, 2 }, { EObjectType.ScrollD, 2 },
        { EObjectType.ScrollE, 2 }, { EObjectType.ScrollF, 2 }, { EObjectType.ScrollG, 2 }, { EObjectType.Map, 2 },
        { EObjectType.RockHammer, 2 }, { EObjectType.FishingPole, 2 }, { EObjectType.Flute, 2 }, { EObjectType.Mandolin, 2 },
        { EObjectType.Goblet, 2 }, { EObjectType.Sceptre, 2 }, { EObjectType.ShinyCup, 2 }, { EObjectType.PieceOfWoodA, 2 },
        { EObjectType.PieceOfWoodB, 2 }, { EObjectType.PlantB, 2 }, { EObjectType.PlantC, 2 }, { EObjectType.PictureOfTom, 2 },
        { EObjectType.GemCutterOfCoulnes, 2 }, { EObjectType.Orb, 2 }, { EObjectType.BrokenBlade, 2 }, { EObjectType.BrokenHilt, 2 },
        { EObjectType.ResilientSphere, 2 }, { EObjectType.Spell, 2 }, { EObjectType.DeadRotworm, 2 },

        // === MEDIUM (SIZE 3) ===
        // --- Weapons (Standard Size) ---
        { EObjectType.Fist, 3}, { EObjectType.HandAxe, 3 }, { EObjectType.BattleAxe, 3 }, { EObjectType.Axe, 3 }, { EObjectType.Shortsword, 3 },
        { EObjectType.Longsword, 3 }, { EObjectType.Broadsword, 3 }, { EObjectType.Cudgel, 3 }, { EObjectType.LightMace, 3 },
        { EObjectType.Mace, 3 }, { EObjectType.ShinySword, 3 }, { EObjectType.JeweledAxe, 3 }, { EObjectType.BlackSword, 3 },
        { EObjectType.JeweledSword, 3 }, { EObjectType.JeweledMace, 3 }, { EObjectType.Sling, 3 }, { EObjectType.Bow, 3 },
        { EObjectType.Crossbow, 3 }, { EObjectType.JeweledBow, 3 }, { EObjectType.BrokenAxe, 3 }, { EObjectType.BrokenSword, 3 },
        { EObjectType.BrokenMace, 3 },
        // --- Smaller Armor & Shields ---
        { EObjectType.LeatherGloves, 3 }, { EObjectType.ChainGauntlets, 3 }, { EObjectType.PlateGauntlets, 3 },
        { EObjectType.LeatherBoots, 3 }, { EObjectType.ChainBoots, 3 }, { EObjectType.PlateBoots, 3 },
        { EObjectType.DragonskinBoots, 3 }, { EObjectType.LeatherCap, 3 }, { EObjectType.ChainCowl, 3 },
        { EObjectType.Helmet, 3 }, { EObjectType.CrownA, 3 }, { EObjectType.CrownB, 3 }, { EObjectType.CrownC, 3 },
        { EObjectType.WoodenShield, 3 }, { EObjectType.SmallShield, 3 }, { EObjectType.Buckler, 3 }, { EObjectType.BrokenShield, 3 },
        // --- Other Medium Items ---
        { EObjectType.Lantern, 3 }, { EObjectType.LitLantern, 3 }, { EObjectType.LitTorch, 3 }, { EObjectType.LeatherVest, 3 },
        { EObjectType.SkullA, 3 }, { EObjectType.SkullB, 3 }, { EObjectType.GlowingRock, 3 }, { EObjectType.Urn, 3 },
        { EObjectType.Quiver, 3 }, { EObjectType.Bowl, 3 }, { EObjectType.BoneA, 3 }, { EObjectType.BoneB, 3 },
        { EObjectType.PileOfBonesA, 3 },

        // === LARGE (SIZE 4) ===
        // --- Bulky Armor & Shields ---
        { EObjectType.MailShirt, 4 }, { EObjectType.Breastplate, 4 }, { EObjectType.LeatherLeggings, 4 },
        { EObjectType.MailLeggings, 4 }, { EObjectType.PlateLeggings, 4 },
        { EObjectType.ShinyShield, 4 }, { EObjectType.JeweledShield, 4 },
        // --- Containers (as items) ---
        { EObjectType.Sack, 4 }, { EObjectType.Pack, 4 }, { EObjectType.Box, 4 },
        { EObjectType.Chest, 4 }, { EObjectType.GoldCoffer, 4 }, { EObjectType.OpenSack, 4 }, { EObjectType.OpenPack, 4 },
        { EObjectType.OpenBox, 4 }, { EObjectType.OpenGoldCoffer, 4 },
        // --- Other Large Items ---
        { EObjectType.Pole, 4 }, { EObjectType.SmallBoulder, 4 },

        // === HUGE (SIZE 5) ===
        { EObjectType.TowerShield, 5 },
        { EObjectType.Bedroll, 5 },
        { EObjectType.Standard, 5 }, // A battle standard/flag
        { EObjectType.Fountain, 5 },
        { EObjectType.Boulder, 5 },
        { EObjectType.LotusEspritTurbo, 5 }
    };

    public static void ShowPanel()
    {
        if (sInv == null || !PlayerPanelState.ArePanelsAvailable)
        {
            return;
        }
        StatsPanel.sStatsPanel?.Hide();
        if (PlayerPanelState.ActivePanel != EPlayerPanel.Inventory)
        {
            PlayerPanelState.SetPanel(EPlayerPanel.Inventory, fromUserToggle: false);
        }
        sInv.holdTime = 2.0f;
    }

    public static void HidePanel(bool fromUserToggle = true)
    {
        if (PlayerPanelState.ActivePanel == EPlayerPanel.Inventory)
        {
            sInv.TryStowMouseCursorCarriedPortable();
            PlayerPanelState.SetPanel(EPlayerPanel.None, fromUserToggle);
        }
        sInv.holdTime = 0.0f;
        sInv.inventorySlideWasOpenLastFrame = false;
    }

    /// <summary>
    /// Return <see cref="mouseCursorCarriedPortable"/> to inventory via <see cref="Add"/> rules (container stack, size, merge).
    /// </summary>
    public bool TryStowMouseCursorCarriedPortable()
    {
        UUObject carried = mouseCursorCarriedPortable;
        if (carried == null)
        {
            return true;
        }

        mouseCursorCarriedPortable = null;
        if (usingItem == carried)
        {
            usingItem = null;
        }

        CancelPendingThrowCloseInventory();

        if (FindObjectInInventory(carried) != null)
        {
            return true;
        }

        Add(carried);
        return true;
    }

    public void BeginWorldPickupFromGround(UUObject obj)
    {
        if (!PlayerPanelState.ArePanelsAvailable)
        {
            return;
        }

        mouseCursorCarriedPortable = obj;
        usingItem = null;
        StatsPanel.sStatsPanel?.Hide();
        if (PlayerPanelState.ActivePanel != EPlayerPanel.Inventory)
        {
            PlayerPanelState.SetPanel(EPlayerPanel.Inventory, fromUserToggle: false);
        }

        currentArea = EInventoryArea.Stuff;
        List<UUObject> list = GetCurrentListInternal();
        index = list.Count;
        viewIndex = Mathf.Max(0, index - 12) & ~3;
        TutorialManager.NotifyPickup();
    }

    /// <summary>
    /// Gets the conceptual size of any game object.
    /// </summary>
    private static int GetObjectSize(UUObject obj)
    {
        if (obj == null) return 0;
        // Return defined size or a default of 2 (Small) if not in the map.
        return objectSizes.TryGetValue(obj.type, out int size) ? size : 2;
    }
    
    // A map of container types to the maximum item size they can hold.
    private static readonly Dictionary<EObjectType, int> containerSizeLimits = new()
    {
        { EObjectType.Pouch, 1 },      // Can only hold Tiny items
        { EObjectType.RuneBag, 1 },    // Can only hold Tiny items
        { EObjectType.MapCase, 2 },    // Scrolls and maps (Small)
        { EObjectType.Sack, 3 },       // Can hold items up to Medium size
        { EObjectType.Pack, 4 },       // Can hold items up to Large size
        { EObjectType.Box, 4 },        // Can hold items up to Large size
        { EObjectType.Chest, 4 },      // Can hold items up to Large size
        { EObjectType.GoldCoffer, 4 }  // Can hold items up to Large size
    };

    /// <summary>
    /// Gets the maximum item size a container can hold.
    /// </summary>
    private static int GetContainerSizeLimit(UUObject container)
    {
        if (container == null) return int.MaxValue; // Root inventory can hold any size
        if (UUObject.GetClass(container.type) != UUObject.EClass.Containers) return 0;
        // Return defined limit or a default of 2 (Small) if not in the map.
        return containerSizeLimits.TryGetValue(container.type, out int limit) ? limit : 2;
    }

    private static bool FitsInMapCase(UUObject obj)
    {
        return obj != null
            && obj.type >= EObjectType.ScrollA
            && obj.type <= EObjectType.ScrollG;
    }
    
    
    public void Clear()
    {
        inventory.Clear();
        stack.Clear();
        for (int i = 0; i < invSlotContents.Length; ++i)
        {
            if (invSlotContents[i] != null)
            {
                invSlotContents[i].Unequip();
                invSlotContents[i] = null;
            }
        }

        index = 0;
        viewIndex = 0;
        usingItem = null;
        mouseCursorCarriedPortable = null;
        HowMany.sHowMany.howManyItem = null;
    }

    public void ClearStack()
    {
        stack.Clear();
    }

    public List<UUObject> GetCurrentListInternal()
    {
        List<UUObject> itemsToDisplay = inventory;
        int stackCount = stack.Count;
        if (stackCount > 0)
        {
            // Check if the stack object is null or destroyed before accessing it
            UUObject topStackObj = stack[stackCount - 1];
            if (topStackObj == null || topStackObj.gameObject == null)
            {
                // Remove destroyed objects from the stack
                stack.RemoveAt(stackCount - 1);
                return itemsToDisplay; // Fall back to root inventory
            }
            
            UUObject stackObj = topStackObj.GetComponent<UUObject>();
            if (stackObj != null && stackObj.contents != null)
            {
                itemsToDisplay = stackObj.contents;
            }
        }

        return itemsToDisplay;
    }

    public UUObject GetCurrentItemInternal()
    {
        List<UUObject> objs = GetCurrentListInternal();
        if (objs != null && index >= 0 && index < objs.Count)
        {
            return objs[index];
        }

        return null;
    }

    public void BackOutOfContainer()
    {
        UUObject bag = stack[^1];
        stack.Remove(bag);
        index = GetCurrentListInternal().IndexOf(bag);
        viewIndex = Mathf.Max(0, index - 12) & ~3;
        Utils.PlayClip2d(backOut);
    }

    public UUObject GetCurrentContainerInternal()
    {
        int stackCount = stack.Count;
        if (stackCount > 0)
        {
            // Check if the stack object is null or destroyed before accessing it
            UUObject topStackObj = stack[stackCount - 1];
            if (topStackObj == null || topStackObj.gameObject == null)
            {
                // Remove destroyed objects from the stack
                stack.RemoveAt(stackCount - 1);
                return null; // Fall back to no container
            }
            
            return topStackObj.GetComponent<UUObject>();
        }
        return null;
    }

    public static UUObject GetCurrentItem()
    {
        return sInv.GetCurrentItemInternal();
    }
    
    public static void Add(UUObject obj)
    {
        ApplyItemGrantSideEffects(obj);
        sInv.AddRecursive(obj);
    }

    /// <summary>Fresh quest/script reward: mouse/kb cursor carry; gamepad direct stuff-list add.</summary>
    public static void GrantNewItemToMouseOrInventory(UUObject obj)
    {
        if (obj == null || sInv == null)
        {
            return;
        }

        ApplyItemGrantSideEffects(obj);
        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard)
        {
            sInv.BeginWorldPickupFromGround(obj);
        }
        else
        {
            sInv.AddRecursive(obj);
            TutorialManager.NotifyPickup();
        }
    }

    private static void ApplyItemGrantSideEffects(UUObject obj)
    {
        if (obj == null)
        {
            return;
        }

        switch (obj.type)
        {
        case EObjectType.BookOfHonesty:
            PlayerData.sData.talismansCollected |= 1 << (int)ETalisman.Book;
            break;
        case EObjectType.BottleOfWine:
            PlayerData.sData.talismansCollected |= 1 << (int)ETalisman.Bottle;
            break;
        case EObjectType.ShinyCup:
            PlayerData.sData.talismansCollected |= 1 << (int)ETalisman.Cup;
            break;
        case EObjectType.IronRing:
            PlayerData.sData.talismansCollected |= 1 << (int)ETalisman.Ring;
            break;
        case EObjectType.ShinyShield:
            PlayerData.sData.talismansCollected |= 1 << (int)ETalisman.Shield;
            break;
        case EObjectType.ShinySword:
            PlayerData.sData.talismansCollected |= 1 << (int)ETalisman.Sword;
            break;
        case EObjectType.Standard:
            PlayerData.sData.talismansCollected |= 1 << (int)ETalisman.Standard;
            break;
        case EObjectType.Taper:
            PlayerData.sData.talismansCollected |= 1 << (int)ETalisman.Taper;
            break;
        }
    }

    private void AddRecursive(UUObject obj)
    {
        UUObject bag = GetCurrentContainerInternal();

        // If we are trying to place an object into a container...
        if (bag != null)
        {
            // --- Special Rule Application ---
            // A pouch cannot contain another small container like itself.
            if (bag.type == EObjectType.Pouch)
            {
                if (obj.type is EObjectType.Pouch or EObjectType.RuneBag or EObjectType.MapCase)
                {
                    Messages.Add("A pouch cannot hold another container.");
                    BackOutOfContainer();
                    AddRecursive(obj); // Recurse to place in parent container
                    return;
                }
            }

            // --- General Size Check ---
            int objectSize = GetObjectSize(obj);
            int containerLimit = GetContainerSizeLimit(bag);

            // Check if the object is too large for this container's limit.
            if (objectSize > containerLimit)
            {
                // The item is too big. Back out and try the parent container.
                string itemName = obj.quantity > 1 ? obj.pluralName : obj.singularName;
                Messages.Add($"The {itemName} won't fit in the {bag.singularName}.");
                BackOutOfContainer();
                AddRecursive(obj); // Recursive call
                return;
            }
        }

        if (bag != null)
        {
            if (bag.type == EObjectType.RuneBag)
            {
                BackOutOfContainer();
            }
            if (bag.type == EObjectType.MapCase)
            {
                if (!FitsInMapCase(obj))
                {
                    Messages.Add("Only scrolls and maps can be placed in a map case.");
                    BackOutOfContainer();
                    AddRecursive(obj); // Recurse to place in parent container
                    return;
                }
            }
            if (bag.type == EObjectType.Quiver)
            {
                if (obj.type is not (EObjectType.Arrow or EObjectType.CrossbowBolt))
                {
                    string itemName = obj.quantity > 1 ? obj.pluralName : obj.singularName;
                    Messages.Add($"Only arrows and bolts can be placed in a quiver.");
                    BackOutOfContainer();
                    AddRecursive(obj); // Recurse to place in parent container
                    return;
                }
            }
        }
        
        if (obj.stackable)
        {
            // try to find another like object in the same list
            List<UUObject> curList = GetCurrentListInternal();
            for (int i = 0; i < curList.Count; ++i)
            {
                // TODO: check for enchanted etc
                if (curList[i].type == obj.type
                    && curList[i].stackable 
                    && curList[i].quantity + obj.quantity < 368
                    && SameStackMergeIdentity(curList[i], obj))
                {
                    curList[i].quantity += obj.quantity;
                    Utils.DestroyItem(obj);

                    // scroll to show it
                    index = i;
                    viewIndex = Mathf.Max(0, index - 12) & ~3;

                    holdTime = 2.0f;
                    return;
                }
            }
        }

        GetCurrentListInternal().Add(obj);

        // scroll to show it at the bottom of the list
        index = GetCurrentListInternal().Count - 1;
        viewIndex = Mathf.Max(0, index - 12) & ~3;
        currentArea = EInventoryArea.Stuff;

        holdTime = 2.0f;
    }
    
    public static bool Contains(EObjectType type)
    {
        return sInv.ContainsInternal(type);
    }

    private bool ContainsInternal(EObjectType type)
    {
        bool found = false;
        foreach (UUObject obj in inventory)
        {
            if (obj.ContainsRecursive(type))
            {
                found = true;
                break;
            }
        }

        return found;
    }

    public static List<UUObject> GetAllItems()
    {
        List<UUObject> allItems = new List<UUObject>();
        allItems.AddRange(sInv.inventory);
        foreach (UUObject obj in sInv.inventory)
        {
            obj.GetAllContents(allItems);
        }

        return allItems;
    }

    private AudioClip GetCombinationSound(EObjectType obj1Type, EObjectType obj2Type)
    {
        if (combinationSounds == null || combinationSounds.Count == 0)
        {
            return null;
        }

        // Check both orders (obj1+obj2 and obj2+obj1) since combinations can work either way
        foreach (CombinationSoundEntry entry in combinationSounds)
        {
            if (entry.soundClip == null)
            {
                continue;
            }

            if ((entry.object1 == obj1Type && entry.object2 == obj2Type) ||
                (entry.object1 == obj2Type && entry.object2 == obj1Type))
            {
                return entry.soundClip;
            }
        }

        return null;
    }

    private void DoEquip(UUObject obj, List<UUObject> currentList)
    {
        if (obj.quantity > 1)
        {
            int quantityRemaining = obj.quantity - 1;
            obj.quantity = 1;
            UUObject newItem = LevelLoader.CreateObjectOfType(obj.type);
            newItem.quantity = quantityRemaining;
            CopyPeeledStackProperties(obj, newItem);
            // Same as Split(): remainder must get name strings for GetLookName()
            newItem.PostLoadInitialize();
            currentList.Insert(index, newItem);
        }
        int swapInsertIndex = index;
        currentList.Remove(obj);
        
        EInvSlot invSlot = EInvSlot.None;
        switch (obj.getClass)
        {
        case UUObject.EClass.Armour:
        case UUObject.EClass.Armour2:
            // find where on the body to put it
            invSlot = slotMap[(int)obj.type & 31];
            if (invSlot == EInvSlot.LeftHand && PlayerData.sData.leftHanded)
            {
                invSlot = EInvSlot.RightHand;
            }

            if (invSlot == EInvSlot.RightFinger
                && invSlotContents[(int)EInvSlot.RightFinger] != null
                && invSlotContents[(int)EInvSlot.LeftFinger] == null)
            {
                invSlot = EInvSlot.LeftFinger;
            }
            break;
        case UUObject.EClass.Weapons:
            // try to put in weapon hand
            invSlot = PlayerData.sData.leftHanded ? EInvSlot.LeftHand : EInvSlot.RightHand;
            if (fist.isActiveAndEnabled)
            {
                fist.Unequip();
            }
            break;
        case UUObject.EClass.LampsAndWands:
            invSlot = EInvSlot.RightShoulder;
            if (invSlotContents[(int)EInvSlot.RightShoulder] != null
                && invSlotContents[(int)EInvSlot.LeftShoulder] == null)
            {
                invSlot = EInvSlot.LeftShoulder;
            }
            break;
        }

        if (invSlot != EInvSlot.None)
        {
            // swap for what's there already
            if (invSlotContents[(int)invSlot] != null)
            {
                UUObject oldObj = invSlotContents[(int)invSlot];
                oldObj.Unequip();
                currentList.Insert(swapInsertIndex, oldObj);
            }

            invSlotContents[(int)invSlot] = obj;
            
            // I tried modifying the pitch based on armor/weapon rating
            // but that was confusing as the sound length changed and longer sounds seemed stronger

            Utils.PlayClip2d(PlayerObject.Player.pickupClip);
        }
        else
        {
            Utils.PlayClip2d(PlayerObject.Player.pickupClip);
        }

        index = Mathf.Max(0, Mathf.Min(index, currentList.Count - 1));
    }

    private void TryEquipSelection(List<UUObject> currentList)
    {
        if (index >= currentList.Count)
        {
            return;
        }

        UUObject obj = currentList[index];
        if (obj == null)
        {
            return;
        }

        switch (obj.Equip())
        {
        case EEquipAction.Consume:
            // remove one from inventory
            if (obj.quantity > 1)
            {
                obj.quantity--;
            }
            else
            {
                currentList.Remove(obj);
                index = Mathf.Clamp(index, 0, currentList.Count - 1);
            }
            break;
        case EEquipAction.Equip:
            DoEquip(obj, currentList);
            break;
        }
        usingItem = null;
    }

    /// <summary>Y / RMB secondary hint for stuff grid (excludes floating Store over containers).</summary>
    private bool TryGetStuffSecondaryUseHint(UUObject obj, bool hoverPaperdoll, out string hint)
    {
        hint = null;
        if (obj == null)
        {
            return false;
        }

        if (hoverPaperdoll)
        {
            hint = "Unequip";
            return true;
        }

        if (obj.getClass == UUObject.EClass.Containers
            && !(Conversations.runningConversation != null && obj.type == EObjectType.RuneBag))
        {
            return false;
        }

        if (Conversations.runningConversation != null)
        {
            hint = "Trade";
            return true;
        }

        hint = obj.GetUseText();
        return !string.IsNullOrEmpty(hint);
    }

    /// <summary>Same as gamepad Y on the stuff list: trade / equip / eat / map Equip side-effects (<see cref="TryEquipSelection"/>).</summary>
    private void RunStuffYButtonActionAtIndex(List<UUObject> currentList, int cellIdx)
    {
        if (cellIdx < 0 || cellIdx >= currentList.Count || currentList[cellIdx] == null)
            return;

        int savedIndex = index;
        index = cellIdx;
        try
        {
            if (Conversations.runningConversation != null)
            {
                UUObject obj = GetCurrentItemInternal();
                if (obj != null)
                {
                    HowMany.sHowMany.howMany = 1;
                    if (obj.quantity > 1)
                    {
                        HowMany.AskHowMany(EHowManyReason.Trade, obj);
                    }
                    else
                    {
                        TryPutSelectionInTradeTray();
                    }
                }
            }
            else
            {
                UUObject obj = GetCurrentItemInternal();
                if (obj != null && TryGetStuffSecondaryUseHint(obj, hoverPaperdoll: false, out _))
                {
                    TryEquipSelection(currentList);
                }
            }
        }
        finally
        {
            index = savedIndex;
        }
    }

    private List<UUObject> FindListContainingItem(List<UUObject> list, UUObject obj)
    {
        if (list.Contains(obj)) return list;
        foreach (UUObject listObj in list)
        {
            if (listObj && listObj.contents != null && listObj.getClass == UUObject.EClass.Containers)
            {
                List<UUObject> potential = FindListContainingItem(listObj.contents, obj);
                if (potential != null)
                {
                    return potential;
                }
            }
        }

        return null;
    }

    public void RemoveItemFromInventory(UUObject obj)
    {
        List<UUObject> list = FindListContainingItem(inventory, obj);
        if (list != null)
        {
            list.Remove(obj);
        }
        if (obj == usingItem)
        {
            usingItem = null;
        }

        if (obj == mouseCursorCarriedPortable)
            mouseCursorCarriedPortable = null;
    }

    public void ReplaceItemInInventory(UUObject obj, UUObject rep)
    {
        List<UUObject> list = FindListContainingItem(inventory, obj);
        if (list != null)
        {
            int i = list.IndexOf(obj);
            list[i] = rep;
        }
    }

#if UNITY_EDITOR
    private static bool hasVerifiedSizes = false;
    /// <summary>
    /// A temporary, editor-only check that runs once to ensure all portable objects
    /// have a size defined in the objectSizes map.
    /// This uses the definitive 'isPortable' flag from the game data.
    /// </summary>
    private void VerifyAllObjectSizes()
    {
        if (hasVerifiedSizes) return;

        // List to hold the names of missing objects.
        List<string> missingObjects = new List<string>();

        // Get all possible values from the EObjectType enum.
        var allObjectTypes = System.Enum.GetValues(typeof(EObjectType));

        foreach (EObjectType itemType in allObjectTypes)
        {
            // Ensure the item type is within the bounds of the properties array.
            if ((int)itemType >= DataLoader.sDataLoader.comObjProps.Length) continue;

            // Check if the object is officially marked as portable in the game's data files.
            if (DataLoader.sDataLoader.comObjProps[(int)itemType].isPortable)
            {
                // If it's portable, it MUST have a size definition.
                if (!objectSizes.ContainsKey(itemType))
                {
                    // Add the missing enum name to our list.
                    missingObjects.Add(itemType.ToString());
                }
            }
        }

        if (missingObjects.Count > 0)
        {
            // Join all missing object names into a single string.
            string missingObjectsString = string.Join(", ", missingObjects);
            Debug.LogWarning($"Verification Finished: Found {missingObjects.Count} portable objects missing a size definition. Please add the following to the 'objectSizes' map:");
            // Log the single string for easy copy-pasting.
            Debug.LogWarning(missingObjectsString);
        }

        hasVerifiedSizes = true; // Mark as complete so it doesn't run again.
    }
#endif

    
    protected void Start()
    {
        sInv = this;
        if (GetComponent<SoftwareCursorOverlay>() == null)
            gameObject.AddComponent<SoftwareCursorOverlay>();

#if UNITY_EDITOR
        // Run the exhaustive check once at startup.
        VerifyAllObjectSizes();
#endif

        if (xButtonChargeRingMaterial != null)
        {
            chargeRingMatInstance = new Material(xButtonChargeRingMaterial);
        }

        if (fistToSpawn != null)
        {
            fist = Instantiate(fistToSpawn);
            fist.Equip();
        }
    }

    private void OnDestroy()
    {
        if (chargeRingMatInstance != null)
        {
            Destroy(chargeRingMatInstance);
            chargeRingMatInstance = null;
        }
    }

    private float aPressedTime;
    public UUObject usingItem;

    private enum EInventoryArea
    {
        Stuff,
        Paperdoll,
        Trading
    }

    private EInventoryArea currentArea;

    private float invMoveNextFireLeft = -1f;
    private float invMoveNextFireRight = -1f;
    private float invMoveNextFireUp = -1f;
    private float invMoveNextFireDown = -1f;

    private static bool InvMoveLeftHeld()
        => (Gamepad.current?.dpad.left.isPressed ?? false) || (Keyboard.current?.leftArrowKey.isPressed ?? false);

    private static bool InvMoveLeftPressedThisFrame()
        => (Gamepad.current?.dpad.left.wasPressedThisFrame ?? false) || (Keyboard.current?.leftArrowKey.wasPressedThisFrame ?? false);

    private static bool InvMoveRightHeld()
        => (Gamepad.current?.dpad.right.isPressed ?? false) || (Keyboard.current?.rightArrowKey.isPressed ?? false);

    private static bool InvMoveRightPressedThisFrame()
        => (Gamepad.current?.dpad.right.wasPressedThisFrame ?? false) || (Keyboard.current?.rightArrowKey.wasPressedThisFrame ?? false);

    private static bool InvMoveUpHeld()
        => (Gamepad.current?.dpad.up.isPressed ?? false) || (Keyboard.current?.upArrowKey.isPressed ?? false);

    private static bool InvMoveUpPressedThisFrame()
        => (Gamepad.current?.dpad.up.wasPressedThisFrame ?? false) || (Keyboard.current?.upArrowKey.wasPressedThisFrame ?? false);

    private static bool InvMoveDownHeld()
        => (Gamepad.current?.dpad.down.isPressed ?? false) || (Keyboard.current?.downArrowKey.isPressed ?? false);

    private static bool InvMoveDownPressedThisFrame()
        => (Gamepad.current?.dpad.down.wasPressedThisFrame ?? false) || (Keyboard.current?.downArrowKey.wasPressedThisFrame ?? false);

    private bool ShouldInventoryMoveRepeat(ref float nextFireUnscaledTime, bool held, bool pressedThisFrame)
    {
        if (!held)
        {
            nextFireUnscaledTime = -1f;
            return false;
        }

        if (pressedThisFrame)
        {
            nextFireUnscaledTime = Time.unscaledTime + inventoryMoveRepeatInitialDelay;
            return true;
        }

        if (nextFireUnscaledTime < 0f)
            nextFireUnscaledTime = Time.unscaledTime + inventoryMoveRepeatInitialDelay;

        if (Time.unscaledTime >= nextFireUnscaledTime)
        {
            nextFireUnscaledTime = Time.unscaledTime + inventoryMoveRepeatInterval;
            return true;
        }

        return false;
    }

    private void SyncInventoryMoveRepeatIdleDirections()
    {
        if (!InvMoveLeftHeld()) invMoveNextFireLeft = -1f;
        if (!InvMoveRightHeld()) invMoveNextFireRight = -1f;
        if (!InvMoveUpHeld()) invMoveNextFireUp = -1f;
        if (!InvMoveDownHeld()) invMoveNextFireDown = -1f;
    }

    private void ResetInventoryMoveRepeatTimers()
    {
        invMoveNextFireLeft = -1f;
        invMoveNextFireRight = -1f;
        invMoveNextFireUp = -1f;
        invMoveNextFireDown = -1f;
    }

    private void RunPaperdollShortPrimaryReleaseAtSlot(int slotIndex)
    {
        UUObject obj = invSlotContents[slotIndex];
        if (obj != null && usingItem != null && TryCombineHeldOilWithPaperdollLight(obj))
        {
            return;
        }

        if (obj != null)
        {
            if (obj is LightSource ls)
            {
                if (!CanCraftInInventory())
                {
                    return;
                }

                if (ls.IsLit())
                {
                    ls.SetLit(false);
                }
                else
                {
                    obj.TryInventoryUse();
                }
            }
            else
            {
                obj.TryInventoryUse();
            }
        }
    }

    /// <summary>Unequip into main stuff list (gamepad Y).</summary>
    private void RunPaperdollUnequipToStuffList(int slotIndex)
    {
        UUObject obj = invSlotContents[slotIndex];
        if (obj == null)
            return;

        obj.Unequip();
        Utils.PlayClip2d(unequip);

        List<UUObject> curList = GetCurrentListInternal();
        invSlotContents[slotIndex] = null;
        curList.Add(obj);
        currentArea = EInventoryArea.Stuff;
        index = curList.Count - 1;
        viewIndex = Mathf.Max(0, index - 12) & ~3;

        if (obj is WeaponBase)
            fist.Equip();
    }

    /// <summary>Strip from paperdoll into <see cref="mouseCursorCarriedPortable"/> (mouse drag-off).</summary>
    private void RunPaperdollDragPickupFromSlot(int slotIndex)
    {
        skipTradeHowManyOnNextFloatingDrop = false;
        UUObject obj = invSlotContents[slotIndex];
        if (obj == null)
            return;

        obj.Unequip();
        Utils.PlayClip2d(unequip);

        invSlotContents[slotIndex] = null;
        if (obj is WeaponBase)
            fist.Equip();

        usingItem = null;
        mouseCursorCarriedPortable = obj;
        currentArea = EInventoryArea.Stuff;
        List<UUObject> curList = GetCurrentListInternal();
        index = curList.Count;
        viewIndex = Mathf.Max(0, index - 12) & ~3;
        equipSlot = slotIndex;
    }

    private void UpdatePaperdoll()
    {
        if (ShouldInventoryMoveRepeat(ref invMoveNextFireLeft, InvMoveLeftHeld(), InvMoveLeftPressedThisFrame()))
        {
            if (invSlots[equipSlot].goLeft != -1)
            {
                Utils.PlayClip2d(moveCursor);
                equipSlot = invSlots[equipSlot].goLeft;
            }
            else if (Conversations.runningConversation != null)
            {
                Utils.PlayClip2d(moveCursor);
                currentArea = EInventoryArea.Trading;
                tradeSlot = 3;
            }
        }
        else if (ShouldInventoryMoveRepeat(ref invMoveNextFireRight, InvMoveRightHeld(), InvMoveRightPressedThisFrame()) && invSlots[equipSlot].goRight != -1)
        {
            Utils.PlayClip2d(moveCursor);
            equipSlot = invSlots[equipSlot].goRight;
        }
        else if (ShouldInventoryMoveRepeat(ref invMoveNextFireUp, InvMoveUpHeld(), InvMoveUpPressedThisFrame()) && invSlots[equipSlot].goUp != -1)
        {
            Utils.PlayClip2d(moveCursor);
            equipSlot = invSlots[equipSlot].goUp;
        }
        else if (ShouldInventoryMoveRepeat(ref invMoveNextFireDown, InvMoveDownHeld(), InvMoveDownPressedThisFrame()))
        {
            if (invSlots[equipSlot].goDown != -1)
            {
                Utils.PlayClip2d(moveCursor);
                equipSlot = invSlots[equipSlot].goDown;
            }
            else
            {
                Utils.PlayClip2d(moveCursor);
                currentArea = EInventoryArea.Stuff;
            }
        }
        else if (Gamepad.current?.yButton.wasPressedThisFrame ?? false)
        {
            RunPaperdollUnequipToStuffList(equipSlot);
        }
        else if ((Gamepad.current?.aButton.wasReleasedThisFrame ?? false) && aPressedTime < 0.2f)
        {
            RunPaperdollShortPrimaryReleaseAtSlot(equipSlot);
        }
    }

    /// <summary>Stack merge identity beyond type. Keys also require matching lock id (ownerIndex).</summary>
    private static bool SameStackMergeIdentity(UUObject a, UUObject b)
    {
        if (a.quality != b.quality)
        {
            return false;
        }
        if (a.getClass == UUObject.EClass.Keys && a.ownerIndex != b.ownerIndex)
        {
            return false;
        }
        return true;
    }

    /// <summary>Copy stack identity fields onto a peeled/split object (mirrors UUObject world partial pickup).</summary>
    public static void CopyPeeledStackProperties(UUObject source, UUObject peeled)
    {
        peeled.quality = source.quality;
        peeled.flags = source.flags;
        peeled.ownerIndex = source.ownerIndex;
    }

    public UUObject Split(UUObject obj)
    {
        // need to split out into stack and single object, so we can destroy the single object.
        // add the single object immediately after the stack in the current list, so that if it's
        // replaced, it's next to the stack
        UUObject splitObj = LevelLoader.CreateObjectOfType(obj.type);
        CopyPeeledStackProperties(obj, splitObj);
        // Initialize name and inventory-related properties for the peeled-off item
        splitObj.PostLoadInitialize();
        --obj.quantity;
        ++index;
        GetCurrentListInternal().Insert(index, splitObj);
        return splitObj;
    }

    /// <summary>Clears combine "held" item; refloats to mouse cursor when not in any list (mouse drag path).</summary>
    private void NormalizeUnusedCombineHeldItem(bool playFailSound)
    {
        if (usingItem == null)
        {
            return;
        }

        if (playFailSound)
        {
            Utils.PlayClip2d(fail);
        }

        if (FindObjectInInventory(usingItem) == null)
        {
            mouseCursorCarriedPortable = usingItem;
        }

        usingItem = null;
    }

    private void TryCombineObjects()
    {
        List<UUObject> currentList = GetCurrentListInternal();

        if (index < currentList.Count && currentList[index] != null && usingItem != null
            && TryIncenseBurnInteraction(usingItem, currentList[index]))
        {
            return;
        }

        // cmb.dat recipes — still require a single-unit HowMany selection
        if (HowMany.sHowMany.howMany == 1)
        {
            UUObject obj1 = currentList[index];
            UUObject obj2 = usingItem;
            ObjectCombination comb = CombineObjects.FindCombination(obj1, obj2);
            if (comb != null)
            {
                if (!CanCraftInInventory())
                {
                    NormalizeUnusedCombineHeldItem(true);
                    return;
                }

                if (obj1.quantity > 1 && (comb.Object1Destroyed() || obj1.type == EObjectType.BlockOfIncense))
                {
                    obj1 = Split(obj1);
                }
                if (obj2.quantity > 1 && (comb.Object2Destroyed() || obj2.type == EObjectType.BlockOfIncense))
                {
                    UUObject obj = LevelLoader.CreateObjectOfType(obj2.type);
                    CopyPeeledStackProperties(obj2, obj);
                    // Mirror Split(obj1): peeled single needs name strings (GetLookName, etc.)
                    obj.PostLoadInitialize();
                    --obj2.quantity;
                    List<UUObject> obj2List = FindObjectInInventory(obj2);
                    obj2List.Add(obj);
                    obj2 = obj;
                }
                if (obj1.type == EObjectType.BlockOfIncense || obj2.type == EObjectType.BlockOfIncense)
                {
                    if (obj1.type == EObjectType.BlockOfIncense)
                    {
                        (obj1 as Incense).TryBurn();
                        if (Utils.CanSleepHere())
                        {
                            usingItem = null;
                        }
                        else
                        {
                            NormalizeUnusedCombineHeldItem(true);
                        }

                        return;
                    }

                    (obj2 as Incense).TryBurn();
                    if (Utils.CanSleepHere())
                    {
                        usingItem = null;
                    }
                    else
                    {
                        NormalizeUnusedCombineHeldItem(true);
                    }

                    return;
                }
                UUObject result = LevelLoader.CreateObjectOfType((EObjectType)comb.GetResult());
                // Initialize name properties so combined result has proper name
                result.PostLoadInitialize();
                
                // Play combination sound if available
                AudioClip combinationSound = GetCombinationSound(obj1.type, obj2.type);
                if (combinationSound != null)
                {
                    Utils.PlayClip2d(combinationSound);
                }
                else
                {
                    // Fall back to default combine sound
                    Utils.PlayClip2d(combine);
                }
                
                if (comb.Object1Destroyed() && comb.Object2Destroyed())
                {
                    // destroy both
                    // replace list item with result
                    // remove using item from inventory
                    currentList[index] = result;
                    Utils.DestroyItem(obj1);
                    Utils.DestroyItem(obj2);
                    usingItem = null;
                }
                else if (comb.Object1Destroyed()) // list item destroyed
                {
                    currentList[index] = result;
                    Utils.DestroyItem(obj1);
                }
                else
                {
                    ReplaceItemInInventory(obj2, result);
                    Utils.DestroyItem(obj2);
                    usingItem = result;
                }

                return;
            }
        }

        // Custom TryCombine (oil→lantern, etc.) — independent of HowMany.howMany (often 0 until a dialog runs).
        if (!CanCraftInInventory())
        {
            if (HowMany.sHowMany.howMany == 1)
            {
                NormalizeUnusedCombineHeldItem(true);
                return;
            }
        }
        else if (usingItem.TryCombine(currentList[index]))
        {
            // Partial consume left a cursor-held stack (e.g. multi oil flask); keep it on the mouse.
            if (usingItem != null && FindObjectInInventory(usingItem) == null)
            {
                mouseCursorCarriedPortable = usingItem;
            }
            usingItem = null;
            return;
        }

        // stack/group — merge count must be usingItem.quantity; HowMany.sHowMany.howMany is unrelated (often still 0 before any dialog).
        if (currentList[index].type == usingItem.type && usingItem.stackable && usingItem != currentList[index])
        {
            int howMany = usingItem.quantity;
            if (SameStackMergeIdentity(currentList[index], usingItem) && currentList[index].quantity + howMany < 368)
            {
                Utils.PlayClip2d(combine);
                currentList[index].quantity += howMany;
                if (howMany == usingItem.quantity)
                {
                    Utils.DestroyItem(usingItem);
                }
                else
                {
                    usingItem.quantity -= howMany;
                }
            }
            else
            {
                Utils.PlayClip2d(fail);
                if (FindObjectInInventory(usingItem) == null)
                {
                    mouseCursorCarriedPortable = usingItem;
                }
            }

            usingItem = null;

            return;
        }

        if (currentList[index].type == EObjectType.Torch)
        {
            Messages.Add(1, 132); // no effect
            NormalizeUnusedCombineHeldItem(false);
            return;
        }

        NormalizeUnusedCombineHeldItem(true);
    }

    private static bool IsRuneBagDirectStowTarget(UUObject container, UUObject carried) =>
        container != null && container.type == EObjectType.RuneBag && carried is RuneStone;

    private bool CanMouseStoreCarriedInContainer(UUObject container, UUObject carried) =>
        container != null
        && carried != null
        && container.getClass == UUObject.EClass.Containers
        && !IsRuneBagDirectStowTarget(container, carried)
        && ValidatePutUsingItemInContainer(container, carried, playFeedback: false);

    /// <summary>When viewing container contents, block placing a new item that fails container rules.</summary>
    private bool TryValidateFloatingPlaceIntoOpenContainer(UUObject item, List<UUObject> targetList)
    {
        UUObject openContainer = GetCurrentContainerInternal();
        if (openContainer == null || item == null)
        {
            return true;
        }

        // Reordering within the open container is allowed.
        if (targetList.Contains(item))
        {
            return true;
        }

        return ValidatePutUsingItemInContainer(openContainer, item, playFeedback: true);
    }

    /// <summary>Same validation as <see cref="TryPutUsingItemInContainer"/> — returns false and plays feedback if the item cannot be stored.</summary>
    private bool ValidatePutUsingItemInContainer(UUObject targetContainer, UUObject item, bool playFeedback = true)
    {
        if (item == null)
        {
            return false;
        }

        if (targetContainer.type == EObjectType.Pouch && item.type is EObjectType.Pouch or EObjectType.RuneBag or EObjectType.MapCase)
        {
            if (playFeedback)
            {
                Messages.Add(1, 248); // that item does not fit
                Utils.PlayClip2d(fail);
            }
            return false;
        }

        if (targetContainer.type == EObjectType.MapCase && !FitsInMapCase(item))
        {
            if (playFeedback)
            {
                Messages.Add("Only scrolls and maps can be placed in a map case.");
                Utils.PlayClip2d(fail);
            }
            return false;
        }

        if (targetContainer.type == EObjectType.RuneBag && !(item is RuneStone))
        {
            if (playFeedback)
            {
                Messages.Add(1, 247); // you can only put runes in the rune bag
                Utils.PlayClip2d(fail);
            }
            return false;
        }

        if (targetContainer.type == EObjectType.Quiver && item.type is not (EObjectType.Arrow or EObjectType.CrossbowBolt))
        {
            if (playFeedback)
            {
                Messages.Add($"Only arrows and bolts can be placed in a quiver.");
                Utils.PlayClip2d(fail);
            }
            return false;
        }

        if (targetContainer.type == EObjectType.RuneBag && item is RuneStone && targetContainer.contents != null)
        {
            foreach (var existing in targetContainer.contents)
            {
                if (existing.type == item.type)
                {
                    if (playFeedback)
                    {
                        Messages.Add($"The rune bag already contains {item.GetLookName()}.");
                        Utils.PlayClip2d(fail);
                    }
                    return false;
                }
            }
        }

        if (GetObjectSize(item) > GetContainerSizeLimit(targetContainer))
        {
            if (playFeedback)
            {
                Messages.Add(1, 248); // that item does not fit
                Utils.PlayClip2d(fail);
            }
            return false;
        }

        return true;
    }

    private void TryPutUsingItemInContainer(bool skipValidation = false)
    {
        List<UUObject> currentList = GetCurrentListInternal();
        UUObject targetContainer = currentList[index];

        if (!skipValidation && !ValidatePutUsingItemInContainer(targetContainer, usingItem))
            return;

        // --- IF ALL CHECKS PASS, MOVE THE ITEM ---
        
        // Find the using item, place in new container then remove from old container
        List<UUObject> sourceContainer = FindObjectInInventoryRecursive(inventory, usingItem);
        // Cursor-held / peeled items use their quantity; gamepad MoveStuff partial deposit uses HowMany while stack stays in list.
        int howMany = usingItem.quantity;
        if (sourceContainer != null && usingItem.stackable)
        {
            howMany = Mathf.Clamp(HowMany.sHowMany.howMany, 1, usingItem.quantity);
        }

        if (usingItem.stackable && howMany != usingItem.quantity)
        {
            UUObject newItem = LevelLoader.CreateObjectOfType(usingItem.type);
            newItem.quantity = howMany;
            CopyPeeledStackProperties(usingItem, newItem);
            // Initialize name properties so item has proper name
            newItem.PostLoadInitialize();
            targetContainer.contents.Add(newItem);
            usingItem.quantity -= howMany;
            if (targetContainer.type == EObjectType.RuneBag && newItem is RuneStone)
            {
                Magic.AddRunestone(newItem.type);
            }
        }
        else
        {
            targetContainer.contents.Add(usingItem);
            if (sourceContainer != null)
            {
                sourceContainer.Remove(usingItem);
            }
            if (targetContainer.type == EObjectType.RuneBag && usingItem is RuneStone)
            {
                Magic.AddRunestone(usingItem.type);
            }
        }

        if (targetContainer.type == EObjectType.RuneBag
            && targetContainer.contents != null
            && targetContainer.contents.Count > 0
            && targetContainer.contents[targetContainer.contents.Count - 1] is RuneStone stowedStone)
        {
            RuneStone.PlayStowSound(stowedStone);
        }
        else
        {
            Utils.PlayClip2d(combine);
        }

        HowMany.sHowMany.howMany = 1;
        usingItem = null;
    }

    public static int[] npcTradeIndices = { 0, 1, 4, 5 };
    public static int[] playerTradeIndices = { 2, 3, 6, 7 };

    public void TryPutSelectionInTradeTray()
    {
        List<UUObject> currentList = GetCurrentListInternal();
        if (index >= 0 && index < currentList.Count)
        {
            UUObject obj = currentList[index];
            if (obj != null)
            {
                // first try to stack with item in trade tray
                bool stacked = false;
                foreach (int i in playerTradeIndices)
                {
                    if (tradeSlots[i] != null
                        && tradeSlots[i].type == obj.type && obj.stackable && SameStackMergeIdentity(tradeSlots[i], obj)
                        && tradeSlots[i].quantity + obj.quantity <= 99)
                    {
                        int howMany = HowMany.sHowMany.howMany;
                        if (howMany < obj.quantity)
                        {
                            tradeSlots[i].quantity += howMany;
                            obj.quantity -= howMany;
                        }
                        else
                        {
                            tradeSlots[i].quantity += obj.quantity;
                            Utils.DestroyItem(obj);
                            obj = null;
                        }
                        tradeSlotSelected[i] = true;
                        usingItem = null;
                        stacked = true;

                        Utils.PlayClip2d(equip);
                        
                        break;
                    }
                }
                if (obj != null && !stacked)
                {
                    // find a free spot in the trade tray
                    foreach (int i in playerTradeIndices)
                    {
                        if (tradeSlots[i] == null)
                        {
                            int howMany = HowMany.sHowMany.howMany;
                            if (obj.stackable && howMany < obj.quantity)
                            {
                                UUObject newObj = LevelLoader.CreateObjectOfType(obj.type);
                                newObj.quantity = howMany;
                                CopyPeeledStackProperties(obj, newObj);
                                // Initialize name properties so item has proper name
                                newObj.PostLoadInitialize();
                                obj.quantity -= howMany;

                                tradeSlots[i] = newObj;
                            }
                            else
                            {
                                tradeSlots[i] = obj;
                                currentList.RemoveAt(index);
                            }
                            tradeSlotSelected[i] = true;
                            usingItem = null;
                        
                            Utils.PlayClip2d(equip);
                        
                            break;
                        }
                    }
                }
                // if there isn't one, report, or swap perhaps
            }
        }
    }

    /// <summary>Restore cursor portable after cancelling HowMany for a floating trade onto a specific slot.</summary>
    public void RestoreFloatingTradeHowManyCancel(UUObject item)
    {
        pendingFloatingTradeSlot = -1;
        if (item != null)
        {
            mouseCursorCarriedPortable = item;
            usingItem = null;
        }
    }

    /// <summary>Floating item (<see cref="usingItem"/> not in list) into one player trade slot; <paramref name="howManyCount"/> from HowMany or full stack.</summary>
    public bool TryPutFloatingIntoTradeTrayAtSlot(int slot, UUObject obj, int howManyCount)
    {
        if (!IsPlayerTradeSlotIndex(slot) || obj == null || howManyCount < 1)
            return false;

        List<UUObject> currentList = GetCurrentListInternal();

        bool objFromList = index >= 0 && index < currentList.Count && currentList[index] == obj;

        if (tradeSlots[slot] != null)
        {
            if (tradeSlots[slot].type == obj.type && obj.stackable && SameStackMergeIdentity(tradeSlots[slot], obj)
                && tradeSlots[slot].quantity + howManyCount <= 99)
            {
                // Stackable quantity uses special; getter returns max(special,1) so "empty" reads as 1 — cannot use obj.quantity <= 0 after subtract.
                int qtyBeforeConsume = obj.quantity;
                tradeSlots[slot].quantity += howManyCount;
                obj.quantity -= howManyCount;
                if (qtyBeforeConsume <= howManyCount)
                {
                    if (objFromList)
                        currentList.RemoveAt(index);
                    else
                        Utils.DestroyItem(obj);
                    obj = null;
                }

                tradeSlotSelected[slot] = true;
                usingItem = null;
                if (obj != null && !objFromList)
                    mouseCursorCarriedPortable = obj;
                else
                    mouseCursorCarriedPortable = null;

                if (mouseCursorCarriedPortable == null)
                    skipTradeHowManyOnNextFloatingDrop = false;

                Utils.PlayClip2d(equip);
                return true;
            }

            Utils.PlayClip2d(fail);
            return false;
        }

        bool splitRemainderToCursor = false;
        if (obj.stackable && howManyCount < obj.quantity)
        {
            UUObject newObj = LevelLoader.CreateObjectOfType(obj.type);
            newObj.quantity = howManyCount;
            CopyPeeledStackProperties(obj, newObj);
            newObj.PostLoadInitialize();
            obj.quantity -= howManyCount;

            tradeSlots[slot] = newObj;
            splitRemainderToCursor = true;
        }
        else
        {
            tradeSlots[slot] = obj;
            if (objFromList)
                currentList.RemoveAt(index);
        }

        tradeSlotSelected[slot] = true;
        usingItem = null;
        if (splitRemainderToCursor && obj != null && obj.quantity > 0 && !objFromList)
            mouseCursorCarriedPortable = obj;
        else
            mouseCursorCarriedPortable = null;

        if (mouseCursorCarriedPortable == null)
            skipTradeHowManyOnNextFloatingDrop = false;

        Utils.PlayClip2d(equip);
        return true;
    }

    private void TryTakeSelectionFromTradeTray()
    {
        if ((tradeSlot & 3) >= 2 && tradeSlots[tradeSlot] != null)
        {
            currentArea = EInventoryArea.Stuff;
            Add(tradeSlots[tradeSlot]);
            tradeSlots[tradeSlot] = null;
            tradeSlotSelected[tradeSlot] = false;
            
            Utils.PlayClip2d(unequip);
        }
    }

    public void TryTakeAllFromTradeTray()
    {
        foreach (int i in playerTradeIndices)
        {
            tradeSlot = i;
            TryTakeSelectionFromTradeTray();
        }
    }

    /// <summary>Gamepad A short release / mouse LMB release — open container, combine, drop, or same as A on item (<see cref="UUObject.TryInventoryUse"/>).</summary>
    private void RunStuffShortPrimaryRelease()
    {
        if (mouseCursorCarriedPortable != null)
            return;

        List<UUObject> currentList = GetCurrentListInternal();
        if (index < currentList.Count && currentList[index] != null
            && currentList[index].getClass == UUObject.EClass.Containers)
        {
            // try putting item on itself
            if (usingItem == currentList[index])
            {
                usingItem = null;
                Utils.PlayClip2d(fail);
            }

            if (currentList[index].contents == null)
            {
                currentList[index].contents = new();
            }

            if (usingItem != null)
            {
                TryPutUsingItemInContainer();
            }
            else
            {
                // open the container
                stack.Add(currentList[index]);
                index = 0;
                viewIndex = 0;

                Utils.PlayClip2d(open);
            }
        }
        else if (index < currentList.Count && currentList[index] != null && usingItem != null)
        {
            TryCombineObjects();
        }
        else if (usingItem != null && index == currentList.Count)
        {
            if (!TryValidateFloatingPlaceIntoOpenContainer(usingItem, currentList))
            {
                return;
            }

            FinishDropIntoOpenContainer(usingItem, currentList);
        }
        else if (index < currentList.Count && currentList[index] != null)
        {
            UUObject obj = currentList[index];
            if (obj is LightSource ls)
            {
                if (!CanCraftInInventory())
                {
                    return;
                }

                if (ls.IsLit())
                {
                    ls.SetLit(false);
                }
                else
                {
                    obj.TryInventoryUse();
                }
            }
            else
            {
                obj.TryInventoryUse();
            }
        }
    }

    private void RunStuffDragPickupFromSelection()
    {
        UUObject obj = GetCurrentItemInternal();
        if (obj == null)
            return;
        HowMany.sHowMany.howMany = 1;
        if (obj.quantity > 1)
        {
            HowMany.AskHowMany(EHowManyReason.MoveStuff, obj);
        }

        usingItem = obj;
    }

    /// <summary>Called from HowMany after <see cref="EHowManyReason.MoveStuffMouse"/>.</summary>
    public void CompleteMoveStuffMouseAfterHowMany(UUObject stackItem, int takeQty)
    {
        if (pendingTradeTrayDragSlot >= 0
            && pendingTradeTrayDragSlot < tradeSlots.Length
            && tradeSlots[pendingTradeTrayDragSlot] != null
            && tradeSlots[pendingTradeTrayDragSlot] == stackItem)
        {
            int slot = pendingTradeTrayDragSlot;
            pendingTradeTrayDragSlot = -1;
            UUObject inTray = tradeSlots[slot];
            if (takeQty >= inTray.quantity)
            {
                tradeSlots[slot] = null;
                tradeSlotSelected[slot] = false;
                mouseCursorCarriedPortable = inTray;
            }
            else
            {
                UUObject peeled = LevelLoader.CreateObjectOfType(inTray.type);
                peeled.quantity = takeQty;
                CopyPeeledStackProperties(inTray, peeled);
                peeled.PostLoadInitialize();
                inTray.quantity -= takeQty;
                mouseCursorCarriedPortable = peeled;
            }

            skipTradeHowManyOnNextFloatingDrop = true;
            usingItem = null;
            Utils.PlayClip2d(unequip);
            return;
        }

        List<UUObject> list = GetCurrentListInternal();
        int idx = list.IndexOf(stackItem);
        if (idx < 0)
            return;

        if (takeQty >= stackItem.quantity)
        {
            list.RemoveAt(idx);
            mouseCursorCarriedPortable = stackItem;
        }
        else
        {
            UUObject peeled = LevelLoader.CreateObjectOfType(stackItem.type);
            peeled.quantity = takeQty;
            CopyPeeledStackProperties(stackItem, peeled);
            peeled.PostLoadInitialize();
            stackItem.quantity -= takeQty;
            mouseCursorCarriedPortable = peeled;
        }

        if (index >= list.Count)
            index = Mathf.Max(0, list.Count - 1);

        skipTradeHowManyOnNextFloatingDrop = true;
    }

    /// <summary>Mouse drag from stuff list — detach from list and follow cursor (not gamepad <see cref="usingItem"/>).</summary>
    private void RunStuffMouseDragPickupFromSelection()
    {
        skipTradeHowManyOnNextFloatingDrop = false;
        UUObject obj = GetCurrentItemInternal();
        if (obj == null)
            return;

        if (obj.quantity > 1)
        {
            HowMany.sHowMany.howMany = 1;
            HowMany.AskHowMany(EHowManyReason.MoveStuffMouse, obj);
            return;
        }

        List<UUObject> list = GetCurrentListInternal();
        int idx = index;
        if (idx < 0 || idx >= list.Count || list[idx] != obj)
            return;

        list.RemoveAt(idx);
        mouseCursorCarriedPortable = obj;
        HowMany.sHowMany.howMany = 1;
        if (index >= list.Count)
            index = Mathf.Max(0, list.Count - 1);
    }

    private void ClearGamepadStuffYUseChargeState()
    {
        gamepadYUseChargeIndex = -1;
        gamepadYUseChargeNormalized = 0f;
    }

    private void UpdateGamepadStuffYUseChargeState()
    {
        if (currentArea != EInventoryArea.Stuff)
        {
            ClearGamepadStuffYUseChargeState();
            gamepadYUseAwaitingRelease = false;
            return;
        }

        Gamepad gp = Gamepad.current;
        if (gp == null)
        {
            ClearGamepadStuffYUseChargeState();
            gamepadYUseAwaitingRelease = false;
            return;
        }

        List<UUObject> currentList = GetCurrentListInternal();

        if (gp.yButton.wasReleasedThisFrame)
        {
            gamepadYUseAwaitingRelease = false;
            ClearGamepadStuffYUseChargeState();
            return;
        }

        if (gp.yButton.wasPressedThisFrame)
        {
            if (!gamepadYUseAwaitingRelease
                && index >= 0 && index < currentList.Count && currentList[index] != null
                && TryGetStuffSecondaryUseHint(currentList[index], hoverPaperdoll: false, out _))
            {
                if (Conversations.runningConversation != null)
                {
                    RunStuffYButtonActionAtIndex(currentList, index);
                    ClearGamepadStuffYUseChargeState();
                    return;
                }

                gamepadYUseChargeIndex = index;
                gamepadYUseChargeNormalized = 0f;
            }
            else
            {
                ClearGamepadStuffYUseChargeState();
            }

            return;
        }

        if (gp.yButton.isPressed && gamepadYUseChargeIndex >= 0)
        {
            if (index != gamepadYUseChargeIndex
                || index < 0
                || index >= currentList.Count
                || currentList[index] == null)
            {
                ClearGamepadStuffYUseChargeState();
            }
            else
            {
                gamepadYUseChargeNormalized += Time.deltaTime / GamepadYUseChargeDurationSeconds;
                if (gamepadYUseChargeNormalized >= 1f)
                {
                    gamepadYUseChargeNormalized = 1f;
                    RunStuffYButtonActionAtIndex(currentList, index);
                    ClearGamepadStuffYUseChargeState();
                    gamepadYUseAwaitingRelease = true;
                }
            }

            return;
        }

        ClearGamepadStuffYUseChargeState();
    }

    private bool IsGamepadStuffYChargeBlockingNavigation()
    {
        Gamepad gp = Gamepad.current;
        if (gp == null || !gp.yButton.isPressed)
        {
            return false;
        }

        if (gamepadYUseChargeIndex < 0)
        {
            return false;
        }

        List<UUObject> currentList = GetCurrentListInternal();
        return index == gamepadYUseChargeIndex
            && index >= 0
            && index < currentList.Count
            && currentList[index] != null;
    }

    private void UpdateStuff()
    {
        if (index == -1)
        {
            index = 0;
            viewIndex = 0;
        }

        List<UUObject> currentList = GetCurrentListInternal();
        if (IsGamepadStuffYChargeBlockingNavigation())
        {
            return;
        }

        if ((Gamepad.current?.aButton.wasReleasedThisFrame ?? false) && aPressedTime < 0.2f)
        {
            RunStuffShortPrimaryRelease();
        }
        else if ((Gamepad.current?.bButton.wasPressedThisFrame ?? false) && stack.Count > 0)
        {
            BackOutOfContainer();
        }
        else if (aPressedTime >= 0.2f)
        {
            RunStuffDragPickupFromSelection();
        }
        else
        {
            // navigate

            if (ShouldInventoryMoveRepeat(ref invMoveNextFireLeft, InvMoveLeftHeld(), InvMoveLeftPressedThisFrame()))
            {
                if (Conversations.runningConversation != null && (index & 3) == 0)
                {
                    currentArea = EInventoryArea.Trading;
                    tradeSlot = 7;
                    
                    Utils.PlayClip2d(moveCursor);
                }
                else if (index > 0)
                {
                    --index;
                    if (index < viewIndex)
                    {
                        viewIndex = index & ~3;
                    }
                    
                    Utils.PlayClip2d(moveCursor);
                }
            }
            else if (ShouldInventoryMoveRepeat(ref invMoveNextFireRight, InvMoveRightHeld(), InvMoveRightPressedThisFrame()) && index < currentList.Count)
            {
                ++index;
                if (index >= viewIndex + 16)
                {
                    viewIndex = (index - 12) & ~3;
                }
                    
                Utils.PlayClip2d(moveCursor);
            }
            else if (ShouldInventoryMoveRepeat(ref invMoveNextFireUp, InvMoveUpHeld(), InvMoveUpPressedThisFrame()))
            {
                if (index >= 4)
                {
                    index -= 4;
                    if (index < viewIndex)
                    {
                        viewIndex = index & ~3;
                    }
                    
                    Utils.PlayClip2d(moveCursor);
                }
                else
                {
                    currentArea = EInventoryArea.Paperdoll;
                    usingItem = null;
                    equipSlot = (int)EInvSlot.Feet;
                    
                    Utils.PlayClip2d(moveCursor);
                }
            }
            else if (ShouldInventoryMoveRepeat(ref invMoveNextFireDown, InvMoveDownHeld(), InvMoveDownPressedThisFrame()))
            {
                if (index < currentList.Count)
                {
                    index += 4;
                    index = Mathf.Min(index, currentList.Count);

                    if (index >= viewIndex + 16)
                    {
                        viewIndex = (index - 12) & ~3;
                    }
                    
                    Utils.PlayClip2d(moveCursor);
                }
            }
            else if ((Gamepad.current?.aButton.wasReleasedThisFrame ?? false) && aPressedTime < 0.2f)
            {
                UUObject x = GetCurrentItemInternal();
                if (x != null)
                {
                    x.TryInventoryUse();
                }
            }
        }
    }

    /// <summary>Mouse RMB while carrying — deposit <see cref="mouseCursorCarriedPortable"/> into hovered container (no open-first).</summary>
    private bool TryMouseStoreCarriedPortableInContainer(Vector2 guiMouse, float invPosition)
    {
        if (mouseCursorCarriedPortable == null || IsInventoryModalUiBlocking(PlayerObject.Player) || lerpIn < 0.001f)
            return false;
        if (!IsGuiMouseOverInventoryPanel(guiMouse, invPosition))
            return false;

        float panelLeft = Screen.width - invPosition;
        if (TryGetPaperdollSlotUnderMouse(guiMouse, panelLeft, out int overlapPdSlot, out _)
            && overlapPdSlot >= 0 && overlapPdSlot < invSlotContents.Length
            && invSlotContents[overlapPdSlot] != null)
            return false;

        if (!TryGetStuffGridCellListIndex(guiMouse, panelLeft, out int cellIdx))
            return false;

        List<UUObject> list = GetCurrentListInternal();
        if (cellIdx < 0 || cellIdx >= list.Count || list[cellIdx] == null)
            return false;

        UUObject container = list[cellIdx];
        if (container.getClass != UUObject.EClass.Containers)
            return false;
        if (container == mouseCursorCarriedPortable)
            return false;
        if (IsRuneBagDirectStowTarget(container, mouseCursorCarriedPortable))
            return false;

        UUObject carried = mouseCursorCarriedPortable;
        usingItem = carried;
        mouseCursorCarriedPortable = null;
        index = cellIdx;
        currentArea = EInventoryArea.Stuff;

        if (!ValidatePutUsingItemInContainer(container, usingItem))
        {
            mouseCursorCarriedPortable = usingItem;
            usingItem = null;
            return false;
        }

        TryPutUsingItemInContainer(skipValidation: true);
        if (usingItem != null)
        {
            mouseCursorCarriedPortable = usingItem;
            usingItem = null;
            return false;
        }

        return true;
    }

    /// <summary>Mouse RMB — same as gamepad Y (unequip from paperdoll, or trade/eat/<see cref="UUObject.Equip"/> on stuff cell).</summary>
    private void TryInventorySecondaryUseAtMouse(Vector2 guiMouse, float invPosition)
    {
        if (mouseCursorCarriedPortable != null)
            return;
        if (Magic.sMagic != null && Magic.sMagic.HasMousePrimedSpellAwaitingAim)
            return;
        if (!IsGuiMouseOverInventoryPanel(guiMouse, invPosition))
            return;

        float panelLeft = Screen.width - invPosition;
        if (TryGetPaperdollSlotUnderMouse(guiMouse, panelLeft, out int pdSlot, out _)
            && pdSlot >= 0 && pdSlot < invSlotContents.Length
            && invSlotContents[pdSlot] != null)
        {
            RunPaperdollUnequipToStuffList(pdSlot);
            return;
        }

        if (TryGetStuffGridCellListIndex(guiMouse, panelLeft, out int cellIdx))
        {
            List<UUObject> list = GetCurrentListInternal();
            if (cellIdx >= 0 && cellIdx < list.Count && list[cellIdx] != null)
            {
                UUObject cellObj = list[cellIdx];
                if (!cellObj.SupportsStuffGridMouseSecondaryUse)
                {
                    return;
                }

                RunStuffYButtonActionAtIndex(list, cellIdx);
            }
        }
    }

    /// <summary>
    /// Mouse on stuff grid: LMB = gamepad A tap; RMB = gamepad Y (<see cref="RunStuffYButtonActionAtIndex"/>); drag past threshold = pick up portable.
    /// </summary>
    private void UpdateStuffMouseInteraction(float invPosition, bool swallowPrimaryPressForFloatingPlace = false)
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        // Update runs before OnGUI: don't arm click-to-look while placing a cursor-carried portable (same click would fire RunStuffShortPrimaryRelease on mouse up).
        if (mouseCursorCarriedPortable != null)
        {
            if (mouse.leftButton.wasPressedThisFrame || mouse.leftButton.wasReleasedThisFrame)
            {
                mouseStuffPendingListIdx = -1;
                mouseStuffDragThresholdCrossed = false;
            }

            Vector2 guiMouseCarried = GuiInput.ScreenToGuiMouse(mouse.position.ReadValue());
            if (mouse.rightButton.wasReleasedThisFrame)
                TryMouseStoreCarriedPortableInContainer(guiMouseCarried, invPosition);

            return;
        }

        Vector2 guiMouse = GuiInput.ScreenToGuiMouse(mouse.position.ReadValue());
        float panelLeft = Screen.width - invPosition;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            mouseStuffPendingListIdx = -1;
            mouseStuffDragThresholdCrossed = false;
            if (swallowPrimaryPressForFloatingPlace)
                return;

            if (!IsGuiMouseOverInventoryPanel(guiMouse, invPosition))
                return;

            // Paperdoll slots overlap the stuff grid; floating placement checks paperdoll first — don't arm a stuff cell under an occupied slot.
            if (TryGetPaperdollSlotUnderMouse(guiMouse, panelLeft, out int overlapPdSlot, out _)
                && overlapPdSlot >= 0 && overlapPdSlot < invSlotContents.Length
                && invSlotContents[overlapPdSlot] != null)
                return;

            if (!TryGetStuffGridCellListIndex(guiMouse, panelLeft, out int cellIdx))
                return;
            List<UUObject> list = GetCurrentListInternal();
            if (cellIdx >= list.Count || list[cellIdx] == null)
                return;
            mouseStuffPendingListIdx = cellIdx;
            mouseStuffDownScreen = mouse.position.ReadValue();
            return;
        }

        if (mouseStuffPendingListIdx >= 0 && mouse.leftButton.isPressed)
        {
            if (!mouseStuffDragThresholdCrossed
                && (mouse.position.ReadValue() - mouseStuffDownScreen).sqrMagnitude
                >= MouseStuffDragThresholdPx * MouseStuffDragThresholdPx)
            {
                index = mouseStuffPendingListIdx;
                mouseStuffDragThresholdCrossed = true;
                RunStuffMouseDragPickupFromSelection();
            }
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            if (suppressStuffMousePrimaryOnNextLeftRelease)
            {
                suppressStuffMousePrimaryOnNextLeftRelease = false;
            }
            else if (mouseStuffPendingListIdx >= 0 && !mouseStuffDragThresholdCrossed)
            {
                index = mouseStuffPendingListIdx;
                RunStuffShortPrimaryRelease();
            }

            mouseStuffPendingListIdx = -1;
            mouseStuffDragThresholdCrossed = false;
        }

        if (mouse.rightButton.wasReleasedThisFrame)
            TryInventorySecondaryUseAtMouse(guiMouse, invPosition);
    }

    /// <summary>
    /// Mouse on paperdoll: LMB = inspect; RMB handled in <see cref="TryInventorySecondaryUseAtMouse"/>; drag off = portable pickup.
    /// </summary>
    private void UpdatePaperdollMouseInteraction(float invPosition, bool swallowPrimaryPressForFloatingPlace = false)
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        if (mouseCursorCarriedPortable != null)
        {
            if (mouse.leftButton.wasPressedThisFrame || mouse.leftButton.wasReleasedThisFrame)
            {
                mousePaperPendingSlotIdx = -1;
                mousePaperDragThresholdCrossed = false;
            }

            return;
        }

        Vector2 guiMouse = GuiInput.ScreenToGuiMouse(mouse.position.ReadValue());
        float panelLeft = Screen.width - invPosition;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            mousePaperPendingSlotIdx = -1;
            mousePaperDragThresholdCrossed = false;
            if (swallowPrimaryPressForFloatingPlace)
                return;

            if (!IsGuiMouseOverInventoryPanel(guiMouse, invPosition))
                return;
            if (!TryGetPaperdollSlotUnderMouse(guiMouse, panelLeft, out int slotIdx, out _))
                return;
            if (invSlotContents[slotIdx] == null)
                return;
            mousePaperPendingSlotIdx = slotIdx;
            mousePaperDownScreen = mouse.position.ReadValue();
            return;
        }

        if (mousePaperPendingSlotIdx >= 0 && mouse.leftButton.isPressed)
        {
            if (!mousePaperDragThresholdCrossed
                && (mouse.position.ReadValue() - mousePaperDownScreen).sqrMagnitude
                >= MouseStuffDragThresholdPx * MouseStuffDragThresholdPx)
            {
                equipSlot = mousePaperPendingSlotIdx;
                mousePaperDragThresholdCrossed = true;
                RunPaperdollDragPickupFromSlot(mousePaperPendingSlotIdx);
            }
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            if (suppressPaperdollMousePrimaryOnNextLeftRelease)
            {
                suppressPaperdollMousePrimaryOnNextLeftRelease = false;
            }
            else if (mousePaperPendingSlotIdx >= 0 && !mousePaperDragThresholdCrossed)
            {
                RunPaperdollShortPrimaryReleaseAtSlot(mousePaperPendingSlotIdx);
            }

            mousePaperPendingSlotIdx = -1;
            mousePaperDragThresholdCrossed = false;
        }
    }

    private void ToggleTradeSlotMouse(int slot)
    {
        if (tradeSlotSelected[slot])
        {
            tradeSlotSelected[slot] = false;
            Utils.PlayClip2d(unequip);
        }
        else if (tradeSlots[slot] != null)
        {
            tradeSlotSelected[slot] = true;
            Utils.PlayClip2d(equip);
        }
    }

    private void RunTradeTrayDragPickupFromSlot(int slotIndex)
    {
        skipTradeHowManyOnNextFloatingDrop = false;
        if (!IsPlayerTradeSlotIndex(slotIndex) || tradeSlots[slotIndex] == null)
            return;

        UUObject o = tradeSlots[slotIndex];
        if (o.quantity > 1 && o.stackable)
        {
            pendingTradeTrayDragSlot = slotIndex;
            HowMany.sHowMany.howMany = 1;
            HowMany.AskHowMany(EHowManyReason.MoveStuffMouse, o);
            return;
        }

        tradeSlots[slotIndex] = null;
        tradeSlotSelected[slotIndex] = false;
        mouseCursorCarriedPortable = o;
        usingItem = null;
        Utils.PlayClip2d(unequip);
    }

    private void UpdateTradeTrayMouseInteraction()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || Conversations.runningConversation == null)
            return;

        if (mouseCursorCarriedPortable != null)
        {
            if (mouse.leftButton.wasPressedThisFrame || mouse.leftButton.wasReleasedThisFrame)
            {
                mouseTradePendingSlotIdx = -1;
                mouseTradeDotPressSlot = -1;
                mouseTradeDragThresholdCrossed = false;
            }

            return;
        }

        Vector2 guiMouse = GuiInput.ScreenToGuiMouse(mouse.position.ReadValue());

        if (mouse.leftButton.wasPressedThisFrame)
        {
            mouseTradePendingSlotIdx = -1;
            mouseTradeDotPressSlot = -1;
            mouseTradeDragThresholdCrossed = false;

            if (TryGetTradeSelectionDotUnderMouse(guiMouse, out int dotSlot) && tradeSlots[dotSlot] != null)
            {
                mouseTradeDotPressSlot = dotSlot;
                mouseTradeDownScreen = mouse.position.ReadValue();
                return;
            }

            if (TryGetTradeSlotUnderMouse(guiMouse, out int cellSlot)
                && IsPlayerTradeSlotIndex(cellSlot)
                && tradeSlots[cellSlot] != null)
            {
                mouseTradePendingSlotIdx = cellSlot;
                mouseTradeDownScreen = mouse.position.ReadValue();
            }

            return;
        }

        if (mouseTradeDotPressSlot >= 0 && mouse.leftButton.isPressed)
        {
            if ((mouse.position.ReadValue() - mouseTradeDownScreen).sqrMagnitude
                >= MouseStuffDragThresholdPx * MouseStuffDragThresholdPx)
                mouseTradeDragThresholdCrossed = true;
        }

        if (mouseTradePendingSlotIdx >= 0 && mouse.leftButton.isPressed)
        {
            if (!mouseTradeDragThresholdCrossed
                && (mouse.position.ReadValue() - mouseTradeDownScreen).sqrMagnitude
                >= MouseStuffDragThresholdPx * MouseStuffDragThresholdPx)
            {
                mouseTradeDragThresholdCrossed = true;
                RunTradeTrayDragPickupFromSlot(mouseTradePendingSlotIdx);
            }
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            if (mouseTradeDotPressSlot >= 0 && !mouseTradeDragThresholdCrossed && tradeSlots[mouseTradeDotPressSlot] != null)
                ToggleTradeSlotMouse(mouseTradeDotPressSlot);

            mouseTradePendingSlotIdx = -1;
            mouseTradeDotPressSlot = -1;
            mouseTradeDragThresholdCrossed = false;
        }
    }

    private void UpdateTrading()
    {
        if (Conversations.runningConversation == null)
        {
            currentArea = EInventoryArea.Stuff;
            index = viewIndex;
            return;
        }
        if (ShouldInventoryMoveRepeat(ref invMoveNextFireLeft, InvMoveLeftHeld(), InvMoveLeftPressedThisFrame()) && (tradeSlot & 3) != 0)
        {
            --tradeSlot;
            Utils.PlayClip2d(moveCursor);
        }
        else if (ShouldInventoryMoveRepeat(ref invMoveNextFireRight, InvMoveRightHeld(), InvMoveRightPressedThisFrame()))
        {
            if ((tradeSlot & 3) == 3)
            {
                currentArea = EInventoryArea.Stuff;
                index = viewIndex;
            }
            else
            {
                ++tradeSlot;
            }
            Utils.PlayClip2d(moveCursor);
        }
        else if (ShouldInventoryMoveRepeat(ref invMoveNextFireUp, InvMoveUpHeld(), InvMoveUpPressedThisFrame()) && tradeSlot > 3)
        {
            tradeSlot -= 4;
            Utils.PlayClip2d(moveCursor);
        }
        else if (ShouldInventoryMoveRepeat(ref invMoveNextFireDown, InvMoveDownHeld(), InvMoveDownPressedThisFrame()) && tradeSlot < 4)
        {
            tradeSlot += 4;
            Utils.PlayClip2d(moveCursor);
        }
        else if (Gamepad.current?.yButton.wasPressedThisFrame ?? false)
        {
            TryTakeSelectionFromTradeTray();
        }
        else if (Gamepad.current?.aButton.wasPressedThisFrame ?? false)
        {
            if (tradeSlotSelected[tradeSlot])
            {
                tradeSlotSelected[tradeSlot] = false;
                Utils.PlayClip2d(unequip);
            }
            else if (tradeSlots[tradeSlot] != null)
            {
                tradeSlotSelected[tradeSlot] = true;
                Utils.PlayClip2d(equip);
            }
        }
    }
    
    protected void Update()
    {
        // Panel open/close is driven by PlayerPanelInput (shoulder / key toggle) and Esc — not by holding RB.
        bool exploringInventory = PlayerPanelState.IsEffectivelyExploringInventory;

        // Omit modal UI masks so exploringInventory stays true with the panel open; inner blocks still gate on InventoryModalUiMask.
        if ((PlayerObject.Player.controlsDisabled & (EControlMask.Map | EControlMask.Cutscene | EControlMask.Flute | EControlMask.RoamingSight | EControlMask.EnterMoongate | EControlMask.SaveLoad | EControlMask.Keyboard)) > 0)
        {
            exploringInventory = false;
        }

        PlayerObject.DisableControls(EControlMask.Inventory, exploringInventory);

        if (exploringInventory)
        {
            if (!IsInventoryModalUiBlocking(PlayerObject.Player))
            {
                SyncInventoryMoveRepeatIdleDirections();
                UpdateGamepadStuffYUseChargeState();
                switch (currentArea)
                {
                case EInventoryArea.Stuff:
                    UpdateStuff();
                    break;
                case EInventoryArea.Paperdoll:
                    UpdatePaperdoll();
                    break;
                case EInventoryArea.Trading:
                    UpdateTrading();
                    break;
                }

                const float kExtendedInvMouse = 16f + 4f * 64f;
                float invMousePx = lerpIn * kExtendedInvMouse;

                // When true, run mouse hit-tests and button handling on the panel (paperdoll before stuff grid). Gamepad path ignores this.
                // Also handle clicks over the panel when LastActiveDevice is still Gamepad (hybrid input) so RMB/LMB are not dropped.
                Vector2 guiMouseInvInput = Mouse.current != null
                    ? GuiInput.ScreenToGuiMouse(Mouse.current.position.ReadValue())
                    : Vector2.zero;
                bool mouseOverInventoryPanelForInput = Mouse.current != null
                    && IsGuiMouseOverInventoryPanel(guiMouseInvInput, invMousePx);
                bool mouseButtonsOverInventoryThisFrame = Mouse.current != null
                    && mouseOverInventoryPanelForInput
                    && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.leftButton.wasReleasedThisFrame
                        || Mouse.current.rightButton.wasPressedThisFrame
                        || Mouse.current.rightButton.wasReleasedThisFrame);
                bool mouseOverTradeTraysForInput = Mouse.current != null
                    && IsGuiMouseOverTradeTraysBounding(guiMouseInvInput);
                bool baseMouseNav = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard
                    || mouseButtonsOverInventoryThisFrame;
                bool conversationTradeMouse = Conversations.runningConversation != null
                    && (mouseOverTradeTraysForInput || mouseCursorCarriedPortable != null);
                bool handleMouseInventoryInput = Mouse.current != null
                    && (baseMouseNav || conversationTradeMouse);

                if (handleMouseInventoryInput)
                {
                    bool swallowPrimaryPress = Mouse.current.leftButton.wasPressedThisFrame
                        && suppressInventoryMouseArmAfterFloatingPlace;
                    if (swallowPrimaryPress)
                        suppressInventoryMouseArmAfterFloatingPlace = false;

                    // Paperdoll before stuff — matches floating-place hit order (TryHandleFloatingUsingItemClick).
                    UpdatePaperdollMouseInteraction(invMousePx, swallowPrimaryPress);
                    UpdateStuffMouseInteraction(invMousePx, swallowPrimaryPress);
                    if (Conversations.runningConversation != null)
                        UpdateTradeTrayMouseInteraction();
                }
                else
                {
                    mouseStuffPendingListIdx = -1;
                    mouseStuffDragThresholdCrossed = false;
                    mousePaperPendingSlotIdx = -1;
                    mousePaperDragThresholdCrossed = false;
                    mouseTradePendingSlotIdx = -1;
                    mouseTradeDotPressSlot = -1;
                    mouseTradeDragThresholdCrossed = false;
                }
            }
            else
            {
                ResetInventoryMoveRepeatTimers();
                mouseStuffPendingListIdx = -1;
                mouseStuffDragThresholdCrossed = false;
                mousePaperPendingSlotIdx = -1;
                mousePaperDragThresholdCrossed = false;
                suppressStuffMousePrimaryOnNextLeftRelease = false;
                suppressPaperdollMousePrimaryOnNextLeftRelease = false;
                suppressInventoryMouseArmAfterFloatingPlace = false;
                CancelPendingThrowCloseInventory();
            }
        }
        else
        {
            ResetInventoryMoveRepeatTimers();
            mouseStuffPendingListIdx = -1;
            mouseStuffDragThresholdCrossed = false;
            mousePaperPendingSlotIdx = -1;
            mousePaperDragThresholdCrossed = false;
            suppressStuffMousePrimaryOnNextLeftRelease = false;
            suppressPaperdollMousePrimaryOnNextLeftRelease = false;
            suppressInventoryMouseArmAfterFloatingPlace = false;
            CancelPendingThrowCloseInventory();
            if (!IsInventoryModalUiBlocking(PlayerObject.Player))
            {
                usingItem = null;
            }
        }

        bool modalBlocksSlide = IsInventoryModalUiBlocking(PlayerObject.Player);
        if (modalBlocksSlide
            && GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard
            && !exploringInventory
            && (PlayerObject.Player.controlsDisabled & EControlMask.HowMany) != 0)
        {
            modalBlocksSlide = false;
        }

        bool shouldHoldSlideOpen = exploringInventory || modalBlocksSlide;

        if (shouldHoldSlideOpen)
            holdTime = 2.0f;
        else if (inventorySlideWasOpenLastFrame)
            holdTime = 0f;

        inventorySlideWasOpenLastFrame = shouldHoldSlideOpen;

        if (holdTime > 0.0f)
            holdTime -= Time.unscaledDeltaTime;

        if (exploringInventory && currentArea == EInventoryArea.Stuff && Conversations.runningConversation == null)
        {
            if (Gamepad.current?.xButton.wasPressedThisFrame ?? false)
            {
                throwTime = 0.0f;
                throwItem = GetCurrentItem();
            }
            else if (Gamepad.current?.xButton.wasReleasedThisFrame ?? false)
            {
                if (throwItem != null && throwItem == GetCurrentItem() && throwTime > 0.0f)
                {
                    throwForce = throwTime;
                    if (throwItem.quantity > 1)
                    {
                        HowMany.AskHowMany(EHowManyReason.Throw, throwItem);
                    }
                    else
                    {
                        HowMany.sHowMany.howMany = 1;
                        TryThrow(throwItem);
                    }
                }
            }
            else if ((Gamepad.current?.xButton.isPressed ?? false) && throwItem != null && throwItem == GetCurrentItem())
            {
                throwTime += Time.deltaTime;
                throwTime = Mathf.Min(throwTime, 1.0f);
            }
            else
            {
                throwItem = null;
                throwTime = 0.0f;
            }
        }
        else if (exploringInventory && Conversations.runningConversation != null)
        {
            throwItem = null;
            throwTime = 0.0f;
        }

        // Mouse: hold LMB while carrying a cursor portable — charge throw (like gamepad X), release to hurl. Outside the slide-out panel (or anytime when inventory UI is closed).
        if (GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard
            && Mouse.current != null
            && mouseCursorCarriedPortable != null
            && Conversations.runningConversation == null)
        {
            if ((PlayerObject.Player.controlsDisabled & EControlMask.RepairDialog) != 0)
            {
                mouseOutsideThrowItem = null;
                mouseOutsideThrowChargeTime = 0f;
            }
            else if (IsInventoryModalUiBlocking(PlayerObject.Player))
            {
                if (HowMany.sHowMany == null || HowMany.sHowMany.howManyReason != EHowManyReason.Throw)
                {
                    mouseOutsideThrowItem = null;
                    mouseOutsideThrowChargeTime = 0f;
                }
            }
            else
            {
            Mouse om = Mouse.current;
            Rect outsideRect = GetPanelGuiRectForOutsideClick();
            Vector2 ogui = GuiInput.ScreenToGuiMouse(om.position.ReadValue());
            bool outsidePanelForThrow = !exploringInventory
                || (outsideRect.width > 0f && !outsideRect.Contains(ogui));

            if (om.leftButton.wasPressedThisFrame && outsidePanelForThrow)
            {
                mouseOutsideThrowChargeTime = 0f;
                mouseOutsideThrowItem = mouseCursorCarriedPortable;
            }
            else if (om.leftButton.wasReleasedThisFrame
                && mouseOutsideThrowItem != null
                && mouseOutsideThrowItem == mouseCursorCarriedPortable
                && outsidePanelForThrow)
            {
                throwForce = Mathf.Min(Mathf.Max(mouseOutsideThrowChargeTime, 0.02f), 1f);
                // Cursor portable quantity was already chosen via Pickup / MoveStuffMouse HowMany.
                HowMany.sHowMany.howMany = mouseOutsideThrowItem.quantity;
                PrepareThrowImpulseFromMouseCursor();
                closeInventoryAfterSuccessfulThrow = exploringInventory;
                TryThrow(mouseOutsideThrowItem);
                SuppressMouseLookOnNextLeftRelease();

                mouseOutsideThrowItem = null;
                mouseOutsideThrowChargeTime = 0f;
            }
            else if (om.leftButton.isPressed
                && mouseOutsideThrowItem != null
                && mouseOutsideThrowItem == mouseCursorCarriedPortable
                && outsidePanelForThrow)
            {
                mouseOutsideThrowChargeTime += Time.deltaTime;
                mouseOutsideThrowChargeTime = Mathf.Min(mouseOutsideThrowChargeTime, 1f);
            }
            else if (mouseOutsideThrowItem != null)
            {
                mouseOutsideThrowItem = null;
                mouseOutsideThrowChargeTime = 0f;
            }
            }
        }

        float lerpTarget = (holdTime > 0.0f ? 1.0f : 0.0f);
        lerpIn = Utils.DampedApproachUnscaledTime(lerpIn, lerpTarget, 0.2f);

        aPressedTime = Gamepad.current?.aButton.isPressed ?? false ? aPressedTime + Time.unscaledDeltaTime : 0.0f;
    }

    private float throwTime;
    private float throwForce;
    private UUObject throwItem;

    private const float GamepadYUseChargeDurationSeconds = 0.5f;
    private float gamepadYUseChargeNormalized;
    private int gamepadYUseChargeIndex = -1;
    private bool gamepadYUseAwaitingRelease;

    private float mouseOutsideThrowChargeTime;
    private UUObject mouseOutsideThrowItem;

    /// <summary>If true, the next successful <see cref="TryThrow"/> closes the inventory (outside-panel mouse throw).</summary>
    public bool closeInventoryAfterSuccessfulThrow;

    /// <summary>Set by mouse aim before <see cref="TryThrow"/>; consumed there. Null uses camera forward (gamepad X).</summary>
    private Vector3? pendingThrowDirectionWorldUnit;

    /// <summary>
    /// Mouse-through-camera ray from <see cref="PrepareThrowImpulseFromMouseCursor"/>; valid until <see cref="TryThrow"/> completes
    /// or <see cref="CancelPendingThrowCloseInventory"/>. Same geometry as throw aim — reuse for any logic that should resolve under
    /// the cursor during that mouse throw (e.g. <see cref="UUObject.FindAnvil"/>, future key-on-door checks).
    /// </summary>
    private Ray? pendingMouseScreenRayWorldQuery;

    /// <summary>Ray from mouse through the camera for thrown-object impulse (call immediately before <see cref="TryThrow"/>).</summary>
    public void PrepareThrowImpulseFromMouseCursor()
    {
        Camera cam = PlayerObject.Player != null ? PlayerObject.Player.mainCamera : null;
        Mouse m = Mouse.current;
        if (cam == null || m == null)
        {
            pendingThrowDirectionWorldUnit = null;
            pendingMouseScreenRayWorldQuery = null;
            return;
        }

        Ray ray = cam.ScreenPointToRay(m.position.ReadValue());
        pendingMouseScreenRayWorldQuery = ray;
        Vector3 d = ray.direction;
        pendingThrowDirectionWorldUnit = d.sqrMagnitude > 1e-10f ? d.normalized : cam.transform.forward;
    }

    /// <summary>Non-consuming read of <see cref="pendingMouseScreenRayWorldQuery"/> while a mouse throw is in progress.</summary>
    public bool TryPeekPendingMouseScreenRayWorldQuery(out Ray ray)
    {
        if (pendingMouseScreenRayWorldQuery.HasValue)
        {
            ray = pendingMouseScreenRayWorldQuery.Value;
            return true;
        }

        ray = default;
        return false;
    }

    private int mouseStuffPendingListIdx = -1;
    private Vector2 mouseStuffDownScreen;
    private bool mouseStuffDragThresholdCrossed;
    private const float MouseStuffDragThresholdPx = 10f;

    private int mousePaperPendingSlotIdx = -1;
    private Vector2 mousePaperDownScreen;
    private bool mousePaperDragThresholdCrossed;

    /// <summary>When <see cref="HowManyReason.Trade"/> targets a mouse drop onto a specific tray cell (<see cref="TryPutFloatingIntoTradeTrayAtSlot"/>).</summary>
    public int pendingFloatingTradeSlot = -1;

    /// <summary>Set during <see cref="HowManyReason.MoveStuffMouse"/> when peeling from a trade tray slot (see <see cref="CompleteMoveStuffMouseAfterHowMany"/>).</summary>
    public int pendingTradeTrayDragSlot = -1;

    /// <summary>After MoveStuffMouse chose a quantity; skip opening Trade HowMany when dropping that portable onto the tray.</summary>
    private bool skipTradeHowManyOnNextFloatingDrop;

    private int mouseTradePendingSlotIdx = -1;
    private int mouseTradeDotPressSlot = -1;
    private Vector2 mouseTradeDownScreen;
    private bool mouseTradeDragThresholdCrossed;

    /// <summary>Set from OnGUI after a cursor-carried portable was placed — suppresses stuff-grid look/use on the matching mouse up (Update can run before OnGUI).</summary>
    private bool suppressStuffMousePrimaryOnNextLeftRelease;

    /// <summary>Same pattern as <see cref="suppressStuffMousePrimaryOnNextLeftRelease"/> for paperdoll LMB short-release inspect.</summary>
    private bool suppressPaperdollMousePrimaryOnNextLeftRelease;

    /// <summary>Suppresses world short-click look on the LMB up that completes a cursor charge-throw.</summary>
    private bool suppressMouseLookOnNextLeftRelease;

    /// <summary>IMGUI MouseDown can place before Input System sees <c>wasPressedThisFrame</c>; swallow the next primary press so we do not arm stuff/paperdoll pending clicks on the drop target (would open sacks / use items on mouse up).</summary>
    private bool suppressInventoryMouseArmAfterFloatingPlace;

    /// <summary>Unity may deliver the same LMB MouseDown to OnGUI more than once per frame; only run one cursor-carried placement attempt.</summary>
    private int floatingPlaceMouseDownHandledFrame = -1;

    private Material chargeRingMatInstance;

    private List<UUObject> FindObjectInInventoryRecursive(List<UUObject> contents, UUObject target)
    {
        if (contents != null)
        {
            foreach (var obj in contents)
            {
                if (obj == target) return contents;
                List<UUObject> maybe = FindObjectInInventoryRecursive(obj.contents, target);
                if (maybe != null) return maybe;
            }
        }

        return null;
    }

    public List<UUObject> FindObjectInInventory(UUObject target)
    {
        return FindObjectInInventoryRecursive(inventory, target);
    }

    public UUObject FindObjectInInventoryRecursive(List<UUObject> contents, EObjectType type)
    {
        if (contents != null)
        {
            foreach (var obj in contents)
            {
                if (obj.type == type) return obj;
                UUObject maybe = FindObjectInInventoryRecursive(obj.contents, type);
                if (maybe != null) return maybe;
            }
        }

        return null;
    }

    public UUObject FindObjectInInventory(EObjectType type)
    {
        return FindObjectInInventoryRecursive(inventory, type);
    }

    private void FindObjectsInInventoryRecursive(List<UUObject> inv, List<UUObject> found, EObjectType type)
    {
        if (inv != null)
        {
            foreach (UUObject obj in inv)
            {
                if (obj.type == type) found.Add(obj);
                FindObjectsInInventoryRecursive(obj.contents, found, type);
            }
        }
    }

    public List<UUObject> FindObjectsInInventory(EObjectType type)
    {
        List<UUObject> found = new List<UUObject>();
        FindObjectsInInventoryRecursive(inventory, found, type);
        return found;
    }

    private void FindObjectsInInventoryRecursive(List<UUObject> inv, List<UUObject> found, UUObject.EClass objClass)
    {
        if (inv != null)
        {
            foreach (UUObject obj in inv)
            {
                if (obj.getClass == objClass) found.Add(obj);
                FindObjectsInInventoryRecursive(obj.contents, found, objClass);
            }
        }
    }

    public List<UUObject> FindObjectsInInventory(UUObject.EClass objClass)
    {
        List<UUObject> found = new List<UUObject>();
        FindObjectsInInventoryRecursive(inventory, found, objClass);
        return found;
    }

    private int GetInventoryWeightRecursive(List<UUObject> contents, int weight)
    {
        int newWeight = weight;
        if (contents != null)
        {
            foreach (var obj in contents)
            {
                newWeight += obj.totalWeight;
                newWeight = GetInventoryWeightRecursive(obj.contents, newWeight);
            }
        }

        return newWeight;
    }

    public float GetInventoryWeight()
    {
        int weight = 0;
        weight = GetInventoryWeightRecursive(inventory, weight);
        foreach (var obj in invSlotContents)
        {
            if (obj != null)
            {
                weight += obj.totalWeight;
            }
        }

        return 0.1f * weight;
    }

    /// <summary>
    /// After <see cref="TryThrow"/>'s logical <see cref="UUObject.Throw"/> path: keys/poles/etc. can succeed without
    /// leaving the cursor — do not close the inventory slide-out in that case.
    /// </summary>
    private bool ItemStillInHandAfterThrowSuccess(UUObject item)
    {
        if (!item)
        {
            return false;
        }

        return mouseCursorCarriedPortable == item || usingItem == item;
    }

    public bool TryThrow(UUObject itemToThrow)
    {
        bool closeInventoryOnSuccess = closeInventoryAfterSuccessfulThrow;
        closeInventoryAfterSuccessfulThrow = false;

        Vector3 throwDir = pendingThrowDirectionWorldUnit ?? PlayerObject.Player.mainCamera.transform.forward;
        pendingThrowDirectionWorldUnit = null;

        try
        {
            void ClearHeldAfterThrow(UUObject thrown)
            {
                if (usingItem == thrown)
                {
                    usingItem = null;
                }

                if (mouseCursorCarriedPortable == thrown)
                {
                    // Mouse charge-throw runs Throw() before physics; keys/poles/bones can "Use" in place.
                    // Only clear the follow-cursor portable when the object is gone or actually in the world.
                    if (thrown == null || LevelLoader.worldObj.Contains(thrown))
                    {
                        mouseCursorCarriedPortable = null;
                    }
                }
            }

            // throw (eg flag) // TODO: switch to use
            if (itemToThrow.Throw())
            {
                ClearHeldAfterThrow(itemToThrow);
                if (itemToThrow != null && !LevelLoader.worldObj.Contains(itemToThrow))
                {
                    TryStowMouseCursorCarriedPortable();
                    CancelPendingThrowCloseInventory();
                }

                if (closeInventoryOnSuccess && !ItemStillInHandAfterThrowSuccess(itemToThrow))
                {
                    HidePanel();
                }

                return true;
            }

            {
                // try to find a spot to start the throw
                Vector3 center = PlayerObject.Player.mainCamera.transform.position;
                Vector3 pos = center + 1.0f * throwDir;
                bool found = false;
                for (int i = 0; i < 10; ++i)
                {
                    int layerMask = LayerMasks.EnvironmentAndCeiling;
                    if (!Physics.SphereCast(center, 0.2f, pos - center, out RaycastHit hit, 1.0f, layerMask)
                        && !Physics.CheckSphere(pos, 0.5f, layerMask))
                    {
                        Debug.DrawLine(center, pos, Color.green, 10.0f);
                        found = true;
                        break;
                    }

                    Debug.DrawLine(pos - 0.5f * Vector3.up, pos + 0.5f * Vector3.up, Color.red, 5.0f);
                    Debug.DrawLine(pos - 0.5f * Vector3.right, pos + 0.5f * Vector3.right, Color.red, 5.0f);
                    Debug.DrawLine(pos - 0.5f * Vector3.forward, pos + 0.5f * Vector3.forward, Color.red, 5.0f);

                    pos = center + Random.onUnitSphere;
                }

                if (found)
                {
                    int howMany = HowMany.sHowMany.howMany;
                    if (itemToThrow.stackable && howMany < itemToThrow.quantity)
                    {
                        // split
                        UUObject newItem = LevelLoader.CreateObjectOfType(itemToThrow.type);
                        newItem.quantity = howMany;
                        CopyPeeledStackProperties(itemToThrow, newItem);
                        // Initialize name properties so item has proper name
                        newItem.PostLoadInitialize();
                        itemToThrow.quantity -= howMany;
                        itemToThrow = newItem;
                    }
                    else
                    {
                        List<UUObject> container = FindObjectInInventoryRecursive(inventory, itemToThrow);
                        if (container != null)
                        {
                            container.Remove(itemToThrow);
                            if (container == GetCurrentListInternal())
                            {
                                index = Mathf.Clamp(index, 0, container.Count - 1);
                            }
                        }
                    }

                    itemToThrow.transform.position = pos;

                    LevelLoader.AddToWorld(itemToThrow);

                    Rigidbody rb = itemToThrow.GetComponentInChildren<Rigidbody>();
                    if (rb == null)
                    {
                        itemToThrow.gameObject.AddComponent<Rigidbody>();
                    }

                    foreach (Rigidbody rig in itemToThrow.GetComponentsInChildren<Rigidbody>())
                    {
                        rig.isKinematic = false;
                        rig.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                        // Throw doesn't need to depend on stats.
                        // Really shouldn't because emerald puzzle relies on a good throw.
                        Vector3 force = 10.0f * throwForce * throwDir;
                        rig.AddForceAtPosition(force, rig.transform.position, ForceMode.Impulse);
                    }

                    Utils.PlayClip2d(throwClip);

                    ClearHeldAfterThrow(itemToThrow);
                    if (closeInventoryOnSuccess)
                        HidePanel();

                    return true;
                }
            }

            return false;
        }
        finally
        {
            pendingMouseScreenRayWorldQuery = null;
        }
    }

    /// <summary>Clears pending close-after-throw (e.g. How many? cancelled).</summary>
    public void CancelPendingThrowCloseInventory()
    {
        closeInventoryAfterSuccessfulThrow = false;
        mouseOutsideThrowItem = null;
        mouseOutsideThrowChargeTime = 0f;
        pendingThrowDirectionWorldUnit = null;
        pendingMouseScreenRayWorldQuery = null;
        ClearGamepadStuffYUseChargeState();
    }

    private EInvSlot[] armorSlots =
    {
        EInvSlot.Head, EInvSlot.Torso, EInvSlot.Hands, EInvSlot.Legs, EInvSlot.Feet, EInvSlot.LeftHand,
        EInvSlot.RightHand
    };

    public int GetTotalArmourScore()
    {
        int score = 0;
        foreach (var armorSlot in armorSlots)
        {
            // TODO: add GetDefence to rings
            UUObject armour = invSlotContents[(int)armorSlot];
            if (armour != null)
            {
                score += armour.GetDefence();
            }
        }

        return score;
    }

    public UUObject GetRandomArmourPiece()
    {
        List<UUObject> equippedArmour = new List<UUObject>();
        foreach (var armorSlot in armorSlots)
        {
            UUObject armour = invSlotContents[(int)armorSlot];
            if (armour != null
                && (armour.getClass == UUObject.EClass.Armour || armour.getClass == UUObject.EClass.Armour2))
            {
                equippedArmour.Add(armour);
            }
        }

        if (equippedArmour.Count > 0)
        {
            return equippedArmour[Random.Range(0, equippedArmour.Count)];
        }

        return null;
    }

    public void DestroyEquippedItem(UUObject obj)
    {
        obj.Unequip();
        for (int i = 0; i < invSlotContents.Length; ++i)
        {
            if (invSlotContents[i] == obj)
            {
                invSlotContents[i] = null;
                Utils.DestroyItem(obj);
                break;
            }
        }
    }

    public bool Wearing(EObjectType type)
    {
        for (int i = 0; i < invSlotContents.Length; ++i)
        {
            if (invSlotContents[i] != null && invSlotContents[i].type == type)
            {
                return true;
            }
        }
        return false;
    }

    public int Conversation_show_inv(ref short[] mem, int posPtr, int itemPtr)
    {
        int num = 0;
        foreach (int i in playerTradeIndices)
        {
            if (tradeSlots[i] != null && tradeSlotSelected[i])
            {
                mem[posPtr++] = (short)i;
                mem[itemPtr++] = (short)tradeSlots[i].type;
                ++num;
            }
        }

        for (int i = num; i < 4; ++i)
        {
            mem[posPtr++] = 0;
            mem[itemPtr++] = 0;
        }

        return num;
    }

    public int Conversation_give_to_npc(Critter npc, ref short[] mem, int posPtr, int count)
    {
        for (int i = 0; i < count; ++i)
        {
            int slot = mem[posPtr++];
            
            npc.contents.Add(tradeSlots[slot]);
            
            tradeSlots[slot] = null; // remove item.
            tradeSlotSelected[slot] = false;
        }

        return count;
    }

    public int Conversation_give_ptr_npc(Critter npc, ref short[] mem, int quantity, int listPos)
    {
        // not sure what to do here
        if (listPos > 0)
        {
            npc.contents.Add(tradeSlots[listPos]);
            
            tradeSlots[listPos] = null;
            tradeSlotSelected[listPos] = false;
            return 1;
        }
        return 0;
    }

    public int Conversation_take_id_from_npc(Critter npc, int contentsIndex)
    {
        int i = npc.contents.Count - contentsIndex;
        if (i >= 0 && i < npc.contents.Count)
        {
            UUObject obj = npc.contents[i];
            Add(obj);
            npc.contents.Remove(obj);
            return 1;
        }
        return 0;
    }

    public int Conversation_x_obj_stuff(ref short[] mem, int pObjIndex, int pMode, int pIdentify, int pOwner,
        int pFlags, int pLink, int pFlag1, int pFlag0, int pQuality)
    {
        int slot = mem[pObjIndex];
        if (slot >= 0 && slot < 8)
        {
            UUObject o = tradeSlots[slot]; 
            if (o != null)
            {
                if (mem[pMode] == 0)
                {
                    // get

                    if (mem[pIdentify] != -1)
                    {
                        // used 0-7
                        mem[pIdentify] = (short)(2 * (short)o.loreResult);
                    }

                    if (mem[pOwner] != -1)
                    {
                        mem[pOwner] = (short) o.ownerIndex;
                    }

                    if (mem[pFlags] != -1)
                    {
                        mem[pFlags] = (short)o.flags;
                    }

                    if (mem[pLink] != -1)
                    {
                        mem[pLink] = (short)(o.link & 0x1ff);
                    }

                    if (mem[pFlag1] != -1)
                    {
                        mem[pFlag1] = (short)((o.flags & 2) >> 1);
                    }

                    if (mem[pFlag0] != -1)
                    {
                        mem[pFlag0] = (short)(o.flags & 1);
                    }

                    if (mem[pQuality] != -1)
                    {
                        mem[pQuality] = (short)o.quality;
                    }
                }
                else
                {
                    // set
                    if (mem[pIdentify] != -1)
                    {
                        // used 0-7
                        o.loreResult = (Skills.ESkillTestResult)(mem[pIdentify] / 2);
                    }

                    if (mem[pOwner] != -1)
                    {
                        o.ownerIndex = mem[pOwner];
                    }
                    
                    if (mem[pFlags] != -1)
                    {
                        o.flags = mem[pFlags];
                    }

                    if (mem[pLink] != -1)
                    {
                        o.link = mem[pLink];
                    }

                    if (mem[pFlag1] != -1)
                    {
                        o.flags &= ~2;
                        o.flags |= mem[pFlag1] != 0 ? 2 : 0;
                    }

                    if (mem[pFlag0] != -1)
                    {
                        o.flags &= ~1;
                        o.flags |= mem[pFlag0] != 0 ? 1 : 0;
                    }

                    if (mem[pQuality] != -1)
                    {
                        o.quality = mem[pQuality];
                    }
                }
            }
        }

        // not important
        return 0;
    }

    public int Conversation_check_inv_quality(int slotIndex)
    {
        UUObject obj = tradeSlots[slotIndex];
        if (obj != null)
        {
            return obj.quality;
        }
        return 0;
    }

    public void Conversation_set_inv_quality(Critter npc, int itemIndex, int quality)
    {
        // since the item isn't put in the trade slot, this must be a modification of something in contents
        // for now just assume the last item taken is the one we want to modify
        // it's also possible it's a 1-indexed offset from the top of contents - i.e. npc.Contents.Count - (itemIndex - 1)
        // let's try that :)
        int contentsIndex = npc.contents.Count - (itemIndex - 1);
        if (contentsIndex < 0 || contentsIndex >= npc.contents.Count)
        {
            contentsIndex = npc.contents.Count - 1;
        }
        if (contentsIndex >= 0 && contentsIndex < npc.contents.Count)
        {
            UUObject obj = npc.contents[contentsIndex];
            if (obj != null)
            {
                obj.quality = quality;
            }
        }
    }

    private int GetValueOfTradeItems(bool useLikesAndDislikes, int[] indices)
    {
        int totalValue = 0;
        foreach (int i in indices)
        {
            UUObject obj = tradeSlots[i];
            if (tradeSlotSelected[i] && obj != null)
            {
                int val = DataLoader.sDataLoader.comObjProps[(int)obj.type].monetaryValue * obj.quantity;
                if (useLikesAndDislikes)
                {
                    if (Conversations.runningConversation.npc.likes.Contains(obj.type)
                        || Conversations.runningConversation.npc.likes.Contains((EObjectType)(1000 + (int)obj.getClass)))
                    {
                        val *= 3;
                        val /= 2;
                    }
                    else if (Conversations.runningConversation.npc.dislikes.Contains(obj.type)
                             || Conversations.runningConversation.npc.dislikes.Contains((EObjectType)(1000 + (int)obj.getClass)))
                    {
                        val /= 2;
                    }
                }

                totalValue += val;
            }
        }

        return totalValue;
    }

    public int GetValueOfNpcTrade(bool useLikesAndDislikes)
    {
        return GetValueOfTradeItems(useLikesAndDislikes, npcTradeIndices);
    }

    public int GetValueOfPlayerTrade(bool useLikesAndDislikes)
    {
        return GetValueOfTradeItems(useLikesAndDislikes, playerTradeIndices);
    }

    public int Conversation_identify_inv(int block, int slot, int addArticle, int loreResult)
    {
        if (tradeSlotSelected[slot])
        {
            UUObject obj = tradeSlots[slot];
            if (obj != null)
            {
                // TODO: depending on who calls this, identify differently
                // really just comes down to Dominus and Shak
                
                // shak: *pUnk1=0; *pUnk2=1; *pUnk3=2 (definitely trade slot, as I moved it and it moved)
                // dominus: *pUnk1=3; *pUnk2=1
                // goldthirst: *pUnk1=0; *pUnk2=0
                // guard: *pUnk1=0; *pUnk2=0
                // so I think unk1 is the lore result, unk2 is whether to write to a string, and unk3 is the trade slot

                if (loreResult == 3)
                {
                    obj.loreResult = (Skills.ESkillTestResult)loreResult;
                    StringLoader.SetString(block, 0, obj.GetLookName());
                }
                else if (addArticle > 0)
                {
                    StringLoader.SetString(block, 0, obj.GetNonMagicalName());
                }
                else
                {
                    StringLoader.SetString(block, 0, obj.singularName);
                }

                return DataLoader.sDataLoader.comObjProps[(int)obj.type].monetaryValue * obj.quantity;
            }
        }

        return 0;
    }

    public int Conversation_count_inv(int pos)
    {
        UUObject obj = tradeSlots[pos];
        if (obj != null)
        {
            return obj.quantity;
        }

        return 0;
    }

    public int GetRelativeValueOfTrade(bool useLikesAndDislikes)
    {
        return GetValueOfNpcTrade(useLikesAndDislikes) - GetValueOfPlayerTrade(useLikesAndDislikes);
    }

    public bool DealPossible()
    {
        int mask = 0;
        for (int i = 0; i < 8; ++i)
        {
            if (tradeSlotSelected[i] && tradeSlots[i] != null)
            {
                mask |= (1 << i);
            }
        }
        // at least one item in 0145 and one item in 2367
        return (mask & 0x33) > 0 && (mask & 0xcc) > 0;
    }

    public string Conversation_do_judgement()
    {
        int relativeValue = GetRelativeValueOfTrade(false);
        int appraiseSkill = Skills.GetSkill(ESkill.Appraise);
        int appraiseVariance = (30 - appraiseSkill) / 3;
        relativeValue += Random.Range(-appraiseVariance, appraiseVariance + 1);
        // I guess...I know
        string message = "<color=#000000><i>" + StringLoader.GetString(7, Mathf.Clamp(3 + appraiseSkill / 6, 3, 7));
        // that I am getting
        message += StringLoader.GetString(7, 2);
        // a <blah> deal
        message += StringLoader.GetString(7, Mathf.Clamp(12 + relativeValue / 5, 8, 16)) + "</i></color>";

        return message;
    }

    public int Conversation_find_barter(int type)
    {
        foreach (int i in playerTradeIndices)
        {
            if (tradeSlotSelected[i] && tradeSlots[i] != null)
            {
                if (type >= 1000)
                {
                    if ((int)tradeSlots[i].type / 16 == type - 1000)
                    {
                        return i;
                    }
                }
                else if ((int)tradeSlots[i].type == type)
                {
                    return i;
                }
            }
        }

        return 0;
    }

    public int Conversation_find_barter_total(int type, ref short[] mem, int ptrToSlots, int ptrToSlotCount)
    {
        mem[ptrToSlotCount] = 0;
        int count = 0;
        foreach (int i in playerTradeIndices)
        {
            if (tradeSlotSelected[i] && tradeSlots[i] != null && (int)tradeSlots[i].type == type)
            {
                ++mem[ptrToSlotCount];
                mem[ptrToSlots++] = (short)i;
                count += tradeSlots[i].quantity;
            }
        }
        return count;
    }

    public UUObject GetCurrentItemIfInventoryActive()
    {
        if (holdTime > 1.8f && currentArea == EInventoryArea.Stuff)
        {
            return GetCurrentItem();
        }
        return null;
    }

    public int carryX = 160;
    public int carryY = 200;

    public Texture2D crackPatch;
    public float crackPatchHeight = 30.0f;

    private Rect gr;

    private void DrawTex(float x, float y, float w, float h, Texture tex)
    {
        gr.x = x;
        gr.y = y;
        gr.width = w;
        gr.height = h;
        GUI.DrawTexture(gr, tex);
    }

    private void DrawChargeRingFill(Rect ringRect, float fill01)
    {
        if (xButtonChargeRing == null || chargeRingMatInstance == null || Event.current.type != EventType.Repaint)
        {
            return;
        }

        chargeRingMatInstance.SetFloat("_Fill", Mathf.Clamp01(fill01));
        Graphics.DrawTexture(ringRect, xButtonChargeRing, chargeRingMatInstance);
    }

    /// <summary>Fill for mouse LMB throw charge while <see cref="mouseCursorCarriedPortable"/> is active (for <see cref="SoftwareCursorOverlay"/>).</summary>
    public bool TryGetMouseCursorThrowChargeFill(out float fill01)
    {
        fill01 = 0f;
        if (mouseCursorCarriedPortable == null
            || mouseOutsideThrowItem != mouseCursorCarriedPortable
            || mouseOutsideThrowChargeTime <= 0f)
        {
            return false;
        }

        fill01 = Mathf.Clamp01(mouseOutsideThrowChargeTime);
        return true;
    }

    /// <summary>Draw throw charge ring at <paramref name="ringRect"/> using the same material as the gamepad X ring.</summary>
    public void DrawThrowChargeRingForFill(Rect ringRect, float fill01)
    {
        DrawChargeRingFill(ringRect, fill01);
    }

    private void DrawXButtonChargeRingOverlay(Rect ringRect)
    {
        bool gamepadCharge = throwItem != null && throwItem == GetCurrentItem() && throwTime > 0.0f;
        bool mouseCharge = mouseCursorCarriedPortable != null
            && mouseOutsideThrowItem == mouseCursorCarriedPortable
            && mouseOutsideThrowChargeTime > 0f;
        if (!gamepadCharge && !mouseCharge)
        {
            return;
        }

        float fill = gamepadCharge ? Mathf.Clamp01(throwTime) : Mathf.Clamp01(mouseOutsideThrowChargeTime);
        DrawChargeRingFill(ringRect, fill);
    }

    private void DrawYButtonUseChargeRingOverlay(Rect ringRect)
    {
        if (currentArea != EInventoryArea.Stuff
            || Conversations.runningConversation != null
            || Gamepad.current?.yButton.isPressed != true
            || gamepadYUseChargeIndex < 0)
        {
            return;
        }

        DrawChargeRingFill(ringRect, Mathf.Clamp01(gamepadYUseChargeNormalized));
    }

    protected void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Inventory;

        if (PlayerPanelState.IsEffectivelyExploringInventory)
        {
            GuiInput.RegisterBlockingRect(GetPanelGuiRectForOutsideClick());
        }

        if (Conversations.runningConversation != null)
        {
            GuiInput.RegisterBlockingRect(GetTradeTraysBoundingRect());
        }

        int x1 = (Screen.width - 882) / 2 + 167;
        int x2 = (Screen.width - 882) / 2 + 602;

        if (Conversations.runningConversation != null)
        {
            for (int i = 0; i < 8; ++i)
            {
                float x = x1 + ddx * (i & 1) + (x2 - x1) * (i & 2) / 2;
                float y = y1 + ddy * (i / 4);

                UUObject tradeObj = tradeSlots[i];
                if (tradeObj != null)
                {
                    Texture2D tex = tradeObj.GetInventoryTex();

                    GUI.DrawTexture(new Rect(x, y, 3 * tex.width, 3.6f * tex.height), tex);

                    if (tradeSlotSelected[i])
                    {
                        int xoff = (i & 1) > 0 ? xo2 : xo1;
                        GUI.DrawTexture(new Rect(x + xoff, y + yo1, 9, 11), tradingLight);
                    }
                }
                if (lerpIn > 0.001f && currentArea == EInventoryArea.Trading && i == tradeSlot)
                {
                    GUI.DrawTexture(new Rect(x, y, 48, 58), cursor);
                }
                if (tradeObj != null && tradeObj.quantity > 1)
                {
                    GUI.Label(new Rect(x + 30, y, 20, 10), tradeObj.quantity.ToString(), descriptionStyle);
                }
            }
        }

        if (lerpIn > 0.001f)
        {
            // draw the inventory
            const float kExtendedInvPosition = (16 + 4 * 64);
            float invPosition = lerpIn * kExtendedInvPosition;

            // panel
            {
                Texture2D tex = DataLoader.sDataLoader.panelsTex[0];
                GUI.DrawTexture(new Rect(Screen.width - invPosition, 30, 3 * tex.width, 3.6f * tex.height), tex);
                // now draw a section at the bottom to extend the panel by two rows
                GUI.DrawTextureWithTexCoords(
                    new Rect(Screen.width - invPosition, 30 + 3.6f * (tex.height - 4), 3 * tex.width, 3.6f * 40),
                    tex,
                    new Rect(0, 0, 1, 40.0f / 114));
                // finally draw the patch to cover the crack
                GUI.DrawTexture(new Rect(Screen.width - invPosition + 9, 30 + 3.6f * (tex.height - crackPatchHeight), 3 * crackPatch.width, 3.6f * crackPatch.height), crackPatch);
            }
            // woman
            {
                Texture2D tex = DataLoader.sDataLoader.bodiesTex[(PlayerData.sData.female ? 5 : 0) + PlayerData.sData.portrait];
                GUI.DrawTexture(new Rect(Screen.width - invPosition + 72, 45, 3 * tex.width, 3.6f * tex.height), tex);
            }
            // carry weight remaining
            int remainingCarryWeight = Mathf.Max(0, PlayerObject.Player.remainingCarryWeight);
            GUI.Label(new Rect(Screen.width - invPosition + 196, 219, 20, 20), remainingCarryWeight.ToString(), carryStyle);

            // scroll indicators
            List<UUObject> itemsToDisplay = GetCurrentListInternal();
            float scrollIndicatorX = Screen.width - invPosition + 174;
            
            // Show up arrow if we can scroll up
            if (viewIndex > 0 && DataLoader.sDataLoader.buttonsTex != null && DataLoader.sDataLoader.buttonsTex.Length > 27)
            {
                Texture2D upArrow = DataLoader.sDataLoader.buttonsTex[27];
                if (upArrow != null)
                {
                    GUI.DrawTexture(new Rect(scrollIndicatorX, 256, 3 * upArrow.width, 3.6f * upArrow.height), upArrow);
                }
            }
            
            // Show down arrow if we can scroll down
            if (itemsToDisplay.Count >= viewIndex + 16 && DataLoader.sDataLoader.buttonsTex != null && DataLoader.sDataLoader.buttonsTex.Length > 28)
            {
                Texture2D downArrow = DataLoader.sDataLoader.buttonsTex[28];
                if (downArrow != null)
                {
                    GUI.DrawTexture(new Rect(scrollIndicatorX + downArrow.width * 3 + 6, 256, 3 * downArrow.width, 3.6f * downArrow.height), downArrow);
                }
            }

            if (stack.Count > 0)
            {
                UUObject stackObj = stack[^1].GetComponent<UUObject>();
                int texIndex = (int)stackObj.type;
                if (stackObj.type <= EObjectType.GoldCoffer) ++texIndex; // open icon, for everything except the urn, quiver, bowl, and runebag
                Texture2D tex = DataLoader.sDataLoader.objTex[texIndex];
                GUI.DrawTexture(new Rect(Screen.width - invPosition + ix, iy - 64, 48, 58), tex);
                
                // TODO: find the "internals" box and draw it stretched out
            }

            // visible inventory
            int firstIndex = viewIndex;
            for (int i = firstIndex; i < Mathf.Min(itemsToDisplay.Count, firstIndex + 16); ++i)
            {
                UUObject invItem = itemsToDisplay[i];
                if (invItem != null)
                {
                    Texture2D tex = invItem.GetInventoryTex();
                    int p = i - firstIndex;
                    float cx = Screen.width - invPosition + ix + dx * (p % 4);
                    float cy = iy + dy * (p / 4);
                    GUI.DrawTexture(new Rect(cx, cy, 48, 58), tex);
                }
            }

            for (int slotIndex = 0; slotIndex < 11; ++slotIndex)
            {
                int i = (int)slotDrawOrder[slotIndex];
                UUObject obj = invSlotContents[i];
                if (obj != null)
                {
                    if ((obj.getClass == UUObject.EClass.Armour || obj.getClass == UUObject.EClass.Armour2) && (int)obj.type < 51) // rings and shields above this
                    {
                        int armorType = (int)obj.type & 31;
                        int armorTexIndex;
                        if (armorType < 15)
                        {
                            armorTexIndex = armorType + 15 * Mathf.Clamp(obj.GetQualityIndex() - 1, 0, 3);
                        }
                        else
                        {
                            armorTexIndex = armorType - 47 + 60;
                        }

                        if ((int)obj.type >= 47) // dragonskin boots, crowns
                        {
                            armorTexIndex = (int)obj.type + 60 - 47;
                        }

                        Texture2D tex = PlayerData.sData.female
                            ? DataLoader.sDataLoader.armor_fTex[armorTexIndex]
                            : DataLoader.sDataLoader.armor_mTex[armorTexIndex];

                        float x = Screen.width - invPosition + 3 * invSlots[i].ax;
                        float y = 30 + 3.6f * invSlots[i].ay;
                        GUI.DrawTexture(new Rect(x, y, 3 * tex.width, 3.6f * tex.height), tex);
                    }
                    else
                    {
                        Texture2D tex = obj.GetInventoryTex();
                        float x = Screen.width - invPosition + 3 * invSlots[i].cx;
                        float y = 30 + 3.6f * invSlots[i].cy;
                        GUI.DrawTexture(new Rect(x, y, 3 * tex.width, 3.6f * tex.height), tex);
                    }
                }
            }

            // attack and defence ratings
            {
                int defence = PlayerObject.Player.GetDefence();
                EInvSlot shieldHandSlot = PlayerData.sData.leftHanded ? EInvSlot.RightHand : EInvSlot.LeftHand;
                float x = Screen.width - invPosition + 3 * invSlots[(int)shieldHandSlot].cx;
                float y = 30 + 3.6f * invSlots[(int)shieldHandSlot].cy;
                GUI.Label(new Rect(x, y + 40, 20, 20), defence.ToString(), carryStyle);
            }
            {
                int attack = 0;
                WeaponBase weapon = invSlotContents[(int)(PlayerData.sData.leftHanded ? EInvSlot.LeftHand : EInvSlot.RightHand)] as WeaponBase;
                if (weapon == null)
                {
                    weapon = fist;
                }
                if (weapon != null)
                {
                    attack = weapon.GetMaxDamage();
                }
                EInvSlot swordHandSlot = PlayerData.sData.leftHanded ? EInvSlot.LeftHand : EInvSlot.RightHand;
                float x = Screen.width - invPosition + 3 * invSlots[(int)swordHandSlot].cx;
                float y = 30 + 3.6f * invSlots[(int)swordHandSlot].cy;
                GUI.Label(new Rect(x, y + 40, 20, 20), attack.ToString(), carryStyle);
            }

            // cursor: mouse/kb scheme follows pointer (drop target); gamepad uses index / equipSlot
            float panelLeftForCursor = Screen.width - invPosition;
            bool mouseInventoryCursor = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard
                && Mouse.current != null
                && (Conversations.runningConversation != null || currentArea != EInventoryArea.Trading);

            if (mouseInventoryCursor)
            {
                Vector2 guiMouse = GuiInput.ScreenToGuiMouse(Mouse.current.position.ReadValue());
                if (Conversations.runningConversation != null
                    && TryGetTradeSlotUnderMouse(guiMouse, out int tradeHoverSlot)
                    && IsPlayerTradeSlotIndex(tradeHoverSlot))
                {
                    Rect tr = GetTradeSlotCellRect(tradeHoverSlot);
                    GUI.DrawTexture(tr, cursor);
                }
                else if (IsGuiMouseOverInventoryPanel(guiMouse, invPosition))
                {
                    if (TryGetPaperdollSlotUnderMouse(guiMouse, panelLeftForCursor, out int pdSlot, out bool handsSecond))
                    {
                        bool showPaperdollCursor = true;
                        UUObject pdPortable = mouseCursorCarriedPortable ?? usingItem;
                        if (pdPortable != null)
                        {
                            showPaperdollCursor = PaperdollSlotAcceptsFloatingEquip(pdPortable, pdSlot)
                                || (CanCraftInInventory()
                                    && invSlotContents[pdSlot] != null
                                    && TryGetIncenseBurnPair(pdPortable, invSlotContents[pdSlot], out _));
                        }

                        if (showPaperdollCursor)
                        {
                            float px = Screen.width - invPosition + 3 * invSlots[pdSlot].cx;
                            float py = 30 + 3.6f * invSlots[pdSlot].cy;
                            if (handsSecond)
                                px += 72;
                            GUI.DrawTexture(new Rect(px, py, 48, 58), cursor);
                        }
                    }
                    else if (mouseCursorCarriedPortable != null
                             && inventoryInsertCursor != null
                             && TryGetStuffListInsertPreviewCenter(guiMouse, panelLeftForCursor, out Vector2 insertCtr))
                    {
                        float iw = 3f * inventoryInsertCursor.width;
                        float ih = 3.6f * inventoryInsertCursor.height;
                        GUI.DrawTexture(new Rect(insertCtr.x - iw * 0.5f, insertCtr.y - ih * 0.5f, iw, ih), inventoryInsertCursor);
                    }
                    else if (TryHitStuffGridForWorldPickup(guiMouse, panelLeftForCursor, out int listIndex))
                    {
                        int p = listIndex - viewIndex;
                        if (p >= 0 && p < 16)
                        {
                            float x = Screen.width - invPosition + ix + dx * (p % 4);
                            float y = iy + dy * (p / 4);
                            GUI.DrawTexture(new Rect(x, y, 48, 58), cursor);
                        }
                    }
                }
            }
            else
            {
                switch (currentArea)
                {
                case EInventoryArea.Paperdoll:
                    GUI.DrawTexture(
                        new Rect(Screen.width - invPosition + 3 * invSlots[equipSlot].cx,
                            30 + 3.6f * invSlots[equipSlot].cy, 48, 58), cursor);
                    if (equipSlot == (int)EInvSlot.Hands)
                    {
                        GUI.DrawTexture(
                            new Rect(Screen.width - invPosition + 3 * invSlots[equipSlot].cx + 72,
                                30 + 3.6f * invSlots[equipSlot].cy, 48, 58), cursor);
                    }
                    break;
                case EInventoryArea.Stuff:
                    if (index != -1)
                    {
                        float x = Screen.width - invPosition + ix + dx * (index % 4);
                        float y = iy + dy * ((index - viewIndex) / 4);
                        GUI.DrawTexture(new Rect(x, y, 48, 58), cursor);

                        if (usingItem != null && mouseCursorCarriedPortable == null)
                        {
                            Texture2D tex = usingItem.GetInventoryTex();
                            GUI.DrawTexture(new Rect(x + 32, y - 1, 2 * tex.width, 2.4f * tex.height), tex);
                        }
                    }
                    break;
                }
            }
            
            // quantities

            // draw quantity on top of the cursor
            for (int i = firstIndex; i < Mathf.Min(itemsToDisplay.Count, firstIndex + 16); ++i)
            {
                UUObject invItem = itemsToDisplay[i];
                if (invItem != null && invItem.quantity > 1)
                {
                    int p = i - firstIndex;
                    float cx = Screen.width - invPosition + ix + dx * (p % 4);
                    float cy = iy + dy * (p / 4);

                    descriptionStyle.alignment = TextAnchor.UpperRight;
                    GUI.Label(new Rect(cx + 20, cy, 30, 10), invItem.quantity.ToString(), descriptionStyle);
                    descriptionStyle.alignment = TextAnchor.UpperLeft;
                }
            }

            bool mouseInventoryUi = GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard
                && Mouse.current != null
                && (Conversations.runningConversation != null || currentArea != EInventoryArea.Trading);

            UUObject hoverDesc = null;
            bool hoverPaperdoll = false;
            int hoverPaperdollSlot = -1;

            Vector2 guiMouseDesc = Mouse.current != null
                ? GuiInput.ScreenToGuiMouse(Mouse.current.position.ReadValue())
                : Vector2.zero;
            float panelLeftDesc = Screen.width - invPosition;
            int tradeHoverDescSlot = -1;
            bool hoverTradeTray = Conversations.runningConversation != null
                && TryGetTradeSlotUnderMouse(guiMouseDesc, out tradeHoverDescSlot)
                && tradeHoverDescSlot >= 0
                && tradeSlots[tradeHoverDescSlot] != null;

            // Match Update() hybrid panel input: pointer can be over the paperdoll while LastActiveDevice is still Gamepad; hover must still resolve so RMB hints match actions.
            bool inventoryPointerHover = Mouse.current != null
                && (Conversations.runningConversation != null || currentArea != EInventoryArea.Trading)
                && (hoverTradeTray || IsGuiMouseOverInventoryPanel(guiMouseDesc, invPosition));

            if (inventoryPointerHover)
            {
                if (!hoverTradeTray)
                {
                    if (TryGetPaperdollSlotUnderMouse(guiMouseDesc, panelLeftDesc, out int pdSlot, out _))
                    {
                        if (pdSlot >= 0 && pdSlot < invSlotContents.Length && invSlotContents[pdSlot] != null)
                        {
                            hoverDesc = invSlotContents[pdSlot];
                            hoverPaperdoll = true;
                            hoverPaperdollSlot = pdSlot;
                        }
                    }
                    else if (TryGetStuffGridCellListIndex(guiMouseDesc, panelLeftDesc, out int cellIdx)
                             && cellIdx >= 0 && cellIdx < itemsToDisplay.Count)
                    {
                        hoverDesc = itemsToDisplay[cellIdx];
                    }
                }
                else
                {
                    hoverDesc = tradeSlots[tradeHoverDescSlot];
                }

                if (hoverDesc != null)
                {
                    if (hoverPaperdoll)
                    {
                        descriptionStyle.alignment = TextAnchor.LowerLeft;
                        Utils.DropShadowText(hoverDesc.GetLookName(), Screen.width - invPosition + ix, 10, 240, 40,
                            descriptionStyle);
                        descriptionStyle.alignment = TextAnchor.UpperLeft;
                    }
                    else if (hoverTradeTray)
                    {
                        Utils.DropShadowText(hoverDesc.GetLookName(),
                            ((tradeHoverDescSlot & 3) >= 2 ? x2 : x1) - 22, y1 + 121, 240, 40, descriptionStyle);
                    }
                    else
                    {
                        Utils.DropShadowText(hoverDesc.GetLookName(), Screen.width - invPosition + ix, iy + 250, 240,
                            20, descriptionStyle);
                    }
                }
            }

            switch (currentArea)
            {
            case EInventoryArea.Stuff:
                if (!mouseInventoryUi && index >= 0 && index < itemsToDisplay.Count)
                {
                    Utils.DropShadowText(itemsToDisplay[index].GetLookName(), Screen.width - invPosition + ix, iy + 250,
                        240, 20, descriptionStyle);
                }

                {
                    float x = Screen.width - invPosition + 20;
                    float y = 590;
                    if (holdTime > 1.8f && mouseInventoryUi && inventoryPointerHover && hoverDesc != null
                        && Conversations.runningConversation == null)
                    {
                        UUObject hintObj = hoverDesc;
                        UUObject carried = mouseCursorCarriedPortable;
                        float rowMouseLr = y;

                        string lookMoveStuff = null;
                        string useText = null;
                        bool showLmbHint = false;
                        bool showRmbHint = false;

                        if (carried != null && hoverPaperdoll)
                        {
                            if (TryGetMousePrimaryHint(carried, hintObj, hoverPaperdollSlot, true, out lookMoveStuff))
                            {
                                showLmbHint = true;
                            }
                        }
                        else if (carried != null && !hoverPaperdoll)
                        {
                            if (TryGetIncenseBurnPair(carried, hintObj, out _) && CanCraftInInventory())
                            {
                                lookMoveStuff = "Burn";
                                showLmbHint = true;
                            }
                            else if (hintObj.getClass == UUObject.EClass.Containers)
                            {
                                if (IsRuneBagDirectStowTarget(hintObj, carried)
                                    && ValidatePutUsingItemInContainer(hintObj, carried, playFeedback: false))
                                {
                                    lookMoveStuff = "Stow";
                                }
                                else
                                {
                                    lookMoveStuff = "Open";
                                    if (CanMouseStoreCarriedInContainer(hintObj, carried))
                                    {
                                        useText = "Store";
                                    }
                                }

                                showLmbHint = true;
                                showRmbHint = !string.IsNullOrEmpty(useText);
                            }
                            else
                            {
                                lookMoveStuff = $"{hintObj.GetLookText()}/Move";
                                showLmbHint = true;
                                showRmbHint = hintObj.SupportsStuffGridMouseSecondaryUse
                                    && TryGetStuffSecondaryUseHint(hintObj, false, out useText);
                            }
                        }
                        else
                        {
                            if (TryGetMousePrimaryHint(null, hintObj, hoverPaperdollSlot, hoverPaperdoll,
                                    out lookMoveStuff))
                            {
                                showLmbHint = true;
                            }

                            if (hoverPaperdoll)
                            {
                                useText = "Unequip";
                                showRmbHint = true;
                            }
                            else if (hintObj.SupportsStuffGridMouseSecondaryUse
                                     && TryGetStuffSecondaryUseHint(hintObj, false, out useText))
                            {
                                showRmbHint = true;
                            }
                        }

                        if (showLmbHint)
                        {
                            if (inventoryMouseLeftHint != null)
                            {
                                DrawTex(x, rowMouseLr, 24f, 24f, inventoryMouseLeftHint);
                            }

                            GUI.Label(new Rect(x + 28f, rowMouseLr, 98f, 20f), lookMoveStuff, descriptionStyle);
                        }

                        if (showRmbHint && !string.IsNullOrEmpty(useText))
                        {
                            float useCol = x + 120f;
                            if (inventoryMouseRightHint != null)
                            {
                                DrawTex(useCol, rowMouseLr, 24f, 24f, inventoryMouseRightHint);
                            }

                            GUI.Label(new Rect(useCol + 28f, rowMouseLr, 140f, 20f), useText, descriptionStyle);
                        }
                    }
                    else if (holdTime > 1.8f && !mouseInventoryUi)
                    {
                        if (GetCurrentItem() != null)
                        {
                            UUObject currentItem = GetCurrentItem();
                            bool hideLightSourceHintDuringTrade = Conversations.runningConversation != null
                                && currentItem is LightSource;
                            string lookText = currentItem.GetLookText();
                            if (!string.IsNullOrEmpty(lookText) && !hideLightSourceHintDuringTrade)
                            {
                                DrawTex(x + 80, y + 48, 24, 24, aButton);
                                GUI.Label(new Rect(x + 110, y + 48, 100, 20), lookText, descriptionStyle);
                            }
                            if (Conversations.runningConversation == null)
                            {
                                DrawTex(x + 40, y + 24, 24, 24, xButton);
                                DrawXButtonChargeRingOverlay(new Rect(x + 40, y + 24, 24, 24));
                                UUObject throwHintItem = GetCurrentItem();
                                if (throwHintItem.IsRepairable() && UUObject.FindAnvil())
                                {
                                    GUI.Label(new Rect(x + 70, y + 24, 100, 20), "Repair", descriptionStyle);
                                }
                                else
                                {
                                    string chargeVerb = throwHintItem.GetGamepadChargeThrowVerbOrNull();
                                    if (!string.IsNullOrEmpty(chargeVerb))
                                    {
                                        GUI.Label(new Rect(x + 70, y + 24, 100, 20), chargeVerb, descriptionStyle);
                                    }
                                    else
                                    {
                                        GUI.Label(new Rect(x + 70, y + 24, 100, 20), "Throw", descriptionStyle);
                                    }
                                }
                            }
                            if (TryGetStuffSecondaryUseHint(GetCurrentItem(), hoverPaperdoll: false, out string useText))
                            {
                                DrawTex(x + 80, y, 24, 24, yButton);
                                if (Conversations.runningConversation == null)
                                {
                                    DrawYButtonUseChargeRingOverlay(new Rect(x + 80, y, 24, 24));
                                }
                                GUI.Label(new Rect(x + 110, y, 100, 20), useText, descriptionStyle);
                            }
                        }
                        if (stack.Count > 0)
                        {
                            DrawTex(x + 120, y + 24, 24, 24, bButton);
                            GUI.Label(new Rect(x + 150, y + 24, 100, 20), "Close", descriptionStyle);
                        }
                    }
                }
                break;
            case EInventoryArea.Paperdoll:
                // name the item
                if (!mouseInventoryUi && invSlotContents[equipSlot] != null)
                {
                    descriptionStyle.alignment = TextAnchor.LowerLeft;
                    Utils.DropShadowText(invSlotContents[equipSlot].GetLookName(), Screen.width - invPosition + ix, 10,
                        240, 40, descriptionStyle);
                    descriptionStyle.alignment = TextAnchor.UpperLeft;
                    if (holdTime > 1.8f)
                    {
                        float x = Screen.width - invPosition + 20;
                        float y = 590;
                        UUObject pdObj = invSlotContents[equipSlot];
                        if (pdObj is LightSource && Conversations.runningConversation == null)
                        {
                            string lookText = pdObj.GetLookText();
                            if (!string.IsNullOrEmpty(lookText))
                            {
                                DrawTex(x + 80, y + 48, 24, 24, aButton);
                                GUI.Label(new Rect(x + 110, y + 48, 100, 20), lookText, descriptionStyle);
                            }
                        }

                        DrawTex(x + 80, y, 24, 24, yButton);
                        GUI.Label(new Rect(x + 110, y, 100, 20), "Unequip", descriptionStyle);
                    }
                }
                if (mouseInventoryUi && inventoryPointerHover && hoverPaperdoll && hoverDesc != null && holdTime > 1.8f
                    && Conversations.runningConversation == null)
                {
                    float px = Screen.width - invPosition + 20;
                    float py = 590;
                    UUObject carried = mouseCursorCarriedPortable;
                    string lookMovePaper = null;
                    bool showLmbHint = false;
                    bool showRmbHint = false;
                    string useHint = null;

                    if (carried != null)
                    {
                        if (TryGetMousePrimaryHint(carried, hoverDesc, hoverPaperdollSlot, true, out lookMovePaper))
                        {
                            showLmbHint = true;
                        }
                    }
                    else
                    {
                        if (TryGetMousePrimaryHint(null, hoverDesc, hoverPaperdollSlot, true, out lookMovePaper))
                        {
                            showLmbHint = true;
                        }

                        useHint = "Unequip";
                        showRmbHint = true;
                    }

                    if (showLmbHint)
                    {
                        if (inventoryMouseLeftHint != null)
                        {
                            DrawTex(px, py, 24f, 24f, inventoryMouseLeftHint);
                        }

                        GUI.Label(new Rect(px + 28f, py, 98f, 20f), lookMovePaper, descriptionStyle);
                    }

                    if (showRmbHint)
                    {
                        float useCol = px + 120f;
                        if (inventoryMouseRightHint != null)
                        {
                            DrawTex(useCol, py, 24f, 24f, inventoryMouseRightHint);
                        }

                        GUI.Label(new Rect(useCol + 28f, py, 140f, 20f), useHint, descriptionStyle);
                    }
                }
                break;
            
            case EInventoryArea.Trading:
                // name the highlighted item
                if (tradeSlots[tradeSlot] != null)
                {
                    Utils.DropShadowText(tradeSlots[tradeSlot].GetLookName(), ((tradeSlot & 3) >= 2 ? x2 : x1) - 22, y1 + 121, 240, 40, descriptionStyle);
                }
                break;
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                && GameInput.LastActiveDevice == GameInputDevice.MouseKeyboard
                && Mouse.current != null
                && !IsInventoryModalUiBlocking(PlayerObject.Player)
                && !MapScreen.IsMapScreenVisible()
                && (Conversations.runningConversation != null || currentArea != EInventoryArea.Trading))
            {
                bool carryingPortableMouseDown = mouseCursorCarriedPortable != null;
                if (carryingPortableMouseDown && floatingPlaceMouseDownHandledFrame == Time.frameCount)
                {
                    Event.current.Use();
                }
                else
                {
                    Vector2 guiMouse = Event.current.mousePosition;
                    bool handledClick = TryHandleInventoryScrollClick(guiMouse, invPosition, itemsToDisplay.Count)
                        || TryHandleOpenContainerCloseClick(guiMouse, invPosition);
                    if (!handledClick && TryHandleFloatingUsingItemClick(guiMouse, invPosition))
                    {
                        suppressStuffMousePrimaryOnNextLeftRelease = true;
                        suppressInventoryMouseArmAfterFloatingPlace = true;
                        handledClick = true;
                    }

                    if (handledClick)
                    {
                        Event.current.Use();
                        if (carryingPortableMouseDown)
                        {
                            floatingPlaceMouseDownHandledFrame = Time.frameCount;
                        }
                    }
                }
            }

            if (Event.current.type == EventType.ScrollWheel
                && currentArea == EInventoryArea.Stuff
                && !IsInventoryModalUiBlocking(PlayerObject.Player)
                && !MapScreen.IsMapScreenVisible())
            {
                float panelLeft = Screen.width - invPosition;
                Rect stuffGridRect = GetStuffGridHitRect(panelLeft);
                if (stuffGridRect.Contains(Event.current.mousePosition))
                {
                    float scrollY = Event.current.delta.y;
                    if (Mathf.Abs(scrollY) > 0.01f)
                    {
                        int rowDelta = scrollY > 0f ? -1 : 1;
                        if (TryApplyStuffViewScroll(rowDelta, itemsToDisplay.Count))
                        {
                            Event.current.Use();
                        }
                    }
                }
            }
        }

#if false
       for (int i = 0; i < 64; ++i)
       {
           Texture2D tex = DataLoader.sDataLoader.armor_fTex[i];
           int x = 80 * (i % 15);
           int y = 200 * (i / 15);
           GUI.DrawTexture(new Rect(x, y, 3*tex.width, 3*tex.height), tex);
       }
#endif
    }
    
    public int y1 = 56;
    public int ddx = 63;
    public int ddy = 65;
    public int xo1 = -12;
    public int xo2 = 52;
    public int yo1 = 23;
}
