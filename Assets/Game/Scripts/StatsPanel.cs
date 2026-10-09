using UnityEngine;
using UnityEngine.InputSystem;

public class StatsPanel : MonoBehaviour
{
    public Font font;
    public Texture2D background;
    private float lerpIn;
    private float holdTime;
    private bool pinnedOpen;
    private bool onLeft;

    public static StatsPanel sStatsPanel;

    /// <summary>Whether the panel is on the left of the screen, where it covers the magic panel.</summary>
    public bool IsOnLeft => onLeft;

    public void Show()
    {
        if (!PlayerPanelState.ArePanelsAvailable)
        {
            return;
        }

        // on the side it last opened on: a shrine or a book shows it, and the click or key that got
        // there says nothing of the hand
        holdTime = 2.0f;
        PlayerPanelInput.ResetWalkDismissTimer();
    }

    public void Hide()
    {
        holdTime = 0.0f;
        pinnedOpen = false;
    }

    /// <param name="fromGamepad">Opened by the pad's trigger, which puts the panel on that trigger's side.</param>
    public void TogglePinnedOpen(bool fromGamepad = false)
    {
        if (!PlayerPanelState.ArePanelsAvailable)
        {
            if (ShouldDismissWithEscape())
            {
                Hide();
            }

            return;
        }

        bool opening = !pinnedOpen;
        if (opening)
        {
            ChooseSide(fromGamepad);
        }

        pinnedOpen = !pinnedOpen;
        holdTime = pinnedOpen ? 2.0f : 0.0f;
        if (pinnedOpen && opening)
        {
            Inventory.HidePanel();
            Magic.HidePanel();
        }

        if (opening)
        {
            PlayerPanelInput.ResetWalkDismissTimer();
            TutorialManager.NotifyStatsToggled(true);
        }
    }

    public bool IsPinnedOpen()
    {
        return pinnedOpen;
    }

    /// <summary>Stats overlay is up (pinned, timed <see cref="Show"/>, or still lerping).</summary>
    public bool ShouldDismissWithEscape()
    {
        return pinnedOpen || holdTime > 0f || lerpIn > 0.002f;
    }

    /// <summary>GUI rect of the sliding stats panel (for outside-click dismiss).</summary>
    public Rect GetPanelGuiRectForOutsideClick()
    {
        if (lerpIn < 0.001f || background == null)
        {
            return default;
        }

        return GetPanelRect();
    }

    // The panel is drawn without the outer row of pixels on its top, bottom and right, to take
    // less room (7 October 2026). The text keeps its place on the wood, so it is laid out from
    // where the uncut top would be.
    private const int CropTop = 1;
    private const int CropBottom = 1;
    private const int CropRight = 1;
    private const float ScaleX = 5;
    private const float ScaleY = 6;

    /// <summary>
    /// Picks the side the panel comes in from when the player opens it. From the pad it is the
    /// side of the trigger that opens it, the left one for a right-handed player. From the keyboard
    /// it is the right, where it leaves the timed spells and the mana flask in view. A panel still
    /// on screen stays where it is, and one shown by the game keeps the last side.
    /// </summary>
    private void ChooseSide(bool fromGamepad)
    {
        if (lerpIn > 0.002f)
        {
            return;
        }

        onLeft = fromGamepad && PlayerData.sData != null && !PlayerData.sData.leftHanded;
    }

    /// <summary>The panel as drawn, on its side and sliding in from it.</summary>
    private Rect GetPanelRect()
    {
        float w = ScaleX * (background.width - CropRight);
        float h = ScaleY * (background.height - CropTop - CropBottom);
        float slide = lerpIn * (w + PlayerPanelState.PanelMargin);
        float x = onLeft ? slide - w : Screen.width - slide;
        return new Rect(x, PlayerPanelState.PanelMargin, w, h);
    }

    private bool statsAnalogTriggerWasHeld;

