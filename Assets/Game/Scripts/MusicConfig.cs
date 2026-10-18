using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// The moments of the game that have music of their own. Music maps its states onto these: the four
/// exploring states are one Atmosphere list, and the title screen has one as well.
/// </summary>
public enum EMusicSituation
{
    Atmosphere,
    Combat,
    Injured,
    Winning,
    Alert,
    Victory,
    LevelUp,
    Death,
    Map,
    Sleep,
    Conversation,
    Title
}

/// <summary>A line of music.txt: a track, and whether it is switched on.</summary>
public class MusicEntry
{
    public readonly string Track;
    public bool Enabled;

    public MusicEntry(string track, bool enabled = true)
    {
        Track = track;
        Enabled = enabled;
    }
}

/// <summary>
/// The player's choice of music for each situation, kept in music.txt in a Music folder beside the
/// saves, one line per track, so that it can be changed by hand as well as from the Setup screen.
/// </summary>
/// <remarks>
/// A line is <c>Situation = file</c>. A plain file name is a file in the Music folder, which is where
/// the Setup screen copies the files the player picks; <c>UW:</c> is a file of the original game's
/// sound folder, so the original tracks are never copied; anything else is a full path and is used as
/// it is. A situation with several lines picks one of them at random each time it starts, and one with
/// no lines plays no music. A line with # in front is a track switched off: it stays in the list, and
/// in the Setup screen, without playing. The file is written the first time it is needed, with the original's
/// tracks, and from then on it is only read, or rewritten by the Setup screen.
/// </remarks>
public static class MusicConfig
{
    public const string SoundFolderPrefix = "UW:";

    private const string FileName = "music.txt";

    /// <summary>Formats a track can be in: the original's XMI, and what Unity's decoder plays.</summary>
    public static readonly string[] Extensions = { ".xmi", ".mp3", ".ogg", ".wav" };

    private static List<MusicEntry>[] lists;

    public static string Folder => Path.Combine(Application.persistentDataPath, "Music");

    public static string FilePath => Path.Combine(Folder, FileName);

    /// <summary>
    /// The original's tracks, as the remake has always placed them, except that the three ambient
    /// tracks are named once each.
    /// </summary>
    /// <remarks>
    /// The original draws its ambient track as rand() % 3 + 2, tracks 2, 3 and 4 with one chance in
    /// three each (UW.EXE 0x20545 and 0x2058d). The remake's four exploring states named uw04 twice,
    /// which played it twice as often as the other two.
    /// </remarks>
    private static readonly string[][] defaults =
    {
        new[] { "UW02.XMI", "UW03.XMI", "UW04.XMI" }, // Atmosphere
        new[] { "UW06.XMI" },                         // Combat
        new[] { "UW07.XMI" },                         // Injured
        new[] { "UW05.XMI" },                         // Winning
        new[] { "UW10.XMI" },                         // Alert
        new[] { "UW11.XMI" },                         // Victory
        new[] { "UW11.XMI" },                         // LevelUp
        new[] { "UW12.XMI" },                         // Death
        new[] { "UW15.XMI" },                         // Map
        new[] { "UW15.XMI" },                         // Sleep
        new[] { "UW15.XMI" },                         // Conversation
        new[] { "UW01.XMI" },                         // Title
    };

    private const string Header =
        "# Unity Underground music\n" +
        "#\n" +
        "# One line per track:  Situation = file\n" +
        "# A plain file name is a file in this folder. UW: is a file of the original game's sound\n" +
        "# folder, and a full path is used as it is.\n" +
        "# A situation with several lines picks one of them at random each time it starts.\n" +
        "# A situation with no lines plays no music.\n" +
        "# A # in front of a line switches its track off without removing it.\n" +
        "# Formats: xmi, mp3, ogg, wav.\n" +
        "# Situations: Atmosphere Combat Injured Winning Alert Victory LevelUp Death\n" +
        "#             Map Sleep Conversation Title\n" +
        "# The Setup screen of the title menu rewrites this file.\n";

    /// <summary>The tracks of a situation, as written in the file, switched on or off.</summary>
    public static IReadOnlyList<MusicEntry> Entries(EMusicSituation situation)
    {
        EnsureLoaded();
        return lists[(int)situation];
    }

