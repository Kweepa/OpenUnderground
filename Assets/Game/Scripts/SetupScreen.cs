using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The Setup screen of the title menu: settings that belong outside a game, one page at a time.
/// </summary>
/// <remarks>
/// One page so far, the music. The screen owns what every page shares - the background, the title,
/// the scale, the way back - so that the next page is only its own contents.
/// It is laid out in the pixels of a 1920x1080 screen and drawn at one uniform scale, capped at 2,
/// like the load list: at 1920x1080 nothing moves, and on a smaller screen it shrinks whole.
/// </remarks>
public class SetupScreen
{
    private const float RefWidth = 1920f;
    private const float RefHeight = 1080f;
    private const float MaxScale = 2f;

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

        float scale = Mathf.Min(Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight), MaxScale);
        Rect area = new Rect((Screen.width - RefWidth * scale) / 2, (Screen.height - RefHeight * scale) / 2,
            RefWidth * scale, RefHeight * scale);

        GUIStyle header = new GUIStyle(frontEnd.achievementHeaderStyle) { fontSize = Mathf.RoundToInt(72 * scale) };
        GUI.Label(new Rect(area.x, area.y + 30 * scale, area.width, 100 * scale), "Setup: Music", header);

        music.OnGUI(area, scale);

        GuiInput.TryConsumeClickInPanel(screen);
    }
}
