using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A page of saves in the game menu: the list on the left, with a scroll bar when it is longer than
/// the panel, the preview of the selected save on the right, and, on the save page, the deleting of a
/// save after a question. SAVE and LOAD in a game (SaveLoadGUI) and LOAD on the title screen (TitleLoadScreen)
/// are each one of these, so the list looks and behaves the same from both.
/// </summary>
/// <remarks>
/// A plain class, held by the host of the menu, which decides what choosing a row does: saving over
/// it, loading it in a game, or loading it from the title screen. Laid out in reference pixels
/// (UiScale), inside the list and detail areas the menu hands its host.
/// </remarks>
public sealed class SaveListPage
{
    private readonly Func<GameMenu> menu;
    private readonly GUIStyle text;
    private readonly AudioClip deleteSound;

    // Every save on disk, newest first, plus the row that makes a new save when this is the save
    // page. The list is whatever is on disk, so it has no fixed length: the rows scroll, and
    // scroll is the first row drawn.
    private SaveGameManager.SaveSlotInfo[] saves = new SaveGameManager.SaveSlotInfo[0];
    private Texture2D[] screenshots = new Texture2D[0];
    private int index;
    private int scroll;

    // Whether the window follows the selection. The keys move the selection and the window
    // follows it; the wheel and the scroll bar move the window alone, and the selection may then
    // be out of view until it moves again.
    private bool scrollFollowsSelection = true;

    // The scroll bar, while its thumb is dragged.
    private bool draggingThumb;
    private float thumbGrab;
    // a row double clicked in the list: the next Update answers it as Enter
    private bool rowDoubleClicked;
    // Set by SelectTop(): the next Show() opens on the top row, not on the row the list in memory had.
    private bool showOnTop;

    private const float SlotRowHeight = 52f;
    private const float SlotFont = 32f;
    private const float SlotTextInset = 20f;
    private const float ScrollBarWidth = 14f;
    private const float PreviewFont = 26f;
    // the shape of the screenshots a save writes
    private const float ScreenshotAspect = 570f / 332f;

