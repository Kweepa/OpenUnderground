using UnityEngine;
using System.IO;

public static class GameDataPath
{
    private const string PLAYER_PREFS_KEY = "GameDataRootPath";
    
    /// <summary>
    /// Gets the root game data directory path from PlayerPrefs.
    /// Returns null if not set or invalid.
    /// </summary>
    public static string GetGameRootPath()
    {
        string savedPath = PlayerPrefs.GetString(PLAYER_PREFS_KEY, "");
        
        if (string.IsNullOrEmpty(savedPath))
        {
            return null;
        }
        
        // Verify the directory exists
        if (!Directory.Exists(savedPath))
        {
            return null;
        }
        
        return savedPath;
    }
    
    /// <summary>
    /// Sets the root game data directory path in PlayerPrefs.
    /// </summary>
    public static void SetGameRootPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            PlayerPrefs.DeleteKey(PLAYER_PREFS_KEY);
        }
        else
        {
            PlayerPrefs.SetString(PLAYER_PREFS_KEY, path);
            PlayerPrefs.Save();
        }
    }
    
    /// <summary>
    /// Gets the path to the DATA directory.
    /// </summary>
    public static string GetDataPath()
    {
        string root = GetGameRootPath();
        if (root == null)
        {
            // Fallback to old behavior
            return Path.Combine(Application.dataPath, "../Data");
        }
        return Path.Combine(root, "Data");
    }
    
    /// <summary>
    /// Gets the path to the CRIT directory.
    /// </summary>
    public static string GetCritPath()
    {
        string root = GetGameRootPath();
        if (root == null)
        {
            // Fallback to old behavior
            return Path.Combine(Application.dataPath, "../Crit");
        }
        return Path.Combine(root, "Crit");
    }
    
    /// <summary>
    /// Gets the path to the SOUND directory.
    /// </summary>
    public static string GetSoundPath()
    {
        string root = GetGameRootPath();
        if (root == null)
        {
            // Fallback to old behavior
            return Path.Combine(Application.dataPath, "../Sound");
        }
        return Path.Combine(root, "Sound");
    }
    
    /// <summary>
    /// Gets the path to the CUTS directory.
    /// </summary>
    public static string GetCutsPath()
    {
        string root = GetGameRootPath();
        if (root == null)
        {
            // Fallback to old behavior
            return Path.Combine(Application.dataPath, "../Cuts");
        }
        return Path.Combine(root, "Cuts");
    }
    
    /// <summary>
    /// Validates that the given path contains SOUND/UW01.XMI.
    /// Returns the root directory (parent of SOUND) if valid, null otherwise.
    /// </summary>
    public static string ValidateGameDirectory(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return null;
        }
        
        // Check if SOUND/UW01.XMI exists directly
        string soundPath = Path.Combine(path, "Sound", "UW01.XMI");
        if (File.Exists(soundPath))
        {
            return path;
        }
        
        // Search subdirectories for SOUND/UW01.XMI
        try
        {
            string[] subdirs = Directory.GetDirectories(path);
            foreach (string subdir in subdirs)
            {
                string testPath = Path.Combine(subdir, "Sound", "UW01.XMI");
                if (File.Exists(testPath))
                {
                    return subdir;
                }
                
                // Also check nested subdirectories (one level deeper)
                try
                {
                    string[] nestedDirs = Directory.GetDirectories(subdir);
                    foreach (string nestedDir in nestedDirs)
                    {
                        string nestedTestPath = Path.Combine(nestedDir, "Sound", "UW01.XMI");
                        if (File.Exists(nestedTestPath))
                        {
                            return nestedDir;
                        }
                    }
                }
                catch (System.Exception)
                {
                    // Skip directories we can't access
                    continue;
                }
            }
        }
        catch (System.Exception)
        {
            // Can't search subdirectories
            return null;
        }
        
        return null;
    }
}

