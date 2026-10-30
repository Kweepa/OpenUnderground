using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The Setup screen of the title menu: settings that belong outside a game, one page at a time.
/// </summary>
/// <remarks>
/// One page so far, the music. The screen owns what every page shares - the background, the title,
/// the scale, the way back - so that the next page is only its own contents.
/// It is laid out in the pixels of a 1920x1080 screen and drawn whole at UiScale.Fit: at
/// 1920x1080 nothing moves, and on a smaller screen it shrinks whole.
/// </remarks>
public class SetupScreen
{
    private readonly FrontEnd frontEnd;
    private readonly MusicSetupPage music;

    /// <summary>True once the player has left the screen; FrontEnd then goes back to the menu.</summary>
    public bool Closed { get; private set; }

    public SetupScreen(FrontEnd frontEnd)
    {
        this.frontEnd = frontEnd;
        music = new MusicSetupPage(frontEnd);
    }

    public void Update()
    {
        GameInput.RefreshLastActiveDevice();

        bool back = (Gamepad.current?.bButton.wasPressedThisFrame ?? false)
            || (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)
            || music.BackClicked;
        music.BackClicked = false;

        // The page gets the first say: B or Escape may only be stepping back out of something of its own.
        if (back && !music.Cancel())
        {
            music.Leave();
            Closed = true;
            return;
        }

        music.Update();
    }

    public void OnGUI()
    {
        Rect screen = new Rect(0, 0, Screen.width, Screen.height);
        GuiInput.RegisterBlockingRect(screen);
        if (frontEnd.achievementsBackground != null)
        {
            GUI.DrawTexture(screen, frontEnd.achievementsBackground, ScaleMode.ScaleAndCrop);
        }

        float scale = UiScale.Fit;
        Rect area = new Rect((Screen.width - UiScale.RefWidth * scale) / 2, (Screen.height - UiScale.RefHeight * scale) / 2,
            UiScale.RefWidth * scale, UiScale.RefHeight * scale);

        // the title as every page draws its own, at the interface scale; the page below keeps UiScale.Fit
        frontEnd.DrawPageTitle("Setup: Music", 72);

        music.OnGUI(area, scale);

        GuiInput.TryConsumeClickInPanel(screen);
    }
}
