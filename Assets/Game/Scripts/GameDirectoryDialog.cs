using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using System.IO;
using System.Collections;
using System;
using System.Globalization;
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
    /// What the window is busy with while a validation or a search runs on a worker thread - "Validating",
    /// "Searching", "Extracting game.gog" - and the folder it is looking at; null when idle. Shown with dots that
    /// count 1, 2, 3, so the window is seen to be working: unpacking game.gog takes a while on a slow disk.
    /// </summary>
    private volatile string workingHeadline;
    /// <summary>The path in the field when it last validated: Validate Path is not offered again for it.</summary>
    private string validatedInput;
    private volatile string workingDetail;
    /// <summary>Application.persistentDataPath, read on the main thread for the extraction on the worker.</summary>
    private string persistentDataPath;
    /// <summary>In LooseData from the start of an extraction to its verified end: a copy that still has it is cut short.</summary>
    private const string ExtractingMarker = "extracting.tmp";
    
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
            if (File.Exists(soundFile))
            {
                // Path is valid, skip to Logos
                UnityEngine.Debug.Log($"Game directory already set: {savedPath}");
                SceneManager.LoadScene("Logos");
                return;
            }
            else
            {
                // Path exists but file is missing, clear it
                GameDataPath.SetGameRootPath(null);
                statusMessage = "Previously saved directory is no longer valid. Please select a new directory.";
            }
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
                    return true; // Always enabled
                case ESelectedControl.AutoSearch:
                    return CanAutoSearch();
                case ESelectedControl.ValidatePath:
                    return CanValidate();
                case ESelectedControl.ContinueQuit:
                    return CanQuit();
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
                    BrowseForFolder();
                    break;
                case ESelectedControl.AutoSearch:
                    if (CanAutoSearch())
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
                    if (CanQuit())
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
        
        // Instructions - centered below title with good spacing
        float instructionsY = titleY + 100;
        GUI.Label(new Rect(dialogX + 60, instructionsY, dialogWidth - 120, 100),
            "Enter the directory containing the original game files, for example a GOG installation. The directory should contain game.gog or CRIT, CUTS, DATA, and SOUND folders.", labelStyle);
        
        // Path input section - centered vertically in dialog
        float inputSectionY = dialogY + dialogHeight / 2 - 30; // Center vertically, adjust for half height of input
        float browseButtonWidth = 120;
        float inputFieldWidth = dialogWidth - 120; // Leave space for padding (60px padding on each side)
        
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
        GUI.enabled = CanAutoSearch();
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
        GUI.enabled = CanQuit();
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
    /// The system's Open dialog, the one the Setup screen's music page uses, showing the two files that mark the game's
    /// folder: game.gog in a GOG installation, UW.EXE in a copy of the files. The folder of the file picked goes in the
    /// field and is validated, which also unpacks game.gog. It used to start explorer.exe and ask for the path to be
    /// pasted, which did nothing under Proton.
    /// </summary>
    private void BrowseForFolder()
    {
        if (!NativeFileDialog.IsAvailable)
        {
            statusMessage = "Type the path of the game's folder above.";
            return;
        }

        string start = !string.IsNullOrEmpty(inputPath) && Directory.Exists(inputPath) ? inputPath : null;
        string file = NativeFileDialog.OpenFile("Find game.gog or UW.EXE in the game's folder", "Game files",
            new[] { "game.gog", "UW.EXE" }, start, out string error);
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
    /// Validate Path is offered when the field holds a path, nothing is being validated or searched, and the path is
    /// not the one that has just validated. After a failure it is offered again, with Auto Search and Quit.
    /// </summary>
    private bool CanValidate()
    {
        return !string.IsNullOrEmpty(inputPath) && !isSearching && !(isValidPath && inputPath == validatedInput);
    }
    
    /// <summary>
    /// Quit is offered when nothing is being validated, searched or unpacked - quitting half way through game.gog
    /// would leave a copy cut short - and not once a path has validated, when the game is about to move on.
    /// </summary>
    private bool CanQuit()
    {
        return !isSearching && !isValidPath;
    }
    
    /// <summary>Auto Search is offered on the same terms as Quit: not while working, nor once the game is about to load.</summary>
    private bool CanAutoSearch()
    {
        return !isSearching && !isValidPath;
    }
    
    private void ValidateInputPath()
    {
        if (isSearching || (isValidPath && inputPath == validatedInput))
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
            validatedInput = path;
            string headline = !lastFoundPathUsedGogExtract ? "Valid game directory found"
                : lastGogExtractPerformedFresh ? "Extracted game data to" : "Using extracted game data at";
            
            // Automatically proceed when valid path is found (after delay)
            StartCoroutine(DelayedContinue(validatedPath, headline));
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
                statusMessage = "Invalid directory. Could not find SOUND/UW01.XMI or game.gog file. Please check the path and try again.";
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
    /// The game's data in a folder: loose files, or game.gog unpacked. Runs on a worker thread, so nothing in here
    /// may touch the Unity API.
    /// </summary>
    private string FindGameData(string path)
    {
        // First try standard validation (loose files)
        string validatedPath = GameDataPath.ValidateGameDirectory(path);
        
        // If not found and input is a directory, check for game.gog file
        if (validatedPath == null && Directory.Exists(path))
        {
            string gogFile = Path.Combine(path, "game.gog");
            if (File.Exists(gogFile))
            {
                // Check if GOG file contains game data
                if (InspectGogFile(gogFile))
                {
                    lastFoundPathUsedGogExtract = true;
                    string extractedPath = ExtractGogFile(gogFile);
                    if (!string.IsNullOrEmpty(extractedPath))
                    {
                        // Verify extraction was successful
                        string extractedSoundFile = Path.Combine(extractedPath, "Sound", "UW01.XMI");
                        if (File.Exists(extractedSoundFile))
                        {
                            validatedPath = extractedPath;
                        }
                    }
                }
            }
        }
        return validatedPath;
    }
    
    private IEnumerator AutoSearch()
    {
        lastGogExtractError = "";
        lastFoundPathUsedGogExtract = false;
        isSearching = true;
        isValidPath = false;
        workingDetail = null;
        workingHeadline = "Searching";
        ShowWorking();
        
        string[] searchPaths = {
            @"C:\GOG Games",
            @"C:\Games",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(Directory.GetCurrentDirectory())
        };
        
        foreach (string searchPath in searchPaths)
        {
            if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath))
            {
                continue;
            }
            
            // Each folder is searched on a worker thread, so the window keeps drawing: unpacking a game.gog found
            // there takes a while.
            workingHeadline = "Searching";
            workingDetail = searchPath;
            System.Threading.Tasks.Task<string> work = System.Threading.Tasks.Task.Run(() => SearchDirectory(searchPath, 4)); // Max depth 4
            while (!work.IsCompleted)
            {
                ShowWorking();
                yield return null;
            }
            
            string found = work.IsFaulted ? null : work.Result;
            if (!string.IsNullOrEmpty(found))
            {
                workingHeadline = null;
                foundPath = found;
                isValidPath = true;
                inputPath = found;
                validatedInput = found;
                string headline = !lastFoundPathUsedGogExtract ? "Found game data"
                    : lastGogExtractPerformedFresh ? "Extracted game data to" : "Using extracted game data at";
                isSearching = false;
                
                // Automatically proceed when valid path is found (after delay)
                StartCoroutine(DelayedContinue(found, headline));
                yield break;
            }
        }
        
        workingHeadline = null;
        isSearching = false;
        if (!string.IsNullOrEmpty(lastGogExtractError))
        {
            statusMessage = lastGogExtractError;
        }
        else
        {
            statusMessage = "Auto-search completed. Could not find game directory. Please enter the path manually.";
        }
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
            
            // If loose files not found, check for game.gog file
            string gogFile = Path.Combine(directory, "game.gog");
            if (File.Exists(gogFile))
            {
                // Check if GOG file contains game data
                if (InspectGogFile(gogFile))
                {
                    lastFoundPathUsedGogExtract = true;
                    string extractedPath = ExtractGogFile(gogFile);
                    if (!string.IsNullOrEmpty(extractedPath))
                    {
                        // Verify extraction was successful
                        string extractedSoundFile = Path.Combine(extractedPath, "Sound", "UW01.XMI");
                        if (File.Exists(extractedSoundFile))
                        {
                            return extractedPath;
                        }
                    }
                }
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
    /// Inspects a GOG file (ISO9660 file system) to check if it contains game data files.
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
    /// Extracts a GOG file (ISO9660) to LooseData under Application.persistentDataPath.
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
            workingHeadline = "Extracting game.gog";
            File.WriteAllText(marker, "game.gog is being extracted here; while this file exists the copy is not complete.");
            using (FileStream isoStream = File.OpenRead(gogFilePath))
            {
                // Try joliet: false first (standard ISO9660)
                CDReader reader = new CDReader(isoStream, false);
                
                // Get all files recursively
                string[] allFiles = reader.GetFiles("", "*.*", SearchOption.AllDirectories);
                
                if (allFiles.Length == 0)
                {
                    lastGogExtractError = "Could not read any files from game.gog (empty or unsupported ISO).";
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
                    "Extracted game.gog but could not find SOUND/UW01.XMI. Try deleting the LooseData folder there and validating again.",
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
            lastGogExtractError = StatusMessageTwoLines("Failed to extract game.gog:", ex.Message);
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
    /// Saves the path and moves on to the title after autoContinueDelay, the headline's dots counting meanwhile so
    /// the window is not taken for stuck.
    /// </summary>
    private IEnumerator DelayedContinue(string path, string headline)
    {
        float end = Time.time + autoContinueDelay;
        while (Time.time < end)
        {
            statusMessage = StatusMessageTwoLines(WithDots(headline), path);
            yield return null;
        }
        GameDataPath.SetGameRootPath(path);
        SceneManager.LoadScene("Logos");
    }
}
