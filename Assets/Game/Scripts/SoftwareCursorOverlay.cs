using UnityEngine;

/// <summary>Where <see cref="SoftwareCursorOverlay"/> anchors the drawn texture relative to the GUI position.</summary>
public enum SoftwareCursorHotspot
{
    /// <summary>Texture center at <c>(guiX, guiY)</c> (default).</summary>
    Center,
    /// <summary>Bottom-left corner of the texture at <c>(guiX, guiY)</c> (map quill).</summary>
    BottomLeft,
}

/// <summary>
/// Draws the gameplay pointer in IMGUI: OS cursor stays hidden; we paint <see cref="DataLoader.cursorTex"/> [0]
/// by default, optional <see cref="DefaultTextureOverride"/>, map quill when the map is open (before world-pickup drag),
/// world-pickup item art,
/// or nothing while mouselook (<see cref="CursorLockMode.Locked"/>) except a primed spell's pointer, at the centre.
/// Save/load and keyboard (options) UI force the neutral arrow only (<see cref="DefaultTextureOverride"/> ignored),
/// except map note editing keeps the map quill while <see cref="MapScreen"/> is open.
/// </summary>
[DefaultExecutionOrder(50)]
public class SoftwareCursorOverlay : MonoBehaviour
{
    /// <summary>
    /// When non-null, drawn instead of <see cref="DataLoader.cursorTex"/>[0] for the default pointer.
    /// </summary>
    public static Texture2D DefaultTextureOverride;

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        GUI.depth = (int)EGUIDepth.SoftwareCursorOverlay;

        if (IsSaveLoadOrKeyboardModalUiBlockingAlternateCursors())
        {
            if (MapScreen.IsMapScreenVisible() && TryDrawMapQuill())
            {
                return;
            }

            TryDrawNeutralDefaultSoftwareCursor();
            return;
        }

        if (MapScreen.IsMapScreenVisible())
        {
            if (TryDrawMapQuill())
            {
                return;
            }
        }

        if (TryDrawWorldPickupDrag())
        {
            return;
        }

