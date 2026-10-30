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
    
    public GUIStyle style;

    public Texture2D[] achievementsButton;
    /// <summary>Title-menu Quit art: [0]=Lit (unselected), [1]=Unlit (selected), same order as achievementsButton.</summary>
    public Texture2D[] quitButton;
    /// <summary>Title-menu Setup art, in the same order.</summary>
    public Texture2D[] setupButton;
    public Texture2D achievementsBackground;
    public GUIStyle achievementHeaderStyle;
    
    public Texture2D bButton;
    public Texture2D yButton;
    // The other two face buttons, for hints.
    public Texture2D aButton;
    public Texture2D xButton;
    public GUIStyle buttonLabelStyle;

    [Header("Keyboard icons (Achievements)")]
    public Texture2D blankKeyTexture;
    public Texture2D escapeKeyTexture;
    public GUIStyle keyLetterStyle;

    [Header("Hints of the introduction and the acknowledgements")]
    public Texture2D leftMouseButtonTexture;
    public Texture2D rightMouseButtonTexture;
    public Texture2D dPadLeftRightTexture;
    
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
        Setup,
        Done
    }

    private EState state = EState.Initialize;

    /// <summary>Whether the title's main menu is on screen, for <see cref="SmallWindowWarning"/>.</summary>
    public bool ShowsMainMenu => state == EState.Menu;
    private SetupScreen setupScreen;
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
    // The first and the last-plus-one rows of each slot's letters, counted from the top of its image.
    [System.NonSerialized] private int[] glyphTop;
    [System.NonSerialized] private int[] glyphBottom;
    // The row just under the line the small letters stand on, counted from the top of the image.
    [System.NonSerialized] private int[] baselineRow;

    // The load list is whatever is on disk, newest first, so it has no fixed length and it scrolls.
    // loadScroll is the first row drawn.
	private SaveGameManager.SaveSlotInfo[] saves = new SaveGameManager.SaveSlotInfo[0];
    private Texture2D[] saveScreenshots = new Texture2D[0];
    private int loadScroll;

    /// <summary>The most save rows the list shows, on a screen 1080 reference pixels tall or more.</summary>
    private const int LOAD_MENU_MAX_ROWS = 10;
    private string pendingLoadSlot;
    private bool hasSaves = false;

    private const float OpeningScreenCropTop = 27f;
    private const float OpeningScreenVirtualHeight = 200f;

    // Texture slots in the order the menu shows them. Setup is slot 6 so that the slots of the
    // entries before it keep their numbers; without saves Journey Onward is not shown.
    private static readonly int[] slotsWithSaves = { 0, 1, 2, 3, SetupSlot, JourneyOnwardSlot, QuitSlot };
    private static readonly int[] slotsWithoutSaves = { 0, 1, 2, 3, SetupSlot, QuitSlot };

    private int MenuItemCount()
    {
        return hasSaves ? slotsWithSaves.Length : slotsWithoutSaves.Length;
    }

    /// <summary>
    /// Maps visible menu index to fixed texture slot (0–6).
    /// </summary>
    private int MenuItemToSlot(int menuItemIndex)
    {
        return hasSaves ? slotsWithSaves[menuItemIndex] : slotsWithoutSaves[menuItemIndex];
    }

    /// <summary>The middle of a menu entry's image on the 200-line screen.</summary>
    /// <remarks>
    /// The entries' baselines - the line the small letters stand on - are spread evenly between the
    /// first entry and the last, so a Setup image of any height lays out by itself. Measured on the
    /// letters and not on the images, whose empty margins differ: the original's are 18 to 23 rows
    /// high, Kweepa's 24. The places are always the seven of the menu with saves: without them
    /// Journey Onward is missing, Quit moves up into its place and the last place stays empty.
    /// </remarks>
    private float MenuRowY(int menuItemIndex)
    {
        int count = slotsWithSaves.Length;
        int first = slotsWithSaves[0];
        int last = slotsWithSaves[count - 1];
        float firstBaseline = MenuFirstLettersTop + (baselineRow[first] - glyphTop[first]) * MenuItemScale;
        float lastBaseline = MenuLastLettersBottom - (glyphBottom[last] - baselineRow[last]) * MenuItemScale;
        float baseline = Mathf.Lerp(firstBaseline, lastBaseline, menuItemIndex / (float)(count - 1))
            - PlaceRaise[menuItemIndex];

        int slot = MenuItemToSlot(menuItemIndex);
        return baseline + (buttons[2 * slot].height / 2f - baselineRow[slot]) * MenuItemScale;
    }

    // The entries are drawn at nine tenths of their size, so that seven fit with room between them.
    private const float MenuItemScale = 0.9f;

    // How far each of the seven places is raised above the even spacing, in lines of the 200-line
    // screen, chosen with the user on 25 October 2026: Journey Onward's descenders came within a line
    // of Quit's letters, so its place goes up two lines, which doubles that gap, and Setup's one, to
    // stay as clear of Journey Onward as before.
    private static readonly float[] PlaceRaise = { 0f, 0f, 0f, 0f, 1.0f, 2.05f, 0f };

    // Where the letters of the first entry start and those of the last end, chosen with the user on
    // 20 October 2026. The first: the title's letters end on row 61 of the opening screen, which is
    // line 40.5; Introduction's started 14.5 lines below that, and now half as far. The last: Quit's
    // letters ended 6 lines above the bottom, and now a third less.
    private const float MenuFirstLettersTop = 47.7f;
    private const float MenuLastLettersBottom = 196f;

    // Texture slots of the two entries the menu opens on, and of the last two.
    private const int CreateCharacterSlot = 1;
    private const int JourneyOnwardSlot = 4;
    private const int QuitSlot = 5;
    private const int SetupSlot = 6;
    private const int SlotCount = 7;

    /// <summary>The visible menu index of a texture slot, or -1 when that entry is not shown.</summary>
    /// <remarks>
    /// Searched rather than fixed, because the entries move up and down with what is shown:
    /// without saves Journey Onward is gone and Quit takes its place.
    /// </remarks>
    private int MenuIndexOfSlot(int slot)
    {
        int numItems = MenuItemCount();
        for (int i = 0; i < numItems; ++i)
        {
            if (MenuItemToSlot(i) == slot)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Selects Journey Onward when there are saves, otherwise Create Character, and puts the pointer
    /// on it.
    /// </summary>
    /// <remarks>
    /// Those are the entries a player wants when the title cutscene ends. Left on Introduction, a
    /// player clicking through the title screen lands in the intro cutscene. The menu order stays
    /// the original's.
    /// </remarks>
    private void SelectFirstMenuItem()
    {
        int index = MenuIndexOfSlot(JourneyOnwardSlot);
        if (index < 0)
        {
            index = MenuIndexOfSlot(CreateCharacterSlot);
        }
        if (index < 0)
        {
            return;
        }

        menuIndex = index;
        Rect item = GetMenuItemRectGui(index, Screen.width / 320.0f, Screen.height / 200.0f);
        WarpMouseToGui(item.center);
    }

    /// <summary>Moves the pointer to a point in GUI space, when there is a mouse.</summary>
    private static void WarpMouseToGui(Vector2 guiPoint)
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        // GUI space counts down from the top, the mouse counts up from the bottom.
        mouse.WarpCursorPosition(new Vector2(guiPoint.x, UnityEngine.Device.Screen.height - guiPoint.y));
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
        case 5:
            return quitButton[variant];
        default:
            return setupButton[variant];
        }
    }

    private void CreateDropShadows()
    {
        // Reallocate each time: Unity may deserialize older smaller array sizes onto the MonoBehaviour.
        buttons = new Texture2D[2 * SlotCount];
        dropShadows = new Texture2D[SlotCount];
        glyphTop = new int[SlotCount];
        glyphBottom = new int[SlotCount];
        baselineRow = new int[SlotCount];

        for (int i = 0; i < 2 * SlotCount; ++i)
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

            if ((i & 1) == 0)
            {
                MeasureLetters(bPix, a.width, a.height, out glyphTop[i / 2], out glyphBottom[i / 2], out baselineRow[i / 2]);
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
    
    /// <summary>
    /// Where an image's letters are, the pixels kept above, in rows counted from the top: the first,
    /// the one after the last, and the one after the baseline.
    /// </summary>
    /// <remarks>
    /// The baseline is where most columns of letters end: the small letters, and the capitals, stand
    /// on it, and only a few strokes - the tail of a y or a J - go below.
    /// </remarks>
    private static void MeasureLetters(Color[] pixels, int width, int height, out int top, out int bottom, out int baseline)
    {
        top = height;
        bottom = 0;
        int[] columnsEnding = new int[height + 1];
        for (int x = 0; x < width; ++x)
        {
            int lowest = -1;
            for (int row = 0; row < height; ++row)
            {
                // GetPixels() gives the bottom row first.
                if (pixels[(height - 1 - row) * width + x].a > 0f)
                {
                    top = Mathf.Min(top, row);
                    lowest = row;
                }
            }
            if (lowest >= 0)
            {
                bottom = Mathf.Max(bottom, lowest + 1);
                ++columnsEnding[lowest + 1];
            }
        }
        if (bottom <= top)
        {
            top = 0;
            bottom = height;
        }

        baseline = bottom;
        for (int row = 0; row <= height; ++row)
        {
            if (columnsEnding[row] > columnsEnding[baseline])
            {
                baseline = row;
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
            
            // Start title music, unless one of the title tracks is already playing
            StartTitleMusic();
            FadeIn();
            SelectFirstMenuItem();
            state = EState.Menu;
            break;
        case EState.Menu:
            palCycle = ((int)(10 * stateTime)) % openingScreen.Length;
            UpdateFade();
            // use inputs to highlight and select options, unless the small window warning has them
            if (!SmallWindowWarning.BlocksInput)
            {
                UpdateMenu();
            }
            break;
        case EState.LoadMenu:
            palCycle = ((int)(10 * stateTime)) % openingScreen.Length;
            UpdateFade();
            UpdateLoadMenu();
            break;
        case EState.Introduction:
            if (cutscene != null)
            {
                UpdateIntroductionKeys();
            }
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
        case EState.Setup:
            setupScreen.Update();
            if (setupScreen.Closed)
            {
                if (backSelection != null)
                    Utils.PlayClip2d(backSelection);
                setupScreen = null;
                state = EState.Menu;
                StartTitleMusic();
            }
            break;
        }
    }

    /// <summary>
    /// Plays one of the title tracks, looping, unless one of them is playing already: coming back
    /// from the Setup screen or from a game does not start it over.
    /// </summary>
    private static void StartTitleMusic()
    {
        MusicPlayer player = MusicPlayer.Instance;
        if (player == null || (player.IsPlaying && MusicConfig.Contains(EMusicSituation.Title, player.currentTrack)))
        {
            return;
        }

        string title = MusicConfig.Pick(EMusicSituation.Title);
        if (title != null)
        {
            player.SwitchTrack(title, true);
        }
        else
        {
            player.StopMusic();
        }
    }

    /// <summary>
    /// The introduction's own keys, besides the fast forward the cutscene has: a left click or A goes
    /// on to the next scene, Escape or Start goes back to the menu.
    /// </summary>
    /// <remarks>
    /// Here and not in CutscenePlayer, as the title cutscene's skip is in Logos, so that the dreams,
    /// the endings and the deaths are untouched. Where the cutscene waits for a press, the press is its
    /// own: it lets the cutscene go on, and does not skip a scene as well.
    /// </remarks>
    private void UpdateIntroductionKeys()
    {
        GameInput.RefreshLastActiveDevice();
        if (GameInput.EscapePressedThisFrame() || (GameInput.CurrentGamepad?.startButton.wasPressedThisFrame ?? false))
        {
            if (backSelection != null)
                Utils.PlayClip2d(backSelection);
            // CutscenePlayer.OnDestroy() gives back the clock. Cleared as well, because Destroy waits for
            // the end of the frame.
            Destroy(cutscene.gameObject);
            cutscene = null;
            return;
        }

        bool next = (GameInput.CurrentMouse?.leftButton.wasPressedThisFrame ?? false)
            || (GameInput.CurrentGamepad?.aButton.wasPressedThisFrame ?? false);
        if (next && !cutscene.WaitingForPress)
        {
            cutscene.SkipToNextScene();
        }

        if (next || PointerMoved())
        {
            hintsShownAt = Time.unscaledTime;
        }
    }

    private Vector2 lastPointer;

    private bool PointerMoved()
    {
        Mouse mouse = GameInput.CurrentMouse;
        if (mouse == null)
        {
            return false;
        }
        Vector2 now = mouse.position.ReadValue();
        bool moved = (now - lastPointer).sqrMagnitude > 4f;
        lastPointer = now;
        return moved;
    }

    // When the introduction's hints were last asked for: they show for HintsSeconds after that, so
    // they do not sit over the pictures for the whole of it.
    private float hintsShownAt;
    private const float HintsSeconds = 4f;

    /// <summary>
    /// The hints of the introduction and of the acknowledgements, drawn as the achievements page draws
    /// its own: the introduction's at the top, where the subtitles are not, fading out a few seconds
    /// after the last press or move of the pointer; the acknowledgements' at the bottom, for good.
    /// </summary>
    private void DrawScreenHints(bool introduction)
    {
        bool pad = GameInput.LastActiveDevice == GameInputDevice.Gamepad;
        Hint[] hints;
        if (introduction)
        {
            hints = pad
                ? new[]
                {
                    new Hint("next scene", HintKey.Of(aButton)),
                    new Hint("fast forward", HintKey.Of(bButton)),
                    new Hint("back", HintKey.PadButton("Start")),
                }
                : new[]
                {
                    new Hint("next scene", HintKey.Of(leftMouseButtonTexture)),
                    new Hint("fast forward", HintKey.Of(rightMouseButtonTexture)),
                    new Hint("back", HintKey.Of(escapeKeyTexture)),
                };
        }
        else
        {
            hints = pad
                ? new[]
                {
                    new Hint("pages", HintKey.Of(dPadLeftRightTexture)),
                    new Hint("back", HintKey.Of(bButton)),
                }
                : new[]
                {
                    new Hint("next page", HintKey.Of(leftMouseButtonTexture)),
                    new Hint("previous page", HintKey.Of(rightMouseButtonTexture)),
                    new Hint("back", HintKey.Of(escapeKeyTexture)),
                };
        }

        float alpha = 1f;
        if (introduction)
        {
            alpha = Mathf.Clamp01(HintsSeconds + 1f - (Time.unscaledTime - hintsShownAt));
            if (alpha <= 0f)
            {
                return;
            }
        }

        float scale = UiScale.Factor;
        float height = 48f * scale;
        Rect row = introduction
            ? new Rect(0, 12f * scale, Screen.width, height)
            : new Rect(0, Screen.height - 50f * scale - height / 2 + 12f * scale, Screen.width, height);

        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.5f * alpha);
        GUI.DrawTexture(new Rect(0, row.y, Screen.width, row.height), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        HintBar.Draw(row, scale, buttonLabelStyle, keyLetterStyle, blankKeyTexture, hints);
        GUI.color = previous;
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

        WarpMouseToGui(GetLoadSlotRectGui(0).center);
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
        int maxScroll = Mathf.Max(0, saves.Length - LoadMenuVisibleRows);
        if (menuIndex < loadScroll)
        {
            loadScroll = menuIndex;
        }
        else if (menuIndex >= loadScroll + LoadMenuVisibleRows)
        {
            loadScroll = menuIndex - LoadMenuVisibleRows + 1;
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
        switch (MenuItemToSlot(menuIndex))
        {
        case 0:
            state = EState.Introduction;
            cutscene = Instantiate(introCutscene);
            hintsShownAt = Time.unscaledTime;
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
        case JourneyOnwardSlot:
            OpenLoadMenu();
            break;
        case QuitSlot:
            QuitApplication();
            break;
        case SetupSlot:
            setupScreen = new SetupScreen(this);
            state = EState.Setup;
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
        float w = t.width * xs * MenuItemScale;
        float h = t.height * ys * MenuItemScale;
        return new Rect(160 * xs - w / 2f, MenuRowY(i) * ys - h / 2f, w, h);
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
        if (wheel != 0.0f && saves.Length > LoadMenuVisibleRows)
        {
            int maxScroll = saves.Length - LoadMenuVisibleRows;
            loadScroll = Mathf.Clamp(loadScroll + (wheel > 0.0f ? -1 : 1), 0, maxScroll);
        }

        if (ignoreHover && !(m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame))
            return;

        Vector2 guiMouse = GuiInput.ScreenToGuiMouse(m.position.ReadValue());

        for (int i = loadScroll; i < saves.Length && i < loadScroll + LoadMenuVisibleRows; i++)
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

    /// <summary>
    /// The space above a page's title is this share of its style's top padding: 15% less than the
    /// style gives, the user's measure for the achievements page of 30 October 2026, and the same on
    /// every page with a title of its own.
    /// </summary>
    private const float PageTitleTopShare = 0.85f;

    /// <summary>
    /// Draws the title of a full-screen page (Achievements, Setup) across the top of the screen at
    /// the interface scale, in the achievements' header style, at fontSize reference pixels or the
    /// style's own size; returns its bottom in screen pixels.
    /// </summary>
    public float DrawPageTitle(string text, int fontSize = 0)
    {
        // Made from the header style on every call, not kept in a field: a GUIStyle field of a component placed in a
        // scene is filled with an empty style when the Editor reloads the scripts, so a "make it once if null" never
        // runs, and the title came out in the default font, black and to the left (30 October 2026).
        GUIStyle source = fontSize > 0 && fontSize != achievementHeaderStyle.fontSize
            ? new GUIStyle(achievementHeaderStyle) { fontSize = fontSize }
            : achievementHeaderStyle;
        GUIStyle header = UiScale.Style(source);
        header.padding.top = Mathf.RoundToInt(header.padding.top * PageTitleTopShare);
        GUI.Label(Screen.safeArea, text, header);
        return Screen.safeArea.y + header.CalcHeight(new GUIContent(text), Screen.safeArea.width);
    }
    //
    // The load screen is drawn at the one interface scale, UiScale.

    // The block - the list, a gap, the preview - is laid out in reference pixels at fixed sizes and
    // centred across the screen, so it keeps its shape from 1366 to 1920 wide. The room for it starts
    // 270 from the top of a 1080 line screen, under the logo painted on the art, and the same share of
    // a shorter or taller one, since the art is stretched to the screen: 192 at 768, below which
    // everything scales. The list is as long as that room allows, up to ten rows, and the block is
    // centred between the foot of the logo and the bottom of the screen, as the achievements are
    // between their title and footer, but never higher than that start: when the interface is kept
    // from growing on a large screen (UiScale.GrowthPercent), the room is far taller than ten rows,
    // and a block hung from its top left the lower part of the screen empty.
    private const float LoadBlockTopShare = 270f / 1080f;
    // The foot of the logo, as a share of the height: row 57 of the 200-row picture, which is drawn
    // without its top 27 rows, so (57 - 27) / 173; 187 at 1080 lines.
    private const float LogoFootShare = (57f - 27f) / 173f;
    // Wide enough for the longest name the game writes itself, "Autosave - Unbroken - Lvl 4", at the
    // full size of the font, with the em kept for the scroll arrow: 506 + 44 by tools/fontmetrics.py;
    // the rest of the width is room to spare, chosen so that the block leaves 43 each side at 1366.
    private const float LoadListWidth = 586f;
    private const float LoadRowHeight = 65f;
    private const float LoadRowFontSize = 44f;
    private const float LoadBlockGap = 30f;
    private const float LoadBlockMinBottom = 30f;

    /// <summary>
    /// A name too long for its row is written smaller, down to this share of the row's font, and
    /// past that cut short with an ellipsis.
    /// </summary>
    private const float LoadRowMinFontShare = 0.375f;

    private static float LoadBlockTopNow => LoadBlockTopShare * UiScale.Height;

    /// <summary>
    /// As many rows as fit between the top of the block and LoadBlockMinBottom above the bottom of
    /// the screen, up to LOAD_MENU_MAX_ROWS: ten at 1080 lines, eight at 768 and on a Steam Deck.
    /// </summary>
    private static int LoadMenuVisibleRows => Mathf.Clamp(
        Mathf.FloorToInt((UiScale.Height - LoadBlockMinBottom - LoadBlockTopNow) / LoadRowHeight), 1, LOAD_MENU_MAX_ROWS);

    /// <summary>
    /// Where the load screen goes, in screen pixels: the list, the preview panel beside it, and
    /// the scale their contents are drawn at.
    /// </summary>
    private void GetLoadMenuLayout(out float scale, out Rect list, out Rect panel)
    {
        scale = UiScale.Factor;

        // The panel keeps its shape: its size is in the same reference pixels its insides are
        // laid out in, scaled as one.
        float panelW = savePanelBackground != null ? 8.0f * savePanelBackground.width : 0.0f;
        float panelH = savePanelBackground != null ? 4.0f * savePanelBackground.height : 0.0f;

        float blockW = LoadListWidth + (panelW > 0.0f ? LoadBlockGap + panelW : 0.0f);
        float left = (UiScale.Width - blockW) / 2;
        float listH = LoadMenuVisibleRows * LoadRowHeight;
        float blockH = Mathf.Max(listH, panelH);
        float foot = LogoFootShare * UiScale.Height;
        float top = Mathf.Max(LoadBlockTopNow, foot + (UiScale.Height - foot - blockH) / 2);

        list = UiScale.R(left, top, LoadListWidth, listH);
        panel = panelW > 0.0f ? UiScale.R(left + LoadListWidth + LoadBlockGap, top, panelW, panelH) : default;
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
        
        // A new game starts on the first level. The level in the Cheats panel is not applied
        // here: in the Editor it is remembered from one run to the next, and a new game would
        // start wherever it was last left.
        Instantiate(levelLoader);
        LevelLoader.sLevelLoader.loadedLevel = 1;
        Instantiate(player);

        // Destroy FrontEnd's AudioListener now that Player's listener is active
        AudioListener frontEndListener = GetComponentInChildren<AudioListener>();
        if (frontEndListener != null)
        {
            Destroy(frontEndListener);
        }

        yield return null;
        
        // New game - explicitly load the starting level
        LevelLoader.sLevelLoader.LoadLevel(1);
        PlayerObject.Player.StartAfterLoad();
        
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
                        float xjs = (menuIndex == i ? juiceScale * xs : xs) * MenuItemScale;
                        float yjs = (menuIndex == i ? juiceScale * ys : ys) * MenuItemScale;
                        Texture t = dropShadows[slot];
                        // Half the height as a float, as MenuRowY() takes it: with t.height / 2 an image of odd
                        // height - three of the original's - stood half a row low, off the even baselines.
                        Rect r = new Rect(160 * xs - (t.width / 2 - 1) * xjs, MenuRowY(i) * ys - (t.height / 2f - 3) * yjs, t.width * xjs, t.height * yjs);
                        GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true);

                        t = buttons[2 * slot + (menuIndex == i ? 1 : 0)];
                        r = new Rect(160 * xs - t.width / 2 * xjs, MenuRowY(i) * ys - t.height / 2f * yjs, t.width * xjs, t.height * yjs);
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

                    for (int row = 0; row < LoadMenuVisibleRows; ++row)
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
                        // is one: a long name stops short of it instead of running underneath. Past
                        // the smallest size it is cut short.
                        float labelWidth = r.width - rowFontSize;
                        style.fontSize = SaveUIHelper.FitFontSize(style, label, labelWidth,
                            rowFontSize, Mathf.Max(10, Mathf.RoundToInt(rowFontSize * LoadRowMinFontShare)));
                        label = Utils.Ellipsize(label, style, labelWidth);
                        GUI.Label(r, label, style);
                        style.fontSize = rowFontSize;

                        // There is more list above or below: say so on the first and last row drawn.
                        bool moreAbove = row == 0 && loadScroll > 0;
                        bool moreBelow = row == LoadMenuVisibleRows - 1 && i < saves.Length - 1;
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
        case EState.Setup:
            setupScreen?.OnGUI();
            break;
        case EState.Achievements:
            foreach (Achievements ach in GameObject.FindObjectsByType<Achievements>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                GUI.DrawTexture(Screen.safeArea, achievementsBackground);
                // The title and the footer at the interface scale.
                float titleBottom = DrawPageTitle("Achievements");
                // draw all achievements is here, between the title and the footer
                string numAchieved = ach.DrawAllAchievements(revealAchievements, titleBottom, Screen.height - UiScale.S(50));
                GUI.Label(UiScale.R(50, UiScale.Height - 50, 100, 24), numAchieved, UiScale.Style(buttonLabelStyle));

                bool useGamepadIcons = GameInput.LastActiveDevice == GameInputDevice.Gamepad;

                Rect toggleIconRect = UiScale.R(UiScale.Width / 2 - 100, UiScale.Height - 50, 24, 24);
                Texture2D toggleIcon = useGamepadIcons ? yButton : blankKeyTexture;
                GUI.DrawTexture(toggleIconRect, toggleIcon);
                if (!useGamepadIcons)
                    GUI.Label(toggleIconRect, "T", UiScale.Style(keyLetterStyle));
                string label = revealAchievements ? "Hide unattained achievements" : "Reveal unattained achievements";
                Rect toggleTextRect = UiScale.R(UiScale.Width / 2 - 60, UiScale.Height - 50, 240, 24);
                GUI.Label(toggleTextRect, label, UiScale.Style(buttonLabelStyle));

                Rect backIconRect = UiScale.R(UiScale.Width - 140, UiScale.Height - 50, 24, 24);
                Texture2D backIcon = useGamepadIcons ? bButton : escapeKeyTexture;
                GUI.DrawTexture(backIconRect, backIcon);
                Rect backTextRect = UiScale.R(UiScale.Width - 100, UiScale.Height - 50, 100, 24);
                GUI.Label(backTextRect, "Back", UiScale.Style(buttonLabelStyle));

                // Mouse click support: click on icon or text.
                if (GuiInput.TryConsumeClickInRect(new Rect(toggleIconRect.x, toggleIconRect.y, toggleTextRect.xMax - toggleIconRect.x, UiScale.S(28))))
                {
                    ToggleRevealAchievements();
                }
                else if (GuiInput.TryConsumeClickInRect(new Rect(backIconRect.x, backIconRect.y, backTextRect.xMax - backIconRect.x, UiScale.S(28))))
                {
                    BackFromAchievements();
                }
                break;
            }
            break;
        case EState.Introduction:
            if (cutscene != null)
            {
                DrawScreenHints(true);
            }
            break;
        case EState.PortCredits:
            // the page's title, and the pages of names under it
            credits.TextTop = DrawPageTitle("Acknowledgements");
            DrawScreenHints(false);
            break;
        }
        Utils.DrawFade(fadeAlpha);
    }
}
