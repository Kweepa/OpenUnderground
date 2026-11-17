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
        return ChildOr(root, "Data");
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
        return ChildOr(root, "Crit");
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
        return ChildOr(root, "Sound");
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
        return ChildOr(root, "Cuts");
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
        
        // SOUND/UW01.XMI, accepting the uppercase names an ISO9660 image extracts.
        if (HasLooseMusic(path))
        {
            return path;
        }
        
        // Search subdirectories for SOUND/UW01.XMI
        try
        {
            string[] subdirs = Directory.GetDirectories(path);
            foreach (string subdir in subdirs)
            {
                if (HasLooseMusic(subdir))
                {
                    return subdir;
                }
                
                // Also check nested subdirectories (one level deeper)
                try
                {
                    string[] nestedDirs = Directory.GetDirectories(subdir);
                    foreach (string nestedDir in nestedDirs)
                    {
                        if (HasLooseMusic(nestedDir))
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

    /// <summary>True when this folder itself holds the game's music file, SOUND/UW01.XMI.</summary>
    public static bool HasLooseMusic(string directory)
    {
        string sound = FindChild(directory, "Sound");
        return sound != null && FindChild(sound, "UW01.XMI") != null;
    }

    /// <summary>
    /// A file or folder directly inside <paramref name="directory"/> named <paramref name="name"/>.
    /// The exact name wins; otherwise the first entry that matches ignoring case, which is how an
    /// ISO9660 extract (SOUND) meets the names the game uses (Sound) on a case-sensitive volume.
    /// </summary>
    public static string FindChild(string directory, string name)
    {
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(name) || !Directory.Exists(directory))
        {
            return null;
        }

        string direct = Path.Combine(directory, name);
        if (File.Exists(direct) || Directory.Exists(direct))
        {
            return direct;
        }

        try
        {
            foreach (string entry in Directory.GetFileSystemEntries(directory))
            {
                if (string.Equals(Path.GetFileName(entry), name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
        }
        catch (System.Exception)
        {
        }

        return null;
    }

    private static string ChildOr(string directory, string name)
    {
        return FindChild(directory, name) ?? Path.Combine(directory, name);
    }
}