    private static readonly Color SelectColor = new Color32(255, 213, 64, 255);
    private static readonly Color UnselectColor = new Color32(187, 123, 1, 255);
    private static readonly Color DimColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);

    /// <param name="menu">The menu the page is drawn in, asked for when needed: the host makes it after the page.</param>
    /// <param name="text">The style whose font the rows and the preview are written in.</param>
    /// <param name="deleteSound">Played when a save is deleted, or null.</param>
    public SaveListPage(Func<GameMenu> menu, GUIStyle text, AudioClip deleteSound)
    {
        this.menu = menu;
        this.text = text;
        this.deleteSound = deleteSound;
    }

    /// <summary>True on the save page, which carries the row that makes a new save at the top.</summary>
    public bool ForSaving { get; private set; }

    public int Count => saves.Length;

    /// <summary>The selected row, or null when the list is empty.</summary>
    public SaveGameManager.SaveSlotInfo Selected => index >= 0 && index < saves.Length ? saves[index] : null;

    /// <summary>True when the selected row is the one that makes a new save.</summary>
    public bool SelectedIsNewSave => SaveUIHelper.IsNewSaveRow(saves, index);

    /// <summary>True when the selected row is a save on disk, which can be loaded or deleted.</summary>
    public bool SelectedIsSave => Selected != null && !string.IsNullOrEmpty(Selected.slotName);

    /// <summary>
    /// The page comes into view, as the save page or the load page: the list is made again, and
    /// what was selected stays selected.
    /// </summary>
    /// <remarks>
    /// What was selected is carried by slot name and not by row number: the save list hides the
    /// saves the game writes and carries the new save row at the top, so the same number is a
    /// different save on the other side. A save that has no row on the other side - a quicksave or
    /// an autosave, going from load to save - falls back to the top. After <see cref="SelectTop"/>
    /// nothing is carried over: the list in memory may be the load page's, whose top row is the
    /// newest save, and the save page then opened on that save instead of on the new save row.
    /// </remarks>
    public void Show(bool forSaving)
    {
        string wasSelected = SelectedIsSave && !showOnTop ? Selected.slotName : "";
        showOnTop = false;
        ForSaving = forSaving;
        index = 0;
        scroll = 0;
        scrollFollowsSelection = true;
        Refresh();
        index = SaveUIHelper.IndexOfSlot(saves, wasSelected, 0);
    }

    /// <summary>The list read again from disk, the selection kept on its row as far as it can.</summary>
    public void Refresh()
    {
        index = SaveUIHelper.RefreshSaves(ref saves, ref screenshots, index, ForSaving);
    }

    /// <summary>The page opens on the top row: the newest save, or the row that makes a new one.</summary>
    public void SelectTop()
    {
        index = 0;
        scroll = 0;
        scrollFollowsSelection = true;
        showOnTop = true;
    }

    /// <summary>
    /// The keys of the page: up and down move the selection, wrapping; the wheel scrolls; Delete on
    /// the keyboard and Y on the pad ask to delete the selected save. True when the
    /// player chose the selected row (A, Enter or a double click).
    /// </summary>
    public bool Update(GameMenu.Nav nav)
    {
        if (nav.Down)
        {
            MoveSelection(1);
        }
        else if (nav.Up)
        {
            MoveSelection(-1);
        }
        else if (nav.Wheel != 0)
        {
            // one row a notch, without moving the selection
            scroll = Mathf.Max(0, scroll + nav.Wheel);
            scrollFollowsSelection = false;
        }
        else if (nav.Accept || rowDoubleClicked)
        {
            rowDoubleClicked = false;
            return true;
        }
        else if ((Keyboard.current?.deleteKey.wasPressedThisFrame ?? false) || (Gamepad.current?.yButton.wasPressedThisFrame ?? false))
        {
            AskDelete();
        }
        return false;
    }

    private void MoveSelection(int delta)
    {
        if (saves.Length == 0)
        {
            index = 0;
            return;
        }

        index = ((index + delta) % saves.Length + saves.Length) % saves.Length;
        scrollFollowsSelection = true;
    }

    /// <summary>
    /// Asks, in the menu's Yes/No box with No lit, whether to delete the selected save, and deletes
    /// it on a yes: its file, its header and its screenshot. The row that makes a new save is not a
    /// save, and nothing is asked for it. On the lists for loading too (the user's rule of 7 November
    /// 2026), where the autosaves are listed; they are not on the save page.
    /// </summary>
    public void AskDelete()
    {
        if (!SelectedIsSave || SaveGameManager.sInstance == null)
        {
            return;
        }

        string slot = Selected.slotName;
        string name = string.IsNullOrEmpty(Selected.displayName) ? slot : Selected.displayName;
        menu().Ask("Delete \"" + name + "\"? It cannot be undone.", () =>
        {
            SaveGameManager.sInstance.DeleteSaveSlot(slot);
            if (deleteSound != null)
            {
                Utils.PlayClip2d(deleteSound);
            }
            // the row after the deleted one takes its place, or the one before at the end of the list
            Refresh();
            index = Mathf.Clamp(index, 0, Mathf.Max(0, saves.Length - 1));
            scrollFollowsSelection = true;
        }, true);
    }

    /// <summary>
    /// Keeps the window inside the list, and the selected row inside the window whenever it was
    /// the selection that moved.
    /// </summary>
    private void ClampScroll(int rows)
    {
        int maxScroll = Mathf.Max(0, saves.Length - rows);
        scroll = Mathf.Clamp(scroll, 0, maxScroll);

        if (scrollFollowsSelection)
        {
            if (index < scroll)
            {
                scroll = index;
            }
            else if (index >= scroll + rows)
            {
                scroll = index - rows + 1;
            }

            scroll = Mathf.Clamp(scroll, 0, maxScroll);
        }
    }

    /// <summary>The list of saves on the left, with a scroll bar when it is longer than the panel, and the preview of the selected one.</summary>
    public void Draw(Rect list, Rect detail)
    {
        float rowsTop = list.y + 8f;
        int rows = Mathf.Max(1, Mathf.FloorToInt((list.height - 16f) / SlotRowHeight));
        ClampScroll(rows);
        bool scrolls = saves.Length > rows;
        Rect bar = new Rect(list.xMax - 8f - ScrollBarWidth, rowsTop, ScrollBarWidth, rows * SlotRowHeight);
        // the rows stop short of the bar, so the selection never lies under it
        float rowsRight = scrolls ? bar.x - 8f : list.xMax - 8f;

        // plain, whatever the source style carries: a name is not markup, and the user found the rows looking bold
        GUIStyle rowStyle = new GUIStyle(text)
        {
            fontSize = Mathf.RoundToInt(SlotFont), alignment = TextAnchor.MiddleLeft, wordWrap = false,
            fontStyle = FontStyle.Normal, richText = false,
        };

        if (saves.Length == 0)
        {
            rowStyle.normal.textColor = DimColor;
            GUI.Label(UiScale.R(list.x + 8f + SlotTextInset, rowsTop, list.width - 16f, SlotRowHeight), "No saved games", UiScale.Style(rowStyle));
        }

        bool asking = menu().Asking;

        // Rows are clicked to be chosen; hovering does not move the selection, nor the preview.
        for (int row = 0; row < rows; ++row)
        {
            int i = scroll + row;
            if (i >= saves.Length)
            {
                break;
            }

            Rect r = new Rect(list.x + 8f, rowsTop + row * SlotRowHeight, rowsRight - list.x - 8f, SlotRowHeight);
            bool isNewSaveRow = SaveUIHelper.IsNewSaveRow(saves, i);
            bool isSelected = index == i;
            if (isSelected)
            {
                Color previous = GUI.color;
                GUI.color = new Color(1f, 0.84f, 0.25f, 0.22f);
                GUI.DrawTexture(UiScale.R(r), Texture2D.whiteTexture);
                GUI.color = previous;
            }

            // Selected rows are bright, the rest dim, and the row that makes a new save is dimmer
            // still: it is an action rather than a save. Written from the left, as on the title screen.
            rowStyle.normal.textColor = isSelected ? Color.white
                : isNewSaveRow ? DimColor
                : SelectColor;
            Rect textRect = UiScale.R(r.x + SlotTextInset, r.y, r.width - 2f * SlotTextInset, r.height);
            GUIStyle scaled = UiScale.Style(rowStyle);
            int size = scaled.fontSize;
            scaled.fontSize = SaveUIHelper.FitFontSize(scaled, saves[i].displayName, textRect.width, size, Mathf.Max(10, (size * 3) / 4));
            GUI.Label(textRect, Utils.Ellipsize(saves[i].displayName, scaled, textRect.width), scaled);
            scaled.fontSize = size;

            // read before the click is taken: taking it marks the event used
            int clicks = Event.current != null ? Event.current.clickCount : 0;
            if (!asking && !draggingThumb && GuiInput.TryConsumeClickInRect(UiScale.R(r)))
            {
                index = i;
                // a double click saves or loads, as Enter does (the user's, 2 November 2026)
                if (clicks >= 2)
                {
                    rowDoubleClicked = true;
                }
            }
        }

        if (scrolls)
        {
            // the menu's scroll bar, one for every list (the user's, 2 November 2026)
            int before = scroll;
            scroll = menu().DrawScrollBar(bar, scroll, rows, saves.Length, ref draggingThumb, ref thumbGrab);
            if (scroll != before || draggingThumb)
            {
                scrollFollowsSelection = false;
            }
        }
        else
        {
            draggingThumb = false;
        }

        DrawPreview(detail);
    }

    /// <summary>
    /// The preview of the selected save, centred in its column: the character above, the screenshot
    /// framed, and when it was saved below.
    /// </summary>
    private void DrawPreview(Rect detail)
    {
        if (!SaveUIHelper.PreviewLines(saves, index, out string character, out string when))
        {
            return;
        }
        SaveUIHelper.EnsureScreenshotLoaded(screenshots, saves, index);

        const float pad = 24f;
        const float gap = 14f;
        float lineH = PreviewFont + 10f;
        float shotW = detail.width - 2f * pad;
        float shotH = shotW / ScreenshotAspect;
        float maxShotH = detail.height - 2f * pad - 2f * (lineH + gap);
        if (shotH > maxShotH)
        {
            shotH = maxShotH;
            shotW = shotH * ScreenshotAspect;
        }
        float blockH = lineH + gap + shotH + gap + lineH;
        float top = detail.y + (detail.height - blockH) / 2f;
        float left = detail.x + (detail.width - shotW) / 2f;

        GUIStyle line = new GUIStyle(text) { fontSize = Mathf.RoundToInt(PreviewFont), alignment = TextAnchor.MiddleLeft, wordWrap = false };
        line.normal.textColor = SelectColor;
        GUIStyle scaled = UiScale.Style(line);
        Rect characterRect = UiScale.R(left, top, shotW, lineH);
        int size = scaled.fontSize;
        scaled.fontSize = SaveUIHelper.FitFontSize(scaled, character, characterRect.width, size, Mathf.Max(10, size / 2));
        GUI.Label(characterRect, character, scaled);
        scaled.fontSize = size;

        Rect shot = new Rect(left, top + lineH + gap, shotW, shotH);
        Texture2D picture = index < screenshots.Length ? screenshots[index] : null;
        if (picture != null)
        {
            GUI.DrawTexture(UiScale.R(shot), picture, ScaleMode.StretchToFill, false);
        }
        else
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(UiScale.R(shot), Texture2D.whiteTexture);
            GUI.color = previous;
        }
        DrawFrame(new Rect(shot.x - 2f, shot.y - 2f, shot.width + 4f, shot.height + 4f), UnselectColor);

        GUI.Label(UiScale.R(left, shot.yMax + gap, shotW, lineH), when, scaled);
    }

    private static void DrawFrame(Rect refRect, Color color)
    {
        Rect r = UiScale.R(refRect);
        float line = Mathf.Max(1f, UiScale.S(2f));
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, line), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - line, r.width, line), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, line, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - line, r.y, line, r.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
