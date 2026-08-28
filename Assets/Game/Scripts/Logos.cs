using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class Logos : MonoBehaviour
{
    public CutscenePlayer titleCutscene;

    private Texture2D originLogo;
    private Texture2D blueSkyLogo;

    private Texture2D[] openingScreen;

    private enum EState
    {
        Start,
        Logo1,
        Logo2,
        Title,
        Done
    }

    private EState state = EState.Start;
    private CutscenePlayer cutscene;
    private float fadeSpeed;
    private float fadeAlpha;
    private float stateTime;

    private void FadeIn()
    {
        stateTime = 0.0f;
        fadeSpeed = -2.0f;
        fadeAlpha = 1.0f;
    }

    private void FadeOut()
    {
        fadeSpeed = 2.0f;
    }

    private void UpdateFade()
    {
        stateTime += Time.deltaTime;
        fadeAlpha += Time.deltaTime * fadeSpeed;
        fadeAlpha = Math.Clamp(fadeAlpha, 0.0f, 1.0f);
    }

    public void Update()
    {
        switch (state)
        {
        case EState.Start:
            state = EState.Logo1;
            StringLoader.LoadStrings();
            originLogo = GraphicsLoader.ReadBYT("../Data/pres1.byt", 5);
            
            // Start title music
            if (MusicPlayer.Instance != null)
            {
                MusicPlayer.Instance.SwitchTrack("UW01.XMI", true);
            }
            
            FadeIn();
            break;
        case EState.Logo1:
            UpdateFade();
            if (fadeSpeed <= 0.0f && stateTime >= 4.5f)
            {
                FadeOut();
            }

            if (fadeAlpha >= 1.0f || (GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false)
                || (GameInput.CurrentMouse?.leftButton.wasPressedThisFrame ?? false))
            {
                state = EState.Logo2;
                blueSkyLogo = GraphicsLoader.ReadBYT("../Data/pres2.byt", 5);
                FadeIn();
            }

            break;
        case EState.Logo2:
            UpdateFade();
            if (fadeSpeed <= 0.0f && stateTime >= 4.5f)
            {
                FadeOut();
            }

            if (fadeAlpha >= 1.0f || (GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false)
                || (GameInput.CurrentMouse?.leftButton.wasPressedThisFrame ?? false))
            {
                state = EState.Title;
                cutscene = Instantiate(titleCutscene);
                fadeAlpha = 0.0f;
            }

            break;
        case EState.Title:
            if (cutscene == null)
            {
                SceneManager.LoadScene("World");
            }
            break;
        case EState.Done:
            break;
        }
    }

    private void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Logos;

        switch (state)
        {
        case EState.Logo1:
            GUI.DrawTexture(Screen.safeArea, originLogo);
            break;
        case EState.Logo2:
            GUI.DrawTexture(Screen.safeArea, blueSkyLogo);
            break;
        }
        Utils.DrawFade(fadeAlpha);
    }
}
