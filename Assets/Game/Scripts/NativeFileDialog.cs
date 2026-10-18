using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// The system's Open dialog, for picking files from anywhere on disk: Windows, and Wine under Proton,
/// which is how the game runs on Linux and on a Steam Deck. Elsewhere it is not available.
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
        error = null;

        // The owner is the game's window, which is the foreground one when the player asks for the
        // dialog. GetActiveWindow() alone may be null, since it only sees the calling thread's
        // windows, and an ownerless dialog leaves the game behind when it closes.
        IntPtr owner = GetForegroundWindow();
        if (owner == IntPtr.Zero)
        {
            owner = GetActiveWindow();
        }

        string patterns = string.Join(";", Array.ConvertAll(extensions, e => "*" + e));
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
                Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_ALLOWMULTISELECT | OFN_NOCHANGEDIR
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
#else
    public static bool IsAvailable => false;

    public static string[] OpenFiles(string title, string filterName, string[] extensions, string initialFolder, out string error)
    {
        error = "no file dialog on this platform";
        return Array.Empty<string>();
    }
#endif
}
