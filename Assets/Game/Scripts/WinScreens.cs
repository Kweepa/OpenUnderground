using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class WinScreens : MonoBehaviour
{
    public GUIStyle textStyle;
    public DataLoader dataLoader;

    private Texture2D win1;
    private Texture2D win2;

    private enum EState
    {
        Start,
        Win1,
        Win2,
        Done
    }

    private EState state = EState.Start;
    private CutscenePlayer cutscene;
    private float fadeSpeed;
    private float fadeAlpha;

    private void FadeIn()
    {
        fadeSpeed = -2.0f;
        fadeAlpha = 1.0f;
    }

    private void FadeOut()
    {
        fadeSpeed = 2.0f;
    }

    private void UpdateFade()
    {
        fadeAlpha += Time.deltaTime * fadeSpeed;
        fadeAlpha = Math.Clamp(fadeAlpha, 0.0f, 1.0f);
    }

    public void Update()
    {
        switch (state)
        {
        case EState.Start:
            state = EState.Win1;
            if (DataLoader.sDataLoader == null)
            {
                DontDestroyOnLoad(Instantiate(dataLoader));
            }
            StringLoader.LoadStrings();
            win1 = GraphicsLoader.ReadBYT("../Data/win1.byt", 7);
            FadeIn();
            break;
        case EState.Win1:
            UpdateFade();
            if ((GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false)
                || (GameInput.CurrentMouse?.leftButton.wasPressedThisFrame ?? false))
            {
                state = EState.Win2;
                win2 = GraphicsLoader.ReadBYT("../Data/win2.byt", 7);
            }
            break;
        case EState.Win2:
            UpdateFade();
            if (fadeSpeed <= 0.0f && ((GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false)
                                    || (GameInput.CurrentMouse?.leftButton.wasPressedThisFrame ?? false)))
            {
                FadeOut();
            }
            if (fadeAlpha == 1.0f)
            {
                state = EState.Done;
                fadeAlpha = 0.0f;
            }
            break;
        case EState.Done:
            break;
        }
    }

    private void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.WinScreen;

        switch (state)
        {
        case EState.Win1:
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height * 1.1f), win1);
            break;
        case EState.Win2:
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height * 1.1f), win2);
            {
                textStyle.fontSize = Screen.height / 16;
                
                // display title (X has banished the Slasher &c.)
                int day = 1 + (int) (PlayerData.sData.gameTime / (24 * 60 * 60));
                string banished = PlayerData.sData.playerName + Environment.NewLine
                    + $"{StringLoader.GetString(1, 187)}{PlayerData.sData.charLevel} {PlayerData.sData.playerClass}\n"
                    + StringLoader.GetString(1, 188) + Environment.NewLine
                    + $"{StringLoader.GetString(1, 189)}{day}{StringLoader.GetString(1, 190)}";
                textStyle.alignment = TextAnchor.UpperCenter;
                GUI.Label(new Rect(0, Screen.height / 9, Screen.width, 200.0f), banished, textStyle);

                textStyle.fontSize = Screen.height / 20;
                
                // show base stats
                string[] stats = { "Strength", "Dexterity", "Intellect", "Vitality", "Mana", "Experience" };
                int experience = PlayerData.sData.xp / 20;
                int[] vals =
                {
                    PlayerData.sData.strength,
                    PlayerData.sData.dexterity,
                    PlayerData.sData.intellect,
                    PlayerData.sData.vitality,
                    PlayerData.sData.maxMana,
                    experience
                };
                for (int i = 0; i < 6; ++i)
                {
                    float spacing = Screen.width / 3;
                    float w = 8 * textStyle.fontSize;
                    float x = Screen.width / 2 - spacing / 2 - w / 2 + spacing * (i / 3);
                    float y = 0.43f * Screen.height + textStyle.fontSize * (i % 3);
                    float h = textStyle.fontSize;
                    Rect r = new Rect(x, y, w, h);
                    textStyle.alignment = TextAnchor.MiddleLeft;
                    GUI.Label(r, stats[i], textStyle);
                    textStyle.alignment = TextAnchor.MiddleRight;
                    GUI.Label(r, vals[i].ToString(), textStyle);
                }

                textStyle.fontSize = Screen.height / 24;
                
                // show skills
                for (int i = 0; i < 20; ++i)
                {
                    float spacing = Screen.width / 6;
                    float w = 6 * textStyle.fontSize;
                    float x = Screen.width / 2 - 3 * spacing / 2 - w / 2 + spacing * (i / 5);
                    float y = 0.6f * Screen.height + textStyle.fontSize * (i % 5);
                    float h = textStyle.fontSize;
                    Rect r = new Rect(x, y, w, h);
                
                    textStyle.fontStyle = (PlayerData.sData.skill[i] > 0) ? FontStyle.Italic : FontStyle.Normal;
                    textStyle.normal.textColor = new Color(0.25f, 0.25f, 0.125f) * (1.0f + PlayerData.sData.skill[i] / 60.0f);

                    textStyle.alignment = TextAnchor.MiddleLeft;
                    GUI.Label(r, ((ESkill)i).ToString(), textStyle);
                    textStyle.alignment = TextAnchor.MiddleRight;
                    GUI.Label(r, PlayerData.sData.skill[i].ToString(), textStyle);
                }
            }
            break;
        }
        Utils.DrawFade(fadeAlpha);
    }
}
