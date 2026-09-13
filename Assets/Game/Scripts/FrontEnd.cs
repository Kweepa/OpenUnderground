using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class FrontEnd : MonoBehaviour
{
    // Shown in the title menu corner and startup logs; update when shipping.
    public static string DisplayVersion = "v3.2";

    public CutscenePlayer introCutscene;
    public CutscenePlayer creditsCutscene;
    public Credits credits;
    public CreateCharacter createCharacter;
    public DataLoader dataLoader;
    public CritterLoader critterLoader;
    public ParticleSpawner particleSpawner;
    public LevelLoader levelLoader;
    public PlayerObject player;
    public MusicPlayer musicPlayer;

    public AudioClip moveSelection;
    public AudioClip makeSelection;
    public AudioClip backSelection;
    
    [Header("Music")]
    [Tooltip("XMI music file to play on the title screen (e.g., UW01.XMI)")]
    public string titleMusicTrack = "UW01.XMI";

    public GUIStyle style;

    public Texture2D[] achievementsButton;
    /// <summary>Title-menu Quit art: [0]=Lit (unselected), [1]=Unlit (selected), same order as achievementsButton.</summary>
    public Texture2D[] quitButton;
    public Texture2D achievementsBackground;
    public GUIStyle achievementHeaderStyle;
    
    public Texture2D bButton;
    public Texture2D yButton;
    public GUIStyle buttonLabelStyle;

    [Header("Keyboard icons (Achievements)")]
    public Texture2D blankKeyTexture;
    public Texture2D escapeKeyTexture;
    public GUIStyle keyLetterStyle;
    
    public Texture2D savePanelBackground;
    public GUIStyle savePanelText;

    private Texture2D[] openingScreen;

    private double juice;

    private enum EState
    {
        Initialize,
        Start,
        Menu,
        LoadMenu,
        Introduction,
        Credits,
        PortCredits,
        CreateCharacter,
        Achievements,
        Done
    }

    private EState state = EState.Initialize;
    private CutscenePlayer cutscene;
    private CreateCharacter creator;
    private int menuIndex;
    private float fadeSpeed;
    private float fadeAlpha;
    private float stateTime;
    private int palCycle;

    private bool revealAchievements;

    private void OnDestroy()
    {
        if (MusicPlayer.Instance != null)
            MusicPlayer.Instance.SetMusicDuckMultiplier(1f);
    }

    private void FadeIn()
    {
        stateTime = 0.0f;
        fadeSpeed = -2.0f;
        fadeAlpha = 1.0f;
    }

    private void UpdateFade()
    {
        stateTime += Time.deltaTime;
        fadeAlpha += Time.deltaTime * fadeSpeed;
        fadeAlpha = Math.Clamp(fadeAlpha, 0.0f, 1.0f);
    }

    [System.NonSerialized] private Texture2D[] dropShadows;
    [System.NonSerialized] private Texture2D[] buttons;

    // The load list is whatever is on disk, newest first, so it has no fixed length and it scrolls.
    // loadScroll is the first row drawn.
	private SaveGameManager.SaveSlotInfo[] saves = new SaveGameManager.SaveSlotInfo[0];
    private Texture2D[] saveScreenshots = new Texture2D[0];
    private int loadScroll;

    /// <summary>How many save rows the menu art has room for.</summary>
    private const int LOAD_MENU_VISIBLE_ROWS = 10;
    private string pendingLoadSlot;
    private bool hasSaves = false;

    private const float OpeningScreenCropTop = 27f;
    private const float OpeningScreenVirtualHeight = 200f;

    private int MenuItemCount()
    {
        return hasSaves ? 6 : 5;
    }

    /// <summary>
    /// Maps visible menu index to fixed texture slot (0–5). Without saves, index 4 is Quit (slot 5).
    /// </summary>
    private int MenuItemToSlot(int menuItemIndex)
    {
        if (hasSaves)
        {
            return menuItemIndex;
        }
        if (menuItemIndex < 4)
        {
            return menuItemIndex;
        }
        return 5;
    }

    private Texture2D GetMenuItemTexture(int i)
    {
        int item = i / 2;
        int variant = i & 1;
        switch (item)
        {
        case 0:
            return DataLoader.sDataLoader.opbtnTex[variant];
        case 1:
            return DataLoader.sDataLoader.opbtnTex[2 + variant];
        case 2:
            return DataLoader.sDataLoader.opbtnTex[4 + variant];
        case 3:
            return achievementsButton[variant];
        case 4:
            return DataLoader.sDataLoader.opbtnTex[6 + variant];
        default:
            return quitButton[variant];
        }
    }

    private void CreateDropShadows()
    {
        // Reallocate each time: Unity may deserialize older smaller array sizes onto the MonoBehaviour.
        buttons = new Texture2D[12];
        dropShadows = new Texture2D[6];

        for (int i = 0; i < 12; ++i)
        {
            Texture2D a = GetMenuItemTexture(i);
            Texture2D b = new Texture2D(a.width, a.height);
            Color[] aPix = a.GetPixels();
            Color[] bPix = new Color[a.width * a.height];
            Texture2D d = null;
            Color[] dPix = null;
            if ((i & 1) > 0)
            {
                d = new Texture2D(a.width, a.height);
                dPix = new Color[a.width * a.height];
            }

            for (int p = 0; p < aPix.Length; ++p)
            {
                bPix[p] = aPix[p];
                if (bPix[p].r <= bPix[p].b)
                {
                    bPix[p].a = 0;
                    if (d != null)
                    {
                        dPix[p].a = 0;
                    }
                }
                else if (d != null)
                {
                    dPix[p] = Color.black;
                }
            }
            b.SetPixels(bPix);
            b.wrapMode = TextureWrapMode.Clamp;
            b.filterMode = FilterMode.Point;
            b.Apply();
            buttons[i] = b;

            if (d != null)
            {
                d.SetPixels(dPix);
                d.wrapMode = TextureWrapMode.Clamp;
                d.filterMode = FilterMode.Point;
                d.Apply();
                dropShadows[i / 2] = d;
            }
        }
    }
    
    public void Update()
    {
        if (state != EState.Done)
        {
            GameplayCursorPolicy.ApplyMenu();
        }

        switch (state)
        {
        case EState.Initialize:
            StringLoader.LoadStrings();
            if (DataLoader.sDataLoader == null && dataLoader != null)
            {
                Instantiate(dataLoader);
            }
            if (CritterLoader.sCritterLoader == null && critterLoader != null)
            {
                Instantiate(critterLoader);
            }
            if (ParticleSpawner.sParticleSpawner == null && particleSpawner != null)
            {
                Instantiate(particleSpawner);
            }
            if (MusicPlayer.Instance == null && musicPlayer != null)
            {
                Instantiate(musicPlayer);
            }
            state = EState.Start;
            break;
        case EState.Start:
            openingScreen = GraphicsLoader.ReadBYT("../Data/opscr.byt", 2, 64, 64);
            CreateDropShadows();
            
            // Check if saves exist (lightweight check, no unzipping)
            CheckSavesExist();
            
            // Start title music
            if (MusicPlayer.Instance != null && !string.IsNullOrEmpty(titleMusicTrack) && MusicPlayer.Instance.currentTrack.ToLower() != titleMusicTrack.ToLower())
            {
                MusicPlayer.Instance.SwitchTrack(titleMusicTrack, true);
            }
            FadeIn();
            state = EState.Menu;
            break;
        case EState.Menu:
            palCycle = ((int)(10 * stateTime)) % openingScreen.Length;
            UpdateFade();
            // use inputs to highlight and select options
            UpdateMenu();
            break;
        case EState.LoadMenu:
            palCycle = ((int)(10 * stateTime)) % openingScreen.Length;
            UpdateFade();
            UpdateLoadMenu();
            break;
        case EState.Introduction:
            if (cutscene == null)
            {
                state = EState.Menu;
                if (MusicPlayer.Instance != null)
                    MusicPlayer.Instance.SetMusicDuckMultiplier(1f);
            }
            break;
        case EState.Credits:
            if (cutscene == null)
            {
                state = EState.PortCredits;
                credits.Play();
                credits.enabled = true;
            }
            break;
        case EState.PortCredits:
            if (!credits.isActiveAndEnabled)
            {
                state = EState.Menu;
            }
            break;
        case EState.CreateCharacter:
            if (creator == null)
            {
                // Check if character creation was cancelled (went back to menu)
                if (CreateCharacter.wasCancelled)
                {
                    CreateCharacter.wasCancelled = false; // Reset flag
                    state = EState.Menu;
                }
                else
                {
                    StartCoroutine(StartNewGame());
                    state = EState.Done;
                }
            }
            break;
        case EState.Achievements:
            GameInput.RefreshLastActiveDevice();
            if (Gamepad.current?.bButton.wasPressedThisFrame ?? false)
                BackFromAchievements();
            else if (Gamepad.current?.yButton.wasPressedThisFrame ?? false)
                ToggleRevealAchievements();

            // Keyboard support
            if (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)
                BackFromAchievements();
            else if (Keyboard.current?.tKey.wasPressedThisFrame ?? false)
                ToggleRevealAchievements();
            break;
        }
    }

    private void BackFromAchievements()
    {
        if (backSelection != null)
            Utils.PlayClip2d(backSelection);
        state = EState.Menu;
    }

    private void ToggleRevealAchievements()
    {
        if (makeSelection != null)
            Utils.PlayClip2d(makeSelection);
        revealAchievements = !revealAchievements;
    }

	private void CheckSavesExist()
	{
		// Lightweight check: just see if any save files exist without unzipping them
		try
		{
			string savesDirectoryPath = Application.persistentDataPath + "/Saves";
			if (Directory.Exists(savesDirectoryPath))
			{
				string[] files = Directory.GetFiles(savesDirectoryPath, "*.json.gz");
				hasSaves = files.Length > 0;
			}
			else
			{
				hasSaves = false;
			}
		}
		catch
		{
			hasSaves = false;
		}
	}

	private void RefreshSaves()
	{
        // Every save on disk, newest first. No new save row: this list only loads.
        menuIndex = SaveUIHelper.RefreshSaves(ref saves, ref saveScreenshots, menuIndex, false);
	}

    /// <summary>
    /// Opens the load list on the newest save and puts the pointer on it.
    /// </summary>
    /// <remarks>
    /// The newest save is the one a player coming back to the game almost always wants, and the
    /// list is now sorted so that it is the top row. Moving the pointer there as well means that
    /// someone who clicks Journey Onward and keeps clicking loads that game rather than whatever
    /// happened to be under the cursor. Only for a player on mouse and keyboard: taking the pointer
    /// away from someone on a gamepad would be rude and pointless.
    /// </remarks>
    private void OpenLoadMenu()
    {
        RefreshSaves();
        state = EState.LoadMenu;
        menuIndex = 0;
        loadScroll = 0;

        if (saves.Length == 0 || GameInput.LastActiveDevice == GameInputDevice.Gamepad)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        Rect row = GetLoadSlotRectGui(0);
        // GUI space counts down from the top, the mouse counts up from the bottom.
        float guiY = row.y + row.height * 0.5f;
        mouse.WarpCursorPosition(new Vector2(row.x + row.width * 0.5f,
            UnityEngine.Device.Screen.height - guiY));
    }

    /// <summary>Moves the load list selection, keeping it inside the window that is drawn.</summary>
    private void MoveLoadSelection(int delta)
    {
        if (saves.Length == 0)
        {
            menuIndex = 0;
            loadScroll = 0;
            return;
        }

        menuIndex = ((menuIndex + delta) % saves.Length + saves.Length) % saves.Length;
        ClampLoadScroll();
    }

    private void ClampLoadScroll()
    {
        int maxScroll = Mathf.Max(0, saves.Length - LOAD_MENU_VISIBLE_ROWS);
        if (menuIndex < loadScroll)
        {
            loadScroll = menuIndex;
        }
        else if (menuIndex >= loadScroll + LOAD_MENU_VISIBLE_ROWS)
        {
            loadScroll = menuIndex - LOAD_MENU_VISIBLE_ROWS + 1;
        }

        loadScroll = Mathf.Clamp(loadScroll, 0, maxScroll);
    }

    private void UpdateMenu()
    {
        GameInput.RefreshLastActiveDevice();

        int numItems = MenuItemCount();
        if (Gamepad.current?.dpad.down.wasPressedThisFrame ?? false)
        {
            menuIndex = (menuIndex + 1) % numItems;
            juice = Time.timeAsDouble;
            if (moveSelection != null)
            {
                Utils.PlayClip2d(moveSelection);
            }
        }
        else if (Gamepad.current?.dpad.up.wasPressedThisFrame ?? false)
        {
            menuIndex = (menuIndex + numItems - 1) % numItems;
            juice = Time.timeAsDouble;
            if (moveSelection != null)
            {
                Utils.PlayClip2d(moveSelection);
            }
        }
        else if (Gamepad.current?.aButton.wasPressedThisFrame ?? false)
        {
            ActivateMenuSelection_Gamepad();
        }
        else
        {
            UpdateMenuMouseKeyboard(numItems);
        }
    }

    private void ActivateMenuSelection_Gamepad()
    {
        bool debugSkipCreateCharacter = Gamepad.current?.rightTrigger.isPressed ?? false;
        ActivateMenuSelection(debugSkipCreateCharacter);
    }

    private void ActivateMenuSelection(bool debugSkipCreateCharacter)
    {
        if (makeSelection != null)
        {
            Utils.PlayClip2d(makeSelection);
        }
        switch (menuIndex)
        {
        case 0:
            state = EState.Introduction;
            cutscene = Instantiate(introCutscene);
            if (MusicPlayer.Instance != null)
                MusicPlayer.Instance.SetMusicDuckMultiplier(0.5f);
            break;
        case 1:
            if (debugSkipCreateCharacter)
            {
                StartCoroutine(StartNewGame());
                state = EState.Done;
            }
            else
            {
                state = EState.CreateCharacter;
                creator = Instantiate(createCharacter);
            }
            break;
        case 2:
            state = EState.Credits;
            if (creditsCutscene != null)
            {
                cutscene = Instantiate(creditsCutscene);
            }
            break;
        case 3:
            state = EState.Achievements;
            revealAchievements = false;
            break;
        case 4:
            if (hasSaves)
            {
                OpenLoadMenu();
            }
            else
            {
                QuitApplication();
            }
            break;
        case 5:
            QuitApplication();
            break;
        }
    }

    private static void QuitApplication()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private Rect GetMenuItemRectGui(int i, float xs, float ys)
    {
        // Use the un-juiced rect for stable hover hit-testing.
        int slot = MenuItemToSlot(i);
        Texture t = buttons[2 * slot];
        return new Rect(160 * xs - (t.width / 2f) * xs, buttonY[i] * ys - (t.height / 2f) * ys, t.width * xs, t.height * ys);
    }

    private void SetMenuIndex(int newIndex)
    {
        if (newIndex == menuIndex)
            return;
        menuIndex = newIndex;
        juice = Time.timeAsDouble;
        if (moveSelection != null)
            Utils.PlayClip2d(moveSelection);
    }

    private void UpdateMenuMouseKeyboard(int numItems)
    {
        GameInput.RefreshLastActiveDevice();

        // If the last real input was gamepad, ignore mouse hover selection.
        // Mouse takes focus only on click/scroll; keyboard takes focus on key press.
        bool ignoreHover = GameInput.LastActiveDevice == GameInputDevice.Gamepad;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.downArrowKey.wasPressedThisFrame)
            {
                SetMenuIndex((menuIndex + 1) % numItems);
                return;
            }
            if (kb.upArrowKey.wasPressedThisFrame)
            {
                SetMenuIndex((menuIndex + numItems - 1) % numItems);
                return;
            }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                ActivateMenuSelection(debugSkipCreateCharacter: false);
                return;
            }
        }

        Mouse m = Mouse.current;
        if (m == null)
            return;

        if (ignoreHover && !(m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame))
            return;

        float xs = Screen.width / 320.0f;
        float ys = Screen.height / 200.0f;
        Vector2 guiMouse = GuiInput.ScreenToGuiMouse(m.position.ReadValue());

        for (int i = 0; i < numItems; i++)
        {
            Rect r = GetMenuItemRectGui(i, xs, ys);
            if (r.Contains(guiMouse))
            {
                SetMenuIndex(i);
                if (m.leftButton.wasPressedThisFrame)
                {
                    bool skipCreateCharacter =
                        i == 1
                        && kb != null
                        && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
                    ActivateMenuSelection(debugSkipCreateCharacter: skipCreateCharacter);
                }
                return;
            }
        }
    }

    private void UpdateLoadMenu()
    {
        GameInput.RefreshLastActiveDevice();

        if (Gamepad.current?.bButton.wasPressedThisFrame ?? false)
        {
            if (backSelection != null)
            {
                Utils.PlayClip2d(backSelection);
            }
            state = EState.Menu;
            menuIndex = 0;
        }
        else if (Gamepad.current?.dpad.down.wasPressedThisFrame ?? false)
        {
            MoveLoadSelection(1);
            juice = Time.timeAsDouble;
            if (moveSelection != null)
            {
                Utils.PlayClip2d(moveSelection);
            }
        }
        else if (Gamepad.current?.dpad.up.wasPressedThisFrame ?? false)
        {
            MoveLoadSelection(-1);
            juice = Time.timeAsDouble;
            if (moveSelection != null)
            {
                Utils.PlayClip2d(moveSelection);
            }
        }
        else if (Gamepad.current?.aButton.wasPressedThisFrame ?? false)
        {
            ActivateLoadMenuSelection();
        }
        else
        {
            UpdateLoadMenuMouseKeyboard();
        }
        
    }

    private void ActivateLoadMenuSelection()
    {
        if (makeSelection != null)
            Utils.PlayClip2d(makeSelection);
        if (menuIndex >= 0 && menuIndex < saves.Length && !string.IsNullOrEmpty(saves[menuIndex].slotName))
        {
            pendingLoadSlot = saves[menuIndex].slotName;
            StartCoroutine(LoadAGame());
            state = EState.Done;
        }
    }

    private void UpdateLoadMenuMouseKeyboard()
    {
        GameInput.RefreshLastActiveDevice();

        bool ignoreHover = GameInput.LastActiveDevice == GameInputDevice.Gamepad;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (backSelection != null)
                    Utils.PlayClip2d(backSelection);
                state = EState.Menu;
                menuIndex = 0;
                return;
            }
            if (kb.downArrowKey.wasPressedThisFrame)
            {
                MoveLoadSelection(1);
                return;
            }
            if (kb.upArrowKey.wasPressedThisFrame)
            {
                MoveLoadSelection(-1);
                return;
            }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                ActivateLoadMenuSelection();
                return;
            }
        }

        Mouse m = Mouse.current;
        if (m == null)
            return;

        // The wheel scrolls the list without moving the selection.
        float wheel = m.scroll.ReadValue().y;
        if (wheel != 0.0f && saves.Length > LOAD_MENU_VISIBLE_ROWS)
        {
            int maxScroll = saves.Length - LOAD_MENU_VISIBLE_ROWS;
            loadScroll = Mathf.Clamp(loadScroll + (wheel > 0.0f ? -1 : 1), 0, maxScroll);
        }

        if (ignoreHover && !(m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame))
            return;

        Vector2 guiMouse = GuiInput.ScreenToGuiMouse(m.position.ReadValue());

        for (int i = loadScroll; i < saves.Length && i < loadScroll + LOAD_MENU_VISIBLE_ROWS; i++)
        {
            Rect r = GetLoadSlotRectGui(i - loadScroll);
            if (r.Contains(guiMouse))
            {
                SetMenuIndex(i);
                if (m.leftButton.wasPressedThisFrame)
                {
                    ActivateLoadMenuSelection();
                }
                return;
            }
        }
    }

    // --- Screen layout ---------------------------------------------------------------------
    //
    // The load screen is laid out in the pixels of a 1920x1080 screen and drawn at one uniform
    // scale, so it keeps its shape on any monitor instead of stretching with the aspect ratio.
    // 1920x1080 is the size this project's interface is already drawn at: it is the default
    // window size in the project settings, and the in game panels are sized in raw pixels to
    // suit it, so there the scale is 1 and nothing moves. Below that everything shrinks
    // together, down to 1280x800, the smallest screen these menus are meant to fit - it is a
    // Steam Deck, and 800 is the height below which panels run off the bottom today. Above it
    // everything grows together until MaxUiScale, past which the border grows instead of the
    // menu, so a very large display does not get a billboard.
    private const float RefScreenWidth = 1920f;
    private const float RefScreenHeight = 1080f;
    private const float MaxUiScale = 2.0f;

    // The load screen hangs off the title painted behind it: the list starts where the title
    // starts, and the preview ends where the title ends. The art is stretched across the whole
    // screen rect, so those two edges are the same fraction of the width at any resolution.
    // Measured on the image rather than judged by eye: the title's letters are the colours the
    // screen cycles - palette indices 64 to 127, the range ReadBYT is told to animate - and they
    // occupy columns 25 to 295 of the 320 the picture is wide, in one band on rows 35 to 61.
    private const float TitleLeftFraction = 25f / 320f;
    private const float TitleRightFraction = 296f / 320f;

    // Two characters of air outside the block, so the list and the preview sit just inside the
    // title rather than flush against its ends, which read as a mistake once it was seen in the
    // game. Two characters of the row font is about one em, which is the font size itself.
    private const float LoadBlockInset = LoadRowFontSize;

    // The list takes whatever the preview leaves, down to this floor.
    private const float LoadListMinWidth = 320f;
    private const float LoadRowHeight = 65f;
    private const float LoadRowFontSize = 54f;
    private const float LoadBlockGap = 40f;
    private const float LoadBlockTop = 320f;

    private static float UiScale()
    {
        return Mathf.Min(
            Mathf.Min(Screen.width / RefScreenWidth, Screen.height / RefScreenHeight),
            MaxUiScale);
    }

    /// <summary>
    /// Where the load screen goes, in screen pixels: the list, the preview panel beside it, and
    /// the scale their contents are drawn at.
    /// </summary>
    private void GetLoadMenuLayout(out float scale, out Rect list, out Rect panel)
    {
        scale = UiScale();

        // The panel keeps its shape: its size is in the same reference pixels its insides are
        // laid out in, scaled as one. Only where it sits comes from the title behind it.
        float panelW = savePanelBackground != null ? 8.0f * savePanelBackground.width * scale : 0.0f;
        float panelH = savePanelBackground != null ? 4.0f * savePanelBackground.height * scale : 0.0f;

        float left = TitleLeftFraction * Screen.width + LoadBlockInset * scale;
        float right = TitleRightFraction * Screen.width - LoadBlockInset * scale;
        float top = LoadBlockTop * scale;

        float listW = right - left;
        if (panelW > 0.0f)
        {
            listW = Mathf.Max(LoadListMinWidth * scale, listW - panelW - LoadBlockGap * scale);
        }

        list = new Rect(left, top, listW, LOAD_MENU_VISIBLE_ROWS * LoadRowHeight * scale);
        panel = panelW > 0.0f ? new Rect(right - panelW, top, panelW, panelH) : default;
    }

    /// <summary>
    /// The rect of a row ON SCREEN, counting from the top of the visible window rather than from
    /// the start of the list: with the list scrolling, the two are not the same.
    /// </summary>
    private Rect GetLoadSlotRectGui(int row)
    {
        GetLoadMenuLayout(out float scale, out Rect list, out _);
        float rowH = LoadRowHeight * scale;
        return new Rect(list.x, list.y + row * rowH, list.width, rowH);
    }

    private IEnumerator StartNewGame()
    {
        // wait a frame for initialization
        yield return null;

        // Untag FrontEnd's camera so we don't have two Main cameras
        Camera frontEndCamera = GetComponentInChildren<Camera>();
        if (frontEndCamera != null)
        {
            frontEndCamera.tag = "Untagged";
        }
        
        Instantiate(levelLoader);
        LevelLoader.sLevelLoader.loadedLevel = Cheats.sCheats.level;
        Instantiate(player);

        // Destroy FrontEnd's AudioListener now that Player's listener is active
        AudioListener frontEndListener = GetComponentInChildren<AudioListener>();
        if (frontEndListener != null)
        {
            Destroy(frontEndListener);
        }

        yield return null;
        
        // New game - explicitly load the starting level
        LevelLoader.sLevelLoader.LoadLevel(Cheats.sCheats.level);
        
        Destroy(gameObject);
    }

    private IEnumerator LoadAGame()
    {
        // wait a frame for initialization
        yield return null;

        // Untag FrontEnd's camera so we don't have two Main cameras
        Camera frontEndCamera = GetComponentInChildren<Camera>();
        if (frontEndCamera != null)
        {
            frontEndCamera.tag = "Untagged";
        }
        
        Instantiate(levelLoader);
        Instantiate(player);

        // Destroy FrontEnd's AudioListener now that Player's listener is active
        AudioListener frontEndListener = GetComponentInChildren<AudioListener>();
        if (frontEndListener != null)
        {
            Destroy(frontEndListener);
        }

        yield return null;
        
        if (!string.IsNullOrEmpty(pendingLoadSlot) && SaveGameManager.sInstance != null)
        {
            // Loading from save - SaveGameManager handles level loading
            SaveGameManager.sInstance.LoadGameFromSlot(pendingLoadSlot);
            pendingLoadSlot = null;
        }
        
        Destroy(gameObject);
    }
    
    private static int[] buttonY = { 64, 87, 111, 135, 158, 181 };

    private Color selectColor = new Color32(255, 213, 64, 255);
    private Color unselectColor = new Color32(187, 123, 1, 255);

    public float subY = 37;
    public int subSize = 160;

    public Rect shinyLogoRect;

    private void DrawOpeningScreenCropped(Rect frontEndRect)
    {
        float visibleVirtualHeight = OpeningScreenVirtualHeight - OpeningScreenCropTop;
        float uvHeight = visibleVirtualHeight / OpeningScreenVirtualHeight;
        // Crop top rows in UV space, stretch the remainder across the full screen rect.
        GUI.DrawTextureWithTexCoords(frontEndRect, openingScreen[palCycle], new Rect(0f, 0f, 1f, uvHeight));
    }

    private void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.FrontEnd;

        switch (state)
        {
        case EState.Menu:
        case EState.LoadMenu:
            {
                Rect frontEndRect = Screen.safeArea;
                GuiInput.RegisterBlockingRect(frontEndRect);
                float xs = Screen.width / 320.0f;
                float ys = Screen.height / 200.0f;
                DrawOpeningScreenCropped(frontEndRect);

                if (state == EState.Menu)
                {
                    float juiceScale = 1.0f;
                    float juiceTime = (float)(Time.timeAsDouble - juice);
                    if (juiceTime < 0.5f)
                    {
                        juiceScale = 1.0f + 0.4f * Mathf.Sin(10.0f * juiceTime) * Mathf.Exp(-10.0f * juiceTime);
                    }

                    int numItems = MenuItemCount();
                    for (int i = 0; i < numItems; ++i)
                    {
                        int slot = MenuItemToSlot(i);
                        float xjs = menuIndex == i ? juiceScale * xs : xs;
                        float yjs = menuIndex == i ? juiceScale * ys : ys;
                        Texture t = dropShadows[slot];
                        Rect r = new Rect(160 * xs - (t.width / 2 - 1) * xjs, buttonY[i] * ys - (t.height / 2 - 3) * yjs, t.width * xjs, t.height * yjs);
                        GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true);

                        t = buttons[2 * slot + (menuIndex == i ? 1 : 0)];
                        r = new Rect(160 * xs - t.width / 2 * xjs, buttonY[i] * ys - t.height / 2 * yjs, t.width * xjs, t.height * yjs);
                        GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true);
                    }
                    
                    GUI.Label(new Rect(10, 0, 100, 20), DisplayVersion);
                }
                else if (state == EState.LoadMenu)
                {
                    GetLoadMenuLayout(out float uiScale, out Rect listRect, out Rect panelRect);

                    int rowFontSize = (int)(LoadRowFontSize * uiScale);
                    style.fontSize = rowFontSize;
                    style.alignment = TextAnchor.UpperLeft;

                    for (int row = 0; row < LOAD_MENU_VISIBLE_ROWS; ++row)
                    {
                        int i = loadScroll + row;
                        if (i >= saves.Length)
                        {
                            break;
                        }

                        Rect r = new Rect(listRect.x, listRect.y + row * LoadRowHeight * uiScale,
                            listRect.width, LoadRowHeight * uiScale);

                        style.normal.textColor = menuIndex == i ? selectColor : unselectColor;
                        style.alignment = TextAnchor.UpperLeft;

                        // A name of wide letters is written smaller rather than allowed off the
                        // end of the list. The row keeps its height either way, and the arrows
                        // below are drawn at the full size again.
                        string label = saves[i].displayName ?? saves[i].slotName ?? "";
                        // One em of the row less, which is where the scroll arrow sits when there
                        // is one: a long name stops short of it instead of running underneath.
                        style.fontSize = SaveUIHelper.FitFontSize(style, label, r.width - rowFontSize,
                            rowFontSize, Mathf.Max(10, rowFontSize / 2));
                        GUI.Label(r, label, style);
                        style.fontSize = rowFontSize;

                        // There is more list above or below: say so on the first and last row drawn.
                        bool moreAbove = row == 0 && loadScroll > 0;
                        bool moreBelow = row == LOAD_MENU_VISIBLE_ROWS - 1 && i < saves.Length - 1;
                        if (moreAbove || moreBelow)
                        {
                            style.alignment = TextAnchor.UpperRight;
                            // The colour a selected row is written in, not the dim one: an arrow
                            // is the only thing saying the list runs on, and dim it was easy to
                            // miss. One pixel further right than the text, so it sits clear of a
                            // name that has been shrunk to fit. The same two arrows, in the same
                            // colour and the same place, as the in game save panel.
                            style.normal.textColor = selectColor;
                            // Smaller than the rows. The glyph is the same one it always was, but
                            // in the bright colour it reads as heavier than it measures, and at
                            // the row's own size it took over the list. Seven tenths puts its
                            // weight back where it was while keeping it easy to see.
                            style.fontSize = Mathf.Max(8, (rowFontSize * 7) / 10);
                            GUI.Label(r, moreAbove ? "\u25b2" : "\u25bc", style);
                            style.fontSize = rowFontSize;
                            style.alignment = TextAnchor.UpperLeft;
                        }
                    }

                    SaveUIHelper.EnsureScreenshotLoaded(saveScreenshots, saves, menuIndex);

                    // The preview of the selected save, beside the list and starting on the same
                    // line. Its insides - the screenshot and the two lines of text - are laid out
                    // in reference pixels, so the whole panel is drawn through a scaled matrix
                    // rather than stretched into a rect: everything inside keeps its place.
                    if (savePanelBackground != null)
                    {
                        GuiInput.RegisterBlockingRect(panelRect);

                        Matrix4x4 previousMatrix = GUI.matrix;
                        GUI.matrix = Matrix4x4.TRS(
                            new Vector3(panelRect.x, panelRect.y, 0.0f),
                            Quaternion.identity,
                            new Vector3(uiScale, uiScale, 1.0f));

                        SaveUIHelper.DrawSaveSlotDetailPanel(
                            0.0f,
                            0.0f,
                            savePanelBackground,
                            savePanelText,
                            selectColor,
                            saves,
                            saveScreenshots,
                            menuIndex);

                        GUI.matrix = previousMatrix;

                        GuiInput.TryConsumeClickInPanel(panelRect);
                    }
                }

                GuiInput.TryConsumeClickInPanel(frontEndRect);
            }
            break;
        case EState.Achievements:
            foreach (Achievements ach in GameObject.FindObjectsByType<Achievements>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                GUI.DrawTexture(Screen.safeArea, achievementsBackground);
                GUI.Label(Screen.safeArea, "Achievements", achievementHeaderStyle);
                // draw all achievements is here
                string numAchieved = ach.DrawAllAchievements(revealAchievements);
                GUI.Label(new Rect(50, Screen.height - 50, 100, 24), numAchieved, buttonLabelStyle);

                bool useGamepadIcons = GameInput.LastActiveDevice == GameInputDevice.Gamepad;

                Rect toggleIconRect = new Rect(Screen.width / 2 - 100, Screen.height - 50, 24, 24);
                Texture2D toggleIcon = useGamepadIcons ? yButton : blankKeyTexture;
                GUI.DrawTexture(toggleIconRect, toggleIcon);
                if (!useGamepadIcons)
                    GUI.Label(toggleIconRect, "T", keyLetterStyle);
                string label = revealAchievements ? "Hide unattained achievements" : "Reveal unattained achievements";
                Rect toggleTextRect = new Rect(Screen.width / 2 - 60, Screen.height - 50, 240, 24);
                GUI.Label(toggleTextRect, label, buttonLabelStyle);

                Rect backIconRect = new Rect(Screen.width - 140, Screen.height - 50, 24, 24);
                Texture2D backIcon = useGamepadIcons ? bButton : escapeKeyTexture;
                GUI.DrawTexture(backIconRect, backIcon);
                Rect backTextRect = new Rect(Screen.width - 100, Screen.height - 50, 100, 24);
                GUI.Label(backTextRect, "Back", buttonLabelStyle);

                // Mouse click support: click on icon or text.
                if (GuiInput.TryConsumeClickInRect(new Rect(toggleIconRect.x, toggleIconRect.y, toggleTextRect.xMax - toggleIconRect.x, 28)))
                {
                    ToggleRevealAchievements();
                }
                else if (GuiInput.TryConsumeClickInRect(new Rect(backIconRect.x, backIconRect.y, backTextRect.xMax - backIconRect.x, 28)))
                {
                    BackFromAchievements();
                }
                break;
            }
            break;
        }
        Utils.DrawFade(fadeAlpha);
    }
}