    public void Update()
    {
        sStatsPanel = this;

        if (!PlayerPanelState.ArePanelsAvailable)
        {
            if (ShouldDismissWithEscape())
            {
                Hide();
            }

            statsAnalogTriggerWasHeld = false;
            float voidLerpTarget = holdTime > 0.0f ? 1.0f : 0.0f;
            lerpIn = Utils.DampedApproachUnscaledTime(lerpIn, voidLerpTarget, 0.2f);
            if (holdTime > 0.0f)
            {
                holdTime -= Time.unscaledDeltaTime;
            }

            return;
        }

        if (pinnedOpen)
        {
            holdTime = 2.0f;
        }

        Gamepad gp = Gamepad.current;
        if (gp != null
            && PlayerData.sData != null
            && PlayerPanelState.ArePanelsAvailable
            && !gp.leftShoulder.isPressed)
        {
            var statsTrigger = PlayerData.sData.leftHanded ? gp.rightTrigger : gp.leftTrigger;
            float v = statsTrigger.ReadValue();
            bool held = v > 0.65f;
            bool edge = held && !statsAnalogTriggerWasHeld;
            statsAnalogTriggerWasHeld = held;
            if (edge)
            {
                TogglePinnedOpen(fromGamepad: true);
            }
        }
        else
        {
            statsAnalogTriggerWasHeld = false;
        }

        if (holdTime > 0.0f)
        {
            holdTime -= Time.unscaledDeltaTime;
        }

        float lerpTarget = holdTime > 0.0f ? 1.0f : 0.0f;
        lerpIn = Utils.DampedApproachUnscaledTime(lerpIn, lerpTarget, 0.2f);
    }

    private static string Bold(string text)
    {
        return "<b>" + text + "</b>";
    }

    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Stats;

        if (PlayerObject.HudHidden)
        {
            return;
        }

