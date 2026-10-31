using UnityEngine;

/// <summary>
/// Journey Onward on the title menu: the game menu with its LOAD tab alone, the same list of saves a
/// game shows, to load one.
/// </summary>
/// <remarks>
/// The menu's host for loading from the title screen, as SetupScreen is for the options there: the
/// title's rock behind, no way back to the title but closing, which goes back to the title menu.
/// Choosing a save hands it to FrontEnd, which starts the game from it. It took the place of the
/// title screen's own list of saves drawn on the rock (fix 433), so that the list looks and behaves
/// the same from the title and in a game.
/// </remarks>
public class TitleLoadScreen : IGameMenuHost
{
    private readonly FrontEnd frontEnd;
    private readonly GameMenu menu;
    private readonly SaveListPage page;

    /// <summary>True once the player has left the screen without loading; FrontEnd then goes back to the menu.</summary>
    public bool Closed { get; private set; }

    /// <summary>The save chosen, or null: FrontEnd loads it.</summary>
    public string ChosenSlot { get; private set; }

    public TitleLoadScreen(FrontEnd frontEnd)
    {
        this.frontEnd = frontEnd;
        page = new SaveListPage(() => menu, frontEnd.buttonLabelStyle, frontEnd.makeSelection);
        // the rock round the panel darkened, as on Setup
        menu = new GameMenu(SetupScreen.TitleSkin(frontEnd), this, new[] { GameMenu.ETab.Load }, true);
        // on the newest save, the one a player coming back to the game almost always wants; before Open(), whose
        // showing of the page takes it, or the next showing would take it and jump back there (fix 465)
        page.SelectTop();
        menu.Open(GameMenu.ETab.Load);
    }

    public void Update()
    {
        if (ChosenSlot != null)
        {
            return;
        }
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

    private void Choose()
    {
        if (ChosenSlot != null || !page.SelectedIsSave)
        {
            return;
        }
        menu.Close();
        ChosenSlot = page.Selected.slotName;
    }

    public void OnTabShown(GameMenu.ETab tab)
    {
        page.Show(false);
    }

    public void UpdatePage(GameMenu.ETab tab, GameMenu.Nav nav)
    {
        if (page.Update(nav))
        {
            Choose();
        }
    }

    public void DrawPage(GameMenu.ETab tab, Rect list, Rect detail)
    {
        page.Draw(list, detail);
    }

    public Hint[] PageHints(GameMenu.ETab tab, bool pad)
    {
        return pad
            ? new[]
            {
                new Hint("load", HintKey.Of(frontEnd.aButton)),
                new Hint("delete", HintKey.Of(frontEnd.yButton)),
                new Hint("back", HintKey.Of(frontEnd.bButton)),
            }
            : new[]
            {
                new Hint("load", HintKey.Keyboard("Enter")),
                new Hint("delete", HintKey.Keyboard("Del")),
                new Hint("back", HintKey.Of(frontEnd.escapeKeyTexture)),
            };
    }

    // Delete first, then Load, as on the game menu's LOAD (the user's rule of 7 November 2026: the autosaves are listed only
    // on a list for loading, so without it they could not be deleted)
    public string[] PageButtons(GameMenu.ETab tab) => new[] { "Delete", "Load" };

    public bool PageButtonEnabled(GameMenu.ETab tab, int button) => ChosenSlot == null && page.SelectedIsSave;

    public void PressPageButton(GameMenu.ETab tab, int button)
    {
        if (button == 0)
        {
            page.AskDelete();
        }
        else
        {
            Choose();
        }
    }

    // No way back to the title but closing, being on it; no pages of options.
    public string EndButton => null;

    public void PressEndButton() { }

    public void DrawEndButtonPadIcon(Rect icon) { }

    public IGameMenuSubPage OpenPage(string key) => null;
}
