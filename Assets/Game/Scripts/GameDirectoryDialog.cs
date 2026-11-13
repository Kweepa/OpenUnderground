using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using System.IO;
using System.Collections;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using DiscUtils.Iso9660;
using DiscUtils.Streams;

public class GameDirectoryDialog : MonoBehaviour
{
    private string inputPath = "";
    private string statusMessage = "";
    private bool isValidPath = false;
    private bool isSearching = false;
    private string foundPath = "";
    /// <summary>Set when game.gog extraction fails; cleared at start of validate / auto-search.</summary>
    private string lastGogExtractError = "";
    /// <summary>True if the last successful path came from game.gog (extract or cache), not loose files.</summary>
    private bool lastFoundPathUsedGogExtract = false;
    /// <summary>True if game.gog was unpacked from ISO this run (false if LooseData cache was reused).</summary>
    private bool lastGogExtractPerformedFresh = false;
    /// <summary>
    /// The game.gog or game.bin the last path found came from: the window names it, and its folder, rather than the
    /// copy in LooseData, which is the same for every source.
    /// </summary>
    private string lastImageFile;
    /// <summary>
    /// True while the window's own search at start runs: the instructions are hidden until it has found nothing, since
    /// when it finds the game they are never needed.
    /// </summary>
    private bool startupSearch;
    /// <summary>
    /// What the window is busy with while a validation or a search runs on a worker thread - "Validating",
    /// "Searching", "Extracting game.gog" - and the folder it is looking at; null when idle. Shown with dots that
    /// count 1, 2, 3, so the window is seen to be working: unpacking game.gog takes a while on a slow disk.
    /// </summary>
    private volatile string workingHeadline;
    private volatile string workingDetail;
    /// <summary>Application.persistentDataPath, read on the main thread for the extraction on the worker.</summary>
    private string persistentDataPath;
    /// <summary>In LooseData from the start of an extraction to its verified end: a copy that still has it is cut short.</summary>
    private const string ExtractingMarker = "extracting.tmp";
    /// <summary>
    /// The names a release gives the image of the game's CD, an ISO9660 image either way: game.gog in the GOG
    /// release, game.bin in the Steam one (November 2026), where the folder UNDEROM1 beside it holds only DATA and CUTS.
    /// </summary>
    private static readonly string[] DiscImageNames = { "game.gog", "game.bin" };
    
    [Header("Testing")]
    [Tooltip("Check this to clear the saved game directory path on startup (for testing)")]
    public bool clearPathOnStart = false;
    
    [Header("Settings")]
    [Tooltip("Delay in seconds before auto-continuing when a valid path is found")]
    public float autoContinueDelay = 1.0f;
    
    private enum ESelectedControl
    {
        TextField,
        Browse,
        AutoSearch,
        ValidatePath,
        ContinueQuit
    }
    
    private ESelectedControl selectedControl = ESelectedControl.TextField;
    
    [Header("GUI Styles")]
    public GUIStyle titleStyle;
    public GUIStyle labelStyle;
    public GUIStyle buttonStyle;
    public GUIStyle textFieldStyle;
    public GUIStyle statusStyle;
    
    [Header("Colors")]
    public Color backgroundColor = new Color(0.545f, 0.353f, 0.169f); // Brown wood color
    public Color buttonBoxColor = new Color(1.0f, 0.843f, 0.0f); // Warm yellow
    public Color buttonBoxHighlightColor = Color.white;
    public Color buttonBoxDisabledColor = new Color(0.482f, 0.341f, 0.204f); // #7B5734, dark brown, for a button greyed out

    private static bool sStartupInfoLogged;

