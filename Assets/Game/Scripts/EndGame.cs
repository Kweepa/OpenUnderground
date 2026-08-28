using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class EndGame : MonoBehaviour
{
    private enum EState
    {
        FadeToBlack,
        CutsceneStart,
        Cutscene,
        Congratulations,
        Stats,
        Done
    }

    public CutscenePlayer endGameCutscene;
    public GUIStyle textStyle;
    public string[] congrats;

    private EState state = EState.FadeToBlack;
    private float fadeSpeed = 2.0f;
    private float fadeAlpha = 1.0f;
    private CutscenePlayer cutscene;
    private Texture2D win1;
    private Texture2D win2;
    
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
        fadeAlpha += Time.unscaledDeltaTime * fadeSpeed;
        fadeAlpha = Math.Clamp(fadeAlpha, 0.0f, 1.0f);
    }
    
    public void Update()
    {
        switch (state)
        {
        case EState.FadeToBlack:
            UpdateFade();
            if (fadeAlpha >= 1.0f)
            {
                LevelLoader.sLevelLoader.DeactivateCurrentLevel();
                state = EState.CutsceneStart;
                cutscene = Instantiate(endGameCutscene);
                fadeAlpha = 0.0f;
            }
            break;
        case EState.CutsceneStart:
            state = EState.Cutscene;
            break;
        case EState.Cutscene:
            if (cutscene == null)
            {
                // move to next stage
                state = EState.Congratulations;
                win1 = GraphicsLoader.ReadBYT("../Data/win1.byt", 7);
                win2 = GraphicsLoader.ReadBYT("../Data/win2.byt", 7);
                FadeIn();
            }
            break;
        case EState.Congratulations:
            UpdateFade();
            if ((GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false)
                || (GameInput.CurrentMouse?.leftButton.wasPressedThisFrame ?? false)
                || GameInput.EscapePressedThisFrame())
            {
                state = EState.Stats;
            }
            break;
        case EState.Stats:
            UpdateFade();
            if (fadeSpeed <= 0.0f && ((GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false)
                                      || (GameInput.CurrentMouse?.leftButton.wasPressedThisFrame ?? false)
                                      || GameInput.EscapePressedThisFrame()))
            {
                FadeOut();
            }
            if (fadeAlpha == 1.0f)
            {
                SceneManager.LoadScene("World");
                state = EState.Done;
                fadeAlpha = 0.0f;
            }
            break;
        case EState.Done:
            break;
        }
    }
    
    public void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.EndGame;

        switch (state)
        {
        case EState.FadeToBlack:
        case EState.CutsceneStart:
            break;
        case EState.Congratulations:
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height * 1.1f), win2);
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, Screen.width, 0.32f * Screen.height * 1.1f), win1, new Rect(0, 0.68f, 1.0f, 0.32f));

            string text = string.Join('\n', congrats);

            textStyle.fontSize = Screen.height / 16;
            textStyle.normal.textColor = new Color(0.27f, 0.1f, 0.05f);
            textStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(-0.003f * Screen.width, 0.347f * Screen.height, Screen.width, 0.5f * Screen.height), text, textStyle);
            GUI.Label(new Rect(0, 0.35f * Screen.height, Screen.width, 0.5f * Screen.height), text, textStyle);
            textStyle.normal.textColor = new Color(1.0f, 0.56f, 0.0f);
            GUI.Label(new Rect(-0.002f * Screen.width, 0.348f * Screen.height, Screen.width, 0.5f * Screen.height), text, textStyle);
            break;
        case EState.Stats:
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height * 1.1f), win2);
            {
                textStyle.fontSize = Screen.height / 16;
                
                // display title (X has banished the Slasher &c)
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
