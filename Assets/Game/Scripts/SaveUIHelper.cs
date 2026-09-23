using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class SaveUIHelper
{
    /// <summary>The row that starts a new save rather than overwriting one.</summary>
    public const string NewSaveRowLabel = "< New save >";

    /// <summary>
    /// The row that holds this slot, or <paramref name="fallback"/> when the list has no such row.
    /// </summary>
    /// <remarks>
    /// Rows are found by name rather than by number when the list changes underneath them: the
    /// save list and the load list are not the same list - one hides the quicksaves and carries
    /// the new save row at the top - so a row number means a different save on each side.
    /// </remarks>
    public static int IndexOfSlot(SaveGameManager.SaveSlotInfo[] saves, string slotName, int fallback)
    {
        if (saves == null || string.IsNullOrEmpty(slotName))
        {
            return fallback;
        }

        for (int i = 0; i < saves.Length; ++i)
        {
            if (saves[i] != null && saves[i].slotName == slotName)
            {
                return i;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Fills <paramref name="saves"/> with every save on disk, newest first, and sizes
    /// <paramref name="screenshots"/> to match. Returns the selected row, clamped.
    /// </summary>
    /// <remarks>
    /// The lists used to be ten fixed rows, Slot0 to Slot9, with the empty ones drawn as "Empty":
    /// the row was the slot. Now the row is whatever is on disk, in date order, which is what lets
    /// the quicksaves and the automatic saves show up beside the saves made by hand without any of
    /// them owning a numbered place. There is no upper limit any more, and the lists scroll.
    ///
    /// <paramref name="forSaving"/> makes it the save list rather than the load list, and that
    /// differs in two ways. Its first row is not a save but the way to make one, because a list of
    /// existing saves can only offer to overwrite them and on a first run there would be nothing to
    /// pick at all. And it leaves out the saves the game writes by itself, quicksaves and autosaves
    /// alike. Those are the game's to write over, and a save written by hand on one keeps the
    /// file it lands in - so the game would later replace it as its own. They are all there in
    /// the load list.
    ///
    /// Screenshots are not loaded here. Only the selected row's picture is ever drawn, so loading
    /// one per save would be decoding a few dozen pictures to show one of them.
    /// <see cref="EnsureScreenshotLoaded"/> keeps exactly one.
    /// </remarks>
    public static int RefreshSaves(
        ref SaveGameManager.SaveSlotInfo[] saves,
        ref Texture2D[] screenshots,
        int currentIndex,
        bool forSaving)
    {
        SaveGameManager.SaveSlotInfo[] all = SaveGameManager.sInstance != null
            ? SaveGameManager.sInstance.ListSaveSlotsNewestFirst()
            : new SaveGameManager.SaveSlotInfo[0];

        List<SaveGameManager.SaveSlotInfo> keep = new List<SaveGameManager.SaveSlotInfo>(all.Length);
        foreach (SaveGameManager.SaveSlotInfo info in all)
        {
            if (forSaving && (SaveGameManager.IsQuickSlot(info?.slotName)
                || SaveGameManager.IsAutoSlot(info?.slotName)))
            {
                continue;
            }

            keep.Add(info);
        }

        SaveGameManager.SaveSlotInfo[] onDisk = keep.ToArray();

        int rows = onDisk.Length + (forSaving ? 1 : 0);
        saves = new SaveGameManager.SaveSlotInfo[rows];

        int first = 0;
        if (forSaving)
        {
            saves[0] = new SaveGameManager.SaveSlotInfo
            {
                slotName = "",
                displayName = NewSaveRowLabel,
                savedAtIso = "",
                level = -1,
                playerName = "",
                xp = 0,
                playerClass = 0,
                inGameTime = "",
                charLevel = 1
            };
            first = 1;
        }

        for (int i = 0; i < onDisk.Length; ++i)
        {
            saves[first + i] = onDisk[i];
        }

        // A quicksave is numbered by how recent it is, not by the file it lives in. The list is
        // sorted newest first and the five roll over each other, so a number stored with the save
        // would read 3, 5, 1, 4, 2 down the rows within a session. The row is built here instead,
        // from the name in the save's own header rather than from whoever is playing now: an old
        // save may belong to a different character.
        int quickRank = 0;
        for (int i = first; i < saves.Length; ++i)
        {
            if (saves[i] == null || !SaveGameManager.IsQuickSlot(saves[i].slotName))
            {
                continue;
            }

            ++quickRank;
            // Two digits, so "Quick 01" is as wide as "Autosave" and the names below line up.
            saves[i].displayName =
                $"Quick {quickRank:00} - {SaveGameManager.ShortName(saves[i].playerName)} - "
                + SaveGameManager.DungeonLevelLabel(saves[i].level);
        }

        // The rows have just been rebuilt, so any picture already loaded belongs to the list that
        // is being replaced: either its row is gone or it now sits on a different one. Forget it
        // and let the next frame load the right picture for the row that is selected now.
        // Without this the preview stayed blank when a selection was carried from one tab to the
        // other: the name had not changed, so the loader saw nothing to do, while the texture it
        // thought it had was destroyed with the old array.
        if (screenshots != null)
        {
            FreeScreenshots(screenshots);
        }

        // Any texture whose row is gone goes with it.
        if (screenshots == null || screenshots.Length != rows)
        {
            if (screenshots != null)
            {
                for (int i = 0; i < screenshots.Length; ++i)
                {
                    if (screenshots[i] != null)
                    {
                        UnityEngine.Object.Destroy(screenshots[i]);
                    }
                }
            }

            screenshots = new Texture2D[rows];
        }

        if (rows == 0)
        {
            return 0;
        }

        if (currentIndex >= rows) currentIndex = rows - 1;
        if (currentIndex < 0) currentIndex = 0;

        return currentIndex;
    }

    /// <summary>
    /// The font size at which this text fits <paramref name="width"/>, never larger than
    /// <paramref name="baseFontSize"/> and never smaller than <paramref name="minFontSize"/>.
    /// </summary>
    /// <remarks>
    /// A row is as wide as it is, but the letters in a name are not all the same width: a name of
    /// m's runs off the end of a window that the same number of i's leaves half empty. So the text
    /// is measured and the letters shrink to fit, while the row keeps its height - the list stays
    /// on its grid and only that one name is written smaller.
    /// Two passes, because the width of a string is not exactly proportional to the font size:
    /// the first guess from the ratio lands close, and the second cleans up what it missed.
    /// The style is left as it was found; the size is returned rather than applied, because the
    /// caller usually has something else to draw at full size afterwards.
    /// </remarks>
    public static int FitFontSize(GUIStyle style, string text, float width, int baseFontSize, int minFontSize)
    {
        if (style == null || string.IsNullOrEmpty(text) || width <= 0.0f || baseFontSize <= 0)
        {
            return baseFontSize;
        }

        int wasSize = style.fontSize;
        int size = baseFontSize;
        style.fontSize = size;

        float textWidth = style.CalcSize(new GUIContent(text)).x;
        if (textWidth > width)
        {
            size = Mathf.Max(minFontSize, (int)(size * (width / textWidth)));
            style.fontSize = size;
            textWidth = style.CalcSize(new GUIContent(text)).x;
            if (textWidth > width)
            {
                size = Mathf.Max(minFontSize, (int)(size * (width / textWidth)));
            }
        }

        style.fontSize = wasSize;
        return size;
    }

    /// <summary>Is this row the one that starts a new save.</summary>
    public static bool IsNewSaveRow(SaveGameManager.SaveSlotInfo[] saves, int index)
    {
        return saves != null && index >= 0 && index < saves.Length && saves[index] != null
            && string.IsNullOrEmpty(saves[index].slotName)
            && saves[index].displayName == NewSaveRowLabel;
    }

    // The one screenshot held in memory, and the slot it belongs to. Static because only one save
    // list is ever on screen: the front end's and the in game panel's cannot both be open.
    private static string loadedScreenshotSlot;

    // The slot the selection has moved to, and when it got there. A picture has to be read and
    // decoded, so it is not done until the selection has stood still for a moment: holding the arrow
    // key down through a long list would otherwise decode one per row, all of them thrown away.
    private static string pendingScreenshotSlot;
    private static float pendingScreenshotSince;
    private const float ScreenshotSettleSeconds = 0.075f;

    /// <summary>
    /// Makes sure the selected row's screenshot is the one loaded, and that it is the only one.
    /// </summary>
    /// <remarks>
    /// Called every frame the list is drawn. The moment the selection moves, the picture it had is
    /// freed - showing another save's screenshot would be worse than showing none - and the new one
    /// is read once the selection has settled.
    /// </remarks>
    public static void EnsureScreenshotLoaded(
        Texture2D[] screenshots,
        SaveGameManager.SaveSlotInfo[] saves,
        int index)
    {
        if (screenshots == null || saves == null || index < 0 || index >= screenshots.Length
            || index >= saves.Length)
        {
            return;
        }

        string wanted = saves[index] != null ? saves[index].slotName : "";
        if (loadedScreenshotSlot == wanted)
        {
            return;
        }

        if (pendingScreenshotSlot != wanted)
        {
            // The selection has just moved: drop what was loaded and start the clock.
            pendingScreenshotSlot = wanted;
            pendingScreenshotSince = Time.unscaledTime;
            FreeScreenshots(screenshots);
            return;
        }

        if (Time.unscaledTime - pendingScreenshotSince < ScreenshotSettleSeconds)
        {
            return;
        }

        loadedScreenshotSlot = wanted;
        if (!string.IsNullOrEmpty(wanted))
        {
            LoadScreenshotForSlot(screenshots, index, wanted);
        }
    }

    private static void FreeScreenshots(Texture2D[] screenshots)
    {
        loadedScreenshotSlot = null;
        for (int i = 0; i < screenshots.Length; ++i)
        {
            if (screenshots[i] != null)
            {
                UnityEngine.Object.Destroy(screenshots[i]);
                screenshots[i] = null;
            }
        }
    }

    public static void LoadScreenshotForSlot(
        Texture2D[] screenshots,
        int slotIndex,
        string slotName)
    {
        if (screenshots == null) throw new ArgumentNullException(nameof(screenshots));
        if (slotIndex < 0 || slotIndex >= screenshots.Length)
            return;

        // Dispose any existing texture for this slot
        if (screenshots[slotIndex] != null)
        {
            UnityEngine.Object.Destroy(screenshots[slotIndex]);
            screenshots[slotIndex] = null;
        }

        if (SaveGameManager.sInstance == null || string.IsNullOrEmpty(slotName))
            return;

        string path = SaveGameManager.sInstance.FindSlotScreenshotPath(slotName);
        try
        {
            if (!File.Exists(path))
                return;

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes != null && bytes.Length > 0)
            {
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (tex.LoadImage(bytes))
                {
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;
                    screenshots[slotIndex] = tex;
                }
                else
                {
                    UnityEngine.Object.Destroy(tex);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to load screenshot for slot '{slotName}' at '{path}': {e.Message}");
        }
    }

    public static Rect GetSaveSlotDetailPanelRect(float x, float y, Texture2D background)
    {
        if (background == null)
        {
            return default;
        }

        return new Rect(x, y, 8.0f * background.width, 4.0f * background.height);
    }

    public static void DrawSaveSlotDetailPanel(
        float x,
        float y,
        Texture2D background,
        GUIStyle textStyle,
        Color highlightColor,
        SaveGameManager.SaveSlotInfo[] saves,
        Texture2D[] screenshots,
        int index)
    {
        if (background == null || textStyle == null || saves == null || screenshots == null)
        {
            return;
        }

        Rect panelRect = GetSaveSlotDetailPanelRect(x, y, background);

        GUI.DrawTexture(panelRect, background, ScaleMode.StretchToFill, true);
        DrawSaveSlotDetailContent(panelRect, textStyle, highlightColor, saves, screenshots, index);
    }

    public static void DrawSaveSlotDetailContent(
        Rect panelRect,
        GUIStyle textStyle,
        Color highlightColor,
        SaveGameManager.SaveSlotInfo[] saves,
        Texture2D[] screenshots,
        int index)
    {
        if (textStyle == null || saves == null || screenshots == null || panelRect.width <= 0f)
        {
            return;
        }

        // Center screenshot inside the panel
        if (index >= 0 && index < screenshots.Length && screenshots[index] != null)
        {
            Texture2D tex = screenshots[index];
            Rect previewRect = new Rect(panelRect.x + 48, panelRect.y + 64, 570, 332);
            GUI.DrawTexture(previewRect, tex, ScaleMode.StretchToFill, false);
        }

        // Bottom info inside the right panel: date/time, level, XP
        if (index >= 0 && index < saves.Length && !string.IsNullOrEmpty(saves[index].slotName))
        {
            string dateTimeStr = "";
            if (!string.IsNullOrEmpty(saves[index].savedAtIso)
                && DateTime.TryParse(saves[index].savedAtIso, out DateTime dt))
            {
                dateTimeStr = dt.ToString("d MMM yyyy HH:mm");
            }

            EPlayerClass playerClass = (EPlayerClass)saves[index].playerClass;
            int charLevel = Mathf.Max(1, saves[index].charLevel);
            string levelOrdinal = charLevel < 4
                ? new[] { "1st", "2nd", "3rd" }[charLevel - 1]
                : $"{charLevel}th";

            // The ordinal two fields to the left is the character's own level; this one is the
            // depth, and it is written the same way here as on the rows of the list.
            string topString = $"{saves[index].playerName} | {levelOrdinal} | {playerClass} | XP {saves[index].xp / 20} | {SaveGameManager.DungeonLevelLabel(saves[index].level)}";
            string inGameTime = saves[index].inGameTime;
            string bottomString = $"{dateTimeStr} ({inGameTime})";

            Rect topLineRect = new Rect(panelRect.x + 48, panelRect.y + 34, panelRect.width - 96, 18f);

            // Shrink top line font size to fit (same approach as KeyboardGUI)
            int baseFontSize = textStyle.fontSize > 0 ? textStyle.fontSize : 18;
            GUIStyle topStyle = new GUIStyle(textStyle);
            topStyle.alignment = TextAnchor.LowerLeft;
            topStyle.normal.textColor = highlightColor;
            topStyle.fontSize = baseFontSize;

            topStyle.fontSize = FitFontSize(topStyle, topString, topLineRect.width, baseFontSize, 10);

            GUI.Label(topLineRect, topString, topStyle);

            textStyle.alignment = TextAnchor.LowerLeft;
            textStyle.normal.textColor = highlightColor;
            GUI.Label(new Rect(panelRect.x + 48, panelRect.yMax - 36, panelRect.width - 96, 18f), bottomString, textStyle);
        }
    }
}
