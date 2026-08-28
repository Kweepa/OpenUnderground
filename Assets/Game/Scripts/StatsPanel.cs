using UnityEngine;
using UnityEngine.InputSystem;

public class StatsPanel : MonoBehaviour
{
    public Font font;
    public Texture2D background;
    private float lerpIn;
    private float holdTime;
    private bool pinnedOpen;

    public static StatsPanel sStatsPanel;

    public void Show()
    {
        if (!PlayerPanelState.ArePanelsAvailable)
        {
            return;
        }

        holdTime = 2.0f;
        PlayerPanelInput.ResetWalkDismissTimer();
    }

    public void Hide()
    {
        holdTime = 0.0f;
        pinnedOpen = false;
    }

    public void TogglePinnedOpen()
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

        Texture2D tex = background;
        float extendedInvPosition = 5 * tex.width;
        float w = 5 * tex.width;
        float h = 6 * tex.height;
        float invPosition = PlayerData.sData.leftHanded
            ? Screen.width - lerpIn * extendedInvPosition - 16
            : lerpIn * extendedInvPosition - extendedInvPosition + 16;
        return new Rect(invPosition, 30f, w, h);
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
                TogglePinnedOpen();
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

    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Stats;

        if (lerpIn > 0.001f)
        {
            GuiInput.RegisterBlockingRect(GetPanelGuiRectForOutsideClick());

            // draw the stats panel
            Texture2D tex = background; // was this: DataLoader.sDataLoader.panelsTex[2];

            float extendedInvPosition = 5 * tex.width;

            // draw stats and skills on the left (or right for left-handed players)
            float invPosition;
            if (PlayerData.sData.leftHanded)
            {
                // Slide in from the right side
                invPosition = Screen.width - lerpIn * extendedInvPosition - 16;
            }
            else
            {
                // Slide in from the left side
                invPosition = lerpIn * extendedInvPosition - extendedInvPosition + 16;
            }

            GUIStyle style = new GUIStyle { fontSize = 30, font = font, fontStyle = FontStyle.Italic };

            GUIStyle rightStyle = new GUIStyle(style) { alignment = TextAnchor.UpperRight };
            
            GUI.DrawTexture(new Rect(invPosition, 30, 5 * tex.width, 6 * tex.height), tex);

            style.alignment = TextAnchor.UpperCenter;
            GUI.Label(new Rect(invPosition + 33, 30 + 20, 300, style.fontSize), PlayerData.sData.playerName, style);
            style.alignment = TextAnchor.UpperLeft;
            GUI.Label(new Rect(invPosition + 33, 30 + 57, 170, style.fontSize), PlayerData.sData.playerClass.ToString(), style);
            string level = PlayerData.sData.charLevel < 4
                ? new [] { "1st", "2nd", "3rd" }[PlayerData.sData.charLevel - 1]
                : PlayerData.sData.charLevel.ToString() + "th"; 
            GUI.Label(new Rect(invPosition + 33, 30 + 57, 333, style.fontSize), level, rightStyle);

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
                Rect r = new Rect(invPosition + 33 + 187 * (i / 3), 30 + 97 + 30 * (i % 3), 150, style.fontSize);
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

            style.wordWrap = true;
            int day = 1 + (int) (PlayerData.sData.gameTime / (24 * 60 * 60));
            string status = StringLoader.GetString(1, 91);
            status += StringLoader.GetString(1, 104 + (255 - PlayerData.sData.hunger) / 30);
            status += StringLoader.GetString(1, 103);
            status += StringLoader.GetString(1, 113 + Mathf.Min((30 - PlayerData.sData.fatigue) / 5, 5));
            status += ". ";
            status += StringLoader.GetString(1, 65);
            status += StringLoader.GetString(1, 410 + LevelLoader.sLevelLoader.loadedLevel);
            status += StringLoader.GetString(1, 66);
            if (day < 100)
            {
                status += StringLoader.GetString(1, 67);
                status += StringLoader.GetString(1, 410 + day);
                status += StringLoader.GetString(1, 68);
            }
            else
            {
                status += StringLoader.GetString(1, 69);
            }

            int thour = (int) (PlayerData.sData.gameTime % (24 * 60 * 60)) / (2 * 60 * 60);
            status += StringLoader.GetString(1, 70);
            status += StringLoader.GetString(1, 71 + thour) + ". ";

            if (PlayerData.sData.poison > 0)
            {
                status += " " + StringLoader.GetString(1, 91);
                status += StringLoader.GetString(1, 84 + PlayerData.sData.poison / 6);
                status += StringLoader.GetString(1, 92);
            }

            status = status.Replace("\r", " ");
            status = status.Replace("\n", " ");
            status = status.Replace("  ", " ");
            status = status.Replace("imprisonment", "captivity");

            style.fontSize = 22;
            style.fontStyle = FontStyle.Normal;
            GUI.Label(new Rect(invPosition + 30, 30 + 200, 360, 150), status, style);

            style.fontSize = 24;
            style.fontStyle = PlayerData.sData.skillPoints > 0 ? FontStyle.Italic : FontStyle.Normal;
            GUI.Label(new Rect(invPosition + 50, 662, 333, style.fontSize), $"Skill points {PlayerData.sData.skillPoints}", style);

            style.fontSize = 24;
            rightStyle.fontSize = 24;
            for (int i = 0; i < 20; ++i)
            {
                int x = 50 + 167 * (i / 10);
                int y = 30 + 377 + 25 * (i % 10);
                
                style.fontStyle = PlayerData.sData.skill[i] > 0 ? FontStyle.Italic : FontStyle.Normal;
                rightStyle.fontStyle = style.fontStyle;
                style.normal.textColor = new Color(0.5f, 0.5f, 0.7f) * (1.0f + PlayerData.sData.skill[i] / 60.0f);
                rightStyle.normal.textColor = style.normal.textColor;

                GUI.Label(new Rect(invPosition + x, y, 133, style.fontSize), ((ESkill)i).ToString(), style);
                GUI.Label(new Rect(invPosition + x, y, 133, style.fontSize), PlayerData.sData.skill[i].ToString(), rightStyle);
            }
        }
    }
}
