using UnityEngine;

/// <summary>
/// The Setup screen of the title menu: the game menu with its OPTIONS and CONTROLS tabs, the same
/// one a game opens, so that a setting can be made before a game starts.
/// </summary>
/// <remarks>
/// This is the menu's host on the title screen, as SaveLoadGUI is in a game: it has no saves, and its
/// Main Menu button closes the menu back to the title menu, as B and Esc do. Its panel is
/// the rock of the title screen's other pages rather than a game's wood. The music
/// tracks open from their row of AUDIO here too; while they are open the title music stops, and it
/// comes back when they close.
/// </remarks>
public class SetupScreen : IGameMenuHost
{
    private readonly FrontEnd frontEnd;
    private readonly GameMenu menu;

    /// <summary>True once the player has left the screen; FrontEnd then goes back to the menu.</summary>
    public bool Closed { get; private set; }

    public SetupScreen(FrontEnd frontEnd)
    {
        this.frontEnd = frontEnd;
        GameMenuSkin skin = TitleSkin(frontEnd);
        // the rock round the panel darkened, as the game is behind it in a game
        menu = new GameMenu(skin, this, new[] { GameMenu.ETab.Options, GameMenu.ETab.Controls }, true);
        menu.Open(GameMenu.ETab.Options);
    }

    /// <summary>The menu's look on the title screen: the title's rock, a frame in its blues, the title's font and sounds.</summary>
    public static GameMenuSkin TitleSkin(FrontEnd frontEnd)
    {
        return new GameMenuSkin
        {
            Font = frontEnd.buttonLabelStyle,
            // the title screen's own high resolution rock (creditsbackground-transformed.png), the
            // user's choice of 31 October 2026; in a game the menu has the signs' wood
            Background = frontEnd.achievementsBackground,
            // the frame in the rock's blues, measured on the picture: its darkest tenth is black, its
            // lightest about (80, 80, 112)
            FrameDark = new Color32(8, 8, 20, 255),
            FrameLight = new Color32(96, 96, 150, 255),
            Wood = frontEnd.savePanelBackground,
            AButton = frontEnd.aButton,
            BButton = frontEnd.bButton,
            XButton = frontEnd.xButton,
            YButton = frontEnd.yButton,
            EscapeKey = frontEnd.escapeKeyTexture,
            BlankKey = frontEnd.blankKeyTexture,
            DPadLeftRight = frontEnd.dPadLeftRightTexture,
            MoveSound = frontEnd.moveSelection,
            MakeSound = frontEnd.makeSelection,
            BackSound = frontEnd.backSelection,
        };
    }

    public void Update()
    {
        if (menu.Update())
        {
            menu.Close();
            Closed = true;
        }
    }

    public void OnGUI()
    {
        Rect screen = new Rect(0, 0, Screen.width, Screen.height);
        if (frontEnd.achievementsBackground != null)
        {
            GUI.DrawTexture(screen, frontEnd.achievementsBackground, ScaleMode.ScaleAndCrop);
        }
        menu.OnGUI();
    }

    public IGameMenuSubPage OpenPage(string key)
    {
        return key == GameOptions.MusicPageKey ? new MusicSetupPage(menu, frontEnd, FrontEnd.StartTitleMusic) : null;
    }

    // The title screen has no pages of saves.
    public void OnTabShown(GameMenu.ETab tab) { }

    public void UpdatePage(GameMenu.ETab tab, GameMenu.Nav nav) { }

    public void DrawPage(GameMenu.ETab tab, Rect list, Rect detail) { }

    public Hint[] PageHints(GameMenu.ETab tab, bool pad) => new Hint[0];

    public string[] PageButtons(GameMenu.ETab tab) => new string[0];

    public bool PageButtonEnabled(GameMenu.ETab tab, int button) => false;

    public void PressPageButton(GameMenu.ETab tab, int button) { }

    // Main Menu, as in a game, here back to the title menu with nothing to lose: the user's request of
    // 15 November 2026, the menu having no other button to leave by.
    public string EndButton => "Main Menu";

    public void PressEndButton()
    {
        if (frontEnd.backSelection != null)
        {
            Utils.PlayClip2d(frontEnd.backSelection);
        }
        menu.Close();
        Closed = true;
    }

    public void DrawEndButtonPadIcon(Rect icon) { }
}
