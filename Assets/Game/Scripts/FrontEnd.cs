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

    // Journey Onward: the game menu with its LOAD tab alone (fix 433).
    private TitleLoadScreen titleLoad;
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
            titleLoad.Update();
            if (titleLoad.ChosenSlot != null)
            {
                if (makeSelection != null)
                    Utils.PlayClip2d(makeSelection);
                pendingLoadSlot = titleLoad.ChosenSlot;
                titleLoad = null;
                StartCoroutine(LoadAGame());
                state = EState.Done;
            }
            else if (titleLoad.Closed)
            {
                if (backSelection != null)
                    Utils.PlayClip2d(backSelection);
                titleLoad = null;
                // saves may have been deleted, and with the last of them Journey Onward goes
                CheckSavesExist();
                state = EState.Menu;
                // back on Journey Onward, or Create Character if the last save went: not on Introduction, which A by habit
                // would play (fix 476); found by its slot, since the entries move when Journey Onward goes
                int back = MenuIndexOfSlot(JourneyOnwardSlot);
                menuIndex = back >= 0 ? back : Mathf.Max(0, MenuIndexOfSlot(CreateCharacterSlot));
            }
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
    /// from the Setup screen or from a game does not start it over. Setup's music tracks call it too,
    /// when they close.
    /// </summary>
    public static void StartTitleMusic()
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

    /// <summary>Journey Onward: the game menu with its LOAD tab, on the newest save.</summary>
    private void OpenLoadMenu()
    {
        titleLoad = new TitleLoadScreen(this);
        state = EState.LoadMenu;
    }

    private void UpdateMenu()
    {
        GameInput.RefreshLastActiveDevice();

        int numItems = MenuItemCount();
        // the left stick as well as the D-pad, as in the game menu and its lists
        if ((Gamepad.current?.dpad.down.wasPressedThisFrame ?? false) || (Gamepad.current?.leftStick.down.wasPressedThisFrame ?? false))
        {
            menuIndex = (menuIndex + 1) % numItems;
            juice = Time.timeAsDouble;
            if (moveSelection != null)
            {
                Utils.PlayClip2d(moveSelection);
            }
        }
        else if ((Gamepad.current?.dpad.up.wasPressedThisFrame ?? false) || (Gamepad.current?.leftStick.up.wasPressedThisFrame ?? false))
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

    // Where the pointer was the last time a menu looked, so that it takes the selection only when it
    // moves: a pointer resting on an item took it back, the frame after, from every arrow key.
    private Vector2 lastMenuPointer = new Vector2(float.NaN, float.NaN);

    /// <summary>True when the pointer has moved since the last call, or a button of it was pressed.</summary>
    private bool PointerTakesSelection(Mouse m, Vector2 guiMouse)
    {
        bool moved = (guiMouse - lastMenuPointer).sqrMagnitude > 0.25f || float.IsNaN(lastMenuPointer.x);
        lastMenuPointer = guiMouse;
        return moved || m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame;
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
        if (!PointerTakesSelection(m, guiMouse))
            return;

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
        case EState.LoadMenu:
            titleLoad?.OnGUI();
            break;
        case EState.Menu:
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
                Texture2D toggleIcon = useGamepadIcons ? PadGlyphs.Face(yButton) : blankKeyTexture;
                GUI.DrawTexture(toggleIconRect, toggleIcon);
                if (!useGamepadIcons)
                    GUI.Label(toggleIconRect, "T", UiScale.Style(keyLetterStyle));
                string label = revealAchievements ? "Hide unattained achievements" : "Reveal unattained achievements";
                Rect toggleTextRect = UiScale.R(UiScale.Width / 2 - 60, UiScale.Height - 50, 240, 24);
                GUI.Label(toggleTextRect, label, UiScale.Style(buttonLabelStyle));

                Rect backIconRect = UiScale.R(UiScale.Width - 140, UiScale.Height - 50, 24, 24);
                Texture2D backIcon = useGamepadIcons ? PadGlyphs.Face(bButton) : escapeKeyTexture;
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
