using System;
using System.IO;
using UnityEngine;

public static class SaveUIHelper
{
    public static int RefreshSaves(
        SaveGameManager.SaveSlotInfo[] saves,
        ref SaveGameManager.SaveSlotInfo[] actualSaves,
        Texture2D[] screenshots,
        int currentIndex)
    {
        if (saves == null) throw new ArgumentNullException(nameof(saves));
        if (screenshots == null) throw new ArgumentNullException(nameof(screenshots));

        // Get actual saves from the manager
        if (SaveGameManager.sInstance != null)
        {
            actualSaves = SaveGameManager.sInstance.ListSaveSlots();
        }
        else
        {
            actualSaves = new SaveGameManager.SaveSlotInfo[0];
        }

        // Create display array with slots in numeric order (Slot0, Slot1, etc.)
        for (int i = 0; i < saves.Length; i++)
        {
            string expectedSlotName = $"Slot{i}";
            SaveGameManager.SaveSlotInfo foundSave = null;

            foreach (var save in actualSaves)
            {
                if (save.slotName == expectedSlotName)
                {
                    foundSave = save;
                    break;
                }
            }

            if (foundSave != null)
            {
                saves[i] = foundSave;
                LoadScreenshotForSlot(screenshots, i, saves[i].slotName);
            }
            else
            {
                // Create empty slot
                saves[i] = new SaveGameManager.SaveSlotInfo
                {
                    slotName = "",
                    displayName = "Empty",
                    savedAtIso = "",
                    level = -1,
                    playerName = "",
                    xp = 0,
                    playerClass = 0,
                    inGameTime = "",
                    charLevel = 1
                };

                // Clear any previous screenshot for this slot
                if (screenshots[i] != null)
                {
                    UnityEngine.Object.Destroy(screenshots[i]);
                    screenshots[i] = null;
                }
            }
        }

        // Clamp index to valid range
        if (saves.Length == 0)
            return 0;

        if (currentIndex >= saves.Length) currentIndex = saves.Length - 1;
        if (currentIndex < 0) currentIndex = 0;

        return currentIndex;
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

        string path = SaveGameManager.sInstance.GetSlotScreenshotPath(slotName);
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

            string topString = $"{saves[index].playerName} | {levelOrdinal} | {playerClass} | XP {saves[index].xp / 20} | Lev {saves[index].level}";
            string inGameTime = saves[index].inGameTime;
            string bottomString = $"{dateTimeStr} ({inGameTime})";

            Rect topLineRect = new Rect(panelRect.x + 48, panelRect.y + 34, panelRect.width - 96, 18f);

            // Shrink top line font size to fit (same approach as KeyboardGUI)
            int baseFontSize = textStyle.fontSize > 0 ? textStyle.fontSize : 18;
            GUIStyle topStyle = new GUIStyle(textStyle);
            topStyle.alignment = TextAnchor.LowerLeft;
            topStyle.normal.textColor = highlightColor;
            topStyle.fontSize = baseFontSize;

            float textWidth = topStyle.CalcSize(new GUIContent(topString)).x;
            if (textWidth > topLineRect.width && topString.Length > 0)
            {
                int fontSize = Mathf.Max(10, (int)(baseFontSize * (topLineRect.width / textWidth)));
                topStyle.fontSize = fontSize;
                textWidth = topStyle.CalcSize(new GUIContent(topString)).x;
                if (textWidth > topLineRect.width)
                {
                    topStyle.fontSize = Mathf.Max(10, (int)(fontSize * (topLineRect.width / textWidth)));
                }
            }

            GUI.Label(topLineRect, topString, topStyle);

            textStyle.alignment = TextAnchor.LowerLeft;
            textStyle.normal.textColor = highlightColor;
            GUI.Label(new Rect(panelRect.x + 48, panelRect.yMax - 36, panelRect.width - 96, 18f), bottomString, textStyle);
        }
    }
}
