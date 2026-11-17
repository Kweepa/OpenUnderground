using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// The system's Open dialog, for picking files from anywhere on disk (the Setup screen's music, the game's folder by one
/// of its files): Windows and Wine under Proton, and macOS. Elsewhere it is not available.
/// </summary>
/// <remarks>
/// Unity has no file dialog of its own in a built game. This one blocks the game until it closes,
/// which is what a modal dialog is for; music keeps playing, since it runs on the audio thread.
/// Tried with mod 900's probe on the Windows player, fullscreen and windowed, and under Proton.
/// </remarks>
public static class NativeFileDialog
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    public static bool IsAvailable => true;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

    [DllImport("comdlg32.dll")]
    private static extern int CommDlgExtendedError();

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    private const int SW_RESTORE = 9;
    private const int OFN_NOCHANGEDIR = 0x8;
    private const int OFN_ALLOWMULTISELECT = 0x200;
    private const int OFN_PATHMUSTEXIST = 0x800;
    private const int OFN_FILEMUSTEXIST = 0x1000;
    private const int OFN_EXPLORER = 0x80000;
    private const int bufferChars = 65536;

    /// <summary>
    /// Opens the dialog and returns the files picked, none if it was cancelled.
    /// </summary>
    /// <param name="title">The dialog's title bar.</param>
    /// <param name="filterName">What the filter is called in the dialog, e.g. "Music".</param>
    /// <param name="extensions">The extensions the filter shows, with their dots.</param>
    /// <param name="initialFolder">Where the dialog opens, or null for where it opened last.</param>
    /// <param name="error">Why nothing came back, when it was not a cancel.</param>
    public static string[] OpenFiles(string title, string filterName, string[] extensions, string initialFolder, out string error)
    {
        return Open(title, filterName, Array.ConvertAll(extensions, e => "*" + e), initialFolder, true, out error);
    }

    /// <summary>
    /// Opens the dialog for one file and returns it, null if it was cancelled.
    /// </summary>
    /// <param name="patterns">What the filter shows: whole names such as "game.gog", or "*.ext".</param>
    public static string OpenFile(string title, string filterName, string[] patterns, string initialFolder, out string error)
    {
        string[] files = Open(title, filterName, patterns, initialFolder, false, out error);
        return files.Length > 0 ? files[0] : null;
    }

    private static string[] Open(string title, string filterName, string[] patternList, string initialFolder, bool multiple,
        out string error)
    {
        error = null;

        // The owner is the game's window, which is the foreground one when the player asks for the
        // dialog. GetActiveWindow() alone may be null, since it only sees the calling thread's
        // windows, and an ownerless dialog leaves the game behind when it closes.
        IntPtr owner = GetForegroundWindow();
        if (owner == IntPtr.Zero)
        {
            owner = GetActiveWindow();
        }

        string patterns = string.Join(";", patternList);
        IntPtr buffer = Marshal.AllocHGlobal(bufferChars * 2);
        try
        {
            Marshal.Copy(new byte[bufferChars * 2], 0, buffer, bufferChars * 2);
            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = owner,
                lpstrFilter = $"{filterName} ({patterns})\0{patterns}\0All files\0*.*\0\0",
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = bufferChars,
                lpstrInitialDir = initialFolder,
                lpstrTitle = title,
                Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | (multiple ? OFN_ALLOWMULTISELECT : 0)
            };

            if (!GetOpenFileName(ref ofn))
            {
                int code = CommDlgExtendedError();
                error = code == 0 ? null : $"the file dialog failed (0x{code:X})";
                return Array.Empty<string>();
            }

            // With several files the buffer holds the folder, then each name, each ended by a NUL,
            // and a second NUL at the end; with one file it holds the full path alone.
            string raw = Marshal.PtrToStringUni(buffer, bufferChars);
            List<string> parts = new();
            foreach (string part in raw.Split('\0'))
            {
                if (part.Length == 0)
                {
                    break;
                }
                parts.Add(part);
            }

            if (parts.Count <= 1)
            {
                return parts.ToArray();
            }

            string[] files = new string[parts.Count - 1];
            for (int i = 1; i < parts.Count; ++i)
            {
                files[i - 1] = Path.Combine(parts[0], parts[i]);
            }
            return files;
        }
        catch (Exception e)
        {
            error = e.Message;
            return Array.Empty<string>();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            BringBack(owner);
        }
    }

    /// <summary>
    /// Puts the game back in front once the dialog has closed.
    /// </summary>
    /// <remarks>
    /// With Visible In Background off in the player settings, a fullscreen game minimizes as soon as
    /// it loses the focus, so the dialog leaves it minimized, and SetForegroundWindow() does not
    /// restore a minimized window: it has to be restored first. Seen and cured with mod 900's probe.
    /// </remarks>
    private static void BringBack(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return;
        }
        if (IsIconic(window))
        {
            ShowWindow(window, SW_RESTORE);
        }
        SetForegroundWindow(window);
    }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
    public static bool IsAvailable => true;

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
    // PlugIns/NativeFileDialogMac.bundle (compiled from .mm). __Internal is iOS/static-link only.
    [DllImport("NativeFileDialogMac")]
    private static extern IntPtr MacNativeFileDialog_OpenFile(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string patterns,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string initialFolder,
        int multiple);

    [DllImport("NativeFileDialogMac")]
    private static extern void MacNativeFileDialog_Free(IntPtr ptr);