        TryDrawDefaultSoftwareCursor();
    }

    private static bool IsSaveLoadOrKeyboardModalUiBlockingAlternateCursors()
    {
        PlayerObject p = PlayerObject.Player;
        if (p == null)
        {
            return false;
        }

        return (p.controlsDisabled & (EControlMask.SaveLoad | EControlMask.Keyboard)) != 0;
    }

    /// <summary>
    /// Draw <paramref name="tex"/> at GUI-space <paramref name="guiX"/>, <paramref name="guiY"/> using <paramref name="hotspot"/>.
    /// Default hotspot is <see cref="SoftwareCursorHotspot.Center"/>.
    /// </summary>
    public static void DrawTextureWithHotspot(
        Texture2D tex,
        float guiX,
        float guiY,
        float width,
        float height,
        SoftwareCursorHotspot hotspot = SoftwareCursorHotspot.Center)
    {
        if (tex == null)
        {
            return;
        }

        switch (hotspot)
        {
            case SoftwareCursorHotspot.Center:
                UiScale.DrawTexture(new Rect(guiX - width * 0.5f, guiY - height * 0.5f, width, height), tex);
                break;
            case SoftwareCursorHotspot.BottomLeft:
                UiScale.DrawTexture(new Rect(guiX, guiY - height, width, height), tex);
                break;
        }
    }

    private static bool TryDrawMapQuill()
    {
        if (!MapScreen.TryGetSoftwareCursorQuill(out Texture2D tex, out float guiX, out float guiY, out float w, out float h))
        {
            return false;
        }

        DrawTextureWithHotspot(tex, guiX, guiY, w, h, SoftwareCursorHotspot.BottomLeft);
        return true;
    }

    private bool TryDrawWorldPickupDrag()
    {
        Inventory inv = Inventory.sInv;
        if (inv == null || inv.mouseCursorCarriedPortable == null)
        {
            return false;
        }

        UUObject item = inv.mouseCursorCarriedPortable;
        if (item == null)
        {
            return false;
        }

        Texture2D tex = item.GetInventoryTex();
        if (tex == null)
        {
            return false;
        }

        float w = UiScale.S(3f * tex.width);
        float h = UiScale.S(3.6f * tex.height);
        // Locked (left Shift), the mouse position only catches up when the mouse moves: the item goes at once
        // where the mouse is put back when Shift is let go, so it does not move either way.
        Vector2 mp = Cursor.lockState == CursorLockMode.Locked
            ? GuiInput.ScreenToGuiMouse(PointerOnCrosshairScreen())
            : GuiInput.MousePositionGuiSpace;
        if (inv.TryGetMouseCursorThrowChargeFill(out float throwFill))
        {
            float ringSize = Mathf.Max(w, h);
            inv.DrawThrowChargeRingForFill(
                new Rect(mp.x - ringSize * 0.5f, mp.y - ringSize * 0.5f, ringSize, ringSize),
                throwFill);
        }

        DrawTextureWithHotspot(tex, mp.x, mp.y, w, h, SoftwareCursorHotspot.Center);

        if (item.quantity > 1)
        {
            inv.descriptionStyle.alignment = TextAnchor.UpperRight;
            GUI.Label(new Rect(mp.x - w * 0.5f + w - UiScale.S(40f), mp.y - h * 0.5f, UiScale.S(36f), UiScale.S(18f)), item.quantity.ToString(),
                UiScale.Style(inv.descriptionStyle));
            inv.descriptionStyle.alignment = TextAnchor.UpperLeft;
        }

        return true;
    }

    private static void TryDrawDefaultSoftwareCursor()
    {
        TryDrawDefaultSoftwareCursorCore(allowDefaultTextureOverride: true);
    }

    private static void TryDrawNeutralDefaultSoftwareCursor()
    {
        TryDrawDefaultSoftwareCursorCore(allowDefaultTextureOverride: false);
    }

    private static void TryDrawDefaultSoftwareCursorCore(bool allowDefaultTextureOverride)
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            TryDrawLockedOverrideAtCentre(allowDefaultTextureOverride);
            return;
        }

        if (GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
        {
            return;
        }

        Texture2D tex = null;
        if (allowDefaultTextureOverride)
        {
            tex = DefaultTextureOverride;
        }

        if (tex == null)
        {
            DataLoader dl = DataLoader.sDataLoader;
            if (dl?.cursorTex == null || dl.cursorTex.Length == 0 || dl.cursorTex[0] == null)
            {
                return;
            }

            tex = dl.cursorTex[0];
        }

        Vector2 mp = GuiInput.MousePositionGuiSpace;
        if (tex == DefaultTextureOverride)
        {
            DrawOnCross(tex, mp.x, mp.y, PointerScaleX);
            return;
        }

        float w = UiScale.S(3f * tex.width);
        float h = UiScale.S(3.6f * tex.height);
        DrawTextureWithHotspot(tex, mp.x, mp.y, w, h, SoftwareCursorHotspot.Center);
    }

    /// <summary>
    /// With the cursor locked over a primed spell (left Shift, see <see cref="PlayerInput.PanelMouseLookHeld"/>),
    /// the spell's pointer stays on screen, drawn over the centre crosshair, so the aim still reads as a spell.
    /// </summary>
    private static void TryDrawLockedOverrideAtCentre(bool allowDefaultTextureOverride)
    {
        Texture2D tex = allowDefaultTextureOverride ? DefaultTextureOverride : null;
        if (tex == null || GameInput.LastActiveDevice != GameInputDevice.MouseKeyboard)
        {
            return;
        }

        DrawOnCross(tex, Screen.width * 0.5f, Screen.height * 0.5f, CrosshairScaleX);
    }

    /// <summary>Reference pixels (UiScale) to a texel across of the centre crosshair, as <see cref="Interaction"/> draws it.</summary>
    private const float CrosshairScaleX = 5f;

    /// <summary>Reference pixels to a texel across of the free pointer, as drawn here.</summary>
    private const float PointerScaleX = 3f;

    /// <summary>
    /// Moves the mouse so that the free pointer's cross lands on the centre crosshair's: the end of the left Shift
    /// mouselook (<see cref="GameplayCursorPolicy.Apply"/>), so the pointer, or a primed spell's circle, comes back
    /// exactly where the crosshair was. Both crosses sit half a texel left of their drawn middle.
    /// </summary>
    public static void WarpPointerOntoCrosshair()
    {
        var mouse = GameInput.CurrentMouse;
        if (mouse == null)
        {
            return;
        }

        mouse.WarpCursorPosition(PointerOnCrosshairScreen());
    }

    /// <summary>
    /// The mouse position, in screen pixels, at which the free pointer's cross lies on the centre crosshair's
    /// (<see cref="WarpPointerOntoCrosshair"/>): 1 px left of the centre.
    /// </summary>
    private static Vector2 PointerOnCrosshairScreen()
    {
        float x = Screen.width * 0.5f - UiScale.S(0.5f * CrosshairScaleX - 0.5f * PointerScaleX);
        return new Vector2(Mathf.Round(x), Mathf.Round(Screen.height * 0.5f));
    }


    /// <summary>
    /// Draws <paramref name="tex"/> centred on the cross of cursor 0 as it is drawn centred on
    /// (<paramref name="guiX"/>, <paramref name="guiY"/>), <paramref name="crossScaleX"/> pixels to a texel across:
    /// 3 for the free pointer here, 5 for the crosshair in <see cref="Interaction"/>. Cursor 0 (16x15) has its cross
    /// on column 7, half a texel left of the bitmap's middle, so a primed spell's circle centred on the same point
    /// sat off the cross. Placed on whole pixels.
    /// </summary>
    private static void DrawOnCross(Texture2D tex, float guiX, float guiY, float crossScaleX)
    {
        float w = UiScale.S(3f * tex.width);
        float h = UiScale.S(3.6f * tex.height);
        float x = Mathf.Round(guiX - UiScale.S(0.5f * crossScaleX) - w * 0.5f);
        float y = Mathf.Round(guiY - h * 0.5f);
        UiScale.DrawTexture(new Rect(x, y, w, h), tex);
    }
}
