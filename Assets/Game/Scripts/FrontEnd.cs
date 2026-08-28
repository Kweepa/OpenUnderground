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

	private const int MAX_SAVE_SLOTS = 10;
	private SaveGameManager.SaveSlotInfo[] saves = new SaveGameManager.SaveSlotInfo[MAX_SAVE_SLOTS];
	private SaveGameManager.SaveSlotInfo[] actualSaves = new SaveGameManager.SaveSlotInfo[0];
    private Texture2D[] saveScreenshots = new Texture2D[MAX_SAVE_SLOTS];
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

    private void LoadScreenshotForSlot(int slotIndex, string slotName)
    {
        // Retained for backwards compatibility in case of existing calls,
        // but delegate to shared helper for the actual work.
        SaveUIHelper.LoadScreenshotForSlot(saveScreenshots, slotIndex, slotName);
    }

	private void RefreshSaves()
	{
        // Use shared helper to populate save slot info and screenshots, and clamp menuIndex
        menuIndex = SaveUIHelper.RefreshSaves(saves, ref actualSaves, saveScreenshots, menuIndex);

		// Ensure menuIndex is within valid range for the current menu
		// Journey Onward is only available if there are actual saves (not empty slots)
		int numItems = MenuItemCount();
		if (menuIndex >= numItems) menuIndex = numItems - 1;
		if (menuIndex < 0) menuIndex = 0;
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
                RefreshSaves();
                state = EState.LoadMenu;
                menuIndex = 0;
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
            menuIndex = (menuIndex + 1) % saves.Length;
            juice = Time.timeAsDouble;
            if (moveSelection != null)
            {
                Utils.PlayClip2d(moveSelection);
            }
        }
        else if (Gamepad.current?.dpad.up.wasPressedThisFrame ?? false)
        {
            menuIndex = (menuIndex + saves.Length - 1) % saves.Length;
            juice = Time.timeAsDouble;
            if (moveSelection != null)
            {
                Utils.PlayClip2d(moveSelection);
            }
        }
        else if (Gamepad.current?.aButton.wasPressedThisFrame ?? false)
        {
            if (makeSelection != null)
            {
                Utils.PlayClip2d(makeSelection);
            }
            // Only load if this is not an empty slot
            if (!string.IsNullOrEmpty(saves[menuIndex].slotName))
            {
                pendingLoadSlot = saves[menuIndex].slotName;
                StartCoroutine(LoadAGame());
                state = EState.Done;
            }
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
        if (!string.IsNullOrEmpty(saves[menuIndex].slotName))
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
                SetMenuIndex((menuIndex + 1) % saves.Length);
                return;
            }
            if (kb.upArrowKey.wasPressedThisFrame)
            {
                SetMenuIndex((menuIndex + saves.Length - 1) % saves.Length);
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

        if (ignoreHover && !(m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame))
            return;

        float xs = Screen.width / 320.0f;
        float ys = Screen.height / 200.0f;
        Vector2 guiMouse = GuiInput.ScreenToGuiMouse(m.position.ReadValue());

        for (int i = 0; i < saves.Length; i++)
        {
            Rect r = GetLoadSlotRectGui(i, xs, ys);
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

    private static Rect GetLoadSlotRectGui(int slotIndex, float xs, float ys)
    {
        // One full row tall hitbox to avoid gaps between rows at high resolutions.
        // Row spacing is 12 in "virtual" units, so scale height with ys.
        float rowH = 12f * ys;
        float yCenter = (70 + 12 * slotIndex) * ys;
        return new Rect(60 * xs, yCenter - rowH * 0.5f, 180 * xs, rowH);
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
                    style.fontSize = (int)(ys * 10);
                    style.alignment = TextAnchor.UpperLeft;

                    Color grey = new Color(0.5f, 0.5f, 0.5f, 0.7f); // Greyed out

                    for (int i = 0; i < saves.Length; ++i)
                    {
                        Rect r = GetLoadSlotRectGui(i, xs, ys);

                        bool isEmpty = string.IsNullOrEmpty(saves[i].slotName);
                        bool isSelected = menuIndex == i;

                        // Color logic: selected items are bright, unselected are dim, empty slots are greyed out
                        if (isSelected)
                        {
                            style.normal.textColor = selectColor;
                        }
                        else if (isEmpty)
                        {
                            style.normal.textColor = Color.grey;
                        }
                        else
                        {
                            style.normal.textColor = unselectColor;
                        }

                        string label = saves[i].displayName ?? saves[i].slotName ?? "Empty";
                        GUI.Label(r, label, style);
                    }

                    // Draw screenshot preview panel with shared background and metadata, matching SaveLoadGUI layout
                    if (savePanelBackground != null)
                    {
                        float wPanel = 5.0f * savePanelBackground.width;
                        float hPanel = 4.0f * savePanelBackground.height;
                        float xPanel = 2 * Screen.width / 3 - wPanel / 2;
                        float yPanel = 2 * Screen.height / 3 - hPanel / 2;
                        float previewX = xPanel + 10.0f;
                        Rect previewRect = SaveUIHelper.GetSaveSlotDetailPanelRect(previewX, yPanel, savePanelBackground);
                        GuiInput.RegisterBlockingRect(previewRect);

                        SaveUIHelper.DrawSaveSlotDetailPanel(
                            previewX,
                            yPanel,
                            savePanelBackground,
                            savePanelText,
                            selectColor,
                            saves,
                            saveScreenshots,
                            menuIndex);

                        GuiInput.TryConsumeClickInPanel(previewRect);
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