    private static void LogStartupInfo()
    {
        if (sStartupInfoLogged)
        {
            return;
        }

        sStartupInfoLogged = true;

        string uwRoot = GameDataPath.GetGameRootPath();
        string uwRootLog = uwRoot ?? "not set";
        double refreshHz = Screen.currentResolution.refreshRateRatio.value;

        UnityEngine.Debug.Log(
            "[Startup] DisplayVersion=" + FrontEnd.DisplayVersion
            + " Application.version=" + Application.version
            + " buildGUID=" + Application.buildGUID
            + " RuntimeUtc=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            + " RuntimeLocal=" + DateTime.Now.ToString("o"));

        UnityEngine.Debug.Log(
            "[Startup] OS=" + SystemInfo.operatingSystem
#if UNITY_STANDALONE_WIN
            + " Environment.OSVersion=" + Environment.OSVersion
            + " Is64BitOS=" + Environment.Is64BitOperatingSystem
#endif
        );

        var cul = CultureInfo.CurrentCulture;
        var uic = CultureInfo.CurrentUICulture;
        UnityEngine.Debug.Log(
            "[Startup] Culture=" + cul.Name + " (" + cul.DisplayName + ")"
            + " UICulture=" + uic.Name + " (" + uic.DisplayName + ")");

        UnityEngine.Debug.Log(
            "[Startup] Unity=" + Application.unityVersion
            + " isEditor=" + Application.isEditor
            + " isDebugBuild=" + UnityEngine.Debug.isDebugBuild
            + " processors=" + SystemInfo.processorCount
            + " systemMemoryMB=" + SystemInfo.systemMemorySize);

        UnityEngine.Debug.Log(
            "[Startup] Screen=" + Screen.width + "x" + Screen.height
            + " refreshHz=" + refreshHz.ToString(CultureInfo.InvariantCulture));

        UnityEngine.Debug.Log(
            "[Startup] persistentDataPath=" + Application.persistentDataPath
            + " dataPath=" + Application.dataPath
            + " UWGameRoot=" + uwRootLog);
    }
    
    private void Awake()
    {
        LogStartupInfo();
        persistentDataPath = Application.persistentDataPath;

        if (statusStyle != null)
        {
            statusStyle.wordWrap = true;
        }
        
        // Clear path if testing flag is set
        if (clearPathOnStart)
        {
            GameDataPath.SetGameRootPath(null);
            UnityEngine.Debug.Log("Cleared saved game directory path (testing mode)");
        }
        
        // Check if path is already set and valid
        string savedPath = GameDataPath.GetGameRootPath();
        if (savedPath != null)
        {
            // Verify SOUND/UW01.XMI exists
            string soundFile = Path.Combine(savedPath, "Sound", "UW01.XMI");
            if (File.Exists(soundFile) && !IsCutShortCopy(savedPath))
            {
                // Path is valid, skip to Logos
                UnityEngine.Debug.Log($"Game directory already set: {savedPath}");
                SceneManager.LoadScene("Logos");
                return;
            }
            else
            {
                // Path exists but the game's files are missing, or were unpacked only in part: clear it
                GameDataPath.SetGameRootPath(null);
                statusMessage = File.Exists(soundFile)
                    ? "The game data unpacked from game.gog or game.bin is incomplete. Please validate the game's folder again."
                    : "Previously saved directory is no longer valid. Please select a new directory.";
            }
        }
        
        // The folders GOG and Steam install the game in, from the registry, before any button: found there, the game
        // loads by itself
        StartCoroutine(RegistrySearch());
    }
      
    /// <summary>
    /// True when the path lies in LooseData and an extraction of game.gog there was cut short (its marker is still
    /// there). The saved path outlives the folder - GetGameRootPath() returns null for a missing folder but keeps the
    /// key - so after LooseData is deleted and a new extraction is stopped half way, the old path would point at a copy
    /// with UW01.XMI and files missing. No Unity API: FindGameData() calls it on the worker.
    /// </summary>
    private bool IsCutShortCopy(string path)
    {
        string looseDataPath = Path.Combine(persistentDataPath, "LooseData");
        if (!File.Exists(Path.Combine(looseDataPath, ExtractingMarker)))
        {
            return false;
        }
        try
        {
            string folder = Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string loose = Path.GetFullPath(looseDataPath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return folder.StartsWith(loose, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return true;
        }
    }
    
    private void Update()
    {
        // Handle controller navigation
        if (Gamepad.current != null)
        {
            // Helper to check if a control is enabled
            bool IsControlEnabled(ESelectedControl control)
            {
                switch (control)
                {
                case ESelectedControl.TextField:
                case ESelectedControl.Browse:
                case ESelectedControl.AutoSearch:
                    return IsIdle();
                case ESelectedControl.ValidatePath:
                    return CanValidate();
                case ESelectedControl.ContinueQuit:
                    return IsIdle();
                default:
                    return true;
                }
            }
            
            // Helper to find next enabled control in a direction
            ESelectedControl FindNextEnabled(ESelectedControl current, bool forward)
            {
                ESelectedControl[] order =
                { 
                    ESelectedControl.TextField, 
                    ESelectedControl.Browse, 
                    ESelectedControl.AutoSearch, 
                    ESelectedControl.ValidatePath, 
                    ESelectedControl.ContinueQuit 
                };
                
                int currentIndex = Array.IndexOf(order, current);
                if (currentIndex == -1) return current;
                
                int direction = forward ? 1 : -1;
                int attempts = 0;
                
                while (attempts < order.Length)
                {
                    currentIndex = (currentIndex + direction + order.Length) % order.Length;
                    if (IsControlEnabled(order[currentIndex]))
                    {
                        return order[currentIndex];
                    }
                    attempts++;
                }
                
                return current; // Fallback to current if none found
            }
            
            // D-pad navigation
            if (Gamepad.current.dpad.down.wasPressedThisFrame)
            {
                selectedControl = FindNextEnabled(selectedControl, true);
            }
            else if (Gamepad.current.dpad.up.wasPressedThisFrame)
            {
                selectedControl = FindNextEnabled(selectedControl, false);
            }
            else if (Gamepad.current.dpad.left.wasPressedThisFrame)
            {
                // Move left: ContinueQuit -> ValidatePath -> AutoSearch
                ESelectedControl[] leftOrder = { 
                    ESelectedControl.AutoSearch, 
                    ESelectedControl.ValidatePath, 
                    ESelectedControl.ContinueQuit 
                };
                int currentIndex = Array.IndexOf(leftOrder, selectedControl);
                if (currentIndex >= 0)
                {
                    int attempts = 0;
                    while (attempts < leftOrder.Length)
                    {
                        currentIndex = (currentIndex - 1 + leftOrder.Length) % leftOrder.Length;
                        if (IsControlEnabled(leftOrder[currentIndex]))
                        {
                            selectedControl = leftOrder[currentIndex];
                            break;
                        }
                        attempts++;
                    }
                }
            }
            else if (Gamepad.current.dpad.right.wasPressedThisFrame)
            {
                // Move right: AutoSearch -> ValidatePath -> ContinueQuit
                ESelectedControl[] rightOrder = { 
                    ESelectedControl.AutoSearch, 
                    ESelectedControl.ValidatePath, 
                    ESelectedControl.ContinueQuit 
                };
                int currentIndex = Array.IndexOf(rightOrder, selectedControl);
                if (currentIndex >= 0)
                {
                    int attempts = 0;
                    while (attempts < rightOrder.Length)
                    {
                        currentIndex = (currentIndex + 1) % rightOrder.Length;
                        if (IsControlEnabled(rightOrder[currentIndex]))
                        {
                            selectedControl = rightOrder[currentIndex];
                            break;
                        }
                        attempts++;
                    }
                }
            }
            else if (Gamepad.current.aButton.wasPressedThisFrame)
            {
                // Activate selected control
                switch (selectedControl)
                {
                case ESelectedControl.Browse:
                    if (IsIdle())
                    {
                        BrowseForFolder();
                    }
                    break;
                case ESelectedControl.AutoSearch:
                    if (IsIdle())
                    {
                        StartCoroutine(AutoSearch());
                    }
                    break;
                case ESelectedControl.ValidatePath:
                    if (CanValidate())
                    {
                        ValidateInputPath();
                    }
                    break;
                case ESelectedControl.ContinueQuit:
                    if (IsIdle())
                    {
                        Application.Quit();
                    }
                    break;
                }
            }
        }
    }
    
    private void OnGUI()
    {
        GUI.depth = (int)EGUIDepth.Logos;
        
        // Draw background
        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
        
        // Calculate dialog area (centered, with padding for 2x font sizes)
        float dialogWidth = 900;
        float dialogHeight = 650;
        float dialogX = Screen.width / 2 - dialogWidth / 2;
        float dialogY = Screen.height / 2 - dialogHeight / 2;
        
        // Draw background rectangle
        Color originalColor = GUI.color;
        GUI.color = backgroundColor;
        GUI.DrawTexture(new Rect(dialogX, dialogY, dialogWidth, dialogHeight), Texture2D.whiteTexture);
        GUI.color = originalColor;
        
        // Title - centered at top with padding
        float titleY = dialogY + 40;
        GUI.Label(new Rect(dialogX, titleY, dialogWidth, 100), 
            "Select Game Installation Directory", titleStyle);
        
        // Instructions - centered below title with good spacing; hidden while the search at start may still find the game
        float instructionsY = titleY + 100;
        if (!startupSearch)
        {
            GUI.Label(new Rect(dialogX + 60, instructionsY, dialogWidth - 120, 100),
                "Press Auto Search to look for the original game, or Browse to pick it in its installation folder - GOG, Steam or the original DOS game - search for: game.gog, game.bin or UW.EXE.", labelStyle);
        }
        
        // Path input section - centered vertically in dialog
        float inputSectionY = dialogY + dialogHeight / 2 - 30; // Center vertically, adjust for half height of input
        float browseButtonWidth = 120;
        float inputFieldWidth = dialogWidth - 120; // Leave space for padding (60px padding on each side)
        
        // The field and Browse are greyed on the same terms as the buttons below: a path given while the game is busy,
        // or about to load, would be ignored, or validated while the first one loads
        GUI.enabled = IsIdle();
        
        // Text field with outline box - highlight if selected
        Rect textFieldRect = new Rect(dialogX + 60, inputSectionY, inputFieldWidth, 60);
        bool textFieldSelected = selectedControl == ESelectedControl.TextField;
        DrawButtonWithBox(textFieldRect, "", textFieldSelected);
        
        GUI.SetNextControlName("pathInput");
        inputPath = GUI.TextField(textFieldRect, inputPath, textFieldStyle);
        
        // Keep focus on text field if it's selected
        if (selectedControl == ESelectedControl.TextField)
        {
            GUI.FocusControl("pathInput");
        }
        
        // Browse button (the system's Open dialog) - aligned with input field
        Rect browseRect = new Rect(dialogX + dialogWidth - 60 - browseButtonWidth, inputSectionY + 70, browseButtonWidth, 60);
        bool browseSelected = selectedControl == ESelectedControl.Browse;
        DrawButtonWithBox(browseRect, "Browse", browseSelected);
        if (GUI.Button(browseRect, "Browse", buttonStyle))
        {
            BrowseForFolder();
        }
        GUI.enabled = true;
        
        // Status message - positioned between input and buttons (two lines + wrap; keep clear of button row)
        float statusY = inputSectionY + 110;
        if (!string.IsNullOrEmpty(statusMessage))
        {
            if (isValidPath)
            {
                GUI.color = Color.green;
            }
            else if (isSearching)
            {
                GUI.color = Color.yellow;
            }
            else
            {
                GUI.color = Color.red;
            }
            
            GUI.Label(new Rect(dialogX + 60, statusY, dialogWidth - 120, 100), 
                statusMessage, statusStyle);
            GUI.color = originalColor;
        }
        
        // Action buttons row - evenly spaced at bottom
        float buttonsY = dialogY + dialogHeight - 120;
        float buttonWidth = 200;
        float buttonSpacing = (dialogWidth - 120 - (buttonWidth * 3)) / 2; // Even spacing between 3 buttons
        float buttonStartX = dialogX + 60;
        
        // Auto Search button
        GUI.enabled = IsIdle();
        Rect autoSearchRect = new Rect(buttonStartX, buttonsY, buttonWidth, 80);
        bool autoSearchSelected = selectedControl == ESelectedControl.AutoSearch;
        DrawButtonWithBox(autoSearchRect, "Auto Search", autoSearchSelected);
        if (GUI.Button(autoSearchRect, "Auto Search", buttonStyle))
        {
            StartCoroutine(AutoSearch());
        }
        GUI.enabled = true;
        
        // Validate button (for manual entry) - greyed out if no path entered
        Rect validateRect = new Rect(buttonStartX + buttonWidth + buttonSpacing, buttonsY, buttonWidth, 80);
        bool validateSelected = selectedControl == ESelectedControl.ValidatePath;
        bool canValidate = CanValidate();
        GUI.enabled = canValidate;
        DrawButtonWithBox(validateRect, "Validate Path", validateSelected && canValidate);
        if (GUI.Button(validateRect, "Validate Path", buttonStyle))
        {
            ValidateInputPath();
        }
        GUI.enabled = true;
        
        // Quit button: a valid path moves on by itself (DelayedContinue), so there is no Continue to press
        GUI.enabled = IsIdle();
        Rect continueRect = new Rect(buttonStartX + (buttonWidth + buttonSpacing) * 2, buttonsY, buttonWidth, 80);
        bool continueSelected = selectedControl == ESelectedControl.ContinueQuit;
        string continueButtonText = "Quit";
        DrawButtonWithBox(continueRect, continueButtonText, continueSelected);
        if (GUI.Button(continueRect, continueButtonText, buttonStyle))
        {
            Application.Quit();
        }
        GUI.enabled = true;
        
        // Handle Enter key in text field
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
        {
            if (GUI.GetNameOfFocusedControl() == "pathInput")
            {
                ValidateInputPath();
            }
        }
    }
    
    private void DrawButtonWithBox(Rect buttonRect, string text, bool isSelected)
    {
        // Check if mouse is over the button or if controller selected
        bool isHighlighted = buttonRect.Contains(Event.current.mousePosition) || isSelected;
        Color boxColor = isHighlighted ? buttonBoxHighlightColor : buttonBoxColor;
        // A greyed-out button has a dark box, and neither the pointer nor the pad's selection lights it.
        if (!GUI.enabled)
        {
            boxColor = buttonBoxDisabledColor;
        }
        
        // Draw outline box (4 lines: top, bottom, left, right)
        float lineWidth = 2f;
        Color originalColor = GUI.color;
        GUI.color = boxColor;
        
        // Top line
        GUI.DrawTexture(new Rect(buttonRect.x, buttonRect.y, buttonRect.width, lineWidth), Texture2D.whiteTexture);
        // Bottom line
        GUI.DrawTexture(new Rect(buttonRect.x, buttonRect.y + buttonRect.height - lineWidth, buttonRect.width, lineWidth), Texture2D.whiteTexture);
        // Left line
        GUI.DrawTexture(new Rect(buttonRect.x, buttonRect.y, lineWidth, buttonRect.height), Texture2D.whiteTexture);
        // Right line
        GUI.DrawTexture(new Rect(buttonRect.x + buttonRect.width - lineWidth, buttonRect.y, lineWidth, buttonRect.height), Texture2D.whiteTexture);
        
        GUI.color = originalColor;
    }
    
    /// <summary>
    /// The system's Open dialog, the one the Setup screen's music page uses, showing the files that mark the game's
    /// folder: game.gog in a GOG installation, game.bin in a Steam one, UW.EXE in a copy of the files. The folder of
    /// the file picked goes in the field and is validated, which also unpacks game.gog or game.bin. It used to start
    /// explorer.exe and ask for the path to be pasted, which did nothing under Proton.
    /// </summary>
    private void BrowseForFolder()
    {
        if (!IsIdle())
        {
            return;
        }
        if (!NativeFileDialog.IsAvailable)
        {
            statusMessage = "Type the path of the game's folder above.";
            return;
        }

        string start = !string.IsNullOrEmpty(inputPath) && Directory.Exists(inputPath) ? inputPath : null;
        string file = NativeFileDialog.OpenFile("Find game.gog, game.bin or UW.EXE in the game's folder", "Game files",
            new[] { "game.gog", "game.bin", "UW.EXE" }, start, out string error);
        if (error != null)
        {
            statusMessage = StatusMessageTwoLines("Could not open file browser:", error);
            return;
        }
        if (file == null)
        {
            return;
        }

        inputPath = Path.GetDirectoryName(file);
        ValidateInputPath();
    }
    
    /// <summary>
    /// Nothing is being validated, searched or unpacked, and no path has validated: the field, Browse, Auto Search and
    /// Quit are offered only then. Quitting half way through game.gog would leave a copy cut short, and once a path has
    /// validated the game moves on by itself (isValidPath is set only where DelayedContinue() starts). After a failure
    /// everything is offered again.
    /// </summary>
    private bool IsIdle()
    {
        return !isSearching && !isValidPath;
    }
    
    /// <summary>Validate Path: when the window is idle and the field holds a path.</summary>
    private bool CanValidate()
    {
        return IsIdle() && !string.IsNullOrEmpty(inputPath);
    }
    
    private void ValidateInputPath()
    {
        if (!IsIdle())
        {
            return;
        }
        lastGogExtractError = "";
        lastFoundPathUsedGogExtract = false;
        if (string.IsNullOrEmpty(inputPath))
        {
            statusMessage = "Please enter a directory path.";
            isValidPath = false;
            return;
        }
        
        StartCoroutine(ValidateOnWorker(inputPath));
    }
    
    /// <summary>
    /// Runs the validation, and the unpacking of game.gog it may need, on a worker thread, so that the window keeps
    /// drawing and shows it is busy; then reports as before.
    /// </summary>
    private IEnumerator ValidateOnWorker(string path)
    {
        isSearching = true;
        isValidPath = false;
        workingHeadline = "Validating";
        workingDetail = path;
        System.Threading.Tasks.Task<string> work = System.Threading.Tasks.Task.Run(() => FindGameData(path));
        while (!work.IsCompleted)
        {
            ShowWorking();
            yield return null;
        }
        workingHeadline = null;
        isSearching = false;
        if (work.IsFaulted)
        {
            UnityEngine.Debug.LogError($"Validating {path} failed: {work.Exception?.GetBaseException().Message}");
        }
        string validatedPath = work.IsFaulted ? null : work.Result;
        
        if (validatedPath != null)
        {
            foundPath = validatedPath;
            isValidPath = true;
            string source = lastFoundPathUsedGogExtract ? lastImageFile : validatedPath;
            
            // Automatically proceed when valid path is found (after delay)
            StartCoroutine(DelayedContinue(validatedPath, FoundHeadline("Valid game directory found"), source));
        }
        else
        {
            isValidPath = false;
            if (!string.IsNullOrEmpty(lastGogExtractError))
            {
                statusMessage = lastGogExtractError;
            }
            else
            {
                statusMessage = "Invalid directory. Could not find SOUND/UW01.XMI or a game.gog or game.bin file. Please check the path and try again.";
            }
        }
    }
    
    /// <summary>The status line while busy: the headline with one, two or three dots, three changes a second.</summary>
    private void ShowWorking()
    {
        string headline = workingHeadline;
        if (headline != null)
        {
            statusMessage = StatusMessageTwoLines(WithDots(headline), workingDetail);
        }
    }
    
    private static string WithDots(string headline)
    {
        return headline + new string('.', 1 + (int)(Time.unscaledTime * 3f) % 3);
    }
    
    /// <summary>
    /// The game's data in a folder: loose files, or game.gog or game.bin unpacked. Runs on a worker thread, so nothing
    /// in here may touch the Unity API.
    /// </summary>
    private string FindGameData(string path)
    {
        // First try standard validation (loose files), unless they are an extraction of game.gog cut short
        string validatedPath = GameDataPath.ValidateGameDirectory(path);
        if (validatedPath != null && IsCutShortCopy(validatedPath))
        {
            validatedPath = null;
        }
        
        // If not found and input is a directory, check for the image of the CD
        if (validatedPath == null && Directory.Exists(path))
        {
            validatedPath = FromDiscImage(path);
        }
        return validatedPath;
    }
    
    /// <summary>
    /// The game's data from the image of the CD in a folder, game.gog or game.bin, unpacked into LooseData or found
    /// there already; null if the folder has no image holding the game. Runs on a worker thread.
    /// </summary>
    private string FromDiscImage(string directory)
    {
        foreach (string name in DiscImageNames)
        {
            string gogFile = Path.Combine(directory, name);
            if (!File.Exists(gogFile) || !InspectGogFile(gogFile))
            {
                continue;
            }
            string extractedPath = ExtractGogFile(gogFile);
            // Verify extraction was successful. The two fields are set only then: after an image that failed, the
            // search may still find loose files, which must not be named as unpacked from it.
            if (!string.IsNullOrEmpty(extractedPath) && File.Exists(Path.Combine(extractedPath, "Sound", "UW01.XMI")))
            {
                lastFoundPathUsedGogExtract = true;
                lastImageFile = gogFile;
                return extractedPath;
            }
        }
        return null;
    }
    
    /// <summary>
    /// At the window's start, before any button: the game's folders as GOG's and Steam's installers write them in the
    /// registry, a few folders searched in a moment. Found there, the game is validated and loads; if not, or if the
    /// search fails, the field, Browse and Auto Search are offered with no message, the instructions saying what to do,
    /// and Auto Search goes on through the usual places.
    /// </summary>
    private IEnumerator RegistrySearch()
    {
        startupSearch = true;
        yield return SearchRoots(RegistrySearchRoots(), null);
        startupSearch = isValidPath;
    }
    
    /// <summary>The Auto Search button: the usual places and the Steam libraries, the registry's folders tried at start.</summary>
    private IEnumerator AutoSearch()
    {
        return SearchRoots(FixedSearchRoots(), "Auto-search completed. Could not find game directory. Please enter the path manually.");
    }
    
    /// <summary>
    /// Searches the folders in order and moves on with the first that holds the game. Not found, the status shows the
    /// extraction's error or notFoundMessage; with notFoundMessage null, what it showed before.
    /// </summary>
    private IEnumerator SearchRoots(List<SearchRoot> roots, string notFoundMessage)
    {
        string previousMessage = statusMessage;
        lastGogExtractError = "";
        lastFoundPathUsedGogExtract = false;
        isSearching = true;
        isValidPath = false;
        workingDetail = null;
        workingHeadline = "Searching";
        ShowWorking();
        
        foreach (SearchRoot root in roots)
        {
            string searchPath = root.Folder;
            int depth = root.Depth;
            if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath))
            {
                continue;
            }
            
            // Each folder is searched on a worker thread, so the window keeps drawing: unpacking a game.gog found
            // there takes a while.
            workingHeadline = "Searching";
            workingDetail = searchPath;
            System.Threading.Tasks.Task<string> work = System.Threading.Tasks.Task.Run(() => SearchDirectory(searchPath, depth));
            while (!work.IsCompleted)
            {
                ShowWorking();
                yield return null;
            }
            
            string found = work.IsFaulted ? null : work.Result;
            if (!string.IsNullOrEmpty(found))
            {
                string source = lastFoundPathUsedGogExtract ? lastImageFile : found;
                UnityEngine.Debug.Log($"Search found the game at {source} (data at {found}), searching {searchPath}");
                workingHeadline = null;
                foundPath = found;
                isValidPath = true;
                inputPath = lastFoundPathUsedGogExtract ? Path.GetDirectoryName(source) : found;
                isSearching = false;
                
                // Automatically proceed when valid path is found (after delay)
                StartCoroutine(DelayedContinue(found, FoundHeadline("Found game data"), source));
                yield break;
            }
        }
        
        workingHeadline = null;
        isSearching = false;
        if (notFoundMessage == null)
        {
            statusMessage = previousMessage;
        }
        else if (!string.IsNullOrEmpty(lastGogExtractError))
        {
            statusMessage = lastGogExtractError;
        }
        else
        {
            statusMessage = notFoundMessage;
        }
    }
    
    /// <summary>
    /// Where the search at the window's start looks: the game's folders as GOG's and Steam's installers write them in
    /// the registry, each two levels deep (the folder itself is the first).
    /// </summary>
    private static List<SearchRoot> RegistrySearchRoots()
    {
        var roots = new List<SearchRoot>();
        List<string> installs = InstallFoldersFromRegistry();
        foreach (string install in installs)
        {
            AddRoot(roots, install, 2);
        }
        UnityEngine.Debug.Log("Search at start: game folders in the registry " + (installs.Count == 0 ? "none" : string.Join("; ", installs)));
        return roots;
    }
    
    /// <summary>
    /// Where Auto Search looks, in order, each folder with how many levels of it are searched (the folder itself is
    /// the first): the usual places, four, among them C:\UW and C:\DOSBox for a copy of the DOS game; then the
    /// steamapps\common folder of every Steam library, three - the folder, each game's, and one below.
    /// </summary>
    private static List<SearchRoot> FixedSearchRoots()
    {
        var roots = new List<SearchRoot>();
        AddRoot(roots, @"C:\GOG Games", 4);
        AddRoot(roots, @"C:\Games", 4);
        AddRoot(roots, @"C:\UW", 4);
        AddRoot(roots, @"C:\DOSBox", 4);
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 4);
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 4);
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 4);
        AddRoot(roots, Directory.GetCurrentDirectory(), 4);
        AddRoot(roots, Path.GetDirectoryName(Directory.GetCurrentDirectory()), 4);
        foreach (string common in SteamCommonFolders())
        {
            AddRoot(roots, common, 3);
        }
        return roots;
    }
    
    /// <summary>
    /// Adds a folder to search, once: the game's parent folder is often C:\Games, which is already there, and searching
    /// it twice costs a whole walk of it when the game is not found.
    /// </summary>
    private static void AddRoot(List<SearchRoot> roots, string folder, int depth)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }
        string key = folder.Replace('/', '\\').TrimEnd('\\');
        if (!roots.Exists(r => string.Equals(r.Folder.Replace('/', '\\').TrimEnd('\\'), key, StringComparison.OrdinalIgnoreCase)))
        {
            roots.Add(new SearchRoot(folder, depth));
        }
    }
    
    /// <summary>A folder Auto Search looks in, and how many levels of it (the folder itself is the first).</summary>
    private struct SearchRoot
    {
        public readonly string Folder;
        public readonly int Depth;
        
        public SearchRoot(string folder, int depth)
        {
            Folder = folder;
            Depth = depth;
        }
    }
    
    /// <summary>
    /// The steamapps\common folder of every Steam library found. Steam puts a game in
    /// &lt;library&gt;\steamapps\common\&lt;game&gt;, deeper than the search from Program Files goes, and a library may be
    /// on any drive: libraryfolders.vdf in the Steam folder lists them all. On Windows the search at start has already
    /// read the game's folder from Steam's uninstall key, so this is for a Steam folder the registry does not name:
    /// in Program Files, and under Proton or Wine the Linux one, reached through drive Z:, which maps the Linux root;
    /// Wine hands HOME to the game as WINEHOME, and Proton gives the Steam folder as STEAM_COMPAT_CLIENT_INSTALL_PATH.
    /// </summary>
    private static List<string> SteamCommonFolders()
    {
        var libraries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("WINEHOME");
        }
        var steamFolders = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
            FromUnixPath(Environment.GetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH"))
        };
        if (!string.IsNullOrEmpty(home))
        {
            steamFolders.Add(FromUnixPath(home + "/.local/share/Steam"));
            steamFolders.Add(FromUnixPath(home + "/.steam/steam"));
            steamFolders.Add(FromUnixPath(home + "/.var/app/com.valvesoftware.Steam/.local/share/Steam"));
        }
        foreach (string steam in steamFolders)
        {
            AddSteamLibraries(steam, libraries, seen);
        }
        UnityEngine.Debug.Log("Auto Search: Steam libraries " + (libraries.Count == 0 ? "none" : string.Join("; ", libraries)));
        return libraries;
    }
    
    /// <summary>
    /// Adds the steamapps\common folders of the libraries a Steam folder lists in its libraryfolders.vdf, and its own
    /// only when no vdf lists any: the vdf names the folder as Steam knows it, and on Linux ~/.steam/steam is a link
    /// to ~/.local/share/Steam, so the same library would be searched twice.
    /// </summary>
    private static void AddSteamLibraries(string steam, List<string> libraries, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(steam) || !Directory.Exists(steam))
        {
            return;
        }
        void AddLibrary(string library)
        {
            string common = Path.Combine(library, "steamapps", "common");
            try
            {
                if (Directory.Exists(common) && seen.Add(Path.GetFullPath(common).TrimEnd('\\', '/')))
                {
                    libraries.Add(common);
                }
            }
            catch (Exception)
            {
                // A malformed path in libraryfolders.vdf
            }
        }
        int listed = 0;
        string[] vdfs = { Path.Combine(steam, "steamapps", "libraryfolders.vdf"), Path.Combine(steam, "config", "libraryfolders.vdf") };
        foreach (string vdf in vdfs)
        {
            try
            {
                if (!File.Exists(vdf))
                {
                    continue;
                }
                // "path"		"D:\\SteamLibrary": the backslashes are doubled in the file
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\""))
                {
                    AddLibrary(FromUnixPath(m.Groups[1].Value.Replace("\\\\", "\\")));
                    listed++;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Could not read {vdf}: {ex.Message}");
            }
        }
        if (listed == 0)
        {
            AddLibrary(steam);
        }
    }
    
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("advapi32.dll", EntryPoint = "RegGetValueW", CharSet = CharSet.Unicode)]
    private static extern int RegGetValue(UIntPtr hkey, string subKey, string valueName, uint flags, IntPtr type,
        StringBuilder data, ref uint dataBytes);
    
    [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyEx(UIntPtr hkey, string subKey, uint options, uint samDesired, out UIntPtr result);
    
    [DllImport("advapi32.dll", EntryPoint = "RegEnumKeyExW", CharSet = CharSet.Unicode)]
    private static extern int RegEnumKeyEx(UIntPtr hkey, uint index, StringBuilder name, ref uint nameChars, IntPtr reserved,
        IntPtr className, IntPtr classChars, IntPtr lastWriteTime);
    
    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(UIntPtr hkey);
    
    private static readonly UIntPtr HKEY_LOCAL_MACHINE = new UIntPtr(0x80000002u);
    private const uint RRF_RT_REG_SZ = 0x2;
    private const uint KEY_READ = 0x20019;
    
    /// <summary>
    /// A string value from the registry, null if it is not there. Read through advapi32, as NativeFileDialog calls
    /// comdlg32: Microsoft.Win32.Registry is not in the .NET Standard profile.
    /// </summary>
    private static string RegistryString(UIntPtr hive, string key, string value)
    {
        try
        {
            var data = new StringBuilder(1024);
            uint bytes = (uint)data.Capacity * 2;
            if (RegGetValue(hive, key, value, RRF_RT_REG_SZ, IntPtr.Zero, data, ref bytes) == 0 && data.Length > 0)
            {
                return data.ToString();
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"Could not read {key}\\{value} in the registry: {ex.Message}");
        }
        return null;
    }
    
    /// <summary>The names of a registry key's subkeys, none if the key is not there.</summary>
    private static List<string> RegistrySubkeys(UIntPtr hive, string key)
    {
        var names = new List<string>();
        try
        {
            if (RegOpenKeyEx(hive, key, 0, KEY_READ, out UIntPtr handle) != 0)
            {
                return names;
            }
            try
            {
                for (uint index = 0; ; index++)
                {
                    var name = new StringBuilder(256);
                    uint chars = (uint)name.Capacity;
                    if (RegEnumKeyEx(handle, index, name, ref chars, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0)
                    {
                        break;
                    }
                    names.Add(name.ToString());
                }
            }
            finally
            {
                RegCloseKey(handle);
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"Could not read {key} in the registry: {ex.Message}");
        }
        return names;
    }
    
    /// <summary>
    /// The game's folders the installers write in the registry, searched when the window opens. GOG writes one key per
    /// game under GOG.com\Games, named by its product id, with the folder in "path" (1207662463 for this game, and
    /// 1207662473 for the sequel, whose game.gog holds this game too): every one is taken, and the search keeps the
    /// folders that hold the game. Steam writes the folder of the game, app 3067580, in its uninstall key.
    /// </summary>
    private static List<string> InstallFoldersFromRegistry()
    {
        var folders = new List<string>();
        foreach (string games in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" })
        {
            foreach (string id in RegistrySubkeys(HKEY_LOCAL_MACHINE, games))
            {
                AddFolder(folders, RegistryString(HKEY_LOCAL_MACHINE, games + "\\" + id, "path"));
            }
        }
        foreach (string uninstall in new[] { @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            AddFolder(folders, RegistryString(HKEY_LOCAL_MACHINE, uninstall + @"\Steam App 3067580", "InstallLocation"));
        }
        return folders;
    }
    
    /// <summary>Adds a folder read from the registry, with backslashes, once.</summary>
    private static void AddFolder(List<string> folders, string folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }
        folder = folder.Replace('/', '\\').TrimEnd('\\');
        if (!folders.Exists(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
        {
            folders.Add(folder);
        }
    }
#else
    /// <summary>No registry outside Windows: the search starts from the usual places.</summary>
    private static List<string> InstallFoldersFromRegistry()
    {
        return new List<string>();
    }
#endif
    
    /// <summary>
    /// A Linux path as this process reaches it: as it is in a native build, through drive Z: under Proton or Wine,
    /// where the game is a Windows program. Anything else comes back unchanged.
    /// </summary>
    private static string FromUnixPath(string path)
    {
        if (string.IsNullOrEmpty(path) || Path.DirectorySeparatorChar == '/' || !path.StartsWith("/"))
        {
            return path;
        }
        return "Z:" + path.Replace('/', '\\');
    }
    
    private string SearchDirectory(string directory, int maxDepth)
    {
        if (maxDepth <= 0)
        {
            return null;
        }
        
        try
        {
            // Check if SOUND/UW01.XMI exists in this directory (loose files)
            string soundFile = Path.Combine(directory, "Sound", "UW01.XMI");
            if (File.Exists(soundFile))
            {
                return directory;
            }
            
            // If loose files not found, check for the image of the CD
            string extractedPath = FromDiscImage(directory);
            if (!string.IsNullOrEmpty(extractedPath))
            {
                return extractedPath;
            }
            
            // Search subdirectories
            string[] subdirs = Directory.GetDirectories(directory);
            foreach (string subdir in subdirs)
            {
                string result = SearchDirectory(subdir, maxDepth - 1);
                if (!string.IsNullOrEmpty(result))
                {
                    return result;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
        catch (Exception)
        {
            // Skip on any error
        }
        
        return null;
    }
    
    /// <summary>
    /// Inspects an image of the CD, game.gog or game.bin (ISO9660 file system), to check if it contains game data files.
    /// Returns true if data files (CRIT, CUTS, DATA, SOUND folders or Sound/UW01.XMI) are found.
    /// Uses DiscUtils.Iso9660 CDReader which treats ISO files as a file system.
    /// </summary>
    private bool InspectGogFile(string gogFilePath)
    {
        try
        {
            using (FileStream isoStream = File.OpenRead(gogFilePath))
            {
                // joliet: false = standard ISO9660
                CDReader reader = new CDReader(isoStream, false);
                return reader.FileExists(@"\UW\SOUND\UW01.XMI;1");
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"Failed to inspect GOG file {gogFilePath}: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Extracts an image of the CD, game.gog or game.bin (ISO9660), to LooseData under Application.persistentDataPath.
    /// Returns the path to the extracted game root if successful, null otherwise.
    /// On failure, sets <see cref="lastGogExtractError"/> for UI. Runs on a worker thread: no Unity API.
    /// </summary>
    private string ExtractGogFile(string gogFilePath)
    {
        lastGogExtractError = "";
        lastGogExtractPerformedFresh = false;
        string looseDataPath = Path.Combine(persistentDataPath, "LooseData");
        try
        {
            // Check if already extracted and valid.
            // This has to search subdirectories the same way the verification at the end of this
            // method does, and for the same reason: the ISO may hold the game in a subfolder
            // rather than at its root, which is exactly what Ultima Underworld's game.gog does -
            // everything sits under UW\. Checking only looseDataPath\Sound\UW01.XMI never matched
            // for those, so the whole ISO was re-extracted on every pass through this dialog.
            // Besides being slow, that failed outright whenever another copy of the game was
            // running and holding the data files open.
            //
            // A copy cut short - the game closed half way through - would pass that check as soon as UW01.XMI is
            // written, so a marker file stands in LooseData from the start of an extraction to its verified end, and
            // while it is there the copy is not trusted and is made again.
            string marker = Path.Combine(looseDataPath, ExtractingMarker);
            string alreadyExtracted = File.Exists(marker) ? null : GameDataPath.ValidateGameDirectory(looseDataPath);
            if (alreadyExtracted != null)
            {
                UnityEngine.Debug.Log($"Using existing extracted data at: {alreadyExtracted}");
                return alreadyExtracted;
            }
            
            // Create LooseData directory if it doesn't exist
            if (!Directory.Exists(looseDataPath))
            {
                Directory.CreateDirectory(looseDataPath);
            }
            
            UnityEngine.Debug.Log($"Extracting GOG file {gogFilePath} to {looseDataPath}...");
            string imageName = Path.GetFileName(gogFilePath);
            workingHeadline = "Extracting " + imageName;
            File.WriteAllText(marker, imageName + " is being extracted here; while this file exists the copy is not complete.");
            using (FileStream isoStream = File.OpenRead(gogFilePath))
            {
                // Try joliet: false first (standard ISO9660)
                CDReader reader = new CDReader(isoStream, false);
                
                // Get all files recursively
                string[] allFiles = reader.GetFiles("", "*.*", SearchOption.AllDirectories);
                
                if (allFiles.Length == 0)
                {
                    lastGogExtractError = $"Could not read any files from {imageName} (empty or unsupported ISO).";
                    UnityEngine.Debug.LogWarning($"No files found in GOG ISO file {gogFilePath}");
                    return null;
                }
                
                lastGogExtractPerformedFresh = true;
                foreach (string file in allFiles)
                {
                    // Strip ISO9660 version suffix (;1) from filename for destination path
                    string cleanFile = file;
                    if (cleanFile.EndsWith(";1"))
                    {
                        cleanFile = cleanFile.Substring(0, cleanFile.Length - 2);
                    }
                    // Strip leading backslashes to prevent absolute path issues
                    cleanFile = cleanFile.TrimStart('\\');
                    string destinationPath = Path.Combine(looseDataPath, cleanFile);
                    string destinationDir = Path.GetDirectoryName(destinationPath);
                    
                    // Create directory if needed
                    if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }
                    
                    // Extract the file
                    using (SparseStream fileStream = reader.OpenFile(file, FileMode.Open))
                    using (FileStream outputStream = File.Create(destinationPath))
                    {
                        fileStream.CopyTo(outputStream);
                    }
                }
            }
            
            // Verify extraction by checking for Sound/UW01.XMI, in the extracted folder or one of
            // its subdirectories. Same call as the cache check above, deliberately: the two must
            // agree on what counts as extracted game data, or the cache never recognises what the
            // extraction just produced.
            string extractedPath = GameDataPath.ValidateGameDirectory(looseDataPath);
            if (extractedPath == null)
            {
                lastGogExtractError = StatusMessageTwoLines(
                    $"Extracted {Path.GetFileName(gogFilePath)} but could not find SOUND/UW01.XMI. Try deleting the LooseData folder there and validating again.",
                    looseDataPath);
                UnityEngine.Debug.LogWarning($"Extracted GOG file but could not find Sound/UW01.XMI in {looseDataPath}");
                return null;
            }
            
            File.Delete(marker);
            UnityEngine.Debug.Log($"Successfully extracted GOG file to: {extractedPath}");
            return extractedPath;
        }
        catch (UnauthorizedAccessException ex)
        {
            lastGogExtractError = StatusMessageTwoLines(
                $"Cannot write extracted game data (access denied): {ex.Message}",
                persistentDataPath);
            UnityEngine.Debug.LogError($"Failed to extract GOG file {gogFilePath}: {ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            lastGogExtractError = StatusMessageTwoLines("Could not write extracted game data (disk or file error):", ex.Message);
            UnityEngine.Debug.LogError($"Failed to extract GOG file {gogFilePath}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            lastGogExtractError = StatusMessageTwoLines($"Failed to extract {Path.GetFileName(gogFilePath)}:", ex.Message);
            UnityEngine.Debug.LogError($"Failed to extract GOG file {gogFilePath}: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>First line = fixed text; second line = path or detail (avoids one long line in the dialog).</summary>
    private static string StatusMessageTwoLines(string headline, string secondLine)
    {
        if (string.IsNullOrEmpty(secondLine))
        {
            return headline;
        }
        return headline + "\n" + secondLine;
    }
    
    /// <summary>
    /// The status headline for a path found: loose files, or a game.gog or game.bin unpacked now or before. The line
    /// under it names the source, not the copy in LooseData.
    /// </summary>
    private string FoundHeadline(string looseFiles)
    {
        return !lastFoundPathUsedGogExtract ? looseFiles
            : lastGogExtractPerformedFresh ? "Extracted game data from" : "Using game data from";
    }
    
    /// <summary>
    /// Saves the path and moves on to the title after autoContinueDelay, the headline's dots counting meanwhile so
    /// the window is not taken for stuck.
    /// </summary>
    private IEnumerator DelayedContinue(string path, string headline, string shown)
    {
        float end = Time.time + autoContinueDelay;
        while (Time.time < end)
        {
            statusMessage = StatusMessageTwoLines(WithDots(headline), shown);
            yield return null;
        }
        GameDataPath.SetGameRootPath(path);
        SceneManager.LoadScene("Logos");
    }
}
