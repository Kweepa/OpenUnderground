using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Unity's splash screen, begun by the game as the first scene loads, and cut short by a key, a click or a pad button:
/// it fades to the game at once instead of playing to its end. The splash is still shown; only its length is the
/// player's.
/// </summary>
/// <remarks>
/// The automatic splash cannot be cut: Unity plays it before the first scene and runs no frame of the game until it has
/// finished (tried: a step put in the player loop first ran after the splash). So Player Settings' Show Splash Screen
/// is off, and the same splash, with its logo and colours from those settings, is begun here with SplashScreen.Begin(),
/// Unity's documented way to control it. SplashScreen.Stop(FadeOut) jumps to its last stage, the fade from its
/// background to the game. Logos waits for it to finish before its first logo and the music.
/// </remarks>
public sealed class SplashSkip : MonoBehaviour
{
    // True when Unity is about to play its own splash, Show Splash Screen being on (a copy of the project whose setting
    // did not follow this change): that one plays, uncut, and no second splash is begun after it
    private static bool automaticSplash;

    // Stop() once only: each call starts the fade over, so fast presses held the splash on screen for good
    private bool stopped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    private static void BeforeAutomaticSplash()
    {
        automaticSplash = !SplashScreen.isFinished;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Create()
    {
        if (automaticSplash)
        {
            return;
        }
        var go = new GameObject(nameof(SplashSkip));
        DontDestroyOnLoad(go);
        go.AddComponent<SplashSkip>();
        SplashScreen.Begin();
    }

    private void Update()
    {
        if (SplashScreen.isFinished)
        {
            Destroy(gameObject);
            return;
        }
        if (!stopped && Pressed())
        {
            stopped = true;
            SplashScreen.Stop(SplashScreen.StopBehavior.FadeOut);
        }
    }

    /// <summary>Any key, a mouse button, or a pad's face button or Start pressed this frame.</summary>
    private static bool Pressed()
    {
        Keyboard keys = Keyboard.current;
        Mouse mouse = Mouse.current;
        Gamepad pad = Gamepad.current;
        return (keys != null && keys.anyKey.wasPressedThisFrame)
            || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
            || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame
                || pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
    }
}