#endif

    public static string[] OpenFiles(string title, string filterName, string[] extensions, string initialFolder, out string error)
    {
        return Open(title, extensions, initialFolder, true, out error);
    }

    public static string OpenFile(string title, string filterName, string[] patterns, string initialFolder, out string error)
    {
        string[] files = Open(title, patterns, initialFolder, false, out error);
        return files.Length > 0 ? files[0] : null;
    }

    private static string[] Open(string title, string[] patterns, string initialFolder, bool multiple, out string error)
    {
        error = null;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        try
        {
            IntPtr ptr = MacNativeFileDialog_OpenFile(title ?? "", ExtensionList(patterns), initialFolder ?? "", multiple ? 1 : 0);
            if (ptr == IntPtr.Zero)
            {
                return Array.Empty<string>();
            }

            string resultStr = Marshal.PtrToStringUTF8(ptr);
            MacNativeFileDialog_Free(ptr);
            if (string.IsNullOrEmpty(resultStr))
            {
                return Array.Empty<string>();
            }

            return resultStr.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        }
        catch (DllNotFoundException)
        {
            return RunAppleScriptChooser(title, initialFolder, multiple, out error);
        }
        catch (EntryPointNotFoundException)
        {
            return RunAppleScriptChooser(title, initialFolder, multiple, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return Array.Empty<string>();
        }
#else
        string ext = ExtensionList(patterns);
        int semi = ext.IndexOf(';');
        if (semi >= 0)
        {
            ext = ext.Substring(0, semi);
        }
        string file = UnityEditor.EditorUtility.OpenFilePanel(title ?? "", initialFolder ?? "", ext);
        return string.IsNullOrEmpty(file) ? Array.Empty<string>() : new[] { file };
#endif
    }

    /// <summary>Extensions only, semicolon-separated: "game.gog" and "*.mp3" both become "gog" and "mp3".</summary>
    private static string ExtensionList(string[] patterns)
    {
        if (patterns == null || patterns.Length == 0)
        {
            return "";
        }

        var exts = new List<string>();
        foreach (string pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern) || pattern == "*" || pattern == "*.*")
            {
                continue;
            }

            string ext = pattern.Trim();
            int dot = ext.LastIndexOf('.');
            if (dot >= 0 && dot < ext.Length - 1)
            {
                ext = ext.Substring(dot + 1);
            }
            ext = ext.Trim().TrimStart('*');
            if (ext.Length == 0 || ext == "*")
            {
                continue;
            }
            if (!exts.Exists(existing => string.Equals(existing, ext, StringComparison.OrdinalIgnoreCase)))
            {
                exts.Add(ext);
            }
        }
        return string.Join(";", exts);
    }

    private static string[] RunAppleScriptChooser(string title, string initialFolder, bool multiple, out string error)
    {
        error = null;
        try
        {
            string prompt = AppleScriptString(string.IsNullOrEmpty(title) ? "Choose a file" : title);
            string location = "";
            if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder))
            {
                location = " default location (POSIX file \"" + AppleScriptString(initialFolder) + "\")";
            }

            string script = multiple
                ? "try\nset theFiles to choose file with prompt \"" + prompt + "\"" + location + " with multiple selections allowed\nset output to \"\"\nrepeat with f in theFiles\nset output to output & (POSIX path of f) & linefeed\nend repeat\nreturn output\non error number -128\nreturn \"\"\nend try"
                : "try\nreturn POSIX path of (choose file with prompt \"" + prompt + "\"" + location + ")\non error number -128\nreturn \"\"\nend try";

            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);

            using Process proc = Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd();
            string errText = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(errText)
                    && errText.IndexOf("User canceled", StringComparison.OrdinalIgnoreCase) < 0
                    && errText.IndexOf("-128", StringComparison.Ordinal) < 0)
                {
                    error = errText.Trim();
                }
                return Array.Empty<string>();
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<string>();
            }

            return output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return Array.Empty<string>();
        }
    }

    private static string AppleScriptString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
#else
    public static bool IsAvailable => false;

    public static string[] OpenFiles(string title, string filterName, string[] extensions, string initialFolder, out string error)
    {
        error = "no file dialog on this platform";
        return Array.Empty<string>();
    }

    public static string OpenFile(string title, string filterName, string[] patterns, string initialFolder, out string error)
    {
        error = "no file dialog on this platform";
        return null;
    }
#endif
}
