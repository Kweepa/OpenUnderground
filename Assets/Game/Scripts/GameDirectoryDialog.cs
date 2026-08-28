using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using System.IO;
using System.Collections;
using System.Diagnostics;
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
                    return !isSearching;
                case ESelectedControl.ValidatePath:
                    return !string.IsNullOrEmpty(inputPath);
                case ESelectedControl.ContinueQuit:
                    return true; // Always enabled (shows Quit if invalid)
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
                    if (!isSearching)
                    {
                        StartCoroutine(AutoSearch());
                    }
                    break;
                case ESelectedControl.ValidatePath:
                    if (!string.IsNullOrEmpty(inputPath))
                    {
                        ValidateInputPath();
                    }
                    break;
                case ESelectedControl.ContinueQuit:
                    if (isValidPath && !string.IsNullOrEmpty(foundPath))
                    {
                        GameDataPath.SetGameRootPath(foundPath);
                        SceneManager.LoadScene("Logos");
                    }
                    else
                    {
                        // Quit
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
        
        // Browse button (opens Windows Explorer) - aligned with input field
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
        GUI.enabled = !isSearching;
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
        bool canValidate = !string.IsNullOrEmpty(inputPath);
        GUI.enabled = canValidate;
        DrawButtonWithBox(validateRect, "Validate Path", validateSelected && canValidate);
        if (GUI.Button(validateRect, "Validate Path", buttonStyle))
        {
            ValidateInputPath();
        }
        GUI.enabled = true;
        
        // Continue/Quit button
        Rect continueRect = new Rect(buttonStartX + (buttonWidth + buttonSpacing) * 2, buttonsY, buttonWidth, 80);
        bool continueSelected = selectedControl == ESelectedControl.ContinueQuit;
        string continueButtonText = isValidPath ? "Continue" : "Quit";
        DrawButtonWithBox(continueRect, continueButtonText, continueSelected);
        if (GUI.Button(continueRect, continueButtonText, buttonStyle))
        {
            if (isValidPath && !string.IsNullOrEmpty(foundPath))
            {
                GameDataPath.SetGameRootPath(foundPath);
                SceneManager.LoadScene("Logos");
            }
            else
            {
                // Quit
                Application.Quit();
            }
        }
        
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
    
    private void BrowseForFolder()
    {
        // On Windows, try to open Explorer
        try
        {
            string startPath = string.IsNullOrEmpty(inputPath) ? Environment.GetFolderPath(Environment.SpecialFolder.MyComputer) : inputPath;
            if (!Directory.Exists(startPath))
            {
                startPath = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);
            }
            
            Process.Start("explorer.exe", startPath);
            statusMessage = "Please navigate to the game directory in Explorer and copy the path, then paste it above.";
        }
        catch (Exception ex)
        {
            statusMessage = StatusMessageTwoLines("Could not open file browser:", ex.Message);
        }
    }
    
    private void ValidateInputPath()
    {
        lastGogExtractError = "";
        lastFoundPathUsedGogExtract = false;
        if (string.IsNullOrEmpty(inputPath))
        {
            statusMessage = "Please enter a directory path.";
            isValidPath = false;
            return;
        }
        
        // First try standard validation (loose files)
        string validatedPath = GameDataPath.ValidateGameDirectory(inputPath);
        
        // If not found and input is a directory, check for game.gog file
        if (validatedPath == null && Directory.Exists(inputPath))
        {
            string gogFile = Path.Combine(inputPath, "game.gog");
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
        
        if (validatedPath != null)
        {
            foundPath = validatedPath;
            isValidPath = true;
            if (lastFoundPathUsedGogExtract)
            {
                statusMessage = lastGogExtractPerformedFresh
                    ? StatusMessageTwoLines("Extracted game data to:", validatedPath)
                    : StatusMessageTwoLines("Using extracted game data at:", validatedPath);
            }
            else
            {
                statusMessage = StatusMessageTwoLines("Valid game directory found:", validatedPath);
            }
            
            // Automatically proceed when valid path is found (after delay)
            StartCoroutine(DelayedContinue(validatedPath));
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
    
    private IEnumerator AutoSearch()
    {
        lastGogExtractError = "";
        lastFoundPathUsedGogExtract = false;
        isSearching = true;
        isValidPath = false;
        statusMessage = "Searching for game directory...";
        
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
            
            statusMessage = StatusMessageTwoLines("Searching in:", searchPath);
            yield return null; // Allow UI to update
            
            string found = SearchDirectory(searchPath, 4); // Max depth 4
            if (!string.IsNullOrEmpty(found))
            {
                foundPath = found;
                isValidPath = true;
                inputPath = found;
                if (lastFoundPathUsedGogExtract)
                {
                    statusMessage = lastGogExtractPerformedFresh
                        ? StatusMessageTwoLines("Extracted game data to:", found)
                        : StatusMessageTwoLines("Using extracted game data at:", found);
                }
                else
                {
                    statusMessage = StatusMessageTwoLines("Found game data:", found);
                }
                isSearching = false;
                
                // Automatically proceed when valid path is found (after delay)
                StartCoroutine(DelayedContinue(found));
                yield break;
            }
        }
        
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
    /// On failure, sets <see cref="lastGogExtractError"/> for UI.
    /// </summary>
    private string ExtractGogFile(string gogFilePath)
    {
        lastGogExtractError = "";
        lastGogExtractPerformedFresh = false;
        string looseDataPath = Path.Combine(Application.persistentDataPath, "LooseData");
        try
        {
            // Check if already extracted and valid
            string existingSoundFile = Path.Combine(looseDataPath, "Sound", "UW01.XMI");
            if (Directory.Exists(looseDataPath) && File.Exists(existingSoundFile))
            {
                UnityEngine.Debug.Log($"Using existing extracted data at: {looseDataPath}");
                return looseDataPath;
            }
            
            // Create LooseData directory if it doesn't exist
            if (!Directory.Exists(looseDataPath))
            {
                Directory.CreateDirectory(looseDataPath);
            }
            
            UnityEngine.Debug.Log($"Extracting GOG file {gogFilePath} to {looseDataPath}...");
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
            
            // Verify extraction by checking for Sound/UW01.XMI
            string extractedSoundFile = Path.Combine(looseDataPath, "Sound", "UW01.XMI");
            if (!File.Exists(extractedSoundFile))
            {
                // Try to find it in subdirectories (GOG files might have nested structure)
                string[] subdirs = Directory.GetDirectories(looseDataPath);
                foreach (string subdir in subdirs)
                {
                    string nestedSoundFile = Path.Combine(subdir, "Sound", "UW01.XMI");
                    if (File.Exists(nestedSoundFile))
                    {
                        UnityEngine.Debug.Log($"Found game data in nested directory: {subdir}");
                        return subdir;
                    }
                }
                
                lastGogExtractError = StatusMessageTwoLines(
                    "Extracted game.gog but could not find SOUND/UW01.XMI. Try deleting the LooseData folder there and validating again.",
                    looseDataPath);
                UnityEngine.Debug.LogWarning($"Extracted GOG file but could not find Sound/UW01.XMI in {looseDataPath}");
                return null;
            }
            
            UnityEngine.Debug.Log($"Successfully extracted GOG file to: {looseDataPath}");
            return looseDataPath;
        }
        catch (UnauthorizedAccessException ex)
        {
            lastGogExtractError = StatusMessageTwoLines(
                $"Cannot write extracted game data (access denied): {ex.Message}",
                Application.persistentDataPath);
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
    
    private IEnumerator DelayedContinue(string path)
    {
        yield return new WaitForSeconds(autoContinueDelay);
        GameDataPath.SetGameRootPath(path);
        SceneManager.LoadScene("Logos");
    }
}