    /// <summary>
    /// Reads music.txt, writing it first if it does not exist. Called by everything that reads the
    /// lists, and once at start-up so the file is there before anyone goes looking for it.
    /// </summary>
    public static void EnsureLoaded()
    {
        if (lists != null)
        {
            return;
        }

        if (!File.Exists(FilePath))
        {
            lists = DefaultLists();
            AddUW2Tracks(lists[(int)EMusicSituation.Atmosphere]);
            Save();
            return;
        }

        lists = NewLists();
        try
        {
            string[] lines = File.ReadAllLines(FilePath);
            for (int i = 0; i < lines.Length; ++i)
            {
                ParseLine(lines[i], i + 1);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Music: cannot read {FilePath}, playing the original's tracks: {e.Message}");
            lists = DefaultLists();
        }
    }

    private static void ParseLine(string line, int number)
    {
        string text = line.Trim();
        if (text.Length == 0)
        {
            return;
        }

        // A # in front is either a comment or a track switched off, and only a line that would be
        // a track without it is the second: anything else after a # is a comment, and says nothing.
        bool enabled = !text.StartsWith("#");
        if (!enabled)
        {
            text = text.Substring(1).Trim();
        }

        int equals = text.IndexOf('=');
        if (equals < 0)
        {
            if (enabled)
            {
                Debug.LogWarning($"Music: {FileName} line {number} has no '=', skipped: {text}");
            }
            return;
        }

        string name = text.Substring(0, equals).Trim();
        string file = text.Substring(equals + 1).Trim();
        if (!Enum.TryParse(name, true, out EMusicSituation situation) || !Enum.IsDefined(typeof(EMusicSituation), situation)
            || name.Length == 0 || char.IsDigit(name[0]))
        {
            if (enabled)
            {
                Debug.LogWarning($"Music: {FileName} line {number} names no situation, skipped: {text}");
            }
            return;
        }
        if (file.Length == 0)
        {
            if (enabled)
            {
                Debug.LogWarning($"Music: {FileName} line {number} names no file, skipped: {text}");
            }
            return;
        }

        lists[(int)situation].Add(new MusicEntry(file, enabled));
    }

    public static void Save()
    {
        StringBuilder text = new StringBuilder(Header);
        foreach (EMusicSituation situation in Enum.GetValues(typeof(EMusicSituation)))
        {
            text.Append('\n');
            foreach (MusicEntry entry in lists[(int)situation])
            {
                text.Append(entry.Enabled ? "" : "# ").Append(situation).Append(" = ").Append(entry.Track).Append('\n');
            }
        }

        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, text.ToString());
        }
        catch (Exception e)
        {
            // The lists still hold for this session: the music plays, it is only not remembered.
            Debug.LogWarning($"Music: cannot write {FilePath}: {e.Message}");
        }
    }

    /// <summary>The full path a line of the file stands for.</summary>
    public static string Resolve(string track)
    {
        if (string.IsNullOrEmpty(track))
        {
            return null;
        }

        try
        {
            if (track.StartsWith(SoundFolderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(GameDataPath.GetSoundPath(), track.Substring(SoundFolderPrefix.Length));
            }

            // Path.Combine returns the second path whole when it is absolute.
            return Path.Combine(Folder, track);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static bool Exists(string track)
    {
        string path = Resolve(track);
        return path != null && File.Exists(path);
    }

    public static bool IsOriginal(string track) => track.StartsWith(SoundFolderPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A track for the situation, as a full path, or null when it has none that is on disk.
    /// </summary>
    /// <param name="keep">
    /// The full path of the track playing now, if any: when it belongs to this situation as well it is
    /// the one returned, so a move between two situations that share a track does not restart it.
    /// </param>
    public static string Pick(EMusicSituation situation, string keep = null)
    {
        List<string> playable = PlayablePaths(situation);
        if (playable.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(keep))
        {
            foreach (string path in playable)
            {
                if (SamePath(path, keep))
                {
                    return path;
                }
            }
        }

        return playable[Random.Range(0, playable.Count)];
    }

    /// <summary>The full paths of the situation's tracks that are switched on and on disk.</summary>
    public static List<string> PlayablePaths(EMusicSituation situation)
    {
        List<string> playable = new();
        foreach (MusicEntry entry in Entries(situation))
        {
            if (!entry.Enabled)
            {
                continue;
            }
            string path = Resolve(entry.Track);
            if (path != null && File.Exists(path))
            {
                playable.Add(path);
            }
        }
        return playable;
    }

    /// <summary>Whether the situation has this track switched on.</summary>
    public static bool Contains(EMusicSituation situation, string path)
    {
        foreach (MusicEntry entry in Entries(situation))
        {
            if (entry.Enabled && SamePath(Resolve(entry.Track), path))
            {
                return true;
            }
        }
        return false;
    }

    public static bool SamePath(string a, string b)
    {
        if (a == null || b == null)
        {
            return false;
        }
        try
        {
            // Windows, and Wine under Proton, are the platforms the game ships for: neither cares
            // about case in a file name.
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool IsSupported(string path)
    {
        string extension = Path.GetExtension(path);
        foreach (string supported in Extensions)
        {
            if (string.Equals(extension, supported, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Adds a file the player picked to a situation. A file of the game's sound folder is named with
    /// UW: and stays where it is; any other is copied into the Music folder, so the player can move or
    /// delete the one they picked.
    /// </summary>
    /// <returns>An error to show, or null.</returns>
    public static string Add(EMusicSituation situation, string pickedPath)
    {
        EnsureLoaded();
        if (!IsSupported(pickedPath))
        {
            return $"{Path.GetFileName(pickedPath)}: not a format the game plays";
        }

        string track;
        try
        {
            track = SoundFolderTrack(pickedPath) ?? LibraryTrack(pickedPath) ?? CopyIntoLibrary(pickedPath);
        }
        catch (Exception e)
        {
            return $"{Path.GetFileName(pickedPath)}: {e.Message}";
        }

        // Picked again when it is already there: switched back on if it was off.
        List<MusicEntry> list = lists[(int)situation];
        foreach (MusicEntry existing in list)
        {
            if (string.Equals(existing.Track, track, StringComparison.OrdinalIgnoreCase))
            {
                if (!existing.Enabled)
                {
                    existing.Enabled = true;
                    Save();
                }
                return null;
            }
        }

        list.Add(new MusicEntry(track));
        Save();
        return null;
    }

    public static void SetEnabled(EMusicSituation situation, int index, bool enabled)
    {
        EnsureLoaded();
        List<MusicEntry> list = lists[(int)situation];
        if (index >= 0 && index < list.Count && list[index].Enabled != enabled)
        {
            list[index].Enabled = enabled;
            Save();
        }
    }

    public static void Remove(EMusicSituation situation, int index)
    {
        EnsureLoaded();
        List<MusicEntry> list = lists[(int)situation];
        if (index < 0 || index >= list.Count)
        {
            return;
        }

        list.RemoveAt(index);
        DeleteUnusedCopies();
        Save();
    }

    /// <summary>Back to the original's tracks only, and the copies nothing names any more deleted.</summary>
    public static void Reset()
    {
        lists = DefaultLists();
        DeleteUnusedCopies();
        Save();
    }

    /// <summary>One situation back to the original's tracks, the others left as they are.</summary>
    public static void Reset(EMusicSituation situation)
    {
        EnsureLoaded();
        lists[(int)situation] = DefaultList(situation);
        DeleteUnusedCopies();
        Save();
    }

    private static List<MusicEntry>[] NewLists()
    {
        int count = Enum.GetValues(typeof(EMusicSituation)).Length;
        List<MusicEntry>[] result = new List<MusicEntry>[count];
        for (int i = 0; i < count; ++i)
        {
            result[i] = new List<MusicEntry>();
        }
        return result;
    }

    private static List<MusicEntry>[] DefaultLists()
    {
        List<MusicEntry>[] result = NewLists();
        for (int i = 0; i < defaults.Length; ++i)
        {
            result[i] = DefaultList((EMusicSituation)i);
        }
        return result;
    }

    private static List<MusicEntry> DefaultList(EMusicSituation situation)
    {
        List<MusicEntry> list = new();
        foreach (string file in defaults[(int)situation])
        {
            list.Add(new MusicEntry(SoundFolderPrefix + file));
        }
        return list;
    }

    /// <summary>
    /// The ambient tracks of the sequel that players were told to copy into a UW2 folder inside the
    /// sound folder, added to Atmosphere where they are. Only when music.txt is first written: from
    /// then on the list is the player's.
    /// </summary>
    /// <remarks>
    /// The sequel ships two arrangements of each track, UWA for AdLib and UWR for Roland. Only the
    /// UWR ones are taken: the UW1 tracks this game plays are the Roland set whatever the soundfont,
    /// and the AdLib set played through the soundfonts it has sounds wrong.
    /// </remarks>
    private static void AddUW2Tracks(List<MusicEntry> atmosphere)
    {
        try
        {
            string folder = Path.Combine(GameDataPath.GetSoundPath(), "UW2");
            if (!Directory.Exists(folder))
            {
                return;
            }

            // Every file, and the extension tested here: a search pattern is case sensitive on some
            // file systems, and the sequel's files are named in capitals.
            string[] files = Directory.GetFiles(folder);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (Path.GetExtension(name).Equals(".xmi", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("UWA", StringComparison.OrdinalIgnoreCase))
                {
                    atmosphere.Add(new MusicEntry(SoundFolderPrefix + "UW2/" + name));
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Music: cannot read the UW2 folder: {e.Message}");
        }
    }

    /// <summary>The UW: name of a file inside the game's sound folder, or null.</summary>
    private static string SoundFolderTrack(string path)
    {
        string relative = RelativeTo(GameDataPath.GetSoundPath(), path);
        return relative != null ? SoundFolderPrefix + relative : null;
    }

    /// <summary>The plain name of a file already in the Music folder, or null.</summary>
    private static string LibraryTrack(string path)
    {
        string relative = RelativeTo(Folder, path);
        return relative != null && relative.IndexOf('/') < 0 ? relative : null;
    }

    private static string RelativeTo(string folder, string path)
    {
        string root = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return full.Substring(root.Length).Replace('\\', '/');
    }

    /// <summary>
    /// Copies a file into the Music folder under its own name, or under a numbered one when a
    /// different file already has that name; the same file picked twice is copied once.
    /// </summary>
    private static string CopyIntoLibrary(string path)
    {
        Directory.CreateDirectory(Folder);
        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        long length = new FileInfo(path).Length;
        for (int n = 1; ; ++n)
        {
            string name = n == 1 ? stem + extension : $"{stem} ({n}){extension}";
            string target = Path.Combine(Folder, name);
            if (!File.Exists(target))
            {
                File.Copy(path, target);
                return name;
            }
            if (new FileInfo(target).Length == length && SameContent(path, target))
            {
                return name;
            }
        }
    }

    private static bool SameContent(string a, string b)
    {
        byte[] x = File.ReadAllBytes(a);
        byte[] y = File.ReadAllBytes(b);
        if (x.Length != y.Length)
        {
            return false;
        }
        for (int i = 0; i < x.Length; ++i)
        {
            if (x[i] != y[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Deletes the music files in the Music folder that no list names any more. Only files of the
    /// formats the game plays, and only in that folder: they are the game's copies, never the player's
    /// own files.
    /// </summary>
    private static void DeleteUnusedCopies()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            // By the name a line comes to in the folder, so that a full path written by hand to a file
            // in this folder counts as well as a plain name.
            HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
            foreach (List<MusicEntry> list in lists)
            {
                foreach (MusicEntry entry in list)
                {
                    string path = Resolve(entry.Track);
                    string name = path != null ? LibraryTrack(path) : null;
                    if (name != null)
                    {
                        used.Add(name);
                    }
                }
            }

            foreach (string file in Directory.GetFiles(Folder))
            {
                string name = Path.GetFileName(file);
                if (IsSupported(name) && !used.Contains(name))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Music: cannot tidy {Folder}: {e.Message}");
        }
    }
}
