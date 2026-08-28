using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// IMGUI helpers for mouse hit-testing (Unity GUI uses top-left origin; Input System mouse is bottom-left).
/// Modal panels register blocking rects in <see cref="OnGUI"/>; world code checks <see cref="BlocksPointer"/> /
/// <see cref="ShouldSuppressWorldMousePrimary"/> after GUI has run (e.g. in <see cref="Interaction"/> LateUpdate).
/// Convention for new panels: <see cref="RegisterBlockingRect"/> for the full panel, widget clicks via
/// <see cref="TryConsumeClickInRect"/>, then <see cref="TryConsumeClickInPanel"/> for background clicks.
/// </summary>
public static class GuiInput
{
    private static readonly List<Rect> blockingRects = new List<Rect>(16);
    private static bool primaryClickConsumed;

    /// <summary>Pointer was over a registered modal at end of the previous frame (for Update raycasts before OnGUI).</summary>
    public static bool LastFrameBlocksPointer { get; private set; }

    public static void BeginFrame()
    {
        blockingRects.Clear();
        primaryClickConsumed = false;
    }

    /// <summary>Call from <see cref="GuiInputGate"/> after gameplay LateUpdate; caches hover block for next frame's Update.</summary>
    public static void EndFrame()
    {
        LastFrameBlocksPointer = BlocksPointer(MousePositionGuiSpace);
        blockingRects.Clear();
        primaryClickConsumed = false;
    }

    public static void RegisterBlockingRect(Rect rectGuiTopLeft)
    {
        if (rectGuiTopLeft.width > 0f && rectGuiTopLeft.height > 0f)
        {
            blockingRects.Add(rectGuiTopLeft);
        }
    }

    public static void RegisterPanelIfVisible(bool visible, Rect rectGuiTopLeft)
    {
        if (visible)
        {
            RegisterBlockingRect(rectGuiTopLeft);
        }
    }

    public static bool BlocksPointer(Vector2 guiMouse)
    {
        for (int i = 0; i < blockingRects.Count; ++i)
        {
            if (blockingRects[i].Contains(guiMouse))
            {
                return true;
            }
        }

        return false;
    }

    public static void MarkPrimaryClickConsumed()
    {
        primaryClickConsumed = true;
    }

    /// <summary>
    /// Pointer in GUI space for drawing overlays. Do not use <see cref="Event.current.mousePosition"/> for
    /// continuous positioning — during <see cref="EventType.Repaint"/> (and many non-mouse events) Unity sets it to (-10000,-10000).
    /// </summary>
    public static Vector2 MousePositionGuiSpace
    {
        get
        {
            Mouse m = Mouse.current;
            if (m != null)
            {
                return ScreenToGuiMouse(m.position.ReadValue());
            }

            return Event.current != null ? Event.current.mousePosition : Vector2.zero;
        }
    }

    public static Vector2 ScreenToGuiMouse(Vector2 screenPixelsBottomLeft)
    {
        // Match <see cref="UnityEngine.Device.Screen"/> used by map/UI that letterboxes to the camera rect.
        float h = UnityEngine.Device.Screen.height;
        return new Vector2(screenPixelsBottomLeft.x, h - screenPixelsBottomLeft.y);
    }

    public static bool IsLeftMouseDownGui =>
        Event.current != null
        && Event.current.type == EventType.MouseDown
        && Event.current.button == 0;

    /// <summary>Eat LMB down on panel chrome that has no widget handler.</summary>
    public static bool TryConsumeClickInPanel(Rect panelGuiTopLeft)
    {
        if (!IsLeftMouseDownGui || !panelGuiTopLeft.Contains(Event.current.mousePosition))
        {
            return false;
        }

        Event.current.Use();
        MarkPrimaryClickConsumed();
        return true;
    }

    public static bool TryConsumeClickInRect(Rect rectGuiTopLeft)
    {
        if (!IsLeftMouseDownGui || !rectGuiTopLeft.Contains(Event.current.mousePosition))
        {
            return false;
        }

        Event.current.Use();
        MarkPrimaryClickConsumed();
        return true;
    }

    /// <summary>Hit-test when <see cref="GUI.matrix"/> is not identity (e.g. conversation UI).</summary>
    public static bool TryConsumeClickInRect(Matrix4x4 guiMatrix, Rect rectInGuiSpace)
    {
        if (!IsLeftMouseDownGui)
        {
            return false;
        }

        Vector3 local = guiMatrix.inverse.MultiplyPoint(new Vector3(Event.current.mousePosition.x, Event.current.mousePosition.y, 0f));
        if (!rectInGuiSpace.Contains(new Vector2(local.x, local.y)))
        {
            return false;
        }

        Event.current.Use();
        MarkPrimaryClickConsumed();
        return true;
    }

    /// <summary>Same space as <see cref="TryConsumeClickInRect(Matrix4x4, Rect)"/> rects — use with <see cref="MousePositionGuiSpace"/>.</summary>
    public static bool PointInGuiMatrixRect(Matrix4x4 guiMatrix, Rect rectInGuiSpace, Vector2 guiMouseTopLeft)
    {
        Vector3 local = guiMatrix.inverse.MultiplyPoint(new Vector3(guiMouseTopLeft.x, guiMouseTopLeft.y, 0f));
        return rectInGuiSpace.Contains(new Vector2(local.x, local.y));
    }

    /// <summary>After OnGUI: block Input System world look/use on this frame's primary mouse button.</summary>
    public static bool ShouldSuppressWorldMousePrimary()
    {
        if (primaryClickConsumed)
        {
            return true;
        }

        Mouse m = Mouse.current;
        if (m == null)
        {
            return false;
        }

        if (!m.leftButton.wasPressedThisFrame && !m.leftButton.wasReleasedThisFrame)
        {
            return false;
        }

        return BlocksPointer(MousePositionGuiSpace);
    }
}
