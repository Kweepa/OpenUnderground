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
/// or nothing while mouselook (<see cref="CursorLockMode.Locked"/>).
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
                GUI.DrawTexture(new Rect(guiX - width * 0.5f, guiY - height * 0.5f, width, height), tex);
                break;
            case SoftwareCursorHotspot.BottomLeft:
                GUI.DrawTexture(new Rect(guiX, guiY - height, width, height), tex);
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

        float w = 3f * tex.width;
        float h = 3.6f * tex.height;
        Vector2 mp = GuiInput.MousePositionGuiSpace;
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
            GUI.Label(new Rect(mp.x - w * 0.5f + w - 40f, mp.y - h * 0.5f, 36f, 18f), item.quantity.ToString(), inv.descriptionStyle);
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

        float w = 3f * tex.width;
        float h = 3.6f * tex.height;
        Vector2 mp = GuiInput.MousePositionGuiSpace;
        DrawTextureWithHotspot(tex, mp.x, mp.y, w, h, SoftwareCursorHotspot.Center);
    }
}