        if (lerpIn > 0.001f)
        {
            GuiInput.RegisterBlockingRect(GetPanelGuiRectForOutsideClick());

            // draw the stats panel
            Texture2D tex = background; // was this: DataLoader.sDataLoader.panelsTex[2];

            Rect panel = GetPanelRect();
            float invPosition = panel.x;
            // where the top of the uncut texture would be: everything below is placed from it
            float top = panel.y - ScaleY * CropTop;

            GUIStyle style = new GUIStyle { fontSize = 30, font = font, fontStyle = FontStyle.Italic };

            GUIStyle rightStyle = new GUIStyle(style) { alignment = TextAnchor.UpperRight };
            
            GUI.DrawTextureWithTexCoords(panel, tex,
                new Rect(0, (float)CropBottom / tex.height, (float)(tex.width - CropRight) / tex.width,
                         (float)(tex.height - CropTop - CropBottom) / tex.height));

            style.alignment = TextAnchor.UpperCenter;
            // centred over the two columns of stats below, which run from 33 to 370
            GUI.Label(new Rect(invPosition + 33, top + 20, 337, style.fontSize), PlayerData.sData.playerName, style);
            style.alignment = TextAnchor.UpperLeft;
            GUI.Label(new Rect(invPosition + 33, top + 57, 170, style.fontSize), PlayerData.sData.playerClass.ToString(), style);
            string level = PlayerData.sData.charLevel < 4
                ? new [] { "1st", "2nd", "3rd" }[PlayerData.sData.charLevel - 1]
                : PlayerData.sData.charLevel.ToString() + "th"; 
            GUI.Label(new Rect(invPosition + 33, top + 57, 337, style.fontSize), level, rightStyle);

            style.fontSize = 24;
            rightStyle.fontSize = 24;
            style.normal.textColor = new Color32(50, 29, 18, 255);
            rightStyle.normal.textColor = style.normal.textColor;
            string[] stats = { "Strength", "Dexterity", "Intellect", "Vitality", "Mana", "Experience" };
            int experience = PlayerData.sData.xp / 20;
            string[] vals =
            {
                PlayerData.sData.strength.ToString(),
                PlayerData.sData.dexterity.ToString(),
                PlayerData.sData.intellect.ToString(),
                $"{PlayerData.sData.hp.ToString()}/{PlayerData.sData.vitality.ToString()}",
                $"{PlayerData.sData.mana.ToString()}/{PlayerData.sData.maxMana.ToString()}",
                experience.ToString()
            };
            for (int i = 0; i < 6; ++i)
            {
                Rect r = new Rect(invPosition + 33 + 187 * (i / 3), top + 97 + 30 * (i % 3), 150, style.fontSize);
                if (i == 5 && vals[i].Length > 2)
                {
                    GUI.Label(r, "Exp.", style);
                }
                else
                {
                    GUI.Label(r, stats[i], style);
                }
                GUI.Label(r, vals[i], rightStyle);
            }

            // One sentence a line, as the strings' own line breaks have it, with what changes in bold.
            style.wordWrap = true;
            style.richText = true;
            int day = 1 + (int) (PlayerData.sData.gameTime / (24 * 60 * 60));
            string status = StringLoader.GetString(1, 91);
            status += Bold(StringLoader.GetString(1, 104 + (255 - PlayerData.sData.hunger) / 30));
            status += StringLoader.GetString(1, 103);
            status += Bold(StringLoader.GetString(1, 113 + Mathf.Min((30 - PlayerData.sData.fatigue) / 5, 5)));
            status += ".\n";
            status += StringLoader.GetString(1, 65);
            status += Bold(StringLoader.GetString(1, 410 + LevelLoader.sLevelLoader.loadedLevel));
            status += StringLoader.GetString(1, 66);
            if (day < 100)
            {
                status += StringLoader.GetString(1, 67);
                status += Bold(StringLoader.GetString(1, 410 + day));
                status += StringLoader.GetString(1, 68);
            }
            else
            {
                status += StringLoader.GetString(1, 69);
            }

            int thour = (int) (PlayerData.sData.gameTime % (24 * 60 * 60)) / (2 * 60 * 60);
            status += StringLoader.GetString(1, 70);
            status += Bold(StringLoader.GetString(1, 71 + thour)) + ".\n";

            if (PlayerData.sData.poison > 0)
            {
                // The original's scale, UW.EXE 0x262b7-0x262bf: 84 + (level - 1) / 3, so the levels
                // 1 to 15 take the five adverbs three each.
                status += StringLoader.GetString(1, 91);
                status += Bold(StringLoader.GetString(1, 84 + Mathf.Clamp((PlayerData.sData.poison - 1) / 3, 0, 4)));
                status += StringLoader.GetString(1, 92);
            }

            status = status.Replace("\r", "");
            status = status.Replace("imprisonment", "captivity");
            status = status.TrimEnd('\n');

            // Size 20 and 365 wide keep the longest case - an uncountable day, a long time of day
            // and the poison - to seven lines, above the skills.
            style.fontSize = 20;
            style.fontStyle = FontStyle.Normal;
            GUI.Label(new Rect(invPosition + 30, top + 200, 365, 177), status, style);
            style.richText = false;

            style.fontSize = 24;
            style.fontStyle = PlayerData.sData.skillPoints > 0 ? FontStyle.Italic : FontStyle.Normal;
            // "Available skill points" is ours: the original shows no such line, nor any string for it
            GUI.Label(new Rect(invPosition + 47, top + 635, 333, style.fontSize), $"Available skill points {PlayerData.sData.skillPoints}", style);

            style.fontSize = 24;
            rightStyle.fontSize = 24;
            // The dark box of the skills runs from 30 to 385. The two columns were 20 from its left
            // and 35 from its right; both margins are now 15, and the columns take the room.
            const float skillsLeft = 30 + 15;
            const float skillsRight = 385 - 15;
            const float columnGap = 34;
            const float columnWidth = (skillsRight - skillsLeft - columnGap) / 2;
            for (int i = 0; i < 20; ++i)
            {
                float x = skillsLeft + (columnWidth + columnGap) * (i / 10);
                float y = top + 374 + 25 * (i % 10);
                
                style.fontStyle = PlayerData.sData.skill[i] > 0 ? FontStyle.Italic : FontStyle.Normal;
                rightStyle.fontStyle = style.fontStyle;
                style.normal.textColor = new Color(0.5f, 0.5f, 0.7f) * (1.0f + PlayerData.sData.skill[i] / 60.0f);
                rightStyle.normal.textColor = style.normal.textColor;

                GUI.Label(new Rect(invPosition + x, y, columnWidth, style.fontSize), ((ESkill)i).ToString(), style);
                GUI.Label(new Rect(invPosition + x, y, columnWidth, style.fontSize), PlayerData.sData.skill[i].ToString(), rightStyle);
            }
        }
    }
}
