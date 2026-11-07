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

    /// <summary>A left click, A, Enter or Space: the logo or the title cutscene on screen ends, and the next one comes.</summary>
    private static bool NextPressed()
    {
        Keyboard keys = GameInput.CurrentKeyboard;
        return (GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false)
            || (GameInput.CurrentMouse?.leftButton.wasPressedThisFrame ?? false)
            || GameInput.EnterPressedThisFrame()
            || (keys?.spaceKey.wasPressedThisFrame ?? false);
    }

    /// <summary>
    /// Esc or Start: the logos and the title cutscene are all skipped, straight to the title menu, as the original's
    /// reference card says ("Press ESC to bypass the titles").
    /// </summary>
    private static bool SkipAllPressed()
    {
        return GameInput.EscapePressedThisFrame() || (GameInput.CurrentGamepad?.startButton.wasPressedThisFrame ?? false);
    }

    public void Update()
    {
        if ((state is EState.Logo1 or EState.Logo2 or EState.Title) && SkipAllPressed())
        {
            // CutscenePlayer.OnDestroy() gives back the clock and the controls, as for the title cutscene's own skip below
            if (cutscene != null)
            {
                Destroy(cutscene.gameObject);
                cutscene = null;
            }
            state = EState.Done;
            SceneManager.LoadScene("World");
            return;
        }

        switch (state)
        {
        case EState.Start:
            // Unity's splash first, begun by SplashSkip: the logos and the music wait for it
            if (!UnityEngine.Rendering.SplashScreen.isFinished)
            {
                break;
            }
            state = EState.Logo1;
            StringLoader.LoadStrings();
            originLogo = GraphicsLoader.ReadBYT("../Data/pres1.byt", 5);
            
            // Start title music. The first read of music.txt is here, which writes it with the
            // original's tracks if it is not there yet: right after the game's files were found.
            MusicConfig.EnsureLoaded();
            string titleTrack = MusicConfig.Pick(EMusicSituation.Title);
            if (MusicPlayer.Instance != null && titleTrack != null)
            {
                MusicPlayer.Instance.SwitchTrack(titleTrack, true);
            }
            
            FadeIn();
            break;
        case EState.Logo1:
            UpdateFade();
            if (fadeSpeed <= 0.0f && stateTime >= 4.5f)
            {
                FadeOut();
            }

            if (fadeAlpha >= 1.0f || NextPressed())
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

            if (fadeAlpha >= 1.0f || NextPressed())
            {
                state = EState.Title;
                cutscene = Instantiate(titleCutscene);
                fadeAlpha = 0.0f;
            }

            break;
        case EState.Title:
            // The two logos above end on a left click or A (and Enter or Space). This one is a cutscene, and
            // CutscenePlayer offers no skip - only a 10x fast forward while you hold the right
            // button, and only after the first half second - so the same click that got you past
            // the logos does nothing here and the startup stops feeling responsive halfway
            // through. Accept the same gesture, and leave the fast forward where it is.
            // Only the title cutscene: this is handled here rather than in CutscenePlayer so that
            // the intro, the credits, the endings and the death scenes are untouched.
            if (cutscene != null && NextPressed())
            {
                // CutscenePlayer.OnDestroy() gives back the clock and the controls. Cleared as well
                // as destroyed, because Destroy is deferred to the end of the frame and the test
                // below runs first.
                Destroy(cutscene.gameObject);
                cutscene = null;
            }

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
        // At the original's 4:3 with black bands, as the title cutscene after them, not stretched to the screen.
        case EState.Logo1:
            Utils.DrawBlack();
            GUI.DrawTexture(Utils.FitOriginalPicture(originLogo.width, originLogo.height), originLogo);
            break;
        case EState.Logo2:
            Utils.DrawBlack();
            GUI.DrawTexture(Utils.FitOriginalPicture(blueSkyLogo.width, blueSkyLogo.height), blueSkyLogo);
            break;
        }
        Utils.DrawFade(fadeAlpha);
    }
}
